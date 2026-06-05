using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sweeprr.API.Integrations.Jellyfin.WebSocket;

/// <summary>
/// Inbound message received from the Jellyfin WebSocket.
/// <c>Data</c> is <see cref="JsonElement?"/> because its shape varies by message type:
/// a bare integer for ForceKeepAlive, a plain object for UserDataChanged / PlaybackStop,
/// or absent for KeepAlive.
/// </summary>
internal sealed record JellyfinWsInbound(
    [property: JsonPropertyName("MessageType")] string MessageType,
    [property: JsonPropertyName("Data")]        JsonElement? Data);

/// <summary>
/// Outbound message sent by Sweeprr to the Jellyfin WebSocket server.
/// <c>Data</c> is <c>null</c> for keep-alive pings; a string payload for subscriptions.
/// Null fields are omitted from serialization.
/// </summary>
internal sealed record JellyfinWsOutbound(
    [property: JsonPropertyName("MessageType")] string  MessageType,
    [property: JsonPropertyName("Data")]        string? Data = null);

// ── UserDataChanged event ─────────────────────────────────────────────────────

/// <summary>
/// Payload of a <c>UserDataChanged</c> message.  Jellyfin batches one or more
/// item-level updates under a single UserId per message.
/// </summary>
internal sealed record UserDataChangedData(
    [property: JsonPropertyName("UserId")]       string                UserId,
    [property: JsonPropertyName("UserDataList")] UserDataChangedItem[]? UserDataList);

internal sealed record UserDataChangedItem(
    [property: JsonPropertyName("ItemId")]                string          ItemId,
    [property: JsonPropertyName("Played")]                bool            Played,
    [property: JsonPropertyName("PlayCount")]             int             PlayCount,
    [property: JsonPropertyName("PlaybackPositionTicks")] long            PlaybackPositionTicks,
    [property: JsonPropertyName("LastPlayedDate")]        DateTimeOffset? LastPlayedDate);

// ── PlaybackStop event ────────────────────────────────────────────────────────

/// <summary>
/// Minimal extraction from the <c>PlaybackStop</c> session payload.
/// The full SessionInfo shape is not modelled — we only need UserId and the
/// now-playing item's Id to trigger an authoritative REST re-fetch.
/// </summary>
internal sealed record PlaybackSessionInfo(
    [property: JsonPropertyName("UserId")]        string?              UserId,
    [property: JsonPropertyName("NowPlayingItem")] PlaybackSessionItem? NowPlayingItem);

internal sealed record PlaybackSessionItem(
    [property: JsonPropertyName("Id")] string? Id);
