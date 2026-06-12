using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Background;
using Sweeprr.API.Data;
using Sweeprr.API.Models;
using Sweeprr.API.Services;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.Tests.Background;

public class ScanPipelineTests : IDisposable
{
    private readonly List<string> _dbPaths = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // ── Error cases ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_GroupNotFound_Throws()
    {
        var (pipeline, _) = CreatePipeline();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ExecuteAsync(999));
    }

    [Fact]
    public async Task ExecuteAsync_DisabledGroup_ReturnsZeroFlagged()
    {
        var (pipeline, scopeFactory) = CreatePipeline();
        var group = await SeedGroupAsync(scopeFactory, "Disabled", isEnabled: false);

        var result = await pipeline.ExecuteAsync(group.Id);

        Assert.Equal(0, result.ItemsFlagged);
        Assert.Equal("Disabled", result.RuleGroupName);
    }

    // ── Empty population ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmptyPopulation_ReconcilesClearingQueue()
    {
        var (pipeline, scopeFactory) = CreatePipeline(mediaItems: []);
        var group = await SeedGroupAsync(scopeFactory, "Empty Group");

        // Seed a stale Pending item that should be removed
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
            db.SweepItems.Add(new SweepItem
            {
                RuleGroupId = group.Id,
                MediaServerItemId = "stale-id",
                Title = "Stale",
                MediaType = MediaType.Movie,
                Status = SweepItemStatus.Pending,
                FlaggedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = await pipeline.ExecuteAsync(group.Id);

        Assert.Equal(0, result.ItemsFlagged);

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
            var remaining = await db.SweepItems.CountAsync(s => s.Status == SweepItemStatus.Pending);
            Assert.Equal(0, remaining);
        }
    }

    // ── Matched items ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithMatches_CreatesPendingItems()
    {
        var items = new[]
        {
            Media("jf-001", "Movie One"),
            Media("jf-002", "Movie Two"),
            Media("jf-003", "Movie Three"),
        };

        var (pipeline, scopeFactory) = CreatePipeline(
            mediaItems: items,
            matchAll: true);

        var group = await SeedGroupAsync(scopeFactory, "Match Group");

        var result = await pipeline.ExecuteAsync(group.Id);

        Assert.Equal(3, result.ItemsFlagged);

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
            var sweepItems = await db.SweepItems
                .Where(s => s.RuleGroupId == group.Id && s.Status == SweepItemStatus.Pending)
                .ToListAsync();
            Assert.Equal(3, sweepItems.Count);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoMatches_ZeroFlagged()
    {
        var items = new[] { Media("jf-001", "Movie One"), Media("jf-002", "Movie Two") };

        var (pipeline, scopeFactory) = CreatePipeline(
            mediaItems: items,
            matchAll: false);

        var group = await SeedGroupAsync(scopeFactory, "No Match Group");

        var result = await pipeline.ExecuteAsync(group.Id);

        Assert.Equal(0, result.ItemsFlagged);
    }

    [Fact]
    public async Task ExecuteAsync_ScanResultContainsGroupMeta()
    {
        var (pipeline, scopeFactory) = CreatePipeline(mediaItems: []);
        var group = await SeedGroupAsync(scopeFactory, "Meta Group");

        var result = await pipeline.ExecuteAsync(group.Id);

        Assert.Equal(group.Id, result.RuleGroupId);
        Assert.Equal("Meta Group", result.RuleGroupName);
        Assert.True(result.Duration.TotalMilliseconds >= 0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private (ScanPipeline Pipeline, IServiceScopeFactory ScopeFactory) CreatePipeline(
        IReadOnlyList<MediaContext>? mediaItems = null,
        bool matchAll = false)
    {
        mediaItems ??= [];

        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_pipe_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);

        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<ISweepQueueService, SweepQueueService>();
        services.AddScoped<IMediaPopulationService>(_ => new FakeMediaPopulationService(mediaItems));
        services.AddScoped<IRuleEvaluator>(_ => new FakeRuleEvaluator(matchAll));
        services.AddSingleton(Channel.CreateUnbounded<byte>());
        services.AddSingleton<INotificationService>(new NotificationService(
            Channel.CreateUnbounded<NotificationDispatchRequest>(), NullLogger<NotificationService>.Instance));
        services.AddScoped<IOverlayRenderingService, FakeOverlayRenderingService>();

        var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SweeprrDbContext>().Database.Migrate();
        }

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var pipeline = new ScanPipeline(scopeFactory, NullLogger<ScanPipeline>.Instance);
        return (pipeline, scopeFactory);
    }

    private static async Task<RuleGroup> SeedGroupAsync(
        IServiceScopeFactory scopeFactory, string name, bool isEnabled = true)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

        var group = new RuleGroup
        {
            Name = name,
            MediaType = MediaType.Movie,
            IsEnabled = isEnabled,
        };

        db.RuleGroups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static MediaContext Media(string itemId, string title) => new()
    {
        ItemId = itemId,
        Title = title,
        MediaType = MediaType.Movie,
    };

    private sealed class FakeMediaPopulationService : IMediaPopulationService
    {
        private readonly IReadOnlyList<MediaContext> _items;
        public FakeMediaPopulationService(IReadOnlyList<MediaContext> items) => _items = items;
        public Task<IReadOnlyList<MediaContext>> PopulateAsync(RuleGroup group, CancellationToken ct = default)
            => Task.FromResult(_items);
    }

    private sealed class FakeRuleEvaluator : IRuleEvaluator
    {
        private readonly bool _matchAll;
        public FakeRuleEvaluator(bool matchAll) => _matchAll = matchAll;

        public Task<IReadOnlyList<EvaluationResult>> EvaluateAsync(
            RuleGroup group,
            IReadOnlyList<MediaContext> items,
            CancellationToken cancellationToken = default)
        {
            var results = items
                .Select(item => _matchAll
                    ? EvaluationResult.Matched(item, "fake rule matched")
                    : EvaluationResult.NoMatch(item))
                .ToList();

            return Task.FromResult<IReadOnlyList<EvaluationResult>>(results);
        }

        public Task<IReadOnlyList<RuleGroupTrace>> TraceAsync(
            MediaContext item,
            IEnumerable<RuleGroup> groups,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RuleGroupTrace>>([]);
    }

    private sealed class FakeOverlayRenderingService : IOverlayRenderingService
    {
        public Task ApplyOverlayAsync(SweepItem item, string labelText, CancellationToken ct) => Task.CompletedTask;
        public Task RestoreOriginalAsync(SweepItem item, CancellationToken ct) => Task.CompletedTask;
    }
}
