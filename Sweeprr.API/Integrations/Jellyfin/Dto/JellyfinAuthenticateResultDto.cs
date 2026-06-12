namespace Sweeprr.API.Integrations.Jellyfin.Dto;

public sealed record JellyfinAuthenticateResultDto
{
    public JellyfinUserDto User { get; init; } = new();
    public string AccessToken { get; init; } = string.Empty;
}
