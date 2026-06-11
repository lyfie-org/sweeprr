using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Jellyfin.Models;

namespace Sweeprr.Tests.Integrations;

/// <summary>
/// Unit tests for JellyfinClient REST methods.
/// Uses stub HttpMessageHandlers (no Polly) to verify URL construction,
/// JSON deserialization, domain-model mapping, pagination, and failure propagation.
/// </summary>
public class JellyfinClientTests
{
    private const string BaseUrl = "http://jellyfin:8096";
    private const string ApiKey  = "test-api-key";

    // ── GetSystemInfoAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetSystemInfoAsync_Success_Maps_To_Domain_Model()
    {
        var client = MakeClient(200, """
            {
                "Id": "abc-server-id",
                "ServerName": "My Jellyfin",
                "Version": "10.9.1",
                "LocalAddress": "http://localhost:8096"
            }
            """);

        var result = await client.GetSystemInfoAsync();

        var s = Assert.IsType<HttpResult<JellyfinSystemInfo>.Success>(result);
        Assert.Equal("abc-server-id", s.Value.ServerId);
        Assert.Equal("My Jellyfin",   s.Value.ServerName);
        Assert.Equal("10.9.1",        s.Value.Version);
    }

    [Fact]
    public async Task GetSystemInfoAsync_401_Returns_DefinitiveFailure()
    {
        var result = await MakeClient(401).GetSystemInfoAsync();
        Assert.IsType<HttpResult<JellyfinSystemInfo>.DefinitiveFailure>(result);
    }

