#!/usr/bin/env bash
# DO-36-01: замер времени полного регресса (ARCHITECTURE_CYCLE36.md §36.4, API_CONTRACT_CYCLE36.md §36.28.1).
# Коды выхода: 0 успех, 1 неверные аргументы, 2 среда не готова / чужой прогон / занята блокировка, 3 есть упавшие тесты.
set -uo pipefail

usage() {
  cat >&2 <<USAGE
Usage: measure.sh --label <label> [--runs N=3] [--suites unit,functional,vitest] [--parallel P] [--seed S]
                  [--route-log] [--concurrent] [--with-build] [--force] [--out-dir DIR] [--filter EXPR]
USAGE
}

LABEL=""; RUNS=3; SUITES="unit,functional,vitest"; PARALLEL=""; SEED=""
ROUTE_LOG=0; CONCURRENT=0; WITH_BUILD=0; FORCE=0; OUT_DIR=""; FILTER=""
while [ $# -gt 0 ]; do
  case "$1" in
    --label) LABEL="${2:-}"; shift 2 ;;
    --runs) RUNS="${2:-}"; shift 2 ;;
    --suites) SUITES="${2:-}"; shift 2 ;;
    --parallel) PARALLEL="${2:-}"; shift 2 ;;
    --seed) SEED="${2:-}"; shift 2 ;;
    --route-log) ROUTE_LOG=1; shift ;;
    --concurrent) CONCURRENT=1; shift ;;
    --with-build) WITH_BUILD=1; shift ;;
    --force) FORCE=1; shift ;;
    --out-dir) OUT_DIR="${2:-}"; shift 2 ;;
    --filter) FILTER="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage; exit 1 ;;
  esac
done

if ! [[ "$LABEL" =~ ^[a-z0-9][a-z0-9._-]{0,63}$ ]]; then echo "--label is required and must match ^[a-z0-9][a-z0-9._-]{0,63}\$" >&2; exit 1; fi
if ! [[ "$RUNS" =~ ^[1-9][0-9]*$ ]]; then echo "--runs must be a positive integer" >&2; exit 1; fi
if [ -n "$PARALLEL" ] && ! [[ "$PARALLEL" =~ ^[1-9][0-9]*$ ]]; then echo "--parallel must be a positive integer" >&2; exit 1; fi
if [ -n "$SEED" ] && ! [[ "$SEED" =~ ^-?[0-9]+$ ]]; then echo "--seed must be an integer" >&2; exit 1; fi
IFS=',' read -r -a SUITE_LIST <<< "$SUITES"
for s in "${SUITE_LIST[@]}"; do
  case "$s" in unit|functional|vitest) ;; *) echo "unknown suite: $s" >&2; exit 1 ;; esac
done
has_suite() { local s; for s in "${SUITE_LIST[@]}"; do [ "$s" = "$1" ] && return 0; done; return 1; }

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT" || exit 2
[ -n "$OUT_DIR" ] || OUT_DIR="$ROOT/TestResults/timing/$LABEL"
mkdir -p "$OUT_DIR" || exit 2
OUT_DIR="$(cd "$OUT_DIR" && pwd)"
METRICS_DIR="$ROOT/TestResults"
mkdir -p "$METRICS_DIR"

# colima: Testcontainers без этих двух переменных не находит Docker.
if [ -S "$HOME/.colima/default/docker.sock" ]; then
  [ -n "${DOCKER_HOST:-}" ] || export DOCKER_HOST="unix://$HOME/.colima/default/docker.sock"
  [ -n "${TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE:-}" ] || export TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock
fi

now() { python3 -c 'import time; print(f"{time.time():.3f}")'; }

# --- 1. окно замера: машинная блокировка
LOCK_BASE="${TMPDIR:-/tmp}"
LOCK="${LOCK_BASE%/}/sb-test-timing.lock"
if ! mkdir "$LOCK" 2>/dev/null; then
  holder="$(cat "$LOCK/pid" 2>/dev/null || true)"
  if [ -n "$holder" ] && ! kill -0 "$holder" 2>/dev/null; then
    echo "stale lock from dead pid $holder, reclaiming" >&2
    rm -rf "$LOCK"; mkdir "$LOCK" || exit 2
  else
    echo "another measurement is in progress (lock $LOCK, pid ${holder:-unknown})" >&2
    exit 2
  fi
