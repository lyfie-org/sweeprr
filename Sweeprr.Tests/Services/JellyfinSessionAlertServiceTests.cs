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
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Bazarr;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Models;
using Sweeprr.API.Services;
using Xunit;

// ReSharper disable AccessToDisposedClosure

namespace Sweeprr.Tests.Services;

public class JellyfinSessionAlertServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly SweeprrDbContext _db;
    private readonly RoutingHandler _handler;
    private readonly int _connectionId;
    private readonly JellyfinSessionAlertService _service;

    public JellyfinSessionAlertServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_sessionalert_{Guid.NewGuid()}.db");

        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(opt => opt.UseSqlite($"Data Source={_dbPath}"));

        _handler = new RoutingHandler();
        var httpClient = new HttpClient(_handler);
        var jellyfinClient = new JellyfinClient(httpClient, "http://localhost:8096", "fake-key", NullLogger<JellyfinClient>.Instance);

        services.AddSingleton<IIntegrationClientFactory>(new FakeIntegrationClientFactory(jellyfinClient));

        var keysDir = Path.Combine(Path.GetTempPath(), $"sweeprr_dp_{Guid.NewGuid()}");
        Directory.CreateDirectory(keysDir);
        var protector = new SecretProtector(DataProtectionProvider.Create(new DirectoryInfo(keysDir)));
        services.AddSingleton<ISecretProtector>(protector);
        services.AddScoped<IConnectionService, ConnectionService>();

        _serviceProvider = services.BuildServiceProvider();

        _db = _serviceProvider.GetRequiredService<SweeprrDbContext>();
        _db.Database.Migrate();

        _db.GlobalSettings.Add(new GlobalSettings { Id = 1 });

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

        _service = new JellyfinSessionAlertService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<JellyfinSessionAlertService>.Instance);
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

    private int SeedSweepItem(string mediaServerItemId, string title, SweepItemStatus status)
    {
        var group = new RuleGroup { Name = "G", MediaType = MediaType.Movie, Action = SweepAction.DeleteAndUnmonitor };
        _db.RuleGroups.Add(group);
        _db.SaveChanges();

        var item = new SweepItem
        {
            RuleGroupId = group.Id,
            MediaServerItemId = mediaServerItemId,
            Title = title,
            MediaType = MediaType.Movie,
            Status = status
        };
        _db.SweepItems.Add(item);
        _db.SaveChanges();
        return item.Id;
    }

    // ── ProcessSessionsUpdateAsync ───────────────────────────────────────────

    [Fact]
    public async Task ProcessSessionsUpdateAsync_Sends_Alert_For_Pending_Item_In_Session()
    {
        SeedSweepItem("item-1", "The Matrix", SweepItemStatus.Pending);

        var sessions = new[] { new JellyfinSession("session-1", "user-1", "item-1") };
        await _service.ProcessSessionsUpdateAsync(_connectionId, sessions);

        var sent = Assert.Single(_handler.SentMessages);
        Assert.Contains("/Sessions/session-1/Message", sent.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The Matrix", sent.Body ?? string.Empty);
    }

    [Fact]
    public async Task ProcessSessionsUpdateAsync_Does_Not_Resend_Within_Cooldown()
    {
        SeedSweepItem("item-1", "The Matrix", SweepItemStatus.Pending);

        var sessions = new[] { new JellyfinSession("session-1", "user-1", "item-1") };
        await _service.ProcessSessionsUpdateAsync(_connectionId, sessions);
        await _service.ProcessSessionsUpdateAsync(_connectionId, sessions);

        Assert.Single(_handler.SentMessages);
    }

    [Fact]
    public async Task ProcessSessionsUpdateAsync_Disabled_Setting_Sends_Nothing()
    {
        var settings = _db.GlobalSettings.First(g => g.Id == 1);
        settings.JellyfinSessionAlertsEnabled = false;
        _db.SaveChanges();

        SeedSweepItem("item-1", "The Matrix", SweepItemStatus.Pending);
        var sessions = new[] { new JellyfinSession("session-1", "user-1", "item-1") };
        await _service.ProcessSessionsUpdateAsync(_connectionId, sessions);

        Assert.Empty(_handler.SentMessages);
    }

    [Fact]
    public async Task ProcessSessionsUpdateAsync_No_Matching_SweepItem_Sends_Nothing()
    {
        var sessions = new[] { new JellyfinSession("session-1", "user-1", "item-unknown") };
        await _service.ProcessSessionsUpdateAsync(_connectionId, sessions);

        Assert.Empty(_handler.SentMessages);
    }

    [Theory]
    [InlineData(SweepItemStatus.Ignored)]
    [InlineData(SweepItemStatus.Swept)]
    public async Task ProcessSessionsUpdateAsync_Ignores_NonQueued_Statuses(SweepItemStatus status)
    {
        SeedSweepItem("item-1", "The Matrix", status);

        var sessions = new[] { new JellyfinSession("session-1", "user-1", "item-1") };
        await _service.ProcessSessionsUpdateAsync(_connectionId, sessions);

        Assert.Empty(_handler.SentMessages);
    }

    // ── TryRegisterAlert (cooldown) ──────────────────────────────────────────

    [Fact]
    public void TryRegisterAlert_Allows_Resend_After_Cooldown_Elapses()
    {
        var now = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromMinutes(15);

        Assert.True(_service.TryRegisterAlert("session-1", "item-1", now, cooldown));
        Assert.False(_service.TryRegisterAlert("session-1", "item-1", now + TimeSpan.FromMinutes(5), cooldown));
        Assert.True(_service.TryRegisterAlert("session-1", "item-1", now + cooldown + TimeSpan.FromSeconds(1), cooldown));
    }

    // ── BroadcastPreSweepWarningAsync ────────────────────────────────────────

    [Fact]
    public async Task BroadcastPreSweepWarningAsync_Sends_To_All_Sessions_With_Users()
    {
        _handler.SessionsResponseJson = """
            [
                {"Id": "session-1", "UserId": "user-1"},
                {"Id": "session-2", "UserId": "user-2"}
            ]
            """;

        await _service.BroadcastPreSweepWarningAsync();

        Assert.Equal(2, _handler.SentMessages.Count);
        Assert.All(_handler.SentMessages, m =>
            Assert.Contains("Sweeprr cleanup will begin in 10 minutes. Some content may be removed.", m.Body ?? string.Empty));
    }

    [Fact]
    public async Task BroadcastPreSweepWarningAsync_Skips_Sessions_Without_User()
    {
        _handler.SessionsResponseJson = """
            [
                {"Id": "session-1", "UserId": "user-1"},
                {"Id": "session-2"}
            ]
            """;

        await _service.BroadcastPreSweepWarningAsync();

        var sent = Assert.Single(_handler.SentMessages);
        Assert.Contains("/Sessions/session-1/Message", sent.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BroadcastPreSweepWarningAsync_Disabled_Setting_Sends_Nothing()
    {
        var settings = _db.GlobalSettings.First(g => g.Id == 1);
        settings.PreSweepBroadcastEnabled = false;
        _db.SaveChanges();

        _handler.SessionsResponseJson = """[{"Id": "session-1", "UserId": "user-1"}]""";

        await _service.BroadcastPreSweepWarningAsync();

        Assert.Empty(_handler.SentMessages);
    }

    [Fact]
    public async Task BroadcastPreSweepWarningAsync_NoOp_When_No_Enabled_Jellyfin_Connection()
    {
        var conn = _db.ServerConnections.First();
        conn.IsEnabled = false;
        _db.SaveChanges();

        _handler.SessionsResponseJson = """[{"Id": "session-1", "UserId": "user-1"}]""";

        await _service.BroadcastPreSweepWarningAsync();

        Assert.Empty(_handler.SentMessages);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Routes <c>GET /Sessions</c> to a configurable JSON list and records every
    /// <c>POST .../Message</c> call (path + body) for assertion.
    /// </summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public string SessionsResponseJson { get; set; } = "[]";
        public List<(string Path, string? Body)> SentMessages { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Get &&
                string.Equals(path, "/Sessions", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SessionsResponseJson, Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Post &&
                path.Contains("/Message", StringComparison.OrdinalIgnoreCase))
            {
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
                SentMessages.Add((path, body));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
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
