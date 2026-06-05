namespace Sweeprr.API.Dtos.Settings;

public sealed record SettingsDto(
    string InstanceName,
    bool GlobalDryRun,
    string DefaultCron,
    int MaxItemsPerRun,
    double MaxGbPerRun,
    double PessimisticSizeGb,
    double? LibraryPercentCap,
    double? OverBroadMatchPct);
