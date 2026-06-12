using System;

namespace Sweeprr.API.Models;

public class PlaybackActivity
{
    public int Id { get; set; }
    public string MediaServerItemId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string Username { get; set; } = null!;
    public int PlayCount { get; set; }
    public DateTime LastWatched { get; set; }
    public bool IsFinished { get; set; }
    public double ProgressPercent { get; set; }
    public long PlaybackPositionTicks { get; set; }
    /// <summary>
    /// Set on every upsert. Used for pruning (not LastWatched) to correctly
    /// expire stale records even when play count is not advancing.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
