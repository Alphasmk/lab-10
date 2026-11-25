using Messaging;
using Xunit;

namespace Server.Tests;

public class PubSubTests
{
    [Fact]
    public void Event_HasRequiredDataAndRejectsInvalidInput()
    {
        EventMessage message = EventMessage.Create("ParticipantRegistered", 17, "Client2", "EventId=3");
        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.Equal(17, message.EntityId);
        Assert.Equal("Client2", message.ClientId);
        Assert.Equal("EventId=3", message.Details);
        Assert.InRange(message.Timestamp, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(() => EventMessage.Create("Unknown", 17, "Client2"));
        Assert.Throws<ArgumentException>(() => EventMessage.Create("TicketIssued", 0, "Client2"));
    }

    [Fact]
    public async Task Broker_DeliversEachTypeOnlyToSubscribers()
    {
        var broker = new InMemoryBroker("Primary");
        var logger = new EventSubscriber("Logger", 0);
        var registration = new EventSubscriber("RegistrationService", 0);
        var notification = new EventSubscriber("NotificationService", 0);
        foreach (EventSubscriber subscriber in new[] { logger, registration, notification })
            broker.Subscribe(subscriber, Subscriptions.For(subscriber.Name));

        foreach (string type in new[] { "ParticipantRegistered", "TicketIssued", "RegistrationCancelled" })
            broker.Publish(EventMessage.Create(type, 1, "Client1"));
        await Until(() => logger.HandledCount("RegistrationCancelled") == 1
            && registration.HandledCount("RegistrationCancelled") == 1
            && notification.HandledCount("TicketIssued") == 1);

        Assert.Equal(5, logger.HandledCount("ParticipantRegistered"));
        Assert.Equal(1, registration.HandledCount("ParticipantRegistered"));
        Assert.Equal(1, notification.HandledCount("ParticipantRegistered"));
        Assert.Equal(0, registration.HandledCount("TicketIssued"));
        Assert.Equal(0, notification.HandledCount("RegistrationCancelled"));
    }

    [Fact]
    public async Task UnavailableSubscriber_KeepsEventWhileOthersContinue()
    {
        var broker = new InMemoryBroker("Primary");
        var failed = new EventSubscriber("RegistrationService", 0) { Available = false };
        var working = new EventSubscriber("Logger", 0);
        broker.Subscribe(failed, "ParticipantRegistered");
        broker.Subscribe(working, "ParticipantRegistered");
        broker.Publish(EventMessage.Create("ParticipantRegistered", 1, "Client1"));

        await Until(() => working.HandledCount("ParticipantRegistered") == 1);
        Assert.Equal(1, broker.Pending("RegistrationService"));
        failed.Available = true;
        broker.Resume();
        await Until(() => failed.HandledCount("ParticipantRegistered") == 1
            && broker.Pending("RegistrationService") == 0);
        Assert.Equal(0, broker.Pending("RegistrationService"));
    }

    [Fact]
    public async Task Subscriber_RetriesAndSucceedsOnThirdAttempt()
    {
        var broker = new InMemoryBroker("Primary");
        var subscriber = new EventSubscriber("RegistrationService", 0);
        subscriber.FailNext(2);
        broker.Subscribe(subscriber, "ParticipantRegistered");
        broker.Publish(EventMessage.Create("ParticipantRegistered", 1, "Client1"));

        await Until(() => subscriber.HandledCount("ParticipantRegistered") == 1);
        Assert.Equal(2, subscriber.Failures);
    }

    [Fact]
    public async Task Publisher_FallsBackToSecondBroker()
    {
        var primary = new InMemoryBroker("Primary");
        var secondary = new InMemoryBroker("Secondary");
        var subscriber = new EventSubscriber("Logger", 0);
        secondary.Subscribe(subscriber, "TicketIssued");
        primary.SetAvailable(false);

        PublishResult result = await new EventPublisher(primary, secondary)
            .PublishAsync(EventMessage.Create("TicketIssued", 1, "Client2"));

        Assert.True(result.Success);
        Assert.Equal("Secondary", result.Broker);
        await Until(() => subscriber.HandledCount("TicketIssued") == 1);
    }

    [Fact]
    public async Task Publisher_ReportsMissingSubscriptionAndBothBrokersUnavailable()
    {
        var primary = new InMemoryBroker("Primary");
        var secondary = new InMemoryBroker("Secondary");
        var publisher = new EventPublisher(primary, secondary);
        PublishResult missing = await publisher.PublishAsync(EventMessage.Create("TicketIssued", 1, "Client1"));
        Assert.False(missing.Success);
        Assert.Contains("Нет подписчиков", missing.Error);

        primary.SetAvailable(false);
        secondary.SetAvailable(false);
        PublishResult unavailable = await publisher.PublishAsync(EventMessage.Create("TicketIssued", 1, "Client1"));
        Assert.False(unavailable.Success);
        Assert.Contains("недоступен", unavailable.Error);
    }

    [Fact]
    public async Task NewSubscriber_ReceivesFutureEventsWithoutPublisherChange()
    {
        var broker = new InMemoryBroker("Primary");
        var logger = new EventSubscriber("Logger", 0);
        broker.Subscribe(logger, "ParticipantRegistered");
        var publisher = new EventPublisher(broker);
        await publisher.PublishAsync(EventMessage.Create("ParticipantRegistered", 1, "Client1"));
        var analytics = new EventSubscriber("AnalyticsService", 0);
        broker.Subscribe(analytics, "ParticipantRegistered");
        await publisher.PublishAsync(EventMessage.Create("ParticipantRegistered", 2, "Client2"));

        await Until(() => analytics.HandledCount("ParticipantRegistered") == 1);
        Assert.Equal(1, analytics.HandledCount("ParticipantRegistered"));
        await Until(() => logger.HandledCount("ParticipantRegistered") == 2);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, timeout.Token);
        }
    }
}
