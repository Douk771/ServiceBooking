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

echo "==> Pruning old releases (keeping $KEEP_RELEASES most recent)"
# shellcheck disable=SC2012
ls -1t "$RELEASES_DIR" 2>/dev/null | tail -n +$((KEEP_RELEASES + 1)) | while read -r old; do
  rm -rf "${RELEASES_DIR:?}/$old"
  echo "    removed release $old"
done

echo "==> Deploy OK. Container status:"
docker compose -f docker-compose.prod.yml ps
