using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Integrations.Sonarr.Models;

namespace Sweeprr.Tests.Integrations;

/// <summary>
/// Unit tests for <see cref="SonarrClient"/> REST methods.
/// Uses stub <see cref="HttpMessageHandler"/>s (no Polly) to verify URL construction,
/// JSON deserialization, domain-model mapping, and failure propagation.
/// </summary>
public class SonarrClientTests
{
    private const string BaseUrl = "http://sonarr:8989";
    private const string ApiKey  = "test-sonarr-key";

    // ── GetSeriesAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSeriesAsync_Maps_Series_With_Seasons()
    {
        var client = MakeClient(200, """
            [
                {
                    "id": 1, "title": "Breaking Bad", "year": 2008,
                    "tvdbId": 81189, "imdbId": "tt0903747",
                    "monitored": true, "qualityProfileId": 3,
                    "tags": [1, 2], "path": "/tv/Breaking Bad",
                    "added": "2021-03-10T00:00:00Z",
                    "seasons": [
                        {
                            "seasonNumber": 1, "monitored": true,
                            "statistics": {
                                "episodeFileCount": 7, "episodeCount": 7,
                                "totalEpisodeCount": 7, "sizeOnDisk": 10000000000,
                                "percentOfEpisodes": 100.0
                            }
                        },
                        {
                            "seasonNumber": 2, "monitored": false,
                            "statistics": {
                                "episodeFileCount": 13, "episodeCount": 13,
                                "totalEpisodeCount": 13, "sizeOnDisk": 20000000000,
                                "percentOfEpisodes": 100.0
                            }
                        }
                    ]
                }
            ]
            """);

        var result = await client.GetSeriesAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<SonarrSeries>>.Success>(result);
        Assert.Single(s.Value);

        var bb = s.Value[0];
        Assert.Equal(1,                "Breaking Bad" == bb.Title ? 1 : 0);
        Assert.Equal("Breaking Bad",   bb.Title);
        Assert.Equal(2008,             bb.Year);
        Assert.Equal(81189,            bb.TvdbId);
        Assert.Equal("tt0903747",      bb.ImdbId);
        Assert.True(bb.Monitored);
        Assert.Equal(2,                bb.Seasons.Count);

        var s1 = bb.Seasons[0];
        Assert.Equal(1,              s1.SeasonNumber);
        Assert.True(s1.Monitored);
        Assert.Equal(7,              s1.EpisodeFileCount);
        Assert.Equal(10_000_000_000L, s1.SizeOnDisk);

        var s2 = bb.Seasons[1];
        Assert.Equal(2,    s2.SeasonNumber);
        Assert.False(s2.Monitored);
    }

    [Fact]
    public async Task GetSeriesAsync_Empty_Returns_Empty_List()
    {
        var result = await MakeClient(200, "[]").GetSeriesAsync();
        var s = Assert.IsType<HttpResult<IReadOnlyList<SonarrSeries>>.Success>(result);
        Assert.Empty(s.Value);
    }

    [Fact]
    public async Task GetSeriesAsync_401_Returns_DefinitiveFailure()
    {
        var result = await MakeClient(401).GetSeriesAsync();
        Assert.IsType<HttpResult<IReadOnlyList<SonarrSeries>>.DefinitiveFailure>(result);
    }

    [Fact]
    public async Task GetSeriesAsync_Calls_Correct_Path()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetSeriesAsync();
        Assert.Contains("/api/v3/series", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSeriesAsync_Includes_XApiKey_Header()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetSeriesAsync();
        Assert.Contains(ApiKey, capture.LastXApiKey ?? string.Empty);
    }

    // ── GetSeriesByIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetSeriesByIdAsync_404_Returns_IsNotFound()
    {
        var result = await MakeClient(404).GetSeriesByIdAsync(999);
        Assert.True(result.IsNotFound);
    }

