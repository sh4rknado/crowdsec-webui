#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
entrypoint="${repo_root}/deploy/entrypoint.sh"
tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/crowdsec-manager-entrypoint.XXXXXX")"

cleanup() {
    case "${tmp_dir}" in
        "${TMPDIR:-/tmp}"/crowdsec-manager-entrypoint.*) rm -rf -- "${tmp_dir}" ;;
        *) echo "Refusing to remove unexpected test directory: ${tmp_dir}" >&2; return 1 ;;
    esac
}
trap cleanup EXIT

stubs="${tmp_dir}/bin"
mkdir -p "${stubs}"

cat >"${stubs}/mkdir" <<'STUB'
#!/usr/bin/env bash
exit 0
STUB

cat >"${stubs}/dotnet" <<'STUB'
#!/usr/bin/env bash
{
    printf 'ARG=%s\n' "$@"
    printf 'CROWDSEC_CONFIG=%s\n' "${CROWDSEC_CONFIG:-}"
    printf 'CROWDSEC_CONFIG_DIR=%s\n' "${CROWDSEC_CONFIG_DIR:-}"
    printf 'CROWDSEC_DATA_DIR=%s\n' "${CROWDSEC_DATA_DIR:-}"
    printf 'LOCAL_API_URL=%s\n' "${LOCAL_API_URL:-}"
    printf 'GEOIP_DB_PATH=%s\n' "${GEOIP_DB_PATH:-}"
    printf 'HOME_LATITUDE=%s\n' "${HOME_LATITUDE:-}"
    printf 'HOME_LONGITUDE=%s\n' "${HOME_LONGITUDE:-}"
    printf 'HOME_LABEL=%s\n' "${HOME_LABEL:-}"
    printf 'CrowdSec__ConfigDir=%s\n' "${CrowdSec__ConfigDir:-}"
    printf 'CrowdSec__DataDir=%s\n' "${CrowdSec__DataDir:-}"
    printf 'CrowdSec__LocalApiUrl=%s\n' "${CrowdSec__LocalApiUrl:-}"
    printf 'CrowdSec__BouncerApiKey=%s\n' "${CrowdSec__BouncerApiKey:-}"
    printf 'GeoIp__DatabasePath=%s\n' "${GeoIp__DatabasePath:-}"
    printf 'GeoIp__HomeLatitude=%s\n' "${GeoIp__HomeLatitude:-}"
    printf 'GeoIp__HomeLongitude=%s\n' "${GeoIp__HomeLongitude:-}"
    printf 'GeoIp__HomeLabel=%s\n' "${GeoIp__HomeLabel:-}"
} >"${ENTRYPOINT_CAPTURE}"
STUB

chmod +x "${stubs}/mkdir" "${stubs}/dotnet"
export PATH="${stubs}:${PATH}"
export ENTRYPOINT_CAPTURE="${tmp_dir}/capture.txt"

NO_BOOTSTRAP_CROWDSEC=1 \
ADMIN_PASSWORD='test-password' \
Database__Path='/tmp/csm-tests/app.db' \
CrowdSec__ConfigDir='/tmp/csm-tests/config' \
CrowdSec__DataDir='/tmp/csm-tests/data' \
CrowdSec__LocalApiUrl='http://127.0.0.1:18080' \
CrowdSec__BouncerApiKey='test-bouncer-key' \
GeoIp__DatabasePath='/tmp/csm-tests/GeoLite2-City.mmdb' \
GeoIp__HomeLatitude='48.8566' \
GeoIp__HomeLongitude='2.3522' \
GeoIp__HomeLabel='Test host' \
"${BASH}" "${entrypoint}"

assert_line() {
    if ! grep -Fqx -- "$1" "${ENTRYPOINT_CAPTURE}"; then
        echo "Expected entrypoint environment value was not passed: $1" >&2
        cat "${ENTRYPOINT_CAPTURE}" >&2
        exit 1
    fi
}

assert_line 'ARG=/app/CrowdSecManager.dll'
assert_line 'CROWDSEC_CONFIG=/tmp/csm-tests/config'
assert_line 'CROWDSEC_CONFIG_DIR=/tmp/csm-tests/config'
assert_line 'CROWDSEC_DATA_DIR=/tmp/csm-tests/data'
assert_line 'LOCAL_API_URL=http://127.0.0.1:18080'
assert_line 'GEOIP_DB_PATH=/tmp/csm-tests/GeoLite2-City.mmdb'
assert_line 'HOME_LATITUDE=48.8566'
assert_line 'HOME_LONGITUDE=2.3522'
assert_line 'HOME_LABEL=Test host'
assert_line 'CrowdSec__ConfigDir=/tmp/csm-tests/config'
assert_line 'CrowdSec__DataDir=/tmp/csm-tests/data'
assert_line 'CrowdSec__LocalApiUrl=http://127.0.0.1:18080'
assert_line 'CrowdSec__BouncerApiKey=test-bouncer-key'
assert_line 'GeoIp__DatabasePath=/tmp/csm-tests/GeoLite2-City.mmdb'
assert_line 'GeoIp__HomeLatitude=48.8566'
assert_line 'GeoIp__HomeLongitude=2.3522'
assert_line 'GeoIp__HomeLabel=Test host'

echo 'Entrypoint environment mapping passed (17 assertions).'
