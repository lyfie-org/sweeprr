namespace Sweeprr.API.Integrations.Radarr.Dto;

internal sealed class RadarrDiskSpaceDto
{
    public string Path      { get; init; } = string.Empty;
    public long   FreeSpace  { get; init; }
    public long   TotalSpace { get; init; }
}
