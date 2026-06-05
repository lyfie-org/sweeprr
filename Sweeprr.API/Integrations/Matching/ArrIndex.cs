namespace Sweeprr.API.Integrations.Matching;

/// <summary>
/// Per-scan, immutable-after-build lookup structure for correlating provider IDs to *arr items.
///
/// Three provider-ID namespaces are tracked independently:
///   IMDB (string, case-insensitive), TMDB (int), TVDB (int).
///
/// Conflict semantics: if two distinct items are indexed under the same key, the key is
/// permanently marked "conflicted" and any lookup on it returns <see cref="IndexLookupKind.Conflicted"/>.
/// This prevents a malformed duplicate from producing a silent false match.
/// </summary>
public sealed class ArrIndex<T> where T : class
{
    private readonly Dictionary<string, T> _byImdb      = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string>       _imdbConflicts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, T>   _byTmdb      = new();
    private readonly HashSet<int>         _tmdbConflicts = new();
    private readonly Dictionary<int, T>   _byTvdb      = new();
    private readonly HashSet<int>         _tvdbConflicts = new();

    // ── Indexing (called only by MediaMatchingService.Build* methods) ─────────

    internal void IndexByImdb(string? rawId, T item)
    {
        var id = NormalizeImdb(rawId);
        if (id is null) return;
        AddEntry(_byImdb, _imdbConflicts, id, item);
    }

    internal void IndexByTmdb(int tmdbId, T item)
    {
        if (tmdbId <= 0) return;
        AddEntry(_byTmdb, _tmdbConflicts, tmdbId, item);
    }

    internal void IndexByTvdb(int tvdbId, T item)
    {
        if (tvdbId <= 0) return;
        AddEntry(_byTvdb, _tvdbConflicts, tvdbId, item);
    }

    // ── Lookup (called only by MediaMatchingService.Match* methods) ──────────

    internal IndexLookup<T> LookupByImdb(string? id)
    {
        var normalised = NormalizeImdb(id);
        if (normalised is null) return IndexLookup<T>.NotFound;
        if (_imdbConflicts.Contains(normalised)) return IndexLookup<T>.Conflicted;
        return _byImdb.TryGetValue(normalised, out var item)
            ? IndexLookup<T>.Found(item)
            : IndexLookup<T>.NotFound;
    }

    internal IndexLookup<T> LookupByTmdb(int? id)
    {
        if (id is null or <= 0) return IndexLookup<T>.NotFound;
        if (_tmdbConflicts.Contains(id.Value)) return IndexLookup<T>.Conflicted;
        return _byTmdb.TryGetValue(id.Value, out var item)
            ? IndexLookup<T>.Found(item)
            : IndexLookup<T>.NotFound;
    }

    internal IndexLookup<T> LookupByTvdb(int? id)
    {
        if (id is null or <= 0) return IndexLookup<T>.NotFound;
        if (_tvdbConflicts.Contains(id.Value)) return IndexLookup<T>.Conflicted;
        return _byTvdb.TryGetValue(id.Value, out var item)
            ? IndexLookup<T>.Found(item)
            : IndexLookup<T>.NotFound;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static void AddEntry<K>(
        Dictionary<K, T> dict,
        HashSet<K>        conflicts,
        K                 key,
        T                 item) where K : notnull
    {
        if (conflicts.Contains(key)) return; // already permanently conflicted — ignore additional entries
        if (dict.ContainsKey(key))
        {
            // Second distinct item with the same key → mark conflicted, remove from match pool
            dict.Remove(key);
            conflicts.Add(key);
        }
        else
        {
            dict[key] = item;
        }
    }

    private static string? NormalizeImdb(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

// ── IndexLookup (internal result of a single ArrIndex lookup) ─────────────────

internal enum IndexLookupKind { NotFound, Found, Conflicted }

internal readonly record struct IndexLookup<T>
{
    public IndexLookupKind Kind  { get; init; }
    public T?              Value { get; init; }

    public static readonly IndexLookup<T> NotFound   = new() { Kind = IndexLookupKind.NotFound };
    public static readonly IndexLookup<T> Conflicted = new() { Kind = IndexLookupKind.Conflicted };

    public static IndexLookup<T> Found(T value) =>
        new() { Kind = IndexLookupKind.Found, Value = value };
}
