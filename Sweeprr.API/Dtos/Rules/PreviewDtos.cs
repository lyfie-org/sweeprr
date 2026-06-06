using System.ComponentModel.DataAnnotations;
using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Rules;

/// <summary>
/// Request body for <c>POST /api/rulegroups/preview</c>.
/// Mirrors <see cref="RuleGroupRequest"/> but is ephemeral — nothing is persisted.
/// </summary>
public sealed class PreviewRequest
{
    [Required]
    public MediaType MediaType { get; init; }

    [Required]
    public IReadOnlyList<RuleConditionDto> Conditions { get; init; } = [];
}

/// <summary>
/// Response body for <c>POST /api/rulegroups/preview</c>.
/// Returns the number of items that would be matched by the supplied conditions,
/// plus up to 5 sample titles for user feedback.
/// </summary>
public sealed record PreviewResponse(
    int MatchCount,
    IReadOnlyList<string> SampleTitles,
    string? Note);
