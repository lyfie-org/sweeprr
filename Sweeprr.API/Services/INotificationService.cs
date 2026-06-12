using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <summary>
/// Entry point for raising notification events from the sweep/scan pipeline.
/// Enqueues onto a shared channel for async delivery by <see cref="NotificationDispatchWorker"/> —
/// never performs webhook I/O itself, so callers are never blocked or affected by delivery failures.
/// </summary>
public interface INotificationService
{
    void Notify(NotificationTrigger trigger, NotificationPayload payload);
}
