namespace Sweeprr.API.Background;

public sealed record ScanResult(
    int RuleGroupId,
    string RuleGroupName,
    int ItemsFlagged,
    TimeSpan Duration);

public interface IScanPipeline
{
    Task<ScanResult> ExecuteAsync(int ruleGroupId, CancellationToken ct = default);
}
