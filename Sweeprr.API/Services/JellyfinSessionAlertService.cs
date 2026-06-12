using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <summary>
/// Singleton orchestrator for Story 10.2 session alerts. Uses
/// <see cref="IServiceScopeFactory"/> to resolve scoped DB/client dependencies per call,
/// since it is invoked from other singletons (<c>JellyfinWebSocketService</c>,
/// <c>SchedulerHostedService</c>).
///
/// All Jellyfin calls are best-effort: failures are logged and swallowed, never thrown —
/// alerts must never crash the WebSocket loop or the scheduler tick.
/// </summary>
public sealed class JellyfinSessionAlertService : IJellyfinSessionAlertService
{
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(15);

    private const string NoticeHeader = "Sweeprr Notice";
    private const string PreSweepMessage =
        "Sweeprr cleanup will begin in 10 minutes. Some content may be removed.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JellyfinSessionAlertService> _logger;

    // (sessionId, itemId) -> last alert time. In-memory only — a UX throttle, not a
    // safety mechanism, so resetting on restart is fine.
    private readonly ConcurrentDictionary<(string SessionId, string ItemId), DateTimeOffset> _lastAlerted = new();

    public JellyfinSessionAlertService(
        IServiceScopeFactory scopeFactory,
        ILogger<JellyfinSessionAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task ProcessSessionsUpdateAsync(
        int connectionId, IReadOnlyList<JellyfinSession> sessions, CancellationToken ct = default)
    {
        if (sessions.Count == 0) return;

        var nowPlayingIds = sessions
            .Where(s => !string.IsNullOrEmpty(s.NowPlayingItemId))
            .Select(s => s.NowPlayingItemId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (nowPlayingIds.Count == 0) return;

        using var scope   = _scopeFactory.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var clientFactory = scope.ServiceProvider.GetRequiredService<IIntegrationClientFactory>();

        var settings = await db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is null || !settings.JellyfinSessionAlertsEnabled)
            return;

        var queuedItems = await db.SweepItems
            .Where(s => (s.Status == SweepItemStatus.Pending || s.Status == SweepItemStatus.Approved)
                     && nowPlayingIds.Contains(s.MediaServerItemId))
            .ToListAsync(ct);

        if (queuedItems.Count == 0) return;

        var queuedByItemId = queuedItems
            .GroupBy(s => s.MediaServerItemId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        JellyfinClient? client = null;

        foreach (var session in sessions)
        {
            if (session.NowPlayingItemId is not { Length: > 0 } itemId) continue;
            if (!queuedByItemId.TryGetValue(itemId, out var item)) continue;
            if (!TryRegisterAlert(session.Id, itemId, now, AlertCooldown)) continue;

            client ??= await clientFactory.CreateJellyfinClientAsync(connectionId, ct);
            if (client is null)
            {
                _logger.LogWarning(
                    "SessionAlert: could not build Jellyfin client for connection {ConnectionId}", connectionId);
                return;
            }

            var text = $"⚠ '{item.Title}' is scheduled for cleanup. It may be removed soon.";
            var result = await client.SendSessionMessageAsync(session.Id, NoticeHeader, text, ct: ct);
            if (result is not HttpResult<EmptyResponse>.Success)
                _logger.LogDebug(
                    "SessionAlert: failed to message session {SessionId} for item {ItemId}: {Result}",
                    session.Id, itemId, result);
        }
    }

    public async Task BroadcastPreSweepWarningAsync(CancellationToken ct = default)
    {
        using var scope   = _scopeFactory.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var clientFactory = scope.ServiceProvider.GetRequiredService<IIntegrationClientFactory>();
        var connService   = scope.ServiceProvider.GetRequiredService<IConnectionService>();

        var settings = await db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is null || !settings.PreSweepBroadcastEnabled)
            return;

        var connections  = await connService.GetAllAsync();
        var jellyfinConn = connections.FirstOrDefault(c => c.Type == ConnectionType.Jellyfin && c.IsEnabled);
        if (jellyfinConn is null)
        {
            _logger.LogDebug("PreSweepBroadcast: no enabled Jellyfin connection, skipping");
            return;
        }

        var client = await clientFactory.CreateJellyfinClientAsync(jellyfinConn.Id, ct);
        if (client is null)
        {
            _logger.LogWarning(
                "PreSweepBroadcast: could not build Jellyfin client for connection {ConnectionId}", jellyfinConn.Id);
            return;
        }

        var sessionsResult = await client.GetActiveSessionsAsync(ct);
        if (sessionsResult is not HttpResult<IReadOnlyList<JellyfinSession>>.Success sessionsOk)
        {
            _logger.LogWarning("PreSweepBroadcast: failed to fetch active sessions: {Result}", sessionsResult);
            return;
        }

        var sent = 0;
        foreach (var session in sessionsOk.Value)
        {
            if (string.IsNullOrEmpty(session.UserId)) continue;

            var result = await client.SendSessionMessageAsync(session.Id, NoticeHeader, PreSweepMessage, ct: ct);
            if (result is HttpResult<EmptyResponse>.Success) sent++;
        }

        _logger.LogInformation("PreSweepBroadcast: sent warning to {Count} session(s)", sent);
    }

    /// <summary>
    /// Returns true and records <paramref name="now"/> if no alert was sent for this
    /// (session, item) pair within <paramref name="cooldown"/>; otherwise returns false.
    /// Takes <paramref name="now"/> explicitly so cooldown behavior is unit-testable
    /// without waiting on real time.
    /// </summary>
    internal bool TryRegisterAlert(string sessionId, string itemId, DateTimeOffset now, TimeSpan cooldown)
    {
        var key = (sessionId, itemId);
        if (_lastAlerted.TryGetValue(key, out var last) && now - last < cooldown)
            return false;

        _lastAlerted[key] = now;
        return true;
    }
}
