using System;

namespace Sweeprr.API.Integrations.Jellyfin.Models;

/// <summary>
/// One row from the Playback Reporting plugin's submit_custom_query backfill query:
/// total play count and most recent play timestamp for one (item, user) pair.
/// </summary>
public sealed record PlaybackReportRow(string ItemId, string UserId, int PlayCount, DateTime LastPlayed);
