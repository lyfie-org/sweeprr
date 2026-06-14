using System.ComponentModel.DataAnnotations;
using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Rules;

/// <summary>
/// Portable representation of a rule group's content (Story 11.2). Excludes local
/// database IDs, schedule overrides, and connection-specific quality profile
/// references — these don't survive a transfer to a different Sweeprr instance.
/// </summary>
public sealed record ExportedRuleGroupDto(
    [Required, MaxLength(200)] string Name,
    string? Description,
    MediaType MediaType,
    SweepAction Action,
    [Required, MinLength(1, ErrorMessage = "A rule group must have at least one condition.")]
    IReadOnlyList<RuleConditionDto> Rules);

/// <summary>Top-level export/import file format for rule groups.</summary>
public sealed record RuleGroupExportEnvelope(
    [Required] string SchemaVersion,
    DateTimeOffset ExportedAt,
    [Required] ExportedRuleGroupDto RuleGroup);
