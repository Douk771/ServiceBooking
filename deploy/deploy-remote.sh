#!/usr/bin/env bash
# Runs ON THE VPS. Rebuilds backend + frontend from the current checkout and reloads nginx.
# Invoked by deploy/deploy.sh over SSH (which does `git pull` right before this).
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."   # repo root (deploy/ is one level down)

echo "==> Backend: rebuild and restart containers"
docker compose -f docker-compose.prod.yml --env-file .env up -d --build

echo "==> Frontend: install, build, publish"
cd frontend
npm ci
npm run build
cp -r dist/* /var/www/ezbook/dist/
restorecon -Rv /var/www/ezbook >/dev/null
cd ..

echo "==> Reloading nginx"
nginx -t
systemctl reload nginx

echo "==> Done. Container status:"
docker compose -f docker-compose.prod.yml ps
