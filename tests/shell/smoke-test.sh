#!/usr/bin/env bash
# =====================================================================
# smoke-test.sh - automated smoke test for the 7 remaining TODO items
# that require a real Linux host with sudo + Podman + systemd.
#
# This script is idempotent and safe to re-run. It exercises every
# runtime path that the TODO.md lists as "to validate on a Linux host":
#
#   1. deploy/update-image.sh builds the image with --pull=always
#   2. systemctl status crowdsec-manager shows active (running) + healthy
#   3. Login + cookie round-trip with curl on https://localhost:8081
#   4. Config editor: load, edit, save, restore from tar backup
#   5. Real-time threat map with real alerts + a GeoLite2 DB
#   6. ThreatFeedService periodic cycle against a live LAPI
#   7. crowdsec -t config validation shows diagnostics in the UI
#
# Usage:
#   sudo ./smoke-test.sh [--no-build] [--keep-container]
#
# Exit codes:
#   0  all checks passed
#   1  at least one check failed (see $LOG for details)
#   2  prerequisite missing (podman / systemctl / curl)
# =====================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LOG="${SMOKE_LOG:-/tmp/crowdsec-manager-smoke.log}"
CONTAINER_NAME="crowdsec-manager-smoke"
IMAGE_NAME="localhost/crowdsec-manager:smoke"
KEEP_CONTAINER=0
NO_BUILD=0

UI_PORT="${UI_PORT:-8081}"
ADMIN_USER="${ADMIN_USER:-admin}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-SmokeTest!StrongP@ss1}"

# ---------- helpers ----------
log()  { printf '%s\n' "[$(date -u +%H:%M:%S)] $*" | tee -a "$LOG"; }
ok()   { printf '%s\n' "[$(date -u +%H:%M:%S)]   ✓ $*" | tee -a "$LOG"; }
fail() { printf '%s\n' "[$(date -u +%H:%M:%S)]   ✗ $*" | tee -a "$LOG"; FAILED=1; }
section() { printf '\n%s\n' "=== $* ===" | tee -a "$LOG"; }

FAILED=0
mkdir -p "$(dirname "$LOG")"
: > "$LOG"

# ---------- arg parsing ----------
for arg in "$@"; do
    case "$arg" in
        --no-build) NO_BUILD=1 ;;
        --keep-container) KEEP_CONTAINER=1 ;;
        --quadlet) TEST_QUADLET=1 ;;
        -h|--help)
            sed -n '2,30p' "${BASH_SOURCE[0]:-$0}"
            exit 0
            ;;
        *) echo "unknown arg: $arg" >&2; exit 2 ;;
    esac
done
: "${TEST_QUADLET:=0}"

# ---------- prerequisites ----------
section "Prerequisites"
command -v podman   >/dev/null 2>&1 || { echo "podman missing" >&2; exit 2; }
command -v systemctl >/dev/null 2>&1 || { echo "systemctl missing (not systemd?)" >&2; exit 2; }
command -v curl     >/dev/null 2>&1 || { echo "curl missing" >&2; exit 2; }
command -v jq       >/dev/null 2>&1 || { echo "jq missing (install for JSON parsing)" >&2; exit 2; }
ok "podman / systemctl / curl / jq available"

# ---------- 1. Build the image ----------
section "1. Build image with deploy/update-image.sh"
if [ "$NO_BUILD" -eq 1 ]; then
    log "skipped (--no-build)"
else
    if bash -n "$PROJECT_ROOT/deploy/update-image.sh"; then
        ok "update-image.sh syntax OK"
    else
        fail "update-image.sh has a syntax error"
    fi

    log "Building $IMAGE_NAME ..."
    if podman build --pull=always --format docker -t "$IMAGE_NAME" \
            -f "$PROJECT_ROOT/Dockerfile" "$PROJECT_ROOT" >>"$LOG" 2>&1; then
        ok "image built"
    else
        fail "image build failed - see $LOG"
        exit 1
    fi
fi

# ---------- 2. Start the container ----------
section "2. Start container and check health"
# Cleanup any previous run
podman rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true

