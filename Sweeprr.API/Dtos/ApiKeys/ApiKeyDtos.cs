namespace Sweeprr.API.Dtos.ApiKeys;

/// <summary>List/detail view of an API key. Never includes the raw key.</summary>
public sealed record ApiKeyResponse(
    int Id,
    string Name,
    string MaskedKey,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    IReadOnlyList<string> Scopes,
    bool IsActive);

public sealed record GenerateApiKeyRequest(
    string Name,
    IReadOnlyList<string> Scopes,
    DateTime? ExpiresAt);

/// <summary>One-time response containing the raw key. Never persisted or returned again.</summary>
public sealed record GenerateApiKeyResponse(
    int Id,
    string Name,
    string RawKey,
    string MaskedKey,
    IReadOnlyList<string> Scopes,
    DateTime? ExpiresAt,
    string Warning);
