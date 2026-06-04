using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Data;

public class DatabaseTests : IDisposable
{
    private readonly SweeprrDbContext _db;
    private readonly string _dbPath;

    public DatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_test_{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _db = new SweeprrDbContext(options);
        _db.Database.Migrate();
        _db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    public void Dispose()
    {
        _db.Dispose();
        // Release WAL file handles before deletion on Windows
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    [Fact]
    public void Migration_Creates_Database_File()
    {
        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public async Task GlobalSettings_Seeds_Exactly_Once()
    {
        if (!await _db.GlobalSettings.AnyAsync())
        {
            _db.GlobalSettings.Add(new GlobalSettings { Id = 1 });
            await _db.SaveChangesAsync();
        }

        var count = await _db.GlobalSettings.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GlobalSettings_SingleRow_Constraint_Enforced()
    {
        _db.GlobalSettings.Add(new GlobalSettings { Id = 1 });
        await _db.SaveChangesAsync();

        _db.GlobalSettings.Add(new GlobalSettings { Id = 2 });
        await Assert.ThrowsAnyAsync<Exception>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task User_RoundTrip()
    {
        var user = new User
        {
            Username = "admin",
            PasswordHash = "hashed_value",
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var loaded = await _db.Users.FirstAsync(u => u.Username == "admin");
        Assert.Equal("admin", loaded.Username);
        Assert.Equal("hashed_value", loaded.PasswordHash);
        Assert.Equal(UserRole.Admin, loaded.Role);
    }

    [Fact]
    public async Task User_Username_Unique_Constraint()
    {
        _db.Users.Add(new User { Username = "admin", PasswordHash = "h1" });
        await _db.SaveChangesAsync();

        _db.Users.Add(new User { Username = "admin", PasswordHash = "h2" });
        await Assert.ThrowsAnyAsync<Exception>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task ServerConnection_RoundTrip()
    {
        var conn = new ServerConnection
        {
            Type = ConnectionType.Jellyfin,
            Name = "My Jellyfin",
            BaseUrl = "http://localhost:8096",
            ApiKeyEncrypted = "encrypted_key",
            IsEnabled = true
        };

        _db.ServerConnections.Add(conn);
        await _db.SaveChangesAsync();

        var loaded = await _db.ServerConnections.FirstAsync();
        Assert.Equal(ConnectionType.Jellyfin, loaded.Type);
        Assert.Equal("My Jellyfin", loaded.Name);
        Assert.Equal("encrypted_key", loaded.ApiKeyEncrypted);
    }

    [Fact]
    public async Task RuleGroup_With_Rules_RoundTrip()
    {
        var group = new RuleGroup
        {
            Name = "Old Movies",
            MediaType = MediaType.Movie,
            Action = SweepAction.DeleteAndUnmonitor,
            Rules = new List<Rule>
            {
                new Rule
                {
                    Section = 0,
                    Field = "LastWatched",
                    Comparator = "InLastDays",
                    Value = "90",
                    ValueType = RuleValueType.RelativeDays
                }
            }
        };

        _db.RuleGroups.Add(group);
        await _db.SaveChangesAsync();

        var loaded = await _db.RuleGroups
            .Include(rg => rg.Rules)
            .FirstAsync();

        Assert.Equal("Old Movies", loaded.Name);
        Assert.Single(loaded.Rules);
        Assert.Equal("LastWatched", loaded.Rules.First().Field);
    }

    [Fact]
    public async Task RuleGroup_Cascade_Deletes_Rules()
    {
        var group = new RuleGroup
        {
            Name = "Test Group",
            MediaType = MediaType.Movie,
            Action = SweepAction.UnmonitorOnly,
            Rules = [new Rule { Section = 0, Field = "Rating", Comparator = "LessThan", Value = "5", ValueType = RuleValueType.Number }]
        };

        _db.RuleGroups.Add(group);
        await _db.SaveChangesAsync();

        _db.RuleGroups.Remove(group);
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _db.Rules.CountAsync());
    }

    [Fact]
    public async Task SweepItem_RoundTrip()
    {
        var group = new RuleGroup { Name = "G", MediaType = MediaType.Movie, Action = SweepAction.DeleteAndUnmonitor };
        _db.RuleGroups.Add(group);
        await _db.SaveChangesAsync();

        var item = new SweepItem
        {
            RuleGroupId = group.Id,
            MediaServerItemId = "jellyfin-abc123",
            Title = "The Test Movie",
            MediaType = MediaType.Movie,
            SizeBytes = 2_000_000_000L,
            MatchedRuleSummary = "Last watched 95 days ago > 90",
            Status = SweepItemStatus.Pending,
            TmdbId = "12345"
        };

        _db.SweepItems.Add(item);
        await _db.SaveChangesAsync();

        var loaded = await _db.SweepItems.FirstAsync(s => s.MediaServerItemId == "jellyfin-abc123");
        Assert.Equal("The Test Movie", loaded.Title);
        Assert.Equal(SweepItemStatus.Pending, loaded.Status);
        Assert.Equal(2_000_000_000L, loaded.SizeBytes);
    }

    [Fact]
    public async Task ActivityLog_RoundTrip()
    {
        _db.ActivityLogs.Add(new ActivityLog
        {
            Level = ActivityLogLevel.Information,
            Category = ActivityLogCategory.Sweep,
            Message = "Swept 5 items, recovered 10.2 GB",
            MetaJson = """{"items":5,"gb":10.2}"""
        });
        await _db.SaveChangesAsync();

        var log = await _db.ActivityLogs.FirstAsync();
        Assert.Equal(ActivityLogCategory.Sweep, log.Category);
        Assert.Contains("10.2 GB", log.Message);
    }

    [Fact]
    public async Task Exclusion_RoundTrip()
    {
        _db.Exclusions.Add(new Exclusion
        {
            MediaServerItemId = "jellyfin-xyz",
            Reason = "User requested permanent keep"
        });
        await _db.SaveChangesAsync();

        var excl = await _db.Exclusions.FirstAsync();
        Assert.Equal("jellyfin-xyz", excl.MediaServerItemId);
    }

    [Fact]
    public async Task WalMode_Is_Enabled()
    {
        // Use ADO.NET directly — PRAGMA is not a composable EF query
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var result = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("wal", result, ignoreCase: true);
    }
}
