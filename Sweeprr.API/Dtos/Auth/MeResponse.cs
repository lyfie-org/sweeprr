using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Auth;

public class MeResponse
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
