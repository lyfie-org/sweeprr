using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Models;

namespace Sweeprr.API.Background;

public sealed class ScanPipeline : IScanPipeline
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScanPipeline> _logger;

    public ScanPipeline(IServiceScopeFactory scopeFactory, ILogger<ScanPipeline> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ScanResult> ExecuteAsync(int ruleGroupId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

        var group = await db.RuleGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == ruleGroupId, ct);

        if (group is null)
            throw new InvalidOperationException($"RuleGroup {ruleGroupId} not found.");

        // Story 4.2 wires the full pipeline: fetch media → evaluate rules → write SweepItems.
        // For now, log and return zero matches so the scheduler infrastructure can be tested end-to-end.
        _logger.LogInformation("Scan pipeline executed for group {GroupId} ({GroupName}) — full evaluation wired in Story 4.2",
            ruleGroupId, group.Name);

        sw.Stop();
        return new ScanResult(ruleGroupId, group.Name, ItemsFlagged: 0, sw.Elapsed);
    }
}
