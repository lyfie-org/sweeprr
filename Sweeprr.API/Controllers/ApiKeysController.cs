using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Auth;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.ApiKeys;
using Sweeprr.API.Models;

namespace Sweeprr.API.Controllers;

/// <summary>Sweeprr API key management (Story 10.3). Admin-only — see ScopeAuthorizationHandler.</summary>
[ApiController]
[Route("api/settings/keys")]
[Authorize(Policy = "AdminOnly")]
public sealed class ApiKeysController : ControllerBase
{
    private readonly SweeprrDbContext _db;

    public ApiKeysController(SweeprrDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists all API keys. Keys are always masked — the raw value is never stored.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ApiKeyResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var keys = await _db.SweeprrApiKeys
            .AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        return Ok(keys.Select(ToResponse));
    }

    /// <summary>Generates a new API key. The raw key is returned once and never shown again.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(GenerateApiKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate([FromBody] GenerateApiKeyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        var scopes = (request.Scopes ?? []).Distinct().ToList();
        if (scopes.Count == 0 || scopes.Except(ApiKeyScopes.All).Any())
            return BadRequest(new { error = $"Scopes must be a non-empty subset of: {string.Join(", ", ApiKeyScopes.All)}" });

        var rawKey = ApiKeyGenerator.Generate();
        var entity = new SweeprrApiKey
        {
            Name = request.Name.Trim(),
            HashedKey = ApiKeyGenerator.Hash(rawKey),
            MaskedKey = ApiKeyGenerator.Mask(rawKey),
            CreatedBy = User.Identity?.Name ?? "unknown",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = request.ExpiresAt,
            Scopes = JsonSerializer.Serialize(scopes),
            IsActive = true,
        };

        _db.SweeprrApiKeys.Add(entity);
        await _db.SaveChangesAsync(ct);

        var response = new GenerateApiKeyResponse(
            entity.Id,
            entity.Name,
            rawKey,
            entity.MaskedKey,
            scopes,
            entity.ExpiresAt,
            "Store this key securely. It will not be shown again.");

        return CreatedAtAction(nameof(GetAll), response);
    }

    /// <summary>Revokes an API key (sets IsActive = false). Does not delete the row.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct)
    {
        var entity = await _db.SweeprrApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (entity is null)
            return NotFound();

        entity.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ApiKeyResponse ToResponse(SweeprrApiKey k) => new(
        k.Id,
        k.Name,
        k.MaskedKey,
        k.CreatedBy,
        k.CreatedAt,
        k.ExpiresAt,
        k.LastUsedAt,
        JsonSerializer.Deserialize<string[]>(k.Scopes) ?? [],
        k.IsActive);
}
