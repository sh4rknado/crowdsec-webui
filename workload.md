# Workload - CrowdSec Manager

Operational and architectural reference for the CrowdSec Manager workload:
component breakdown, data flow, persistent state, failure modes, and the
procedures required to run it on a Podman + systemd host.

This document is the counterpart of `readme.md`. Where the readme is
"how do I deploy and use it", this is "what is running, where, and what
breaks if I touch X".

---

## 1. Workload identity

| Field | Value |
| --- | --- |
| Name | `crowdsec-manager` |
| Type | Long-running system service (systemd unit, Podman Quadlet) |
| Container image | `localhost/crowdsec-manager:latest` (built locally from `Dockerfile`) |
| Base image | `crowdsecurity/crowdsec:latest` (Debian-based, ships `crowdsec` + `cscli`) |
| App runtime | .NET 8.0 ASP.NET Core (Blazor Server) |
| Process model | 1 container, 2 logical processes: (a) `crowdsec` daemon, (b) `dotnet CrowdSecManager.dll` |
| Listen ports | `8080/tcp` (CrowdSec LAPI, in-container) and `8081/tcp` (Blazor UI) |
| Network mode | `host` (so cscli + LAPI can reach a host CrowdSec if present) |
| UID inside container | `0` (cscli requires root); rely on Podman userns remapping for isolation |
| Restart policy | `Restart=always`, `RestartSec=5` |

---

## 2. Components

### 2.1 Application processes

```
PID 1:  tini -> entrypoint.sh
          |
          +---> /bin/bash /docker_start.sh (background)
                    └── exec crowdsec -error         # Log processor + Local API (port 8080)
          +---> dotnet CrowdSecManager.dll             # Blazor UI (port 8081) - foreground
```

The `entrypoint.sh` script (run by `tini` as PID 1) is responsible for:

1. Creating bind-mount target directories.
2. Starting the official CrowdSec image initializer, which populates an empty `/etc/crowdsec` from `/staging/etc/crowdsec`, registers the local machine and then execs `crowdsec`; `cscli config gen` is not a supported CrowdSec command.
3. Waiting up to 120 seconds for the Local API health endpoint before registering `crowdsec-manager` as a bouncer; CrowdSec startup logs go to container stdout/stderr and therefore `journalctl`.
4. Persisting an auto-generated bouncer API key in the manager data volume so container restarts reuse the same credential.
5. Mapping the bouncer key and deployment paths into ASP.NET configuration variables.
6. `exec`ing `dotnet CrowdSecManager.dll` as the foreground process so termination signals propagate correctly.

If `NO_BOOTSTRAP_CROWDSEC=1` is set, config bootstrap and the in-container
CrowdSec daemon are skipped. Bouncer registration still runs unless an API
key was provided; `cscli` must then be configured for the host Local API.
This is the typical deployment when CrowdSec already runs as a host systemd
unit. The manager reads its API key and URL from `CrowdSec__BouncerApiKey` and
`CrowdSec__LocalApiUrl`; legacy `CROWDSEC_BOUNCER_APIKEY` and `LOCAL_API_URL`
names are mapped to those keys by the entrypoint and application.

### 2.2 .NET application services

