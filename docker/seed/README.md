# Dev Stack: First-Boot Setup

After `docker compose up`, each service needs one-time configuration before
its API is usable. Follow the steps below to retrieve API keys and wire them
into Sweeprr's connection settings.

---

## 1. Seed dummy media (optional but recommended)

Run the seed script to populate the shared `media` volume with placeholder
`.mkv` files so Jellyfin and the *arr can see libraries without real content:

```sh
docker compose run --rm sweeprr sh docker/seed/init-media.sh
```

---

## 2. Jellyfin (http://localhost:8097)

1. Open http://localhost:8097 and complete the setup wizard.
2. When asked for a library, point it to `/media` (already mounted).
3. After setup, go to **Dashboard → API Keys** (or **Administration → API Keys**).
4. Create a new key called `sweeprr-dev`.
5. Note the key — you'll enter it in Sweeprr's Connection settings.

**Troubleshooting:**
- If the wizard redirects to a different port, the container may still be starting. Wait ~60s.
- The Jellyfin `deviceId` Sweeprr uses is a stable GUID it generates on first connect.

---

## 3. Radarr (http://localhost:7879)

1. Open http://localhost:7879 and complete the setup wizard.
2. When asked for a root folder, use `/movies` (mounted from the `media` volume).
3. After setup, go to **Settings → General → Security → API Key**.
4. Copy the key and add a Radarr connection in Sweeprr.

**Adding dummy movies to Radarr:**
In Radarr's UI, search for any movie and point it to `/movies`. Radarr won't
actually download anything since there's no download client configured — but it
will show items in its library for Sweeprr to match against.

---

## 4. Sonarr (http://localhost:8990)

1. Open http://localhost:8990 and complete the setup wizard.
2. Use `/tv` as the root series folder.
3. Go to **Settings → General → Security → API Key**, copy the key.
4. Add a Sonarr connection in Sweeprr.

---

## 5. Sweeprr (http://localhost:8080)

Once the above services have API keys:

1. Open http://localhost:8080 and create your admin account (first-run setup).
2. Go to **Connections** and add each service using its API key.
3. Click **Test** on each connection — you should see a green check with the
   server version.

> **Dev hot-reload mode:** the override file maps Sweeprr to port **5000**
> instead of 8080. If you're running with `docker compose up` (which picks up
> the override), use http://localhost:5000 for the API and
> http://localhost:5173 for the Vite dev server.

---

## Port map

| Service  | Host port | Container port | Default port |
|----------|-----------|---------------|--------------|
| Sweeprr  | 8080      | 8080          | — |
| Sweeprr (dev override) | 5000 | 5000 | — |
| Jellyfin | 8097      | 8096          | 8096 |
| Radarr   | 7879      | 7878          | 7878 |
| Sonarr   | 8990      | 8989          | 8989 |

Non-default host ports avoid collisions with a developer's existing media stack.

---

## ARM / Apple Silicon

All images used are multi-arch (`linux/amd64` + `linux/arm64`):
- `jellyfin/jellyfin` — official multi-arch
- `lscr.io/linuxserver/radarr` — linuxserver multi-arch
- `lscr.io/linuxserver/sonarr` — linuxserver multi-arch
- `mcr.microsoft.com/dotnet/*:9.0` — official multi-arch

No extra flags needed on Apple Silicon or Raspberry Pi.

---

## Resetting a service

To wipe a service's config and start fresh:

```sh
docker compose down
docker volume rm sweeprr_<service>_config   # e.g. sweeprr_radarr_config
docker compose up
```

> Warning: this also deletes API keys. You'll need to re-enter them in Sweeprr.
