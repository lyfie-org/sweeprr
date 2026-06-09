using System.Text.Json.Nodes;
using Sweeprr.API.Integrations.Sonarr.Dto;
using Sweeprr.API.Integrations.Sonarr.Models;

namespace Sweeprr.API.Integrations.Sonarr;

/// <summary>
/// Typed HTTP client for the Sonarr v3 REST API.
///
/// <b>Auth:</b> All requests carry the <c>X-Api-Key</c> header, set on
/// <see cref="System.Net.Http.HttpClient.DefaultRequestHeaders"/> at construction time.
///
/// <b>Unmonitor strategy:</b> <see cref="UnmonitorSeasonAsync"/> uses a GET-then-PUT
/// round-trip via <see cref="JsonNode"/> so every Sonarr field is preserved without
/// requiring an exhaustive DTO. Individual episode unmonitoring uses
/// <c>PUT /api/v3/episode/monitor</c> which accepts a minimal request body.
///
/// <b>Caller responsibility (Story 4.3):</b> The "don't delete a series whose latest
/// season is still monitored" guard and the unmonitor-before-delete ordering are
/// enforced by the Sweep Executor, not here.
/// </summary>
public sealed class SonarrClient : ClientBase
{
    private const string ApiKeyHeader = "X-Api-Key";