    [Fact]
    public async Task GetSeriesByIdAsync_Includes_Id_In_Path()
    {
        var capture = new CapturingHandler(200, SingleSeriesJson(42));
        await MakeClient(capture).GetSeriesByIdAsync(42);
        Assert.Contains("/api/v3/series/42", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSeriesByIdAsync_Maps_Season_Statistics_Missing_To_Zeroes()
    {
        var client = MakeClient(200, """
            {
                "id": 5, "title": "Test", "year": 2020,
                "tvdbId": 99, "monitored": true, "qualityProfileId": 1,
                "tags": [], "added": "2020-01-01T00:00:00Z",
                "seasons": [
                    {"seasonNumber": 0, "monitored": false}
                ]
            }
            """);

        var result = await client.GetSeriesByIdAsync(5);
        var s = Assert.IsType<HttpResult<SonarrSeries>.Success>(result);
        var specials = s.Value.Seasons[0];
        Assert.Equal(0, specials.EpisodeFileCount);
        Assert.Equal(0L, specials.SizeOnDisk);
    }

    // ── GetEpisodesAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetEpisodesAsync_Maps_Episodes()
    {
        var client = MakeClient(200, """
            [
                {
                    "id": 101, "seriesId": 1, "seasonNumber": 1,
                    "episodeNumber": 1, "episodeFileId": 201, "tvdbId": 3000001,
                    "hasFile": true, "monitored": true,
                    "airDate": "2008-01-20", "title": "Pilot"
                },
                {
                    "id": 102, "seriesId": 1, "seasonNumber": 1,
                    "episodeNumber": 2, "hasFile": false, "monitored": true,
                    "title": "Cat's in the Bag"
                }
            ]
            """);

        var result = await client.GetEpisodesAsync(1);

        var s = Assert.IsType<HttpResult<IReadOnlyList<SonarrEpisode>>.Success>(result);
        Assert.Equal(2, s.Value.Count);

        var pilot = s.Value[0];
        Assert.Equal(101,   pilot.Id);
        Assert.Equal(1,     pilot.SeasonNumber);
        Assert.Equal(1,     pilot.EpisodeNumber);
        Assert.Equal(201,   pilot.EpisodeFileId);
        Assert.True(pilot.HasFile);
        Assert.True(pilot.Monitored);
        Assert.Equal("Pilot", pilot.Title);
        Assert.Equal(2008,    pilot.AirDate?.Year);

        var ep2 = s.Value[1];
        Assert.False(ep2.HasFile);
        Assert.Null(ep2.EpisodeFileId);
    }

    [Fact]
    public async Task GetEpisodesAsync_Includes_SeriesId_In_Path()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetEpisodesAsync(77);
        Assert.Contains("seriesId=77", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── GetEpisodeFilesAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetEpisodeFilesAsync_Maps_Files()
    {
        var client = MakeClient(200, """
            [
                {
                    "id": 201, "seriesId": 1, "seasonNumber": 1,
                    "relativePath": "Season 01/Episode 01.mkv",
                    "path": "/tv/BB/Season 01/Episode 01.mkv",
                    "size": 1500000000,
                    "dateAdded": "2021-03-10T08:00:00Z",
                    "releaseGroup": "YIFY"
                }
            ]
            """);

        var result = await client.GetEpisodeFilesAsync(1);

        var s = Assert.IsType<HttpResult<IReadOnlyList<SonarrEpisodeFile>>.Success>(result);
        Assert.Single(s.Value);
        var f = s.Value[0];
        Assert.Equal(201,                        f.Id);
        Assert.Equal(1,                          f.SeasonNumber);
        Assert.Equal(1_500_000_000L,             f.Size);
        Assert.Equal("YIFY",                     f.ReleaseGroup);
        Assert.Equal(2021,                       f.DateAdded?.Year);
    }

    [Fact]
    public async Task GetEpisodeFilesAsync_Includes_SeriesId_In_Path()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetEpisodeFilesAsync(88);
        Assert.Contains("/api/v3/episodefile", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("seriesId=88", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── GetSeriesHistoryAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetSeriesHistoryAsync_Maps_Records()
    {
        var client = MakeClient(200, """
            [
                {
                    "id": 500, "seriesId": 1, "episodeId": 101,
                    "eventType": "downloadFolderImported",
                    "date": "2021-03-10T09:00:00Z",
                    "data": {
                        "importedPath": "/tv/BB/S01E01.mkv",
                        "droppedPath": "/downloads/BB.S01E01.mkv"
                    }
                }
            ]
            """);

        var result = await client.GetSeriesHistoryAsync(1);

        var s = Assert.IsType<HttpResult<IReadOnlyList<SonarrHistoryRecord>>.Success>(result);
        Assert.Single(s.Value);
        var rec = s.Value[0];
        Assert.Equal(500,                        rec.Id);
        Assert.Equal(1,                          rec.SeriesId);
        Assert.Equal(101,                        rec.EpisodeId);
        Assert.Equal("downloadFolderImported",   rec.EventType);
        Assert.Equal(2021,                       rec.Date.Year);
        Assert.Equal("/tv/BB/S01E01.mkv",        rec.ImportedPath);
    }

    [Fact]
    public async Task GetSeriesHistoryAsync_Includes_SeriesId_Param()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetSeriesHistoryAsync(33);
        Assert.Contains("seriesId=33", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── GetQualityProfilesAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetQualityProfilesAsync_Maps_Profiles()
    {
        var client = MakeClient(200, """
            [{"id": 1, "name": "Any", "upgradeAllowed": false},
             {"id": 2, "name": "HD-1080p", "upgradeAllowed": true}]
            """);

        var result = await client.GetQualityProfilesAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<SonarrQualityProfile>>.Success>(result);
        Assert.Equal(2, s.Value.Count);
        Assert.Equal("Any",     s.Value[0].Name);
        Assert.Equal("HD-1080p", s.Value[1].Name);
        Assert.True(s.Value[1].UpgradeAllowed);
    }

    // ── GetTagsAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTagsAsync_Maps_Tags()
    {
        var client = MakeClient(200, """[{"id": 10, "label": "anime"}]""");

        var result = await client.GetTagsAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<SonarrTag>>.Success>(result);
        Assert.Single(s.Value);
        Assert.Equal(10, s.Value[0].Id);
        Assert.Equal("anime", s.Value[0].Label);
    }

    // ── UnmonitorSeasonAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UnmonitorSeasonAsync_Sets_Season_Monitored_False()
    {
        var seq = new SequenceHandler(
            Response(200, SingleSeriesJson(1, season1Monitored: true, season2Monitored: true)),
            Response(200, SingleSeriesJson(1, season1Monitored: false, season2Monitored: true)));

        var result = await MakeClient(seq).UnmonitorSeasonAsync(1, seasonNumber: 1);

        var s = Assert.IsType<HttpResult<SonarrSeries>.Success>(result);
        Assert.Equal(2, seq.CallCount); // GET + PUT
        Assert.False(s.Value.Seasons[0].Monitored);
    }

    [Fact]
    public async Task UnmonitorSeasonAsync_PUT_Body_Has_Season_Monitored_False()
    {
        var capturingSeq = new CapturingSequenceHandler(
            Response(200, SingleSeriesJson(3, season1Monitored: true, season2Monitored: true)),
            Response(200, SingleSeriesJson(3, season1Monitored: false, season2Monitored: true)));

        await MakeClient(capturingSeq).UnmonitorSeasonAsync(3, seasonNumber: 1);

        var putBody = capturingSeq.Bodies.ElementAtOrDefault(1);
        Assert.NotNull(putBody);

        using var doc = JsonDocument.Parse(putBody!);
        var seasons = doc.RootElement.GetProperty("seasons");
        var s1 = seasons.EnumerateArray()
            .First(s => s.GetProperty("seasonNumber").GetInt32() == 1);
        Assert.False(s1.GetProperty("monitored").GetBoolean());

        var s2 = seasons.EnumerateArray()
            .First(s => s.GetProperty("seasonNumber").GetInt32() == 2);
        Assert.True(s2.GetProperty("monitored").GetBoolean());
    }

    [Fact]
    public async Task UnmonitorSeasonAsync_Season_Not_Found_Returns_DefinitiveFailure()
    {
        var seq = new SequenceHandler(
            Response(200, SingleSeriesJson(1)));

        var result = await MakeClient(seq).UnmonitorSeasonAsync(1, seasonNumber: 99);

        var d = Assert.IsType<HttpResult<SonarrSeries>.DefinitiveFailure>(result);
        Assert.Equal(404, d.StatusCode);
        Assert.Equal(1, seq.CallCount); // GET only — no PUT attempted
    }

    [Fact]
    public async Task UnmonitorSeasonAsync_Propagates_GET_TransientFailure()
    {
        var seq = new SequenceHandler(Response(503, null));
        var result = await MakeClient(seq).UnmonitorSeasonAsync(1, 1);
        Assert.IsType<HttpResult<SonarrSeries>.TransientFailure>(result);
        Assert.Equal(1, seq.CallCount);
    }

    [Fact]
    public async Task UnmonitorSeasonAsync_Calls_Correct_Paths()
    {
        var capturingSeq = new CapturingSequenceHandler(
            Response(200, SingleSeriesJson(10, season1Monitored: true)),
            Response(200, SingleSeriesJson(10, season1Monitored: false)));

        await MakeClient(capturingSeq).UnmonitorSeasonAsync(10, 1);

        Assert.Contains("/api/v3/series/10", capturingSeq.Requests[0].uri,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/v3/series/10", capturingSeq.Requests[1].uri,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── UnmonitorEpisodesAsync ────────────────────────────────────────────────

    [Fact]
    public async Task UnmonitorEpisodesAsync_200_Returns_Success()
    {
        var result = await MakeClient(200, "").UnmonitorEpisodesAsync([101, 102, 103]);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UnmonitorEpisodesAsync_Calls_Episode_Monitor_Path()
    {
        var capture = new CapturingHandler(200, "");
        await MakeClient(capture).UnmonitorEpisodesAsync([1, 2]);
        Assert.Contains("/api/v3/episode/monitor", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnmonitorEpisodesAsync_Sends_Monitored_False_Body()
    {
        var capture = new BodyCapturingHandler(200, "");
        await MakeClient(capture).UnmonitorEpisodesAsync([10, 20]);

        using var doc = JsonDocument.Parse(capture.LastBody ?? "{}");
        Assert.False(doc.RootElement.GetProperty("monitored").GetBoolean());
        var ids = doc.RootElement.GetProperty("episodeIds").EnumerateArray()
            .Select(e => e.GetInt32()).ToList();
        Assert.Contains(10, ids);
        Assert.Contains(20, ids);
    }

    // ── DeleteSeriesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSeriesAsync_204_Returns_Success()
    {
        var result = await MakeClient(204).DeleteSeriesAsync(1, deleteFiles: true, addExclusion: false);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteSeriesAsync_Includes_Query_Params()
    {
        var capture = new CapturingHandler(204, null);
        await MakeClient(capture).DeleteSeriesAsync(9, deleteFiles: true, addExclusion: true);

        var uri = capture.LastRequestUri ?? string.Empty;
        Assert.Contains("/api/v3/series/9", uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deleteFiles=true", uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("addImportListExclusion=true", uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteSeriesAsync_False_Params_Send_False()
    {
        var capture = new CapturingHandler(204, null);
        await MakeClient(capture).DeleteSeriesAsync(9, deleteFiles: false, addExclusion: false);

        var uri = capture.LastRequestUri ?? string.Empty;
        Assert.Contains("deleteFiles=false", uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("addImportListExclusion=false", uri, StringComparison.OrdinalIgnoreCase);
    }

    // ── DeleteEpisodeFileAsync ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteEpisodeFileAsync_204_Returns_Success()
    {
        var result = await MakeClient(204).DeleteEpisodeFileAsync(201);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteEpisodeFileAsync_Includes_Id_In_Path()
    {
        var capture = new CapturingHandler(204, null);
        await MakeClient(capture).DeleteEpisodeFileAsync(777);
        Assert.Contains("/api/v3/episodefile/777", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── AddImportExclusionAsync ───────────────────────────────────────────────

    [Fact]
    public async Task AddImportExclusionAsync_200_Returns_Success()
    {
        var result = await MakeClient(200, """{"id":1}""")
            .AddImportExclusionAsync(81189, "Breaking Bad");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddImportExclusionAsync_Calls_ImportListExclusion_Path()
    {
        var capture = new CapturingHandler(200, """{"id":1}""");
        await MakeClient(capture).AddImportExclusionAsync(99999, "Test Series");
        Assert.Contains("/api/v3/importlistexclusion", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── Date edge cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetSeriesByIdAsync_Epoch_Added_Date_Returns_Null_Added()
    {
        var client = MakeClient(200, """
            {
                "id": 1, "title": "X", "year": 2020, "tvdbId": 1,
                "monitored": false, "qualityProfileId": 1, "tags": [],
                "added": "0001-01-01T00:00:00Z", "seasons": []
            }
            """);

        var result = await client.GetSeriesByIdAsync(1);
        var s = Assert.IsType<HttpResult<SonarrSeries>.Success>(result);
        Assert.Null(s.Value.Added);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SonarrClient MakeClient(int statusCode, string? body = null)
        => MakeClient(new StubHandler(statusCode, body));

    private static SonarrClient MakeClient(HttpMessageHandler handler)
        => new(new HttpClient(handler), BaseUrl, ApiKey, NullLogger<SonarrClient>.Instance);

    private static string SingleSeriesJson(
        int id,
        bool season1Monitored = true,
        bool season2Monitored = false) => $$"""
        {
            "id": {{id}}, "title": "Test Series {{id}}", "year": 2020,
            "tvdbId": {{id * 1000}}, "monitored": true, "qualityProfileId": 1,
            "tags": [], "added": "2021-01-01T00:00:00Z",
            "seasons": [
                {
                    "seasonNumber": 1,
                    "monitored": {{season1Monitored.ToString().ToLower()}},
                    "statistics": {
                        "episodeFileCount": 5, "episodeCount": 5,
                        "totalEpisodeCount": 5, "sizeOnDisk": 5000000000,
                        "percentOfEpisodes": 100.0
                    }
                },
                {
                    "seasonNumber": 2,
                    "monitored": {{season2Monitored.ToString().ToLower()}},
                    "statistics": {
                        "episodeFileCount": 3, "episodeCount": 3,
                        "totalEpisodeCount": 3, "sizeOnDisk": 3000000000,
                        "percentOfEpisodes": 100.0
                    }
                }
            ]
        }
        """;

    private static HttpResponseMessage Response(int code, string? body)
    {
        var r = new HttpResponseMessage((HttpStatusCode)code);
        if (body is not null)
            r.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return r;
    }

    // ── Stub handlers ─────────────────────────────────────────────────────────

    private sealed class StubHandler(int code, string? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage _, CancellationToken ct)
        {
            var r = new HttpResponseMessage((HttpStatusCode)code);
            if (body is not null)
                r.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return Task.FromResult(r);
        }
    }

    private sealed class CapturingHandler(int code, string? body) : HttpMessageHandler
    {
        public string? LastRequestUri { get; private set; }
        public string? LastXApiKey   { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage req, CancellationToken ct)
        {
            LastRequestUri = req.RequestUri?.ToString();
            if (req.Headers.TryGetValues("X-Api-Key", out var vals))
                LastXApiKey = string.Join(",", vals);

            var r = new HttpResponseMessage((HttpStatusCode)code);
            if (body is not null)
                r.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return Task.FromResult(r);
        }
    }

    private sealed class BodyCapturingHandler(int code, string? responseBody) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage req, CancellationToken ct)
        {
            if (req.Content is not null)
                LastBody = await req.Content.ReadAsStringAsync(ct);

            var r = new HttpResponseMessage((HttpStatusCode)code);
            if (responseBody is not null)
                r.Content = new StringContent(responseBody, Encoding.UTF8, "application/json");
            return r;
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _queue;
        public int CallCount { get; private set; }

        public SequenceHandler(params HttpResponseMessage[] responses)
            => _queue = new Queue<HttpResponseMessage>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage _, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(
                _queue.TryDequeue(out var r)
                    ? r
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class CapturingSequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _queue;

        public List<(string uri, string method, string? body)> Requests { get; } = [];
        public List<string?> Bodies => Requests.Select(r => r.body).ToList();

        public CapturingSequenceHandler(params HttpResponseMessage[] responses)
            => _queue = new Queue<HttpResponseMessage>(responses);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage req, CancellationToken ct)
        {
            string? body = null;
            if (req.Content is not null)
                body = await req.Content.ReadAsStringAsync(ct);

            Requests.Add((
                req.RequestUri?.ToString() ?? string.Empty,
                req.Method.Method,
                body));

            return _queue.TryDequeue(out var r)
                ? r
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}
