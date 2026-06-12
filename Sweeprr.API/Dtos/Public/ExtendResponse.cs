namespace Sweeprr.API.Dtos.Public;

public sealed record ExtendResponse(bool Success, DateTime? NewExpiresAt, string? Error);
