namespace Sweeprr.API.Integrations.Radarr.Dto;

internal sealed class RadarrHistoryRecordDto
{
    public int                   Id        { get; set; }
    public int                   MovieId   { get; set; }
    public string                EventType { get; set; } = string.Empty;
    public string                Date      { get; set; } = string.Empty;
    public RadarrHistoryDataDto  Data      { get; set; } = new();
}
