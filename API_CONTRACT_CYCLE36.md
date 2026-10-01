# API_CONTRACT — цикл 36 ServiceBooking: ревизия регрессионных тестов и ускорение полного прогона

**Разделы §36.20–§36.29.** Решения и механизмы — `ARCHITECTURE_CYCLE36.md` §36.0–§36.17. Требования — корневой `SPEC.md`
цикла 36. Базовая ревизия — `a1e2259`.

Корневой `API_CONTRACT.md` — документ цикла 3. По конвенции проекта (`CURRENT_STATE.md` §6.5, §10.5) он не перезаписывается,
поэтому в этом цикле ничего не архивируется: файл цикла новый.

**Источник истины по форме** — JSON Schema в `contracts/cycle36/` (draft-07, без `format`, проверяются голым `ajv-cli`, как в
цикле 30). Если текст расходится со схемой по форме, права схема. По смыслу и порядку действий прав этот текст.

---

## §36.20. Итог: HTTP API в цикле 36 не меняется

- **Новых маршрутов, полей DTO, кодов ответа, текстов сервера, заголовков, лимитов нет.** Цикл не трогает продуктовые проекты
  `ServiceBooking.API`, `.Core`, `.Infrastructure`, `.LegalKit` и миграции. Единственное допустимое исключение — точка подмены
  по Q-36-3 (`ARCHITECTURE_CYCLE36.md` §36.7.5). Она не меняет ни одного маршрута и поведения, и если появится, то описывается
  в реестре. Сейчас ни одна не запланирована.
- **Новой OpenAPI нет.** Пустая или копийная `contracts/cycle36/openapi.yaml` дала бы QA ложное «проверено» (так же решено в
  циклах 27, 30, 34). Источники истины по форме HTTP API прежние: `contracts/cycle{7,9,…,33}/openapi.yaml` (дельты своих
  циклов) и полный перечень маршрутов с атрибутами — эталон `ServiceBooking.Tests/Tests/Cycle22RouteTable.golden.txt`.
  `contracts/redocly.yaml`, список линта и генератов в CI не меняются.
- **Потребители HTTP API в цикле** — только функциональные тесты, как и раньше. Удаление или объединение тестов не меняет
  API, но может уменьшить **покрытие** маршрутов. Для этого есть отдельная машинная проверка (§36.24, `route_coverage.py`).
- **Новые договорённости цикла — файловые и CLI.** Кто кому что передаёт, описано в §36.22–§36.29.

## §36.21. Как автоматически проверить, что API не изменился

QA выполняет на HEAD ветки. Все проверки должны пройти:

| # | Проверка | Команда | Ожидаем |
|---|---|---|---|
| 1 | Эталон маршрутов не правился | `git diff --exit-code a1e2259 -- ServiceBooking.Tests/Tests/Cycle22RouteTable.golden.txt` | код 0 |
| 2 | Живая таблица маршрутов равна эталону | `dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~Cycle22RouteTableTests"` | CY22-12 зелёный |
| 3 | Контракты прежних циклов и генераты фронта не правились | `git diff --exit-code a1e2259 -- contracts ':(exclude)contracts/cycle36' frontend/src/types` | код 0 |
| 4 | Продуктовый код не менялся | `git diff --stat a1e2259 -- ServiceBooking.API ServiceBooking.Core ServiceBooking.Infrastructure ServiceBooking.LegalKit` | пусто; иначе каждая строка есть в реестре с действием «изменение продуктового кода» |
| 5 | Валидатор OpenAPI в тестах зелёный | `dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~OpenApiContractValidatorTests\|FullyQualifiedName~Cycle29ContractTests"` | зелёные |
| 6 | CI-шаги генератов и линта контрактов зелёные без правок | джоб `frontend`: «API types must match…», «Lint API contracts», «Generated API types must match the contracts», «Contract JSON must match…» | зелёные |

Проверки 1–4 не зависят от того, что сказано в отчётах разработчиков. Это и есть машинное доказательство, что backend и
frontend «сошлись» по интерфейсу, который в этом цикле не менялся.

