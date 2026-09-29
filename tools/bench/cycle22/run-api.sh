#!/bin/bash
# Usage: run-api.sh <before|after> <port> [dbport]
# Starts the API (Release build) of the given version against DB bench_<version>. dbport (default 5432)
# lets the SQL-counting pass go through sqlproxy.py instead of Postgres directly.
V=$1; PORT=$2; DBPORT=${3:-5432}
DIR=/tmp/bench-$V/ServiceBooking.API
mkdir -p /tmp/bench/state/$V/public /tmp/bench/state/$V/private /tmp/bench/state/$V/logs
cd $DIR/bin/Release/net8.0 || exit 1
export ASPNETCORE_ENVIRONMENT=Testing
export ASPNETCORE_URLS=http://127.0.0.1:$PORT
export ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=$DBPORT;Database=bench_$V;Username=postgres;Password=postgres;SSL Mode=Disable;Maximum Pool Size=20"
export Jwt__Key=BENCH_ONLY_SECRET_KEY_AT_LEAST_32_CHARACTERS_LONG
export Jwt__Issuer=ServiceBooking
export Jwt__Audience=ServiceBookingClient
export SuperAdmin__Phone=+70000000009
export SuperAdmin__Email=superadmin@bench.local
export SuperAdmin__Password='SuperAdmin123!'
export SmartCaptcha__SecretKey=
export SmartCaptcha__SiteKey=
export Storage__PublicRoot=/tmp/bench/state/$V/public
export Storage__PrivateRoot=/tmp/bench/state/$V/private
export Logs__Directory=/tmp/bench/state/$V/logs
export Legal__Root=$DIR/App_Data/legal
# Benchmark only: lift per-IP limits that would otherwise 429 the measurement loops
# (appsettings.Testing.json already lifts the others).
export RateLimits__availability__PermitLimit=1000000
export RateLimits__booking-create__PermitLimit=1000000
export RateLimits__booking-create__AnonymousPermitLimit=1000000
export RateLimits__auth-login__PermitLimit=1000000
export RateLimits__auth-register__PermitLimit=1000000
exec dotnet ServiceBooking.API.dll
