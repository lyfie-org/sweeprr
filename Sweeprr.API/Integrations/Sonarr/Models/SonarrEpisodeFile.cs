namespace Sweeprr.API.Integrations.Sonarr.Models;

public sealed record SonarrEpisodeFile(
    int             Id,
    int             SeriesId,
    int             SeasonNumber,
    string          RelativePath,
    string?         Path,
    long?           Size,
    DateTimeOffset? DateAdded,
    string?         ReleaseGroup);
