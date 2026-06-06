using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Background;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Rules;
using Sweeprr.API.Integrations;
using Sweeprr.API.Models;
using Sweeprr.API.Services;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/rulegroups")]
public sealed class RuleGroupsController : ControllerBase
{
    private readonly SweeprrDbContext _db;
    private readonly IRuleValidationService _validator;
    private readonly SchedulerHostedService _scheduler;
    private readonly IIntegrationClientFactory _clientFactory;
    private readonly IMediaPopulationService _populationService;
    private readonly IRuleEvaluator _evaluator;

    public RuleGroupsController(
        SweeprrDbContext db,
        IRuleValidationService validator,
        SchedulerHostedService scheduler,
        IIntegrationClientFactory clientFactory,
        IMediaPopulationService populationService,
        IRuleEvaluator evaluator)
    {
        _db = db;
        _validator = validator;
        _scheduler = scheduler;
        _clientFactory = clientFactory;
        _populationService = populationService;
        _evaluator = evaluator;
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

    // ── Tags proxy (for Tag multiselect in the Rule Builder UI) ─────────────

    /// <summary>
    /// Fetches tags from a Radarr or Sonarr connection and returns them as
    /// simple { id, label } pairs for the Tag field multiselect in the Rule Builder.
    /// </summary>
    [HttpGet("tags")]
    public async Task<IActionResult> GetTags([FromQuery] int connectionId, CancellationToken ct)
    {
        // Resolve connection type from DB first so we surface a helpful 400 if it is wrong
        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (conn is null)
            return NotFound(new { error = $"Connection {connectionId} not found." });

        if (conn.Type == ConnectionType.Jellyfin)
            return BadRequest(new { error = "Tags are only available for Radarr or Sonarr connections." });

        if (conn.Type == ConnectionType.Radarr)
        {
            var client = await _clientFactory.CreateRadarrClientAsync(connectionId, ct);
            if (client is null)
                return StatusCode(502, new { error = "Could not connect to Radarr — check connection settings." });

            var result = await client.GetTagsAsync(ct);
            if (result is not Integrations.HttpResult<System.Collections.Generic.IReadOnlyList<Integrations.Radarr.Models.RadarrTag>>.Success ok)
                return StatusCode(502, new { error = "Failed to fetch tags from Radarr." });

            return Ok(new TagsResponse(ok.Value.Select(t => new TagDto(t.Id, t.Label)).ToList()));
        }
        else // Sonarr
        {
            var client = await _clientFactory.CreateSonarrClientAsync(connectionId, ct);
            if (client is null)
                return StatusCode(502, new { error = "Could not connect to Sonarr — check connection settings." });

            var result = await client.GetTagsAsync(ct);
            if (result is not Integrations.HttpResult<System.Collections.Generic.IReadOnlyList<Integrations.Sonarr.Models.SonarrTag>>.Success okS)
                return StatusCode(502, new { error = "Failed to fetch tags from Sonarr." });

            return Ok(new TagsResponse(okS.Value.Select(t => new TagDto(t.Id, t.Label)).ToList()));
        }
    }

    // ── Preview (live match-count for Rule Builder chip) ───────────────────────

    /// <summary>
    /// Runs rule evaluation against the current scan data without persisting anything.
    /// Returns a match count and up to 5 sample titles for the "Would match N" chip.
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest request, CancellationToken ct)
    {
        var validation = _validator.Validate(request.MediaType, request.Conditions);
        if (!validation.IsValid)
            return UnprocessableEntity(new { errors = validation.Errors });

        // Build a transient (unsaved) RuleGroup to drive the evaluator
        var transientGroup = new RuleGroup
        {
            Id          = 0,
            Name        = "__preview__",
            MediaType   = request.MediaType,
            IsEnabled   = true,
            Action      = SweepAction.DeleteAndUnmonitor,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
            Rules       = MapConditions(request.Conditions),
        };

        IReadOnlyList<MediaContext> items;
        try
        {
            items = await _populationService.PopulateAsync(transientGroup, ct);
        }
        catch (Exception ex)
        {
            // If population fails (e.g. no connections configured), return 0 with a note
            return Ok(new PreviewResponse(
                MatchCount: 0,
                SampleTitles: [],
                Note: $"Could not populate media data: {ex.Message}"));
        }

        if (items.Count == 0)
        {
            return Ok(new PreviewResponse(
                MatchCount: 0,
                SampleTitles: [],
                Note: "No scan data available yet — run a scan first."));
        }

        var results = await _evaluator.EvaluateAsync(transientGroup, items, ct);
        var matched = results.Where(r => r.IsMatch).ToList();

        return Ok(new PreviewResponse(
            MatchCount: matched.Count,
            SampleTitles: matched.Take(5).Select(r => r.Item.Title).ToList(),
            Note: null));
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
