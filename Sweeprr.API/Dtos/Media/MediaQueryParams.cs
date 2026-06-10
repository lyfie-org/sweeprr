using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Media;

public sealed class MediaQueryParams
{
    /// <summary>Case-insensitive substring match against the item title.</summary>
    public string? Search { get; init; }

    /// <summary>Filter by media type (Movie, Series, Season, Episode).</summary>
    public MediaType? Type { get; init; }

    /// <summary>Filter by curation status (Pending, Approved, Ignored, Swept, Failed).</summary>
    public SweepItemStatus? Status { get; init; }

    /// <summary>One of: title, year, sizegb, lastwatched, status. Defaults to title.</summary>
    public string? SortBy { get; init; }

    /// <summary>"asc" or "desc". Defaults to asc.</summary>
    public string? SortDir { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
