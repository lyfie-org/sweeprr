using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations.Jellyfin;

namespace Sweeprr.API.Controllers;

/// <summary>
/// Serves the Jellyfin in-UI client script (Story 10.5) — added via Jellyfin's
/// Dashboard → General → Custom JavaScript to show "Leaving Soon" banners.
/// </summary>
[ApiController]
[Route("api/integrations/jellyfin")]
[AllowAnonymous]
public sealed class JellyfinIntegrationController : ControllerBase
{
    private readonly SweeprrDbContext _db;

    public JellyfinIntegrationController(SweeprrDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns the generated client script with the Sweeprr base URL embedded, so it
    /// can call <c>/api/public/media/{id}/status</c> from Jellyfin's origin.
    /// </summary>
    [HttpGet("client-script.js")]
    [EnableCors("PublicApi")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClientScript(CancellationToken ct)
    {
        var settings = await _db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);

        var baseUrl = string.IsNullOrWhiteSpace(settings?.PublicBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : settings.PublicBaseUrl;

        var script = JellyfinClientScriptGenerator.Generate(baseUrl);

        Response.Headers.CacheControl = "no-store";
        return Content(script, "application/javascript");
    }
}
