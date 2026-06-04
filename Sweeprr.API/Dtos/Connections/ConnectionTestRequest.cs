using System.ComponentModel.DataAnnotations;
using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Connections;

/// <summary>
/// Used by POST /api/connections/test-unsaved to test credentials
/// before a connection is saved.
/// </summary>
public class ConnectionTestRequest
{
    [Required]
    public ConnectionType Type { get; set; }

    [Required, MinLength(7), MaxLength(500)]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public bool AllowInsecure { get; set; } = false;
}
