using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Connections;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services;

public class ConnectionService : IConnectionService
{
    private readonly SweeprrDbContext _db;
    private readonly ISecretProtector _protector;

    // ExtraJson schema: {"allowInsecure": bool}
    // Kept minimal now; Sprint 2 adds jellyfinUserId, jellyfinDeviceId, etc.
    private record ConnectionExtras(bool AllowInsecure = false);

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ConnectionService(SweeprrDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<IEnumerable<ConnectionResponse>> GetAllAsync()
    {
        var all = await _db.ServerConnections.AsNoTracking().ToListAsync();
        return all.Select(ToResponse);
    }

    public async Task<ConnectionResponse?> GetByIdAsync(int id)
    {
        var conn = await _db.ServerConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);
        return conn is null ? null : ToResponse(conn);
    }

    public async Task<(ConnectionResponse connection, string? warning)> CreateAsync(ConnectionRequest request)
    {
        var (normalizedUrl, urlWarning) = NormalizeUrl(request.BaseUrl);

        string? warning = null;
        if (await _db.ServerConnections.AnyAsync(c => c.BaseUrl == normalizedUrl))
            warning = $"A connection to '{normalizedUrl}' already exists. Duplicate connections may cause unexpected behavior.";

        var conn = new ServerConnection
        {
            Name = request.Name.Trim(),
            Type = request.Type,
            BaseUrl = normalizedUrl,
            IsEnabled = request.IsEnabled,
            ExtraJson = SerializeExtras(new ConnectionExtras(request.AllowInsecure))
        };

        if (request.ApiKey is not null)
            conn.ApiKeyEncrypted = EncryptKey(request.ApiKey);

        _db.ServerConnections.Add(conn);
        await _db.SaveChangesAsync();

        return (ToResponse(conn), warning ?? urlWarning);
    }

    public async Task<ConnectionResponse?> UpdateAsync(int id, ConnectionRequest request)
    {
        var conn = await _db.ServerConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (conn is null) return null;

        var (normalizedUrl, _) = NormalizeUrl(request.BaseUrl);

        conn.Name = request.Name.Trim();
        conn.Type = request.Type;
        conn.BaseUrl = normalizedUrl;
        conn.IsEnabled = request.IsEnabled;
        conn.ExtraJson = SerializeExtras(new ConnectionExtras(request.AllowInsecure));

        // Null = preserve existing key; non-null (incl. "") = replace
        if (request.ApiKey is not null)
            conn.ApiKeyEncrypted = EncryptKey(request.ApiKey);

        await _db.SaveChangesAsync();
        return ToResponse(conn);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var conn = await _db.ServerConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (conn is null) return false;

        // Sprint 3 will add ServerConnectionId FK to RuleGroup. At that point, block deletion
        // if any RuleGroup references this connection. For now, allow delete unconditionally.

        _db.ServerConnections.Remove(conn);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<string?> GetDecryptedKeyAsync(int id)
    {
        var conn = await _db.ServerConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (conn is null || string.IsNullOrEmpty(conn.ApiKeyEncrypted))
            return null;

        return _protector.Unprotect(conn.ApiKeyEncrypted);
    }

    public async Task PersistTestResultAsync(int id, bool success)
    {
        var conn = await _db.ServerConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (conn is null) return;

        conn.LastConnectedAt = DateTime.UtcNow;
        conn.LastConnectionOk = success;
        await _db.SaveChangesAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private ConnectionResponse ToResponse(ServerConnection conn)
    {
        var extras = DeserializeExtras(conn.ExtraJson);
        var plainKey = string.IsNullOrEmpty(conn.ApiKeyEncrypted)
            ? null
            : _protector.Unprotect(conn.ApiKeyEncrypted);

        return new ConnectionResponse
        {
            Id = conn.Id,
            Name = conn.Name,
            Type = conn.Type,
            BaseUrl = conn.BaseUrl,
            HasKey = !string.IsNullOrEmpty(plainKey),
            MaskedKey = MaskKey(plainKey),
            IsEnabled = conn.IsEnabled,
            AllowInsecure = extras.AllowInsecure,
            LastConnectedAt = conn.LastConnectedAt,
            LastConnectionOk = conn.LastConnectionOk
        };
    }

    private string EncryptKey(string plaintext)
        => string.IsNullOrEmpty(plaintext) ? string.Empty : _protector.Protect(plaintext);

    private static string MaskKey(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        if (plaintext.Length <= 4) return "••••";
        return "••••" + plaintext[^4..];
    }

    /// <summary>
    /// Normalizes a URL: trims whitespace, strips trailing slash, validates scheme.
    /// Returns (normalizedUrl, warning?) where warning is non-null on unusual but accepted input.
    /// Throws <see cref="ArgumentException"/> on invalid URLs.
    /// </summary>
    private static (string url, string? warning) NormalizeUrl(string raw)
    {
        var trimmed = raw.Trim().TrimEnd('/');

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new ArgumentException($"'{raw}' is not a valid URL.");

        if (uri.Scheme is not "http" and not "https")
            throw new ArgumentException($"URL scheme must be http or https, got '{uri.Scheme}'.");

        string? warning = null;
        if (!string.IsNullOrEmpty(uri.PathAndQuery) && uri.PathAndQuery != "/")
            warning = "URL contains a path. Ensure this is the base URL of the service (e.g. 'http://host/radarr' is valid for reverse-proxied installs).";

        return (trimmed, warning);
    }

    private static string SerializeExtras(ConnectionExtras extras)
        => JsonSerializer.Serialize(extras, _jsonOpts);

    private static ConnectionExtras DeserializeExtras(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ConnectionExtras();
        try { return JsonSerializer.Deserialize<ConnectionExtras>(json, _jsonOpts) ?? new ConnectionExtras(); }
        catch { return new ConnectionExtras(); }
    }
}
