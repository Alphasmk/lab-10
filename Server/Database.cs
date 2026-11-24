using EventManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Server
{
    public class Database : IDataStore
    {
        private readonly string connectionString;
        private readonly object syncRoot = new object();
        private readonly object initializationLock = new object();
        private bool initialized;
        public Database(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public EventDbContext CreateContext()
        {
            DbContextOptionsBuilder<EventDbContext> optionsBuilder = new DbContextOptionsBuilder<EventDbContext>();
            optionsBuilder.UseNpgsql(connectionString);
            return new EventDbContext(optionsBuilder.Options);
        }

        public void Initialize()
        {
            lock (initializationLock)
            {
                if (initialized)
                {
                    return;
                }

                using (EventDbContext context = CreateContext())
                {
                    context.Database.EnsureCreated();
                    initialized = true;
                }
            }
        }

        public void Add<T>(T entity)
            where T : class
        {
            using (EventDbContext context = CreateContext())
            {
                context.Add(entity);
                context.SaveChanges();
            }
        }

        public bool IsAvailable()
        {
            try
            {
                Initialize();
                using (EventDbContext context = CreateContext())
                {
                    return context.Database.CanConnect();
                }
            }
            catch (Exception ex) when (ReplicatedDatabase.IsStorageError(ex))
            {
                return false;
            }
        }

        public DataSnapshot ReadSnapshot()
        {
            using (EventDbContext context = CreateContext())
            using (IDbContextTransaction transaction = context.Database.BeginTransaction(System.Data.IsolationLevel.RepeatableRead))
            {
                DataSnapshot snapshot = new DataSnapshot
                {
                    Venues = context.Venues.AsNoTracking().OrderBy(v => v.Id).ToList(),
                    Participants = context.Participants.AsNoTracking().OrderBy(p => p.Id).ToList(),
                    Events = context.Events.AsNoTracking().OrderBy(e => e.Id).ToList(),
                    Registrations = context.Registrations.AsNoTracking().OrderBy(r => r.Id).ToList()
                };
                transaction.Commit();
                return snapshot;
            }
        }

        public void WriteSnapshot(DataSnapshot snapshot)
        {
            WriteSnapshot(() => snapshot);
        }

        public DataSnapshot CopyFrom(IDataStore source)
        {
            return WriteSnapshot(source.ReadSnapshot);
        }

        private DataSnapshot WriteSnapshot(Func<DataSnapshot> read)
        {
            using (EventDbContext context = CreateContext())
            using (IDbContextTransaction transaction = context.Database.BeginTransaction())
            {
                context.Database.ExecuteSqlRaw("SELECT pg_advisory_xact_lock(908123)");
                DataSnapshot snapshot = read();
                context.Database.ExecuteSqlRaw("TRUNCATE registrations, events, participants, venues RESTART IDENTITY");
                foreach (Venue v in snapshot.Venues)
                {
                    context.Database.ExecuteSqlInterpolated($"INSERT INTO venues (id, name, address) OVERRIDING SYSTEM VALUE VALUES ({v.Id}, {v.Name}, {v.Address})");
                }
                foreach (Participant p in snapshot.Participants)
                {
                    context.Database.ExecuteSqlInterpolated($"INSERT INTO participants (id, name) OVERRIDING SYSTEM VALUE VALUES ({p.Id}, {p.Name})");
                }
                foreach (Event e in snapshot.Events)
                {
                    context.Database.ExecuteSqlInterpolated($"INSERT INTO events (id, name, venue_id, available_places, capacity) OVERRIDING SYSTEM VALUE VALUES ({e.Id}, {e.Name}, {e.VenueId}, {e.AvailablePlaces}, {e.Capacity})");
                }
                foreach (Registration r in snapshot.Registrations)
                {
                    context.Database.ExecuteSqlInterpolated($"INSERT INTO registrations (id, event_id, participant_id, place_reserved) OVERRIDING SYSTEM VALUE VALUES ({r.Id}, {r.EventId}, {r.ParticipantId}, {r.PlaceReserved})");
                }
                context.Database.ExecuteSqlRaw("""
                    SELECT setval(pg_get_serial_sequence('venues', 'id'), COALESCE(MAX(id), 0) + 1, false) FROM venues;
                    SELECT setval(pg_get_serial_sequence('participants', 'id'), COALESCE(MAX(id), 0) + 1, false) FROM participants;
                    SELECT setval(pg_get_serial_sequence('events', 'id'), COALESCE(MAX(id), 0) + 1, false) FROM events;
                    SELECT setval(pg_get_serial_sequence('registrations', 'id'), COALESCE(MAX(id), 0) + 1, false) FROM registrations;
                    """);
                transaction.Commit();
                return snapshot;
            }
        }

        public OperationResult Register(Registration reg, string clientId, bool useTransaction = true, bool simulateError = false, bool useLock = true, int delayMs = 0)
        {
            Log(clientId, "RegisterParticipant", "START");
            if (reg.EventId <= 0 || reg.ParticipantId <= 0 || string.IsNullOrWhiteSpace(clientId) || (delayMs < 0 || delayMs > 2000))
            {
                return new OperationResult(400, "Некорректный запрос", "FAIL");
            }

            using (EventDbContext context = CreateContext())
            {
                context.Database.OpenConnection();
                if (!useLock)
                {
                    return ProcessRegistration(context, reg, clientId, useTransaction, simulateError, false, delayMs);
                }

                lock (syncRoot)
                {
                    Log(clientId, "RegisterParticipant", "LOCK_ENTER");
                    try
                    {
                        return ProcessRegistration(context, reg, clientId, useTransaction, simulateError, true, delayMs);
                    }
                    finally
                    {
                        Log(clientId, "RegisterParticipant", "LOCK_EXIT");
                    }
                }
            }
        }

        private OperationResult ProcessRegistration(EventDbContext context, Registration reg, string clientId, bool useTransaction, bool simulateError, bool useLock, int delayMs)
        {
            IDbContextTransaction? transaction = null;
            if (useTransaction)
            {
                transaction = context.Database.BeginTransaction();
            }

            using (transaction)
            {
                if (useTransaction)
                {
                    Log(clientId, "RegisterParticipant", "BEGIN");
                }

                try
                {
                    Event? ev;
                    if (useLock && useTransaction)
                    {
                        ev = context.Events.FromSqlInterpolated($"SELECT * FROM events WHERE id={reg.EventId} FOR UPDATE").SingleOrDefault();
                    }
                    else
                    {
                        ev = context.Events.SingleOrDefault(e => e.Id == reg.EventId);
                    }

                    if (ev == null)
                    {
                        return Fail(404, "Мероприятие не найдено");
                    }

                    Log(clientId, "RegisterParticipant", "CHECK_PLACES", $"AvailablePlaces={ev.AvailablePlaces}");
                    if (ev.AvailablePlaces <= 0)
                    {
                        return Fail(409, "Нет свободных мест");
                    }

                    if (!context.Participants.Any(p => p.Id == reg.ParticipantId))
                    {
                        return Fail(404, "Участник не найден");
                    }

                    if (context.Registrations.Any(r => r.EventId == reg.EventId && r.ParticipantId == reg.ParticipantId))
                    {
                        return Fail(409, "Участник уже зарегистрирован");
                    }

                    if (delayMs > 0)
                    {
                        Thread.Sleep(delayMs);
                    }

                    Registration registration = new Registration
                    {
                        EventId = reg.EventId,
                        ParticipantId = reg.ParticipantId
                    };
                    context.Registrations.Add(registration);
                    context.SaveChanges();
                    Log(clientId, "RegisterParticipant", "OPERATION_1", $"Регистрация Id={registration.Id}");
                    if (simulateError)
                    {
                        Log(clientId, "RegisterParticipant", "ERROR", "Искусственная ошибка между операциями");
                        if (transaction != null)
                        {
                            transaction.Rollback();
                            Log(clientId, "RegisterParticipant", "ROLLBACK");
                            return new OperationResult(409, "Ошибка: изменения отменены", "ROLLBACK");
                        }

                        Log(clientId, "RegisterParticipant", "PARTIAL_CHANGE");
                        return new OperationResult(409, "Ошибка: регистрация сохранена, число мест не изменено", "PARTIAL_CHANGE");
                    }

                    ev.AvailablePlaces--;
                    registration.PlaceReserved = true;
                    context.SaveChanges();
                    Log(clientId, "RegisterParticipant", "OPERATION_2", $"AvailablePlaces={ev.AvailablePlaces}");
                    string outcome = "AUTOCOMMIT";
                    if (transaction != null)
                    {
                        transaction.Commit();
                        outcome = "COMMIT";
                    }

                    Log(clientId, "RegisterParticipant", outcome);
                    Log(clientId, "RegisterParticipant", "SUCCESS", $"Id={registration.Id}");
                    return new OperationResult(200, "Регистрация успешна", outcome, registration.Id);
                }
                catch
                {
                    if (transaction != null)
                    {
                        try
                        {
                            transaction.Rollback();
                            Log(clientId, "RegisterParticipant", "ROLLBACK");
                        }
                        catch (Exception ex) when (ex is NpgsqlException || ex is InvalidOperationException)
                        {
                            Log(clientId, "RegisterParticipant", "ROLLBACK_UNAVAILABLE", "Транзакция завершена или соединение потеряно; проверьте БД");
                        }
                    }

                    if (useTransaction)
                    {
                        Log(clientId, "RegisterParticipant", "FAIL", "Ошибка БД/COMMIT");
                    }
                    else
                    {
                        Log(clientId, "RegisterParticipant", "FAIL", "Возможны частичные изменения");
                    }

                    throw;
                }

                OperationResult Fail(int status, string message)
                {
                    if (transaction != null)
                    {
                        transaction.Rollback();
                    }

                    if (useTransaction)
                    {
                        Log(clientId, "RegisterParticipant", "ROLLBACK");
                    }

                    Log(clientId, "RegisterParticipant", "FAIL", message);
                    return new OperationResult(status, message, "FAIL");
                }
            }
        }

        public OperationResult Cancel(int id, string clientId)
        {
            Log(clientId, "CancelRegistration", "START");
            if (id <= 0 || string.IsNullOrWhiteSpace(clientId))
            {
                return new OperationResult(400, "Некорректный запрос", "FAIL");
            }

            using (EventDbContext context = CreateContext())
            {
                context.Database.OpenConnection();
                lock (syncRoot)
                {
                    Log(clientId, "CancelRegistration", "LOCK_ENTER");
                    try
                    {
                        using (IDbContextTransaction transaction = context.Database.BeginTransaction())
                        {
                            try
                            {
                                Registration? registration = context.Registrations.SingleOrDefault(r => r.Id == id);
                                if (registration == null)
                                {
                                    transaction.Rollback();
                                    Log(clientId, "CancelRegistration", "ROLLBACK");
                                    Log(clientId, "CancelRegistration", "FAIL", "Регистрация не найдена");
                                    return new OperationResult(404, "Регистрация не найдена", "FAIL");
                                }

                                Event ev = context.Events.FromSqlInterpolated($"SELECT * FROM events WHERE id={registration.EventId} FOR UPDATE").Single();
                                if (registration.PlaceReserved)
                                {
                                    ev.AvailablePlaces++;
                                }

                                context.Registrations.Remove(registration);
                                context.SaveChanges();
                                transaction.Commit();
                                Log(clientId, "CancelRegistration", "COMMIT");
                                return new OperationResult(200, "Регистрация отменена, место возвращено", "COMMIT", id);
                            }
                            catch
                            {
                                try
                                {
                                    transaction.Rollback();
                                    Log(clientId, "CancelRegistration", "ROLLBACK");
                                }
                                catch (Exception ex) when (ex is NpgsqlException || ex is InvalidOperationException)
                                {
                                    Log(clientId, "CancelRegistration", "ROLLBACK_UNAVAILABLE");
                                }

                                throw;
                            }
                        }
                    }
                    finally
                    {
                        Log(clientId, "CancelRegistration", "LOCK_EXIT");
                    }
                }
            }
        }

        public static void Log(string clientId, string operation, string phase, string details = "")
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [{clientId}] {operation} [{phase}] {details}");
        }
    }

    public class OperationResult
    {
        public int StatusCode { get; }
        public string Message { get; }
        public string Outcome { get; }
        public int? Id { get; }

        public OperationResult(int statusCode, string message, string outcome, int? id = null)
        {
            StatusCode = statusCode;
            Message = message;
            Outcome = outcome;
            Id = id;
        }
    }
}
