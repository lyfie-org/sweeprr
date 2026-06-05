namespace Sweeprr.API.Models;

public sealed record UserWatchState(
    string UserId,
    string ItemId,
    bool Played,
    DateTimeOffset? LastPlayedDate,
    int PlayCount,
    long PlaybackPositionTicks);
