using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sweeprr.API.Integrations.Jellyfin.Models;

namespace Sweeprr.API.Integrations.Jellyfin.Dto;

/// <summary>
/// Defensive DTO for the jellyfin-plugin-playback-reporting `submit_custom_query` response,
/// which is shaped as <c>{"colums": [...], "results": [[...], ...]}</c>. The plugin's own
/// JSON key has historically been misspelled "colums" — accept "columns" too in case a
/// future plugin version fixes the typo.
/// </summary>
public sealed record PlaybackReportQueryResultDto
{
    [JsonPropertyName("colums")]
    public List<string>? Colums { get; init; }

    [JsonPropertyName("columns")]
    public List<string>? Columns { get; init; }

    public List<List<JsonElement>> Results { get; init; } = [];

    /// <summary>
    /// Maps each result row to a <see cref="PlaybackReportRow"/> using the column names
    /// to locate ItemId/UserId/PlayCount/LastPlayed by position. Rows that are missing a
    /// required field or fail to parse are skipped (logged) rather than throwing —
    /// a single malformed row must not abort the entire backfill.
    /// </summary>
    public IReadOnlyList<PlaybackReportRow> ToRows(ILogger? logger = null)
    {
        var columnNames = Columns ?? Colums ?? [];
        var rows = new List<PlaybackReportRow>();

        var itemIdIdx    = columnNames.FindIndex(c => string.Equals(c, "ItemId", StringComparison.OrdinalIgnoreCase));
        var userIdIdx    = columnNames.FindIndex(c => string.Equals(c, "UserId", StringComparison.OrdinalIgnoreCase));
        var playCountIdx = columnNames.FindIndex(c => string.Equals(c, "PlayCount", StringComparison.OrdinalIgnoreCase));
        var lastPlayedIdx = columnNames.FindIndex(c => string.Equals(c, "LastPlayed", StringComparison.OrdinalIgnoreCase));

        if (itemIdIdx < 0 || userIdIdx < 0 || playCountIdx < 0 || lastPlayedIdx < 0)
        {
            logger?.LogWarning(
                "[PlaybackReportQueryResultDto] Response missing one or more expected columns (ItemId/UserId/PlayCount/LastPlayed). Got: {Columns}",
                string.Join(", ", columnNames));
            return rows;
        }

        foreach (var row in Results)
        {
            try
            {
                var itemId    = row[itemIdIdx].GetString();
                var userId    = row[userIdIdx].GetString();
                var playCount = row[playCountIdx].GetInt32();
                var lastPlayedRaw = row[lastPlayedIdx].GetString();

                if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(userId) || lastPlayedRaw is null)
                {
                    logger?.LogWarning("[PlaybackReportQueryResultDto] Skipping row with null/empty ItemId, UserId, or LastPlayed");
                    continue;
                }

                if (!DateTime.TryParse(
                        lastPlayedRaw, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out var lastPlayed))
                {
                    logger?.LogWarning("[PlaybackReportQueryResultDto] Skipping row with unparseable LastPlayed value '{Value}'", lastPlayedRaw);
                    continue;
                }

                rows.Add(new PlaybackReportRow(itemId, userId, playCount, lastPlayed));
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[PlaybackReportQueryResultDto] Skipping malformed playback report row");
            }
        }

        return rows;
    }
}
