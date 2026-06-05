using System.Diagnostics;

namespace Sweeprr.API.Integrations;

/// <summary>
/// Discriminated union representing the outcome of an HTTP call against an external service.
///
/// <b>Safety contract:</b> callers MUST NEVER delete or act destructively on a
/// <see cref="TransientFailure"/>. A transient failure means the value could not be
/// read — NOT that the item is absent. Only a <see cref="DefinitiveFailure"/> with
/// <see cref="DefinitiveFailure.StatusCode"/> 404 means the item is definitively gone.
/// </summary>
public abstract record HttpResult<T>
{
    // Prevent external subclassing — this is a closed hierarchy.
    private protected HttpResult() { }

    // ── Cases ────────────────────────────────────────────────────────────────

    /// <summary>The call succeeded. <see cref="Value"/> is ready to use.</summary>
    public sealed record Success(T Value) : HttpResult<T>;

    /// <summary>
    /// A transient, retryable failure (network error, timeout, 5xx, open circuit breaker).
    /// The item's existence is <b>unknown</b>.
    /// <b>Never treat as "absent". Never use to justify deletion.</b>
    /// </summary>
    public sealed record TransientFailure(string Reason, Exception? Exception = null) : HttpResult<T>;

    /// <summary>
    /// A definitive, non-retryable failure. <see cref="StatusCode"/> is authoritative:
    /// 404 = item genuinely absent; 401/403 = auth misconfiguration.
    /// </summary>
    public sealed record DefinitiveFailure(int? StatusCode, string Reason) : HttpResult<T>;

    // ── Factory helpers ──────────────────────────────────────────────────────

    /// <summary>Creates a successful result wrapping <paramref name="value"/>.</summary>
    public static HttpResult<T> Ok(T value) => new Success(value);

    /// <summary>Creates a transient-failure result. Optionally attaches the originating exception.</summary>
    public static HttpResult<T> Transient(string reason, Exception? ex = null)
        => new TransientFailure(reason, ex);

    /// <summary>Creates a definitive-failure result with an HTTP status code.</summary>
    public static HttpResult<T> Definitive(int? statusCode, string reason)
        => new DefinitiveFailure(statusCode, reason);

    // ── State queries ────────────────────────────────────────────────────────

    public bool IsSuccess    => this is Success;
    public bool IsTransient  => this is TransientFailure;
    public bool IsDefinitive => this is DefinitiveFailure;

    /// <summary>True only when the item is definitively absent (HTTP 404).</summary>
    public bool IsNotFound => this is DefinitiveFailure { StatusCode: 404 };

    // ── Projection ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the value. Throws <see cref="InvalidOperationException"/> if not a
    /// <see cref="Success"/>.
    /// </summary>
    public T Unwrap() => this is Success s
        ? s.Value
        : throw new InvalidOperationException($"HttpResult is not a success: {this}");

    /// <summary>
    /// Projects a successful value via <paramref name="fn"/>; propagates failures unchanged.
    /// </summary>
    public HttpResult<TOut> Map<TOut>(Func<T, TOut> fn) => this switch
    {
        Success s          => HttpResult<TOut>.Ok(fn(s.Value)),
        TransientFailure t => HttpResult<TOut>.Transient(t.Reason, t.Exception),
        DefinitiveFailure d => HttpResult<TOut>.Definitive(d.StatusCode, d.Reason),
        _                  => throw new UnreachableException("Exhaustive match failed on HttpResult<T>.")
    };

    /// <summary>
    /// Returns the value if successful, <paramref name="fallback"/> otherwise.
    /// Use only for optional/non-critical metadata.
    /// <b>Do NOT use to decide deletion eligibility.</b>
    /// </summary>
    public T OrDefault(T fallback) => this is Success s ? s.Value : fallback;
}

/// <summary>
/// Marker response type for HTTP operations that return no meaningful body
/// (e.g. DELETE 200/204, unmonitor PUT with empty response).
/// </summary>
public sealed record EmptyResponse;
