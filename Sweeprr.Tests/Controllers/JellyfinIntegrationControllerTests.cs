using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Controllers;
using Sweeprr.API.Data;
using Sweeprr.API.Models;

namespace Sweeprr.Tests.Controllers;

public class JellyfinIntegrationControllerTests : IDisposable
{
    private readonly List<string> _dbPaths = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }

    [Fact]
    public async Task GetClientScript_UsesPublicBaseUrl_WhenSet()
    {
        var (controller, db) = CreateController(scheme: "https", host: "request-host.example");
        db.GlobalSettings.Add(new GlobalSettings { Id = 1, PublicBaseUrl = "https://sweeprr.example.com/" });
        await db.SaveChangesAsync();

        var result = await controller.GetClientScript(CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/javascript", content.ContentType);
        Assert.Contains("const SWEEPRR_BASE = \"https://sweeprr.example.com\";", content.Content);
    }

    [Fact]
    public async Task GetClientScript_FallsBackToRequestHost_WhenPublicBaseUrlNotSet()
    {
        var (controller, _) = CreateController(scheme: "https", host: "sweeprr.local");

        var result = await controller.GetClientScript(CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("const SWEEPRR_BASE = \"https://sweeprr.local\";", content.Content);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private (JellyfinIntegrationController Controller, SweeprrDbContext Db) CreateController(string scheme, string host)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_jellyfin_integration_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);
        var connectionString = $"Data Source={dbPath}";

        var db = new SweeprrDbContext(new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite(connectionString)
            .Options);
        db.Database.Migrate();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = scheme;
        httpContext.Request.Host = new HostString(host);

        var controller = new JellyfinIntegrationController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, db);
    }
}
