namespace Sweeprr.API.Dtos.Settings;

/// <summary>
/// PATCH semantics: null fields are ignored (keep existing value).
/// To explicitly DISABLE a nullable cap, set the corresponding Clear* flag to true.
/// </summary>
public sealed class UpdateSettingsRequest
{
    public string? InstanceName { get; init; }
    public bool? GlobalDryRun { get; init; }
    public string? DefaultCron { get; init; }
    public int? MaxItemsPerRun { get; init; }
    public double? MaxGbPerRun { get; init; }
    public double? PessimisticSizeGb { get; init; }
    public double? LibraryPercentCap { get; init; }
    public bool ClearLibraryPercentCap { get; init; }
    public double? OverBroadMatchPct { get; init; }
    public bool ClearOverBroadMatchPct { get; init; }

    /// <summary>
    /// When <c>true</c>, orphaned Jellyfin items (no *arr match) are deleted directly
    /// via the Jellyfin API. <c>false</c> (default) means they are skipped as Failed.
    /// </summary>
    public bool? AllowDirectJellyfinDeletion { get; init; }

    /// <summary>
    /// Enables or disables the "Leaving Soon" Jellyfin collection sync.
    /// When <c>true</c>, Pending/Approved sweep items are kept in sync with a BoxSet
    /// collection so Jellyfin users can see what content is scheduled for removal.
    /// </summary>
    public bool? LeavingSoonSyncEnabled { get; init; }

    /// <summary>
    /// When <c>true</c>, Sweeprr renders a "Leaving Soon" banner overlay on Jellyfin
    /// posters when items enter the Sweep Queue. Originals are backed up and restored
    /// automatically when items leave the queue.
    /// </summary>
    public bool? PosterOverlaysEnabled { get; init; }

    /// <summary>
    /// Filesystem directory for poster backup files. Defaults to <c>/config/poster-backups</c>.
    /// Only relevant when <c>PosterOverlaysEnabled</c> is <c>true</c>.
    /// </summary>
    public string? PosterBackupDir { get; init; }
}
