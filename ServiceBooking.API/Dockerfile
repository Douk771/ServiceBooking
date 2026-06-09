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
