#!/usr/bin/env bash
# Smoke-tests the built frontend artifact the way US-112 (ARCHITECTURE_CYCLE9.md §103.2) requires: prove
# the tab icon is actually served with a real HTTP request against the built dist/, not just present in
# frontend/public/ in source. This is NOT part of deploy/ci/smoke.sh - that script targets the API Docker
# image's $BASE_URL, and the API never serves these files (no wwwroot/SPA fallback in Program.cs;
# production serves frontend/dist via nginx on the host, deploy/nginx/ezbook.conf, a separate artifact).
#
# Runnable two ways:
#   - from CI (frontend job in .github/workflows/ci.yml), right after `npm run build`, against
#     frontend/dist;
#   - by hand, against any already-built dist: `DIST_DIR=frontend/dist deploy/ci/smoke-frontend.sh`
#
# SMOKE_PROFILE=goods (ARCHITECTURE_CYCLE23.md §401.4, cycle 24 §463.2) checks the goods.ezbook.ru build in
# dist/__goods: index.html with the React root, favicon.ico/svg, apple-touch-icon.png, and — since cycle 24 (goods is
# a Home Screen app with push) — the same home-screen checks as ezbook: manifest served, parsed, display=standalone,
# linked from index.html, and GET /sw.js = 200. Default profile (ezbook) is unchanged.
#
# SMOKE_PROFILE=dom (ARCHITECTURE_CYCLE37.md §37.15.4) checks the dom.ezbook.ru build in dist/__dom: index.html with the React
# root, favicon.ico/svg, apple-touch-icon.png, manifest (display=standalone) and GET /sw.js = 200 — same as goods.
#
# Any failed step exits 1.
set -euo pipefail

DIST_DIR="${DIST_DIR:-frontend/dist}"
SMOKE_PROFILE="${SMOKE_PROFILE:-ezbook}"
PORT="${PORT:-4173}"
BASE_URL="http://127.0.0.1:$PORT"
READY_TIMEOUT_SECONDS="${READY_TIMEOUT_SECONDS:-15}"

log() { echo "[smoke-frontend] $*"; }
fail() { echo "[smoke-frontend] FAILED: $*" >&2; exit 1; }

[ -d "$DIST_DIR" ] || fail "$DIST_DIR does not exist - run \`npm run build\` first"

log "serving $DIST_DIR on $BASE_URL"
python3 -m http.server "$PORT" --directory "$DIST_DIR" >/tmp/smoke-frontend-server.log 2>&1 &
SERVER_PID=$!
trap 'kill "$SERVER_PID" 2>/dev/null || true' EXIT

log "waiting for static server (timeout ${READY_TIMEOUT_SECONDS}s)"
deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
until code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL/index.html" 2>/dev/null) && [ "$code" = "200" ]; do
  kill -0 "$SERVER_PID" 2>/dev/null || fail "static server died before serving anything - see /tmp/smoke-frontend-server.log"
  [ "$SECONDS" -lt "$deadline" ] || fail "timed out waiting for static server (last code: ${code:-none})"
  sleep 0.5
done
log "server up"

# favicon.svg is the primary, modern-browser icon; favicon.ico covers bookmarks/history/older browsers
# (ARCHITECTURE_CYCLE9.md §103.2 table) - both must actually ship in dist/, not just public/.
favicon_ico_code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL/favicon.ico")
[ "$favicon_ico_code" = "200" ] || fail "GET /favicon.ico returned $favicon_ico_code (expected 200)"
favicon_svg_code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL/favicon.svg")
[ "$favicon_svg_code" = "200" ] || fail "GET /favicon.svg returned $favicon_svg_code (expected 200)"
log "favicon OK"

if [ "$SMOKE_PROFILE" = "goods" ] || [ "$SMOKE_PROFILE" = "dom" ]; then
  index_html=$(curl -sf "$BASE_URL/index.html") || fail "GET /index.html failed"
  grep -q '<div id="root">' <<<"$index_html" || fail "$SMOKE_PROFILE index.html has no <div id=\"root\">"
  apple_icon_code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL/apple-touch-icon.png")
  [ "$apple_icon_code" = "200" ] || fail "GET /apple-touch-icon.png returned $apple_icon_code (expected 200)"
  # Cycle 24 (ARCHITECTURE_CYCLE24.md §454, §463.2): goods is a standalone app now.
  curl -sf "$BASE_URL/manifest.webmanifest" -o /tmp/smoke-frontend-goods-manifest.json \
    || fail "$SMOKE_PROFILE: GET /manifest.webmanifest did not return 200"
  python3 - /tmp/smoke-frontend-goods-manifest.json <<'PY' || fail "goods manifest.webmanifest must parse and have display=standalone, start_url, scope and icons"
import json, sys
m = json.load(open(sys.argv[1], encoding="utf-8"))
assert m.get("display") in ("standalone", "fullscreen"), m.get("display")
assert m.get("start_url") and m.get("scope"), (m.get("start_url"), m.get("scope"))
assert m.get("icons"), "icons"
PY
  grep -q 'rel="manifest" href="/manifest.webmanifest"' <<<"$index_html" || fail "goods index.html does not link the manifest"
  grep -q 'rel="apple-touch-icon"' <<<"$index_html" || fail "goods index.html does not link apple-touch-icon"
  sw_code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL/sw.js")
  [ "$sw_code" = "200" ] || fail "$SMOKE_PROFILE: GET /sw.js returned $sw_code (expected 200)"
  log "ALL FRONTEND SMOKE CHECKS PASSED ($SMOKE_PROFILE)"
  exit 0
fi

# ARCHITECTURE_CYCLE21.md §365 (US-21-03) - iPhone Web Push exists only for an app opened from the
# Home Screen, and iOS opens it as an app only when the served manifest says `display: standalone`.
# Cycle 9 shipped `display: "browser"`, which turned "Add to Home Screen" into a plain Safari bookmark
# with no push at all - silently. A request, not a glance at public/: the manifest must ship, parse,
# and keep the standalone display mode; index.html must still link it.
curl -sf "$BASE_URL/manifest.webmanifest" -o /tmp/smoke-frontend-manifest.json \
  || fail "GET /manifest.webmanifest did not return 200"
python3 - /tmp/smoke-frontend-manifest.json <<'PY' || fail "manifest.webmanifest must parse and have display=standalone, start_url, scope and icons"
import json, sys
m = json.load(open(sys.argv[1], encoding="utf-8"))
assert m.get("display") in ("standalone", "fullscreen"), m.get("display")
assert m.get("start_url") and m.get("scope"), (m.get("start_url"), m.get("scope"))
assert m.get("icons"), "icons"
PY
index_html=$(curl -sf "$BASE_URL/index.html") || fail "GET /index.html failed"
grep -q 'rel="manifest" href="/manifest.webmanifest"' <<<"$index_html" || fail "index.html no longer links the manifest"
grep -q 'rel="apple-touch-icon"' <<<"$index_html" || fail "index.html no longer links apple-touch-icon"
apple_icon_code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL/apple-touch-icon.png")
[ "$apple_icon_code" = "200" ] || fail "GET /apple-touch-icon.png returned $apple_icon_code (expected 200)"
log "home screen manifest OK (display=standalone)"

log "ALL FRONTEND SMOKE CHECKS PASSED"
