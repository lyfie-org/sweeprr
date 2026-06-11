using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations.Jellyfin.Dto;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.API.Integrations.Jellyfin.WebSocket;

/// <summary>
/// Persistent, self-healing WebSocket listener for the Jellyfin real-time event stream.
///
/// Lifecycle:
/// <list type="number">
///   <item>Queries the DB for the first enabled Jellyfin connection.</item>
///   <item>Connects to <c>ws(s)://host/socket?api_key=…&amp;deviceId=…</c>.</item>
///   <item>Sends <c>SessionsStart</c> to subscribe to the event stream.</item>
///   <item>Runs concurrent keep-alive and message loops.</item>
///   <item>On disconnect: waits with exponential backoff, then reconnects.</item>
///   <item>On every (re)connect: triggers a REST backfill to reconcile missed events.</item>
/// </list>
///
/// The DeviceId is intentionally identical to <see cref="JellyfinClient"/>'s constant —
/// Jellyfin must see one logical device for both REST and WebSocket interactions.
/// </summary>
public sealed class JellyfinWebSocketService : BackgroundService, IJellyfinWebSocketStatus
{
    // Must match JellyfinClient.DeviceId — Jellyfin ties sessions to DeviceId
    private const string DeviceId = "c4a1b2e3-d5f6-7890-abcd-ef1234567890";

