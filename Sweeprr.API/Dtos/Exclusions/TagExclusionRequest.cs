using System.ComponentModel.DataAnnotations;

namespace Sweeprr.API.Dtos.Exclusions;

public sealed record TagExclusionRequest(
    [Required, MaxLength(200)] string TagName,
    int TagId,
    int ServerConnectionId,
    int? RuleGroupId);
