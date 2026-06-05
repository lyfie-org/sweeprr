using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Background;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Sweep;
using Sweeprr.API.Services;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/sweep")]
public sealed class SweepController : ControllerBase
{
    private readonly ISweepQueueService _queue;
    private readonly ISweepExecutor _executor;
    private readonly SweeprrDbContext _db;
    private readonly SchedulerHostedService _scheduler;

    public SweepController(
        ISweepQueueService queue,
        ISweepExecutor executor,
        SweeprrDbContext db,
        SchedulerHostedService scheduler)
    {
        _queue = queue;
        _executor = executor;
        _db = db;
        _scheduler = scheduler;
    }

    // ── Query ────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] SweepQueryParams query, CancellationToken ct)
    {
        var result = await _queue.QueryAsync(query, ct);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var summary = await _queue.GetSummaryAsync(ct);
        return Ok(summary);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var item = await _queue.GetByIdAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var item = await _queue.ApproveAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost("{id:int}/ignore")]
    public async Task<IActionResult> Ignore(int id, [FromBody] IgnoreRequest? body, CancellationToken ct)
    {
        var item = await _queue.IgnoreAsync(id, body?.CreateExclusion ?? false, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost("{id:int}/skip")]
    public async Task<IActionResult> Skip(int id, [FromBody] SkipRequest? body, CancellationToken ct)
    {
        var item = await _queue.SkipAsync(id, body?.Reason, ct);
        return item is null ? NotFound() : Ok(item);
    }

    // ── Sweep execution ───────────────────────────────────────────────────────

    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] ExecuteSweepRequest? body, CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync(body ?? new ExecuteSweepRequest(), ct);
        return Ok(result);
    }

    // ── Manual scan run (triggers rule evaluation → populates queue) ─────────

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] RunSweepRequest? body, CancellationToken ct)
    {
        var settings = await _db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var isDryRun = settings?.GlobalDryRun ?? true;

        if (body?.RuleGroupId.HasValue == true)
        {
            var groupId = body.RuleGroupId.Value;
            var exists = await _db.RuleGroups.AnyAsync(g => g.Id == groupId, ct);
            if (!exists) return NotFound(new { error = $"RuleGroup {groupId} not found." });

            ScanResult result;
            try { result = await _scheduler.TriggerScanAsync(groupId, ct); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already running"))
            { return Conflict(new { error = ex.Message }); }

            return Ok(new
            {
                IsDryRun = isDryRun,
                Results = new[]
                {
                    new
                    {
                        result.RuleGroupId,
                        result.RuleGroupName,
                        result.ItemsFlagged,
                        DurationMs = (int)result.Duration.TotalMilliseconds,
                    }
                },
            });
        }

        // Run all enabled groups
        var groups = await _db.RuleGroups
            .AsNoTracking()
            .Where(g => g.IsEnabled)
            .Select(g => g.Id)
            .ToListAsync(ct);

        var results = new List<object>();
        foreach (var groupId in groups)
        {
            try
            {
                var result = await _scheduler.TriggerScanAsync(groupId, ct);
                results.Add(new
                {
                    result.RuleGroupId,
                    result.RuleGroupName,
                    result.ItemsFlagged,
                    DurationMs = (int)result.Duration.TotalMilliseconds,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already running"))
            {
                results.Add(new { RuleGroupId = groupId, Error = ex.Message });
            }
        }

        return Ok(new { IsDryRun = isDryRun, Results = results });
    }
}
