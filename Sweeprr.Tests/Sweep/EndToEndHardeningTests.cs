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
using Sweeprr.API.Integrations.Bazarr;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Matching;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Models;
using Sweeprr.API.Services;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.Tests.Sweep;

public class EndToEndHardeningTests : IDisposable
{
    private readonly List<string> _dbPaths = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // ── G1: Transient Failure Protection ─────────────────────────────────────

    [Fact]
    public async Task Scan_WithTransientFailure_ExcludesItemFromDeletions()
    {
        var valueResolver = new ValueResolver();
        var evaluator = new RuleEvaluator(valueResolver);

        var group = new RuleGroup
        {
            MediaType = MediaType.Movie,
            Rules = [new Rule { Field = RuleField.FileSizeGb, Comparator = RuleComparator.GreaterThan, Value = "1", ValueType = RuleValueType.Number }]
        };

        // Seed an item with HasTransientFailure = true
        var items = new List<MediaContext>
        {
            new() { ItemId = "jf-01", Title = "Healthy Movie", MediaType = MediaType.Movie, FileSizeGb = 10m },
            new() { ItemId = "jf-02", Title = "Transient Error Movie", MediaType = MediaType.Movie, HasTransientFailure = true, TransientFailureReason = "Radarr timeout", FileSizeGb = 50m }
        };

        var results = await evaluator.EvaluateAsync(group, items);

        Assert.Equal(2, results.Count);
        // Healthy movie matches
        Assert.True(results[0].IsMatch);
        // Transient error movie gets excluded unconditionally
        Assert.False(results[1].IsMatch);
        Assert.True(results[1].WasExcluded);
        Assert.Contains("Radarr timeout", results[1].MatchedRuleSummary);
    }

    [Fact]
    public async Task Sweep_WithHttpClientTransientFailure_MarksFailedButBatchContinues()
    {
        var handler = new RecordingHandler();
        var (executor, db) = CreateExecutor(handler, globalDryRun: false);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-111", arrConnId: 1); // will fail transiently
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-222", arrConnId: 1); // will succeed

        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(111, 222));

