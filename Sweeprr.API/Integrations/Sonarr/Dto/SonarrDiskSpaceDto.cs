namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrDiskSpaceDto
{
    public string Path      { get; init; } = string.Empty;
    public long   FreeSpace  { get; init; }
    public long   TotalSpace { get; init; }
}
