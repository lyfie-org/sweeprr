using Sweeprr.API.Integrations.Jellyfin.Dto;

namespace Sweeprr.API.Integrations.Jellyfin.Models;

/// <summary>
/// Typed, normalized provider IDs extracted from a Jellyfin item.
/// Used by the media matching service to correlate Jellyfin items with
/// their Radarr/Sonarr counterparts.
///
/// Normalization rules:
///   ImdbId — kept as string; preserves the "tt" prefix that IMDB requires.
///   TmdbId / TvdbId — parsed to int?; "0", empty, or non-numeric → null.
///   These are null-means-absent: Story 2.5 matching uses nulls as "no ID" signals.
/// </summary>
public sealed record ProviderIds(
    string? ImdbId,
    int? TmdbId,
    int? TvdbId)
{
    public static readonly ProviderIds Empty = new(null, null, null);

    /// <summary>True when at least one ID is present — required for *arr matching.</summary>
    public bool HasAny => ImdbId is not null || TmdbId is not null || TvdbId is not null;

    public static ProviderIds From(JellyfinProviderIdsDto? dto)
    {
        if (dto is null) return Empty;
        return new ProviderIds(
            ImdbId: NormalizeImdb(dto.Imdb),
            TmdbId: ParseNumericId(dto.Tmdb),
            TvdbId: ParseNumericId(dto.Tvdb));
    }

    private static string? NormalizeImdb(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static int? ParseNumericId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw.Trim(), out var id) && id > 0 ? id : null;
    }
}