    [Fact]
    public async Task GetSystemInfoAsync_Calls_Correct_Path()
    {
        var capture = new CapturingHandler(200, """{"Id":"x","ServerName":"s","Version":"1"}""");
        var client  = MakeClient(capture);

        await client.GetSystemInfoAsync();

        Assert.Contains("/System/Info", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── GetUsersAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsersAsync_Returns_All_Users()
    {
        var client = MakeClient(200, """
            [
                {"Id": "user-1", "Name": "Alice"},
                {"Id": "user-2", "Name": "Bob"}
            ]
            """);

        var result = await client.GetUsersAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<JellyfinUser>>.Success>(result);
        Assert.Equal(2, s.Value.Count);
        Assert.Equal("user-1", s.Value[0].Id);
        Assert.Equal("Alice",  s.Value[0].Name);
        Assert.Equal("user-2", s.Value[1].Id);
        Assert.Equal("Bob",    s.Value[1].Name);
    }

    [Fact]
    public async Task GetUsersAsync_Empty_Array_Returns_Empty_List()
    {
        var result = await MakeClient(200, "[]").GetUsersAsync();
        var s = Assert.IsType<HttpResult<IReadOnlyList<JellyfinUser>>.Success>(result);
        Assert.Empty(s.Value);
    }

    [Fact]
    public async Task GetUsersAsync_Calls_Correct_Path()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetUsersAsync();
        Assert.Contains("/Users", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── GetItemsAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetItemsAsync_Without_UserId_Calls_Items_Endpoint()
    {
        var capture = new CapturingHandler(200, EmptyItemsJson());
        await MakeClient(capture).GetItemsAsync(new GetItemsRequest());
        Assert.DoesNotContain("/Users/", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Items", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetItemsAsync_With_UserId_Calls_Users_Items_Endpoint()
    {
        var capture = new CapturingHandler(200, EmptyItemsJson());
        var request = new GetItemsRequest { UserId = "user-123" };

        await MakeClient(capture).GetItemsAsync(request);

        Assert.Contains("/Users/user-123/Items", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetItemsAsync_Includes_Recursive_And_Pagination_Params()
    {
        var capture = new CapturingHandler(200, EmptyItemsJson());
        var request = new GetItemsRequest { StartIndex = 50, Limit = 25 };

        await MakeClient(capture).GetItemsAsync(request);

        var uri = capture.LastRequestUri ?? string.Empty;
        Assert.Contains("Recursive=true",   uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StartIndex=50",     uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Limit=25",          uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetItemsAsync_Includes_IncludeItemTypes_And_Fields()
    {
        var capture = new CapturingHandler(200, EmptyItemsJson());
        var request = new GetItemsRequest
        {
            IncludeItemTypes = ["Movie", "Series"],
            Fields = ["ProviderIds", "UserData"]
        };

        await MakeClient(capture).GetItemsAsync(request);

        var uri = capture.LastRequestUri ?? string.Empty;
        Assert.Contains("IncludeItemTypes=", uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fields=",           uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetItemsAsync_With_ParentId_Includes_ParentId_Param()
    {
        var capture = new CapturingHandler(200, EmptyItemsJson());
        var request = new GetItemsRequest { ParentId = "lib-folder-id" };

        await MakeClient(capture).GetItemsAsync(request);

        Assert.Contains("ParentId=lib-folder-id", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetItemsAsync_Maps_Items_And_TotalRecordCount()
    {
        var body = """
            {
                "Items": [
                    {
                        "Id": "item-1",
                        "Name": "Inception",
                        "Type": "Movie",
                        "ProviderIds": {"Imdb": "tt1375666", "Tmdb": "27205", "Tvdb": null}
                    }
                ],
                "TotalRecordCount": 42,
                "StartIndex": 0
            }
            """;

        var result = await MakeClient(200, body).GetItemsAsync(new GetItemsRequest());

        var s = Assert.IsType<HttpResult<JellyfinItemsPage>.Success>(result);
        Assert.Equal(42, s.Value.TotalRecordCount);
        Assert.Single(s.Value.Items);
        var item = s.Value.Items[0];
        Assert.Equal("item-1",          item.Id);
        Assert.Equal("Inception",       item.Name);
        Assert.Equal(JellyfinMediaType.Movie, item.Type);
        Assert.Equal("tt1375666",       item.ProviderIds.ImdbId);
        Assert.Equal(27205,             item.ProviderIds.TmdbId);
        Assert.Null(item.ProviderIds.TvdbId);
    }

    // ── GetItemAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetItemAsync_Without_UserId_Calls_Items_ItemId()
    {
        var capture = new CapturingHandler(200, SingleItemJson("item-99", "Movie"));
        await MakeClient(capture).GetItemAsync("item-99");
        Assert.Contains("/Items/item-99", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Users/", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetItemAsync_With_UserId_Calls_Users_Items_ItemId()
    {
        var capture = new CapturingHandler(200, SingleItemJson("item-99", "Movie"));
        await MakeClient(capture).GetItemAsync("item-99", userId: "user-1");
        Assert.Contains("/Users/user-1/Items/item-99", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetItemAsync_404_Returns_DefinitiveFailure_IsNotFound()
    {
        var result = await MakeClient(404).GetItemAsync("missing");
        Assert.True(result.IsNotFound);
    }

    // ── GetUserDataAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserDataAsync_Maps_Played_And_Timestamps()
    {
        var body = """
            {
                "Id": "item-5",
                "Name": "Dune",
                "Type": "Movie",
                "UserData": {
                    "Played": true,
                    "LastPlayedDate": "2024-03-15T20:30:00Z",
                    "PlayCount": 2,
                    "PlaybackPositionTicks": 0
                }
            }
            """;

        var result = await MakeClient(200, body).GetUserDataAsync("user-1", "item-5");

        var s = Assert.IsType<HttpResult<JellyfinUserData>.Success>(result);
        Assert.True(s.Value.Played);
        Assert.Equal(2, s.Value.PlayCount);
        Assert.NotNull(s.Value.LastPlayedDate);
        Assert.Equal(2024, s.Value.LastPlayedDate!.Value.Year);
    }

    [Fact]
    public async Task GetUserDataAsync_Returns_Empty_When_UserData_Absent()
    {
        var body = """{"Id":"item-5","Name":"Dune","Type":"Movie"}""";
        var result = await MakeClient(200, body).GetUserDataAsync("user-1", "item-5");
        var s = Assert.IsType<HttpResult<JellyfinUserData>.Success>(result);
        Assert.False(s.Value.Played);
        Assert.Equal(0, s.Value.PlayCount);
        Assert.Null(s.Value.LastPlayedDate);
    }

    [Fact]
    public async Task GetUserDataAsync_Calls_Correct_User_Item_Path()
    {
        var capture = new CapturingHandler(200, SingleItemJson("item-5", "Movie"));
        await MakeClient(capture).GetUserDataAsync("user-abc", "item-5");
        Assert.Contains("/Users/user-abc/Items/item-5", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── DeleteItemAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteItemAsync_204_Returns_Success()
    {
        var result = await MakeClient(204).DeleteItemAsync("item-del");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteItemAsync_Calls_Correct_Delete_Path()
    {
        var capture = new CapturingHandler(204, null);
        await MakeClient(capture).DeleteItemAsync("item-del");
        Assert.Contains("/Items/item-del", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── ProviderIds normalization ─────────────────────────────────────────────

    [Fact]
    public void ProviderIds_From_Null_Dto_Returns_Empty()
    {
        var ids = ProviderIds.From(null);
        Assert.False(ids.HasAny);
        Assert.Null(ids.ImdbId);
        Assert.Null(ids.TmdbId);
        Assert.Null(ids.TvdbId);
    }

    [Fact]
    public void ProviderIds_Parses_Valid_Numeric_Tmdb_And_Tvdb()
    {
        var dto = new API.Integrations.Jellyfin.Dto.JellyfinProviderIdsDto
        {
            Imdb = "tt1375666",
            Tmdb = "27205",
            Tvdb = "81189"
        };
        var ids = ProviderIds.From(dto);
        Assert.Equal("tt1375666", ids.ImdbId);
        Assert.Equal(27205,       ids.TmdbId);
        Assert.Equal(81189,       ids.TvdbId);
        Assert.True(ids.HasAny);
    }

    [Fact]
    public void ProviderIds_Zero_String_Parses_To_Null()
    {
        var dto = new API.Integrations.Jellyfin.Dto.JellyfinProviderIdsDto
        {
            Tmdb = "0",
            Tvdb = "0"
        };
        var ids = ProviderIds.From(dto);
        Assert.Null(ids.TmdbId);
        Assert.Null(ids.TvdbId);
    }

    [Fact]
    public void ProviderIds_Empty_String_Parses_To_Null()
    {
        var dto = new API.Integrations.Jellyfin.Dto.JellyfinProviderIdsDto
        {
            Imdb = "",
            Tmdb = "  ",
            Tvdb = null
        };
        var ids = ProviderIds.From(dto);
        Assert.Null(ids.ImdbId);
        Assert.Null(ids.TmdbId);
        Assert.Null(ids.TvdbId);
        Assert.False(ids.HasAny);
    }

    [Fact]
    public void ProviderIds_NonNumeric_Tmdb_Tvdb_Returns_Null()
    {
        var dto = new API.Integrations.Jellyfin.Dto.JellyfinProviderIdsDto
        {
            Tmdb = "N/A",
            Tvdb = "unknown"
        };
        var ids = ProviderIds.From(dto);
        Assert.Null(ids.TmdbId);
        Assert.Null(ids.TvdbId);
    }

    [Fact]
    public void ProviderIds_Imdb_Without_Tt_Prefix_Preserved_As_Is()
    {
        // Some Jellyfin instances store IMDB IDs without the tt prefix — preserve them.
        var dto = new API.Integrations.Jellyfin.Dto.JellyfinProviderIdsDto { Imdb = "1375666" };
        var ids = ProviderIds.From(dto);
        Assert.Equal("1375666", ids.ImdbId);
    }

    // ── JellyfinItem type parsing ─────────────────────────────────────────────

    [Theory]
    [InlineData("Movie",   JellyfinMediaType.Movie)]
    [InlineData("Series",  JellyfinMediaType.Series)]
    [InlineData("Season",  JellyfinMediaType.Season)]
    [InlineData("Episode", JellyfinMediaType.Episode)]
    [InlineData("BoxSet",  JellyfinMediaType.Unknown)]
    [InlineData("",        JellyfinMediaType.Unknown)]
    public async Task GetItemAsync_Parses_MediaType_Correctly(
        string rawType, JellyfinMediaType expected)
    {
        var body = $$"""{"Id":"x","Name":"Test","Type":"{{rawType}}"}""";

        var result = await MakeClient(200, body).GetItemAsync("x");

        var s = Assert.IsType<HttpResult<JellyfinItem>.Success>(result);
        Assert.Equal(expected, s.Value.Type);
    }

    // ── GetAllItemsAsync (pagination) ─────────────────────────────────────────

    [Fact]
    public async Task GetAllItemsAsync_Fetches_Multiple_Pages()
    {
        // Page 1: 2 items, TotalRecordCount=3
        // Page 2: 1 item — exhausts the count
        var handler = new SequenceHandler(
            Response(200, PagedItemsJson(
                totalRecordCount: 3, startIndex: 0,
                items: [("id-1", "Movie"), ("id-2", "Movie")])),
            Response(200, PagedItemsJson(
                totalRecordCount: 3, startIndex: 2,
                items: [("id-3", "Series")])));

        var result = await MakeClient(handler).GetAllItemsAsync(
            new GetItemsRequest { Limit = 2 });

        var s = Assert.IsType<HttpResult<IReadOnlyList<JellyfinItem>>.Success>(result);
        Assert.Equal(3, s.Value.Count);
        Assert.Equal("id-1", s.Value[0].Id);
        Assert.Equal("id-3", s.Value[2].Id);
    }

    [Fact]
    public async Task GetAllItemsAsync_Stops_At_MaxItems_Cap()
    {
        // 5 items available, cap at 3
        var handler = new SequenceHandler(
            Response(200, PagedItemsJson(
                totalRecordCount: 5, startIndex: 0,
                items: [("id-1", "Movie"), ("id-2", "Movie"), ("id-3", "Movie")])));

        var result = await MakeClient(handler).GetAllItemsAsync(
            new GetItemsRequest { Limit = 3 }, maxItems: 3);

        var s = Assert.IsType<HttpResult<IReadOnlyList<JellyfinItem>>.Success>(result);
        Assert.Equal(3, s.Value.Count);
    }

    [Fact]
    public async Task GetAllItemsAsync_Returns_TransientFailure_On_Page_Error()
    {
        // First page succeeds, second fails — whole result must be a failure
        // (partial data is unsafe for deletion decisions).
        var handler = new SequenceHandler(
            Response(200, PagedItemsJson(
                totalRecordCount: 4, startIndex: 0,
                items: [("id-1", "Movie"), ("id-2", "Movie")])),
            Response(503, null));

        var result = await MakeClient(handler).GetAllItemsAsync(
            new GetItemsRequest { Limit = 2 });

        Assert.True(result.IsTransient);
    }

    [Fact]
    public async Task GetAllItemsAsync_Single_Page_When_Count_Matches_Limit()
    {
        var handler = new SequenceHandler(
            Response(200, PagedItemsJson(
                totalRecordCount: 2, startIndex: 0,
                items: [("id-1", "Movie"), ("id-2", "Movie")])));

        var result = await MakeClient(handler).GetAllItemsAsync(
            new GetItemsRequest { Limit = 100 });

        var s = Assert.IsType<HttpResult<IReadOnlyList<JellyfinItem>>.Success>(result);
        Assert.Equal(2, s.Value.Count);
        // Only one HTTP call should have been made
        Assert.Equal(1, handler.CallCount);
    }

    // ── Auth header presence ──────────────────────────────────────────────────

    [Fact]
    public async Task All_Requests_Include_MediaBrowser_Authorization_Header()
    {
        var capture = new CapturingHandler(200, """{"Id":"x","ServerName":"s","Version":"1"}""");
        await MakeClient(capture).GetSystemInfoAsync();

        Assert.NotNull(capture.LastAuthorizationHeader);
        Assert.Contains("MediaBrowser Token=",  capture.LastAuthorizationHeader,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Client=",             capture.LastAuthorizationHeader,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeviceId=",           capture.LastAuthorizationHeader,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── GetPlaybackReportingPluginStatusAsync ────────────────────────────────

    [Fact]
    public async Task GetPlaybackReportingPluginStatusAsync_200_Returns_True()
    {
        var result = await MakeClient(200, "[]").GetPlaybackReportingPluginStatusAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task GetPlaybackReportingPluginStatusAsync_404_Returns_False()
    {
        var result = await MakeClient(404).GetPlaybackReportingPluginStatusAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task GetPlaybackReportingPluginStatusAsync_500_Returns_Null()
    {
        var result = await MakeClient(500).GetPlaybackReportingPluginStatusAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPlaybackReportingPluginStatusAsync_Calls_Correct_Path()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetPlaybackReportingPluginStatusAsync();
        Assert.Contains("/PlaybackReporting/Report/Hourly/User", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── GetPlaybackReportBackfillAsync ───────────────────────────────────────

    [Fact]
    public async Task GetPlaybackReportBackfillAsync_Posts_Correct_Path_And_Body()
    {
        var capture = new CapturingHandler(200, """{"colums":[],"results":[]}""");
        await MakeClient(capture).GetPlaybackReportBackfillAsync();

        Assert.Contains("/user_usage_stats/submit_custom_query", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("customQueryString", capture.LastRequestBody ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replaceUserId", capture.LastRequestBody ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY ItemId, UserId", capture.LastRequestBody ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPlaybackReportBackfillAsync_Parses_Colums_And_Results()
    {
        var body = """
            {
                "colums": ["ItemId", "UserId", "PlayCount", "LastPlayed"],
                "results": [
                    ["item-1", "user-1", 5, "2024-01-15 10:30:00"],
                    ["item-2", "user-2", 1, "2024-02-20 08:00:00"]
                ]
            }
            """;

        var result = await MakeClient(200, body).GetPlaybackReportBackfillAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<PlaybackReportRow>>.Success>(result);
        Assert.Equal(2, s.Value.Count);
        Assert.Equal("item-1", s.Value[0].ItemId);
        Assert.Equal("user-1", s.Value[0].UserId);
        Assert.Equal(5, s.Value[0].PlayCount);
        Assert.Equal(2024, s.Value[0].LastPlayed.Year);
    }

    [Fact]
    public async Task GetPlaybackReportBackfillAsync_Accepts_Columns_Spelling()
    {
        var body = """
            {
                "columns": ["ItemId", "UserId", "PlayCount", "LastPlayed"],
                "results": [["item-1", "user-1", 3, "2024-01-01 00:00:00"]]
            }
            """;

        var result = await MakeClient(200, body).GetPlaybackReportBackfillAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<PlaybackReportRow>>.Success>(result);
        Assert.Single(s.Value);
    }

    [Fact]
    public async Task GetPlaybackReportBackfillAsync_Empty_Results_Returns_Empty_List()
    {
        var body = """{"colums":["ItemId","UserId","PlayCount","LastPlayed"],"results":[]}""";

        var result = await MakeClient(200, body).GetPlaybackReportBackfillAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<PlaybackReportRow>>.Success>(result);
        Assert.Empty(s.Value);
    }

    [Fact]
    public async Task GetPlaybackReportBackfillAsync_Missing_Expected_Columns_Returns_Empty()
    {
        var body = """{"colums":["Foo","Bar"],"results":[["a","b"]]}""";

        var result = await MakeClient(200, body).GetPlaybackReportBackfillAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<PlaybackReportRow>>.Success>(result);
        Assert.Empty(s.Value);
    }

    [Fact]
    public async Task GetPlaybackReportBackfillAsync_Skips_Malformed_Row_Without_Throwing()
    {
        var body = """
            {
                "colums": ["ItemId", "UserId", "PlayCount", "LastPlayed"],
                "results": [
                    ["item-1", "user-1", 5, "2024-01-15 10:30:00"],
                    ["item-2", "user-2", "not-a-number", "2024-02-20 08:00:00"],
                    ["item-3", "user-3", 2, "not-a-date"]
                ]
            }
            """;

        var result = await MakeClient(200, body).GetPlaybackReportBackfillAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<PlaybackReportRow>>.Success>(result);
        Assert.Single(s.Value);
        Assert.Equal("item-1", s.Value[0].ItemId);
    }

    [Fact]
    public async Task GetPlaybackReportBackfillAsync_404_Returns_DefinitiveFailure()
    {
        var result = await MakeClient(404).GetPlaybackReportBackfillAsync();
        Assert.True(result.IsNotFound);
    }

    // ── GetActiveSessionsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveSessionsAsync_Returns_Sessions_With_NowPlayingItem()
    {
        var body = """
            [
                {"Id": "session-1", "UserId": "user-1", "NowPlayingItem": {"Id": "item-1"}},
                {"Id": "session-2", "UserId": "user-2"}
            ]
            """;

        var result = await MakeClient(200, body).GetActiveSessionsAsync();

        var s = Assert.IsType<HttpResult<IReadOnlyList<JellyfinSession>>.Success>(result);
        Assert.Equal(2, s.Value.Count);
        Assert.Equal("session-1", s.Value[0].Id);
        Assert.Equal("user-1",    s.Value[0].UserId);
        Assert.Equal("item-1",    s.Value[0].NowPlayingItemId);
        Assert.Equal("session-2", s.Value[1].Id);
        Assert.Null(s.Value[1].NowPlayingItemId);
    }

    [Fact]
    public async Task GetActiveSessionsAsync_Empty_Array_Returns_Empty_List()
    {
        var result = await MakeClient(200, "[]").GetActiveSessionsAsync();
        var s = Assert.IsType<HttpResult<IReadOnlyList<JellyfinSession>>.Success>(result);
        Assert.Empty(s.Value);
    }

    [Fact]
    public async Task GetActiveSessionsAsync_Calls_Correct_Path()
    {
        var capture = new CapturingHandler(200, "[]");
        await MakeClient(capture).GetActiveSessionsAsync();
        Assert.Contains("/Sessions", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // ── SendSessionMessageAsync ───────────────────────────────────────────────

    [Fact]
    public async Task SendSessionMessageAsync_Posts_Correct_Path_And_Body()
    {
        var capture = new CapturingHandler(204, null);
        await MakeClient(capture).SendSessionMessageAsync(
            "session-1", "Sweeprr Notice", "Hello", 8000);

        Assert.Contains("/Sessions/session-1/Message", capture.LastRequestUri ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Header\":\"Sweeprr Notice\"", capture.LastRequestBody ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Text\":\"Hello\"", capture.LastRequestBody ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"TimeoutMs\":8000", capture.LastRequestBody ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendSessionMessageAsync_204_Returns_Success()
    {
        var result = await MakeClient(204).SendSessionMessageAsync(
            "session-1", "Sweeprr Notice", "Hello");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SendSessionMessageAsync_404_Returns_DefinitiveFailure()
    {
        var result = await MakeClient(404).SendSessionMessageAsync(
            "missing-session", "Sweeprr Notice", "Hello");
        Assert.True(result.IsNotFound);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JellyfinClient MakeClient(int statusCode, string? body = null)
        => MakeClient(new StubHandler(statusCode, body));

    private static JellyfinClient MakeClient(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            BaseUrl,
            ApiKey,
            NullLogger<JellyfinClient>.Instance);

    private static string EmptyItemsJson() =>
        """{"Items":[],"TotalRecordCount":0,"StartIndex":0}""";

    private static string SingleItemJson(string id, string type) =>
        $$"""
        {
            "Id": "{{id}}",
            "Name": "Test Item",
            "Type": "{{type}}"
        }
        """;

    private static string PagedItemsJson(
        int totalRecordCount,
        int startIndex,
        (string id, string type)[] items)
    {
        var itemsJson = string.Join(",\n", items.Select(i =>
            $$"""{"Id":"{{i.id}}","Name":"Item {{i.id}}","Type":"{{i.type}}"}"""));
        return $$"""
            {
                "Items": [{{itemsJson}}],
                "TotalRecordCount": {{totalRecordCount}},
                "StartIndex": {{startIndex}}
            }
            """;
    }

    private static HttpResponseMessage Response(int code, string? body)
    {
        var r = new HttpResponseMessage((HttpStatusCode)code);
        if (body is not null)
            r.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return r;
    }

    // ── Stub handlers ─────────────────────────────────────────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string? _body;

        public StubHandler(int code, string? body)
        {
            _code = (HttpStatusCode)code;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage _, CancellationToken ct)
        {
            var r = new HttpResponseMessage(_code);
            if (_body is not null)
                r.Content = new StringContent(_body, Encoding.UTF8, "application/json");
            return Task.FromResult(r);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string? _body;

        public string? LastRequestUri { get; private set; }
        public string? LastAuthorizationHeader { get; private set; }
        public string? LastRequestBody { get; private set; }

        public CapturingHandler(int code, string? body)
        {
            _code = (HttpStatusCode)code;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage req, CancellationToken ct)
        {
            LastRequestUri = req.RequestUri?.ToString();

            if (req.Headers.TryGetValues("Authorization", out var vals))
                LastAuthorizationHeader = string.Join(", ", vals);

            if (req.Content is not null)
                LastRequestBody = await req.Content.ReadAsStringAsync(ct);

            var r = new HttpResponseMessage(_code);
            if (_body is not null)
                r.Content = new StringContent(_body, Encoding.UTF8, "application/json");
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
}
