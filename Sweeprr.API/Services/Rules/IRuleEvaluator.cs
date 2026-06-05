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
}
