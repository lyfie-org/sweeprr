namespace Sweeprr.API.Services;

/// <inheritdoc cref="IFailsafeService"/>
public sealed class FailsafeService : IFailsafeService
{
    private int _maxItems;
    private long _maxBytes;
    private long _pessimisticBytes;
    private int _totalQueueItems;
    private double? _libraryPercentCap;
    private double? _overBroadMatchPct;

    private int _itemCount;
    private long _byteCount;

    public void Initialize(
        int maxItems,
        double maxGb,
        double pessimisticSizeGb,
        int totalQueueItems,
        double? libraryPercentCap,
        double? overBroadMatchPct)
    {
        // 0 or negative = block all (conservative safe default documented in PRD 4.4)
        _maxItems = maxItems > 0 ? maxItems : 0;
        _maxBytes = maxGb > 0 ? (long)(maxGb * 1_073_741_824.0) : 0;
        _pessimisticBytes = pessimisticSizeGb > 0 ? (long)(pessimisticSizeGb * 1_073_741_824.0) : 0;
        _totalQueueItems = totalQueueItems;
        _libraryPercentCap = libraryPercentCap is > 0 ? libraryPercentCap : null;
        _overBroadMatchPct = overBroadMatchPct is > 0 ? overBroadMatchPct : null;
        _itemCount = 0;
        _byteCount = 0;
    }

    public FailsafeGateResult CheckPreRunBreadth(int approvedCount)
    {
        if (_libraryPercentCap is not { } cap || _totalQueueItems <= 0)
            return FailsafeGateResult.Ok();

        var pct = (double)approvedCount / _totalQueueItems;
        if (pct > cap)
            return FailsafeGateResult.PercentLimit(
                cap * 100,
                $"Failsafe: run would delete {pct * 100:F1}% of sweep queue ({approvedCount}/{_totalQueueItems} items) — limit is {cap * 100:F0}%");

        return FailsafeGateResult.Ok();
    }

    public FailsafeGateResult CheckGroupBreadth(int groupCount, int totalApproved)
    {
        if (_overBroadMatchPct is not { } cap || totalApproved <= 0)
            return FailsafeGateResult.Ok();

        var pct = (double)groupCount / totalApproved;
        if (pct > cap)
            return FailsafeGateResult.PercentLimit(
                cap * 100,
                $"Failsafe: rule group accounts for {pct * 100:F1}% of the approved batch ({groupCount}/{totalApproved} items) — limit is {cap * 100:F0}%");

        return FailsafeGateResult.Ok();
    }

    public FailsafeGateResult CheckAndRecord(long? sizeBytes)
    {
        if (_maxItems == 0)
            return FailsafeGateResult.ItemLimit(0);

        if (_itemCount >= _maxItems)
            return FailsafeGateResult.ItemLimit(_maxItems);

        // Unknown-size items count pessimistically toward the GB cap — prevents
        // a batch of size-unknown items from circumventing the limit entirely.
        var effectiveSize = sizeBytes ?? _pessimisticBytes;

        if (_maxBytes > 0)
        {
            var projected = _byteCount + effectiveSize;
            if (projected > _maxBytes)
                return FailsafeGateResult.GbLimit(_maxBytes / 1_073_741_824.0);
        }

        _itemCount++;
        _byteCount += effectiveSize;
        return FailsafeGateResult.Ok();
    }
}