fi
echo "$$" > "$LOCK/pid"
STATS_PIDS=()
cleanup() {
  local p
  for p in ${STATS_PIDS[@]+"${STATS_PIDS[@]}"}; do [ -n "$p" ] && kill "$p" 2>/dev/null; done
  rm -rf "$LOCK"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# --- 2. чужие живые тестовые ресурсы
FOREIGN=false
STATUS_JSON="$OUT_DIR/testkit-status.json"
if dotnet run --project ServiceBooking.TestKit -- status --json > "$STATUS_JSON.raw" 2> "$STATUS_JSON.err"; then
  python3 - "$STATUS_JSON.raw" "$STATUS_JSON" <<'PY'
import sys
raw = open(sys.argv[1], encoding="utf-8").read()
i = raw.find("{")
open(sys.argv[2], "w", encoding="utf-8").write(raw[i:] if i >= 0 else "{}")
PY
  rm -f "$STATUS_JSON.raw"
  alive="$(python3 - "$STATUS_JSON" <<'PY'
import json, sys
try:
    d = json.load(open(sys.argv[1], encoding="utf-8"))
except Exception:
    print("error"); raise SystemExit
print(sum(1 for r in d.get("testResources", []) if r.get("liveness") == "alive"))
PY
)"
  if [ "$alive" = "error" ]; then
    echo "cannot parse TestKit status output ($STATUS_JSON)" >&2; exit 2
  elif [ "$alive" != "0" ]; then
    if [ "$FORCE" = 1 ]; then
      echo "WARNING: $alive foreign live test resource(s); --force: report marked foreignRunsDetected" >&2
      FOREIGN=true
    else
      echo "foreign live test resources detected ($alive); see $STATUS_JSON. Wait for them to finish or use --force." >&2
      exit 2
    fi
  fi
else
  if has_suite functional && [ -z "${SERVICEBOOKING_TEST_CONNECTION:-}" ]; then
    echo "TestKit status failed (Docker unavailable?); see $STATUS_JSON.err" >&2; exit 2
  fi
  echo "WARNING: TestKit status failed; foreign-run check skipped" >&2
fi

# --- 3. окружение -> env.json
if [ -n "$SEED" ]; then SEED_VALUE="$SEED"; else SEED_VALUE="$(( (RANDOM * 32768 + RANDOM) % 2000000000 + 1 ))"; fi
echo "order seed: $SEED_VALUE"
HOSTNAME_V="$(hostname)"
if [ "$(uname -s)" = Darwin ]; then
  OS_V="$(sw_vers -productName 2>/dev/null) $(sw_vers -productVersion 2>/dev/null) ($(uname -m))"
  CORES="$(sysctl -n hw.ncpu)"; MEM_BYTES="$(sysctl -n hw.memsize)"
else
  OS_V="$(uname -srm)"; CORES="$(nproc)"; MEM_BYTES="$(awk '/MemTotal/ {print $2*1024}' /proc/meminfo)"
fi
COLIMA_JSON="$(colima list --json 2>/dev/null | head -1 || true)"
DOTNET_V="$(dotnet --version 2>/dev/null || true)"
NODE_V="$(node --version 2>/dev/null || true)"
MODE="container"; [ -z "${SERVICEBOOKING_TEST_CONNECTION:-}" ] || MODE="external"
if [ -z "$PARALLEL" ]; then
  PARALLEL="$(python3 -c 'import json; print(json.load(open("ServiceBooking.Tests/xunit.runner.json")).get("maxParallelThreads", 4))' 2>/dev/null || echo 4)"
fi
COMMIT="$(git rev-parse --short=12 HEAD)"
DIRTY=false; [ -z "$(git status --porcelain --untracked-files=no)" ] || DIRTY=true
BUILD_MODE="no-build"; [ "$WITH_BUILD" = 0 ] || BUILD_MODE="with-build"
NOTES=""
[ -z "$FILTER" ] || NOTES="partial run, --filter $FILTER: not comparable. "
[ "$ROUTE_LOG" = 0 ] || NOTES="${NOTES}route-log enabled: timings not comparable. "

