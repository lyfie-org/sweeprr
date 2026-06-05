using Sweeprr.API.Services;

namespace Sweeprr.Tests.Sweep;

public class FailsafeServiceTests
{
    // Helper: creates and initializes a FailsafeService with the given limits.
    // totalQueueItems defaults to 100 so percent-based tests can use clean percentages.
    private static FailsafeService Build(
        int maxItems = 20,
        double maxGb = 50.0,
        double pessimisticSizeGb = 5.0,
        int totalQueueItems = 100,
        double? libraryPercentCap = null,
        double? overBroadMatchPct = null)
    {
        var svc = new FailsafeService();
        svc.Initialize(maxItems, maxGb, pessimisticSizeGb,
            totalQueueItems, libraryPercentCap, overBroadMatchPct);
        return svc;
    }

    // ── Item cap ──────────────────────────────────────────────────────────────

    [Fact]
    public void ItemCap_AllowsUpToLimit()
    {
        var svc = Build(maxItems: 3);
        Assert.True(svc.CheckAndRecord(null).IsOk);
        Assert.True(svc.CheckAndRecord(null).IsOk);
        Assert.True(svc.CheckAndRecord(null).IsOk);
    }

    [Fact]
    public void ItemCap_BlocksItemBeyondLimit()
    {
        var svc = Build(maxItems: 2);
        svc.CheckAndRecord(null);
        svc.CheckAndRecord(null);
        var result = svc.CheckAndRecord(null);
        Assert.False(result.IsOk);
        Assert.Equal(FailsafeGateStatus.ItemLimitReached, result.Status);
    }

    [Fact]
    public void ItemCap_Zero_BlocksAll()
    {
        var svc = Build(maxItems: 0);
        var result = svc.CheckAndRecord(1_073_741_824L);
        Assert.False(result.IsOk);
        Assert.Equal(FailsafeGateStatus.ItemLimitReached, result.Status);
    }

    // ── GB cap ────────────────────────────────────────────────────────────────

    [Fact]
    public void GbCap_AllowsWhenBelowLimit()
    {
        var svc = Build(maxGb: 2.0, maxItems: 100);
        // 1 GB item — should pass
        var result = svc.CheckAndRecord(1_073_741_824L);
        Assert.True(result.IsOk);
    }

    [Fact]
    public void GbCap_BlocksWhenProjectedExceedsLimit()
    {
        // Cap = 1.5 GB; two 1 GB items — first passes, second is blocked
        var svc = Build(maxGb: 1.5, maxItems: 100);
        Assert.True(svc.CheckAndRecord(1_073_741_824L).IsOk);
        var result = svc.CheckAndRecord(1_073_741_824L);
        Assert.False(result.IsOk);
        Assert.Equal(FailsafeGateStatus.GbLimitReached, result.Status);
    }

    [Fact]
    public void GbCap_ExactlyAtLimit_Passes()
    {
        // Cap = 1.0 GB; single 1 GB item — exact boundary should pass
        var svc = Build(maxGb: 1.0, maxItems: 100);
        Assert.True(svc.CheckAndRecord(1_073_741_824L).IsOk);
    }

    // ── Unknown-size pessimism ────────────────────────────────────────────────

    [Fact]
    public void UnknownSize_CountsPessimisticallyTowardGbCap()
    {
        // pessimisticSizeGb=5, cap=6 GB. First null item uses 5 GB, second (1 GB) pushes to 6 GB = ok,
        // third null item would add 5 GB = 11 GB projected > 6 GB cap → blocked.
        var svc = Build(maxGb: 6.0, pessimisticSizeGb: 5.0, maxItems: 100);
        Assert.True(svc.CheckAndRecord(null).IsOk);              // 5 GB consumed
        Assert.True(svc.CheckAndRecord(1_073_741_824L).IsOk);   // 6 GB consumed
        var blocked = svc.CheckAndRecord(null);                   // would add 5 GB → 11 GB > 6 GB
        Assert.False(blocked.IsOk);
        Assert.Equal(FailsafeGateStatus.GbLimitReached, blocked.Status);
    }

    [Fact]
    public void UnknownSize_WhenGbCapDisabled_PassesRegardless()
    {
        // maxGb=0 disables the GB cap; unknown-size items should still pass the item cap
        var svc = Build(maxGb: 0, maxItems: 5);
        for (var i = 0; i < 5; i++)
            Assert.True(svc.CheckAndRecord(null).IsOk);
    }

