namespace Sweeprr.API.Models;

public class Exclusion
{
    public int Id { get; set; }
    public string MediaServerItemId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
