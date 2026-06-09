using Sweeprr.API.Models;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.Tests.Rules;

/// <summary>
/// Unit tests for <see cref="RuleEvaluator"/>.
///
/// Pure in-memory — no database, no HTTP.
/// Covers:
///   - Anti-wipe: empty group matches nothing
///   - All comparator types (numeric, date, bool, text, text-list, exists)
///   - AND / OR within a section (truth-table)
///   - Multi-section combinations (AND and OR between sections)
///   - Three-section compound expression
///   - Transient failure → item excluded, never matched
///   - Missing value → condition false, item NOT excluded
///   - MatchedRuleSummary accuracy (only satisfied conditions; only true conditions for OR)
///   - Result ordering preserved under parallel evaluation
/// </summary>
public class RuleEvaluatorTests
{
    private readonly RuleEvaluator _evaluator = new(new ValueResolver());

    // ── Builder helpers ───────────────────────────────────────────────────────

    private static RuleGroup Group(params Rule[] rules) => new()
    {
        Id        = 1,
        Name      = "Test Group",
        MediaType = MediaType.Movie,
        Rules     = rules.ToList()
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
        Id             = id,
        RuleGroupId    = 1,
        Section        = section,
        LogicalOperator = op,
        Field          = field,
        Comparator     = comparator,
        Value          = value,
        ValueType      = valueType
    };

    private static MediaContext Movie(
        string title = "Test Movie",
        string id    = "1") => new()
    {
        ItemId    = id,
        Title     = title,
        MediaType = MediaType.Movie
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // Anti-wipe: empty rule group
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmptyGroup_MatchesNothing()
    {
        var results = await _evaluator.EvaluateAsync(Group(), [Movie()]);

        Assert.Single(results);
        Assert.False(results[0].IsMatch);
        Assert.False(results[0].WasExcluded);
    }

    [Fact]
    public async Task EmptyGroup_MultipleItems_AllUnmatched()
    {
        var items   = Enumerable.Range(1, 5).Select(i => Movie(id: i.ToString())).ToList();
        var results = await _evaluator.EvaluateAsync(Group(), items);

        Assert.All(results, r => Assert.False(r.IsMatch));
        Assert.All(results, r => Assert.False(r.WasExcluded));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Numeric comparators
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Numeric_GreaterThan_Matches()
    {
        var group = Group(R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "20", RuleValueType.Number));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Big",   MediaType = MediaType.Movie, FileSizeGb = 35m },
            new MediaContext { ItemId = "2", Title = "Small", MediaType = MediaType.Movie, FileSizeGb = 10m },
            new MediaContext { ItemId = "3", Title = "Equal", MediaType = MediaType.Movie, FileSizeGb = 20m }
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);   // 35 > 20
        Assert.False(results[1].IsMatch);  // 10 < 20
        Assert.False(results[2].IsMatch);  // 20 is not > 20 (strict)
    }

