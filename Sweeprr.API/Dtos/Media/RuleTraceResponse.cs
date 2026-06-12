namespace Sweeprr.API.Dtos.Media;

/// <summary>Full rule-trace for a single media item — how every rule group
/// (matching the item's media type) currently evaluates against it.</summary>
public sealed record RuleTraceResponse(
    string ItemId,
    string Title,
    IReadOnlyList<RuleTraceEvaluation> Evaluations);
