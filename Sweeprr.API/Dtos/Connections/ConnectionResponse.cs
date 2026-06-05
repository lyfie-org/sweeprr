using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Connections;

public class ConnectionResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ConnectionType Type { get; set; }
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Masked representation of the stored key (e.g. "••••abcd").
    /// The raw key is never returned to the client.
    /// </summary>
    public string MaskedKey { get; set; } = string.Empty;

    /// <summary>True when a key is stored, regardless of the masked value.</summary>
    public bool HasKey { get; set; }

    public bool IsEnabled { get; set; }
    public bool AllowInsecure { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public bool? LastConnectionOk { get; set; }
}
