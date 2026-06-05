namespace Sweeprr.API.Integrations.Radarr.Models;

public sealed record RadarrMovieFile(
    int              Id,
    int              MovieId,
    string           RelativePath,
    string?          Path,
    long?            Size,
    DateTimeOffset?  DateAdded,
    string?          ReleaseGroup);
