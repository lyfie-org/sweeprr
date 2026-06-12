namespace Sweeprr.API.Integrations.Jellyfin.Dto;

/// <summary>Response body from POST /Collections.</summary>
public sealed record JellyfinCollectionResponseDto
{
    public string Id { get; init; } = string.Empty;
}
