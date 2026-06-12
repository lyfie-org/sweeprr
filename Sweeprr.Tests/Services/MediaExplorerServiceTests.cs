using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Media;
using Sweeprr.API.Models;
using Sweeprr.API.Services;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.Tests.Services;

public class MediaExplorerServiceTests : IDisposable
{
    private readonly List<string> _dbPaths = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // ── GetPagedAsync: grouping ──────────────────────────────────────────────

    [Fact]
    public async Task GetPagedAsync_NoGrouping_ReturnsOneRowPerItem()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Movie One (2020)", SweepItemStatus.Pending, sizeBytes: 1_073_741_824);
        await SeedItemAsync(db, group.Id, "item-2", "Movie Two (2021)", SweepItemStatus.Approved, sizeBytes: 2_147_483_648);

        var result = await svc.GetPagedAsync(new MediaQueryParams());

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetPagedAsync_SameMediaServerItemId_CollapsesToSingleRow_UsesHighestPriorityStatus()
    {
        var (svc, db) = CreateService();
        var g1 = await SeedGroupAsync(db, "Group A");
        var g2 = await SeedGroupAsync(db, "Group B");

        // Same item flagged by two rule groups: one stale Swept record, one current Pending record
        await SeedItemAsync(db, g1.Id, "shared-item", "Shared Movie (2019)", SweepItemStatus.Swept, flaggedAt: DateTime.UtcNow.AddDays(-5));
        await SeedItemAsync(db, g2.Id, "shared-item", "Shared Movie (2019)", SweepItemStatus.Pending, flaggedAt: DateTime.UtcNow);

        var result = await svc.GetPagedAsync(new MediaQueryParams());

        Assert.Equal(1, result.TotalCount);
        var item = result.Items[0];
        Assert.Equal(SweepItemStatus.Pending, item.Status);
        var matchedGroup = Assert.Single(item.MatchedRuleGroups);
        Assert.Equal("Group B", matchedGroup.RuleGroupName);
    }

    [Fact]
    public async Task GetPagedAsync_OnlyPendingItemsContributeMatchedRuleGroups()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Old Approved Item", SweepItemStatus.Approved, summary: "Some old match");

        var result = await svc.GetPagedAsync(new MediaQueryParams());

