using Sweeprr.API.Integrations.Jellyfin.Dto;

namespace Sweeprr.API.Integrations.Jellyfin.Models;

public enum JellyfinMediaType { Movie, Series, Season, Episode, Unknown }

public sealed record JellyfinItem(
    string Id,
    string Name,
    JellyfinMediaType Type,
    ProviderIds ProviderIds,
    JellyfinUserData? UserData,
    DateTimeOffset? DateCreated,
    int? ProductionYear,
    double? CommunityRating,
    long? RunTimeTicks,
    string? SeriesId,
    string? SeasonId,
    int? IndexNumber,
    int? ParentIndexNumber,
    string? Path)
{
    public static JellyfinItem From(JellyfinItemDto dto) => new(
        Id:                dto.Id,
        Name:              dto.Name,
        Type:              ParseType(dto.Type),
        ProviderIds:       ProviderIds.From(dto.ProviderIds),
        UserData:          dto.UserData is not null ? JellyfinUserData.From(dto.UserData) : null,
        DateCreated:       dto.DateCreated,
        ProductionYear:    dto.ProductionYear,
        CommunityRating:   dto.CommunityRating,
        RunTimeTicks:      dto.RunTimeTicks,
        SeriesId:          dto.SeriesId,
        SeasonId:          dto.SeasonId,
        IndexNumber:       dto.IndexNumber,
        ParentIndexNumber: dto.ParentIndexNumber,
        Path:              dto.Path);

    private static JellyfinMediaType ParseType(string type) => type switch
    {
        "Movie"   => JellyfinMediaType.Movie,
        "Series"  => JellyfinMediaType.Series,
        "Season"  => JellyfinMediaType.Season,
        "Episode" => JellyfinMediaType.Episode,
        _         => JellyfinMediaType.Unknown
    };
}
