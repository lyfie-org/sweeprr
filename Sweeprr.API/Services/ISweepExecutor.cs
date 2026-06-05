using Sweeprr.API.Dtos.Sweep;

namespace Sweeprr.API.Services;

/// <summary>
/// Executes approved sweep items: unmonitors in Radarr/Sonarr (when applicable),
/// deletes files, records results, and enforces all failsafe caps.
///
/// Key invariants:
/// <list type="bullet">
///   <item>Unmonitor always precedes delete for <c>DeleteAndUnmonitor</c>. If unmonitor
///         fails, the delete for that item is aborted and the item is marked Failed.</item>
///   <item>A 404 from the *arr on delete is treated as a success (already gone).</item>
///   <item>One item failing never aborts the rest of the batch.</item>
///   <item><see cref="GlobalSettings.GlobalDryRun"/> blocks all destructive calls — items
///         remain Approved and the result reports what would have been swept.</item>
///   <item>The failsafe gate is checked before each destructive action. Items beyond the
///         limit stay Approved with a <c>SkippedReason</c> set.</item>
/// </list>
/// </summary>
public interface ISweepExecutor
{
    Task<ExecuteSweepResult> ExecuteAsync(ExecuteSweepRequest request, CancellationToken ct = default);
}
