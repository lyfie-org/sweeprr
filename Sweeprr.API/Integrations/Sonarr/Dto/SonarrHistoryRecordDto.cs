namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrHistoryRecordDto
{
    public int                   Id        { get; set; }
    public int                   SeriesId  { get; set; }
    public int                   EpisodeId { get; set; }
    public string                EventType { get; set; } = string.Empty;
    public string                Date      { get; set; } = string.Empty;
    public SonarrHistoryDataDto  Data      { get; set; } = new();
}
