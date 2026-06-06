# Sweeprr Rule Engine Reference Guide

This document defines the strongly-typed fields, comparators, and logical grouping rules used to construct media-cleanup policies in Sweeprr.

## Rule Groups & Media Types

Each **Rule Group** targets a specific media type:
- **Movie**: Evaluated on Radarr movies.
- **Series**: Evaluated on Sonarr series (delete the entire series).
- **Season**: Evaluated on Sonarr seasons (delete a specific season of a show).
- **Episode**: Evaluated on Sonarr episodes (delete individual episodes).

---

## Fields Reference

Sweeprr uses the following fields in the Rule Builder:

### Watch & Usage Metrics (Jellyfin)
- **Last Watched**: The time elapsed since the item was last watched by any (or specific) users.
- **Play Count**: Total number of times this item has been played.
- **Watched By Any User**: True if at least one user has watched the item.
- **Watched By All Users**: True if all users (or all whitelisted users) have watched the item.
- **Seen By User Count**: Number of unique users who have watched the item.

### Media Metadata
- **Release Date**: The date the movie, episode, or series was originally aired/released.
- **Date Added**: The date the item was imported into Jellyfin / the *arr.
- **Rating**: User/critic rating score (0.0 to 10.0).
- **Genre**: Genre tags of the media (text-matching).
- **Resolution Height**: Height of the video stream (e.g. `1080` for 1080p, `2160` for 4K).

### Arr State
- **Monitored**: Whether the item is currently monitored in Radarr or Sonarr.
- **Tags**: Label tags associated with the item in Radarr or Sonarr.
- **Quality Profile**: The quality profile name currently assigned to the item in Radarr or Sonarr.
- **File Size (GB)**: The disk space occupied by the media item.

---

## Comparators & Value Types

Fields are matched using logical operators depending on their underlying data type:

| Value Type | Allowed Comparators | Example Value |
|---|---|---|
| **Number** | `Equals`, `NotEquals`, `GreaterThan`, `LessThan` | `10` (e.g. Resolution Height or Play Count) |
| **Text** | `Equals`, `NotEquals`, `Contains`, `NotContains` | `4K` (Quality Profile) or `Sci-Fi` (Genre) |
| **Date** | `Before`, `After` | `2024-01-01` (Release Date) |
| **RelativeDays** | `InLastDays`, `NotInLastDays` | `30` (Last Watched > 30 days ago) |
| **Bool** | `Equals` | `True` or `False` |

---

## Logical AND/OR Sections

Sweeprr implements an advanced logical grouping structure adapted from Maintainerr, using explicit **AND/OR sections**:

1. **Inside a Section**: Conditions are joined by the chosen logical operator (`AND` or `OR`).
   - Example: `(Last Watched > 30 Days AND Monitored == True)`
2. **Between Sections**: Sections are combined with the leading operator of the subsequent section.
   - Example: `Section 1` `OR` `Section 2` → `(Last Watched > 30 Days) OR (Play Count > 5)`

> [!CAUTION]
> **Anti-Wipe Safety Invariant**:
> An empty rule group will match **nothing**. Sweeprr explicitly prevents empty groups from matching all media to avoid accidental wipeouts of your library.
