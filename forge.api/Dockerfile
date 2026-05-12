# ── Stage 1: Build ────────────────────────────────────────────────────────────
# Use Debian-based SDK so pwsh + Playwright install work (Alpine lacks glibc)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files and restore (cached layer)
COPY forge.api/forge.api.csproj forge.api/
COPY forge.core/forge.core.csproj forge.core/
COPY forge.data/forge.data.csproj forge.data/
COPY forge.integrations/forge.integrations.csproj forge.integrations/
RUN dotnet restore forge.api/forge.api.csproj

# Copy source and publish
COPY . .
RUN dotnet publish forge.api/forge.api.csproj -c Release -o /app/publish \
    /p:UseAppHost=false

# Install Playwright browsers in the build stage so they land in /root/.cache/ms-playwright
# pwsh is pre-installed in the .NET SDK image
RUN apt-get update && apt-get install -y --no-install-recommends \
    libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 libxkbcommon0 \
    libxcomposite1 libxdamage1 libxfixes3 libxrandr2 libgbm1 libasound2 \
    && rm -rf /var/lib/apt/lists/* \
    && pwsh /app/publish/playwright.ps1 install chromium \
    && pwsh /app/publish/playwright.ps1 install ffmpeg

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Install Chromium (PuppeteerSharp fallback) + ffmpeg (AI video muxing).
# wget is required by the HEALTHCHECK below — aspnet:9.0 base image doesn't
# ship it and Docker's container-level probe was reporting "unhealthy" on
# every interval despite the API responding 200 to /api/v1/health.
RUN apt-get update && apt-get install -y --no-install-recommends \
    chromium \
    fonts-liberation \
    libnss3 \
    libatk-bridge2.0-0 \
    libx11-xcb1 \
    libxcomposite1 \
    libxdamage1 \
    libxrandr2 \
    libgbm1 \
    libasound2 \
    libpangocairo-1.0-0 \
    libgtk-3-0 \
    ffmpeg \
    wget \
    && rm -rf /var/lib/apt/lists/*

ENV PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium

RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
WORKDIR /app
COPY --from=build /app/publish .

# Copy Playwright browser binaries from the build stage
COPY --from=build /root/.cache/ms-playwright /root/.cache/ms-playwright
RUN chmod -R 755 /root/.cache/ms-playwright

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/api/v1/health || exit 1

USER appuser
EXPOSE 8080
ENTRYPOINT ["dotnet", "forge.api.dll"]