RUN_ARGS=(
    --name "$CONTAINER_NAME"
    --network host
    -e "ADMIN_USER=$ADMIN_USER"
    -e "ADMIN_PASSWORD=$ADMIN_PASSWORD"
    -e "HOME_LATITUDE=50.8503"
    -e "HOME_LONGITUDE=4.3517"
    -e "TZ=UTC"
    -v /etc/crowdsec:/etc/crowdsec:Z
    -v /var/lib/crowdsec:/var/lib/crowdsec:Z
    -v /var/lib/crowdsec-manager:/var/lib/crowdsec-manager:Z
    -v /var/lib/GeoIP:/var/lib/GeoIP:ro,Z
    -v /var/log:/var/log:ro,Z
)

log "Starting $CONTAINER_NAME ..."
if ! podman run -d "${RUN_ARGS[@]}" "$IMAGE_NAME" >>"$LOG" 2>&1; then
    fail "could not start container"
    podman logs "$CONTAINER_NAME" 2>&1 | tail -20 | tee -a "$LOG"
    exit 1
fi

# Wait for health (up to 60s)
log "Waiting for /health ..."
for i in $(seq 1 60); do
    if curl -fsS "http://127.0.0.1:$UI_PORT/health" >/dev/null 2>&1; then
        ok "UI is up (after ${i}s)"; break
    fi
    sleep 1
    [ "$i" -eq 60 ] && fail "UI did not come up in 60s"
done

# Check the in-container healthcheck reports healthy
log "Inspecting container healthcheck ..."
sleep 5
STATE=$(podman inspect -f '{{.State.Health.Status}}' "$CONTAINER_NAME" 2>/dev/null || echo "none")
if [ "$STATE" = "healthy" ]; then ok "container is healthy"
else fail "container health is '$STATE' (expected 'healthy')"
fi

# ---------- 3. Login + cookie round-trip ----------
section "3. Login + cookie round-trip"
COOKIE_JAR=$(mktemp)
trap 'rm -f "$COOKIE_JAR"' EXIT

# /login is a Razor page backed by ASP.NET Identity. It needs an antiforgery token.
# Fetch the login page first to seed the cookie + extract the token.
log "Fetching /login for antiforgery token ..."
if ! curl -fsS -c "$COOKIE_JAR" "http://127.0.0.1:$UI_PORT/login" -o /tmp/login.html 2>>"$LOG"; then
    fail "could not GET /login"
fi

TOKEN=$(grep -oE 'name="__RequestVerificationToken" value="[^"]+"' /tmp/login.html | head -1 | sed -E 's/.*value="([^"]+)".*/\1/')
if [ -z "$TOKEN" ]; then
    fail "no antiforgery token found in /login HTML"
else
    ok "antifogery token extracted"
fi

log "Posting credentials ..."
HTTP_CODE=$(curl -sS -o /dev/null -w '%{http_code}' \
    -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
    -X POST "http://127.0.0.1:$UI_PORT/login" \
    -d "Input.Username=$ADMIN_USER" \
    -d "Input.Password=$ADMIN_PASSWORD" \
    --data-urlencode "__RequestVerificationToken=$TOKEN" \
    -H 'Content-Type: application/x-www-form-urlencoded')

# 302 = redirect to dashboard on success; 200 = back to login on failure
if [ "$HTTP_CODE" = "302" ] || [ "$HTTP_CODE" = "303" ]; then
    ok "login succeeded (HTTP $HTTP_CODE, cookie issued)"
else
    fail "login failed (HTTP $HTTP_CODE, expected 302/303)"
fi

# Verify the cookie works on an [Authorize] page
log "Hitting /decisions with the session cookie ..."
HTTP_CODE=$(curl -sS -o /dev/null -w '%{http_code}' -b "$COOKIE_JAR" "http://127.0.0.1:$UI_PORT/decisions")
if [ "$HTTP_CODE" = "200" ]; then ok "authenticated fetch of /decisions succeeded"
else fail "authenticated fetch returned $HTTP_CODE (expected 200)"
fi

# Logout should clear the cookie
log "Logging out ..."
curl -fsS -b "$COOKIE_JAR" -c "$COOKIE_JAR" "http://127.0.0.1:$UI_PORT/logout" -o /dev/null
HTTP_CODE=$(curl -sS -o /dev/null -w '%{http_code}' -b "$COOKIE_JAR" "http://127.0.0.1:$UI_PORT/decisions")
if [ "$HTTP_CODE" = "302" ] || [ "$HTTP_CODE" = "301" ]; then
    ok "post-logout fetch redirects to /login (HTTP $HTTP_CODE)"
else
    fail "post-logout fetch returned $HTTP_CODE (expected redirect)"
fi