    // ── Library percent cap (CheckPreRunBreadth) ──────────────────────────────

    [Fact]
    public void PreRunBreadth_Disabled_WhenCapIsNull()
    {
        var svc = Build(totalQueueItems: 100, libraryPercentCap: null);
        // Any number of approved items should pass when the cap is disabled
        Assert.True(svc.CheckPreRunBreadth(99).IsOk);
    }

    [Fact]
    public void PreRunBreadth_AllowsWhenBelowCap()
    {
        // Cap 50%: approving 49 of 100 items should pass
        var svc = Build(totalQueueItems: 100, libraryPercentCap: 0.50);
        Assert.True(svc.CheckPreRunBreadth(49).IsOk);
    }

    [Fact]
    public void PreRunBreadth_AllowsAtExactCap()
    {
        // Exactly 50% should pass (not strictly greater than)
        var svc = Build(totalQueueItems: 100, libraryPercentCap: 0.50);
        Assert.True(svc.CheckPreRunBreadth(50).IsOk);
    }

    [Fact]
    public void PreRunBreadth_BlocksWhenAboveCap()
    {
        // Cap 50%: approving 51 of 100 items should be blocked
        var svc = Build(totalQueueItems: 100, libraryPercentCap: 0.50);
        var result = svc.CheckPreRunBreadth(51);
        Assert.False(result.IsOk);
        Assert.Equal(FailsafeGateStatus.PercentLimitReached, result.Status);
        Assert.Contains("51/100", result.Reason);
    }

    [Fact]
    public void PreRunBreadth_ZeroQueueItems_DisablesCheck()
    {
        // totalQueueItems=0 → division by zero risk; guard should disable gracefully
        var svc = Build(totalQueueItems: 0, libraryPercentCap: 0.50);
        Assert.True(svc.CheckPreRunBreadth(5).IsOk);
    }

    // ── Over-broad match guard (CheckGroupBreadth) ────────────────────────────

    [Fact]
    public void GroupBreadth_Disabled_WhenCapIsNull()
    {
        var svc = Build(overBroadMatchPct: null);
        Assert.True(svc.CheckGroupBreadth(100, 100).IsOk);
    }

    [Fact]
    public void GroupBreadth_AllowsWhenBelowCap()
    {
        // Cap 80%: a group with 79 of 100 items should pass
        var svc = Build(overBroadMatchPct: 0.80);
        Assert.True(svc.CheckGroupBreadth(79, 100).IsOk);
    }

    [Fact]
    public void GroupBreadth_AllowsAtExactCap()
    {
        var svc = Build(overBroadMatchPct: 0.80);
        Assert.True(svc.CheckGroupBreadth(80, 100).IsOk);
    }

    [Fact]
    public void GroupBreadth_BlocksWhenAboveCap()
    {
        // Cap 80%: a single group with 81 of 100 items is over-broad
        var svc = Build(overBroadMatchPct: 0.80);
        var result = svc.CheckGroupBreadth(81, 100);
        Assert.False(result.IsOk);
        Assert.Equal(FailsafeGateStatus.PercentLimitReached, result.Status);
        Assert.Contains("81/100", result.Reason);
    }

    [Fact]
    public void GroupBreadth_ZeroTotalApproved_DisablesCheck()
    {
        var svc = Build(overBroadMatchPct: 0.80);
        Assert.True(svc.CheckGroupBreadth(0, 0).IsOk);
    }

    // ── Run-start snapshot isolation ──────────────────────────────────────────

    [Fact]
    public void Initialize_ResetsCounters_BetweenRuns()
    {
        var svc = new FailsafeService();
        svc.Initialize(2, 50.0, 5.0, 100, null, null);
        svc.CheckAndRecord(null);
        svc.CheckAndRecord(null);

        // Re-initialize for a new run — counters should reset
        svc.Initialize(2, 50.0, 5.0, 100, null, null);
        Assert.True(svc.CheckAndRecord(null).IsOk);
        Assert.True(svc.CheckAndRecord(null).IsOk);
        Assert.False(svc.CheckAndRecord(null).IsOk); // item 3 of 2 max
    }
}
