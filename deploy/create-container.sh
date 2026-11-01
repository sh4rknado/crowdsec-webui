#!/usr/bin/env bash
# Build (when needed) and run CrowdSec Manager directly with Podman.
#
# Single-container design, host networking:
#   - CrowdSec engine (LAPI on 8080) and Blazor UI (on 8081) run inside ONE
#     container that shares the host network namespace (--network host).
#   - Both listeners bind DIRECTLY on the host: CrowdSec on host:8080, the UI
#     on host:8081. No --publish flags (they are meaningless and discarded in
#     host mode).
#   - The pre-flight below refuses to start if 8080 or 8081 is already taken on
#     the host (e.g. by an existing host CrowdSec).
set -euo pipefail

IMAGE_NAME="localhost/crowdsec-manager:latest"
CONTAINER_NAME="crowdsec-manager"
BUILD=0
UI_PORT="${CROWDSEC_UI_PORT:-8081}"
LAPI_PORT="${CROWDSEC_LAPI_PORT:-8080}"

usage() {
    echo "Usage: sudo $0 [--build]"
    echo "  --build   Rebuild the image before creating the container"
    echo ""
    echo "  Env overrides:"
    echo "    CROWDSEC_UI_PORT   UI port on the host (default 8081)"
    echo "    CROWDSEC_LAPI_PORT LAPI port on the host (default 8080)"
}

for arg in "$@"; do
    case "$arg" in
        --build) BUILD=1 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $arg" >&2; usage >&2; exit 2 ;;
    esac
done

if [[ "${EUID}" -ne 0 ]]; then
    echo "Run this script as root (for example: sudo $0)." >&2
    exit 1
fi
if ! command -v podman >/dev/null 2>&1; then
    echo "Podman is required. Install it before running this script." >&2
    exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_dir="$(cd -- "${script_dir}/.." && pwd)"

# --- Host-side port pre-flight (inline, no external file) ---
# In host mode BOTH listeners bind directly on the host, so we must check that
# 8080 (LAPI) and 8081 (UI) are free before starting. Uses ss(8) from iproute2;
# falls back to /proc/net/tcp if ss is missing.
preflight_ports() {
    local ports=("$LAPI_PORT" "$UI_PORT")
    local conflict=0 port holder
    for port in "${ports[@]}"; do
        if command -v ss >/dev/null 2>&1; then
            if ss -ltn 2>/dev/null | awk 'NR>1{print $4}' | grep -Eq "[:.]${port}\$"; then
                holder="$(ss -ltnp 2>/dev/null | awk -v p=":${port}\$" '$4 ~ p {print $NF}' | head -n1)"
                echo "[create-container] ERROR: host port ${port} is already in use${holder:+ by ${holder}}." >&2
                echo "[create-container]         Stop the conflicting service or set CROWDSEC_UI_PORT/CROWDSEC_LAPI_PORT." >&2
                conflict=1
            fi
        else
            # Fallback: parse /proc/net/tcp (port in hex, 16-bit)
            local hex
            hex=$(printf '%04X' "$port")
            if grep -Eq "^[0-9]+: [0-9A-F]{8}:${hex} " /proc/net/tcp /proc/net/tcp6 2>/dev/null; then
                echo "[create-container] ERROR: host port ${port} is already in use." >&2
                conflict=1
            fi
        fi
    done
    if [ "$conflict" -ne 0 ]; then
        echo "[create-container] Refusing to start: port conflict detected on the host." >&2
        return 1
    fi
    echo "[create-container] Pre-flight OK: host ports ${LAPI_PORT} (LAPI) and ${UI_PORT} (UI) are free."
    return 0
}

preflight_ports

if podman container exists "$CONTAINER_NAME"; then
    echo "Container '$CONTAINER_NAME' already exists. Remove it first with:" >&2
    echo "  sudo podman rm -f $CONTAINER_NAME" >&2
    exit 1
fi

if [[ "$BUILD" -eq 1 ]] || ! podman image exists "$IMAGE_NAME"; then
    echo "Building image $IMAGE_NAME..."
    podman build --network=host --format docker --pull=always -t "$IMAGE_NAME" \
        -f "$project_dir/Dockerfile" "$project_dir"
fi

for dir in /etc/crowdsec /var/lib/crowdsec /var/lib/crowdsec-manager /var/lib/GeoIP; do
    mkdir -p "$dir"
done

if [[ -z "${ADMIN_PASSWORD:-}" ]]; then
    read -r -s -p "Set the initial admin password: " ADMIN_PASSWORD
    echo
    if [[ -z "$ADMIN_PASSWORD" ]]; then
        echo "The initial admin password cannot be empty." >&2
        exit 1
    fi
    export ADMIN_PASSWORD
fi

# --- Launch the container in host networking mode ---
# No --publish flags: in --network host the container shares the host namespace,
# so CrowdSec binds host:${LAPI_PORT} and the UI binds host:${UI_PORT} directly.
echo "Creating and starting $CONTAINER_NAME (host network)..."
podman run --detach \
    --name "$CONTAINER_NAME" \
    --restart=unless-stopped \
    --network host \
    --user 0 \
    --cap-add NET_BIND_SERVICE \
    --cap-add NET_RAW \
    --env ADMIN_USER="${ADMIN_USER:-admin}" \
    --env ADMIN_PASSWORD \
    --env HOME_LATITUDE="${HOME_LATITUDE:-50.8503}" \
    --env HOME_LONGITUDE="${HOME_LONGITUDE:-4.3517}" \
    --env HOME_LABEL="${HOME_LABEL:-Protected host}" \
    --env CROWDSEC_BOUNCER_APIKEY="${CROWDSEC_BOUNCER_APIKEY:-}" \
    --env NO_BOOTSTRAP_CROWDSEC=0 \
    --env CrowdSec__LocalApiUrl="http://127.0.0.1:8080" \
    --env CrowdSec__ConfigDir=/etc/crowdsec \
    --env CrowdSec__DataDir=/var/lib/crowdsec/data \
    --env GeoIp__DatabasePath=/var/lib/GeoIP/GeoLite2-City.mmdb \
    --env GeoIp__HomeLatitude="${HOME_LATITUDE:-50.8503}" \
    --env GeoIp__HomeLongitude="${HOME_LONGITUDE:-4.3517}" \
    --env Database__Path=/var/lib/crowdsec-manager/app.db \
    --volume /etc/crowdsec:/etc/crowdsec:Z \
    --volume /var/lib/crowdsec:/var/lib/crowdsec:Z \
    --volume /var/lib/crowdsec-manager:/var/lib/crowdsec-manager:Z \
    --volume /var/lib/GeoIP:/var/lib/GeoIP:ro,Z \
    --volume /var/log:/var/log:ro,Z \
    "$IMAGE_NAME"

echo "Container started."
echo "  UI:   http://localhost:${UI_PORT}"
echo "  LAPI: http://localhost:${LAPI_PORT}"
echo "Logs:   sudo podman logs -f $CONTAINER_NAME"
echo "Stop:   sudo podman stop $CONTAINER_NAME"
