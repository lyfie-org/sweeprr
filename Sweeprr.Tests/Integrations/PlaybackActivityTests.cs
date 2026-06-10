using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Bazarr;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Integrations.Jellyfin.WebSocket;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Models;
using Xunit;

namespace Sweeprr.Tests.Integrations;

public class PlaybackActivityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SweeprrDbContext _db;
    private readonly ServiceProvider _serviceProvider;
    private readonly FakeHttpMessageHandler _httpHandler;

    public PlaybackActivityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_playback_{Guid.NewGuid()}.db");
        
        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(opt => opt.UseSqlite($"Data Source={_dbPath}"));
        services.AddSingleton<IPlaystateCache, PlaystateCache>();
        services.AddSingleton<IPlaybackActivityWriter, PlaybackActivityWriter>();
        
        _httpHandler = new FakeHttpMessageHandler();
        var httpClient = new HttpClient(_httpHandler);
        var jellyfinClient = new JellyfinClient(httpClient, "http://localhost:8096", "fake-key", NullLogger<JellyfinClient>.Instance);

        var clientFactory = new FakeIntegrationClientFactory(jellyfinClient);
        services.AddSingleton<IIntegrationClientFactory>(clientFactory);
        services.AddSingleton<ILogger<PlaybackActivityWriter>>(NullLogger<PlaybackActivityWriter>.Instance);

        _serviceProvider = services.BuildServiceProvider();

        _db = _serviceProvider.GetRequiredService<SweeprrDbContext>();
        _db.Database.Migrate();

        // Seed Jellyfin connection so client factory works
        _db.ServerConnections.Add(new ServerConnection
        {
            Type = ConnectionType.Jellyfin,
            Name = "Jellyfin Home",
            BaseUrl = "http://localhost:8096",
            ApiKeyEncrypted = "fake-key",
            IsEnabled = true
        });
        _db.SaveChanges();
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
    public async Task Enqueue_Writes_To_Database_And_Forces_Flush()
    {
        var writer = _serviceProvider.GetRequiredService<IPlaybackActivityWriter>();
        
        // Mock runtime ticks = 1000
        _httpHandler.ResponseJson = """
        {
            "Id": "item-1",
            "Name": "Interstellar",
            "Type": "Movie",
            "RunTimeTicks": 1000
        }
        """;

        var data = new JellyfinUserData(Played: false, LastPlayedDate: DateTimeOffset.UtcNow, PlayCount: 1, PlaybackPositionTicks: 250);
        
        writer.Enqueue("item-1", "user-A", data, "Alice");
        
        // Allow background tasks to run briefly, then flush
        await Task.Delay(200);
        await writer.ForceFlushAsync(CancellationToken.None);

        var record = await _db.PlaybackActivities.FirstOrDefaultAsync(p => p.MediaServerItemId == "item-1" && p.UserId == "user-A");
        
        Assert.NotNull(record);
        Assert.Equal("Alice", record!.Username);
        Assert.Equal(1, record.PlayCount);
        Assert.False(record.IsFinished);
        Assert.Equal(250, record.PlaybackPositionTicks);
        Assert.Equal(25.0, record.ProgressPercent); // 250 / 1000 * 100
    }

    [Fact]
    public async Task Enqueue_Debounces_Writes_Within_Ten_Percent_Boundary()
    {
        var writer = _serviceProvider.GetRequiredService<IPlaybackActivityWriter>();
        
        _httpHandler.ResponseJson = """
        {
            "Id": "item-1",
            "Name": "Interstellar",
            "Type": "Movie",
            "RunTimeTicks": 1000
        }
        """;

        // First write (progress 2%) -> should write
        var data1 = new JellyfinUserData(Played: false, LastPlayedDate: DateTimeOffset.UtcNow, PlayCount: 1, PlaybackPositionTicks: 20);
        writer.Enqueue("item-1", "user-A", data1, "Alice");
        
        await Task.Delay(200);
        await writer.ForceFlushAsync(CancellationToken.None);

        var count1 = await _db.PlaybackActivities.CountAsync();
        Assert.Equal(1, count1);
        
        var record1 = await _db.PlaybackActivities.FirstAsync();
        Assert.Equal(2.0, record1.ProgressPercent);

        // Second write (progress 5%) -> same 10% boundary (0-9%) -> should NOT write
        var data2 = new JellyfinUserData(Played: false, LastPlayedDate: DateTimeOffset.UtcNow, PlayCount: 1, PlaybackPositionTicks: 50);
        writer.Enqueue("item-1", "user-A", data2, "Alice");
        
        await Task.Delay(100);
        await writer.ForceFlushAsync(CancellationToken.None);

        _db.ChangeTracker.Clear();
        var record2 = await _db.PlaybackActivities.FirstAsync();
        Assert.Equal(2.0, record2.ProgressPercent); // Still 2% in DB

        // Third write (progress 12%) -> crosses 10% boundary -> should write
        var data3 = new JellyfinUserData(Played: false, LastPlayedDate: DateTimeOffset.UtcNow, PlayCount: 1, PlaybackPositionTicks: 120);
        writer.Enqueue("item-1", "user-A", data3, "Alice");
        
        await Task.Delay(100);
        await writer.ForceFlushAsync(CancellationToken.None);

        _db.ChangeTracker.Clear();
        var record3 = await _db.PlaybackActivities.FirstAsync();
        Assert.Equal(12.0, record3.ProgressPercent); // Updated to 12% in DB
    }

    [Fact]
    public async Task PruneOldActivities_Removes_Old_Records()
    {
        var writer = _serviceProvider.GetRequiredService<IPlaybackActivityWriter>();

        _db.PlaybackActivities.Add(new PlaybackActivity
        {
            MediaServerItemId = "item-old",
            UserId = "user-A",
            Username = "Alice",
            LastWatched = DateTime.UtcNow.AddDays(-370),
            IsFinished = true,
            ProgressPercent = 100.0,
            UpdatedAt = DateTime.UtcNow.AddDays(-370),
        });

        _db.PlaybackActivities.Add(new PlaybackActivity
        {
            MediaServerItemId = "item-new",
            UserId = "user-A",
            Username = "Alice",
            LastWatched = DateTime.UtcNow.AddDays(-10),
            IsFinished = true,
            ProgressPercent = 100.0,
            UpdatedAt = DateTime.UtcNow.AddDays(-10),
        });

        await _db.SaveChangesAsync();

        await writer.PruneOldActivitiesAsync(365, CancellationToken.None);

        var remaining = await _db.PlaybackActivities.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("item-new", remaining[0].MediaServerItemId);
    }

    [Fact]
    public async Task WebSocketService_Loads_Initial_State_On_Startup()
    {
        var cache = _serviceProvider.GetRequiredService<IPlaystateCache>();
        
        _db.PlaybackActivities.Add(new PlaybackActivity
        {
            MediaServerItemId = "item-saved",
            UserId = "user-B",
            Username = "Bob",
            LastWatched = DateTime.UtcNow,
            PlayCount = 4,
            IsFinished = true,
            PlaybackPositionTicks = 999
        });
        await _db.SaveChangesAsync();

        var wsService = new JellyfinWebSocketService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            cache,
            _serviceProvider.GetRequiredService<IPlaybackActivityWriter>(),
            NullLogger<JellyfinWebSocketService>.Instance);

        // Load initial state is internal/private, but gets called in ExecuteAsync.
        // We can invoke it indirectly by letting ExecuteAsync run briefly, or by calling the helper method via reflection.
        var method = typeof(JellyfinWebSocketService).GetMethod("LoadInitialStateAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        
        var task = (Task)method!.Invoke(wsService, new object[] { CancellationToken.None })!;
        await task;

        var cached = cache.Get("item-saved", "user-B");
        Assert.NotNull(cached);
        Assert.True(cached!.Played);
        Assert.Equal(4, cached.PlayCount);
        Assert.Equal(999, cached.PlaybackPositionTicks);
    }

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        public string ResponseJson { get; set; } = "{}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private class FakeIntegrationClientFactory : IIntegrationClientFactory
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