        // Movie 1 -> GET fails transiently
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/1", 503, "\"Service Unavailable\"");

        // Movie 2 -> GET succeeds, then PUT/DELETE succeed
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/2", 200, SingleMovieJson(2, 222));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/2", 200, SingleMovieJson(2, 222, monitored: false));
        handler.Enqueue(HttpMethod.Delete, "/api/v3/movie/2", 200, "null");

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(1, result.ItemsSwept);
        Assert.Equal(1, result.ItemsFailed);

        var first = await db.SweepItems.FirstAsync(s => s.TmdbId == "111");
        var second = await db.SweepItems.FirstAsync(s => s.TmdbId == "222");

        Assert.Equal(SweepItemStatus.Failed, first.Status);
        Assert.Contains("transient", first.SkippedReason);
        Assert.Equal(SweepItemStatus.Swept, second.Status);
    }

    // ── G2: Anti-Wipe Protection (Empty Group) ───────────────────────────────

    [Fact]
    public async Task RuleEvaluator_EmptyRuleGroup_MatchesNothing()
    {
        var valueResolver = new ValueResolver();
        var evaluator = new RuleEvaluator(valueResolver);

        var group = new RuleGroup { MediaType = MediaType.Movie, Rules = [] }; // Empty
        var items = new List<MediaContext>
        {
            new() { ItemId = "jf-01", Title = "Movie One", MediaType = MediaType.Movie, FileSizeGb = 10m }
        };

        var results = await evaluator.EvaluateAsync(group, items);

        Assert.Single(results);
        Assert.False(results[0].IsMatch);
    }

    // ── G3: Unmonitor Before Delete Ordering ─────────────────────────────────

    [Fact]
    public async Task Sweep_DeleteAndUnmonitor_EnforcesStrictOrderAndAbortsOnFailure()
    {
        var handler = new RecordingHandler();
        var (executor, db) = CreateExecutor(handler, globalDryRun: false);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-111", arrConnId: 1);

        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(111));
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/1", 200, SingleMovieJson(1, 111));
        
        // Mock a failed PUT (unmonitor fails)
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/1", 500, "\"Internal Server Error\"");

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(0, result.ItemsSwept);
        Assert.Equal(1, result.ItemsFailed);

        // Verify DELETE was never called
        Assert.DoesNotContain(handler.Calls, c => c.Method == HttpMethod.Delete);

        var item = await db.SweepItems.FirstAsync(s => s.TmdbId == "111");
        Assert.Equal(SweepItemStatus.Failed, item.Status);
    }

    // ── G4: Failsafe Gates ───────────────────────────────────────────────────

    [Fact]
    public async Task Sweep_ItemLimitCap_HaltsAndSkipsRemainder()
    {
        var handler = new RecordingHandler();
        // Limit = 1 max items
        var (executor, db) = CreateExecutor(handler, globalDryRun: false, maxItemsPerRun: 1);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-111", arrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-222", arrConnId: 1);

        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(111, 222));
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/1", 200, SingleMovieJson(1, 111));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/1", 200, SingleMovieJson(1, 111, monitored: false));
        handler.Enqueue(HttpMethod.Delete, "/api/v3/movie/1", 200, "null");

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(1, result.ItemsSwept);
        Assert.Equal(0, result.ItemsFailed);
        Assert.Equal(1, result.ItemsSkippedByFailsafe);

        var skippedItem = await db.SweepItems.FirstAsync(s => s.TmdbId == "222");
        Assert.Equal(SweepItemStatus.Approved, skippedItem.Status); // status remains Approved
        Assert.Contains("Failsafe", skippedItem.SkippedReason);
    }

    [Fact]
    public async Task Sweep_GbLimitCap_HaltsWhenCapExceeded()
    {
        var handler = new RecordingHandler();
        // Limit = 1.5 GB. Each item is 1 GB.
        var (executor, db) = CreateExecutor(handler, globalDryRun: false, maxItemsPerRun: 10, maxGbPerRun: 1.5);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-111", arrConnId: 1, sizeBytes: 1_073_741_824L); // 1 GB
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-222", arrConnId: 1, sizeBytes: 1_073_741_824L); // 1 GB

        handler.Enqueue(HttpMethod.Get, "/api/v3/movie", 200, MovieListJson(111, 222));
        handler.Enqueue(HttpMethod.Get, "/api/v3/movie/1", 200, SingleMovieJson(1, 111));
        handler.Enqueue(HttpMethod.Put, "/api/v3/movie/1", 200, SingleMovieJson(1, 111, monitored: false));
        handler.Enqueue(HttpMethod.Delete, "/api/v3/movie/1", 200, "null");

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(1, result.ItemsSwept);
        Assert.Equal(1, result.ItemsSkippedByFailsafe);

        var skippedItem = await db.SweepItems.FirstAsync(s => s.TmdbId == "222");
        Assert.Equal(SweepItemStatus.Approved, skippedItem.Status);
        Assert.Contains("size limit", skippedItem.SkippedReason);
    }

    [Fact]
    public async Task Sweep_LibraryPercentCap_AbortsBeforeExecution()
    {
        var handler = new RecordingHandler();
        // TotalApproved = 3, TotalQueueItems = 4 (from DB). Cap = 50% (0.50) → 3/4 = 75% > 50% → Halt!
        var (executor, db) = CreateExecutor(handler, globalDryRun: false, libraryPercentCap: 0.50);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-111", arrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-222", arrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-333", arrConnId: 1);

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(0, result.ItemsSwept);
        Assert.Equal(3, result.ItemsSkippedByFailsafe);

        // Verify no HTTP calls made to Radarr/Sonarr at all
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Sweep_OverBroadMatchCap_AbortsSpecificInstanceGroup()
    {
        var handler = new RecordingHandler();
        // TotalApproved = 4. Group has 4 (100%). OverBroadMatchPct = 80% (0.80) → Halt group!
        var (executor, db) = CreateExecutor(handler, globalDryRun: false, overBroadMatchPct: 0.80);

        var group = await SeedGroupAsync(db, SweepAction.DeleteAndUnmonitor, radarrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-111", arrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-222", arrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-333", arrConnId: 1);
        await SeedApprovedMovieItemAsync(db, group.Id, "tmdb-444", arrConnId: 1);

        var result = await executor.ExecuteAsync(new ExecuteSweepRequest());

        Assert.Equal(0, result.ItemsSwept);
        Assert.Equal(4, result.ItemsSkippedByFailsafe);

        // Verify no calls
        Assert.Empty(handler.Calls);
    }

    // ── Test infrastructure ──────────────────────────────────────────────────

    private (SweepExecutor Executor, SweeprrDbContext Db) CreateExecutor(
        RecordingHandler handler,
        bool globalDryRun = true,
        int maxItemsPerRun = 20,
        double maxGbPerRun = 50.0,
        double? libraryPercentCap = null,
        double? overBroadMatchPct = null,
        bool seedConnection = true)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_hardening_{Guid.NewGuid()}.db");
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
                LibraryPercentCap = libraryPercentCap,
                OverBroadMatchPct = overBroadMatchPct,
                JwtSecret = "e2e-hardening-test-secret-key-that-is-long",
            });

            if (seedConnection)
            {
                db.ServerConnections.Add(new ServerConnection
                {
                    Id = 1,
                    Type = ConnectionType.Radarr,
                    Name = "Radarr",
                    BaseUrl = "http://radarr:7878",
                    ApiKeyEncrypted = "test-key-encrypted",
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
            new FakeOverlayRenderingService(),
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
            Name = "E2E Hardening Group",
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
        long? sizeBytes = 5_368_709_120L)
    {
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

        public Task<BazarrClient?> CreateBazarrClientAsync(CancellationToken ct = default)
            => Task.FromResult<BazarrClient?>(null);
    }

    // ── Fake IOverlayRenderingService ─────────────────────────────────────────

    private sealed class FakeOverlayRenderingService : IOverlayRenderingService
    {
        public Task ApplyOverlayAsync(SweepItem item, string labelText, CancellationToken ct) => Task.CompletedTask;
        public Task RestoreOriginalAsync(SweepItem item, CancellationToken ct) => Task.CompletedTask;
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
