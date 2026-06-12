using Sweeprr.API.Dtos.Media;
using Sweeprr.API.Dtos.Sweep;

namespace Sweeprr.API.Services;

/// <summary>
/// Backs the Media Explorer / curation dashboard (Story 9.5). Aggregates
/// <c>SweepItem</c> records (across all rule groups and statuses) by
/// <c>MediaServerItemId</c> into one row per media item, and provides
/// rule-trace, manual-queue, and bulk-exclusion operations.
/// </summary>
public interface IMediaExplorerService
{
    Task<PagedResponse<MediaItemResponse>> GetPagedAsync(MediaQueryParams query, CancellationToken ct = default);

    Task<RuleTraceResponse?> GetRuleTraceAsync(string itemId, CancellationToken ct = default);

    Task<QueueManualResponse> QueueManualAsync(QueueManualRequest request, CancellationToken ct = default);

    Task<ExcludeBulkResponse> ExcludeBulkAsync(ExcludeBulkRequest request, CancellationToken ct = default);
}
