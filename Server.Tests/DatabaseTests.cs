using EventManagement;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server;
using Xunit;

namespace Server.Tests
{
    public class DatabaseTests : IDisposable
    {
        private readonly Database db;
        private readonly int venueId;
        private readonly int eventId;
        private readonly int participant1;
        private readonly int participant2;
        public DatabaseTests()
        {
            string connectionString = Environment.GetEnvironmentVariable("LAB06_TEST_DATABASE")
                ?? throw new InvalidOperationException("Укажите строку подключения в LAB06_TEST_DATABASE");

            db = new Database(connectionString);
            db.Initialize();
            Venue venue = new Venue
            {
                Name = "Test-" + Guid.NewGuid()
            };
            db.Add(venue);
            venueId = venue.Id;
            Event ev = new Event
            {
                Name = "Test",
                VenueId = venueId,
                Capacity = 1,
                AvailablePlaces = 1
            };
            db.Add(ev);
            eventId = ev.Id;
            Participant first = new Participant
            {
                Name = "Test1"
            };
            Participant second = new Participant
            {
                Name = "Test2"
            };
            db.Add(first);
            db.Add(second);
            participant1 = first.Id;
            participant2 = second.Id;
        }

        private Registration Registration(int participant)
        {
            return new Registration()
            {
                EventId = eventId,
                ParticipantId = participant
            };
        }

        private (int Places, int Count, int Reserved) State()
        {
            using (EventDbContext context = db.CreateContext())
            {
                return (context.Events.AsNoTracking().Single(e => e.Id == eventId).AvailablePlaces, context.Registrations.Count(r => r.EventId == eventId), context.Registrations.Count(r => r.EventId == eventId && r.PlaceReserved));
            }
        }

        [Fact]
        public void Registration_CommitsBothChanges_VisibleFromNewConnection()
        {
            OperationResult result = db.Register(Registration(participant1), "Test1");
            Assert.Equal(200, result.StatusCode);
            Assert.Equal("COMMIT", result.Outcome);
            Assert.Equal((0, 1, 1), State());
        }

        [Fact]
        public void OnePlace_CannotBeReservedTwice()
        {
            Assert.Equal(200, db.Register(Registration(participant1), "Test1").StatusCode);
            Assert.Equal(409, db.Register(Registration(participant2), "Test2").StatusCode);
            Assert.Equal((0, 1, 1), State());
        }

        [Fact]
        public async Task TwoConcurrentClients_OnlyOneSucceeds()
        {
            using (Barrier start = new Barrier(2))
            {
                Task<OperationResult> Request(int participant)
                {
                    return Task.Factory.StartNew(() =>
                    {
                        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)));
                        return db.Register(Registration(participant), "Test" + participant, delayMs: 100);
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }

                OperationResult[] responses = await Task.WhenAll(Request(participant1), Request(participant2));
                Assert.Equal(new int[] { 200, 409 }, responses.Select(r => r.StatusCode).Order().ToArray());
                Assert.Equal((0, 1, 1), State());
            }
        }

        [Fact]
        public void Rollback_LeavesNoPartialChanges_AndAllowsRetry()
        {
            OperationResult result = db.Register(Registration(participant1), "Test1", simulateError: true);
            Assert.Equal("ROLLBACK", result.Outcome);
            Assert.Equal((1, 0, 0), State());
            Assert.Equal(200, db.Register(Registration(participant1), "Test1").StatusCode);
            Assert.Equal((0, 1, 1), State());
        }

        [Fact]
        public void NoTransaction_ErrorLeavesPartialRegistration()
        {
            Assert.Equal("PARTIAL_CHANGE", db.Register(Registration(participant1), "Test1", useTransaction: false, simulateError: true).Outcome);
            Assert.Equal((1, 1, 0), State());
        }

        [Fact]
        public void NoTransaction_SuccessSavesBothChanges()
        {
            Assert.Equal("AUTOCOMMIT", db.Register(Registration(participant1), "Test1", useTransaction: false).Outcome);
            Assert.Equal((0, 1, 1), State());
        }

