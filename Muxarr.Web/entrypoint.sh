#!/bin/sh
set -e

PUID=${PUID:-888}
PGID=${PGID:-888}

# Temporarily set home to /root to avoid issues with /config not being mounted yet
USERHOME=$(grep appuser /etc/passwd | cut -d ":" -f6)
usermod -d /root appuser

# Remap appuser/appgroup to requested PUID/PGID (allow non-unique)
groupmod -o -g "$PGID" appgroup
usermod -o -u "$PUID" appuser

# Restore home directory
usermod -d "$USERHOME" appuser

mkdir -p /config /data
chown appuser:appgroup /app || echo "Warning: Could not set ownership on /app. Remote or read-only mount?"
chown appuser:appgroup /config || echo "Warning: Could not set ownership on /config. Remote or read-only mount?"
chmod 755 /config || true

# Legacy layout only: /data used to hold the database. On the new layout /data
# is the user's media mount and its ownership is not ours to change.
if ! grep -q " /config " /proc/mounts; then
    chown appuser:appgroup /data || echo "Warning: Could not set ownership on /data. Remote or read-only mount?"
    chmod 755 /data || true
fi

cd /app

exec gosu appuser "$@"
