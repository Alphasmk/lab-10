using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Server.Tests;

public class ContainerTests
{
    private readonly HttpClient client = new() { BaseAddress = new Uri(Environment.GetEnvironmentVariable("LAB_TEST_SERVER_URL") ?? "http://localhost:5000"), Timeout = TimeSpan.FromSeconds(120) };

    private async Task<int> Create(string path, object value)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, value);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetInt32();
    }

    private async Task<int> Places(int eventId)
    {
        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/events"));
        return json.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == eventId)
            .GetProperty("availablePlaces").GetInt32();
    }

    [Fact]
    public async Task Load_OneHundredRegistrationsForTenPlaces()
    {
        string name = "Load " + Guid.NewGuid();
        int venue = await Create("/api/venues", new { name, address = "Test" });
        int eventId = await Create("/api/events", new { name, venueId = venue, availablePlaces = 10 });
        List<int> participants = new();
        for (int i = 0; i < 100; i++)
        {
            participants.Add(await Create("/api/participants", new { name = $"{name} {i}" }));
        }

        async Task<(HttpStatusCode Status, double Milliseconds)> Register(int participant)
        {
            Stopwatch watch = Stopwatch.StartNew();
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/registrations?clientId=Load", new { eventId, participantId = participant });
            return (response.StatusCode, watch.Elapsed.TotalMilliseconds);
        }

        Stopwatch total = Stopwatch.StartNew();
        var results = await Task.WhenAll(participants.Select(Register));
        total.Stop();
        double[] latencies = results.Select(result => result.Milliseconds).Order().ToArray();
        Console.WriteLine($"Время: {total.Elapsed.TotalSeconds:F2} с, запросов/с: {100 / total.Elapsed.TotalSeconds:F2}, p95: {latencies[(int)Math.Ceiling(latencies.Length * 0.95) - 1]:F0} мс");
        Console.WriteLine($"HTTP 200: {results.Count(result => result.Status == HttpStatusCode.OK)}, HTTP 409: {results.Count(result => result.Status == HttpStatusCode.Conflict)}");
        Assert.Equal(10, results.Count(result => result.Status == HttpStatusCode.OK));
        Assert.Equal(90, results.Count(result => result.Status == HttpStatusCode.Conflict));
        Assert.Equal(0, await Places(eventId));
    }
}

