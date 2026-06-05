using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Sweep;

public sealed class SweepQueryParams
{
    public SweepItemStatus? Status { get; init; }
    public int? RuleGroupId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
