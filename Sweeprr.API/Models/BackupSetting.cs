namespace Sweeprr.API.Models;

/// <summary>
/// Singleton (Id = 1) configuration for automated SQLite backups (Story 11.3).
/// A snapshot of <c>/config/sweeprr.db</c> and <c>/config/keys/</c> is zipped and written
/// either to <see cref="LocalPath"/> or uploaded to an S3/MinIO-compatible bucket.
/// </summary>
public class BackupSetting
{
    public int Id { get; set; }

    /// <summary>When <c>true</c>, <c>BackupSchedulerHostedService</c> runs backups on <see cref="ScheduleCron"/>.</summary>
    public bool IsEnabled { get; set; }

    public BackupDestinationType DestinationType { get; set; } = BackupDestinationType.Local;

    /// <summary>Directory backups are written to when <see cref="DestinationType"/> is <c>Local</c>. Defaults to <c>/config/backups</c>.</summary>
    public string? LocalPath { get; set; }

    /// <summary>S3-compatible endpoint override (e.g. <c>http://minio:9000</c>). Leave null for AWS S3.</summary>
    public string? S3Endpoint { get; set; }

    public string? S3Region { get; set; }
    public string? S3Bucket { get; set; }
    public string? S3AccessKey { get; set; }

    /// <summary>S3 secret access key, encrypted via <see cref="Sweeprr.API.Services.ISecretProtector"/>.</summary>
    public string? S3SecretKeyEncrypted { get; set; }

    /// <summary>Number of most-recent backups to retain; older backups are deleted after each run.</summary>
    public int RetentionCount { get; set; } = 5;

    /// <summary>Cron expression for scheduled backups. Default: Sunday at 3 AM.</summary>
    public string ScheduleCron { get; set; } = "0 3 * * 0";
}
