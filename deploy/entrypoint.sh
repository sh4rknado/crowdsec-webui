#!/bin/bash
# entrypoint.sh - launched by tini as PID 1 inside the container.
#
# Responsibilities:
#   1. Ensure CrowdSec data + config dirs exist (they come from bind mounts).
#   2. Optionally bootstrap a fresh CrowdSec install if /etc/crowdsec is empty.
#   3. Start the CrowdSec daemon (which serves both the log processor and LAPI).
#   4. Register the manager as a bouncer so the LAPI client can authenticate.
#   5. Supervise CrowdSec and ASP.NET; stop the sibling process when either exits.
#
# Env vars (override via Quadlet / podman run -e):
#   ADMIN_USER               default admin username (default: admin)
#   ADMIN_PASSWORD           default admin password (REQUIRED on first boot)
#   CROWDSEC_BOUNCER_APIKEY  if set, use this key instead of registering one
#   NO_BOOTSTRAP_CROWDSEC    if "1", do not start a CrowdSec daemon inside the
#                            container (use a host or sidecar CrowdSec instead)
#   LOCAL_API_URL            override the LAPI URL the manager connects to
set -euo pipefail

CONFIG_DIR="${CrowdSec__ConfigDir:-${CROWDSEC_CONFIG_DIR:-/etc/crowdsec}}"
DATA_DIR="${CrowdSec__DataDir:-${CROWDSEC_DATA_DIR:-/var/lib/crowdsec/data}}"
MANAGER_DB_DIR="$(dirname "${Database__Path:-/var/lib/crowdsec-manager/app.db}")"
LOCAL_API_URL="${CrowdSec__LocalApiUrl:-${LOCAL_API_URL:-http://127.0.0.1:8080}}"
GEOIP_DB_PATH="${GeoIp__DatabasePath:-${GEOIP_DB_PATH:-/var/lib/GeoIP/GeoLite2-City.mmdb}}"
HOME_LATITUDE="${GeoIp__HomeLatitude:-${HOME_LATITUDE:-50.8503}}"
HOME_LONGITUDE="${GeoIp__HomeLongitude:-${HOME_LONGITUDE:-4.3517}}"
HOME_LABEL="${GeoIp__HomeLabel:-${HOME_LABEL:-Protected host}}"
BOUNCER_KEY_FILE="${CrowdSec__BouncerApiKeyFile:-${CROWDSEC_BOUNCER_APIKEY_FILE:-${MANAGER_DB_DIR}/crowdsec-manager-bouncer.key}}"
CROWDSEC_DOCKER_START_SCRIPT="${CROWDSEC_DOCKER_START_SCRIPT:-/docker_start.sh}"
CROWDSEC_STAGING_CONFIG_DIR="${CROWDSEC_STAGING_CONFIG_DIR:-/staging/etc/crowdsec}"
CROWDSEC_ENGINE_LOG="${CROWDSEC_ENGINE_LOG:-/dev/stdout}"
CROWDSEC_STARTUP_TIMEOUT_SECONDS="${CROWDSEC_STARTUP_TIMEOUT_SECONDS:-120}"

