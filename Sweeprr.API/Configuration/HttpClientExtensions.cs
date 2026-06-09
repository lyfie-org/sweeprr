using Microsoft.Extensions.Http.Resilience;
using Polly;
using Sweeprr.API.Integrations;

namespace Sweeprr.API.Configuration;

/// <summary>
/// Registers named <see cref="HttpClient"/> pools and their Polly resilience pipelines
/// for the Jellyfin, Radarr, and Sonarr integrations.
///
/// <b>Two named clients per service type:</b>
/// <list type="bullet">
///   <item><c>"Jellyfin"</c> / <c>"Radarr"</c> / <c>"Sonarr"</c> — normal TLS validation.</item>
///   <item><c>"Jellyfin:insecure"</c> etc. — TLS bypass for self-signed certificates
///     (opt-in per connection via <c>AllowInsecure</c>).</item>
/// </list>
///
/// <b>Polly pipeline per client (innermost to outermost, i.e. execution order):</b>
/// <code>
///   Request
///     → AttemptTimeout (10 s per attempt)
///     → Retry (3 retries, exp backoff + jitter, transient errors only)
///     → CircuitBreaker (trip at 50% failure rate, 30 s break)
///     → HttpClient
/// </code>
/// The circuit breaker is keyed per named-client, giving independent state for
/// each service type. For multiple *arr instances sharing a named pool, the
/// circuit-breaker state is shared — acceptable for v1; per-host keying can be
/// added later via <c>ResiliencePipelineRegistry</c>.
/// </summary>
public static class HttpClientExtensions
{
    private static readonly string[] ServiceTypes = ["Jellyfin", "Radarr", "Sonarr", "Bazarr"];

    public static IServiceCollection AddSweeprrHttpClients(this IServiceCollection services)
    {
        foreach (var serviceType in ServiceTypes)
        {
            RegisterNamedClient(services, serviceType, allowInsecure: false);
            RegisterNamedClient(services, $"{serviceType}:insecure", allowInsecure: true);
        }

        services.AddScoped<IIntegrationClientFactory, IntegrationClientFactory>();

        return services;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void RegisterNamedClient(
        IServiceCollection services, string name, bool allowInsecure)
    {
        var builder = services.AddHttpClient(name);

        if (allowInsecure)
        {
            // Intentional: user explicitly opted in per-connection for self-signed certs.
            builder.ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                return handler;
            });
        }

        builder.AddResilienceHandler($"{name}-pipeline", ConfigureResiliencePipeline);
    }

    /// <summary>
    /// Configures the three-layer Polly resilience pipeline applied to every
    /// request made through these named clients.
    ///
    /// Execution order (applied innermost first):
    /// <list type="number">
    ///   <item><b>AttemptTimeout</b> — aborts a single attempt after 10 s.</item>
    ///   <item>
    ///     <b>Retry</b> — retries up to 3 times on transient failures (5xx, transport
    ///     errors, 408, 429) with exponential backoff + jitter. 4xx are NOT retried —
    ///     they are definitive answers.
    ///   </item>
    ///   <item>
    ///     <b>CircuitBreaker</b> — trips when ≥50% of the last 5 requests fail within
    ///     a 30-second sampling window. Stays open 30 s before allowing a trial request.
    ///     Prevents retry storms against an already-struggling host.
    ///   </item>
    /// </list>
    /// </summary>
    private static void ConfigureResiliencePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> pipeline)
    {
        // 1. Per-attempt timeout (applied before each individual attempt).
        pipeline.AddTimeout(TimeSpan.FromSeconds(10));

        // 2. Retry with exponential backoff + jitter.
        //    HttpRetryStrategyOptions defaults: retry on 5xx, 408, 429, HttpRequestException.
        //    4xx (definitive) are NOT in the default ShouldHandle predicate.
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType      = DelayBackoffType.Exponential,
            UseJitter        = true,
            Delay            = TimeSpan.FromSeconds(1),
            MaxDelay         = TimeSpan.FromSeconds(16)
        });

        // 3. Circuit breaker — protects against retry storms on a failing host.
        //    HttpCircuitBreakerStrategyOptions defaults: break on same conditions as retry.
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration  = TimeSpan.FromSeconds(30),
            MinimumThroughput = 5,
            FailureRatio      = 0.5,
            BreakDuration     = TimeSpan.FromSeconds(30)
        });
    }
}
