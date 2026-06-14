using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Backup;

public record BackupSettingResponse(
    bool IsEnabled,
    BackupDestinationType DestinationType,
    string? LocalPath,
    string? S3Endpoint,
    string? S3Region,
    string? S3Bucket,
    string? S3AccessKey,
    string? MaskedS3SecretKey,
    int RetentionCount,
    string ScheduleCron,
    DateTimeOffset? NextScheduledRun);
