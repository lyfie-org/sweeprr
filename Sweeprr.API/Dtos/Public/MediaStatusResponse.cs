namespace Sweeprr.API.Dtos.Public;

public sealed record MediaStatusResponse(bool IsQueued, int? DaysRemaining, string? Title, string? PosterUrl);
