using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sweeprr.API.Dtos.Auth;
using Sweeprr.API.Models;
using Sweeprr.API.Services;
using System.Security.Claims;

namespace Sweeprr.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    /// <summary>
    /// First-run only. Creates the single admin account.
    /// Returns 409 if any user already exists.
    /// </summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Setup([FromBody] SetupRequest request)
    {
        if (!await _auth.IsFirstRunAsync())
            return Conflict(new { error = "Admin account already exists." });

        try
        {
            var token = await _auth.SetupAsync(request.Username, request.Password);
            return Ok(token);
        }
        catch (InvalidOperationException ex)
        {
            // Race: another request won the DB write between IsFirstRun check and SetupAsync.
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Authenticate and receive a JWT access token.
    /// Rate-limited: 10 requests per minute per IP.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var token = await _auth.LoginAsync(request.Username, request.Password);

        if (token is null)
            return Unauthorized(new { error = "Invalid credentials." });

        return Ok(token);
    }

    /// <summary>
    /// Returns the currently authenticated user's profile.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

        if (sub is null || !int.TryParse(sub.Value, out var userId))
            return Unauthorized();

        var username = User.Identity?.Name ?? string.Empty;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;

        return Ok(new MeResponse
        {
            Id = userId,
            Username = username,
            Role = Enum.TryParse<UserRole>(role, out var r) ? r : UserRole.Admin
        });
    }

    /// <summary>
    /// Returns whether this is a first-run instance.
    /// Used by the frontend to choose between Setup and Login screens.
    /// </summary>
    [HttpGet("status")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status()
    {
        var isFirstRun = await _auth.IsFirstRunAsync();
        return Ok(new { isFirstRun });
    }
}
