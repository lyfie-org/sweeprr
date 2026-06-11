using System.Collections.Generic;
using System.Linq;
using Sweeprr.API.Integrations.Jellyfin.Dto;
using Sweeprr.API.Integrations.Jellyfin.Models;

namespace Sweeprr.API.Integrations.Jellyfin;

/// <summary>
/// Typed HTTP client for the Jellyfin media server REST API.
///
/// Auth: All requests carry the MediaBrowser Token header set once at construction.
/// The stable DeviceId identifies Sweeprr as an application — it does NOT represent
/// individual Jellyfin users. Per-user data is accessed by passing userId to the
/// relevant methods, which route to /Users/{userId}/Items endpoints.
///
/// Thread-safety: safe for concurrent use; HttpClient is not mutated after construction.
/// </summary>
public sealed class JellyfinClient : ClientBase
{
    // Must not change after first connection — Jellyfin treats a new DeviceId as a new device
    // and will issue a new session, losing any existing playstate subscriptions.
    private const string DeviceId   = "c4a1b2e3-d5f6-7890-abcd-ef1234567890";
    private const string AppVersion = "1.0.0";
    private const string AppName    = "Sweeprr";
    private const string DeviceName = "Sweeprr-Server";

    public JellyfinClient(
        HttpClient http,
        string baseUrl,
        string apiKey,
        ILogger<JellyfinClient> logger)
        : base(http, baseUrl, logger)
    {
        // Safe: IHttpClientFactory gives us a new HttpClient instance per call, so
        // mutating DefaultRequestHeaders here does not affect the shared pooled handler.
        http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization",
            $"MediaBrowser Token=\"{apiKey}\", " +
            $"Client=\"{AppName}\", " +
            $"Device=\"{DeviceName}\", " +
            $"DeviceId=\"{DeviceId}\", " +
            $"Version=\"{AppVersion}\"");
    }

    // ── Users ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all Jellyfin users. Used to build the user scope for multi-user
    /// watch aggregation (Story 3.3) — must return ALL users, not just admin.
    /// </summary>
    public async Task<HttpResult<IReadOnlyList<JellyfinUser>>> GetUsersAsync(
        CancellationToken ct = default)
    {
        var result = await GetAsync<JellyfinUserDto[]>("/Users", ct);
        return result.Map(dtos =>
            (IReadOnlyList<JellyfinUser>)Array.ConvertAll(dtos, JellyfinUser.From));
    }

    // ── System info ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns server identity and version. Used by the connection test (Story 1.3)
    /// to verify credentials and surface server version in the UI.
    /// </summary>
    public async Task<HttpResult<JellyfinSystemInfo>> GetSystemInfoAsync(
        CancellationToken ct = default)
    {
        var result = await GetAsync<JellyfinSystemInfoDto>("/System/Info", ct);
        return result.Map(JellyfinSystemInfo.From);
    }

    // ── Genres ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all available genres from the Jellyfin server.
    /// </summary>
    public async Task<HttpResult<IReadOnlyList<string>>> GetGenresAsync(
        CancellationToken ct = default)
    {
        var result = await GetAsync<JellyfinGenresResponseDto>("/Genres", ct);
        return result.Map(dto =>
            (IReadOnlyList<string>)dto.Items.Select(g => g.Name).ToList());
    }

    // ── Library enumeration ──────────────────────────────────────────────────

    /// <summary>
    /// Returns one page of library items matching the request parameters.
    /// When request.UserId is set the endpoint is /Users/{userId}/Items, which
    /// embeds per-user UserData inline — required for watch-state evaluation.
    /// </summary>
    public async Task<HttpResult<JellyfinItemsPage>> GetItemsAsync(
        GetItemsRequest request,
        CancellationToken ct = default)
    {
        var path = BuildItemsPath(request) + BuildItemsQuery(request);
        var result = await GetAsync<JellyfinItemsResponseDto>(path, ct);
        return result.Map(dto => new JellyfinItemsPage(
            Items:            Array.ConvertAll(dto.Items, JellyfinItem.From),
            TotalRecordCount: dto.TotalRecordCount,
            StartIndex:       dto.StartIndex));
    }

    /// <summary>
    /// Fetches all pages until the library is exhausted or maxItems is reached.
    /// maxItems caps memory use on very large libraries — callers must not rely on
    /// receiving more than maxItems items even if TotalRecordCount is higher.
    /// Returns TransientFailure on any page error (partial results are not returned
    /// — acting on incomplete data could lead to unsafe deletion decisions).
    /// </summary>
    public async Task<HttpResult<IReadOnlyList<JellyfinItem>>> GetAllItemsAsync(
        GetItemsRequest request,
        int maxItems = 10_000,
        CancellationToken ct = default)
    {
        var all = new List<JellyfinItem>();
        var startIndex = 0;

        while (true)
        {
            var page = request with { StartIndex = startIndex };
            var result = await GetItemsAsync(page, ct);

            if (result is not HttpResult<JellyfinItemsPage>.Success success)
                return result.Map(_ => (IReadOnlyList<JellyfinItem>)all);

            all.AddRange(success.Value.Items);

            if (success.Value.Items.Count == 0          // empty page — server has no more
             || all.Count >= success.Value.TotalRecordCount
             || all.Count >= maxItems)
                break;

            startIndex += page.Limit;
        }

        return HttpResult<IReadOnlyList<JellyfinItem>>.Ok(all);
    }

    // ── Single item ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a single item. When userId is supplied, routes to
    /// /Users/{userId}/Items/{itemId} which includes per-user UserData.
    /// </summary>
    public async Task<HttpResult<JellyfinItem>> GetItemAsync(
        string itemId,
        string? userId = null,
        CancellationToken ct = default)
    {
        var path = userId is not null
            ? $"/Users/{Uri.EscapeDataString(userId)}/Items/{Uri.EscapeDataString(itemId)}"
            : $"/Items/{Uri.EscapeDataString(itemId)}";
        var result = await GetAsync<JellyfinItemDto>(path, ct);
        return result.Map(JellyfinItem.From);
    }

    // ── Per-user watch data ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the watch state for a specific user + item. Fetches the item via the
    /// Users endpoint (which embeds UserData) and extracts the UserData portion.
    /// Returns JellyfinUserData.Empty when UserData is absent in the response
    /// (item exists but has never been played by this user).
    /// </summary>
    public async Task<HttpResult<JellyfinUserData>> GetUserDataAsync(
        string userId,
        string itemId,
        CancellationToken ct = default)
    {
        var path = $"/Users/{Uri.EscapeDataString(userId)}/Items/{Uri.EscapeDataString(itemId)}" +
                   "?Fields=UserData";
        var result = await GetAsync<JellyfinItemDto>(path, ct);
        return result.Map(dto =>
            dto.UserData is not null
                ? JellyfinUserData.From(dto.UserData)
                : JellyfinUserData.Empty);
    }

    // ── Collections ──────────────────────────────────────────────────────────

    /// <summary>Returns all BoxSet collections in the library.</summary>
    public Task<HttpResult<IReadOnlyList<JellyfinItem>>> GetCollectionsAsync(
        CancellationToken ct = default)
        => GetAllItemsAsync(
            new GetItemsRequest
            {
                IncludeItemTypes = ["BoxSet"],
                Fields           = [],
                Recursive        = true,
                Limit            = 500
            },
            maxItems: 2000,
            ct);

    /// <summary>
    /// Creates a new BoxSet collection with the given name.
    /// Returns the new collection's Jellyfin item ID.
    /// </summary>
    public async Task<HttpResult<string>> CreateCollectionAsync(
        string name, CancellationToken ct = default)
    {
        var path   = $"/Collections?Name={Uri.EscapeDataString(name)}&IsLocked=false";
        var result = await PostAsync<JellyfinCollectionResponseDto>(path, body: null, ct);
        return result.Map(dto => dto.Id);
    }

    /// <summary>Returns all items currently in the given collection (one level deep).</summary>
    public Task<HttpResult<IReadOnlyList<JellyfinItem>>> GetCollectionItemsAsync(
        string collectionId, CancellationToken ct = default)
        => GetAllItemsAsync(
            new GetItemsRequest
            {
                ParentId         = collectionId,
                IncludeItemTypes = [],
                Fields           = [],
                Recursive        = false,
                Limit            = 500
            },
            maxItems: 10_000,
            ct);

    /// <summary>Adds items to a collection. Callers must batch to ≤100 IDs per call.</summary>
    public Task<HttpResult<EmptyResponse>> AddItemsToCollectionAsync(
        string collectionId, IEnumerable<string> itemIds, CancellationToken ct = default)
    {
        var ids = Uri.EscapeDataString(string.Join(",", itemIds));
        return PostAsync<EmptyResponse>(
            $"/Collections/{Uri.EscapeDataString(collectionId)}/Items?Ids={ids}",
            body: null, ct);
    }

    /// <summary>Removes items from a collection. Callers must batch to ≤100 IDs per call.</summary>
    public Task<HttpResult<EmptyResponse>> RemoveItemsFromCollectionAsync(
        string collectionId, IEnumerable<string> itemIds, CancellationToken ct = default)
    {
        var ids = Uri.EscapeDataString(string.Join(",", itemIds));
        return DeleteAsync(
            $"/Collections/{Uri.EscapeDataString(collectionId)}/Items?Ids={ids}", ct);
    }

    // ── Images ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the primary poster image for the given item.
    /// Returns raw JPEG/PNG bytes. Callers should treat the format as opaque
    /// and re-encode as needed before uploading.
    /// </summary>
    public async Task<byte[]?> DownloadPosterAsync(
        string itemId,
        CancellationToken ct = default)
    {
        var url = BuildUrl($"/Items/{Uri.EscapeDataString(itemId)}/Images/Primary?maxWidth=600");
        try
        {
            return await Http.GetByteArrayAsync(url, ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[JellyfinClient] Failed to download poster for item {ItemId}", itemId);
            return null;
        }
    }

    /// <summary>
    /// Uploads JPEG bytes as the primary poster for the given item.
    /// Overwrites whatever image Jellyfin currently has stored.
    /// </summary>
    public async Task<bool> UploadPosterAsync(
        string itemId,
        byte[] jpegBytes,
        CancellationToken ct = default)
    {
        var url = BuildUrl($"/Items/{Uri.EscapeDataString(itemId)}/Images/Primary");
        try
        {
            using var content = new ByteArrayContent(jpegBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            using var response = await Http.PostAsync(url, content, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[JellyfinClient] Failed to upload poster for item {ItemId}", itemId);
            return false;
        }
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes an item directly from Jellyfin. This is a fallback path — prefer
    /// deleting via the *arr (which also handles unmonitoring) in Story 4.3.
    /// </summary>
    public Task<HttpResult<EmptyResponse>> DeleteItemAsync(
        string itemId,
        CancellationToken ct = default)
        => DeleteAsync($"/Items/{Uri.EscapeDataString(itemId)}", ct);

    // ── Sessions ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all currently active Jellyfin playback sessions, including each
    /// session's NowPlayingItem (if any). Used by Story 10.2 to match in-progress
    /// playback against the sweep queue and to broadcast pre-sweep warnings.
    /// </summary>
    public async Task<HttpResult<IReadOnlyList<JellyfinSession>>> GetActiveSessionsAsync(
        CancellationToken ct = default)
    {
        var result = await GetAsync<JellyfinSessionDto[]>("/Sessions", ct);
        return result.Map(dtos =>
            (IReadOnlyList<JellyfinSession>)Array.ConvertAll(dtos, JellyfinSession.From));
    }

    /// <summary>
    /// Sends an in-app toast/message to the given session's Jellyfin client.
    /// Best-effort — callers should treat failures as non-fatal (the session may
    /// have ended between the GetActiveSessionsAsync call and this one).
    /// </summary>
    public Task<HttpResult<EmptyResponse>> SendSessionMessageAsync(
        string sessionId,
        string header,
        string text,
        int timeoutMs = 8000,
        CancellationToken ct = default)
    {
        var body = new { Header = header, Text = text, TimeoutMs = timeoutMs };
        return PostAsync<EmptyResponse>(
            $"/Sessions/{Uri.EscapeDataString(sessionId)}/Message", body, ct);
    }

    // ── Playback Reporting plugin ───────────────────────────────────────────────

    /// <summary>
    /// Detects whether the jellyfin-plugin-playback-reporting plugin is installed by
    /// probing one of its report endpoints. Returns true (200 OK), false (404 Not Found),
    /// or null when the result is inconclusive (network error, 5xx, etc.) — callers
    /// should leave the previously-known status unchanged on null.
    /// </summary>
    public async Task<bool?> GetPlaybackReportingPluginStatusAsync(CancellationToken ct = default)
    {
        var url = BuildUrl("/PlaybackReporting/Report/Hourly/User");
        try
        {
            using var response = await Http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode) return true;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;

            Logger.LogWarning(
                "[JellyfinClient] Unexpected status {Status} probing Playback Reporting plugin",
                (int)response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[JellyfinClient] Failed to probe Playback Reporting plugin");
            return null;
        }
    }

    /// <summary>
    /// Runs the Playback Reporting plugin's aggregate backfill query: total play count
    /// and most recent play timestamp per (item, user) pair across all recorded history.
    /// Requires the plugin to be installed — call
    /// <see cref="GetPlaybackReportingPluginStatusAsync"/> first.
    /// </summary>
    public async Task<HttpResult<IReadOnlyList<PlaybackReportRow>>> GetPlaybackReportBackfillAsync(
        CancellationToken ct = default)
    {
        var body = new
        {
            CustomQueryString = "SELECT ItemId, UserId, COUNT(*) as PlayCount, MAX(DateCreated) as LastPlayed FROM PlaybackActivity GROUP BY ItemId, UserId",
            ReplaceUserId = true
        };

        var result = await PostAsync<PlaybackReportQueryResultDto>("/user_usage_stats/submit_custom_query", body, ct);
        return result.Map(dto => dto.ToRows(Logger));
    }

    // ── Path / query building ────────────────────────────────────────────────

    private static string BuildItemsPath(GetItemsRequest request)
        => request.UserId is not null
            ? $"/Users/{Uri.EscapeDataString(request.UserId)}/Items"
            : "/Items";

    private static string BuildItemsQuery(GetItemsRequest request)
    {
        var parts = new List<string>
        {
            $"Recursive={request.Recursive.ToString().ToLowerInvariant()}",
            $"StartIndex={request.StartIndex}",
            $"Limit={request.Limit}"
        };

        if (request.IncludeItemTypes.Count > 0)
            parts.Add("IncludeItemTypes=" +
                Uri.EscapeDataString(string.Join(",", request.IncludeItemTypes)));

        if (request.Fields.Count > 0)
            parts.Add("Fields=" +
                Uri.EscapeDataString(string.Join(",", request.Fields)));

        if (request.ParentId is not null)
            parts.Add("ParentId=" + Uri.EscapeDataString(request.ParentId));

        return "?" + string.Join("&", parts);
    }
}
