#!/usr/bin/env bash
# Runs on YOUR machine (or from Rider's Run Configuration — see DEPLOY.md §9).
# SSHes into the VPS, pulls the latest code, and runs deploy-remote.sh there.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="$SCRIPT_DIR/../.deploy.env"

if [ -f "$ENV_FILE" ]; then
  # shellcheck disable=SC1090
  source "$ENV_FILE"
fi

if [ -z "${SSH_HOST:-}" ]; then
  echo "SSH_HOST is not set. Copy .deploy.env.example to .deploy.env (repo root) and fill it in." >&2
  exit 1
fi

REMOTE_DIR="${REMOTE_DIR:-/opt/ezbook/app}"

echo "Deploying to $SSH_HOST ($REMOTE_DIR) ..."
ssh "$SSH_HOST" "cd $REMOTE_DIR && git pull && bash deploy/deploy-remote.sh"
