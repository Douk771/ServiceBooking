#!/usr/bin/env bash
# Nightly backup for the ServiceBooking VPS (T-D2, US-40, ARCHITECTURE.md §12.1).
#
# Runs ON THE HOST (never inside a container, never as an IScheduledTask inside the API): it has to
# work when the application is down, and it snapshots docker VOLUMES from the outside, which would
# otherwise require mounting the docker socket into the API container — i.e. handing a web app root
# on the host. Invoked by systemd (servicebooking-backup.timer), not cron — see the .timer/.service
# files next to this script for why.
#
# Decision recorded here and in DEPLOY.md/README.md (must not be lost): this backup is LOCAL to the
# VPS. There is no offsite copy (customer decision, SPEC R13) — it protects against data corruption
# and operator mistakes, NOT against losing or being locked out of the VPS itself.
set -euo pipefail

# ---- configuration (edit here, not via environment — this runs from a systemd unit with a fixed env) ----
REPO_DIR="${SERVICEBOOKING_REPO_DIR:-/opt/ezbook/app}"
COMPOSE_FILE="$REPO_DIR/docker-compose.prod.yml"
ENV_FILE="$REPO_DIR/.env"
BACKUP_ROOT="/var/backups/servicebooking"     # deliberately OUTSIDE any docker volume (US-40 п.4):
                                               # `docker compose down -v` must not be able to take the
                                               # backups down with the data they're a copy of.
KEEP_DAILY=7
KEEP_WEEKLY=4        # Sunday runs are kept longer, tagged -weekly
MIN_FREE_MULTIPLIER_NUM=3   # required free space = last backup set size * (NUM/DEN), checked before
MIN_FREE_MULTIPLIER_DEN=2   # starting — integer bash arithmetic (3/2 = 1.5x), no python3 dependency here

# Compose project name is derived from the containing directory by default ("app" here, since the repo
# is cloned into .../app per DEPLOY.md §2) — override if you renamed the checkout directory.
PROJECT_NAME="${SERVICEBOOKING_COMPOSE_PROJECT:-app}"

ts="$(date -u +%Y%m%dT%H%M%SZ)"
is_sunday="$(date -u +%u)"   # 7 = Sunday
suffix=""
[ "$is_sunday" = "7" ] && suffix="-weekly"

log() { logger -t servicebooking-backup "$*"; echo "$*"; }
fail() {
  log "ERROR: $*"
  send_glitchtip_event "backup failed: $*"
  exit 1
}

# GlitchTip alert on failure (ARCHITECTURE.md §11.5) — reuses the same DSN the app itself sends errors
# to, so backup failures show up in the same one channel the operator already watches. No-op if DSN
# isn't configured (Sentry envelope format; 6 lines, deliberately not a dependency on any SDK).
send_glitchtip_event() {
  local message="$1"
  local dsn="${SENTRY_DSN:-}"
  [ -n "$dsn" ] || return 0
  # DSN shape: https://<key>@<host>/<project_id>
  local key host project_id
  key="$(echo "$dsn" | sed -E 's#https?://([^@]+)@.*#\1#')"
  host="$(echo "$dsn" | sed -E 's#https?://[^@]+@([^/]+)/.*#\1#')"
  project_id="$(echo "$dsn" | sed -E 's#.*/([0-9]+)$#\1#')"
  local event_id
  event_id="$(python3 -c 'import uuid; print(uuid.uuid4().hex)' 2>/dev/null || echo "00000000000000000000000000000000")"
  local body
  body=$(cat <<EOF
{"event_id":"$event_id","timestamp":"$(date -u +%Y-%m-%dT%H:%M:%SZ)","level":"error","logger":"servicebooking-backup","platform":"other","message":{"formatted":"$message"}}
EOF
)
  curl -s -m 10 -X POST "https://$host/api/$project_id/store/" \
    -H "Content-Type: application/json" \
    -H "X-Sentry-Auth: Sentry sentry_version=7, sentry_key=$key" \
    -d "$body" >/dev/null 2>&1 || true
}

[ -f "$ENV_FILE" ] && set -a && source "$ENV_FILE" && set +a || true

mkdir -p "$BACKUP_ROOT"
chmod 0700 "$BACKUP_ROOT"   # personal data lives in here (client-note photos) — SPEC §7 п.3

# ---- 1. free space check, BEFORE writing anything ----
# The LAST BACKUP SET, not the whole retained archive: `du -sk "$BACKUP_ROOT"` used to measure all 7
# daily + 4 weekly sets combined, so required space grew with the archive's age instead of tracking what
# a single new set actually needs — a false "not enough free space" alarm (and a GlitchTip page, since
# fail() reports to it) was only a matter of weeks away. Sum the newest file of each of the three kinds
# instead — that's what one backup run is actually about to write.
last_set_size_kb=0
for pattern in "db-*.dump" "uploads-*.tar.gz" "private-uploads-*.tar.gz" "env-*.txt"; do
  # shellcheck disable=SC2012
  # `|| true` is load-bearing under `set -euo pipefail`: with no matching file the glob stays literal,
  # ls exits non-zero and pipefail propagates that as the pipeline's status — which, in a bare
  # assignment, aborts the whole script. That is exactly the first-ever run (nothing to measure yet),
  # the case the `required_kb` fallback below is written for, and it would abort silently: before
  # fail(), so without a log line and without a GlitchTip alert.
  newest="$(ls -1t "$BACKUP_ROOT"/$pattern 2>/dev/null | head -n1 || true)"
  if [ -n "$newest" ]; then
    file_kb="$(du -sk "$newest" 2>/dev/null | awk '{print $1}')"
    last_set_size_kb=$((last_set_size_kb + ${file_kb:-0}))
  fi
