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

    public RuleGroup? RuleGroup { get; set; }
}
