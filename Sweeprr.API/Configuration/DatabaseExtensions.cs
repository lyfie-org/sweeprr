using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations.Jellyfin.WebSocket;
using Sweeprr.API.Models;

namespace Sweeprr.API.Configuration;

public static class DatabaseExtensions
{
    public static IServiceCollection AddSweeprrDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configDir = configuration["ConfigDir"] ?? "/config";
        Directory.CreateDirectory(configDir);

        var dbPath = Path.Combine(configDir, "sweeprr.db");

        services.AddDbContext<SweeprrDbContext>(options =>
            options.UseSqlite(
                $"Data Source={dbPath}",
                sqlite => sqlite.MigrationsAssembly(typeof(SweeprrDbContext).Assembly.FullName)
            )
        );

        return services;
    }

    public static async Task MigrateAndSeedAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SweeprrDbContext>>();

        try
        {
            await db.Database.MigrateAsync();

            // Enable WAL mode for better concurrent read/write performance
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

            // Seed GlobalSettings if absent (single-row guarantee)
            if (!await db.GlobalSettings.AnyAsync())
            {
                db.GlobalSettings.Add(new GlobalSettings
                {
                    Id = 1,
                    InstanceName = "Sweeprr",
                    JwtSecret = GenerateSecret(),
                    MaxItemsPerRun = 20,
                    MaxGbPerRun = 50.0,
                    GlobalDryRun = true,
                    DefaultCron = "0 3 * * *"
                });
                await db.SaveChangesAsync();
                logger.LogInformation("GlobalSettings seeded with defaults.");
            }
        }
        catch (Exception ex) when (ex.Message.Contains("permission") || ex.Message.Contains("read-only"))
        {
            logger.LogCritical(
                "Cannot write to config directory. Ensure the /config volume is mounted and writable. Error: {Message}",
                ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Restores PlaybackActivity rows from SQLite into the in-memory PlaystateCache.
    /// Paginates in batches of 500 to avoid large single queries on big libraries.
    /// Must be called AFTER MigrateAndSeedAsync.
    /// </summary>
    public static async Task BackfillPlaystateCacheAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var cache  = scope.ServiceProvider.GetRequiredService<IPlaystateCache>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SweeprrDbContext>>();

        const int pageSize = 500;
        int page  = 0;
        int total = 0;

        while (true)
        {
            var batch = await db.PlaybackActivities
                .AsNoTracking()
                .OrderBy(p => p.Id)
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToListAsync();

            if (batch.Count == 0) break;

            foreach (var a in batch)
            {
                cache.Upsert(a.MediaServerItemId, a.UserId,
                    new Integrations.Jellyfin.Models.JellyfinUserData(
                        Played:                a.IsFinished,
                        PlayCount:             a.PlayCount,
                        LastPlayedDate:        a.LastWatched == default ? null
                                                   : new DateTimeOffset(a.LastWatched, TimeSpan.Zero),
                        PlaybackPositionTicks: a.PlaybackPositionTicks));
            }

            total += batch.Count;
            page++;

            if (batch.Count < pageSize) break;
        }

        logger.LogInformation("Boot backfill: restored {Count} playback activity record(s) into PlaystateCache", total);
    }

    private static string GenerateSecret()
    {
        var bytes = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
