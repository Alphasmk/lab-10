namespace EventManagement
{
    public class Participant
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class Venue
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
    }

    public class Event
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int VenueId { get; set; }
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