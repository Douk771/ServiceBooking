#!/usr/bin/env bash
# Смоук демо «Заказов» (US-35-08, ARCHITECTURE_CYCLE35.md §35.14.3): проверяет живой api-demo по HTTP.
# Отдельный скрипт, demo-smoke.sh (демо «Записи») не трогает.
#
# Запуск:
#   на машине, прямо в API:   BASE_URL=http://127.0.0.1:5001 deploy/ci/demo-zakaz-smoke.sh
#   через nginx + TLS:        deploy/ci/demo-zakaz-smoke.sh https://demo.zakaz.ezbook.ru
# (первый аргумент = BASE_URL; по умолчанию http://127.0.0.1:5001)
# EXPECTED_ORDERS_URL — какой адрес «Заказов» ждём в ответах (по умолчанию https://demo.zakaz.ezbook.ru).
#
# Что проверяет:
#   1. /api/health/live и /api/health/ready = 200;
#   2. GET /api/demo/status?product=orders = 200, demoMode=true, resetting=false (ждём конца сброса),
#      роли shop-owner, shop-staff, shop-customer, siteUrls.orders = EXPECTED_ORDERS_URL; заголовок X-Robots-Tag: noindex;
#   3. POST /api/demo/login под тремя ролями — 200, токен принимается (GET /api/profile = 200);
#      409 = демо-данные не созданы: нужен `ops demo reset --yes` (DEPLOY.md §27);
#   4. под shop-owner: GET /api/shops/my — хотя бы один магазин, publicUrl начинается с EXPECTED_ORDERS_URL/;
#   5. GET /api/goods/catalog — непустой список (магазины витрины на месте);
#   6. ни в одном из ответов шагов 2-5 нет боевых доменов (goods.ezbook.ru, zakaz.ezbook.ru без demo., ezbook.ru без demo./goods.);
#   7. только для https://-адреса: главная отдаёт SPA и X-Robots-Tag noindex, /robots.txt запрещает индексацию.
# Скрипт ничего не пишет, кроме трёх входов (токены не сохраняются и не печатаются). Любой сбой — exit 1.
# Вынесен в файл, а не инлайн в ci.yml (урок про парсинг YAML).
set -euo pipefail

BASE_URL="${1:-${BASE_URL:-http://127.0.0.1:5001}}"
BASE_URL="${BASE_URL%/}"
EXPECTED_ORDERS_URL="${EXPECTED_ORDERS_URL:-https://demo.zakaz.ezbook.ru}"
EXPECTED_ORDERS_URL="${EXPECTED_ORDERS_URL%/}"
LIVE_TIMEOUT_SECONDS="${LIVE_TIMEOUT_SECONDS:-90}"
READY_TIMEOUT_SECONDS="${READY_TIMEOUT_SECONDS:-90}"
RESET_WAIT_SECONDS="${RESET_WAIT_SECONDS:-240}"

log() { echo "[demo-zakaz-smoke] $*"; }
fail() { echo "[demo-zakaz-smoke] FAILED: $*" >&2; exit 1; }

command -v curl >/dev/null 2>&1 || fail "curl is required"
command -v python3 >/dev/null 2>&1 || fail "python3 is required (JSON parsing)"

BODY_FILE="$(mktemp)"
ALL_BODIES="$(mktemp)"
trap 'rm -f "$BODY_FILE" "$ALL_BODIES"' EXIT

http_code() { curl -s --max-time 15 -o "$BODY_FILE" -w '%{http_code}' "$@" 2>/dev/null || echo 000; }
http_headers() { curl -s --max-time 15 -D - -o /dev/null "$@" 2>/dev/null || true; }
json_get() { python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); print(eval(sys.argv[2]))' "$BODY_FILE" "$1"; }
keep_body() { cat "$BODY_FILE" >> "$ALL_BODIES"; echo >> "$ALL_BODIES"; }

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

# --- 2. статус демо «Заказов». Во время сброса ждём его конца ---
deadline=$((SECONDS + RESET_WAIT_SECONDS))
while :; do
  code=$(http_code "$BASE_URL/api/demo/status?product=orders")
  [ "$code" = "200" ] || fail "GET /api/demo/status?product=orders returned $code (expected 200). 404 = DemoMode__Enabled не включён; 503 = идёт сброс/нет ответа API; 400 = образ API старше цикла 35"
  resetting=$(json_get 'str(d["resetting"]).lower()' 2>/dev/null) || fail "/api/demo/status did not return JSON with a 'resetting' field"
  [ "$resetting" = "true" ] || break
  [ "$SECONDS" -lt "$deadline" ] || fail "the demo reset did not finish within ${RESET_WAIT_SECONDS}s"
  log "reset in progress, waiting"
  sleep 5
