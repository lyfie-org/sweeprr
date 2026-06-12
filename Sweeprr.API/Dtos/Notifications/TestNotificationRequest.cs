using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Notifications;

/// <summary>Request to send a test payload to an in-progress (not yet saved) webhook configuration.</summary>
public record TestNotificationRequest(NotificationProviderType ProviderType, string WebhookUrl);

public record TestNotificationResponse(bool Success, string? Error);
