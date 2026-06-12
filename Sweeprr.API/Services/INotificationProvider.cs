using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <summary>Sends a <see cref="NotificationPayload"/> to a single webhook URL for one provider type.</summary>
public interface INotificationProvider
{
    NotificationProviderType ProviderType { get; }

    /// <summary>Posts the payload to <paramref name="webhookUrl"/>. Never throws — returns false on any failure.</summary>
    Task<bool> SendAsync(string webhookUrl, NotificationPayload payload, CancellationToken ct = default);
}
