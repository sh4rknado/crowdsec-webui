#!/bin/bash
# entrypoint.sh - launched by tini as PID 1 inside the container.
#
# Responsibilities:
#   1. Ensure CrowdSec data + config dirs exist (they come from bind mounts).
#   2. Optionally bootstrap a fresh CrowdSec install if /etc/crowdsec is empty.
#   3. Start the CrowdSec daemon (which serves both the log processor and LAPI).
#   4. Register the manager as a bouncer so the LAPI client can authenticate.
#   5. Start the ASP.NET Blazor app in the foreground.
#
# Env vars (override via Quadlet / podman run -e):
#   ADMIN_USER               default admin username (default: admin)
#   ADMIN_PASSWORD           default admin password (REQUIRED on first boot)
#   CROWDSEC_BOUNCER_APIKEY  if set, use this key instead of registering one
#   NO_BOOTSTRAP_CROWDSEC    if "1", do not start a CrowdSec daemon inside the
#                            container (use a host or sidecar CrowdSec instead)
#   LOCAL_API_URL            override the LAPI URL the manager connects to
set -euo pipefail

CONFIG_DIR="${CROWDSEC_CONFIG_DIR:-/etc/crowdsec}"
DATA_DIR="${CROWDSEC_DATA_DIR:-/var/lib/crowdsec/data}"
MANAGER_DB_DIR="$(dirname "${Database__Path:-/var/lib/crowdsec-manager/app.db}")"
LOCAL_API_URL="${LOCAL_API_URL:-http://127.0.0.1:8080}"

mkdir -p "$CONFIG_DIR" "$DATA_DIR" "$MANAGER_DB_DIR" /var/lib/GeoIP /var/lib/crowdsec-manager/backups

log() { echo "[entrypoint] $*"; }

# --- 1. Bootstrap CrowdSec config if missing ---
if [ ! -f "$CONFIG_DIR/config.yaml" ] && [ "${NO_BOOTSTRAP_CROWDSEC:-0}" != "1" ]; then
    log "Bootstrapping CrowdSec config in $CONFIG_DIR ..."
    export CROWDSEC_CONFIG="$CONFIG_DIR"
    # cscli config gen writes a default config.yaml + profiles.yaml + acquis.yaml
    # into $CROWDSEC_CONFIG (defaults to /etc/crowdsec)
    if command -v cscli >/dev/null 2>&1; then
        cscli config gen 2>&1 || {
            log "WARNING: 'cscli config gen' failed - assuming config will be mounted separately"
        }
        # Pull the hub index so collections/parsers/scenarios can be installed later
        cscli hub update 2>&1 || log "WARNING: 'cscli hub update' failed (offline?)"
    fi
fi

# --- 2. Start the CrowdSec daemon ---
if [ "${NO_BOOTSTRAP_CROWDSEC:-0}" != "1" ] && command -v crowdsec >/dev/null 2>&1; then
    log "Starting crowdsec daemon (log processor + LAPI on :8080) ..."
    # crowdsec with no flags runs in foreground; we want it in the background.
    nohup crowdsec -error >/var/log/crowdsec.log 2>&1 &
    CS_PID=$!
    log "CrowdSec PID: $CS_PID"

    # Wait for LAPI to be reachable
    log "Waiting for LAPI at $LOCAL_API_URL ..."
    for i in $(seq 1 30); do
        if curl -fsS "$LOCAL_API_URL/health" >/dev/null 2>&1; then
            log "LAPI is up (after ${i}s)"
            break
        fi
        sleep 1
    done
fi

# --- 3. Register the manager as a bouncer (idempotent) ---
if [ -z "${CROWDSEC_BOUNCER_APIKEY:-}" ]; then
    log "Registering crowdsec-manager bouncer ..."
    if reg=$(cscli bouncers add crowdsec-manager -o raw 2>/dev/null); then
        # cscli prints: "Api key for 'crowdsec-manager':\n\n<key>"
        export CROWDSEC_BOUNCER_APIKEY="$(echo "$reg" | grep -E '^[A-Za-z0-9/+=]{20,}$' | head -n1 | tr -d '[:space:]')"
        if [ -n "$CROWDSEC_BOUNCER_APIKEY" ]; then
            log "Bouncer key registered (length: ${#CROWDSEC_BOUNCER_APIKEY})"
        else
            log "WARNING: could not parse bouncer API key from cscli output"
        fi
    else
        log "WARNING: 'cscli bouncers add' failed - is the LAPI running?"
    fi
fi

# --- 4. Verify an admin password was provided ---
if [ -z "${ADMIN_PASSWORD:-}" ]; then
    log "WARNING: ADMIN_PASSWORD is not set. The admin user will not be seeded."
    log "         Set ADMIN_USER / ADMIN_PASSWORD env vars to log in."
fi

# --- 5. Start the ASP.NET Blazor app in the foreground ---
log "Starting CrowdSec Manager on http://0.0.0.0:8081 ..."
exec dotnet /app/CrowdSecManager.dll
