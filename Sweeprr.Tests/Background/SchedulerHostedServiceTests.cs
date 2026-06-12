using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Background;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Background;

public class SchedulerHostedServiceTests : IDisposable
{
    private readonly List<string> _dbPaths = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }
    // ── Cron validation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("0 3 * * *", true)]
    [InlineData("*/5 * * * *", true)]
    [InlineData("0 0 1 1 *", true)]
    [InlineData("not-a-cron", false)]
    [InlineData("", false)]
    [InlineData("* * * *", false)]       // 4-field (need 5)
    [InlineData("60 * * * *", false)]     // minute out of range
    [InlineData("0 25 * * *", false)]     // hour out of range
    public void IsValidCron_ReturnsExpected(string expression, bool expected)
    {
        Assert.Equal(expected, SchedulerHostedService.IsValidCron(expression));
    }

    // ── Manual trigger ──────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerScanAsync_RunsPipelineAndReturnsResult()
    {
        var (service, pipeline, _) = CreateService();
        var group = await SeedGroupAsync(pipeline.ScopeFactory, "Test Group", "*/5 * * * *");

        var result = await service.TriggerScanAsync(group.Id);

        Assert.Equal(group.Id, result.RuleGroupId);
        Assert.Equal("Test Group", result.RuleGroupName);
        Assert.Equal(0, result.ItemsFlagged);
        Assert.True(result.Duration.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task TriggerScanAsync_NonexistentGroup_Throws()
    {
        var (service, _, _) = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TriggerScanAsync(999));
    }

    // ── Dedupe ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerScanAsync_ConcurrentSameGroup_SecondCallThrows()
    {
        var (service, pipeline, _) = CreateService();
        var group = await SeedGroupAsync(pipeline.ScopeFactory, "Slow Group", null);

        pipeline.Delay = TimeSpan.FromSeconds(2);

        var first = service.TriggerScanAsync(group.Id);
        await Task.Delay(50); // let first acquire the lock

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TriggerScanAsync(group.Id));
        Assert.Contains("already running", ex.Message);

        await first; // clean up
    }

    [Fact]
    public async Task TriggerScanAsync_DifferentGroups_BothRunSerially()
    {
        var (service, pipeline, _) = CreateService();
        var g1 = await SeedGroupAsync(pipeline.ScopeFactory, "Group A", null);
        var g2 = await SeedGroupAsync(pipeline.ScopeFactory, "Group B", null);

        var r1 = await service.TriggerScanAsync(g1.Id);
        var r2 = await service.TriggerScanAsync(g2.Id);

        Assert.Equal("Group A", r1.RuleGroupName);
        Assert.Equal("Group B", r2.RuleGroupName);
    }

    // ── Activity logging ────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerScanAsync_WritesActivityLog()
    {
        var (service, pipeline, _) = CreateService();
        var group = await SeedGroupAsync(pipeline.ScopeFactory, "Logged Group", null);

        await service.TriggerScanAsync(group.Id);

        await using var scope = pipeline.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var logs = await db.ActivityLogs
            .Where(l => l.Category == ActivityLogCategory.Rule)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();

        Assert.True(logs.Count >= 2); // started + completed
        Assert.Contains(logs, l => l.Message.Contains("started"));
        Assert.Contains(logs, l => l.Message.Contains("completed"));
    }

    // ── Scheduler loop ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StartsAndStopsCleanly()
    {
        var (service, _, _) = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);

        // let it tick once
        await Task.Delay(500);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ForceTickAsync_FiresDueSchedule()
    {
        var (service, pipeline, _) = CreateService();
        await SeedGroupAsync(pipeline.ScopeFactory, "Fast Cron", "* * * * *");

        // Load schedules — next fire is at the upcoming minute boundary
        await service.ForceReloadAsync();

        // Wait past the next minute boundary
        var now = DateTime.UtcNow;
        var nextMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(1);
        var waitMs = (int)(nextMinute - DateTime.UtcNow).TotalMilliseconds + 200;
        if (waitMs > 0 && waitMs < 62_000)
            await Task.Delay(waitMs);

        // Fire without reloading — the already-loaded entry is now due
        await service.ForceFireAsync();

        Assert.True(pipeline.ExecutionCount > 0,
            "Expected the pipeline to fire after passing the minute boundary");
    }

    [Fact]
    public async Task ForceTickAsync_DisabledGroup_NotScheduled()
    {
        var (service, pipeline, _) = CreateService();

        await using (var scope = pipeline.ScopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
            db.RuleGroups.Add(new RuleGroup
            {
                Name = "Disabled",
                MediaType = MediaType.Movie,
                IsEnabled = false,
                CronOverride = "* * * * *",
            });
            await db.SaveChangesAsync();
        }

        // Disabled group should not appear in schedules at all
        await service.ForceReloadAsync();

        var now = DateTime.UtcNow;
        var nextMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(1);
        var waitMs = (int)(nextMinute - DateTime.UtcNow).TotalMilliseconds + 200;
        if (waitMs > 0 && waitMs < 62_000)
            await Task.Delay(waitMs);

        await service.ForceFireAsync();

        Assert.Equal(0, pipeline.ExecutionCount);
    }

    [Fact]
    public async Task ForceTickAsync_UsesGroupCronOverrideOverDefault()
    {
        var (service, pipeline, _) = CreateService();

        // Default cron "0 3 * * *" won't fire. Override "* * * * *" will.
        await SeedGroupAsync(pipeline.ScopeFactory, "Override Group", "* * * * *");

        await service.ForceReloadAsync();

        var now = DateTime.UtcNow;
        var nextMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(1);
        var waitMs = (int)(nextMinute - DateTime.UtcNow).TotalMilliseconds + 200;
        if (waitMs > 0 && waitMs < 62_000)
            await Task.Delay(waitMs);

        await service.ForceFireAsync();

        Assert.True(pipeline.ExecutionCount > 0);
    }

    [Fact]
    public async Task ForceTickAsync_InvalidCronOverride_FallsBackToDefault()
    {
        var (service, pipeline, _) = CreateService();

        // Invalid cron override → falls back to default "0 3 * * *" → won't fire
        await SeedGroupAsync(pipeline.ScopeFactory, "Bad Cron Group", "not-a-cron");

        await service.ForceTickAsync();

        Assert.Equal(0, pipeline.ExecutionCount);
    }

    // ── Pre-sweep broadcast ──────────────────────────────────────────────────

    [Fact]
    public async Task ForceCheckPreSweepAsync_ScheduleWithinWindow_SendsBroadcast()
    {
        var (service, pipeline, _) = CreateService(out var sessionAlerts);
        // "* * * * *" always fires within the next 60s — well inside the 10-minute window
        await SeedGroupAsync(pipeline.ScopeFactory, "Imminent Group", "* * * * *");

        await service.ForceReloadAsync();
        await service.ForceCheckPreSweepAsync();

        Assert.Equal(1, sessionAlerts.BroadcastCount);
    }

    [Fact]
    public async Task ForceCheckPreSweepAsync_DoesNotResend_OnRepeatedTicks()
    {
        var (service, pipeline, _) = CreateService(out var sessionAlerts);
        await SeedGroupAsync(pipeline.ScopeFactory, "Imminent Group", "* * * * *");

        await service.ForceReloadAsync();
        await service.ForceCheckPreSweepAsync();
        await service.ForceCheckPreSweepAsync();
        await service.ForceCheckPreSweepAsync();

        Assert.Equal(1, sessionAlerts.BroadcastCount);
    }

    [Fact]
    public async Task ForceCheckPreSweepAsync_NoSchedules_SendsNothing()
    {
        var (service, _, _) = CreateService(out var sessionAlerts);

        await service.ForceReloadAsync();
        await service.ForceCheckPreSweepAsync();

        Assert.Equal(0, sessionAlerts.BroadcastCount);
    }

    // ── CronValidationException ─────────────────────────────────────────────

    [Fact]
    public void CronValidationException_ContainsExpression()
    {
        var ex = new CronValidationException("bad-cron");
        Assert.Equal("bad-cron", ex.Expression);
        Assert.Contains("bad-cron", ex.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════════

    private (SchedulerHostedService Service, FakeScanPipeline Pipeline, IServiceScopeFactory ScopeFactory)
        CreateService() => CreateService(out _);

    private (SchedulerHostedService Service, FakeScanPipeline Pipeline, IServiceScopeFactory ScopeFactory)
        CreateService(out FakeSessionAlertService sessionAlerts)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_sched_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);

        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(opt =>
            opt.UseSqlite($"Data Source={dbPath}"));

        var sp = services.BuildServiceProvider();

        using (var initScope = sp.CreateScope())
        {
            var db = initScope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
            db.Database.Migrate();

            if (!db.GlobalSettings.Any())
            {
                db.GlobalSettings.Add(new GlobalSettings
                {
                    Id = 1,
                    DefaultCron = "0 3 * * *",
                    GlobalDryRun = true,
                });
                db.SaveChanges();
            }
        }

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var pipeline = new FakeScanPipeline(scopeFactory);
        sessionAlerts = new FakeSessionAlertService();

        var service = new SchedulerHostedService(
            scopeFactory,
            pipeline,
            sessionAlerts,
            NullLogger<SchedulerHostedService>.Instance);

        return (service, pipeline, scopeFactory);
    }

    private static async Task<RuleGroup> SeedGroupAsync(
        IServiceScopeFactory scopeFactory, string name, string? cronOverride)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

        var group = new RuleGroup
        {
            Name = name,
            MediaType = MediaType.Movie,
            IsEnabled = true,
            CronOverride = cronOverride,
        };

        db.RuleGroups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private sealed class FakeSessionAlertService : IJellyfinSessionAlertService
    {
        public int BroadcastCount;

        public Task ProcessSessionsUpdateAsync(
            int connectionId, IReadOnlyList<JellyfinSession> sessions, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task BroadcastPreSweepWarningAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref BroadcastCount);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScanPipeline : IScanPipeline
    {
        public readonly IServiceScopeFactory ScopeFactory;
        public int ExecutionCount;
        public TimeSpan Delay = TimeSpan.Zero;

        public FakeScanPipeline(IServiceScopeFactory scopeFactory) => ScopeFactory = scopeFactory;

        public async Task<ScanResult> ExecuteAsync(int ruleGroupId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ExecutionCount);

            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, ct);

            await using var scope = ScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
            var group = await db.RuleGroups.FirstAsync(g => g.Id == ruleGroupId, ct);

            return new ScanResult(ruleGroupId, group.Name, 0, TimeSpan.FromMilliseconds(1));
        }
    }
}
