#!/usr/bin/env bash
# install-quadlet.sh - installs the CrowdSec Manager Quadlet on the host
#
# Usage:
#   sudo ./install-quadlet.sh [--build]
#
# What it does:
#   1. Copies crowdsec-manager.container to /etc/containers/systemd/
#   2. Optionally builds the container image with `podman build`
#   3. Reloads systemd, enables and starts the unit
#   4. Prints the URL where the UI is reachable
set -euo pipefail

QUADLET_SRC="$(cd "$(dirname "$0")" && pwd)/crowdsec-manager.container"
QUADLET_DST="/etc/containers/systemd/crowdsec-manager.container"
PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
IMAGE_NAME="localhost/crowdsec-manager:latest"

BUILD=0
[ "${1:-}" = "--build" ] && BUILD=1

if [ "$(id -u)" -ne 0 ]; then
    echo "Must run as root (sudo)" >&2; exit 1
fi

if ! command -v podman >/dev/null 2>&1; then
    echo "podman is not installed. Install it first:" >&2
    echo "  sudo dnf install podman podman-plugins  # RHEL/Fedora" >&2
    echo "  sudo apt install podman                  # Debian/Ubuntu" >&2
    exit 1
fi

echo "==> Installing Quadlet: $QUADLET_DST"
install -m 0644 "$QUADLET_SRC" "$QUADLET_DST"

# Make sure the bind-mount directories exist so the container does not create
# them with root:root permissions the host admin cannot edit.
for d in /etc/crowdsec /var/lib/crowdsec /var/lib/crowdsec-manager; do
    mkdir -p "$d"
done

if [ "$BUILD" -eq 1 ]; then
    echo "==> Building container image: $IMAGE_NAME"
    podman build --format docker -t "$IMAGE_NAME" -f "$PROJECT_ROOT/Dockerfile" "$PROJECT_ROOT"
fi

echo "==> Reloading systemd"
systemctl daemon-reload

echo "==> Starting crowdsec-manager.service"
systemctl enable --now crowdsec-manager.service

echo ""
echo "==> Done. The UI will be available on http://localhost:8081"
echo "    First boot initializes staged CrowdSec config and registers the manager bouncer."
echo "    Set ADMIN_PASSWORD in the Quadlet or its EnvironmentFile before first boot."
echo ""
echo "Logs:        sudo journalctl -u crowdsec-manager -f"
echo "Status:      sudo systemctl status crowdsec-manager"
echo "Stop:        sudo systemctl stop crowdsec-manager"
echo "Reconfigure: edit /etc/containers/systemd/crowdsec-manager.container && sudo systemctl daemon-reload && sudo systemctl restart crowdsec-manager"
