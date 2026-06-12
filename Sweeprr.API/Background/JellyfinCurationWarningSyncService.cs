using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.API.Background;

/// <summary>
/// Keeps the "Sweeprr - Leaving Soon" Jellyfin BoxSet collection in sync with the
/// current Sweep Queue (Pending + Approved items).
///
/// Runs on two triggers:
///   1. A queue-change signal written to the shared <see cref="Channel{T}"/> by
///      <see cref="SweepQueueService"/> whenever item statuses change.
///   2. A 60-minute periodic fallback in case no signal arrives.
///
/// Guards concurrent runs with a <see cref="SemaphoreSlim"/> — a second trigger while
/// a sync is in progress is dropped; the next tick will catch any delta.
/// </summary>
public sealed class JellyfinCurationWarningSyncService : BackgroundService
{
    private const string CollectionName = "Sweeprr - Leaving Soon";
    private const int    BatchSize      = 100;

    private readonly IServiceScopeFactory                          _scopeFactory;
    private readonly ChannelReader<byte>                           _triggerReader;
    private readonly ILogger<JellyfinCurationWarningSyncService>   _logger;
    private readonly SemaphoreSlim                                 _syncLock = new(1, 1);

    public JellyfinCurationWarningSyncService(
        IServiceScopeFactory scopeFactory,
        Channel<byte> triggerChannel,
        ILogger<JellyfinCurationWarningSyncService> logger)
    {
        _scopeFactory  = scopeFactory;
        _triggerReader = triggerChannel.Reader;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial sync shortly after startup — give other hosted services time to connect.
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
        await RunSyncAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait up to 60 min for a trigger signal.
                // CancelAfter wakes us on timeout; stoppingToken wakes us on shutdown.
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                delayCts.CancelAfter(TimeSpan.FromMinutes(60));

                try
                {
                    await _triggerReader.WaitToReadAsync(delayCts.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // 60-minute timer elapsed — fall through to periodic sync
                }

                if (stoppingToken.IsCancellationRequested) break;

                // Drain any queued signals so we do one sync, not N
                while (_triggerReader.TryRead(out _)) { }

                await RunSyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LeavingSoonSync: unexpected error in event loop");
            }
        }
    }

    // ── Sync execution ────────────────────────────────────────────────────────

    private async Task RunSyncAsync(CancellationToken ct)
    {
        if (!await _syncLock.WaitAsync(0))
        {
            _logger.LogDebug("LeavingSoonSync: skipped — sync already running");
            return;
        }

        try
        {
            await ExecuteSyncAsync(ct);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LeavingSoonSync: unhandled error during sync");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task ExecuteSyncAsync(CancellationToken ct)
    {
        using var scope        = _scopeFactory.CreateScope();
        var db                 = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var clientFactory      = scope.ServiceProvider.GetRequiredService<IIntegrationClientFactory>();
        var connService        = scope.ServiceProvider.GetRequiredService<IConnectionService>();

        var settings = await db.GlobalSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);

        if (settings is null || !settings.LeavingSoonSyncEnabled)
        {
            _logger.LogDebug("LeavingSoonSync: disabled in settings, skipping");
            return;
        }

        var connections  = await connService.GetAllAsync();
        var jellyfinConn = connections.FirstOrDefault(c => c.Type == ConnectionType.Jellyfin && c.IsEnabled);

        if (jellyfinConn is null)
        {
            _logger.LogDebug("LeavingSoonSync: no enabled Jellyfin connection, skipping");
            return;
        }

        var client = await clientFactory.CreateJellyfinClientAsync(jellyfinConn.Id, ct);
        if (client is null)
        {
            _logger.LogWarning("LeavingSoonSync: could not build Jellyfin client for connection {Id}", jellyfinConn.Id);
            return;
        }

        // Collect Pending + Approved item IDs from the sweep queue
        var queuedIds = await db.SweepItems
            .Where(s => (s.Status == SweepItemStatus.Pending || s.Status == SweepItemStatus.Approved)
                     && !string.IsNullOrEmpty(s.MediaServerItemId))
            .Select(s => s.MediaServerItemId)
            .ToListAsync(ct);

        var queuedSet = queuedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var collectionId = await GetOrCreateCollectionAsync(client, settings, db, ct);
        if (collectionId is null)
        {
            _logger.LogWarning("LeavingSoonSync: cannot resolve or create collection, aborting");
            return;
        }

        var membersResult = await client.GetCollectionItemsAsync(collectionId, ct);
        if (membersResult is not HttpResult<IReadOnlyList<JellyfinItem>>.Success membersOk)
        {
            _logger.LogWarning("LeavingSoonSync: failed to load collection members: {Result}", membersResult);
            return;
        }

        var currentIds = membersOk.Value
            .Select(i => i.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd    = queuedSet.Except(currentIds).ToList();
        var toRemove = currentIds.Except(queuedSet).ToList();

        int addFail = 0, removeFail = 0;

        foreach (var batch in toAdd.Chunk(BatchSize))
        {
            var r = await client.AddItemsToCollectionAsync(collectionId, batch, ct);
            if (r is not HttpResult<EmptyResponse>.Success) addFail += batch.Length;
        }

        foreach (var batch in toRemove.Chunk(BatchSize))
        {
            var r = await client.RemoveItemsFromCollectionAsync(collectionId, batch, ct);
            if (r is not HttpResult<EmptyResponse>.Success) removeFail += batch.Length;
        }

        if (addFail > 0 || removeFail > 0)
            _logger.LogWarning(
                "LeavingSoonSync: partial sync — {AddFail} add failures, {RemFail} remove failures",
                addFail, removeFail);
        else
            _logger.LogInformation(
                "LeavingSoonSync: +{Add} / -{Remove} items synced to '{Collection}'",
                toAdd.Count, toRemove.Count, CollectionName);
    }

    // ── Collection resolution ─────────────────────────────────────────────────

    private async Task<string?> GetOrCreateCollectionAsync(
        JellyfinClient client,
        GlobalSettings settings,
        SweeprrDbContext db,
        CancellationToken ct)
    {
        // 1. Validate stored ID — only trust it if the collection is still reachable.
        //    Transient failures are not grounds for recreation (we don't know it's gone).
        if (!string.IsNullOrEmpty(settings.LeavingSoonCollectionId))
        {
            var probe = await client.GetCollectionItemsAsync(settings.LeavingSoonCollectionId, ct);
            if (probe.IsSuccess || !probe.IsNotFound)
                return settings.LeavingSoonCollectionId;

            // Definitively 404 — deleted in Jellyfin; recreate
            _logger.LogWarning(
                "LeavingSoonSync: stored collection ID {Id} returned 404, will recreate",
                settings.LeavingSoonCollectionId);
            settings.LeavingSoonCollectionId = null;
        }

        // 2. Search existing BoxSet collections for a name match
        var listResult = await client.GetCollectionsAsync(ct);
        if (listResult is HttpResult<IReadOnlyList<JellyfinItem>>.Success listOk)
        {
            var match = listOk.Value.FirstOrDefault(c =>
                string.Equals(c.Name, CollectionName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                settings.LeavingSoonCollectionId = match.Id;
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "LeavingSoonSync: found existing collection '{Name}' (ID: {Id})",
                    CollectionName, match.Id);
                return match.Id;
            }
        }

        // 3. Create new collection
        var createResult = await client.CreateCollectionAsync(CollectionName, ct);
        if (createResult is not HttpResult<string>.Success createOk)
        {
            _logger.LogError(
                "LeavingSoonSync: failed to create collection '{Name}': {Result}",
                CollectionName, createResult);
            return null;
        }

        settings.LeavingSoonCollectionId = createOk.Value;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "LeavingSoonSync: created Jellyfin collection '{Name}' (ID: {Id})",
            CollectionName, createOk.Value);
        return createOk.Value;
    }
}
