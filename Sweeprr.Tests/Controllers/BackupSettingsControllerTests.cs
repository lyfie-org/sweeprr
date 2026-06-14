using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Background;
using Sweeprr.API.Controllers;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Backup;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Controllers;

public class BackupSettingsControllerTests : IDisposable
{
    private readonly SweeprrDbContext _db;
    private readonly string _dbPath;
    private readonly ServiceProvider _schedulerProvider;
    private readonly BackupSchedulerHostedService _scheduler;
    private readonly FakeBackupService _backupService = new();
    private readonly BackupSettingsController _controller;

    public BackupSettingsControllerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_backup_settings_{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _db = new SweeprrDbContext(options);
        _db.Database.Migrate();

        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(opt => opt.UseSqlite($"Data Source={_dbPath}"));
        _schedulerProvider = services.BuildServiceProvider();

        _scheduler = new BackupSchedulerHostedService(
            _schedulerProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BackupSchedulerHostedService>.Instance);

        _controller = new BackupSettingsController(_db, new PrefixSecretProtector(), _backupService, _scheduler);
    }

    public void Dispose()
    {
        _db.Dispose();
        _schedulerProvider.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private async Task SeedAsync(BackupSetting? settings = null)
    {
        settings ??= new BackupSetting();
        settings.Id = 1;
        _db.BackupSettings.Add(settings);
        await _db.SaveChangesAsync();
    }

    // ── Get ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsSeededDefaults()
    {
        await SeedAsync();

        var result = await _controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<BackupSettingResponse>(ok.Value);

        Assert.False(response.IsEnabled);
        Assert.Equal(BackupDestinationType.Local, response.DestinationType);
        Assert.Equal(5, response.RetentionCount);
        Assert.Equal("0 3 * * 0", response.ScheduleCron);
        Assert.Null(response.MaskedS3SecretKey);
    }

    [Fact]
    public async Task Get_NotInitialized_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _controller.Get(CancellationToken.None));
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_PartialUpdate_AppliesOnlyProvidedFields()
    {
        await SeedAsync();

        var result = await _controller.Update(new UpdateBackupSettingRequest(
            IsEnabled: true,
            DestinationType: null,
            LocalPath: null,
            S3Endpoint: null,
            S3Region: null,
            S3Bucket: null,
            S3AccessKey: null,
            S3SecretKey: null,
            RetentionCount: 10,
            ScheduleCron: "0 4 * * *"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<BackupSettingResponse>(ok.Value);

        Assert.True(response.IsEnabled);
        Assert.Equal(BackupDestinationType.Local, response.DestinationType);
        Assert.Equal(10, response.RetentionCount);
        Assert.Equal("0 4 * * *", response.ScheduleCron);
    }

    [Fact]
    public async Task Update_SetsS3SecretKey_EncryptsAndMasksResponse()
    {
        await SeedAsync();

        var result = await _controller.Update(new UpdateBackupSettingRequest(
            null, BackupDestinationType.S3, null, null, null, "sweeprr-backups", "AKIAEXAMPLE", "supersecret1234", null, null),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<BackupSettingResponse>(ok.Value);

        Assert.Equal(BackupDestinationType.S3, response.DestinationType);
        Assert.Equal("sweeprr-backups", response.S3Bucket);
        Assert.Equal("AKIAEXAMPLE", response.S3AccessKey);
        Assert.Equal("···1234", response.MaskedS3SecretKey);

        var entity = await _db.BackupSettings.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal("enc:supersecret1234", entity.S3SecretKeyEncrypted);
    }

    [Fact]
    public async Task Update_EmptyS3SecretKey_PreservesExistingSecret()
    {
        await SeedAsync(new BackupSetting { S3SecretKeyEncrypted = "enc:original-secret" });

        await _controller.Update(new UpdateBackupSettingRequest(
            null, null, null, null, null, null, null, null, null, null), CancellationToken.None);

        var entity = await _db.BackupSettings.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal("enc:original-secret", entity.S3SecretKeyEncrypted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Update_InvalidRetentionCount_ReturnsBadRequest(int retentionCount)
    {
        await SeedAsync();

        var result = await _controller.Update(new UpdateBackupSettingRequest(
            null, null, null, null, null, null, null, null, retentionCount, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);

        var entity = await _db.BackupSettings.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal(5, entity.RetentionCount);
    }

    [Fact]
    public async Task Update_InvalidScheduleCron_ReturnsBadRequest()
    {
        await SeedAsync();

        var result = await _controller.Update(new UpdateBackupSettingRequest(
            null, null, null, null, null, null, null, null, null, "not-a-cron"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);

        var entity = await _db.BackupSettings.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal("0 3 * * 0", entity.ScheduleCron);
    }

    [Fact]
    public async Task Update_ReloadsScheduler_NextRunReflectsNewCron()
    {
        await SeedAsync();
        Assert.Null(_scheduler.GetNextScheduledRun());

        await _controller.Update(new UpdateBackupSettingRequest(
            true, null, null, null, null, null, null, null, null, "* * * * *"), CancellationToken.None);

        Assert.NotNull(_scheduler.GetNextScheduledRun());
    }

    // ── Trigger ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Trigger_SuccessResult_ReturnsSizeInKb()
    {
        await SeedAsync();
        _backupService.Result = new BackupResult(true, "sweeprr-backup-2026-06-13-120000.zip", 2048, null);

        var result = await _controller.Trigger(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TriggerBackupResponse>(ok.Value);

        Assert.True(response.Success);
        Assert.Equal("sweeprr-backup-2026-06-13-120000.zip", response.Filename);
        Assert.Equal((long?)2, response.SizeKb);
        Assert.Null(response.Error);
    }

    [Fact]
    public async Task Trigger_FailureResult_ReturnsError()
    {
        await SeedAsync();
        _backupService.Result = new BackupResult(false, null, null, "boom");

        var result = await _controller.Trigger(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TriggerBackupResponse>(ok.Value);

        Assert.False(response.Success);
        Assert.Null(response.Filename);
        Assert.Null(response.SizeKb);
        Assert.Equal("boom", response.Error);
    }

    // ── History ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_ReturnsBackupServiceResult()
    {
        _backupService.History = [new BackupHistoryEntry("sweeprr-backup-test.zip", 1024, DateTimeOffset.UtcNow)];

        var result = await _controller.GetHistory(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var history = Assert.IsAssignableFrom<IReadOnlyList<BackupHistoryEntry>>(ok.Value);

        Assert.Single(history);
        Assert.Equal("sweeprr-backup-test.zip", history[0].Filename);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Round-trips via an "enc:" prefix; returns null for anything not produced by Protect — mirrors the "unreadable secret" path.</summary>
    private sealed class PrefixSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => $"enc:{plaintext}";
        public string? Unprotect(string ciphertext) => ciphertext.StartsWith("enc:") ? ciphertext[4..] : null;
    }

    private sealed class FakeBackupService : IBackupService
    {
        public BackupResult Result = new(true, "sweeprr-backup-test.zip", 1024, null);
        public IReadOnlyList<BackupHistoryEntry> History = [];

        public Task<BackupResult> RunBackupAsync(CancellationToken ct = default) => Task.FromResult(Result);

        public Task<IReadOnlyList<BackupHistoryEntry>> ListHistoryAsync(CancellationToken ct = default) => Task.FromResult(History);
    }
}
