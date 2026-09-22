#!/usr/bin/env bash
# Cross-checks the Postgres image MAJOR version across every place it's pinned
# (ARCHITECTURE_CYCLE8.md §74.3). "One place" isn't achievable literally — the same value has to be
# readable by C# (ServiceBooking.TestKit, Testcontainers), YAML (docker-compose*.yml,
# .github/workflows/ci.yml) and there is no generator step in this repo that would derive one from the
# other (T8-O4 explicitly rules out adding one - fifth configuration mechanism, §62). So the source of
# truth is ServiceBooking.TestKit/TestInfrastructure.cs's PostgresImage constant, and this script
# catches drift instead: it fails loudly if any other pin's MAJOR version disagrees with it.
#
# Exact TAG equality is deliberately NOT required — "postgres:16" (CI service container) and
# "postgres:16-alpine" (everywhere else) are both fine; only the major version in front of the first
# "-" or end of string has to match.
set -euo pipefail

cd "$(dirname "$0")/../.."

TESTKIT_FILE="ServiceBooking.TestKit/TestInfrastructure.cs"
[ -f "$TESTKIT_FILE" ] || { echo "::error::$TESTKIT_FILE not found — is check-image-pins.sh being run from a stale checkout?"; exit 1; }

# Authoritative value: `public const string PostgresImage = "postgres:16-alpine";`
authoritative_image=$(grep -oE 'PostgresImage\s*=\s*"[^"]+"' "$TESTKIT_FILE" | head -1 | sed -E 's/.*"([^"]+)".*/\1/') \
  || true
[ -n "${authoritative_image:-}" ] || { echo "::error::could not find PostgresImage constant in $TESTKIT_FILE"; exit 1; }

major_of() {
  # "postgres:16-alpine" -> 16 ; "postgres:16" -> 16
  echo "$1" | sed -E 's#^postgres:([0-9]+).*#\1#'
}

authoritative_major=$(major_of "$authoritative_image")
[ -n "$authoritative_major" ] && [[ "$authoritative_major" =~ ^[0-9]+$ ]] \
  || { echo "::error::could not parse a major version out of PostgresImage=\"$authoritative_image\""; exit 1; }

echo "[check-image-pins] authoritative: $TESTKIT_FILE PostgresImage=$authoritative_image (major $authoritative_major)"

status=0

# Checks EVERY postgres:<tag> reference in a file, not just the first — ci.yml alone has two
# (the `backend` job's service container and the `docker-build` job's `docker run` for smoke tests).
check_file() {
  local file="$1" label="$2"
  [ -f "$file" ] || { echo "::warning::$file not found, skipping"; return; }
  local found_any=0
  while IFS= read -r image; do
    [ -n "$image" ] || continue
    found_any=1
    local major
    major=$(major_of "$image")
    if [ "$major" != "$authoritative_major" ]; then
      echo "::error::$label ($file): pins postgres major $major (\"$image\"), expected major $authoritative_major (from $TESTKIT_FILE)"
      status=1
    else
      echo "[check-image-pins] OK: $label ($file) -> $image"
    fi
  done < <(grep -oE 'postgres:[0-9A-Za-z.-]+' "$file" | sort -u)
  if [ "$found_any" -eq 0 ]; then
    echo "::error::$label ($file): no postgres image pin found"
    status=1
  fi
}

check_file "docker-compose.yml" "docker-compose.yml"
check_file "docker-compose.prod.yml" "docker-compose.prod.yml"
check_file ".github/workflows/ci.yml" "ci.yml (service container + docker-build job)"

if [ "$status" -ne 0 ]; then
  echo "::error::Postgres image pins have drifted (ARCHITECTURE_CYCLE8.md §74.3). Bring every pin above to major $authoritative_major, or update PostgresImage in $TESTKIT_FILE if $authoritative_major is now wrong."
fi

exit "$status"
