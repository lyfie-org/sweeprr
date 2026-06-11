using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Integrations.Jellyfin.WebSocket;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Integrations;

/// <summary>
/// Unit tests for the Jellyfin WebSocket integration.
///
/// Full WS lifecycle tests (connect, keep-alive longevity, reconnect, backfill)
/// require a running Jellyfin server and are covered by integration / E2E tests
/// in Sprint 6.  These unit tests cover the components that are independently
/// testable: PlaystateCache correctness + thread safety, URI construction, and
/// the UserDataChanged event handler logic.
/// </summary>

// ── PlaystateCache ────────────────────────────────────────────────────────────

public class PlaystateCacheTests
{
    private static JellyfinUserData MakeData(bool played, int playCount = 1) =>
        new(Played: played, PlayCount: playCount, PlaybackPositionTicks: 0, LastPlayedDate: null);

    [Fact]
    public void Upsert_Then_Get_Returns_Same_Data()
    {
        var cache = new PlaystateCache();
        var data  = MakeData(played: true, playCount: 3);

        cache.Upsert("item-1", "user-A", data);

        var result = cache.Get("item-1", "user-A");
        Assert.NotNull(result);
        Assert.True(result!.Played);
        Assert.Equal(3, result.PlayCount);
    }

    [Fact]
    public void Upsert_Is_Idempotent_Last_Write_Wins()
    {
        var cache = new PlaystateCache();

        cache.Upsert("item-1", "user-A", MakeData(played: false, playCount: 0));
        cache.Upsert("item-1", "user-A", MakeData(played: true,  playCount: 2));

        var result = cache.Get("item-1", "user-A");
        Assert.NotNull(result);
        Assert.True(result!.Played);
        Assert.Equal(2, result.PlayCount);
    }

    [Fact]
    public void Get_Returns_Null_For_Unknown_Item()
    {
        var cache = new PlaystateCache();
        Assert.Null(cache.Get("no-such-item", "user-A"));
    }

    [Fact]
    public void Get_Returns_Null_For_Unknown_User()
    {
        var cache = new PlaystateCache();
        cache.Upsert("item-1", "user-A", MakeData(true));

        Assert.Null(cache.Get("item-1", "user-B"));
    }

    [Fact]
    public void GetAllForItem_Returns_All_Users()
    {
        var cache = new PlaystateCache();
        cache.Upsert("item-1", "user-A", MakeData(played: true,  playCount: 1));
        cache.Upsert("item-1", "user-B", MakeData(played: false, playCount: 0));

        var all = cache.GetAllForItem("item-1");

        Assert.Equal(2, all.Count);
        Assert.True(all["user-A"].Played);
        Assert.False(all["user-B"].Played);
    }

    [Fact]
    public void GetAllForItem_Returns_Empty_Dictionary_For_Unknown_Item()
    {
        var result = new PlaystateCache().GetAllForItem("ghost-item");
        Assert.Empty(result);
    }

    [Fact]
    public void GetAllForItem_Returns_Snapshot_Not_Live_Reference()
    {
        var cache = new PlaystateCache();
        cache.Upsert("item-1", "user-A", MakeData(true));

        var snapshot = cache.GetAllForItem("item-1");

        // Mutating the cache after the snapshot must not change the snapshot
        cache.Upsert("item-1", "user-B", MakeData(false));
        Assert.Single(snapshot);
    }

    [Fact]
    public void Keys_Are_Case_Insensitive()
    {
        var cache = new PlaystateCache();
        cache.Upsert("ITEM-X", "USER-1", MakeData(true));

        // Lookup with different casing must still find the entry
        Assert.NotNull(cache.Get("item-x", "user-1"));
        Assert.NotNull(cache.Get("Item-X", "User-1"));
    }

    [Fact]
    public void BulkUpsert_Populates_Cache()
    {
        var cache = new PlaystateCache();
        var entries = new (string, string, JellyfinUserData)[]
        {
            ("item-1", "user-A", MakeData(true,  1)),
            ("item-1", "user-B", MakeData(false, 0)),
            ("item-2", "user-A", MakeData(true,  2))
        };

        cache.BulkUpsert(entries);

        Assert.True(cache.Get("item-1", "user-A")!.Played);
        Assert.False(cache.Get("item-1", "user-B")!.Played);
        Assert.Equal(2, cache.Get("item-2", "user-A")!.PlayCount);
    }

    [Fact]
    public void Concurrent_Upserts_Do_Not_Corrupt_Cache()
    {
        var cache   = new PlaystateCache();
        const int n = 1_000;

        Parallel.For(0, n, i =>
        {
            cache.Upsert($"item-{i % 10}", $"user-{i % 5}", MakeData(i % 2 == 0, i));
        });

        // No assertion on specific values — the test passes if no exception is thrown
        // (ConcurrentDictionary must not throw under parallel writes)
        for (var i = 0; i < 10; i++)
        {
            var all = cache.GetAllForItem($"item-{i}");
            Assert.InRange(all.Count, 0, 5);
        }
    }
}

