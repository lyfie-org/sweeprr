using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sweeprr.API.Dtos.Connections;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

public class ConnectionTestService : IConnectionTestService
{
    private readonly IConnectionService _connections;
    private readonly ILogger<ConnectionTestService> _logger;
    private readonly Func<bool, HttpMessageHandler> _handlerFactory;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const string AppVersion = "1.0.0";

    // Stable Sweeprr server-side device identity for the Jellyfin MediaBrowser protocol.
    // This is an application-level constant; it does NOT represent individual users.
    private const string JellyfinDeviceId = "c4a1b2e3-d5f6-7890-abcd-ef1234567890";

    public ConnectionTestService(
        IConnectionService connections,
        ILogger<ConnectionTestService> logger)
        : this(connections, logger, CreateDefaultHandler)
    {
    }

    // Constructor for unit tests — inject a mock handler factory.
    public ConnectionTestService(
        IConnectionService connections,
        ILogger<ConnectionTestService> logger,
        Func<bool, HttpMessageHandler> handlerFactory)
    {
        _connections = connections;
        _logger = logger;
        _handlerFactory = handlerFactory;
    }

    public async Task<ConnectionTestResult> TestSavedAsync(int connectionId)
    {
        var response = await _connections.GetByIdAsync(connectionId);
        if (response is null)
            return ConnectionTestResult.Fail("Connection not found.");

        var apiKey = await _connections.GetDecryptedKeyAsync(connectionId);
        if (apiKey is null)
            return ConnectionTestResult.Fail("API key is not set or could not be decrypted. Re-enter the key.");

        var result = await TestUnsavedAsync(response.Type, response.BaseUrl, apiKey, response.AllowInsecure);

        // Persist the result regardless of success/failure
        await _connections.PersistTestResultAsync(connectionId, result.Success);

        return result;
    }

    public async Task<ConnectionTestResult> TestUnsavedAsync(
        ConnectionType type, string baseUrl, string apiKey, bool allowInsecure)
    {
        using var client = CreateClient(allowInsecure);

        return type switch
        {
            ConnectionType.Jellyfin => await TestJellyfinAsync(client, baseUrl, apiKey),
            ConnectionType.Radarr => await TestArrAsync(client, baseUrl, apiKey, "Radarr"),
            ConnectionType.Sonarr => await TestArrAsync(client, baseUrl, apiKey, "Sonarr"),
            _ => ConnectionTestResult.Fail($"Unknown connection type '{type}'.")
        };
    }

    // ── Per-type handshakes ──────────────────────────────────────────────────

    private async Task<ConnectionTestResult> TestJellyfinAsync(
        HttpClient client, string baseUrl, string apiKey)
    {
        var url = $"{baseUrl.TrimEnd('/')}/System/Info";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"MediaBrowser Token=\"{apiKey}\", Client=\"Sweeprr\", Device=\"Sweeprr-Server\", DeviceId=\"{JellyfinDeviceId}\", Version=\"{AppVersion}\"");

        return await ExecuteTestAsync(client, request, resp =>
        {
            var serverName = resp.GetProperty("ServerName").GetString();
            var version = resp.GetProperty("Version").GetString();
            return (serverName, version);
        });
    }

    private async Task<ConnectionTestResult> TestArrAsync(
        HttpClient client, string baseUrl, string apiKey, string expectedAppName)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/v3/system/status";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

        return await ExecuteTestAsync(client, request, resp =>
        {
            var appName = resp.GetProperty("appName").GetString() ?? expectedAppName;
            var version = resp.GetProperty("version").GetString();
            return (appName, version);
        });
    }

    // ── Core execution ───────────────────────────────────────────────────────

    private async Task<ConnectionTestResult> ExecuteTestAsync(
        HttpClient client,
        HttpRequestMessage request,
        Func<JsonElement, (string? serverName, string? version)> parseResponse)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request);
            sw.Stop();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return ConnectionTestResult.Fail("401 — Check your API key.");

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return ConnectionTestResult.Fail("403 — Access forbidden. Check key permissions.");

            if (!response.IsSuccessStatusCode)
                return ConnectionTestResult.Fail(
                    $"Unexpected response: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body).RootElement;
            var (serverName, version) = parseResponse(json);

            return ConnectionTestResult.Ok(serverName, version, sw.ElapsedMilliseconds);
        }
        catch (TaskCanceledException)
        {
            return ConnectionTestResult.Fail(
                $"Connection timed out after {Timeout.TotalSeconds:0}s — is the host reachable?");
        }
        catch (HttpRequestException ex)
        {
            var inner = ex.InnerException?.Message ?? ex.Message;
            if (inner.Contains("SSL") || inner.Contains("certificate") || inner.Contains("TLS"))
                return ConnectionTestResult.Fail(
                    "TLS/SSL error — if the server uses a self-signed certificate, enable 'Allow insecure' on this connection.");
            if (inner.Contains("refused") || inner.Contains("actively refused"))
                return ConnectionTestResult.Fail("Connection refused — is the service running on the specified port?");
            if (inner.Contains("resolve") || inner.Contains("DNS"))
                return ConnectionTestResult.Fail("Hostname could not be resolved — check the URL.");

            return ConnectionTestResult.Fail($"Network error: {inner}");
        }
        catch (JsonException)
        {
            return ConnectionTestResult.Fail("Server returned an unexpected response format. Verify this is the correct service URL.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error during connection test.");
            return ConnectionTestResult.Fail($"Unexpected error: {ex.Message}");
        }
    }

    // ── Client factory ───────────────────────────────────────────────────────

    private HttpClient CreateClient(bool allowInsecure)
    {
        var handler = _handlerFactory(allowInsecure);
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout };
    }

    private static HttpMessageHandler CreateDefaultHandler(bool allowInsecure)
    {
        var handler = new HttpClientHandler();
        if (allowInsecure)
        {
            // Intentional: user explicitly opted in per-connection for self-hosted setups.
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    }
}