| Service | Lifetime | Responsibility |
| --- | --- | --- |
| `AppDbContext` | Scoped | EF Core SQLite for app metadata: `ApplicationUser`, `IdentityRole`, `ThreatHistoryEntry`. |
| `ApplicationUser` / `IdentityRole` | — | ASP.NET Identity entities. Roles seeded at first boot: `Administrator`. |
| `ICrowdSecCliService` | Singleton | Wraps `cscli` via `Process.Start` with async stdout/stderr streaming, JSON output auto-append, configurable timeout. All hub/decisions/bouncers/machines/alerts commands go through this. |
| `ICrowdSecApiService` | Scoped (HttpClient) | REST client for CrowdSec LAPI. Bearer-token auth via `CrowdSec__BouncerApiKey` (the entrypoint maps the legacy `CROWDSEC_BOUNCER_APIKEY`); used for health and decision reads. Bouncer credentials cannot search alerts. |
| `IGeoIpService` | Singleton | MaxMind `DatabaseReader` over `GeoLite2-City.mmdb`. Falls back to a hash-derived location when no DB is mounted. Resolves private IPs to the home location. |
| `IConfigFileService` | Scoped | Reads/writes whitelisted top-level CrowdSec YAML plus YAML under `acquis.d`, `notifications`, parsers, scenarios, postoverflows, collections, contexts and AppSec config directories. Validates syntax with YamlDotNet and atomically replaces files. Tar-backs up `/etc/crowdsec/` on demand. |
| `ThreatFeedService` | Hosted (BackgroundService) | Polls alerts via `cscli` every `ThreatFeed:PollIntervalSeconds` (default 8s), reads decisions from LAPI, geolocates new attacker IPs, persists them to `ThreatHistoryEntry`, and publishes events through `ThreatMapEventBus`. Falls back to `cscli` for decisions if LAPI is unreachable. |
| `ThreatMapEventBus` | Singleton | In-process fan-out and bounded event snapshot for threat-map components. Events reach authenticated browsers only through their existing Blazor Server circuits; no separate public threat-map hub is mapped. |

### 2.3 Browser side

- Blazor Server renders the UI server-side; the browser receives DOM diffs over a SignalR circuit.
- The threat map subscribes to `ThreatMapEventBus` in its authenticated Blazor
  circuit. New events are forwarded to `window.threatMap.plotThreat(ev)` via
  JS interop; no anonymous `/hubs/threatmap` endpoint is exposed.
- `wwwroot/js/threatmap.js` renders a `<canvas>`:
  - Loads TopoJSON `countries-110m.json` from jsDelivr CDN (cached) for the
    world coastline. Falls back to a deterministic dot pattern if offline.
  - Equirectangular projection (`(lon, lat) -> (x, y)`).
  - Quadratic-Bézier arcs from source to home, with a trail and severity color.
  - Pulsing source dots, expanding impact ripples, and a fading ring around the home marker.
  - 60 simultaneous arcs max (`MAX_ARCS`); older arcs are evicted FIFO.

---

## 3. Data flow

### 3.1 Normal alert → threat map

```
[crowdsec log processor]
        │ parses /var/log/*, evaluates scenarios
        ▼
[CS SQLite DB]  ── (LAPI write) ──►  [crowdsec daemon / LAPI :8080]
                                              ▲
                                              │ cscli alerts list (poll every 8s)
                                              │
                                   [ThreatFeedService.PollOnceAsync]
                                              │ LAPI GET /v1/decisions
                                              │ Bearer: CROWDSEC_BOUNCER_APIKEY
                                              │
                                              │ for each new alert:
                                              │   1. Extract source IP
                                              │   2. GeoIpService.Resolve(ip)
                                              │   3. Build ThreatEvent
                                              │   4. Persist ThreatHistoryEntry
                                              │   5. ThreatMapEventBus.PublishThreat(ev, stats)
                                              ▼
                                   [ThreatMapEventBus → authenticated Blazor circuit]
                                              │
                                              ▼
                                   [Browser: ThreatMap.razor]
                                              │ JS interop
                                              ▼
                                   [Canvas: threatMap.plotThreat(ev)]
```

### 3.2 User action → CrowdSec

```
[Browser: button click]
        │ Blazor event handler
        ▼
[Component page (e.g. Decisions.razor)]
        │ invokes ICrowdSecCliService.AddDecisionAsync(ip, duration, ...)
        ▼
[CrowdSecCliService.RunAsync("decisions", "add", "-i", ip, "-d", dur, ...)]
        │ Process.Start("/usr/bin/cscli", ...)
        │ env CROWDSEC_CONFIG=/etc/crowdsec
        ▼
[cscli subprocess] ── (reads /etc/crowdsec/local_api_credentials.yaml) ──► [LAPI :8080]
        │ stdout (json) + stderr
        ▼
[CliResult]  ──►  [Component state]  ──►  [Blazor re-render]
```

