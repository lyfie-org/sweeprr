using Sweeprr.API.Integrations.Jellyfin.Models;

namespace Sweeprr.API.Services;

/// <summary>
/// Sends in-app Jellyfin session alerts (Story 10.2): per-session "now playing"
/// warnings for items queued for cleanup, and a pre-sweep broadcast to all
/// active sessions shortly before a scheduled run.
/// </summary>
public interface IJellyfinSessionAlertService
{
    /// <summary>
    /// Called whenever the Jellyfin WebSocket pushes a "Sessions" update.
    /// For each session whose NowPlayingItem is Pending/Approved in the Sweep Queue
    /// (and not within its alert cooldown), sends an in-app warning message.
    /// No-op if session alerts are disabled in GlobalSettings.
    /// </summary>
    Task ProcessSessionsUpdateAsync(
        int connectionId, IReadOnlyList<JellyfinSession> sessions, CancellationToken ct = default);

    /// <summary>
    /// Sends a "cleanup starting soon" message to every active Jellyfin session
    /// with a real user attached. No-op if pre-sweep broadcasts are disabled in
    /// GlobalSettings or no enabled Jellyfin connection exists.
    /// </summary>
    Task BroadcastPreSweepWarningAsync(CancellationToken ct = default);
}
