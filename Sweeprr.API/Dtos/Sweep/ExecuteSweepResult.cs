namespace Sweeprr.API.Dtos.Sweep;

public sealed record ExecuteSweepResult(
    int ItemsSwept,
    int ItemsFailed,
    int ItemsSkippedByFailsafe,
    long BytesRecovered,
    bool WasDryRun);
