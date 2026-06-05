namespace Sweeprr.API.Models;

/// <summary>Per-item outcome of evaluating a <see cref="RuleGroup"/>.</summary>
public sealed record EvaluationResult(
    MediaContext Item,
    bool IsMatch,
    bool WasExcluded,
    string MatchedRuleSummary)
{
    /// <summary>
    /// Item had a transient data failure. Never deleted; appears in the
    /// Sweep Queue with the <paramref name="reason"/> for operator review.
    /// </summary>
    public static EvaluationResult Excluded(MediaContext item, string reason)
        => new(item, IsMatch: false, WasExcluded: true, MatchedRuleSummary: reason);

    /// <summary>Item evaluated cleanly but did not satisfy the rule conditions.</summary>
    public static EvaluationResult NoMatch(MediaContext item)
        => new(item, IsMatch: false, WasExcluded: false, MatchedRuleSummary: string.Empty);

    /// <summary>
    /// Item satisfied all rule conditions. <paramref name="summary"/> is stored on
    /// <c>SweepItem.MatchedRuleSummary</c> so the Sweep Queue can explain why.
    /// </summary>
    public static EvaluationResult Matched(MediaContext item, string summary)
        => new(item, IsMatch: true, WasExcluded: false, MatchedRuleSummary: summary);
}
