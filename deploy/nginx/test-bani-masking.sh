#!/usr/bin/env bash
# Проверка маскирования токенов в access log bani.ezbook.conf (/b/, /s/, API baths|stays).
# Нужен nginx в PATH (иначе docker nginx:alpine). Использование: deploy/nginx/test-bani-masking.sh
set -euo pipefail
cd "$(dirname "$0")"
W=$(mktemp -d); trap 'rm -rf "$W"' EXIT
{ echo "pid $W/n.pid; error_log $W/e.log; events{} http{"
  sed -n '/^map \$request_uri/,/^}/p;/^map \$http_referer/,/^}/p' bani.ezbook.conf
  sed -n '/^log_format/,/;$/p' bani.ezbook.conf
  echo "server{listen 18090; access_log $W/a.log bani_masked; location / {return 200 ok;}} }"; } > "$W/n.conf"
nginx -t -c "$W/n.conf" -p "$W" 2>/dev/null || { echo "nginx -t failed"; exit 1; }
nginx -c "$W/n.conf" -p "$W"; sleep 1
for u in /b/SECRETB /s/SECRETS /api/baths/service-orders/public/SECRETX/cancel /api/stays/bookings/public/SECRETA /api/stays/service-orders/public/SECRETO; do
  curl -s -H 'Referer: https://bani.ezbook.ru/s/SECRETREF' "localhost:18090$u" >/dev/null; done
nginx -c "$W/n.conf" -p "$W" -s stop
if grep -q SECRET "$W/a.log"; then echo "FAIL: токен в логе"; cat "$W/a.log"; exit 1; fi
echo "OK: токены замаскированы"
