using Cronos;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.API.Background;

/// <summary>
/// Runs scheduled SQLite backups (Story 11.3) based on the singleton
/// <see cref="BackupSetting"/> row's <see cref="BackupSetting.ScheduleCron"/>.
/// </summary>
public sealed class BackupSchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupSchedulerHostedService> _logger;

    private readonly TimeSpan _tickInterval;
    private readonly TimeSpan _scheduleReloadInterval;

    private CronExpression? _cron;
    private DateTimeOffset? _nextFire;
    private DateTimeOffset _lastScheduleLoad = DateTimeOffset.MinValue;

    public BackupSchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<BackupSchedulerHostedService> logger,
        TimeSpan? tickInterval = null,
        TimeSpan? scheduleReloadInterval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _tickInterval = tickInterval ?? TimeSpan.FromSeconds(30);
        _scheduleReloadInterval = scheduleReloadInterval ?? TimeSpan.FromMinutes(2);
    }

    // ── Test hooks ────────────────────────────────────────────────────────

    internal async Task ForceReloadAsync(CancellationToken ct = default)
        => await LoadScheduleAsync(ct);

    internal async Task ForceFireAsync(CancellationToken ct = default)
        => await FireIfDueAsync(ct);

    internal async Task ForceTickAsync(CancellationToken ct = default)
    {
        await LoadScheduleAsync(ct);
        await FireIfDueAsync(ct);
    }

    // ── Public accessors ─────────────────────────────────────────────────

    public DateTimeOffset? GetNextScheduledRun() => _nextFire;

    // ── Background loop ─────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backup scheduler started");

        await LoadScheduleAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - _lastScheduleLoad > _scheduleReloadInterval)
                    await LoadScheduleAsync(stoppingToken);

                await FireIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup scheduler tick error");
            }

            try
            {
                await Task.Delay(_tickInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Backup scheduler stopped");
    }

    // ── Schedule loading ────────────────────────────────────────────────────

    private async Task LoadScheduleAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

            var settings = await db.BackupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
            _lastScheduleLoad = DateTimeOffset.UtcNow;

            if (settings is null || !settings.IsEnabled)
            {
                _cron = null;
                _nextFire = null;
                return;
            }

            _cron = TryParseCron(settings.ScheduleCron);
            if (_cron is null)
            {
                _nextFire = null;
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var next = _cron.GetNextOccurrence(now.UtcDateTime, inclusive: false);
            _nextFire = next.HasValue ? new DateTimeOffset(next.Value, TimeSpan.Zero) : null;

            _logger.LogDebug("Backup schedule loaded — next run at {NextFire}", _nextFire);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to reload backup schedule — will retry next tick");
        }
    }

    // ── Fire due job ────────────────────────────────────────────────────────

    private async Task FireIfDueAsync(CancellationToken ct)
    {
        if (_cron is null || _nextFire is null) return;

        var now = DateTimeOffset.UtcNow;
        if (_nextFire > now) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();

            _logger.LogInformation("Scheduled backup starting");
            var result = await backupService.RunBackupAsync(ct);

            if (result.Success)
                _logger.LogInformation("Scheduled backup completed: {Filename} ({SizeBytes} bytes)", result.Filename, result.SizeBytes);
            else
                _logger.LogError("Scheduled backup failed: {Error}", result.Error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled backup failed");
        }
        finally
        {
            var next = _cron.GetNextOccurrence(now.UtcDateTime, inclusive: false);
            _nextFire = next.HasValue ? new DateTimeOffset(next.Value, TimeSpan.Zero) : null;
        }
    }

    // ── Cron helpers ────────────────────────────────────────────────────────

    private CronExpression? TryParseCron(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        try
        {
            return CronExpression.Parse(expression);
        }
        catch (CronFormatException ex)
        {
            _logger.LogWarning("Invalid backup cron expression '{Cron}': {Error}", expression, ex.Message);
            return null;
        }
    }
}
