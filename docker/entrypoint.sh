#!/bin/sh

# Fallback to standard UID/GID if not provided
PUID=${PUID:-1000}
PGID=${PGID:-1000}

echo "Setting permissions for Sweeprr (PUID: $PUID, PGID: $PGID)..."

# Create group if it doesn't exist
if ! getent group sweeprr >/dev/null; then
    groupadd -g "$PGID" sweeprr 2>/dev/null || addgroup -g "$PGID" -S sweeprr
fi

# Create user if it doesn't exist
if ! getent passwd sweeprr >/dev/null; then
    useradd -u "$PUID" -g "$PGID" -d /config -s /bin/sh -m sweeprr 2>/dev/null || adduser -u "$PUID" -G sweeprr -h /config -s /bin/sh -D sweeprr
fi

# Ensure database and application folders are owned by the configured user
chown -R sweeprr:sweeprr /config
chown -R sweeprr:sweeprr /app

# Execute the application under the specified user
exec gosu sweeprr dotnet Sweeprr.API.dll "$@"
