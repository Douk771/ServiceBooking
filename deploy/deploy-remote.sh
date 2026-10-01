#!/usr/bin/env bash
# Runs ON THE VPS. Called by deploy/deploy.sh after it has already:
#   1. downloaded the CI-built frontend `dist` for the commit being deployed, and
#   2. scp'd it into a fresh timestamped directory under /var/www/ezbook/releases/<ts>/ on this box.
#
# This script's job (T-D3, US-44, ARCHITECTURE.md §12.3):
#   - tag the current API image `previous` and record the current release symlink, so rollback.sh has
#     something to go back to;
#   - atomically switch the `current` symlink nginx serves from to the new release;
#   - rebuild + restart the API container;
#   - wait for /api/health/ready (NOT just "container started" — that would ship a container still
#     mid-migration as if the deploy had succeeded);
#   - if readiness never comes: say so in plain words, DUMP THE ACTUAL FAILURE CAUSE from the
#     container's own logs (see dump_failure_diagnostics below — a run failing on this step used to
#     say only "readiness did not come back", with the real reason sitting silently in the api_logs
#     volume, see incident writeup referenced from DEPLOY.md §10.2c), and print the exact rollback
#     command as the very next line, so a tired operator can copy-paste it without thinking.
#
# Usage: deploy-remote.sh <release-timestamp>
#   <release-timestamp> must already exist as a directory under /var/www/ezbook/releases/
set -euo pipefail

RELEASE_TS="${1:?Usage: deploy-remote.sh <release-timestamp> (matching a directory under /var/www/ezbook/releases/)}"

cd "$(dirname "${BASH_SOURCE[0]}")/.."   # repo root (deploy/ is one level down)

WEB_ROOT="${WEB_ROOT:-/var/www/ezbook}"
RELEASES_DIR="$WEB_ROOT/releases"
CURRENT_LINK="$WEB_ROOT/current"
PREVIOUS_MARKER="$WEB_ROOT/.previous"
KEEP_RELEASES=3
READY_TIMEOUT_SECONDS=120
# ARCHITECTURE_CYCLE23.md §401.3 — goods.ezbook.ru is served by its own nginx vhost from current/__goods.
# GOODS_SMOKE=0 is the emergency off switch (e.g. the vhost is being reissued); it does NOT skip ezbook checks.
GOODS_HOST="${GOODS_HOST:-goods.ezbook.ru}"
GOODS_SMOKE="${GOODS_SMOKE:-1}"

NEW_RELEASE_DIR="$RELEASES_DIR/$RELEASE_TS"
[ -d "$NEW_RELEASE_DIR" ] || { echo "ERROR: $NEW_RELEASE_DIR does not exist — did deploy.sh finish uploading it?" >&2; exit 1; }

rollback_hint() {
  echo "ДЕПЛОЙ НЕУСПЕШЕН."
  echo "ssh $(whoami)@$(hostname -f 2>/dev/null || hostname) 'cd $(pwd) && bash deploy/rollback.sh'"
}

# Prints the tail of both places a startup failure can be hiding:
#   1. whatever the container wrote to stdout/stderr before dying/restarting (`docker compose logs`
#      keeps this even for a crash-looping/exited container — unlike `exec`, it needs no running
#      process);
#   2. the Serilog FILE sink under /app/logs, which lives in the `api_logs` NAMED VOLUME and therefore
#      survives the container recreate that just happened — this is where the 2026-09-23 incident's
#      actual cause (`Unknown legal document type 'Terms'`) was found, and it never reached the run's
#      own log because nothing had dumped it there before. `docker compose run` (not `exec`) is used so
#      this works even when the api service is currently down/restarting — it starts a short-lived
#      sibling container from the same image+volumes, not a shell into the broken one.
# Kept short on purpose (tail, not cat) — the goal is "visible in the run log", not "reproduce logs.txt
# wholesale here".
dump_failure_diagnostics() {
  echo "------------------------------------------------------------------"
  echo "==> Diagnostics: last 80 lines of 'docker compose logs api'"
  docker compose -f docker-compose.prod.yml logs --no-color --tail=80 api 2>&1 \
    || echo "    (could not read container logs)"
  echo "==> Diagnostics: last 60 lines of the Serilog file sink (api_logs volume, survives the restart)"
  docker compose -f docker-compose.prod.yml run --rm --no-deps --entrypoint sh api -c \
    'f=$(ls -t /app/logs/*.txt 2>/dev/null | head -n1); if [ -n "$f" ]; then echo "== $f =="; tail -n 60 "$f"; else echo "no log files found under /app/logs"; fi' \
    2>&1 || echo "    (could not read file log from the api_logs volume)"
  echo "------------------------------------------------------------------"
}