        Assert.Empty(result.Items.Single().MatchedRuleGroups);
    }

    // ── GetPagedAsync: title parsing / size formatting ───────────────────────

    [Fact]
    public async Task GetPagedAsync_ExtractsYearFromTitleSuffix()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Movie One (2020)", SweepItemStatus.Pending);
        await SeedItemAsync(db, group.Id, "item-2", "No Year Movie", SweepItemStatus.Pending);

        var result = await svc.GetPagedAsync(new MediaQueryParams());

        Assert.Equal(2020, result.Items.Single(i => i.Id == "item-1").Year);
        Assert.Null(result.Items.Single(i => i.Id == "item-2").Year);
    }

    [Fact]
    public async Task GetPagedAsync_FormatsSizeLabelByMagnitude()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "tiny", "Tiny File", SweepItemStatus.Pending, sizeBytes: 50_000_000);       // ~0.047 GB
        await SeedItemAsync(db, group.Id, "medium", "Medium File", SweepItemStatus.Pending, sizeBytes: 2_147_483_648); // 2 GB
        await SeedItemAsync(db, group.Id, "large", "Large File", SweepItemStatus.Pending, sizeBytes: 21_474_836_480);  // 20 GB
        await SeedItemAsync(db, group.Id, "unknown", "Unknown Size", SweepItemStatus.Pending, sizeBytes: null);

        var result = await svc.GetPagedAsync(new MediaQueryParams { PageSize = 10 });

        Assert.Equal("< 0.1 GB", result.Items.Single(i => i.Id == "tiny").SizeLabel);
        Assert.Equal("2.00 GB", result.Items.Single(i => i.Id == "medium").SizeLabel);
        Assert.Equal("20.0 GB", result.Items.Single(i => i.Id == "large").SizeLabel);
        Assert.Equal("—", result.Items.Single(i => i.Id == "unknown").SizeLabel);
    }

    // ── GetPagedAsync: filters ────────────────────────────────────────────────

    [Fact]
    public async Task GetPagedAsync_FilterBySearch_MatchesTitleSubstringCaseInsensitive()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "The Matrix (1999)", SweepItemStatus.Pending);
        await SeedItemAsync(db, group.Id, "item-2", "Inception (2010)", SweepItemStatus.Pending);

        var result = await svc.GetPagedAsync(new MediaQueryParams { Search = "matrix" });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("item-1", result.Items[0].Id);
    }

    [Fact]
    public async Task GetPagedAsync_FilterByType_ReturnsMatchingItems()
    {
        var (svc, db) = CreateService();
        var movieGroup = await SeedGroupAsync(db, "Movies", MediaType.Movie);
        var seriesGroup = await SeedGroupAsync(db, "Series", MediaType.Series);
        await SeedItemAsync(db, movieGroup.Id, "movie-1", "A Movie", SweepItemStatus.Pending, mediaType: MediaType.Movie);
        await SeedItemAsync(db, seriesGroup.Id, "series-1", "A Series", SweepItemStatus.Pending, mediaType: MediaType.Series);

        var result = await svc.GetPagedAsync(new MediaQueryParams { Type = MediaType.Series });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("series-1", result.Items[0].Id);
    }

    [Fact]
    public async Task GetPagedAsync_FilterByStatus_ReturnsMatchingItems()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Pending Item", SweepItemStatus.Pending);
        await SeedItemAsync(db, group.Id, "item-2", "Approved Item", SweepItemStatus.Approved);

        var result = await svc.GetPagedAsync(new MediaQueryParams { Status = SweepItemStatus.Approved });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("item-2", result.Items[0].Id);
    }

    // ── GetPagedAsync: sort + pagination ─────────────────────────────────────

    [Fact]
    public async Task GetPagedAsync_SortBySizeGbDescending()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "small", "Small", SweepItemStatus.Pending, sizeBytes: 1_073_741_824);
        await SeedItemAsync(db, group.Id, "large", "Large", SweepItemStatus.Pending, sizeBytes: 10_737_418_240);

        var result = await svc.GetPagedAsync(new MediaQueryParams { SortBy = "sizegb", SortDir = "desc" });

        Assert.Equal("large", result.Items[0].Id);
        Assert.Equal("small", result.Items[1].Id);
    }

    [Fact]
    public async Task GetPagedAsync_DefaultSort_OrdersByTitleAscending()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-b", "Beta", SweepItemStatus.Pending);
        await SeedItemAsync(db, group.Id, "item-a", "Alpha", SweepItemStatus.Pending);

        var result = await svc.GetPagedAsync(new MediaQueryParams());

        Assert.Equal("Alpha", result.Items[0].Title);
        Assert.Equal("Beta", result.Items[1].Title);
    }

    [Fact]
    public async Task GetPagedAsync_Paginates()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        for (var i = 1; i <= 5; i++)
            await SeedItemAsync(db, group.Id, $"item-{i}", $"Item {i}", SweepItemStatus.Pending);

        var page1 = await svc.GetPagedAsync(new MediaQueryParams { Page = 1, PageSize = 2 });
        var page2 = await svc.GetPagedAsync(new MediaQueryParams { Page = 2, PageSize = 2 });
        var page3 = await svc.GetPagedAsync(new MediaQueryParams { Page = 3, PageSize = 2 });

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);
    }

    // ── GetPagedAsync: playback + exclusion enrichment ───────────────────────

    [Fact]
    public async Task GetPagedAsync_IncludesPlaybackAndExclusionData()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "watched-item", "Watched Movie", SweepItemStatus.Pending);

        db.PlaybackActivities.Add(new PlaybackActivity
        {
            MediaServerItemId = "watched-item",
            UserId = "user-1",
            Username = "alice",
            PlayCount = 2,
            LastWatched = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = DateTime.UtcNow,
        });
        db.Exclusions.Add(new Exclusion { MediaServerItemId = "watched-item", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await svc.GetPagedAsync(new MediaQueryParams());

        var item = result.Items.Single();
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), item.LastWatched);
        Assert.Equal(1, item.WatchedByCount);
        Assert.True(item.IsExcluded);
    }

    [Fact]
    public async Task GetPagedAsync_ExpiredExclusion_NotMarkedExcluded()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Item", SweepItemStatus.Pending);
        db.Exclusions.Add(new Exclusion
        {
            MediaServerItemId = "item-1",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var result = await svc.GetPagedAsync(new MediaQueryParams());

        Assert.False(result.Items.Single().IsExcluded);
    }

    // ── GetRuleTraceAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetRuleTraceAsync_UnknownItem_ReturnsNull()
    {
        var (svc, _) = CreateService();

        var result = await svc.GetRuleTraceAsync("missing-item");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRuleTraceAsync_ReturnsEvaluationsForMatchingMediaTypeGroups()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db, "Big Files", MediaType.Movie);
        db.Rules.Add(new Rule
        {
            RuleGroupId = group.Id,
            Section = 0,
            Field = RuleField.FileSizeGb,
            Comparator = RuleComparator.GreaterThan,
            Value = "10",
            ValueType = RuleValueType.Number,
        });
        await db.SaveChangesAsync();

        await SeedItemAsync(db, group.Id, "item-1", "Big Movie", SweepItemStatus.Pending, sizeBytes: 21_474_836_480, mediaType: MediaType.Movie);

        var result = await svc.GetRuleTraceAsync("item-1");

        Assert.NotNull(result);
        Assert.Equal("Big Movie", result!.Title);
        var evaluation = Assert.Single(result.Evaluations);
        Assert.Equal("Big Files", evaluation.RuleGroupName);
        Assert.True(evaluation.Matched);
        var clause = Assert.Single(evaluation.ClauseResults);
        Assert.Equal("FileSizeGb", clause.Field);
        Assert.True(clause.Result);
    }

    [Fact]
    public async Task GetRuleTraceAsync_OnlyEvaluatesGroupsMatchingItemMediaType()
    {
        var (svc, db) = CreateService();
        var movieGroup = await SeedGroupAsync(db, "Movie Rules", MediaType.Movie);
        var seriesGroup = await SeedGroupAsync(db, "Series Rules", MediaType.Series);
        db.Rules.Add(new Rule
        {
            RuleGroupId = seriesGroup.Id,
            Section = 0,
            Field = RuleField.FileSizeGb,
            Comparator = RuleComparator.GreaterThan,
            Value = "1",
            ValueType = RuleValueType.Number,
        });
        await db.SaveChangesAsync();

        await SeedItemAsync(db, movieGroup.Id, "item-1", "A Movie", SweepItemStatus.Pending, mediaType: MediaType.Movie);

        var result = await svc.GetRuleTraceAsync("item-1");

        Assert.NotNull(result);
        Assert.DoesNotContain(result!.Evaluations, e => e.RuleGroupName == "Series Rules");
    }

    // ── QueueManualAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task QueueManualAsync_QueuesNewPendingItemFromTemplate()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Old Sweep", SweepItemStatus.Swept);

        var result = await svc.QueueManualAsync(new QueueManualRequest { Ids = ["item-1"] });

        Assert.Equal(1, result.Queued);
        Assert.Equal(0, result.AlreadyQueued);

        var pending = await db.SweepItems
            .Where(s => s.MediaServerItemId == "item-1" && s.Status == SweepItemStatus.Pending)
            .ToListAsync();
        var created = Assert.Single(pending);
        Assert.Equal("Manually added via Media Explorer", created.MatchedRuleSummary);
    }

    [Fact]
    public async Task QueueManualAsync_AlreadyPending_NotDuplicated()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "Already Queued", SweepItemStatus.Pending);

        var result = await svc.QueueManualAsync(new QueueManualRequest { Ids = ["item-1"] });

        Assert.Equal(0, result.Queued);
        Assert.Equal(1, result.AlreadyQueued);
    }

    [Fact]
    public async Task QueueManualAsync_UnknownId_NotQueued()
    {
        var (svc, _) = CreateService();

        var result = await svc.QueueManualAsync(new QueueManualRequest { Ids = ["never-seen"] });

        Assert.Equal(0, result.Queued);
        Assert.Equal(0, result.AlreadyQueued);
    }

    // ── ExcludeBulkAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExcludeBulkAsync_CreatesGlobalExclusionAndRemovesPendingItem()
    {
        var (svc, db) = CreateService();
        var group = await SeedGroupAsync(db);
        await SeedItemAsync(db, group.Id, "item-1", "To Exclude", SweepItemStatus.Pending);

        var result = await svc.ExcludeBulkAsync(new ExcludeBulkRequest { Ids = ["item-1"] });

        Assert.Equal(1, result.Excluded);

        var exclusion = await db.Exclusions.SingleAsync(e => e.MediaServerItemId == "item-1");
        Assert.Null(exclusion.RuleGroupId);
        Assert.Equal("Excluded from Media Explorer", exclusion.Reason);

        var remainingPending = await db.SweepItems
            .CountAsync(s => s.MediaServerItemId == "item-1" && s.Status == SweepItemStatus.Pending);
        Assert.Equal(0, remainingPending);
    }

    [Fact]
    public async Task ExcludeBulkAsync_DedupesExistingExclusion()
    {
        var (svc, db) = CreateService();
        db.Exclusions.Add(new Exclusion { MediaServerItemId = "item-1", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await svc.ExcludeBulkAsync(new ExcludeBulkRequest { Ids = ["item-1"] });

        var count = await db.Exclusions.CountAsync(e => e.MediaServerItemId == "item-1");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ExcludeBulkAsync_ScopedToRuleGroup_OnlyRemovesMatchingPendingItems()
    {
        var (svc, db) = CreateService();
        var g1 = await SeedGroupAsync(db, "Group 1");
        var g2 = await SeedGroupAsync(db, "Group 2");
        await SeedItemAsync(db, g1.Id, "item-1", "Item", SweepItemStatus.Pending);
        await SeedItemAsync(db, g2.Id, "item-1", "Item", SweepItemStatus.Pending);

        var result = await svc.ExcludeBulkAsync(new ExcludeBulkRequest { Ids = ["item-1"], RuleGroupId = g1.Id });

        Assert.Equal(1, result.Excluded);

        var remaining = await db.SweepItems
            .Where(s => s.MediaServerItemId == "item-1" && s.Status == SweepItemStatus.Pending)
            .ToListAsync();
        var remainingItem = Assert.Single(remaining);
        Assert.Equal(g2.Id, remainingItem.RuleGroupId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private (MediaExplorerService Service, SweeprrDbContext Db) CreateService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_media_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);

        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var db = new SweeprrDbContext(options);
        db.Database.Migrate();

        var evaluator = new RuleEvaluator(new ValueResolver());
        var channel = Channel.CreateUnbounded<byte>();
        var overlay = new FakeOverlayRenderingService();

        return (new MediaExplorerService(db, evaluator, channel, overlay), db);
    }

    private static async Task<RuleGroup> SeedGroupAsync(SweeprrDbContext db, string name = "Test Group", MediaType mediaType = MediaType.Movie)
    {
        var group = new RuleGroup
        {
            Name = name,
            MediaType = mediaType,
            IsEnabled = true,
        };
        db.RuleGroups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<SweepItem> SeedItemAsync(
        SweeprrDbContext db, int ruleGroupId, string itemId, string title,
        SweepItemStatus status = SweepItemStatus.Pending,
        long? sizeBytes = null,
        MediaType mediaType = MediaType.Movie,
        DateTime? flaggedAt = null,
        string? summary = null)
    {
        var item = new SweepItem
        {
            RuleGroupId = ruleGroupId,
            MediaServerItemId = itemId,
            Title = title,
            MediaType = mediaType,
            Status = status,
            SizeBytes = sizeBytes,
            FlaggedAt = flaggedAt ?? DateTime.UtcNow,
            MatchedRuleSummary = summary,
        };
        db.SweepItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private sealed class FakeOverlayRenderingService : IOverlayRenderingService
    {
        public Task ApplyOverlayAsync(SweepItem item, string labelText, CancellationToken ct) => Task.CompletedTask;
        public Task RestoreOriginalAsync(SweepItem item, CancellationToken ct) => Task.CompletedTask;
    }
}
