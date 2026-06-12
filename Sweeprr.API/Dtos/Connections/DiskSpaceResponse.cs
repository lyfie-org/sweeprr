namespace Sweeprr.API.Dtos.Connections;

public sealed record DiskSpaceResponse(
    double FreeSpaceGb,
    double TotalSpaceGb,
    double FreePercent);
