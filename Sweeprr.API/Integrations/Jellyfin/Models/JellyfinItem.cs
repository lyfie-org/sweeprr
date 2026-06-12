using System;
using System.Collections.Generic;
using System.Linq;
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
    string? Path,
    IReadOnlyList<string>? Genres,
    int? ResolutionHeight,
    string? VideoCodec,
    int? AudioChannels)
{
    public static JellyfinItem From(JellyfinItemDto dto)
    {
        var videoStream = dto.MediaStreams?.FirstOrDefault(
            s => string.Equals(s.Type, "Video", StringComparison.OrdinalIgnoreCase));
        var audioStream = dto.MediaStreams?.FirstOrDefault(
            s => string.Equals(s.Type, "Audio", StringComparison.OrdinalIgnoreCase));

        return new(
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
            Path:              dto.Path,
            Genres:            dto.Genres,
            ResolutionHeight:  videoStream?.Height,
            VideoCodec:        videoStream?.Codec?.ToLowerInvariant(),
            AudioChannels:     audioStream?.Channels);
    }

    private static JellyfinMediaType ParseType(string type) => type switch
    {
        "Movie"   => JellyfinMediaType.Movie,
        "Series"  => JellyfinMediaType.Series,
        "Season"  => JellyfinMediaType.Season,
        "Episode" => JellyfinMediaType.Episode,
        _         => JellyfinMediaType.Unknown
    };
}
