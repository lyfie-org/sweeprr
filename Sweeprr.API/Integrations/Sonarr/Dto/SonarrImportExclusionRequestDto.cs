namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrImportExclusionRequestDto
{
    public int    TvdbId { get; init; }
    public string Title  { get; init; } = string.Empty;
}