---

## §36.22. Файловые контракты цикла: кто пишет, кто читает

| Файл (в `TestResults/` — не в git) | Схема | Пишет | Читает |
|---|---|---|---|
| `TestResults/sb-test-metrics-<runKey>.jsonl` | `contracts/cycle36/test-metrics.schema.json` (на строку) | тестовая инфраструктура `ServiceBooking.Tests` (BE-36-01) | `tools/test-timing/analyze.py` (DO-36-01, DO-36-04) |
| `TestResults/sb-test-routes-<runKey>.jsonl` | `contracts/cycle36/route-hits.schema.json` (на строку) | тестовый хост при `SERVICEBOOKING_TEST_ROUTE_LOG=1` (BE-36-01) | `tools/test-audit/route_coverage.py` (BE-36-02, QA-36-02) |
| `TestResults/timing/<label>/report.json`; коммитятся `tools/test-timing/results/{b0-baseline,final}.json` | `contracts/cycle36/timing-report.schema.json` | `analyze.py` | QA, `docs/testing-isolation.md`, CI-сводка |
| `tools/test-audit/results/{before,after}-<suite>.json` (в git) | `contracts/cycle36/test-inventory.schema.json` | `tools/test-audit/inventory.py` (BE-36-02; запускает QA) | `diff_inventory.py`, QA |
| `contracts/cycle36/test-areas.json` (в git) | `contracts/cycle36/test-areas.schema.json` | архитектор (id); FE-36-03 (`frontendPathPrefixes`) | guard CY36-01 (бэкенд), guard CY36-02 (vitest), `frontend/scripts/test-area.mjs`, `inventory.py` |
| `tools/test-timing/thresholds.json` (в git) | §36.28.4 | DO-36-04 | `analyze.py --ci-summary` |
| `TEST_CATALOG.md`, раздел «Цикл 36 — ревизия» (в git) | §36.29 | BE-36-02 (скелет), BE-36-03/08, FE-36-01 | `diff_inventory.py`, QA, заказчик |

Проверка формы вручную (QA): `npx --yes ajv-cli@5 validate -s contracts/cycle36/<schema> -d <file>`. Для JSONL — по строке:
`jq -c . <file>.jsonl | while read -r l; do echo "$l" > /tmp/l.json; npx --yes ajv-cli@5 validate -s … -d /tmp/l.json || exit 1; done`.

## §36.23. `sb-test-metrics-<runKey>.jsonl`

- Путь: `<корень рабочей копии>/TestResults/`, как у `sb-test-run.json` (`TestInfrastructure.WorkingCopyRoot`). В имени ключ
  прогона, поэтому процессы и прогоны не пишут в один файл.
- Кодировка UTF-8, одна JSON-строка на событие, `\n`. Запись лучшим усилием: ошибка ввода-вывода проглатывается, тест из-за
  неё не падает. `SERVICEBOOKING_TEST_METRICS=0` — файла нет.
- События и их смысл:

| `event` | Когда | Поля кроме `v, event, runKey, utc` |
|---|---|---|
| `run-start` | первый класс поднял окружение прогона | `mode`, `parallel`, `pid`, необяз. `orderSeed` |
| `server-ready` | контейнер поднят или внешний сервер подключён | `ms` |
| `template-ready` | шаблон мигрирован | `ms` |
| `class-db-created` / `class-db-dropped` | клон базы класса создан / удалён | `slot`, `ms` |
| `class-recorded` | первый тест класса сообщил своё имя | `slot`, `testClass` (полное имя, вложенные — с `+`) |
| `host-booted` | хост поднят | `slot`, `factoryTag`, `factoryType`, `ms` (от `TestHostSettings.Apply` до `ApplicationStarted`) |
| `host-disposed` | хост остановлен (если измерено) | `slot`, `factoryTag`, `factoryType`, `ms` |

