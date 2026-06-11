using Sweeprr.API.Services;

namespace Sweeprr.API.Background;

/// <summary>
/// Runs Jellyfin Playback Reporting plugin detection + backfill (Story 10.1) shortly
/// after startup (treated as "first connect"), then once daily.
/// </summary>
public sealed class PlaybackReportingBackfillWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlaybackReportingBackfillWorker> _logger;

    public PlaybackReportingBackfillWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PlaybackReportingBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunAsync(stoppingToken);

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IPlaybackReportingService>();
            await service.RunAsync(ct);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlaybackReportingBackfillWorker: unhandled error during run");
        }
    }
}
