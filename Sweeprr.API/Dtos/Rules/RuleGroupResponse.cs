using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Rules;

public sealed record RuleConditionResponse(
    int Id,
    int Section,
    LogicalOperator? LogicalOperator,
    RuleField Field,
    RuleComparator Comparator,
    string Value,
    RuleValueType ValueType);

public sealed record RuleGroupResponse(
    int Id,
    string Name,
    string? Description,
    MediaType MediaType,
    bool IsEnabled,
    string? CronOverride,
    SweepAction Action,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<RuleConditionResponse> Conditions);

// ── Fields-metadata endpoint response ───────────────────────────────────────

public sealed record FieldDescriptorResponse(
    string Field,
    string Label,
    string PrimaryValueType,
    IReadOnlyList<string> ApplicableMediaTypes,
    IReadOnlyList<string> AllowedComparators);

public sealed record FieldsMetaResponse(
    IReadOnlyList<FieldDescriptorResponse> Fields);
