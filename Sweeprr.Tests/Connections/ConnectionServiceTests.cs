using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Connections;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

// ReSharper disable AccessToDisposedClosure

namespace Sweeprr.Tests.Connections;

public class ConnectionServiceTests : IDisposable
{
    private readonly SweeprrDbContext _db;
    private readonly ConnectionService _svc;
    private readonly string _dbPath;

    public ConnectionServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_conn_{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _db = new SweeprrDbContext(options);
        _db.Database.Migrate();

        // Use a real DataProtection stack with ephemeral file-based keys (fine for tests)
        var keysDir = Path.Combine(Path.GetTempPath(), $"sweeprr_dp_{Guid.NewGuid()}");
        Directory.CreateDirectory(keysDir);
        var protector = new SecretProtector(DataProtectionProvider.Create(new DirectoryInfo(keysDir)));
        _svc = new ConnectionService(_db, protector);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    // ── GetAll ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_Returns_Empty_When_No_Connections()
    {
        var result = await _svc.GetAllAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAll_Returns_All_Connections()
    {
        await _svc.CreateAsync(JellyfinRequest("Jellyfin Home", "http://localhost:8096", "key1"));
        await _svc.CreateAsync(RadarrRequest("Radarr", "http://localhost:7878", "key2"));

        var result = (await _svc.GetAllAsync()).ToList();
        Assert.Equal(2, result.Count);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Stores_Connection_With_Masked_Key()
    {
        var (conn, _) = await _svc.CreateAsync(JellyfinRequest("Jellyfin", "http://host:8096", "mySecretKey123"));

        Assert.Equal("Jellyfin", conn.Name);
        Assert.Equal("http://host:8096", conn.BaseUrl);
        Assert.True(conn.HasKey);
        // Key must NOT appear in the response
        Assert.DoesNotContain("mySecretKey123", conn.MaskedKey);
        Assert.StartsWith("••••", conn.MaskedKey);
        Assert.EndsWith("y123", conn.MaskedKey); // last 4 chars of "mySecretKey123"
    }

    [Fact]
    public async Task Create_Strips_Trailing_Slash_From_BaseUrl()
    {
        var (conn, _) = await _svc.CreateAsync(JellyfinRequest("J", "http://host:8096/", "k"));
        Assert.Equal("http://host:8096", conn.BaseUrl);
    }

    [Fact]
    public async Task Create_Without_ApiKey_Stores_No_Key()
    {
        var req = JellyfinRequest("J", "http://host:8096", null);
        var (conn, _) = await _svc.CreateAsync(req);

        Assert.False(conn.HasKey);
        Assert.Empty(conn.MaskedKey);
    }

    [Fact]
    public async Task Create_Warns_On_Duplicate_BaseUrl()
    {
        await _svc.CreateAsync(JellyfinRequest("First", "http://host:8096", "k1"));
        var (_, warning) = await _svc.CreateAsync(JellyfinRequest("Second", "http://host:8096", "k2"));

        Assert.NotNull(warning);
        Assert.Contains("already exists", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_Stores_AllowInsecure_Flag()
    {
        var req = JellyfinRequest("J", "http://host:8096", "k");
        req.AllowInsecure = true;

        var (conn, _) = await _svc.CreateAsync(req);
        Assert.True(conn.AllowInsecure);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Changes_Name_And_BaseUrl()
    {
        var (created, _) = await _svc.CreateAsync(JellyfinRequest("Old", "http://old:8096", "key"));

        var updateReq = JellyfinRequest("New Name", "http://new:8096", null);
        var updated = await _svc.UpdateAsync(created.Id, updateReq);

        Assert.NotNull(updated);
        Assert.Equal("New Name", updated!.Name);
        Assert.Equal("http://new:8096", updated.BaseUrl);
    }

    [Fact]
    public async Task Update_With_Null_Key_Preserves_Existing_Key()
    {
        var (created, _) = await _svc.CreateAsync(JellyfinRequest("J", "http://host:8096", "originalKey"));

        var updateReq = JellyfinRequest("J", "http://host:8096", null); // null = keep key
        await _svc.UpdateAsync(created.Id, updateReq);

        // The raw key in the DB should still decrypt to the original
        var decrypted = await _svc.GetDecryptedKeyAsync(created.Id);
        Assert.Equal("originalKey", decrypted);
    }

    [Fact]
    public async Task Update_With_New_Key_Replaces_Existing_Key()
    {
        var (created, _) = await _svc.CreateAsync(JellyfinRequest("J", "http://host:8096", "oldKey"));

        var updateReq = JellyfinRequest("J", "http://host:8096", "newKey");
        await _svc.UpdateAsync(created.Id, updateReq);

        var decrypted = await _svc.GetDecryptedKeyAsync(created.Id);
        Assert.Equal("newKey", decrypted);
    }

    [Fact]
    public async Task Update_Returns_Null_For_Nonexistent_Id()
    {
        var result = await _svc.UpdateAsync(9999, JellyfinRequest("X", "http://x:8096", "k"));
        Assert.Null(result);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Removes_Connection()
    {
        var (created, _) = await _svc.CreateAsync(JellyfinRequest("J", "http://host:8096", "k"));

        var deleted = await _svc.DeleteAsync(created.Id);
        Assert.True(deleted);

        var found = await _svc.GetByIdAsync(created.Id);
        Assert.Null(found);
    }

    [Fact]
    public async Task Delete_Returns_False_For_Nonexistent_Id()
    {
        var result = await _svc.DeleteAsync(9999);
        Assert.False(result);
    }

    // ── Key masking ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Short_Key_Masked_As_Four_Bullets()
    {
        var (conn, _) = await _svc.CreateAsync(JellyfinRequest("J", "http://host:8096", "abc"));
        // key ≤ 4 chars → just bullets, no hint
        Assert.Equal("••••", conn.MaskedKey);
    }

    [Fact]
    public async Task Long_Key_Shows_Last_Four_Chars()
    {
        var (conn, _) = await _svc.CreateAsync(JellyfinRequest("J", "http://host:8096", "abcdefghij"));
        Assert.Equal("••••ghij", conn.MaskedKey);
    }

    // ── Encryption at rest ───────────────────────────────────────────────────

    [Fact]
    public async Task Api_Key_Stored_Encrypted_In_Database()
    {
        await _svc.CreateAsync(JellyfinRequest("J", "http://host:8096", "plainTextKey"));

        var raw = await _db.ServerConnections.Select(c => c.ApiKeyEncrypted).FirstAsync();
        Assert.NotEqual("plainTextKey", raw);
        Assert.False(string.IsNullOrEmpty(raw));
    }

    [Fact]
    public async Task GetDecryptedKey_Returns_Original_Plaintext()
    {
        var (conn, _) = await _svc.CreateAsync(JellyfinRequest("J", "http://host:8096", "MyApiKey99"));

        var decrypted = await _svc.GetDecryptedKeyAsync(conn.Id);
        Assert.Equal("MyApiKey99", decrypted);
    }

    // ── PersistTestResult ────────────────────────────────────────────────────

    [Fact]
    public async Task PersistTestResult_Sets_LastConnectionOk_And_Timestamp()
    {
        var (conn, _) = await _svc.CreateAsync(JellyfinRequest("J", "http://host:8096", "k"));

        await _svc.PersistTestResultAsync(conn.Id, success: true);

        var updated = await _svc.GetByIdAsync(conn.Id);
        Assert.True(updated!.LastConnectionOk);
        Assert.NotNull(updated.LastConnectedAt);
        Assert.True(updated.LastConnectedAt > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task PersistTestResult_False_Records_Failure()
    {
        var (conn, _) = await _svc.CreateAsync(JellyfinRequest("J", "http://host:8096", "k"));

        await _svc.PersistTestResultAsync(conn.Id, success: false);

        var updated = await _svc.GetByIdAsync(conn.Id);
        Assert.False(updated!.LastConnectionOk);
    }

    // ── Multiple connection types ─────────────────────────────────────────────

    [Fact]
    public async Task Multiple_Arr_Instances_Coexist()
    {
        await _svc.CreateAsync(RadarrRequest("Radarr 4K", "http://radarr4k:7878", "k1"));
        await _svc.CreateAsync(RadarrRequest("Radarr SD", "http://radarrsd:7878", "k2"));
        await _svc.CreateAsync(SonarrRequest("Sonarr", "http://sonarr:8989", "k3"));

        var all = (await _svc.GetAllAsync()).ToList();
        Assert.Equal(3, all.Count);
        Assert.Equal(2, all.Count(c => c.Type == ConnectionType.Radarr));
        Assert.Equal(1, all.Count(c => c.Type == ConnectionType.Sonarr));
    }

    // ── URL validation ───────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Throws_For_Invalid_Scheme()
    {
        var req = JellyfinRequest("J", "ftp://host:8096", "k");
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.CreateAsync(req));
    }

    [Fact]
    public async Task Create_Accepts_Path_In_BaseUrl_With_Warning()
    {
        // Reverse-proxied installs use paths like http://host/radarr
        var req = RadarrRequest("R", "http://host/radarr", "k");
        var (conn, warning) = await _svc.CreateAsync(req);

        Assert.Equal("http://host/radarr", conn.BaseUrl);
        Assert.NotNull(warning); // warns about the path
    }

    // ── Factories ────────────────────────────────────────────────────────────

    private static ConnectionRequest JellyfinRequest(string name, string url, string? key) => new()
    {
        Name = name, Type = ConnectionType.Jellyfin, BaseUrl = url, ApiKey = key
    };

    private static ConnectionRequest RadarrRequest(string name, string url, string? key) => new()
    {
        Name = name, Type = ConnectionType.Radarr, BaseUrl = url, ApiKey = key
    };

    private static ConnectionRequest SonarrRequest(string name, string url, string? key) => new()
    {
        Name = name, Type = ConnectionType.Sonarr, BaseUrl = url, ApiKey = key
    };
}
