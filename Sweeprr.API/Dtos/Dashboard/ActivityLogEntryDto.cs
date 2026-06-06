namespace Sweeprr.API.Dtos.Dashboard;

public sealed record ActivityLogEntryDto(
    int Id,
    DateTime Timestamp,
    string Level,
    string Category,
    string Message,
    string? MetaJson
);
