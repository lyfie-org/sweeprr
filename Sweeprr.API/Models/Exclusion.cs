namespace Sweeprr.API.Models;

public class Exclusion
{
    public int Id { get; set; }
    public string MediaServerItemId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null = global exclusion. Non-null = scoped to a specific rule group.</summary>
    public int? RuleGroupId { get; set; }

    /// <summary>Null = permanent. Non-null = expires at this UTC timestamp.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Jellyfin username that requested this exclusion via the public extension portal
    /// (Story 10.4). Null for admin-created or system-generated exclusions.
    /// </summary>
    public string? CreatedBy { get; set; }

    public RuleGroup? RuleGroup { get; set; }
}
