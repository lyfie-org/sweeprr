namespace Sweeprr.API.Integrations.Bazarr.Dto;

public sealed record BazarrEpisodeDto(
    int SonarrEpisodeId,
    string? Title,
    BazarrSubtitleDto[]? Subtitles);

public sealed record BazarrEpisodeResponseDto(
    BazarrEpisodeDto[]? Data,
    int Total);
