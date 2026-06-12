namespace Sweeprr.API.Dtos.Notifications;

/// <summary>Partial update — null fields are left unchanged. <c>WebhookUrl</c> omitted keeps the existing stored URL.</summary>
public record UpdateNotificationSettingRequest(
    string? Name,
    string? WebhookUrl,
    bool? IsEnabled,
    bool? TriggerOnFailsafe,
    bool? TriggerOnSweepComplete,
    bool? TriggerOnPendingItems,
    bool? TriggerOnConnectionError);
