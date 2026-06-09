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
    VideoCodec        = 15,   // Text: Equals/NotEquals/Contains — e.g. "hevc", "h264"
    AudioChannels     = 16,   // Number: Equals/GreaterThan/LessThan — e.g. 6 = 5.1

    // *arr
    Monitored         = 20,
    Tags              = 21,
    QualityProfile    = 22,
    FileSizeGb        = 23,

    // TV-specific (Sonarr)
    SeriesEnded       = 30,  // Bool: series status = "ended" in Sonarr
    IsFinale          = 31,  // Bool: season has a finale episode (finaleType != null, Sonarr v4+)
    CutoffMet         = 32,  // Bool: file quality meets the profile cutoff

    // Multi-instance
    HasComplementaryCopy = 40,  // Bool: TMDB/TVDB ID exists in another same-type *arr instance

    // Disk space (connection-level, cached once per scan run)
    DiskFreeSpacePercent = 50,  // Number: free space as a percentage of total (0–100)
    DiskFreeSpaceGb      = 51,  // Number: free space in gigabytes
}
