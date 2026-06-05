namespace Sweeprr.API.Services.Rules;

/// <summary>
/// Discriminated union representing the outcome of resolving a field value
/// from a <see cref="Models.MediaContext"/>.
///
/// - <see cref="Success"/>  : value was found; use it for comparisons.
/// - <see cref="Missing"/>  : value is definitively absent (null/empty in the source).
///                            Comparators that require a value return false.
///                            <c>NotExists</c> returns true.
/// - <see cref="Transient"/>: value could not be retrieved due to a non-definitive failure
///                            (timeout, network error, etc.).
///                            The item MUST be excluded from deletion — never matched.
/// </summary>
public abstract record ResolvedValue
{
    private ResolvedValue() { }

    public sealed record Success(object Value) : ResolvedValue;
    public sealed record Missing() : ResolvedValue;
    public sealed record Transient(string Reason) : ResolvedValue;
}
