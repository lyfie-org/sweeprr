namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrSeasonStatisticsDto
{
    public int    EpisodeFileCount    { get; set; }
    public int    EpisodeCount        { get; set; }
    public int    TotalEpisodeCount   { get; set; }
    public long   SizeOnDisk          { get; set; }
    public double PercentOfEpisodes   { get; set; }
}
