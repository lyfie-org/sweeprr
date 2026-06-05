using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Sweep;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Sweep;

public class SweepQueueServiceTests : IDisposable
{
    private readonly List<string> _dbPaths = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // ── Query ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_NoFilter_ReturnsAllItems()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", SweepItemStatus.Pending);
        await SeedItemAsync(db, group.Id, "item-2", SweepItemStatus.Approved);

        var result = await svc.QueryAsync(new SweepQueryParams());

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task QueryAsync_FilterByStatus_ReturnsMatchingItems()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", SweepItemStatus.Pending);
        await SeedItemAsync(db, group.Id, "item-2", SweepItemStatus.Approved);
        await SeedItemAsync(db, group.Id, "item-3", SweepItemStatus.Pending);

        var result = await svc.QueryAsync(new SweepQueryParams { Status = SweepItemStatus.Pending });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal(SweepItemStatus.Pending, i.Status));
    }

    [Fact]
    public async Task QueryAsync_FilterByRuleGroupId_ReturnsMatchingItems()
    {
        var (svc, db) = CreateService();
        var g1 = await SeedGroupAsync(db, "Group 1");
        var g2 = await SeedGroupAsync(db, "Group 2");
        await SeedItemAsync(db, g1.Id, "item-a");
        await SeedItemAsync(db, g2.Id, "item-b");

        var result = await svc.QueryAsync(new SweepQueryParams { RuleGroupId = g1.Id });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("item-a", result.Items[0].MediaServerItemId);
    }

    [Fact]
    public async Task QueryAsync_Paginates()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        for (var i = 1; i <= 5; i++)
            await SeedItemAsync(db, group.Id, $"item-{i}");

        var page1 = await svc.QueryAsync(new SweepQueryParams { Page = 1, PageSize = 2 });
        var page2 = await svc.QueryAsync(new SweepQueryParams { Page = 2, PageSize = 2 });
        var page3 = await svc.QueryAsync(new SweepQueryParams { Page = 3, PageSize = 2 });

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);
    }

    // ── GetById ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingItem_ReturnsResponse()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        var item = await SeedItemAsync(db, group.Id, "item-x");

        var result = await svc.GetByIdAsync(item.Id);

        Assert.NotNull(result);
        Assert.Equal("item-x", result.MediaServerItemId);
        Assert.Equal(group.Name, result.RuleGroupName);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var (svc, _) = CreateService();

        var result = await svc.GetByIdAsync(9999);

        Assert.Null(result);
    }

    // ── Summary ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummaryAsync_CountsAndSizesCorrectly()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "p1", SweepItemStatus.Pending, sizeBytes: 1_000_000);
        await SeedItemAsync(db, group.Id, "p2", SweepItemStatus.Pending, sizeBytes: 2_000_000);
        await SeedItemAsync(db, group.Id, "a1", SweepItemStatus.Approved, sizeBytes: 3_000_000);

        var summary = await svc.GetSummaryAsync();

        Assert.Equal(2, summary.PendingCount);
        Assert.Equal(1, summary.ApprovedCount);
        Assert.Equal(3_000_000, summary.PendingBytes);
        Assert.Equal(3_000_000, summary.ApprovedBytes);
    }

    // ── Approve ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_PendingItem_SetsApproved()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        var item = await SeedItemAsync(db, group.Id, "item-1", SweepItemStatus.Pending);

        var result = await svc.ApproveAsync(item.Id);

        Assert.NotNull(result);
        Assert.Equal(SweepItemStatus.Approved, result.Status);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyApproved_ReturnsSameStatus()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        var item = await SeedItemAsync(db, group.Id, "item-1", SweepItemStatus.Approved);

        var result = await svc.ApproveAsync(item.Id);

        Assert.NotNull(result);
        Assert.Equal(SweepItemStatus.Approved, result.Status);
    }

    [Fact]
    public async Task ApproveAsync_NotFound_ReturnsNull()
    {
        var (svc, _) = CreateService();

        var result = await svc.ApproveAsync(9999);

        Assert.Null(result);
    }

    // ── Ignore ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IgnoreAsync_PendingItem_SetsIgnored()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        var item = await SeedItemAsync(db, group.Id, "item-1", SweepItemStatus.Pending);

        var result = await svc.IgnoreAsync(item.Id, createExclusion: false);

        Assert.NotNull(result);
        Assert.Equal(SweepItemStatus.Ignored, result.Status);
    }

    [Fact]
    public async Task IgnoreAsync_WithCreateExclusion_AddsExclusion()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        var item = await SeedItemAsync(db, group.Id, "item-exclude", SweepItemStatus.Pending);

        await svc.IgnoreAsync(item.Id, createExclusion: true);

        var exclusion = await db.Exclusions.FirstOrDefaultAsync(e => e.MediaServerItemId == "item-exclude");
        Assert.NotNull(exclusion);
    }

    [Fact]
    public async Task IgnoreAsync_WithCreateExclusion_DedupesExistingExclusion()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);

        db.Exclusions.Add(new Exclusion { MediaServerItemId = "item-dup", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var item = await SeedItemAsync(db, group.Id, "item-dup", SweepItemStatus.Pending);
        await svc.IgnoreAsync(item.Id, createExclusion: true);

        var count = await db.Exclusions.CountAsync(e => e.MediaServerItemId == "item-dup");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task IgnoreAsync_NotFound_ReturnsNull()
    {
        var (svc, _) = CreateService();

        var result = await svc.IgnoreAsync(9999, createExclusion: false);

        Assert.Null(result);
    }

    // ── Reconcile ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_NewMatches_CreatesPendingItems()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);

        var results = new[]
        {
            EvaluationResult.Matched(Media("item-1", "Title One"), "Rule A matched"),
            EvaluationResult.Matched(Media("item-2", "Title Two"), "Rule B matched"),
        };

        var count = await svc.ReconcileAsync(group.Id, results);

        Assert.Equal(2, count);
        var items = await db.SweepItems.Where(s => s.RuleGroupId == group.Id).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.All(items, s => Assert.Equal(SweepItemStatus.Pending, s.Status));
    }

    [Fact]
    public async Task ReconcileAsync_NoMatchResults_RemovesStalePendingItems()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "stale-item", SweepItemStatus.Pending);

        var count = await svc.ReconcileAsync(group.Id, []);

        Assert.Equal(0, count);
        var remaining = await db.SweepItems.CountAsync(s => s.RuleGroupId == group.Id && s.Status == SweepItemStatus.Pending);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task ReconcileAsync_UpdatesExistingPendingItem()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-x", SweepItemStatus.Pending);

        var results = new[]
        {
            EvaluationResult.Matched(Media("item-x", "Updated Title"), "Updated summary"),
        };

        var count = await svc.ReconcileAsync(group.Id, results);

        Assert.Equal(1, count);
        var updated = await db.SweepItems.FirstAsync(s => s.MediaServerItemId == "item-x");
        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal("Updated summary", updated.MatchedRuleSummary);
    }

    [Fact]
    public async Task ReconcileAsync_SkipsExcludedItems()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        db.Exclusions.Add(new Exclusion { MediaServerItemId = "excluded-item", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var results = new[]
        {
            EvaluationResult.Matched(Media("excluded-item", "Should Skip"), "rule"),
            EvaluationResult.Matched(Media("allowed-item", "Should Upsert"), "rule"),
        };

        var count = await svc.ReconcileAsync(group.Id, results);

        Assert.Equal(1, count);
        var items = await db.SweepItems.Where(s => s.RuleGroupId == group.Id).ToListAsync();
        Assert.Single(items);
        Assert.Equal("allowed-item", items[0].MediaServerItemId);
    }

    [Fact]
    public async Task ReconcileAsync_NonMatchingResults_NotAddedToQueue()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);

        var results = new[]
        {
            EvaluationResult.NoMatch(Media("no-match", "Title")),
            EvaluationResult.Excluded(Media("excluded", "Title"), "transient error"),
        };

        var count = await svc.ReconcileAsync(group.Id, results);

        Assert.Equal(0, count);
        Assert.Empty(await db.SweepItems.ToListAsync());
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private (SweepQueueService Service, SweeprrDbContext Db) CreateService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_sweep_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);

        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new SweeprrDbContext(options);
        db.Database.Migrate();

        return (new SweepQueueService(db), db);
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
        SweeprrDbContext db, int ruleGroupId, string itemId,
        SweepItemStatus status = SweepItemStatus.Pending,
        long? sizeBytes = null)
    {
        var item = new SweepItem
        {
            RuleGroupId = ruleGroupId,
            MediaServerItemId = itemId,
            Title = itemId,
            MediaType = MediaType.Movie,
            Status = status,
            SizeBytes = sizeBytes,
            FlaggedAt = DateTime.UtcNow,
        };
        db.SweepItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static MediaContext Media(string itemId, string title) => new()
    {
        ItemId = itemId,
        Title = title,
        MediaType = MediaType.Movie,
    };
}
