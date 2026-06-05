namespace Sweeprr.API.Integrations.Sonarr.Models;

public sealed record SonarrSeason(
    int  SeasonNumber,
    bool Monitored,
    int  EpisodeFileCount,
    int  EpisodeCount,
    int  TotalEpisodeCount,
    long SizeOnDisk);
