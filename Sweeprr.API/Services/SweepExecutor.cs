using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Sweep;
using Sweeprr.API.Integrations;
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
    private readonly SweeprrDbContext _db;
    private readonly IIntegrationClientFactory _clientFactory;
    private readonly IMediaMatchingService _matcher;
    private readonly IFailsafeService _failsafe;
    private readonly ILogger<SweepExecutor> _logger;

    public SweepExecutor(
        SweeprrDbContext db,
        IIntegrationClientFactory clientFactory,
        IMediaMatchingService matcher,
        IFailsafeService failsafe,
        ILogger<SweepExecutor> logger)
    {
        _db = db;
        _clientFactory = clientFactory;
        _matcher = matcher;
        _failsafe = failsafe;
        _logger = logger;
    }

    public async Task<ExecuteSweepResult> ExecuteAsync(
        ExecuteSweepRequest request, CancellationToken ct = default)
    {
        var settings = await _db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new GlobalSettings();

        bool isDryRun = settings.GlobalDryRun;

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
                await ProcessRadarrGroupAsync(grp.Items, connId.Value, isDryRun, counters, ct);
            else
                await ProcessSonarrGroupAsync(grp.Items, connId.Value, isDryRun, counters, ct);
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

        return new ExecuteSweepResult(
            counters.Swept, counters.Failed, counters.FailsafeSkipped,
            counters.BytesRecovered, isDryRun);
    }

    // ── Radarr group ─────────────────────────────────────────────────────────

    private async Task ProcessRadarrGroupAsync(
        IReadOnlyList<SweepItem> items, int connId, bool isDryRun,
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
            await ProcessRadarrItemAsync(client, radarrIndex, item, isDryRun, counters, ct);
    }

    private async Task ProcessRadarrItemAsync(
        RadarrClient client, ArrIndex<RadarrMovie> radarrIndex, SweepItem item,
        bool isDryRun, RunCounters counters, CancellationToken ct)
    {
        var identity = BuildIdentity(item);
        var matchResult = _matcher.MatchMovie(identity, radarrIndex);

        if (matchResult is not MatchResult<RadarrMovie>.Matched m)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = matchResult is MatchResult<RadarrMovie>.Unmatched
                ? "Item not found in Radarr — verify provider IDs"
                : "Ambiguous Radarr match — manual review required";
            _logger.LogWarning("Cannot sweep '{Title}': {Reason}", item.Title, item.SkippedReason);
            counters.Failed++;
            await _db.SaveChangesAsync(ct);
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
            SweepAction.UnmonitorOnly => await UnmonitorOnlyRadarrAsync(client, radarrMovieId, item, ct),
            SweepAction.DeleteOnly    => await DeleteOnlyRadarrAsync(client, radarrMovieId, item, ct),
            _                        => await DeleteAndUnmonitorRadarrAsync(client, radarrMovieId, item, ct),
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
            "DeleteOnly on '{Title}': deleting without unmonitoring — Radarr may re-download",
            item.Title);

        var result = await client.DeleteMovieAsync(
            movieId, deleteFiles: true, addExclusion: item.RuleGroup.AddImportExclusion, ct);

        if (result is HttpResult<EmptyResponse>.DefinitiveFailure { StatusCode: 404 }) return true;
        if (result is not HttpResult<EmptyResponse>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(result, "Delete");
            return false;
        }
        return true;
    }

    // ── Sonarr group ─────────────────────────────────────────────────────────

    private async Task ProcessSonarrGroupAsync(
        IReadOnlyList<SweepItem> items, int connId, bool isDryRun,
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
            await ProcessSonarrItemAsync(client, sonarrIndex, item, isDryRun, counters, ct);
    }

    private async Task ProcessSonarrItemAsync(
        SonarrClient client, ArrIndex<SonarrSeries> sonarrIndex, SweepItem item,
        bool isDryRun, RunCounters counters, CancellationToken ct)
    {
        var identity = BuildIdentity(item);
        var matchResult = _matcher.MatchSeries(identity, sonarrIndex);

        if (matchResult is not MatchResult<SonarrSeriesMatch>.Matched m)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = matchResult is MatchResult<SonarrSeriesMatch>.Unmatched
                ? "Item not found in Sonarr — verify provider IDs"
                : "Ambiguous Sonarr match — manual review required";
            _logger.LogWarning("Cannot sweep '{Title}': {Reason}", item.Title, item.SkippedReason);
            counters.Failed++;
            await _db.SaveChangesAsync(ct);
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
            "DeleteOnly on '{Title}': deleting without unmonitoring — Sonarr may re-download",
            item.Title);

        if (seasonNumber.HasValue)
            return await DeleteSeasonFilesAsync(client, seriesId, seasonNumber.Value, item, ct);

        var result = await client.DeleteSeriesAsync(
            seriesId, deleteFiles: true, addExclusion: item.RuleGroup.AddImportExclusion, ct);

        if (result is HttpResult<EmptyResponse>.DefinitiveFailure { StatusCode: 404 }) return true;
        if (result is not HttpResult<EmptyResponse>.Success)
        {
            item.Status = SweepItemStatus.Failed;
            item.SkippedReason = FailureReason(result, "Delete series");
            return false;
        }
        return true;
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

    private async Task<int?> ResolveConnectionIdAsync(
        int? storedConnId, ConnectionType type, CancellationToken ct)
    {
        if (storedConnId.HasValue) return storedConnId.Value;

        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == type && c.IsEnabled, ct);
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
