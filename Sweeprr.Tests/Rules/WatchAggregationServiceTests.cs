using Sweeprr.API.Models;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.Tests.Rules;

public class WatchAggregationServiceTests
{
    private readonly WatchAggregationService _svc = new();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static UserWatchState Watched(string userId, string itemId = "item1",
        DateTimeOffset? lastPlayed = null, int playCount = 1) =>
        new(userId, itemId, Played: true,
            LastPlayedDate: lastPlayed ?? new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
            PlayCount: playCount, PlaybackPositionTicks: 0);

    private static UserWatchState NotWatched(string userId, string itemId = "item1") =>
        new(userId, itemId, Played: false, LastPlayedDate: null, PlayCount: 0, PlaybackPositionTicks: 0);

    private static UserWatchState InProgress(string userId, string itemId = "item1",
        long ticks = 50_000_000_000) =>
        new(userId, itemId, Played: false, LastPlayedDate: null, PlayCount: 0,
            PlaybackPositionTicks: ticks);

    private static EpisodeWatchData Ep(string epId, params UserWatchState[] states) =>
        new(epId, states);

    // ═══════════════════════════════════════════════════════════════════════════
    //  MOVIE AGGREGATION
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Movie_AllUsersWatched_WatchedByAllTrue()
    {
        var result = _svc.AggregateMovie(
            [Watched("u1"), Watched("u2"), Watched("u3")],
            UserScope.Default);

        Assert.True(result.WatchedByAllUsers);
        Assert.True(result.WatchedByAnyUser);
        Assert.Equal(3, result.SeenByUserCount);
        Assert.False(result.IsProtected);
        Assert.Null(result.ProtectionReason);
    }

    [Fact]
    public void Movie_SomeUsersWatched_WatchedByAllFalse()
    {
        var result = _svc.AggregateMovie(
            [Watched("u1"), NotWatched("u2")],
            UserScope.Default);

        Assert.False(result.WatchedByAllUsers);
        Assert.True(result.WatchedByAnyUser);
        Assert.Equal(1, result.SeenByUserCount);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Movie_NoUsersWatched_NothingTrue()
    {
        var result = _svc.AggregateMovie(
            [NotWatched("u1"), NotWatched("u2")],
            UserScope.Default);

        Assert.False(result.WatchedByAllUsers);
        Assert.False(result.WatchedByAnyUser);
        Assert.Equal(0, result.SeenByUserCount);
    }

    [Fact]
    public void Movie_InProgress_IsProtected()
    {
        var result = _svc.AggregateMovie(
            [Watched("u1"), InProgress("u2")],
            UserScope.Default);

        Assert.True(result.IsProtected);
        Assert.Contains("u2", result.ProtectionReason!);
        Assert.Contains("currently watching", result.ProtectionReason!);
    }

    [Fact]
    public void Movie_LatestLastWatched_PicksMostRecent()
    {
        var earlier = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2025, 5, 20, 0, 0, 0, TimeSpan.Zero);

        var result = _svc.AggregateMovie(
            [Watched("u1", lastPlayed: earlier), Watched("u2", lastPlayed: later)],
            UserScope.Default);

        Assert.Equal(later.UtcDateTime, result.LatestLastWatched);
    }

    [Fact]
    public void Movie_MaxPlayCount_PicksHighest()
    {
        var result = _svc.AggregateMovie(
            [Watched("u1", playCount: 3), Watched("u2", playCount: 7)],
            UserScope.Default);

        Assert.Equal(7, result.MaxPlayCount);
    }

    [Fact]
    public void Movie_NoLastPlayedDates_LatestIsNull()
    {
        var result = _svc.AggregateMovie(
            [new UserWatchState("u1", "item1", true, null, 1, 0)],
            UserScope.Default);

        Assert.Null(result.LatestLastWatched);
    }

