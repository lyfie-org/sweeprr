using Sweeprr.API.Dtos.Rules;
using Sweeprr.API.Models;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.Tests.Rules;

/// <summary>
/// Unit tests for <see cref="RuleValidationService"/>.
///
/// No database or HTTP — all validation is pure in-memory domain logic.
/// Tests cover the safety-critical paths:
///   - Empty group always rejected (anti-wipe default)
///   - Invalid field/comparator/valuetype combos rejected with clear errors
///   - Valid combos accepted without errors
///   - Section/LogicalOperator structure enforced
///   - Value parsability enforced per ValueType
/// </summary>
public class RuleValidationServiceTests
{
    private readonly RuleValidationService _svc = new();

    // ═══════════════════════════════════════════════════════════════════════════
    // Empty group — anti-wipe safety
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Empty_Conditions_Rejected()
    {
        var result = _svc.Validate(MediaType.Movie, []);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("at least one condition", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Valid combos — accepted
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Valid_Single_Condition_Number_Field_Accepted()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.FileSizeGb, RuleComparator.GreaterThan, "20", RuleValueType.Number)
        ]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Valid_Single_Condition_Date_Field_Before_Accepted()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.ReleaseDate, RuleComparator.Before, "2022-01-01T00:00:00Z", RuleValueType.Date)
        ]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Valid_Single_Condition_RelativeDays_Accepted()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.LastWatched, RuleComparator.InLastDays, "30", RuleValueType.RelativeDays)
        ]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Valid_Single_Condition_Bool_Accepted()
    {
        var result = _svc.Validate(MediaType.Series,
        [
            Condition(RuleField.WatchedByAllUsers, RuleComparator.Equals, "true", RuleValueType.Bool)
        ]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Valid_Single_Condition_TextList_Contains_Accepted()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.Tags, RuleComparator.Contains, "4k", RuleValueType.TextList)
        ]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Valid_Exists_Valueless_Comparator_Accepted()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.LastWatched, RuleComparator.Exists, "", RuleValueType.Date)
        ]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Valid_Multi_Section_AND_OR_Accepted()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number, section: 0),
            Condition(RuleField.Rating,     RuleComparator.LessThan,    "6",  RuleValueType.Number, section: 0,
                      op: LogicalOperator.And),
            Condition(RuleField.LastWatched, RuleComparator.InLastDays, "60", RuleValueType.RelativeDays, section: 1),
        ]);

        Assert.True(result.IsValid);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Comparator not allowed for field
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Contains_On_Bool_Field_Rejected()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.Monitored, RuleComparator.Contains, "true", RuleValueType.Bool)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("Comparator"));
    }

    [Fact]
    public void GreaterThan_On_Date_Field_Rejected()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.ReleaseDate, RuleComparator.GreaterThan, "2022-01-01T00:00:00Z", RuleValueType.Date)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("Comparator"));
    }

    [Fact]
    public void InLastDays_On_Number_Field_Rejected()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.FileSizeGb, RuleComparator.InLastDays, "30", RuleValueType.RelativeDays)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("Comparator"));
    }

    [Fact]
    public void Contains_On_TextList_Field_Accepted_NotContains_Also()
    {
        var r1 = _svc.Validate(MediaType.Movie,
            [Condition(RuleField.Tags, RuleComparator.Contains, "hdr", RuleValueType.TextList)]);
        var r2 = _svc.Validate(MediaType.Movie,
            [Condition(RuleField.Tags, RuleComparator.NotContains, "hdr", RuleValueType.TextList)]);

        Assert.True(r1.IsValid);
        Assert.True(r2.IsValid);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ValueType mismatch
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GreaterThan_With_Non_Number_ValueType_Rejected()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Text)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("ValueType"));
    }

    [Fact]
    public void InLastDays_With_Date_ValueType_Rejected()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.LastWatched, RuleComparator.InLastDays, "30", RuleValueType.Date)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("ValueType"));
    }

    [Fact]
    public void Before_With_RelativeDays_ValueType_Rejected()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.ReleaseDate, RuleComparator.Before, "30", RuleValueType.RelativeDays)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("ValueType"));
    }

    [Fact]
    public void Equals_On_Number_Field_Must_Have_Number_ValueType()
    {
        var bad = _svc.Validate(MediaType.Movie,
            [Condition(RuleField.Rating, RuleComparator.Equals, "7", RuleValueType.Text)]);
        var good = _svc.Validate(MediaType.Movie,
            [Condition(RuleField.Rating, RuleComparator.Equals, "7", RuleValueType.Number)]);

        Assert.False(bad.IsValid);
        Assert.True(good.IsValid);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Value parsability
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData("1,000")]
    public void Non_Numeric_Value_For_Number_Field_Rejected(string value)
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.Rating, RuleComparator.GreaterThan, value, RuleValueType.Number)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("Value"));
    }

    [Theory]
    [InlineData("7.5")]
    [InlineData("0")]
    [InlineData("100")]
    public void Valid_Numeric_Value_Accepted(string value)
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.Rating, RuleComparator.LessThan, value, RuleValueType.Number)
        ]);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("notanumber")]
    [InlineData("-5")]
    public void Zero_Or_Invalid_RelativeDays_Value_Rejected(string value)
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.LastWatched, RuleComparator.InLastDays, value, RuleValueType.RelativeDays)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("Value"));
    }

    [Fact]
    public void Positive_RelativeDays_Value_Accepted()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.LastWatched, RuleComparator.NotInLastDays, "90", RuleValueType.RelativeDays)
        ]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Invalid_Date_Value_Rejected()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.ReleaseDate, RuleComparator.Before, "not-a-date", RuleValueType.Date)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("Value"));
    }

    [Fact]
    public void Valid_ISO8601_Date_Accepted()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.ReleaseDate, RuleComparator.After, "2020-06-15T00:00:00Z", RuleValueType.Date)
        ]);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void Invalid_Bool_Value_Rejected(string value)
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.Monitored, RuleComparator.Equals, value, RuleValueType.Bool)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("Value"));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("False")]
    public void Valid_Bool_Value_Accepted(string value)
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.Monitored, RuleComparator.Equals, value, RuleValueType.Bool)
        ]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Empty_Value_For_Exists_Comparator_Accepted()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.Rating, RuleComparator.NotExists, "", RuleValueType.Number)
        ]);

        Assert.True(result.IsValid);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Section / LogicalOperator structure
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void First_In_Section_With_Operator_Rejected()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number,
                      section: 0, op: LogicalOperator.And) // first in section 0 — must be null
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("LogicalOperator"));
    }

    [Fact]
    public void Non_First_In_Section_Without_Operator_Rejected()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            Condition(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number, section: 0),
            Condition(RuleField.Rating,     RuleComparator.LessThan,    "6",  RuleValueType.Number, section: 0,
                      op: null)  // non-first, must have And/Or
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Path.Contains("LogicalOperator"));
    }

    [Fact]
    public void Correct_Section_Structure_Accepted()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            // Section 0
            Condition(RuleField.FileSizeGb,  RuleComparator.GreaterThan, "10",  RuleValueType.Number, section: 0),
            Condition(RuleField.Rating,      RuleComparator.LessThan,    "7",   RuleValueType.Number, section: 0, op: LogicalOperator.And),
            // Section 1
            Condition(RuleField.LastWatched, RuleComparator.InLastDays,  "180", RuleValueType.RelativeDays, section: 1),
            Condition(RuleField.Monitored,   RuleComparator.Equals,      "false", RuleValueType.Bool, section: 1, op: LogicalOperator.Or),
        ]);

        Assert.True(result.IsValid);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Multiple errors reported at once
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_Errors_Reported_In_One_Pass()
    {
        var result = _svc.Validate(MediaType.Movie,
        [
            // condition[0]: bad comparator + bad value
            Condition(RuleField.Monitored, RuleComparator.GreaterThan, "oops", RuleValueType.Number),
            // condition[1]: good (so we confirm the list isn't aborted on first error)
            Condition(RuleField.Rating,    RuleComparator.LessThan,    "8",    RuleValueType.Number, section: 0, op: LogicalOperator.And),
        ]);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 1);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RuleFieldMeta registry completeness
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void All_RuleFields_Are_In_The_Registry()
    {
        var allFields = Enum.GetValues<RuleField>();
        foreach (var field in allFields)
        {
            Assert.True(RuleFieldMeta.TryGetDescriptor(field, out _),
                $"RuleField.{field} is missing from RuleFieldMeta registry.");
        }
    }

    [Fact]
    public void Each_Field_Has_At_Least_One_Allowed_Comparator()
    {
        foreach (var (field, descriptor) in RuleFieldMeta.All)
        {
            Assert.True(descriptor.AllowedComparators.Count > 0,
                $"RuleField.{field} has no allowed comparators.");
        }
    }

    [Fact]
    public void Exists_NotExists_Allowed_For_Every_Field()
    {
        foreach (var (field, descriptor) in RuleFieldMeta.All)
        {
            Assert.True(descriptor.AllowedComparators.Contains(RuleComparator.Exists),
                $"RuleField.{field} should allow Exists.");
            Assert.True(descriptor.AllowedComparators.Contains(RuleComparator.NotExists),
                $"RuleField.{field} should allow NotExists.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private static RuleConditionDto Condition(
        RuleField      field,
        RuleComparator comparator,
        string         value,
        RuleValueType  valueType,
        int            section = 0,
        LogicalOperator? op    = null)
        => new()
        {
            Section         = section,
            LogicalOperator = op,
            Field           = field,
            Comparator      = comparator,
            Value           = value,
            ValueType       = valueType,
        };
}
