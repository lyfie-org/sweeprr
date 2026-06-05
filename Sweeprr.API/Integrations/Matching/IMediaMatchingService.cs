using Sweeprr.API.Integrations.Radarr.Models;
using Sweeprr.API.Integrations.Sonarr.Models;

namespace Sweeprr.API.Integrations.Matching;

/// <summary>
/// Correlates a Jellyfin media item (identified by <see cref="MediaIdentity"/>) with its
/// counterpart in Radarr or Sonarr using OR-fallback provider-ID matching.
///
/// Usage pattern per scan:
/// <code>
///   var radarrIndex = _matcher.BuildRadarrIndex(await radarrClient.GetMoviesAsync());
///   var result      = _matcher.MatchMovie(identity, radarrIndex);
/// </code>
///
/// The index is built once per scan in O(n) time; each subsequent match is O(1).
/// </summary>
public interface IMediaMatchingService
{
    /// <summary>
    /// Build a provider-ID lookup index from the full Radarr movie library.
    /// Must be rebuilt each scan to reflect library changes.
    /// </summary>
    ArrIndex<RadarrMovie> BuildRadarrIndex(IReadOnlyList<RadarrMovie> movies);

    /// <summary>
    /// Build a provider-ID lookup index from the full Sonarr series library.
    /// Must be rebuilt each scan to reflect library changes.
    /// </summary>
    ArrIndex<SonarrSeries> BuildSonarrIndex(IReadOnlyList<SonarrSeries> series);

    /// <summary>
    /// Match a Jellyfin movie item to its Radarr counterpart using TMDB then IMDB fallback.
    /// Returns <see cref="MatchResult{T}.Matched"/> only when exactly one candidate is found.
    /// </summary>
    MatchResult<RadarrMovie> MatchMovie(MediaIdentity identity, ArrIndex<RadarrMovie> index);

    /// <summary>
    /// Match a Jellyfin series/season item to its Sonarr counterpart using TVDB then IMDB fallback.
    /// When <see cref="MediaIdentity.SeasonNumber"/> is set, also resolves the specific
    /// <see cref="SonarrSeriesMatch.Season"/> from the matched series.
    /// </summary>
    MatchResult<SonarrSeriesMatch> MatchSeries(MediaIdentity identity, ArrIndex<SonarrSeries> index);
}
