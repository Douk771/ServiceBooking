#!/usr/bin/env bash
# Проверка vhost about.ezbook.conf на собранной странице (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.9.2, DO-44-5).
# Поднимает nginx с настоящим vhost'ом (порт и root подменены, HTTP без TLS) и проверяет curl'ом заголовки и коды.
# Нужен nginx в PATH. Использование: DIST_DIR=frontend/dist/__about deploy/nginx/test-about-vhost.sh
set -euo pipefail
cd "$(dirname "$0")"
DIST_DIR="${DIST_DIR:-../../frontend/dist/__about}"
[ -f "$DIST_DIR/index.html" ] || DIST_DIR="../../frontend/dist-about"
[ -f "$DIST_DIR/index.html" ] || { echo "FAIL: нет собранной страницы (npm run build:about)"; exit 1; }
DIST_DIR=$(cd "$DIST_DIR" && pwd)
PORT="${PORT:-18091}"
W=$(mktemp -d)
trap 'nginx -c "$W/n.conf" -p "$W" -s stop 2>/dev/null || true; rm -rf "$W"' EXIT
{ echo "pid $W/n.pid; error_log $W/e.log; events{} http{"
  echo "include $(dirname "$(command -v nginx)")/../etc/nginx/mime.types;"
  echo "client_body_temp_path $W/b; proxy_temp_path $W/p; fastcgi_temp_path $W/f; uwsgi_temp_path $W/u; scgi_temp_path $W/s;"
  sed -e "s#listen 80;#listen $PORT;#" -e '/listen \[::\]:80;/d' \
      -e "s#root /var/www/ezbook/current/__about;#root $DIST_DIR;#" \
      -e "s#access_log /var/log/nginx/about.access.log;#access_log $W/a.log;#" about.ezbook.conf
  echo "}"; } > "$W/n.conf"
[ -f "$(dirname "$(command -v nginx)")/../etc/nginx/mime.types" ] || sed -i.bak '/mime.types/d' "$W/n.conf"
nginx -t -c "$W/n.conf" -p "$W" 2>"$W/t.log" || { echo "FAIL: nginx -t"; cat "$W/t.log"; exit 1; }
nginx -c "$W/n.conf" -p "$W"; sleep 1
B="http://127.0.0.1:$PORT"
fail() { echo "FAIL: $*"; exit 1; }
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

[ "$(code "$B/")" = 200 ] || fail "/ != 200"
h=$(curl -sI "$B/")
grep -qi '^content-security-policy:' <<<"$h" || fail "нет CSP"
grep -i '^content-security-policy:' <<<"$h" | grep -q 'script-src' && fail "в CSP есть script-src"
grep -qi "default-src 'none'" <<<"$h" || fail "CSP без default-src 'none'"
grep -qi '^x-robots-tag:' <<<"$h" && fail "есть X-Robots-Tag"
for n in x-frame-options strict-transport-security x-content-type-options referrer-policy; do grep -qi "^$n:" <<<"$h" || fail "нет $n"; done
[ "$(code "$B/index.html")" = 301 ] || fail "/index.html != 301"
[ "$(code "$B/nope")" = 404 ] || fail "/nope != 404"
curl -s "$B/nope" | grep -q 'href="/"' || fail "тело 404 не из 404.html"
[ "$(code "$B/api/health/ready")" = 404 ] || fail "/api/health/ready != 404"
[ "$(code "$B/uploads/x")" = 404 ] || fail "/uploads/ != 404"
css=$(grep -o '/assets/[^"]*\.css' "$DIST_DIR/index.html" | head -n1)
[ -n "$css" ] || fail "в index.html нет css"
curl -sI "$B$css" | grep -qi 'cache-control:.*immutable' || fail "css без immutable"
[ "$(code "$B/robots.txt")" = 200 ] || fail "robots.txt != 200"
[ "$(code "$B/sitemap.xml")" = 200 ] || fail "sitemap.xml != 200"
echo "OK: vhost about.ezbook.conf"
