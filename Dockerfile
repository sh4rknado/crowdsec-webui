# =====================================================================
# CrowdSec Manager - multi-stage containerfile (build + runtime)
# Target: Podman / Docker, .NET 8 Blazor Server + cscli
# =====================================================================

# ---------- Stage 1: build the .NET app ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
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

# ---------- Stage 2: runtime ----------
# We base on the official crowdsec image so cscli + crowdsec daemon are bundled,
# then add the .NET ASP.NET runtime on top.
FROM crowdsecurity/crowdsec:latest AS runtime

# The manager starts CrowdSec alongside ASP.NET, so it invokes the official
# image initializer itself instead of the base image's default ENTRYPOINT.
RUN test -f /docker_start.sh \
    && test -f /staging/etc/crowdsec/config.yaml \
    && test -f /staging/etc/crowdsec/local_api_credentials.yaml

# Switch to root for system-level installs
USER root

# Install ASP.NET 8 runtime + jq + ca-certificates + iproute2 (for cscli LAPI reload).
# The official CrowdSec image is Alpine Linux, so use apk and the musl runtime.
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

# Entrypoint: ensure cscli is callable, register our manager as a bouncer, then start dotnet
COPY deploy/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

# CrowdSec already exposes 8080 (LAPI); we expose 8081 for the Blazor UI
EXPOSE 8081 8080

# Health probe for the UI
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8081/health || exit 1

ENTRYPOINT ["tini", "--", "/usr/local/bin/entrypoint.sh"]