    [Fact]
    public void Movie_EmptyUserList_ReturnsNoQualifyingDefaults()
    {
        var result = _svc.AggregateMovie([], UserScope.Default);

        Assert.False(result.WatchedByAllUsers);
        Assert.False(result.WatchedByAnyUser);
        Assert.Equal(0, result.SeenByUserCount);
        Assert.Null(result.LatestLastWatched);
        Assert.False(result.IsProtected);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  USER SCOPE FILTERING
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Movie_WhitelistScope_OnlyCountsWhitelistedUsers()
    {
        var scope = new UserScope(UserScopeMode.Whitelist, ["u1"]);

        var result = _svc.AggregateMovie(
            [Watched("u1"), NotWatched("u2")],
            scope);

        Assert.True(result.WatchedByAllUsers);
        Assert.True(result.WatchedByAnyUser);
        Assert.Equal(1, result.SeenByUserCount);
    }

    [Fact]
    public void Movie_ExcludeScope_IgnoresExcludedUsers()
    {
        var scope = new UserScope(UserScopeMode.Exclude, ["guest"]);

        var result = _svc.AggregateMovie(
            [Watched("u1"), Watched("u2"), InProgress("guest")],
            scope);

        Assert.True(result.WatchedByAllUsers);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Movie_ExcludeScope_GuestPartialDoesNotProtect()
    {
        var scope = new UserScope(UserScopeMode.Exclude, ["guest"]);

        var result = _svc.AggregateMovie(
            [Watched("u1"), InProgress("guest")],
            scope);

        Assert.True(result.WatchedByAllUsers);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Movie_WhitelistScope_EmptyWhitelist_NoQualifyingUsers()
    {
        var scope = new UserScope(UserScopeMode.Whitelist, []);

        var result = _svc.AggregateMovie(
            [Watched("u1"), Watched("u2")],
            scope);

        Assert.False(result.WatchedByAllUsers);
        Assert.False(result.WatchedByAnyUser);
        Assert.Equal(0, result.SeenByUserCount);
    }

    [Fact]
    public void Movie_AllScope_CountsEveryone()
    {
        var result = _svc.AggregateMovie(
            [Watched("u1"), Watched("u2"), Watched("u3")],
            UserScope.Default);

        Assert.Equal(3, result.SeenByUserCount);
    }

    [Fact]
    public void UserScope_CaseInsensitive()
    {
        var scope = new UserScope(UserScopeMode.Whitelist, ["User1"]);

        Assert.True(scope.IsUserQualifying("user1"));
        Assert.True(scope.IsUserQualifying("USER1"));
        Assert.False(scope.IsUserQualifying("user2"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SEASON (EPISODE-GRANULAR) AGGREGATION
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Season_AllUsersFinishedAllEpisodes_WatchedByAllTrue()
    {
        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1"), Watched("u2", "e1")),
            Ep("e2", Watched("u1", "e2"), Watched("u2", "e2")),
            Ep("e3", Watched("u1", "e3"), Watched("u2", "e3"))
        };

        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.True(result.WatchedByAllUsers);
        Assert.True(result.WatchedByAnyUser);
        Assert.Equal(2, result.SeenByUserCount);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Season_TheCanonicalCase_UserBMidSeason_Protected()
    {
        var episodes = new[]
        {
            Ep("e1", Watched("userA", "e1"), Watched("userB", "e1")),
            Ep("e2", Watched("userA", "e2"), Watched("userB", "e2")),
            Ep("e3", Watched("userA", "e3"), Watched("userB", "e3")),
            Ep("e4", Watched("userA", "e4"), Watched("userB", "e4")),
            Ep("e5", Watched("userA", "e5"), NotWatched("userB", "e5")),
            Ep("e6", Watched("userA", "e6"), NotWatched("userB", "e6")),
            Ep("e7", Watched("userA", "e7"), NotWatched("userB", "e7")),
            Ep("e8", Watched("userA", "e8"), NotWatched("userB", "e8"))
        };

        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.False(result.WatchedByAllUsers);
        Assert.True(result.WatchedByAnyUser);
        Assert.Equal(1, result.SeenByUserCount);
        Assert.True(result.IsProtected);
        Assert.Contains("userB", result.ProtectionReason!);
        Assert.Contains("mid-season", result.ProtectionReason!);
    }

    [Fact]
    public void Season_OneUserNotStarted_NotProtected()
    {
        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1"), NotWatched("u2", "e1")),
            Ep("e2", Watched("u1", "e2"), NotWatched("u2", "e2"))
        };

        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.False(result.WatchedByAllUsers);
        Assert.True(result.WatchedByAnyUser);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Season_UserPartiallyPlayedOneEpisode_Protected()
    {
        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1"), InProgress("u2", "e1")),
            Ep("e2", Watched("u1", "e2"), NotWatched("u2", "e2"))
        };

        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.True(result.IsProtected);
        Assert.Contains("u2", result.ProtectionReason!);
    }

    [Fact]
    public void Season_EmptyEpisodes_SafeDefaults()
    {
        var result = _svc.AggregateSeason([], UserScope.Default);

        Assert.False(result.WatchedByAllUsers);
        Assert.False(result.WatchedByAnyUser);
        Assert.Equal(0, result.SeenByUserCount);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Season_WhitelistScope_IgnoresNonWhitelistedUser()
    {
        var scope = new UserScope(UserScopeMode.Whitelist, ["u1"]);

        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1"), NotWatched("u2", "e1")),
            Ep("e2", Watched("u1", "e2"), NotWatched("u2", "e2"))
        };

        var result = _svc.AggregateSeason(episodes, scope);

        Assert.True(result.WatchedByAllUsers);
        Assert.Equal(1, result.SeenByUserCount);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Season_ExcludeScope_GuestMidSeasonDoesNotProtect()
    {
        var scope = new UserScope(UserScopeMode.Exclude, ["guest"]);

        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1"), Watched("guest", "e1")),
            Ep("e2", Watched("u1", "e2"), NotWatched("guest", "e2"))
        };

        var result = _svc.AggregateSeason(episodes, scope);

        Assert.True(result.WatchedByAllUsers);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Season_LatestLastWatched_AcrossUsersAndEpisodes()
    {
        var early = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var mid = new DateTimeOffset(2025, 3, 15, 0, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1", early), Watched("u2", "e1", mid)),
            Ep("e2", Watched("u1", "e2", late), Watched("u2", "e2", mid))
        };

        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.Equal(late.UtcDateTime, result.LatestLastWatched);
    }

    [Fact]
    public void Season_MaxPlayCount_AcrossUsersAndEpisodes()
    {
        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1", playCount: 2), Watched("u2", "e1", playCount: 5)),
            Ep("e2", Watched("u1", "e2", playCount: 1), Watched("u2", "e2", playCount: 3))
        };

        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.Equal(5, result.MaxPlayCount);
    }

    [Fact]
    public void Season_ThreeUsers_TwoFinished_OneMidSeason()
    {
        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1"), Watched("u2", "e1"), Watched("u3", "e1")),
            Ep("e2", Watched("u1", "e2"), Watched("u2", "e2"), NotWatched("u3", "e2")),
            Ep("e3", Watched("u1", "e3"), Watched("u2", "e3"), NotWatched("u3", "e3"))
        };

        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.False(result.WatchedByAllUsers);
        Assert.True(result.WatchedByAnyUser);
        Assert.Equal(2, result.SeenByUserCount);
        Assert.True(result.IsProtected);
        Assert.Contains("u3", result.ProtectionReason!);
    }

    [Fact]
    public void Season_UserMissedMiddleEpisode_NotFullyWatched()
    {
        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1")),
            Ep("e2", NotWatched("u1", "e2")),
            Ep("e3", Watched("u1", "e3"))
        };

        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.False(result.WatchedByAllUsers);
        Assert.True(result.WatchedByAnyUser);
        Assert.Equal(0, result.SeenByUserCount);
        Assert.True(result.IsProtected);
    }

    [Fact]
    public void Season_NoQualifyingUsersInScope_SafeDefaults()
    {
        var scope = new UserScope(UserScopeMode.Whitelist, ["nobody"]);

        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1"), Watched("u2", "e1"))
        };

        var result = _svc.AggregateSeason(episodes, scope);

        Assert.False(result.WatchedByAllUsers);
        Assert.False(result.WatchedByAnyUser);
        Assert.Equal(0, result.SeenByUserCount);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Season_UserMissingFromOneEpisode_TreatedAsNotWatched()
    {
        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1"), Watched("u2", "e1")),
            Ep("e2", Watched("u1", "e2"))
        };

        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.False(result.WatchedByAllUsers);
        Assert.Equal(1, result.SeenByUserCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  EDGE CASES
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Movie_SingleUser_Watched_Eligible()
    {
        var result = _svc.AggregateMovie(
            [Watched("solo")],
            UserScope.Default);

        Assert.True(result.WatchedByAllUsers);
        Assert.True(result.WatchedByAnyUser);
        Assert.Equal(1, result.SeenByUserCount);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Movie_SingleUser_InProgress_Protected()
    {
        var result = _svc.AggregateMovie(
            [InProgress("solo")],
            UserScope.Default);

        Assert.False(result.WatchedByAllUsers);
        Assert.False(result.WatchedByAnyUser);
        Assert.True(result.IsProtected);
    }

    [Fact]
    public void Movie_NotWatchedNotInProgress_NotProtected()
    {
        var result = _svc.AggregateMovie(
            [NotWatched("u1")],
            UserScope.Default);

        Assert.False(result.IsProtected);
        Assert.Null(result.ProtectionReason);
    }

    [Fact]
    public void Season_SingleEpisodeSeason_AllWatched()
    {
        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1"), Watched("u2", "e1"))
        };

        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.True(result.WatchedByAllUsers);
        Assert.False(result.IsProtected);
    }

    [Fact]
    public void Season_MultipleInProgressUsers_AllListed()
    {
        var episodes = new[]
        {
            Ep("e1", Watched("u1", "e1"), Watched("u2", "e1"), Watched("u3", "e1")),
            Ep("e2", Watched("u1", "e2"), NotWatched("u2", "e2"), NotWatched("u3", "e3"))
        };

        // u2 and u3 are mid-season (watched ep1 but not ep2)
        var result = _svc.AggregateSeason(episodes, UserScope.Default);

        Assert.True(result.IsProtected);
        Assert.Contains("u2", result.ProtectionReason!);
        Assert.Contains("u3", result.ProtectionReason!);
    }
}