Пример:
```json
{"v":1,"event":"run-start","runKey":"a3f19c7b","utc":"2026-10-02T09:00:01.120Z","mode":"container","parallel":4,"pid":48211}
{"v":1,"event":"class-db-created","runKey":"a3f19c7b","utc":"2026-10-02T09:00:09.004Z","slot":"c07","ms":212.4}
{"v":1,"event":"class-recorded","runKey":"a3f19c7b","utc":"2026-10-02T09:00:09.950Z","slot":"c07","testClass":"ServiceBooking.Tests.Tests.PhoneVerificationTests"}
{"v":1,"event":"host-booted","runKey":"a3f19c7b","utc":"2026-10-02T09:00:11.301Z","slot":"c07","factoryTag":"api","factoryType":"PhoneVerificationEnabledFactory","ms":1834.0}
```

## §36.24. `sb-test-routes-<runKey>.jsonl` и правило сопоставления с эталоном

- Пишется только при `SERVICEBOOKING_TEST_ROUTE_LOG=1`. Одна строка на уникальную четвёрку `(slot, method, route, status)` в
  пределах процесса. Запросы, не попавшие ни в один `RouteEndpoint` (404 маршрутизации, статика `/uploads`), не пишутся.
- `route` — `RoutePattern.RawText` сработавшего endpoint, **байт в байт** как второе поле строки эталона
  (`api/Companies/{id:guid}/members/{memberId:guid}`, `/api/health/live`).
- Ключ строки эталона — начало строки до первого пробела после шаблона: `<METHODS> <RawText>`. `METHODS` бывает списком через
  запятую или `ANY`. Маршрут эталона **покрыт**, если в журнале есть строка с тем же `route` и `method` ∈ `METHODS`. Для `ANY`
  метод любой.
- Пример строки:
```json
{"v":1,"runKey":"a3f19c7b","slot":"c07","method":"POST","route":"api/phone-verification/sessions","status":201}
```

## §36.25. Отчёт замера `report.json`

Форма — `timing-report.schema.json`. Смысл ключевых полей (подробности расчёта — `ARCHITECTURE_CYCLE36.md` §36.4.2):

| Поле | Смысл |
|---|---|
| `label` | метка замера (`b0-baseline`, `b1-instrumented`, `l1-class-hosts`, `final`, …) |
| `environment` | условия, без которых числа несравнимы. Сравнивать можно только отчёты с одинаковыми `host`, `cpuCores`, `colima`, `mode`, `parallelism`*, `build` и `foreignRunsDetected: false` (*кроме замеров, которые меняют сам P) |
| `suites[].medianWallSeconds` | медиана wall-времени команды набора по `runs` |
| `suites[].functional.hostBoots` | сколько раз и за сколько поднимались хосты; `byClass` — где больше всего стартов |
| `suites[].functional.scheduling` | простой потоков, самый длинный класс, хвост |
| `suites[].vitest.phases` | фазы из строки `Duration` vitest (суммы по воркерам) |
| `totalMedianSeconds` | сумма медиан наборов = «весь регресс» из блока заказчика, п. 1 (без `npm ci`, lint и сборки) |

Рядом `analyze.py` кладёт `report.md`: те же числа таблицами — топ-20 классов, топ-20 тестов, старты хоста по фабрикам,
топ-20 файлов vitest. В сравнении `--compare a.json b.json` — колонки «до», «после», Δ, Δ %.

## §36.26. Инвентарь тестов

Форма — `test-inventory.schema.json`. Правила построения (`tools/test-audit/inventory.py`):

- **.NET (`unit`, `functional`):**
  - список — `dotnet test <проект> --no-build --list-tests`. Docker и база не нужны: фикстуры не создаются;
  - строка списка `Namespace.Class[+Nested].Method(args…)` → `key` = без `(args…)`; `cases` = число строк с этим `key`;
  - `id` — из `[TestCase("…")]` на методе (регэксп по исходнику файла класса, атрибут перед сигнатурой метода);
  - `file` — файл, где объявлен метод; `className`, `method` — из ключа;
  - `areas` — значения `[Trait("Area", …)]` класса, для вложенных — самого вложенного класса; для `unit` пусто;
  - `protectedZone` — по правилам «файл» и «префикс ID» из `ARCHITECTURE_CYCLE36.md` §36.9.3.
