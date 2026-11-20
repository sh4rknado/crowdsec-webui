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

# Extract assembly version from main project for semantic versioning
echo "Reading AssemblyVersion from the main application project..."
main_project="${repo_dir}/src/CrowdSecManager/CrowdSecManager.csproj"
if [[ -f "$main_project" ]]; then
    # Get AssemblyVersion property (default to 1.0.0 if not found)
    version_tag=$(grep -oP '<AssemblyVersion>\K[^<]+' "${main_project}" | tr -d ' ')
    
    # If no AssemblyVersion was explicitly set, extract from obj/ directory or use default
    if [[ -z "$version_tag" ]]; then
        echo "No explicit AssemblyVersion found, using latest tag..."
        version_tag="latest"
    else
        # Clean the version tag (remove any suffixes)
        version_tag=$(echo "$version_tag" | cut -d',' -f1 | tr -d '()[]"\'')
        echo "Detected AssemblyVersion: ${version_tag}"
        
        # Create a versioned tag
        versioned_image="${image}:${version_tag}"
        echo "Creating versioned image tag: ${versioned_image}"
    fi
else
    version_tag="latest"
fi

# Tag with latest and version (if applicable)
podman tag "${image}" "${image}:latest"

if [[ "$version_tag" != "latest" ]]; then
    podman tag "${image}" "${image}:${version_tag}"
fi

echo ""
echo "Verifying image healthcheck and GeoIP database..."
podman image inspect "${image}" --format '{{json .Config.Healthcheck}}'
podman run --rm --network=none --entrypoint /bin/ls "${image}" -lh /var/lib/GeoIP/GeoLite2-City.mmdb

echo ""
echo "Image is ready to push: podman push ${image}"
if [[ "$version_tag" != "latest" ]]; then
    echo "Also available as: podman push ${image}:${version_tag}"
fi

# Push the images (and versioned one if applicable)
podman push "${image}"

if [[ "$version_tag" != "latest" ]]; then
    podman push "${image}:${version_tag}"
fi

echo ""
echo "Restarting crowdsec-manager.service..."
systemctl restart crowdsec-manager.service
systemctl --no-pager --full status crowdsec-manager.service