# Mechanical precheck for one specific, already-seen failure mode (2026-09-23 incident, see
# DEPLOY.md §10.2c): a code change that adds/renames/splits legal document TYPES (not just their
# text) will not even boot on a machine whose `legal/legal.json` manifest still lists the old type
# set — LegalDocumentProvider throws before the port opens, so this fails BEFORE migrations, and the
# readiness loop below would otherwise burn the full 120s timeout just to say "no". Comparing the
# type/key sets here costs under a second and, when it fails, names exactly what's missing instead of
# leaving that to guesswork from an on-call phone. Deliberately checked before anything is touched
# (before the release symlink switch, before tagging/rebuilding) so a failure here leaves the host
# completely untouched — nothing to roll back.
check_legal_manifest() {
  local expected="ServiceBooking.API/App_Data/legal/legal.json"   # baked into this release's checkout
  local live="legal/legal.json"                                    # bind-mounted from the host, gitignored

  [ -f "$expected" ] || return 0   # nothing to compare this release against — shouldn't happen, don't block on it
  if [ ! -f "$live" ]; then
    echo "ERROR: $live does not exist on this host (see DEPLOY.md §2.2 — first-time setup)." >&2
    return 1
  fi

  local py
  py="$(command -v python3 || true)"
  if [ -z "$py" ]; then
    echo "WARNING: python3 not found on this host — skipping the automatic legal-manifest precheck." >&2
    echo "         Verify manually per DEPLOY.md §10.2c before trusting this deploy." >&2
    return 0
  fi

  "$py" - "$expected" "$live" <<'PYEOF'
import json
import sys

expected_path, live_path = sys.argv[1], sys.argv[2]


def load(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    types = sorted(d["type"] for d in data.get("documents", []))
    ui_keys = sorted(t["key"] for t in data.get("uiTexts", []))
    return set(types), set(ui_keys)


expected_types, expected_ui = load(expected_path)
live_types, live_ui = load(live_path)

missing_types = sorted(expected_types - live_types)
missing_ui = sorted(expected_ui - live_ui)
extra_types = sorted(live_types - expected_types)

ok = True
if missing_types:
    print(f"missing document type(s) in {live_path}: {', '.join(missing_types)}")
    ok = False
if missing_ui:
    print(f"missing uiTexts key(s) in {live_path}: {', '.join(missing_ui)}")
    ok = False
if extra_types:
    print(f"note: {live_path} also lists type(s) this release does not require: {', '.join(extra_types)} (harmless)")

sys.exit(0 if ok else 1)
PYEOF
}

echo "==> Precheck: legal document manifest (host) matches what this release's code expects"
if ! check_legal_manifest; then
  echo "ERROR: the legal manifest on this host is missing type(s)/key(s) this release requires." >&2
  echo "       This is the 2026-09-23 failure class — see DEPLOY.md §10.2c: update /opt/ezbook/app/legal/legal.json" >&2
  echo "       (add the new document/uiText entries) BEFORE deploying this code, then retry." >&2
  echo "Nothing has been changed on this host yet — no rollback needed." >&2
  exit 1
fi
echo "    OK"

# Cycle 19 (ARCHITECTURE_CYCLE19.md §385.3, DO-2): cycle 19 retires the "extra employees / extra
# companies" limit options. A still-live purchase of one would silently lose its extra limit on this
# release, so the deploy stops until the customer decides (DEPLOY.md "Выкат цикла 19"). READ-ONLY:
# writes nothing. Fail-closed: any psql error refuses the deploy. Runs before anything on the host is
# touched, so a refusal needs no rollback.
check_retired_limit_options() {
  local dc=(docker compose -f docker-compose.prod.yml --env-file .env)
  local report="deploy/checks/cycle19-limit-options-report.sql"
  local live_sql="deploy/checks/cycle19-retired-limit-options-live.sql"

  if [ -z "$("${dc[@]}" ps --status running -q postgres 2>/dev/null)" ]; then
    echo "    SKIPPED: postgres service is not running (first deploy)"
    return 0
  fi

  echo "    --- report for the customer (US-19-05) ---"
  if ! "${dc[@]}" exec -T postgres psql -U postgres -d servicebooking -v ON_ERROR_STOP=1 < "$report"; then
    echo "ERROR: could not run $report — refusing to deploy (fail-closed)." >&2
    echo "Nothing has been changed on this host yet — no rollback needed." >&2
    return 1
  fi
  echo "    --- end of report ---"

  local rows
  if ! rows="$("${dc[@]}" exec -T postgres psql -U postgres -d servicebooking -v ON_ERROR_STOP=1 -At -F ' | ' < "$live_sql")"; then
    echo "ERROR: could not run $live_sql — refusing to deploy (fail-closed)." >&2
    echo "Nothing has been changed on this host yet — no rollback needed." >&2
    return 1
  fi

  if [ -n "$rows" ]; then
    echo "$rows"
    echo "ERROR: найдены незавершённые покупки опций «Дополнительные сотрудники/компании» (см. строки выше). Цикл 19 выводит эти опции из оборота; выкат остановлен до решения заказчика — DEPLOY.md §20 «Выкат цикла 19»." >&2
    echo "Nothing has been changed on this host yet — no rollback needed." >&2
    exit 1
  fi
  return 0
}

echo "==> Precheck: cycle 19 retired limit options (no live purchases of extra employees/companies)"
check_retired_limit_options
echo "    OK"

echo "==> Recording rollback point"
if [ -L "$CURRENT_LINK" ]; then
  readlink -f "$CURRENT_LINK" > "$PREVIOUS_MARKER"
  echo "    previous release: $(cat "$PREVIOUS_MARKER")"
else
  echo "    no existing release (first deploy) — nothing to roll back to yet"
  rm -f "$PREVIOUS_MARKER"
fi
# Tag the currently-running API image `previous` BEFORE rebuilding — this is what makes rollback a
# single `docker tag previous ...` instead of "hope you kept the old image around". docker-compose.prod.yml
# pins `image: servicebooking-api:latest`, so this tag is always where the CURRENTLY RUNNING image is.
if docker image inspect servicebooking-api:latest >/dev/null 2>&1; then
  docker tag servicebooking-api:latest servicebooking-api:previous
  echo "    previous API image tagged"
else
  echo "    no existing API image (first deploy) — nothing to tag"
fi

echo "==> Frontend: switching release symlink"
ln -sfn "$NEW_RELEASE_DIR" "$WEB_ROOT/current.tmp"
mv -Tf "$WEB_ROOT/current.tmp" "$CURRENT_LINK"   # atomic rename(2) — no half-switched state (US-44 п.4)
command -v restorecon >/dev/null 2>&1 && restorecon -R "$WEB_ROOT" >/dev/null || true
# `sudo` here is intentional and load-bearing, not a leftover: this script runs as the unprivileged
# `ezbookdeploy` deploy user (see DEPLOY.md §1.4/§9), which owns /var/www/ezbook and is in the `docker`
# group but does NOT have a shell login or broad sudo — only these two exact commands, granted via
# /etc/sudoers.d/ezbook-deploy (NOPASSWD, no wildcard). If you see "sudo: a password is required" here,
# the sudoers file on this box doesn't match what DEPLOY.md §1.4 documents — fix the sudoers file, don't
# drop the `sudo` from this script.
if ! sudo /usr/sbin/nginx -t; then
  echo "ERROR: nginx config test failed for the new release — see nginx's own output above." >&2
  rollback_hint
  exit 1
fi
if ! sudo /usr/bin/systemctl reload nginx; then
  echo "ERROR: nginx reload failed after a passing config test — see systemctl output above." >&2
  rollback_hint
  exit 1
fi
echo "    now serving: $RELEASE_TS"

echo "==> Backend: rebuild and restart containers"
if ! GIT_SHA="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)" \
    docker compose -f docker-compose.prod.yml --env-file .env up -d --build; then
  echo "ERROR: 'docker compose up --build' failed — see compose's own output above for the build/start error." >&2
  dump_failure_diagnostics
  rollback_hint
  exit 1
fi

echo "==> Waiting for readiness (timeout ${READY_TIMEOUT_SECONDS}s)"
deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
until code=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5000/api/health/ready 2>/dev/null) && [ "$code" = "200" ]; do
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "readiness did not come back within ${READY_TIMEOUT_SECONDS}s (last code: ${code:-none})" >&2
    dump_failure_diagnostics
    rollback_hint
    exit 1
  fi
  sleep 2
