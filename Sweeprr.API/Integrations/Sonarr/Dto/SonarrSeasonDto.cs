namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrSeasonDto
{
    public int                          SeasonNumber { get; set; }
    public bool                         Monitored    { get; set; }
    public SonarrSeasonStatisticsDto?   Statistics   { get; set; }
}
