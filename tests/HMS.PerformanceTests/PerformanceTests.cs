using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using HMS.Api.IntegrationTests;
using HMS.Contracts.Auth;
using HMS.Contracts.Guests;
using HMS.Contracts.Reservations;
using Xunit.Abstractions;

namespace HMS.PerformanceTests;

/// <summary>Runs only when HMS_SQLSERVER_TEST_CONNECTION is set. Timings on SQLite say nothing about production.</summary>
public sealed class PerformanceFactAttribute : FactAttribute
{
    public PerformanceFactAttribute()
    {
        if (!HmsApiFactory.UsesSqlServer) Skip = "Set HMS_SQLSERVER_TEST_CONNECTION to run performance tests against SQL Server.";
    }
}

/// <summary>
/// Measures the blueprint's p95 targets on a seeded hotel, checks the last rooms under a burst of bookings, and runs a
/// short mixed workload. The in-process host and a local SQL Server make these a floor, not a production benchmark:
/// they catch regressions such as a missing index or an N+1 query. Results are written to the test output and to
/// TestResults/performance.md.
/// </summary>
public sealed class PerformanceTests(HmsApiFactory factory, ITestOutputHelper output) : IClassFixture<HmsApiFactory>
{
    private const int Samples = 40;
    private const int GuestCount = 300;
    private const int ReservationCount = 120;

    private static readonly ConcurrentDictionary<string, string> Report = new();

