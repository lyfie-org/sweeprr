namespace Sweeprr.API.Dtos.Exclusions;

public sealed record TagExclusionResponse(
    int Id,
    string TagName,
    int TagId,
    int ServerConnectionId,
    string ConnectionName,
    int? RuleGroupId,
    string? RuleGroupName);
