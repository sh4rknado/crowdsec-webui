#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -eq 0 ]]; then
    echo 'Run this deployment build test as a non-root user.' >&2
    exit 2
fi

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
temp_dir="$(mktemp -d)"
trap 'rm -rf -- "${temp_dir}"' EXIT
stub_dir="${temp_dir}/bin"
calls_file="${temp_dir}/calls.log"
mkdir -p "${stub_dir}"

for command_name in id install mkdir podman systemctl; do
    cat > "${stub_dir}/${command_name}" <<'STUB'
#!/usr/bin/env bash
tool="${0##*/}"
printf '%s %s\n' "${tool}" "$*" >> "${CALL_LOG}"
if [[ "${tool}" == id ]]; then
    printf '0\n'
fi
STUB
    chmod +x "${stub_dir}/${command_name}"
done

export CALL_LOG="${calls_file}"
export PATH="${stub_dir}:${PATH}"

bash "${repo_root}/deploy/install-quadlet.sh" --build >"${temp_dir}/output.log" 2>&1

assert_contains() {
    local expected="$1"
    if ! grep -Fq -- "${expected}" "${calls_file}"; then
        echo "Expected deployment stub call: ${expected}" >&2
        cat "${calls_file}" >&2
        exit 1
    fi
}

assert_contains 'podman build --format docker'
assert_contains 'systemctl daemon-reload'
assert_contains 'systemctl enable --now crowdsec-manager.service'

if ! grep -Fq -- 'podman build --format docker --pull=always' "${repo_root}/deploy/update-image.sh"; then
    echo 'update-image.sh must preserve the image healthcheck with Docker format.' >&2
    exit 1
fi

echo 'Deployment build format and Quadlet stub flow passed (4 assertions).'
