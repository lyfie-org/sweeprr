namespace Sweeprr.API.Dtos.Media;

/// <summary>Per-rule-group outcome of tracing a single media item.</summary>
public sealed record RuleTraceEvaluation(
    int RuleGroupId,
    string RuleGroupName,
    bool Matched,
    IReadOnlyList<ClauseTraceResult> ClauseResults);
