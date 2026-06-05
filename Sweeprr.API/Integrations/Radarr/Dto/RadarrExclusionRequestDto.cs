namespace Sweeprr.API.Integrations.Radarr.Dto;

internal sealed class RadarrExclusionRequestDto
{
    public int    TmdbId { get; init; }
    public string Name   { get; init; } = string.Empty;
    public int    Year   { get; init; }
}
