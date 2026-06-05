using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Radarr.Models;

namespace Sweeprr.Tests.Integrations;

/// <summary>
/// Unit tests for <see cref="RadarrClient"/> REST methods.
/// Uses stub <see cref="HttpMessageHandler"/>s (no Polly) to verify URL construction,
/// JSON deserialization, domain-model mapping, and failure propagation.
/// </summary>
public class RadarrClientTests
{
    private const string BaseUrl = "http://radarr:7878";
    private const string ApiKey  = "test-radarr-key";

    // ── GetMoviesAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMoviesAsync_Maps_List_To_Domain_Models()
    {
        var client = MakeClient(200, """
            [
                {
                    "id": 1, "title": "Dune", "year": 2021,
                    "tmdbId": 438631, "imdbId": "tt1160419",
                    "monitored": true, "hasFile": true,
                    "qualityProfileId": 4, "tags": [1, 2],
                    "path": "/movies/Dune", "sizeOnDisk": 8000000000,
                    "added": "2022-01-15T00:00:00Z", "status": "released"
                },
                {
                    "id": 2, "title": "Inception", "year": 2010,
                    "tmdbId": 27205, "monitored": false, "hasFile": false,
                    "qualityProfileId": 4, "tags": [],
                    "sizeOnDisk": 0, "added": "2021-06-01T00:00:00Z"
                }
            ]
            """);

        var result = await client.GetMoviesAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<RadarrMovie>>.Success>(result);
        Assert.Equal(2, s.Value.Count);

        var dune = s.Value[0];
        Assert.Equal(1,             dune.Id);
        Assert.Equal("Dune",        dune.Title);
        Assert.Equal(2021,          dune.Year);
        Assert.Equal(438631,        dune.TmdbId);
        Assert.Equal("tt1160419",   dune.ImdbId);
        Assert.True(dune.Monitored);
        Assert.True(dune.HasFile);
        Assert.Equal(4,             dune.QualityProfileId);
        Assert.Equal(2,             dune.Tags.Count);
        Assert.Equal(8_000_000_000L, dune.SizeOnDisk);
        Assert.Equal(2022,          dune.Added?.Year);

        var inception = s.Value[1];
        Assert.False(inception.Monitored);
        Assert.Null(inception.ImdbId);
        Assert.Empty(inception.Tags);
    }

    [Fact]
    public async Task GetMoviesAsync_Empty_Returns_Empty_List()
    {
        var result = await MakeClient(200, "[]").GetMoviesAsync();
        var s = Assert.IsType<HttpResult<IReadOnlyList<RadarrMovie>>.Success>(result);
        Assert.Empty(s.Value);
    }

    [Fact]
    public async Task GetMoviesAsync_401_Returns_DefinitiveFailure()
    {
        var result = await MakeClient(401).GetMoviesAsync();
        Assert.IsType<HttpResult<IReadOnlyList<RadarrMovie>>.DefinitiveFailure>(result);
    }

    [Fact]
    public async Task GetMoviesAsync_Calls_Correct_Path()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetMoviesAsync();
        Assert.Contains("/api/v3/movie", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMoviesAsync_Includes_XApiKey_Header()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetMoviesAsync();
        Assert.Contains(ApiKey, capture.LastXApiKey ?? string.Empty);
    }

    // ── GetMovieAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMovieAsync_Maps_Single_Movie()
    {
        var client = MakeClient(200, """
            {
                "id": 99, "title": "The Matrix", "year": 1999,
                "tmdbId": 603, "imdbId": "tt0133093",
                "monitored": true, "hasFile": true,
                "qualityProfileId": 2, "tags": [5],
                "path": "/movies/The Matrix", "sizeOnDisk": 5000000000,
                "added": "2020-03-01T12:00:00Z", "status": "released",
                "movieFile": {
                    "id": 10, "movieId": 99,
                    "relativePath": "The Matrix (1999).mkv",
                    "path": "/movies/The Matrix/The Matrix (1999).mkv",
                    "size": 5000000000,
                    "dateAdded": "2020-03-01T12:00:00Z",
                    "releaseGroup": "GROUP"
                }
            }
            """);

        var result = await client.GetMovieAsync(99);

        var s = Assert.IsType<HttpResult<RadarrMovie>.Success>(result);
        Assert.Equal(99,           s.Value.Id);
        Assert.Equal("The Matrix", s.Value.Title);
        Assert.Equal(603,          s.Value.TmdbId);
        Assert.NotNull(s.Value.MovieFile);
        Assert.Equal(10,           s.Value.MovieFile!.Id);
        Assert.Equal(5_000_000_000L, s.Value.MovieFile.Size);
        Assert.Equal("GROUP",      s.Value.MovieFile.ReleaseGroup);
        Assert.Equal(2020,         s.Value.MovieFile.DateAdded?.Year);
    }

