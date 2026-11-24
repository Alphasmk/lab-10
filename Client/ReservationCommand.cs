using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using EventManagement;

namespace Client
{
    public static class ReservationCommand
    {
        public static async Task<int> RunAsync(string[] args, HttpClient client)
        {
            try
            {
                string Value(string key, string? fallback = null)
                {
                    int index = Array.IndexOf(args, key);
                    if (index >= 0 && index + 1 < args.Length)
                    {
                        return args[index + 1];
                    }

                    if (fallback != null)
                    {
                        return fallback;
                    }

                    throw new ArgumentException($"Не задан {key}");
                }

                string id = Value("--client", "Client1");
                if (args.Contains("--url"))
                {
                    client.BaseAddress = new Uri(Value("--url"));
                }

                int eventId = int.Parse(Value("--event"));
                int participantId = int.Parse(Value("--participant"));
                if (eventId <= 0 || participantId <= 0)
                {
                    throw new ArgumentException("ID должен быть положительным");
                }

                if (args.Contains("--at"))
                {
                    TimeSpan remaining = DateTimeOffset.Parse(Value("--at"), CultureInfo.InvariantCulture) - DateTimeOffset.UtcNow;
                    if (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(remaining);
                    }
                }

                DateTimeOffset sent = DateTimeOffset.UtcNow;
                using (HttpResponseMessage response = await client.PostAsJsonAsync($"/api/registrations?clientId={Uri.EscapeDataString(id)}" + $"&useLock={!args.Contains("--no-lock")}&useTransaction={!args.Contains("--no-transaction")}" + $"&simulateError={args.Contains("--error")}&delayMs={int.Parse(Value("--delay", "0"))}", new Registration { EventId = eventId, ParticipantId = participantId }))
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        ClientId = id,
                        SentAt = sent,
                        ReceivedAt = DateTimeOffset.UtcNow,
                        StatusCode = (int)response.StatusCode,
                        Response = await response.Content.ReadAsStringAsync(),
                        Instance = response.Headers.TryGetValues("X-Data-Instance", out IEnumerable<string>? instances) ? instances.First() : null,
                        Replication = response.Headers.TryGetValues("X-Replication", out IEnumerable<string>? replication) ? replication.First() : null
                    }));
                    if (response.IsSuccessStatusCode)
                    {
                        return 0;
                    }

                    return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { Error = ex.Message }));
                return 1;
            }
        }
    }
}
