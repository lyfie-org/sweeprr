namespace Sweeprr.API.Integrations.Sonarr.Models;

public sealed record SonarrSeries(
    int                       Id,
    string                    Title,
    int                       Year,
    int                       TvdbId,
    string?                   ImdbId,
    bool                      Monitored,
    int                       QualityProfileId,
    IReadOnlyList<int>        Tags,
    string?                   Path,
    DateTimeOffset?           Added,
    IReadOnlyList<SonarrSeason> Seasons);
