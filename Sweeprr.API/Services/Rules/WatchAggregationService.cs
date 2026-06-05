using Sweeprr.API.Models;

namespace Sweeprr.API.Services.Rules;

public sealed class WatchAggregationService : IWatchAggregationService
{
    public AggregatedWatchState AggregateMovie(
        IReadOnlyList<UserWatchState> perUserStates,
        UserScope scope)
    {
        var qualifying = FilterQualifying(perUserStates, scope);

        if (qualifying.Count == 0)
            return NoQualifyingUsers();

        var watchedUsers = qualifying.Where(u => u.Played).ToList();
        var watchedByAll = watchedUsers.Count == qualifying.Count;
        var watchedByAny = watchedUsers.Count > 0;
        var seenByUserCount = watchedUsers.Count;

        var latestLastWatched = qualifying
            .Where(u => u.LastPlayedDate.HasValue)
            .Select(u => u.LastPlayedDate!.Value.UtcDateTime)
            .DefaultIfEmpty()
            .Max();
        var latestOrNull = latestLastWatched == default ? (DateTime?)null : latestLastWatched;

        var maxPlayCount = qualifying.Max(u => u.PlayCount);

        var inProgress = qualifying.Any(u => !u.Played && u.PlaybackPositionTicks > 0);
        string? protectionReason = inProgress
            ? BuildMovieProtectionReason(qualifying.Where(u => !u.Played && u.PlaybackPositionTicks > 0))
            : null;

        return new AggregatedWatchState(
            WatchedByAllUsers: watchedByAll,
            WatchedByAnyUser: watchedByAny,
            SeenByUserCount: seenByUserCount,
            LatestLastWatched: latestOrNull,
            MaxPlayCount: maxPlayCount,
            IsProtected: inProgress,
            ProtectionReason: protectionReason);
    }

    public AggregatedWatchState AggregateSeason(
        IReadOnlyList<EpisodeWatchData> episodes,
        UserScope scope)
    {
        if (episodes.Count == 0)
            return NoQualifyingUsers();

        var allQualifyingUserIds = episodes
            .SelectMany(e => e.PerUserStates)
            .Where(u => scope.IsUserQualifying(u.UserId))
            .Select(u => u.UserId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allQualifyingUserIds.Count == 0)
            return NoQualifyingUsers();

        var usersWhoFinishedAll = new List<string>();
        var usersWhoWatchedAny = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usersInProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTime latestLastWatched = default;
        int maxPlayCount = 0;

        foreach (var userId in allQualifyingUserIds)
        {
            bool finishedAll = true;
            bool watchedAny = false;
            bool hasPartial = false;

            foreach (var ep in episodes)
            {
                var state = ep.PerUserStates
                    .FirstOrDefault(u => string.Equals(u.UserId, userId, StringComparison.OrdinalIgnoreCase));

                if (state is null || !state.Played)
                {
                    finishedAll = false;
                    if (state is not null && state.PlaybackPositionTicks > 0)
                        hasPartial = true;
                }
                else
                {
                    watchedAny = true;
                }

                if (state is not null)
                {
                    if (state.LastPlayedDate.HasValue && state.LastPlayedDate.Value.UtcDateTime > latestLastWatched)
                        latestLastWatched = state.LastPlayedDate.Value.UtcDateTime;

                    if (state.PlayCount > maxPlayCount)
                        maxPlayCount = state.PlayCount;
                }
            }

            if (finishedAll)
                usersWhoFinishedAll.Add(userId);
            if (watchedAny)
                usersWhoWatchedAny.Add(userId);
            if (!finishedAll && (watchedAny || hasPartial))
                usersInProgress.Add(userId);
        }

        var watchedByAll = usersWhoFinishedAll.Count == allQualifyingUserIds.Count;
        var watchedByAny = usersWhoWatchedAny.Count > 0;
        var isProtected = usersInProgress.Count > 0;

        string? protectionReason = isProtected
            ? $"Protected: {string.Join(", ", usersInProgress.Select(id => $"user {id}"))} mid-season"
            : null;

        return new AggregatedWatchState(
            WatchedByAllUsers: watchedByAll,
            WatchedByAnyUser: watchedByAny,
            SeenByUserCount: usersWhoFinishedAll.Count,
            LatestLastWatched: latestLastWatched == default ? null : latestLastWatched,
            MaxPlayCount: maxPlayCount,
            IsProtected: isProtected,
            ProtectionReason: protectionReason);
    }

    private static List<UserWatchState> FilterQualifying(
        IReadOnlyList<UserWatchState> states,
        UserScope scope)
    {
        return states.Where(u => scope.IsUserQualifying(u.UserId)).ToList();
    }

    private static AggregatedWatchState NoQualifyingUsers() => new(
        WatchedByAllUsers: false,
        WatchedByAnyUser: false,
        SeenByUserCount: 0,
        LatestLastWatched: null,
        MaxPlayCount: 0,
        IsProtected: false,
        ProtectionReason: null);

    private static string BuildMovieProtectionReason(IEnumerable<UserWatchState> inProgressUsers)
    {
        var ids = string.Join(", ", inProgressUsers.Select(u => $"user {u.UserId}"));
        return $"Protected: {ids} currently watching";
    }
}
