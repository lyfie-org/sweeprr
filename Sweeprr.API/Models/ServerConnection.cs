namespace Sweeprr.API.Models;

public class ServerConnection
{
    public int Id { get; set; }
    public ConnectionType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKeyEncrypted { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastConnectedAt { get; set; }
    public bool? LastConnectionOk { get; set; }
    public string? ExtraJson { get; set; }
}
