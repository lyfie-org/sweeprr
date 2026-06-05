using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Models;
using Sweeprr.API.Services;
using Sweeprr.API.Services.Rules;

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
        var populationService = scope.ServiceProvider.GetRequiredService<IMediaPopulationService>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IRuleEvaluator>();
        var sweepQueue = scope.ServiceProvider.GetRequiredService<ISweepQueueService>();

        var group = await db.RuleGroups
            .Include(g => g.Rules)
            .FirstOrDefaultAsync(g => g.Id == ruleGroupId, ct);

        if (group is null)
            throw new InvalidOperationException($"RuleGroup {ruleGroupId} not found.");

        if (!group.IsEnabled)
        {
            _logger.LogInformation("Skipping disabled group {GroupId} ({GroupName})", ruleGroupId, group.Name);
            sw.Stop();
            return new ScanResult(ruleGroupId, group.Name, ItemsFlagged: 0, sw.Elapsed);
        }

        // 1. Populate media items with metadata + watch state
        _logger.LogInformation("Populating media for group {GroupId} ({GroupName})", ruleGroupId, group.Name);
        var items = await populationService.PopulateAsync(group, ct);
        _logger.LogInformation("Populated {Count} items for group {GroupId}", items.Count, ruleGroupId);

        if (items.Count == 0)
        {
            await sweepQueue.ReconcileAsync(ruleGroupId, [], ct);
            sw.Stop();
            return new ScanResult(ruleGroupId, group.Name, ItemsFlagged: 0, sw.Elapsed);
        }

        // 2. Evaluate rules against the population
        var results = await evaluator.EvaluateAsync(group, items, ct);

        // 3. Reconcile into the sweep queue (upsert Pending, drop stale)
        var flaggedCount = await sweepQueue.ReconcileAsync(ruleGroupId, results, ct);

        _logger.LogInformation("Scan complete for group {GroupId}: {Flagged} item(s) flagged out of {Total}",
            ruleGroupId, flaggedCount, items.Count);

        sw.Stop();
        return new ScanResult(ruleGroupId, group.Name, ItemsFlagged: flaggedCount, sw.Elapsed);
    }
}
