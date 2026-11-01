# =====================================================================
# CrowdSec Manager - single-container image (build + runtime)
#
# One container ships BOTH:
#   - the CrowdSec engine (daemon + LAPI on 8080)  -> from the base image
#   - the ASP.NET 8 Blazor manager UI (on 8081)    -> added on top
#
# The two processes share the same container network namespace, so the
# manager reaches LAPI at http://127.0.0.1:8080 without crossing the
# host network. The host only needs to publish 8081 (the UI). Publish
# 8080 too ONLY if host-side bouncers must query LAPI directly.
# =====================================================================

# Global build arguments (declared BEFORE the first FROM so they can be used
# in FROM lines). Override on the command line, e.g.:
#   podman build --build-arg CROWDSEC_IMAGE=crowdsecurity/crowdsec:v1.6.8 ...
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0
ARG CROWDSEC_IMAGE=crowdsecurity/crowdsec:latest

# ---------- Stage 1: build the .NET app ----------
FROM ${DOTNET_SDK_IMAGE} AS build
LABEL stage=build

WORKDIR /src

# Restore first for better layer caching
COPY src/CrowdSecManager.sln ./CrowdSecManager.sln
COPY src/CrowdSecManager/CrowdSecManager.csproj ./CrowdSecManager/CrowdSecManager.csproj
RUN dotnet restore ./CrowdSecManager/CrowdSecManager.csproj

# Copy the rest of the source and publish
COPY src/ ./
RUN dotnet publish ./CrowdSecManager/CrowdSecManager.csproj \
    -c Release \
    -o /app \
    /p:UseAppHost=false \
    --no-restore

# ---------- Stage 2: runtime (CrowdSec + ASP.NET in ONE container) ----------
# Pin a version in production instead of :latest, e.g. crowdsecurity/crowdsec:v1.6.x
FROM ${CROWDSEC_IMAGE} AS runtime

# Sanity-check that the CrowdSec base image ships its initializer and a
# staged default config. The entrypoint relies on these.
RUN test -f /docker_start.sh \
    && test -f /staging/etc/crowdsec/config.yaml \
    && test -f /staging/etc/crowdsec/local_api_credentials.yaml

# Switch to root for system-level installs
USER root

# Install ASP.NET 8 runtime + jq + ca-certificates + iproute2 (for cscli LAPI reload
# and for the host-side port preflight). The official CrowdSec image is Alpine Linux,
# so use apk and the musl runtime.
RUN apk add --no-cache \
        bash \
        ca-certificates \
        curl \
        icu-libs \
        iproute2 \
        jq \
        libstdc++ \
        procps \
        tini \
    && curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 8.0 --runtime aspnetcore --install-dir /usr/share/dotnet \
    && ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm -f /tmp/dotnet-install.sh

ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_URLS=http://+:8081 \
    ASPNETCORE_ENVIRONMENT=Production

# Where CrowdSec Manager expects its volumes
ENV CROWDSEC_CONFIG_DIR=/etc/crowdsec \
    CROWDSEC_DATA_DIR=/var/lib/crowdsec/data \
    GEOIP_DB_PATH=/var/lib/GeoIP/GeoLite2-City.mmdb \
    Database__Path=/var/lib/crowdsec-manager/app.db

# Copy the published app
WORKDIR /app
COPY --from=build /app ./

# Helper directories
RUN mkdir -p /var/lib/crowdsec-manager/backups /var/lib/GeoIP

# Entrypoint: bootstrap CrowdSec, register the manager as a bouncer, then run both
COPY deploy/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

# Two listeners inside the container:
#   8080 -> CrowdSec Local API (LAPI)  - internal, reached via 127.0.0.1:8080
#   8081 -> CrowdSec Manager UI (Blazor) - publish this one to the host
# They are distinct ports, so there is never an in-container bind conflict.
EXPOSE 8080 8081

# Health probe for the UI (LAPI has its own /health on 8080)
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8081/health || exit 1

ENTRYPOINT ["tini", "--", "/usr/local/bin/entrypoint.sh"]
