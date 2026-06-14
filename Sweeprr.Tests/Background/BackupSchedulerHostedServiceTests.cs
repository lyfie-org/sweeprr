using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Background;
using Sweeprr.API.Data;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Background;

public class BackupSchedulerHostedServiceTests : IDisposable
{
    private readonly List<string> _dbPaths = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // ── Schedule loading ─────────────────────────────────────────────────────

    [Fact]
    public async Task ForceReloadAsync_NoSettingsRow_NextRunIsNull()
    {
        var (service, _, _) = CreateService();

        await service.ForceReloadAsync();

        Assert.Null(service.GetNextScheduledRun());
    }

    [Fact]
    public async Task ForceReloadAsync_DisabledSetting_NextRunIsNull()
    {
        var (service, _, scopeFactory) = CreateService();
        await SeedSettingsAsync(scopeFactory, new BackupSetting { IsEnabled = false, ScheduleCron = "* * * * *" });

        await service.ForceReloadAsync();

        Assert.Null(service.GetNextScheduledRun());
    }

    [Fact]
    public async Task ForceReloadAsync_EnabledWithValidCron_ComputesNextRun()
    {
        var (service, _, scopeFactory) = CreateService();
        await SeedSettingsAsync(scopeFactory, new BackupSetting { IsEnabled = true, ScheduleCron = "* * * * *" });

        await service.ForceReloadAsync();

        var next = service.GetNextScheduledRun();
        Assert.NotNull(next);
        Assert.True(next > DateTimeOffset.UtcNow);
        Assert.True(next <= DateTimeOffset.UtcNow.AddMinutes(1).AddSeconds(1));
    }

    [Fact]
    public async Task ForceReloadAsync_EnabledWithInvalidCron_NextRunIsNull()
    {
        var (service, _, scopeFactory) = CreateService();
        await SeedSettingsAsync(scopeFactory, new BackupSetting { IsEnabled = true, ScheduleCron = "not-a-cron" });

        await service.ForceReloadAsync();

        Assert.Null(service.GetNextScheduledRun());
    }

    // ── Firing ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ForceFireAsync_NotDue_DoesNotRunBackup()
    {
        var (service, backupService, scopeFactory) = CreateService();
        await SeedSettingsAsync(scopeFactory, new BackupSetting { IsEnabled = true, ScheduleCron = "0 0 1 1 *" });

        await service.ForceReloadAsync();
        await service.ForceFireAsync();

        Assert.Equal(0, backupService.RunCount);
    }

    [Fact]
    public async Task ForceFireAsync_DueSchedule_RunsBackupAndRecomputesNextRun()
    {
        var (service, backupService, scopeFactory) = CreateService();
        await SeedSettingsAsync(scopeFactory, new BackupSetting { IsEnabled = true, ScheduleCron = "* * * * *" });

        await service.ForceReloadAsync();
        var firstNext = service.GetNextScheduledRun();
        Assert.NotNull(firstNext);

        var waitMs = (int)(firstNext!.Value - DateTimeOffset.UtcNow).TotalMilliseconds + 200;
        if (waitMs > 0 && waitMs < 62_000)
            await Task.Delay(waitMs);

        await service.ForceFireAsync();

        Assert.True(backupService.RunCount > 0);
        Assert.NotEqual(firstNext, service.GetNextScheduledRun());
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StartsAndStopsCleanly()
    {
        var (service, _, _) = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);

        await Task.Delay(200);

        await service.StopAsync(CancellationToken.None);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════════

    private (BackupSchedulerHostedService Service, FakeBackupService BackupService, IServiceScopeFactory ScopeFactory)
        CreateService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_backup_sched_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);

        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));

        var backupService = new FakeBackupService();
        services.AddSingleton<IBackupService>(backupService);

        var sp = services.BuildServiceProvider();

        using (var initScope = sp.CreateScope())
        {
            var db = initScope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
            db.Database.Migrate();
        }

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var service = new BackupSchedulerHostedService(
            scopeFactory,
            NullLogger<BackupSchedulerHostedService>.Instance,
            tickInterval: TimeSpan.FromMilliseconds(50),
            scheduleReloadInterval: TimeSpan.FromMinutes(2));

        return (service, backupService, scopeFactory);
    }

    private static async Task SeedSettingsAsync(IServiceScopeFactory scopeFactory, BackupSetting settings)
    {
        settings.Id = 1;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        db.BackupSettings.Add(settings);
        await db.SaveChangesAsync();
    }

    private sealed class FakeBackupService : IBackupService
    {
        public int RunCount;
        public BackupResult Result = new(true, "sweeprr-backup-test.zip", 100, null);

        public Task<BackupResult> RunBackupAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref RunCount);
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<BackupHistoryEntry>> ListHistoryAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BackupHistoryEntry>>([]);
    }
}
