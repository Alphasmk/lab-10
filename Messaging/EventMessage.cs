namespace Messaging;

public record EventMessage(Guid Id, string EventType, int EntityId, string ClientId,
    DateTimeOffset Timestamp, string Details)
{
    public static EventMessage Create(string type, int entityId, string clientId, string details = "")
    {
        if (type is not ("ParticipantRegistered" or "RegistrationCancelled" or "TicketIssued")
            || entityId <= 0 || string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Некорректное событие");
        return new EventMessage(Guid.NewGuid(), type, entityId, clientId, DateTimeOffset.UtcNow, details);
    }
}
