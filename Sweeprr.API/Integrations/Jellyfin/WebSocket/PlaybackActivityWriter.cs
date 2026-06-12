using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Models;

namespace Sweeprr.API.Integrations.Jellyfin.WebSocket;

public sealed class PlaybackActivityWriter : IPlaybackActivityWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlaybackActivityWriter> _logger;

    private readonly ConcurrentDictionary<(string itemId, string userId), (JellyfinUserData Data, string Username)> _pendingUpdates = new();
    private readonly ConcurrentDictionary<string, long> _itemRuntimeTicks = new();
    private readonly ConcurrentDictionary<(string itemId, string userId), (bool Played, int PlayCount, double ProgressPercent)> _lastWrittenState = new();

    private readonly SemaphoreSlim _dbSemaphore = new(1, 1);

    public PlaybackActivityWriter(
        IServiceScopeFactory scopeFactory,
        ILogger<PlaybackActivityWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Enqueue(string itemId, string userId, JellyfinUserData data, string username)
    {
        _pendingUpdates[(itemId, userId)] = (data, username);

        // Fetch runtime ticks in the background if not cached
        if (!_itemRuntimeTicks.ContainsKey(itemId))
        {
            _ = Task.Run(() => EnsureRuntimeTicksCachedAsync(itemId, userId));
        }

        // Process update asynchronously
        _ = Task.Run(() => ProcessPendingUpdateAsync(itemId, userId));
    }

    private async Task EnsureRuntimeTicksCachedAsync(string itemId, string userId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
            var conn = await db.ServerConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Type == ConnectionType.Jellyfin && c.IsEnabled)
                .ConfigureAwait(false);

            if (conn is null) return;

            var factory = scope.ServiceProvider.GetRequiredService<IIntegrationClientFactory>();
            var client = await factory.CreateJellyfinClientAsync(conn.Id, CancellationToken.None).ConfigureAwait(false);
            if (client is null) return;

            var result = await client.GetItemAsync(itemId, userId, CancellationToken.None).ConfigureAwait(false);
            if (result is HttpResult<JellyfinItem>.Success ok)
            {
                var runtimeTicks = ok.Value.RunTimeTicks ?? 0;
                _itemRuntimeTicks[itemId] = runtimeTicks;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch runtime ticks for item {ItemId}", itemId);
        }
    }

    private async Task ProcessPendingUpdateAsync(string itemId, string userId)
    {
        if (!_pendingUpdates.TryGetValue((itemId, userId), out var update)) return;

        var data = update.Data;
        var username = update.Username;

        _itemRuntimeTicks.TryGetValue(itemId, out var runtimeTicks);

        // Skip: ticks unknown → progress = 0 → corrupts debounce baseline; flush will retry once ticks are cached
        if (runtimeTicks == 0 && !data.Played)
            return;

        double progressPercent = 0.0;
        if (data.Played)
        {
            progressPercent = 100.0;
        }
        else if (runtimeTicks > 0)
        {
            progressPercent = (double)data.PlaybackPositionTicks / runtimeTicks * 100.0;
            if (progressPercent > 100.0) progressPercent = 100.0;
        }

        bool shouldWrite = false;
        var key = (itemId, userId);

        if (!_lastWrittenState.TryGetValue(key, out var lastWritten))
        {
            shouldWrite = true;
        }
        else
        {
            if (data.Played != lastWritten.Played)
            {
                shouldWrite = true;
            }
            else if (data.PlayCount > lastWritten.PlayCount)
            {
                shouldWrite = true;
            }
            else
            {
                int oldBoundary = (int)(lastWritten.ProgressPercent / 10.0);
                int newBoundary = (int)(progressPercent / 10.0);
                if (newBoundary != oldBoundary)
                {
                    shouldWrite = true;
                }
            }
        }

        if (shouldWrite)
        {
            await _dbSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

                var existing = await db.PlaybackActivities
                    .FirstOrDefaultAsync(p => p.MediaServerItemId == itemId && p.UserId == userId)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    existing = new PlaybackActivity
                    {
                        MediaServerItemId = itemId,
                        UserId = userId,
                        Username = username
                    };
                    db.PlaybackActivities.Add(existing);
                }

                existing.PlayCount = data.PlayCount;
                existing.LastWatched = data.LastPlayedDate?.UtcDateTime ?? DateTime.UtcNow;
                existing.IsFinished = data.Played;
                existing.ProgressPercent = progressPercent;
                existing.PlaybackPositionTicks = data.PlaybackPositionTicks;
                existing.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync().ConfigureAwait(false);

                _lastWrittenState[key] = (data.Played, data.PlayCount, progressPercent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write playback activity to DB for item {ItemId}, user {UserId}", itemId, userId);
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }
    }

    public async Task ForceFlushAsync(CancellationToken ct = default)
    {
        await _dbSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

            foreach (var kvp in _pendingUpdates)
            {
                if (ct.IsCancellationRequested) break;

                var (itemId, userId) = kvp.Key;
                var (data, username) = kvp.Value;

                _itemRuntimeTicks.TryGetValue(itemId, out var runtimeTicks);

                double progressPercent = data.Played ? 100.0 : 0.0;
                if (!data.Played && runtimeTicks > 0)
                {
                    progressPercent = (double)data.PlaybackPositionTicks / runtimeTicks * 100.0;
                    if (progressPercent > 100.0) progressPercent = 100.0;
                }

                // Apply the same debounce logic as ProcessPendingUpdateAsync
                bool shouldWrite = false;
                var key = (itemId, userId);
                if (!_lastWrittenState.TryGetValue(key, out var lastWritten))
                {
                    shouldWrite = true;
                }
                else if (data.Played != lastWritten.Played)
                {
                    shouldWrite = true;
                }
                else if (data.PlayCount > lastWritten.PlayCount)
                {
                    shouldWrite = true;
                }
                else
                {
                    int oldBoundary = (int)(lastWritten.ProgressPercent / 10.0);
                    int newBoundary = (int)(progressPercent / 10.0);
                    if (newBoundary != oldBoundary) shouldWrite = true;
                }

                if (!shouldWrite) continue;

                var existing = await db.PlaybackActivities
                    .FirstOrDefaultAsync(p => p.MediaServerItemId == itemId && p.UserId == userId, ct)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    existing = new PlaybackActivity
                    {
                        MediaServerItemId = itemId,
                        UserId = userId,
                        Username = username
                    };
                    db.PlaybackActivities.Add(existing);
                }

                existing.PlayCount = data.PlayCount;
                existing.LastWatched = data.LastPlayedDate?.UtcDateTime ?? DateTime.UtcNow;
                existing.IsFinished = data.Played;
                existing.ProgressPercent = progressPercent;
                existing.PlaybackPositionTicks = data.PlaybackPositionTicks;
                existing.UpdatedAt = DateTime.UtcNow;

                _lastWrittenState[key] = (data.Played, data.PlayCount, progressPercent);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to force flush playback activities to DB");
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }

    public async Task PruneOldActivitiesAsync(int ageLimitDays, CancellationToken ct = default)
    {
        await _dbSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

            var cutoff = DateTime.UtcNow.AddDays(-ageLimitDays);
            // Prune by UpdatedAt (last-upserted), not LastWatched.
            // A record not updated in N days is stale regardless of when media was watched.
            var toRemove = await db.PlaybackActivities
                .Where(p => p.UpdatedAt < cutoff)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (toRemove.Count > 0)
            {
                db.PlaybackActivities.RemoveRange(toRemove);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                _logger.LogInformation("Pruned {Count} playback activity records older than {Days} days", toRemove.Count, ageLimitDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prune old playback activities from DB");
        }
        finally
        {
            _dbSemaphore.Release();
        }
    }
}