    [Fact]
    public async Task Numeric_LessThan_Matches()
    {
        var group = Group(R(RuleField.PlayCount, RuleComparator.LessThan, "3", RuleValueType.Number));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "A", MediaType = MediaType.Movie, PlayCount = 1 },
            new MediaContext { ItemId = "2", Title = "B", MediaType = MediaType.Movie, PlayCount = 3 },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task Numeric_Equals_Matches()
    {
        var group = Group(R(RuleField.Rating, RuleComparator.Equals, "7.5", RuleValueType.Number));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "A", MediaType = MediaType.Movie, Rating = 7.5m },
            new MediaContext { ItemId = "2", Title = "B", MediaType = MediaType.Movie, Rating = 7.6m },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task Numeric_NotEquals_Matches()
    {
        var group = Group(R(RuleField.Rating, RuleComparator.NotEquals, "10", RuleValueType.Number));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "A", MediaType = MediaType.Movie, Rating = 9m },
            new MediaContext { ItemId = "2", Title = "B", MediaType = MediaType.Movie, Rating = 10m },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Date comparators
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Date_Before_Matches()
    {
        var group = Group(R(RuleField.ReleaseDate, RuleComparator.Before, "2023-01-01", RuleValueType.Date));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Old", MediaType = MediaType.Movie, ReleaseDate = new DateTime(2022, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
            new MediaContext { ItemId = "2", Title = "New", MediaType = MediaType.Movie, ReleaseDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task Date_After_Matches()
    {
        var group = Group(R(RuleField.DateAdded, RuleComparator.After, "2023-01-01", RuleValueType.Date));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Recent", MediaType = MediaType.Movie, DateAdded = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new MediaContext { ItemId = "2", Title = "Old",    MediaType = MediaType.Movie, DateAdded = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task Date_InLastDays_Matches_RecentItem()
    {
        var group = Group(R(RuleField.LastWatched, RuleComparator.InLastDays, "30", RuleValueType.RelativeDays));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Recent", MediaType = MediaType.Movie, LastWatched = DateTime.UtcNow.AddDays(-10) },
            new MediaContext { ItemId = "2", Title = "Old",    MediaType = MediaType.Movie, LastWatched = DateTime.UtcNow.AddDays(-60) },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task Date_NotInLastDays_Matches_StaleItem()
    {
        var group = Group(R(RuleField.LastWatched, RuleComparator.NotInLastDays, "30", RuleValueType.RelativeDays));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Stale",  MediaType = MediaType.Movie, LastWatched = DateTime.UtcNow.AddDays(-60) },
            new MediaContext { ItemId = "2", Title = "Recent", MediaType = MediaType.Movie, LastWatched = DateTime.UtcNow.AddDays(-10) },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Bool comparators
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Bool_Equals_False_Matches_Unwatched()
    {
        var group = Group(R(RuleField.WatchedByAnyUser, RuleComparator.Equals, "false", RuleValueType.Bool));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Unwatched", MediaType = MediaType.Movie, WatchedByAnyUser = false },
            new MediaContext { ItemId = "2", Title = "Watched",   MediaType = MediaType.Movie, WatchedByAnyUser = true },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task Bool_Equals_True_Matches()
    {
        var group = Group(R(RuleField.Monitored, RuleComparator.Equals, "true", RuleValueType.Bool));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Monitored",   MediaType = MediaType.Movie, Monitored = true },
            new MediaContext { ItemId = "2", Title = "Unmonitored", MediaType = MediaType.Movie, Monitored = false },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Text comparators
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Genre_Contains_Matches()
    {
        var group = Group(R(RuleField.Genre, RuleComparator.Contains, "Action", RuleValueType.TextList));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "A", MediaType = MediaType.Movie, Genres = new[] { "Action", "Adventure" } },
            new MediaContext { ItemId = "2", Title = "B", MediaType = MediaType.Movie, Genres = new[] { "Comedy" } },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task Genre_NotContains_Matches()
    {
        var group = Group(R(RuleField.Genre, RuleComparator.NotContains, "Action", RuleValueType.TextList));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "A", MediaType = MediaType.Movie, Genres = new[] { "Comedy" } },
            new MediaContext { ItemId = "2", Title = "B", MediaType = MediaType.Movie, Genres = new[] { "Action" } },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task Text_Equals_QualityProfile_Matches()
    {
        var group = Group(R(RuleField.QualityProfile, RuleComparator.Equals, "Remux-1080p", RuleValueType.Text));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "A", MediaType = MediaType.Movie, QualityProfile = "Remux-1080p" },
            new MediaContext { ItemId = "2", Title = "B", MediaType = MediaType.Movie, QualityProfile = "HD-1080p" },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TextList comparators (Tags)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TextList_Contains_Tag_Matches()
    {
        var group = Group(R(RuleField.Tags, RuleComparator.Contains, "4k", RuleValueType.TextList));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "A", MediaType = MediaType.Movie, Tags = ["4k", "hdr"] },
            new MediaContext { ItemId = "2", Title = "B", MediaType = MediaType.Movie, Tags = ["hd"] },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task TextList_Contains_CaseInsensitive()
    {
        var group = Group(R(RuleField.Tags, RuleComparator.Contains, "4K", RuleValueType.TextList));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "A", MediaType = MediaType.Movie, Tags = ["4k"] }
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
    }

    [Fact]
    public async Task TextList_NotContains_Tag_Matches()
    {
        var group = Group(R(RuleField.Tags, RuleComparator.NotContains, "hdr", RuleValueType.TextList));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "A", MediaType = MediaType.Movie, Tags = ["4k"] },
            new MediaContext { ItemId = "2", Title = "B", MediaType = MediaType.Movie, Tags = ["4k", "hdr"] },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Exists / NotExists
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Exists_True_When_Value_Present()
    {
        var group = Group(R(RuleField.LastWatched, RuleComparator.Exists, "", RuleValueType.Date));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Watched",   MediaType = MediaType.Movie, LastWatched = DateTime.UtcNow },
            new MediaContext { ItemId = "2", Title = "Unwatched", MediaType = MediaType.Movie }   // null
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task NotExists_True_When_Value_Absent()
    {
        var group = Group(R(RuleField.LastWatched, RuleComparator.NotExists, "", RuleValueType.Date));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Unwatched", MediaType = MediaType.Movie },  // null → not-exists = true
            new MediaContext { ItemId = "2", Title = "Watched",   MediaType = MediaType.Movie, LastWatched = DateTime.UtcNow }
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Missing value (definitively absent — not a transient failure)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MissingValue_ConditionFalse_ItemNotExcluded()
    {
        var group = Group(R(RuleField.Rating, RuleComparator.LessThan, "7", RuleValueType.Number));
        // Rating is null (not loaded) but HasTransientFailure = false
        var item  = Movie();

        var results = await _evaluator.EvaluateAsync(group, [item]);

        Assert.False(results[0].IsMatch);
        Assert.False(results[0].WasExcluded);  // Missing ≠ transient failure
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Transient failure — safety-critical
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TransientFailure_ItemExcluded_NeverMatched()
    {
        var group = Group(R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "1", RuleValueType.Number));
        // FileSizeGb = 100 would match, but HasTransientFailure blocks it
        var item  = new MediaContext
        {
            ItemId                = "1",
            Title                 = "Broken",
            MediaType             = MediaType.Movie,
            HasTransientFailure   = true,
            TransientFailureReason = "Radarr timed out",
            FileSizeGb            = 100m
        };

        var results = await _evaluator.EvaluateAsync(group, [item]);

        Assert.False(results[0].IsMatch);
        Assert.True(results[0].WasExcluded);
        Assert.Contains("Radarr timed out", results[0].MatchedRuleSummary);
    }

    [Fact]
    public async Task TransientFailure_DoesNotAffectOtherItems()
    {
        var group = Group(R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "1", RuleValueType.Number));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "OK",     MediaType = MediaType.Movie, FileSizeGb = 50m },
            new MediaContext { ItemId = "2", Title = "Broken", MediaType = MediaType.Movie, HasTransientFailure = true, FileSizeGb = 50m },
            new MediaContext { ItemId = "3", Title = "OK2",    MediaType = MediaType.Movie, FileSizeGb = 50m },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.True(results[1].WasExcluded);
        Assert.False(results[1].IsMatch);
        Assert.True(results[2].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AND within a section
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Section_And_TruthTable()
    {
        var group = Group(
            R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number, section: 0, op: null,                  id: 1),
            R(RuleField.PlayCount,  RuleComparator.LessThan,    "5",  RuleValueType.Number, section: 0, op: LogicalOperator.And, id: 2)
        );
        var items = new[]
        {
            // A=true, B=true  → true
            new MediaContext { ItemId = "1", Title = "TT", MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 3 },
            // A=true, B=false → false
            new MediaContext { ItemId = "2", Title = "TF", MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 10 },
            // A=false, B=true → false
            new MediaContext { ItemId = "3", Title = "FT", MediaType = MediaType.Movie, FileSizeGb = 5m,  PlayCount = 3 },
            // A=false, B=false → false
            new MediaContext { ItemId = "4", Title = "FF", MediaType = MediaType.Movie, FileSizeGb = 5m,  PlayCount = 10 },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
        Assert.False(results[2].IsMatch);
        Assert.False(results[3].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OR within a section
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Section_Or_TruthTable()
    {
        var group = Group(
            R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number, section: 0, op: null,                 id: 1),
            R(RuleField.PlayCount,  RuleComparator.LessThan,    "5",  RuleValueType.Number, section: 0, op: LogicalOperator.Or, id: 2)
        );
        var items = new[]
        {
            // A=true,  B=true  → true
            new MediaContext { ItemId = "1", Title = "TT", MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 3 },
            // A=true,  B=false → true
            new MediaContext { ItemId = "2", Title = "TF", MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 10 },
            // A=false, B=true  → true
            new MediaContext { ItemId = "3", Title = "FT", MediaType = MediaType.Movie, FileSizeGb = 5m,  PlayCount = 3 },
            // A=false, B=false → false
            new MediaContext { ItemId = "4", Title = "FF", MediaType = MediaType.Movie, FileSizeGb = 5m,  PlayCount = 10 },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.True(results[1].IsMatch);
        Assert.True(results[2].IsMatch);
        Assert.False(results[3].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Multi-section: OR between sections
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultiSection_Or_EitherSectionSuffices()
    {
        // Section 0: FileSizeGb > 10
        // Section 1 (OR): PlayCount < 5
        // Final: S0 OR S1
        var group = Group(
            R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number, section: 0, op: null,                id: 1),
            R(RuleField.PlayCount,  RuleComparator.LessThan,    "5",  RuleValueType.Number, section: 1, op: LogicalOperator.Or, id: 2)
        );
        var items = new[]
        {
            // S0=true,  S1=false → true
            new MediaContext { ItemId = "1", Title = "S0",   MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 10 },
            // S0=false, S1=true  → true
            new MediaContext { ItemId = "2", Title = "S1",   MediaType = MediaType.Movie, FileSizeGb = 5m,  PlayCount = 2 },
            // S0=true,  S1=true  → true
            new MediaContext { ItemId = "3", Title = "Both", MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 2 },
            // S0=false, S1=false → false
            new MediaContext { ItemId = "4", Title = "None", MediaType = MediaType.Movie, FileSizeGb = 5m,  PlayCount = 10 },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.True(results[1].IsMatch);
        Assert.True(results[2].IsMatch);
        Assert.False(results[3].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Multi-section: AND between sections
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultiSection_And_BothSectionsMustMatch()
    {
        // Section 0: FileSizeGb > 10
        // Section 1 (AND): PlayCount < 5
        // Final: S0 AND S1
        var group = Group(
            R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number, section: 0, op: null,                 id: 1),
            R(RuleField.PlayCount,  RuleComparator.LessThan,    "5",  RuleValueType.Number, section: 1, op: LogicalOperator.And, id: 2)
        );
        var items = new[]
        {
            // S0=true, S1=true  → true
            new MediaContext { ItemId = "1", Title = "Both", MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 2 },
            // S0=true, S1=false → false
            new MediaContext { ItemId = "2", Title = "S0",   MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 10 },
            // S0=false, S1=true → false
            new MediaContext { ItemId = "3", Title = "S1",   MediaType = MediaType.Movie, FileSizeGb = 5m,  PlayCount = 2 },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
        Assert.False(results[2].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Three-section compound expression
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ThreeSections_CompoundExpression()
    {
        // Section 0:         FileSizeGb > 10
        // Section 1 (AND):   PlayCount  < 5
        // Section 2 (OR):    Rating     > 8
        // Final: ((S0 AND S1) OR S2)
        var group = Group(
            R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "10", RuleValueType.Number, section: 0, op: null,                 id: 1),
            R(RuleField.PlayCount,  RuleComparator.LessThan,    "5",  RuleValueType.Number, section: 1, op: LogicalOperator.And, id: 2),
            R(RuleField.Rating,     RuleComparator.GreaterThan, "8",  RuleValueType.Number, section: 2, op: LogicalOperator.Or,  id: 3)
        );
        var items = new[]
        {
            // (true AND true) OR false = true
            new MediaContext { ItemId = "1", Title = "S01",  MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 2,  Rating = 5m },
            // (false AND false) OR true = true
            new MediaContext { ItemId = "2", Title = "S2",   MediaType = MediaType.Movie, FileSizeGb = 5m,  PlayCount = 10, Rating = 9m },
            // (true AND false) OR false = false
            new MediaContext { ItemId = "3", Title = "None", MediaType = MediaType.Movie, FileSizeGb = 20m, PlayCount = 10, Rating = 5m },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.True(results[1].IsMatch);
        Assert.False(results[2].IsMatch);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MatchedRuleSummary
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MatchedRuleSummary_IncludesSatisfiedConditions()
    {
        var group = Group(
            R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "20", RuleValueType.Number, section: 0, op: null,                 id: 1),
            R(RuleField.PlayCount,  RuleComparator.LessThan,    "5",  RuleValueType.Number, section: 0, op: LogicalOperator.And, id: 2)
        );
        var item = new MediaContext
        {
            ItemId    = "1",
            Title     = "T",
            MediaType = MediaType.Movie,
            FileSizeGb = 35m,
            PlayCount  = 3
        };

        var results = await _evaluator.EvaluateAsync(group, [item]);

        Assert.True(results[0].IsMatch);
        Assert.Contains("FileSizeGb", results[0].MatchedRuleSummary);
        Assert.Contains("PlayCount",  results[0].MatchedRuleSummary);
        Assert.Contains("35",         results[0].MatchedRuleSummary); // actual value present
        Assert.Contains("3",          results[0].MatchedRuleSummary);
    }

    [Fact]
    public async Task MatchedRuleSummary_EmptyOnNoMatch()
    {
        var group   = Group(R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "100", RuleValueType.Number));
        var item    = new MediaContext { ItemId = "1", Title = "T", MediaType = MediaType.Movie, FileSizeGb = 5m };
        var results = await _evaluator.EvaluateAsync(group, [item]);

        Assert.False(results[0].IsMatch);
        Assert.Equal(string.Empty, results[0].MatchedRuleSummary);
    }

    [Fact]
    public async Task MatchedRuleSummary_Or_OnlyTrueConditionsIncluded()
    {
        // First condition false, second true — summary should reference only PlayCount
        var group = Group(
            R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "100", RuleValueType.Number, section: 0, op: null,                id: 1),
            R(RuleField.PlayCount,  RuleComparator.LessThan,    "5",   RuleValueType.Number, section: 0, op: LogicalOperator.Or, id: 2)
        );
        var item = new MediaContext
        {
            ItemId    = "1",
            Title     = "T",
            MediaType = MediaType.Movie,
            FileSizeGb = 5m,  // false: 5 is not > 100
            PlayCount  = 2     // true:  2 < 5
        };

        var results = await _evaluator.EvaluateAsync(group, [item]);

        Assert.True(results[0].IsMatch);
        Assert.DoesNotContain("FileSizeGb", results[0].MatchedRuleSummary);
        Assert.Contains("PlayCount",        results[0].MatchedRuleSummary);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Result ordering under parallel evaluation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResultOrder_PreservedUnderParallelism()
    {
        // 20 items — enough to exercise the parallel path (cap = 8)
        var group = Group(R(RuleField.FileSizeGb, RuleComparator.GreaterThan, "5", RuleValueType.Number));
        var items = Enumerable.Range(1, 20)
            .Select(i => new MediaContext
            {
                ItemId    = i.ToString(),
                Title     = $"Item {i}",
                MediaType = MediaType.Movie,
                FileSizeGb = (decimal)i   // items 6–20 have FileSizeGb > 5
            })
            .ToList();

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.Equal(items.Count, results.Count);
        for (var i = 0; i < items.Count; i++)
        {
            // result[i] must correspond to items[i]
            Assert.Equal(items[i].ItemId, results[i].Item.ItemId);
            // items 1–5 (index 0–4): FileSizeGb 1–5, not > 5
            // items 6–20 (index 5–19): FileSizeGb 6–20, all > 5
            Assert.Equal(i >= 5, results[i].IsMatch);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TV-specific fields: SeriesEnded, IsFinale, CutoffMet (Story 7.3)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SeriesEnded_True_Matches()
    {
        var group = Group(R(RuleField.SeriesEnded, RuleComparator.Equals, "true", RuleValueType.Bool));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Ended",   MediaType = MediaType.Series, SeriesEnded = true  },
            new MediaContext { ItemId = "2", Title = "Ongoing", MediaType = MediaType.Series, SeriesEnded = false },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task SeriesEnded_False_MatchesOngoing()
    {
        var group = Group(R(RuleField.SeriesEnded, RuleComparator.Equals, "false", RuleValueType.Bool));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Ended",   MediaType = MediaType.Series, SeriesEnded = true  },
            new MediaContext { ItemId = "2", Title = "Ongoing", MediaType = MediaType.Series, SeriesEnded = false },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.False(results[0].IsMatch);
        Assert.True(results[1].IsMatch);
    }

    [Fact]
    public async Task SeriesEnded_Null_IsMissing_DoesNotMatch()
    {
        var group = Group(R(RuleField.SeriesEnded, RuleComparator.Equals, "true", RuleValueType.Bool));
        var item  = new MediaContext { ItemId = "1", Title = "No Sonarr data", MediaType = MediaType.Series };

        var results = await _evaluator.EvaluateAsync(group, [item]);

        Assert.False(results[0].IsMatch);
        Assert.False(results[0].WasExcluded);  // Missing ≠ Transient; item not excluded
    }

    [Fact]
    public async Task IsFinale_True_Matches()
    {
        var group = Group(R(RuleField.IsFinale, RuleComparator.Equals, "true", RuleValueType.Bool));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "Finale Season",     MediaType = MediaType.Season, IsFinale = true  },
            new MediaContext { ItemId = "2", Title = "Non-Finale Season",  MediaType = MediaType.Season, IsFinale = false },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task CutoffMet_True_MatchesHighQuality()
    {
        var group = Group(R(RuleField.CutoffMet, RuleComparator.Equals, "true", RuleValueType.Bool));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "HD Quality",   MediaType = MediaType.Movie, CutoffMet = true  },
            new MediaContext { ItemId = "2", Title = "Still Seeking", MediaType = MediaType.Movie, CutoffMet = false },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
    }

    [Fact]
    public async Task CutoffMet_False_MatchesStillSeeking()
    {
        var group = Group(R(RuleField.CutoffMet, RuleComparator.Equals, "false", RuleValueType.Bool));
        var items = new[]
        {
            new MediaContext { ItemId = "1", Title = "HD Quality",    MediaType = MediaType.Movie, CutoffMet = true  },
            new MediaContext { ItemId = "2", Title = "Still Seeking", MediaType = MediaType.Movie, CutoffMet = false },
        };

        var results = await _evaluator.EvaluateAsync(group, items);

        Assert.False(results[0].IsMatch);
        Assert.True(results[1].IsMatch);
    }

    [Fact]
    public async Task SeriesEnded_And_LastWatched_CompoundRule_Matches()
    {
        // Series must be ended AND watched more than 30 days ago
        var cutoff = DateTime.UtcNow.AddDays(-40);
        var group  = Group(
            R(RuleField.SeriesEnded,  RuleComparator.Equals,      "true",  RuleValueType.Bool,         section: 0, op: null,                    id: 1),
            R(RuleField.LastWatched,  RuleComparator.InLastDays,  "30",    RuleValueType.RelativeDays, section: 0, op: LogicalOperator.And,      id: 2)
        );

        // InLastDays=30 → true means watched within 30 days. We want NOT in last 30 days.
        // Using NotInLastDays instead:
        var group2 = Group(
            R(RuleField.SeriesEnded,  RuleComparator.Equals,         "true", RuleValueType.Bool,         section: 0, op: null,                id: 1),
            R(RuleField.LastWatched,  RuleComparator.NotInLastDays,  "30",   RuleValueType.RelativeDays, section: 0, op: LogicalOperator.And, id: 2)
        );

        var items = new[]
        {
            // Ended + watched 40 days ago → matches NotInLastDays(30)
            new MediaContext { ItemId = "1", Title = "Match",     MediaType = MediaType.Series, SeriesEnded = true,  LastWatched = cutoff },
            // Ended + watched recently → doesn't match NotInLastDays(30)
            new MediaContext { ItemId = "2", Title = "TooRecent", MediaType = MediaType.Series, SeriesEnded = true,  LastWatched = DateTime.UtcNow.AddDays(-5) },
            // Not ended + watched 40 days ago → doesn't match SeriesEnded
            new MediaContext { ItemId = "3", Title = "NotEnded",  MediaType = MediaType.Series, SeriesEnded = false, LastWatched = cutoff },
        };

        var results = await _evaluator.EvaluateAsync(group2, items);

        Assert.True(results[0].IsMatch);
        Assert.False(results[1].IsMatch);
        Assert.False(results[2].IsMatch);
    }
}
