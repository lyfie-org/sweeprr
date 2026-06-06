using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Auth;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

public class AuthService : IAuthService
{
    private readonly SweeprrDbContext _db;
    private readonly PasswordHasher<User> _hasher = new();
    private readonly ILogger<AuthService> _logger;

    private const int TokenLifetimeMinutes = 60 * 24; // 24 h

    public AuthService(SweeprrDbContext db, ILogger<AuthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> IsFirstRunAsync()
        => !await _db.Users.AnyAsync();

    public async Task<AuthTokenResponse> SetupAsync(string username, string password)
    {
        // Check inside a serialized path — unique index on Username is the final guard.
        // We throw here so the controller can return 409 before hitting the DB constraint.
        if (await _db.Users.AnyAsync())
            throw new InvalidOperationException("Admin account already exists.");

        var user = new User
        {
            Username = username,
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _hasher.HashPassword(user, password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Admin account created for {Username}.", username);

        var settings = await _db.GlobalSettings.FirstOrDefaultAsync(x => x.Id == 1) ?? throw new InvalidOperationException("GlobalSettings not initialized.");
        return BuildToken(user, settings.JwtSecret);
    }

    public async Task<AuthTokenResponse?> LoginAsync(string username, string password)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user is null)
            return null;

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var settings = await _db.GlobalSettings.FirstOrDefaultAsync(x => x.Id == 1) ?? throw new InvalidOperationException("GlobalSettings not initialized.");
        return BuildToken(user, settings.JwtSecret);
    }

    private static AuthTokenResponse BuildToken(User user, string jwtSecret)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddMinutes(TokenLifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: "sweeprr",
            audience: "sweeprr",
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new AuthTokenResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiry,
            Username = user.Username
        };
    }
}
