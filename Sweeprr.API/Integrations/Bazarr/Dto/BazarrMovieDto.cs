namespace Sweeprr.API.Integrations.Bazarr.Dto;

public sealed record BazarrMovieDto(
    int RadarrId,
    string? Title,
    BazarrSubtitleDto[]? Subtitles);

public sealed record BazarrMovieResponseDto(
    BazarrMovieDto[]? Data,
    int Total);
