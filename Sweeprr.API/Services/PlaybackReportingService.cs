using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <summary>
/// Orchestrates Playback Reporting plugin detection + backfill (Story 10.1).
/// Detection status is persisted via <see cref="IConnectionService.SetPlaybackReportingPluginStatusAsync"/>
/// so the Connections UI can surface a badge. Backfill rows are merged into
/// <see cref="PlaybackActivity"/>, which is also written live by the WebSocket
/// playback tracker (Story 7.1) — merge semantics must not clobber live state.
/// </summary>
public sealed class PlaybackReportingService : IPlaybackReportingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlaybackReportingService> _logger;

    public PlaybackReportingService(
        IServiceScopeFactory scopeFactory,
        ILogger<PlaybackReportingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var factory     = scope.ServiceProvider.GetRequiredService<IIntegrationClientFactory>();
        var connections = scope.ServiceProvider.GetRequiredService<IConnectionService>();

        var conn = await db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Jellyfin && c.IsEnabled, ct);
        if (conn is null) return;

        var client = await factory.CreateJellyfinClientAsync(conn.Id, ct);
        if (client is null) return;

        var pluginActive = await client.GetPlaybackReportingPluginStatusAsync(ct);

        // Null = inconclusive (network error, 5xx) — leave previously-known status unchanged.
        if (pluginActive is not null)
            await connections.SetPlaybackReportingPluginStatusAsync(conn.Id, pluginActive, ct);

        if (pluginActive != true) return;

        var backfillResult = await client.GetPlaybackReportBackfillAsync(ct);
        if (backfillResult is not HttpResult<IReadOnlyList<PlaybackReportRow>>.Success success)
        {
            _logger.LogWarning("[PlaybackReportingService] Backfill query failed for connection {ConnectionId}", conn.Id);
            return;
        }

        var rows = success.Value;
        if (rows.Count == 0) return;

        var usersResult  = await client.GetUsersAsync(ct);
        var usernameMap  = usersResult is HttpResult<IReadOnlyList<JellyfinUser>>.Success usersOk
            ? usersOk.Value.ToDictionary(u => u.Id, u => u.Name)
            : new Dictionary<string, string>();

        var existing = await db.PlaybackActivities
            .ToDictionaryAsync(p => (p.MediaServerItemId, p.UserId), p => p, ct);

        foreach (var row in rows)
        {
            if (existing.TryGetValue((row.ItemId, row.UserId), out var entity))
            {
                // Live-tracked fields (IsFinished/ProgressPercent/PlaybackPositionTicks)
                // are left untouched — the backfill only fills in/extends history.
                entity.PlayCount = Math.Max(entity.PlayCount, row.PlayCount);
                if (row.LastPlayed > entity.LastWatched)
                    entity.LastWatched = row.LastPlayed;
                entity.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.PlaybackActivities.Add(new PlaybackActivity
                {
                    MediaServerItemId    = row.ItemId,
                    UserId               = row.UserId,
                    Username             = usernameMap.GetValueOrDefault(row.UserId, "Unknown"),
                    PlayCount            = row.PlayCount,
                    LastWatched          = row.LastPlayed,
                    IsFinished           = true,
                    ProgressPercent      = 100.0,
                    PlaybackPositionTicks = 0,
                    UpdatedAt            = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "[PlaybackReportingService] Backfill processed {Count} rows for connection {ConnectionId}",
            rows.Count, conn.Id);
    }
}
