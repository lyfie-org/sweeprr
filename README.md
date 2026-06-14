# Sweeprr

Sweeprr is a self-hosted media library management app that integrates Jellyfin with Radarr and Sonarr to automatically clean up ("sweep") watched media. 

Unlike previous clean-up tools (like Cleanarr or Maintainerr), Sweeprr is designed with **first-class Jellyfin WebSocket support**, enabling real-time event tracking and multi-user watch state protection, wrapped in a lightweight, single-port Docker container.

---

## ⚡ The Four Core Guarantees

To ensure you can trust Sweeprr with your library, it enforces four strict safety invariants:

1. **Jellyfin WebSocket Realtime Sync**: Sweeprr connects directly to Jellyfin's WebSocket socket, updating watch statistics immediately when a user finishes playing. If the socket drops, an automated REST backfill runs on reconnect.
2. **Flawless Multi-User Sync**: Before any movie or season is flagged, Sweeprr aggregates the watch state of all users (or a configured whitelist). If User A finished a season but User B is mid-season, the files are protected.
3. **The "Arr" Sync (Unmonitor first)**: Every file deletion is preceded by an unmonitor command to Radarr/Sonarr (and optional import exclusion). This breaks the re-download loop where the Arr immediately grabs the deleted file again.
4. **Hard Failsafes (Anti-Wipe)**: You define global per-run limits (e.g. "never sweep more than 20 items or 50 GB"). If a scan matches more than your limit, Sweeprr halts the sweep and triggers an administrative warning.

---

## 🐳 Deployment

Sweeprr is packaged as a single Docker image containing both the C# backend API and the compiled React frontend, served from a single port.

### Docker Run
```bash
docker run -d \
  --name sweeprr \
  -p 8080:8080 \
  -v sweeprr_config:/config \
  -e PUID=1000 \
  -e PGID=1000 \
  -e TZ=Etc/UTC \
  lyfie/sweeprr:latest
```

### Docker Compose
```yaml
services:
  sweeprr:
    image: lyfie/sweeprr:latest
    container_name: sweeprr
    ports:
      - "8080:8080"
    volumes:
      - sweeprr_config:/config
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Etc/UTC
    restart: unless-stopped

volumes:
  sweeprr_config:
```

> [!IMPORTANT]
> **Volume Persistence**:
> Always mount a persistent volume at `/config`. This folder holds your SQLite database (`sweeprr.db`) and the encryption keys (`/keys`) used to secure your API tokens. Refer to the [Backup & Restore Guide](docs/backup-restore.md) for details.

---

## ⚙️ Initial Configuration

1. **First-Run Setup**: On first boot, visit `http://localhost:8080` to create your administrator account.
2. **Setup Connections**: Navigate to **Connections** and add your servers:
   - **Jellyfin**: Enter your Server URL and an API token (generated in Jellyfin under Dashboard > API Keys).
   - **Radarr/Sonarr**: Enter the instance URL and API key.
   - Click **Test** on each connection card to run a handshake validation.
3. **Configure Settings**: Under **Settings**, configure your global scan schedule (cron format), global dry-run mode, and anti-wipe safety thresholds.

---

## 📊 Rules & The Sweep Queue

Sweeprr organizes cleanup policies into **Rule Groups**:
- **Rules**: Built visually using metadata (Release Date, File Size, Rating) and watch states (Last Watched, Play Count) grouped in logical AND/OR sections.
- **Dry-Run Scan**: Scans run on your schedule. If **Global Dry-Run** is enabled (default), items are scanned and flagged in the **Sweep Queue** without any changes to disk or the Arrs.
- **Manual Actions**: Review matches in the Sweep Queue. You can see the multi-user watch status, the reason a rule triggered, and manually select items to **Sweep** (execute unmonitor + delete) or **Ignore** (which adds a permanent exclusion record).

---

## 📚 Deep-Dive Documentation

- [What's New in v1.1](docs/v1.1-what-is-new.md)
- [Rule Builder Reference](docs/rules.md)
- [Realtime WebSocket Architecture](docs/realtime.md)
- [Multi-User Safety Model](docs/multi-user.md)
- [Radarr & Sonarr API Sync](docs/arr-sync.md)
- [Backup & Restore Procedures](docs/backup-restore.md)
- [Automated Backups (Local & S3/MinIO)](docs/s3-backup.md)
- [Bazarr Subtitle Integration](docs/bazarr-integration.md)
- [API Keys](docs/api-keys.md)
- [Jellyfin In-UI "Leaving Soon" Banner](docs/jellyfin-script-injection.md)
- ["Request Extension" Public Portal](docs/extension-portal.md)
- [Changelog](CHANGELOG.md)

---

## 🛠️ Troubleshooting

### API Connection Failures (401 Unauthorized)
- Verify that your Jellyfin, Radarr, or Sonarr API keys are correct and have not been rotated.
- If you restored your SQLite database without restoring the `/config/keys` directory, Sweeprr will be unable to decrypt your API keys. Re-enter the keys in the Connections UI and save.

### WebSocket Disconnects
- Verify that your Jellyfin URL is accessible from the Sweeprr container and that reverse proxies (e.g. Nginx, Traefik) allow WebSocket connections (`Upgrade` and `Connection` headers).
- Check the **Logs** tab in Sweeprr to view connection status events.

### Deleted Media Re-downloading
- Ensure Sweeprr's connection to Sonarr/Radarr is active.
- Verify that the item is indeed unmonitored in the Arr. If unmonitoring failed, Sweeprr will abort the deletion.
