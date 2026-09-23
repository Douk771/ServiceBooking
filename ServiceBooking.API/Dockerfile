# Do NOT change this tag to an -alpine variant. SkiaSharp.NativeAssets.Linux.NoDependencies (added in
# cycle B for image processing, ARCHITECTURE.md §2.1) ships a glibc-linked libSkiaSharp.so with no
# apt-get dependencies required — that only works on a glibc base like this one (Debian bookworm).
# Alpine is musl-based and would fail to load the native library at runtime. If Alpine is ever needed,
# swap the SkiaSharp native-assets package for a musl-compatible one first.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# curl is needed for two things the base image doesn't ship (aspnet:8.0 has neither curl nor wget):
# the docker-compose HEALTHCHECK (docker-compose.prod.yml, T-D7/US-43) and deploy/ci/smoke.sh polling
# /api/health/live from inside the same container network in CI (T-D1/US-41). ~1.5 MB, no dev headers.
#
# tzdata, explicit (ARCHITECTURE_CYCLE4.md §34.3): the base image already carries a timezone database
# today, and DeploymentSafetyChecks.ValidateTimeZoneDatabase() (resolving Asia/Barnaul, not the more
# forgiving Europe/Moscow) fail-fasts on startup if it's ever missing — but that check only turns a
# missing package into a caught deploy failure, not into no failure at all. An explicit install here
# costs one line and removes the dependency on Microsoft never trimming tzdata from a future aspnet:8.0
# image; the startup check stays as the second line of defense for every other way this could break.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl tzdata \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["ServiceBooking.API/ServiceBooking.API.csproj", "ServiceBooking.API/"]
COPY ["ServiceBooking.Core/ServiceBooking.Core.csproj", "ServiceBooking.Core/"]
COPY ["ServiceBooking.Infrastructure/ServiceBooking.Infrastructure.csproj", "ServiceBooking.Infrastructure/"]
COPY ["ServiceBooking.LegalKit/ServiceBooking.LegalKit.csproj", "ServiceBooking.LegalKit/"]
RUN dotnet restore "ServiceBooking.API/ServiceBooking.API.csproj"
RUN dotnet restore "ServiceBooking.LegalKit/ServiceBooking.LegalKit.csproj"
COPY . .
WORKDIR "/src/ServiceBooking.API"
RUN dotnet build "ServiceBooking.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ServiceBooking.API.csproj" -c Release -o /app/publish

# ARCHITECTURE_CYCLE11.md §104/§107.3: the `legal-tools` compose service runs this DLL against the same
# image — no separate build/registry entry, one image serves both the running API and the one-shot
# publish/rollback tool.
FROM build AS publish-tools
WORKDIR /src/ServiceBooking.LegalKit
RUN dotnet publish "ServiceBooking.LegalKit.csproj" -c Release -o /app/tools

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=publish-tools /app/tools ./tools
ENTRYPOINT ["dotnet", "ServiceBooking.API.dll"]
