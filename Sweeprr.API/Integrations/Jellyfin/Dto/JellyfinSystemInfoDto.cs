namespace Sweeprr.API.Integrations.Jellyfin.Dto;

public sealed record JellyfinSystemInfoDto
{
    // Jellyfin returns "Id" (not "ServerId") for the server's GUID.
    public string Id { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? LocalAddress { get; init; }
}
