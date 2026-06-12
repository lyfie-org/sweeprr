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
    /// Skips an item for the current run by setting its <c>SkippedReason</c> and
    /// resetting status to <c>Pending</c> so it re-appears next scan.
    /// </summary>
    Task<SweepItemResponse?> SkipAsync(int id, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Reconciles scan results into the sweep queue: upserts Pending items,
    /// removes stale Pending items that no longer match.
    /// Returns the number of newly created or updated Pending items.
    /// </summary>
    Task<int> ReconcileAsync(int ruleGroupId, IReadOnlyList<EvaluationResult> results, CancellationToken ct = default);

    /// <summary>
    /// Handles a "Request Extension" submission from the public portal (Story 10.4).
    /// If <paramref name="mediaServerItemId"/> is Pending or Approved in the sweep queue and
    /// <paramref name="jellyfinUsername"/> has not extended it within the last 7 days, removes
    /// the item from the queue and creates a global <c>Exclusion</c> expiring in
    /// <paramref name="requestedDays"/> days (clamped to 1-14).
    /// </summary>
    Task<ExtendResult> ExtendAsync(
        string mediaServerItemId, string jellyfinUsername, int requestedDays, CancellationToken ct = default);
}
