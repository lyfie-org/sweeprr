using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <summary>
/// Provider-agnostic notification content. <see cref="Fields"/> holds pre-formatted
/// display strings for chat-style providers (Discord embed fields); <see cref="Data"/>
/// holds raw, structured values for generic JSON webhooks.
/// </summary>
public sealed record NotificationPayload(
    NotificationTrigger Trigger,
    string Title,
    IReadOnlyList<(string Name, string Value)> Fields,
    object Data,
    DateTimeOffset Timestamp);

/// <summary>A queued notification awaiting async delivery by <see cref="NotificationDispatchWorker"/>.</summary>
public sealed record NotificationDispatchRequest(NotificationTrigger Trigger, NotificationPayload Payload);