done
required_kb=$((last_set_size_kb * MIN_FREE_MULTIPLIER_NUM / MIN_FREE_MULTIPLIER_DEN))
[ "$required_kb" -gt 0 ] || required_kb=102400   # first-ever run, nothing to measure yet: require 100MB
avail_kb="$(df -Pk "$BACKUP_ROOT" | awk 'NR==2 {print $4}')"
if [ "$avail_kb" -lt "$required_kb" ]; then
  fail "not enough free space: have ${avail_kb}KB, need ${required_kb}KB (1.5x last backup set, not the whole archive) — aborting before writing anything"
fi

# ---- 2. pg_dump, custom format (already compressed) ----
log "starting pg_dump"
if ! docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" exec -T postgres \
    pg_dump -U postgres -Fc servicebooking > "$BACKUP_ROOT/db-${ts}${suffix}.dump.tmp"; then
  rm -f "$BACKUP_ROOT/db-${ts}${suffix}.dump.tmp"
  fail "pg_dump failed"
fi
mv "$BACKUP_ROOT/db-${ts}${suffix}.dump.tmp" "$BACKUP_ROOT/db-${ts}${suffix}.dump"
log "pg_dump OK: db-${ts}${suffix}.dump ($(du -h "$BACKUP_ROOT/db-${ts}${suffix}.dump" | cut -f1))"

# ---- 3. both docker volumes, without stopping the app ----
# Read-only source mount + files written once and never rewritten by the upload pipeline (cycle B
# convention) means a "hot" tar is consistent; worst case is a file uploaded mid-snapshot missing this
# round — it'll be in tomorrow's.
for vol in "${PROJECT_NAME}_api_uploads:uploads" "${PROJECT_NAME}_api_private_uploads:private-uploads"; do
  volume_name="${vol%%:*}"
  label="${vol##*:}"
  out="$BACKUP_ROOT/${label}-${ts}${suffix}.tar.gz.tmp"
  log "starting volume backup: $volume_name"
  if ! docker run --rm -v "${volume_name}:/src:ro" -v "$BACKUP_ROOT:/dst" alpine \
      tar czf "/dst/$(basename "$out")" -C /src .; then
    rm -f "$out"
    fail "volume backup failed: $volume_name"
  fi
  mv "$out" "${out%.tmp}"
  log "volume backup OK: $(basename "${out%.tmp}") ($(du -h "${out%.tmp}" | cut -f1))"
done

# ---- 3b. .env ----
# Конфигурация не восстанавливается ниоткуда: файл в .gitignore и генерируется на машине. Без него
# дамп базы и тома бесполезны — нет ни ключа JWT, ни пароля Postgres, ни ключей капчи, ни секрета
# GlitchTip, а часть значений выдаётся внешними кабинетами и заново берётся только оттуда.
# Права 0600: внутри секреты, а каталог 0700 root-only — но лишний рубеж здесь дешевле разбирательств.
# ВАЖНО: это НЕ защита от потери машины — копия лежит на ней же. От потери машины спасает только
# менеджер паролей у оператора (см. DEPLOY.md, инвентарь секретов).
if [ -f "$ENV_FILE" ]; then
  if cp "$ENV_FILE" "$BACKUP_ROOT/env-${ts}${suffix}.txt.tmp"; then
    chmod 600 "$BACKUP_ROOT/env-${ts}${suffix}.txt.tmp"
    mv "$BACKUP_ROOT/env-${ts}${suffix}.txt.tmp" "$BACKUP_ROOT/env-${ts}${suffix}.txt"
    log "env backup OK: env-${ts}${suffix}.txt"
  else
    rm -f "$BACKUP_ROOT/env-${ts}${suffix}.txt.tmp"
    fail "env backup failed"
  fi
else
  log "WARNING: $ENV_FILE не найден — конфигурация в копию не попала"
fi

# ---- 4. rotation ----
# The trailing `|| true` on both pipelines is required, not defensive noise: under `set -euo pipefail`
# a pattern with no matches makes ls exit non-zero, pipefail turns that into the pipeline's status, and
# the script aborts here — AFTER the backup was already written but BEFORE the "finished OK" log, i.e.
# a unit that systemd reports as failed for a backup that in fact succeeded. The weekly patterns match
# nothing at all until the first Sunday run, so this fires on ordinary days, not just in theory.
log "rotating: keep $KEEP_DAILY daily, $KEEP_WEEKLY weekly"
for pattern in "db-*[0-9]Z.dump" "uploads-*[0-9]Z.tar.gz" "private-uploads-*[0-9]Z.tar.gz" "env-*[0-9]Z.txt"; do
  # shellcheck disable=SC2012
  ls -1t "$BACKUP_ROOT"/$pattern 2>/dev/null | tail -n +$((KEEP_DAILY + 1)) | xargs -r rm -f || true
done
for pattern in "db-*-weekly.dump" "uploads-*-weekly.tar.gz" "private-uploads-*-weekly.tar.gz" "env-*-weekly.txt"; do
  # shellcheck disable=SC2012
  ls -1t "$BACKUP_ROOT"/$pattern 2>/dev/null | tail -n +$((KEEP_WEEKLY + 1)) | xargs -r rm -f || true
done

# ---- 5. place under a future offsite copy (SPEC R13, not built this cycle — decided by the customer) ----
# Deliberately a no-op stub: when an offsite destination is chosen, this is the ONE function to fill in,
# not a rewrite of the script above it.
upload_offsite() { :; }
upload_offsite

log "backup run finished OK: $ts$suffix"
