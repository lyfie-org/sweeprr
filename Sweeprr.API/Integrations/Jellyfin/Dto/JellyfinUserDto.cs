namespace Sweeprr.API.Integrations.Jellyfin.Dto;

public sealed record JellyfinUserDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}
