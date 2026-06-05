namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrHistoryDataDto
{
    public string? ImportedPath   { get; set; }
    public string? DroppedPath    { get; set; }
    public string? DownloadClient { get; set; }
    public string? PublishedDate  { get; set; }
}
