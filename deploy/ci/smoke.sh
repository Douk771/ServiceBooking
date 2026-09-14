#!/usr/bin/env bash
# Smoke-tests the built API image the way ARCHITECTURE.md §12.2 (T-D1, US-41) describes: hit a real
# running container over real HTTP and prove the thing that plain `dotnet build`/`dotnet run` cannot —
# that SkiaSharp.NativeAssets.Linux.NoDependencies actually loads inside THIS container, by running an
# image upload end to end and checking it comes back as a real, servable file.
#
# Runnable two ways:
#   - from CI (docker-build job in .github/workflows/ci.yml), against a container started there;
#   - by hand, against any running instance: `BASE_URL=http://localhost:5000 deploy/ci/smoke.sh`.
#
# Any failed step exits 1 — the caller (CI job) is expected to print `docker logs <container>` on
# failure so the actual crash/exception is visible, not just "smoke failed".
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
LIVE_TIMEOUT_SECONDS="${LIVE_TIMEOUT_SECONDS:-90}"
READY_TIMEOUT_SECONDS="${READY_TIMEOUT_SECONDS:-90}"

# A random phone per run - reruns against a container that already migrated (e.g. local debugging)
# must not collide with a previous run's account. $RANDOM is 0-32767, i.e. 1-5 digits, so two of them
# concatenated can be as short as 2 digits - zero-pad each to 5 first, or a short run makes the phone
# fewer than 10 digits, PhoneNormalizer 400s, and smoke fails for a reason that has nothing to do with
# the thing it's testing.
SUFFIX="$(printf '%05d%05d' "$RANDOM" "$RANDOM")"
PHONE="+7999${SUFFIX:0:7}"
PASSWORD="Sm0ke!Test$SUFFIX"

log() { echo "[smoke] $*"; }
fail() { echo "[smoke] FAILED: $*" >&2; exit 1; }

# Step 1: /api/health/live must answer 200 — polled, not slept, so a fast-booting container doesn't
# waste the timeout and a slow one (migrations still running is NOT this check - see step 2) isn't
# declared dead early.
log "waiting for /api/health/live (timeout ${LIVE_TIMEOUT_SECONDS}s)"
deadline=$((SECONDS + LIVE_TIMEOUT_SECONDS))
until code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL/api/health/live" 2>/dev/null) && [ "$code" = "200" ]; do
  [ "$SECONDS" -lt "$deadline" ] || fail "timed out waiting for /api/health/live (last code: ${code:-none})"
  sleep 1
done
log "live OK"

# Step 2: /api/health/ready must answer 200 - this is the actual confirmation that migrations applied
# (DatabaseReadyHealthCheck, ARCHITECTURE.md §10.2), which is what start_period covers in compose.
log "waiting for /api/health/ready (timeout ${READY_TIMEOUT_SECONDS}s)"
deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
until code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL/api/health/ready" 2>/dev/null) && [ "$code" = "200" ]; do
  [ "$SECONDS" -lt "$deadline" ] || fail "timed out waiting for /api/health/ready (last code: ${code:-none})"
  sleep 1
done
log "ready OK"

# Step 3: register a throwaway account (acceptedLegal:true - US-37 makes this mandatory) and grab the
# token. This is also an implicit check that Legal:Root loaded a valid manifest in this exact image
# (Program.cs fail-fasts on startup otherwise, ARCHITECTURE.md §4.4 - so if this endpoint answers at
# all with a token, the legal documents shipped correctly in the published image).
log "registering throwaway account $PHONE"
register_response=$(curl -s -w '\n%{http_code}' -X POST "$BASE_URL/api/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"firstName\":\"Smoke\",\"lastName\":\"Test\",\"phone\":\"$PHONE\",\"password\":\"$PASSWORD\",\"acceptedLegal\":true}")
register_code=$(echo "$register_response" | tail -n1)
register_body=$(echo "$register_response" | sed '$d')
[ "$register_code" = "200" ] || fail "POST /api/auth/register returned $register_code, body: $register_body"
TOKEN=$(echo "$register_body" | python3 -c 'import sys,json; print(json.load(sys.stdin)["token"])' 2>/dev/null) \
  || fail "could not extract token from register response: $register_body"
log "registered, got token"

# Step 4: upload a real JPEG as the avatar (POST /api/profile/avatar) - the shortest of the four upload
# endpoints (no company needed) and the one thing in this whole product that actually calls into
# SkiaSharp's native library (decode -> EXIF orientation -> resize -> re-encode, ARCHITECTURE.md §4.12).
# The 64x64 red-ish JPEG below is a real, valid file (base64), not a placeholder - if the native
# libSkiaSharp.so failed to load in this image, this is the request that throws.
TMP_JPEG="$(mktemp /tmp/smoke-avatar-XXXXXX.jpg)"
trap 'rm -f "$TMP_JPEG"' EXIT
base64 -d > "$TMP_JPEG" <<'JPEG_B64'
/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAoHBwgHBgoICAgLCgoLDhgQDg0NDh0VFhEYIx8lJCIfIiEmKzcvJik0KSEiMEExNDk7Pj4+JS5ESUM8SDc9Pjv/2wBDAQoLCw4NDhwQEBw7KCIoOzs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozv/wAARCABAAEADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDNooor6o6AooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigD/9k=
JPEG_B64

log "uploading avatar (SkiaSharp path)"
upload_response=$(curl -s -w '\n%{http_code}' -X POST "$BASE_URL/api/profile/avatar" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@${TMP_JPEG};type=image/jpeg")
upload_code=$(echo "$upload_response" | tail -n1)
upload_body=$(echo "$upload_response" | sed '$d')
# ARCHITECTURE.md §19.5: the actual, contract-documented response code is 200, not the 201 SPEC US-41
# p.3 names - matching the real API_CONTRACT.md rather than the SPEC wording, deliberately.
[ "$upload_code" = "200" ] || fail "POST /api/profile/avatar returned $upload_code (expected 200), body: $upload_body"
AVATAR_URL=$(echo "$upload_body" | python3 -c 'import sys,json; print(json.load(sys.stdin)["avatarUrl"])' 2>/dev/null) \
  || fail "could not extract avatarUrl from upload response: $upload_body"
[ -n "$AVATAR_URL" ] && [ "$AVATAR_URL" != "None" ] || fail "avatarUrl was empty in upload response: $upload_body"
log "avatar uploaded: $AVATAR_URL"

# Step 5: the processed file must actually be servable over HTTP - proves SavePublicAsync + UseStaticFiles
# wiring works too, not just the SkiaSharp call in isolation.
serve_code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL$AVATAR_URL")
[ "$serve_code" = "200" ] || fail "GET $AVATAR_URL returned $serve_code (expected 200)"
log "avatar served back OK"

log "ALL SMOKE CHECKS PASSED"
