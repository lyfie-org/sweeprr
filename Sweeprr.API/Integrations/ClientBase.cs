using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sweeprr.API.Integrations;

/// <summary>
/// Abstract base for all external-service HTTP clients. Provides:
/// <list type="bullet">
///   <item>JSON GET / POST / PUT / DELETE helpers returning <see cref="HttpResult{T}"/>.</item>
///   <item>Consistent exception → transient mapping (Polly exhausted retries, circuit open, etc.).</item>
///   <item>4xx → <see cref="HttpResult{T}.DefinitiveFailure"/>; 5xx (post-retry) → <see cref="HttpResult{T}.TransientFailure"/>.</item>
///   <item>Structured log hooks on every call.</item>
/// </list>
/// Polly's retry / circuit-breaker pipeline runs transparently inside the injected
/// <see cref="HttpClient"/>. This class only sees the final outcome.
/// </summary>
public abstract class ClientBase
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly ILogger _logger;

    protected ClientBase(HttpClient http, string baseUrl, ILogger logger)
    {
        _http    = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger  = logger;
    }

    // ── HTTP helpers (protected for use by concrete clients) ─────────────────

    protected Task<HttpResult<T>> GetAsync<T>(
        string path, CancellationToken ct = default)
        => SendAsync<T>(HttpMethod.Get, path, body: null, ct);

    protected Task<HttpResult<T>> PostAsync<T>(
        string path, object? body, CancellationToken ct = default)
        => SendAsync<T>(HttpMethod.Post, path, body, ct);

    protected Task<HttpResult<T>> PutAsync<T>(
        string path, object? body, CancellationToken ct = default)
        => SendAsync<T>(HttpMethod.Put, path, body, ct);

    protected Task<HttpResult<T>> DeleteAsync<T>(
        string path, CancellationToken ct = default)
        => SendAsync<T>(HttpMethod.Delete, path, body: null, ct);

    /// <summary>
    /// DELETE overload for endpoints that return no meaningful body.
    /// </summary>
    protected Task<HttpResult<EmptyResponse>> DeleteAsync(
        string path, CancellationToken ct = default)
        => SendAsync<EmptyResponse>(HttpMethod.Delete, path, body: null, ct);

    // ── Core dispatch ────────────────────────────────────────────────────────

    private async Task<HttpResult<T>> SendAsync<T>(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var url = BuildUrl(path);

        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
                request.Content = JsonContent.Create(body, options: SerializerOptions);

            _logger.LogDebug("[{Client}] → {Method} {Url}",
                GetType().Name, method.Method, url);

            using var response = await _http.SendAsync(request, ct);

            _logger.LogDebug("[{Client}] ← {Method} {Url} HTTP {Status}",
                GetType().Name, method.Method, url, (int)response.StatusCode);

            return await MapResponseAsync<T>(response);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller cancelled — propagate; this is not a service fault.
            throw;
        }
        catch (JsonException ex)
        {
            // Valid HTTP 2xx but body failed to deserialize → config/schema problem.
            // Definitive: retrying won't help until the service or our DTO changes.
            _logger.LogWarning(ex,
                "[{Client}] JSON deserialization failed for {Method} {Url}: {Message}",
                GetType().Name, method.Method, url, ex.Message);
            return HttpResult<T>.Definitive(
                null, $"Response from {url} could not be deserialized: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Everything else: network errors, Polly circuit open, exhausted retries, etc.
            var reason = ClassifyException(ex);
            _logger.LogWarning(ex,
                "[{Client}] Transient failure {Method} {Url}: {Reason}",
                GetType().Name, method.Method, url, reason);
            return HttpResult<T>.Transient(reason, ex);
        }
    }

    // ── Response mapping ─────────────────────────────────────────────────────

    private static async Task<HttpResult<T>> MapResponseAsync<T>(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            // 204 No Content or the caller asked for EmptyResponse → skip body read.
            if (response.StatusCode == HttpStatusCode.NoContent
                || typeof(T) == typeof(EmptyResponse))
            {
                return HttpResult<T>.Ok(default!);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(SerializerOptions);
            return HttpResult<T>.Ok(value!);
        }

        var snippet = await ReadSnippetAsync(response);

        // 4xx = definitive: the service gave a clear, permanent answer.
        // 5xx that survived all Polly retries = transient.
        return IsDefinitiveStatus(code)
            ? HttpResult<T>.Definitive(code, $"HTTP {code}: {snippet}")
            : HttpResult<T>.Transient($"HTTP {code}: {snippet}");
    }

    /// <summary>All 4xx responses are definitive — retrying won't change the answer.</summary>
    private static bool IsDefinitiveStatus(int code) => code is >= 400 and < 500;

    private static async Task<string> ReadSnippetAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            return body.Length > 200 ? body[..200] : body;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ── Exception classification ─────────────────────────────────────────────

    /// <summary>
    /// Maps exceptions to human-readable transient-failure reasons.
    /// All exceptions reaching here are treated as transient (Polly has already
    /// retried, the circuit is open, or a transport error occurred).
    /// </summary>
    private static string ClassifyException(Exception ex)
    {
        // HttpClient.Timeout or Polly AttemptTimeout → TaskCanceledException
        // wrapping TimeoutException, or a plain TaskCanceledException.
        if (ex is TaskCanceledException tce)
            return tce.InnerException is TimeoutException
                ? "Request timed out"
                : "Request timed out";

        // DNS failures, connection refused, all Polly retries exhausted on transport error.
        if (ex is HttpRequestException hre)
        {
            var inner = hre.InnerException?.Message ?? hre.Message;
            return $"Transport error: {inner}";
        }

        // Polly v8 circuit-breaker open: BrokenCircuitException<HttpResponseMessage>
        // or IsolatedCircuitException. Check by name to avoid a direct Polly type dependency.
        var typeName = ex.GetType().Name;
        if (typeName.Contains("BrokenCircuit") || typeName.Contains("IsolatedCircuit"))
            return "Circuit breaker is open — service has too many recent failures";

        // Polly AttemptTimeout fired before the request completed.
        if (typeName.Contains("TimeoutRejected"))
            return "Request timed out (attempt timeout)";

        return $"Unexpected error ({ex.GetType().Name}): {ex.Message}";
    }

    // ── URL construction ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds an absolute URL. Accepts:
    /// <list type="bullet">
    ///   <item>Already-absolute URLs (http:// / https://) — returned as-is.</item>
    ///   <item>Paths beginning with '/' — appended to base URL.</item>
    ///   <item>Relative paths — joined with base URL and a separator '/'.</item>
    /// </list>
    /// </summary>
    protected string BuildUrl(string path)
    {
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
         || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return path;

        return path.StartsWith('/')
            ? $"{_baseUrl}{path}"
            : $"{_baseUrl}/{path}";
    }

    // ── Shared JSON options ──────────────────────────────────────────────────

    /// <summary>
    /// Lenient options shared across all typed clients — case-insensitive, camelCase,
    /// unknown fields ignored, trailing commas/comments tolerated.
    /// Exposed as internal so tests can reuse the same options.
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive  = true,
        PropertyNamingPolicy         = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition       = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas          = true,
        ReadCommentHandling          = JsonCommentHandling.Skip,
        NumberHandling               = JsonNumberHandling.AllowReadingFromString
    };
}
