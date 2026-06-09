using System.Text.Json.Nodes;
using Sweeprr.API.Integrations.Radarr.Dto;
using Sweeprr.API.Integrations.Radarr.Models;

namespace Sweeprr.API.Integrations.Radarr;

/// <summary>
/// Typed HTTP client for the Radarr v3 REST API.
///
/// <b>Auth:</b> All requests carry the <c>X-Api-Key</c> header, set on
/// <see cref="System.Net.Http.HttpClient.DefaultRequestHeaders"/> at construction time.
///
/// <b>Unmonitor strategy:</b> <see cref="UnmonitorMovieAsync"/> uses a GET-then-PUT
/// round-trip via <see cref="JsonNode"/> so every Radarr field is preserved without
/// requiring an exhaustive DTO. This prevents accidental data loss from a partial PUT body.
///
/// <b>Caller responsibility (Story 4.3):</b> The destructive methods
/// (<see cref="UnmonitorMovieAsync"/>, <see cref="DeleteMovieAsync"/>) are intentionally
/// separate. The Sweep Executor must call unmonitor <em>before</em> delete and abort the
/// delete if unmonitor fails — that ordering is not enforced here.
/// </summary>
public sealed class RadarrClient : ClientBase
{
    private const string ApiKeyHeader = "X-Api-Key";

