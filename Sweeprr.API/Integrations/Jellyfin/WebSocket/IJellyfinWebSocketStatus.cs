namespace Sweeprr.API.Integrations.Jellyfin.WebSocket;

public interface IJellyfinWebSocketStatus
{
    WsConnectionState State { get; }
    DateTimeOffset? LastConnectedAt { get; }
}
