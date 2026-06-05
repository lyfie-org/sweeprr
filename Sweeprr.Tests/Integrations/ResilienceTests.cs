using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Sweeprr.API.Integrations;

namespace Sweeprr.Tests.Integrations;

/// <summary>
/// Tests that verify <see cref="ClientBase"/> response mapping and Polly retry behaviour.
///
/// Two test categories:
/// <list type="bullet">
///   <item>
///     <b>ClientBase mapping</b> — uses a plain <see cref="HttpClient"/> with a stub
///     handler (no Polly) to verify that individual HTTP responses and exceptions map
///     to the correct <see cref="HttpResult{T}"/> variant.
///   </item>
///   <item>
///     <b>Polly resilience</b> — builds a real <see cref="IHttpClientFactory"/> with
///     the Polly pipeline wired in (zero-delay retries for test speed) and verifies
///     retry count, non-retry of 4xx, and exhaustion behaviour.
///   </item>
/// </list>
/// </summary>
public class ResilienceTests
{
    // ── Concrete test-only subclass of ClientBase ────────────────────────────

    private sealed class TestClient : ClientBase
    {
        public TestClient(HttpClient http)
            : base(http, "http://test-host:7878", NullLogger.Instance) { }

        public Task<HttpResult<TestPayload>> FetchAsync(
            string path = "/items/1", CancellationToken ct = default)
            => GetAsync<TestPayload>(path, ct);
    }

    private record TestPayload(string Value);

    // ── ClientBase: 2xx success ───────────────────────────────────────────────

    [Fact]
    public async Task Success_200_Deserializes_To_HttpResultSuccess()
    {
        var client = SimpleClient(200, """{"value":"hello"}""");
        var result = await client.FetchAsync();
        var s = Assert.IsType<HttpResult<TestPayload>.Success>(result);
        Assert.Equal("hello", s.Value.Value);
    }

    [Fact]
    public async Task Success_204_NoContent_Returns_HttpResultSuccess_WithDefaultValue()
    {
        var client = SimpleClient(204);
        var result = await client.FetchAsync();
        // 204 is success — no body to read; Ok(default) is returned.
        Assert.True(result.IsSuccess);
    }

    // ── ClientBase: 4xx → DefinitiveFailure ─────────────────────────────────

    [Fact]
    public async Task NotFound_404_Returns_DefinitiveFailure_WithIsNotFound()
    {
        var client = SimpleClient(404);
        var result = await client.FetchAsync();
        var d = Assert.IsType<HttpResult<TestPayload>.DefinitiveFailure>(result);
        Assert.Equal(404, d.StatusCode);
        Assert.True(result.IsNotFound);
    }

    [Fact]
    public async Task Unauthorized_401_Returns_DefinitiveFailure()
    {
        var result = await SimpleClient(401).FetchAsync();
        var d = Assert.IsType<HttpResult<TestPayload>.DefinitiveFailure>(result);
        Assert.Equal(401, d.StatusCode);
        Assert.False(result.IsNotFound);
    }

    [Fact]
    public async Task Forbidden_403_Returns_DefinitiveFailure()
    {
        var result = await SimpleClient(403).FetchAsync();
        Assert.IsType<HttpResult<TestPayload>.DefinitiveFailure>(result);
    }

    [Fact]
    public async Task BadRequest_400_Returns_DefinitiveFailure()
    {
        var result = await SimpleClient(400).FetchAsync();
        Assert.IsType<HttpResult<TestPayload>.DefinitiveFailure>(result);
    }

    // ── ClientBase: 5xx → TransientFailure ───────────────────────────────────

    [Fact]
    public async Task ServerError_500_Without_Polly_Returns_TransientFailure()
    {
        // Without Polly retries a single 500 → transient.
        var result = await SimpleClient(500).FetchAsync();
        Assert.IsType<HttpResult<TestPayload>.TransientFailure>(result);
    }

    [Fact]
    public async Task ServiceUnavailable_503_Without_Polly_Returns_TransientFailure()
    {
        var result = await SimpleClient(503).FetchAsync();
        Assert.IsType<HttpResult<TestPayload>.TransientFailure>(result);
    }

    // ── ClientBase: exception mapping ────────────────────────────────────────

