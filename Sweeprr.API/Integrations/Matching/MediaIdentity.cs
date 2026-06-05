using Sweeprr.API.Integrations.Jellyfin.Models;

namespace Sweeprr.API.Integrations.Matching;

/// <summary>
/// Normalized provider-ID bundle describing a single media item.
/// Built from a Jellyfin item's <see cref="ProviderIds"/> before matching against Radarr/Sonarr.
///
/// Null means "absent / unknown" — a null ID must never produce a positive match.
/// SeasonNumber is set when the Jellyfin item is a Season; null for movies and series roots.
/// </summary>
public sealed record MediaIdentity(
    string? ImdbId,
    int?    TmdbId,
    int?    TvdbId,
    int?    SeasonNumber)
{
    public static readonly MediaIdentity Empty = new(null, null, null, null);

    /// <summary>True when at least one provider ID is present (required for any *arr lookup).</summary>
    public bool HasAny => ImdbId is not null || TmdbId is not null || TvdbId is not null;

    /// <summary>Create from a Jellyfin <see cref="ProviderIds"/>, optionally with a season number.</summary>
    public static MediaIdentity From(ProviderIds ids, int? seasonNumber = null) =>
        new(ids.ImdbId, ids.TmdbId, ids.TvdbId, seasonNumber);
}
