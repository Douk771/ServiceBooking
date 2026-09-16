#!/usr/bin/env bash
# Runs ON THE VPS. Rolls back to the previous release with ONE command and no arguments (T-D3, US-44,
# ARCHITECTURE.md §12.3, "выполнимо ли это ночью, с телефона, по скопированной команде" — SPEC Р8):
#
#   bash deploy/rollback.sh
#
# Optional single argument: a release timestamp (a directory name under /var/www/ezbook/releases/) to
# roll the FRONTEND back to something older than the immediately previous release (US-44 п.3). The API
# container always goes back to the `:previous` image tag either way — there is only one previous API
# image kept, not a history of them.
#
#   bash deploy/rollback.sh 20260913T120000Z
#
# IMPORTANT — what this does NOT do: undo a database migration. Migrations apply automatically on API
# startup and are not reversible by this script. If the code you're rolling back to predates a migration
# that already ran, see "Откат кода после применённой миграции" in DEPLOY.md before running this.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."   # repo root

WEB_ROOT="${WEB_ROOT:-/var/www/ezbook}"
RELEASES_DIR="$WEB_ROOT/releases"
CURRENT_LINK="$WEB_ROOT/current"
PREVIOUS_MARKER="$WEB_ROOT/.previous"
READY_TIMEOUT_SECONDS=120

TARGET_RELEASE="${1:-}"
if [ -z "$TARGET_RELEASE" ]; then
  [ -f "$PREVIOUS_MARKER" ] || { echo "ERROR: no $PREVIOUS_MARKER — nothing recorded to roll back to (has a deploy ever run?)" >&2; exit 1; }
  TARGET_RELEASE_DIR="$(cat "$PREVIOUS_MARKER")"
else
  TARGET_RELEASE_DIR="$RELEASES_DIR/$TARGET_RELEASE"
fi
[ -d "$TARGET_RELEASE_DIR" ] || { echo "ERROR: $TARGET_RELEASE_DIR does not exist" >&2; exit 1; }

echo "==> Frontend: switching release symlink back to $TARGET_RELEASE_DIR"
ln -sfn "$TARGET_RELEASE_DIR" "$WEB_ROOT/current.tmp"
mv -Tf "$WEB_ROOT/current.tmp" "$CURRENT_LINK"
command -v restorecon >/dev/null 2>&1 && restorecon -R "$WEB_ROOT" >/dev/null || true
# See the same comment in deploy-remote.sh: this runs as the unprivileged `ezbookdeploy` user, `sudo`
# is scoped to exactly these two commands via /etc/sudoers.d/ezbook-deploy (DEPLOY.md §1.4).
sudo /usr/sbin/nginx -t
sudo /usr/bin/systemctl reload nginx
echo "    now serving: $(basename "$TARGET_RELEASE_DIR")"

echo "==> Backend: reverting to previous image (no rebuild)"
if ! docker image inspect servicebooking-api:previous >/dev/null 2>&1; then
  echo "ERROR: no servicebooking-api:previous image found — was a deploy ever run on this box before?" >&2
  exit 1
fi
docker tag servicebooking-api:previous servicebooking-api:latest
docker compose -f docker-compose.prod.yml --env-file .env up -d --no-build

echo "==> Waiting for readiness (timeout ${READY_TIMEOUT_SECONDS}s)"
deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
until code=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5000/api/health/ready 2>/dev/null) && [ "$code" = "200" ]; do
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "readiness did not come back within ${READY_TIMEOUT_SECONDS}s (last code: ${code:-none}) — rollback itself is unhealthy, this needs a human now" >&2
    exit 1
  fi
  sleep 2
done

echo "==> Rollback OK. Container status:"
docker compose -f docker-compose.prod.yml ps
