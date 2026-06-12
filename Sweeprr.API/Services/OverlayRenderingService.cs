using Microsoft.EntityFrameworkCore;
using QRCoder;
using SkiaSharp;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

public sealed class OverlayRenderingService : IOverlayRenderingService
{
    private readonly SweeprrDbContext _db;
    private readonly IIntegrationClientFactory _clientFactory;
    private readonly ILogger<OverlayRenderingService> _logger;

    public OverlayRenderingService(
        SweeprrDbContext db,
        IIntegrationClientFactory clientFactory,
        ILogger<OverlayRenderingService> logger)
    {
        _db            = db;
        _clientFactory = clientFactory;
        _logger        = logger;
    }

    public async Task ApplyOverlayAsync(SweepItem item, string labelText, CancellationToken ct)
    {
        try
        {
            var settings = await LoadSettingsAsync(ct);
            if (!settings.PosterOverlaysEnabled) return;

            var client = await ResolveJellyfinClientAsync(ct);
            if (client is null) return;

            var originalBytes = await client.DownloadPosterAsync(item.MediaServerItemId, ct);
            if (originalBytes is null || originalBytes.Length == 0)
            {
                _logger.LogWarning("Overlay skipped for '{Title}': could not download poster", item.Title);
                return;
            }

            var backupDir = settings.PosterBackupDir;
            Directory.CreateDirectory(backupDir);

            var backupPath = BackupPath(backupDir, item.MediaServerItemId);
            await File.WriteAllBytesAsync(backupPath, originalBytes, ct);

            var extendUrl = BuildExtendUrl(settings.PublicBaseUrl, item.MediaServerItemId);
            var overlayBytes = RenderOverlay(originalBytes, labelText, extendUrl);
            if (overlayBytes is null)
            {
                _logger.LogWarning("Overlay rendering failed for '{Title}' — backup retained", item.Title);
                return;
            }

            var uploaded = await client.UploadPosterAsync(item.MediaServerItemId, overlayBytes, ct);
            if (!uploaded)
                _logger.LogWarning("Overlay upload failed for '{Title}'", item.Title);
            else
                _logger.LogInformation("Poster overlay applied for '{Title}'", item.Title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Poster overlay apply failed for '{Title}' — continuing", item.Title);
        }
    }

    public async Task RestoreOriginalAsync(SweepItem item, CancellationToken ct)
    {
        try
        {
            var settings  = await LoadSettingsAsync(ct);
            var backupDir = settings.PosterBackupDir;
            var backupPath = BackupPath(backupDir, item.MediaServerItemId);

            if (!File.Exists(backupPath)) return;

            var originalBytes = await File.ReadAllBytesAsync(backupPath, ct);

            var client = await ResolveJellyfinClientAsync(ct);
            if (client is null)
            {
                _logger.LogWarning("Overlay restore skipped for '{Title}': no Jellyfin client", item.Title);
                return;
            }

            var uploaded = await client.UploadPosterAsync(item.MediaServerItemId, originalBytes, ct);
            if (uploaded)
            {
                File.Delete(backupPath);
                _logger.LogInformation("Poster overlay restored for '{Title}'", item.Title);
            }
            else
            {
                _logger.LogWarning("Overlay restore upload failed for '{Title}' — backup retained", item.Title);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Poster overlay restore failed for '{Title}' — continuing", item.Title);
        }
    }

    // ── SkiaSharp rendering ──────────────────────────────────────────────────

    internal static byte[]? RenderOverlay(byte[] originalBytes, string labelText, string? extendUrl)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(originalBytes);
            if (bitmap is null) return null;

            using var canvas = new SKCanvas(bitmap);

            // Red gradient banner occupying the bottom 18% of the image
            var bannerTop    = bitmap.Height * 0.82f;
            var bannerRect   = new SKRect(0, bannerTop, bitmap.Width, bitmap.Height);
            var bannerHeight = bannerRect.Height;

            using var gradientPaint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, bannerRect.Top),
                    new SKPoint(0, bannerRect.Bottom),
                    [new SKColor(180, 30, 30, 200), new SKColor(120, 10, 10, 230)],
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(bannerRect, gradientPaint);

            // Optional QR code in the banner's right edge, linking to the extension-request page.
            // Reserves horizontal space so the label text below can shrink to avoid overlap.
            var qrAreaWidth = 0f;
            if (extendUrl is not null)
            {
                var padding = bitmap.Width * 0.02f;
                var qrSize  = Math.Min(bitmap.Width * 0.16f, bannerHeight - padding * 2);

                if (qrSize >= 40)
                {
                    using var qrBitmap = TryGenerateQrBitmap(extendUrl);
                    if (qrBitmap is not null)
                    {
                        var qrRect = new SKRect(
                            bitmap.Width - qrSize - padding,
                            bannerTop + (bannerHeight - qrSize) / 2,
                            bitmap.Width - padding,
                            bannerTop + (bannerHeight - qrSize) / 2 + qrSize);

                        // White backing so the QR code stays scannable against the red banner
                        using var whitePaint = new SKPaint { Color = SKColors.White };
                        canvas.DrawRect(qrRect, whitePaint);

                        // Nearest-neighbor scaling keeps QR module edges crisp (no blur)
                        using var qrPaint = new SKPaint { FilterQuality = SKFilterQuality.None };
                        canvas.DrawBitmap(qrBitmap, qrRect, qrPaint);

                        qrAreaWidth = qrSize + padding * 2;
                    }
                }
            }

            // Text: bold, white, ~7% of image width in size
            using var textPaint = new SKPaint
            {
                Color     = SKColors.White,
                IsAntialias = true,
            };
            using var font = new SKFont(
                SKTypeface.FromFamilyName(
                    "sans-serif",
                    SKFontStyleWeight.Bold,
                    SKFontStyleWidth.Normal,
                    SKFontStyleSlant.Upright),
                size: bitmap.Width * 0.07f);

            var textX = bitmap.Width * 0.05f;
            var maxTextWidth = bitmap.Width - textX - qrAreaWidth;
            if (maxTextWidth > 0)
            {
                using var measurePaint = new SKPaint { Typeface = font.Typeface, TextSize = font.Size };
                var textWidth = measurePaint.MeasureText(labelText);
                if (textWidth > maxTextWidth)
                    font.Size *= maxTextWidth / textWidth;
            }

            canvas.DrawText(
                labelText,
                textX,
                bitmap.Height * 0.94f,
                font,
                textPaint);

            using var image = SKImage.FromBitmap(bitmap);
            using var data  = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            return data.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generates a QR code for <paramref name="url"/> as an SKBitmap, or null if generation fails.
    /// </summary>
    internal static SKBitmap? TryGenerateQrBitmap(string url)
    {
        try
        {
            var generator = new QRCodeGenerator();
            using var qrData = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var pngBytes = new PngByteQRCode(qrData).GetGraphic(10);
            return SKBitmap.Decode(pngBytes);
        }
        catch
        {
            return null;
        }
    }

    private static string? BuildExtendUrl(string? publicBaseUrl, string mediaServerItemId)
        => string.IsNullOrWhiteSpace(publicBaseUrl)
            ? null
            : $"{publicBaseUrl.TrimEnd('/')}/extend?itemId={Uri.EscapeDataString(mediaServerItemId)}";

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<GlobalSettings> LoadSettingsAsync(CancellationToken ct)
        => await _db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct)
           ?? new GlobalSettings();

    private async Task<Integrations.Jellyfin.JellyfinClient?> ResolveJellyfinClientAsync(CancellationToken ct)
    {
        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Jellyfin && c.IsEnabled, ct);

        if (conn is null) return null;
        return await _clientFactory.CreateJellyfinClientAsync(conn.Id, ct);
    }

    private static string BackupPath(string backupDir, string itemId)
        => Path.Combine(backupDir, $"{itemId}.jpg");
}
