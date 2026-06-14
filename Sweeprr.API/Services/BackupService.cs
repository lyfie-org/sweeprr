using System.IO.Compression;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <summary>
/// Implements Story 11.3: snapshots the SQLite database (via <c>VACUUM INTO</c> for WAL
/// consistency) plus the Data Protection key ring, zips them together, and writes the
/// archive to either a local directory or an S3/MinIO-compatible bucket. Retention is
/// enforced by deleting the oldest backups beyond <see cref="BackupSetting.RetentionCount"/>.
/// </summary>
public sealed class BackupService : IBackupService
{
    private const string FilePrefix = "sweeprr-backup-";
    private const string FileExtension = ".zip";

    private readonly SweeprrDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly string _configDir;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        SweeprrDbContext db,
        ISecretProtector protector,
        IConfiguration configuration,
        ILogger<BackupService> logger)
    {
        _db = db;
        _protector = protector;
        _configDir = configuration["ConfigDir"] ?? "/config";
        _logger = logger;
    }

    public async Task<BackupResult> RunBackupAsync(CancellationToken ct = default)
    {
        var settings = await _db.BackupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is null)
            return new BackupResult(false, null, null, "Backup settings not initialized.");

        var filename = $"{FilePrefix}{DateTime.UtcNow:yyyy-MM-dd-HHmmss}{FileExtension}";
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"sweeprr-vacuum-{Guid.NewGuid()}.db");

        try
        {
            await SnapshotDatabaseAsync(tempDbPath, ct);

            using var zipStream = new MemoryStream();
            BuildArchive(zipStream, tempDbPath);
            var sizeBytes = zipStream.Length;

            var result = settings.DestinationType == BackupDestinationType.S3
                ? await UploadToS3Async(settings, filename, zipStream, sizeBytes, ct)
                : await WriteLocalAsync(settings, filename, zipStream, sizeBytes, ct);

            await LogActivityAsync(
                result.Success ? ActivityLogLevel.Information : ActivityLogLevel.Error,
                result.Success
                    ? $"Backup completed: {result.Filename} ({result.SizeBytes:N0} bytes)"
                    : $"Backup failed: {result.Error}",
                ct);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed");
            await LogActivityAsync(ActivityLogLevel.Error, $"Backup failed: {ex.Message}", ct);
            return new BackupResult(false, null, null, ex.Message);
        }
        finally
        {
            if (File.Exists(tempDbPath))
                File.Delete(tempDbPath);
        }
    }

    public async Task<IReadOnlyList<BackupHistoryEntry>> ListHistoryAsync(CancellationToken ct = default)
    {
        var settings = await _db.BackupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is null)
            return [];

        return settings.DestinationType == BackupDestinationType.S3
            ? await ListS3HistoryAsync(settings, ct)
            : ListLocalHistory(settings);
    }

    // ── Snapshot + archive ──────────────────────────────────────────────────

    private async Task SnapshotDatabaseAsync(string tempDbPath, CancellationToken ct)
    {
        // Flush the WAL into the main db file so the VACUUM INTO copy is self-contained.
        await _db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", ct);

        await _db.Database.ExecuteSqlInterpolatedAsync($"VACUUM INTO {tempDbPath}", ct);
    }

    private void BuildArchive(MemoryStream zipStream, string tempDbPath)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);

        archive.CreateEntryFromFile(tempDbPath, "sweeprr.db", CompressionLevel.Optimal);

        var keysDir = Path.Combine(_configDir, "keys");
        if (!Directory.Exists(keysDir))
            return;

        foreach (var file in Directory.GetFiles(keysDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(keysDir, file).Replace(Path.DirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(file, $"keys/{relative}", CompressionLevel.Optimal);
        }
    }

    // ── Local destination ───────────────────────────────────────────────────

    private async Task<BackupResult> WriteLocalAsync(
        BackupSetting settings, string filename, MemoryStream zipStream, long sizeBytes, CancellationToken ct)
    {
        var localPath = ResolveLocalPath(settings);
        Directory.CreateDirectory(localPath);

        zipStream.Position = 0;
        var fullPath = Path.Combine(localPath, filename);
        await using (var fileStream = File.Create(fullPath))
        {
            await zipStream.CopyToAsync(fileStream, ct);
        }

        PruneLocalRetention(localPath, settings.RetentionCount);

        return new BackupResult(true, filename, sizeBytes, null);
    }

    private List<BackupHistoryEntry> ListLocalHistory(BackupSetting settings)
    {
        var localPath = ResolveLocalPath(settings);
        if (!Directory.Exists(localPath))
            return [];

        return Directory.GetFiles(localPath, $"{FilePrefix}*{FileExtension}")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupHistoryEntry(f.Name, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
            .ToList();
    }

    private static void PruneLocalRetention(string localPath, int retentionCount)
    {
        var stale = Directory.GetFiles(localPath, $"{FilePrefix}*{FileExtension}")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(Math.Max(retentionCount, 0));

        foreach (var file in stale)
            file.Delete();
    }

    private string ResolveLocalPath(BackupSetting settings) =>
        string.IsNullOrWhiteSpace(settings.LocalPath)
            ? Path.Combine(_configDir, "backups")
            : settings.LocalPath;

    // ── S3 destination ──────────────────────────────────────────────────────

    private async Task<BackupResult> UploadToS3Async(
        BackupSetting settings, string filename, MemoryStream zipStream, long sizeBytes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.S3Bucket))
            return new BackupResult(false, null, null, "S3 bucket is not configured.");

        if (string.IsNullOrWhiteSpace(settings.S3AccessKey) || string.IsNullOrEmpty(settings.S3SecretKeyEncrypted))
            return new BackupResult(false, null, null, "S3 access key/secret key are not configured.");

        var secretKey = _protector.Unprotect(settings.S3SecretKeyEncrypted);
        if (string.IsNullOrEmpty(secretKey))
            return new BackupResult(false, null, null, "Stored S3 secret key could not be decrypted.");

        using var client = CreateS3Client(settings, secretKey);

        zipStream.Position = 0;
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = settings.S3Bucket,
            Key = filename,
            InputStream = zipStream,
            AutoCloseStream = false,
        }, ct);

        await PruneS3RetentionAsync(client, settings.S3Bucket, settings.RetentionCount, ct);

        return new BackupResult(true, filename, sizeBytes, null);
    }

    private async Task<List<BackupHistoryEntry>> ListS3HistoryAsync(BackupSetting settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.S3Bucket)
            || string.IsNullOrWhiteSpace(settings.S3AccessKey)
            || string.IsNullOrEmpty(settings.S3SecretKeyEncrypted))
        {
            return [];
        }

        var secretKey = _protector.Unprotect(settings.S3SecretKeyEncrypted);
        if (string.IsNullOrEmpty(secretKey))
            return [];

        using var client = CreateS3Client(settings, secretKey);
        var response = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = settings.S3Bucket }, ct);

        return response.S3Objects
            .Where(o => o.Key.StartsWith(FilePrefix, StringComparison.Ordinal) && o.Key.EndsWith(FileExtension, StringComparison.Ordinal))
            .OrderByDescending(o => o.LastModified)
            .Select(o => new BackupHistoryEntry(
                o.Key,
                o.Size,
                new DateTimeOffset(DateTime.SpecifyKind(o.LastModified, DateTimeKind.Utc))))
            .ToList();
    }

    private static async Task PruneS3RetentionAsync(IAmazonS3 client, string bucket, int retentionCount, CancellationToken ct)
    {
        var response = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket }, ct);

        var stale = response.S3Objects
            .Where(o => o.Key.StartsWith(FilePrefix, StringComparison.Ordinal) && o.Key.EndsWith(FileExtension, StringComparison.Ordinal))
            .OrderByDescending(o => o.LastModified)
            .Skip(Math.Max(retentionCount, 0));

        foreach (var obj in stale)
            await client.DeleteObjectAsync(bucket, obj.Key, ct);
    }

    private static IAmazonS3 CreateS3Client(BackupSetting settings, string secretKey)
    {
        var config = new AmazonS3Config();

        if (!string.IsNullOrWhiteSpace(settings.S3Endpoint))
        {
            config.ServiceURL = settings.S3Endpoint;
            config.ForcePathStyle = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.S3Region))
            config.AuthenticationRegion = settings.S3Region;

        return new AmazonS3Client(new BasicAWSCredentials(settings.S3AccessKey, secretKey), config);
    }

    // ── Activity log ────────────────────────────────────────────────────────

    private async Task LogActivityAsync(ActivityLogLevel level, string message, CancellationToken ct)
    {
        _db.ActivityLogs.Add(new ActivityLog
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = ActivityLogCategory.Backup,
            Message = message,
        });

        await _db.SaveChangesAsync(ct);
    }
}
