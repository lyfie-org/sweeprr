using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

public interface IMediaPopulationService
{
    Task<IReadOnlyList<MediaContext>> PopulateAsync(
        RuleGroup group,
        CancellationToken ct = default);
}
