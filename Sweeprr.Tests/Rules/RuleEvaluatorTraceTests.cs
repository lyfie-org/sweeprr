using Sweeprr.API.Models;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.Tests.Rules;

/// <summary>
/// Unit tests for <see cref="RuleEvaluator.TraceAsync"/> — the per-clause rule
/// trace used by the Media Explorer "why was this matched?" drawer (Story 9.5).
///
/// Pure in-memory — no database, no HTTP.
/// </summary>
public class RuleEvaluatorTraceTests
{
    private readonly RuleEvaluator _evaluator = new(new ValueResolver());

    private static RuleGroup Group(int id, string name, params Rule[] rules) => new()
    {
        Id = id,
        Name = name,
        MediaType = MediaType.Movie,
        Rules = rules.ToList(),
    };

    private static Rule R(
        RuleField field,
        RuleComparator comparator,
        string value,
        RuleValueType valueType,
        int section = 0,
        LogicalOperator? op = null,
        int id = 1) => new()
    {
        Id = id,
        RuleGroupId = 1,
        Section = section,
        LogicalOperator = op,
        Field = field,
        Comparator = comparator,
        Value = value,
        ValueType = valueType,
    };

    private static MediaContext Movie(string id = "1", string title = "Test Movie") => new()
    {
        ItemId = id,
        Title = title,
        MediaType = MediaType.Movie,
    };

    [Fact]
    public async Task EmptyGroup_ReturnsUnmatchedTraceWithNoClauses()
    {
        var group = Group(1, "Empty Group");

        var traces = await _evaluator.TraceAsync(Movie(), [group]);

        var trace = Assert.Single(traces);
        Assert.Equal(1, trace.RuleGroupId);
        Assert.Equal("Empty Group", trace.RuleGroupName);
        Assert.False(trace.Matched);
        Assert.Empty(trace.Clauses);
    }

    [Fact]
    public async Task SingleRule_Matches_RecordsTrueClauseAndMatchedGroup()
    {
        var group = Group(1, "Big Files", R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number));
        var item = new MediaContext { ItemId = "1", Title = "Big Movie", MediaType = MediaType.Movie, FileSizeGb = 35m };

        var traces = await _evaluator.TraceAsync(item, [group]);

        var trace = Assert.Single(traces);
        Assert.True(trace.Matched);
        var clause = Assert.Single(trace.Clauses);
        Assert.Equal(RuleField.FileSizeGb, clause.Field);
        Assert.Equal(RuleComparator.GreaterThan, clause.Comparator);
        Assert.Equal("10", clause.Value);
        Assert.Equal(0, clause.Section);
        Assert.Null(clause.LogicalOperator);
        Assert.True(clause.Result);
    }

    [Fact]
    public async Task SingleRule_DoesNotMatch_RecordsFalseClauseAndUnmatchedGroup()
    {
        var group = Group(1, "Big Files", R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "100", RuleValueType.Number));
        var item = new MediaContext { ItemId = "1", Title = "Small Movie", MediaType = MediaType.Movie, FileSizeGb = 5m };

        var traces = await _evaluator.TraceAsync(item, [group]);

        var trace = Assert.Single(traces);
        Assert.False(trace.Matched);
        var clause = Assert.Single(trace.Clauses);
        Assert.False(clause.Result);
    }

    [Fact]
    public async Task AndSection_RecordsEveryClauseEvenWhenOneIsFalse()
    {
        var group = Group(1, "Compound",
            R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number, section: 0, op: null, id: 1),
            R(RuleField.PlayCount, RuleComparator.LessThan, "5", RuleValueType.Number, section: 0, op: LogicalOperator.And, id: 2));

        // FileSizeGb=20 > 10 → true; PlayCount=10, not < 5 → false
        var item = new MediaContext { ItemId = "1", Title = "Item", MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 10 };

        var traces = await _evaluator.TraceAsync(item, [group]);

        var trace = Assert.Single(traces);
        Assert.False(trace.Matched);
        Assert.Equal(2, trace.Clauses.Count);
        Assert.True(trace.Clauses[0].Result);
        Assert.False(trace.Clauses[1].Result);
    }

    [Fact]
    public async Task TransientFailure_AllClausesRecordNullResult_GroupNotMatched()
    {
        var group = Group(1, "Big Files", R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number));
        var item = new MediaContext
        {
            ItemId = "1",
            Title = "Broken",
            MediaType = MediaType.Movie,
            HasTransientFailure = true,
            TransientFailureReason = "Radarr timed out",
            FileSizeGb = 100m,
        };

        var traces = await _evaluator.TraceAsync(item, [group]);

        var trace = Assert.Single(traces);
        Assert.False(trace.Matched);
        var clause = Assert.Single(trace.Clauses);
        Assert.Null(clause.Result);
    }

    [Fact]
    public async Task MultipleGroups_ReturnsOneTracePerGroupInOrder()
    {
        var matchingGroup = Group(1, "Matches", R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number));
        var nonMatchingGroup = Group(2, "Does Not Match", R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "100", RuleValueType.Number));
        var item = new MediaContext { ItemId = "1", Title = "Item", MediaType = MediaType.Movie, FileSizeGb = 50m };

        var traces = await _evaluator.TraceAsync(item, [matchingGroup, nonMatchingGroup]);

        Assert.Equal(2, traces.Count);
        Assert.Equal(1, traces[0].RuleGroupId);
        Assert.True(traces[0].Matched);
        Assert.Equal(2, traces[1].RuleGroupId);
        Assert.False(traces[1].Matched);
    }

    [Fact]
    public async Task MultiSection_Or_RecordsBothSectionsAndMatchesWhenEitherTrue()
    {
        // Section 0: FileSizeGb > 100 (false for this item)
        // Section 1 (OR): PlayCount < 5 (true for this item)
        var group = Group(1, "Compound",
            R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "100", RuleValueType.Number, section: 0, op: null, id: 1),
            R(RuleField.PlayCount, RuleComparator.LessThan, "5", RuleValueType.Number, section: 1, op: LogicalOperator.Or, id: 2));

        var item = new MediaContext { ItemId = "1", Title = "Item", MediaType = MediaType.Movie, FileSizeGb = 5m, PlayCount = 1 };

        var traces = await _evaluator.TraceAsync(item, [group]);

        var trace = Assert.Single(traces);
        Assert.True(trace.Matched);
        Assert.Equal(2, trace.Clauses.Count);
        Assert.False(trace.Clauses[0].Result);
        Assert.True(trace.Clauses[1].Result);
        Assert.Equal(LogicalOperator.Or, trace.Clauses[1].LogicalOperator);
    }
}