- **vitest:**
  - список — `cd frontend && npx vitest list --json` → `[{name, file}]`;
  - `key` = `<file от frontend/>::<name>`, где `name` — `describe > … > it`; `cases` = число элементов с этим `key` (`it.each`);
  - `id` — ID в начале имени `it`, если он есть (`^[A-Z][A-Z0-9]*(-[A-Za-z0-9]+)+`), иначе `null`;
  - `areas` — по `frontendPathPrefixes`;
  - `protectedZone` — по префиксам путей из §36.9.3.
- `totalCases` = Σ `cases` = число запусков набора. Оно же пишется в `TEST_CATALOG.md` и `CURRENT_STATE.md` §7.1 (QA-36-04).
- Ключи сортируются по возрастанию (ordinal), поэтому дифф двух инвентарей в git читается.

## §36.27. Области быстрого контура

Форма — `test-areas.schema.json`, данные — `contracts/cycle36/test-areas.json`.

- `id` — закрытый список: `account`, `bookings`, `companies`, `orders`, `notifications`, `billing`, `legal`, `admin`,
  `security`, `platform`. Добавить или переименовать область — это правка контракта (архитектор).
- **Бэкенд:** трейт `[Trait("Area", "<id>")]` на классе. Имя трейта — поле `traitName` (`"Area"`). Константы — в
  `ServiceBooking.Tests/Infrastructure/TestAreas.cs`, значения байт в байт как `id`. Guard CY36-01 требует ≥1 трейт с
  допустимым `id` на каждом не абстрактном классе с тестами.
- **Фронт:** файл `*.test.ts(x)` входит в область, если его путь от `frontend/` начинается с одного из
  `frontendPathPrefixes` (`String.prototype.startsWith`, без glob). Файл может входить в несколько областей. Guard CY36-02
  требует ≥1 область на каждый файл.

## §36.28. CLI-контракты

Коды выхода общие для всех инструментов цикла: **0** — успех; **1** — неверные аргументы; **2** — среда не готова (нет
Docker, живой чужой прогон, занята блокировка замера); **3** — в прогоне есть упавшие тесты (отчёт всё равно пишется);
**4** — нарушение сверки (инвентарь, покрытие маршрутов).

### §36.28.1 `tools/test-timing/measure.sh`

```
measure.sh --label <label> [--runs N=3] [--suites unit,functional,vitest] [--parallel P] [--seed S]
           [--route-log] [--concurrent] [--with-build] [--force] [--out-dir DIR]
```

| Флаг | Смысл |
|---|---|
| `--label` | обязательна, `^[a-z0-9][a-z0-9._-]{0,63}$` |
| `--runs` | прогонов на набор; для решений о рычагах — 1, для `b0`/`final` — 3 |
| `--suites` | подмножество наборов |
| `--parallel P` | P функционального набора: передаётся **и** в `-- xUnit.MaxParallelThreads=P`, **и** в `SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS=P`. По умолчанию — из `xunit.runner.json` |
| `--seed S` | `SERVICEBOOKING_TEST_ORDER_SEED=S` для всех прогонов (по умолчанию случайное, печатается) |
| `--route-log` | `SERVICEBOOKING_TEST_ROUTE_LOG=1`; такой прогон для сравнения времени непригоден, в отчёте `notes` |
| `--concurrent` | vitest идёт одновременно с функциональным набором (эксперимент L12); `environment.concurrentSuites: true` |
| `--with-build` | `dotnet test` без `--no-build` (показать цену сборки) |
| `--force` | не останавливаться на чужих живых прогонах; `foreignRunsDetected: true` |
| `--out-dir` | по умолчанию `TestResults/timing/<label>/` |

Раскладка каталога замера: `env.json`, `unit-<i>.trx`, `functional-<i>.trx`, `functional-<i>.stdout.log`,
`sb-test-metrics-<runKey>.jsonl` (копия), `sb-test-routes-<runKey>.jsonl` (при `--route-log`),
`docker-stats-functional-<i>.csv`, `vitest-<i>.json`, `vitest-<i>.stdout.log`, `report.json`, `report.md`.
Выход: 0 или 3 по тестам, 2 по среде.

