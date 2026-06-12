using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Background;
using Sweeprr.API.Controllers;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Public;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Bazarr;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Controllers;

public class PublicControllerTests : IDisposable
{
    private readonly List<string> _dbPaths = [];
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Dispose();

        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // ── GetMediaStatus ────────────────────────────────────────────────────

    [Fact]
    public async Task GetMediaStatus_ReturnsNotQueued_WhenItemNotInSweepQueue()
    {
        var (controller, _, _) = CreateController();

        var result = await controller.GetMediaStatus("unknown-item", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var status = Assert.IsType<MediaStatusResponse>(ok.Value);
        Assert.False(status.IsQueued);
        Assert.Null(status.DaysRemaining);
        Assert.Null(status.Title);
        Assert.Null(status.PosterUrl);
    }

    [Fact]
    public async Task GetMediaStatus_ReturnsQueuedItem_WithTitleAndPosterUrl()
    {
        var (controller, db, _) = CreateController();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Some Movie");
        await SeedJellyfinConnectionAsync(db, "http://jellyfin.local");

        var result = await controller.GetMediaStatus("item-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var status = Assert.IsType<MediaStatusResponse>(ok.Value);
        Assert.True(status.IsQueued);
        Assert.Equal("Some Movie", status.Title);
        Assert.Equal("http://jellyfin.local/Items/item-1/Images/Primary", status.PosterUrl);
    }

    [Fact]
    public async Task GetMediaStatus_ComputesDaysRemaining_FromSchedulerNextRun()
    {
        var (controller, db, scheduler) = CreateController();
        await SeedGlobalSettingsAsync(db);
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Some Movie");

        await scheduler.ForceReloadAsync();

        var result = await controller.GetMediaStatus("item-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var status = Assert.IsType<MediaStatusResponse>(ok.Value);
        Assert.True(status.IsQueued);
        Assert.NotNull(status.DaysRemaining);
        Assert.True(status.DaysRemaining >= 0);
    }

    // ── Extend ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Extend_Success_RemovesItemFromQueue_AndCreatesExclusion()
    {
        var (controller, db, _) = CreateController("alice");
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Some Movie");

        var result = await controller.Extend(new ExtendRequest("item-1", 14), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ExtendResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.NotNull(response.NewExpiresAt);
        Assert.InRange(response.NewExpiresAt!.Value, DateTime.UtcNow.AddDays(13.9), DateTime.UtcNow.AddDays(14.1));

        Assert.False(await db.SweepItems.AnyAsync(s => s.MediaServerItemId == "item-1"));

        var exclusion = await db.Exclusions.AsNoTracking().FirstAsync(e => e.MediaServerItemId == "item-1");
        Assert.Equal("alice", exclusion.CreatedBy);
        Assert.Null(exclusion.RuleGroupId);
        Assert.NotNull(exclusion.ExpiresAt);
    }

    [Fact]
    public async Task Extend_ReturnsNotFound_WhenItemNotInQueue()
    {
        var (controller, _, _) = CreateController("alice");

        var result = await controller.Extend(new ExtendRequest("unknown-item", 14), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var response = Assert.IsType<ExtendResponse>(notFound.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task Extend_ReturnsTooManyRequests_WhenAlreadyExtendedWithinSevenDays()
    {
        var (controller, db, _) = CreateController("alice");
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Some Movie");

        db.Exclusions.Add(new Exclusion
        {
            MediaServerItemId = "item-1",
            CreatedBy = "alice",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var result = await controller.Extend(new ExtendRequest("item-1", 14), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objectResult.StatusCode);
        var response = Assert.IsType<ExtendResponse>(objectResult.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task Extend_ClampsRequestedDays_AboveMaximum()
    {
        var (controller, db, _) = CreateController("alice");
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Some Movie");

        var result = await controller.Extend(new ExtendRequest("item-1", 9999), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ExtendResponse>(ok.Value);
        Assert.NotNull(response.NewExpiresAt);
        Assert.InRange(response.NewExpiresAt!.Value, DateTime.UtcNow.AddDays(13.9), DateTime.UtcNow.AddDays(14.1));
    }

    // ── AuthenticateJellyfin ─────────────────────────────────────────────────

    [Fact]
    public async Task AuthenticateJellyfin_ReturnsServiceUnavailable_WhenNoJellyfinConnectionConfigured()
    {
        var (controller, _, _) = CreateController();

        var result = await controller.AuthenticateJellyfin(new JellyfinAuthRequest("alice", "pw"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private (PublicController Controller, SweeprrDbContext Db, SchedulerHostedService Scheduler) CreateController(string? username = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_public_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);
        var connectionString = $"Data Source={dbPath}";

        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(o => o.UseSqlite(connectionString));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var db = new SweeprrDbContext(new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite(connectionString)
            .Options);
        db.Database.Migrate();

        var channel = Channel.CreateUnbounded<byte>();
        var notifications = new NotificationService(
            Channel.CreateUnbounded<NotificationDispatchRequest>(),
            NullLogger<NotificationService>.Instance);
        var sweepQueue = new SweepQueueService(db, channel, new FakeOverlayRenderingService(), notifications);

        var scheduler = new SchedulerHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeScanPipeline(),
            new FakeSessionAlertService(),
            NullLogger<SchedulerHostedService>.Instance);

        var keyProvider = new JwtKeyProvider(provider.GetRequiredService<IServiceScopeFactory>());

        var identity = username is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.UniqueName, username)], "Test");

        var controller = new PublicController(db, new FakeIntegrationClientFactory(), sweepQueue, keyProvider, scheduler)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };

        return (controller, db, scheduler);
    }

    private sealed class FakeOverlayRenderingService : IOverlayRenderingService
    {
        public Task ApplyOverlayAsync(SweepItem item, string labelText, CancellationToken ct) => Task.CompletedTask;
        public Task RestoreOriginalAsync(SweepItem item, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeScanPipeline : IScanPipeline
    {
        public Task<ScanResult> ExecuteAsync(int ruleGroupId, CancellationToken ct = default)
            => Task.FromResult(new ScanResult(ruleGroupId, "Test Group", 0, TimeSpan.Zero));
    }

    private sealed class FakeSessionAlertService : IJellyfinSessionAlertService
    {
        public Task ProcessSessionsUpdateAsync(int connectionId, IReadOnlyList<JellyfinSession> sessions, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task BroadcastPreSweepWarningAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeIntegrationClientFactory : IIntegrationClientFactory
    {
        public Task<JellyfinClient?> CreateJellyfinClientAsync(int connectionId, CancellationToken ct = default)
            => Task.FromResult<JellyfinClient?>(null);

        public Task<RadarrClient?> CreateRadarrClientAsync(int connectionId, CancellationToken ct = default)
            => Task.FromResult<RadarrClient?>(null);

        public Task<SonarrClient?> CreateSonarrClientAsync(int connectionId, CancellationToken ct = default)
            => Task.FromResult<SonarrClient?>(null);

        public Task<BazarrClient?> CreateBazarrClientAsync(CancellationToken ct = default)
            => Task.FromResult<BazarrClient?>(null);
    }

    private static async Task<RuleGroup> SeedGroupAsync(SweeprrDbContext db, string name = "Test Group")
    {
        var group = new RuleGroup
        {
            Name = name,
            MediaType = MediaType.Movie,
            IsEnabled = true,
        };
        db.RuleGroups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<SweepItem> SeedItemAsync(
        SweeprrDbContext db, int ruleGroupId, string itemId, string title,
        SweepItemStatus status = SweepItemStatus.Pending)
    {
        var item = new SweepItem
        {
            RuleGroupId = ruleGroupId,
            MediaServerItemId = itemId,
            Title = title,
            MediaType = MediaType.Movie,
            Status = status,
            FlaggedAt = DateTime.UtcNow,
        };
        db.SweepItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task SeedJellyfinConnectionAsync(SweeprrDbContext db, string baseUrl)
    {
        db.ServerConnections.Add(new ServerConnection
        {
            Type = ConnectionType.Jellyfin,
            Name = "Jellyfin",
            BaseUrl = baseUrl,
            ApiKeyEncrypted = "encrypted",
            IsEnabled = true,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedGlobalSettingsAsync(SweeprrDbContext db)
    {
        db.GlobalSettings.Add(new GlobalSettings { Id = 1 });
        await db.SaveChangesAsync();
    }
}
