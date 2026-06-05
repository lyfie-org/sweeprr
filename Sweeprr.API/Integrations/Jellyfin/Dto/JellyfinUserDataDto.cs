namespace Sweeprr.API.Integrations.Jellyfin.Dto;

public sealed record JellyfinUserDataDto
{
    public bool Played { get; init; }
    public DateTimeOffset? LastPlayedDate { get; init; }
    public int PlayCount { get; init; }
    public long PlaybackPositionTicks { get; init; }
    public double? PlayedPercentage { get; init; }
}
