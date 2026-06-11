namespace Sweeprr.API.Services;

/// <summary>
/// Detects the Jellyfin Playback Reporting plugin and, when present, backfills
/// PlaybackActivity history from the plugin's aggregate usage-stats database.
/// </summary>
public interface IPlaybackReportingService
{
    /// <summary>
    /// Runs detection (and backfill, if the plugin is active) against the first
    /// enabled Jellyfin connection. No-op if no enabled Jellyfin connection exists.
    /// </summary>
    Task RunAsync(CancellationToken ct = default);
}
