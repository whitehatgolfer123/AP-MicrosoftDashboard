# ============================================================================
# Container image for the Microsoft Ads Optimisation Platform.
#
# Multi-stage: the SDK image restores, builds and publishes; only the published
# output and the ASP.NET runtime reach the final image. Phase 0 of ROADMAP.md
# asks for a container definition; Phase 27 builds on this for hosting.
#
#   docker build -t msads-auditor .
#   docker run --rm -p 5114:8080 msads-auditor
#
# The image runs in Demo mode with no credentials by default, which keeps the
# "a fresh checkout runs with no credentials" guarantee true in a container too.
# ============================================================================

# --- build -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only what restore needs first, so the restore layer caches independently
# of source edits.
COPY global.json Directory.Build.props APMicrosoftTool.slnx ./
COPY src/MsAds.Auditor.Core/MsAds.Auditor.Core.csproj       src/MsAds.Auditor.Core/
COPY src/MsAds.Auditor.BingAds/MsAds.Auditor.BingAds.csproj src/MsAds.Auditor.BingAds/
COPY src/MsAds.Auditor.Ai/MsAds.Auditor.Ai.csproj           src/MsAds.Auditor.Ai/
COPY src/MsAds.Auditor.Web/MsAds.Auditor.Web.csproj         src/MsAds.Auditor.Web/
COPY tests/MsAds.Auditor.Tests/MsAds.Auditor.Tests.csproj   tests/MsAds.Auditor.Tests/
RUN dotnet restore

COPY . .

# Build the whole solution with warnings fatal, so an image is never produced
# from source that would fail CI.
RUN dotnet build --no-restore -c Release -warnaserror

FROM build AS test
RUN dotnet test --no-build -c Release --verbosity normal

# --- publish -----------------------------------------------------------------
FROM build AS publish
RUN dotnet publish src/MsAds.Auditor.Web \
        --no-build -c Release -o /app/publish

# --- runtime -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is not in the aspnet image, and HEALTHCHECK without it fails silently
# rather than loudly. Installed as root before dropping to the app user.
USER root
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/*

# Culture data is needed: money and dates are formatted per account culture by
# MetricFormatter, so the invariant-only runtime would render them wrongly.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

# Run as the non-root user the base image already provides.
USER $APP_UID

COPY --from=publish /app/publish .

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/health/live || exit 1"]

ENTRYPOINT ["dotnet", "MsAds.Auditor.Web.dll"]
