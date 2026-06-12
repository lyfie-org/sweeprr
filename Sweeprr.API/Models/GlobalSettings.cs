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

    /// <summary>
    /// When <c>true</c>, items with no Radarr/Sonarr match (orphaned Jellyfin-only media)
    /// are deleted directly via the Jellyfin API instead of being skipped.
    /// <para>
    /// Default is <c>false</c>. Enabling this bypasses *arr entirely — files are permanently
    /// deleted and cannot be re-downloaded automatically.
    /// </para>
    /// </summary>
    public bool AllowDirectJellyfinDeletion { get; set; } = false;

    /// <summary>
    /// Jellyfin BoxSet collection ID for the "Sweeprr - Leaving Soon" collection.
    /// Populated automatically by <c>JellyfinCurationWarningSyncService</c> when it creates
    /// or locates the collection. Null until first sync.
    /// </summary>
    public string? LeavingSoonCollectionId { get; set; }

    /// <summary>
    /// When <c>true</c>, the "Leaving Soon" sync service keeps a Jellyfin BoxSet collection
    /// in sync with the current Sweep Queue (Pending + Approved items).
    /// Default is <c>true</c>.
    /// </summary>
    public bool LeavingSoonSyncEnabled { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, Sweeprr applies a "Leaving Soon" gradient banner overlay to Jellyfin
    /// posters when items enter the Sweep Queue. Originals are backed up before modification
    /// and restored when items are ignored, failed, or removed from the queue.
    /// Default is <c>false</c>.
    /// </summary>
    public bool PosterOverlaysEnabled { get; set; } = false;

    /// <summary>
    /// Filesystem directory where original Jellyfin poster images are backed up before
    /// overlay rendering. Backups are named <c>{jellyfinItemId}.jpg</c>.
    /// Default is <c>/config/poster-backups</c>.
    /// </summary>
    public string PosterBackupDir { get; set; } = "/config/poster-backups";

    /// <summary>
    /// When <c>true</c>, <c>JellyfinSessionAlertService</c> sends an in-app message to any
    /// active Jellyfin session whose NowPlayingItem is Pending or Approved in the Sweep
    /// Queue, warning that the item may be removed soon.
    /// Default is <c>true</c>.
    /// </summary>
    public bool JellyfinSessionAlertsEnabled { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, 10 minutes before each scheduled sweep run, a broadcast message
    /// is sent to all active Jellyfin sessions warning that cleanup is about to begin.
    /// Default is <c>true</c>.
    /// </summary>
    public bool PreSweepBroadcastEnabled { get; set; } = true;

    /// <summary>
    /// Externally-reachable base URL for this Sweeprr instance (e.g. <c>https://sweeprr.example.com</c>),
    /// used to build links embedded in poster overlays (e.g. the extension-request QR code) and
    /// other user-facing links that must work from outside the admin network. Null/empty disables
    /// these features.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}
