using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Media;

/// <summary>One row in the Media Explorer table — a media item aggregated across all
/// <see cref="SweepItem"/> records (any rule group, any status) that share its
/// <c>MediaServerItemId</c>.</summary>
public sealed record MediaItemResponse(
    string Id,
    string Title,
    int? Year,
    MediaType Type,
    double SizeGb,
    string SizeLabel,
    DateTime? LastWatched,
    int WatchedByCount,
    SweepItemStatus? Status,
    IReadOnlyList<MatchedRuleGroupDto> MatchedRuleGroups,
    IReadOnlyList<string> Tags,
    bool IsExcluded);
