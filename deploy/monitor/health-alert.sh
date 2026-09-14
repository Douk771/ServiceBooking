#!/usr/bin/env bash
# Uptime alert for /api/health/ready (T-D4, US-45 п. 4г, ARCHITECTURE.md §19.6). Runs ON THE HOST via
# systemd timer (health-alert.timer), NOT inside GlitchTip — GlitchTip has no built-in uptime monitor,
# and an external SaaS uptime monitor is out of scope (data leaves the perimeter for no strong reason,
# SPEC principle §1 п.4). This is "cheap with what we already have" (US-45 п. 4г), literally 30 lines.
#
# Honest limitation, same shape as the backup one (DEPLOY.md §10.3): if the whole VPS goes down, this
# alert doesn't fire either — it runs on the same box it's checking.
set -euo pipefail

REPO_DIR="${SERVICEBOOKING_REPO_DIR:-/opt/ezbook/app}"
ENV_FILE="$REPO_DIR/.env"
URL="${HEALTH_ALERT_URL:-http://127.0.0.1:5000/api/health/ready}"
STATE_FILE="/var/run/servicebooking-health-alert.failcount"
FAILURES_BEFORE_ALERT=3

[ -f "$ENV_FILE" ] && set -a && source "$ENV_FILE" && set +a || true

fail_count=0
[ -f "$STATE_FILE" ] && fail_count="$(cat "$STATE_FILE" 2>/dev/null || echo 0)"

code="$(curl -s -o /dev/null -m 10 -w '%{http_code}' "$URL" 2>/dev/null || echo 000)"

if [ "$code" = "200" ]; then
  # Recovered — reset the counter (and, if we'd already alerted, this is implicitly the "all clear";
  # GlitchTip issues auto-resolve on their own schedule, we don't send a second "recovered" event).
  echo 0 > "$STATE_FILE"
  exit 0
fi

fail_count=$((fail_count + 1))
echo "$fail_count" > "$STATE_FILE"
logger -t servicebooking-health-alert "readiness check failed ($code), consecutive failures: $fail_count"

if [ "$fail_count" -eq "$FAILURES_BEFORE_ALERT" ]; then
  dsn="${SENTRY_DSN:-}"
  if [ -n "$dsn" ]; then
    key="$(echo "$dsn" | sed -E 's#https?://([^@]+)@.*#\1#')"
    host="$(echo "$dsn" | sed -E 's#https?://[^@]+@([^/]+)/.*#\1#')"
    project_id="$(echo "$dsn" | sed -E 's#.*/([0-9]+)$#\1#')"
    event_id="$(python3 -c 'import uuid; print(uuid.uuid4().hex)' 2>/dev/null || echo "00000000000000000000000000000000")"
    body=$(cat <<EOF
{"event_id":"$event_id","timestamp":"$(date -u +%Y-%m-%dT%H:%M:%SZ)","level":"error","logger":"servicebooking-health-alert","platform":"other","message":{"formatted":"readiness has failed $fail_count times in a row (last code: $code)"}}
EOF
)
    curl -s -m 10 -X POST "https://$host/api/$project_id/store/" \
      -H "Content-Type: application/json" \
      -H "X-Sentry-Auth: Sentry sentry_version=7, sentry_key=$key" \
      -d "$body" >/dev/null 2>&1 || true
  fi
fi
# Failures beyond the 3rd don't re-alert every 5 minutes (that would defeat "one alert" - a human is
# either already looking at it or the VPS itself is down and no alert would reach anyone anyway) -
# the counter keeps climbing in the log/journal for anyone who checks, but only failure #3 fires GlitchTip.
