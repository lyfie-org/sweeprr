using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Connections;
using Sweeprr.API.Integrations;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/connections")]
public class ConnectionsController : ControllerBase
{
    private readonly IConnectionService _connections;
    private readonly IConnectionTestService _tester;
    private readonly IIntegrationClientFactory _clientFactory;
    private readonly SweeprrDbContext _db;

    public ConnectionsController(
        IConnectionService connections,
        IConnectionTestService tester,
        IIntegrationClientFactory clientFactory,
        SweeprrDbContext db)
    {
        _connections = connections;
        _tester = tester;
        _clientFactory = clientFactory;
        _db = db;
    }

    // ── CRUD (Story 1.2) ─────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves all saved server connections.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ConnectionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
        => Ok(await _connections.GetAllAsync());

    /// <summary>
    /// Retrieves a specific connection by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ConnectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var conn = await _connections.GetByIdAsync(id);
        return conn is null ? NotFound() : Ok(conn);
    }

    /// <summary>
    /// Creates a new server connection.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] ConnectionRequest request)
    {
        if (!TryValidateUrl(request.BaseUrl, out var urlError))
            return ValidationProblem(urlError!, "BaseUrl");

        try
        {
            var (conn, warning) = await _connections.CreateAsync(request);
            var result = new { connection = conn, warning };
            return CreatedAtAction(nameof(GetById), new { id = conn.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(ex.Message, "BaseUrl");
        }
    }

    /// <summary>
    /// Updates an existing server connection.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ConnectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(int id, [FromBody] ConnectionRequest request)
    {
        if (!TryValidateUrl(request.BaseUrl, out var urlError))
            return ValidationProblem(urlError!, "BaseUrl");

        try
        {
            var conn = await _connections.UpdateAsync(id, request);
            return conn is null ? NotFound() : Ok(conn);
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(ex.Message, "BaseUrl");
        }
    }

    /// <summary>
    /// Deletes a server connection by ID.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _connections.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    // ── Test endpoints (Story 1.3) ───────────────────────────────────────────

    /// <summary>
    /// Tests a saved connection by ID.
    /// Decrypts the stored key, performs a live handshake, and persists the result.
    /// </summary>
    [HttpPost("{id:int}/test")]
    [ProducesResponseType(typeof(ConnectionTestResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestSaved(int id)
    {
        var result = await _tester.TestSavedAsync(id);
        return Ok(result);
    }

    /// <summary>
    /// Tests credentials inline without saving.
    /// Allows verifying a connection before committing it to the database.
    /// </summary>
    [HttpPost("test-unsaved")]
    [ProducesResponseType(typeof(ConnectionTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TestUnsaved([FromBody] ConnectionTestRequest request)
    {
        if (!TryValidateUrl(request.BaseUrl, out var urlError))
            return ValidationProblem(urlError!, "BaseUrl");

        var result = await _tester.TestUnsavedAsync(
            request.Type, request.BaseUrl, request.ApiKey, request.AllowInsecure);

        return Ok(result);
    }

    // ── Quality profiles proxy (used by Rule Group editor for ChangeQualityProfile action) ──

    /// <summary>
    /// Returns available quality profiles from a Radarr or Sonarr connection.
    /// Used by the Rule Group editor to populate the target-profile dropdown.
    /// </summary>
    [HttpGet("{id:int}/qualityprofiles")]
    [ProducesResponseType(typeof(IEnumerable<QualityProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetQualityProfiles(int id, CancellationToken ct)
    {
        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (conn is null)
            return NotFound(new { error = $"Connection {id} not found." });

        if (conn.Type == ConnectionType.Jellyfin)
            return BadRequest(new { error = "Quality profiles are only available for Radarr or Sonarr connections." });

        if (conn.Type == ConnectionType.Radarr)
        {
            var client = await _clientFactory.CreateRadarrClientAsync(id, ct);
            if (client is null)
                return StatusCode(502, new { error = "Could not connect to Radarr — check connection settings." });

            var result = await client.GetQualityProfilesAsync(ct);
            if (result is not Integrations.HttpResult<System.Collections.Generic.IReadOnlyList<Integrations.Radarr.Models.RadarrQualityProfile>>.Success ok)
                return StatusCode(502, new { error = "Failed to fetch quality profiles from Radarr." });

            return Ok(ok.Value.Select(p => new QualityProfileResponse(p.Id, p.Name)));
        }
        else // Sonarr
        {
            var client = await _clientFactory.CreateSonarrClientAsync(id, ct);
            if (client is null)
                return StatusCode(502, new { error = "Could not connect to Sonarr — check connection settings." });

            var result = await client.GetQualityProfilesAsync(ct);
            if (result is not Integrations.HttpResult<System.Collections.Generic.IReadOnlyList<Integrations.Sonarr.Models.SonarrQualityProfile>>.Success ok)
                return StatusCode(502, new { error = "Failed to fetch quality profiles from Sonarr." });

            return Ok(ok.Value.Select(p => new QualityProfileResponse(p.Id, p.Name)));
        }
    }

    // ── Tags proxy (used by Exclusions UI for tag-based whitelisting) ────────

    /// <summary>
    /// Returns all tags defined in a Radarr or Sonarr connection.
    /// Used by the Exclusions page to populate the tag dropdown.
    /// </summary>
    [HttpGet("{id:int}/tags")]
    [ProducesResponseType(typeof(IEnumerable<TagResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetTags(int id, CancellationToken ct)
    {
        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (conn is null)
            return NotFound(new { error = $"Connection {id} not found." });

        if (conn.Type == ConnectionType.Jellyfin)
            return BadRequest(new { error = "Tags are only available for Radarr or Sonarr connections." });

        if (conn.Type == ConnectionType.Radarr)
        {
            var client = await _clientFactory.CreateRadarrClientAsync(id, ct);
            if (client is null)
                return StatusCode(502, new { error = "Could not connect to Radarr — check connection settings." });

            var result = await client.GetTagsAsync(ct);
            if (result is not Integrations.HttpResult<System.Collections.Generic.IReadOnlyList<Integrations.Radarr.Models.RadarrTag>>.Success ok)
                return StatusCode(502, new { error = "Failed to fetch tags from Radarr." });

            return Ok(ok.Value.Select(t => new TagResponse(t.Id, t.Label)));
        }
        else // Sonarr
        {
            var client = await _clientFactory.CreateSonarrClientAsync(id, ct);
            if (client is null)
                return StatusCode(502, new { error = "Could not connect to Sonarr — check connection settings." });

            var result = await client.GetTagsAsync(ct);
            if (result is not Integrations.HttpResult<System.Collections.Generic.IReadOnlyList<Integrations.Sonarr.Models.SonarrTag>>.Success ok)
                return StatusCode(502, new { error = "Failed to fetch tags from Sonarr." });

            return Ok(ok.Value.Select(t => new TagResponse(t.Id, t.Label)));
        }
    }

    // ── Disk space proxy (used by Rule Builder helper text for disk-space fields) ──

    /// <summary>
    /// Returns current disk-space stats from a Radarr or Sonarr connection.
    /// Used by the rule builder to display helper text (e.g. "Current: 21% free").
    /// </summary>
    [HttpGet("{id:int}/diskspace")]
    [ProducesResponseType(typeof(DiskSpaceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetDiskSpace(int id, CancellationToken ct)
    {
        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (conn is null)
            return NotFound(new { error = $"Connection {id} not found." });

        if (conn.Type == ConnectionType.Jellyfin)
            return BadRequest(new { error = "Disk space is only available for Radarr or Sonarr connections." });

        if (conn.Type == ConnectionType.Radarr)
        {
            var client = await _clientFactory.CreateRadarrClientAsync(id, ct);
            if (client is null)
                return StatusCode(502, new { error = "Could not connect to Radarr — check connection settings." });

            var result = await client.GetDiskSpaceAsync(ct);
            if (result is not Integrations.HttpResult<(double FreePercent, double FreeGb)>.Success ok)
                return StatusCode(502, new { error = "Failed to fetch disk space from Radarr." });

            // Re-fetch total from client to build the response (sum of all paths)
            var (freePercent, freeGb) = ok.Value;
            var totalGb = freePercent > 0 ? freeGb / (freePercent / 100.0) : 0.0;
            return Ok(new DiskSpaceResponse(
                FreeSpaceGb: Math.Round(freeGb, 1),
                TotalSpaceGb: Math.Round(totalGb, 1),
                FreePercent: Math.Round(freePercent, 1)));
        }
        else // Sonarr
        {
            var client = await _clientFactory.CreateSonarrClientAsync(id, ct);
            if (client is null)
                return StatusCode(502, new { error = "Could not connect to Sonarr — check connection settings." });

            var result = await client.GetDiskSpaceAsync(ct);
            if (result is not Integrations.HttpResult<(double FreePercent, double FreeGb)>.Success ok)
                return StatusCode(502, new { error = "Failed to fetch disk space from Sonarr." });

            var (freePercent, freeGb) = ok.Value;
            var totalGb = freePercent > 0 ? freeGb / (freePercent / 100.0) : 0.0;
            return Ok(new DiskSpaceResponse(
                FreeSpaceGb: Math.Round(freeGb, 1),
                TotalSpaceGb: Math.Round(totalGb, 1),
                FreePercent: Math.Round(freePercent, 1)));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool TryValidateUrl(string raw, out string? error)
    {
        var trimmed = raw.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            uri.Scheme is not "http" and not "https")
        {
            error = "BaseUrl must be a valid http:// or https:// URL.";
            return false;
        }
        error = null;
        return true;
    }

    private IActionResult ValidationProblem(string message, string field)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
