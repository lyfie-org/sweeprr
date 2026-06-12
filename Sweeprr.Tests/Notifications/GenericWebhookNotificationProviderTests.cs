using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Notifications;

/// <summary>Unit tests for <see cref="GenericWebhookNotificationProvider"/> — JSON body shape, event names, and failure handling.</summary>
public class GenericWebhookNotificationProviderTests
{
    private const string WebhookUrl = "https://example.com/hooks/sweeprr";

    [Fact]
    public async Task SendAsync_SuccessResponse_ReturnsTrue()
    {
        var (provider, _) = MakeProvider(200);

        var ok = await provider.SendAsync(WebhookUrl, SamplePayload(), CancellationToken.None);

        Assert.True(ok);
    }

    [Fact]
    public async Task SendAsync_NonSuccessStatusCode_ReturnsFalse()
    {
        var (provider, _) = MakeProvider(503);

        var ok = await provider.SendAsync(WebhookUrl, SamplePayload(), CancellationToken.None);

        Assert.False(ok);
    }

    [Fact]
    public async Task SendAsync_TransportException_ReturnsFalseWithoutThrowing()
    {
        var provider = new GenericWebhookNotificationProvider(
            new FakeHttpClientFactory(new ThrowingHandler()), NullLogger<GenericWebhookNotificationProvider>.Instance);

        var ok = await provider.SendAsync(WebhookUrl, SamplePayload(), CancellationToken.None);

        Assert.False(ok);
    }

    [Fact]
    public async Task SendAsync_PostsToProvidedWebhookUrl()
    {
        var (provider, capture) = MakeProvider(204);

        await provider.SendAsync(WebhookUrl, SamplePayload(), CancellationToken.None);

        Assert.Equal(WebhookUrl, capture.LastRequestUri);
    }

    [Fact]
    public async Task SendAsync_BodyContainsEventTimestampAndData()
    {
        var (provider, capture) = MakeProvider(204);
        var timestamp = DateTimeOffset.Parse("2026-06-12T10:00:00Z");
        var payload = new NotificationPayload(
            NotificationTrigger.SweepComplete,
            "🧹 Sweep Complete",
            [("Swept", "5")],
            new { swept = 5, failed = 0 },
            timestamp);

        await provider.SendAsync(WebhookUrl, payload, CancellationToken.None);

        var json = JsonDocument.Parse(capture.LastRequestBody!);
        var root = json.RootElement;

        Assert.Equal("sweep_complete", root.GetProperty("event").GetString());
        Assert.Equal("2026-06-12T10:00:00.0000000Z", root.GetProperty("timestamp").GetString());
        Assert.Equal(5, root.GetProperty("data").GetProperty("swept").GetInt32());
        Assert.Equal(0, root.GetProperty("data").GetProperty("failed").GetInt32());
    }

    [Theory]
    [InlineData(NotificationTrigger.SweepComplete, "sweep_complete")]
    [InlineData(NotificationTrigger.FailsafeTripped, "failsafe_tripped")]
    [InlineData(NotificationTrigger.PendingItems, "pending_items")]
    [InlineData(NotificationTrigger.ConnectionError, "connection_error")]
    public async Task SendAsync_MapsTriggerToEventName(NotificationTrigger trigger, string expectedEventName)
    {
        var (provider, capture) = MakeProvider(204);
        var payload = new NotificationPayload(trigger, "Title", [], new { }, DateTimeOffset.UtcNow);

        await provider.SendAsync(WebhookUrl, payload, CancellationToken.None);

        var json = JsonDocument.Parse(capture.LastRequestBody!);
        Assert.Equal(expectedEventName, json.RootElement.GetProperty("event").GetString());
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private static (GenericWebhookNotificationProvider Provider, CapturingHandler Capture) MakeProvider(int statusCode)
    {
        var capture = new CapturingHandler(statusCode);
        var provider = new GenericWebhookNotificationProvider(
            new FakeHttpClientFactory(capture), NullLogger<GenericWebhookNotificationProvider>.Instance);
        return (provider, capture);
    }

    private static NotificationPayload SamplePayload() => new(
        NotificationTrigger.SweepComplete,
        "Test",
        [("Key", "Value")],
        new { key = "value" },
        DateTimeOffset.UtcNow);

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;

        public string? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        public CapturingHandler(int code) => _code = (HttpStatusCode)code;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri?.ToString();
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(ct);

            return new HttpResponseMessage(_code);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("Simulated transport failure");
    }
}
