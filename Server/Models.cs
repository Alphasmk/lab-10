using System.ComponentModel.DataAnnotations.Schema;

namespace EventManagement
{
    [Table("participants")]
    public class Participant
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }

    [Table("venues")]
    public class Venue
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("address")]
        public string Address { get; set; } = string.Empty;
    }

    [Table("events")]
    public class Event
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("venue_id")]
        public int VenueId { get; set; }

        [Column("available_places")]
        public int AvailablePlaces { get; set; }

        [Column("capacity")]
        public int Capacity { get; set; }
    }

    [Table("registrations")]
    public class Registration
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("event_id")]
        public int EventId { get; set; }

        [Column("participant_id")]
        public int ParticipantId { get; set; }

        [Column("place_reserved")]
        public bool PlaceReserved { get; set; }
    }
}
