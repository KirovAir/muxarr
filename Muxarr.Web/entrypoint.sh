#!/bin/sh
set -e

PUID=${PUID:-1000}
PGID=${PGID:-100}

# Create group if it doesn't exist
grep -q ":$PGID:" /etc/group || groupadd -g $PGID appgroup
# Create user if it doesn't exist
id -u appuser >/dev/null 2>&1 || useradd -m -u $PUID -g $PGID appuser

mkdir -p /data
chown appuser:appgroup /data
chmod 755 /data

cd /app

exec gosu appuser "$@"

