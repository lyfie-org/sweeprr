# API Keys

Sweeprr API keys let scripts, dashboards, and automation tools call the Sweeprr API without
using an admin login session. Each key carries its own set of scopes and can be revoked
independently at any time.

---

## Generating a Key

1. Go to **Settings → API Keys**.
2. Click **Generate Key**, give it a descriptive name (e.g. `homeassistant`, `grafana-dashboard`),
   and select one or more scopes.
3. Optionally set an expiry date.
4. Click **Create**. The raw key is shown **once** — copy it immediately.

> [!CAUTION]
> The raw key (`spr_live_...`) is never shown again after creation, and Sweeprr only stores a
> SHA-256 hash of it. If you lose it, revoke the key and generate a new one.

The keys list shows a masked form (`spr_live_••••••••XXXX`), creation date, last-used date, and
expiry — never the full key.

---

## Scopes

| Scope | Meaning |
|---|---|
| `read:sweep` | Read-only access to rules, sweep queue, and activity data. |
| `write:sweep` | Create/update rules, exclusions, and settings. |
| `execute:sweep` | Trigger sweep runs (`POST /api/sweep/execute`, `POST /api/sweep/run`). |
| `admin` | Full access — satisfies every scope check, including settings endpoints that are otherwise admin-only. |

A key must have at least one scope. Most endpoints only require a valid, non-expired,
non-revoked key (any scope); a smaller set of sensitive endpoints check for a specific scope:

- `POST /api/sweep/execute` and `POST /api/sweep/run` require `execute:sweep` (or `admin`).
- `/api/settings/**` endpoints (connections, backup, notifications, API keys themselves, etc.)
  require `admin`.

---

## Using a Key

Send the raw key as a Bearer token:

```bash
curl -H "Authorization: Bearer spr_live_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX" \
  https://sweeprr.example.com/api/rulegroups
```

Sweeprr tries the API key scheme first for any Bearer token starting with `spr_live_`; tokens
that don't match this prefix fall through to the normal admin JWT scheme, so API keys and the
web UI session can be used interchangeably against the same endpoints.

---

## Revoking a Key

Click **Revoke** next to a key in **Settings → API Keys**. Revocation is immediate and
permanent — the key row is kept (for audit/last-used history) but `IsActive` is set to false, so
any further requests using that key return `401 Unauthorized`.

> [!NOTE]
> Expired keys (past their `ExpiresAt` date) are also rejected automatically — you don't need to
> revoke a key just because it has expired.
