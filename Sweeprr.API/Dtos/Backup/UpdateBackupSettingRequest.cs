using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Backup;

/// <summary>Partial update — null fields are left unchanged. <c>S3SecretKey</c> omitted/empty keeps the existing stored secret.</summary>
public record UpdateBackupSettingRequest(
    bool? IsEnabled,
    BackupDestinationType? DestinationType,
    string? LocalPath,
    string? S3Endpoint,
    string? S3Region,
    string? S3Bucket,
    string? S3AccessKey,
    string? S3SecretKey,
    int? RetentionCount,
    string? ScheduleCron);