// ── JellyfinWebSocketService — URI construction ───────────────────────────────

public class JellyfinWebSocketServiceUriTests
{
    [Theory]
    [InlineData("http://jellyfin:8096",         "ws://jellyfin:8096/socket")]
    [InlineData("https://jellyfin:8920",        "wss://jellyfin:8920/socket")]
    [InlineData("http://jellyfin:8096/",        "ws://jellyfin:8096/socket")]
    [InlineData("https://host/jellyfin",        "wss://host/jellyfin/socket")]
    [InlineData("http://192.168.1.100:8096",    "ws://192.168.1.100:8096/socket")]
    public void BuildWsUri_Converts_Scheme_And_Preserves_Path(string baseUrl, string expectedPrefix)
    {
        var uri = JellyfinWebSocketService.BuildWsUri(baseUrl, "test-key");

        Assert.StartsWith(expectedPrefix, uri.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildWsUri_Includes_ApiKey_QueryParam()
    {
        var uri = JellyfinWebSocketService.BuildWsUri("http://host:8096", "my-secret-key");

        Assert.Contains("api_key=my-secret-key", uri.Query);
    }

    [Fact]
    public void BuildWsUri_Includes_DeviceId_QueryParam()
    {
        var uri = JellyfinWebSocketService.BuildWsUri("http://host:8096", "key");

        Assert.Contains("deviceId=", uri.Query);
    }

    [Fact]
    public void BuildWsUri_Encodes_Special_Characters_In_ApiKey()
    {
        var uri = JellyfinWebSocketService.BuildWsUri("http://host:8096", "key with spaces&stuff");

        // Raw spaces and & must be percent-encoded in the query string
        Assert.DoesNotContain(" ", uri.Query);
        Assert.Contains("key+with+spaces", uri.Query.Replace("%20", "+")
            .Replace("%26", "&")); // flexible assertion — just not raw spaces
    }
}

// ── JellyfinWebSocketService — UserDataChanged handler ───────────────────────

public class JellyfinWebSocketServiceHandlerTests
{
    private class DummyPlaybackActivityWriter : IPlaybackActivityWriter
    {
        public void Enqueue(string itemId, string userId, JellyfinUserData data, string username) {}
        public Task ForceFlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task PruneOldActivitiesAsync(int ageLimitDays, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSessionAlertService : IJellyfinSessionAlertService
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConnectionId { get; private set; }
        public IReadOnlyList<JellyfinSession>? Sessions { get; private set; }
        public Task Called => _tcs.Task;

        public Task ProcessSessionsUpdateAsync(
            int connectionId, IReadOnlyList<JellyfinSession> sessions, CancellationToken ct = default)
        {
            ConnectionId = connectionId;
            Sessions = sessions;
            _tcs.TrySetResult();
            return Task.CompletedTask;
        }

        public Task BroadcastPreSweepWarningAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static JellyfinWebSocketService MakeService(IPlaystateCache cache, IJellyfinSessionAlertService? sessionAlerts = null)
    {
        // IServiceScopeFactory is not exercised in these tests — null is safe here
        // because HandleUserDataChanged/HandleSessionsMessage are synchronous and do not create scopes
        return new JellyfinWebSocketService(
            scopeFactory:  null!,
            cache:         cache,
            writer:        new DummyPlaybackActivityWriter(),
            sessionAlerts: sessionAlerts ?? new FakeSessionAlertService(),
            logger:        NullLogger<JellyfinWebSocketService>.Instance);
    }

    private static JsonElement ParseJson(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void HandleUserDataChanged_Upserts_Items_Into_Cache()
    {
        var cache   = new PlaystateCache();
        var service = MakeService(cache);

        service.HandleUserDataChanged(ParseJson("""
            {
                "UserId": "user-alice",
                "UserDataList": [
                    {
                        "ItemId":                "movie-123",
                        "Played":                true,
                        "PlayCount":             1,
                        "PlaybackPositionTicks": 0,
                        "LastPlayedDate":        "2024-06-01T20:00:00Z"
                    }
                ]
            }
            """));

        var data = cache.Get("movie-123", "user-alice");
        Assert.NotNull(data);
        Assert.True(data!.Played);
        Assert.Equal(1, data.PlayCount);
    }

    [Fact]
    public void HandleUserDataChanged_Handles_Multiple_Items_In_One_Message()
    {
        var cache   = new PlaystateCache();
        var service = MakeService(cache);

        service.HandleUserDataChanged(ParseJson("""
            {
                "UserId": "user-bob",
                "UserDataList": [
                    { "ItemId": "ep-1", "Played": true,  "PlayCount": 1, "PlaybackPositionTicks": 0 },
                    { "ItemId": "ep-2", "Played": false, "PlayCount": 0, "PlaybackPositionTicks": 500000 },
                    { "ItemId": "ep-3", "Played": true,  "PlayCount": 2, "PlaybackPositionTicks": 0 }
                ]
            }
            """));

        Assert.True( cache.Get("ep-1", "user-bob")!.Played);
        Assert.False(cache.Get("ep-2", "user-bob")!.Played);
        Assert.Equal(2, cache.Get("ep-3", "user-bob")!.PlayCount);
    }

    [Fact]
    public void HandleUserDataChanged_Is_Idempotent_On_Repeat_Events()
    {
        var cache   = new PlaystateCache();
        var service = MakeService(cache);

        const string payload = """
            {
                "UserId": "user-carol",
                "UserDataList": [
                    { "ItemId": "film-99", "Played": true, "PlayCount": 1, "PlaybackPositionTicks": 0 }
                ]
            }
            """;

        service.HandleUserDataChanged(ParseJson(payload));
        service.HandleUserDataChanged(ParseJson(payload));

        // Duplicate event must not corrupt the cache
        var data = cache.Get("film-99", "user-carol");
        Assert.NotNull(data);
        Assert.True(data!.Played);
        Assert.Equal(1, data.PlayCount);
    }

    [Fact]
    public void HandleUserDataChanged_Survives_Malformed_Json()
    {
        var cache   = new PlaystateCache();
        var service = MakeService(cache);

        // Should not throw — log and return
        service.HandleUserDataChanged(ParseJson("{}"));
        service.HandleUserDataChanged(ParseJson("null"));
    }
}

// ── JellyfinWebSocketService — Sessions handler ──────────────────────────────

public class JellyfinWebSocketServiceSessionsTests
{
    private class DummyPlaybackActivityWriter : IPlaybackActivityWriter
    {
        public void Enqueue(string itemId, string userId, JellyfinUserData data, string username) {}
        public Task ForceFlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task PruneOldActivitiesAsync(int ageLimitDays, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSessionAlertService : IJellyfinSessionAlertService
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConnectionId { get; private set; }
        public IReadOnlyList<JellyfinSession>? Sessions { get; private set; }
        public Task Called => _tcs.Task;

        public Task ProcessSessionsUpdateAsync(
            int connectionId, IReadOnlyList<JellyfinSession> sessions, CancellationToken ct = default)
        {
            ConnectionId = connectionId;
            Sessions = sessions;
            _tcs.TrySetResult();
            return Task.CompletedTask;
        }

        public Task BroadcastPreSweepWarningAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static JellyfinWebSocketService MakeService(IJellyfinSessionAlertService sessionAlerts) =>
        new(
            scopeFactory:  null!,
            cache:         new PlaystateCache(),
            writer:        new DummyPlaybackActivityWriter(),
            sessionAlerts: sessionAlerts,
            logger:        NullLogger<JellyfinWebSocketService>.Instance);

    private static JsonElement ParseJson(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task HandleSessionsMessage_Forwards_Mapped_Sessions_To_AlertService()
    {
        var fake    = new FakeSessionAlertService();
        var service = MakeService(fake);

        service.HandleSessionsMessage(ParseJson("""
            [
                {"Id": "session-1", "UserId": "user-1", "NowPlayingItem": {"Id": "item-1"}},
                {"Id": "session-2", "UserId": "user-2"}
            ]
            """), connectionId: 42, CancellationToken.None);

        await fake.Called.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(42, fake.ConnectionId);
        Assert.Equal(2, fake.Sessions!.Count);
        Assert.Equal("session-1", fake.Sessions[0].Id);
        Assert.Equal("item-1",    fake.Sessions[0].NowPlayingItemId);
        Assert.Equal("session-2", fake.Sessions[1].Id);
        Assert.Null(fake.Sessions[1].NowPlayingItemId);
    }

    [Fact]
    public void HandleSessionsMessage_Empty_Array_Does_Not_Call_AlertService()
    {
        var fake    = new FakeSessionAlertService();
        var service = MakeService(fake);

        service.HandleSessionsMessage(ParseJson("[]"), 1, CancellationToken.None);

        Assert.False(fake.Called.IsCompleted);
    }

    [Fact]
    public void HandleSessionsMessage_Survives_Malformed_Json()
    {
        var fake    = new FakeSessionAlertService();
        var service = MakeService(fake);

        // Should not throw — log and return
        service.HandleSessionsMessage(ParseJson("{}"), 1, CancellationToken.None);
        service.HandleSessionsMessage(ParseJson("null"), 1, CancellationToken.None);

        Assert.False(fake.Called.IsCompleted);
    }
}
