using System.ComponentModel.DataAnnotations;
using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Connections;

public class ConnectionRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public ConnectionType Type { get; set; }

    /// <summary>
    /// Base URL of the service. Trailing slashes are stripped on write.
    /// Must be http:// or https://.
    /// </summary>
    [Required, MinLength(7), MaxLength(500)]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The API key to store (encrypted). Null = preserve existing key on update.
    /// Pass an empty string to explicitly clear the key.
    /// </summary>
    public string? ApiKey { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Opt in to accepting self-signed TLS certificates.
    /// Required for self-hosters running Jellyfin/*arr behind their own CA.
    /// </summary>
    public bool AllowInsecure { get; set; } = false;
}
