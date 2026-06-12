namespace Sweeprr.API.Models;

public class NotificationSetting
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public NotificationProviderType ProviderType { get; set; }

    /// <summary>Webhook URL, encrypted via <see cref="Sweeprr.API.Services.ISecretProtector"/> — treated as a credential.</summary>
    public string WebhookUrlEncrypted { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public bool TriggerOnFailsafe { get; set; } = true;
    public bool TriggerOnSweepComplete { get; set; } = true;
    public bool TriggerOnPendingItems { get; set; }
    public bool TriggerOnConnectionError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
