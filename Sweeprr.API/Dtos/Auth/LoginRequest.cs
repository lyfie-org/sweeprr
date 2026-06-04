using System.ComponentModel.DataAnnotations;

namespace Sweeprr.API.Dtos.Auth;

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
