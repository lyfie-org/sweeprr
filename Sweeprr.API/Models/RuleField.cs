namespace Sweeprr.API.Models;

public enum RuleField
{
    // Watch / usage
    LastWatched,
    PlayCount,
    WatchedByAnyUser,
    WatchedByAllUsers,
    SeenByUserCount,

    // Metadata
    ReleaseDate,
    DateAdded,
    Rating,
    Genre,
    ResolutionHeight,

    // *arr
    Monitored,
    Tags,
    QualityProfile,
    FileSizeGb
}
