using Microsoft.AspNetCore.Mvc;
using Sweeprr.API.Dtos.Connections;
using Sweeprr.API.Services;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/connections")]
public class ConnectionsController : ControllerBase
{
    private readonly IConnectionService _connections;
    private readonly IConnectionTestService _tester;

    public ConnectionsController(IConnectionService connections, IConnectionTestService tester)
    {
        _connections = connections;
        _tester = tester;
    }

    // ── CRUD (Story 1.2) ─────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _connections.GetAllAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var conn = await _connections.GetByIdAsync(id);
        return conn is null ? NotFound() : Ok(conn);
    }

    [HttpPost]
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

    [HttpPut("{id:int}")]
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

    [HttpDelete("{id:int}")]
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
    public async Task<IActionResult> TestUnsaved([FromBody] ConnectionTestRequest request)
    {
        if (!TryValidateUrl(request.BaseUrl, out var urlError))
            return ValidationProblem(urlError!, "BaseUrl");

        var result = await _tester.TestUnsavedAsync(
            request.Type, request.BaseUrl, request.ApiKey, request.AllowInsecure);

        return Ok(result);
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
