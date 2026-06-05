using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Sweep;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

public sealed class SweepQueueService : ISweepQueueService
{
    private readonly SweeprrDbContext _db;

    public SweepQueueService(SweeprrDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResponse<SweepItemResponse>> QueryAsync(
        SweepQueryParams query, CancellationToken ct = default)
    {
        var q = _db.SweepItems
            .Include(s => s.RuleGroup)
            .AsNoTracking()
            .AsQueryable();

        if (query.Status.HasValue)
            q = q.Where(s => s.Status == query.Status.Value);

        if (query.RuleGroupId.HasValue)
            q = q.Where(s => s.RuleGroupId == query.RuleGroupId.Value);

        var total = await q.CountAsync(ct);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var items = await q
            .OrderByDescending(s => s.FlaggedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => ToResponse(s))
            .ToListAsync(ct);

        return new PagedResponse<SweepItemResponse>(items, total, page, pageSize);
    }

    public async Task<SweepItemResponse?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var item = await _db.SweepItems
            .Include(s => s.RuleGroup)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        return item is null ? null : ToResponse(item);
    }

    public async Task<SweepSummaryResponse> GetSummaryAsync(CancellationToken ct = default)
    {
        var pending = await _db.SweepItems
            .Where(s => s.Status == SweepItemStatus.Pending)
            .Select(s => new { s.SizeBytes })
            .ToListAsync(ct);

        var approved = await _db.SweepItems
            .Where(s => s.Status == SweepItemStatus.Approved)
            .Select(s => new { s.SizeBytes })
            .ToListAsync(ct);

        return new SweepSummaryResponse(
            PendingCount: pending.Count,
            ApprovedCount: approved.Count,
            PendingBytes: pending.Sum(s => s.SizeBytes ?? 0),
            ApprovedBytes: approved.Sum(s => s.SizeBytes ?? 0));
    }

    public async Task<SweepItemResponse?> ApproveAsync(int id, CancellationToken ct = default)
    {
        var item = await _db.SweepItems
            .Include(s => s.RuleGroup)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (item is null) return null;

        if (item.Status != SweepItemStatus.Pending)
            return ToResponse(item);

        item.Status = SweepItemStatus.Approved;
        await _db.SaveChangesAsync(ct);

        return ToResponse(item);
    }

    public async Task<SweepItemResponse?> IgnoreAsync(
        int id, bool createExclusion, CancellationToken ct = default)
    {
        var item = await _db.SweepItems
            .Include(s => s.RuleGroup)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (item is null) return null;

        if (item.Status != SweepItemStatus.Pending)
            return ToResponse(item);

        item.Status = SweepItemStatus.Ignored;

        if (createExclusion)
        {
            var alreadyExcluded = await _db.Exclusions
                .AnyAsync(e => e.MediaServerItemId == item.MediaServerItemId, ct);

            if (!alreadyExcluded)
            {
                _db.Exclusions.Add(new Exclusion
                {
                    MediaServerItemId = item.MediaServerItemId,
                    Reason = $"Ignored from sweep queue (rule group: {item.RuleGroup.Name})",
                    CreatedAt = DateTime.UtcNow,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return ToResponse(item);
    }

    public async Task<int> ReconcileAsync(
        int ruleGroupId,
        IReadOnlyList<EvaluationResult> results,
        CancellationToken ct = default)
    {
        var matchedItems = results.Where(r => r.IsMatch).ToList();
        var matchedItemIds = matchedItems
            .Select(r => r.Item.ItemId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Load existing Pending items for this group
        var existingPending = await _db.SweepItems
            .Where(s => s.RuleGroupId == ruleGroupId && s.Status == SweepItemStatus.Pending)
            .ToListAsync(ct);

        var existingByItemId = existingPending
            .ToDictionary(s => s.MediaServerItemId, StringComparer.OrdinalIgnoreCase);

        // Load excluded item IDs to skip them
        var excludedItemIds = await _db.Exclusions
            .Select(e => e.MediaServerItemId)
            .ToListAsync(ct);
        var excludedSet = excludedItemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var upsertCount = 0;

        foreach (var eval in matchedItems)
        {
            // Skip permanently excluded items
            if (excludedSet.Contains(eval.Item.ItemId))
                continue;

            if (existingByItemId.TryGetValue(eval.Item.ItemId, out var existing))
            {
                // Update existing Pending item
                existing.Title = eval.Item.Title;
                existing.MatchedRuleSummary = eval.MatchedRuleSummary;
                existing.SizeBytes = eval.Item.FileSizeGb.HasValue
                    ? (long)(eval.Item.FileSizeGb.Value * 1_073_741_824m)
                    : null;
                existing.FlaggedAt = DateTime.UtcNow;
                existing.TmdbId = eval.Item.TmdbId?.ToString();
                existing.TvdbId = eval.Item.TvdbId?.ToString();
                existing.ImdbId = eval.Item.ImdbId;
                existing.ArrInstanceId = eval.Item.ArrConnectionId;
                existing.SeasonNumber = eval.Item.SeasonNumber;
                upsertCount++;
            }
            else
            {
                // Create new Pending item
                _db.SweepItems.Add(new SweepItem
                {
                    RuleGroupId = ruleGroupId,
                    MediaServerItemId = eval.Item.ItemId,
                    Title = eval.Item.Title,
                    MediaType = eval.Item.MediaType,
                    SizeBytes = eval.Item.FileSizeGb.HasValue
                        ? (long)(eval.Item.FileSizeGb.Value * 1_073_741_824m)
                        : null,
                    MatchedRuleSummary = eval.MatchedRuleSummary,
                    Status = SweepItemStatus.Pending,
                    FlaggedAt = DateTime.UtcNow,
                    TmdbId = eval.Item.TmdbId?.ToString(),
                    TvdbId = eval.Item.TvdbId?.ToString(),
                    ImdbId = eval.Item.ImdbId,
                    ArrInstanceId = eval.Item.ArrConnectionId,
                    SeasonNumber = eval.Item.SeasonNumber,
                });
                upsertCount++;
            }
        }

        // Remove stale Pending items (no longer matched by the current scan)
        var staleItems = existingPending
            .Where(s => !matchedItemIds.Contains(s.MediaServerItemId))
            .ToList();

        if (staleItems.Count > 0)
            _db.SweepItems.RemoveRange(staleItems);

        await _db.SaveChangesAsync(ct);
        return upsertCount;
    }

    public async Task<SweepItemResponse?> SkipAsync(
        int id, string? reason, CancellationToken ct = default)
    {
        var item = await _db.SweepItems
            .Include(s => s.RuleGroup)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (item is null) return null;

        item.SkippedReason = string.IsNullOrWhiteSpace(reason) ? "Manually skipped" : reason;
        // Reset to Pending so the item re-appears for the next run
        item.Status = SweepItemStatus.Pending;

        await _db.SaveChangesAsync(ct);
        return ToResponse(item);
    }

    private static SweepItemResponse ToResponse(SweepItem s) => new(
        s.Id,
        s.RuleGroupId,
        s.RuleGroup.Name,
        s.MediaServerItemId,
        s.Title,
        s.MediaType,
        s.SizeBytes,
        s.MatchedRuleSummary,
        s.Status,
        s.ArrInstanceId,
        s.TmdbId,
        s.TvdbId,
        s.ImdbId,
        s.FlaggedAt,
        s.SweptAt,
        s.SkippedReason);
}