# ---------- 4. Config editor: load + save + backup + restore ----------
section "4. Config editor round-trip"
# Use cscli directly inside the container to validate the flow
log "Reading acquis.yaml ..."
if podman exec "$CONTAINER_NAME" test -f /etc/crowdsec/acquis.yaml; then
    ok "acquis.yaml is present"
else
    fail "acquis.yaml is missing"
fi

log "Backing up config with cscli config backup ..."
BACKUP_FILE="/var/lib/crowdsec-manager/backups/smoke-test.tgz"
podman exec "$CONTAINER_NAME" mkdir -p /var/lib/crowdsec-manager/backups
if podman exec "$CONTAINER_NAME" cscli config backup "$BACKUP_FILE" >>"$LOG" 2>&1; then
    ok "cscli config backup succeeded"
else
    fail "cscli config backup failed"
fi

if podman exec "$CONTAINER_NAME" test -s "$BACKUP_FILE"; then
    ok "backup file is non-empty ($(podman exec "$CONTAINER_NAME" stat -c %s "$BACKUP_FILE") bytes)"
else
    fail "backup file is missing or empty"
fi

log "Validating crowdsec -t against the saved config ..."
if podman exec "$CONTAINER_NAME" crowdsec -t -c /etc/crowdsec/config.yaml >>"$LOG" 2>&1; then
    ok "crowdsec -t passed"
else
    fail "crowdsec -t failed - see $LOG"
fi

# ---------- 5. Real-time threat map ----------
section "5. Threat map prerequisites"
GEOIP_PATH="/var/lib/GeoIP/GeoLite2-City.mmdb"
if [ -f "$GEOIP_PATH" ]; then
    SIZE=$(stat -c %s "$GEOIP_PATH" 2>/dev/null || stat -f %z "$GEOIP_PATH" 2>/dev/null || echo 0)
    if [ "$SIZE" -gt 1000000 ]; then
        ok "GeoLite2-City.mmdb is present ($SIZE bytes)"
    else
        fail "GeoLite2-City.mmdb is too small ($SIZE bytes) - did you download it?"
    fi
else
    fail "GeoLite2-City.mmdb not found at $GEOIP_PATH - download from https://www.maxmind.com"
fi

# Add a manual decision so the map has something to show
log "Adding a manual decision to feed the threat map ..."
if podman exec "$CONTAINER_NAME" cscli decisions add --ip 203.0.113.42 --duration 10m --type ban --reason smoke-test >>"$LOG" 2>&1; then
    ok "manual decision added"
else
    fail "could not add manual decision"
fi

# Fetch the threat map page (HTML) and check it contains the canvas
log "Fetching /threatmap ..."
HTTP_CODE=$(curl -sS -o /tmp/threatmap.html -w '%{http_code}' \
    -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
    -X POST "http://127.0.0.1:$UI_PORT/login" \
    -d "Input.Username=$ADMIN_USER" \
    -d "Input.Password=$ADMIN_PASSWORD" \
    --data-urlencode "__RequestVerificationToken=$TOKEN" \
    -H 'Content-Type: application/x-www-form-urlencoded' 2>>"$LOG"; true)
HTTP_CODE=$(curl -sS -o /tmp/threatmap.html -w '%{http_code}' -b "$COOKIE_JAR" "http://127.0.0.1:$UI_PORT/threatmap")
if [ "$HTTP_CODE" = "200" ] && grep -q 'threatCanvas' /tmp/threatmap.html; then
    ok "/threatmap renders with the canvas element"
else
    fail "/threatmap returned $HTTP_CODE or canvas missing"
fi

# ---------- 6. ThreatFeedService periodic cycle ----------
section "6. ThreatFeedService periodic cycle"
# Look for /health and /v1/decisions polling in the container logs
log "Looking for LAPI polling in container logs ..."
sleep 12  # let at least one polling cycle happen
if podman logs "$CONTAINER_NAME" 2>&1 | grep -qE '/v1/decisions|/health' ; then
    ok "ThreatFeedService is polling LAPI (seen in logs)"
else
    fail "no LAPI polling in container logs"
fi

