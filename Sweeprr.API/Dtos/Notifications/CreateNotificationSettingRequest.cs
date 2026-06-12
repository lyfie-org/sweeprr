using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Notifications;

public record CreateNotificationSettingRequest(
    string Name,
    NotificationProviderType ProviderType,
    string WebhookUrl,
    bool IsEnabled,
    bool TriggerOnFailsafe,
    bool TriggerOnSweepComplete,
    bool TriggerOnPendingItems,
    bool TriggerOnConnectionError);
