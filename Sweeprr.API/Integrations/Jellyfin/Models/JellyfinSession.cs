using Sweeprr.API.Integrations.Jellyfin.Dto;

namespace Sweeprr.API.Integrations.Jellyfin.Models;

public sealed record JellyfinSession(string Id, string? UserId, string? NowPlayingItemId)
{
    public static JellyfinSession From(JellyfinSessionDto dto) =>
        new(dto.Id, dto.UserId, dto.NowPlayingItem?.Id);
}
