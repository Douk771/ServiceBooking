# tools/test-timing — замер времени регресса (цикл 36, DO-36-01)

Нормативно: `ARCHITECTURE_CYCLE36.md` §36.4, `API_CONTRACT_CYCLE36.md` §36.28, схема отчёта `contracts/cycle36/timing-report.schema.json`.

## Полный замер одной командой

```bash
tools/test-timing/measure.sh --label <label> --runs 3        # b0-baseline / final
tools/test-timing/measure.sh --label adhoc --runs 1          # повседневный полный регресс
```

Скрипт собирает решение один раз (`dotnet build`, в замер не входит), затем по `--runs` раз гоняет unit (.NET), функциональные (.NET)
и vitest и вызывает `analyze.py`. Результат: `TestResults/timing/<label>/{report.json,report.md}` (каталог не в git).
Итоговые отчёты `b0-baseline` и `final` копируются в `tools/test-timing/results/`.

Флаги: `--suites unit,functional,vitest`, `--parallel P` (и в `xUnit.MaxParallelThreads`, и в
`SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS`), `--seed S` (`SERVICEBOOKING_TEST_ORDER_SEED`), `--route-log`
(`SERVICEBOOKING_TEST_ROUTE_LOG=1`, по времени не сравнивать), `--concurrent` (vitest одновременно с функциональными),
`--with-build`, `--force`, `--out-dir`. Вспомогательный `--filter EXPR` — `dotnet test --filter` для дымовых проверок;
отчёт помечается как частичный.

Метка прогона — обязательная `--label` (`^[a-z0-9][a-z0-9._-]{0,63}$`): `b0-baseline`, `b1-instrumented`, `l1-class-hosts`, `final`.

## Окно замера

- Машинная блокировка `${TMPDIR}/sb-test-timing.lock`: второй замер не стартует (код 2). Блокировка с умершим PID забирается.
- Перед стартом `dotnet run --project ServiceBooking.TestKit -- status --json`: любой живой (`liveness=alive`) тестовый ресурс —
  чужой прогон, код выхода 2. `--force` разрешает старт, но в отчёте `environment.foreignRunsDetected: true`; такой отчёт
  для сравнения до/после непригоден. Не запускайте замер, пока идёт любой другой прогон тестов на машине.
- Коды выхода: 0 успех, 1 аргументы, 2 среда/чужой прогон/блокировка, 3 есть упавшие тесты (отчёт всё равно пишется).

## colima

Если есть `~/.colima/default/docker.sock`, а переменные не заданы, скрипт сам выставляет
`DOCKER_HOST=unix://$HOME/.colima/default/docker.sock` и `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`.

## Каталог замера

`env.json`, `runs.jsonl` (wall и код каждого прогона, список metrics-файлов), `unit-<i>.trx`, `functional-<i>.trx`,
`*.stdout.log`, копии `sb-test-metrics-<runKey>.jsonl` (и `sb-test-routes-*.jsonl`), `docker-stats-functional-<i>.csv`,
`vitest-<i>.json`, `testkit-status.json`, `build.log`, `report.json`, `report.md`.

## analyze.py

```bash
python3 tools/test-timing/analyze.py <dir> --out <dir>/report.json --md <dir>/report.md
python3 tools/test-timing/analyze.py --compare a.json b.json
python3 tools/test-timing/analyze.py --emit-durations report.json --out ServiceBooking.Tests/test-durations.json
```

`report.md` содержит: время по фазам функционального набора (сборка, старт контейнера, шаблон БД, базы классов, старты хоста,
тесты), топ медленных классов, число и суммарное время стартов хоста по фабрикам и по классам, простой потоков и хвост,
CPU Postgres, фазы и топ файлов vitest. Валидация: `npx ajv-cli validate -s contracts/cycle36/timing-report.schema.json -d report.json`.
`--ci-summary <trx|vitest.json>... [--metrics "<glob>"] --thresholds thresholds.json` — DO-36-04: Markdown (топ-10 классов и тестов, старты хоста) в stdout и `::warning title=Slow tests::` при превышении порогов; код выхода 0 (1 только при ошибке разбора). `--emit-durations` берёт классы из `topClasses` отчёта (топ-20), для L7 этого
может не хватить — тогда доработать вместе с L7.
