namespace Sweeprr.API.Models;

public class SweeprrApiKey
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256(raw key), hex-encoded. The raw key is never stored.</summary>
    public string HashedKey { get; set; } = string.Empty;

    /// <summary>Display form, e.g. "spr_live_••••••••2345" (last 4 chars visible).</summary>
    public string MaskedKey { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    /// <summary>JSON array of scope strings, e.g. ["read:sweep","execute:sweep"].</summary>
    public string Scopes { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
}
