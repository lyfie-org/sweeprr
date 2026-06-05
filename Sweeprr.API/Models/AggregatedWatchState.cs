namespace Sweeprr.API.Models;

public sealed record AggregatedWatchState(
    bool WatchedByAllUsers,
    bool WatchedByAnyUser,
    int SeenByUserCount,
    DateTime? LatestLastWatched,
    int MaxPlayCount,
    bool IsProtected,
    string? ProtectionReason);
