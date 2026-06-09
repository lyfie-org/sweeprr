using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <summary>
/// Applies and restores "Leaving Soon" poster overlays on Jellyfin item images.
/// All operations are best-effort — failures are logged but never propagated to callers.
/// </summary>
public interface IOverlayRenderingService
{
    /// <summary>
    /// Downloads the item's primary poster, backs it up to disk, renders a red gradient
    /// "Leaving Soon" banner using SkiaSharp, then uploads the modified image to Jellyfin.
    /// No-op when <c>PosterOverlaysEnabled</c> is false.
    /// </summary>
    Task ApplyOverlayAsync(SweepItem item, string labelText, CancellationToken ct);

    /// <summary>
    /// Reads the backed-up original poster from disk and uploads it back to Jellyfin,
    /// then deletes the backup file. No-op when no backup exists.
    /// </summary>
    Task RestoreOriginalAsync(SweepItem item, CancellationToken ct);
}