### 3.3 Config edit → file

```
[ConfigEditor.razor]
        │ textarea binding to _draft
        ▼ Save()
[IConfigFileService.Save(relativePath, content)]
        │ 1. path check (top-level whitelist and approved config directories)
        │ 2. YamlDotNet parse (validation)
        │ 3. File.WriteAllText(/etc/crowdsec/<file>)
        ▼
[/etc/crowdsec/<file>]  ── (engine test + reload/restart picks it up) ──► [CrowdSec]
```

The editor can invoke `crowdsec -t -c /etc/crowdsec/config.yaml` to validate
the saved configuration with the engine. This checks the main configuration
and its references; runtime behavior still needs verification on the target
CrowdSec installation.

The UI offers an explicit reload action that sends `SIGHUP` to a CrowdSec
process in the same container. When using a host CrowdSec service, reload that
host service manually; the manager container cannot signal host processes.

---

## 4. Persistent state

| Path (host) | Path (container) | Type | Purpose | Backup? |
| --- | --- | --- | --- | --- |
| `/etc/crowdsec/` | `/etc/crowdsec/` (rw) | YAML | CrowdSec configuration, hub index, profiles, acquis, notifications | **Yes** — `tar` snapshot via Dashboard → Backup config |
| `/var/lib/crowdsec/` | `/var/lib/crowdsec/` (rw) | SQLite + blob | CrowdSec database (alerts, decisions, metrics, machines) | **Yes** — stop container, `cp -a` |
| `/var/lib/crowdsec-manager/` | `/var/lib/crowdsec-manager/` (rw) | SQLite | App DB: users, threat history (30-day window) | **Yes** — `app.db` can be hot-copied |
| `/var/lib/crowdsec-manager/backups/` | same | tgz | Dashboard-generated tar snapshots of `/etc/crowdsec/` | Yes |
| `/var/lib/GeoIP/GeoLite2-City.mmdb` | `/var/lib/GeoIP/` (ro) | MaxMind binary | IP geolocation | Yes (external download) |
| `/var/log/` | `/var/log/` (ro) | text | CrowdSec log sources | n/a (host logs) |

> The `:Z` suffix on every `Volume=` line tells Podman to apply SELinux
> private-volume relabeling. Without it, RHEL/Fedora hosts will deny the
> container access to bind-mounted host paths.

### 4.1 Schema (app DB)

```sql
-- AspNetUsers / AspNetRoles / AspNetUserRoles  (Identity defaults)
-- plus:
CREATE TABLE ThreatHistoryEntry (
    Id           INTEGER PRIMARY KEY,
    CreatedAt    TEXT NOT NULL,
    AlertId      INTEGER NOT NULL,
    SourceIp     TEXT,
    CountryIso   TEXT,
    Country      TEXT,
    City         TEXT,
    Latitude     REAL,
    Longitude    REAL,
    Scenario     TEXT,
    Message      TEXT,
    Severity     TEXT
);
CREATE INDEX IX_ThreatHistory_CreatedAt ON ThreatHistoryEntry (CreatedAt);
CREATE INDEX IX_ThreatHistory_SourceIp  ON ThreatHistoryEntry (SourceIp);
```

The `ThreatFeedService` trims entries older than 30 days every 100 inserts.

---

## 5. Network topology

