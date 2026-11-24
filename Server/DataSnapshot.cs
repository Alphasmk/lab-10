using EventManagement;
using System.Text.Json;

namespace Server
{
    public class DataSnapshot
    {
        public List<Venue> Venues { get; set; } = new List<Venue>();
        public List<Participant> Participants { get; set; } = new List<Participant>();
        public List<Event> Events { get; set; } = new List<Event>();
        public List<Registration> Registrations { get; set; } = new List<Registration>();

        public bool Matches(DataSnapshot other)
        {
            return JsonSerializer.Serialize(this) == JsonSerializer.Serialize(other);
        }
    }

    public interface IDataStore
    {
        void Initialize();
        bool IsAvailable();
        DataSnapshot ReadSnapshot();
        void WriteSnapshot(DataSnapshot snapshot);
        DataSnapshot CopyFrom(IDataStore source);
    }
}
