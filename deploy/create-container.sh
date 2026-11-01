#!/usr/bin/env bash
# Build (when needed) and run CrowdSec Manager directly with Podman.
# Use install-quadlet.sh instead when systemd should manage the container.
set -euo pipefail

IMAGE_NAME="localhost/crowdsec-manager:latest"
CONTAINER_NAME="crowdsec-manager"
BUILD=0

usage() {
    echo "Usage: sudo $0 [--build]"
    echo "  --build  Rebuild the image before creating the container"
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

echo "Creating and starting $CONTAINER_NAME..."
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
    --env CrowdSec__LocalApiUrl="${CrowdSec__LocalApiUrl:-http://127.0.0.1:8080}" \
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

echo "Container started. UI: http://localhost:8081"
echo "Logs:   sudo podman logs -f $CONTAINER_NAME"
echo "Stop:   sudo podman stop $CONTAINER_NAME"
