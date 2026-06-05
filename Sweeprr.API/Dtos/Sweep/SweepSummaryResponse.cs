namespace Sweeprr.API.Dtos.Sweep;

public sealed record SweepSummaryResponse(
    int PendingCount,
    int ApprovedCount,
    long PendingBytes,
    long ApprovedBytes);
