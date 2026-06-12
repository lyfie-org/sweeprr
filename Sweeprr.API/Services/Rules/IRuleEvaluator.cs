using Sweeprr.API.Models;

namespace Sweeprr.API.Services.Rules;

/// <summary>
/// Evaluates a <see cref="RuleGroup"/> against a population of media items
/// and returns one <see cref="EvaluationResult"/> per item, in the same order
/// as the input list.
/// </summary>
public interface IRuleEvaluator
{
    Task<IReadOnlyList<EvaluationResult>> EvaluateAsync(
        RuleGroup group,
        IReadOnlyList<MediaContext> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a single item against each of the given rule groups, returning a
    /// per-clause breakdown for every rule. Unlike <see cref="EvaluateAsync"/>, this
    /// does not short-circuit on a transient value — every clause is recorded so the
    /// caller can render a full trace.
    /// </summary>
    Task<IReadOnlyList<RuleGroupTrace>> TraceAsync(
        MediaContext item,
        IEnumerable<RuleGroup> groups,
        CancellationToken cancellationToken = default);
}
