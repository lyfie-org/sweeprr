namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrQualityProfileDto
{
    public int    Id             { get; set; }
    public string Name           { get; set; } = string.Empty;
    public bool   UpgradeAllowed { get; set; }
}
