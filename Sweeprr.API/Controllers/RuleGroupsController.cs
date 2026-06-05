using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Background;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Rules;
using Sweeprr.API.Models;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/rulegroups")]
public sealed class RuleGroupsController : ControllerBase
{
    private readonly SweeprrDbContext _db;
    private readonly IRuleValidationService _validator;
    private readonly SchedulerHostedService _scheduler;

    public RuleGroupsController(
        SweeprrDbContext db,
        IRuleValidationService validator,
        SchedulerHostedService scheduler)
    {
        _db = db;
        _validator = validator;
        _scheduler = scheduler;
    }

    // ── Query ────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var groups = await _db.RuleGroups
            .AsNoTracking()
            .Include(g => g.Rules)
            .OrderBy(g => g.Name)
            .ToListAsync();

        return Ok(groups.Select(ToResponse));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var group = await FindGroupAsync(id);
        return group is null ? NotFound() : Ok(ToResponse(group));
    }

    // ── Fields metadata (contract for the Rule Builder UI, Story 5.5) ────────

    [HttpGet("fields")]
    public IActionResult GetFieldsMeta()
    {
        var fields = RuleFieldMeta.All.Select(kvp => new FieldDescriptorResponse(
            Field:                kvp.Key.ToString(),
            Label:                ToLabel(kvp.Key),
            PrimaryValueType:     kvp.Value.PrimaryValueType.ToString(),
            ApplicableMediaTypes: kvp.Value.ApplicableMediaTypes.Select(m => m.ToString()).ToList(),
            AllowedComparators:   kvp.Value.AllowedComparators.Select(c => c.ToString()).ToList()
        )).ToList();

        return Ok(new FieldsMetaResponse(fields));
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RuleGroupRequest request)
    {
        var validation = _validator.Validate(request.MediaType, request.Conditions);
        if (!validation.IsValid)
            return UnprocessableEntity(new { errors = validation.Errors });

        string? cron;
        try { cron = NormalizeCron(request.CronOverride); }
        catch (CronValidationException ex)
        { return BadRequest(new { error = ex.Message }); }

        var group = new RuleGroup
        {
            Name        = request.Name.Trim(),
            Description = request.Description?.Trim(),
            MediaType   = request.MediaType,
            IsEnabled   = request.IsEnabled,
            CronOverride = cron,
            Action      = request.Action,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
            Rules       = MapConditions(request.Conditions),
        };

        _db.RuleGroups.Add(group);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = group.Id }, ToResponse(group));
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] RuleGroupRequest request)
    {
        var group = await FindGroupAsync(id);
        if (group is null) return NotFound();

        var validation = _validator.Validate(request.MediaType, request.Conditions);
        if (!validation.IsValid)
            return UnprocessableEntity(new { errors = validation.Errors });

        string? cron;
        try { cron = NormalizeCron(request.CronOverride); }
        catch (CronValidationException ex)
        { return BadRequest(new { error = ex.Message }); }

        group.Name        = request.Name.Trim();
        group.Description = request.Description?.Trim();
        group.MediaType   = request.MediaType;
        group.IsEnabled   = request.IsEnabled;
        group.CronOverride = cron;
        group.Action      = request.Action;
        group.UpdatedAt   = DateTime.UtcNow;

        // Replace all conditions — cascade delete handles orphaned rules
        _db.Rules.RemoveRange(group.Rules);
        group.Rules = MapConditions(request.Conditions);

        await _db.SaveChangesAsync();
        return Ok(ToResponse(group));
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var group = await _db.RuleGroups
            .Include(g => g.SweepItems)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group is null) return NotFound();

        var activeItems = group.SweepItems
            .Count(s => s.Status is SweepItemStatus.Pending or SweepItemStatus.Approved);

        if (activeItems > 0)
            return Conflict(new
            {
                error = $"Cannot delete rule group: {activeItems} item(s) are Pending or Approved in the Sweep Queue. Ignore or sweep them first."
            });

        _db.RuleGroups.Remove(group);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Manual scan trigger ────────────────────────────────────────────────

    [HttpPost("{id:int}/scan")]
    public async Task<IActionResult> Scan(int id, CancellationToken ct)
    {
        var exists = await _db.RuleGroups.AnyAsync(g => g.Id == id, ct);
        if (!exists) return NotFound();

        ScanResult result;
        try
        {
            result = await _scheduler.TriggerScanAsync(id, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already running"))
        {
            return Conflict(new { error = ex.Message });
        }

        return Ok(new
        {
            result.RuleGroupId,
            result.RuleGroupName,
            result.ItemsFlagged,
            DurationMs = (int)result.Duration.TotalMilliseconds,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task<RuleGroup?> FindGroupAsync(int id)
        => _db.RuleGroups
            .AsNoTracking()
            .Include(g => g.Rules)
            .FirstOrDefaultAsync(g => g.Id == id);

    private static ICollection<Rule> MapConditions(IReadOnlyList<RuleConditionDto> conditions)
        => conditions.Select(c => new Rule
        {
            Section         = c.Section,
            LogicalOperator = c.LogicalOperator,
            Field           = c.Field,
            Comparator      = c.Comparator,
            Value           = c.Value?.Trim() ?? string.Empty,
            ValueType       = c.ValueType,
        }).ToList();

    private static RuleGroupResponse ToResponse(RuleGroup g) => new(
        Id:          g.Id,
        Name:        g.Name,
        Description: g.Description,
        MediaType:   g.MediaType,
        IsEnabled:   g.IsEnabled,
        CronOverride: g.CronOverride,
        Action:      g.Action,
        CreatedAt:   g.CreatedAt,
        UpdatedAt:   g.UpdatedAt,
        Conditions:  g.Rules
            .OrderBy(r => r.Section)
            .ThenBy(r => r.Id)
            .Select(r => new RuleConditionResponse(
                r.Id, r.Section, r.LogicalOperator,
                r.Field, r.Comparator, r.Value, r.ValueType))
            .ToList());

    private static string? NormalizeCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron)) return null;
        var trimmed = cron.Trim();
        if (!SchedulerHostedService.IsValidCron(trimmed))
            throw new CronValidationException(trimmed);
        return trimmed;
    }

    private static string ToLabel(RuleField field) => field switch
    {
        RuleField.LastWatched       => "Last Watched",
        RuleField.PlayCount         => "Play Count",
        RuleField.WatchedByAnyUser  => "Watched By Any User",
        RuleField.WatchedByAllUsers => "Watched By All Users",
        RuleField.SeenByUserCount   => "Seen By User Count",
        RuleField.ReleaseDate       => "Release Date",
        RuleField.DateAdded         => "Date Added",
        RuleField.Rating            => "Rating",
        RuleField.Genre             => "Genre",
        RuleField.ResolutionHeight  => "Resolution (Height)",
        RuleField.Monitored         => "Monitored",
        RuleField.Tags              => "Tags",
        RuleField.QualityProfile    => "Quality Profile",
        RuleField.FileSizeGb        => "File Size (GB)",
        _                           => field.ToString(),
    };
}