```
                         ┌──────────────────────────┐
                         │       Host (Brussels)    │
                         │  systemd + Podman 4.4+   │
                         │                          │
                         │   ┌──────────────────┐   │
   browser  ──── :8081 ─►│   │ crowdsec-manager │   │
                         │   │   container      │   │
                         │   │  (Network=host)  │   │
                         │   └────────┬─────────┘   │
                         │            │             │
                         │   ┌────────▼─────────┐   │
                         │   │  CrowdSec daemon │   │
                         │   │  + LAPI :8080    │   │
                         │   │  + cscli         │   │
                         │   └────────┬─────────┘   │
                         │            │             │
                         │   ┌────────▼─────────┐   │
                         │   │  /var/log/nginx  │   │
                         │   │  /var/log/auth   │   │
                         │   │  ...             │   │
                         │   └──────────────────┘   │
                         └──────────────────────────┘
```

With `Network=host`, the container shares the host network namespace.
This means:

- The Blazor UI on `:8081` is reachable from anywhere that can reach the
  host on `8081`. **Filter this port** at the host firewall unless a
  reverse proxy is in front.
- `cscli` inside the container can talk to a host CrowdSec daemon on
  `127.0.0.1:8080` without extra plumbing.
- The same LAPI port is reused for in-container CrowdSec and host
  CrowdSec — pick one deployment model and stick to it (see §7).

### 5.1 Firewall recommendations

```bash
# Allow UI only from the SOC subnet
sudo firewall-cmd --add-rich-rule='rule family="ipv4" source address="10.20.0.0/24" port port=8081 protocol=tcp accept' --permanent
sudo firewall-cmd --reload
```

---

## 6. Failure modes & recovery

| Failure | Detection | Impact | Recovery |
| --- | --- | --- | --- |
| `crowdsec` daemon exits | `entrypoint.sh` background process dies; `cscli alerts list` returns non-zero | Threat map stops updating; CLI-based pages (Decisions/Hub/...) return errors | `podman restart crowdsec-manager` (entrypoint re-launches daemon) |
| LAPI unreachable (port 8080 down) | `CrowdSecApiService.IsReachableAsync()` returns false; footer shows "offline" | Threat map falls back to `cscli` polling (slower); dashboard stats degrade | `podman exec crowdsec-manager cscli lapi status`; restart LAPI |
| MaxMind DB missing | `GeoIpService.IsDatabaseAvailable == false`; dashboard card "GeoIP DB: missing" | Threat map plots IPs at hash-derived fallback locations | Drop `GeoLite2-City.mmdb` at `/var/lib/GeoIP/` and restart container |
| cscli binary missing | `CrowdSecCliService.RunAsync` returns exit code -1 with "Failed to start cscli" | All hub/decisions/bouncers/machines pages fail with descriptive error | Rebuild the image; ensure the Dockerfile stage 2 is based on `crowdsecurity/crowdsec:latest` |
| App DB locked / corrupt | EF Core throws `SqliteException` | Login fails; new threat events not persisted | Stop container, `cp /var/lib/crowdsec-manager/app.db /var/lib/crowdsec-manager/app.db.broken`, restart (DB is recreated on first boot via `EnsureCreated`) |
| Bouncer API key rejected by LAPI | Health may still succeed, but decision reads fail; alert history is read separately through `cscli` | Decision statistics may fall back to `cscli` | `podman exec crowdsec-manager cscli bouncers list`; register/copy a key and set `CROWDSEC_BOUNCER_APIKEY`, then restart the container |
| Browser disconnected from Blazor circuit | `DisconnectedCircuitMaxRetained` (30) reached; user sees "Reconnecting…" overlay | Threat map canvas freezes (no new events until reconnect) | User reloads; server keeps the SignalR hub open |
| Container OOM | `podman stats` shows memory pegged at limit; systemd restarts the unit | Cold restart of all services | Increase `LimitMemory` in the Quadlet; investigate with `dotnet-counters` |
| Disk full on `/var/lib/crowdsec-manager/` | SQLite write errors; threat history persist fails silently (logged at Debug level) | Threat map degrades; login may fail | Clean old backups in `/var/lib/crowdsec-manager/backups/`; prune old `ThreatHistoryEntry` rows |
| Host CrowdSec and in-container CrowdSec both run on 8080 | LAPI bind fails; only one process owns the port | Confusing state where some queries hit the wrong LAPI | Pick one model (see §7). Disable in-container CrowdSec with `NO_BOOTSTRAP_CROWDSEC=1` |

