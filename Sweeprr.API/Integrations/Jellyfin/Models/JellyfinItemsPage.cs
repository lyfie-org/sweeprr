namespace Sweeprr.API.Integrations.Jellyfin.Models;

public sealed record JellyfinItemsPage(
    IReadOnlyList<JellyfinItem> Items,
    int TotalRecordCount,
    int StartIndex);
