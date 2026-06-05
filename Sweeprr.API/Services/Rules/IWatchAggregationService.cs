using Sweeprr.API.Models;

namespace Sweeprr.API.Services.Rules;

public interface IWatchAggregationService
{
    AggregatedWatchState AggregateMovie(
        IReadOnlyList<UserWatchState> perUserStates,
        UserScope scope);

    AggregatedWatchState AggregateSeason(
        IReadOnlyList<EpisodeWatchData> episodes,
        UserScope scope);
}

public sealed record EpisodeWatchData(
    string EpisodeId,
    IReadOnlyList<UserWatchState> PerUserStates);