---

## 7. Deployment models

### Model A: self-contained (default)

The container ships and runs CrowdSec itself. Use this for a greenfield
deployment where the host has no existing CrowdSec.

- Quadlet `Network=host` is fine; in-container `crowdsec` binds `:8080`.
- All CrowdSec data lives in `/var/lib/crowdsec/` on the host.
- Bouncers (firewall, nginx, etc.) on the host connect to `127.0.0.1:8080`.

### Model B: side-by-side with host CrowdSec

You already run CrowdSec as a host systemd unit and want only the
management UI in a container.

```ini
# In crowdsec-manager.container, replace the bootstrap-related env vars:
Environment=NO_BOOTSTRAP_CROWDSEC=1
Environment=CrowdSec__LocalApiUrl=http://127.0.0.1:8080
Environment=CROWDSEC_BOUNCER_APIKEY=<key from host cscli bouncers add>
```

The container does not start its own `crowdsec` daemon. `cscli` inside
the container still works (it talks to the host LAPI via
`/etc/crowdsec/local_api_credentials.yaml`, which is bind-mounted).

### Model C: user (rootless) Quadlet

Move the `.container` file to `~/.config/containers/systemd/` and run
`systemctl --user daemon-reload`. You must:

- Use high ports (>1024) for both 8080 and 8081 (or rely on `sysctl
  net.ipv4.ip_unprivileged_port_start=80`).
- Replace `:Z` SELinux flags with the user-specific label scheme.
- Pre-create bind-mount dirs under `~/.local/share/crowdsec/...`.

This model is not the default — the provided `.container` file targets the
system service (rootful) use case.

---

## 8. Operational procedures

### 8.1 Updating the manager

```bash
cd /path/to/crowdsec-manager
git pull
sudo podman build -t localhost/crowdsec-manager:latest -f Dockerfile .
sudo systemctl restart crowdsec-manager
```

The app DB is migrated by `EnsureCreated` on boot — schema additions
require EF migrations if you've changed the model.

### 8.2 Updating CrowdSec

The base image is `crowdsecurity/crowdsec:latest`. To pull a new CrowdSec
release:

```bash
sudo podman pull crowdsecurity/crowdsec:latest
sudo podman build --no-cache -t localhost/crowdsec-manager:latest -f Dockerfile .
sudo systemctl restart crowdsec-manager

# Then upgrade hub content from the UI (Hub & Collections → Update index → Upgrade all)
```

### 8.3 Backup procedure

```bash
# Cold backup of all persistent state
sudo systemctl stop crowdsec-manager
sudo tar czf crowdsec-full-$(date +%Y%m%d).tgz \
    /etc/crowdsec \
    /var/lib/crowdsec \
    /var/lib/crowdsec-manager \
    /var/lib/GeoIP
sudo systemctl start crowdsec-manager
```

For ad-hoc config-only backups, use **Dashboard → Backup config** (no
restart required; produces a tarball under
`/var/lib/crowdsec-manager/backups/`).

### 8.4 Resetting the admin password

```bash
sudo podman exec -it crowdsec-manager bash
# Inside the container:
dotnet /app/CrowdSecManager.dll --  # not applicable
# Instead, edit the SQLite DB directly:
apt-get install -y sqlite3  # if missing
sqlite3 /var/lib/crowdsec-manager/app.db
# Inside sqlite3:
DELETE FROM "AspNetUsers" WHERE UserName='admin';
.exit
# Restart - the seeder will recreate admin from ADMIN_PASSWORD env var
exit
sudo systemctl restart crowdsec-manager
```

### 8.5 Auditing threat history

