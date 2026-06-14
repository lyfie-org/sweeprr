# Automated Backups (Local & S3/MinIO)

Sweeprr can automatically snapshot its database and encryption keys on a schedule — or on
demand — and write the result to a local directory or an S3-compatible bucket (AWS S3, MinIO,
Backblaze B2, etc.).

This complements the manual procedures in [Backup & Restore](backup-restore.md) — once configured,
you no longer need to `docker cp` files by hand.

---

## What Gets Backed Up

Each backup is a single `.zip` archive named `sweeprr-backup-{yyyy-MM-dd-HHmmss}.zip` containing:

- `sweeprr.db` — a consistent snapshot of the database, taken via SQLite's
  `PRAGMA wal_checkpoint(TRUNCATE)` followed by `VACUUM INTO`, so it's safe even while Sweeprr
  is running under WAL mode.
- `keys/` — the entire ASP.NET Core Data Protection key ring from `/config/keys/`, required to
  decrypt stored connection credentials and S3 secret keys after a restore.

---

## Configuration

Go to **Settings → Backup & Restore**.

| Field | Description |
|---|---|
| **Scheduled backups** | Toggle automatic backups on/off. |
| **Destination** | `Local` or `S3 / MinIO`. |
| **Local path** | Directory for local backups. Defaults to `/config/backups`. |
| **S3 endpoint** | Custom endpoint URL for MinIO/S3-compatible services (e.g. `http://minio:9000`). Leave blank to use AWS S3's default endpoints. |
| **Region** | AWS region, e.g. `us-east-1`. |
| **Bucket** | Target bucket name. |
| **Access key** / **Secret key** | S3 credentials. The secret key is encrypted at rest and only ever shown masked (`···XXXX`) after saving. |
| **Retention count** | How many most-recent backups to keep (1–20). Older backups are deleted automatically after each run. |
| **Schedule (cron)** | Standard 5-field cron expression. Default `0 3 * * 0` (every Sunday at 03:00 UTC). |

After saving, the **next scheduled run** time is shown if scheduled backups are enabled.

### Local Destination

Make sure the configured local path is inside (or a subdirectory of) your `/config` volume, so
backups persist across container recreation and are visible from the host.

### S3 / MinIO Destination

- For **AWS S3**, leave **S3 endpoint** blank and set **Region** and **Bucket**.
- For **MinIO** or another S3-compatible service, set **S3 endpoint** to that service's URL
  (e.g. `http://minio:9000`). Sweeprr uses path-style addressing automatically for custom
  endpoints.
- The IAM user/access key needs `s3:PutObject`, `s3:ListBucket`, and `s3:DeleteObject` on the
  target bucket (delete is needed for retention pruning).

---

## Manual Backup

Click **Back Up Now** in the Backup & Restore card to run a backup immediately, regardless of
the schedule. The result (filename and size) is shown as a toast notification, and the backup
appears in the **Backup History** table below.

Every backup run — scheduled or manual, successful or failed — is also recorded in the
**Activity Log** under the `Backup` category.

---

## Restoring from a Backup

1. Download or copy the desired `sweeprr-backup-*.zip` (from your local path or S3 bucket).
2. Stop the Sweeprr container.
3. Extract the archive and replace `/config/sweeprr.db` and the `/config/keys/` directory with
   the extracted `sweeprr.db` and `keys/` contents.
4. Start the Sweeprr container and verify connections still test successfully (this confirms the
   restored keys can decrypt the restored credentials).

> [!CAUTION]
> Always restore `sweeprr.db` and `keys/` together — they come from the same backup archive.
> Mixing a database from one backup with keys from another will make stored credentials
> undecryptable.
