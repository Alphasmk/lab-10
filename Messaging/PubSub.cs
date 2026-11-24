using System.Collections.Concurrent;

namespace Messaging;

public record PublishResult(bool Success, string Broker, string? Error = null);

public sealed class EventPublisher(params InMemoryBroker[] brokers)
{
    public Task<PublishResult> PublishAsync(EventMessage message)
    {
        string error = "";
        foreach (InMemoryBroker broker in brokers)
        {
            try
            {
                broker.Publish(message);
                Log(message, $"Publisher -> {broker.Name} PUBLISHED");
                return Task.FromResult(new PublishResult(true, broker.Name));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log(message, $"Publisher -> {broker.Name} FAIL: {error}");
            }
        }
        return Task.FromResult(new PublishResult(false, "", error));
    }

    internal static void Log(EventMessage message, string text) =>
        Console.WriteLine($"[{DateTimeOffset.UtcNow:HH:mm:ss.fff} UTC] [{message.ClientId}] {message.EventType} {text}");
}

public sealed class EventSubscriber(string name, int delayMs)
{
    private int failNext;
    private int failures;
    private volatile bool available = true;
    private readonly ConcurrentDictionary<string, int> counts = new();

    public string Name => name;
    public bool Available { get => available; set => available = value; }
    public int Failures => Volatile.Read(ref failures);
    public int HandledCount(string type) => counts.TryGetValue(type, out int count) ? count : 0;
    public void FailNext(int count) => Interlocked.Exchange(ref failNext, count);

    public async Task<string> HandleAsync(EventMessage message)
    {
        if (!Available) throw new IOException("Subscriber недоступен");
        await Task.Delay(delayMs);
        if (!Available || Interlocked.Decrement(ref failNext) >= 0)
        {
            Interlocked.Increment(ref failures);
            throw new IOException("Искусственный отказ Subscriber");
        }
        int total = counts.AddOrUpdate(message.EventType, 1, (_, n) => n + 1);
        return name switch
        {
            "Logger" => "записано в журнал",
            "RegistrationService" => "регистрация учтена",
            "NotificationService" => "уведомление подготовлено",
            _ => $"статистика: {message.EventType}={total}"
        };
    }
}

public sealed class InMemoryBroker(string name)
{
    private readonly Dictionary<string, Subscription> subscriptions = new();
    private volatile bool available = true;
    public string Name => name;
    public bool Available => available;

    public void Subscribe(EventSubscriber subscriber, params string[] types)
    {
        lock (subscriptions) subscriptions.Add(subscriber.Name, new Subscription(this, subscriber, types));
    }

    public void SetAvailable(bool value)
    {
        available = value;
        if (value) Resume();
    }

    public void Resume()
    {
        lock (subscriptions)
            foreach (Subscription subscription in subscriptions.Values) subscription.Resume();
    }

    public int Pending(string subscriber)
    {
        lock (subscriptions) return subscriptions[subscriber].Pending;
    }

    public void Publish(EventMessage message)
    {
        if (!Available) throw new IOException($"Broker {name} недоступен");
        if (message.EntityId <= 0 || string.IsNullOrWhiteSpace(message.ClientId)
            || message.EventType is not ("ParticipantRegistered" or "RegistrationCancelled" or "TicketIssued"))
            throw new ArgumentException("Некорректное событие");
        Subscription[] targets;
        lock (subscriptions) targets = subscriptions.Values.Where(s => s.Types.Contains(message.EventType)).ToArray();
        if (targets.Length == 0) throw new InvalidOperationException("Нет подписчиков на событие");
        EventPublisher.Log(message, $"Broker {name} EVENT RECEIVED; subscribers={targets.Length}");
        foreach (Subscription target in targets) target.Enqueue(message);
    }

    private sealed class Subscription(InMemoryBroker broker, EventSubscriber subscriber, string[] types)
    {
        private readonly Queue<EventMessage> queue = new();
        private readonly object gate = new();
        private bool running;
        public string[] Types => types;
        public int Pending { get { lock (gate) return queue.Count; } }

        public void Enqueue(EventMessage message)
        {
            lock (gate) { queue.Enqueue(message); Start(); }
        }

        public void Resume()
        {
            lock (gate) Start();
        }

        private void Start()
        {
            if (running || queue.Count == 0 || !broker.Available || !subscriber.Available) return;
            running = true;
            _ = Task.Run(DrainAsync);
        }

        private async Task DrainAsync()
        {
            while (true)
            {
                EventMessage message;
                lock (gate)
                {
                    if (!broker.Available || !subscriber.Available || queue.Count == 0)
                    {
                        running = false;
                        return;
                    }
                    message = queue.Peek();
                }
                bool success = false;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        EventPublisher.Log(message, $"Broker {broker.Name} -> {subscriber.Name} START attempt={attempt}");
                        string result = await subscriber.HandleAsync(message);
                        EventPublisher.Log(message, $"{subscriber.Name} SUCCESS {result}");
                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        EventPublisher.Log(message, $"{subscriber.Name} FAIL attempt={attempt}: {ex.Message}");
                        if (attempt < 3) await Task.Delay(300);
                    }
                }
                if (!success)
                {
                    subscriber.Available = false;
                    EventPublisher.Log(message, $"{subscriber.Name} UNAVAILABLE; event remains in queue");
                    lock (gate) running = false;
                    return;
                }
                lock (gate) queue.Dequeue();
            }
        }
    }
}
