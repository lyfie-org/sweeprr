using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Data;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Sweep;

/// <summary>
/// Tests for Story 8.2 — Scoped Exclusions and Tag-Based Whitelisting.
/// Covers: T1 (global tag exclusion), T2 (scoped tag exclusion), T3 (expired exclusion cleanup).
/// </summary>
public class TagExclusionTests : IDisposable
{
    private readonly List<string> _dbPaths = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // ── T1: Global tag exclusion blocks item ──────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_GlobalTagExclusion_SkipsTaggedItem()
    {
        var (svc, db) = CreateService();
        var conn = await SeedConnectionAsync(db);
        var group = await SeedGroupAsync(db);

        db.TagExclusions.Add(new TagExclusion
        {
            TagName = "sweeprr-keep",
            TagId = 1,
            ServerConnectionId = conn.Id,
            RuleGroupId = null, // global
        });
        await db.SaveChangesAsync();

        var results = new[]
        {
            EvaluationResult.Matched(MediaWithTags("tagged-item", ["sweeprr-keep"]), "rule"),
            EvaluationResult.Matched(MediaWithTags("clean-item",  []), "rule"),
        };

        var count = await svc.ReconcileAsync(group.Id, results);

        Assert.Equal(1, count);
        var items = await db.SweepItems.Where(s => s.RuleGroupId == group.Id).ToListAsync();
        Assert.Single(items);
        Assert.Equal("clean-item", items[0].MediaServerItemId);
    }

    // ── T2: Scoped tag exclusion — excluded in Group A, matched in Group B ────

    [Fact]
    public async Task ReconcileAsync_ScopedTagExclusion_OnlyBlocksInTargetGroup()
    {
        var (svc, db) = CreateService();
        var conn = await SeedConnectionAsync(db);
        var groupA = await SeedGroupAsync(db, "Group A");
        var groupB = await SeedGroupAsync(db, "Group B");

        db.TagExclusions.Add(new TagExclusion
        {
            TagName = "scope-test",
            TagId = 2,
            ServerConnectionId = conn.Id,
            RuleGroupId = groupA.Id, // scoped to Group A only
        });
        await db.SaveChangesAsync();

        var results = new[]
        {
            EvaluationResult.Matched(MediaWithTags("item-x", ["scope-test"]), "rule"),
        };

        // Group A: item should be blocked
        var countA = await svc.ReconcileAsync(groupA.Id, results);
        Assert.Equal(0, countA);

        // Group B: same item should pass through
        var countB = await svc.ReconcileAsync(groupB.Id, results);
        Assert.Equal(1, countB);

        var inGroupB = await db.SweepItems
            .Where(s => s.RuleGroupId == groupB.Id)
            .ToListAsync();
        Assert.Single(inGroupB);
        Assert.Equal("item-x", inGroupB[0].MediaServerItemId);
    }

    // ── T3: Expired media exclusion is not honoured ───────────────────────────

    [Fact]
    public async Task ReconcileAsync_ExpiredMediaExclusion_ItemIsQueued()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);

        // Add an exclusion that expired yesterday
        db.Exclusions.Add(new Exclusion
        {
            MediaServerItemId = "was-excluded",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // already expired
        });
        await db.SaveChangesAsync();

        var results = new[]
        {
            EvaluationResult.Matched(MediaWithTags("was-excluded", []), "rule"),
        };

        var count = await svc.ReconcileAsync(group.Id, results);

        // Expired exclusion should not block the item
        Assert.Equal(1, count);
        var items = await db.SweepItems.Where(s => s.RuleGroupId == group.Id).ToListAsync();
        Assert.Single(items);
        Assert.Equal("was-excluded", items[0].MediaServerItemId);
    }

    // ── T4: Active (non-expired) media exclusion still blocks ─────────────────

    [Fact]
    public async Task ReconcileAsync_ActiveMediaExclusion_ItemSkipped()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);

        db.Exclusions.Add(new Exclusion
        {
            MediaServerItemId = "temp-excluded",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(14), // expires in the future
        });
        await db.SaveChangesAsync();

        var results = new[]
        {
            EvaluationResult.Matched(MediaWithTags("temp-excluded", []), "rule"),
        };

        var count = await svc.ReconcileAsync(group.Id, results);

        Assert.Equal(0, count);
        Assert.Empty(await db.SweepItems.Where(s => s.RuleGroupId == group.Id).ToListAsync());
    }

    // ── T5: Case-insensitive tag matching ─────────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_TagMatchIsCaseInsensitive()
    {
        var (svc, db) = CreateService();
        var conn = await SeedConnectionAsync(db);
        var group = await SeedGroupAsync(db);

        db.TagExclusions.Add(new TagExclusion
        {
            TagName = "Sweeprr-Keep", // stored with mixed case
            TagId = 1,
            ServerConnectionId = conn.Id,
            RuleGroupId = null,
        });
        await db.SaveChangesAsync();

        var results = new[]
        {
            EvaluationResult.Matched(MediaWithTags("item-1", ["sweeprr-keep"]), "rule"), // lowercase
        };

        var count = await svc.ReconcileAsync(group.Id, results);
        Assert.Equal(0, count);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Infrastructure
    // ═══════════════════════════════════════════════════════════════════════════

    private (SweepQueueService Service, SweeprrDbContext Db) CreateService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_tag_excl_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);

        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new SweeprrDbContext(options);
        db.Database.Migrate();

        var channel = Channel.CreateUnbounded<byte>();
        var overlay = new FakeOverlayRenderingService();
        var notifications = new NotificationService(
            Channel.CreateUnbounded<NotificationDispatchRequest>(),
            NullLogger<NotificationService>.Instance);

        return (new SweepQueueService(db, channel, overlay, notifications), db);
    }

    private sealed class FakeOverlayRenderingService : IOverlayRenderingService
    {
        public Task ApplyOverlayAsync(SweepItem item, string labelText, CancellationToken ct) => Task.CompletedTask;
        public Task RestoreOriginalAsync(SweepItem item, CancellationToken ct) => Task.CompletedTask;
    }

    private static async Task<RuleGroup> SeedGroupAsync(SweeprrDbContext db, string name = "Test Group")
    {
        var group = new RuleGroup { Name = name, MediaType = MediaType.Movie, IsEnabled = true };
        db.RuleGroups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<ServerConnection> SeedConnectionAsync(SweeprrDbContext db)
    {
        var conn = new ServerConnection
        {
            Name = "Test Radarr",
            Type = ConnectionType.Radarr,
            BaseUrl = "http://localhost:7878",
            IsEnabled = true,
        };
        db.ServerConnections.Add(conn);
        await db.SaveChangesAsync();
        return conn;
    }

    private static MediaContext MediaWithTags(string itemId, IReadOnlyList<string> tags) => new()
    {
        ItemId = itemId,
        Title = itemId,
        MediaType = MediaType.Movie,
        Tags = tags,
    };
}
