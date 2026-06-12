# Backup & Restore Guide

Sweeprr stores all state, including settings, connections, rules, activity history, and the sweep queue, in the `/config` directory. 

To keep your data safe and ensure you can restore in case of hardware or host failure, follow this guide.

## The `/config` Directory Layout

Inside the volume mounted to `/config`, Sweeprr creates the following structure:
```
/config/
  ├── sweeprr.db         <-- The SQLite database containing all rules, logs, and state
  ├── sweeprr.db-wal     <-- Write-Ahead Log (created during runtime)
  ├── sweeprr.db-shm     <-- Shared memory file (created during runtime)
  ├── keys/              <-- ASP.NET Core Data Protection keys (XML files)
  └── logs/              <-- Serilog rolling text logs
```

---

## The Encryption Key Dependency

> [!CAUTION]
> **Data Protection Key Dependency**:
> Sweeprr encrypts all sensitive connection credentials (Radarr, Sonarr, and Jellyfin API keys) at rest in the database using the ASP.NET Core Data Protection API.
> 
> The encryption keys are stored inside `/config/keys/`. If you backup `sweeprr.db` but **do not** backup the `/config/keys/` directory, you will be unable to read the saved connection credentials upon restoration, resulting in API connection failures. You must backup the database and the keys together.

---

## Backup Procedure

### Method 1: Tar Archive (Recommended)
You can create a compressed backup archive of the `/config` directory.

Run on the host machine (assuming the docker container is named `sweeprr`):
```bash
# Safely stop the container first to prevent SQLite WAL mismatch
docker stop sweeprr

# Create a backup archive
tar -czf sweeprr_backup_$(date +%F).tar.gz -C /var/lib/docker/volumes/sweeprr_config/_data .

# Start the container
docker start sweeprr
```

### Method 2: SQLite Online Backup (Database only, no downtime)
If you want to backup just the database schema and values without stopping the container, you can copy the database file. However, you must still copy the `/config/keys` folder separately.
```bash
# Copy database file
docker cp sweeprr:/config/sweeprr.db ./sweeprr.db
# Copy the keys directory
docker cp sweeprr:/config/keys ./keys
```

---

## Restore Procedure

To restore Sweeprr on a new host or after a reset:

1. Create a new volume (or target folder) for `/config`.
2. Extract the backup archive into that folder.
   ```bash
   tar -xzf sweeprr_backup_XXXX-XX-XX.tar.gz -C /var/lib/docker/volumes/sweeprr_config/_data
   ```
3. Boot the docker container mounting the restored volume:
   ```bash
   docker run -d \
     --name sweeprr \
     -p 8080:8080 \
     -v sweeprr_config:/config \
     lyfie/sweeprr:latest
   ```
4. Verify that the settings load and your connection credentials are decrypted successfully by running a connection test in the settings panel.
