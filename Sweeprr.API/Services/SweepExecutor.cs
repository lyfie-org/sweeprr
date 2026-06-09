using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Sweep;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Bazarr;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Matching;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Radarr.Models;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Integrations.Sonarr.Models;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

/// <inheritdoc cref="ISweepExecutor"/>
public sealed class SweepExecutor : ISweepExecutor
{
    private readonly SweeprrDbContext         _db;
    private readonly IIntegrationClientFactory _clientFactory;
    private readonly IMediaMatchingService     _matcher;
    private readonly IFailsafeService          _failsafe;
    private readonly IOverlayRenderingService  _overlayService;
    private readonly ILogger<SweepExecutor>    _logger;

    public SweepExecutor(
        SweeprrDbContext db,
        IIntegrationClientFactory clientFactory,
        IMediaMatchingService matcher,
        IFailsafeService failsafe,
        IOverlayRenderingService overlayService,
        ILogger<SweepExecutor> logger)
    {
        _db             = db;
        _clientFactory  = clientFactory;
        _matcher        = matcher;
        _failsafe       = failsafe;
        _overlayService = overlayService;
        _logger         = logger;
    }

    public async Task<ExecuteSweepResult> ExecuteAsync(
        ExecuteSweepRequest request, CancellationToken ct = default)
    {
        var settings = await _db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct)
            ?? new GlobalSettings();

        bool isDryRun = settings.GlobalDryRun;
        bool allowDirectDelete = settings.AllowDirectJellyfinDeletion;

        // Resolve a Jellyfin connection ID once — only needed if direct-delete is enabled.
        int? jellyfinConnId = allowDirectDelete
            ? await ResolveJellyfinConnectionIdAsync(ct)
            : null;

        // Resolve optional Bazarr client for subtitle cleanup (null when not configured).
        var bazarrClient = await _clientFactory.CreateBazarrClientAsync(ct);
        if (bazarrClient is not null && !isDryRun && !await bazarrClient.IsAvailableAsync(ct))
        {
            _logger.LogWarning("Bazarr is unreachable — subtitle cleanup will be skipped for this sweep run");
            bazarrClient = null;
        }

        int totalQueueItems = await _db.SweepItems.CountAsync(ct);
        _failsafe.Initialize(
            settings.MaxItemsPerRun, settings.MaxGbPerRun, settings.PessimisticSizeGb,
            totalQueueItems, settings.LibraryPercentCap, settings.OverBroadMatchPct);

        var query = _db.SweepItems
            .Include(s => s.RuleGroup)
            .Where(s => s.Status == SweepItemStatus.Approved);

        if (request.ItemIds is { Count: > 0 } ids)
            query = query.Where(s => ids.Contains(s.Id));

        var items = await query.ToListAsync(ct);

        var counters = new RunCounters();

        // Pre-run breadth check: block entire run if approved count exceeds library % cap
        var preRunGate = _failsafe.CheckPreRunBreadth(items.Count);
        if (!preRunGate.IsOk)
        {
            _logger.LogWarning("Failsafe halted sweep run before execution: {Reason}", preRunGate.Reason);
            foreach (var item in items)
                item.SkippedReason = preRunGate.Reason;
            counters.FailsafeSkipped = items.Count;
            _db.ActivityLogs.Add(new ActivityLog
            {
                Category = ActivityLogCategory.Sweep,
                Level = ActivityLogLevel.Warning,
                Timestamp = DateTime.UtcNow,
                Message = preRunGate.Reason,
            });
            await _db.SaveChangesAsync(ct);
            return new ExecuteSweepResult(0, 0, counters.FailsafeSkipped, 0, isDryRun);
        }

