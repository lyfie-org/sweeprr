namespace Sweeprr.API.Integrations.Matching;

/// <summary>
/// Discriminated union returned by every match operation.
///
/// <list type="bullet">
///   <item><see cref="Matched"/> — exactly one *arr item found; safe to act on.</item>
///   <item><see cref="Unmatched"/> — no *arr item could be correlated; skip silently.</item>
///   <item><see cref="Ambiguous"/> — two or more candidates, or a conflicted index key;
///     route to the Sweep Queue for manual review — never auto-delete.</item>
/// </list>
/// </summary>
public abstract record MatchResult<T>
{
    public sealed record Matched(T Value) : MatchResult<T>;
    public sealed record Unmatched : MatchResult<T>;
    public sealed record Ambiguous : MatchResult<T>;

    public static MatchResult<T> Match(T value) => new Matched(value);
    public static MatchResult<T> NoMatch  => new Unmatched();
    public static MatchResult<T> Conflict => new Ambiguous();
}