    [PerformanceFact]
    public async Task Common_operations_meet_their_p95_targets()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 40);
        await SeedAsync(hotel);
        var folioId = (await hotel.BookAsync(nights: 2, arrival: hotel.Today.AddDays(90))).FolioId!.Value;

        var results = new List<(string Name, double P95, int TargetMs)>
        {
            await MeasureAsync("Login", 1000, async _ =>
            {
                using var client = factory.CreateClient();
                return await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(HmsApiFactory.AdminEmail, HmsApiFactory.AdminPassword));
            }),
            await MeasureAsync("Room availability search", 1000, i =>
                hotel.Admin.GetAsync(hotel.P($"availability?arrivalDate={hotel.Today.AddDays(i % 30):yyyy-MM-dd}&departureDate={hotel.Today.AddDays(i % 30 + 3):yyyy-MM-dd}&adults=2"))),
            await MeasureAsync("Guest search", 750, i => hotel.Admin.GetAsync(hotel.P($"guests?search=Guest{i % 50}&pageSize=20"))),
            await MeasureAsync("Reservation creation", 2000, i =>
                hotel.PostBookingAsync(hotel.Admin, HotelSetup.Booking(hotel.Standard.Id, hotel.Today.AddDays(120 + i * 2), 1, $"Perf{i}"))),
            await MeasureAsync("Folio and payment history", 1000, _ => hotel.Admin.GetAsync(hotel.P($"folios/{folioId}"))),
            await MeasureAsync("Standard dashboard", 2000, _ =>
                hotel.Admin.GetAsync(hotel.P($"analytics/dashboard?from={hotel.Today.AddDays(-30):yyyy-MM-dd}&to={hotel.Today:yyyy-MM-dd}"))),
        };

        var table = new StringBuilder("| Operation | p95 (ms) | Target (ms) |\n| --- | ---: | ---: |\n");
        foreach (var r in results) table.Append($"| {r.Name} | {r.P95:F0} | {r.TargetMs} |\n");
        Write("p95", table.ToString());
        Assert.All(results, r => Assert.True(r.P95 <= r.TargetMs, $"{r.Name}: p95 {r.P95:F0} ms is over the {r.TargetMs} ms target"));
    }

    [PerformanceFact]
    public async Task A_burst_of_bookings_for_the_last_rooms_never_oversells()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 3);
        var arrival = hotel.Today.AddDays(10);
        var tasks = Enumerable.Range(0, 25).Select(async i =>
        {
            var client = i % 2 == 0 ? hotel.Admin : await hotel.StaffAsync(HotelSetup.Receptionist);
            return await hotel.PostBookingAsync(client, HotelSetup.Booking(hotel.Standard.Id, arrival, 2, $"Burst{i}"));
        }).ToList();
        var responses = await Task.WhenAll(tasks);

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var crashed = responses.Count(r => (int)r.StatusCode >= 500);
        Write("burst", $"25 bookings for 3 rooms: {created} created, {responses.Length - created - crashed} refused, {crashed} server errors.\n");
        Assert.Equal(0, crashed);
        Assert.Equal(3, created);
        var availability = await hotel.GetAsync<List<RoomTypeAvailabilityDto>>(
            $"availability?arrivalDate={arrival:yyyy-MM-dd}&departureDate={arrival.AddDays(2):yyyy-MM-dd}");
        Assert.Equal(0, availability.Single().AvailableRooms);
    }

    [PerformanceFact]
    public async Task A_sustained_front_desk_workload_has_no_errors()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 40);
        await SeedAsync(hotel);
        var workers = 8;
        var duration = TimeSpan.FromSeconds(20);
        var latencies = new ConcurrentBag<double>();
        var failures = new ConcurrentBag<string>();
        var clients = new List<HttpClient> { hotel.Admin };
        for (var i = 1; i < workers; i++) clients.Add(await hotel.StaffAsync(HotelSetup.Receptionist));

        var stop = Stopwatch.StartNew();
        await Task.WhenAll(clients.Select((client, w) => Task.Run(async () =>
        {
            var n = 0;
            while (stop.Elapsed < duration)
            {
                n++;
                var watch = Stopwatch.StartNew();
                using var response = (n % 5) switch
                {
                    0 => await hotel.PostBookingAsync(client, HotelSetup.Booking(hotel.Standard.Id, hotel.Today.AddDays(200 + (w * 1000 + n) % 300), 1, $"Load{w}x{n}")),
                    1 => await client.GetAsync(hotel.P($"guests?search=Guest{n % 50}")),
                    2 => await client.GetAsync(hotel.P("reservations?pageSize=20")),
                    _ => await client.GetAsync(hotel.P($"availability?arrivalDate={hotel.Today.AddDays(n % 60):yyyy-MM-dd}&departureDate={hotel.Today.AddDays(n % 60 + 2):yyyy-MM-dd}")),
                };
                latencies.Add(watch.Elapsed.TotalMilliseconds);
                // A booking can lose to another worker for the last room; anything else is a failure.
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
                    failures.Add($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri?.PathAndQuery}");
            }
        })));

        var sorted = latencies.Order().ToArray();
        var summary = $"{workers} workers for {duration.TotalSeconds:F0} s: {sorted.Length} requests " +
                      $"({sorted.Length / duration.TotalSeconds:F0}/s), p50 {Percentile(sorted, 0.50):F0} ms, " +
                      $"p95 {Percentile(sorted, 0.95):F0} ms, p99 {Percentile(sorted, 0.99):F0} ms, {failures.Count} errors.\n";
        Write("sustained", summary);
        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(10)));
        Assert.True(Percentile(sorted, 0.95) <= 2000, summary);
    }

    private async Task SeedAsync(HotelSetup hotel)
    {
        await Parallel.ForEachAsync(Enumerable.Range(0, GuestCount), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, _) =>
        {
            var response = await hotel.Admin.PostAsJsonAsync(hotel.P("guests"),
                new SaveGuestRequest($"Guest{i % 50}", $"Seeded{i}", $"guest{i}@seed.test", $"+23480{i:D8}", "NG", null, null, null, null));
            response.EnsureSuccessStatusCode();
        });
        await Parallel.ForEachAsync(Enumerable.Range(0, ReservationCount), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, _) =>
        {
            var response = await hotel.PostBookingAsync(hotel.Admin, HotelSetup.Booking(hotel.Standard.Id, hotel.Today.AddDays(i % 60), 1 + i % 4, $"Seed{i}"));
            if (response.StatusCode != HttpStatusCode.Created && response.StatusCode != HttpStatusCode.Conflict)
                Assert.Fail($"Seeding booking {i} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        });
    }

    private static async Task<(string, double, int)> MeasureAsync(string name, int targetMs, Func<int, Task<HttpResponseMessage>> call)
    {
        for (var i = 0; i < 5; i++) (await call(-1 - i)).Dispose(); // warm up caches and query plans
        var timings = new double[Samples];
        for (var i = 0; i < Samples; i++)
        {
            var watch = Stopwatch.StartNew();
            using var response = await call(i);
            timings[i] = watch.Elapsed.TotalMilliseconds;
            if (!response.IsSuccessStatusCode) Assert.Fail($"{name} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
        Array.Sort(timings);
        return (name, Percentile(timings, 0.95), targetMs);
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    private void Write(string key, string text)
    {
        output.WriteLine(text);
        Report[key] = text;
        var folder = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "performance.md"),
            "# Performance run\n\n" + string.Join("\n", Report.OrderBy(k => k.Key).Select(k => k.Value)));
    }
}
