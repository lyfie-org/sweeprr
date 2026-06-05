namespace Sweeprr.API.Dtos.Sweep;

public sealed record ExecuteSweepRequest(IReadOnlyList<int>? ItemIds = null);
