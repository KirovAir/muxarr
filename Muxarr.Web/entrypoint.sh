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
chown -R appuser:appgroup /config || echo "Warning: Could not set ownership on /config. Remote or read-only mount?"

# Legacy database location; leave /data alone otherwise, it may be a media mount.
if [ -e /data/muxarr.db ]; then
    chown appuser:appgroup /data /data/muxarr.db* || echo "Warning: Could not set ownership on /data. Remote or read-only mount?"
fi

umask "${UMASK:-022}"
cd /app

exec gosu appuser "$@"
