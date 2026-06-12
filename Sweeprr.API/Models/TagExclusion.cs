namespace Sweeprr.API.Models;

/// <summary>
/// An *arr tag that acts as an auto-exclusion signal.
/// When an item carries this tag, it is skipped during sweep queue reconciliation.
/// RuleGroupId = null → global (applied to all rule groups).
/// RuleGroupId = N   → scoped (applied only to that rule group).
/// </summary>
public class TagExclusion
{
    public int Id { get; set; }
    public string TagName { get; set; } = null!;
    public int TagId { get; set; }
    public int ServerConnectionId { get; set; }
    public int? RuleGroupId { get; set; }

    public ServerConnection ServerConnection { get; set; } = null!;
    public RuleGroup? RuleGroup { get; set; }
}
