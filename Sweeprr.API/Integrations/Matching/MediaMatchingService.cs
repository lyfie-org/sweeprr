using Sweeprr.API.Integrations.Radarr.Models;
using Sweeprr.API.Integrations.Sonarr.Models;

namespace Sweeprr.API.Integrations.Matching;

/// <inheritdoc cref="IMediaMatchingService"/>
public sealed class MediaMatchingService : IMediaMatchingService
{
    // ── Index builders ────────────────────────────────────────────────────────

    public ArrIndex<RadarrMovie> BuildRadarrIndex(IReadOnlyList<RadarrMovie> movies)
    {
        var index = new ArrIndex<RadarrMovie>();
        foreach (var movie in movies)
        {
            // Radarr: TMDB (int) + IMDB (string). No TVDB.
            index.IndexByTmdb(movie.TmdbId, movie);
            index.IndexByImdb(movie.ImdbId, movie);
        }
        return index;
    }

    public ArrIndex<SonarrSeries> BuildSonarrIndex(IReadOnlyList<SonarrSeries> series)
    {
        var index = new ArrIndex<SonarrSeries>();
        foreach (var s in series)
        {
            // Sonarr: TVDB (int) + IMDB (string). No TMDB.
            index.IndexByTvdb(s.TvdbId, s);
            index.IndexByImdb(s.ImdbId, s);
        }
        return index;
    }

    // ── Match methods ─────────────────────────────────────────────────────────

    public MatchResult<RadarrMovie> MatchMovie(
        MediaIdentity        identity,
        ArrIndex<RadarrMovie> index)
    {
        if (!identity.HasAny) return MatchResult<RadarrMovie>.NoMatch;

        var result = Resolve(
            [index.LookupByTmdb(identity.TmdbId), index.LookupByImdb(identity.ImdbId)],
            m => m.Id);

        return result;
    }

    public MatchResult<SonarrSeriesMatch> MatchSeries(
        MediaIdentity          identity,
        ArrIndex<SonarrSeries> index)
    {
        if (!identity.HasAny) return MatchResult<SonarrSeriesMatch>.NoMatch;

        var seriesResult = Resolve(
            [index.LookupByTvdb(identity.TvdbId), index.LookupByImdb(identity.ImdbId)],
            s => s.Id);

        return seriesResult switch
        {
            MatchResult<SonarrSeries>.Matched m =>
                MatchResult<SonarrSeriesMatch>.Match(
                    new SonarrSeriesMatch(m.Value, ResolveSeasonFor(m.Value, identity.SeasonNumber))),

            MatchResult<SonarrSeries>.Unmatched =>
                MatchResult<SonarrSeriesMatch>.NoMatch,

            _ => // Ambiguous
                MatchResult<SonarrSeriesMatch>.Conflict
        };
    }

    // ── Core resolution logic ─────────────────────────────────────────────────

    /// <summary>
    /// Aggregate a set of index lookups into a single <see cref="MatchResult{T}"/>.
    ///
    /// Rules:
    ///   • Any conflicted lookup → Ambiguous (conservative: do not act).
    ///   • Deduplicate found items by their *arr integer ID (same item via multiple IDs = one candidate).
    ///   • 0 unique candidates → Unmatched.
    ///   • 1 unique candidate  → Matched.
    ///   • 2+ unique candidates → Ambiguous.
    /// </summary>
    private static MatchResult<T> Resolve<T>(
        IndexLookup<T>[]  lookups,
        Func<T, int>      idSelector) where T : class
    {
        Dictionary<int, T>? candidates = null;

        foreach (var lookup in lookups)
        {
            switch (lookup.Kind)
            {
                case IndexLookupKind.Conflicted:
                    return MatchResult<T>.Conflict;

                case IndexLookupKind.Found:
                    candidates ??= new Dictionary<int, T>();
                    candidates[idSelector(lookup.Value!)] = lookup.Value!;
                    break;
            }
        }

        if (candidates is null || candidates.Count == 0) return MatchResult<T>.NoMatch;
        if (candidates.Count == 1) return MatchResult<T>.Match(candidates.Values.First());
        return MatchResult<T>.Conflict;
    }

    private static SonarrSeason? ResolveSeasonFor(SonarrSeries series, int? seasonNumber) =>
        seasonNumber is null
            ? null
            : series.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNumber.Value);
}
