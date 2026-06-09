using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/playback")]
public sealed class PlaybackController : ControllerBase
{
    private readonly SweeprrDbContext _db;

    public PlaybackController(SweeprrDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns playback activity records for a given media item across all users.
    /// </summary>
    [HttpGet("activity/{mediaServerItemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivity(string mediaServerItemId, CancellationToken ct)
    {
        var records = await _db.PlaybackActivities
            .AsNoTracking()
            .Where(p => p.MediaServerItemId == mediaServerItemId)
            .OrderByDescending(p => p.LastWatched)
            .Select(p => new
            {
                userId         = p.UserId,
                username       = p.Username,
                playCount      = p.PlayCount,
                lastWatched    = p.LastWatched,
                isFinished     = p.IsFinished,
                progressPercent = p.ProgressPercent,
            })
            .ToListAsync(ct);

        return Ok(records);
    }
}
