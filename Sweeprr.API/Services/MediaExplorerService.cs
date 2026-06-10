using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Media;
using Sweeprr.API.Dtos.Sweep;
using Sweeprr.API.Models;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.API.Services;

public sealed partial class MediaExplorerService : IMediaExplorerService
{
    private readonly SweeprrDbContext _db;
    private readonly IRuleEvaluator _evaluator;
    private readonly ChannelWriter<byte> _syncTrigger;
    private readonly IOverlayRenderingService _overlayService;

    public MediaExplorerService(
        SweeprrDbContext db,
        IRuleEvaluator evaluator,
        Channel<byte> syncChannel,
        IOverlayRenderingService overlayService)
    {
        _db = db;
        _evaluator = evaluator;
        _syncTrigger = syncChannel.Writer;
        _overlayService = overlayService;
    }

    // ── Paged catalog ────────────────────────────────────────────────────────

    public async Task<PagedResponse<MediaItemResponse>> GetPagedAsync(MediaQueryParams query, CancellationToken ct = default)
    {
        var sweepItems = await _db.SweepItems
            .Include(s => s.RuleGroup)
            .AsNoTracking()
            .ToListAsync(ct);

        var grouped = sweepItems.GroupBy(s => s.MediaServerItemId, StringComparer.OrdinalIgnoreCase).ToList();
        var itemIds = grouped.Select(g => g.Key).ToList();

        var playback = await _db.PlaybackActivities
            .AsNoTracking()
            .Where(p => itemIds.Contains(p.MediaServerItemId))
            .ToListAsync(ct);
        var playbackByItem = playback.ToLookup(p => p.MediaServerItemId, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var excludedIds = await _db.Exclusions
            .AsNoTracking()
            .Where(e => itemIds.Contains(e.MediaServerItemId) && (e.ExpiresAt == null || e.ExpiresAt > now))
            .Select(e => e.MediaServerItemId)
            .ToListAsync(ct);
        var excludedSet = excludedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = grouped
            .Select(g => ToMediaItem(g, playbackByItem[g.Key].ToList(), excludedSet.Contains(g.Key)))
            .ToList();

        // ── Filters ──
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            items = items.Where(i => i.Title.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (query.Type.HasValue)
            items = items.Where(i => i.Type == query.Type.Value).ToList();

        if (query.Status.HasValue)
            items = items.Where(i => i.Status == query.Status.Value).ToList();

        // ── Sort ──
        var descending = string.Equals(query.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var sortDir = descending ? -1 : 1;

        Comparison<MediaItemResponse> comparer = (query.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "year"        => (a, b) => Nullable.Compare(a.Year, b.Year) * sortDir,
            "sizegb"      => (a, b) => a.SizeGb.CompareTo(b.SizeGb) * sortDir,
            "lastwatched" => (a, b) => Nullable.Compare(a.LastWatched, b.LastWatched) * sortDir,
            "status"      => (a, b) => string.Compare(a.Status?.ToString() ?? "", b.Status?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) * sortDir,
            _             => (a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase) * sortDir,
        };

        items.Sort(comparer);

        // ── Paginate ──
        var total = items.Count;
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var pageItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResponse<MediaItemResponse>(pageItems, total, page, pageSize);
    }

    private static MediaItemResponse ToMediaItem(IGrouping<string, SweepItem> group, List<PlaybackActivity> playback, bool isExcluded)
    {
        var ordered = group.OrderByDescending(s => s.FlaggedAt).ToList();
        var representative = ordered[0];

        var status = ordered
            .Select(s => s.Status)
            .OrderBy(StatusPriority)
            .First();

        var matchedRuleGroups = ordered
            .Where(s => s.Status == SweepItemStatus.Pending)
            .Select(s => new MatchedRuleGroupDto(s.RuleGroupId, s.RuleGroup.Name, s.MatchedRuleSummary ?? string.Empty))
            .ToList();

        var lastWatched = playback.Count > 0 ? playback.Max(p => (DateTime?)p.LastWatched) : null;
        var watchedByCount = playback.Count(p => p.PlayCount > 0);

        return new MediaItemResponse(
            Id: group.Key,
            Title: representative.Title,
            Year: ExtractYear(representative.Title),
            Type: representative.MediaType,
            SizeGb: representative.SizeBytes.HasValue ? Math.Round(representative.SizeBytes.Value / 1_073_741_824.0, 2) : 0,
            SizeLabel: FormatSizeLabel(representative.SizeBytes),
            LastWatched: lastWatched,
            WatchedByCount: watchedByCount,
            Status: status,
            MatchedRuleGroups: matchedRuleGroups,
            Tags: [],
            IsExcluded: isExcluded);
    }

    /// <summary>Lower number = higher priority when picking the headline status for an item.</summary>
    private static int StatusPriority(SweepItemStatus status) => status switch
    {
        SweepItemStatus.Pending  => 0,
        SweepItemStatus.Approved => 1,
        SweepItemStatus.Failed   => 2,
        SweepItemStatus.Ignored  => 3,
        SweepItemStatus.Swept    => 4,
        _                        => 5,
    };

    [GeneratedRegex(@"\((\d{4})\)\s*$")]
    private static partial Regex YearSuffixRegex();

    private static int? ExtractYear(string title)
    {
        var match = YearSuffixRegex().Match(title);
        return match.Success && int.TryParse(match.Groups[1].Value, out var year) ? year : null;
    }

    private static string FormatSizeLabel(long? sizeBytes)
    {
        if (!sizeBytes.HasValue || sizeBytes.Value <= 0)
            return "—";

        var gb = sizeBytes.Value / 1_073_741_824.0;
        if (gb < 0.1) return "< 0.1 GB";
        return gb < 10 ? $"{gb:0.00} GB" : $"{gb:0.0} GB";
    }

    // ── Rule trace ───────────────────────────────────────────────────────────

    public async Task<RuleTraceResponse?> GetRuleTraceAsync(string itemId, CancellationToken ct = default)
    {
        var sweepItems = await _db.SweepItems
            .AsNoTracking()
            .Where(s => s.MediaServerItemId == itemId)
            .OrderByDescending(s => s.FlaggedAt)
            .ToListAsync(ct);

        if (sweepItems.Count == 0)
            return null;

        var representative = sweepItems[0];

        var playback = await _db.PlaybackActivities
            .AsNoTracking()
            .Where(p => p.MediaServerItemId == itemId)
            .ToListAsync(ct);

        var context = BuildContext(representative, playback);

        var ruleGroups = await _db.RuleGroups
            .Include(g => g.Rules)
            .AsNoTracking()
            .Where(g => g.MediaType == representative.MediaType)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

        var traces = await _evaluator.TraceAsync(context, ruleGroups, ct);

        var evaluations = traces
            .Select(t => new RuleTraceEvaluation(
                t.RuleGroupId,
                t.RuleGroupName,
                t.Matched,
                t.Clauses
                    .Select(c => new ClauseTraceResult(
                        c.Section,
                        c.LogicalOperator,
                        c.Field.ToString(),
                        c.Comparator.ToString(),
                        c.Value,
                        c.Result))
                    .ToList()))
            .ToList();

        return new RuleTraceResponse(itemId, representative.Title, evaluations);
    }

    private static MediaContext BuildContext(SweepItem item, List<PlaybackActivity> playback)
    {
        DateTime? lastWatched = playback.Count > 0 ? playback.Max(p => p.LastWatched) : null;
        int? playCount = playback.Count > 0 ? playback.Sum(p => p.PlayCount) : null;
        bool? watchedByAny = playback.Count > 0 ? playback.Any(p => p.PlayCount > 0) : null;
        bool? watchedByAll = playback.Count > 0 ? playback.All(p => p.PlayCount > 0) : null;
        int? seenByCount = playback.Count > 0 ? playback.Count(p => p.PlayCount > 0) : null;

        return new MediaContext
        {
            ItemId = item.MediaServerItemId,
            Title = item.Title,
            MediaType = item.MediaType,
            LastWatched = lastWatched,
            PlayCount = playCount,
            WatchedByAnyUser = watchedByAny,
            WatchedByAllUsers = watchedByAll,
            SeenByUserCount = seenByCount,
            Genres = item.Genres?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ResolutionHeight = item.ResolutionHeight,
            VideoCodec = item.VideoCodec,
            AudioChannels = item.AudioChannels,
            FileSizeGb = item.SizeBytes.HasValue ? item.SizeBytes.Value / 1_073_741_824m : null,
            ImdbId = item.ImdbId,
            TmdbId = int.TryParse(item.TmdbId, out var tmdbId) ? tmdbId : null,
            TvdbId = int.TryParse(item.TvdbId, out var tvdbId) ? tvdbId : null,
            ArrConnectionId = item.ArrInstanceId,
            SeasonNumber = item.SeasonNumber,
            HasTransientFailure = false,
        };
    }

    // ── Bulk manual queue ────────────────────────────────────────────────────

    public async Task<QueueManualResponse> QueueManualAsync(QueueManualRequest request, CancellationToken ct = default)
    {
        var ids = request.Ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var alreadyQueuedIds = await _db.SweepItems
            .Where(s => ids.Contains(s.MediaServerItemId) && s.Status == SweepItemStatus.Pending)
            .Select(s => s.MediaServerItemId)
            .ToListAsync(ct);
        var alreadyQueuedSet = alreadyQueuedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toQueue = ids.Where(id => !alreadyQueuedSet.Contains(id)).ToList();
        if (toQueue.Count == 0)
            return new QueueManualResponse(0, alreadyQueuedSet.Count);

        var candidates = await _db.SweepItems
            .Where(s => toQueue.Contains(s.MediaServerItemId))
            .OrderByDescending(s => s.FlaggedAt)
            .ToListAsync(ct);

        var templateByItemId = candidates
            .GroupBy(s => s.MediaServerItemId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var newItems = new List<SweepItem>();

        foreach (var id in toQueue)
        {
            if (!templateByItemId.TryGetValue(id, out var template))
                continue; // No prior record for this item — nothing to base a manual entry on.

            var newItem = new SweepItem
            {
                RuleGroupId = template.RuleGroupId,
                MediaServerItemId = template.MediaServerItemId,
                Title = template.Title,
                MediaType = template.MediaType,
                SizeBytes = template.SizeBytes,
                MatchedRuleSummary = "Manually added via Media Explorer",
                Status = SweepItemStatus.Pending,
                FlaggedAt = DateTime.UtcNow,
                TmdbId = template.TmdbId,
                TvdbId = template.TvdbId,
                ImdbId = template.ImdbId,
                ArrInstanceId = template.ArrInstanceId,
                SeasonNumber = template.SeasonNumber,
                Genres = template.Genres,
                ResolutionHeight = template.ResolutionHeight,
                VideoCodec = template.VideoCodec,
                AudioChannels = template.AudioChannels,
            };

            _db.SweepItems.Add(newItem);
            newItems.Add(newItem);
        }

        if (newItems.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _syncTrigger.TryWrite(1);

            foreach (var created in newItems)
                await _overlayService.ApplyOverlayAsync(created, "Leaving Soon", ct);
        }

        return new QueueManualResponse(newItems.Count, alreadyQueuedSet.Count);
    }

    // ── Bulk exclude ─────────────────────────────────────────────────────────

    public async Task<ExcludeBulkResponse> ExcludeBulkAsync(ExcludeBulkRequest request, CancellationToken ct = default)
    {
        var ids = request.Ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var now = DateTime.UtcNow;

        var existingIds = await _db.Exclusions
            .Where(e => ids.Contains(e.MediaServerItemId)
                     && e.RuleGroupId == request.RuleGroupId
                     && (e.ExpiresAt == null || e.ExpiresAt > now))
            .Select(e => e.MediaServerItemId)
            .ToListAsync(ct);
        var existingSet = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Excluded from Media Explorer"
            : request.Reason.Trim();

        var toCreate = ids.Where(id => !existingSet.Contains(id)).ToList();
        foreach (var id in toCreate)
        {
            _db.Exclusions.Add(new Exclusion
            {
                MediaServerItemId = id,
                Reason = reason,
                RuleGroupId = request.RuleGroupId,
                ExpiresAt = request.ExpiresAt,
                CreatedAt = now,
            });
        }

        if (toCreate.Count > 0)
            await _db.SaveChangesAsync(ct);

        // Remove now-excluded Pending items, mirroring SweepQueueService.ReconcileAsync.
        var pendingQuery = _db.SweepItems
            .Where(s => ids.Contains(s.MediaServerItemId) && s.Status == SweepItemStatus.Pending);

        if (request.RuleGroupId.HasValue)
            pendingQuery = pendingQuery.Where(s => s.RuleGroupId == request.RuleGroupId.Value);

        var pendingToRemove = await pendingQuery.ToListAsync(ct);

        if (pendingToRemove.Count > 0)
        {
            _db.SweepItems.RemoveRange(pendingToRemove);
            await _db.SaveChangesAsync(ct);
            _syncTrigger.TryWrite(1);

            foreach (var removed in pendingToRemove)
                await _overlayService.RestoreOriginalAsync(removed, ct);
        }

        return new ExcludeBulkResponse(ids.Count);
    }
}
