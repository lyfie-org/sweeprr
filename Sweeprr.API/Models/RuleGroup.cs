namespace Sweeprr.API.Models;

public class RuleGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public MediaType MediaType { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? CronOverride { get; set; }
    public SweepAction Action { get; set; } = SweepAction.DeleteAndUnmonitor;
    public bool AddImportExclusion { get; set; } = false;
    public int? TargetQualityProfileId { get; set; }
    public string? TargetQualityProfileName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Rule> Rules { get; set; } = new List<Rule>();
    public ICollection<SweepItem> SweepItems { get; set; } = new List<SweepItem>();
}
