# "Request Extension" Public Portal

The extension portal is a small public page (`/extend`) that lets your Jellyfin users ask for
more time on an item that's about to be swept — without needing a Sweeprr admin account.

It's the page linked from the [in-UI "Leaving Soon" banner](jellyfin-script-injection.md) and
from poster overlay QR codes.

---

## How It Works

1. A user reaches `/extend?itemId={jellyfinItemId}` (from the Jellyfin banner, a QR code, or a
   shared link).
2. The page calls the anonymous `GET /api/public/media/{itemId}/status` endpoint to show the
   item's title, poster, and days remaining until removal.
3. If the item is queued for removal, the user signs in with their **Jellyfin username and
   password** (`POST /api/public/auth/jellyfin`).
4. Sweeprr verifies the credentials against Jellyfin directly and issues a short-lived (1 hour)
   "ExtensionPortal" JWT — scoped only to the `/api/public/extend` endpoint. The user's Jellyfin
   password and access token are never stored or returned to the browser.
5. The user clicks **Extend by 14 days**, which calls `POST /api/public/extend`.
6. Sweeprr creates a temporary exclusion for that item (clamped to 1–14 days, defaulting to the
   requested value), removes it from the sweep queue, and restores any poster overlay that was
   applied.

---

## Setup

1. Set **Settings → Public Base URL** to your Sweeprr instance's externally-reachable URL, e.g.
   `https://sweeprr.example.com`. This is the base used for links generated in poster overlays
   and the Jellyfin script injector — without it, links may point at an internal/Docker-only
   hostname that users can't reach.
2. Make sure an enabled **Jellyfin** connection exists in **Settings → Connections** — the
   portal's login proxies authentication to this connection.
3. No further setup is required. The `/extend` route is part of the Sweeprr SPA and the
   `/api/public/**` endpoints are reachable without a Sweeprr login.

---

## Abuse Prevention

- Extension requests are rate-limited per item/user: if the same Jellyfin user already extended
  the same item within the last 7 days, a further request returns
  `429 Too Many Requests` ("This item was already extended recently. Please try again later.").
- Requested extension lengths are clamped server-side to **1–14 days**, regardless of what the
  client sends.
- If the item is no longer in the sweep queue (already swept, or removed by an admin), the
  extend request returns `404 Not Found`.

---

## Security Notes

- The "ExtensionPortal" JWT uses a separate issuer/audience (`sweeprr-extension-portal`) from the
  admin JWT and **cannot** authenticate against any admin-gated endpoint — it only satisfies the
  `ExtensionPortal` authorization policy used by `POST /api/public/extend`.
- `GET /api/public/media/{itemId}/status` is intentionally anonymous and returns only
  non-sensitive display data (title, poster URL, queue status, days remaining) — never admin
  data, connection details, or other users' information.