done
echo "    ready OK"

if [ "$GOODS_SMOKE" != "0" ]; then
  # Through the LOCAL nginx (--resolve), so this checks the vhost + certificate + current/__goods on this
  # machine, not DNS. Both requests must pass: the SPA shell and the API proxied by the goods vhost.
  echo "==> goods smoke: https://$GOODS_HOST/ and /api/health/ready via local nginx"
  [ -f "$CURRENT_LINK/__goods/index.html" ] || {
    echo "ERROR: $CURRENT_LINK/__goods/index.html is missing — the release was built without goods (npm run build instead of build:release?)" >&2
    rollback_hint
    exit 1
  }
  goods_index=$(curl -sf --max-time 10 --resolve "$GOODS_HOST:443:127.0.0.1" "https://$GOODS_HOST/") || {
    echo "ERROR: https://$GOODS_HOST/ did not answer 200 via local nginx — is the goods vhost installed and does it have a certificate? (DEPLOY.md §21)" >&2
    rollback_hint
    exit 1
  }
  grep -q '<div id="root">' <<<"$goods_index" || {
    echo "ERROR: https://$GOODS_HOST/ answered, but not with the goods SPA shell (<div id=\"root\"> missing)" >&2
    rollback_hint
    exit 1
  }
  goods_ready=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 --resolve "$GOODS_HOST:443:127.0.0.1" "https://$GOODS_HOST/api/health/ready")
  [ "$goods_ready" = "200" ] || {
    echo "ERROR: https://$GOODS_HOST/api/health/ready returned $goods_ready via local nginx (expected 200)" >&2
    rollback_hint
    exit 1
  }
  # ARCHITECTURE_CYCLE24.md §463.3 (DO-3) — goods is a Home Screen app: service worker and manifest must be real files.
  # The SPA fallback (`try_files ... /index.html`) answers 200 for ANY path, so a missing sw.js is caught by
  # checking the body is not the HTML shell, not only the status code.
  goods_curl=(curl -s --max-time 10 --resolve "$GOODS_HOST:443:127.0.0.1")
  goods_sw_headers=$("${goods_curl[@]}" -D - -o /tmp/goods-sw.js.$$ -w '' "https://$GOODS_HOST/sw.js") || true
  goods_sw_code=$(head -n1 <<<"$goods_sw_headers" | awk '{print $2}')
  if [ "$goods_sw_code" != "200" ] || grep -qi '<div id="root">' /tmp/goods-sw.js.$$ 2>/dev/null; then
    rm -f /tmp/goods-sw.js.$$
    echo "ERROR: https://$GOODS_HOST/sw.js did not answer 200 with the service worker file (got: ${goods_sw_code:-none}) — was goods built with build:release (goods/public/sw.js)?" >&2
    rollback_hint
    exit 1
  fi
  rm -f /tmp/goods-sw.js.$$
  # The vhost of goods is maintained by hand on this machine (DEPLOY.md §22): agents cannot apply an nginx change
  # (needs sudo). So a wrong Cache-Control is only a WARNING — it does not fail the deploy, but a browser may keep
  # an old push handler until the vhost is updated.
  if ! grep -qi '^cache-control:.*no-cache' <<<"$goods_sw_headers"; then
    echo "WARNING: https://$GOODS_HOST/sw.js has no 'Cache-Control: no-cache' — the goods vhost on this machine is not updated to deploy/nginx/goods.ezbook.conf (location = /sw.js). Manual step: DEPLOY.md §22.2." >&2
  fi
  goods_manifest=$("${goods_curl[@]}" -f "https://$GOODS_HOST/manifest.webmanifest") || {
    echo "ERROR: https://$GOODS_HOST/manifest.webmanifest did not answer 200" >&2
    rollback_hint
    exit 1
  }
  grep -Eq '"display"[[:space:]]*:[[:space:]]*"standalone"' <<<"$goods_manifest" || {
    echo "ERROR: goods manifest.webmanifest is not a standalone app manifest (\"display\": \"standalone\" missing)" >&2
    rollback_hint
    exit 1
  }
  # ARCHITECTURE_CYCLE25.md §514 (DO-3) — the anonymous shop catalog on the goods home page: the API route must answer 200 with JSON that has
  # the "items" array, through the same local nginx. A red here means the catalog (the goods home page) is broken — roll back.
  goods_catalog_headers=$("${goods_curl[@]}" -D - -o /tmp/goods-catalog.json.$$ -w '' "https://$GOODS_HOST/api/goods/catalog") || true
  goods_catalog_code=$(head -n1 <<<"$goods_catalog_headers" | awk '{print $2}')
  if [ "$goods_catalog_code" != "200" ] \
    || ! grep -qi '^content-type:.*application/json' <<<"$goods_catalog_headers" \
    || ! grep -q '"items"' /tmp/goods-catalog.json.$$ 2>/dev/null; then
    rm -f /tmp/goods-catalog.json.$$
    echo "ERROR: https://$GOODS_HOST/api/goods/catalog did not answer 200 application/json with an \"items\" array (got: ${goods_catalog_code:-none}) — the goods home page would be empty" >&2
    rollback_hint
    exit 1
  fi
  rm -f /tmp/goods-catalog.json.$$
  echo "    goods OK"
