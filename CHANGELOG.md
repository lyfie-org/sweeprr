# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.1.0] - 2026-06-13

See [What's New in v1.1](docs/v1.1-what-is-new.md) for a full narrative tour.

### Added
- Playstate persistence in the database — watch/progress state survives restarts.
- Genre and resolution rule conditions, plus new TV-lifecycle conditions (`SeriesEnded`,
  `IsSeasonFinale`, `CutoffMet`).
- `ChangeQualityProfile` sweep action for downgrading Radarr/Sonarr quality profiles instead of
  deleting outright.
- Scoped and tag-based exclusions.
- Optional direct Jellyfin deletion fallback for orphaned (Jellyfin-only) media.
- Dual-instance Radarr/Sonarr cross-instance awareness.
- Bazarr integration — automatic subtitle cleanup after a sweep
  ([docs](docs/bazarr-integration.md)).
- Native "Leaving Soon" Jellyfin collection sync.
- Poster overlay banners for queued items, with automatic restore.
- Disk-space-aware rule fields (`DiskFreeSpacePercent`, `DiskFreeSpaceGb`).
- Sandbox simulator for previewing rule group impact before enabling.
- Media Explorer / Curation Dashboard ("Media" tab).
- Jellyfin Playback Reporting plugin support for richer watch-history rules.
- Jellyfin session control and in-app "Leaving Soon" / pre-sweep alerts.
- Scoped Sweeprr API keys (`spr_live_...`) for automation
  ([docs](docs/api-keys.md)).
- "Request Extension" public portal at `/extend`
  ([docs](docs/extension-portal.md)).
- In-UI "Leaving Soon" banner via Jellyfin custom script injection
  ([docs](docs/jellyfin-script-injection.md)).
- Discord and generic webhook notifications for sweep completion, failsafe trips, pending
  items, and connection errors.
- Rule group import/export as portable JSON.
- Automated database backups to local storage or S3/MinIO, with retention and on-demand trigger
  ([docs](docs/s3-backup.md)).
- `GET /api/system/info` now returns a `releaseDate` alongside `version`.

## [1.0.0] - Initial Release

Initial production-ready release: Jellyfin/Radarr/Sonarr connections, rule-based sweep engine,
sweep queue with manual review, multi-user watch-state aggregation, global dry-run and failsafe
caps, JWT authentication, and the single-container Docker deployment model.
