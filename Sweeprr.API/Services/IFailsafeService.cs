namespace Sweeprr.API.Services;

public enum FailsafeGateStatus { Ok, ItemLimitReached, GbLimitReached, PercentLimitReached }

public sealed record FailsafeGateResult(FailsafeGateStatus Status, string Reason)
{
    public bool IsOk => Status == FailsafeGateStatus.Ok;

    public static FailsafeGateResult Ok() => new(FailsafeGateStatus.Ok, string.Empty);

    public static FailsafeGateResult ItemLimit(int limit) =>
        new(FailsafeGateStatus.ItemLimitReached,
            $"Failsafe: item limit of {limit} per run reached");

    public static FailsafeGateResult GbLimit(double limitGb) =>
        new(FailsafeGateStatus.GbLimitReached,
            $"Failsafe: size limit of {limitGb:F1} GB per run reached");

    public static FailsafeGateResult PercentLimit(double pct, string detail) =>
        new(FailsafeGateStatus.PercentLimitReached, detail);
}

/// <summary>
/// Tracks per-run counters and enforces all hard deletion caps from
/// <c>GlobalSettings</c>: item count, GB, and percentage-of-library guards.
///
/// Must be initialized once per sweep run via <see cref="Initialize"/> before
/// any gate checks — this snapshots all limits for the duration of the run.
/// </summary>
public interface IFailsafeService
{
    /// <summary>
    /// Snapshots all limits for the current run and resets counters.
    /// Limits of 0 block ALL deletions (conservative safe default).
    /// </summary>
    void Initialize(
        int maxItems,
        double maxGb,
        double pessimisticSizeGb,
        int totalQueueItems,
        double? libraryPercentCap,
        double? overBroadMatchPct);

    /// <summary>
    /// Pre-run check: blocks the entire run if the approved item count exceeds
    /// <paramref name="approvedCount"/> / totalQueueItems > <c>LibraryPercentCap</c>.
    /// Returns <see cref="FailsafeGateResult.Ok"/> when the cap is disabled (<c>null</c>).
    /// </summary>
    FailsafeGateResult CheckPreRunBreadth(int approvedCount);

    /// <summary>
    /// Per-group breadth check: blocks a group whose items represent an implausibly
    /// large fraction (<c>OverBroadMatchPct</c>) of the total approved batch —
    /// a signal that the rule may be misconfigured.
    /// Returns <see cref="FailsafeGateResult.Ok"/> when the cap is disabled (<c>null</c>).
    /// </summary>
    FailsafeGateResult CheckGroupBreadth(int groupCount, int totalApproved);

    /// <summary>
    /// Per-item gate: checks item and GB caps, then records the item if the gate passes.
    /// Items with unknown size (<c>null</c>) count pessimistically toward the GB cap.
    /// Counters are NOT incremented on failure; the item stays Pending.
    /// </summary>
    FailsafeGateResult CheckAndRecord(long? sizeBytes);
}
