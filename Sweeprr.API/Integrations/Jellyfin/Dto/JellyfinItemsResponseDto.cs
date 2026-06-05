namespace Sweeprr.API.Integrations.Jellyfin.Dto;

public sealed record JellyfinItemsResponseDto
{
    public JellyfinItemDto[] Items { get; init; } = [];
    public int TotalRecordCount { get; init; }
    public int StartIndex { get; init; }
}
