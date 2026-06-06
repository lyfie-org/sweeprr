# Realtime WebSocket Integration

Sweeprr stands out by utilizing Jellyfin's WebSocket protocol for real-time playstate updates. Rather than relying on heavy REST polling loops, Sweeprr listens continuously for watch events.

## Connection Architecture

`JellyfinWebSocketService` is a .NET `BackgroundService` that connects to:
`ws(s)://<host>/socket?api_key=<key>&deviceId=<deviceId>`

On startup, it executes a subscription handshake (`SessionsStart`) to begin receiving the event stream.

### Resilient Keep-Alives
Jellyfin periodically sends `KeepAlive` or `ForceKeepAlive` commands to clients. Sweeprr listens to these frames and immediately responds with a client-side `KeepAlive` packet, preventing socket timeouts and drops by the media server.

---

## Event Tracking

The service listens for the following events:
1. **`UserDataChanged`**: Dispatched when user-specific metadata changes, such as marking a video as played or changing its progress.
2. **`PlaybackStart` / `PlaybackStop`**: Emitted when a user starts or stops playing video. Sweeprr parses the session details to update the cache.

When these events occur, they update the `IPlaystateCache`, an in-memory thread-safe store that matches watch statistics (played status, last watched timestamp, play counts) for Jellyfin media items.

---

## Self-Healing & REST Backfill

WebSockets can disconnect due to network blips or Jellyfin restarts. Sweeprr handles this gracefully through a two-tiered safety architecture:

### 1. Exponential Backoff Reconnection
If the connection is interrupted, the socket client enters a reconnection loop that backs off exponentially (starting at 2s up to a maximum of 5 minutes) to avoid hammering the host.

### 2. REST Backfill Reconciliation
Because WebSocket events are transient (not sent retroactively for events that occurred while disconnected), Sweeprr triggers a REST sync on startup and immediately after every WebSocket reconnection. 

This REST sync checks the user watch history since the last successful sync, reconciling any playstate changes that occurred while the socket was offline.

> [!NOTE]
> This dual approach guarantees that Sweeprr remains responsive and accurate, never missing a watch event even during server maintenance.
