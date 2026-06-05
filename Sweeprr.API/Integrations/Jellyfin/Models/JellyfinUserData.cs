using Sweeprr.API.Integrations.Jellyfin.Dto;

namespace Sweeprr.API.Integrations.Jellyfin.Models;

public sealed record JellyfinUserData(
    bool Played,
    DateTimeOffset? LastPlayedDate,
    int PlayCount,
    long PlaybackPositionTicks)
{
    public static readonly JellyfinUserData Empty = new(false, null, 0, 0);

    public static JellyfinUserData From(JellyfinUserDataDto dto) => new(
        dto.Played,
        dto.LastPlayedDate,
        dto.PlayCount,
        dto.PlaybackPositionTicks);
}
