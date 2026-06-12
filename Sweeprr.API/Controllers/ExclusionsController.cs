using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Exclusions;
using Sweeprr.API.Models;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/exclusions")]
public class ExclusionsController : ControllerBase
{
    private readonly SweeprrDbContext _db;

    public ExclusionsController(SweeprrDbContext db)
    {
        _db = db;
    }

    // ── Media exclusions ─────────────────────────────────────────────────────

    /// <summary>Lists all media exclusions (global and scoped).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ExclusionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMediaExclusions(CancellationToken ct)
    {
        var items = await _db.Exclusions
            .Include(e => e.RuleGroup)
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new ExclusionResponse(
                e.Id,
                e.MediaServerItemId,
                e.Reason,
                e.CreatedAt,
                e.RuleGroupId,
                e.RuleGroup != null ? e.RuleGroup.Name : null,
                e.ExpiresAt,
                e.CreatedBy))
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>Deletes a media exclusion by ID.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMediaExclusion(int id, CancellationToken ct)
    {
        var entity = await _db.Exclusions.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
            return NotFound();

        _db.Exclusions.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Tag exclusions ───────────────────────────────────────────────────────

    /// <summary>Lists all tag exclusions.</summary>
    [HttpGet("tags")]
    [ProducesResponseType(typeof(IEnumerable<TagExclusionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTagExclusions(CancellationToken ct)
    {
        var items = await _db.TagExclusions
            .Include(t => t.ServerConnection)
            .Include(t => t.RuleGroup)
            .AsNoTracking()
            .OrderBy(t => t.TagName)
            .Select(t => new TagExclusionResponse(
                t.Id,
                t.TagName,
                t.TagId,
                t.ServerConnectionId,
                t.ServerConnection.Name,
                t.RuleGroupId,
                t.RuleGroup != null ? t.RuleGroup.Name : null))
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>Creates a new tag exclusion.</summary>
    [HttpPost("tags")]
    [ProducesResponseType(typeof(TagExclusionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateTagExclusion(
        [FromBody] TagExclusionRequest request, CancellationToken ct)
    {
        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ServerConnectionId, ct);

        if (conn is null)
            return NotFound(new { error = $"Connection {request.ServerConnectionId} not found." });

        if (request.RuleGroupId.HasValue)
        {
            var ruleGroupExists = await _db.RuleGroups
                .AnyAsync(rg => rg.Id == request.RuleGroupId.Value, ct);
            if (!ruleGroupExists)
                return NotFound(new { error = $"Rule group {request.RuleGroupId} not found." });
        }

        var entity = new TagExclusion
        {
            TagName = request.TagName.Trim(),
            TagId = request.TagId,
            ServerConnectionId = request.ServerConnectionId,
            RuleGroupId = request.RuleGroupId,
        };

        _db.TagExclusions.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Reload with navigation properties for the response
        await _db.Entry(entity).Reference(e => e.ServerConnection).LoadAsync(ct);
        if (entity.RuleGroupId.HasValue)
            await _db.Entry(entity).Reference(e => e.RuleGroup).LoadAsync(ct);

        var response = new TagExclusionResponse(
            entity.Id,
            entity.TagName,
            entity.TagId,
            entity.ServerConnectionId,
            entity.ServerConnection.Name,
            entity.RuleGroupId,
            entity.RuleGroup?.Name);

        return CreatedAtAction(nameof(GetTagExclusions), response);
    }

    /// <summary>Deletes a tag exclusion by ID.</summary>
    [HttpDelete("tags/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTagExclusion(int id, CancellationToken ct)
    {
        var entity = await _db.TagExclusions.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null)
            return NotFound();

        _db.TagExclusions.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
