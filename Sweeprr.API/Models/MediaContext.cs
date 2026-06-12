namespace Sweeprr.API.Models;

/// <summary>
/// Aggregated metadata and watch state for a single media item,
/// ready to be evaluated against a <see cref="RuleGroup"/>.
///
/// Null on any field means "not loaded." The <c>ValueResolver</c> returns
/// <c>Missing</c> for definitively-absent fields and <c>Transient</c> when
/// <see cref="HasTransientFailure"/> is also set.
///
/// Data-contract between:
///   - Population layer: Jellyfin/Radarr/Sonarr clients (Stories 2.x, 3.3)
///   - Evaluation layer: RuleEvaluator (Story 3.2)
/// </summary>
public sealed class MediaContext
{
    public required string ItemId { get; init; }
    public required string Title { get; init; }
    public required MediaType MediaType { get; init; }

    // Watch / usage — WatchAggregationService (Story 3.3)
    public DateTime? LastWatched { get; init; }
    public int? PlayCount { get; init; }
    public bool? WatchedByAnyUser { get; init; }
    public bool? WatchedByAllUsers { get; init; }
    public int? SeenByUserCount { get; init; }

    // Metadata — Jellyfin REST client (Story 2.2)
    public DateTime? ReleaseDate { get; init; }
    public DateTime? DateAdded { get; init; }
    public decimal? Rating { get; init; }
    public IReadOnlyList<string>? Genres { get; init; }
    public int? ResolutionHeight { get; init; }
    public string? VideoCodec { get; init; }
    public int? AudioChannels { get; init; }

    // *arr — Radarr/Sonarr clients (Story 2.4)
    public bool? Monitored { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? QualityProfile { get; init; }
    public decimal? FileSizeGb { get; init; }

    // TV-specific (Story 7.3) — null when not applicable or data unavailable (→ Missing, not Transient)
    public bool? SeriesEnded { get; init; }
    public bool? IsFinale    { get; init; }
    public bool? CutoffMet   { get; init; }

    // Multi-instance (Story 8.4) — null when only one instance configured or field not requested
    public bool? HasComplementaryCopy { get; init; }

    // Disk space (Story 9.3) — null when not requested or *arr unavailable
    public double? DiskFreeSpacePercent { get; init; }
    public double? DiskFreeSpaceGb      { get; init; }

    // Provider IDs (from Jellyfin — stored on SweepItem for execution-time *arr matching)
    public string? ImdbId { get; init; }
    public int? TmdbId { get; init; }
    public int? TvdbId { get; init; }

    // Arr resolution metadata (set during population when the item is matched to an *arr instance)
    public int? ArrConnectionId { get; init; }   // ServerConnection.Id
    public int? SeasonNumber { get; init; }       // Non-null for Season-type items only

    /// <summary>
    /// True when any data source returned a transient (non-definitive) error during
    /// population. The item is excluded from deletion regardless of rule conditions.
    /// </summary>
    public bool HasTransientFailure { get; init; }

    /// <summary>Human-readable explanation for a transient failure (surfaced in the Sweep Queue).</summary>
    public string? TransientFailureReason { get; init; }
}
