using Sweeprr.API.Integrations.Bazarr;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Sonarr;

namespace Sweeprr.API.Integrations;

/// <summary>
/// Creates typed, Polly-backed HTTP clients for external service connections.
/// Each returned client is configured with the correct base URL, auth headers,
/// and resilience pipeline for the requested <c>ServerConnection</c>.
///
/// Returns <c>null</c> when a connection is not found, disabled, or has no stored key.
/// Callers must guard against null before use.
/// </summary>
public interface IIntegrationClientFactory
{
    /// <summary>
    /// Returns a <see cref="JellyfinClient"/> for the given connection ID,
    /// or <c>null</c> when the connection is missing, disabled, or has no key.
    /// </summary>
    Task<JellyfinClient?> CreateJellyfinClientAsync(
        int connectionId, CancellationToken ct = default);

    /// <summary>
    /// Returns a <see cref="RadarrClient"/> for the given connection ID,
    /// or <c>null</c> when the connection is missing, disabled, or has no key.
    /// </summary>
    Task<RadarrClient?> CreateRadarrClientAsync(
        int connectionId, CancellationToken ct = default);

    /// <summary>
    /// Returns a <see cref="SonarrClient"/> for the given connection ID,
    /// or <c>null</c> when the connection is missing, disabled, or has no key.
    /// </summary>
    Task<SonarrClient?> CreateSonarrClientAsync(
        int connectionId, CancellationToken ct = default);

    /// <summary>
    /// Returns a <see cref="BazarrClient"/> for the first enabled Bazarr connection,
    /// or <c>null</c> when no Bazarr connection is configured or enabled.
    /// </summary>
    Task<BazarrClient?> CreateBazarrClientAsync(CancellationToken ct = default);
}
