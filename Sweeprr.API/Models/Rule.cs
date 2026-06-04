namespace Sweeprr.API.Models;

public class Rule
{
    public int Id { get; set; }
    public int RuleGroupId { get; set; }
    public int Section { get; set; }
    public LogicalOperator? LogicalOperator { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Comparator { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public RuleValueType ValueType { get; set; }

    public RuleGroup RuleGroup { get; set; } = null!;
}
