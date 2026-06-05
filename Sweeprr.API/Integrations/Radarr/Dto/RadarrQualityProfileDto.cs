namespace Sweeprr.API.Integrations.Radarr.Dto;

internal sealed class RadarrQualityProfileDto
{
    public int    Id             { get; set; }
    public string Name           { get; set; } = string.Empty;
    public bool   UpgradeAllowed { get; set; }
}
