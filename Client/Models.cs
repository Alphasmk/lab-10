namespace EventManagement
{
    public class Event
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class Registration
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public int ParticipantId { get; set; }
    }

    public class Ticket
    {
        public int Id { get; set; }
        public int RegistrationId { get; set; }
    }
}