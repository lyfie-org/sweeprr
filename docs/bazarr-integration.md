# Bazarr Integration

[Bazarr](https://www.bazarr.media/) manages subtitles for your Radarr movies and Sonarr series.
When Sweeprr removes a movie or episode, any subtitles Bazarr downloaded for it are left behind
unless something cleans them up. Connecting Bazarr closes that gap: after a successful sweep,
Sweeprr asks Bazarr to delete the subtitle files it tracks for that title.

---

## How It Works

- Bazarr cleanup is **best-effort**. It runs *after* a sweep action has already succeeded
  (Radarr/Sonarr deletion, or unmonitor), and a Bazarr failure never changes the outcome of the
  sweep item — it's only logged as a warning.
- At the start of each sweep run, Sweeprr checks that Bazarr is reachable
  (`GET /api/system/status`). If it isn't, subtitle cleanup is skipped for the entire run and a
  warning is logged — the sweep itself proceeds normally.
- In **dry-run** mode, Sweeprr logs what it *would* delete from Bazarr without making any calls.
- For movies, Sweeprr looks up the title in Bazarr by Radarr movie ID
  (`/api/movies?radarrid[]={id}`) and deletes every subtitle Bazarr has on file for it.
- For series, Sweeprr looks up every episode by Sonarr series ID
  (`/api/episodes?seriesid[]={id}`) and deletes the subtitles for each one.

---

## Setup

1. In Sweeprr, go to **Settings → Connections → Add Connection**.
2. Set **Type** to `Bazarr`.
3. Set **Base URL** to your Bazarr instance, e.g. `http://bazarr:6767`.
4. Set **API Key** to a Bazarr API key (Bazarr → Settings → General → Security).
5. Save and run **Test Connection** to confirm Sweeprr can reach Bazarr.
6. Make sure the connection is **enabled** — Sweeprr only uses the first enabled Bazarr
   connection.

No further configuration is needed. Once an enabled Bazarr connection exists, subtitle cleanup
runs automatically as part of every sweep — there's no separate toggle or rule condition.

> [!NOTE]
> Bazarr cleanup only fires for items removed via Radarr/Sonarr (the normal sweep path). It does
> not run for items removed via the [direct Jellyfin deletion fallback](v1.1-what-is-new.md),
> since those items have no Radarr/Sonarr ID to look up in Bazarr.
