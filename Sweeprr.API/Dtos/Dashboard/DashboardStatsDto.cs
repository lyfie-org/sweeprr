namespace Sweeprr.API.Dtos.Dashboard;

public sealed record DashboardStatsDto(
    double TotalGbRecovered,
    int TotalItemsSwept,
    int ItemsSweptLast30d,
    double GbRecoveredLast30d,
    int PendingQueueCount,
    DateTimeOffset? NextScheduledRun,
    string WsState,
    bool GlobalDryRun
);
