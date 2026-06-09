namespace Sweeprr.API.Dtos.Exclusions;

public sealed record ExclusionResponse(
    int Id,
    string MediaServerItemId,
    string? Reason,
    DateTime CreatedAt,
    int? RuleGroupId,
    string? RuleGroupName,
    DateTime? ExpiresAt);
