using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sweeprr.API.Auth;
using Sweeprr.API.Configuration;
using Sweeprr.API.Data;
using Sweeprr.API.Models;

namespace Sweeprr.Tests.Auth;

/// <summary>
/// Lightweight TestServer covering Story 10.3 PRD test cases T2-T5 for
/// <see cref="ApiKeyAuthenticationHandler"/> and <see cref="ScopeAuthorizationHandler"/>.
/// </summary>
public class ApiKeyAuthenticationHandlerTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_apikey_{Guid.NewGuid()}.db");
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddDbContext<SweeprrDbContext>(opt => opt.UseSqlite($"Data Source={_dbPath}"));
                    services.AddSweeprrAuth();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/test/read", () => Results.Ok());
                        endpoints.MapGet("/test/execute", () => Results.Ok())
                            .RequireAuthorization("ExecuteSweep");
                    });
                });
            });

        _host = await hostBuilder.StartAsync();

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
            db.Database.Migrate();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        }

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private async Task<(string Raw, int Id)> SeedKeyAsync(string[] scopes, bool isActive = true, DateTime? expiresAt = null)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();

        var raw = ApiKeyGenerator.Generate();
        var entity = new SweeprrApiKey
        {
            Name = "Test Key",
            HashedKey = ApiKeyGenerator.Hash(raw),
            MaskedKey = ApiKeyGenerator.Mask(raw),
            CreatedBy = "tester",
            Scopes = JsonSerializer.Serialize(scopes),
            IsActive = isActive,
            ExpiresAt = expiresAt,
        };
        db.SweeprrApiKeys.Add(entity);
        await db.SaveChangesAsync();
        return (raw, entity.Id);
    }

    private static HttpRequestMessage Authed(string path, string rawKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawKey);
        return req;
    }

    [Fact]
    public async Task NoAuthHeader_Returns401()
    {
        var resp = await _client.GetAsync("/test/read");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task UnknownKey_Returns401()
    {
        var resp = await _client.SendAsync(Authed("/test/read", "spr_live_" + new string('x', 32)));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // T2: a valid key authenticates (200) and updates LastUsedAt
    [Fact]
    public async Task ValidKey_AuthenticatesAndUpdatesLastUsedAt()
    {
        var (raw, id) = await SeedKeyAsync([ApiKeyScopes.ReadSweep]);

        var resp = await _client.SendAsync(Authed("/test/read", raw));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var reloaded = await db.SweeprrApiKeys.AsNoTracking().FirstAsync(k => k.Id == id);
        Assert.NotNull(reloaded.LastUsedAt);
    }

    // T3: a revoked key is rejected with 401
    [Fact]
    public async Task RevokedKey_Returns401()
    {
        var (raw, _) = await SeedKeyAsync([ApiKeyScopes.ReadSweep], isActive: false);

        var resp = await _client.SendAsync(Authed("/test/read", raw));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // T4: a read:sweep-only key is forbidden on an execute-scoped endpoint
    [Fact]
    public async Task InsufficientScope_Returns403()
    {
        var (raw, _) = await SeedKeyAsync([ApiKeyScopes.ReadSweep]);

        var resp = await _client.SendAsync(Authed("/test/execute", raw));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // T5: an expired key is rejected with 401
    [Fact]
    public async Task ExpiredKey_Returns401()
    {
        var (raw, _) = await SeedKeyAsync([ApiKeyScopes.ReadSweep], expiresAt: DateTime.UtcNow.AddDays(-1));

        var resp = await _client.SendAsync(Authed("/test/read", raw));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ExecuteScopedKey_CanCallExecuteEndpoint()
    {
        var (raw, _) = await SeedKeyAsync([ApiKeyScopes.ExecuteSweep]);

        var resp = await _client.SendAsync(Authed("/test/execute", raw));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task AdminScopedKey_SatisfiesAnyScopePolicy()
    {
        var (raw, _) = await SeedKeyAsync([ApiKeyScopes.Admin]);

        var resp = await _client.SendAsync(Authed("/test/execute", raw));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
