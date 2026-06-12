namespace Sweeprr.API.Dtos.Public;

public sealed record ExtensionPortalTokenResponse(string AccessToken, DateTime ExpiresAt, string Username);
