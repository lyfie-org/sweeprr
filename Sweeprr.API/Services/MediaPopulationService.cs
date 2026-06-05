using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Integrations.Jellyfin.WebSocket;
using Sweeprr.API.Integrations.Matching;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Radarr.Models;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Integrations.Sonarr.Models;
using Sweeprr.API.Models;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.API.Services;

public sealed class MediaPopulationService : IMediaPopulationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlaystateCache _playstateCache;
    private readonly ILogger<MediaPopulationService> _logger;

    public MediaPopulationService(
        IServiceScopeFactory scopeFactory,
        IPlaystateCache playstateCache,
        ILogger<MediaPopulationService> logger)
    {
        _scopeFactory = scopeFactory;
        _playstateCache = playstateCache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MediaContext>> PopulateAsync(
        RuleGroup group, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SweeprrDbContext>();
        var clientFactory = scope.ServiceProvider.GetRequiredService<IIntegrationClientFactory>();
        var matcher = scope.ServiceProvider.GetRequiredService<IMediaMatchingService>();
        var watchAgg = scope.ServiceProvider.GetRequiredService<IWatchAggregationService>();

        // Find Jellyfin connection
        var jellyfinConn = await db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Jellyfin && c.IsEnabled, ct);

        if (jellyfinConn is null)
        {
            _logger.LogWarning("No enabled Jellyfin connection found — scan returns empty");
            return [];
        }

        var jellyfinClient = await clientFactory.CreateJellyfinClientAsync(jellyfinConn.Id, ct);
        if (jellyfinClient is null)
        {
            _logger.LogWarning("Could not create Jellyfin client for connection {Id}", jellyfinConn.Id);
            return [];
        }

        // Fetch Jellyfin users for multi-user aggregation
        var usersResult = await jellyfinClient.GetUsersAsync(ct);
        var users = usersResult is HttpResult<IReadOnlyList<JellyfinUser>>.Success usersOk
            ? usersOk.Value
            : [];

        // Determine item types to fetch based on the group's MediaType
        var itemTypes = group.MediaType switch
        {
            MediaType.Movie => new[] { "Movie" },
            MediaType.Series => new[] { "Series" },
            MediaType.Season => new[] { "Season" },
            MediaType.Episode => new[] { "Episode" },
            _ => new[] { "Movie", "Series", "Season", "Episode" }
        };

        // Fetch *arr data in parallel with Jellyfin items
        var (radarrIndex, radarrConnId) = await BuildRadarrIndexAsync(db, clientFactory, matcher, ct);
        var (sonarrIndex, sonarrConnId) = await BuildSonarrIndexAsync(db, clientFactory, matcher, ct);
        var radarrTags = await LoadRadarrTagsAsync(db, clientFactory, ct);
        var sonarrTags = await LoadSonarrTagsAsync(db, clientFactory, ct);
        var radarrProfiles = await LoadRadarrProfilesAsync(db, clientFactory, ct);
        var sonarrProfiles = await LoadSonarrProfilesAsync(db, clientFactory, ct);

        // Fetch Jellyfin items (per-user to get UserData)
        var primaryUserId = users.Count > 0 ? users[0].Id : null;
        var itemsResult = await jellyfinClient.GetAllItemsAsync(
            new GetItemsRequest
            {
                UserId = primaryUserId,
                IncludeItemTypes = itemTypes,
                Fields = ["ProviderIds", "DateCreated", "UserData", "Path", "MediaStreams"],
            },
            maxItems: 10_000,
            ct: ct);

        if (itemsResult is not HttpResult<IReadOnlyList<JellyfinItem>>.Success itemsOk)
        {
            _logger.LogWarning("Failed to fetch Jellyfin items: {Result}", itemsResult);
            return [];
        }

        var jellyfinItems = itemsOk.Value;
        _logger.LogInformation("Fetched {Count} Jellyfin items of type(s) [{Types}]",
            jellyfinItems.Count, string.Join(", ", itemTypes));

        // Build MediaContext for each item
        var contexts = new List<MediaContext>(jellyfinItems.Count);
        foreach (var jItem in jellyfinItems)
        {
            var ctx = BuildMediaContext(
                jItem, group.MediaType, users,
                matcher, radarrIndex, radarrConnId, sonarrIndex, sonarrConnId,
                radarrTags, sonarrTags, radarrProfiles, sonarrProfiles,
                watchAgg);
            contexts.Add(ctx);
        }

        return contexts;
    }

    private MediaContext BuildMediaContext(
        JellyfinItem jItem,
        MediaType groupMediaType,
        IReadOnlyList<JellyfinUser> users,
        IMediaMatchingService matcher,
        ArrIndex<RadarrMovie>? radarrIndex,
        int? radarrConnId,
        ArrIndex<SonarrSeries>? sonarrIndex,
        int? sonarrConnId,
        IReadOnlyDictionary<int, string> radarrTags,
        IReadOnlyDictionary<int, string> sonarrTags,
        IReadOnlyDictionary<int, string> radarrProfiles,
        IReadOnlyDictionary<int, string> sonarrProfiles,
        IWatchAggregationService watchAgg)
    {
        int? seasonNumber = jItem.Type == JellyfinMediaType.Season ? jItem.IndexNumber : null;
        var identity = MediaIdentity.From(jItem.ProviderIds, seasonNumber);

        // Watch aggregation from playstate cache
        var watchState = AggregateWatchState(jItem.Id, users, watchAgg);

        // *arr enrichment
        bool? monitored = null;
        IReadOnlyList<string>? tags = null;
        string? qualityProfile = null;
        decimal? fileSizeGb = null;
        int? arrConnectionId = null;

        if (groupMediaType == MediaType.Movie && radarrIndex is not null)
        {
            var match = matcher.MatchMovie(identity, radarrIndex);
            if (match is MatchResult<RadarrMovie>.Matched m)
            {
                var movie = m.Value;
                arrConnectionId = radarrConnId; // ServerConnection.Id (not the arr-item ID)
                monitored = movie.Monitored;
                tags = movie.Tags.Select(t => radarrTags.GetValueOrDefault(t, t.ToString())).ToList();
                qualityProfile = radarrProfiles.GetValueOrDefault(movie.QualityProfileId);
                fileSizeGb = movie.SizeOnDisk > 0
                    ? Math.Round((decimal)movie.SizeOnDisk / 1_073_741_824m, 2)
                    : null;
            }
        }
        else if (groupMediaType is MediaType.Series or MediaType.Season && sonarrIndex is not null)
        {
            var match = matcher.MatchSeries(identity, sonarrIndex);
            if (match is MatchResult<SonarrSeriesMatch>.Matched m)
            {
                var series = m.Value.Series;
                arrConnectionId = sonarrConnId; // ServerConnection.Id
                monitored = series.Monitored;
                tags = series.Tags.Select(t => sonarrTags.GetValueOrDefault(t, t.ToString())).ToList();
                qualityProfile = sonarrProfiles.GetValueOrDefault(series.QualityProfileId);

                if (m.Value.Season is { } season)
                {
                    fileSizeGb = season.SizeOnDisk > 0
                        ? Math.Round((decimal)season.SizeOnDisk / 1_073_741_824m, 2)
                        : null;
                }
            }
        }

        return new MediaContext
        {
            ItemId = jItem.Id,
            Title = jItem.Name,
            MediaType = groupMediaType,
            LastWatched = watchState?.LatestLastWatched,
            PlayCount = watchState?.MaxPlayCount,
            WatchedByAnyUser = watchState?.WatchedByAnyUser,
            WatchedByAllUsers = watchState?.WatchedByAllUsers,
            SeenByUserCount = watchState?.SeenByUserCount,
            ReleaseDate = jItem.ProductionYear.HasValue
                ? new DateTime(jItem.ProductionYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                : null,
            DateAdded = jItem.DateCreated?.UtcDateTime,
            Rating = jItem.CommunityRating.HasValue ? (decimal)jItem.CommunityRating.Value : null,
            Genre = null,
            ResolutionHeight = null,
            Monitored = monitored,
            Tags = tags,
            QualityProfile = qualityProfile,
            FileSizeGb = fileSizeGb,
            // Provider IDs forwarded for execution-time *arr matching (stored on SweepItem)
            ImdbId = jItem.ProviderIds.ImdbId,
            TmdbId = jItem.ProviderIds.TmdbId,
            TvdbId = jItem.ProviderIds.TvdbId,
            ArrConnectionId = arrConnectionId,
            SeasonNumber = seasonNumber,
        };
    }

    private AggregatedWatchState? AggregateWatchState(
        string itemId,
        IReadOnlyList<JellyfinUser> users,
        IWatchAggregationService watchAgg)
    {
        if (users.Count == 0) return null;

        var perUserStates = new List<UserWatchState>();
        foreach (var user in users)
        {
            var cached = _playstateCache.Get(itemId, user.Id);
            if (cached is not null)
            {
                perUserStates.Add(new UserWatchState(
                    user.Id, itemId,
                    cached.Played, cached.LastPlayedDate,
                    cached.PlayCount, cached.PlaybackPositionTicks));
            }
            else
            {
                perUserStates.Add(new UserWatchState(
                    user.Id, itemId, false, null, 0, 0));
            }
        }

        return watchAgg.AggregateMovie(perUserStates, UserScope.Default);
    }

    // ── *arr index/tag/profile loaders ──────────────────────────────────────

    private async Task<(ArrIndex<RadarrMovie>? Index, int? ConnectionId)> BuildRadarrIndexAsync(
        SweeprrDbContext db, IIntegrationClientFactory factory,
        IMediaMatchingService matcher, CancellationToken ct)
    {
        var conn = await db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Radarr && c.IsEnabled, ct);

        if (conn is null) return (null, null);

        var client = await factory.CreateRadarrClientAsync(conn.Id, ct);
        if (client is null) return (null, conn.Id);

        var result = await client.GetMoviesAsync(ct);
        if (result is not HttpResult<IReadOnlyList<RadarrMovie>>.Success ok) return (null, conn.Id);

        return (matcher.BuildRadarrIndex(ok.Value), conn.Id);
    }

    private async Task<(ArrIndex<SonarrSeries>? Index, int? ConnectionId)> BuildSonarrIndexAsync(
        SweeprrDbContext db, IIntegrationClientFactory factory,
        IMediaMatchingService matcher, CancellationToken ct)
    {
        var conn = await db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Sonarr && c.IsEnabled, ct);

        if (conn is null) return (null, null);

        var client = await factory.CreateSonarrClientAsync(conn.Id, ct);
        if (client is null) return (null, conn.Id);

        var result = await client.GetSeriesAsync(ct);
        if (result is not HttpResult<IReadOnlyList<SonarrSeries>>.Success ok) return (null, conn.Id);

        return (matcher.BuildSonarrIndex(ok.Value), conn.Id);
    }

    private async Task<IReadOnlyDictionary<int, string>> LoadRadarrTagsAsync(
        SweeprrDbContext db, IIntegrationClientFactory factory, CancellationToken ct)
    {
        var conn = await db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Radarr && c.IsEnabled, ct);

        if (conn is null) return new Dictionary<int, string>();

        var client = await factory.CreateRadarrClientAsync(conn.Id, ct);
        if (client is null) return new Dictionary<int, string>();

        var result = await client.GetTagsAsync(ct);
        return result is HttpResult<IReadOnlyList<RadarrTag>>.Success ok
            ? ok.Value.ToDictionary(t => t.Id, t => t.Label)
            : new Dictionary<int, string>();
    }

    private async Task<IReadOnlyDictionary<int, string>> LoadSonarrTagsAsync(
        SweeprrDbContext db, IIntegrationClientFactory factory, CancellationToken ct)
    {
        var conn = await db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Sonarr && c.IsEnabled, ct);

        if (conn is null) return new Dictionary<int, string>();

        var client = await factory.CreateSonarrClientAsync(conn.Id, ct);
        if (client is null) return new Dictionary<int, string>();

        var result = await client.GetTagsAsync(ct);
        return result is HttpResult<IReadOnlyList<SonarrTag>>.Success ok
            ? ok.Value.ToDictionary(t => t.Id, t => t.Label)
            : new Dictionary<int, string>();
    }

    private async Task<IReadOnlyDictionary<int, string>> LoadRadarrProfilesAsync(
        SweeprrDbContext db, IIntegrationClientFactory factory, CancellationToken ct)
    {
        var conn = await db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Radarr && c.IsEnabled, ct);

        if (conn is null) return new Dictionary<int, string>();

        var client = await factory.CreateRadarrClientAsync(conn.Id, ct);
        if (client is null) return new Dictionary<int, string>();

        var result = await client.GetQualityProfilesAsync(ct);
        return result is HttpResult<IReadOnlyList<RadarrQualityProfile>>.Success ok
            ? ok.Value.ToDictionary(p => p.Id, p => p.Name)
            : new Dictionary<int, string>();
    }

    private async Task<IReadOnlyDictionary<int, string>> LoadSonarrProfilesAsync(
        SweeprrDbContext db, IIntegrationClientFactory factory, CancellationToken ct)
    {
        var conn = await db.ServerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Type == ConnectionType.Sonarr && c.IsEnabled, ct);

        if (conn is null) return new Dictionary<int, string>();

        var client = await factory.CreateSonarrClientAsync(conn.Id, ct);
        if (client is null) return new Dictionary<int, string>();

        var result = await client.GetQualityProfilesAsync(ct);
        return result is HttpResult<IReadOnlyList<SonarrQualityProfile>>.Success ok
            ? ok.Value.ToDictionary(p => p.Id, p => p.Name)
            : new Dictionary<int, string>();
    }
}
