using System.ComponentModel.DataAnnotations;
using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Rules;

/// <summary>
/// A single typed condition within a rule group.
/// Section + LogicalOperator implement AND/OR section grouping
/// (first condition in each section has null LogicalOperator).
/// </summary>
public sealed class RuleConditionDto
{
    [Range(0, int.MaxValue)]
    public int Section { get; init; }

    public LogicalOperator? LogicalOperator { get; init; }

    [Required]
    public RuleField Field { get; init; }

    [Required]
    public RuleComparator Comparator { get; init; }

    public string Value { get; init; } = string.Empty;

    [Required]
    public RuleValueType ValueType { get; init; }
}