if ! [[ "$CROWDSEC_STARTUP_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]]; then
    echo "[entrypoint] ERROR: CROWDSEC_STARTUP_TIMEOUT_SECONDS must be a positive integer." >&2
    exit 1
fi

if [ -z "${CROWDSEC_BOUNCER_APIKEY:-}" ] && [ -n "${CrowdSec__BouncerApiKey:-}" ]; then
    CROWDSEC_BOUNCER_APIKEY="${CrowdSec__BouncerApiKey}"
fi

# Keep legacy deployment names and ASP.NET's nested configuration keys in sync.
export CROWDSEC_CONFIG="$CONFIG_DIR"
export CROWDSEC_CONFIG_DIR="$CONFIG_DIR"
export CROWDSEC_DATA_DIR="$DATA_DIR"
export LOCAL_API_URL
export GEOIP_DB_PATH
export HOME_LATITUDE HOME_LONGITUDE HOME_LABEL
export CrowdSec__ConfigDir="$CONFIG_DIR"
export CrowdSec__DataDir="$DATA_DIR"
export CrowdSec__LocalApiUrl="$LOCAL_API_URL"
export GeoIp__DatabasePath="$GEOIP_DB_PATH"
export GeoIp__HomeLatitude="$HOME_LATITUDE"
export GeoIp__HomeLongitude="$HOME_LONGITUDE"
export GeoIp__HomeLabel="$HOME_LABEL"

mkdir -p "$CONFIG_DIR" "$DATA_DIR" "$MANAGER_DB_DIR" "$(dirname "$BOUNCER_KEY_FILE")" /var/lib/GeoIP /var/lib/crowdsec-manager/backups

log() { echo "[entrypoint] $*"; }

APP_PID=""
SHUTDOWN_REQUESTED=0

shutdown_children() {
    SHUTDOWN_REQUESTED=1
    log "Stopping CrowdSec Manager processes ..."
    if [ -n "${APP_PID:-}" ]; then kill -TERM "$APP_PID" 2>/dev/null || true; fi
    if [ -n "${CS_PID:-}" ]; then kill -TERM "$CS_PID" 2>/dev/null || true; fi
}

trap shutdown_children TERM INT

process_is_running() {
    local state
    state="$(ps -o stat= -p "$1" 2>/dev/null || true)"
    [ -n "$state" ] && [[ "$state" != Z* ]]
}

# --- 1. Initialize and start the bundled CrowdSec engine ---
if [ "${NO_BOOTSTRAP_CROWDSEC:-0}" != "1" ]; then
    if [ ! -f "$CONFIG_DIR/config.yaml" ] && [ ! -f "$CROWDSEC_STAGING_CONFIG_DIR/config.yaml" ]; then
        log "ERROR: $CONFIG_DIR/config.yaml is missing and the CrowdSec image has no staged defaults."
        exit 1
    fi
    if [ ! -f "$CROWDSEC_DOCKER_START_SCRIPT" ]; then
        log "ERROR: CrowdSec image startup script not found at $CROWDSEC_DOCKER_START_SCRIPT."
        exit 1
    fi

    log "Starting the official CrowdSec image setup and daemon ..."
    LEVEL_ERROR=true /bin/bash "$CROWDSEC_DOCKER_START_SCRIPT" >"$CROWDSEC_ENGINE_LOG" 2>&1 &
    CS_PID=$!
    log "CrowdSec PID: $CS_PID"

    # Wait for LAPI to be reachable
    log "Waiting for LAPI at $LOCAL_API_URL ..."
    LAPI_READY=0
    for i in $(seq 1 "$CROWDSEC_STARTUP_TIMEOUT_SECONDS"); do
        if [ "$SHUTDOWN_REQUESTED" -eq 1 ]; then
            if wait "$CS_PID"; then :; else :; fi
            exit 0
        fi
        if ! process_is_running "$CS_PID"; then
            log "ERROR: CrowdSec startup exited before LAPI became ready. See $CROWDSEC_ENGINE_LOG."
            if wait "$CS_PID"; then :; else :; fi
            exit 1
        fi
        if curl -fsS "$LOCAL_API_URL/health" >/dev/null 2>&1; then
            log "LAPI is up (after ${i}s)"
            LAPI_READY=1
            break
        fi
        sleep 1
    done
    if [ "$LAPI_READY" -ne 1 ]; then
        log "ERROR: LAPI did not become ready at $LOCAL_API_URL within $CROWDSEC_STARTUP_TIMEOUT_SECONDS seconds. See $CROWDSEC_ENGINE_LOG."
        kill "$CS_PID" 2>/dev/null || true
        if wait "$CS_PID"; then :; else :; fi
        exit 1
    fi
fi

# --- 2. Load or register the manager bouncer key ---
if [ -z "${CROWDSEC_BOUNCER_APIKEY:-}" ] && [ -s "$BOUNCER_KEY_FILE" ]; then
    IFS= read -r CROWDSEC_BOUNCER_APIKEY < "$BOUNCER_KEY_FILE" || true
    if [ -n "$CROWDSEC_BOUNCER_APIKEY" ]; then
        log "Loaded persisted bouncer key."
    fi
fi

if [ -z "${CROWDSEC_BOUNCER_APIKEY:-}" ]; then
    log "Registering crowdsec-manager bouncer ..."
    if reg=$(cscli bouncers add crowdsec-manager -o raw 2>/dev/null); then
        # cscli prints: "Api key for 'crowdsec-manager':\n\n<key>"
        export CROWDSEC_BOUNCER_APIKEY="$(echo "$reg" | grep -E '^[A-Za-z0-9/+=]{20,}$' | head -n1 | tr -d '[:space:]')"
        if [ -n "$CROWDSEC_BOUNCER_APIKEY" ]; then
            log "Bouncer key registered (length: ${#CROWDSEC_BOUNCER_APIKEY})"
            key_tmp="$(mktemp "${BOUNCER_KEY_FILE}.XXXXXX")"
            chmod 0600 "$key_tmp"
            printf '%s\n' "$CROWDSEC_BOUNCER_APIKEY" > "$key_tmp"
            mv -f -- "$key_tmp" "$BOUNCER_KEY_FILE"
            log "Bouncer key saved to persistent application storage."
        else
            log "WARNING: could not parse bouncer API key from cscli output"
        fi
    else
        log "WARNING: 'cscli bouncers add' failed - is the LAPI running?"
    fi
fi

# ASP.NET reads the bouncer token from CrowdSec:BouncerApiKey. Export it after
# registration too, because registration may have generated the token above.
export CrowdSec__BouncerApiKey="${CROWDSEC_BOUNCER_APIKEY:-}"

if [ "$SHUTDOWN_REQUESTED" -eq 1 ]; then
    if [ -n "${CS_PID:-}" ]; then
        if wait "$CS_PID"; then :; else :; fi
    fi
    exit 0
fi

# --- 3. Verify an admin password was provided ---
if [ -z "${ADMIN_PASSWORD:-}" ]; then
    log "WARNING: ADMIN_PASSWORD is not set. The admin user will not be seeded."
    log "         Set ADMIN_USER / ADMIN_PASSWORD env vars to log in."
fi

# --- 4. Start ASP.NET and supervise both long-running processes ---
log "Starting CrowdSec Manager on http://0.0.0.0:8081 ..."
dotnet /app/CrowdSecManager.dll &
APP_PID=$!

if [ -n "${CS_PID:-}" ]; then
    EXITED_PID=""
    if wait -n -p EXITED_PID "$APP_PID" "$CS_PID"; then
        EXITED_STATUS=0
    else
        EXITED_STATUS=$?
    fi

    if [ "$SHUTDOWN_REQUESTED" -eq 1 ]; then
        if wait "$APP_PID"; then :; else :; fi
        if wait "$CS_PID"; then :; else :; fi
        exit 0
    fi

    if [ "$EXITED_PID" = "$CS_PID" ]; then
        log "ERROR: CrowdSec exited while the manager was running. Restarting the container is required."
        kill -TERM "$APP_PID" 2>/dev/null || true
        if wait "$APP_PID"; then :; else :; fi
        exit 1
    fi

    kill -TERM "$CS_PID" 2>/dev/null || true
    if wait "$CS_PID"; then :; else :; fi
    exit "$EXITED_STATUS"
fi

if wait "$APP_PID"; then
    exit 0
else
    exit $?
fi
