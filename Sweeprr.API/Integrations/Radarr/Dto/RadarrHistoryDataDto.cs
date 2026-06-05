namespace Sweeprr.API.Integrations.Radarr.Dto;

internal sealed class RadarrHistoryDataDto
{
    public string? ImportedPath    { get; set; }
    public string? DroppedPath     { get; set; }
    public string? DownloadClient  { get; set; }
    public string? PublishedDate   { get; set; }
}
