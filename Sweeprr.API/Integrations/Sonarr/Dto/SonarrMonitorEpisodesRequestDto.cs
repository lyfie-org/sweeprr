namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrMonitorEpisodesRequestDto
{
    public IReadOnlyList<int> EpisodeIds { get; init; } = [];
    public bool               Monitored  { get; init; }
}