    // Reconnect schedule: 2 → 4 → 8 → 16 → 30s (capped at 30s)
    private static readonly TimeSpan[] BackoffDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(30)
    ];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas         = true,
        ReadCommentHandling         = JsonCommentHandling.Skip
    };

    // Serializes concurrent sends (keep-alive loop + PlaybackStop re-fetch)
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // Thread-safe state visible to the status controller
    private volatile WsConnectionState _state = WsConnectionState.Disconnected;
    private DateTimeOffset? _lastConnectedAt;

    // Updated from ForceKeepAlive messages; read by the keep-alive loop
    private volatile int _keepAliveInterval = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlaystateCache      _cache;
    private readonly IPlaybackActivityWriter _writer;
    private readonly IJellyfinSessionAlertService _sessionAlerts;
    private readonly ILogger<JellyfinWebSocketService> _logger;

    private readonly ConcurrentDictionary<string, string> _userIdToUsernameMap = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lastPrunedAt;

    public WsConnectionState State            => _state;
    public DateTimeOffset?   LastConnectedAt  => _lastConnectedAt;

    public JellyfinWebSocketService(
        IServiceScopeFactory scopeFactory,
        IPlaystateCache      cache,
        IPlaybackActivityWriter writer,
        IJellyfinSessionAlertService sessionAlerts,
        ILogger<JellyfinWebSocketService> logger)
    {
        _scopeFactory  = scopeFactory;
        _cache         = cache;
        _writer        = writer;
        _sessionAlerts = sessionAlerts;
        _logger        = logger;
    }

    // ── BackgroundService entry point ────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadInitialStateAsync(stoppingToken).ConfigureAwait(false);

        var backoffIndex = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_lastPrunedAt == null || DateTime.UtcNow - _lastPrunedAt.Value > TimeSpan.FromDays(1))
            {
                _ = Task.Run(() => PruneActivitiesAsync(stoppingToken), stoppingToken);
            }
            var conn = await GetJellyfinConnectionAsync(stoppingToken).ConfigureAwait(false);

            if (conn is null)
            {
                _logger.LogDebug(
                    "No enabled Jellyfin connection found; WebSocket listener idle.");
                _state = WsConnectionState.Disconnected;
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
                continue;
            }

            _state = WsConnectionState.Connecting;
            _logger.LogInformation(
                "Jellyfin WebSocket: connecting to {BaseUrl}…", conn.BaseUrl);

            try
            {
                using var ws     = CreateWebSocket(conn.AllowInsecure);
                var       wsUri  = BuildWsUri(conn.BaseUrl, conn.ApiKey);

                await ws.ConnectAsync(wsUri, stoppingToken).ConfigureAwait(false);

                _state           = WsConnectionState.Connected;
                _lastConnectedAt = DateTimeOffset.UtcNow;
                backoffIndex     = 0;

                _logger.LogInformation("Jellyfin WebSocket connected.");

                // Subscribe to the session / playstate event stream
                await SendMessageAsync(
                    ws,
                    new JellyfinWsOutbound("SessionsStart", "0,1500"),
                    stoppingToken)
                    .ConfigureAwait(false);

                // Backfill runs concurrently — failures are non-fatal
                _ = BackfillAsync(conn.ConnectionId, stoppingToken);

                // Keep-alive and message loops run concurrently; either ending signals the other
                using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                var messageTask  = MessageLoopAsync(ws, conn.ConnectionId, loopCts.Token);
                var keepAliveTask = KeepAliveLoopAsync(ws, loopCts.Token);

                await Task.WhenAny(messageTask, keepAliveTask).ConfigureAwait(false);
                await loopCts.CancelAsync().ConfigureAwait(false);

                // Drain both tasks, swallowing cancellation / socket errors
                await Task
                    .WhenAll(
                        messageTask .ContinueWith(_ => { }, CancellationToken.None),
                        keepAliveTask.ContinueWith(_ => { }, CancellationToken.None))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Jellyfin WebSocket disconnected: {Message}", ex.Message);
            }

            if (stoppingToken.IsCancellationRequested) break;

            _state = WsConnectionState.Reconnecting;

            var delay = BackoffDelays[Math.Min(backoffIndex, BackoffDelays.Length - 1)];
            backoffIndex++;

            _logger.LogInformation(
                "Jellyfin WebSocket: reconnecting in {Seconds}s (attempt {Attempt})…",
                (int)delay.TotalSeconds, backoffIndex);

            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }

        _state = WsConnectionState.Disconnected;
    }

    // ── Message loop ─────────────────────────────────────────────────────────

    private async Task MessageLoopAsync(
        ClientWebSocket ws, int connectionId, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;

            // Accumulate frames until EndOfMessage (handles Jellyfin fragmented sends)
            do
            {
                result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogDebug("Jellyfin WebSocket received Close frame.");
                    return;
                }

                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            ms.Seek(0, SeekOrigin.Begin);
            ProcessMessage(ms, connectionId, ct);
        }
    }

    private void ProcessMessage(MemoryStream ms, int connectionId, CancellationToken ct)
    {
        JellyfinWsInbound? message;

        try
        {
            message = JsonSerializer.Deserialize<JellyfinWsInbound>(ms, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Jellyfin WS message.");
            return;
        }

        if (message is null) return;

        switch (message.MessageType)
        {
            case "ForceKeepAlive":
                UpdateKeepAliveInterval(message.Data);
                break;

            case "KeepAlive":
                // Server acknowledged our keep-alive — no action required
                break;

            case "UserDataChanged":
                if (message.Data.HasValue)
                    HandleUserDataChanged(message.Data.Value);
                break;

            case "PlaybackStop":
                if (message.Data.HasValue)
                    HandlePlaybackStop(message.Data.Value, connectionId, ct);
                break;

            case "Sessions":
                if (message.Data.HasValue)
                    HandleSessionsMessage(message.Data.Value, connectionId, ct);
                break;

            default:
                _logger.LogDebug(
                    "Jellyfin WS: ignoring message type '{Type}'", message.MessageType);
                break;
        }
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void UpdateKeepAliveInterval(JsonElement? data)
    {
        if (data is null) return;

        var interval = data.Value.ValueKind switch
        {
            JsonValueKind.Number => data.Value.GetInt32(),
            JsonValueKind.String when int.TryParse(data.Value.GetString(), out var n) => n,
            _ => -1
        };

        if (interval > 0)
        {
            _keepAliveInterval = interval;
            _logger.LogDebug(
                "Jellyfin ForceKeepAlive: keep-alive interval updated to {Interval}s.", interval);
        }
    }

    internal void HandleUserDataChanged(JsonElement data)
    {
        UserDataChangedData? changed;

        try { changed = data.Deserialize<UserDataChangedData>(JsonOpts); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize UserDataChanged payload.");
            return;
        }

        if (changed is null
            || string.IsNullOrEmpty(changed.UserId)
            || changed.UserDataList is not { Length: > 0 })
            return;

        _userIdToUsernameMap.TryGetValue(changed.UserId, out var username);
        username ??= changed.UserId;

        foreach (var item in changed.UserDataList)
        {
            var userData = new JellyfinUserData(
                Played:                item.Played,
                PlayCount:             item.PlayCount,
                PlaybackPositionTicks: item.PlaybackPositionTicks,
                LastPlayedDate:        item.LastPlayedDate);

            _cache.Upsert(item.ItemId, changed.UserId, userData);
            _writer.Enqueue(item.ItemId, changed.UserId, userData, username);

            _logger.LogDebug(
                "Playstate updated via WS: item={ItemId} user={UserId} played={Played}",
                item.ItemId, changed.UserId, item.Played);
        }
    }

    private void HandlePlaybackStop(JsonElement data, int connectionId, CancellationToken ct)
    {
        PlaybackSessionInfo? session;

        try { session = data.Deserialize<PlaybackSessionInfo>(JsonOpts); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize PlaybackStop payload.");
            return;
        }

        var itemId = session?.NowPlayingItem?.Id;
        var userId = session?.UserId;

        if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(userId)) return;

        // Re-fetch via REST so we get authoritative data (WS delta may be incomplete)
        _ = RefetchUserDataAsync(connectionId, itemId, userId, ct);
    }

    internal void HandleSessionsMessage(JsonElement data, int connectionId, CancellationToken ct)
    {
        JellyfinSessionDto[]? sessions;

        try { sessions = data.Deserialize<JellyfinSessionDto[]>(JsonOpts); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Sessions payload.");
            return;
        }

        if (sessions is not { Length: > 0 }) return;

        var mapped = Array.ConvertAll(sessions, JellyfinSession.From);
        _ = ProcessSessionAlertsAsync(connectionId, mapped, ct);
    }

    /// <summary>
    /// Forwards a "Sessions" WS push to <see cref="IJellyfinSessionAlertService"/>.
    /// Wrapped here (in addition to the service's own internal guards) so that an
    /// unexpected exception can never take down the WS message loop.
    /// </summary>
    private async Task ProcessSessionAlertsAsync(
        int connectionId, IReadOnlyList<JellyfinSession> sessions, CancellationToken ct)
    {
        try
        {
            await _sessionAlerts.ProcessSessionsUpdateAsync(connectionId, sessions, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process Sessions update for session alerts.");
        }
    }

    // ── REST re-fetch on PlaybackStop ────────────────────────────────────────

    private async Task RefetchUserDataAsync(
        int connectionId, string itemId, string userId, CancellationToken ct)
    {
        try
        {
            using var scope   = _scopeFactory.CreateScope();
            var       factory = scope.ServiceProvider
                .GetRequiredService<IIntegrationClientFactory>();

            var client = await factory
                .CreateJellyfinClientAsync(connectionId, ct)
                .ConfigureAwait(false);

            if (client is null) return;

            var result = await client
                .GetUserDataAsync(userId, itemId, ct)
                .ConfigureAwait(false);

            if (result is HttpResult<JellyfinUserData>.Success { Value: var ud })
            {
                _cache.Upsert(itemId, userId, ud);
                
                _userIdToUsernameMap.TryGetValue(userId, out var username);
                username ??= userId;
                _writer.Enqueue(itemId, userId, ud, username);

                _logger.LogDebug(
                    "PlaybackStop: refreshed user data for item={ItemId} user={UserId}",
                    itemId, userId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to re-fetch user data after PlaybackStop for item={ItemId}.", itemId);
        }
    }

    // ── Keep-alive loop ──────────────────────────────────────────────────────

    private async Task KeepAliveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            await Task.Delay(
                    TimeSpan.FromSeconds(_keepAliveInterval), ct)
                .ConfigureAwait(false);

            if (ws.State != WebSocketState.Open || ct.IsCancellationRequested) break;

            await SendMessageAsync(ws, new JellyfinWsOutbound("KeepAlive"), ct)
                .ConfigureAwait(false);

            _logger.LogDebug("Jellyfin WebSocket keep-alive sent.");
        }
    }

    // ── REST backfill ────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches all Jellyfin users and then all their per-item UserData via REST,
    /// seeding the playstate cache to reconcile any events missed while the socket
    /// was disconnected.  Failures are non-fatal — the WS stream will keep the
    /// cache current going forward.
    /// </summary>
    private async Task BackfillAsync(int connectionId, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Jellyfin playstate backfill starting…");

            using var scope   = _scopeFactory.CreateScope();
            var       factory = scope.ServiceProvider
                .GetRequiredService<IIntegrationClientFactory>();

            var client = await factory
                .CreateJellyfinClientAsync(connectionId, ct)
                .ConfigureAwait(false);

            if (client is null)
            {
                _logger.LogWarning("Backfill aborted: could not create Jellyfin client.");
                return;
            }

            var usersResult = await client.GetUsersAsync(ct).ConfigureAwait(false);

            if (usersResult is not HttpResult<IReadOnlyList<JellyfinUser>>.Success usersOk)
            {
                _logger.LogWarning("Backfill aborted: could not retrieve Jellyfin users.");
                return;
            }

            var users        = usersOk.Value;
            foreach (var user in users)
            {
                _userIdToUsernameMap[user.Id] = user.Name;
            }

            var totalUpdated = 0;

            foreach (var user in users)
            {
                if (ct.IsCancellationRequested) break;

                var request = new GetItemsRequest
                {
                    UserId           = user.Id,
                    IncludeItemTypes = ["Movie", "Series", "Season", "Episode"],
                    Fields           = ["UserData"],
                    Recursive        = true,
                    Limit            = 500
                };

                var itemsResult = await client
                    .GetAllItemsAsync(request, maxItems: 50_000, ct: ct)
                    .ConfigureAwait(false);

                if (itemsResult is not HttpResult<IReadOnlyList<JellyfinItem>>.Success itemsOk)
                {
                    _logger.LogWarning(
                        "Backfill: failed to retrieve items for user {UserId}; skipping.", user.Id);
                    continue;
                }

                var count = 0;
                foreach (var item in itemsOk.Value)
                {
                    if (item.UserData is null) continue;
                    _cache.Upsert(item.Id, user.Id, item.UserData);
                    count++;
                }

                totalUpdated += count;
                _logger.LogDebug(
                    "Backfill: {Count} items indexed for user {UserId}.", count, user.Id);
            }

            _logger.LogInformation(
                "Jellyfin playstate backfill complete: {Total} entries across {Users} users.",
                totalUpdated, users.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Jellyfin backfill cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jellyfin backfill encountered an error.");
        }
    }

    // ── Send helper ──────────────────────────────────────────────────────────

    private async Task SendMessageAsync(
        ClientWebSocket ws, JellyfinWsOutbound message, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;

        var json  = JsonSerializer.Serialize(message, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (ws.State != WebSocketState.Open) return;

            await ws.SendAsync(
                    bytes.AsMemory(),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ── DB helpers ───────────────────────────────────────────────────────────

    private sealed record JellyfinConnectionInfo(
        int    ConnectionId,
        string BaseUrl,
        string ApiKey,
        bool   AllowInsecure);

    private async Task<JellyfinConnectionInfo?> GetJellyfinConnectionAsync(CancellationToken ct)
    {
        try
        {
            using var scope   = _scopeFactory.CreateScope();
            var       connSvc = scope.ServiceProvider
                .GetRequiredService<IConnectionService>();

            var all  = await connSvc.GetAllAsync().ConfigureAwait(false);
            var conn = all.FirstOrDefault(
                c => c.Type == ConnectionType.Jellyfin && c.IsEnabled);

            if (conn is null) return null;

            var apiKey = await connSvc
                .GetDecryptedKeyAsync(conn.Id)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning(
                    "Jellyfin connection '{Name}' has no API key; WebSocket listener idle.",
                    conn.Name);
                return null;
            }

            return new JellyfinConnectionInfo(conn.Id, conn.BaseUrl, apiKey, conn.AllowInsecure);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Jellyfin connection from database.");
            return null;
        }
    }

    // ── WebSocket construction ────────────────────────────────────────────────

    /// <summary>
    /// Converts an HTTP base URL to its WebSocket equivalent and appends
    /// the Jellyfin socket path with authentication query parameters.
    /// Examples:
    ///   http://jellyfin:8096         → ws://jellyfin:8096/socket?api_key=…&deviceId=…
    ///   https://jellyfin.example.com → wss://jellyfin.example.com/socket?api_key=…&deviceId=…
    ///   http://host/jellyfin         → ws://host/jellyfin/socket?api_key=…&deviceId=…
    /// </summary>
    internal static Uri BuildWsUri(string baseUrl, string apiKey)
    {
        var http   = new Uri(baseUrl.TrimEnd('/'));
        var scheme = http.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            ? "wss" : "ws";

        // Preserve any reverse-proxy sub-path (e.g. /jellyfin)
        var basePath = http.AbsolutePath.TrimEnd('/');

        return new Uri(
            $"{scheme}://{http.Authority}{basePath}/socket" +
            $"?api_key={Uri.EscapeDataString(apiKey)}" +
            $"&deviceId={Uri.EscapeDataString(DeviceId)}");
    }

    private static ClientWebSocket CreateWebSocket(bool allowInsecure)
    {
        var ws = new ClientWebSocket();

        if (allowInsecure)
        {
            // Intentional — user opted in per-connection for self-signed certificates
            ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        return ws;
    }

    private async Task LoadInitialStateAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

            var activities = await db.PlaybackActivities
                .AsNoTracking()
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var entries = activities.Select(a => (
                a.MediaServerItemId,
                a.UserId,
                new JellyfinUserData(a.IsFinished, a.LastWatched, a.PlayCount, a.PlaybackPositionTicks)
            ));

            _cache.BulkUpsert(entries);

            foreach (var act in activities)
            {
                if (!string.IsNullOrEmpty(act.Username))
                {
                    _userIdToUsernameMap[act.UserId] = act.Username;
                }
            }

            _logger.LogInformation("Loaded {Count} playback activity records from database on boot.", activities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load initial playback activities from database.");
        }
    }

    private async Task PruneActivitiesAsync(CancellationToken ct)
    {
        try
        {
            _lastPrunedAt = DateTime.UtcNow;
            await _writer.PruneOldActivitiesAsync(365, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prune old playback activities.");
        }
    }
}
