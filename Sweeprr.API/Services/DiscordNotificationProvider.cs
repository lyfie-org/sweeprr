using System.Net.Http.Json;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <inheritdoc cref="INotificationProvider"/>
/// <remarks>Builds a single Discord embed from the payload and POSTs it to a Discord webhook URL.</remarks>
public sealed class DiscordNotificationProvider : INotificationProvider
{
    private static readonly Dictionary<NotificationTrigger, int> EmbedColors = new()
    {
        [NotificationTrigger.SweepComplete]    = 0x57F287, // green
        [NotificationTrigger.FailsafeTripped]  = 0xED4245, // red
        [NotificationTrigger.PendingItems]     = 0x5865F2, // blurple
        [NotificationTrigger.ConnectionError]  = 0xED4245, // red
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DiscordNotificationProvider> _logger;

    public NotificationProviderType ProviderType => NotificationProviderType.Discord;

    public DiscordNotificationProvider(
        IHttpClientFactory httpClientFactory, ILogger<DiscordNotificationProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string webhookUrl, NotificationPayload payload, CancellationToken ct = default)
    {
        var body = new
        {
            embeds = new[]
            {
                new
                {
                    title = payload.Title,
                    color = EmbedColors.GetValueOrDefault(payload.Trigger, 0x5865F2),
                    fields = payload.Fields
                        .Select(f => new { name = f.Name, value = f.Value, inline = true })
                        .ToArray(),
                    timestamp = payload.Timestamp.UtcDateTime.ToString("o"),
                    footer = new { text = "Sweeprr V1.1" },
                }
            }
        };

        try
        {
            var client = _httpClientFactory.CreateClient("Webhook");
            using var response = await client.PostAsJsonAsync(webhookUrl, body, NotificationJsonOptions.Default, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord webhook delivery failed");
            return false;
        }
    }
}