    /// <param name="http">
    ///   HttpClient from <see cref="IHttpClientFactory"/> with the Polly resilience
    ///   pipeline already wired in.
    /// </param>
    /// <param name="baseUrl">Sonarr base URL, e.g. <c>http://sonarr:8989</c>.</param>
    /// <param name="apiKey">Plain-text API key (decrypted by the factory).</param>
    public SonarrClient(
        HttpClient http,
        string baseUrl,
        string apiKey,
        ILogger<SonarrClient> logger)
        : base(http, baseUrl, logger)
    {
        http.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeader, apiKey);
    }

    // ── Read-only queries ────────────────────────────────────────────────────

    public async Task<HttpResult<IReadOnlyList<SonarrSeries>>> GetSeriesAsync(
        CancellationToken ct = default)
    {
        var result = await GetAsync<List<SonarrSeriesDto>>("/api/v3/series", ct);
        return result.Map(dtos =>
            (IReadOnlyList<SonarrSeries>)dtos.Select(ToSeries).ToList());
    }

    public async Task<HttpResult<SonarrSeries>> GetSeriesByIdAsync(
        int id, CancellationToken ct = default)
    {
        var result = await GetAsync<SonarrSeriesDto>($"/api/v3/series/{id}", ct);
        return result.Map(ToSeries);
    }

    public async Task<HttpResult<IReadOnlyList<SonarrEpisode>>> GetEpisodesAsync(
        int seriesId, CancellationToken ct = default)
    {
        var result = await GetAsync<List<SonarrEpisodeDto>>(
            $"/api/v3/episode?seriesId={seriesId}", ct);
        return result.Map(dtos =>
            (IReadOnlyList<SonarrEpisode>)dtos.Select(ToEpisode).ToList());
    }

    public async Task<HttpResult<IReadOnlyList<SonarrEpisodeFile>>> GetEpisodeFilesAsync(
        int seriesId, CancellationToken ct = default)
    {
        var result = await GetAsync<List<SonarrEpisodeFileDto>>(
            $"/api/v3/episodefile?seriesId={seriesId}", ct);
        return result.Map(dtos =>
            (IReadOnlyList<SonarrEpisodeFile>)dtos.Select(ToEpisodeFile).ToList());
    }

    /// <summary>
    /// Returns all history records for a series, newest first.
    /// History dates drive "age" calculations; callers choose OLDEST vs MOST_RECENT strategy.
    /// </summary>
    public async Task<HttpResult<IReadOnlyList<SonarrHistoryRecord>>> GetSeriesHistoryAsync(
        int seriesId, CancellationToken ct = default)
    {
        var result = await GetAsync<List<SonarrHistoryRecordDto>>(
            $"/api/v3/history/series?seriesId={seriesId}&sortKey=date&sortDirection=descending",
            ct);
        return result.Map(dtos =>
            (IReadOnlyList<SonarrHistoryRecord>)dtos.Select(ToHistoryRecord).ToList());
    }

    public async Task<HttpResult<IReadOnlyList<SonarrQualityProfile>>> GetQualityProfilesAsync(
        CancellationToken ct = default)
    {
        var result = await GetAsync<List<SonarrQualityProfileDto>>("/api/v3/qualityprofile", ct);
        return result.Map(dtos =>
            (IReadOnlyList<SonarrQualityProfile>)dtos
                .Select(d => new SonarrQualityProfile(d.Id, d.Name, d.UpgradeAllowed))
                .ToList());
    }

    public async Task<HttpResult<IReadOnlyList<SonarrTag>>> GetTagsAsync(
        CancellationToken ct = default)
    {
        var result = await GetAsync<List<SonarrTagDto>>("/api/v3/tag", ct);
        return result.Map(dtos =>
            (IReadOnlyList<SonarrTag>)dtos
                .Select(d => new SonarrTag(d.Id, d.Label))
                .ToList());
    }

    // ── Write operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Sets <c>monitored = false</c> on the specified season within a Sonarr series.
    ///
    /// Uses a GET-then-PUT round-trip via <see cref="JsonNode"/> so all Sonarr
    /// series fields are preserved in the PUT body.  If the GET fails, or the
    /// <paramref name="seasonNumber"/> is not found in the series, the failure is
    /// returned and no PUT is attempted.
    ///
    /// <b>The "don't delete a series whose latest season is still monitored" guard
    /// is the Sweep Executor's responsibility (Story 4.3).</b>
    /// </summary>
    public async Task<HttpResult<SonarrSeries>> UnmonitorSeasonAsync(
        int seriesId, int seasonNumber, CancellationToken ct = default)
    {
        var getResult = await GetAsync<JsonNode?>($"/api/v3/series/{seriesId}", ct);

        switch (getResult)
        {
            case HttpResult<JsonNode?>.TransientFailure t:
                return HttpResult<SonarrSeries>.Transient(t.Reason, t.Exception);

            case HttpResult<JsonNode?>.DefinitiveFailure d:
                return HttpResult<SonarrSeries>.Definitive(d.StatusCode, d.Reason);

            case HttpResult<JsonNode?>.Success { Value: JsonObject seriesNode }:
                var seasons = seriesNode["seasons"]?.AsArray();
                if (seasons is null)
                    return HttpResult<SonarrSeries>.Transient(
                        $"Series {seriesId} response from Sonarr has no 'seasons' array");

                var found = false;
                foreach (var node in seasons)
                {
                    if (node is JsonObject season &&
                        season["seasonNumber"]?.GetValue<int>() == seasonNumber)
                    {
                        season["monitored"] = false;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return HttpResult<SonarrSeries>.Definitive(404,
                        $"Season {seasonNumber} not found in Sonarr series {seriesId}");

                var putResult = await PutAsync<SonarrSeriesDto>(
                    $"/api/v3/series/{seriesId}", seriesNode, ct);
                return putResult.Map(ToSeries);

            default:
                return HttpResult<SonarrSeries>.Transient(
                    $"Unexpected response shape from Sonarr GET /api/v3/series/{seriesId}");
        }
    }

    /// <summary>
    /// Sets <c>monitored = false</c> on the series root AND on every season.
    ///
    /// Uses a GET-then-PUT round-trip via <see cref="JsonNode"/> so all Sonarr fields
    /// are preserved. This is the correct pre-delete unmonitor for a whole series —
    /// it prevents both new-season and per-episode re-downloads.
    /// </summary>
    public async Task<HttpResult<SonarrSeries>> UnmonitorSeriesAsync(
        int seriesId, CancellationToken ct = default)
    {
        var getResult = await GetAsync<JsonNode?>($"/api/v3/series/{seriesId}", ct);

        switch (getResult)
        {
            case HttpResult<JsonNode?>.TransientFailure t:
                return HttpResult<SonarrSeries>.Transient(t.Reason, t.Exception);

            case HttpResult<JsonNode?>.DefinitiveFailure d:
                return HttpResult<SonarrSeries>.Definitive(d.StatusCode, d.Reason);

            case HttpResult<JsonNode?>.Success { Value: JsonObject seriesNode }:
                seriesNode["monitored"] = false;
                var seasons = seriesNode["seasons"]?.AsArray();
                if (seasons is not null)
                {
                    foreach (var node in seasons)
                    {
                        if (node is JsonObject season)
                            season["monitored"] = false;
                    }
                }
                var putResult = await PutAsync<SonarrSeriesDto>(
                    $"/api/v3/series/{seriesId}", seriesNode, ct);
                return putResult.Map(ToSeries);

            default:
                return HttpResult<SonarrSeries>.Transient(
                    $"Unexpected response shape from Sonarr GET /api/v3/series/{seriesId}");
        }
    }

    /// <summary>
    /// Bulk-sets <c>monitored = false</c> on a list of episodes.
    /// Uses Sonarr's dedicated <c>PUT /api/v3/episode/monitor</c> endpoint,
    /// which accepts a minimal body — no GET required.
    /// </summary>
    public Task<HttpResult<EmptyResponse>> UnmonitorEpisodesAsync(
        IEnumerable<int> episodeIds, CancellationToken ct = default)
        => PutAsync<EmptyResponse>("/api/v3/episode/monitor",
            new SonarrMonitorEpisodesRequestDto
            {
                EpisodeIds = episodeIds.ToList(),
                Monitored  = false
            },
            ct);

    /// <summary>
    /// Changes the quality profile of a Sonarr series.
    ///
    /// Uses a GET-then-PUT round-trip via <see cref="JsonNode"/> so all Sonarr fields
    /// are preserved in the PUT body — only <c>qualityProfileId</c> is mutated.
    /// </summary>
    public async Task<HttpResult<EmptyResponse>> UpdateSeriesQualityProfileAsync(
        int id, int profileId, CancellationToken ct = default)
    {
        var getResult = await GetAsync<JsonNode?>($"/api/v3/series/{id}", ct);

        switch (getResult)
        {
            case HttpResult<JsonNode?>.TransientFailure t:
                return HttpResult<EmptyResponse>.Transient(t.Reason, t.Exception);

            case HttpResult<JsonNode?>.DefinitiveFailure d:
                return HttpResult<EmptyResponse>.Definitive(d.StatusCode, d.Reason);

            case HttpResult<JsonNode?>.Success { Value: JsonObject seriesNode }:
                seriesNode["qualityProfileId"] = profileId;
                var putResult = await PutAsync<EmptyResponse>($"/api/v3/series/{id}", seriesNode, ct);
                return putResult;

            default:
                return HttpResult<EmptyResponse>.Transient(
                    $"Unexpected response shape from Sonarr GET /api/v3/series/{id}");
        }
    }

    /// <summary>
    /// Triggers Sonarr to search for new releases matching the current quality profile.
    /// </summary>
    public Task<HttpResult<EmptyResponse>> TriggerSeriesSearchAsync(
        int seriesId, CancellationToken ct = default)
        => PostAsync<EmptyResponse>("/api/v3/command",
            new { name = "SeriesSearch", seriesId },
            ct);

    /// <summary>
    /// Permanently deletes a series from Sonarr.
    ///
    /// <paramref name="deleteFiles"/> removes files on disk.
    /// <paramref name="addExclusion"/> prevents Sonarr re-importing the series.
    ///
    /// <b>Callers must unmonitor before calling this</b> (Story 4.3 responsibility).
    /// </summary>
    public Task<HttpResult<EmptyResponse>> DeleteSeriesAsync(
        int id, bool deleteFiles, bool addExclusion, CancellationToken ct = default)
        => DeleteAsync(
            $"/api/v3/series/{id}" +
            $"?deleteFiles={(deleteFiles ? "true" : "false")}" +
            $"&addImportListExclusion={(addExclusion ? "true" : "false")}",
            ct);

    /// <summary>Deletes a single episode file from disk via Sonarr.</summary>
    public Task<HttpResult<EmptyResponse>> DeleteEpisodeFileAsync(
        int episodeFileId, CancellationToken ct = default)
        => DeleteAsync($"/api/v3/episodefile/{episodeFileId}", ct);

    /// <summary>
    /// Adds a permanent Sonarr import-list exclusion so the series is never re-downloaded.
    /// </summary>
    public Task<HttpResult<EmptyResponse>> AddImportExclusionAsync(
        int tvdbId, string title, CancellationToken ct = default)
        => PostAsync<EmptyResponse>("/api/v3/importlistexclusion",
            new SonarrImportExclusionRequestDto { TvdbId = tvdbId, Title = title },
            ct);

    // ── DTO → domain model mappers ───────────────────────────────────────────

    private static SonarrSeries ToSeries(SonarrSeriesDto d) => new(
        d.Id,
        d.Title,
        d.Year,
        d.TvdbId,
        d.ImdbId,
        d.Monitored,
        d.QualityProfileId,
        d.Tags.AsReadOnly(),
        d.Path,
        ParseDate(d.Added),
        d.Ended,
        d.Seasons.Select(ToSeason).ToList().AsReadOnly());

    private static SonarrSeason ToSeason(SonarrSeasonDto d) => new(
        d.SeasonNumber,
        d.Monitored,
        d.Statistics?.EpisodeFileCount  ?? 0,
        d.Statistics?.EpisodeCount      ?? 0,
        d.Statistics?.TotalEpisodeCount ?? 0,
        d.Statistics?.SizeOnDisk        ?? 0L);

    private static SonarrEpisode ToEpisode(SonarrEpisodeDto d) => new(
        d.Id,
        d.SeriesId,
        d.SeasonNumber,
        d.EpisodeNumber,
        d.EpisodeFileId,
        d.TvdbId,
        d.HasFile,
        d.Monitored,
        ParseDate(d.AirDate),
        d.Title,
        d.FinaleType);

    private static SonarrEpisodeFile ToEpisodeFile(SonarrEpisodeFileDto d) => new(
        d.Id,
        d.SeriesId,
        d.SeasonNumber,
        d.RelativePath,
        d.Path,
        d.Size,
        ParseDate(d.DateAdded),
        d.ReleaseGroup,
        d.QualityCutoffNotMet);

    private static SonarrHistoryRecord ToHistoryRecord(SonarrHistoryRecordDto d) => new(
        d.Id,
        d.SeriesId,
        d.EpisodeId,
        d.EventType,
        ParseDate(d.Date) ?? DateTimeOffset.MinValue,
        d.Data.ImportedPath,
        d.Data.DroppedPath);

    // Sonarr encodes "never set" dates as 0001-01-01T00:00:00Z — treat those as null.
    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(raw, out var dt) && dt.Year > 1 ? dt : null;
    }
}
