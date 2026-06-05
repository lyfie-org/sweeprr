namespace Sweeprr.API.Integrations.Jellyfin.Dto;

public sealed record JellyfinItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? MediaType { get; init; }
    public JellyfinProviderIdsDto? ProviderIds { get; init; }
    public JellyfinUserDataDto? UserData { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public int? ProductionYear { get; init; }
    public double? CommunityRating { get; init; }
    public string? OfficialRating { get; init; }
    public long? RunTimeTicks { get; init; }
    public string? SeriesId { get; init; }
    public string? SeriesName { get; init; }
    public string? SeasonId { get; init; }
    public int? IndexNumber { get; init; }
    public int? ParentIndexNumber { get; init; }
    public string? Path { get; init; }
}
