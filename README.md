# CrowdSec Manager

An ASP.NET 10 Blazor Server application that wraps and extends
[CrowdSec](https://github.com/crowdsecurity/crowdsec) into a single
admin-friendly web console: manage the hub (collections, parsers,
scenarios), edit configuration, add or remove manual IP bans, and watch
a real-time threat map that visualises attacks as they are detected.

It ships as a Podman container driven by a **systemd Quadlet** so it can
be deployed next to (or alongside) your existing CrowdSec instance with
a single `systemctl start`.

---

## Features

| Area | What you get |
| --- | --- |
| **Dashboard** | Live counts of decisions / alerts / 24h alerts / GeoIP status, quick actions for `cscli hub update` / `hub upgrade` / config backup, recent blocked IPs table. |
| **Real-time threat map** | Interactive Leaflet/OpenStreetMap map. Each new CrowdSec alert is geolocated with MaxMind GeoLite2 and drawn as a severity-colored arc from the attacker IP to your protected host, with source markers, top source countries, and a live recent-attacks feed. |
| **Decisions** | Add a manual IP ban (`ban` / `captcha` / `throttle`), with custom duration, reason and an optional **bypass-allowlist** flag (forces the ban even if the IP is in a CrowdSec allowlist). List, delete, or wipe all active decisions. |
| **Alerts** | Browse the last N alerts with scenario, source, event count, decision count and message. Delete individual alerts. |
| **Hub & Collections** | Run `cscli hub update` / `hub upgrade`, install/remove individual collections, parsers, scenarios, postoverflows, AppSec rules and contexts. Filter by name/type. See local vs latest version and taint state. |
| **Explain** | Test the log pipeline against a single log line or a log file with `cscli explain --log/--file --type`. Useful to validate that a parser chain works before deploying it. |
| **Bouncers** | Register new bouncers (returns the one-shot API key, or use a pre-shared key with `--key`), list active bouncers with last-pull / pulls / status, inspect a bouncer's resolved configuration, revoke, and prune revoked bouncers in bulk. |
| **Machines** | List CrowdSec machines registered against this Local API with version, last push, validated state. Register new machines (`--auto` optional), delete by ID, prune expired machines. |
| **LAPI** | Show Local API status (`cscli lapi status`), register a machine against a remote LAPI (`cscli lapi register --machine/--url/--token`), and list LAPI contexts (`cscli lapi context list`). |
| **Simulation** | Toggle global simulation mode (`cscli simulation enable/disable`) and manage per-scenario simulation entries (`add`/`remove`/`list`). Useful to dry-run new scenarios without enforcing bans. |
| **Notifications** | List configured notification plugins (`cscli notifications list`) and inspect their resolved configuration. Notification YAML files are edited from the Configuration page. |
| **Console** | Show CrowdSec Central API registration status (`cscli console status`) and toggle alert sharing (`console enable/disable alerts`). |
| **Configuration editor** | Whitelisted YAML config files edited in a textarea with live YAML validation, on-demand tar backup of the whole `/etc/crowdsec` directory, and `cscli config` tools: show merged config, show merged (YAML), feature flags, backup. |
| **Authentication** | ASP.NET Identity with SQLite, cookie auth, configurable password policy, admin role seeded on first boot. |

---

## Architecture at a glance

```
┌──────────────────────────────────────────────────────────────────────┐
│                  Podman container (crowdsec-manager)                 │
│                                                                      │
│   ┌────────────────────┐         ┌──────────────────────────────┐    │
│   │  CrowdSec daemon   │  LAPI   │   CrowdSec Manager (.NET 10)  │    │
│   │  + cscli           │ ◄─────► │   Blazor Server + SignalR    │    │
│   │  (port 8080)       │  HTTP   │   port 8081                  │    │
│   └────────────────────┘         └──────────────┬───────────────┘    │
│             ▲                                   │                    │
│             │ /var/log (read-only)              │ Blazor circuit     │
│             │ /etc/crowdsec (rw)                ▼                    │
│             │ /var/lib/crowdsec (rw)       Browser (dark SOC UI)     │
│             │                                    │ GeoLite2 database │
└─────────────┼────────────────────────────────────┼────────────────────┘
              │                                    │
              ▼                                    ▼
   Bind-mounted host volumes (Quadlet)
```

The application talks to CrowdSec through **two** channels:

1. **`cscli` subprocess** (`/usr/bin/cscli`) — for hub management, decisions,
   bouncers, machines, alerts. The wrapper in
   `Services/CrowdSecCliService.cs` always appends `-o json` for parseable
   commands and streams stdout/stderr with a configurable timeout.
2. **CrowdSec Local API (LAPI)** on `http://127.0.0.1:8080` — the bouncer
   token is used for decision reads and the health indicator. CrowdSec limits
   bouncer credentials to decisions, so alert history is read with `cscli`
   using the machine credentials from the mounted CrowdSec configuration.
   `ThreatFeedService` polls alerts, geolocates new attacker IPs and sends
   them to the browser through the app's SignalR hub.

The LAPI is polled for decision status. `cscli` handles alert history and
administrative operations. The threat feed is polled at the configured
interval (8 seconds by default); SignalR pushes each detected change from
the manager to connected browsers.

---

## Repository layout

```
crowdsec-manager/
├── Dockerfile                       # multi-stage build (dotnet sdk -> crowdsec runtime)
├── deploy/
│   ├── crowdsec-manager.container   # Podman Quadlet (system service)
│   ├── entrypoint.sh                # container PID 1 (bootstrap cscli + bouncer + dotnet)
│   └── install-quadlet.sh           # host install script
├── src/
│   ├── CrowdSecManager.sln
│   └── CrowdSecManager/
│       ├── Program.cs               # DI, auth, Blazor, SignalR, DB bootstrap
│       ├── Components/
│       │   ├── App.razor            # router with AuthorizeRouteView
│       │   ├── _Imports.razor
│       │   ├── Layout/
│       │   │   ├── MainLayout.razor
│       │   │   ├── NavMenu.razor
│       │   │   ├── TopBar.razor
│       │   │   └── LoginLayout.razor
│       │   ├── Pages/
│       │   │   ├── Dashboard.razor
│       │   │   ├── ThreatMap.razor          ★ real-time map
│       │   │   ├── Decisions.razor          ★ add/remove IP bans (+ bypass-allowlist)
│       │   │   ├── Alerts.razor
│       │   │   ├── HubManagement.razor      ★ update hub / collections / appsec / contexts
│       │   │   ├── ExplainPage.razor       ★ test the log pipeline (cscli explain)
│       │   │   ├── Bouncers.razor
│       │   │   ├── Machines.razor
│       │   │   ├── LapiPage.razor         ★ LAPI status, register, contexts
│       │   │   ├── Simulation.razor         ★ dry-run scenarios (cscli simulation)
│       │   │   ├── Notifications.razor      ★ list/inspect plugins (cscli notifications)
│       │   │   ├── ConsolePage.razor        ★ Central API status (cscli console)
│       │   │   ├── ConfigEditor.razor       ★ edit YAML config
│       │   │   ├── Login.razor
│       │   │   └── Logout.razor
│       │   └── Shared/RedirectToLogin.razor
│       ├── Services/
│       │   ├── CrowdSecCliService.cs       # cscli wrapper
│       │   ├── CrowdSecApiService.cs       # LAPI REST client
│       │   ├── GeoIpService.cs             # MaxMind GeoLite2 reader
│       │   ├── ConfigFileService.cs        # YAML round-trip + validation
│       │   ├── ThreatFeedService.cs        # BackgroundService polling CrowdSec
│       │   └── ThreatMapEventBus.cs        # Authenticated Blazor circuit fan-out
│       ├── Data/                            # SQLite AppDbContext + seeder
│       ├── Models/                          # DTOs + options
│       ├── Pages/_Host.cshtml               # Blazor host page
│       └── wwwroot/
│           ├── css/soc.css                  # dark SOC theme
│           ├── css/threatmap.css
│           ├── js/threatmap.js              # Leaflet + live threat arcs
│           └── favicon.svg
├── readme.md                        # ← you are here
└── workload.md                      # architecture deep-dive
```

---

## Prerequisites

- Host with **Podman 4.4+** and `podman-quadlet` (Quadlet support is built-in
  on RHEL 9 / Fedora 38+ / Debian 12+ / Ubuntu 23.04+; install
  `podman-plugins` on older systems)
- **systemd** (Quadlet files are translated to systemd units on `daemon-reload`)
- Optional: a MaxMind account to download newer GeoLite2 database releases
- Optional: existing CrowdSec install on the host (otherwise the container
  will bootstrap a fresh one inside its own config volume)

---

## Quick start

### 1. Build the image

```bash
cd crowdsec-manager
sudo podman build --format docker --pull=always -t localhost/crowdsec-manager:latest -f Dockerfile .
```

### 2. Set the initial admin password

Before first start, edit `deploy/crowdsec-manager.container` and add a strong
password:

```ini
Environment=ADMIN_PASSWORD=your-strong-password-here
```

For a host environment file instead, see the **Secrets** section below.
The Quadlet intentionally has no built-in admin password.

### 3. Install the Quadlet

```bash
sudo ./deploy/install-quadlet.sh
```

The script:

- Copies `deploy/crowdsec-manager.container` to `/etc/containers/systemd/`
- Creates the host bind-mount directories (`/etc/crowdsec`,
  `/var/lib/crowdsec`, `/var/lib/crowdsec-manager`)
- Reloads systemd and enables `crowdsec-manager.service`

You can also do it manually:

```bash
sudo install -m 0644 deploy/crowdsec-manager.container /etc/containers/systemd/
sudo systemctl daemon-reload
sudo systemctl enable --now crowdsec-manager
```

> The `ADMIN_PASSWORD` env var is read **only on first boot** to seed the
> `admin` user. After that, change the password through the UI or with
> `dotnet user secrets` / direct DB edit.

### 4. GeoIP database

The repository includes `deploy/GeoLite2-City.mmdb`, and the Dockerfile copies
it into the image at `/var/lib/GeoIP/GeoLite2-City.mmdb`. It maps public source
IPs from CrowdSec alerts to approximate countries, cities, and coordinates so
the threat map can place attacks on the map. It does not contain CrowdSec
decisions and does not control blocking. See [the GeoIP database guide](docs/GEOIP_DATABASE.md)
for configuration, limitations, and update instructions.

Without a readable database, the application continues using deterministic
fallback coordinates and labels the country as unknown. Those coordinates are
not a real geolocation. The dashboard indicates whether the database loaded.

### 5. Open the UI

Browse to <http://localhost:8081> and sign in with `admin` + the password
you set.

> The UI listens on `8081` to avoid clashing with CrowdSec's own LAPI on
> `8080`. Both are exposed through `Network=host` in the Quadlet, so the
> host firewall is the only network boundary. **Put a TLS-terminating
> reverse proxy (Caddy, Traefik, nginx) in front for production.**

---

## Quadlet reference

The Quadlet file `deploy/crowdsec-manager.container` is a systemd-native
alternative to a `docker run` line. Highlights:

| Directive | Value | Why |
| --- | --- | --- |
| `Image=localhost/crowdsec-manager:latest` | local image | built from the Dockerfile |
| `Network=host` | host network | so the manager's `cscli` and LAPI client can reach a host CrowdSec daemon |
| `Volume=/etc/crowdsec:/etc/crowdsec:Z` | rw | YAML config editor + hub installs |
| `Volume=/var/lib/crowdsec:/var/lib/crowdsec:Z` | rw | CrowdSec SQLite DB |
| `Volume=/var/lib/crowdsec-manager:/var/lib/crowdsec-manager:Z` | rw | app DB + backups |
| `Volume=/var/log:/var/log:ro,Z` | ro | CrowdSec log sources |
| `Environment=ADMIN_USER=admin` | | seed user |
| `Environment=ADMIN_PASSWORD=...` | | first-boot seed password |
| `Notify=healthy` | | wait for `HEALTHCHECK` before marking unit started |
| `Restart=always` | | systemd-level auto-restart |

The `:Z` private-volume flag tells Podman to relabel the bind-mount so
SELinux-enabled hosts (RHEL/Fedora) do not block access.

### Common operations

```bash
# Status
sudo systemctl status crowdsec-manager

# Logs (live)
sudo journalctl -u crowdsec-manager -f

# Restart after config change
sudo systemctl restart crowdsec-manager

# Stop / start
sudo systemctl stop crowdsec-manager
sudo systemctl start crowdsec-manager

# Rebuild image after code change
sudo podman build --format docker -t localhost/crowdsec-manager:latest -f Dockerfile .
sudo systemctl restart crowdsec-manager

# Update CrowdSec engine and manager from current source/base image
sudo bash deploy/update-image.sh

# Enter the container for debugging
sudo podman exec -it crowdsec-manager bash
```

---

## Using each feature

### Update CrowdSec hub / collections

1. Open the **Hub & Collections** page.
2. Click **Update index** to refresh the local hub index from the upstream
   git repo (this is `cscli hub update`).
3. Click **Upgrade all** to bring every installed collection/parser/scenario
   to its latest version (`cscli hub upgrade`).
4. To install or remove a specific item, use the buttons in each row.
   `cscli collections install crowdsecurity/nginx` and
   `cscli collections remove crowdsecurity/nginx` are run under the hood.
5. Click **Reload CrowdSec** after changes to signal the in-container engine.
   If using a host CrowdSec service, reload it on the host instead.

### Edit configuration

1. Open **Configuration**.
2. Pick a file from the list on the left (`config.yaml`, `profiles.yaml`,
   `acquis.yaml`, etc.).
3. Edit the textarea. The **Validate** button parses the YAML and reports
   syntax errors. The **Save** button re-validates and writes to
   `/etc/crowdsec/<file>`. **Test CrowdSec config** runs the engine's `-t`
   check against the saved main configuration file.
4. After saving, click **Apply / reload CrowdSec** to signal an engine in the
   same container. In side-by-side mode, restart the host service manually
   because the manager container cannot signal a host process:

   ```bash
   sudo systemctl restart crowdsec
   ```

5. Use **Backup config** on the dashboard to dump a timestamped
   `crowdsec-config-YYYYMMDD-HHMMSS.tgz` into
   `/var/lib/crowdsec-manager/backups`.

### Add / remove IP bans

1. Open **Decisions**.
2. Fill the form: IP address (single IP or CIDR), duration (`4h`, `7d`,
   `30m`), type (`ban`, `captcha`, `throttle`), reason.
3. Click **Ban IP** — runs `cscli decisions add -i <ip> -d <dur> -t <type> -r <reason>`.
4. The new decision appears in the table below. Click **Delete** to remove
   one, or **Delete all** to wipe every active decision.

### Real-time threat map

1. Open **Threat Map**.
2. The page receives events through its authenticated Blazor Server circuit
   as the manager polls CrowdSec (8 seconds by default).
3. Each arc is colored by severity (info / low / medium / high / critical)
   and animates from the attacker's geolocated source to your home marker
   (Brussels by default — override with `HOME_LATITUDE` / `HOME_LONGITUDE`
   / `HOME_LABEL` env vars).
4. The right panel shows live stats, top source countries (with bars), and
   the 20 most recent attacks.

> If GeoLite2 is missing or cannot be read, IP locations use the fallback
> coordinates and are marked as unknown. See the [GeoIP database guide](docs/GEOIP_DATABASE.md).

### Bouncers

1. Open **Bouncers**.
2. Type a name (`nginx-bouncer`, `cloudflare-bouncer`, etc.).
3. Optionally fill the **API key** field to use a pre-shared secret
   (`cscli bouncers add NAME --key KEY`). Leave it empty to let cscli
   generate one.
4. Click **Register bouncer**. When cscli generated the key, it is shown
   once below the form - copy it into your bouncer's config.
5. The list shows every registered bouncer with its last pull time and
   status. Use **Inspect** to see the bouncer's resolved configuration,
   **Revoke** to delete it, or **Prune revoked** to bulk-delete all revoked
   bouncers.

### Machines

List, register and delete CrowdSec machines registered against this Local API.
Useful for multi-host setups where multiple CrowdSec agents push to a
central LAPI.

1. To register a new machine, type its name (or leave blank for an auto-generated
   name), tick **Auto-validate** to skip manual validation, and click
   **Register machine**. The output (including the generated credentials file
   path) is shown in a `<details>` block.
2. To remove a machine, click **Delete** on its row.
3. Use **Prune expired** in the header to remove machines that have not pushed
   in a while (`cscli machines prune`).

### LAPI

The LAPI page exposes the local CrowdSec API management commands.

1. **Status** runs `cscli lapi status` and shows whether the LAPI is reachable
   from this machine, plus the registered URL and credentials path.
2. **Register a machine against a remote LAPI** is for agent-mode setups where
   this CrowdSec instance pushes to a remote LAPI. Fill the form
   (machine name, LAPI URL, optional auto-registration token) and click
   **Register**.
3. **LAPI contexts** lists the per-bouncer filter contexts
   (`cscli lapi context list`). Use the Configuration page to edit them.

### Explain

The Explain page lets you test how CrowdSec parses a log line or a log file
with the currently installed parsers and scenarios, **without** having to
ingest the data through `acquis.yaml`. This is the equivalent of:

```bash
cscli explain --log "Sep 19 18:33:22 scw sshd[24347]: pam_unix(sshd:auth): authentication failure; rhost=1.2.3.4" --type syslog
cscli explain --file /var/log/nginx/access.log --type nginx --verbose
```

1. Pick an **acquisition type** matching one of the labels declared in
   `acquis.yaml` (syslog, nginx, apache2, sshd, docker, file, ...).
2. Optionally add **labels** in `key:value,key2:value2` form.
3. Either paste a single log line, or provide a path to a log file inside the
   container (typically under `/var/log/...`).
4. Click **Explain line** or **Explain file**. The full pipeline output is
   shown in a `<details>` block with the exit code.

### Simulation

Simulation mode lets you validate new scenarios without enforcing bans.
When enabled, CrowdSec still records alerts and decisions in its database,
but bouncers do not actually block the source IP.

1. Open **Simulation** to see the global state and per-scenario entries.
2. Use **Enable globally** / **Disable globally** to flip the global flag
   (`cscli simulation enable` / `disable`).
3. To dry-run a single scenario, type its name (`crowdsecurity/ssh-bf`) and
   click **Add scenario**. Use **Remove** to take it out of simulation.

### Notifications

Notification plugins forward alerts to external systems (email, Slack,
HTTP webhook, Sentinel). The plugin lifecycle is:

1. Drop a YAML config in `/etc/crowdsec/notifications/<name>.yaml`.
2. Reference it in `profiles.yaml` under `notifications`.
3. Reload CrowdSec (use the **Reload CrowdSec** action on the Hub page).

The **Notifications** page lists configured plugins (`cscli notifications
list`) and lets you inspect each one's resolved configuration. The YAML
files themselves are edited from the **Configuration** page.

### Console (Central API)

The CrowdSec Console is the central SaaS for aggregating alerts across
instances and sharing threat intelligence. Registration is free.

1. Open **Console** to see the registration status
   (`cscli console status`).
2. Use **Enable alert sharing** to push new alerts to the Console
   (`cscli console enable alerts`), or **Disable** to stop pushing.
3. The credentials file (`online_api_credentials.yaml`) is editable from
   the **Configuration** page if you need to re-register.

---

## Configuration reference

All configuration is via environment variables (overriding
`appsettings.json`). The Quadlet file sets sane defaults — override only
what you need.

| Variable | Default | Purpose |
| --- | --- | --- |
| `ADMIN_USER` | `admin` | First-boot admin username |
| `ADMIN_PASSWORD` | *(unset)* | First-boot admin password (REQUIRED on first start) |
| `CROWDSEC_BOUNCER_APIKEY` | *(auto)* | Pre-set the bouncer API key instead of auto-registering |
| `CrowdSec__BouncerApiKeyFile` | `<Database:Path directory>/crowdsec-manager-bouncer.key` | Persistent file for the auto-generated bouncer key (mode `0600`) |
| `CROWDSEC_STARTUP_TIMEOUT_SECONDS` | `120` | Maximum wait for the bundled CrowdSec image initializer and LAPI health check |
| `CROWDSEC_CONFIG_DIR` | `/etc/crowdsec` | Path to crowdsec config |
| `CROWDSEC_DATA_DIR` | `/var/lib/crowdsec/data` | Path to crowdsec data |
| `HOME_LATITUDE` / `HOME_LONGITUDE` / `HOME_LABEL` | Brussels | Home marker on the threat map |
| `CrowdSec__LocalApiUrl` | `http://127.0.0.1:8080` | LAPI URL |
| `CrowdSec__CliPath` | `/usr/bin/cscli` | cscli binary path |
| `CrowdSec__CommandTimeoutSeconds` | `120` | cscli subprocess timeout |
| `CrowdSec__HubRefreshSeconds` | `300` | Hub list cache TTL |
| `GeoIp__DatabasePath` | `/var/lib/GeoIP/GeoLite2-City.mmdb` | MaxMind DB path |
| `Database__Path` | `/var/lib/crowdsec-manager/app.db` | App SQLite DB |
| `DataProtection__KeysPath` | `<database directory>/keys` | Persistent ASP.NET cookie encryption keys |
| `ThreatFeed__PollIntervalSeconds` | `8` | LAPI poll interval |
| `ThreatFeed__MaxActiveArcs` | `60` | Max concurrent arcs on the map |

### Secrets

For production, do **not** put `ADMIN_PASSWORD` in the `.container` file
plaintext. Use a Quadlet environment file:

```ini
# /etc/containers/systemd/crowdsec-manager.env  (mode 0600, root:root)
ADMIN_PASSWORD=...
CROWDSEC_BOUNCER_APIKEY=...
```

Then uncomment the `EnvironmentFile=` line in `crowdsec-manager.container`.
The Data Protection key directory is restricted to the service account on
Linux; protect the containing host volume accordingly.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| UI loads but `CrowdSec LAPI: offline` in footer | CrowdSec daemon not running in the container or on the host | Check `sudo journalctl -u crowdsec-manager -f`; in bundled-engine mode the entrypoint stops the UI when CrowdSec exits and Quadlet restarts the container. In side-by-side mode, check/restart the host CrowdSec service. |
| Threat map stays empty | No alerts in CrowdSec, or source addresses cannot be geolocated | `cscli alerts list` to confirm; check the dashboard GeoIP status and see the [GeoIP database guide](docs/GEOIP_DATABASE.md) |
| `401 Unauthorized` from LAPI in logs | Bouncer API key missing, stale or from another LAPI | Check that the CrowdSec and manager data volumes are persistent and belong to the same deployment. If the CrowdSec database was reset, remove `/var/lib/crowdsec-manager/crowdsec-manager-bouncer.key` from the manager data volume and restart so the entrypoint registers a new key. |
| `cscli: command not found` | Wrong base image or runtime stage stripped cscli | Rebuild from the official `crowdsecurity/crowdsec` base image (see Dockerfile stage 2) |
| YAML save fails with "not in the editable whitelist" | File is outside the approved CrowdSec config paths | Keep edits under `/etc/crowdsec` or extend `ConfigFileService`'s approved paths |
| Login page loops after sign-in | Cookie not set (SecurePolicy mismatch over HTTP) | Either use HTTPS, or set `CookieSecurePolicy.SameAsRequest` (already default) and ensure the reverse proxy forwards the correct scheme |
| SELinux denials in `journalctl` | Missing `:Z` flag on bind mounts | Ensure every `Volume=` line in the Quadlet ends with `:Z` (the default file does) |

---

## Security notes

- The container runs as **root inside** (required by `cscli`) but uses
  Podman's user-namespace remapping by default on most distros; consider
  enabling `/etc/subuid` + `/etc/subgid` for true rootless operation.
- The Blazor UI is **not** TLS-terminated by default. Put a reverse proxy
  in front. The Identity cookie falls back to plain HTTP only when
  `CookieSecurePolicy.SameAsRequest` is set (already the case).
- The configuration editor allows editing `/etc/crowdsec/*.yaml` — this is
  intentional but powerful. Restrict UI access to your SOC network.
- Editing Hub-managed parser/scenario files can mark them tainted and affect
  future upgrades; the editor displays a warning before direct edits.
- Bouncer API keys are shown **once** at creation time ( CrowdSec stores
  them hashed). Save them immediately.

---

## Smoke test on a Linux host

The script `tests/shell/smoke-test.sh` orchestrates the end-to-end checks
that require a real Linux host with `sudo`, `podman`, `systemd` and `curl`.
It builds the image, starts a temporary container, runs through the
login/cookie round-trip, validates `crowdsec -t`, exercises the config
backup, adds a manual decision and verifies that the `ThreatFeedService`
is polling LAPI in the logs.

```bash
sudo ./tests/shell/smoke-test.sh
# or, after you already built the image:
sudo ./tests/shell/smoke-test.sh --no-build
# to also install the Quadlet and verify systemctl is-active/enabled:
sudo ./tests/shell/smoke-test.sh --quadlet
# to keep the container running at the end (for manual inspection):
sudo ./tests/shell/smoke-test.sh --keep-container
```

The script writes a detailed log to `/tmp/crowdsec-manager-smoke.log` and
exits `0` on success, `1` if any check failed, `2` if a prerequisite is
missing. It exercises the 7 "to validate on Linux host" items from
`TODO.md` in a single pass. A "TODO.md mapping" section at the end of the
log explicitly maps each script section to the corresponding TODO line.

---

## Development

### Run locally (without Docker)

Prerequisites: .NET 10 SDK, `cscli` on PATH (or set
`CrowdSec__CliPath=/path/to/cscli`).

```bash
cd src/CrowdSecManager
dotnet user-secrets set "Database:Path" "/tmp/csm-app.db"
dotnet user-secrets set "CrowdSec:ConfigDir" "/etc/crowdsec"
export ADMIN_USER=admin
export ADMIN_PASSWORD=Admin!234
dotnet run
```

Browse to <http://localhost:57938> (the HTTP port in
`Properties/launchSettings.json`). Run the unit tests with
`dotnet test src/CrowdSecManager.Tests/CrowdSecManager.Tests.csproj`.

### EF Core migrations (if you change the schema)

```bash
cd src/CrowdSecManager
dotnet tool restore                # if dotnet-ef is not installed
dotnet ef migrations add Initial
dotnet ef database update
```

The app currently uses `db.Database.EnsureCreated()` for simplicity —
switch to migrations before going to production with schema changes.

---

## License

This wrapper is provided as-is, free to use and modify. CrowdSec itself is
MIT-licensed — see <https://github.com/crowdsecurity/crowdsec>.
