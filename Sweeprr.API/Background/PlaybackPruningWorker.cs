using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations.Jellyfin.WebSocket;

namespace Sweeprr.API.Background;

/// <summary>
/// Runs once daily at approximately 03:00 AM UTC.
/// Deletes PlaybackActivity rows whose UpdatedAt exceeds GlobalSettings.PlaybackHistoryRetentionDays.
/// </summary>
public sealed class PlaybackPruningWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlaybackActivityWriter _writer;
    private readonly ILogger<PlaybackPruningWorker> _logger;

    public PlaybackPruningWorker(
        IServiceScopeFactory scopeFactory,
        IPlaybackActivityWriter writer,
        ILogger<PlaybackPruningWorker> logger)
    {
        _scopeFactory   = scopeFactory;
        _writer         = writer;
        _logger         = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay startup until 03:00 AM UTC on the next calendar day.
        await WaitUntil3AmUtcAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunPruneAsync(stoppingToken);

            // Wait ~24 h for the next 03:00 AM window.
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunPruneAsync(CancellationToken ct)
    {
        try
        {
            // Read retention setting fresh each run so changes take effect immediately.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

            var settings = await db.GlobalSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == 1, ct);

            int retentionDays = settings?.PlaybackHistoryRetentionDays ?? 365;

            _logger.LogInformation("PlaybackPruningWorker: pruning records older than {Days} days", retentionDays);
            await _writer.PruneOldActivitiesAsync(retentionDays, ct);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlaybackPruningWorker: unhandled error during prune");
        }
    }

    /// <summary>Delays until the next 03:00 AM UTC moment.</summary>
    private static async Task WaitUntil3AmUtcAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var next3Am = now.Date.AddHours(3);
        if (next3Am <= now)
            next3Am = next3Am.AddDays(1);

        var delay = next3Am - now;
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
            // Cancellation during initial delay — exit gracefully.
        }
    }
}
