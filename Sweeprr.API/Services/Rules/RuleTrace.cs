using Sweeprr.API.Models;

namespace Sweeprr.API.Services.Rules;

/// <summary>Result of evaluating a single rule clause against a <see cref="MediaContext"/>.
/// <c>Result</c> is <c>null</c> when the resolved value was transient — the clause could
/// not be evaluated to a definite outcome.</summary>
public sealed record ClauseTrace(
    int Section,
    LogicalOperator? LogicalOperator,
    RuleField Field,
    RuleComparator Comparator,
    string Value,
    bool? Result);

/// <summary>Per-rule-group result of <see cref="IRuleEvaluator.TraceAsync"/>.</summary>
public sealed record RuleGroupTrace(
    int RuleGroupId,
    string RuleGroupName,
    bool Matched,
    IReadOnlyList<ClauseTrace> Clauses);
