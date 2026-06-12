using System.Security.Cryptography;
using System.Text;

namespace Sweeprr.API.Auth;

/// <summary>
/// Generates and hashes Sweeprr API keys (Story 10.3).
/// Format: <c>spr_live_{32 chars base62}</c>. Only <see cref="Hash"/> output is ever
/// persisted — the raw key is shown to the user once, at creation time.
/// </summary>
public static class ApiKeyGenerator
{
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var sb = new StringBuilder("spr_live_");
        foreach (var b in bytes)
            sb.Append(Chars[b % Chars.Length]);
        return sb.ToString();
    }

    public static string Hash(string rawKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();

    public static string Mask(string rawKey) =>
        $"spr_live_{"••••••••"}{rawKey[^4..]}";
}
