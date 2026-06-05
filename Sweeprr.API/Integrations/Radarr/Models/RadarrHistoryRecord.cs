namespace Sweeprr.API.Integrations.Radarr.Models;

/// <summary>
/// A single Radarr history event (grab, import, etc.).
/// <see cref="Date"/> drives "age" calculations for rule evaluation.
/// Both import paths are captured so callers can choose OLDEST vs MOST_RECENT strategy.
/// </summary>
public sealed record RadarrHistoryRecord(
    int             Id,
    int             MovieId,
    string          EventType,
    DateTimeOffset  Date,
    string?         ImportedPath,
    string?         DroppedPath);
