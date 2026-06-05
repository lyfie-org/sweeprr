using Sweeprr.API.Dtos.Sweep;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

public interface ISweepQueueService
{
    Task<PagedResponse<SweepItemResponse>> QueryAsync(SweepQueryParams query, CancellationToken ct = default);
    Task<SweepItemResponse?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<SweepSummaryResponse> GetSummaryAsync(CancellationToken ct = default);
    Task<SweepItemResponse?> ApproveAsync(int id, CancellationToken ct = default);
    Task<SweepItemResponse?> IgnoreAsync(int id, bool createExclusion, CancellationToken ct = default);

    /// <summary>
    /// Reconciles scan results into the sweep queue: upserts Pending items,
    /// removes stale Pending items that no longer match.
    /// Returns the number of newly created or updated Pending items.
    /// </summary>
    Task<int> ReconcileAsync(int ruleGroupId, IReadOnlyList<EvaluationResult> results, CancellationToken ct = default);
}
