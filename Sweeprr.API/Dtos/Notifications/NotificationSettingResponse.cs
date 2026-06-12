using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Notifications;

public record NotificationSettingResponse(
    int Id,
    string Name,
    NotificationProviderType ProviderType,
    string MaskedWebhookUrl,
    bool IsEnabled,
    bool TriggerOnFailsafe,
    bool TriggerOnSweepComplete,
    bool TriggerOnPendingItems,
    bool TriggerOnConnectionError,
    DateTime CreatedAt);
