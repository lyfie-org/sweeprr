namespace Sweeprr.API.Dtos.Media;

/// <summary>One rule group that currently matches a media item (Status == Pending).</summary>
public sealed record MatchedRuleGroupDto(
    int RuleGroupId,
    string RuleGroupName,
    string MatchReason);
