#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
    echo "Run this script as root (for example: sudo $0)." >&2
    exit 1
fi

if ! command -v podman >/dev/null 2>&1; then
    echo "podman is required to rebuild the CrowdSec Manager image." >&2
    exit 1
fi

if ! command -v systemctl >/dev/null 2>&1; then
    echo "systemctl is required to restart the Quadlet service." >&2
    exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_dir="$(cd -- "${script_dir}/.." && pwd)"
image="docker.io/cobrakaii/crowdsec-dashboard:latest"

echo "Rebuilding ${image} and refreshing the CrowdSec base image..."
podman build --network=host --format docker --pull=always --no-cache -t "${image}" -f "${repo_dir}/Dockerfile" "${repo_dir}"

echo "Verifying image healthcheck and GeoIP database..."
podman image inspect "${image}" --format '{{json .Config.Healthcheck}}'
podman run --rm --network=none --entrypoint /bin/ls "${image}" -lh /var/lib/GeoIP/GeoLite2-City.mmdb

echo "Image is ready to push: podman push ${image}"

echo "Restarting crowdsec-manager.service..."
systemctl restart crowdsec-manager.service
systemctl --no-pager --full status crowdsec-manager.service
