using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sweeprr.API.Background;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Public;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.API.Controllers;

/// <summary>
/// Unauthenticated/lightly-authenticated endpoints backing the public "Request Extension"
/// portal (Story 10.4). Reachable from the Jellyfin in-app banner and QR codes without a
/// Sweeprr admin session — never expose admin-scoped data here.
/// </summary>
[ApiController]
[Route("api/public")]
public sealed class PublicController : ControllerBase
{
    private readonly SweeprrDbContext _db;
    private readonly IIntegrationClientFactory _clientFactory;
    private readonly ISweepQueueService _sweepQueue;
    private readonly JwtKeyProvider _keyProvider;
    private readonly SchedulerHostedService _scheduler;

    public PublicController(
        SweeprrDbContext db,
        IIntegrationClientFactory clientFactory,
        ISweepQueueService sweepQueue,
        JwtKeyProvider keyProvider,
        SchedulerHostedService scheduler)
    {
        _db = db;
        _clientFactory = clientFactory;
        _sweepQueue = sweepQueue;
        _keyProvider = keyProvider;
        _scheduler = scheduler;
    }

    /// <summary>
    /// Proxies Jellyfin username/password authentication. On success, issues a
    /// short-lived (1h) "ExtensionPortal"-scoped JWT — the Jellyfin access token
    /// itself is never returned to the client.
    /// </summary>
    [HttpPost("auth/jellyfin")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ExtensionPortalTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AuthenticateJellyfin(
        [FromBody] JellyfinAuthRequest request, CancellationToken ct)
    {
        var client = await ResolveJellyfinClientAsync(ct);
        if (client is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "No Jellyfin connection is configured." });

        var result = await client.AuthenticateByNameAsync(request.Username, request.Password, ct);

        if (result is not HttpResult<JellyfinAuthResult>.Success success)
            return Unauthorized(new { error = "Invalid Jellyfin username or password." });

        return Ok(BuildExtensionToken(success.Value.User));
    }

    /// <summary>
    /// Removes an item from the sweep queue and creates a temporary global exclusion
    /// on behalf of the authenticated Jellyfin user.
    /// </summary>
    [HttpPost("extend")]
    [Authorize(Policy = "ExtensionPortal")]
    [ProducesResponseType(typeof(ExtendResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Extend([FromBody] ExtendRequest request, CancellationToken ct)
    {
        var username = User.FindFirstValue(JwtRegisteredClaimNames.UniqueName) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        var result = await _sweepQueue.ExtendAsync(request.JellyfinItemId, username, request.RequestedDays, ct);

        return result switch
        {
            ExtendResult.Success s => Ok(new ExtendResponse(true, s.Exclusion.ExpiresAt, null)),
            ExtendResult.AbuseLimited => StatusCode(StatusCodes.Status429TooManyRequests,
                new ExtendResponse(false, null, "This item was already extended recently. Please try again later.")),
            ExtendResult.NotQueued => NotFound(
                new ExtendResponse(false, null, "This item is not currently scheduled for removal.")),
            _ => throw new UnreachableException("Exhaustive match failed on ExtendResult.")
        };
    }

    /// <summary>
    /// Anonymous lookup used by the Jellyfin in-UI client script (Story 10.5) and the
    /// `/extend` page to show an item's removal status without authentication.
    /// </summary>
    [HttpGet("media/{jellyfinItemId}/status")]
    [AllowAnonymous]
    [EnableCors("PublicApi")]
    [ProducesResponseType(typeof(MediaStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMediaStatus(string jellyfinItemId, CancellationToken ct)
    {
        var item = await _db.SweepItems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.MediaServerItemId == jellyfinItemId
                && (s.Status == SweepItemStatus.Pending || s.Status == SweepItemStatus.Approved), ct);

        if (item is null)
            return Ok(new MediaStatusResponse(false, null, null, null));

        int? daysRemaining = null;
        var nextRun = _scheduler.GetNextRunForGroup(item.RuleGroupId);
        if (nextRun.HasValue)
        {
            var remaining = nextRun.Value - DateTimeOffset.UtcNow;
            daysRemaining = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
        }

        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Jellyfin && c.IsEnabled, ct);

        var posterUrl = conn is not null
            ? $"{conn.BaseUrl.TrimEnd('/')}/Items/{Uri.EscapeDataString(jellyfinItemId)}/Images/Primary"
            : null;

        return Ok(new MediaStatusResponse(true, daysRemaining, item.Title, posterUrl));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<JellyfinClient?> ResolveJellyfinClientAsync(CancellationToken ct)
    {
        var conn = await _db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Jellyfin && c.IsEnabled, ct);

        if (conn is null) return null;
        return await _clientFactory.CreateJellyfinClientAsync(conn.Id, ct);
    }

    /// <summary>
    /// Issues a 1h JWT for the "ExtensionPortal" scheme — a separate issuer/audience
    /// from the admin JWT so it can never authenticate against admin-gated endpoints.
    /// </summary>
    private ExtensionPortalTokenResponse BuildExtensionToken(JellyfinUser user)
    {
        var creds = new SigningCredentials(_keyProvider.GetKey(), SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddHours(1);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Name),
        };

        var token = new JwtSecurityToken(
            issuer: "sweeprr-extension-portal",
            audience: "sweeprr-extension-portal",
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new ExtensionPortalTokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiry,
            user.Name);
    }
}
