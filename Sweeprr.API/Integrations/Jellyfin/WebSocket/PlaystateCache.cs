using System.Collections.Concurrent;
using Sweeprr.API.Integrations.Jellyfin.Models;

namespace Sweeprr.API.Integrations.Jellyfin.WebSocket;

/// <summary>
/// Thread-safe in-memory playstate cache.
///
/// Structure: outer key = itemId (case-insensitive); inner key = userId (case-insensitive).
/// <c>ConcurrentDictionary</c> at both levels means individual upserts are lock-free and
/// safe to call from the WebSocket message loop, the REST backfill, and the rule engine
/// simultaneously.
/// </summary>
public sealed class PlaystateCache : IPlaystateCache
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, JellyfinUserData>> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    public void Upsert(string itemId, string userId, JellyfinUserData data)
    {
        var byUser = _cache.GetOrAdd(
            itemId,
            _ => new ConcurrentDictionary<string, JellyfinUserData>(StringComparer.OrdinalIgnoreCase));

        byUser[userId] = data;
    }

    public JellyfinUserData? Get(string itemId, string userId)
    {
        if (!_cache.TryGetValue(itemId, out var byUser)) return null;
        return byUser.TryGetValue(userId, out var data) ? data : null;
    }

    public IReadOnlyDictionary<string, JellyfinUserData> GetAllForItem(string itemId)
    {
        if (!_cache.TryGetValue(itemId, out var byUser))
            return new Dictionary<string, JellyfinUserData>(StringComparer.OrdinalIgnoreCase);

        // Snapshot so the caller gets a stable view even if the cache updates concurrently
        return new Dictionary<string, JellyfinUserData>(byUser, StringComparer.OrdinalIgnoreCase);
    }

    public void BulkUpsert(IEnumerable<(string itemId, string userId, JellyfinUserData data)> entries)
    {
        foreach (var (itemId, userId, data) in entries)
            Upsert(itemId, userId, data);
    }
}
