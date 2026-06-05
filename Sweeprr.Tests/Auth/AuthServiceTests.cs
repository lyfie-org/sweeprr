using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Data;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Auth;

public class AuthServiceTests : IDisposable
{
    private readonly SweeprrDbContext _db;
    private readonly AuthService _auth;
    private readonly string _dbPath;

    public AuthServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_auth_{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _db = new SweeprrDbContext(options);
        _db.Database.Migrate();
        _db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

        // Seed GlobalSettings with a known secret
        _db.GlobalSettings.Add(new GlobalSettings { Id = 1, JwtSecret = GenerateSecret() });
        _db.SaveChanges();

        _auth = new AuthService(_db, NullLogger<AuthService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // ── IsFirstRun ──────────────────────────────────────────────────────────

    [Fact]
    public async Task IsFirstRun_True_When_No_Users()
    {
        Assert.True(await _auth.IsFirstRunAsync());
    }

    [Fact]
    public async Task IsFirstRun_False_After_Setup()
    {
        await _auth.SetupAsync("admin", "Password1!");
        Assert.False(await _auth.IsFirstRunAsync());
    }

    // ── Setup ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Setup_Creates_Admin_And_Returns_Token()
    {
        var result = await _auth.SetupAsync("admin", "Password1!");

        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.Equal("admin", result.Username);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Setup_Throws_When_User_Already_Exists()
    {
        await _auth.SetupAsync("admin", "Password1!");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _auth.SetupAsync("admin2", "Password2!"));
    }

    [Fact]
    public async Task Setup_Stores_Password_As_Hash_Not_Plaintext()
    {
        const string password = "Password1!";
        await _auth.SetupAsync("admin", password);

        var user = await _db.Users.FirstAsync();
        Assert.NotEqual(password, user.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
        // PBKDF2 hashes from ASP.NET Identity start with a known prefix/format
        Assert.True(user.PasswordHash.Length > 50, "Hash appears too short to be PBKDF2");
    }

    // ── Login ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Returns_Token_For_Correct_Credentials()
    {
        await _auth.SetupAsync("admin", "Password1!");

        var result = await _auth.LoginAsync("admin", "Password1!");

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.AccessToken));
        Assert.Equal("admin", result.Username);
    }

    [Fact]
    public async Task Login_Returns_Null_For_Wrong_Password()
    {
        await _auth.SetupAsync("admin", "Password1!");

        var result = await _auth.LoginAsync("admin", "WrongPassword!");

        Assert.Null(result);
    }

    [Fact]
    public async Task Login_Returns_Null_For_Unknown_Username()
    {
        await _auth.SetupAsync("admin", "Password1!");

        var result = await _auth.LoginAsync("ghost", "Password1!");

        Assert.Null(result);
    }

    [Fact]
    public async Task Login_Updates_LastLoginAt()
    {
        await _auth.SetupAsync("admin", "Password1!");

        await _auth.LoginAsync("admin", "Password1!");

        var user = await _db.Users.FirstAsync();
        Assert.NotNull(user.LastLoginAt);
        Assert.True(user.LastLoginAt > DateTime.UtcNow.AddSeconds(-5));
    }

    // ── JWT content ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Token_Contains_Expected_Claims()
    {
        await _auth.SetupAsync("admin", "Password1!");
        var result = await _auth.LoginAsync("admin", "Password1!");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result!.AccessToken);

        Assert.Equal("sweeprr", jwt.Issuer);
        Assert.Equal("admin", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.UniqueName).Value);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
    }

    [Fact]
    public async Task Token_Expires_After_24_Hours()
    {
        var result = await _auth.SetupAsync("admin", "Password1!");

        // Should expire roughly 24 h from now (allow ±2 min tolerance)
        var expectedExpiry = DateTime.UtcNow.AddHours(24);
        Assert.True(Math.Abs((result.ExpiresAt - expectedExpiry).TotalMinutes) < 3);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string GenerateSecret()
    {
        var bytes = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
