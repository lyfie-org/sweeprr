using Sweeprr.API.Integrations.Bazarr.Dto;

namespace Sweeprr.API.Integrations.Bazarr;

/// <summary>
/// Typed HTTP client for the Bazarr subtitle management API.
///
/// Auth: All requests carry an X-API-KEY header set at construction.
/// Bazarr operations are best-effort — failures are logged but never propagate
/// to affect sweep item status.
/// </summary>
public sealed class BazarrClient : ClientBase
{
    public BazarrClient(
        HttpClient http,
        string baseUrl,
        string apiKey,
        ILogger<BazarrClient> logger)
        : base(http, baseUrl, logger)
    {
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-KEY", apiKey);
    }

    /// <summary>
    /// Returns <c>true</c> if Bazarr is reachable and the API key is accepted.
    /// Used once per sweep run to avoid per-item availability roundtrips.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var result = await GetAsync<BazarrSystemStatusDto>("/api/system/status", ct);
        return result is HttpResult<BazarrSystemStatusDto>.Success;
    }

    /// <summary>
    /// Deletes all subtitles tracked by Bazarr for the given Radarr movie ID.
    /// Fetches the movie's subtitle list from Bazarr then issues a DELETE per subtitle.
    /// No-ops silently if the movie has no Bazarr entries.
    /// </summary>
    public async Task DeleteSubtitlesForMovieAsync(int radarrMovieId, CancellationToken ct = default)
    {
        var result = await GetAsync<BazarrMovieResponseDto>(
            $"/api/movies?radarrid[]={radarrMovieId}", ct);

        if (result is not HttpResult<BazarrMovieResponseDto>.Success ok || ok.Value.Data is null)
            return;

        foreach (var movie in ok.Value.Data)
        foreach (var subtitle in movie.Subtitles ?? [])
        {
            if (string.IsNullOrEmpty(subtitle.Path)) continue;
            await DeleteSubtitleAsync(subtitle.Path, "movie", subtitle.Language?.Code2 ?? "en", ct);
        }
    }

    /// <summary>
    /// Deletes all subtitles tracked by Bazarr for the given Sonarr series ID.
    /// Fetches every episode's subtitle list then issues a DELETE per subtitle.
    /// No-ops silently if the series has no Bazarr entries.
    /// </summary>
    public async Task DeleteSubtitlesForSeriesAsync(int sonarrSeriesId, CancellationToken ct = default)
    {
        var result = await GetAsync<BazarrEpisodeResponseDto>(
            $"/api/episodes?seriesid[]={sonarrSeriesId}", ct);

        if (result is not HttpResult<BazarrEpisodeResponseDto>.Success ok || ok.Value.Data is null)
            return;

        foreach (var episode in ok.Value.Data)
        foreach (var subtitle in episode.Subtitles ?? [])
        {
            if (string.IsNullOrEmpty(subtitle.Path)) continue;
            await DeleteSubtitleAsync(subtitle.Path, "series", subtitle.Language?.Code2 ?? "en", ct);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private Task<HttpResult<EmptyResponse>> DeleteSubtitleAsync(
        string path, string type, string language, CancellationToken ct)
        => DeleteAsync("/api/subtitles", new { id = path, type, language }, ct);
}
