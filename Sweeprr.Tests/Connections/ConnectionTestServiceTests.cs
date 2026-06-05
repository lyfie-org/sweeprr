using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Connections;
using Sweeprr.API.Models;
using Sweeprr.API.Services;
using System.IO;

namespace Sweeprr.Tests.Connections;

public class ConnectionTestServiceTests : IDisposable
{
    private readonly SweeprrDbContext _db;
    private readonly ConnectionService _connSvc;
    private readonly string _dbPath;

    public ConnectionTestServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_ctest_{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _db = new SweeprrDbContext(options);
        _db.Database.Migrate();

        var keysDir = Path.Combine(Path.GetTempPath(), $"sweeprr_dp2_{Guid.NewGuid()}");
        Directory.CreateDirectory(keysDir);
        var protector = new SecretProtector(DataProtectionProvider.Create(new DirectoryInfo(keysDir)));
        _connSvc = new ConnectionService(_db, protector);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    // ── TestUnsaved — Jellyfin ───────────────────────────────────────────────

    [Fact]
    public async Task Jellyfin_SuccessResponse_Returns_ServerName_And_Version()
    {
        var svc = BuildTestService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""{"ServerName":"HomeServer","Version":"10.9.4","Id":"abc"}""")
        });

        var result = await svc.TestUnsavedAsync(
            ConnectionType.Jellyfin, "http://jellyfin:8096", "validKey", false);

        Assert.True(result.Success);
        Assert.Equal("HomeServer", result.ServerName);
        Assert.Equal("10.9.4", result.Version);
        Assert.NotNull(result.LatencyMs);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task Jellyfin_401_Returns_FriendlyKeyError()
    {
        var svc = BuildTestService(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await svc.TestUnsavedAsync(
            ConnectionType.Jellyfin, "http://jellyfin:8096", "badKey", false);

        Assert.False(result.Success);
        Assert.Contains("401", result.ErrorMessage);
        Assert.Contains("API key", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Jellyfin_403_Returns_FriendlyForbiddenError()
    {
        var svc = BuildTestService(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await svc.TestUnsavedAsync(
            ConnectionType.Jellyfin, "http://jellyfin:8096", "key", false);

        Assert.False(result.Success);
        Assert.Contains("403", result.ErrorMessage);
    }

    // ── TestUnsaved — Radarr ─────────────────────────────────────────────────

    [Fact]
    public async Task Radarr_SuccessResponse_Returns_AppName_And_Version()
    {
        var svc = BuildTestService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""{"appName":"Radarr","version":"5.3.0.8079","isDebug":false}""")
        });

        var result = await svc.TestUnsavedAsync(
            ConnectionType.Radarr, "http://radarr:7878", "key", false);

        Assert.True(result.Success);
        Assert.Equal("Radarr", result.ServerName);
        Assert.Equal("5.3.0.8079", result.Version);
    }

    // ── TestUnsaved — Sonarr ─────────────────────────────────────────────────

    [Fact]
    public async Task Sonarr_SuccessResponse_Returns_AppName_And_Version()
    {
        var svc = BuildTestService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""{"appName":"Sonarr","version":"4.0.0.748","isDebug":false}""")
        });

        var result = await svc.TestUnsavedAsync(
            ConnectionType.Sonarr, "http://sonarr:8989", "key", false);

        Assert.True(result.Success);
        Assert.Equal("Sonarr", result.ServerName);
    }

    // ── Timeout ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Timeout_Returns_FriendlyTimeoutError()
    {
        var svc = BuildTestService(new TimeoutHttpHandler());

        var result = await svc.TestUnsavedAsync(
            ConnectionType.Radarr, "http://unreachable:7878", "key", false);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── TestSaved ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TestSaved_Returns_NotFound_Error_For_Unknown_Id()
    {
        var svc = BuildTestService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""{"ServerName":"X","Version":"1.0"}""")
        });

        var result = await svc.TestSavedAsync(9999);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestSaved_Returns_KeyError_When_No_Key_Stored()
    {
        var (created, _) = await _connSvc.CreateAsync(new ConnectionRequest
        {
            Name = "J", Type = ConnectionType.Jellyfin, BaseUrl = "http://jellyfin:8096",
            ApiKey = null // no key stored
        });

        var svc = BuildTestServiceWithConnSvc(_connSvc, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""{"ServerName":"X","Version":"1.0"}""")
        });

        var result = await svc.TestSavedAsync(created.Id);

        Assert.False(result.Success);
        Assert.Contains("API key", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestSaved_Persists_Success_Status()
    {
        var (created, _) = await _connSvc.CreateAsync(new ConnectionRequest
        {
            Name = "J", Type = ConnectionType.Jellyfin, BaseUrl = "http://jellyfin:8096",
            ApiKey = "myKey"
        });

        var svc = BuildTestServiceWithConnSvc(_connSvc, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""{"ServerName":"Home","Version":"10.9"}""")
        });

        await svc.TestSavedAsync(created.Id);

        var updated = await _connSvc.GetByIdAsync(created.Id);
        Assert.True(updated!.LastConnectionOk);
        Assert.NotNull(updated.LastConnectedAt);
    }

    [Fact]
    public async Task TestSaved_Persists_Failure_Status()
    {
        var (created, _) = await _connSvc.CreateAsync(new ConnectionRequest
        {
            Name = "R", Type = ConnectionType.Radarr, BaseUrl = "http://radarr:7878",
            ApiKey = "badKey"
        });

        var svc = BuildTestServiceWithConnSvc(_connSvc,
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await svc.TestSavedAsync(created.Id);

        var updated = await _connSvc.GetByIdAsync(created.Id);
        Assert.False(updated!.LastConnectionOk);
    }

    // ── Unexpected payload ───────────────────────────────────────────────────

    [Fact]
    public async Task Non_Json_Response_Returns_FriendlyError()
    {
        var svc = BuildTestService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Not JSON</html>", Encoding.UTF8, "text/html")
        });

        var result = await svc.TestUnsavedAsync(
            ConnectionType.Radarr, "http://radarr:7878", "key", false);

        Assert.False(result.Success);
        Assert.Contains("unexpected response", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── HTTP 500 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Server_500_Returns_FriendlyError()
    {
        var svc = BuildTestService(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await svc.TestUnsavedAsync(
            ConnectionType.Sonarr, "http://sonarr:8989", "key", false);

        Assert.False(result.Success);
        Assert.Contains("500", result.ErrorMessage);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ConnectionTestService BuildTestService(HttpResponseMessage response)
        => BuildTestService(new MockHttpHandler(response));

    private static ConnectionTestService BuildTestService(HttpMessageHandler handler)
    {
        var db = CreateInMemoryDb();
        var keysDir = Path.Combine(Path.GetTempPath(), $"sweeprr_dp3_{Guid.NewGuid()}");
        Directory.CreateDirectory(keysDir);
        var protector = new SecretProtector(DataProtectionProvider.Create(new DirectoryInfo(keysDir)));
        var connSvc = new ConnectionService(db, protector);
        return new ConnectionTestService(connSvc, NullLogger<ConnectionTestService>.Instance,
            _ => handler);
    }

    private static ConnectionTestService BuildTestServiceWithConnSvc(
        ConnectionService connSvc, HttpResponseMessage response)
        => new(connSvc, NullLogger<ConnectionTestService>.Instance,
            _ => new MockHttpHandler(response));

    private static SweeprrDbContext CreateInMemoryDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tmp_{Guid.NewGuid()}.db");
        var opts = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={path}").Options;
        var db = new SweeprrDbContext(opts);
        db.Database.Migrate();
        return db;
    }

    private static StringContent JsonContent(string json)
        => new(json, Encoding.UTF8, "application/json");

    // Returns a canned response synchronously — latency will be ~0ms which is fine for tests.
    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public MockHttpHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    // Simulates a request that never completes (triggers HttpClient.Timeout).
    private sealed class TimeoutHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK); // never reached
        }
    }
}
