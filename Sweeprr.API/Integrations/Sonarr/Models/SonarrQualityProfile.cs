namespace Sweeprr.API.Integrations.Sonarr.Models;

public sealed record SonarrQualityProfile(
    int    Id,
    string Name,
    bool   UpgradeAllowed);
