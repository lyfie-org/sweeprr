using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Auth;
using Sweeprr.API.Controllers;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.ApiKeys;

namespace Sweeprr.Tests.Controllers;

public class ApiKeysControllerTests : IDisposable
{
    private readonly SweeprrDbContext _db;
    private readonly string _dbPath;
    private readonly ApiKeysController _controller;

    public ApiKeysControllerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_apikeys_{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _db = new SweeprrDbContext(options);
        _db.Database.Migrate();
        _db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

        _controller = new ApiKeysController(_db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Name, "admin")], "Test"))
                }
            }
        };
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoKeys()
    {
        var result = await _controller.GetAll(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var keys = Assert.IsAssignableFrom<IEnumerable<ApiKeyResponse>>(ok.Value);
        Assert.Empty(keys);
    }

    [Fact]
    public async Task Generate_CreatesKey_AndReturnsRawKeyOnce()
    {
        var request = new GenerateApiKeyRequest("CI Bot", [ApiKeyScopes.ReadSweep], null);

        var result = await _controller.Generate(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<GenerateApiKeyResponse>(created.Value);

        Assert.Equal("CI Bot", response.Name);
        Assert.StartsWith("spr_live_", response.RawKey);
        Assert.Equal([ApiKeyScopes.ReadSweep], response.Scopes);

        var entity = await _db.SweeprrApiKeys.AsNoTracking().FirstAsync();
        Assert.Equal("admin", entity.CreatedBy);
    }

    // T1: the database stores only the SHA-256 hash — the raw key is never persisted.
    [Fact]
    public async Task Generate_StoresOnlyHashedKey_NotRawKey()
    {
        var request = new GenerateApiKeyRequest("CI Bot", [ApiKeyScopes.ReadSweep], null);

        var result = await _controller.Generate(request, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<GenerateApiKeyResponse>(created.Value);

        var entity = await _db.SweeprrApiKeys.AsNoTracking().FirstAsync();

        Assert.Equal(ApiKeyGenerator.Hash(response.RawKey), entity.HashedKey);
        Assert.NotEqual(response.RawKey, entity.HashedKey);
        Assert.Equal(64, entity.HashedKey.Length); // SHA-256 hex digest
        Assert.Matches("^[0-9a-f]{64}$", entity.HashedKey);
        Assert.Equal(response.MaskedKey, entity.MaskedKey);
    }

    [Fact]
    public async Task Generate_RejectsEmptyName()
    {
        var request = new GenerateApiKeyRequest("   ", [ApiKeyScopes.ReadSweep], null);

        var result = await _controller.Generate(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await _db.SweeprrApiKeys.ToListAsync());
    }

    [Fact]
    public async Task Generate_RejectsEmptyScopes()
    {
        var request = new GenerateApiKeyRequest("CI Bot", [], null);

        var result = await _controller.Generate(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await _db.SweeprrApiKeys.ToListAsync());
    }

    [Fact]
    public async Task Generate_RejectsUnknownScope()
    {
        var request = new GenerateApiKeyRequest("CI Bot", ["delete:everything"], null);

        var result = await _controller.Generate(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await _db.SweeprrApiKeys.ToListAsync());
    }

    [Fact]
    public async Task Revoke_SetsIsActiveFalse_AndReturnsNoContent()
    {
        var generateResult = await _controller.Generate(
            new GenerateApiKeyRequest("CI Bot", [ApiKeyScopes.ReadSweep], null), CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(generateResult);
        var response = Assert.IsType<GenerateApiKeyResponse>(created.Value);

        var result = await _controller.Revoke(response.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        var entity = await _db.SweeprrApiKeys.AsNoTracking().FirstAsync(k => k.Id == response.Id);
        Assert.False(entity.IsActive);
    }

    [Fact]
    public async Task Revoke_ReturnsNotFound_ForUnknownId()
    {
        var result = await _controller.Revoke(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetAll_ReturnsMaskedKey_OrderedByCreatedAtDescending()
    {
        await _controller.Generate(
            new GenerateApiKeyRequest("First", [ApiKeyScopes.ReadSweep], null), CancellationToken.None);
        await _controller.Generate(
            new GenerateApiKeyRequest("Second", [ApiKeyScopes.WriteSweep], null), CancellationToken.None);

        var result = await _controller.GetAll(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var keys = Assert.IsAssignableFrom<IEnumerable<ApiKeyResponse>>(ok.Value).ToList();

        Assert.Equal(2, keys.Count);
        Assert.Equal("Second", keys[0].Name);
        Assert.Equal("First", keys[1].Name);
        Assert.All(keys, k => Assert.StartsWith("spr_live_••••••••", k.MaskedKey));
    }
}
