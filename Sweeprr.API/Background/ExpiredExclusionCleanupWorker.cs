using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;

namespace Sweeprr.API.Background;

/// <summary>
/// Runs once daily. Removes Exclusion rows whose ExpiresAt has passed.
/// Permanent exclusions (ExpiresAt = null) are never touched.
/// </summary>
public sealed class ExpiredExclusionCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredExclusionCleanupWorker> _logger;

    public ExpiredExclusionCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredExclusionCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCleanupAsync(stoppingToken);

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

            var now = DateTime.UtcNow;
            var deleted = await db.Exclusions
                .Where(e => e.ExpiresAt != null && e.ExpiresAt <= now)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation("ExpiredExclusionCleanupWorker: removed {Count} expired exclusion(s)", deleted);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExpiredExclusionCleanupWorker: unhandled error during cleanup");
        }
    }
}
