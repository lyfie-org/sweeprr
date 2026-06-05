using Sweeprr.API.Models;

namespace Sweeprr.API.Services.Rules;

/// <summary>
/// Evaluates a <see cref="RuleGroup"/> against a population of <see cref="MediaContext"/> items.
///
/// Section model:
///   - Rules are partitioned into sections by <see cref="Rule.Section"/>.
///   - Within a section: conditions fold using each rule's <see cref="Rule.LogicalOperator"/>
///     (first rule is the anchor; subsequent rules apply And/Or against the running result).
///   - Across sections: the first rule of each section beyond section 0 declares how
///     that section's result combines with the accumulated result (And/Or).
///
/// Safety invariants (enforced here, not just at validation time):
///   - Empty rule group → nothing matches (anti-wipe).
///   - Any transient failure on an item → item excluded, never matched.
///   - Evaluation is bounded to <see cref="MaxDegreeOfParallelism"/> concurrent items.
/// </summary>
public sealed class RuleEvaluator : IRuleEvaluator
{
    private const int MaxDegreeOfParallelism = 8;

    private readonly IValueResolver _valueResolver;

    public RuleEvaluator(IValueResolver valueResolver)
    {
        _valueResolver = valueResolver;
    }

    public async Task<IReadOnlyList<EvaluationResult>> EvaluateAsync(
        RuleGroup group,
        IReadOnlyList<MediaContext> items,
        CancellationToken cancellationToken = default)
    {
        // Anti-wipe: a group with no rules must never match anything
        if (group.Rules is null || group.Rules.Count == 0)
            return items.Select(EvaluationResult.NoMatch).ToList();

        // Pre-build the section list once; it is read-only across all parallel evaluations
        var sections = group.Rules
            .OrderBy(r => r.Section)
            .ThenBy(r => r.Id)
            .GroupBy(r => r.Section)
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .ToList();

        // Use an array so results[i] = items[i] (insertion order preserved, no locking)
        var results = new EvaluationResult[items.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, items.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            (i, _) =>
            {
                results[i] = EvaluateItem(items[i], sections);
                return ValueTask.CompletedTask;
            });

        return results;
    }

    // ── Per-item evaluation ───────────────────────────────────────────────────

    private EvaluationResult EvaluateItem(MediaContext item, List<List<Rule>> sections)
    {
        // Transient context → exclude unconditionally; do not attempt evaluation
        if (item.HasTransientFailure)
        {
            var reason = item.TransientFailureReason ?? "Transient data source failure";
            return EvaluationResult.Excluded(item, reason);
        }

        bool? runningResult       = null;
        var   matchedDescriptions = new List<string>();

        for (var si = 0; si < sections.Count; si++)
        {
            var sectionRules = sections[si];
            var (sectionResult, sectionDescriptions) = EvaluateSection(item, sectionRules);

            // A null section result means a transient value was encountered mid-evaluation
            if (sectionResult is null)
                return EvaluationResult.Excluded(item, "Transient value resolution — excluded from deletion");

            if (si == 0)
            {
                runningResult = sectionResult.Value;
            }
            else
            {
                // Inter-section operator: the LogicalOperator on the first rule of this section.
                // Defaults to And if somehow null (matches validation intent).
                var interOp = sectionRules[0].LogicalOperator ?? LogicalOperator.And;
                runningResult = interOp == LogicalOperator.Or
                    ? (runningResult ?? false) || sectionResult.Value
                    : (runningResult ?? false) && sectionResult.Value;
            }

            // Only accumulate descriptions for sections that individually matched —
            // keeps the summary focused on conditions that actually triggered the match
            if (sectionResult.Value)
                matchedDescriptions.AddRange(sectionDescriptions);
        }

        var matched = runningResult ?? false;

        return matched
            ? EvaluationResult.Matched(item, string.Join("; ", matchedDescriptions))
            : EvaluationResult.NoMatch(item);
    }

    /// <summary>
    /// Folds all conditions in a single section using their logical operators.
    /// Returns <c>null</c> as the result when a transient failure is encountered.
    /// <paramref name="satisfiedDescriptions"/> contains descriptions only for individually-true conditions.
    /// </summary>
    private (bool? Result, List<string> SatisfiedDescriptions) EvaluateSection(
        MediaContext item, List<Rule> sectionRules)
    {
        bool? result   = null;
        var   satisfied = new List<string>();

        foreach (var rule in sectionRules)
        {
            var resolved        = _valueResolver.Resolve(rule.Field, item);
            var conditionResult = ConditionEvaluator.Evaluate(rule, resolved);

            if (conditionResult is null)
                return (null, satisfied); // Transient — bubble up to EvaluateItem

            if (result is null)
            {
                // First rule in the section is the anchor (no operator applied)
                result = conditionResult.Value;
            }
            else
            {
                var op = rule.LogicalOperator ?? LogicalOperator.And;
                result = op == LogicalOperator.Or
                    ? result.Value || conditionResult.Value
                    : result.Value && conditionResult.Value;
            }

            if (conditionResult.Value)
                satisfied.Add(ConditionEvaluator.Describe(rule, resolved));
        }

        return (result ?? false, satisfied);
    }
}
