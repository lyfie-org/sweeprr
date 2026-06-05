namespace Sweeprr.API.Dtos.Settings;

/// <summary>
/// PATCH semantics: null fields are ignored (keep existing value).
/// To explicitly DISABLE a nullable cap, set the corresponding Clear* flag to true.
/// </summary>
public sealed class UpdateSettingsRequest
{
    public string? InstanceName { get; init; }
    public bool? GlobalDryRun { get; init; }
    public string? DefaultCron { get; init; }
    public int? MaxItemsPerRun { get; init; }
    public double? MaxGbPerRun { get; init; }
    public double? PessimisticSizeGb { get; init; }
    public double? LibraryPercentCap { get; init; }
    public bool ClearLibraryPercentCap { get; init; }
    public double? OverBroadMatchPct { get; init; }
    public bool ClearOverBroadMatchPct { get; init; }
}
