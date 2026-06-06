using Cronos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Settings;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly SweeprrDbContext _db;

    public SettingsController(SweeprrDbContext db) => _db = db;

    /// <summary>
    /// Retrieves the global application settings.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var s = await _db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new Models.GlobalSettings();
        return Ok(ToDto(s));
    }

    /// <summary>
    /// Partially updates the global application settings.
    /// </summary>
    [HttpPatch]
    [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Patch([FromBody] UpdateSettingsRequest req, CancellationToken ct)
    {
        var s = await _db.GlobalSettings.FirstOrDefaultAsync(ct);
        if (s is null)
            return NotFound(new { error = "GlobalSettings not initialized." });

        if (req.InstanceName is not null)
            s.InstanceName = req.InstanceName;

        if (req.GlobalDryRun.HasValue)
            s.GlobalDryRun = req.GlobalDryRun.Value;

        if (req.DefaultCron is not null)
        {
            if (!IsValidCron(req.DefaultCron))
                return BadRequest(new { error = $"Invalid cron expression: '{req.DefaultCron}'." });
            s.DefaultCron = req.DefaultCron;
        }

        if (req.MaxItemsPerRun.HasValue)
        {
            if (req.MaxItemsPerRun.Value < 0)
                return BadRequest(new { error = "MaxItemsPerRun must be >= 0 (0 = block all)." });
            s.MaxItemsPerRun = req.MaxItemsPerRun.Value;
        }

        if (req.MaxGbPerRun.HasValue)
        {
            if (req.MaxGbPerRun.Value < 0)
                return BadRequest(new { error = "MaxGbPerRun must be >= 0 (0 = block all)." });
            s.MaxGbPerRun = req.MaxGbPerRun.Value;
        }

        if (req.PessimisticSizeGb.HasValue)
        {
            if (req.PessimisticSizeGb.Value < 0)
                return BadRequest(new { error = "PessimisticSizeGb must be >= 0." });
            s.PessimisticSizeGb = req.PessimisticSizeGb.Value;
        }

        if (req.ClearLibraryPercentCap)
            s.LibraryPercentCap = null;
        else if (req.LibraryPercentCap.HasValue)
        {
            if (req.LibraryPercentCap.Value is <= 0 or > 1)
                return BadRequest(new { error = "LibraryPercentCap must be between 0 (exclusive) and 1 (inclusive)." });
            s.LibraryPercentCap = req.LibraryPercentCap.Value;
        }

        if (req.ClearOverBroadMatchPct)
            s.OverBroadMatchPct = null;
        else if (req.OverBroadMatchPct.HasValue)
        {
            if (req.OverBroadMatchPct.Value is <= 0 or > 1)
                return BadRequest(new { error = "OverBroadMatchPct must be between 0 (exclusive) and 1 (inclusive)." });
            s.OverBroadMatchPct = req.OverBroadMatchPct.Value;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(s));
    }

    private static SettingsDto ToDto(Models.GlobalSettings s) => new(
        s.InstanceName,
        s.GlobalDryRun,
        s.DefaultCron,
        s.MaxItemsPerRun,
        s.MaxGbPerRun,
        s.PessimisticSizeGb,
        s.LibraryPercentCap,
        s.OverBroadMatchPct);

    private static bool IsValidCron(string expression)
    {
        try { CronExpression.Parse(expression); return true; }
        catch { return false; }
    }
}
