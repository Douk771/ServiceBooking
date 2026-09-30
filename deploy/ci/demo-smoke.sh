#!/usr/bin/env bash
# Смоук демо-стенда (US-28-12, ARCHITECTURE_CYCLE28.md §581.1): проверяет живой api-demo по HTTP.
#
# Запуск:
#   на машине, прямо в API:   BASE_URL=http://127.0.0.1:5001 deploy/ci/demo-smoke.sh
#   через nginx + TLS:        deploy/ci/demo-smoke.sh https://demo.visit.ezbook.ru
# (первый аргумент = BASE_URL; по умолчанию http://127.0.0.1:5001)
#
# Что проверяет:
#   1. /api/health/live и /api/health/ready = 200 (опрос, не sleep);
#   2. GET /api/demo/status = 200, JSON с demoMode=true, resetting=false и ролями owner/master/client;
#      заголовок X-Robots-Tag: noindex (экземпляр и есть демо);
#   3. POST /api/demo/login под тремя ролями — 200 и непустой токен, токен принимается (GET /api/profile = 200);
#      409 значит «демо-данные не созданы» — нужен `ops demo reset --yes` (DEPLOY.md §25);
#   4. только для https://-адреса (проверка через nginx): главная отдаёт SPA (<div id="root">) и заголовок X-Robots-Tag,
#      /robots.txt запрещает индексацию. На http://127.0.0.1:5001 SPA не раздаётся (её отдаёт nginx) — шаг пропускается.
#
# Боевой хост проверять на 404 здесь не нужно: это делают тесты API. Скрипт ничего не пишет, кроме трёх входов (токены
# не сохраняются и не печатаются). Любой сбой — exit 1. Вынесен в файл, а не инлайн в ci.yml (урок про парсинг YAML).
set -euo pipefail

BASE_URL="${1:-${BASE_URL:-http://127.0.0.1:5001}}"
BASE_URL="${BASE_URL%/}"
LIVE_TIMEOUT_SECONDS="${LIVE_TIMEOUT_SECONDS:-90}"
READY_TIMEOUT_SECONDS="${READY_TIMEOUT_SECONDS:-90}"
# Сброс идёт ≤ 3 мин: на время сброса API отвечает 503, статус отдаёт resetting=true. Ждём его окончания.
RESET_WAIT_SECONDS="${RESET_WAIT_SECONDS:-240}"

log() { echo "[demo-smoke] $*"; }
fail() { echo "[demo-smoke] FAILED: $*" >&2; exit 1; }

command -v curl >/dev/null 2>&1 || fail "curl is required"
command -v python3 >/dev/null 2>&1 || fail "python3 is required (JSON parsing)"

BODY_FILE="$(mktemp)"
trap 'rm -f "$BODY_FILE"' EXIT

# http_code печатает код ответа, тело кладёт в $BODY_FILE; http_headers печатает только заголовки.
http_code() { curl -s --max-time 15 -o "$BODY_FILE" -w '%{http_code}' "$@" 2>/dev/null || echo 000; }
http_headers() { curl -s --max-time 15 -D - -o /dev/null "$@" 2>/dev/null || true; }
# json_get '<python-expr над d>' — значение из $BODY_FILE
json_get() { python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); print(eval(sys.argv[2]))' "$BODY_FILE" "$1"; }

wait_for_200() { # $1 = path, $2 = timeout, $3 = label
  local deadline=$((SECONDS + $2)) code
  until code=$(http_code "$BASE_URL$1") && [ "$code" = "200" ]; do
    [ "$SECONDS" -lt "$deadline" ] || fail "timed out waiting for $1 (last code: ${code:-none})"
    sleep 1
  done
  log "$3 OK"
}

wait_for_200 /api/health/live "$LIVE_TIMEOUT_SECONDS" live
wait_for_200 /api/health/ready "$READY_TIMEOUT_SECONDS" ready

# --- 2. статус демо. Во время сброса ждём его конца (resetting=true не ошибка, но входить нельзя) ---
deadline=$((SECONDS + RESET_WAIT_SECONDS))
while :; do
  code=$(http_code "$BASE_URL/api/demo/status")
  [ "$code" = "200" ] || fail "GET /api/demo/status returned $code (expected 200). 404 = DemoMode__Enabled не включён; 503 = идёт сброс/нет ответа API"
  resetting=$(json_get 'str(d["resetting"]).lower()' 2>/dev/null) || fail "/api/demo/status did not return JSON with a 'resetting' field"
  [ "$resetting" = "true" ] || break
  [ "$SECONDS" -lt "$deadline" ] || fail "the demo reset did not finish within ${RESET_WAIT_SECONDS}s"
  log "reset in progress, waiting"
  sleep 5
done
[ "$(json_get 'str(d["demoMode"]).lower()')" = "true" ] || fail "/api/demo/status: demoMode is not true"
roles=$(json_get '",".join(sorted(r["role"] for r in d["roles"]))')
[ "$roles" = "client,master,owner" ] || fail "/api/demo/status: roles are '$roles', expected client,master,owner"
log "status OK (roles: $roles, last reset: $(json_get 'd.get("lastResetAtUtc")'))"

status_headers=$(http_headers "$BASE_URL/api/demo/status")
grep -qi '^x-robots-tag:.*noindex' <<<"$status_headers" || fail "/api/demo/status has no 'X-Robots-Tag: noindex' header"
log "X-Robots-Tag OK"

# --- 3. вход под тремя ролями ---
for role in owner master client; do
  code=$(http_code -X POST -H 'Content-Type: application/json' -d "{\"role\":\"$role\"}" "$BASE_URL/api/demo/login")
  case "$code" in
    200) ;;
    409) fail "POST /api/demo/login ($role) = 409: демо-данные ещё не созданы. Выполните 'ops demo reset --yes' (DEPLOY.md §25)" ;;
    *)   fail "POST /api/demo/login ($role) returned $code (expected 200)" ;;
  esac
  token=$(json_get 'd["token"]' 2>/dev/null) || fail "login ($role): no 'token' in the response"
  [ -n "$token" ] || fail "login ($role): empty token"
  code=$(http_code -H "Authorization: Bearer $token" "$BASE_URL/api/profile")
  [ "$code" = "200" ] || fail "login ($role): the issued token is not accepted (GET /api/profile = $code)"
  log "login as $role OK"
done

# --- 4. через nginx (только https) ---
case "$BASE_URL" in
  https://*)
    code=$(http_code "$BASE_URL/")
    [ "$code" = "200" ] || fail "GET / returned $code (expected 200)"
    grep -q '<div id="root">' "$BODY_FILE" || fail "GET / did not return the SPA shell (<div id=\"root\"> missing)"
    grep -qi '^x-robots-tag:.*noindex' <<<"$(http_headers "$BASE_URL/")" || fail "GET / has no 'X-Robots-Tag: noindex' header (vhost of the demo is not installed?)"
    code=$(http_code "$BASE_URL/robots.txt")
    [ "$code" = "200" ] && grep -qi '^disallow: */' "$BODY_FILE" || fail "/robots.txt does not disallow indexing (code $code)"
    log "nginx: SPA, X-Robots-Tag, robots.txt OK"
    ;;
  *) log "nginx checks skipped (BASE_URL is not https://)" ;;
esac

log "ALL OK"
