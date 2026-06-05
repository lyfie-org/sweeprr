namespace Sweeprr.API.Integrations.Radarr.Models;

public sealed record RadarrMovie(
    int                    Id,
    string                 Title,
    int                    Year,
    int                    TmdbId,
    string?                ImdbId,
    bool                   Monitored,
    bool                   HasFile,
    int                    QualityProfileId,
    IReadOnlyList<int>     Tags,
    string?                Path,
    long                   SizeOnDisk,
    DateTimeOffset?        Added,
    string?                Status,
    RadarrMovieFile?       MovieFile);
