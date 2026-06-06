namespace Sweeprr.API.Dtos.Dashboard;

public sealed record SparklinePointDto(
    DateOnly Date,
    double GbRecovered,
    int ItemsSwept
);
