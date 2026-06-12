using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Notifications;

/// <summary>Unit tests for <see cref="DiscordNotificationProvider"/> — embed shape, color mapping, and failure handling.</summary>
public class DiscordNotificationProviderTests
{
    private const string WebhookUrl = "https://discord.com/api/webhooks/123456789/abcdefghijklmnop";

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
        var (provider, _) = MakeProvider(500);

        var ok = await provider.SendAsync(WebhookUrl, SamplePayload(), CancellationToken.None);

        Assert.False(ok);
    }

    [Fact]
    public async Task SendAsync_TransportException_ReturnsFalseWithoutThrowing()
    {
        var provider = new DiscordNotificationProvider(
            new FakeHttpClientFactory(new ThrowingHandler()), NullLogger<DiscordNotificationProvider>.Instance);

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
    public async Task SendAsync_BuildsEmbedWithTitleAndFields()
    {
        var (provider, capture) = MakeProvider(204);
        var payload = new NotificationPayload(
            NotificationTrigger.SweepComplete,
            "🧹 Sweep Complete",
            [("Swept", "5"), ("Failed", "0")],
            new { swept = 5, failed = 0 },
            DateTimeOffset.UtcNow);

        await provider.SendAsync(WebhookUrl, payload, CancellationToken.None);

        var json = JsonDocument.Parse(capture.LastRequestBody!);
        var embed = json.RootElement.GetProperty("embeds")[0];

        Assert.Equal("🧹 Sweep Complete", embed.GetProperty("title").GetString());

        var fields = embed.GetProperty("fields");
        Assert.Equal(2, fields.GetArrayLength());
        Assert.Equal("Swept", fields[0].GetProperty("name").GetString());
        Assert.Equal("5", fields[0].GetProperty("value").GetString());
        Assert.True(fields[0].GetProperty("inline").GetBoolean());
        Assert.Equal("Failed", fields[1].GetProperty("name").GetString());
        Assert.Equal("0", fields[1].GetProperty("value").GetString());
    }

    [Theory]
    [InlineData(NotificationTrigger.SweepComplete, 0x57F287)]
    [InlineData(NotificationTrigger.FailsafeTripped, 0xED4245)]
    [InlineData(NotificationTrigger.PendingItems, 0x5865F2)]
    [InlineData(NotificationTrigger.ConnectionError, 0xED4245)]
    public async Task SendAsync_UsesTriggerSpecificEmbedColor(NotificationTrigger trigger, int expectedColor)
    {
        var (provider, capture) = MakeProvider(204);
        var payload = new NotificationPayload(trigger, "Title", [], new { }, DateTimeOffset.UtcNow);

        await provider.SendAsync(WebhookUrl, payload, CancellationToken.None);

        var json = JsonDocument.Parse(capture.LastRequestBody!);
        var color = json.RootElement.GetProperty("embeds")[0].GetProperty("color").GetInt32();

        Assert.Equal(expectedColor, color);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private static (DiscordNotificationProvider Provider, CapturingHandler Capture) MakeProvider(int statusCode)
    {
        var capture = new CapturingHandler(statusCode);
        var provider = new DiscordNotificationProvider(
            new FakeHttpClientFactory(capture), NullLogger<DiscordNotificationProvider>.Instance);
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
