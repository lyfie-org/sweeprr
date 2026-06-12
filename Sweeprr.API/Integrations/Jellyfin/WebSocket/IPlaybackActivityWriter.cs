using System.Threading;
using System.Threading.Tasks;
using Sweeprr.API.Integrations.Jellyfin.Models;

namespace Sweeprr.API.Integrations.Jellyfin.WebSocket;

public interface IPlaybackActivityWriter
{
    void Enqueue(string itemId, string userId, JellyfinUserData data, string username);
    Task ForceFlushAsync(CancellationToken ct = default);
    Task PruneOldActivitiesAsync(int ageLimitDays, CancellationToken ct = default);
}