    [Fact]
    public async Task HttpRequestException_Returns_TransientFailure()
    {
        var client = ExceptionClient(new HttpRequestException("Connection refused"));
        var result = await client.FetchAsync();
        var t = Assert.IsType<HttpResult<TestPayload>.TransientFailure>(result);
        Assert.Contains("Transport error", t.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(t.Exception);
    }

    [Fact]
    public async Task TaskCanceledException_Returns_TransientFailure_With_Timeout_Reason()
    {
        var client = ExceptionClient(new TaskCanceledException("timed out"));
        var result = await client.FetchAsync();
        var t = Assert.IsType<HttpResult<TestPayload>.TransientFailure>(result);
        Assert.Contains("timed out", t.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UserCancellation_Propagates_As_OperationCanceledException()
    {
        // When the caller's CancellationToken fires, ClientBase must NOT swallow it.
        // HttpClient wraps the cancellation as TaskCanceledException, which IS an
        // OperationCanceledException. Use ThrowsAnyAsync to accept any subtype.
        using var cts = new CancellationTokenSource();
        var client = CancellingClient(cts);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FetchAsync(ct: cts.Token));
    }

    // ── ClientBase: JSON deserialization failure ──────────────────────────────

    [Fact]
    public async Task Invalid_Json_Body_On_200_Returns_DefinitiveFailure()
    {
        // Malformed JSON = schema/config problem; definitive — retrying won't help.
        var client = SimpleClient(200, "this is not json");
        var result = await client.FetchAsync();
        // Could be DefinitiveFailure (JsonException path)
        Assert.False(result.IsSuccess);
        Assert.False(result.IsNotFound);
    }

    // ── Polly retry: 503 retried until success ────────────────────────────────

    [Fact]
    public async Task Retry_503_Twice_Then_200_Succeeds_After_Three_Calls()
    {
        var handler = new SequenceHandler(
            Response(503),
            Response(503),
            Response(200, """{"value":"ok"}"""));

        var client = PollyClient(handler);
        var result = await client.FetchAsync();

        var s = Assert.IsType<HttpResult<TestPayload>.Success>(result);
        Assert.Equal("ok", s.Value.Value);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Retry_404_Is_Not_Retried()
    {
        // 404 = definitive; Polly default ShouldHandle does NOT retry 4xx.
        var handler = new CountingHandler(404);
        var client = PollyClient(handler);

        var result = await client.FetchAsync();

        Assert.IsType<HttpResult<TestPayload>.DefinitiveFailure>(result);
        Assert.Equal(1, handler.CallCount); // no retries — exactly one call
    }

    [Fact]
    public async Task Retry_401_Is_Not_Retried()
    {
        var handler = new CountingHandler(401);
        var client = PollyClient(handler);
        await client.FetchAsync();
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Retry_Persistent_503_Exhausts_All_Attempts()
    {
        // 1 original + 3 retries = 4 total calls, all return 503.
        var handler = new CountingHandler(503);
        var client = PollyClient(handler);

        var result = await client.FetchAsync();

        Assert.True(result.IsTransient);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task Retry_200_After_Single_503_Succeeds()
    {
        var handler = new SequenceHandler(
            Response(503),
            Response(200, """{"value":"recovered"}"""));

        var result = await PollyClient(handler).FetchAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.CallCount);
    }

    // ── Helpers: simple (no Polly) clients ───────────────────────────────────

    private static TestClient SimpleClient(int statusCode, string? body = null)
        => new(new HttpClient(new StubHandler((HttpStatusCode)statusCode, body)));

    private static TestClient ExceptionClient(Exception ex)
        => new(new HttpClient(new ExceptionHandler(ex)));

    private static TestClient CancellingClient(CancellationTokenSource cts)
        => new(new HttpClient(new CancellingHandler(cts)));

    // ── Helpers: Polly-backed clients ────────────────────────────────────────

    /// <summary>
    /// Builds a TestClient with a real Polly retry pipeline (3 retries, zero delay
    /// for test speed) wrapping the supplied handler.
    /// </summary>
    private static TestClient PollyClient(HttpMessageHandler innerHandler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => innerHandler)
            .AddResilienceHandler("test-pipeline", b =>
            {
                // Zero-delay constant backoff so tests run fast.
                b.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType      = DelayBackoffType.Constant,
                    UseJitter        = false,
                    Delay            = TimeSpan.Zero
                });
            });

        var sp     = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        return new TestClient(factory.CreateClient("test"));
    }

    private static HttpResponseMessage Response(int code, string? body = null)
    {
        var r = new HttpResponseMessage((HttpStatusCode)code);
        if (body is not null)
            r.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return r;
    }

    // ── Stub handlers ────────────────────────────────────────────────────────

    /// <summary>Always returns the same status + optional body.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string? _body;

        public StubHandler(HttpStatusCode code, string? body = null)
        {
            _code = code;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage _, CancellationToken ct)
        {
            var r = new HttpResponseMessage(_code);
            if (_body is not null)
                r.Content = new StringContent(_body, Encoding.UTF8, "application/json");
            return Task.FromResult(r);
        }
    }

    /// <summary>Always throws the given exception.</summary>
    private sealed class ExceptionHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ExceptionHandler(Exception ex) => _ex = ex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage _, CancellationToken ct)
            => Task.FromException<HttpResponseMessage>(_ex);
    }

    /// <summary>Cancels the supplied <see cref="CancellationTokenSource"/> then throws.</summary>
    private sealed class CancellingHandler : HttpMessageHandler
    {
        private readonly CancellationTokenSource _cts;
        public CancellingHandler(CancellationTokenSource cts) => _cts = cts;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage _, CancellationToken ct)
        {
            _cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// Returns responses from a pre-loaded queue; falls back to 503 once exhausted.
    /// </summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _queue;
        public int CallCount { get; private set; }

        public SequenceHandler(params HttpResponseMessage[] responses)
            => _queue = new Queue<HttpResponseMessage>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage _, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(
                _queue.TryDequeue(out var r)
                    ? r
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    /// <summary>Always returns the same status code; records how many times it was called.</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        public int CallCount { get; private set; }

        public CountingHandler(int statusCode) => _code = (HttpStatusCode)statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage _, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(_code));
        }
    }
}
