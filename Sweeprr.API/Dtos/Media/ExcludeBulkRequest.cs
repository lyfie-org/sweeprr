using System.ComponentModel.DataAnnotations;

namespace Sweeprr.API.Dtos.Media;

public sealed class ExcludeBulkRequest
{
    [Required, MinLength(1, ErrorMessage = "At least one item ID is required.")]
    public IReadOnlyList<string> Ids { get; init; } = [];

    /// <summary>Null = global exclusion. Non-null = scoped to a specific rule group.</summary>
    public int? RuleGroupId { get; init; }

    public string? Reason { get; init; }

    /// <summary>Null = permanent. Non-null = expires at this UTC timestamp.</summary>
    public DateTime? ExpiresAt { get; init; }
}
