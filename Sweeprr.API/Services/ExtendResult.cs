using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <summary>
/// Outcome of <see cref="ISweepQueueService.ExtendAsync"/> (Story 10.4 — public extension portal).
/// </summary>
public abstract record ExtendResult
{
    private protected ExtendResult() { }

    /// <summary>The item was removed from the sweep queue and a temporary <see cref="Exclusion"/> was created.</summary>
    public sealed record Success(Exclusion Exclusion) : ExtendResult;

    /// <summary>The item is not currently Pending or Approved — there is nothing to extend.</summary>
    public sealed record NotQueued : ExtendResult;

    /// <summary>This Jellyfin user already extended this item within the last 7 days.</summary>
    public sealed record AbuseLimited : ExtendResult;
}
