using System.Globalization;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services.Rules;

/// <summary>
/// Evaluates a single <see cref="Rule"/> condition against its <see cref="ResolvedValue"/>.
/// </summary>
internal static class ConditionEvaluator
{
    /// <summary>
    /// Returns:
    ///   <c>true</c>  — condition satisfied.
    ///   <c>false</c> — condition not satisfied.
    ///   <c>null</c>  — value is <see cref="ResolvedValue.Transient"/>; caller must exclude the item.
    /// </summary>
    public static bool? Evaluate(Rule rule, ResolvedValue resolved)
    {
        if (resolved is ResolvedValue.Transient)
            return null;

        // Existence comparators operate on presence alone
        if (rule.Comparator is RuleComparator.Exists)
            return resolved is ResolvedValue.Success;
        if (rule.Comparator is RuleComparator.NotExists)
            return resolved is ResolvedValue.Missing;

        // All other comparators require a value; definitively absent = not satisfied
        if (resolved is not ResolvedValue.Success success)
            return false;

        var value = success.Value;

        return rule.Comparator switch
        {
            RuleComparator.Equals        => EvaluateEquals(value, rule.Value, rule.ValueType),
            RuleComparator.NotEquals     => !EvaluateEquals(value, rule.Value, rule.ValueType),
            RuleComparator.GreaterThan   => EvaluateNumeric(value, rule.Value, (a, b) => a > b),
            RuleComparator.LessThan      => EvaluateNumeric(value, rule.Value, (a, b) => a < b),
            RuleComparator.Before        => EvaluateDate(value, rule.Value, (a, b) => a < b),
            RuleComparator.After         => EvaluateDate(value, rule.Value, (a, b) => a > b),
            RuleComparator.InLastDays    => EvaluateInLastDays(value, rule.Value, invert: false),
            RuleComparator.NotInLastDays => EvaluateInLastDays(value, rule.Value, invert: true),
            RuleComparator.Contains      => EvaluateContains(value, rule.Value),
            RuleComparator.NotContains   => !EvaluateContains(value, rule.Value),
            _                            => false
        };
    }

    /// <summary>
    /// Human-readable description of a satisfied condition for <c>MatchedRuleSummary</c>.
    /// Only call when the condition evaluated to <c>true</c>.
    /// </summary>
    public static string Describe(Rule rule, ResolvedValue resolved)
    {
        var actual = resolved is ResolvedValue.Success s ? FormatActual(s.Value) : "(not present)";
        var op     = DescribeComparator(rule.Comparator, rule.Value);
        return $"{rule.Field} {op} [actual: {actual}]";
    }

    // ── Comparison helpers ────────────────────────────────────────────────────

    private static bool EvaluateEquals(object actual, string ruleValue, RuleValueType valueType)
    {
        return valueType switch
        {
            RuleValueType.Number
                => ToDecimal(actual) is { } av
                   && decimal.TryParse(ruleValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var nv)
                   && av == nv,

            RuleValueType.Bool
                => actual is bool ab
                   && bool.TryParse(ruleValue, out var bv)
                   && ab == bv,

            RuleValueType.Date
                => actual is DateTime ad
                   && DateTime.TryParse(ruleValue, null, DateTimeStyles.AssumeUniversal, out var dv)
                   && ad.ToUniversalTime().Date == dv.ToUniversalTime().Date,

            _ => string.Equals(actual.ToString(), ruleValue, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool EvaluateNumeric(object actual, string ruleValue, Func<decimal, decimal, bool> compare)
    {
        if (!decimal.TryParse(ruleValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var rv))
            return false;

        return ToDecimal(actual) is { } av && compare(av, rv);
    }

    private static bool EvaluateDate(object actual, string ruleValue, Func<DateTime, DateTime, bool> compare)
    {
        if (actual is not DateTime av)
            return false;
        if (!DateTime.TryParse(ruleValue, null, DateTimeStyles.AssumeUniversal, out var rv))
            return false;

        return compare(av.ToUniversalTime(), rv.ToUniversalTime());
    }

    private static bool EvaluateInLastDays(object actual, string ruleValue, bool invert)
    {
        if (!int.TryParse(ruleValue, out var days) || days < 0)
            return false;
        if (actual is not DateTime av)
            return false;

        var cutoff    = DateTime.UtcNow.AddDays(-days);
        var isInRange = av.ToUniversalTime() >= cutoff;
        return invert ? !isInRange : isInRange;
    }

    private static bool EvaluateContains(object actual, string ruleValue)
    {
        if (actual is IReadOnlyList<string> list)
            return list.Any(t => string.Equals(t, ruleValue, StringComparison.OrdinalIgnoreCase));
        if (actual is string s)
            return s.Contains(ruleValue, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static decimal? ToDecimal(object v) => v switch
    {
        decimal d => d,
        int i     => (decimal)i,
        long l    => (decimal)l,
        double db => (decimal)db,
        float f   => (decimal)f,
        _         => null
    };

    // ── Description helpers ───────────────────────────────────────────────────

    private static string DescribeComparator(RuleComparator c, string ruleValue) => c switch
    {
        RuleComparator.Equals        => $"= {ruleValue}",
        RuleComparator.NotEquals     => $"≠ {ruleValue}",
        RuleComparator.GreaterThan   => $"> {ruleValue}",
        RuleComparator.LessThan      => $"< {ruleValue}",
        RuleComparator.Before        => $"before {ruleValue}",
        RuleComparator.After         => $"after {ruleValue}",
        RuleComparator.InLastDays    => $"in last {ruleValue} days",
        RuleComparator.NotInLastDays => $"not in last {ruleValue} days",
        RuleComparator.Contains      => $"contains \"{ruleValue}\"",
        RuleComparator.NotContains   => $"not contains \"{ruleValue}\"",
        RuleComparator.Exists        => "exists",
        RuleComparator.NotExists     => "not exists",
        _                            => c.ToString()
    };

    private static string FormatActual(object v) => v switch
    {
        DateTime dt               => dt.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IReadOnlyList<string> lst => $"[{string.Join(", ", lst)}]",
        decimal d                 => d.ToString("G", CultureInfo.InvariantCulture),
        int i                     => i.ToString(CultureInfo.InvariantCulture),
        bool b                    => b.ToString().ToLowerInvariant(),
        _                         => v.ToString() ?? "—"
    };
}
