namespace Sweeprr.API.Integrations.Jellyfin.Models;

/// <summary>
/// Parameters for a Jellyfin items query.
/// When UserId is set the request targets /Users/{userId}/Items, which embeds
/// per-user UserData inline — required for multi-user watch aggregation (Story 3.3).
/// Without UserId the request targets /Items (no UserData).
/// </summary>
public sealed record GetItemsRequest
{
    public string? UserId { get; init; }
    public string? ParentId { get; init; }

    public IReadOnlyList<string> IncludeItemTypes { get; init; } =
        ["Movie", "Series", "Season", "Episode"];

    public IReadOnlyList<string> Fields { get; init; } =
        ["ProviderIds", "DateCreated", "UserData", "Path", "MediaStreams"];

    public bool Recursive { get; init; } = true;
    public int StartIndex { get; init; } = 0;
    public int Limit { get; init; } = 100;
}
