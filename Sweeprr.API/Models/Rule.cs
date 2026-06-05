namespace Sweeprr.API.Models;

public class Rule
{
    public int Id { get; set; }
    public int RuleGroupId { get; set; }
    public int Section { get; set; }
    public LogicalOperator? LogicalOperator { get; set; }
    public RuleField Field { get; set; }
    public RuleComparator Comparator { get; set; }
    public string Value { get; set; } = string.Empty;
    public RuleValueType ValueType { get; set; }

    public RuleGroup RuleGroup { get; set; } = null!;
}
