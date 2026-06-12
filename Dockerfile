# =============================================================================
#  Sweeprr — Production Multi-Stage Dockerfile
#
#  Stage 1 (client-build): Compile React SPA with pnpm → dist/
#  Stage 2 (api-build):    Embed dist/ into wwwroot/, dotnet publish
#  Stage 3 (runtime):      Slim aspnet:9.0 image, single port, /config volume
#
#  Run:
#    docker build -t sweeprr .
#    docker run -p 8080:8080 -v sweeprr_config:/config sweeprr
#
#  The app is then available at http://localhost:8080.
#  Mount a named volume at /config to persist the database and Data
#  Protection keys across container restarts.
# =============================================================================


# ── Stage 1: Build React client ───────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM node:22-alpine AS client-build

# Install pnpm explicitly (avoids corepack version-negotiation edge cases)
RUN npm install -g pnpm@11.5.1

WORKDIR /workspace

# Copy workspace manifests first so `pnpm install` is cached until they change
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml ./
COPY Sweeprr.Client/package.json Sweeprr.Client/

RUN pnpm install --frozen-lockfile

# Copy full client source and build
COPY Sweeprr.Client/ Sweeprr.Client/

RUN pnpm --filter sweeprr-client build
# Produces: Sweeprr.Client/dist/ (hashed JS/CSS, index.html)


# ── Stage 2: Build .NET API ───────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS api-build
WORKDIR /src

# Restore deps in a separate layer (cached until csproj changes)
COPY Sweeprr.API/Sweeprr.API.csproj Sweeprr.API/
RUN dotnet restore Sweeprr.API/Sweeprr.API.csproj

# Copy API source
COPY Sweeprr.API/ Sweeprr.API/

# Embed the compiled SPA into wwwroot before publish so it ships inside the image
COPY --from=client-build /workspace/Sweeprr.Client/dist ./Sweeprr.API/wwwroot/

RUN dotnet publish Sweeprr.API/Sweeprr.API.csproj \
        -c Release \
        -o /app/publish \
        --no-restore
# /app/publish/wwwroot/ now contains the React SPA


# ── Stage 3: Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends wget tzdata gosu && rm -rf /var/lib/apt/lists/*

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConfigDir=/config

# /config holds sweeprr.db + Data Protection keys.
# Always mount a persistent volume here:
#   docker run -v sweeprr_config:/config ...
VOLUME ["/config"]

COPY --from=api-build /app/publish .

# Copy and configure the entrypoint script for PUID/PGID support
COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
