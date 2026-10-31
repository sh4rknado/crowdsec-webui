#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
entrypoint="${repo_root}/deploy/entrypoint.sh"
tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/crowdsec-manager-engine.XXXXXX")"
real_mkdir="$(command -v mkdir)"
real_chmod="$(command -v chmod)"

cleanup() {
    case "${tmp_dir}" in
        "${TMPDIR:-/tmp}"/crowdsec-manager-engine.*) rm -rf -- "${tmp_dir}" ;;
        *) echo "Refusing to remove unexpected test directory: ${tmp_dir}" >&2; return 1 ;;
    esac
}
trap cleanup EXIT

stubs="${tmp_dir}/bin"
config_dir="${tmp_dir}/config"
staging_dir="${tmp_dir}/staging"
engine_script="${tmp_dir}/docker_start.sh"
engine_capture="${tmp_dir}/engine-starts.txt"
cli_capture="${tmp_dir}/cscli-calls.txt"
chmod_capture="${tmp_dir}/chmod-calls.txt"
app_capture="${tmp_dir}/dotnet-env.txt"
key_file="${tmp_dir}/bouncer.key"
engine_log="${tmp_dir}/crowdsec.log"

"${real_mkdir}" -p "${stubs}" "${config_dir}" "${staging_dir}"
printf 'test-default-config\n' >"${staging_dir}/config.yaml"

cat >"${stubs}/mkdir" <<'STUB'
#!/usr/bin/env bash
exit 0
STUB

cat >"${stubs}/curl" <<'STUB'
#!/usr/bin/env bash
[[ -f "${CrowdSec__ConfigDir}/config.yaml" ]] || exit 1
starts="$(wc -l <"${ENGINE_CAPTURE}")"
[[ "${starts}" -ge "${EXPECTED_ENGINE_STARTS}" ]]
STUB

cat >"${stubs}/cscli" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >>"${CSCLI_CAPTURE}"
printf 'BouncerKeyForTests0123456789\n'
STUB

cat >"${stubs}/chmod" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >>"${CHMOD_CAPTURE}"
"${REAL_CHMOD}" "$@"
STUB

cat >"${stubs}/dotnet" <<'STUB'
#!/usr/bin/env bash
{
    printf 'ARG=%s\n' "$@"
    printf 'BOUNCER_KEY=%s\n' "${CrowdSec__BouncerApiKey:-}"
} >"${APP_CAPTURE}"
STUB

cat >"${engine_script}" <<'STUB'
#!/usr/bin/env bash
printf 'LEVEL_ERROR=%s CONFIG=%s\n' "${LEVEL_ERROR:-}" "${CrowdSec__ConfigDir}" >>"${ENGINE_CAPTURE}"
cp -an "${CROWDSEC_STAGING_CONFIG_DIR}/." "${CrowdSec__ConfigDir}/"
STUB

"${real_chmod}" +x "${stubs}/mkdir" "${stubs}/curl" "${stubs}/cscli" "${stubs}/chmod" "${stubs}/dotnet" "${engine_script}"
export PATH="${stubs}:${PATH}"
export ENGINE_CAPTURE="${engine_capture}"
export CSCLI_CAPTURE="${cli_capture}"
export CHMOD_CAPTURE="${chmod_capture}"
export REAL_CHMOD="${real_chmod}"
export APP_CAPTURE="${app_capture}"

run_engine_start() {
    EXPECTED_ENGINE_STARTS="$1" \
    ADMIN_PASSWORD='test-password' \
    Database__Path="${tmp_dir}/app.db" \
    CrowdSec__ConfigDir="${config_dir}" \
    CrowdSec__DataDir="${tmp_dir}/data" \
    CrowdSec__LocalApiUrl='http://127.0.0.1:18080' \
    CROWDSEC_STAGING_CONFIG_DIR="${staging_dir}" \
    CROWDSEC_DOCKER_START_SCRIPT="${engine_script}" \
    CROWDSEC_BOUNCER_APIKEY_FILE="${key_file}" \
    CROWDSEC_ENGINE_LOG="${engine_log}" \
    "${BASH}" "${entrypoint}" >"${tmp_dir}/entrypoint.log"
}

run_engine_start 1
[[ "$(cat "${key_file}")" == 'BouncerKeyForTests0123456789' ]]
chmod_call="$(grep '^0600 ' "${chmod_capture}")"
[[ "${chmod_call}" == "0600 ${key_file}."* ]]
[[ "$(wc -l <"${cli_capture}")" -eq 1 ]]
grep -Fqx 'BOUNCER_KEY=BouncerKeyForTests0123456789' "${app_capture}"
grep -Fqx "LEVEL_ERROR=true CONFIG=${config_dir}" "${engine_capture}"

run_engine_start 2
[[ "$(wc -l <"${cli_capture}")" -eq 1 ]]
[[ "$(wc -l <"${engine_capture}")" -eq 2 ]]
grep -Fq 'Loaded persisted bouncer key.' "${tmp_dir}/entrypoint.log"
grep -Fqx 'BOUNCER_KEY=BouncerKeyForTests0123456789' "${app_capture}"

echo 'Bundled engine startup and persisted bouncer key passed (9 assertions).'
