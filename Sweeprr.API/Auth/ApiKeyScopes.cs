namespace Sweeprr.API.Auth;

/// <summary>Claim type under which Sweeprr API key scopes are stored on the ClaimsPrincipal.</summary>
public static class ApiKeyClaims
{
    public const string Scope = "scope";
}

/// <summary>Known Sweeprr API key scopes (Story 10.3).</summary>
public static class ApiKeyScopes
{
    public const string ReadSweep = "read:sweep";
    public const string WriteSweep = "write:sweep";
    public const string ExecuteSweep = "execute:sweep";
    public const string Admin = "admin";

    public static readonly string[] All = [ReadSweep, WriteSweep, ExecuteSweep, Admin];
}