### §36.28.2 `tools/test-timing/analyze.py`

```
analyze.py <measure-dir> --out report.json [--md report.md]
analyze.py --compare <a.json> <b.json> [--md]
analyze.py --ci-summary <file.trx|vitest.json>... [--metrics "<glob>"] --thresholds tools/test-timing/thresholds.json
analyze.py --emit-durations <report.json> --out ServiceBooking.Tests/test-durations.json
```

- `--ci-summary` печатает Markdown в stdout (CI дописывает его в `$GITHUB_STEP_SUMMARY`), а превышения порогов — строками
  `::warning title=Slow tests::…`. **Код выхода всегда 0**, кроме ошибки разбора (1).
- `--emit-durations` выводит `{"schemaVersion":1,"classes":{"<полное имя класса>": <секунды>}}` для L7.

### §36.28.3 `tools/test-audit/*`

```
inventory.py --suite {unit|functional|vitest} --out <file.json>
diff_inventory.py --suite <s> --before <before.json> --after <after.json> --registry TEST_CATALOG.md
route_coverage.py --golden ServiceBooking.Tests/Tests/Cycle22RouteTable.golden.txt --before <routes.jsonl>... --after <routes.jsonl>...
```

- `inventory.py` для `unit`/`functional` ждёт уже собранные проекты (`dotnet build` до запуска). Для `vitest` — установленный
  `node_modules`.
- `diff_inventory.py` печатает три списка: «исчезло без записи в реестре», «появилось без записи», «изменилось `cases` без
  записи», плюс «удалён защищённый». Любой непустой список — код 4.
- `route_coverage.py` печатает «покрыто до», «покрыто после», **«потеряно»** и «никогда не покрыто» (информационно).
  Непустое «потеряно» — код 4.

### §36.28.4 `tools/test-timing/thresholds.json`

```json
{
  "schemaVersion": 1,
  "unitWallSecondsWarn": 0,
  "functionalWallSecondsWarn": 0,
  "vitestWallSecondsWarn": 0,
  "classWallSecondsWarn": 60,
  "testSecondsWarn": 10,
  "hostBootsPerClassWarn": 3
}
```
Все ключи обязательны, числа ≥ 0. `0` означает «порог не задан». Значения `*WallSecondsWarn` ставит DO-36-04 как 1,25 ×
медианы `ci-final` соответствующего шага. Превышение — предупреждение, не падение (SPEC US-36-07).

### §36.28.5 Частичный прогон (быстрый контур)

```bash
dotnet test ServiceBooking.Tests --filter "Area=orders"                      # одна область
dotnet test ServiceBooking.Tests --filter "Area=orders|Area=notifications"   # несколько
dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~CompaniesTests" # класс со всеми частями разбиения
cd frontend && npm run test:area -- orders notifications                     # vitest по областям
```
`npm run test:area` без аргументов или с неизвестной областью — код 1 и список допустимых `id`.

### §36.28.6 Переменные окружения тестов (новые)

| Переменная | Значения | По умолчанию | Действие |
|---|---|---|---|
| `SERVICEBOOKING_TEST_METRICS` | `0` / иное | включено | `0` — не писать `sb-test-metrics-*.jsonl` |
| `SERVICEBOOKING_TEST_ROUTE_LOG` | `1` / иное | выключено | `1` — писать `sb-test-routes-*.jsonl` |
| `SERVICEBOOKING_TEST_FAST_HASH` | `0` / иное | включено | `0` — штатное число итераций PBKDF2 в тестовых хостах |

Прежние переменные (`SERVICEBOOKING_TEST_CONNECTION`, `…_MAX_PARALLEL_THREADS`, `…_RUN_KEY`, `…_NO_TEMPLATE`,
`…_ORDER_SEED`) не меняются. Обязательных переменных не появляется: Rider «Run» работает без них.

