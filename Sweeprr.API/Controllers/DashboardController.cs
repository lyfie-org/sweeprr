using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Background;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Dashboard;
using Sweeprr.API.Integrations.Jellyfin.WebSocket;
using Sweeprr.API.Models;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly SweeprrDbContext _db;
    private readonly SchedulerHostedService _scheduler;
    private readonly IJellyfinWebSocketStatus _wsStatus;

    public DashboardController(
        SweeprrDbContext db,
        SchedulerHostedService scheduler,
        IJellyfinWebSocketStatus wsStatus)
    {
        _db = db;
        _scheduler = scheduler;
        _wsStatus = wsStatus;
    }

    /// <summary>
    /// Retrieves aggregate dashboard statistics including swept items and queue size.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(DashboardStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var cutoff30d = DateTime.UtcNow.AddDays(-30);

        var swept = await _db.SweepItems
            .AsNoTracking()
            .Where(i => i.Status == SweepItemStatus.Swept)
            .Select(i => new { i.SizeBytes, i.SweptAt })
            .ToListAsync(ct);

        var totalGb = swept.Sum(i => (i.SizeBytes ?? 0) / 1_000_000_000.0);
        var totalCount = swept.Count;

        var last30d = swept.Where(i => i.SweptAt >= cutoff30d).ToList();
        var gbLast30d = last30d.Sum(i => (i.SizeBytes ?? 0) / 1_000_000_000.0);
        var countLast30d = last30d.Count;

        var pending = await _db.SweepItems
            .AsNoTracking()
            .CountAsync(i => i.Status == SweepItemStatus.Pending, ct);

        var settings = await _db.GlobalSettings
            .AsNoTracking()
            .Where(s => s.Id == 1)
            .Select(s => s.GlobalDryRun)
            .FirstOrDefaultAsync(ct);

        var dto = new DashboardStatsDto(
            TotalGbRecovered: Math.Round(totalGb, 2),
            TotalItemsSwept: totalCount,
            ItemsSweptLast30d: countLast30d,
            GbRecoveredLast30d: Math.Round(gbLast30d, 2),
            PendingQueueCount: pending,
            NextScheduledRun: _scheduler.GetNextScheduledRun(),
            WsState: _wsStatus.State.ToString(),
            GlobalDryRun: settings
        );

        return Ok(dto);
    }

    /// <summary>
    /// Retrieves recent activity logs.
    /// </summary>
    [HttpGet("activity")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityLogEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivity([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        var entries = await _db.ActivityLogs
            .AsNoTracking()
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .Select(l => new ActivityLogEntryDto(
                l.Id,
                l.Timestamp,
                l.Level.ToString(),
                l.Category.ToString(),
                l.Message,
                l.MetaJson))
            .ToListAsync(ct);

        return Ok(entries);
    }

    /// <summary>
    /// Retrieves historical data points for the dashboard sparkline charts.
    /// </summary>
    [HttpGet("sparkline")]
    [ProducesResponseType(typeof(IReadOnlyList<SparklinePointDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSparkline([FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 90);

        var cutoff = DateTime.UtcNow.Date.AddDays(-days + 1);

        var swept = await _db.SweepItems
            .AsNoTracking()
            .Where(i => i.Status == SweepItemStatus.Swept && i.SweptAt != null && i.SweptAt.Value >= cutoff)
            .Select(i => new { i.SizeBytes, i.SweptAt })
            .ToListAsync(ct);

        // Build a zero-filled map for each day in the window
        var byDate = swept
            .GroupBy(i => DateOnly.FromDateTime(i.SweptAt!.Value.Date))
            .ToDictionary(g => g.Key, g => new
            {
                GbRecovered = g.Sum(x => (x.SizeBytes ?? 0) / 1_000_000_000.0),
                ItemsSwept = g.Count(),
            });

        var points = Enumerable.Range(0, days)
            .Select(offset =>
            {
                var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-(days - 1) + offset));
                return byDate.TryGetValue(date, out var data)
                    ? new SparklinePointDto(date, Math.Round(data.GbRecovered, 2), data.ItemsSwept)
                    : new SparklinePointDto(date, 0, 0);
            })
            .ToList();

        return Ok(points);
    }
}
