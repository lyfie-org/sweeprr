namespace Sweeprr.API.Models;

public class SweepItem
{
    public int Id { get; set; }
    public int RuleGroupId { get; set; }
    public string MediaServerItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public long? SizeBytes { get; set; }
    public string? MatchedRuleSummary { get; set; }
    public SweepItemStatus Status { get; set; } = SweepItemStatus.Pending;
    public int? ArrInstanceId { get; set; }
    public string? TmdbId { get; set; }
    public string? TvdbId { get; set; }
    public string? ImdbId { get; set; }
    public DateTime FlaggedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SweptAt { get; set; }
    public string? SkippedReason { get; set; }
    public int? SeasonNumber { get; set; }
    public string? Genres { get; set; }
    public int? ResolutionHeight { get; set; }
    public string? VideoCodec { get; set; }
    public int? AudioChannels { get; set; }

    public RuleGroup RuleGroup { get; set; } = null!;
}
