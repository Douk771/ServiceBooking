# Do NOT change this tag to an -alpine variant. SkiaSharp.NativeAssets.Linux.NoDependencies (added in
# cycle B for image processing, ARCHITECTURE.md §2.1) ships a glibc-linked libSkiaSharp.so with no
# apt-get dependencies required — that only works on a glibc base like this one (Debian bookworm).
# Alpine is musl-based and would fail to load the native library at runtime. If Alpine is ever needed,
# swap the SkiaSharp native-assets package for a musl-compatible one first.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["ServiceBooking.API/ServiceBooking.API.csproj", "ServiceBooking.API/"]
COPY ["ServiceBooking.Core/ServiceBooking.Core.csproj", "ServiceBooking.Core/"]
COPY ["ServiceBooking.Infrastructure/ServiceBooking.Infrastructure.csproj", "ServiceBooking.Infrastructure/"]
RUN dotnet restore "ServiceBooking.API/ServiceBooking.API.csproj"
COPY . .
WORKDIR "/src/ServiceBooking.API"
RUN dotnet build "ServiceBooking.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ServiceBooking.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ServiceBooking.API.dll"]