done
keep_body
[ "$(json_get 'str(d["demoMode"]).lower()')" = "true" ] || fail "/api/demo/status: demoMode is not true"
roles=$(json_get '",".join(sorted(r["role"] for r in d["roles"]))')
[ "$roles" = "shop-customer,shop-owner,shop-staff" ] || fail "/api/demo/status?product=orders: roles are '$roles', expected shop-customer,shop-owner,shop-staff"
orders_url=$(json_get 'd["siteUrls"]["orders"]')
[ "$orders_url" = "$EXPECTED_ORDERS_URL" ] || fail "/api/demo/status: siteUrls.orders is '$orders_url', expected '$EXPECTED_ORDERS_URL' (PublicSites__OrdersBaseUrl / DEMO_ORDERS_BASE_URL)"
log "status OK (roles: $roles, orders: $orders_url)"

grep -qi '^x-robots-tag:.*noindex' <<<"$(http_headers "$BASE_URL/api/demo/status?product=orders")" || fail "/api/demo/status has no 'X-Robots-Tag: noindex' header"
log "X-Robots-Tag OK"

# --- 3. вход под тремя ролями ---
owner_token=""
for role in shop-owner shop-staff shop-customer; do
  code=$(http_code -X POST -H 'Content-Type: application/json' -d "{\"role\":\"$role\"}" "$BASE_URL/api/demo/login")
  case "$code" in
    200) ;;
    409) fail "POST /api/demo/login ($role) = 409: демо-данные ещё не созданы. Выполните 'ops demo reset --yes' (DEPLOY.md §27)" ;;
    *)   fail "POST /api/demo/login ($role) returned $code (expected 200)" ;;
  esac
  token=$(json_get 'd["token"]' 2>/dev/null) || fail "login ($role): no 'token' in the response"
  [ -n "$token" ] || fail "login ($role): empty token"
  code=$(http_code -H "Authorization: Bearer $token" "$BASE_URL/api/profile")
  [ "$code" = "200" ] || fail "login ($role): the issued token is not accepted (GET /api/profile = $code)"
  [ "$role" = "shop-owner" ] && owner_token="$token"
  log "login as $role OK"
done

# --- 4. магазины владельца и ссылки ---
code=$(http_code -H "Authorization: Bearer $owner_token" "$BASE_URL/api/shops/my")
[ "$code" = "200" ] || fail "GET /api/shops/my returned $code (expected 200)"
keep_body
shop_id=$(json_get '(d if isinstance(d, list) else d.get("items", []))[0]["id"]' 2>/dev/null) || fail "/api/shops/my: no shops for the demo owner"
code=$(http_code -H "Authorization: Bearer $owner_token" "$BASE_URL/api/shops/$shop_id")
[ "$code" = "200" ] || fail "GET /api/shops/$shop_id returned $code (expected 200)"
keep_body
public_url=$(json_get 'd["publicUrl"]')
case "$public_url" in
  "$EXPECTED_ORDERS_URL"/*) ;;
  *) fail "shop publicUrl is '$public_url', expected to start with '$EXPECTED_ORDERS_URL/'" ;;
esac
log "shops OK (publicUrl: $public_url)"

# --- 5. каталог goods ---
code=$(http_code "$BASE_URL/api/goods/catalog")
[ "$code" = "200" ] || fail "GET /api/goods/catalog returned $code (expected 200)"
keep_body
[ "$(json_get 'len(d["items"])')" -gt 0 ] || fail "/api/goods/catalog is empty (витрина не создана? 'ops demo reset --yes')"
log "catalog OK ($(json_get 'd.get("totalCount", len(d["items"]))') shops)"

# --- 6. нет боевых доменов в ответах ---
# Допустимы только demo.visit/demo.zakaz; ищем goods.ezbook.ru и любой ezbook.ru без префикса demo.
found=$(grep -Eo '[A-Za-z0-9.-]*ezbook\.ru' "$ALL_BODIES" | grep -Ev '^demo\.(visit|zakaz)\.ezbook\.ru$' | sort -u | tr '\n' ' ' || true)
[ -z "$found" ] || fail "production domains found in API responses: $found"
log "no production domains in responses OK"

# --- 7. через nginx (только https) ---
case "$BASE_URL" in
  https://*)
    code=$(http_code "$BASE_URL/")
    [ "$code" = "200" ] || fail "GET / returned $code (expected 200)"
    grep -q '<div id="root">' "$BODY_FILE" || fail "GET / did not return the SPA shell (<div id=\"root\"> missing)"
    grep -qi '^x-robots-tag:.*noindex' <<<"$(http_headers "$BASE_URL/")" || fail "GET / has no 'X-Robots-Tag: noindex' header (vhost demo.zakaz is not installed?)"
    code=$(http_code "$BASE_URL/robots.txt")
    [ "$code" = "200" ] && grep -qi '^disallow: */' "$BODY_FILE" || fail "/robots.txt does not disallow indexing (code $code)"
    log "nginx: SPA, X-Robots-Tag, robots.txt OK"
    ;;
  *) log "nginx checks skipped (BASE_URL is not https://)" ;;
esac

log "ALL OK"