```bash
sudo podman exec crowdsec-manager sqlite3 /var/lib/crowdsec-manager/app.db \
    "SELECT date(CreatedAt) AS d, COUNT(*) FROM ThreatHistoryEntry GROUP BY d ORDER BY d DESC LIMIT 30;"
```

---

## 9. Resource sizing

| Host size | CPU | RAM | Disk | Expected load |
| --- | --- | --- | --- | --- |
| Small (homelab) | 1 vCPU | 512 MB | 1 GB | < 50 alerts/day, 1 user |
| Medium (small org) | 2 vCPU | 1 GB | 5 GB | 100–500 alerts/day, 2–3 concurrent users |
| Large (SOC) | 4 vCPU | 2 GB | 20 GB | > 1000 alerts/day, 5+ concurrent users |

The .NET Blazor Server holds one circuit per connected browser, each
consuming ~10–30 MB resident. The CrowdSec daemon adds ~80–150 MB.

Tune with the commented-out `LimitCPU` / `LimitMemory` lines in the Quadlet.

---

## 10. Observability

| Source | What you get | Where |
| --- | --- | --- |
| `journalctl -u crowdsec-manager -f` | All container stdout/stderr (Blazor + crowdsec + cscli) | host |
| `podman logs crowdsec-manager` | Same as journal | host |
| `/health` HTTP endpoint | Built-in health probe (returns 200 OK) | `curl http://localhost:8081/health` |
| `cscli metrics` (in-container) | CrowdSec ingestion / decision / parser counters | `podman exec crowdsec-manager cscli metrics` |
| App DB `ThreatHistoryEntry` | 30-day rolling window of geolocated attacks | sqlite query (see §8.5) |
| `--health-cmd` in Dockerfile | `curl -fsS http://127.0.0.1:8081/health` (30s interval) | reported to systemd via `Notify=healthy` |

For long-term metrics, point a Prometheus scrape at CrowdSec's built-in
`/metrics` endpoint on `:8080` (out of scope for this manager — it
focuses on the UI surface).

---

## 11. Test plan (smoke test)

After deploying, run through this checklist to confirm the workload is
healthy:

1. `systemctl status crowdsec-manager` → `active (running)`, health
   `healthy`.
2. `curl -fsS http://localhost:8081/health` → `200`.
3. Browse to `http://localhost:8081/` → redirects to `/login`.
4. Sign in with `admin` / `ADMIN_PASSWORD` → lands on the dashboard.
5. Footer shows `CrowdSec LAPI: online`.
6. Dashboard → **Update hub index** → `Hub index updated in X.Xs.`
7. **Decisions** → enter `1.2.3.4`, duration `1h`, click **Ban IP** → row
   appears in the table.
8. **Decisions** → click **Delete** on that row → row disappears.
9. **Configuration** → pick `acquis.yaml` → **Validate** → `YAML is valid.`
10. **Threat Map** → wait 30s → at least one arc animates if there are
    active alerts; right panel shows stats and recent attacks.
11. **Bouncers** → click **Generate API key** for `test-bouncer` → API
    key is shown once and the bouncer appears in the list.
12. **Bouncers** → **Revoke** `test-bouncer` → row disappears.
13. `sudo journalctl -u crowdsec-manager --since "10 min ago" | grep -i
    error` → no errors.

If any step fails, consult §6 (Failure modes) or the readme's
troubleshooting table.

---

## 12. Open items / future work

- TLS termination: integrate Caddy as a sidecar Quadlet (`*.container` +
  `Pod=`), or document a recommended nginx front-end config.
- EF Core migrations: switch from `EnsureCreated` to a real migration
  pipeline before adding schema-changing features.
- Threat feed enrichment: optional pull from public feeds (abuse.ch,
  Emerging Threats) for a richer map (originally proposed as an option,
  deferred to keep the v1 surface focused on CrowdSec's own data).
- Threat map clustering: when an IP fires many alerts, cluster source
  dots instead of stacking them.
- Audit log: persist every UI action (decision add/remove, config edit)
  to a tamper-evident log.
