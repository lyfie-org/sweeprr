using System.Net.Http.Json;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <inheritdoc cref="INotificationProvider"/>
/// <remarks>POSTs <c>{ event, timestamp, data }</c> to an arbitrary JSON webhook endpoint.</remarks>
public sealed class GenericWebhookNotificationProvider : INotificationProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GenericWebhookNotificationProvider> _logger;

    public NotificationProviderType ProviderType => NotificationProviderType.GenericWebhook;

    public GenericWebhookNotificationProvider(
        IHttpClientFactory httpClientFactory, ILogger<GenericWebhookNotificationProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string webhookUrl, NotificationPayload payload, CancellationToken ct = default)
    {
        var body = new
        {
            @event = EventName(payload.Trigger),
            timestamp = payload.Timestamp.UtcDateTime.ToString("o"),
            data = payload.Data,
        };

        try
        {
            var client = _httpClientFactory.CreateClient("Webhook");
            using var response = await client.PostAsJsonAsync(webhookUrl, body, NotificationJsonOptions.Default, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generic webhook delivery failed");
            return false;
        }
    }

    private static string EventName(NotificationTrigger trigger) => trigger switch
    {
        NotificationTrigger.SweepComplete   => "sweep_complete",
        NotificationTrigger.FailsafeTripped => "failsafe_tripped",
        NotificationTrigger.PendingItems    => "pending_items",
        NotificationTrigger.ConnectionError => "connection_error",
        _ => trigger.ToString(),
    };
}