# Значения идут в Python через окружение, а не подстановкой в heredoc: --filter, имя хоста и JSON colima
# могут содержать кавычки и обратные слэши (heredoc ниже поэтому в кавычках и ничего не раскрывает).
SB_HOST="$HOSTNAME_V" SB_OS="$OS_V" SB_CORES="$CORES" SB_MEM_BYTES="$MEM_BYTES" SB_MODE="$MODE" SB_PARALLEL="$PARALLEL" \
SB_BUILD_MODE="$BUILD_MODE" SB_DOTNET="$DOTNET_V" SB_NODE="$NODE_V" SB_CONCURRENT="$CONCURRENT" SB_FOREIGN="$FOREIGN" \
SB_NOTES="$NOTES" SB_LABEL="$LABEL" SB_COMMIT="$COMMIT" SB_DIRTY="$DIRTY" SB_SEED="$SEED_VALUE" SB_COLIMA_JSON="$COLIMA_JSON" \
python3 - "$OUT_DIR/env.json" <<'PY'
import json, sys, os
E = os.environ
colima = None
raw = E.get("SB_COLIMA_JSON", "").strip()
if raw:
    try:
        c = json.loads(raw)
        colima = {"cpu": int(c.get("cpus", 1)), "memoryGb": round(c.get("memory", 0) / 1073741824, 2), "profile": c.get("name", "default")}
    except Exception:
        colima = None
