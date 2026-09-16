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
#   - if readiness never comes: say so in plain words and print the exact rollback command as the very
#     next line, so a tired operator can copy-paste it without thinking.
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
sudo /usr/sbin/nginx -t
sudo /usr/bin/systemctl reload nginx
echo "    now serving: $RELEASE_TS"

echo "==> Backend: rebuild and restart containers"
if ! GIT_SHA="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)" \
    docker compose -f docker-compose.prod.yml --env-file .env up -d --build; then
  rollback_hint
  exit 1
fi

echo "==> Waiting for readiness (timeout ${READY_TIMEOUT_SECONDS}s)"
deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
until code=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5000/api/health/ready 2>/dev/null) && [ "$code" = "200" ]; do
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "readiness did not come back within ${READY_TIMEOUT_SECONDS}s (last code: ${code:-none})" >&2
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