else
  echo "==> goods smoke skipped (GOODS_SMOKE=0)"
fi

echo "==> Pruning old releases (keeping $KEEP_RELEASES most recent)"
# shellcheck disable=SC2012
ls -1t "$RELEASES_DIR" 2>/dev/null | tail -n +$((KEEP_RELEASES + 1)) | while read -r old; do
  rm -rf "${RELEASES_DIR:?}/$old"
  echo "    removed release $old"
done

# ARCHITECTURE_CYCLE28.md §581.1 (DO-4) — optional demo stand refresh (demo.visit.ezbook.ru, DEPLOY.md §25).
# OFF by default: it runs only when DEMO_ENABLED=true is set in the environment or in the host's .env (a person turns it on
# once the stand is installed by hand, §25). It never changes the verdict of a production deploy: any failure here is a
# WARNING in the log, not a rollback — the production API is already up and verified by this point. The API image is the one
# just built above (docker-compose.demo.yml has no `build:`), so the demo runs the same code; migrations apply on its start.
# Value of KEY from an env file (last occurrence, surrounding quotes/whitespace stripped); empty if absent.
env_file_value() {
  local v
  v="$(grep -E "^$2=" "$1" 2>/dev/null | tail -n1 | cut -d= -f2- || true)"
  v="${v//[\"\' $'\t\r']/}"
  printf '%s' "$v"
}

