using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Sweep;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Matching;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Sweep;

public class SweepExecutorTests : IDisposable
{
    private readonly List<string> _dbPaths = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // ── Dry-run ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_DryRun_NoDestructiveCallsMade()
    {
        var handler = new RecordingHandler();
        var (executor, db) = CreateExecutor(handler, globalDryRun: true);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-438631", arrConnId: 1);

        // Enqueue the GET /api/v3/movie response so the executor can build the index
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(438631));

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.True(result.WasDryRun);
        Assert.Equal(1, result.ItemsSwept);
        Assert.Equal(0, result.ItemsFailed);
        // The only call should be GET /movie (loading index) — no PUT/DELETE
        Assert.DoesNotContain(handler.Calls, c => c.Method == HttpMethod.Put);
        Assert.DoesNotContain(handler.Calls, c => c.Method == HttpMethod.Delete);
    }

    // ── DeleteAndUnmonitor call ordering ──────────────────────────────────────

    [Fact]
    public async Task Execute_DeleteAndUnmonitor_UnmonitorBeforeDelete()
    {
        var handler = new RecordingHandler();
        var (executor, db) = CreateExecutor(handler, globalDryRun: false);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-438631", arrConnId: 1);

        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(438631));
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/1", 200, SingleMovieJson(1, 438631));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/1", 200, SingleMovieJson(1, 438631, monitored: false));
        handler.Enqueue(HttpMethod.Delete, "/api/v3/movie/1", 200, "null");

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(1, result.ItemsSwept);
        Assert.Equal(0, result.ItemsFailed);

        // Verify strict ordering: GET list → GET item → PUT (unmonitor) → DELETE
        var writeCalls = handler.Calls
            .Where(c => c.Method == HttpMethod.Put || c.Method == HttpMethod.Delete)
            .ToList();

        Assert.Equal(2, writeCalls.Count);
        Assert.Equal(HttpMethod.Put, writeCalls[0].Method);    // unmonitor first
        Assert.Equal(HttpMethod.Delete, writeCalls[1].Method); // delete second
    }

    // ── Unmonitor failure aborts delete ───────────────────────────────────────

    [Fact]
    public async Task Execute_UnmonitorFails_AbortsDelete_ItemMarkedFailed()
    {
        var handler = new RecordingHandler();
        var (executor, db) = CreateExecutor(handler, globalDryRun: false);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        var item = await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-438631", arrConnId: 1);

        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(438631));
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/1", 200, SingleMovieJson(1, 438631));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/1", 500, "\"Internal Server Error\"");
        // No DELETE should follow

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(0, result.ItemsSwept);
        Assert.Equal(1, result.ItemsFailed);

        // Verify no DELETE was issued
        Assert.DoesNotContain(handler.Calls, c => c.Method == HttpMethod.Delete);

        // Verify item status
        var sweepItem = await db.SweepItems.FindAsync(item.Id);
        Assert.Equal(SweepItemStatus.Failed, sweepItem!.Status);
        Assert.Contains("Unmonitor", sweepItem.SkippedReason);
    }

    // ── 404 on delete = already gone = success ────────────────────────────────

    [Fact]
    public async Task Execute_DeleteReturns404_TreatedAsSuccess()
    {
        var handler = new RecordingHandler();
        var (executor, db) = CreateExecutor(handler, globalDryRun: false);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-438631", arrConnId: 1);

        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(438631));
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/1", 200, SingleMovieJson(1, 438631));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/1", 200, SingleMovieJson(1, 438631, monitored: false));
        handler.Enqueue(HttpMethod.Delete, "/api/v3/movie/1", 404, "null");

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(1, result.ItemsSwept);
        Assert.Equal(0, result.ItemsFailed);
    }

    // ── One item fails, batch continues ──────────────────────────────────────

    [Fact]
    public async Task Execute_OneItemFails_BatchContinues()
    {
        var handler = new RecordingHandler();
        var (executor, db) = CreateExecutor(handler, globalDryRun: false);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-111", arrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-222", arrConnId: 1);

        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(111, 222));

        // Item 1 (tmdbId=111, radarrId=1): unmonitor fails
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/1", 200, SingleMovieJson(1, 111));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/1", 503, "\"Service Unavailable\"");

        // Item 2 (tmdbId=222, radarrId=2): succeeds
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/2", 200, SingleMovieJson(2, 222));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/2", 200, SingleMovieJson(2, 222, monitored: false));
        handler.Enqueue(HttpMethod.Delete, "/api/v3/movie/2", 200, "null");

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(1, result.ItemsSwept);
        Assert.Equal(1, result.ItemsFailed);
    }

    // ── Failsafe: item count cap ──────────────────────────────────────────────

    [Fact]
    public async Task Execute_FailsafeItemCap_HaltsAtLimit()
    {
        var handler = new RecordingHandler();
        var (executor, db) = CreateExecutor(handler, globalDryRun: false, maxItemsPerRun: 2);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-111", arrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-222", arrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-333", arrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-444", arrConnId: 1);

        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(111, 222, 333, 444));

        // Item 1: success
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/1", 200, SingleMovieJson(1, 111));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/1", 200, SingleMovieJson(1, 111, monitored: false));
        handler.Enqueue(HttpMethod.Delete, "/api/v3/movie/1", 200, "null");
        // Item 2: success
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/2", 200, SingleMovieJson(2, 222));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/2", 200, SingleMovieJson(2, 222, monitored: false));
        handler.Enqueue(HttpMethod.Delete, "/api/v3/movie/2", 200, "null");

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(2, result.ItemsSwept);
        Assert.Equal(0, result.ItemsFailed);
        Assert.Equal(2, result.ItemsSkippedByFailsafe);

        // Remaining 2 items should still be Approved with SkippedReason set
        var skipped = await db.SweepItems
            .Where(s => s.SkippedReason != null && s.Status == SweepItemStatus.Approved)
            .ToListAsync();
        Assert.Equal(2, skipped.Count);
        Assert.All(skipped, s => Assert.Contains("Failsafe", s.SkippedReason));
    }

    // ── Failsafe: GB cap ──────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_FailsafeGbCap_HaltsWhenExceeded()
    {
        var handler = new RecordingHandler();
        // Cap = 1.5 GB; each item = 1 GB. First item (1 GB) passes; second (2 GB projected) trips cap.
        var (executor, db) = CreateExecutor(handler, globalDryRun: false, maxItemsPerRun: 100, maxGbPerRun: 1.5);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-111", arrConnId: 1,
            sizeBytes: 1_073_741_824L); // 1 GB
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-222", arrConnId: 1,
            sizeBytes: 1_073_741_824L); // 1 GB

        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(111, 222));
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/1", 200, SingleMovieJson(1, 111));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/1", 200, SingleMovieJson(1, 111, monitored: false));
        handler.Enqueue(HttpMethod.Delete, "/api/v3/movie/1", 200, "null");

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        // First item swept (1 GB ≤ 1.5 GB cap), second skipped (2 GB projected > 1.5 GB cap)
        Assert.Equal(1, result.ItemsSwept);
        Assert.Equal(1, result.ItemsSkippedByFailsafe);
    }

    // ── No connection found ──────────────────────────────────────────────────

    [Fact]
    public async Task Execute_NoConnection_ItemsMarkedFailed()
    {
        var handler = new RecordingHandler();
        // No ServerConnection in DB
        var (executor, db) = CreateExecutor(handler, globalDryRun: false, seedConnection: false);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: null);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-438631", arrConnId: null);

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(0, result.ItemsSwept);
        Assert.Equal(1, result.ItemsFailed);

        var item = await db.SweepItems.FirstAsync();
        Assert.Equal(SweepItemStatus.Failed, item.Status);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private (SweepExecutor Executor, SweeprrDbContext Db) CreateExecutor(
        RecordingHandler handler,
        bool globalDryRun = true,
        int maxItemsPerRun = 20,
        double maxGbPerRun = 50.0,
        bool seedConnection = true)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_exec_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);

        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));

        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
            db.Database.Migrate();

            db.GlobalSettings.Add(new GlobalSettings
            {
                GlobalDryRun = globalDryRun,
                MaxItemsPerRun = maxItemsPerRun,
                MaxGbPerRun = maxGbPerRun,
                JwtSecret = "test",
            });

            if (seedConnection)
            {
                db.ServerConnections.Add(new ServerConnection
                {
                    Id = 1,
                    Type = ConnectionType.Radarr,
                    Name = "Radarr",
                    BaseUrl = "http://radarr:7878",
                    ApiKeyEncrypted = "test-key",
                    IsEnabled = true,
                });
            }

            db.SaveChanges();
        }

        var db2 = sp.CreateScope().ServiceProvider.GetRequiredService<SweeprrDbContext>();

        var clientFactory = new FakeClientFactory(handler);
        var matcher = new MediaMatchingService();
        var failsafe = new FailsafeService();

        var executor = new SweepExecutor(
            db2, clientFactory, matcher, failsafe,
            NullLogger<SweepExecutor>.Instance);

        return (executor, db2);
    }

    private static async Task<RuleGroup> SeedGroupAsync(
        SweeprrDbContext db,
        SweepAction action,
        int? radarrConnId = 1)
    {
        var group = new RuleGroup
        {
            Name = "Test Group",
            MediaType = MediaType.Movie,
            Action = action,
            IsEnabled = true,
        };
        db.RuleGroups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<SweepItem> SeedApprovedMovieItemAsync(
        SweeprrDbContext db,
        int groupId,
        string tmdbIdStr,
        int? arrConnId,
        long? sizeBytes = 5_368_709_120L) // 5 GB default
    {
        // Parse tmdbId from string like "tmdb-438631"
        var rawTmdb = tmdbIdStr.Replace("tmdb-", "");
        var item = new SweepItem
        {
            RuleGroupId = groupId,
            MediaServerItemId = $"jf-{tmdbIdStr}",
            Title = $"Movie {tmdbIdStr}",
            MediaType = MediaType.Movie,
            Status = SweepItemStatus.Approved,
            TmdbId = rawTmdb,
            ArrInstanceId = arrConnId,
            SizeBytes = sizeBytes,
            FlaggedAt = DateTime.UtcNow,
        };
        db.SweepItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    // ── Mock HTTP handler ─────────────────────────────────────────────────────

    internal sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpMethod Method, int Status, string Body)> _responses = new();
        public readonly List<(HttpMethod Method, string Path)> Calls = [];

        public void Enqueue(HttpMethod method, string path, int status, string body = "null")
            => _responses.Enqueue((method, status, body));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery));

            if (_responses.TryDequeue(out var resp))
            {
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)resp.Status)
                {
                    Content = new StringContent(resp.Body, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    // ── Fake IIntegrationClientFactory ────────────────────────────────────────

    private sealed class FakeClientFactory : IIntegrationClientFactory
    {
        private readonly RecordingHandler _handler;

        public FakeClientFactory(RecordingHandler handler) => _handler = handler;

        public Task<JellyfinClient?> CreateJellyfinClientAsync(int connectionId, CancellationToken ct)
            => Task.FromResult<JellyfinClient?>(null);

        public Task<RadarrClient?> CreateRadarrClientAsync(int connectionId, CancellationToken ct)
        {
            var http = new HttpClient(_handler) { BaseAddress = new Uri("http://radarr:7878") };
            var client = new RadarrClient(http, "http://radarr:7878", "test-key",
                NullLogger<RadarrClient>.Instance);
            return Task.FromResult<RadarrClient?>(client);
        }

        public Task<SonarrClient?> CreateSonarrClientAsync(int connectionId, CancellationToken ct)
            => Task.FromResult<SonarrClient?>(null);
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────

    private static string MovieListJson(params int[] tmdbIds)
    {
        var movies = tmdbIds.Select((tmdb, i) => SingleMovieObject(i + 1, tmdb)).ToList();
        return JsonSerializer.Serialize(movies);
    }

    private static string SingleMovieJson(int id, int tmdbId, bool monitored = true)
        => JsonSerializer.Serialize(SingleMovieObject(id, tmdbId, monitored));

    private static object SingleMovieObject(int id, int tmdbId, bool monitored = true) => new
    {
        id,
        title = $"Movie {tmdbId}",
        year = 2020,
        tmdbId,
        imdbId = (string?)null,
        monitored,
        hasFile = true,
        qualityProfileId = 1,
        tags = Array.Empty<int>(),
        path = $"/movies/movie-{tmdbId}",
        sizeOnDisk = 5_368_709_120L,
        added = "2022-01-01T00:00:00Z",
        status = "released",
    };
}
