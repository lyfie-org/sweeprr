namespace Sweeprr.API.Models;

/// <summary>
/// The "what to measure" dimension of a rule condition.
/// Explicit int values ensure DB stability across renames.
/// </summary>
public enum RuleField
{
    // Watch / usage
    LastWatched       = 1,
    PlayCount         = 2,
    WatchedByAnyUser  = 3,
    WatchedByAllUsers = 4,
    SeenByUserCount   = 5,

    // Metadata
    ReleaseDate       = 10,
    DateAdded         = 11,
    Rating            = 12,
    Genre             = 13,
    ResolutionHeight  = 14,

    // *arr
    Monitored         = 20,
    Tags              = 21,
    QualityProfile    = 22,
    FileSizeGb        = 23,
}
