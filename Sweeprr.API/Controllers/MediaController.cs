using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Media;
using Sweeprr.API.Dtos.Sweep;
using Sweeprr.API.Services;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/media")]
public sealed class MediaController : ControllerBase
{
    private readonly IMediaExplorerService _media;
    private readonly SweeprrDbContext _db;

    public MediaController(IMediaExplorerService media, SweeprrDbContext db)
    {
        _media = media;
        _db = db;
    }

    /// <summary>
    /// Retrieves a paginated, filterable, sortable view of the media library
    /// (one row per item, aggregated across all sweep queue history).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<MediaItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] MediaQueryParams query, CancellationToken ct)
    {
        var result = await _media.GetPagedAsync(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a per-rule-group, per-clause breakdown of how a media item
    /// currently evaluates against every rule group of its media type.
    /// </summary>
    [HttpGet("{id}/ruletrace")]
    [ProducesResponseType(typeof(RuleTraceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRuleTrace(string id, CancellationToken ct)
    {
        var trace = await _media.GetRuleTraceAsync(id, ct);
        return trace is null ? NotFound() : Ok(trace);
    }

    /// <summary>
    /// Adds the given media items to the sweep queue as Pending, regardless of
    /// whether any rule group currently matches them.
    /// </summary>
    [HttpPost("queue-manual")]
    [ProducesResponseType(typeof(QueueManualResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> QueueManual([FromBody] QueueManualRequest request, CancellationToken ct)
    {
        var result = await _media.QueueManualAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Excludes the given media items, either globally or scoped to a rule group,
    /// and removes any matching Pending sweep queue entries.
    /// </summary>
    [HttpPost("exclude-bulk")]
    [ProducesResponseType(typeof(ExcludeBulkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExcludeBulk([FromBody] ExcludeBulkRequest request, CancellationToken ct)
    {
        if (request.RuleGroupId.HasValue)
        {
            var exists = await _db.RuleGroups.AnyAsync(g => g.Id == request.RuleGroupId.Value, ct);
            if (!exists)
                return NotFound(new { error = $"Rule group {request.RuleGroupId} not found." });
        }

        var result = await _media.ExcludeBulkAsync(request, ct);
        return Ok(result);
    }
}