    [Fact]
    public async Task GetMovieAsync_404_Returns_IsNotFound()
    {
        var result = await MakeClient(404).GetMovieAsync(999);
        Assert.True(result.IsNotFound);
    }

    [Fact]
    public async Task GetMovieAsync_Includes_Id_In_Path()
    {
        var capture = new CapturingHandler(200, SingleMovieJson(42));
        await MakeClient(capture).GetMovieAsync(42);
        Assert.Contains("/api/v3/movie/42", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── GetMovieHistoryAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMovieHistoryAsync_Maps_History_Records()
    {
        var client = MakeClient(200, """
            [
                {
                    "id": 100, "movieId": 1,
                    "eventType": "downloadFolderImported",
                    "date": "2023-05-10T14:30:00Z",
                    "data": {
                        "importedPath": "/movies/Dune/Dune.mkv",
                        "droppedPath": "/downloads/Dune.mkv"
                    }
                }
            ]
            """);

        var result = await client.GetMovieHistoryAsync(1);

        var s = Assert.IsType<HttpResult<IReadOnlyList<RadarrHistoryRecord>>.Success>(result);
        Assert.Single(s.Value);
        var rec = s.Value[0];
        Assert.Equal(100,                            rec.Id);
        Assert.Equal(1,                              rec.MovieId);
        Assert.Equal("downloadFolderImported",       rec.EventType);
        Assert.Equal(2023,                           rec.Date.Year);
        Assert.Equal("/movies/Dune/Dune.mkv",        rec.ImportedPath);
        Assert.Equal("/downloads/Dune.mkv",          rec.DroppedPath);
    }

    [Fact]
    public async Task GetMovieHistoryAsync_Includes_MovieId_In_Path()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetMovieHistoryAsync(55);
        Assert.Contains("movieId=55", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── GetQualityProfilesAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetQualityProfilesAsync_Maps_Profiles()
    {
        var client = MakeClient(200, """
            [{"id": 1, "name": "HD-1080p", "upgradeAllowed": true},
             {"id": 2, "name": "Any",      "upgradeAllowed": false}]
            """);

        var result = await client.GetQualityProfilesAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<RadarrQualityProfile>>.Success>(result);
        Assert.Equal(2, s.Value.Count);
        Assert.Equal("HD-1080p", s.Value[0].Name);
        Assert.True(s.Value[0].UpgradeAllowed);
        Assert.Equal("Any", s.Value[1].Name);
        Assert.False(s.Value[1].UpgradeAllowed);
    }

    [Fact]
    public async Task GetQualityProfilesAsync_Calls_Correct_Path()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetQualityProfilesAsync();
        Assert.Contains("/api/v3/qualityprofile", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── GetTagsAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTagsAsync_Maps_Tags()
    {
        var client = MakeClient(200, """
            [{"id": 1, "label": "4k"}, {"id": 2, "label": "hdr"}]
            """);

        var result = await client.GetTagsAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<RadarrTag>>.Success>(result);
        Assert.Equal(2, s.Value.Count);
        Assert.Equal(1, s.Value[0].Id);
        Assert.Equal("4k", s.Value[0].Label);
        Assert.Equal("hdr", s.Value[1].Label);
    }

    [Fact]
    public async Task GetTagsAsync_Calls_Correct_Path()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetTagsAsync();
        Assert.Contains("/api/v3/tag", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── UnmonitorMovieAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UnmonitorMovieAsync_Sets_Monitored_False_And_Returns_Updated_Movie()
    {
        // First call (GET) returns monitored=true; second call (PUT) returns monitored=false.
        var seq = new SequenceHandler(
            Response(200, SingleMovieJson(1, monitored: true)),
            Response(200, SingleMovieJson(1, monitored: false)));

        var result = await MakeClient(seq).UnmonitorMovieAsync(1);

        var s = Assert.IsType<HttpResult<RadarrMovie>.Success>(result);
        Assert.False(s.Value.Monitored);
        Assert.Equal(2, seq.CallCount); // GET + PUT
    }

    [Fact]
    public async Task UnmonitorMovieAsync_PUT_Body_Contains_Monitored_False()
    {
        var capturingSeq = new CapturingSequenceHandler(
            Response(200, SingleMovieJson(7, monitored: true)),
            Response(200, SingleMovieJson(7, monitored: false)));

        await MakeClient(capturingSeq).UnmonitorMovieAsync(7);

        var putBody = capturingSeq.Bodies.ElementAtOrDefault(1);
        Assert.NotNull(putBody);
        using var doc = JsonDocument.Parse(putBody!);
        Assert.False(doc.RootElement.GetProperty("monitored").GetBoolean());
    }

    [Fact]
    public async Task UnmonitorMovieAsync_Propagates_GET_TransientFailure()
    {
        // Only one response — the GET returns 503 (transient after Polly)
        var seq = new SequenceHandler(Response(503, null));

        var result = await MakeClient(seq).UnmonitorMovieAsync(1);

        Assert.IsType<HttpResult<RadarrMovie>.TransientFailure>(result);
        Assert.Equal(1, seq.CallCount); // GET only; no PUT attempted
    }

    [Fact]
    public async Task UnmonitorMovieAsync_Propagates_GET_DefinitiveFailure()
    {
        var seq = new SequenceHandler(Response(404, null));

        var result = await MakeClient(seq).UnmonitorMovieAsync(999);

        Assert.True(result.IsNotFound);
        Assert.Equal(1, seq.CallCount); // no PUT
    }

    [Fact]
    public async Task UnmonitorMovieAsync_Calls_Correct_Paths()
    {
        var capturingSeq = new CapturingSequenceHandler(
            Response(200, SingleMovieJson(5, monitored: true)),
            Response(200, SingleMovieJson(5, monitored: false)));

        await MakeClient(capturingSeq).UnmonitorMovieAsync(5);

        Assert.Equal(2, capturingSeq.Requests.Count);
        Assert.Contains("/api/v3/movie/5", capturingSeq.Requests[0].uri,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/v3/movie/5", capturingSeq.Requests[1].uri,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── DeleteMovieAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteMovieAsync_204_Returns_Success()
    {
        var result = await MakeClient(204).DeleteMovieAsync(1, deleteFiles: true, addExclusion: false);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteMovieAsync_Includes_Query_Params()
    {
        var capture = new CapturingHandler(204, null);
        await MakeClient(capture).DeleteMovieAsync(7, deleteFiles: true, addExclusion: true);

        var uri = capture.LastRequestUri ?? string.Empty;
        Assert.Contains("/api/v3/movie/7", uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deleteFiles=true", uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("addImportExclusion=true", uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteMovieAsync_False_Exclusion_Sends_False()
    {
        var capture = new CapturingHandler(204, null);
        await MakeClient(capture).DeleteMovieAsync(7, deleteFiles: false, addExclusion: false);

        var uri = capture.LastRequestUri ?? string.Empty;
        Assert.Contains("deleteFiles=false", uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("addImportExclusion=false", uri, StringComparison.OrdinalIgnoreCase);
    }

    // ── AddImportExclusionAsync ───────────────────────────────────────────────

    [Fact]
    public async Task AddImportExclusionAsync_200_Returns_Success()
    {
        var result = await MakeClient(200, """{"id":1,"tmdbId":12345,"name":"Dune","year":2021}""")
            .AddImportExclusionAsync(12345, "Dune", 2021);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddImportExclusionAsync_Calls_Exclusions_Path()
    {
        var capture = new CapturingHandler(200, """{"id":1}""");
        await MakeClient(capture).AddImportExclusionAsync(999, "Test", 2020);
        Assert.Contains("/api/v3/exclusions", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── Date parsing edge cases ───────────────────────────────────────────────

    [Fact]
    public async Task GetMovieAsync_Epoch_Added_Date_Returns_Null_Added()
    {
        // Radarr uses "0001-01-01T00:00:00Z" for movies not yet added — must map to null.
        var client = MakeClient(200, """
            {
                "id": 1, "title": "X", "year": 2000, "tmdbId": 1,
                "monitored": false, "hasFile": false,
                "qualityProfileId": 1, "tags": [], "sizeOnDisk": 0,
                "added": "0001-01-01T00:00:00Z"
            }
            """);

        var result = await client.GetMovieAsync(1);
        var s = Assert.IsType<HttpResult<RadarrMovie>.Success>(result);
        Assert.Null(s.Value.Added);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RadarrClient MakeClient(int statusCode, string? body = null)
        => MakeClient(new StubHandler(statusCode, body));

    private static RadarrClient MakeClient(HttpMessageHandler handler)
        => new(new HttpClient(handler), BaseUrl, ApiKey, NullLogger<RadarrClient>.Instance);

    private static string SingleMovieJson(int id, bool monitored = true) => $$"""
        {
            "id": {{id}}, "title": "Test Movie {{id}}", "year": 2020,
            "tmdbId": {{id * 1000}}, "monitored": {{monitored.ToString().ToLower()}},
            "hasFile": true, "qualityProfileId": 1, "tags": [],
            "sizeOnDisk": 1000000000, "added": "2021-01-01T00:00:00Z", "status": "released"
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

    /// <summary>
    /// Like <see cref="SequenceHandler"/> but also captures the URI, method, and body
    /// of each request in order — used to verify the GET-then-PUT pattern.
    /// </summary>
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
