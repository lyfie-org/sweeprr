namespace Sweeprr.API.Integrations.Jellyfin.Dto;

public sealed record JellyfinProviderIdsDto
{
    public string? Imdb { get; init; }
    public string? Tmdb { get; init; }
    public string? Tvdb { get; init; }
}