    /// <param name="http">
    ///   HttpClient from <see cref="IHttpClientFactory"/> with the Polly resilience
    ///   pipeline already wired in.
    /// </param>
    /// <param name="baseUrl">Radarr base URL, e.g. <c>http://radarr:7878</c>.</param>
    /// <param name="apiKey">Plain-text API key (decrypted by the factory).</param>
    public RadarrClient(
        HttpClient http,
        string baseUrl,
        string apiKey,
        ILogger<RadarrClient> logger)
        : base(http, baseUrl, logger)
    {
        http.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeader, apiKey);
    }

    // ── Read-only queries ────────────────────────────────────────────────────

    public async Task<HttpResult<IReadOnlyList<RadarrMovie>>> GetMoviesAsync(
        CancellationToken ct = default)
    {
        var result = await GetAsync<List<RadarrMovieDto>>("/api/v3/movie", ct);
        return result.Map(dtos => (IReadOnlyList<RadarrMovie>)dtos.Select(ToMovie).ToList());
    }

    public async Task<HttpResult<RadarrMovie>> GetMovieAsync(
        int id, CancellationToken ct = default)
    {
        var result = await GetAsync<RadarrMovieDto>($"/api/v3/movie/{id}", ct);
        return result.Map(ToMovie);
    }

    /// <summary>
    /// Returns all history records for a movie, newest first.
    /// History dates drive "age" calculations; callers choose OLDEST vs MOST_RECENT strategy.
    /// </summary>
    public async Task<HttpResult<IReadOnlyList<RadarrHistoryRecord>>> GetMovieHistoryAsync(
        int movieId, CancellationToken ct = default)
    {
        var result = await GetAsync<List<RadarrHistoryRecordDto>>(
            $"/api/v3/history/movie?movieId={movieId}&sortKey=date&sortDirection=descending", ct);
        return result.Map(dtos =>
            (IReadOnlyList<RadarrHistoryRecord>)dtos.Select(ToHistoryRecord).ToList());
    }

    public async Task<HttpResult<IReadOnlyList<RadarrQualityProfile>>> GetQualityProfilesAsync(
        CancellationToken ct = default)
    {
        var result = await GetAsync<List<RadarrQualityProfileDto>>("/api/v3/qualityprofile", ct);
        return result.Map(dtos =>
            (IReadOnlyList<RadarrQualityProfile>)dtos
                .Select(d => new RadarrQualityProfile(d.Id, d.Name, d.UpgradeAllowed))
                .ToList());
    }

    public async Task<HttpResult<IReadOnlyList<RadarrTag>>> GetTagsAsync(
        CancellationToken ct = default)
    {
        var result = await GetAsync<List<RadarrTagDto>>("/api/v3/tag", ct);
        return result.Map(dtos =>
            (IReadOnlyList<RadarrTag>)dtos
                .Select(d => new RadarrTag(d.Id, d.Label))
                .ToList());
    }

    // ── Write operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Sets <c>monitored = false</c> on the Radarr movie.
    ///
    /// Uses a GET-then-PUT round-trip via <see cref="JsonNode"/> so all Radarr
    /// fields are preserved in the PUT body — no risk of clearing data we don't
    /// know about. If the GET fails (transient or definitive), the failure is
    /// propagated and no PUT is attempted.
    /// </summary>
    public async Task<HttpResult<RadarrMovie>> UnmonitorMovieAsync(
        int id, CancellationToken ct = default)
    {
        var getResult = await GetAsync<JsonNode?>($"/api/v3/movie/{id}", ct);

        switch (getResult)
        {
            case HttpResult<JsonNode?>.TransientFailure t:
                return HttpResult<RadarrMovie>.Transient(t.Reason, t.Exception);

            case HttpResult<JsonNode?>.DefinitiveFailure d:
                return HttpResult<RadarrMovie>.Definitive(d.StatusCode, d.Reason);

            case HttpResult<JsonNode?>.Success { Value: JsonObject movieNode }:
                movieNode["monitored"] = false;
                var putResult = await PutAsync<RadarrMovieDto>(
                    $"/api/v3/movie/{id}", movieNode, ct);
                return putResult.Map(ToMovie);

            default:
                return HttpResult<RadarrMovie>.Transient(
                    $"Unexpected response shape from Radarr GET /api/v3/movie/{id}");
        }
    }

    /// <summary>
    /// Changes the quality profile of a Radarr movie.
    ///
    /// Uses a GET-then-PUT round-trip via <see cref="JsonNode"/> so all Radarr fields
    /// are preserved in the PUT body — only <c>qualityProfileId</c> is mutated.
    /// </summary>
    public async Task<HttpResult<EmptyResponse>> UpdateMovieQualityProfileAsync(
        int id, int profileId, CancellationToken ct = default)
    {
        var getResult = await GetAsync<JsonNode?>($"/api/v3/movie/{id}", ct);

        switch (getResult)
        {
            case HttpResult<JsonNode?>.TransientFailure t:
                return HttpResult<EmptyResponse>.Transient(t.Reason, t.Exception);

            case HttpResult<JsonNode?>.DefinitiveFailure d:
                return HttpResult<EmptyResponse>.Definitive(d.StatusCode, d.Reason);

            case HttpResult<JsonNode?>.Success { Value: JsonObject movieNode }:
                movieNode["qualityProfileId"] = profileId;
                var putResult = await PutAsync<EmptyResponse>($"/api/v3/movie/{id}", movieNode, ct);
                return putResult;

            default:
                return HttpResult<EmptyResponse>.Transient(
                    $"Unexpected response shape from Radarr GET /api/v3/movie/{id}");
        }
    }

    /// <summary>
    /// Triggers Radarr to search for a new release matching the current quality profile.
    /// </summary>
    public Task<HttpResult<EmptyResponse>> TriggerMovieSearchAsync(
        int movieId, CancellationToken ct = default)
        => PostAsync<EmptyResponse>("/api/v3/command",
            new { name = "MoviesSearch", movieIds = new[] { movieId } },
            ct);

    /// <summary>
    /// Permanently deletes a movie from Radarr.
    ///
    /// <paramref name="deleteFiles"/> removes the files on disk.
    /// <paramref name="addExclusion"/> prevents Radarr re-importing the movie.
    ///
    /// <b>Callers must unmonitor before calling this</b> (Story 4.3 responsibility).
    /// </summary>
    public Task<HttpResult<EmptyResponse>> DeleteMovieAsync(
        int id, bool deleteFiles, bool addExclusion, CancellationToken ct = default)
        => DeleteAsync(
            $"/api/v3/movie/{id}" +
            $"?deleteFiles={(deleteFiles ? "true" : "false")}" +
            $"&addImportExclusion={(addExclusion ? "true" : "false")}",
            ct);

    /// <summary>
    /// Adds a permanent Radarr import exclusion so the movie is never re-downloaded.
    /// Call after delete when <paramref name="addExclusion"/> was false in
    /// <see cref="DeleteMovieAsync"/> but an explicit exclusion record is still desired.
    /// </summary>
    public Task<HttpResult<EmptyResponse>> AddImportExclusionAsync(
        int tmdbId, string title, int year, CancellationToken ct = default)
        => PostAsync<EmptyResponse>("/api/v3/exclusions",
            new RadarrExclusionRequestDto { TmdbId = tmdbId, Name = title, Year = year },
            ct);

    // ── DTO → domain model mappers ───────────────────────────────────────────

    private static RadarrMovie ToMovie(RadarrMovieDto d) => new(
        d.Id,
        d.Title,
        d.Year,
        d.TmdbId,
        d.ImdbId,
        d.Monitored,
        d.HasFile,
        d.QualityProfileId,
        d.Tags.AsReadOnly(),
        d.Path,
        d.SizeOnDisk,
        ParseDate(d.Added),
        d.Status,
        d.MovieFile is null ? null : ToMovieFile(d.MovieFile));

    private static RadarrMovieFile ToMovieFile(RadarrMovieFileDto d) => new(
        d.Id,
        d.MovieId,
        d.RelativePath,
        d.Path,
        d.Size,
        ParseDate(d.DateAdded),
        d.ReleaseGroup,
        d.QualityCutoffNotMet);

    private static RadarrHistoryRecord ToHistoryRecord(RadarrHistoryRecordDto d) => new(
        d.Id,
        d.MovieId,
        d.EventType,
        ParseDate(d.Date) ?? DateTimeOffset.MinValue,
        d.Data.ImportedPath,
        d.Data.DroppedPath);

    // Radarr encodes "never set" dates as 0001-01-01T00:00:00Z — treat those as null.
    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(raw, out var dt) && dt.Year > 1 ? dt : null;
    }
}
