using System.ComponentModel.DataAnnotations;
using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Rules;

public sealed class RuleGroupRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; init; }

    [Required]
    public MediaType MediaType { get; init; }

    public bool IsEnabled { get; init; } = true;

    public string? CronOverride { get; init; }

    [Required]
    public SweepAction Action { get; init; } = SweepAction.DeleteAndUnmonitor;

    [Required, MinLength(1, ErrorMessage = "A rule group must have at least one condition.")]
    public IReadOnlyList<RuleConditionDto> Conditions { get; init; } = [];
}
