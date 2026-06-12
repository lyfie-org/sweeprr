namespace Sweeprr.API.Integrations.Bazarr.Dto;

public sealed record BazarrLanguageDto(string? Code2, string? Name);

public sealed record BazarrSubtitleDto(string? Path, BazarrLanguageDto? Language);
