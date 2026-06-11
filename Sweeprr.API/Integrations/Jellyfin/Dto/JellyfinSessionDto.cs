namespace Sweeprr.API.Integrations.Jellyfin.Dto;

public sealed record JellyfinSessionDto
{
    public string Id { get; init; } = string.Empty;
    public string? UserId { get; init; }
    public JellyfinSessionNowPlayingItemDto? NowPlayingItem { get; init; }
}

public sealed record JellyfinSessionNowPlayingItemDto
{
    public string Id { get; init; } = string.Empty;
}
