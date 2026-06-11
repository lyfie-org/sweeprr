using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sweeprr.API.Data;

namespace Sweeprr.API.Auth;

/// <summary>
/// Authenticates requests bearing <c>Authorization: Bearer spr_live_...</c> against
/// <see cref="SweeprrDbContext.SweeprrApiKeys"/> (Story 10.3). Hashes the incoming key,
/// looks it up by <see cref="Models.SweeprrApiKey.HashedKey"/>, and validates
/// <c>IsActive</c> + <c>ExpiresAt</c>. On success, sets a ClaimsPrincipal carrying one
/// "scope" claim per entry in <see cref="Models.SweeprrApiKey.Scopes"/> and updates
/// <c>LastUsedAt</c>.
/// <para>
/// Requests with no Authorization header, or a Bearer token that isn't a Sweeprr API key,
/// return <see cref="AuthenticateResult.NoResult"/> so the JWT scheme can be tried instead.
/// </para>
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private const string KeyPrefix = "spr_live_";

    private readonly SweeprrDbContext _db;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        SweeprrDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.NoResult();

        var header = authHeader.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var rawKey = header["Bearer ".Length..].Trim();
        if (!rawKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var hashedKey = ApiKeyGenerator.Hash(rawKey);
        var key = await _db.SweeprrApiKeys.FirstOrDefaultAsync(k => k.HashedKey == hashedKey);

        if (key is null)
            return AuthenticateResult.Fail("Invalid API key.");

        if (!key.IsActive)
            return AuthenticateResult.Fail("API key has been revoked.");

        if (key.ExpiresAt is { } expiresAt && expiresAt < DateTime.UtcNow)
            return AuthenticateResult.Fail("API key has expired.");

        key.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, key.Id.ToString()),
            new(ClaimTypes.Name, key.Name),
        };

        var scopes = JsonSerializer.Deserialize<string[]>(key.Scopes) ?? [];
        claims.AddRange(scopes.Select(scope => new Claim(ApiKeyClaims.Scope, scope)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
