#!/usr/bin/env bash
# Forced command for the GitHub Actions deploy SSH key (see DEPLOY.md §9 and
# .github/workflows/deploy-*.yml). Installed on the target machine, OUTSIDE the deploy user's own
# writable tree (root-owned, e.g. /usr/local/sbin/ezbook-deploy-wrapper.sh, mode 0755, owner root:root)
# and referenced from authorized_keys like this:
#
#   command="/usr/local/sbin/ezbook-deploy-wrapper.sh",no-agent-forwarding,no-port-forwarding,\
#   no-X11-forwarding,no-pty ssh-ed25519 AAAA... github-actions-deploy
#
# `command=` in authorized_keys means sshd runs THIS script for every SSH/SCP attempt made with that
# key, no matter what the client actually asked for — the client's request lands in
# $SSH_ORIGINAL_COMMAND as plain text, nothing more. That is the whole point: a leaked private key
# (the realistic risk of a cloud-hosted GitHub Actions runner holding it, see DEPLOY.md §9) gets an
# attacker exactly the handful of operations matched below, never an interactive shell, never arbitrary
# commands, and never anything involving the neighbour's `fleetservice.service` on :8443 (deliberately
# not one of the allowed cases).
#
# Runs as the unprivileged `ezbookdeploy` system user (DEPLOY.md §1.4) — NOT root, and its own sudo
# rights are separately restricted (via /etc/sudoers.d/ezbook-deploy) to exactly the two commands
# deploy-remote.sh/rollback.sh need. This wrapper is the outer gate; sudoers is the inner one.
set -euo pipefail

REPO_DIR=/opt/ezbook/app
RELEASES_DIR=/var/www/ezbook/releases
SHA_RE='^[0-9a-f]{7,40}$'
TS_RE='^[0-9]{8}T[0-9]{6}Z$'

refuse() {
  echo "refused: $1" >&2
  exit 1
}

cmd="${SSH_ORIGINAL_COMMAND:-}"

case "$cmd" in
  "upload-release "*)
    ts="${cmd#upload-release }"
    [[ "$ts" =~ $TS_RE ]] || refuse "bad release timestamp '$ts'"
    mkdir -p "$RELEASES_DIR/$ts"
    # Client streams a tar.gz of frontend/dist over stdin (see deploy-staging.yml/deploy-production.yml)
    # — never a raw scp/sftp subsystem, so this one case is the entire file-transfer surface of this key.
    tar -xzf - -C "$RELEASES_DIR/$ts"
    ;;

  "deploy "*)
    # shellcheck disable=SC2206
    parts=($cmd)
    sha="${parts[1]:-}"
    ts="${parts[2]:-}"
    [[ "$sha" =~ $SHA_RE ]] || refuse "bad commit sha '$sha'"
    [[ "$ts" =~ $TS_RE ]] || refuse "bad release timestamp '$ts'"
    [ -d "$RELEASES_DIR/$ts" ] || refuse "release $ts was never uploaded — run upload-release first"
    cd "$REPO_DIR"
    git fetch --quiet origin
    # $REPO_DIR is owned by the deploy pipeline: this must land the tree EXACTLY on $sha regardless of
    # what an operator left behind by hand (an edit to a tracked file, or an untracked file at a path
    # the target commit also uses) — plain `git checkout` refuses in both cases and used to abort the
    # deploy mid-way, after the frontend release was already uploaded (see DEPLOY.md, "Не редактируйте
    # /opt/ezbook/app вручную"). Log what is about to be discarded BEFORE forcing, so it's visible in the
    # GitHub Actions run log afterwards — then force deterministically:
    #   - `git checkout --force` discards local modifications to tracked files AND overwrites untracked
    #     files that collide with a path in the target commit;
    #   - `git clean -fd` (no -x) removes any other stray untracked files/dirs. Without -x it never
    #     touches anything matched by .gitignore — which is exactly where .env and /legal/ live — so
    #     neither is ever removed by this step.
    dirty="$(git status --short)"
    if [ -n "$dirty" ]; then
      echo "WARNING: $REPO_DIR has local changes that this deploy is about to discard (see DEPLOY.md):" >&2
      echo "$dirty" >&2
    fi
    git checkout --quiet --force "$sha"
    echo "==> Removing stray untracked files in $REPO_DIR (ignored paths like .env and legal/ are never touched):"
    git clean -fd
    exec bash deploy/deploy-remote.sh "$ts"
    ;;

  "rollback")
    cd "$REPO_DIR"
    exec bash deploy/rollback.sh
    ;;

  "rollback "*)
    ts="${cmd#rollback }"
    [[ "$ts" =~ $TS_RE ]] || refuse "bad release timestamp '$ts'"
    cd "$REPO_DIR"
    exec bash deploy/rollback.sh "$ts"
    ;;

  "health")
    curl -s http://127.0.0.1:5000/api/health/ready
    ;;

  *)
    refuse "command not in the allowlist (got: '${cmd}')"
    ;;
esac
