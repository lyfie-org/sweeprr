namespace Sweeprr.API.Integrations.Sonarr.Models;

public sealed record SonarrEpisode(
    int             Id,
    int             SeriesId,
    int             SeasonNumber,
    int             EpisodeNumber,
    int?            EpisodeFileId,
    int?            TvdbId,
    bool            HasFile,
    bool            Monitored,
    DateTimeOffset? AirDate,
    string?         Title,
    string?         FinaleType);   // "series" | "season" | null (Sonarr v4+ only)