## §36.29. Реестр ревизии в `TEST_CATALOG.md`

Раздел `## Цикл 36 — ревизия (реестр удалений и изменений)` в конце `TEST_CATALOG.md`. Внутри — подразделы в этом порядке:

1. `### Категории по файлам` — таблица на все файлы трёх наборов:

   | Набор | Файл | Кат. | Примечание |
   |---|---|---|---|
   | functional | `ServiceBooking.Tests/Tests/CompanyAddressTests.cs` | Г | А для ADDR-0xx, см. R36-B0xx |

2. `### Реестр — бэкенд (unit, functional)` и `### Реестр — фронтенд (vitest)` — **машинно читаемые** таблицы, строго с
   такими колонками:

   | № | Набор | ID | Тест | Кат. | Действие | Причина | Замена / ссылка |
   |---|---|---|---|---|---|---|---|
   | R36-B001 | functional | ADDR-012 | `CompanyAddressTests.Put_Address_Geocodes_…` | А | удалён | проверяет ответ геокодера, удалённого в цикле 19 | функция удалена в цикле 19, `ARCHITECTURE_CYCLE19.md` §388 |
   | R36-B002 | functional | CO-041 | `CompaniesTests.…` | Б | удалён | тот же маршрут, роль, данные и утверждение, что CO-017 | CO-017 |
   | R36-B003 | functional | CY26-07 | `Cycle26CompanyCardTests.…` | В | перенесён в юнит | матрица чистого правила | `ServiceBooking.UnitTests.CompanyCardRulesTests.…` |
   | R36-F001 | vitest | — | `src/pages/ProfilePage.test.tsx::… > …` | Б | удалён | повторяет тест общего компонента | `src/components/push/DevicesAndNotificationsSection.test.tsx::… > …` |

   Правила разбора (`diff_inventory.py`):
   - строка реестра — это строка таблицы, у которой первая ячейка соответствует `^R36-[BF]\d{3}$`. Нумерация сквозная внутри
     подраздела, номера не переиспользуются;
   - `Набор` ∈ {`unit`, `functional`, `vitest`};
   - `ID` — `TestCase`-ID или ID из имени `it`; `—`, если ID нет. Выведенные из обращения ID повторно не используются (Q-36-6);
   - `Тест` — ключ инвентаря (§36.26) или его **суффикс**, однозначно определяющий тест. Обратные кавычки допускаются и
     отбрасываются;
   - `Кат.` ∈ {`А`, `Б`, `В`, `Г`, `Д`, `Е`};
   - `Действие` ∈ {`удалён`, `перенесён в юнит`, `объединён`, `ускорен`, `разделён`, `переименован`, `флейк-долг`,
     `добавлен (замена)`, `изменение продуктового кода`};
   - `Замена / ссылка`:
     - для `удалён` (Б), `перенесён в юнит`, `объединён` — ключи или ID тестов-замен через `; `;
     - для `удалён` (А) — текст «функция удалена в цикле N, <коммит или §>»;
     - для `объединён` в защищённой зоне — плюс перечень утверждений, которые перенесены;
   - `удалён` допустим только при `Кат.` ∈ {`А`, `Б`}.
3. `### Переименования классов` — для разбиения (§36.7.4) и переименований:

   | Было | Стало | Задача |
   |---|---|---|
   | `CompaniesTests` | `CompaniesTests+Members`, `CompaniesTests+Profile`, `CompaniesTests+PublicListing` | BE-36-06 |

   `diff_inventory.py` сопоставляет .NET-тесты по `TestCase`-ID автоматически. Эта таблица — для людей и для обновления
   фильтров в документах.
4. `### Долг цикла 36` — флейки (Д) и дыры покрытия («никогда не покрыто» из `route_coverage.py`), с ID `C36-n` и причиной.
5. Строки новых guard-тестов: `CY36-01` (бэкенд, `TestAreaCoverageTests`), `CY36-02` (vitest, `testAreas.guard.test.ts`).

Реестр показывается заказчику до мерджа в `develop` (блок заказчика, п. 3).
