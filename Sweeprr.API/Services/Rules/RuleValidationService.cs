using Sweeprr.API.Dtos.Rules;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services.Rules;

public sealed class RuleValidationService : IRuleValidationService
{
    public RuleValidationResult Validate(MediaType groupMediaType, IReadOnlyList<RuleConditionDto> conditions)
    {
        var errors = new List<RuleValidationError>();

        // ── Anti-wipe safety: empty groups match nothing — reject outright ───
        if (conditions.Count == 0)
        {
            errors.Add(new("Conditions", "A rule group must have at least one condition. An empty group matches nothing and cannot be saved as enabled."));
            return RuleValidationResult.Fail(errors);
        }

        // Track the first condition index per section for operator validation
        var firstInSection = new Dictionary<int, int>(); // section → first condition index

        for (var i = 0; i < conditions.Count; i++)
        {
            var c = conditions[i];
            var prefix = $"Conditions[{i}]";

            // ── Field applicability for the group's MediaType ────────────────
            if (!RuleFieldMeta.TryGetDescriptor(c.Field, out var descriptor))
            {
                errors.Add(new(prefix + ".Field", $"Unknown field '{c.Field}'."));
                continue;
            }

            if (!descriptor.ApplicableMediaTypes.Contains(groupMediaType))
            {
                errors.Add(new(prefix + ".Field",
                    $"Field '{c.Field}' is not applicable to media type '{groupMediaType}'."));
            }

            // ── Comparator allowed for this field ────────────────────────────
            if (!descriptor.AllowedComparators.Contains(c.Comparator))
            {
                errors.Add(new(prefix + ".Comparator",
                    $"Comparator '{c.Comparator}' is not valid for field '{c.Field}'. " +
                    $"Allowed: {string.Join(", ", descriptor.AllowedComparators)}."));
            }
            else
            {
                // ── ValueType consistency ────────────────────────────────────
                ValidateValueType(c, descriptor, prefix, errors);

                // ── Value parsability (skip for valueless comparators) ───────
                if (!RuleFieldMeta.IsValueless(c.Comparator))
                    ValidateValue(c, prefix, errors);
            }

            // ── Section/operator structure ───────────────────────────────────
            if (!firstInSection.ContainsKey(c.Section))
            {
                firstInSection[c.Section] = i;

                // First condition in a section must have no logical operator
                if (c.LogicalOperator.HasValue)
                    errors.Add(new(prefix + ".LogicalOperator",
                        $"The first condition in section {c.Section} must have a null LogicalOperator."));
            }
            else
            {
                // Subsequent conditions in a section must have an operator
                if (!c.LogicalOperator.HasValue)
                    errors.Add(new(prefix + ".LogicalOperator",
                        $"Condition at index {i} is not the first in section {c.Section} and must have And or Or."));
            }
        }

        return errors.Count == 0
            ? RuleValidationResult.Ok()
            : RuleValidationResult.Fail(errors);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void ValidateValueType(
        RuleConditionDto c,
        RuleFieldMeta.FieldDescriptor descriptor,
        string prefix,
        List<RuleValidationError> errors)
    {
        // Valueless comparators (Exists/NotExists) — ValueType must match field's primary type
        if (RuleFieldMeta.IsValueless(c.Comparator))
        {
            if (c.ValueType != descriptor.PrimaryValueType)
                errors.Add(new(prefix + ".ValueType",
                    $"For '{c.Comparator}' on '{c.Field}', ValueType should be '{descriptor.PrimaryValueType}'."));
            return;
        }

        var required = RuleFieldMeta.GetRequiredValueType(c.Comparator);

        if (required is not null)
        {
            // Comparator dictates a specific ValueType
            if (c.ValueType != required)
                errors.Add(new(prefix + ".ValueType",
                    $"Comparator '{c.Comparator}' requires ValueType '{required}', got '{c.ValueType}'."));
        }
        else
        {
            // Polymorphic comparator (Equals, NotEquals, Contains, NotContains) —
            // ValueType must match the field's primary type, or TextList for Contains/NotContains on TextList fields
            var expected = descriptor.PrimaryValueType;
            if (c.ValueType != expected)
                errors.Add(new(prefix + ".ValueType",
                    $"Field '{c.Field}' with comparator '{c.Comparator}' requires ValueType '{expected}', got '{c.ValueType}'."));
        }
    }

    private static void ValidateValue(
        RuleConditionDto c,
        string prefix,
        List<RuleValidationError> errors)
    {
        var value = c.Value?.Trim() ?? string.Empty;

        switch (c.ValueType)
        {
            case RuleValueType.Number:
                // Float style: no thousand separators — avoids locale ambiguity in stored rule values.
                if (!decimal.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    errors.Add(new(prefix + ".Value", $"Value '{value}' is not a valid number."));
                break;

            case RuleValueType.RelativeDays:
                if (!uint.TryParse(value, out var days) || days == 0)
                    errors.Add(new(prefix + ".Value",
                        $"Value '{value}' must be a positive integer (number of days)."));
                break;

            case RuleValueType.Date:
                if (!DateTime.TryParse(value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out _))
                    errors.Add(new(prefix + ".Value",
                        $"Value '{value}' is not a valid ISO 8601 date. Example: '2024-01-15T00:00:00Z'."));
                break;

            case RuleValueType.Bool:
                if (!bool.TryParse(value, out _))
                    errors.Add(new(prefix + ".Value", $"Value '{value}' must be 'true' or 'false'."));
                break;

            case RuleValueType.Text:
                if (string.IsNullOrWhiteSpace(value))
                    errors.Add(new(prefix + ".Value", "Value must not be empty for a Text field."));
                break;

            case RuleValueType.TextList:
                if (string.IsNullOrWhiteSpace(value))
                    errors.Add(new(prefix + ".Value", "Value must not be empty for a TextList field."));
                break;
        }
    }
}
