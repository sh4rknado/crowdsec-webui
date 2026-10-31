#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -eq 0 ]]; then
    echo 'Run this preflight test as a non-root user.' >&2
    exit 2
fi

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"

assert_root_guard() {
    local script="$1"
    local expected="$2"
    local output
    local status

    set +e
    output="$("${BASH}" "${repo_root}/${script}" 2>&1)"
    status=$?
    set -e

    if [[ "${status}" -eq 0 || "${output}" != *"${expected}"* ]]; then
        echo "Expected ${script} to reject a non-root invocation." >&2
        echo "Exit status: ${status}" >&2
        echo "Output: ${output}" >&2
        exit 1
    fi
}

assert_root_guard 'deploy/update-image.sh' 'Run this script as root'
assert_root_guard 'deploy/install-quadlet.sh' 'Must run as root'

echo 'Deployment script root guards passed (2 assertions).'