        foreach (var grp in GroupByArrInstance(items))
        {
            // Per-group over-broad-match guard: block group if it dominates the batch
            var groupGate = _failsafe.CheckGroupBreadth(grp.Items.Count, items.Count);
            if (!groupGate.IsOk)
            {
                _logger.LogWarning("Failsafe blocked group (over-broad match): {Reason}", groupGate.Reason);
                foreach (var item in grp.Items)
                    item.SkippedReason = groupGate.Reason;
                counters.FailsafeSkipped += grp.Items.Count;
                _db.ActivityLogs.Add(new ActivityLog
                {
                    Category = ActivityLogCategory.Sweep,
                    Level = ActivityLogLevel.Warning,
                    Timestamp = DateTime.UtcNow,
                    Message = groupGate.Reason,
                });
                await _db.SaveChangesAsync(ct);
                continue;
            }

            int? connId = await ResolveConnectionIdAsync(
                grp.ConnectionId,
                grp.IsRadarr ? ConnectionType.Radarr : ConnectionType.Sonarr,
                ct);

            if (connId is null)
            {
                foreach (var item in grp.Items)
                {
                    item.Status = SweepItemStatus.Failed;
                    item.SkippedReason = "No enabled arr connection found";
                    counters.Failed++;
                }
                await _db.SaveChangesAsync(ct);
                continue;
            }

            if (grp.IsRadarr)
                await ProcessRadarrGroupAsync(grp.Items, connId.Value, isDryRun, allowDirectDelete, jellyfinConnId, bazarrClient, counters, ct);
            else
                await ProcessSonarrGroupAsync(grp.Items, connId.Value, isDryRun, allowDirectDelete, jellyfinConnId, bazarrClient, counters, ct);
        }

        if (counters.Swept > 0 || counters.Failed > 0 || counters.FailsafeSkipped > 0)
        {
            _db.ActivityLogs.Add(new ActivityLog
            {
                Category = ActivityLogCategory.Sweep,
                Level = ActivityLogLevel.Information,
                Timestamp = DateTime.UtcNow,
                Message = isDryRun
                    ? $"Dry-run sweep: would sweep {counters.Swept} item(s) / {counters.BytesRecovered / 1_073_741_824.0:F2} GB"
                    : $"Sweep: {counters.Swept} swept, {counters.Failed} failed, {counters.FailsafeSkipped} skipped (failsafe), {counters.BytesRecovered / 1_073_741_824.0:F2} GB recovered",
            });
            await _db.SaveChangesAsync(ct);
        }

        // Restore poster overlays for any items that ended up as Failed
        foreach (var item in items.Where(i => i.Status == SweepItemStatus.Failed))
            await _overlayService.RestoreOriginalAsync(item, ct);

