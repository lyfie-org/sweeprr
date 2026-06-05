using Sweeprr.API.Integrations.Sonarr.Models;

namespace Sweeprr.API.Integrations.Matching;

/// <summary>
/// Carries both the matched Sonarr series and the resolved season (when a season number was
/// requested via <see cref="MediaIdentity.SeasonNumber"/>).
///
/// <c>Season</c> is null when no season was requested or when the series was matched but the
/// specific season number was not found in Sonarr — callers performing season-level actions
/// should treat a null Season as "series-level only" and decide accordingly.
/// </summary>
public sealed record SonarrSeriesMatch(
    SonarrSeries  Series,
    SonarrSeason? Season);
