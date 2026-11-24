using EventManagement;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server;
using Messaging;
using System.Text;

namespace Server
{
    public record AvailabilityRequest(bool? Available);
    public record FailRequest(int Count);
    public record VoteRequest(bool? Yes);

    public class Program
    {
        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine($"Application mode: {Environment.GetEnvironmentVariable("APP_MODE") ?? "default"}");
            ThreadPool.GetMinThreads(out int workerThreads, out int completionThreads);
            ThreadPool.SetMinThreads(Math.Max(workerThreads, 32), completionThreads);
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            WebApplication app = builder.Build();
            PubSubRuntime bus = new();
            ConsensusCluster consensus = new();
            using Timer heartbeat = new Timer(_ => consensus.Check(), null, 1000, 1000);
            EventPublisher publisher = bus.Publisher;
            string? connectionString = builder.Configuration.GetConnectionString("Database");
            if (connectionString == null)
            {
                throw new InvalidOperationException("Не задана строка подключения Database");
            }

            string replicaConnection = builder.Configuration.GetConnectionString("Replica")
                ?? throw new InvalidOperationException("Не задана строка подключения Replica");
            NpgsqlConnectionStringBuilder primarySettings = new NpgsqlConnectionStringBuilder(connectionString);
            NpgsqlConnectionStringBuilder replicaSettings = new NpgsqlConnectionStringBuilder(replicaConnection);
            if (primarySettings.Host == replicaSettings.Host && primarySettings.Port == replicaSettings.Port
                && primarySettings.Database == replicaSettings.Database)
            {
                throw new InvalidOperationException("Primary и Replica должны быть разными БД");
            }
            ReplicatedDatabase cluster = new ReplicatedDatabase(new Database(connectionString), new Database(replicaConnection), builder.Configuration["ReplicationStatePath"]);
            app.Use(async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (Exception ex) when (ex is NpgsqlException || ex is DbUpdateException || ex is BadHttpRequestException || ex is ArgumentException || ex is InvalidOperationException || ex is IOException || ex is TimeoutException)
                {
                    int status = 503;
                    string message = ex is StorageUnavailableException ? ex.Message
                        : "Ошибка хранилища: операция или синхронизация не завершена. Проверьте состояние копий перед повтором.";
                    if (ex.GetBaseException() is PostgresException pg)
                    {
                        if (pg.SqlState == "23505")
                        {
                            status = 409;
                            message = "Запись уже существует";
                        }
                        else if (pg.SqlState == "23503")
                        {
                            status = 404;
                            message = "Связанная запись не найдена";
                        }
                        else if (pg.SqlState.StartsWith("23"))
                        {
                            status = 400;
                            message = "Нарушено ограничение целостности БД";
                        }
                    }
                    else if (ex is BadHttpRequestException || ex is ArgumentException)
                    {
                        status = 400;
                        message = "Некорректный запрос";
                    }

                    app.Logger.LogWarning("{Message}: {Error}", message, ex.GetBaseException().Message);
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = status;
                        await context.Response.WriteAsJsonAsync(new { Message = message, Outcome = "FAIL" });
                    }
                }
            });

            T Execute<T>(HttpContext http, string operation, bool write, Func<IDataStore, T> action)
            {
                string clientId = http.Request.Query["clientId"].FirstOrDefault()
                    ?? http.Request.Headers["X-Client-Id"].FirstOrDefault() ?? "Client";
                StorageResponse<T> response = cluster.Execute(clientId, operation, write, action);
                http.Response.Headers["X-Data-Instance"] = response.Instance;
                http.Response.Headers["X-Replication"] = response.Replication;
                if (response.Warning != null)
                {
                    http.Response.Headers["X-Synchronization-Required"] = "true";
                }
                return response.Value;
            }
            string ClientId(HttpContext http)
            {
                return http.Request.Headers["X-Client-Id"].FirstOrDefault()
                    ?? http.Request.Query["clientId"].FirstOrDefault() ?? "Client";
            }
            async Task Publish(HttpContext http, params EventMessage[] messages)
            {
                bool allPublished = true;
                foreach (EventMessage message in messages)
                {
                    PublishResult result = await publisher.PublishAsync(message);
                    allPublished &= result.Success;
                    if (result.Success) http.Response.Headers["X-Event-Broker"] = result.Broker;
                    if (!result.Success) app.Logger.LogError("Событие {Type} не опубликовано: {Error}", message.EventType, result.Error);
                }
                http.Response.Headers["X-Event-Publication"] = allPublished ? "PUBLISHED" : "FAILED";
            }
            app.MapGet("/api/health", (HttpContext http) => cluster.Status(ClientId(http)));
            app.MapGet("/api/replication", (HttpContext http) => cluster.Status(ClientId(http)));
            app.MapPost("/api/replication/{instance}/availability", (string instance, AvailabilityRequest request, HttpContext http) =>
            {
                cluster.Initialize();
                if (request.Available == null) return Results.BadRequest(new { Message = "Укажите Available" });
                cluster.SetAvailable(instance, request.Available.Value, ClientId(http));
                return Results.Ok(new { Message = $"{instance}: {(request.Available.Value ? "восстановлен" : "отключен")}" });
            });
            app.MapPost("/api/replication/synchronize", (HttpContext http) =>
            {
                cluster.Synchronize(ClientId(http));
                return Results.Ok(cluster.Status(ClientId(http)));
            });
            app.MapGet("/api/pubsub", () => bus.Status());
            app.MapGet("/api/consensus", () => consensus.Status());
            app.MapPost("/api/consensus/check", () => { consensus.Check(); consensus.Elect(); return Results.Ok(consensus.Status()); });
            app.MapPost("/api/consensus/{id:int}/availability", (int id, AvailabilityRequest request) =>
            {
                if (request.Available == null) return Results.BadRequest(new { Message = "Укажите Available" });
                try { consensus.SetAvailable(id, request.Available.Value); return Results.Ok(consensus.Status()); }
                catch (ArgumentException ex) { return Results.BadRequest(new { ex.Message }); }
            });
            app.MapPost("/api/consensus/{id:int}/vote", (int id, VoteRequest request) =>
            {
                if (request.Yes == null) return Results.BadRequest(new { Message = "Укажите Yes" });
                try { consensus.SetVote(id, request.Yes.Value); return Results.Ok(consensus.Status()); }
                catch (ArgumentException ex) { return Results.BadRequest(new { ex.Message }); }
            });
            app.MapPost("/api/pubsub/subscribers/analytics", () => { bus.AddAnalytics(); return Results.Ok(bus.Status()); });
            app.MapPost("/api/pubsub/subscribers/{name}/availability", (string name, AvailabilityRequest request) =>
            {
                if (request.Available == null) return Results.BadRequest(new { Message = "Укажите Available" });
                bus.SetSubscriberAvailable(name, request.Available.Value);
                return Results.Ok(bus.Status());
            });
            app.MapPost("/api/pubsub/subscribers/{name}/fail-next", (string name, FailRequest request) =>
            {
                bus.FailNext(name, request.Count);
                return Results.Ok(bus.Status());
            });
            app.MapPost("/api/pubsub/brokers/{name}/availability", (string name, AvailabilityRequest request) =>
            {
                if (request.Available == null) return Results.BadRequest(new { Message = "Укажите Available" });
                bus.SetBrokerAvailable(name, request.Available.Value);
                return Results.Ok(bus.Status());
            });
            app.MapGet("/api/events", (HttpContext http) => Execute(http, "ReadEvents", false, db => db.ReadSnapshot().Events));
            app.MapGet("/api/registrations", (HttpContext http) => Execute(http, "ReadRegistrations", false, db => db.ReadSnapshot().Registrations));
            app.MapGet("/api/participants", (HttpContext http) => Execute(http, "ReadParticipants", false, db => db.ReadSnapshot().Participants));
            app.MapGet("/api/venues", (HttpContext http) => Execute(http, "ReadVenues", false, db => db.ReadSnapshot().Venues));
            app.MapPost("/api/events", (Event ev, HttpContext http) =>
            {
                if (string.IsNullOrWhiteSpace(ev.Name) || ev.VenueId <= 0 || ev.AvailablePlaces < 0)
                {
                    return Results.BadRequest(new { Message = "Проверьте название, площадку и число мест" });
                }

                ev.Id = 0;
                ev.Name = ev.Name.Trim();
                ev.Capacity = ev.AvailablePlaces;
                Execute(http, "CreateEvent", true, db => { ((Database)db).Add(ev); return ev.Id; });
                return Results.Ok(new { Message = "Мероприятие создано", Id = ev.Id });
            });
            app.MapPost("/api/participants", (Participant p, HttpContext http) =>
            {
                if (string.IsNullOrWhiteSpace(p.Name))
                {
                    return Results.BadRequest(new { Message = "Введите имя участника" });
                }

                p.Id = 0;
                p.Name = p.Name.Trim();
                Execute(http, "CreateParticipant", true, db => { ((Database)db).Add(p); return p.Id; });
                return Results.Ok(new { Id = p.Id });
            });
            app.MapPost("/api/venues", (Venue v, HttpContext http) =>
            {
                if (string.IsNullOrWhiteSpace(v.Name))
                {
                    return Results.BadRequest(new { Message = "Введите название площадки" });
                }

                v.Id = 0;
                v.Name = v.Name.Trim();
                if (v.Address == null)
                {
                    v.Address = "";
                }

                Execute(http, "CreateVenue", true, db => { ((Database)db).Add(v); return v.Id; });
                return Results.Ok(new { Id = v.Id });
            });
            app.MapPost("/api/registrations", async (Registration reg, string clientId, HttpContext http, bool useTransaction = true, bool simulateError = false, bool useLock = true, int delayMs = 0) =>
            {
                var outcome = consensus.Execute($"RegistrationStatus=Registered; EventId={reg.EventId}; ParticipantId={reg.ParticipantId}", () => Execute(http, "RegisterParticipant", true, db => ((Database)db).Register(reg, clientId, useTransaction, simulateError, useLock, delayMs)));
                if (outcome.Result == null) return Results.Json(outcome.Vote, statusCode: 503);
                OperationResult result = outcome.Result;
                if (result.StatusCode < 400 && result.Id is int id)
                    await Publish(http, EventMessage.Create("ParticipantRegistered", id, clientId, $"EventId={reg.EventId}; ParticipantId={reg.ParticipantId}"),
                        EventMessage.Create("TicketIssued", id, clientId, $"EventId={reg.EventId}; ParticipantId={reg.ParticipantId}"));
                return Results.Json(result, statusCode: result.StatusCode);
            });
            app.MapDelete("/api/registrations/{id:int}", async (int id, string clientId, HttpContext http) =>
            {
                var outcome = consensus.Execute($"RegistrationStatus=Cancelled; RegistrationId={id}", () => Execute(http, "CancelRegistration", true, db => ((Database)db).Cancel(id, clientId)));
                if (outcome.Result == null) return Results.Json(outcome.Vote, statusCode: 503);
                OperationResult result = outcome.Result;
                if (result.StatusCode < 400)
                    await Publish(http, EventMessage.Create("RegistrationCancelled", id, clientId));
                return Results.Json(result, statusCode: result.StatusCode);
            });
            app.MapFallback(() =>
            {
                return Results.NotFound(new { Message = "Неизвестная операция" });
            });
            string? url = builder.Configuration["Urls"];
            if (url == null)
            {
                url = "http://localhost:5000";
            }

            app.Run(url);
        }
    }
}
