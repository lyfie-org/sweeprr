using Sweeprr.API.Integrations.Jellyfin.Models;

namespace Sweeprr.API.Integrations.Jellyfin.WebSocket;

/// <summary>
/// In-memory, thread-safe store of Jellyfin per-user watch state, keyed by
/// (itemId, userId). Populated by the WebSocket listener on live events and
/// by the REST backfill on connect/reconnect. Consumed by the rule engine
/// (Sprint 3) to evaluate multi-user watch conditions without hitting the
/// Jellyfin API on every rule evaluation.
/// </summary>
public interface IPlaystateCache
{
    /// <summary>
    /// Inserts or replaces the watch state for a specific item + user pair.
    /// Safe to call from multiple threads concurrently.
    /// </summary>
    void Upsert(string itemId, string userId, JellyfinUserData data);

    /// <summary>
    /// Returns the watch state for the given item + user, or <c>null</c> if not cached.
    /// </summary>
    JellyfinUserData? Get(string itemId, string userId);

    /// <summary>
    /// Returns a snapshot of all cached user watch states for a given item,
    /// keyed by userId. Returns an empty dictionary when the item is unknown.
    /// </summary>
    IReadOnlyDictionary<string, JellyfinUserData> GetAllForItem(string itemId);

    /// <summary>
    /// Inserts or replaces many entries in one pass. Used by the REST backfill
    /// to seed the cache without per-entry locking overhead.
    /// </summary>
    void BulkUpsert(IEnumerable<(string itemId, string userId, JellyfinUserData data)> entries);
}
