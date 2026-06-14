using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Data;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Services;

public class BackupServiceTests : IDisposable
{
    private readonly SweeprrDbContext _db;
    private readonly string _dbPath;
    private readonly string _tempConfigDir;
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_backup_svc_{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _db = new SweeprrDbContext(options);
        _db.Database.Migrate();

        _tempConfigDir = Path.Combine(Path.GetTempPath(), $"sweeprr_backup_cfg_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempConfigDir);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConfigDir"] = _tempConfigDir })
            .Build();

        _service = new BackupService(_db, new PrefixSecretProtector(), config, NullLogger<BackupService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { if (File.Exists(path)) File.Delete(path); } catch { }

        try { if (Directory.Exists(_tempConfigDir)) Directory.Delete(_tempConfigDir, true); } catch { }
    }

    private async Task SeedSettingsAsync(BackupSetting settings)
    {
        settings.Id = 1;
        _db.BackupSettings.Add(settings);
        await _db.SaveChangesAsync();
    }

    // ── RunBackupAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunBackupAsync_NoSettings_ReturnsFailure()
    {
        var result = await _service.RunBackupAsync();

        Assert.False(result.Success);
        Assert.Equal("Backup settings not initialized.", result.Error);
    }

    [Fact]
    public async Task RunBackupAsync_LocalDestination_WritesZipAndLogsActivity()
    {
        await SeedSettingsAsync(new BackupSetting { DestinationType = BackupDestinationType.Local });

        var result = await _service.RunBackupAsync();

        Assert.True(result.Success);
        Assert.NotNull(result.Filename);
        Assert.StartsWith("sweeprr-backup-", result.Filename);
        Assert.EndsWith(".zip", result.Filename);
        Assert.True(result.SizeBytes > 0);

        var backupPath = Path.Combine(_tempConfigDir, "backups", result.Filename!);
        Assert.True(File.Exists(backupPath));

        var logs = await _db.ActivityLogs.Where(l => l.Category == ActivityLogCategory.Backup).ToListAsync();
        Assert.Single(logs);
        Assert.Equal(ActivityLogLevel.Information, logs[0].Level);
        Assert.Contains("Backup completed", logs[0].Message);
    }

    [Fact]
    public async Task RunBackupAsync_LocalDestination_CustomPath_WritesToConfiguredDirectory()
    {
        var customPath = Path.Combine(_tempConfigDir, "custom-backups");
        await SeedSettingsAsync(new BackupSetting { DestinationType = BackupDestinationType.Local, LocalPath = customPath });

        var result = await _service.RunBackupAsync();

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(customPath, result.Filename!)));
    }

    [Fact]
    public async Task RunBackupAsync_PrunesOldBackupsBeyondRetention()
    {
        await SeedSettingsAsync(new BackupSetting { DestinationType = BackupDestinationType.Local, RetentionCount = 2 });

        var backupsDir = Path.Combine(_tempConfigDir, "backups");
        Directory.CreateDirectory(backupsDir);

        // 3 pre-existing backups with distinct timestamps, oldest (-3h) to newest (-1h).
        for (var i = 0; i < 3; i++)
        {
            var path = Path.Combine(backupsDir, $"sweeprr-backup-seed-{i}.zip");
            await File.WriteAllTextAsync(path, "dummy");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-3 + i));
        }

        var result = await _service.RunBackupAsync();
        Assert.True(result.Success);

        var remaining = Directory.GetFiles(backupsDir, "sweeprr-backup-*.zip").Select(Path.GetFileName).ToList();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(result.Filename, remaining);
        Assert.Contains("sweeprr-backup-seed-2.zip", remaining); // most recent of the seeds
        Assert.DoesNotContain("sweeprr-backup-seed-1.zip", remaining);
        Assert.DoesNotContain("sweeprr-backup-seed-0.zip", remaining);
    }

    // ── ListHistoryAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListHistoryAsync_NoSettings_ReturnsEmpty()
    {
        var history = await _service.ListHistoryAsync();
        Assert.Empty(history);
    }

    [Fact]
    public async Task ListHistoryAsync_LocalDestination_NoBackupsDir_ReturnsEmpty()
    {
        await SeedSettingsAsync(new BackupSetting { DestinationType = BackupDestinationType.Local });

        var history = await _service.ListHistoryAsync();
        Assert.Empty(history);
    }

    [Fact]
    public async Task ListHistoryAsync_LocalDestination_ReturnsNewestFirst()
    {
        await SeedSettingsAsync(new BackupSetting { DestinationType = BackupDestinationType.Local });

        var backupsDir = Path.Combine(_tempConfigDir, "backups");
        Directory.CreateDirectory(backupsDir);

        var older = Path.Combine(backupsDir, "sweeprr-backup-2026-06-01-030000.zip");
        var newer = Path.Combine(backupsDir, "sweeprr-backup-2026-06-08-030000.zip");
        await File.WriteAllTextAsync(older, "old-data");
        await File.WriteAllTextAsync(newer, "newer-data-longer");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-7));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var history = await _service.ListHistoryAsync();

        Assert.Equal(2, history.Count);
        Assert.Equal("sweeprr-backup-2026-06-08-030000.zip", history[0].Filename);
        Assert.Equal("sweeprr-backup-2026-06-01-030000.zip", history[1].Filename);
        Assert.Equal(new FileInfo(newer).Length, history[0].SizeBytes);
    }

    // ── S3 destination — config validation (no live S3/MinIO required) ──────

    [Fact]
    public async Task RunBackupAsync_S3Destination_MissingBucket_ReturnsFailure()
    {
        await SeedSettingsAsync(new BackupSetting { DestinationType = BackupDestinationType.S3 });

        var result = await _service.RunBackupAsync();

        Assert.False(result.Success);
        Assert.Equal("S3 bucket is not configured.", result.Error);
    }

    [Fact]
    public async Task RunBackupAsync_S3Destination_MissingCredentials_ReturnsFailure()
    {
        await SeedSettingsAsync(new BackupSetting { DestinationType = BackupDestinationType.S3, S3Bucket = "sweeprr-backups" });

        var result = await _service.RunBackupAsync();

        Assert.False(result.Success);
        Assert.Equal("S3 access key/secret key are not configured.", result.Error);
    }

    [Fact]
    public async Task RunBackupAsync_S3Destination_UndecryptableSecret_ReturnsFailure()
    {
        await SeedSettingsAsync(new BackupSetting
        {
            DestinationType = BackupDestinationType.S3,
            S3Bucket = "sweeprr-backups",
            S3AccessKey = "AKIAEXAMPLE",
            S3SecretKeyEncrypted = "not-encrypted-by-us",
        });

        var result = await _service.RunBackupAsync();

        Assert.False(result.Success);
        Assert.Equal("Stored S3 secret key could not be decrypted.", result.Error);
    }

    [Fact]
    public async Task ListHistoryAsync_S3Destination_MissingCredentials_ReturnsEmpty()
    {
        await SeedSettingsAsync(new BackupSetting { DestinationType = BackupDestinationType.S3 });

        var history = await _service.ListHistoryAsync();
        Assert.Empty(history);
    }

    // ── Test infrastructure ──────────────────────────────────────────────────

    /// <summary>Round-trips via an "enc:" prefix; returns null for anything not produced by Protect — mirrors the "unreadable secret" path.</summary>
    private sealed class PrefixSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => $"enc:{plaintext}";
        public string? Unprotect(string ciphertext) => ciphertext.StartsWith("enc:") ? ciphertext[4..] : null;
    }
}
