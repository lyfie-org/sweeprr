namespace Sweeprr.API.Models;

public class GlobalSettings
{
    public int Id { get; set; }
    public string InstanceName { get; set; } = "Sweeprr";
    public string JwtSecret { get; set; } = string.Empty;
    public int MaxItemsPerRun { get; set; } = 20;
    public double MaxGbPerRun { get; set; } = 50.0;
    public double PessimisticSizeGb { get; set; } = 5.0;
    public double? LibraryPercentCap { get; set; }
    public double? OverBroadMatchPct { get; set; }
    public bool GlobalDryRun { get; set; } = true;
    public string DefaultCron { get; set; } = "0 3 * * *";
    /// <summary>
    /// Number of days to retain playback activity records. Default 365.
    /// Records whose UpdatedAt is older than this threshold are pruned daily.
    /// </summary>
    public int PlaybackHistoryRetentionDays { get; set; } = 365;
}
