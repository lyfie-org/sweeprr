namespace Sweeprr.API.Integrations.Radarr.Models;

public sealed record RadarrQualityProfile(
    int    Id,
    string Name,
    bool   UpgradeAllowed);