        return new ExecuteSweepResult(
            counters.Swept, counters.Failed, counters.FailsafeSkipped,
            counters.BytesRecovered, isDryRun);
    }

    // ── Radarr group ─────────────────────────────────────────────────────────

    private async Task ProcessRadarrGroupAsync(
        IReadOnlyList<SweepItem> items, int connId, bool isDryRun,
        bool allowDirectDelete, int? jellyfinConnId, BazarrClient? bazarrClient,
        RunCounters counters, CancellationToken ct)
    {
        var client = await _clientFactory.CreateRadarrClientAsync(connId, ct);
        if (client is null)
        {
            foreach (var item in items)
            {
                item.Status = SweepItemStatus.Failed;
                item.SkippedReason = $"Could not create Radarr client for connection {connId}";
                counters.Failed++;
            }
            await _db.SaveChangesAsync(ct);
            return;
        }

        var moviesResult = await client.GetMoviesAsync(ct);
        if (moviesResult is not HttpResult<IReadOnlyList<RadarrMovie>>.Success moviesOk)
        {
            var reason = moviesResult switch
            {
                HttpResult<IReadOnlyList<RadarrMovie>>.TransientFailure t => $"Radarr transient: {t.Reason}",
                HttpResult<IReadOnlyList<RadarrMovie>>.DefinitiveFailure d => $"Radarr error {d.StatusCode}: {d.Reason}",
                _ => "Radarr: failed to load movies"
            };
            foreach (var item in items)
            {
                item.Status = SweepItemStatus.Failed;
                item.SkippedReason = reason;
                counters.Failed++;
            }
            await _db.SaveChangesAsync(ct);
            return;
        }

        var radarrIndex = _matcher.BuildRadarrIndex(moviesOk.Value);

        foreach (var item in items)
            await ProcessRadarrItemAsync(client, radarrIndex, item, isDryRun, allowDirectDelete, jellyfinConnId, bazarrClient, counters, ct);
    }

    private async Task ProcessRadarrItemAsync(
        RadarrClient client, ArrIndex<RadarrMovie> radarrIndex, SweepItem item,
        bool isDryRun, bool allowDirectDelete, int? jellyfinConnId, BazarrClient? bazarrClient,
        RunCounters counters, CancellationToken ct)
    {
        var identity = BuildIdentity(item);
        var matchResult = _matcher.MatchMovie(identity, radarrIndex);

        if (matchResult is not MatchResult<RadarrMovie>.Matched m)
        {
            if (matchResult is MatchResult<RadarrMovie>.Unmatched && allowDirectDelete && jellyfinConnId.HasValue)
            {
                await DirectJellyfinDeleteAsync(item, jellyfinConnId.Value, isDryRun, counters, ct);
            }
            else
            {
                item.Status = SweepItemStatus.Failed;
                item.SkippedReason = matchResult is MatchResult<RadarrMovie>.Unmatched
                    ? "OrphanedNoArrMatch"
                    : "Ambiguous Radarr match — manual review required";
                _logger.LogWarning("Cannot sweep '{Title}': {Reason}", item.Title, item.SkippedReason);
                counters.Failed++;
                await _db.SaveChangesAsync(ct);
            }
            return;
        }

        var radarrMovieId = m.Value.Id;

        var gate = _failsafe.CheckAndRecord(item.SizeBytes);
        if (!gate.IsOk)
        {
            item.SkippedReason = gate.Reason;
            counters.FailsafeSkipped++;
            _logger.LogWarning("Failsafe halted sweep of '{Title}': {Reason}", item.Title, gate.Reason);
            await _db.SaveChangesAsync(ct);
            return;
        }

        if (isDryRun)
        {
            _logger.LogInformation(
                "Dry-run: would {Action} Radarr movie '{Title}' (RadarrId={Id})",
                item.RuleGroup.Action, item.Title, radarrMovieId);
            counters.Swept++;
            counters.BytesRecovered += item.SizeBytes ?? 0;
            await _db.SaveChangesAsync(ct);
            return;
        }

        bool success = item.RuleGroup.Action switch
        {
            SweepAction.UnmonitorOnly        => await UnmonitorOnlyRadarrAsync(client, radarrMovieId, item, ct),
            SweepAction.DeleteOnly           => await DeleteOnlyRadarrAsync(client, radarrMovieId, item, ct),
            SweepAction.ChangeQualityProfile => await ChangeQualityProfileRadarrAsync(client, radarrMovieId, item, ct),
            _                               => await DeleteAndUnmonitorRadarrAsync(client, radarrMovieId, item, ct),
        };

        if (success)
        {
            item.Status = SweepItemStatus.Swept;
            item.SweptAt = DateTime.UtcNow;
            counters.Swept++;
            counters.BytesRecovered += item.SizeBytes ?? 0;
            _logger.LogInformation(
                "Swept '{Title}' ({Action}): {Gb:F2} GB recovered",
                item.Title, item.RuleGroup.Action, (item.SizeBytes ?? 0) / 1_073_741_824.0);

            if (bazarrClient is not null)
                await TriggerBazarrCleanupAsync(item.Title, radarrMovieId, isMovie: true, bazarrClient, isDryRun, ct);
        }
        else
        {
            counters.Failed++;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<bool> DeleteAndUnmonitorRadarrAsync(
        RadarrClient client, int movieId, SweepItem item, CancellationToken ct)
    {
        var unmonitorResult = await client.UnmonitorMovieAsync(movieId, ct);
        if (unmonitorResult is not HttpResult<RadarrMovie>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(unmonitorResult, "Unmonitor");
            _logger.LogWarning("Aborting delete of '{Title}': unmonitor failed — {Reason}",
                item.Title, item.SkippedReason);
            return false;
        }

        var deleteResult = await client.DeleteMovieAsync(
            movieId, deleteFiles: true, addExclusion: item.RuleGroup.AddImportExclusion, ct);

        if (deleteResult is HttpResult<EmptyResponse>.DefinitiveFailure { StatusCode: 404 })
            return true; // already gone — treat as success

        if (deleteResult is not HttpResult<EmptyResponse>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(deleteResult, "Delete");
            return false;
        }
        return true;
    }

    private async Task<bool> UnmonitorOnlyRadarrAsync(
        RadarrClient client, int movieId, SweepItem item, CancellationToken ct)
    {
        var result = await client.UnmonitorMovieAsync(movieId, ct);
        if (result is not HttpResult<RadarrMovie>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(result, "Unmonitor");
            return false;
        }
        return true;
    }

    private async Task<bool> DeleteOnlyRadarrAsync(
        RadarrClient client, int movieId, SweepItem item, CancellationToken ct)
    {
        _logger.LogWarning(
            "DeleteOnly on '{Title}': escalating to DeleteAndUnmonitor to satisfy safety invariant (must always unmonitor)",
            item.Title);

        return await DeleteAndUnmonitorRadarrAsync(client, movieId, item, ct);
    }

    // ── Sonarr group ─────────────────────────────────────────────────────────

    private async Task ProcessSonarrGroupAsync(
        IReadOnlyList<SweepItem> items, int connId, bool isDryRun,
        bool allowDirectDelete, int? jellyfinConnId, BazarrClient? bazarrClient,
        RunCounters counters, CancellationToken ct)
    {
        var client = await _clientFactory.CreateSonarrClientAsync(connId, ct);
        if (client is null)
        {
            foreach (var item in items)
            {
                item.Status = SweepItemStatus.Failed;
                item.SkippedReason = $"Could not create Sonarr client for connection {connId}";
                counters.Failed++;
            }
            await _db.SaveChangesAsync(ct);
            return;
        }

        var seriesResult = await client.GetSeriesAsync(ct);
        if (seriesResult is not HttpResult<IReadOnlyList<SonarrSeries>>.Success seriesOk)
        {
            var reason = seriesResult switch
            {
                HttpResult<IReadOnlyList<SonarrSeries>>.TransientFailure t => $"Sonarr transient: {t.Reason}",
                HttpResult<IReadOnlyList<SonarrSeries>>.DefinitiveFailure d => $"Sonarr error {d.StatusCode}: {d.Reason}",
                _ => "Sonarr: failed to load series"
            };
            foreach (var item in items)
            {
                item.Status = SweepItemStatus.Failed;
                item.SkippedReason = reason;
                counters.Failed++;
            }
            await _db.SaveChangesAsync(ct);
            return;
        }

        var sonarrIndex = _matcher.BuildSonarrIndex(seriesOk.Value);

        foreach (var item in items)
            await ProcessSonarrItemAsync(client, sonarrIndex, item, isDryRun, allowDirectDelete, jellyfinConnId, bazarrClient, counters, ct);
    }

    private async Task ProcessSonarrItemAsync(
        SonarrClient client, ArrIndex<SonarrSeries> sonarrIndex, SweepItem item,
        bool isDryRun, bool allowDirectDelete, int? jellyfinConnId, BazarrClient? bazarrClient,
        RunCounters counters, CancellationToken ct)
    {
        var identity = BuildIdentity(item);
        var matchResult = _matcher.MatchSeries(identity, sonarrIndex);

        if (matchResult is not MatchResult<SonarrSeriesMatch>.Matched m)
        {
            if (matchResult is MatchResult<SonarrSeriesMatch>.Unmatched && allowDirectDelete && jellyfinConnId.HasValue)
            {
                await DirectJellyfinDeleteAsync(item, jellyfinConnId.Value, isDryRun, counters, ct);
            }
            else
            {
                item.Status = SweepItemStatus.Failed;
                item.SkippedReason = matchResult is MatchResult<SonarrSeriesMatch>.Unmatched
                    ? "OrphanedNoArrMatch"
                    : "Ambiguous Sonarr match — manual review required";
                _logger.LogWarning("Cannot sweep '{Title}': {Reason}", item.Title, item.SkippedReason);
                counters.Failed++;
                await _db.SaveChangesAsync(ct);
            }
            return;
        }

        var seriesId = m.Value.Series.Id;
        var seasonNumber = item.SeasonNumber;

        var gate = _failsafe.CheckAndRecord(item.SizeBytes);
        if (!gate.IsOk)
        {
            item.SkippedReason = gate.Reason;
            counters.FailsafeSkipped++;
            _logger.LogWarning("Failsafe halted sweep of '{Title}': {Reason}", item.Title, gate.Reason);
            await _db.SaveChangesAsync(ct);
            return;
        }

        if (isDryRun)
        {
            _logger.LogInformation(
                "Dry-run: would {Action} Sonarr series '{Title}' (SeriesId={Id}, Season={Season})",
                item.RuleGroup.Action, item.Title, seriesId, seasonNumber?.ToString() ?? "all");
            counters.Swept++;
            counters.BytesRecovered += item.SizeBytes ?? 0;
            await _db.SaveChangesAsync(ct);
            return;
        }

        bool success = item.RuleGroup.Action switch
        {
            SweepAction.UnmonitorOnly =>
                await UnmonitorOnlySonarrAsync(client, seriesId, seasonNumber, item, ct),
            SweepAction.DeleteOnly =>
                await DeleteOnlySonarrAsync(client, seriesId, seasonNumber, item, ct),
            SweepAction.DeleteSeriesIfEmpty =>
                await DeleteSeriesIfEmptyAsync(client, seriesId, item, ct),
            SweepAction.UnmonitorSeasonIfEmpty =>
                seasonNumber.HasValue
                    ? await UnmonitorSeasonIfEmptyAsync(client, seriesId, seasonNumber.Value, item, ct)
                    : MarkFailed(item, "UnmonitorSeasonIfEmpty requires a season number"),
            SweepAction.ChangeQualityProfile =>
                await ChangeQualityProfileSonarrAsync(client, seriesId, item, ct),
            _ =>
                await DeleteAndUnmonitorSonarrAsync(client, seriesId, seasonNumber, item, ct),
        };

        if (success)
        {
            item.Status = SweepItemStatus.Swept;
            item.SweptAt = DateTime.UtcNow;
            counters.Swept++;
            counters.BytesRecovered += item.SizeBytes ?? 0;
            _logger.LogInformation(
                "Swept '{Title}' ({Action}, Season={Season}): {Gb:F2} GB recovered",
                item.Title, item.RuleGroup.Action,
                seasonNumber?.ToString() ?? "all",
                (item.SizeBytes ?? 0) / 1_073_741_824.0);

            if (bazarrClient is not null)
                await TriggerBazarrCleanupAsync(item.Title, seriesId, isMovie: false, bazarrClient, isDryRun, ct);
        }
        else
        {
            counters.Failed++;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<bool> DeleteAndUnmonitorSonarrAsync(
        SonarrClient client, int seriesId, int? seasonNumber, SweepItem item, CancellationToken ct)
    {
        HttpResult<SonarrSeries> unmonitorResult = seasonNumber.HasValue
            ? await client.UnmonitorSeasonAsync(seriesId, seasonNumber.Value, ct)
            : await client.UnmonitorSeriesAsync(seriesId, ct);

        if (unmonitorResult is not HttpResult<SonarrSeries>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(unmonitorResult, "Unmonitor");
            _logger.LogWarning("Aborting delete of '{Title}': unmonitor failed — {Reason}",
                item.Title, item.SkippedReason);
            return false;
        }

        if (seasonNumber.HasValue)
            return await DeleteSeasonFilesAsync(client, seriesId, seasonNumber.Value, item, ct);

        var deleteResult = await client.DeleteSeriesAsync(
            seriesId, deleteFiles: true, addExclusion: item.RuleGroup.AddImportExclusion, ct);

        if (deleteResult is HttpResult<EmptyResponse>.DefinitiveFailure { StatusCode: 404 }) return true;
        if (deleteResult is not HttpResult<EmptyResponse>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(deleteResult, "Delete series");
            return false;
        }
        return true;
    }

    private async Task<bool> DeleteSeasonFilesAsync(
        SonarrClient client, int seriesId, int seasonNumber, SweepItem item, CancellationToken ct)
    {
        var filesResult = await client.GetEpisodeFilesAsync(seriesId, ct);
        if (filesResult is not HttpResult<IReadOnlyList<SonarrEpisodeFile>>.Success filesOk)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(filesResult, "Get episode files");
            return false;
        }

        var seasonFiles = filesOk.Value.Where(f => f.SeasonNumber == seasonNumber).ToList();
        foreach (var file in seasonFiles)
        {
            var del = await client.DeleteEpisodeFileAsync(file.Id, ct);
            if (del is not HttpResult<EmptyResponse>.Success
                and not HttpResult<EmptyResponse>.DefinitiveFailure { StatusCode: 404 })
            {
                _logger.LogWarning("Failed to delete episode file {Id} for '{Title}': {Reason}",
                    file.Id, item.Title, FailureReason(del, "Delete episode file"));
            }
        }
        return true;
    }

    private async Task<bool> UnmonitorOnlySonarrAsync(
        SonarrClient client, int seriesId, int? seasonNumber, SweepItem item, CancellationToken ct)
    {
        var result = seasonNumber.HasValue
            ? await client.UnmonitorSeasonAsync(seriesId, seasonNumber.Value, ct)
            : await client.UnmonitorSeriesAsync(seriesId, ct);

        if (result is not HttpResult<SonarrSeries>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(result, "Unmonitor");
            return false;
        }
        return true;
    }

    private async Task<bool> DeleteOnlySonarrAsync(
        SonarrClient client, int seriesId, int? seasonNumber, SweepItem item, CancellationToken ct)
    {
        _logger.LogWarning(
            "DeleteOnly on '{Title}': escalating to DeleteAndUnmonitor to satisfy safety invariant (must always unmonitor)",
            item.Title);

        return await DeleteAndUnmonitorSonarrAsync(client, seriesId, seasonNumber, item, ct);
    }

    private async Task<bool> DeleteSeriesIfEmptyAsync(
        SonarrClient client, int seriesId, SweepItem item, CancellationToken ct)
    {
        var filesResult = await client.GetEpisodeFilesAsync(seriesId, ct);
        if (filesResult is not HttpResult<IReadOnlyList<SonarrEpisodeFile>>.Success filesOk)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(filesResult, "Get episode files");
            return false;
        }

        if (filesOk.Value.Count > 0)
        {
            _logger.LogInformation(
                "DeleteSeriesIfEmpty: '{Title}' still has {Count} episode file(s) — skipping",
                item.Title, filesOk.Value.Count);
            item.SkippedReason = $"Series not empty ({filesOk.Value.Count} episode files remain)";
            return false;
        }

        // Guard: do not delete if the latest season is still monitored (future episodes expected)
        var seriesResult = await client.GetSeriesByIdAsync(seriesId, ct);
        if (seriesResult is HttpResult<SonarrSeries>.Success seriesOk)
        {
            var latestSeason = seriesOk.Value.Seasons
                .Where(s => s.SeasonNumber > 0)
                .OrderByDescending(s => s.SeasonNumber)
                .FirstOrDefault();

            if (latestSeason?.Monitored == true)
            {
                _logger.LogInformation(
                    "DeleteSeriesIfEmpty: '{Title}' latest season S{S} still monitored — skipping",
                    item.Title, latestSeason.SeasonNumber);
                item.SkippedReason = $"Latest season (S{latestSeason.SeasonNumber}) is still monitored";
                return false;
            }
        }

        var deleteResult = await client.DeleteSeriesAsync(
            seriesId, deleteFiles: false, addExclusion: item.RuleGroup.AddImportExclusion, ct);

        if (deleteResult is HttpResult<EmptyResponse>.DefinitiveFailure { StatusCode: 404 }) return true;
        if (deleteResult is not HttpResult<EmptyResponse>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(deleteResult, "Delete empty series");
            return false;
        }
        return true;
    }

    private async Task<bool> UnmonitorSeasonIfEmptyAsync(
        SonarrClient client, int seriesId, int seasonNumber, SweepItem item, CancellationToken ct)
    {
        var unmonitorResult = await client.UnmonitorSeasonAsync(seriesId, seasonNumber, ct);
        if (unmonitorResult is not HttpResult<SonarrSeries>.Success unmonitorOk)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(unmonitorResult, "Unmonitor season");
            return false;
        }

        var allUnmonitored = unmonitorOk.Value.Seasons
            .Where(s => s.SeasonNumber > 0)
            .All(s => !s.Monitored);

        if (allUnmonitored)
        {
            _logger.LogInformation(
                "UnmonitorSeasonIfEmpty: all seasons unmonitored for '{Title}' — unmonitoring series root",
                item.Title);
            await client.UnmonitorSeriesAsync(seriesId, ct);
        }

        return true;
    }

    // ── Quality profile downgrade ─────────────────────────────────────────────

    private async Task<bool> ChangeQualityProfileRadarrAsync(
        RadarrClient client, int movieId, SweepItem item, CancellationToken ct)
    {
        if (item.RuleGroup.TargetQualityProfileId is null)
        {
            _logger.LogWarning(
                "ChangeQualityProfile: no target profile set on rule group '{Group}'. Skipping '{Title}'.",
                item.RuleGroup.Name, item.Title);
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = "ChangeQualityProfile: TargetQualityProfileId not set on rule group";
            return false;
        }

        var updateResult = await client.UpdateMovieQualityProfileAsync(
            movieId, item.RuleGroup.TargetQualityProfileId.Value, ct);

        if (updateResult is not HttpResult<EmptyResponse>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(updateResult, "UpdateQualityProfile");
            _logger.LogWarning("ChangeQualityProfile failed for '{Title}': {Reason}",
                item.Title, item.SkippedReason);
            return false;
        }

        var searchResult = await client.TriggerMovieSearchAsync(movieId, ct);
        if (searchResult is not HttpResult<EmptyResponse>.Success)
        {
            // Profile was changed successfully — search trigger failure is non-fatal.
            _logger.LogWarning(
                "ChangeQualityProfile: profile updated for '{Title}' but search trigger failed: {Reason}",
                item.Title, FailureReason(searchResult, "TriggerSearch"));
        }

        _logger.LogInformation(
            "ChangeQualityProfile: updated Radarr movie '{Title}' to profile {ProfileId} ('{ProfileName}') and triggered search",
            item.Title,
            item.RuleGroup.TargetQualityProfileId.Value,
            item.RuleGroup.TargetQualityProfileName ?? "unknown");

        return true;
    }

    private async Task<bool> ChangeQualityProfileSonarrAsync(
        SonarrClient client, int seriesId, SweepItem item, CancellationToken ct)
    {
        if (item.RuleGroup.TargetQualityProfileId is null)
        {
            _logger.LogWarning(
                "ChangeQualityProfile: no target profile set on rule group '{Group}'. Skipping '{Title}'.",
                item.RuleGroup.Name, item.Title);
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = "ChangeQualityProfile: TargetQualityProfileId not set on rule group";
            return false;
        }

        var updateResult = await client.UpdateSeriesQualityProfileAsync(
            seriesId, item.RuleGroup.TargetQualityProfileId.Value, ct);

        if (updateResult is not HttpResult<EmptyResponse>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(updateResult, "UpdateQualityProfile");
            _logger.LogWarning("ChangeQualityProfile failed for '{Title}': {Reason}",
                item.Title, item.SkippedReason);
            return false;
        }

        var searchResult = await client.TriggerSeriesSearchAsync(seriesId, ct);
        if (searchResult is not HttpResult<EmptyResponse>.Success)
        {
            _logger.LogWarning(
                "ChangeQualityProfile: profile updated for '{Title}' but search trigger failed: {Reason}",
                item.Title, FailureReason(searchResult, "TriggerSearch"));
        }

        _logger.LogInformation(
            "ChangeQualityProfile: updated Sonarr series '{Title}' to profile {ProfileId} ('{ProfileName}') and triggered search",
            item.Title,
            item.RuleGroup.TargetQualityProfileId.Value,
            item.RuleGroup.TargetQualityProfileName ?? "unknown");

        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool MarkFailed(SweepItem item, string reason)
    {
        item.Status = SweepItemStatus.Failed;
        item.SkippedReason = reason;
        return false;
    }

    private static MediaIdentity BuildIdentity(SweepItem item)
    {
        int? tmdbId = item.TmdbId is not null && int.TryParse(item.TmdbId, out var t) ? t : null;
        int? tvdbId = item.TvdbId is not null && int.TryParse(item.TvdbId, out var tv) ? tv : null;
        return new MediaIdentity(item.ImdbId, tmdbId, tvdbId, item.SeasonNumber);
    }

    private static string FailureReason<T>(HttpResult<T> result, string operation) => result switch
    {
        HttpResult<T>.TransientFailure tf  => $"{operation} failed (transient): {tf.Reason}",
        HttpResult<T>.DefinitiveFailure df => $"{operation} failed ({df.StatusCode}): {df.Reason}",
        _                                  => $"{operation} failed: unexpected result"
    };

    // ── Bazarr subtitle cleanup ──────────────────────────────────────────────

    /// <summary>
    /// Triggers Bazarr subtitle cleanup after a successful sweep action.
    /// Failures are logged as warnings and never affect the sweep item's outcome.
    /// </summary>
    private async Task TriggerBazarrCleanupAsync(
        string title, int arrId, bool isMovie,
        BazarrClient bazarrClient, bool isDryRun, CancellationToken ct)
    {
        try
        {
            if (isDryRun)
            {
                _logger.LogInformation(
                    "[DRY-RUN] Would delete Bazarr subtitles for '{Title}' ({Type}, ArrId={Id})",
                    title, isMovie ? "movie" : "series", arrId);
                return;
            }

            if (isMovie)
                await bazarrClient.DeleteSubtitlesForMovieAsync(arrId, ct);
            else
                await bazarrClient.DeleteSubtitlesForSeriesAsync(arrId, ct);

            _logger.LogInformation("Bazarr: subtitle cleanup triggered for '{Title}'", title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Bazarr subtitle cleanup failed for '{Title}' — sweep result is unaffected", title);
        }
    }

    // ── Direct Jellyfin fallback ─────────────────────────────────────────────

    /// <summary>
    /// Deletes an orphaned item (no *arr match) directly via the Jellyfin API.
    /// Exclusion check, dry-run gate, and failsafe are all enforced before
    /// the destructive call — safety invariants are never bypassed.
    /// </summary>
    private async Task DirectJellyfinDeleteAsync(
        SweepItem item, int jellyfinConnId, bool isDryRun,
        RunCounters counters, CancellationToken ct)
    {
        // Safety: re-verify the item is not excluded (global or scoped).
        bool excluded = await _db.Exclusions.AnyAsync(
            e => e.MediaServerItemId == item.MediaServerItemId
                 && (e.RuleGroupId == null || e.RuleGroupId == item.RuleGroupId)
                 && (e.ExpiresAt == null || e.ExpiresAt > DateTime.UtcNow),
            ct);

        if (excluded)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = "Excluded";
            _logger.LogInformation(
                "DirectJellyfinDelete skipped — '{Title}' is excluded.", item.Title);
            counters.Failed++;
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Failsafe: orphan deletions count toward the per-run limits.
        var gate = _failsafe.CheckAndRecord(item.SizeBytes);
        if (!gate.IsOk)
        {
            item.SkippedReason = gate.Reason;
            counters.FailsafeSkipped++;
            _logger.LogWarning("Failsafe halted direct-delete of '{Title}': {Reason}",
                item.Title, gate.Reason);
            await _db.SaveChangesAsync(ct);
            return;
        }

        if (isDryRun)
        {
            _logger.LogInformation(
                "[DRY-RUN] Would direct-delete orphaned Jellyfin item '{Title}' (ItemId={Id})",
                item.Title, item.MediaServerItemId);
            item.Status = SweepItemStatus.Swept;
            item.SweptAt = DateTime.UtcNow;
            counters.Swept++;
            counters.BytesRecovered += item.SizeBytes ?? 0;
            await _db.SaveChangesAsync(ct);
            return;
        }

        var jellyfinClient = await _clientFactory.CreateJellyfinClientAsync(jellyfinConnId, ct);
        if (jellyfinClient is null)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = $"Could not create Jellyfin client for connection {jellyfinConnId}";
            counters.Failed++;
            _logger.LogWarning("DirectJellyfinDelete: could not build client for '{Title}'.", item.Title);
            await _db.SaveChangesAsync(ct);
            return;
        }

        var result = await jellyfinClient.DeleteItemAsync(item.MediaServerItemId, ct);

        if (result is HttpResult<EmptyResponse>.Success
            or HttpResult<EmptyResponse>.DefinitiveFailure { StatusCode: 404 })
        {
            // 404 = already gone; treat as success.
            item.Status = SweepItemStatus.Swept;
            item.SweptAt = DateTime.UtcNow;
            counters.Swept++;
            counters.BytesRecovered += item.SizeBytes ?? 0;
            _logger.LogInformation(
                "[DIRECT-DELETE] Deleted orphaned Jellyfin item '{Title}' (ItemId={Id}): {Gb:F2} GB recovered",
                item.Title, item.MediaServerItemId, (item.SizeBytes ?? 0) / 1_073_741_824.0);
        }
        else
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(result, "DirectJellyfinDelete");
            counters.Failed++;
            _logger.LogWarning(
                "DirectJellyfinDelete failed for '{Title}': {Reason}", item.Title, item.SkippedReason);
        }

        await _db.SaveChangesAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<int?> ResolveConnectionIdAsync(
        int? storedConnId, ConnectionType type, CancellationToken ct)
    {
        if (storedConnId.HasValue) return storedConnId.Value;

        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == type && c.IsEnabled, ct);
        return conn?.Id;
    }

    private async Task<int?> ResolveJellyfinConnectionIdAsync(CancellationToken ct)
    {
        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Jellyfin && c.IsEnabled, ct);
        return conn?.Id;
    }

    private static IEnumerable<ArrGroup> GroupByArrInstance(IReadOnlyList<SweepItem> items) =>
        items
            .GroupBy(s => (IsRadarr: s.MediaType == MediaType.Movie, ConnId: s.ArrInstanceId))
            .Select(g => new ArrGroup(g.Key.IsRadarr, g.Key.ConnId, g.ToList().AsReadOnly()));

    private sealed class RunCounters
    {
        public int Swept;
        public int Failed;
        public int FailsafeSkipped;
        public long BytesRecovered;
    }

    private sealed record ArrGroup(
        bool IsRadarr,
        int? ConnectionId,
        IReadOnlyList<SweepItem> Items);
}