        [Fact]
        public void Cancel_ReturnsReservedPlace()
        {
            int registrationId = db.Register(Registration(participant1), "Test1").Id!.Value;
            Assert.Equal("COMMIT", db.Cancel(registrationId, "Test1").Outcome);
            Assert.Equal((1, 0, 0), State());
            Assert.Equal(404, db.Cancel(registrationId, "Test1").StatusCode);
        }

        [Fact]
        public void CancelPartialRegistration_DoesNotAddPlace()
        {
            db.Register(Registration(participant1), "Test1", useTransaction: false, simulateError: true);
            using (EventDbContext context = db.CreateContext())
            {
                int id = context.Registrations.AsNoTracking().Single(r => r.EventId == eventId).Id;
                Assert.Equal("COMMIT", db.Cancel(id, "Test1").Outcome);
                Assert.Equal((1, 0, 0), State());
            }
        }

        [Fact]
        public void MissingResourceAndParticipant_DoNotModifyData()
        {
            Assert.Equal(404, db.Register(new Registration() { EventId = int.MaxValue, ParticipantId = participant1 }, "Test").StatusCode);
            Assert.Equal(404, db.Register(Registration(int.MaxValue), "Test").StatusCode);
            Assert.Equal((1, 0, 0), State());
        }

        [Fact]
        public void CommitFailure_RollsBackBothChanges()
        {
            using (EventDbContext context = db.CreateContext())
            {
                string setupSql = $"""
            CREATE FUNCTION test_commit_{eventId}() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RAISE EXCEPTION 'Test COMMIT failure' USING ERRCODE='23514'; END $$;
            CREATE CONSTRAINT TRIGGER test_commit_{eventId} AFTER INSERT ON registrations
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.event_id = {eventId})
            EXECUTE FUNCTION test_commit_{eventId}();
            """;
                context.Database.ExecuteSqlRaw(setupSql);
                try
                {
                    PostgresException exception = Assert.Throws<PostgresException>(() =>
                    {
                        db.Register(Registration(participant1), "CommitFail");
                    });
                    Assert.Equal("23514", exception.SqlState);
                    Assert.Equal((1, 0, 0), State());
                }
                finally
                {
                    string cleanupSql = $"DROP TRIGGER test_commit_{eventId} ON registrations; DROP FUNCTION test_commit_{eventId}();";
                    context.Database.ExecuteSqlRaw(cleanupSql);
                }
            }
            Assert.Fail();
        }

        [Fact]
        public void DatabaseConstraint_RejectsNegativePlaces()
        {
            using (EventDbContext context = db.CreateContext())
            {
                context.Events.Single(e => e.Id == eventId).AvailablePlaces = -1;
                DbUpdateException exception = Assert.Throws<DbUpdateException>(() =>
                {
                    context.SaveChanges();
                });
                Assert.Equal("23514", Assert.IsType<PostgresException>(exception.GetBaseException()).SqlState);
                Assert.Equal((1, 0, 0), State());
            }
        }

        public void Dispose()
        {
            using (EventDbContext context = db.CreateContext())
            {
                context.Registrations.Where(r => r.EventId == eventId).ExecuteDelete();
                context.Events.Where(e => e.Id == eventId).ExecuteDelete();
                context.Participants.Where(p => p.Id == participant1 || p.Id == participant2).ExecuteDelete();
                context.Venues.Where(v => v.Id == venueId).ExecuteDelete();
            }
        }
    }

    public class ValidationTests
    {
        [Theory]
        [InlineData(0, 1, "Client", 0)]
        [InlineData(1, 0, "Client", 0)]
        [InlineData(1, 1, "", 0)]
        [InlineData(1, 1, "Client", -1)]
        public void InvalidRequest_IsRejectedWithoutConnectingToDatabase(int eventId, int participantId, string client, int delay)
        {
            Database db = new Database("Host=invalid");
            Assert.Equal(400, db.Register(new Registration() { EventId = eventId, ParticipantId = participantId }, client, delayMs: delay).StatusCode);
        }
    }
}

