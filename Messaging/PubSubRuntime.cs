namespace Messaging;

public sealed class PubSubRuntime
{
    private readonly Dictionary<string, EventSubscriber> subscribers = new();
    public InMemoryBroker Primary { get; } = new("Primary");
    public InMemoryBroker Secondary { get; } = new("Secondary");
    public EventPublisher Publisher { get; }

    public PubSubRuntime()
    {
        Publisher = new EventPublisher(Primary, Secondary);
        Add("Logger", 1000);
        Add("RegistrationService", 3000);
        Add("NotificationService", 2000);
    }

    public void AddAnalytics() => Add("AnalyticsService", 500);

    private void Add(string name, int delay)
    {
        lock (subscribers)
        {
            if (subscribers.ContainsKey(name)) return;
            var subscriber = new EventSubscriber(name, delay);
            Primary.Subscribe(subscriber, Subscriptions.For(name));
            Secondary.Subscribe(subscriber, Subscriptions.For(name));
            subscribers.Add(name, subscriber);
        }
    }

    public void SetSubscriberAvailable(string name, bool available)
    {
        lock (subscribers) Get(name).Available = available;
        if (available) { Primary.Resume(); Secondary.Resume(); }
    }

    public void FailNext(string name, int count)
    {
        if (count < 0 || count > 10) throw new ArgumentException("Число ошибок должно быть от 0 до 10");
        lock (subscribers) Get(name).FailNext(count);
    }

    private EventSubscriber Get(string name) => subscribers.TryGetValue(name, out EventSubscriber? subscriber)
        ? subscriber : throw new ArgumentException("Subscriber не зарегистрирован");

    public void SetBrokerAvailable(string name, bool available)
    {
        (name switch { "Primary" => Primary, "Secondary" => Secondary,
            _ => throw new ArgumentException("Укажите Primary или Secondary") }).SetAvailable(available);
    }

    public object Status()
    {
        lock (subscribers)
            return new
            {
                Primary = Primary.Available,
                Secondary = Secondary.Available,
                Subscribers = subscribers.Values.Select(s => new
                {
                    s.Name, s.Available, Events = Subscriptions.For(s.Name), s.Failures,
                    Processed = Subscriptions.For(s.Name).ToDictionary(type => type, s.HandledCount),
                    PendingPrimary = Primary.Pending(s.Name), PendingSecondary = Secondary.Pending(s.Name)
                }).ToArray()
            };
    }
}
