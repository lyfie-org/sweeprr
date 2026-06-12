using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Bazarr;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Models;
using Sweeprr.API.Services;
using Xunit;

// ReSharper disable AccessToDisposedClosure

namespace Sweeprr.Tests.Services;

public class PlaybackReportingServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly SweeprrDbContext _db;
    private readonly RoutingHandler _handler;
    private readonly int _connectionId;

    public PlaybackReportingServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_pbreport_{Guid.NewGuid()}.db");

        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(opt => opt.UseSqlite($"Data Source={_dbPath}"));

        _handler = new RoutingHandler();
        var httpClient = new HttpClient(_handler);
        var jellyfinClient = new JellyfinClient(httpClient, "http://localhost:8096", "fake-key", NullLogger<JellyfinClient>.Instance);

        services.AddSingleton<IIntegrationClientFactory>(new FakeIntegrationClientFactory(jellyfinClient));
        services.AddSingleton<ILogger<PlaybackReportingService>>(NullLogger<PlaybackReportingService>.Instance);
        services.AddScoped<IPlaybackReportingService, PlaybackReportingService>();

        var keysDir = Path.Combine(Path.GetTempPath(), $"sweeprr_dp_{Guid.NewGuid()}");
        Directory.CreateDirectory(keysDir);
        var protector = new SecretProtector(DataProtectionProvider.Create(new DirectoryInfo(keysDir)));
        services.AddSingleton<ISecretProtector>(protector);
        services.AddScoped<IConnectionService, ConnectionService>();

        _serviceProvider = services.BuildServiceProvider();

        _db = _serviceProvider.GetRequiredService<SweeprrDbContext>();
        _db.Database.Migrate();

        var conn = new ServerConnection
        {
            Type = ConnectionType.Jellyfin,
            Name = "Jellyfin Home",
            BaseUrl = "http://localhost:8096",
            ApiKeyEncrypted = "fake-key",
            IsEnabled = true
        };
        _db.ServerConnections.Add(conn);
        _db.SaveChanges();
        _connectionId = conn.Id;
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }

    [Fact]
    public async Task PluginActive_Persists_Status_And_Backfills_New_Row()
    {
        _handler.DetectionStatusCode = 200;
        _handler.BackfillResponseJson = """
            {
                "colums": ["ItemId", "UserId", "PlayCount", "LastPlayed"],
                "results": [
                    ["item-1", "user-1", 5, "2024-01-15 10:30:00"]
                ]
            }
            """;
        _handler.UsersResponseJson = """[{"Id":"user-1","Name":"Alice"}]""";

        var service = _serviceProvider.GetRequiredService<IPlaybackReportingService>();
        await service.RunAsync(CancellationToken.None);

        var connections = _serviceProvider.GetRequiredService<IConnectionService>();
        var conn = await connections.GetByIdAsync(_connectionId);
        Assert.True(conn!.PlaybackReportingPluginActive);

        var record = await _db.PlaybackActivities.AsNoTracking()
            .FirstOrDefaultAsync(p => p.MediaServerItemId == "item-1" && p.UserId == "user-1");

        Assert.NotNull(record);
        Assert.Equal("Alice", record!.Username);
        Assert.Equal(5, record.PlayCount);
        Assert.True(record.IsFinished);
        Assert.Equal(100.0, record.ProgressPercent);
        Assert.Equal(0, record.PlaybackPositionTicks);
        Assert.Equal(2024, record.LastWatched.Year);
    }

    [Fact]
    public async Task PluginNotDetected_Persists_False_And_Skips_Backfill()
    {
        _handler.DetectionStatusCode = 404;

        var service = _serviceProvider.GetRequiredService<IPlaybackReportingService>();
        await service.RunAsync(CancellationToken.None);

        var connections = _serviceProvider.GetRequiredService<IConnectionService>();
        var conn = await connections.GetByIdAsync(_connectionId);
        Assert.False(conn!.PlaybackReportingPluginActive);

        Assert.Equal(0, _handler.BackfillCallCount);
    }

    [Fact]
    public async Task ExistingRow_Merges_PlayCount_And_LastWatched_Without_Touching_Live_Fields()
    {
        _db.PlaybackActivities.Add(new PlaybackActivity
        {
            MediaServerItemId = "item-1",
            UserId = "user-1",
            Username = "Alice",
            PlayCount = 2,
            LastWatched = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsFinished = false,
            ProgressPercent = 40.0,
            PlaybackPositionTicks = 500,
            UpdatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _handler.DetectionStatusCode = 200;
        _handler.BackfillResponseJson = """
            {
                "colums": ["ItemId", "UserId", "PlayCount", "LastPlayed"],
                "results": [
                    ["item-1", "user-1", 5, "2024-06-01 00:00:00"]
                ]
            }
            """;
        _handler.UsersResponseJson = """[{"Id":"user-1","Name":"Alice"}]""";

        var service = _serviceProvider.GetRequiredService<IPlaybackReportingService>();
        await service.RunAsync(CancellationToken.None);

        _db.ChangeTracker.Clear();
        var record = await _db.PlaybackActivities.AsNoTracking()
            .FirstAsync(p => p.MediaServerItemId == "item-1" && p.UserId == "user-1");

        Assert.Equal(5, record.PlayCount);          // max(2, 5)
        Assert.Equal(2024, record.LastWatched.Year); // newer LastPlayed wins
        Assert.False(record.IsFinished);             // untouched
        Assert.Equal(40.0, record.ProgressPercent);  // untouched
        Assert.Equal(500, record.PlaybackPositionTicks); // untouched
    }

    [Fact]
    public async Task NewRow_With_Unknown_UserId_Falls_Back_To_Unknown_Username()
    {
        _handler.DetectionStatusCode = 200;
        _handler.BackfillResponseJson = """
            {
                "colums": ["ItemId", "UserId", "PlayCount", "LastPlayed"],
                "results": [
                    ["item-2", "user-ghost", 1, "2024-03-01 00:00:00"]
                ]
            }
            """;
        _handler.UsersResponseJson = """[{"Id":"user-1","Name":"Alice"}]"""; // user-ghost not present

        var service = _serviceProvider.GetRequiredService<IPlaybackReportingService>();
        await service.RunAsync(CancellationToken.None);

        var record = await _db.PlaybackActivities.AsNoTracking()
            .FirstOrDefaultAsync(p => p.MediaServerItemId == "item-2" && p.UserId == "user-ghost");

        Assert.NotNull(record);
        Assert.Equal("Unknown", record!.Username);
    }

    [Fact]
    public async Task TransientDetectionFailure_Leaves_Status_Unchanged_And_Skips_Backfill()
    {
        _handler.DetectionStatusCode = 500;

        var service = _serviceProvider.GetRequiredService<IPlaybackReportingService>();
        await service.RunAsync(CancellationToken.None);

        var connections = _serviceProvider.GetRequiredService<IConnectionService>();
        var conn = await connections.GetByIdAsync(_connectionId);
        Assert.Null(conn!.PlaybackReportingPluginActive);

        Assert.Equal(0, _handler.BackfillCallCount);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Routes requests by path: detection (PlaybackReporting probe), backfill
    /// (submit_custom_query), and user list — each independently configurable per test.
    /// </summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public int DetectionStatusCode { get; set; } = 200;
        public string BackfillResponseJson { get; set; } =
            """{"colums":["ItemId","UserId","PlayCount","LastPlayed"],"results":[]}""";
        public string UsersResponseJson { get; set; } = "[]";

        public int BackfillCallCount { get; private set; }
        public int DetectionCallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (string.Equals(path, "/PlaybackReporting/Report/Hourly/User", StringComparison.OrdinalIgnoreCase))
            {
                DetectionCallCount++;
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)DetectionStatusCode));
            }

            if (string.Equals(path, "/user_usage_stats/submit_custom_query", StringComparison.OrdinalIgnoreCase))
            {
                BackfillCallCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BackfillResponseJson, Encoding.UTF8, "application/json")
                });
            }

            if (string.Equals(path, "/Users", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(UsersResponseJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FakeIntegrationClientFactory : IIntegrationClientFactory
    {
        private readonly JellyfinClient _jellyfinClient;

        public FakeIntegrationClientFactory(JellyfinClient jellyfinClient)
        {
            _jellyfinClient = jellyfinClient;
        }

        public Task<JellyfinClient?> CreateJellyfinClientAsync(int connectionId, CancellationToken ct = default)
            => Task.FromResult<JellyfinClient?>(_jellyfinClient);

        public Task<RadarrClient?> CreateRadarrClientAsync(int connectionId, CancellationToken ct = default)
            => Task.FromResult<RadarrClient?>(null);

        public Task<SonarrClient?> CreateSonarrClientAsync(int connectionId, CancellationToken ct = default)
            => Task.FromResult<SonarrClient?>(null);

        public Task<BazarrClient?> CreateBazarrClientAsync(CancellationToken ct = default)
            => Task.FromResult<BazarrClient?>(null);
    }
}
