#!/usr/bin/env bash
# CY48-D3 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.15, §48.11, DO-48-2): настоящие ezbook.conf и goods.ezbook.conf над собранным dist.
# Проверяет, что пререндеренные /contacts и /payment-info отдаются теми же правилами, что и остальной сайт:
# 200 (или ровно один 301 на свой домен на путь со слэшем — тогда это печатается, и это нужно отметить в DEPLOY.md),
# Cache-Control: no-cache, CSP, HSTS, nosniff. Правка vhost в цикле 48 не требуется — тест это доказывает.
# Нужен nginx в PATH. Использование: deploy/nginx/test-storefront-pages.sh
#   DIST_EZBOOK=frontend/dist  DIST_GOODS=frontend/dist/__goods  (по умолчанию так)
set -euo pipefail
cd "$(dirname "$0")"
ROOT=$(cd ../.. && pwd)
DIST_EZBOOK="${DIST_EZBOOK:-$ROOT/frontend/dist}"
DIST_GOODS="${DIST_GOODS:-$ROOT/frontend/dist/__goods}"
PORT="${PORT:-18092}"
W=$(mktemp -d)
trap 'nginx -c "$W/n.conf" -p "$W" -s stop 2>/dev/null || true; rm -rf "$W"' EXIT
fail() { echo "FAIL: $*"; exit 1; }
command -v nginx >/dev/null || fail "nginx не найден в PATH"
MIME="$(dirname "$(command -v nginx)")/../etc/nginx/mime.types"

# $1 — конф, $2 — root из конфа, $3 — каталог dist, $4 — Host
check_site() {
  local conf="$1" confroot="$2" dist="$3" host="$4"
  [ -f "$dist/index.html" ] || fail "$host: нет $dist/index.html (npm run build:release)"
  [ -f "$dist/contacts/index.html" ] || fail "$host: нет $dist/contacts/index.html (пререндер витрины, FE-48-4)"
  [ -f "$dist/payment-info/index.html" ] || fail "$host: нет $dist/payment-info/index.html (пререндер витрины, FE-48-4)"
  dist=$(cd "$dist" && pwd)
  grep -q "root $confroot;" "$conf" || fail "$conf: не нашёл 'root $confroot;' — тест надо поправить вместе с vhost"
  { echo "pid $W/n.pid; error_log $W/e.log; events{} http{"
    [ -f "$MIME" ] && echo "include $MIME;"
    # Без access_log на уровне http nginx -t открывает встроенный /var/log/nginx/access.log — в CI без sudo это 13: Permission denied.
    echo "access_log $W/a.log;"
    echo "client_body_temp_path $W/b; proxy_temp_path $W/p; fastcgi_temp_path $W/f; uwsgi_temp_path $W/u; scgi_temp_path $W/s;"
    sed -e "s#listen 80;#listen $PORT;#" -e '/listen \[::\]:80;/d' \
        -e "s#root $confroot;#root $dist;#" \
        -e "s#/var/log/nginx/[A-Za-z0-9_.-]*#$W/a.log#" "$conf"
    echo "}"; } > "$W/n.conf"
  nginx -t -c "$W/n.conf" -p "$W" 2>"$W/t.log" || { echo "FAIL: nginx -t ($conf)"; cat "$W/t.log"; exit 1; }
  nginx -c "$W/n.conf" -p "$W"; sleep 1
  local B="http://127.0.0.1:$PORT" p code h loc marker
  for p in /contacts /payment-info; do
    case "$p" in /contacts) marker='Контакты и реквизиты' ;; *) marker='' ;; esac
    code=$(curl -s -o /dev/null -w '%{http_code}' -H "Host: $host" "$B$p")
    if [ "$code" = 301 ]; then
      loc=$(curl -sI -H "Host: $host" "$B$p" | tr -d '\r' | sed -n 's/^[Ll]ocation: //p')
      case "$loc" in
        "http://$host$p/"|"https://$host$p/"|"http://$host:$PORT$p/"|"$p/") echo "NOTE: $host$p -> 301 $loc (один редирект на свой домен; отметить в DEPLOY.md)" ;;
        *) fail "$host$p: 301 на чужой адрес $loc" ;;
      esac
      p="$p/"
      code=$(curl -s -o /dev/null -w '%{http_code}' -H "Host: $host" "$B$p")
    fi
    [ "$code" = 200 ] || fail "$host$p != 200 (got $code)"
    h=$(curl -sI -H "Host: $host" "$B$p")
    grep -qi '^cache-control:.*no-cache' <<<"$h" || fail "$host$p: нет Cache-Control: no-cache"
    grep -qi '^content-security-policy:' <<<"$h" || fail "$host$p: нет CSP"
    grep -qi '^strict-transport-security:' <<<"$h" || fail "$host$p: нет HSTS"
    grep -qi '^x-content-type-options:' <<<"$h" || fail "$host$p: нет nosniff"
    body=$(curl -s -H "Host: $host" "$B$p")
    grep -q '<div id="root">' <<<"$body" || fail "$host$p: нет <div id=\"root\">"
    [ -z "$marker" ] || grep -q "$marker" <<<"$body" || fail "$host$p: в теле нет «$marker»"
  done
  # Вариант со слэшем должен отдаваться сразу.
  for p in /contacts/ /payment-info/; do
    [ "$(curl -s -o /dev/null -w '%{http_code}' -H "Host: $host" "$B$p")" = 200 ] || fail "$host$p != 200"
  done
  nginx -c "$W/n.conf" -p "$W" -s stop; sleep 1
  echo "OK: $conf ($host)"
}

check_site ezbook.conf /var/www/ezbook/current "$DIST_EZBOOK" ezbook.ru
check_site goods.ezbook.conf /var/www/ezbook/current/__goods "$DIST_GOODS" goods.ezbook.ru
echo "OK: страницы витрины через настоящие vhost'ы"
