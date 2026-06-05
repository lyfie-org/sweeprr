namespace Sweeprr.API.Integrations.Sonarr.Models;

/// <summary>
/// A single Sonarr history event (grab, import, etc.).
/// <see cref="Date"/> drives "age" calculations for rule evaluation.
/// Both import paths are captured so callers can choose OLDEST vs MOST_RECENT strategy.
/// </summary>
public sealed record SonarrHistoryRecord(
    int            Id,
    int            SeriesId,
    int            EpisodeId,
    string         EventType,
    DateTimeOffset Date,
    string?        ImportedPath,
    string?        DroppedPath);
