using System.Collections.Concurrent;
using System.Text.Json;
using Cronos;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Models;

namespace Sweeprr.API.Background;

public sealed class SchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IScanPipeline _pipeline;
    private readonly ILogger<SchedulerHostedService> _logger;

    private readonly ConcurrentDictionary<int, byte> _runningGroups = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);

    private readonly TimeSpan _tickInterval;
    private readonly TimeSpan _scheduleReloadInterval;

    private readonly record struct ScheduleEntry(int RuleGroupId, CronExpression Cron, DateTimeOffset NextFire);

    private List<ScheduleEntry> _schedules = [];
    private DateTimeOffset _lastScheduleLoad = DateTimeOffset.MinValue;

    public SchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        IScanPipeline pipeline,
        ILogger<SchedulerHostedService> logger,
        TimeSpan? tickInterval = null,
        TimeSpan? scheduleReloadInterval = null)
    {
        _scopeFactory = scopeFactory;
        _pipeline = pipeline;
        _logger = logger;
        _tickInterval = tickInterval ?? TimeSpan.FromSeconds(30);
        _scheduleReloadInterval = scheduleReloadInterval ?? TimeSpan.FromMinutes(2);
    }

    // ── Test hooks ────────────────────────────────────────────────────────

    internal async Task ForceReloadAsync(CancellationToken ct = default)
        => await LoadSchedulesAsync(ct);

    internal async Task ForceFireAsync(CancellationToken ct = default)
        => await FireDueJobsAsync(ct);

    internal async Task ForceTickAsync(CancellationToken ct = default)
    {
        await LoadSchedulesAsync(ct);
        await FireDueJobsAsync(ct);
    }

    // ── Public accessors ─────────────────────────────────────────────────────

    public DateTimeOffset? GetNextScheduledRun()
        => _schedules.Count == 0 ? null : _schedules.Min(s => s.NextFire);

    // ── Manual trigger (called from the controller) ─────────────────────────

    public async Task<ScanResult> TriggerScanAsync(int ruleGroupId, CancellationToken ct = default)
    {
        if (!_runningGroups.TryAdd(ruleGroupId, 0))
            throw new InvalidOperationException($"A scan for RuleGroup {ruleGroupId} is already running.");

        try
        {
            await _executionGate.WaitAsync(ct);
            try
            {
                return await RunScanWithLogging(ruleGroupId, manual: true, ct);
            }
            finally
            {
                _executionGate.Release();
            }
        }
        finally
        {
            _runningGroups.TryRemove(ruleGroupId, out _);
        }
    }

    // ── Background loop ─────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduler started");

        await LoadSchedulesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - _lastScheduleLoad > _scheduleReloadInterval)
                    await LoadSchedulesAsync(stoppingToken);

                await FireDueJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler tick error");
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

        _logger.LogInformation("Scheduler stopped");
    }

    // ── Schedule loading ────────────────────────────────────────────────────

    private async Task LoadSchedulesAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

            var settings = await db.GlobalSettings.FirstAsync(ct);
            var defaultCron = TryParseCron(settings.DefaultCron);

            var groups = await db.RuleGroups
                .AsNoTracking()
                .Where(g => g.IsEnabled)
                .Select(g => new { g.Id, g.CronOverride })
                .ToListAsync(ct);

            var now = DateTimeOffset.UtcNow;
            var entries = new List<ScheduleEntry>();

            foreach (var g in groups)
            {
                var cron = (!string.IsNullOrWhiteSpace(g.CronOverride)
                    ? TryParseCron(g.CronOverride)
                    : defaultCron);

                if (cron is null) continue;

                var nextFire = cron.GetNextOccurrence(now.UtcDateTime, inclusive: false);
                if (nextFire.HasValue)
                    entries.Add(new ScheduleEntry(g.Id, cron, new DateTimeOffset(nextFire.Value, TimeSpan.Zero)));
            }

            _schedules = entries;
            _lastScheduleLoad = DateTimeOffset.UtcNow;

            _logger.LogDebug("Loaded {Count} schedules", entries.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to reload schedules — will retry next tick");
        }
    }

    // ── Fire due jobs ───────────────────────────────────────────────────────

    private async Task FireDueJobsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = new List<ScheduleEntry>();

        foreach (var entry in _schedules)
        {
            if (entry.NextFire <= now)
            {
                if (_runningGroups.TryAdd(entry.RuleGroupId, 0))
                {
                    try
                    {
                        await _executionGate.WaitAsync(ct);
                        try
                        {
                            await RunScanWithLogging(entry.RuleGroupId, manual: false, ct);
                        }
                        finally
                        {
                            _executionGate.Release();
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Scheduled scan failed for group {GroupId}", entry.RuleGroupId);
                    }
                    finally
                    {
                        _runningGroups.TryRemove(entry.RuleGroupId, out _);
                    }
                }
                else
                {
                    _logger.LogDebug("Skipping group {GroupId} — already running", entry.RuleGroupId);
                }

                var next = entry.Cron.GetNextOccurrence(now.UtcDateTime, inclusive: false);
                if (next.HasValue)
                    updated.Add(entry with { NextFire = new DateTimeOffset(next.Value, TimeSpan.Zero) });
            }
            else
            {
                updated.Add(entry);
            }
        }

        _schedules = updated;
    }

    // ── Run + log ───────────────────────────────────────────────────────────

    private async Task<ScanResult> RunScanWithLogging(int ruleGroupId, bool manual, CancellationToken ct)
    {
        var trigger = manual ? "manual" : "scheduled";
        _logger.LogInformation("Scan started for group {GroupId} ({Trigger})", ruleGroupId, trigger);

        await LogActivityAsync(ActivityLogLevel.Information, ActivityLogCategory.Rule,
            $"Scan started for group {ruleGroupId} ({trigger})", null, ct);

        ScanResult result;
        try
        {
            result = await _pipeline.ExecuteAsync(ruleGroupId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed for group {GroupId}", ruleGroupId);

            await LogActivityAsync(ActivityLogLevel.Error, ActivityLogCategory.Rule,
                $"Scan failed for group {ruleGroupId}: {ex.Message}", null, ct);

            throw;
        }

        _logger.LogInformation("Scan completed for group {GroupId} ({GroupName}): {Items} item(s) flagged in {Duration:F1}s",
            result.RuleGroupId, result.RuleGroupName, result.ItemsFlagged, result.Duration.TotalSeconds);

        await LogActivityAsync(ActivityLogLevel.Information, ActivityLogCategory.Rule,
            $"Scan completed for group {result.RuleGroupId} ({result.RuleGroupName}): {result.ItemsFlagged} item(s) flagged",
            JsonSerializer.Serialize(new { result.RuleGroupId, result.ItemsFlagged, DurationMs = (int)result.Duration.TotalMilliseconds }),
            ct);

        return result;
    }

    private async Task LogActivityAsync(
        ActivityLogLevel level, ActivityLogCategory category,
        string message, string? metaJson, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

            db.ActivityLogs.Add(new ActivityLog
            {
                Timestamp = DateTime.UtcNow,
                Level     = level,
                Category  = category,
                Message   = message,
                MetaJson  = metaJson,
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write activity log");
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
            _logger.LogWarning("Invalid cron expression '{Cron}': {Error}", expression, ex.Message);
            return null;
        }
    }

    internal static bool IsValidCron(string expression)
    {
        try
        {
            CronExpression.Parse(expression);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }
}
