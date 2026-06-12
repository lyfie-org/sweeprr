using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <summary>
/// Singleton background worker that drains <see cref="NotificationDispatchRequest"/>s from
/// the shared channel and delivers them to every enabled <see cref="NotificationSetting"/>
/// whose trigger flags match. Webhook I/O happens entirely here, decoupled from the
/// sweep/scan pipeline that raised the event — delivery failures are logged and swallowed.
/// </summary>
public sealed class NotificationDispatchWorker : BackgroundService
{
    private readonly Channel<NotificationDispatchRequest> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnumerable<INotificationProvider> _providers;
    private readonly ILogger<NotificationDispatchWorker> _logger;

    public NotificationDispatchWorker(
        Channel<NotificationDispatchRequest> channel,
        IServiceScopeFactory scopeFactory,
        IEnumerable<INotificationProvider> providers,
        ILogger<NotificationDispatchWorker> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _providers = providers;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification dispatch failed for trigger {Trigger}", request.Trigger);
            }
        }
    }

    private async Task DispatchAsync(NotificationDispatchRequest request, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

        var settings = await db.NotificationSettings
            .AsNoTracking()
            .Where(n => n.IsEnabled)
            .ToListAsync(ct);

        foreach (var setting in settings.Where(s => MatchesTrigger(s, request.Trigger)))
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderType == setting.ProviderType);
            if (provider is null)
            {
                _logger.LogWarning("No notification provider registered for {ProviderType}", setting.ProviderType);
                continue;
            }

            var url = protector.Unprotect(setting.WebhookUrlEncrypted);
            if (string.IsNullOrEmpty(url))
            {
                _logger.LogWarning(
                    "Notification setting {Id} ({Name}) has an unreadable webhook URL — skipping",
                    setting.Id, setting.Name);
                continue;
            }

            var ok = await provider.SendAsync(url, request.Payload, ct);
            if (!ok)
                _logger.LogWarning(
                    "Notification delivery failed: setting {Id} ({Name}), trigger {Trigger}",
                    setting.Id, setting.Name, request.Trigger);
        }
    }

    private static bool MatchesTrigger(NotificationSetting setting, NotificationTrigger trigger) => trigger switch
    {
        NotificationTrigger.SweepComplete   => setting.TriggerOnSweepComplete,
        NotificationTrigger.FailsafeTripped => setting.TriggerOnFailsafe,
        NotificationTrigger.PendingItems    => setting.TriggerOnPendingItems,
        NotificationTrigger.ConnectionError => setting.TriggerOnConnectionError,
        _ => false,
    };
}
