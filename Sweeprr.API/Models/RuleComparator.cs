namespace Sweeprr.API.Models;

/// <summary>
/// The "how to compare" dimension of a rule condition.
/// Which comparators are legal for a given field is enforced by <see cref="RuleFieldMeta"/>.
/// Explicit int values ensure DB stability across renames.
/// </summary>
public enum RuleComparator
{
    Equals         = 1,
    NotEquals      = 2,
    GreaterThan    = 3,
    LessThan       = 4,
    Contains       = 5,
    NotContains    = 6,
    Before         = 7,
    After          = 8,
    InLastDays     = 9,
    NotInLastDays  = 10,
    Exists         = 11,
    NotExists      = 12,
}
