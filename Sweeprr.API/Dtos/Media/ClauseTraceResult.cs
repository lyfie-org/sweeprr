using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Media;

/// <summary>Result of evaluating a single rule clause against an item's reconstructed context.
/// <c>Result</c> is <c>null</c> when the underlying value could not be resolved
/// (transient failure) — the clause was excluded from the match decision.</summary>
public sealed record ClauseTraceResult(
    int Section,
    LogicalOperator? LogicalOperator,
    string Field,
    string Comparator,
    string Value,
    bool? Result);
