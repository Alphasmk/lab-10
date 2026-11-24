using System.Net.Http.Json;
using System.Text.Json;
using EventManagement;

namespace Client
{
    public class ClientApplication
    {
        private readonly HttpClient client;
        private readonly TextReader input;
        private readonly TextWriter output;
        private readonly string clientId;
        public ClientApplication(HttpClient client, TextReader input, TextWriter output, string clientId = "Client1")
        {
            this.client = client;
            this.input = input;
            this.output = output;
            this.clientId = clientId;
            client.DefaultRequestHeaders.Remove("X-Client-Id");
            client.DefaultRequestHeaders.Add("X-Client-Id", clientId);
        }

        public async Task ExecuteAsync(string choice)
        {
            try
            {
                HttpResponseMessage? response;
                switch (choice)
                {
                    case "1":
                        {
                            response = await CreateVenueAsync();
                            break;
                        }

                    case "2":
                        {
                            response = await CreateParticipantAsync();
                            break;
                        }

                    case "3":
                        {
                            response = await CreateEventAsync();
                            break;
                        }

                    case "4":
                        response = await client.GetAsync("/api/events");
                        break;
                    case "5":
                        response = await RegisterParticipantAsync();
                        break;
                    case "6":
                        response = await CancelRegistrationAsync();
                        break;
                    case "7":
                        response = await client.GetAsync("/api/registrations");
                        break;
                    case "8":
                        response = await client.GetAsync("/api/consensus");
                        break;
                    case "9":
                        response = await SetNodeAsync(false);
                        break;
                    case "10":
                        response = await client.PostAsync("/api/consensus/check", null);
                        break;
                    case "11":
                        response = await SetNodeAsync(true);
                        break;
                    default:
                        {
                            response = null;
                            break;
                        }
                }

                if (response == null)
                {
                    await output.WriteLineAsync("Неверная операция.");
                    return;
                }

                string result = await response.Content.ReadAsStringAsync();
                try
                {
                    using JsonDocument json = JsonDocument.Parse(result);
                    result = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
                }
                catch (JsonException)
                {
                }
                await output.WriteLineAsync($"\n[{clientId}] [{DateTime.UtcNow:HH:mm:ss.fff} UTC] Статус: {response.StatusCode}\nОтвет:\n{result}");
                await PrintInstanceAsync(response);
                response.Dispose();
            }
            catch (Exception ex)
            {
                await output.WriteLineAsync($"\n[{clientId}] Ошибка: {ex.Message}");
            }
        }

        private async Task PrintInstanceAsync(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("X-Data-Instance", out IEnumerable<string>? values))
            {
                await output.WriteLineAsync($"Использованный экземпляр: {values.First()}");
            }
            if (response.Headers.TryGetValues("X-Replication", out values))
            {
                await output.WriteLineAsync($"Репликация: {values.First()}");
            }
            if (response.Headers.Contains("X-Synchronization-Required"))
            {
                await output.WriteLineAsync("Требуется синхронизация копий после восстановления");
            }
            if (response.Headers.TryGetValues("X-Event-Publication", out values))
            {
                await output.WriteLineAsync(values.First() == "PUBLISHED"
                    ? "Событие опубликовано через Pub/Sub"
                    : "Ошибка публикации (подробности в журнале сервера)");
            }
            if (response.Headers.TryGetValues("X-Event-Broker", out values))
                await output.WriteLineAsync($"Брокер: {values.First()}");
        }

        private async Task<HttpResponseMessage> SetNodeAsync(bool vote)
        {
            await output.WriteAsync("ID узла (1–5): ");
            int id = await ReadIntAsync("ID узла");
            if (id > 5) throw new ArgumentException("ID узла должен быть от 1 до 5");
            await output.WriteAsync(vote ? "Голос YES (1) или NO (0): " : "Доступен (1/0): ");
            string? value = await input.ReadLineAsync();
            if (value is not ("1" or "0")) throw new ArgumentException("Введите 1 или 0");
            if (vote) return await client.PostAsJsonAsync($"/api/consensus/{id}/vote", new { Yes = value == "1" });
            return await client.PostAsJsonAsync($"/api/consensus/{id}/availability", new { Available = value == "1" });
        }

        private async Task<HttpResponseMessage> CreateEventAsync()
        {
            await output.WriteAsync("Название мероприятия: ");
            string? name = await input.ReadLineAsync();
            if (name == null)
            {
                name = "New Event";
            }

            await output.WriteAsync("Количество свободных мест: ");
            int places = await ReadIntAsync("Количество мест", allowZero: true);
            await output.WriteAsync("ID площадки: ");
            int venueId = await ReadIntAsync("ID площадки");
            return await client.PostAsJsonAsync($"/api/events?clientId={Uri.EscapeDataString(clientId)}", new Event { Name = name, AvailablePlaces = places, VenueId = venueId });
        }

        private async Task<HttpResponseMessage> RegisterParticipantAsync()
        {
            await output.WriteAsync("ID мероприятия: ");
            int eventId = await ReadIntAsync("ID мероприятия");
            await output.WriteAsync("ID участника: ");
            int participantId = await ReadIntAsync("ID участника");
            string endpoint = $"/api/registrations?clientId={Uri.EscapeDataString(clientId)}";
            return await client.PostAsJsonAsync(endpoint, new Registration { EventId = eventId, ParticipantId = participantId });
        }

        private async Task<HttpResponseMessage> CancelRegistrationAsync()
        {
            await output.WriteAsync("ID регистрации для отмены: ");
            int id = await ReadIntAsync("ID регистрации");
            return await client.DeleteAsync($"/api/registrations/{id}?clientId={Uri.EscapeDataString(clientId)}");
        }

        private async Task<int> ReadIntAsync(string field, bool allowZero = false)
        {
            int minimum = 1;
            string requirement = "положительное";
            if (allowZero)
            {
                minimum = 0;
                requirement = "неотрицательное";
            }

            if (!int.TryParse(await input.ReadLineAsync(), out int value) || value < minimum)
            {
                throw new ArgumentException($"{field}: требуется {requirement} целое число");
            }

            return value;
        }

        private async Task<HttpResponseMessage> CreateParticipantAsync()
        {
            await output.WriteAsync("Имя участника: ");
            string? name = await input.ReadLineAsync();
            if (name == null)
            {
                name = "";
            }

            return await client.PostAsJsonAsync("/api/participants", new Participant { Name = name });
        }

        private async Task<HttpResponseMessage> CreateVenueAsync()
        {
            await output.WriteAsync("Название площадки: ");
            string? name = await input.ReadLineAsync();
            if (name == null)
            {
                name = "";
            }

            await output.WriteAsync("Адрес: ");
            string? address = await input.ReadLineAsync();
            if (address == null)
            {
                address = "";
            }

            return await client.PostAsJsonAsync("/api/venues", new Venue { Name = name, Address = address });
        }

    }
}