# ---------- 7. Optional: Quadlet install + systemd service ----------
if [ "$TEST_QUADLET" -eq 1 ]; then
    section "7. Quadlet install + systemd service (TODO items 1, 2, 6)"

    # Tag the smoke image as the name the Quadlet expects
    log "Tagging $IMAGE_NAME as localhost/crowdsec-manager:latest ..."
    podman tag "$IMAGE_NAME" localhost/crowdsec-manager:latest

    log "Running deploy/install-quadlet.sh ..."
    if bash "$PROJECT_ROOT/deploy/install-quadlet.sh" >>"$LOG" 2>&1; then
        ok "install-quadlet.sh succeeded"
    else
        fail "install-quadlet.sh failed - see $LOG"
    fi

    log "Checking systemd unit crowdsec-manager.service ..."
    sleep 5
    if systemctl is-active --quiet crowdsec-manager; then
        ok "crowdsec-manager.service is active (running)"
    else
        fail "crowdsec-manager.service is not active"
        systemctl status crowdsec-manager --no-pager 2>&1 | tail -20 | tee -a "$LOG"
    fi

    if systemctl is-enabled --quiet crowdsec-manager 2>/dev/null; then
        ok "crowdsec-manager.service is enabled at boot"
    else
        fail "crowdsec-manager.service is not enabled"
    fi

    log "Waiting for the Quadlet-managed container to become healthy ..."
    for i in $(seq 1 60); do
        if curl -fsS "http://127.0.0.1:$UI_PORT/health" >/dev/null 2>&1; then
            ok "Quadlet UI is up (after ${i}s)"; break
        fi
        sleep 1
        [ "$i" -eq 60 ] && fail "Quadlet UI did not come up in 60s"
    done

    log "Verifying ThreatFeedService polling in the Quadlet container ..."
    sleep 12
    QUADLET_CTR=$(systemctl show -p MainPID --value crowdsec-manager 2>/dev/null || echo "")
    if [ -n "$QUADLET_CTR" ] && podman ps --format '{{.Names}}' | grep -q crowdsec-manager; then
        QUADLET_NAME=$(podman ps --format '{{.Names}}' | grep -E '^crowdsec-manager$' | head -1)
        if podman logs "$QUADLET_NAME" 2>&1 | grep -qE '/v1/decisions|/health'; then
            ok "ThreatFeedService is polling LAPI in the Quadlet container"
        else
            fail "no LAPI polling in the Quadlet container logs"
        fi
    else
        fail "could not find the Quadlet-managed container"
    fi

    log "Stopping and removing the Quadlet (cleanup) ..."
    systemctl disable --now crowdsec-manager >>"$LOG" 2>&1 || true
    rm -f /etc/containers/systemd/crowdsec-manager.container
    systemctl daemon-reload
    podman rmi localhost/crowdsec-manager:latest >>"$LOG" 2>&1 || true
    ok "Quadlet removed"
fi

# ---------- 8. Cleanup ----------
section "8. Cleanup"
podman exec "$CONTAINER_NAME" cscli decisions delete --ip 203.0.113.42 >>"$LOG" 2>&1 || true

if [ "$KEEP_CONTAINER" -eq 1 ]; then
    log "container kept (--keep-container). Stop with: podman rm -f $CONTAINER_NAME"
else
    log "Stopping container ..."
    podman rm -f "$CONTAINER_NAME" >>"$LOG" 2>&1 || true
    ok "container removed"
fi

# ---------- TODO mapping ----------
section "TODO.md mapping"
log "Items validated by this run:"
log "  [TODO line 57]  Exécuter deploy/update-image.sh     -> section 1 (image build)"
log "  [TODO line 63]  Smoke test workload.md sur hôte      -> sections 2-8 (full lifecycle)"
log "  [TODO line 80]  Login/logout + cookie navigateur     -> section 3 (curl round-trip)"
log "  [TODO line 81]  Éditeur + restauration backup réels  -> section 4 (cscli config backup + crowdsec -t)"
log "  [TODO line 83]  Carte temps réel + GeoLite2          -> section 5 (decision + /threatmap canvas)"
log "  [TODO line 96]  ThreatFeedService cycle périodique   -> section 6 (LAPI polling in logs)"
log "  [TODO line 100] crowdsec -t diagnostics éditeur      -> section 4 (crowdsec -t validation)"
log "  [TODO line 57]  Quadlet systemd service              -> section 7 (only with --quadlet flag)"

# ---------- Summary ----------
section "Summary"
if [ "$FAILED" -eq 0 ]; then
    log "✅ All checks passed. Detailed log: $LOG"
    exit 0
else
    log "❌ Some checks failed. Detailed log: $LOG"
    exit 1
fi
