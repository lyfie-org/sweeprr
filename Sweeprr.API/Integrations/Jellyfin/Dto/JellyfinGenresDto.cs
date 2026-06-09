namespace Sweeprr.API.Integrations.Jellyfin.Dto;

public sealed record JellyfinGenreDto(
    string Name,
    string Id
);

public sealed record JellyfinGenresResponseDto(
    JellyfinGenreDto[] Items
);
