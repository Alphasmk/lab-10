namespace Messaging;

public static class Subscriptions
{
    public static string[] For(string name) => name switch
    {
        "Logger" => ["ParticipantRegistered", "RegistrationCancelled", "TicketIssued"],
        "RegistrationService" => ["ParticipantRegistered", "RegistrationCancelled"],
        "NotificationService" => ["ParticipantRegistered", "TicketIssued"],
        "AnalyticsService" => ["ParticipantRegistered", "RegistrationCancelled", "TicketIssued"],
        _ => throw new ArgumentException("Неизвестный Subscriber")
    };
}
