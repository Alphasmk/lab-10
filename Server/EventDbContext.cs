using EventManagement;
using Microsoft.EntityFrameworkCore;

namespace Server
{
    public class EventDbContext : DbContext
    {
        public EventDbContext(DbContextOptions<EventDbContext> options) : base(options)
        {
        }

        public DbSet<Participant> Participants
        {
            get
            {
                return Set<Participant>();
            }
        }

        public DbSet<Venue> Venues
        {
            get
            {
                return Set<Venue>();
            }
        }

        public DbSet<Event> Events
        {
            get
            {
                return Set<Event>();
            }
        }

        public DbSet<Registration> Registrations
        {
            get
            {
                return Set<Registration>();
            }
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.UseIdentityAlwaysColumns();
            model.Entity<Participant>().ToTable("participants", table =>
            {
                table.HasCheckConstraint("participants_name_check", "length(trim(name)) > 0");
            });
            model.Entity<Venue>().ToTable("venues", table =>
            {
                table.HasCheckConstraint("venues_name_check", "length(trim(name)) > 0");
            });
            model.Entity<Event>().ToTable("events", table =>
            {
                table.HasCheckConstraint("events_name_check", "length(trim(name)) > 0");
                table.HasCheckConstraint("events_capacity_check", "capacity >= 0");
                table.HasCheckConstraint("events_available_places_check", "available_places BETWEEN 0 AND capacity");
            });
            model.Entity<Event>().HasOne<Venue>().WithMany().HasForeignKey(e => e.VenueId).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Registration>().HasOne<Event>().WithMany().HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Registration>().HasOne<Participant>().WithMany().HasForeignKey(r => r.ParticipantId).OnDelete(DeleteBehavior.Restrict);
            model.Entity<Registration>().HasIndex(r => new { r.EventId, r.ParticipantId }).IsUnique();
        }
    }
}