docker = "colima" if colima else ("docker-engine" if os.path.exists("/var/run/docker.sock") else "none")
if E.get("CI") == "true": docker = "github-service"
env = {
  "host": E["SB_HOST"], "os": E["SB_OS"], "cpuCores": int(E["SB_CORES"]), "memoryGb": round(int(E["SB_MEM_BYTES"]) / 1073741824, 2),
  "dockerRuntime": docker, "mode": E["SB_MODE"], "parallelism": int(E["SB_PARALLEL"]), "build": E["SB_BUILD_MODE"],
  "dotnetSdk": E["SB_DOTNET"], "node": E["SB_NODE"], "concurrentSuites": E["SB_CONCURRENT"] == "1",
  "foreignRunsDetected": E["SB_FOREIGN"] == "true",
}
if colima: env["colima"] = colima
notes = E["SB_NOTES"].strip()
if notes: env["notes"] = notes
json.dump({"label": E["SB_LABEL"], "buildSeconds": None, "commit": E["SB_COMMIT"], "dirty": E["SB_DIRTY"] == "true", "seed": int(E["SB_SEED"]), "environment": env,
           "createdAtUtc": __import__("datetime").datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")}, open(sys.argv[1], "w"), indent=2)
PY
[ -s "$OUT_DIR/env.json" ] || { echo "failed to write env.json" >&2; exit 2; }

# --- 4. сборка один раз (в замер не входит)
if has_suite unit || has_suite functional; then
  echo "== build =="
  B0="$(now)"
  dotnet build ServiceBooking.sln -c Debug --nologo -v q > "$OUT_DIR/build.log" 2>&1 || { echo "build failed, see $OUT_DIR/build.log" >&2; exit 2; }
  B1="$(now)"
  python3 - "$OUT_DIR/env.json" "$B0" "$B1" <<'PY'
import json, sys
f, a, b = sys.argv[1:4]
d = json.load(open(f)); d["buildSeconds"] = round(float(b) - float(a), 1)
json.dump(d, open(f, "w"), indent=2)
PY
fi

# --- 5. прогоны
RUNS_FILE="$OUT_DIR/runs.jsonl"
: > "$RUNS_FILE"
ANY_FAILED=0
export SERVICEBOOKING_TEST_ORDER_SEED="$SEED_VALUE"
[ "$ROUTE_LOG" = 0 ] || export SERVICEBOOKING_TEST_ROUTE_LOG=1
NOBUILD="--no-build"; [ "$WITH_BUILD" = 0 ] || NOBUILD=""
# --filter — вспомогательный флаг для дымовых проверок (не из контракта): частичный прогон, для сравнения непригоден.
FILTER_ARGS=(); [ -z "$FILTER" ] || FILTER_ARGS=(--filter "$FILTER")

start_stats() { # $1 = csv
  (
    echo "ts,container,cpu_percent" > "$1"
    while true; do
      for c in $(docker ps -q --filter "label=com.servicebooking.test=1" --filter "label=com.servicebooking.test.workdir=$ROOT" 2>/dev/null); do
        docker stats --no-stream --format '{{.Name}},{{.CPUPerc}}' "$c" 2>/dev/null | sed "s/^/$(date +%s),/; s/%//" >> "$1"
      done
      sleep 5
    done
  ) &
  STATS_PIDS+=("$!")
}

record_run() { # suite index start end exit metrics-list
  python3 - "$RUNS_FILE" "$@" <<'PY'
import json, sys
f, suite, idx, start, end, code, metrics = sys.argv[1:8]
with open(f, "a") as fh:
    fh.write(json.dumps({"suite": suite, "index": int(idx), "startEpoch": float(start), "endEpoch": float(end),
                         "wallSeconds": round(float(end) - float(start), 3), "exitCode": int(code),
                         "metricsFiles": [m for m in metrics.split(",") if m]}) + "\n")
PY
}

run_unit() { # $1 index
  local i="$1" t0 t1 code
  t0="$(now)"
  dotnet test ServiceBooking.UnitTests $NOBUILD --nologo ${FILTER_ARGS[@]+"${FILTER_ARGS[@]}"} --logger "trx;LogFileName=unit-$i.trx" --results-directory "$OUT_DIR" > "$OUT_DIR/unit-$i.stdout.log" 2>&1
  code=$?; t1="$(now)"
  record_run unit "$i" "$t0" "$t1" "$code" ""
  [ "$code" = 0 ] || ANY_FAILED=1
}

run_functional() { # $1 index
  local i="$1" t0 t1 code before after m files=""
  before="$(ls "$METRICS_DIR"/sb-test-metrics-*.jsonl "$METRICS_DIR"/sb-test-routes-*.jsonl 2>/dev/null | sort || true)"
  start_stats "$OUT_DIR/docker-stats-functional-$i.csv"; local sp="${STATS_PIDS[${#STATS_PIDS[@]}-1]}"
  t0="$(now)"
  SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS="$PARALLEL" dotnet test ServiceBooking.Tests $NOBUILD --nologo ${FILTER_ARGS[@]+"${FILTER_ARGS[@]}"} \
    --logger "trx;LogFileName=functional-$i.trx" --results-directory "$OUT_DIR" \
    -- xUnit.MaxParallelThreads="$PARALLEL" > "$OUT_DIR/functional-$i.stdout.log" 2>&1
  code=$?; t1="$(now)"
  kill "$sp" 2>/dev/null; wait "$sp" 2>/dev/null
  after="$(ls "$METRICS_DIR"/sb-test-metrics-*.jsonl "$METRICS_DIR"/sb-test-routes-*.jsonl 2>/dev/null | sort || true)"
  for m in $(comm -13 <(echo "$before") <(echo "$after")); do
    cp "$m" "$OUT_DIR/"
    case "$(basename "$m")" in sb-test-metrics-*) files="$files,$(basename "$m")" ;; esac
  done
  record_run functional "$i" "$t0" "$t1" "$code" "${files#,}"
  [ "$code" = 0 ] || ANY_FAILED=1
}

run_vitest() { # $1 index
  local i="$1" t0 t1 code
  t0="$(now)"
  ( cd "$ROOT/frontend" && VITE_SMARTCAPTCHA_SITEKEY= npx vitest run --reporter=default --reporter=json --outputFile.json="$OUT_DIR/vitest-$i.json" ) > "$OUT_DIR/vitest-$i.stdout.log" 2>&1
  code=$?; t1="$(now)"
  record_run vitest "$i" "$t0" "$t1" "$code" ""
  [ "$code" = 0 ] || ANY_FAILED=1
}

for i in $(seq 1 "$RUNS"); do
  if [ "$CONCURRENT" = 1 ] && has_suite functional && has_suite vitest; then
    echo "== run $i: functional + vitest concurrently =="
    run_vitest "$i" & vp=$!
    run_functional "$i"
    wait "$vp"
    has_suite unit && { echo "== run $i: unit =="; run_unit "$i"; }
  else
    for s in "${SUITE_LIST[@]}"; do
      echo "== run $i: $s =="
      case "$s" in unit) run_unit "$i" ;; functional) run_functional "$i" ;; vitest) run_vitest "$i" ;; esac
    done
  fi
done
# при параллельном запуске vitest ANY_FAILED выставляется в подоболочке; доверяем кодам в runs.jsonl
python3 - "$RUNS_FILE" <<'PY' && ANY_FAILED=0 || ANY_FAILED=1
import json, sys
sys.exit(1 if any(json.loads(l)["exitCode"] != 0 for l in open(sys.argv[1]) if l.strip()) else 0)
PY

# --- 6. анализ
python3 "$ROOT/tools/test-timing/analyze.py" "$OUT_DIR" --out "$OUT_DIR/report.json" --md "$OUT_DIR/report.md" || { echo "analysis failed" >&2; exit 2; }
echo "report: $OUT_DIR/report.json"
[ "$ANY_FAILED" = 0 ] || { echo "some test runs failed (exit 3)" >&2; exit 3; }
exit 0
