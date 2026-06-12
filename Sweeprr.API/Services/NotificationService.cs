using System.Threading.Channels;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <inheritdoc cref="INotificationService"/>
public sealed class NotificationService : INotificationService
{
    private readonly ChannelWriter<NotificationDispatchRequest> _writer;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        Channel<NotificationDispatchRequest> channel,
        ILogger<NotificationService> logger)
    {
        _writer = channel.Writer;
        _logger = logger;
    }

    public void Notify(NotificationTrigger trigger, NotificationPayload payload)
    {
        if (!_writer.TryWrite(new NotificationDispatchRequest(trigger, payload)))
            _logger.LogDebug("Notification queue rejected {Trigger} notification", trigger);
    }
}