update_demo_stand() {
  local enabled="${DEMO_ENABLED:-}"
  if [ -z "$enabled" ] && [ -f .env ]; then
    enabled="$(env_file_value .env DEMO_ENABLED)"
  fi
  [ "$enabled" = "true" ] || return 0

  echo "==> Demo stand: updating (DEMO_ENABLED=true)"
  if [ ! -f .env.demo ]; then
    echo "WARNING: DEMO_ENABLED=true, but .env.demo does not exist — demo stand NOT updated (DEPLOY.md §25, step 'первый запуск')." >&2
    return 0
  fi
  local dc=(docker compose -f docker-compose.demo.yml --env-file .env.demo)
  if ! "${dc[@]}" up -d; then
    echo "WARNING: demo stand: 'docker compose up' failed — production deploy is NOT affected. Logs: docker compose -f docker-compose.demo.yml --env-file .env.demo logs api-demo" >&2
    return 0
  fi
  local demo_port
  demo_port="$(env_file_value .env.demo DEMO_API_PORT)"
  demo_port="${demo_port:-5001}"
  local deadline=$((SECONDS + READY_TIMEOUT_SECONDS)) code
  until code=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$demo_port/api/health/ready" 2>/dev/null) && [ "$code" = "200" ]; do
    if [ "$SECONDS" -ge "$deadline" ]; then
      echo "WARNING: demo stand did not become ready within ${READY_TIMEOUT_SECONDS}s (last code: ${code:-none}) — production deploy is NOT affected." >&2
      "${dc[@]}" logs --no-color --tail=40 api-demo 2>&1 || true
      return 0
    fi
    sleep 2
  done
  if bash deploy/ci/demo-smoke.sh "http://127.0.0.1:$demo_port"; then
    echo "    demo stand OK"
  else
    echo "WARNING: demo smoke failed — production deploy is NOT affected. If it says 409, run the first reset: DEPLOY.md §25." >&2
  fi
  # Цикл 35 (T-35-40): смоук демо «Заказов» — только если в .env задан DEMO_ZAKAZ_ENABLED=true (vhost demo.zakaz поставлен, DEPLOY.md §27).
  local zakaz_enabled="${DEMO_ZAKAZ_ENABLED:-}"
  if [ -z "$zakaz_enabled" ] && [ -f .env ]; then
    zakaz_enabled="$(env_file_value .env DEMO_ZAKAZ_ENABLED)"
  fi
  if [ "$zakaz_enabled" = "true" ]; then
    if bash deploy/ci/demo-zakaz-smoke.sh "http://127.0.0.1:$demo_port"; then
      echo "    demo zakaz OK"
    else
      echo "WARNING: demo zakaz smoke failed — production deploy is NOT affected. If it says 409, run the reset: DEPLOY.md §27." >&2
    fi
  fi
  return 0
}
update_demo_stand || echo "WARNING: demo stand step failed unexpectedly — production deploy is NOT affected." >&2

echo "==> Deploy OK. Container status:"
docker compose -f docker-compose.prod.yml ps
