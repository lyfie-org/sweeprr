namespace Sweeprr.API.Models;

/// <summary>
/// Static registry that binds each <see cref="RuleField"/> to its metadata:
/// primary value type, which media types it applies to, and which comparators are legal.
///
/// This is the single source of truth consumed by:
///  - <c>RuleValidationService</c> (backend guard)
///  - <c>GET /api/rulegroups/fields</c> (frontend Rule Builder contract)
/// </summary>
public static class RuleFieldMeta
{
    public sealed record FieldDescriptor(
        RuleValueType                 PrimaryValueType,
        IReadOnlySet<MediaType>       ApplicableMediaTypes,
        IReadOnlySet<RuleComparator>  AllowedComparators);

    // ── Shared applicability sets ────────────────────────────────────────────

    private static readonly IReadOnlySet<MediaType> AllMediaTypes = MSet(
        MediaType.Movie, MediaType.Series, MediaType.Season, MediaType.Episode);

    // ── Comparator groups ────────────────────────────────────────────────────

    private static readonly IReadOnlySet<RuleComparator> NumericComparators = CSet(
        RuleComparator.Equals, RuleComparator.NotEquals,
        RuleComparator.GreaterThan, RuleComparator.LessThan,
        RuleComparator.Exists, RuleComparator.NotExists);

    private static readonly IReadOnlySet<RuleComparator> DateComparators = CSet(
        RuleComparator.Before, RuleComparator.After,
        RuleComparator.InLastDays, RuleComparator.NotInLastDays,
        RuleComparator.Exists, RuleComparator.NotExists);

    private static readonly IReadOnlySet<RuleComparator> BoolComparators = CSet(
        RuleComparator.Equals,
        RuleComparator.Exists, RuleComparator.NotExists);

    private static readonly IReadOnlySet<RuleComparator> TextComparators = CSet(
        RuleComparator.Equals, RuleComparator.NotEquals,
        RuleComparator.Contains, RuleComparator.NotContains,
        RuleComparator.Exists, RuleComparator.NotExists);

    private static readonly IReadOnlySet<RuleComparator> TextListComparators = CSet(
        RuleComparator.Contains, RuleComparator.NotContains,
        RuleComparator.Exists, RuleComparator.NotExists);

    // ── Field registry ───────────────────────────────────────────────────────

    private static readonly Dictionary<RuleField, FieldDescriptor> _registry = new()
    {
        // Watch / usage
        [RuleField.LastWatched]       = new(RuleValueType.Date,     AllMediaTypes, DateComparators),
        [RuleField.PlayCount]         = new(RuleValueType.Number,   AllMediaTypes, NumericComparators),
        [RuleField.WatchedByAnyUser]  = new(RuleValueType.Bool,     AllMediaTypes, BoolComparators),
        [RuleField.WatchedByAllUsers] = new(RuleValueType.Bool,     AllMediaTypes, BoolComparators),
        [RuleField.SeenByUserCount]   = new(RuleValueType.Number,   AllMediaTypes, NumericComparators),

        // Metadata
        [RuleField.ReleaseDate]       = new(RuleValueType.Date,     AllMediaTypes, DateComparators),
        [RuleField.DateAdded]         = new(RuleValueType.Date,     AllMediaTypes, DateComparators),
        [RuleField.Rating]            = new(RuleValueType.Number,   AllMediaTypes, NumericComparators),
        [RuleField.Genre]             = new(RuleValueType.Text,     AllMediaTypes, TextComparators),
        [RuleField.ResolutionHeight]  = new(RuleValueType.Number,   AllMediaTypes, NumericComparators),

        // *arr
        [RuleField.Monitored]         = new(RuleValueType.Bool,     AllMediaTypes, BoolComparators),
        [RuleField.Tags]              = new(RuleValueType.TextList, AllMediaTypes, TextListComparators),
        [RuleField.QualityProfile]    = new(RuleValueType.Text,     AllMediaTypes, TextComparators),
        [RuleField.FileSizeGb]        = new(RuleValueType.Number,   AllMediaTypes, NumericComparators),
    };

    // ── Comparator → expected ValueType ─────────────────────────────────────
    // null means "matches the field's PrimaryValueType" (polymorphic comparators).

    private static readonly Dictionary<RuleComparator, RuleValueType?> _comparatorValueTypes = new()
    {
        [RuleComparator.GreaterThan]   = RuleValueType.Number,
        [RuleComparator.LessThan]      = RuleValueType.Number,
        [RuleComparator.Before]        = RuleValueType.Date,
        [RuleComparator.After]         = RuleValueType.Date,
        [RuleComparator.InLastDays]    = RuleValueType.RelativeDays,
        [RuleComparator.NotInLastDays] = RuleValueType.RelativeDays,
        [RuleComparator.Contains]      = null,
        [RuleComparator.NotContains]   = null,
        [RuleComparator.Equals]        = null,
        [RuleComparator.NotEquals]     = null,
        [RuleComparator.Exists]        = null,
        [RuleComparator.NotExists]     = null,
    };

    // ── Public API ───────────────────────────────────────────────────────────

    public static bool TryGetDescriptor(RuleField field, out FieldDescriptor descriptor)
        => _registry.TryGetValue(field, out descriptor!);

    public static FieldDescriptor GetDescriptor(RuleField field)
        => _registry[field];

    public static IReadOnlyDictionary<RuleField, FieldDescriptor> All => _registry;

    /// <summary>
    /// Returns the ValueType a given comparator requires.
    /// <c>null</c> means the comparator defers to the field's PrimaryValueType.
    /// </summary>
    public static RuleValueType? GetRequiredValueType(RuleComparator comparator)
        => _comparatorValueTypes.TryGetValue(comparator, out var vt) ? vt : null;

    /// <summary>
    /// True when the comparator does not require a user-supplied value
    /// (Exists / NotExists).
    /// </summary>
    public static bool IsValueless(RuleComparator comparator)
        => comparator is RuleComparator.Exists or RuleComparator.NotExists;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlySet<MediaType> MSet(params MediaType[] types)
        => new HashSet<MediaType>(types);

    private static IReadOnlySet<RuleComparator> CSet(params RuleComparator[] comparators)
        => new HashSet<RuleComparator>(comparators);
}
