using System.ComponentModel.DataAnnotations;
using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Rules;

public sealed class SimulateRequest
{
    [Required]
    public MediaType MediaType { get; init; }

    [Required, MinLength(1, ErrorMessage = "At least one condition is required.")]
    public IReadOnlyList<RuleConditionDto> Conditions { get; init; } = [];
}

public sealed class SimulateLibraryBreakdown
{
    public string Library { get; init; } = string.Empty;
    public int MatchedCount { get; init; }
    public double ReclaimedGb { get; init; }
}

public sealed class SimulateResponse
{
    public int MatchedCount { get; init; }
    public double TotalReclaimedGb { get; init; }
    public Dictionary<string, double> CategoryBreakdown { get; init; } = [];
    public IReadOnlyList<SimulateLibraryBreakdown> LibraryBreakdown { get; init; } = [];
    public IReadOnlyList<string> SampleTitles { get; init; } = [];
    public string? Note { get; init; }
}
