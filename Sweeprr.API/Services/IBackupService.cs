namespace Sweeprr.API.Services;

public interface IBackupService
{
    /// <summary>Snapshots the database + Data Protection keys, writes to Local or S3, and prunes old backups.</summary>
    Task<BackupResult> RunBackupAsync(CancellationToken ct = default);

    /// <summary>Lists existing backups at the configured destination, newest first.</summary>
    Task<IReadOnlyList<BackupHistoryEntry>> ListHistoryAsync(CancellationToken ct = default);
}

public sealed record BackupResult(bool Success, string? Filename, long? SizeBytes, string? Error);

public sealed record BackupHistoryEntry(string Filename, long SizeBytes, DateTimeOffset CreatedAt);
