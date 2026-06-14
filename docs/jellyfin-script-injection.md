# Jellyfin In-UI "Leaving Soon" Banner

Sweeprr can inject a small script into Jellyfin's web client that shows a "Leaving Soon" banner
directly on an item's detail page when it's queued for removal, with a one-click link to the
[extension portal](extension-portal.md) so the viewer can ask for more time.

---

## How It Works

The script is served from your Sweeprr instance at:

```
GET /api/integrations/jellyfin/client-script.js
```

Once loaded inside Jellyfin's web UI, it:

1. Watches the page for navigation (Jellyfin is a single-page app, so it uses a
   `MutationObserver` rather than page-load events).
2. Extracts the current item's ID from the URL (`#/details?id=...`).
3. Calls `GET /api/public/media/{itemId}/status` on your Sweeprr instance — an anonymous,
   CORS-enabled endpoint.
4. If the item is `Pending` or `Approved` in the sweep queue, renders a red "Leaving Soon —
   N days remaining" banner under the title's action buttons, with a **Keep It** button linking
   to `/extend?itemId=...`.

No data about your Jellyfin library is sent anywhere except your own Sweeprr instance.

---

## Setup

1. (Recommended) Set **Settings → Public Base URL** to the externally-reachable URL of your
   Sweeprr instance, e.g. `https://sweeprr.example.com`. This is the URL the script and the
   "Keep It" link will use. If left blank, the script falls back to the request origin, which
   may not be reachable from a client browsing Jellyfin.
2. In Sweeprr, go to **Settings** and find the **Client Script Injection** card. It shows the
   exact script URL and a ready-to-paste `<script>` tag — use the copy buttons.
3. In Jellyfin, go to **Dashboard → General → Custom CSS / JavaScript** (the "Custom JavaScript"
   field) and paste the `<script src="...">` tag.
4. Save in Jellyfin and reload the web client. Open any item that's currently in the sweep
   queue to confirm the banner appears.

---

## Notes

- The script is regenerated on every request with the current `PublicBaseUrl` baked in — if you
  change that setting, just reload Jellyfin's page (no need to re-copy the script tag).
- The response is served with `Cache-Control: no-store` so Jellyfin always gets the latest
  version.
- This endpoint and `/api/public/media/{itemId}/status` are served under a permissive CORS
  policy (`PublicApi`) so they can be called from Jellyfin's own origin.
