using Sweeprr.API.Integrations.Bazarr;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.API.Integrations;

/// <summary>
/// Scoped factory that resolves <see cref="ServerConnection"/> records, decrypts their
/// API keys via <see cref="IConnectionService"/>, and constructs typed HTTP clients
/// backed by the appropriate Polly resilience pipeline.
///
/// Design notes:
/// <list type="bullet">
///   <item>
///     Two named <c>HttpClient</c> pools per service type: <c>"Jellyfin"</c> and
///     <c>"Jellyfin:insecure"</c> (TLS bypass for self-signed certificates). Polly
///     pipelines are identical; only the primary <see cref="HttpClientHandler"/> differs.
///   </item>
///   <item>
///     <c>BaseAddress</c> is NOT set on the <c>HttpClient</c>; typed clients construct
///     absolute URLs from the stored <c>BaseUrl</c> field in <see cref="ClientBase"/>.
///     This keeps the pooled handler state clean and supports multiple instances
///     sharing the same named pool.
///   </item>
/// </list>
/// </summary>
public sealed class IntegrationClientFactory : IIntegrationClientFactory
{
    private readonly IHttpClientFactory _http;
    private readonly IConnectionService _connections;
    private readonly ILoggerFactory _loggerFactory;

    public IntegrationClientFactory(
        IHttpClientFactory http,
        IConnectionService connections,
        ILoggerFactory loggerFactory)
    {
        _http        = http;
        _connections = connections;
        _loggerFactory = loggerFactory;
    }

    // ── Public factory methods ───────────────────────────────────────────────

    public async Task<JellyfinClient?> CreateJellyfinClientAsync(
        int connectionId, CancellationToken ct = default)
    {
        var (conn, apiKey) = await ResolveAsync(connectionId, ConnectionType.Jellyfin, ct);
        if (conn is null || apiKey is null) return null;

        return new JellyfinClient(
            CreateHttpClient("Jellyfin", conn.AllowInsecure),
            conn.BaseUrl,
            apiKey,
            _loggerFactory.CreateLogger<JellyfinClient>());
    }

    public async Task<RadarrClient?> CreateRadarrClientAsync(
        int connectionId, CancellationToken ct = default)
    {
        var (conn, apiKey) = await ResolveAsync(connectionId, ConnectionType.Radarr, ct);
        if (conn is null || apiKey is null) return null;

        return new RadarrClient(
            CreateHttpClient("Radarr", conn.AllowInsecure),
            conn.BaseUrl,
            apiKey,
            _loggerFactory.CreateLogger<RadarrClient>());
    }

    public async Task<SonarrClient?> CreateSonarrClientAsync(
        int connectionId, CancellationToken ct = default)
    {
        var (conn, apiKey) = await ResolveAsync(connectionId, ConnectionType.Sonarr, ct);
        if (conn is null || apiKey is null) return null;

        return new SonarrClient(
            CreateHttpClient("Sonarr", conn.AllowInsecure),
            conn.BaseUrl,
            apiKey,
            _loggerFactory.CreateLogger<SonarrClient>());
    }

    public async Task<BazarrClient?> CreateBazarrClientAsync(CancellationToken ct = default)
    {
        var all = await _connections.GetAllAsync();
        var conn = all.FirstOrDefault(c => c.Type == ConnectionType.Bazarr && c.IsEnabled);
        if (conn is null) return null;

        var apiKey = await _connections.GetDecryptedKeyAsync(conn.Id);
        if (apiKey is null) return null;

        return new BazarrClient(
            CreateHttpClient("Bazarr", conn.AllowInsecure),
            conn.BaseUrl,
            apiKey,
            _loggerFactory.CreateLogger<BazarrClient>());
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Loads the connection and decrypts its key. Returns (null, null) on any failure
    /// (not found, disabled, wrong type, no key, decryption failure).
    /// </summary>
    private async Task<(Dtos.Connections.ConnectionResponse? conn, string? apiKey)> ResolveAsync(
        int connectionId, ConnectionType expectedType, CancellationToken ct)
    {
        var conn = await _connections.GetByIdAsync(connectionId);

        if (conn is null)
            return (null, null);

        if (!conn.IsEnabled)
            return (null, null);

        if (conn.Type != expectedType)
            return (null, null);

        var apiKey = await _connections.GetDecryptedKeyAsync(connectionId);
        return (conn, apiKey); // apiKey may be null if no key stored
    }

    /// <summary>
    /// Returns a fresh <see cref="HttpClient"/> from the named pool.
    /// <c>BaseAddress</c> is intentionally NOT set (see class-level doc).
    /// The Polly resilience pipeline is already wired into the pooled handler.
    /// </summary>
    private HttpClient CreateHttpClient(string serviceType, bool allowInsecure)
    {
        // Insecure variant bypasses TLS validation; Polly pipeline is identical.
        var clientName = allowInsecure ? $"{serviceType}:insecure" : serviceType;
        return _http.CreateClient(clientName);
    }

    /// <summary>
    /// Returns the client name that would be chosen for a given service type and
    /// allow-insecure flag. Used by the DI extension to pre-register both variants.
    /// </summary>
    internal static string ClientName(string serviceType, bool allowInsecure)
        => allowInsecure ? $"{serviceType}:insecure" : serviceType;
}
