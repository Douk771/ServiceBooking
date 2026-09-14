#!/usr/bin/env bash
# Runs on YOUR machine (or from Rider's Run Configuration — see DEPLOY.md §9).
# Pulls the frontend `dist` that CI already built for the commit being deployed (T-D3, US-44,
# ARCHITECTURE.md §12.3 — the build no longer happens on the production server at all), ships it to the
# VPS as a new timestamped release, then SSHes in to run deploy-remote.sh (which switches the release
# symlink, rebuilds the API container, and waits for readiness).
#
# Requires the GitHub CLI (`gh`), authenticated once with `gh auth login` — needed only on the machine
# that runs this script, not on the VPS.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.deploy.env"

if [ -f "$ENV_FILE" ]; then
  # shellcheck disable=SC1090
  source "$ENV_FILE"
fi

if [ -z "${SSH_HOST:-}" ]; then
  echo "SSH_HOST is not set. Copy .deploy.env.example to .deploy.env (repo root) and fill it in." >&2
  exit 1
fi
command -v gh >/dev/null 2>&1 || { echo "gh (GitHub CLI) is required — https://cli.github.com, then \`gh auth login\`." >&2; exit 1; }

REMOTE_DIR="${REMOTE_DIR:-/opt/ezbook/app}"
SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"
SHORT_SHA="$(git -C "$REPO_ROOT" rev-parse --short HEAD)"
BRANCH="$(git -C "$REPO_ROOT" rev-parse --abbrev-ref HEAD)"
RELEASE_TS="$(date -u +%Y%m%dT%H%M%SZ)"

echo "==> Looking up the CI run for $SHORT_SHA on $BRANCH ..."
RUN_ID="$(gh run list --workflow=ci.yml --branch="$BRANCH" --json headSha,databaseId,status,conclusion \
  --jq "[.[] | select(.headSha == \"$SHA\" and .status == \"completed\" and .conclusion == \"success\")][0].databaseId" 2>/dev/null || true)"
if [ -z "$RUN_ID" ] || [ "$RUN_ID" = "null" ]; then
  echo "ERROR: no successful CI run found for commit $SHA on branch $BRANCH." >&2
  echo "Push the commit and wait for CI to go green before deploying — the frontend build lives there now." >&2
  exit 1
fi
echo "    found run $RUN_ID"

TMP_DIST="$(mktemp -d)"
trap 'rm -rf "$TMP_DIST"' EXIT
echo "==> Downloading frontend-dist-$SHA artifact"
gh run download "$RUN_ID" -n "frontend-dist-$SHA" -D "$TMP_DIST"
[ -f "$TMP_DIST/index.html" ] || { echo "ERROR: downloaded artifact has no index.html — something's wrong with the build." >&2; exit 1; }

echo "==> Shipping release $RELEASE_TS to $SSH_HOST"
ssh "$SSH_HOST" "mkdir -p /var/www/ezbook/releases/$RELEASE_TS"
scp -rq "$TMP_DIST"/. "$SSH_HOST:/var/www/ezbook/releases/$RELEASE_TS/"

echo "==> Deploying on $SSH_HOST ($REMOTE_DIR) ..."
ssh "$SSH_HOST" "cd $REMOTE_DIR && git fetch --quiet && git checkout --quiet $SHA && bash deploy/deploy-remote.sh $RELEASE_TS"
