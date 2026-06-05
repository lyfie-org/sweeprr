namespace Sweeprr.API.Dtos.Connections;

public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string? ServerName { get; set; }
    public string? Version { get; set; }
    public long? LatencyMs { get; set; }

    /// <summary>
    /// Human-readable failure reason (e.g. "401 — check your API key",
    /// "Connection timed out — is the host reachable?").
    /// Null on success.
    /// </summary>
    public string? ErrorMessage { get; set; }

    public static ConnectionTestResult Ok(string? serverName, string? version, long latencyMs)
        => new() { Success = true, ServerName = serverName, Version = version, LatencyMs = latencyMs };

    public static ConnectionTestResult Fail(string reason)
        => new() { Success = false, ErrorMessage = reason };
}
