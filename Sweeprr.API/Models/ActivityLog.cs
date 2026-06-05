namespace Sweeprr.API.Models;

public class ActivityLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public ActivityLogLevel Level { get; set; } = ActivityLogLevel.Information;
    public ActivityLogCategory Category { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? MetaJson { get; set; }
}
