namespace Sweeprr.API.Dtos.Backup;

public record TriggerBackupResponse(bool Success, string? Filename, long? SizeKb, string? Error);
