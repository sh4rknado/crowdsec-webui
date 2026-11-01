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
image="localhost/crowdsec-manager:latest"

echo "Rebuilding ${image} and refreshing the CrowdSec base image..."
podman build --format docker --pull=always -t "${image}" -f "${repo_dir}/Dockerfile" "${repo_dir}"

echo "Restarting crowdsec-manager.service..."
systemctl restart crowdsec-manager.service
systemctl --no-pager --full status crowdsec-manager.service
