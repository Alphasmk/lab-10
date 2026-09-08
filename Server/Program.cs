using EventManagement;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var events = new List<Event>();
var registrations = new List<Registration>();
var tickets = new List<Ticket>();

int eventIdCounter = 1;
int regIdCounter = 1;
int ticketIdCounter = 1;

app.MapGet("/api/events", () =>
{
    return Results.Ok(events);
});

app.MapGet("/api/registrations", () =>
{
    return Results.Ok(registrations);
});

app.MapGet("/api/tickets", () =>
{
    return Results.Ok(tickets);
});

app.MapPost("/api/events", (Event newEvent) =>
{
    newEvent.Id = eventIdCounter++;
    events.Add(newEvent);
    return Results.Ok(new { EventName = "EventCreated", Message = $"Мероприятие '{newEvent.Name}' создано", Id = newEvent.Id });
});

app.MapPost("/api/registrations", (Registration reg) =>
{
    reg.Id = regIdCounter++;
    registrations.Add(reg);
    return Results.Ok(new { EventName = "ParticipantRegistered", Message = "Регистрация успешна", Id = reg.Id });
});

app.MapDelete("/api/registrations/{id}", (int id) =>
{
    var reg = registrations.FirstOrDefault(r => r.Id == id);
    if (reg == null)
        return Results.NotFound(new { Message = "Регистрация не найдена" });

    registrations.Remove(reg);
    return Results.Ok(new { EventName = "RegistrationCancelled", Message = "Регистрация отменена" });
});

app.MapPost("/api/tickets/{registrationId}", (int registrationId) =>
{
    if (!registrations.Any(r => r.Id == registrationId))
        return Results.NotFound(new { Message = "Регистрация не найдена" });

    var ticket = new Ticket { Id = ticketIdCounter++, RegistrationId = registrationId };
    tickets.Add(ticket);
    return Results.Ok(new { EventName = "TicketIssued", Message = "Билет выпущен", TicketId = ticket.Id });
});

app.Run("http://localhost:5000");