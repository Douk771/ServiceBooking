# ARCHITECTURE — цикл 36 ServiceBooking: ревизия регрессионных тестов и ускорение полного прогона

**Разделы §36.0–§36.17.** Контракт — `API_CONTRACT_CYCLE36.md` §36.20–§36.29. Машиночитаемые схемы цикла лежат в
`contracts/cycle36/` (§36.12). Нумерация § идёт по номеру цикла, как в циклах 29–33. В коде и документах ссылаться с
именем файла: `ARCHITECTURE_CYCLE36.md §36.7.1`.

**На входе:**
- корневой `SPEC.md` цикла 36. Допущения блока «Что стоит проверить с заказчиком» заказчик принял как есть;
- `CURRENT_STATE.md` на `2834e00` (§1, §2, §6.4, §7), `docs/testing-isolation.md`, `TEST_CATALOG.md`;
- код ветки `cycle/036-test-suite-audit` (= `develop` `a1e2259`), сверен по файлам, названным ниже. Ничего не запускалось:
  все числа в §36.2 получены статическим разбором кода, а не замером.

Ветку подготовил devops-инженер, архитектор её не трогает. Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` — документы
цикла 3. По конвенции (`CURRENT_STATE.md` §6.5, §10.5) они не перезаписываются: документы цикла лежат в корне с суффиксом.
Поэтому архивировать в этом цикле нечего: файлы `*_CYCLE36.md` новые, прежние версии не заменяются.

| Файл | Что в нём | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE36.md` (этот) | диагноз, решения, рычаги, решающая таблица после замера, задачи, порядок, риски | все |
| `API_CONTRACT_CYCLE36.md` | HTTP API не меняется, и как это проверить; файловые и CLI-контракты между исполнителями | backend, frontend, devops, QA |
| `contracts/cycle36/*.schema.json`, `test-areas.json` | **источник истины по форме** файлов, которыми обмениваются исполнители (JSON Schema draft-07, проверка голым `ajv-cli`) | все, CI |

---

## §36.0. Что это за цикл для архитектуры

Цикл про **механику тестов**, а не про продукт. Продуктовый код, API, БД и миграции не меняются (§36.1, `API_CONTRACT_CYCLE36.md` §36.20).
Меняются четыре вещи:

1. **Появляется замер** — одна команда `tools/test-timing/measure.sh`. Она снимает время каждой части регресса, топ классов и
   тестов, число и цену стартов хоста, подготовку баз, простой потоков, фазы vitest (§36.4). Для этого тестовая
   инфраструктура пишет события в `TestResults/sb-test-metrics-<runKey>.jsonl` (§36.5).
2. **Ревизия набора**: категории А–Е по всем файлам, удаление только А и Б, реестр в `TEST_CATALOG.md`. Автоматическая сверка
   «после = до − реестр» по инвентарям и по покрытию маршрутов (§36.9).
3. **Ускорение** по заранее заданной решающей таблице (§36.6). Каждый рычаг включается только при выполнении своего условия
   на замере, подтверждается повторным замером и откатывается, если выигрыша нет.
4. **Быстрый контур** по областям (`[Trait("Area", …)]` на бэкенде, префиксы путей на фронте) и **CI** с одним числом
   параллелизма, отчётом о медленных тестах и предупреждением о росте времени (§36.10, §36.11).

Следствия:
- **Стек не пересматривается.** Новых NuGet- и npm-пакетов нет, новых фреймворков нет. Инструменты замера и ревизии пишутся
  на `python3` (только стандартная библиотека) и `bash`, как `tools/bench/cycle22`, `deploy/ci/check-drift-probe.py` (§36.1).
- **Изоляция «база на класс» остаётся.** Все рычаги работают внутри неё. Переход на «транзакцию на тест» или общую базу вне цикла
  (SPEC §3). Если цель без этого недостижима, это стоп-точка с отчётом заказчику (§36.6.3).
- **Три исполнителя плюс QA работают параллельно** по разным файлам (§36.12.2). Задачи devops-инженера выделены в отдельный
  трек `DO-36-xx` и сверяются с `git log` до мерджа (§36.13.4, SPEC T-36-04).

---

## §36.1. Стек и что добавляется

| Что | Решение | Почему |
|---|---|---|
| Тест-фреймворки | xUnit 2.5.3, Vitest 3.2, Testcontainers 3.10 — без изменений | SPEC §3: смена фреймворков вне цикла |
| Разметка областей бэкенда | штатный `[Trait("Area", "<id>")]` xUnit на **классе** | работает в `dotnet test --filter "Area=orders"`, в Rider (группировка по трейтам) и в TRX; не нужен свой `ITraitDiscoverer` |
| Разметка областей фронта | префиксы путей в `contracts/cycle36/test-areas.json`, скрипт `frontend/scripts/test-area.mjs` | у vitest нет трейтов; `startsWith` не требует glob-библиотеки. `picomatch` в `package.json` не объявлен, а тянуть его транзитивно нельзя — это урок C31-9 |
| Замер и анализ | `tools/test-timing/measure.sh` (bash) + `tools/test-timing/analyze.py` (python3, stdlib: `xml.etree`, `json`, `statistics`) | TRX (`--logger trx`) даёт время старта и конца каждого теста. Из него считаются классы, простой и хвост. Python 3 есть и на macOS, и на раннере GitHub |
| Инвентарь и сверки | `tools/test-audit/{inventory,diff_inventory,route_coverage}.py` | источник — `dotnet test --list-tests`, `npx vitest list --json` и разбор исходников. Старые таблицы `TEST_CATALOG.md` как источник не годятся (SPEC О-35-6) |
| Проверка JSON по схемам | `npx --yes ajv-cli@5 validate -s … -d …` вручную (QA) | как в цикле 30. В CI не добавляется: отчёты пишет один скрипт, и форма держится им |
| Продуктовый код | **не меняется**. Точки подмены по Q-36-3 разрешены, но ни одна не запланирована (§36.7.5) | все рычаги §36.6 работают в тестовых фабриках и фикстурах |

Новые переменные окружения тестов. Все необязательные, Rider работает без них (SPEC §6):

| Переменная | По умолчанию | Смысл |
|---|---|---|
| `SERVICEBOOKING_TEST_METRICS` | включено | `0` отключает запись `sb-test-metrics-*.jsonl` |
| `SERVICEBOOKING_TEST_ROUTE_LOG` | выключено | `1` пишет `sb-test-routes-*.jsonl` (§36.9.5). Нужна только для сверки покрытия маршрутов |
| `SERVICEBOOKING_TEST_FAST_HASH` | включено | `0` возвращает штатные 100 000 итераций PBKDF2 в тестовых хостах. Нужна для замера A/B и для диагностики (§36.7.3) |

---

## §36.2. Диагноз по коду (до замера) — гипотезы О-35-1

По коду ниже — **гипотезы**. Каждая проверяется конкретной метрикой замера (§36.4) и включает свой рычаг только при
выполнении условия (§36.6).

| # | Гипотеза | Что видно в коде на `a1e2259` | Метрика, которая подтвердит |
|---|---|---|---|
| **H1** | **Хост поднимается на каждый тест**, а не на класс | ≈120 мест создания хоста внутри тестовых методов в 36 файлах (`new …Factory(ConnectionString)`, `WithWebHostBuilder`). Самые массовые: `PhoneVerificationTests` 18, `CompanyAddressTests` 17, `StaffPushTests` 10, `Cycle28DemoScenarioTests` 10, `Cycle24NotificationsTests` 6, `Cycle25CatalogTests` 6, `RateLimitingTests` 6, `NotificationDispatchExtraTests` 7. `NotificationTestBase` создаёт `NotificationTestFactory` **в конструкторе** — это старт на каждый тест в трёх классах, 39 тестов. `LegalPricingGateTests` и `LegalConsentVersionChangeTests` поднимают хост в `IAsyncLifetime.InitializeAsync` уровня экземпляра — тоже на каждый тест, 14 стартов. Каждый старт — это `Program.cs`: `MigrateAsync` по 79 миграциям, роли и SuperAdmin, копия правового манифеста, DI | `functional.hostBoots.count` сильно больше `functional.classes`, `medianMs` |
| **H1b** | **Хост фикстуры поднимается впустую** | `TestDatabaseFixture.InitializeAsync` всегда стартует `CustomWebApplicationFactory` (`_ = Factory.Services`). Этот хост не используют классы на `NotificationTestBase` (3), `LegalPricingGateTests`, `LegalConsentVersionChangeTests`, `Cycle18TrialMailingWindowHookTests` и другие, которым нужна только строка подключения | `hostBoots.byFactory[CustomWebApplicationFactory].count` > числа классов на `ApiTestBase` |
| **H2** | **PBKDF2 на каждой регистрации и входе** | В коде нет ни `PasswordHasherOptions`, ни `IterationCount`: Identity хеширует по умолчанию (PBKDF2-HMACSHA512, 100 000 итераций). Около 1 350 вызовов `RegisterAsync`/`LoginAsync`/`CreateOwnerWithCompanyAsync`/`AddMasterAsync`/`LoginAsSuperAdminAsync` в 78 файлах, а `CreateOwnerWithCompanyAsync` и `AddMasterAsync` — это регистрация плюс вход, два прогона PBKDF2. Больше всего в `BookingsFlowSmokeTests` (174 вызова), `CompaniesTests` (150), `AdminTests` (88) | A/B-замер `SERVICEBOOKING_TEST_FAST_HASH=0/1` (§36.7.3) |
| **H3** | **Критический путь — длинные классы** | Внутри класса тесты идут последовательно. Самые крупные классы: `CompaniesTests` 83 атрибута, `BookingsFlowSmokeTests` 63, `AdminTests` 51, `Cycle18TrialPlanTests` 35, `PricingTests` 33. Документ цикла 8 уже называл `CompaniesTests` нижней границей | `scheduling.longestClassSeconds`, `tailSeconds`, `threadIdleFraction` |
| **H4** | **Реальные ожидания** | `LegalPricingGateTests`: 7 × `Task.Delay(2500)` — 17,5 с на класс плюс хост на тест. Это остаток TD-02 цикла 16, причина описана в шапке файла. Остальные задержки ≤ 500 мс (`NotificationWebhookUnsubscribeTests` 500, `Cycle25CustomerTests` 300, `SchedulerTests` 300, `NotificationDispatch*` 200 в цикле ожидания) | `topTests` |
| **H5** | **Упор в ресурсы colima** | Postgres в контейнере уже без долговечности (`TestInfrastructure.cs:49-52`: `fsync=off`, `full_page_writes=off`, `synchronous_commit=off`). Процесс `dotnet` идёт на macOS, Postgres — в виртуальной машине colima (по умолчанию у colima 2 CPU и 2 ГБ) | `postgresCpu.maxPercent` ≈ `100 × cpuLimit` |
| **H6** | **EF строит модель на каждый хост** | `AddDbContext` без общего внутреннего провайдера (`ApplicationServicesExtensions.cs:18`). Каждый новый хост может получать свой внутренний сервис-провайдер EF и строить модель заново (62 `DbSet`) | `efManyServiceProvidersWarning`, `hostBoots.medianMs` ≥ 1000 |

**Предварительный вывод, который замер должен подтвердить или опровергнуть.** Рост в 10+ раз при вдвое большем числе
тестов проще всего объяснить **H1 + H2**. Начиная с цикла 13 набор чаще поднимает хост на тест, и каждый такой тест
регистрирует пользователей с дорогим хешем. Длина классов (H3) даёт нижнюю границу, когда H1/H2 сняты. Поэтому порядок
рычагов в §36.6 такой: сначала H1/H2 (дёшево, без изменения проверок), потом H3 (меняет структуру классов), потом
параллелизм.

**Расхождения SPEC с кодом (учтены в плане):**
1. `SERVICEBOOKING_TEST_SEEDED_TEMPLATE` **в коде нет**. Переменная есть только в `docs/testing-isolation.md` и документах
   цикла 8. Готового рычага «засеянный шаблон» нет. Строку в таблице переменных `docs/testing-isolation.md` исправляет BE-36-11
   («не реализована»). Сам рычаг — только по условию §36.6, L8.
2. Строка `ADDR-` в `TEST_CATALOG.md` ссылается на `AddressVerificationTests.cs` (28 тестов). Файл переименован в цикле 19 в
   `CompanyAddressTests.cs`, в нём 18 атрибутов.
3. Тестов удалённых компонентов фронта (`CompanyAddressField`, `DevicesPage`, `MyDevicesCard`) в репозитории **уже нет**. Кандидаты
   А на фронте — только дубли и тесты переписанных сценариев (§36.9).
4. Postgres в CI (сервис `postgres:16`) работает **с** `fsync` и остальной долговечностью, в отличие от локального контейнера.
   Это отдельный рычаг CI (L9, DO-36-03).
5. `max_connections` в CI — стандартные 100, то есть безопасно 90. По формуле `P × 2 × 8 + 4` это ограничивает P в CI до 5 (§36.7.6).

---

## §36.3. Инварианты качества и как каждый проверяется

| Инвариант (SPEC §6) | Механизм проверки | Кто и когда |
|---|---|---|
| Набор проверок уменьшается только на записи реестра | `diff_inventory.py` сравнивает инвентари до и после с реестром `TEST_CATALOG.md` (§36.9.4), код выхода 0 | QA-36-02, перед мерджем |
| Ни один маршрут, покрытый до цикла, не остался без покрытия | `route_coverage.py` сравнивает журналы вызовов маршрутов до и после с эталоном `Cycle22RouteTable.golden.txt` (§36.9.5) | QA-36-02 |
| Защищённые зоны не удалены | `inventory.py` помечает `protectedZone` (§36.9.3); `diff_inventory.py` падает, если удалён защищённый тест с действием `удалён` | QA-36-02 |
| База на класс, клон из шаблона | `TestDatabaseFixture` остаётся `IClassFixture`. Новые хосты класса (§36.7.1) живут на той же базе класса. Ревью: grep по `ICollectionFixture` и `CollectionDefinition` не должен давать новых совпадений | QA-36-03 |
| Случайный порядок, семя печатается | `RandomTestCaseOrderer` не трогается; 5 прогонов с разными семенами (US-36-04) | QA-36-03 |
| Ryuk не отключается, `EnsureDeletedAsync` не появляется | `grep -rn "EnsureDeleted\|RYUK_DISABLED" ServiceBooking.Tests ServiceBooking.TestKit` — без новых совпадений; `TestKit doctor` зелёный | QA-36-03 |
| Удаляются только `sbtest_*` | `TestDatabaseNaming` не трогается | ревью |
| Два прогона из двух рабочих копий не мешают друг другу | так как меняется устройство фикстур (§36.7.1–§36.7.2): один одновременный полный прогон из двух worktree, оба зелёные | QA-36-03 |
| Флейков не больше | 5 прогонов бэкенда (один с `-- xUnit.MaxParallelThreads=1`), 3 прогона vitest с пустым и заполненным `VITE_SMARTCAPTCHA_SITEKEY` | QA-36-03 |
| Rider «Run» работает без новых обязательных переменных | новые переменные необязательные (§36.1); хосты класса и ленивая фикстура не требуют настройки | QA-36-03, ручная проверка M36-01 |
| Прод не меняется | `git diff a1e2259..HEAD -- ServiceBooking.API ServiceBooking.Core ServiceBooking.Infrastructure` пуст. Если по §36.7.5 добавлена точка подмены, в диффе только она, и она описана в реестре | QA-36-02 |
| Тесты не ходят наружу и не пишут вне временных каталогов | новые файлы пишутся только в `TestResults/` (в `.gitignore`) и `$TMPDIR/sb-test/…` | ревью |

---

## §36.4. Замер (US-36-01, T-36-01)

### §36.4.1 Команда

```bash
tools/test-timing/measure.sh --label <label> [--runs 3] [--suites unit,functional,vitest] \
    [--parallel N] [--seed S] [--route-log] [--concurrent] [--with-build] [--force]
```

Полный контракт CLI, коды выхода и раскладка файлов — `API_CONTRACT_CYCLE36.md` §36.28. Суть:

1. **Окно замера.** Скрипт берёт машинную блокировку `mkdir "${TMPDIR:-/tmp}/sb-test-timing.lock"`: второй замер не
   стартует. Затем проверяет `dotnet run --project ServiceBooking.TestKit -- status --json`: если есть живые **чужие** тестовые
   ресурсы (прогон из другой рабочей копии или агента), выходит с кодом 2. `--force` разрешает запуск, но ставит в отчёте
   `foreignRunsDetected: true`, и такой отчёт для сравнения до/после непригоден (SPEC Р-35-1, Р-35-2).
2. **Окружение** пишется в `env.json` и попадает в `environment`: `hostname`, `sw_vers`/`uname`, `sysctl hw.ncpu`,
   `hw.memsize`, `colima list --json` (CPU и память ВМ), режим (`container`/`external` по
   `SERVICEBOOKING_TEST_CONNECTION`), P, `--no-build`/сборка, версии SDK и Node.
3. **Сборка один раз** (`dotnet build ServiceBooking.sln -c Debug`) до замеров, в время не входит. Дальше каждый прогон идёт с
   `--no-build`. Режим `--with-build` нужен, только чтобы один раз показать цену сборки на каждом `dotnet test` (SPEC §5,
   «сборка»).
4. **Прогоны:** по `--runs` раз каждый набор, подряд, без перекрытия (кроме эксперимента `--concurrent`, L11):
   - юнит: `dotnet test ServiceBooking.UnitTests --no-build --logger "trx;LogFileName=unit-<i>.trx" --results-directory <dir>`;
   - функциональные: то же для `ServiceBooking.Tests` с `-- xUnit.MaxParallelThreads=<P>` и
     `SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS=<P>` (одно число, §36.11). Параллельно каждые 5 с снимается `docker stats` по
     контейнеру прогона (метку контейнера брать из `TestServerLease`). После прогона `sb-test-metrics-<runKey>.jsonl`
     копируется в каталог замера;
   - vitest: `cd frontend && VITE_SMARTCAPTCHA_SITEKEY= npx vitest run --reporter=default --reporter=json --outputFile.json=<dir>/vitest-<i>.json`,
     stdout сохраняется (строка `Duration … (transform …, setup …, collect …, tests …, environment …, prepare …)`).
5. **Анализ:** `python3 tools/test-timing/analyze.py <dir> --out <dir>/report.json --md <dir>/report.md`. Отчёт соответствует
   `contracts/cycle36/timing-report.schema.json`.

Для повседневного полного регресса одной командой та же команда: `measure.sh --runs 1 --label adhoc`.

### §36.4.2 Что считает `analyze.py`

| Метрика | Как |
|---|---|
| `medianWallSeconds` набора | медиана wall-времени команды `dotnet test`/`vitest run` по прогонам |
| `topClasses` (топ-20), `topTests` (топ-20) | из TRX медианного прогона: `UnitTestResult/@duration`, класс из `TestDefinitions/UnitTest/TestMethod/@className`. Для класса ещё `wallSeconds` = max(`endTime`) − min(`startTime`) |
| `hostBoots` | события `host-booted` из metrics: число, сумма, медиана, p95, разрез по `factoryType`/`factoryTag`, топ классов по числу стартов (слот → класс по `class-recorded`) |
| `databases` | `server-ready`, `template-ready`, суммы `class-db-created`/`class-db-dropped` |
| `scheduling.threadIdleFraction` | 1 − Σ(wall классов) / (P × wall прогона) |
| `scheduling.tailSeconds` | сколько секунд до конца прогона число активных классов было меньше P (по интервалам классов) |
| `postgresCpu` | максимум и среднее `CPUPerc` из `docker stats` и лимит CPU (colima) |
| `efManyServiceProvidersWarning` | grep `ManyServiceProvidersCreatedWarning` в stdout прогона |
| vitest `phases` | разбор строки `Duration` медианного прогона |
| vitest `topFiles` | JSON-репортёр: `endTime − startTime` файла, сумма `duration` тестов, разница — накладные. `environment` — по docblock `@vitest-environment` в файле, иначе `jsdom` |

### §36.4.3 Какие замеры снимаются и когда

| Метка | Когда | Коммит | Что | Кто |
|---|---|---|---|---|
| `b0-baseline` | до любых изменений | `a1e2259` (чистый) | 3 прогона каждого набора; TRX, vitest JSON, окружение. Метрик хоста ещё нет | QA-36-01 |
| `b1-instrumented` | сразу после BE-36-01 | коммит инструментирования | 3 прогона функционального набора. Итог сравнивается с `b0`: расхождение медианы больше 5 % — инструментирование дорогое и чинится до продолжения. Даёт `hostBoots`, `databases` | QA-36-01 |
| `b1-routes` | там же | там же | 1 прогон с `--route-log` → журнал маршрутов «до» (§36.9.5); по времени не сравнивается | QA-36-01 |
| `l<N>-<рычаг>` | после каждого рычага | коммит рычага | 1 прогон функционального набора или vitest. Решение «оставить или откатить» по §36.6.2 | исполнитель рычага |
| `final` | в конце | HEAD ветки | 3 прогона всех наборов, то же окружение, что `b0` | QA-36-03 |
| `ci-baseline`, `ci-final` | до и после | последние 3 зелёных прогона `develop` и ветки | время шагов CI из `gh run view --json jobs` | DO-36-02 |

`b0-baseline` и `final` коммитятся в `tools/test-timing/results/{b0-baseline,final}.{json,md}`, как
`tools/bench/cycle22/results`. Остальные отчёты остаются в `TestResults/` (не в git), их числа попадают в отчёты исполнителей.

### §36.4.4 Гейт G1 — «главное время не там»

После `b0` QA проверяет долю функционального набора в сумме трёх наборов:
- **≥ 50 %** — план идёт как есть;
- **< 50 %**, или полный регресс локально ≤ 10 минут (то есть жалоба заказчика не воспроизводится) — **стоп-точка перед
  рычагами**. QA пишет в отчёт числа и что именно доминирует, оркестратор показывает это заказчику. Ревизия (US-36-02) может
  идти дальше, рычаги §36.6 ждут ответа заказчика. Исполнители сами порядок рычагов не меняют.

---

## §36.5. Инструментирование тестовой инфраструктуры (BE-36-01)

Только `ServiceBooking.Tests/Infrastructure/`, продуктовый код не меняется. Формат строк — `contracts/cycle36/test-metrics.schema.json`
и `route-hits.schema.json`.

- **`TestRunMetrics`** (новый статический класс): потокобезопасная запись строк JSONL в
  `<WorkingCopyRoot>/TestResults/sb-test-metrics-<runKey>.jsonl`. Ключ прогона в имени нужен, чтобы процессы не писали в
  один файл. Лучшим усилием: `IOException` глотается, как в `TestRunEnvironment.PrintBanner`. `SERVICEBOOKING_TEST_METRICS=0`
  отключает запись.
- **Точки записи:**
  - `TestRunEnvironment.EnsureEnvironmentAsync` → `run-start`, `server-ready` (вокруг `TestServerLease.AcquireAsync`),
    `template-ready` (вокруг `EnsureTemplateAsync`);
  - `LeaseClassDatabaseAsync` → `class-db-created`; `ReleaseClassDatabaseAsync` → `class-db-dropped`;
  - `TestDatabaseFixture.RecordTestClass` → `class-recorded`, один раз на класс, с полным именем класса. Для вложенных
    классов (§36.7.4) имя с `+`. Туда же передать то же имя, что уходит в `COMMENT ON DATABASE`;
  - **`host-booted`**: `TestHostSettings.Apply` запоминает `Stopwatch` и через
    `builder.ConfigureTestServices(...)` регистрирует маленький `IHostedService`. Он подписывается на
    `IHostApplicationLifetime.ApplicationStarted` и пишет `ms` от `Apply` до старта. `factoryType` — это
    `builder`-владелец, передаётся новым необязательным параметром `Apply(..., factoryType: GetType().Name)` из каждой
    фабрики. Параметр необязательный, чтобы не ломать вызовы: без него пишется `"unknown"`.
- **Журнал маршрутов** (только при `SERVICEBOOKING_TEST_ROUTE_LOG=1`): `TestHostSettings.Apply` через `ConfigureTestServices`
  добавляет `IStartupFilter` с middleware после маршрутизации. Оно берёт `HttpContext.GetEndpoint() as RouteEndpoint`, и если
  endpoint есть, пишет `{method, route: RoutePattern.RawText, status, slot}` один раз на уникальную четвёрку в
  `sb-test-routes-<runKey>.jsonl`. `IStartupFilter` из тестового хоста не меняет конвейер продукта: он добавляет только
  наблюдающий middleware и не трогает ответ.
- **Проверка BE-36-01:** числа тестов и их зелёность не меняются. `b1-instrumented` отличается от `b0` по медиане не больше
  чем на 5 % (§36.4.3).

---

## §36.6. Решающая таблица после замера (гейт G2)

Разбивка задач уже учитывает все ветвления: повторно архитектор не нужен. Исполнитель применяет рычаг, **только если
выполнено его условие** на последнем замере (`b1` для первых, `l<N>` для следующих), и записывает в отчёт числа «до/после».

### §36.6.1 Рычаги и условия

Обозначения из отчёта: `W` — медиана wall функционального набора, `P` — параллелизм, `W*` — цель = min(0,5 × `W_b0`, 300 с).

| # | Рычаг | Задача | Условие применения | Ожидаемый эффект | Риск и защита |
|---|---|---|---|---|---|
| **L1** | Хосты уровня класса вместо хоста на тест (§36.7.1) | BE-36-04 | `hostBoots.count` ≥ 1,5 × `classes` **и** `medianMs` ≥ 300 | минус (лишние старты × `medianMs`) / P | утечка состояния хоста между тестами класса. Правило допуска §36.7.1, прогон с 3 семенами |
| **L2** | Ленивый хост фикстуры (§36.7.2) | BE-36-04 | всегда вместе с L1 (тот же файл, риск низкий) | минус холостые старты `CustomWebApplicationFactory` | порядок посева SuperAdmin меняется. Покрывается полным прогоном |
| **L3** | Быстрый хеш паролей в тестовых хостах (§36.7.3) | BE-36-05 | всегда пробуется: A/B `SERVICEBOOKING_TEST_FAST_HASH=0/1`, по одному прогону | по H2: десятки секунд wall и больше | тесты хеширования: их нет (grep `PasswordHasher` пуст). Прод не затрагивается |
| **L4** | Разбиение длинных классов (§36.7.4) | BE-36-06 | после L1–L3: класс с `wallSeconds` > `W*`/3, **или** `threadIdleFraction` > 0,2 | нижняя граница wall = самый длинный класс, то есть до `W*`/4 | меняются имена классов и слоты. Вложенные классы сохраняют старые фильтры (О-35-4) |
| **L5** | Реальные ожидания → детерминированные (§36.7.5) | BE-36-07 | тест из `topTests` (топ-20) с фиксированной задержкой ≥ 2 с в сумме | для `LegalPricingGateTests` около 17 с + старты | TD-02: замена `Task.Delay` на `ReloadLegalNow` вскрывает расхождение посева. Чинить посев, а не тест |
| **L6** | P локально (§36.7.6) | BE-36-10 | после L1–L5: `postgresCpu.maxPercent` < 70 % от `100 × cpuLimit` **и** `threadIdleFraction` < 0,2 | до ≈ P_new/P_old на CPU-связанной части | таймауты Npgsql на colima (О-35-3). Бюджет соединений, 5 прогонов |
| **L7** | Порядок классов «самые длинные первыми» | BE-36-10 | после L4 и L6: `tailSeconds` > 0,15 × `W` | минус хвост | файл длительностей устаревает. Это только подсказка порядка: на корректность не влияет |
| **L8** | Засеянный шаблон (роли и SuperAdmin в шаблоне) | BE-36-11 | после L1–L3: `hostBoots.medianMs` ≥ 1000 **и** это не H6 (`efManyServiceProvidersWarning` = false) | минус посев при каждом старте | тесты считают пользователей и суперадминов. Только если остальное не дало цель |
| **L9** | Postgres CI без долговечности | DO-36-03 | всегда (только CI) | заметно на записи; в CI сейчас `fsync=on` | ничего продуктового; CI-база одноразовая |
| **L10** | vitest: окружение `node` для файлов без DOM (§36.8) | FE-36-02 | `phases.environmentSeconds` ≥ 0,3 × Σ фаз | минус jsdom на ≈90 файлах | файл тайно использует `window`/`localStorage` — тогда он остаётся на jsdom |
| **L11** | vitest: пул `threads` вместо `forks` | FE-36-02 | всегда пробуется; оставить, если ≥ 10 % быстрее и 3 прогона зелёные | 10–30 % | флейки таймеров и `fetch`-подмен. 3 прогона |
| **L12** | Перекрытие наборов в регрессе (`measure.sh --concurrent`: vitest одновременно с функциональными) | DO-36-01 | эксперимент после L1–L6: оставить режимом по умолчанию в `measure.sh`, если сумма ≥ 15 % меньше и 2 прогона зелёные | до min(сумма, max) | CPU-конкуренция → таймауты. Флаг остаётся опциональным |
| **L13** | `dotnet test ServiceBooking.sln` одной командой (юнит и функциональные параллельно) | DO-36-01 | эксперимент, как L12 | ≈ время юнит-набора | два ключа прогона в логе (известно, безвредно) |

**Рычаги, которые не применяются** (и почему) — §36.7.7.

### §36.6.2 Правило «оставить или откатить»

- Каждый рычаг — **отдельный коммит** (`perf(tests): L1 class-scoped hosts (BE-36-04)`), чтобы его можно было откатить `git revert`.
- Оставить, если выигрыш на `l<N>` ≥ max(3 % `W`, 10 с) и прогон зелёный. Иначе откатить и записать в отчёт «не дал эффекта»:
  лишняя сложность без выигрыша — это вред.
- Когда достигнута цель `W` ≤ 0,85 × `W*` (запас 15 % на шум), следующие по таблице P1/P2-рычаги **не обязательны**.
  L9 (CI) и L10/L11 (vitest) при этом делаются: у них свои цели.

### §36.6.3 Стоп-точки

- **G1** (§36.4.4) — главное время не в функциональном наборе.
- **G3** — все применимые рычаги применены, а `W` > `W*`. QA записывает числа и оставшуюся нижнюю границу (самый длинный
  класс, простой, CPU Postgres), оркестратор показывает это заказчику. Дальше возможны только решения вне цикла: другая модель
  изоляции (SPEC §3), ресурсы colima (блок заказчика, п. 5), состав CI (п. 4). Сами исполнители их не принимают.
- **G4** — замер не подтвердил ни одной гипотезы H1–H6 (ни одно условие L1–L8 не выполнено). Порядок тот же, что G3.

---

## §36.7. Рычаги бэкенда

### §36.7.1 Хосты уровня класса (L1)

**API фикстуры** (`TestDatabaseFixture`, BE-36-04):

```csharp
/// Один хост на КЛАСС для данного ключа: создаётся при первом вызове, стартует сразу,
/// живёт до конца класса, освобождается в DisposeAsync фикстуры (до удаления базы).
public TFactory ClassHost<TFactory>(string key, Func<string /*connectionString*/, TFactory> create)
    where TFactory : WebApplicationFactory<Program>;
```

- `key` — стабильная строка `"<тип>[:<конфигурация>]"`, уникальная внутри класса: `"push"`, `"phv"`, `"addr"`, `"addr:permit=3"`.
  Разные настройки — разные ключи, то есть разные хосты.
- Хранение — словарь в фикстуре. Освобождение в `DisposeAsync` в обратном порядке создания, **до** `_lease.DropAsync()`.
  Ошибка остановки хоста ловится и печатается, как ошибка удаления базы в `TestClassDatabaseLease`, и не мешает удалить базу.
- Тесты внутри класса xUnit гоняет последовательно, так что гонок за хост нет. Блокировка в `ClassHost` всё равно ставится:
  она дешёвая.
- В тесте было `await using var push = new PushEnabledFactory(ConnectionString);`, стало
  `var push = Fixture.ClassHost("push", cs => new PushEnabledFactory(cs));`, без `using`.
- `NotificationTestBase`: `Factory = fixture.ClassHost("ntf", cs => new NotificationTestFactory(cs));`. Его `DisposeAsync`
  больше не освобождает хост (владелец — фикстура).

**Правило допуска (О-35-2).** Тест можно перевести на хост класса, только если выполнены **все** пункты. Если нет — тест
остаётся на своём хосте, и это тоже нормальный итог:

1. **Конфигурация хоста не меняется во время теста.** Тест не переписывает манифест в `Legal:Root`, не зовёт `ReloadLegalNow`,
   не меняет `IOptionsMonitor`-источники. Иначе следующий тест увидит чужое состояние: хост на тест раньше заново копировал
   манифест в `TestHostSettings.Apply`.
2. **Записывающие фейки** (клиент MAX, `IWebPushSender`, записыватели уведомлений): утверждения фильтруют записи по своим
   уникальным данным (телефон, endpoint подписки, id компании или заказа) **или** тест в начале зовёт `Reset()` фейка
   (добавить `Reset()`, если его нет). Утверждения вида «ровно N отправок всего» без фильтра нельзя.
3. **Rate limit.** Тесты, которые проверяют порог 429, остаются на своём хосте (`RateLimitingTests`, `Cycle31GalleryRateLimitTests`,
   `NTF-W005`, тест `CompanyAddressTests` с `permitLimit: 3`). Если на общем хосте продовые лимиты, число запросов всего класса к
   ограниченному маршруту должно быть меньше лимита окна. Иначе хост остаётся на тест.
4. **Кеши с TTL** (каталог goods 30 с, `PricingCatalogCache`, снимок правовых документов): тест, который меняет закешированное и
   ждёт свежего чтения, либо работает на уникальных ключах (свой город, свой магазин), либо сбрасывает кеш явно.
5. **Подменённое время** (`INotificationClock` и т. п.): тест выставляет часы сам в начале, а не полагается на значение по
   умолчанию после старта хоста.
6. **Ручные «тики» диспетчеров** (`PushDispatchTestFactory(disableAutomaticTicking: true)`): тик обрабатывает всю очередь базы
   класса, включая хвосты прошлых тестов. Утверждения фильтруют свои записи, иначе хост остаётся на тест.

**Проверка после перевода каждого класса:** класс трижды с разными семенами
(`SERVICEBOOKING_TEST_ORDER_SEED=<s> dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~<Class>"`) и один раз с
`-- xUnit.MaxParallelThreads=1` в полном прогоне (US-36-04).

**Кандидаты и предварительное решение** (исполнитель подтверждает по правилу допуска и пишет итог в отчёт):

| Класс | Сейчас | Решение по умолчанию |
|---|---|---|
| `PhoneVerificationTests` | 18 × `PhoneVerificationEnabledFactory` на тест | → `ClassHost("phv")`. Проверить п. 2 (фейк бота MAX) и лимиты сессий на номер: номера уникальные. `BadPhoneVerificationProviderFactory` (хост, который не должен стартовать) — без изменений |
| `CompanyAddressTests` | 17 × `CompanyAddressTestFactory` на тест | **сначала ревизия А** (геокодер удалён в цикле 19, §36.9.1). Оставшиеся → `ClassHost("addr")`. Тест с `permitLimit: 3` и тест с `legacySettings` остаются на своих хостах |
| `StaffPushTests` | 6 × `PushEnabledFactory`, 4 × `PushDispatchTestFactory` | `PushEnabledFactory` → `ClassHost("push")`. `PushDispatchTestFactory` — только если выполнен п. 6 |
| `Cycle24NotificationsTests` | 6 × `PushEnabledFactory` | → `ClassHost("push")` |
| `Cycle25CatalogTests` | 6 × `CatalogTestFactory` | → `ClassHost("catalog")`, если п. 4 выполняется: у каждого теста свой город или магазин |
| `NotificationChannelsTests`, `NotificationWebhookUnsubscribeTests`, `Cycle22ChannelFundingTests` (`NotificationTestBase`) | хост в конструкторе базы — на каждый тест, 39 тестов | → `ClassHost("ntf")` в базе. `NTF-W005` оставляет свой `RateLimitTestFactory`. Проверить п. 4 для `SetChannelPriceAsync` (настройки платформы: кеш) |
| `Cycle23StrictModeAndDataTests` | 3 × `PhoneVerificationEnabledFactory`, 3 × `WithWebHostBuilder` с разными лимитами | первые → `ClassHost("phv")`. `WithWebHostBuilder` — у каждого своя конфигурация и один тест: без изменений |
| `Cycle18TrialLifecycleTests` | 5 созданий | по правилу допуска; класс в защищённой зоне (биллинг) — только ускорение |
| `LegalPricingGateTests`, `LegalConsentVersionChangeTests` | хост на тест через `IAsyncLifetime` | **остаются на тест** (п. 1: переписывают манифест). Ускорять только через L5, и только если класс в топ-10 |
| `RateLimitingTests`, `Cycle28DemoScenarioTests`, `NotificationDispatchExtraTests`, `NotificationTransportStartupTests` | разные конфигурации на тест, намеренно | без изменений |

### §36.7.2 Ленивый хост фикстуры (L2)

- `TestDatabaseFixture.Factory` становится ленивым (`Lazy<CustomWebApplicationFactory>`): создаётся и стартует при первом
  обращении. `Identity` — через `Factory.Identity`.
- `InitializeAsync` берёт только аренду базы и `TestData`. `DisposeAsync` останавливает хост, только если он создан.
- `RecordTestClass` сейчас читает `Identity.DatabaseName` и этим поднимает хост. Имя базы брать из аренды: добавить
  `TestClassDatabaseLease.DatabaseName`.
- Для `ApiTestBase` ничего не меняется: конструктор берёт `fixture.Factory` и поднимает хост, как раньше.
- Следствие: у классов, которым нужна только строка подключения, в базе не будет SuperAdmin тега `api`. Ни один такой класс
  этим SuperAdmin не пользуется: они логинятся суперадмином своего `factoryTag`. Полный прогон это подтверждает.

### §36.7.3 Быстрый хеш паролей в тестовых хостах (L3)

- В `TestHostSettings.Apply`, если `SERVICEBOOKING_TEST_FAST_HASH` ≠ `0`:
  `builder.ConfigureTestServices(s => s.Configure<PasswordHasherOptions>(o => o.IterationCount = 1));`.
- Продукт не меняется: `ServiceBooking.API` не знает про эту настройку, прод хеширует со штатными 100 000 итераций.
  Формат хеша Identity V3 хранит число итераций внутри, поэтому проверка пароля в тестах работает так же.
- Что при этом не проверяется и не проверялось раньше: стойкость хеша. Ни один функциональный тест её не утверждает, для этого
  нужен отдельный юнит-тест настроек, а он вне цикла (SPEC §3, «новое покрытие»).
- Решение по замеру: A/B одним прогоном `SERVICEBOOKING_TEST_FAST_HASH=0` и `=1` на одном коммите, выигрыш в отчёт.

### §36.7.4 Разбиение длинных классов (L4)

Делается, только если выполнено условие L4 (§36.6.1). Форма — **вложенные классы в partial-файлах**:

```csharp
// CompaniesTests.cs — внешний класс без тестов и без фикстуры, только общий базовый класс помощников
public partial class CompaniesTests
{
    public abstract class Base(TestDatabaseFixture fixture) : ApiTestBase(fixture) { /* бывшие private-помощники */ }
}

// CompaniesTests.Members.cs
public partial class CompaniesTests
{
    [Trait("Area", "companies")]
    public sealed class Members(TestDatabaseFixture fixture) : Base(fixture) { /* CO-0xx про сотрудников */ }
}
```

- Полное имя теста — `ServiceBooking.Tests.Tests.CompaniesTests+Members.<Method>`, поэтому старые фильтры
  `--filter "FullyQualifiedName~CompaniesTests"` в документах **продолжают работать** (О-35-4). Каждая часть — отдельный
  xUnit-класс со своей `IClassFixture`, то есть своей базой.
- Части делятся **по теме** (сотрудники, профиль и настройки, публичные выборки, права) и балансируются по длительности из
  TRX: каждая часть ≤ `W*`/4. Число частей k = ⌈`wallSeconds` / (`W*`/4)⌉.
- `TestCase`-ID, имена методов и тела тестов не меняются. Меняется только класс-владелец. `diff_inventory.py` сопоставляет по ID
  (§36.9.4). Разбиение записывается в реестр таблицей «Переименования классов» (§36.29 контракта).
- `ApiTestBase` передаёт в `RecordTestClass` имя с внешним классом (`CompaniesTests+Members`), а не просто `Members`.
- Кандидаты по коду: `CompaniesTests`, `BookingsFlowSmokeTests`, `AdminTests`, `Cycle18TrialPlanTests`, `PricingTests`. Какие
  действительно делить, решает замер.

### §36.7.5 Реальные ожидания (L5) и точки подмены (Q-36-3)

- Опрос с таймаутом (`WaitUntil`-подобные циклы в `Cycle28OutboundSuppressionTests`, `Task.WhenAny(…, Delay(90 s))` в
  `Cycle18TrialLifecycleTests`) — **нормально**: выходит сразу по событию. Не трогать.
- Фиксированные `Task.Delay` менять только у тестов из топ-20 (условие L5). Главный кандидат — `LegalPricingGateTests`
  (7 × 2,5 с). Причина, по которой TD-02 там не применён, записана в шапке файла: посеянный SuperAdmin несёт версию `TermsOwner`
  дефолтного манифеста. Исправление — в тестовом посеве или в самом тесте: после каждого `WriteManifest` + `ReloadLegalNow`
  явно принимать новую версию `TermsOwner` за SuperAdmin, как класс уже делает для `Privacy`/`TermsClient` в
  `AcceptCurrentLegalAsync`. Утверждения теста не меняются. Класс в защищённой зоне: это ускорение, а не удаление.
- **Продуктовый код.** Сейчас ни одна точка подмены не нужна. Если исполнитель находит реальное ожидание в продукте без
  подмены (пауза, задержка, часы), он добавляет её строго по образцу `IDispatchDelay`/`IPauseGenerator`/`INotificationClock`:
  - интерфейс в `ServiceBooking.API/Services/…`;
  - продовая реализация повторяет текущее поведение байт в байт;
  - регистрация в `ApplicationServicesExtensions`;
  - подмена — только в тестовой фабрике;
  - юнит-тест, что продовая реализация ведёт себя как раньше;
  - строка в реестре с пометкой «изменение продуктового кода по Q-36-3».
  
  Миграций и новых настроек нет.

### §36.7.6 Параллелизм (L6) и бюджет соединений

- Формула бюджета не меняется: `P × 2 × 8 + 4` ≤ 0,9 × `max_connections` (`EnvStatus.RequiredConnections`,
  `hostsPerClass = 2`). После L1 у класса может быть больше двух хостов (фикстура + несколько `ClassHost`). Пулы Npgsql
  с одинаковой строкой подключения общие на процесс, поэтому реальная нагрузка на соединения не растёт кратно. `hostsPerClass`
  оставить 2. Если в классе больше двух `ClassHost` с **разными** строками подключения, так делать нельзя (у всех хостов
  класса одна база).
- Локально контейнер даёт `max_connections=300`, то есть P ≤ 16 по бюджету. Реальный потолок задаёт CPU. Перебор
  P ∈ {4, 6, 8} по одному прогону → лучший с нулём падений → 3 прогона. Значение пишется в
  `ServiceBooking.Tests/xunit.runner.json`. Абзац «Поднимать параллелизм выше 6 смысла нет» в `docs/testing-isolation.md`
  обновляется по факту.
- Если `postgresCpu.maxPercent` упирается в `100 × cpuLimit` colima, P не поднимать. В отчёт — рекомендация заказчику
  (`colima stop && colima start --cpu <N> --memory <M>`), применяет её он сам (блок заказчика, п. 5).
- CI — §36.11.

**L7 (порядок классов).** Только по условию. Реализуется через
`[assembly: TestCollectionOrderer("ServiceBooking.Tests.Infrastructure.LongestFirstCollectionOrderer", "ServiceBooking.Tests")]`.
Оркестратор сортирует коллекции (класс = коллекция) по убыванию длительности из `ServiceBooking.Tests/test-durations.json`,
который генерирует `analyze.py --emit-durations`. Неизвестные классы идут в конец в исходном порядке. Случайный порядок
**внутри** класса не затрагивается.

### §36.7.7 Что не делается и почему

| Идея | Почему нет |
|---|---|
| Транзакция на тест, общая база, `Respawn` | другая модель изоляции (SPEC §3). Только по стоп-точке G3 с решением заказчика |
| `parallelizeAssembly: true` | в сборке одна коллекция на класс, а сборка одна. Сам по себе флаг ничего не даёт. Перекрытие двух проектов — это L13 |
| Скомпилированная модель EF (`UseModel`) против H6 | меняет продуктовый код и сборку, это не точка подмены по Q-36-3. Если H6 подтвердится, это вывод в отчёт и кандидат на следующий цикл. L1 и так резко снижает число стартов |
| Отключение `RandomTestCaseOrderer` или Ryuk ради скорости | инварианты SPEC §6 |
| Урезание CI или ночные прогоны | блок заказчика, п. 4 |
| `isolate: false` в vitest | общий модульный стейт (zustand, `vi.mock`) между файлами. Флейки дороже выигрыша |

---

## §36.8. Рычаги vitest (FE-36-02)

- **L10: окружение `node` по файлам.** Для `*.test.ts`, которые не трогают DOM, — докблок первой строкой
  `// @vitest-environment node`. Список кандидатов — около 90 файлов `.test.ts`: `src/utils/*`, `goods/src/utils/*`, `src/api/*`
  и др. Файл получает докблок, **только если зелёный в `node`**. Признаки, что оставлять на jsdom: `document`, `window`,
  `localStorage`, `navigator`, `render`, `renderHook`, `HTMLElement`, `Notification`, `self`. Выбран докблок, а не
  `test.projects`: изменение локальное и явное, CLI и `vitest.config.ts` не меняются, откат — удалить строку.
- **`src/test/setup.ts`** должен работать в `node`: `@testing-library/jest-dom/vitest` остаётся, а `cleanup` из
  `@testing-library/react` подключается только при `typeof document !== 'undefined'` через `await import(...)`. Так
  node-файлы не платят за импорт RTL.
- **L11: пул.** Сравнить `pool: 'forks'` (по умолчанию в Vitest 3) и `pool: 'threads'` по 3 прогона. `threads` оставить, если
  выполнено условие L11.
- `isolate` и `fileParallelism` не трогать (§36.7.7).
- **Тяжёлые файлы:** для файлов из `topFiles` с `seconds` > 10 разобрать причину. Реальные таймеры меняются на
  `vi.useFakeTimers()`, а у `userEvent` при фейковых таймерах нужен `advanceTimers`. Утверждения не меняются.
- Проверка: 3 прогона подряд зелёные, и дважды — с пустым и заполненным `VITE_SMARTCAPTCHA_SITEKEY` (урок C34-1).
  Тесты плашки обновления, push и воркеров не должны стать флейками (US-36-05).

---

## §36.9. Ревизия (US-36-02, US-36-03, T-36-02)

### §36.9.1 Порядок работы

1. **Инвентари «до»** снимает QA-36-01 **на коммите инструментирования, до любых удалений**:
   `python3 tools/test-audit/inventory.py --suite {unit,functional,vitest} --out tools/test-audit/results/before-<suite>.json`.
2. **Разнесение по категориям.** Исполнитель своего набора проходит **каждый файл** и ставит категорию на файл. Отдельным
   тестам категорию ставит, только если она отличается от категории файла. Итог — таблица «Категории по файлам» в разделе
   реестра (§36.9.4). В ней все файлы, включая Е.
3. **Удаление только А и Б** (SPEC US-36-02), каждое строкой реестра. **В** переносится в юнит (§36.9.6), **Г** ускоряется
   рычагами §36.6–§36.8, **Д** чинится или записывается в долг с ID. `Skip` запрещён.
4. **Решение «дубль» — по сути проверок, а не по имени (О-35-5).** Б — это тот же маршрут, та же роль, тот же тариф, тот же вид
   компании, те же данные и **то же утверждение**. Если отличается хоть одно, это не дубль. В реестре для Б указывается тест,
   который остаётся, и одной фразой — чем они совпадают.
5. **Первые кандидаты** (SPEC T-36-02) и что уже известно по коду:
   - `CompanyAddressTests.cs` (18 атрибутов, префикс `ADDR-`): геокодер удалён в цикле 19, маршруты `PUT …/address` и
     `POST …/address/notice` живы. А — только тесты, которые проверяют ответ геокодера или теневые колонки как функцию.
     Тесты, которые проверяют, что геокодер **не вызывается** или что ответ без его полей, — это «надгробия», они остаются.
   - `Cycle22RefactorEquivalenceTests.cs` (8): доказательство эквивалентности разбиения контроллеров. Б — если каждое его
     утверждение покрыто доменным тестом с тем же маршрутом и данными (в реестре — ID тестов-замен).
     `Cycle22RouteTableTests` (CY22-12, эталон маршрутов) — **не кандидат**: это страж контракта.
   - «Цикловые» файлы `Cycle15*`…`Cycle33*` против доменных: по правилу п. 4.
   - Фронт: дубли между тестом страницы и тестом её общего компонента (цикл 32 — `CompanyProfileCard`/`SalonProfileSection`,
     цикл 33 — `DevicesAndNotificationsSection` в профилях обоих сайтов), `*.qa.test.tsx` против `*.test.tsx` одного
     компонента (`BusinessBlock`, `BuyersBlock`, `UpdateBanner.acceptance`).
   - Юнит ↔ функциональные по одним `*Rules`/`*Policy` → категория В (§36.9.6), а не Б.

### §36.9.2 «Надгробия» — не удаляются

Тест, который проверяет, что удалённая функция **остаётся выключенной**: 410 у `POST …/health-consent`, отсутствие маршрута,
отказ старого поля, 404 вместо старого поведения. Такие тесты считаются актуальными (SPEC US-36-02), категория Е.

### §36.9.3 Защищённые зоны (блок заказчика, п. 3)

Тест защищён и **не удаляется** (только ускоряется или объединяется без потери утверждений), если выполнено хоть одно:

- **По файлу (бэкенд):**
  - ПДн, согласия, права субъекта, retention: `LegalConsentTests`, `LegalConsentVersionChangeTests`, `LegalPriorityTests`,
    `LegalPricingGateTests`, `DataRightsTests`, `Cycle20SubjectRequestsTests`, `Cycle20HealthWrittenConsentTests`,
    `Cycle20PlatformNoticesTests`, `GuestDataGateCycle16Tests`, `Cycle24PersonalDataTests`, `ClientNotePhotosTests`;
  - биллинг и лимиты тарифа: `BillingTests`, `PricingTests`, `Cycle15PlansTests`, `Cycle18TrialPlanTests`,
    `Cycle18TrialLifecycleTests`, `Cycle19TariffLimitsTests`, `Cycle19RetiredLimitGateParityTests`, `Cycle24TariffTests`,
    `Cycle28TariffsTests`, `Cycle22ChannelFundingTests`, `AdminBillingAccountsTests`, `Cycle20ManualPlanReasonTests`;
  - безопасность, права доступа и rate limit: `AuthTests`, `IdentityRoleSyncTests`, `RateLimitingTests`, `Cycle25RateLimitTests`,
    `Cycle31GalleryRateLimitTests`, `PushAddressGuardTests`, `UploadsStaticFilesTests`, `CompanyTransferTests`,
    `Cycle20CompanyTransferLg6Tests`, `Cycle23StrictModeAndDataTests`, `Cycle28OutboundSuppressionTests`,
    `Cycle28ShowcaseGuardsTests`, `PhoneVerificationTests`.
- **По префиксу ID:** `LEG-`, `LGL-`, `SEC-`, `PRC-`, `BLL-`, `ABA-`, `CY18-`, `CY18L-`, `CY20-`.
- **По утверждению — в любом файле любого набора:** тест утверждает код **401, 402, 403, 404-не-оракул, 429 или 451**, или отказ по
  тарифу, роли, согласию или капче. Так ловятся проверки прав внутри доменных файлов (`CompaniesTests`, `AdminTests` и т. д.).
- **Юнит-тесты правил** `Services/Legal/*`, `Services/Retention/*`, `Services/Billing/*`, `Services/Subjects/*`.
- **Фронт:** всё под `src/components/legal/`, `src/pages/{ConsentsPage,SubjectRequestPage,LegalDocumentPage}`,
  `src/components/billing/`, `src/components/pricing/`, `src/pages/{BillingPage,PricingPage}`, тесты капчи
  (`*.captcha.test.tsx`, `CartPanel.test.tsx`), `src/utils/legal*`, `src/utils/healthConsent*`.

`inventory.py` ставит `protectedZone: true` по правилам «файл» и «префикс ID». Правило «по утверждению» проверяет человек:
исполнитель при удалении, QA при приёмке. `diff_inventory.py` падает на удалении теста с `protectedZone: true` и действием
`удалён`. **Объединение** защищённых тестов разрешено, только если в реестре перечислены все утверждения старых тестов и тест,
который их теперь содержит.

### §36.9.4 Реестр и сверка «после = до − реестр»

- Раздел `TEST_CATALOG.md` «Цикл 36 — ревизия» в строгом табличном формате. Точные колонки и допустимые значения —
  `API_CONTRACT_CYCLE36.md` §36.29. Таблицу разбирает `diff_inventory.py`. Два подраздела, чтобы бэкенд и фронт не правили
  одни строки: «Реестр — бэкенд (unit, functional)» и «Реестр — фронтенд (vitest)». Скелет раздела создаёт BE-36-02.
- Инвентари «после» — `tools/test-audit/results/after-<suite>.json`, снимает QA-36-02 на HEAD.
- Сверка: `python3 tools/test-audit/diff_inventory.py --suite <s> --before … --after … --registry TEST_CATALOG.md`:
  - всё, что исчезло, есть в реестре с действием `удалён`, `перенесён в юнит`, `объединён` или `переименован`;
  - всё новое есть в реестре с действием `добавлен (замена)` или `переименован`, либо это юнит-тест, указанный заменой;
  - смена `cases` у теста (например, урезанная матрица `[Theory]`) — только при строке реестра по этому ID;
  - для .NET тесты с одинаковым `TestCase`-ID при смене класса сопоставляются автоматически (разбиение §36.7.4);
  - код выхода 0 — сошлось, 4 — нарушение с перечнем.

### §36.9.5 Покрытие маршрутов

- «До» — журнал `b1-routes` (§36.4.3). «После» — прогон QA-36-02 с `SERVICEBOOKING_TEST_ROUTE_LOG=1`.
- `python3 tools/test-audit/route_coverage.py --golden ServiceBooking.Tests/Tests/Cycle22RouteTable.golden.txt --before … --after …`:
  - ключ маршрута — `<METHOD> <RawText>` из начала строки эталона. Для строк `ANY` — только `RawText`;
  - в выводе: маршруты эталона, покрытые до; покрытые после; **потерянные** (покрыты до, не покрыты после);
    никогда не покрытые (информация, в долг, SPEC §3);
  - код выхода 4, если список потерянных не пуст.
- По логу, а не по поиску строк в исходниках: интерполированные пути (`$"/api/companies/{id}/members"`) grep не находит
  надёжно, а журнал фиксирует фактический endpoint. Для доказательства покрытия статус не важен: 4xx тоже покрывает маршрут,
  раз тест на нём что-то утверждает.

### §36.9.6 Категория В — матрицы в юнит (US-36-03, P1)

- Кандидат — функциональный `[Theory]` с большой матрицей значений **чистого** правила (статический класс без EF и HTTP,
  `CURRENT_STATE` §2), у которого есть или может быть юнит-тест.
- Матрица переезжает в `ServiceBooking.UnitTests/<Правило>Tests.cs`, если её там ещё нет. В функциональном наборе остаются 1–2
  сквозных кейса: правило подключено и отдаёт нужный код и текст. Юнит-ID — по конвенции файла юнит-тестов. Строка реестра:
  старые ID → новые юнит-тесты.
- **Исключение (остаётся целиком):** матрицы, которые доказывают согласованность правила и его SQL-двойника:
  `SalonListingRules`/`SalonListingQuery`, CY31-05, `PublicListingQuery` (`CURRENT_STATE` §6.1).
- Матрицы в защищённой зоне тоже можно переносить, если в функциональном наборе на каждый код отказа (402/403/451) остаётся
  хотя бы один сквозной кейс.

---

## §36.10. Быстрый контур (US-36-06)

- **Области** — закрытый список в `contracts/cycle36/test-areas.json`: `account`, `bookings`, `companies`, `orders`,
  `notifications`, `billing`, `legal`, `admin`, `security`, `platform`. Id добавляет только архитектор.
- **Бэкенд, функциональные:** `[Trait("Area", "<id>")]` на **каждом** не абстрактном тест-классе, у которого есть `[Fact]`/`[Theory]`.
  Можно несколько трейтов. Константы — `ServiceBooking.Tests/Infrastructure/TestAreas.cs`
  (`public const string Orders = "orders";`…). Начальная раскладка файлов — §36.10.1.
  - Команда: `dotnet test ServiceBooking.Tests --filter "Area=orders"`, несколько областей — `"Area=orders|Area=notifications"`.
  - Guard: `ServiceBooking.Tests/Tests/TestAreaCoverageTests.cs` — **без фикстуры**, поэтому базе и Docker не нужен. Он:
    - рефлексией проходит сборку;
    - читает `contracts/cycle36/test-areas.json` (путь ищется подъёмом от `AppContext.BaseDirectory`, как
      `TestHostSettings.FindRepoLegalRoot`);
    - падает, если у тест-класса нет трейта `Area`, или значение не из списка. В сообщении — перечень нарушителей.
  - ID guard-теста — `CY36-01` (строка в `TEST_CATALOG.md`).
- **Бэкенд, юнит:** не размечается. Набор без Docker, и по замеру он должен оставаться быстрым целиком. Частичный прогон —
  `--filter "FullyQualifiedName~<Правило>"`. Если `b0` покажет юнит-набор дольше 60 с, это отдельный пункт отчёта, а не
  разметка.
- **Фронт:** `frontend/scripts/test-area.mjs` и скрипт `"test:area": "node scripts/test-area.mjs"` в `package.json`. Скрипт:
  - читает `../contracts/cycle36/test-areas.json`;
  - обходит `src/` и `goods/src/` через `fs.readdirSync` рекурсивно;
  - отбирает `*.test.ts(x)` по `startsWith` префиксов;
  - запускает `npx vitest run <файлы…>`;
  - неизвестная область — код выхода 1 со списком допустимых.
  
  Команда: `npm run test:area -- orders notifications`.
- Guard фронта: `frontend/src/test/testAreas.guard.test.ts` (окружение `node`, ID `CY36-02`). Берёт список
  `import.meta.glob(['../**/*.test.{ts,tsx}', '../../goods/src/**/*.test.{ts,tsx}'])` (только ключи) и падает на файле, не
  попавшем ни в одну область. JSON импортируется из `../../../contracts/cycle36/test-areas.json` (прецедент —
  `legalRuntimeValues.test.ts`). Префиксы в `test-areas.json` правит FE-разработчик. Начальные — лучшая оценка архитектора,
  guard покажет пробелы.
- В `docs/testing-isolation.md` прямо: **быстрый контур не заменяет полный прогон перед мерджем** (Q-36-4).

### §36.10.1 Начальная раскладка функциональных классов по областям

Для исполнителя BE-36-09. Новые классы, появившиеся при разбиении (§36.7.4), наследуют трейт исходного.

| Области | Файлы (`ServiceBooking.Tests/Tests/`) |
|---|---|
| `account` | `AuthTests` (+`security`), `IdentityRoleSyncTests` (+`security`), `PhoneVerificationTests`, `ProfileTests` |
| `bookings` | `BookingHistoryTests`, `BookingsFlowSmokeTests`, `Cycle15ClientRescheduleTests`, `Cycle17ClientCancelTests`, `Cycle20DateFilterTests`, `ManualBookingFreedomTests`, `MastersTests`, `MultiServiceBookingTests`, `ReportsTests`, `ReviewsTests`, `ScheduleTemplateTests`, `SchedulerTests`, `ServicesTests`, `WorkingHoursTests`, `ClientNotePhotosTests` (+`legal`), `GuestDataGateCycle16Tests` (+`legal`) |
| `companies` | `CompaniesTests`, `CompanyAddressTests`, `CompanyPhotosTests`, `Cycle15MapLinksTests`, `Cycle26CompanyCardTests`, `Cycle29ContractTests`, `Cycle29QaTests`, `Cycle31CatalogListingTests`, `Cycle31GalleryRateLimitTests` (+`security`), `CompanyTransferTests` (+`legal`), `Cycle20CompanyTransferLg6Tests` (+`legal`) |
| `orders` | `Cycle23OrdersTests`, `Cycle23ShopsCatalogTests`, `Cycle23StaffOrdersTests`, `Cycle23StrictModeAndDataTests` (+`security`), `Cycle24AvailabilityTests`, `Cycle24HoursAcceptanceTests`, `Cycle24PickupTests`, `Cycle24NotificationsTests` (+`notifications`), `Cycle24PersonalDataTests` (+`legal`), `Cycle24TariffTests` (+`billing`), `Cycle25CatalogTests`, `Cycle25CustomerTests`, `Cycle25PickListTests`, `Cycle25RateLimitTests` (+`security`), `Cycle25ReportsTests`, `Cycle25StaffMaxTests` (+`notifications`), `Cycle25WorkingDayTests` |
| `notifications` | `MailingTests`, `NotificationChannelsTests`, `NotificationCitiesTimeZoneTests`, `NotificationDispatchExtraTests`, `NotificationDispatchTests`, `NotificationMaxTransportTests`, `NotificationQueueingTests`, `NotificationTransportStartupTests` (+`platform`), `NotificationWebhookUnsubscribeTests` (+`security`), `StaffPushTests` (оба класса), `Cycle33UnifiedPushTests`, `PushAddressGuardTests` (+`security`), `Cycle22ChannelFundingTests` (+`billing`) |
| `billing` | `BillingTests`, `PricingTests`, `Cycle15PlansTests`, `Cycle18TrialLifecycleTests` (все классы файла), `Cycle18TrialPlanTests`, `Cycle19RetiredLimitGateParityTests`, `Cycle19TariffLimitsTests`, `Cycle28TariffsTests`, `LegalPricingGateTests` (+`legal`) |
| `legal` | `DataRightsTests`, `LegalConsentTests`, `LegalConsentVersionChangeTests`, `LegalPriorityTests`, `Cycle20HealthWrittenConsentTests`, `Cycle20SubjectRequestsTests` |
| `admin` | `AdminTests`, `AdminBillingAccountsTests` (+`billing`), `Cycle20ManualPlanReasonTests` (+`billing`), `Cycle20PlatformNoticesTests` (+`legal`) |
| `security` | `RateLimitingTests`, `UploadsStaticFilesTests` (+`platform`) |
| `platform` | `HealthTests`, `PaginationTests`, `OpenApiContractValidatorTests`, `Cycle22RouteTableTests`, `Cycle22RefactorEquivalenceTests`, `Cycle28DemoContractTests`, `Cycle28DemoScenarioTests`, `Cycle28ShowcaseGeneratorTests`, `Cycle28ShowcaseGuardsTests` (+`security`), `Cycle28OutboundSuppressionTests` (+`security`), `TestAreaCoverageTests` |

---

## §36.11. CI (US-36-07, US-36-08) — трек DevOps

Все правки `.github/workflows/ci.yml` делает **только devops-инженер** (DO-36-xx). Ни один шаг не удаляется и не
ослабляется. Переставлять и разносить шаги по джобам можно.

1. **P одним числом (DO-36-03).** Остаётся env джоба `SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS: "<N>"`, а шаг берёт из него:
   `dotnet test ServiceBooking.Tests --no-build -c Release … -- xUnit.MaxParallelThreads=${SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS}`.
   Число пишется в одном месте. Выбор N: в лог выводится `nproc`. N = min(`nproc`, 4), с проверкой бюджета: при
   `max_connections=100` P ≤ 5. Сравнить два прогона CI на ветке (старое и новое N), оставить быстрый и зелёный.
2. **Postgres CI без долговечности (L9, DO-36-03).** Шаг сразу после старта сервиса:
   `docker exec ${{ job.services.postgres.id }} psql -U postgres -c "ALTER SYSTEM SET fsync=off" -c "ALTER SYSTEM SET synchronous_commit=off" -c "ALTER SYSTEM SET full_page_writes=off" -c "SELECT pg_reload_conf()"`.
   Все три параметра меняются перезагрузкой конфигурации, рестарт не нужен. Сервис одноразовый. Шаг `check-image-pins.sh`
   это не затрагивает: образ тот же.
3. **Отчёт о медленных тестах и предупреждение (DO-36-04, US-36-07):**
   - юнит и функциональные шаги получают `--logger "trx;LogFileName=<suite>.trx" --results-directory TestResults/ci`,
     `test:run` фронта — `--reporter=default --reporter=json --outputFile.json=../TestResults/ci/vitest.json`;
   - новый шаг `if: always()`:
     `python3 tools/test-timing/analyze.py --ci-summary TestResults/ci/<suite>.trx --metrics "TestResults/sb-test-metrics-*.jsonl" --thresholds tools/test-timing/thresholds.json >> "$GITHUB_STEP_SUMMARY"`.
     В сводку идут топ-10 классов, топ-10 тестов, старты хоста, wall. При превышении порогов печатается `::warning::…`,
     **код выхода 0** (порог — предупреждение, SPEC US-36-07);
   - `actions/upload-artifact` каталога `TestResults/` с `if: always()`, `retention-days: 14`;
   - пороги в `tools/test-timing/thresholds.json` (форма — `API_CONTRACT_CYCLE36.md` §36.28.4) ставит DO-36-04 по `ci-final`:
     `functionalWallSecondsWarn` = 1,25 × медианы `ci-final`, `classWallSecondsWarn` = 60, `testSecondsWarn` = 10,
     `vitestWallSecondsWarn` = 1,25 × медианы.
4. **Разнесение джобов (DO-36-05, по условию).** Делается, только если после рычагов джоб идёт дольше 9 минут (запас до 10):
   - `backend` оставляет restore, build, юнит, функциональные. Новый параллельный `backend-checks` (restore/build или
     `dotnet tool restore`) получает `check-image-pins`, дрейф снапшота, монотонность, `LegalKit check`;
   - `frontend` оставляет `npm ci`, генераты, lint, tsc ×3, `test:run`. Новый `frontend-build` (свой `npm ci`) получает
     `build:release`, смоуки и загрузку `frontend-dist-<sha>`: имя артефакта не меняется, его скачивает `deploy/deploy.sh`;
   - существующие id джобов `backend`, `frontend`, `docker-build` **не переименовываются**: они могут быть обязательными
     проверками в защите веток. DO проверяет `gh api repos/{owner}/{repo}/branches/develop/protection` и пишет результат в
     отчёт. Если новые джобы нужно сделать обязательными, это делает владелец репозитория, в отчёте — готовая инструкция.
5. **Замер CI (DO-36-02):** `gh run list --workflow ci.yml --branch develop --status success --limit 3 --json databaseId` →
   `gh run view <id> --json jobs`. Длительность каждого шага — таблица «CI: до/после» в `docs/testing-isolation.md`.

---

## §36.12. Структура: что появляется и что меняется

### §36.12.1 Файлы

| Путь | Новое или правка | Задача | Владелец |
|---|---|---|---|
| `contracts/cycle36/test-metrics.schema.json`, `route-hits.schema.json`, `timing-report.schema.json`, `test-inventory.schema.json`, `test-areas.schema.json`, `test-areas.json` | новые (созданы архитектором) | — | архитектор. В `test-areas.json` FE правит только `frontendPathPrefixes` |
| `ServiceBooking.Tests/Infrastructure/TestRunMetrics.cs` | новый | BE-36-01 | backend |
| `ServiceBooking.Tests/Infrastructure/{TestHostSettings,TestRunEnvironment,TestDatabaseFixture,ApiTestBase,NotificationTestBase}.cs`, фабрики `*Factory.cs` (параметр `factoryType`) | правка | BE-36-01, 04, 05 | backend |
| `ServiceBooking.Tests/Infrastructure/TestAreas.cs`, `Tests/TestAreaCoverageTests.cs` | новые | BE-36-09 | backend |
| `ServiceBooking.Tests/Infrastructure/LongestFirstCollectionOrderer.cs`, `ServiceBooking.Tests/test-durations.json`, `AssemblyInfo.cs` | новые или правка, по условию L7 | BE-36-10 | backend |
| `ServiceBooking.Tests/Tests/*.cs` | правка (удаления по реестру, хосты класса, трейты, разбиение) | BE-36-03…09 | backend |
| `ServiceBooking.Tests/xunit.runner.json` | правка (P), по условию L6 | BE-36-10 | backend |
| `ServiceBooking.UnitTests/*Tests.cs` | новые или правка (перенос матриц В, удаления А/Б) | BE-36-03, 08 | backend |
| `tools/test-audit/{inventory,diff_inventory,route_coverage}.py`, `tools/test-audit/README.md`, `tools/test-audit/results/{before,after}-<suite>.json` | новые | BE-36-02 (скрипты), QA-36-01/02 (results) | backend, QA |
| `tools/test-timing/{measure.sh,analyze.py,thresholds.json,README.md}`, `tools/test-timing/results/{b0-baseline,final}.{json,md}` | новые | DO-36-01, 04 (скрипты), QA-36-01/03 (results) | devops, QA |
| `.github/workflows/ci.yml` | правка | DO-36-03…05 | devops |
| `frontend/src/test/setup.ts`, `frontend/vitest.config.ts` (только `pool`), докблоки в `*.test.ts` | правка | FE-36-02 | frontend |
| `frontend/scripts/test-area.mjs`, `frontend/src/test/testAreas.guard.test.ts`, `frontend/package.json` (скрипт `test:area`) | новые или правка | FE-36-03 | frontend |
| `frontend/**/*.test.ts(x)` | правка (удаления по реестру, ускорение тяжёлых) | FE-36-01, 02 | frontend |
| `TEST_CATALOG.md` | новый раздел «Цикл 36 — ревизия» (скелет BE-36-02; подразделы BE и FE), пересчёт таблицы префиксов, строки CY36-01/02, исправление строки `ADDR-` | BE-36-02/03, FE-36-01, QA-36-04 | см. задачи |
| `docs/testing-isolation.md` | правка (§36.16) | BE-36-11, FE-36-03, DO-36-04, QA-36-04 | см. §36.16 |
| `CURRENT_STATE.md` §7.1, §7.2 | правка чисел и команд | QA-36-04 | QA |

Продуктовые проекты (`ServiceBooking.API`, `.Core`, `.Infrastructure`, `.LegalKit`), `contracts/cycle{≤33}/**`,
`frontend/src/**` (кроме тестов и `src/test/`), `frontend/goods/src/**` (кроме тестов) — **не меняются** (исключение по
Q-36-3 — §36.7.5).

### §36.12.2 Правило параллельной работы в файлах

- BE, FE и DO работают в **непересекающихся файлах** (таблица выше). Каждому агенту нужен свой worktree: в одном чекауте
  они затирают правки друг друга.
- Общие файлы — `TEST_CATALOG.md` и `docs/testing-isolation.md`. Каждый правит **только свой подраздел**, отдельным коммитом,
  ближе к концу своей работы. Сведение чисел — последним коммитом QA-36-04.
- **До завершения QA-36-01 никто не коммитит изменения тестов** (кроме BE-36-01 и BE-36-02): «до» должно сниматься на
  неизменённом наборе.

---

## §36.13. Задачи

Приоритеты — из SPEC §1. Порядок урезания: US-36-07 → US-36-06 → US-36-05 → US-36-03. P0 не режутся.
Commit-сообщения содержат ID задачи: `test(infra): … (BE-36-04)`, `ci: … (DO-36-03)`.

### §36.13.1 Backend (backend-developer)

| ID | P | Что | Зависит от | Готово, когда |
|---|---|---|---|---|
| **BE-36-01** | P0 | Инструментирование §36.5: `TestRunMetrics`, события, `factoryType`, журнал маршрутов под `SERVICEBOOKING_TEST_ROUTE_LOG` | — | полный прогон зелёный; файлы валидны по схемам (одна строка каждого события через `ajv-cli`); `b1` ≈ `b0` ±5 % (проверяет QA-36-01) |
| **BE-36-02** | P0 | `tools/test-audit/inventory.py`, `diff_inventory.py`, `route_coverage.py` + README по §36.28 контракта; скелет раздела реестра в `TEST_CATALOG.md` | — | на неизменённом наборе: `diff_inventory` before↔before = 0, `route_coverage` before↔before = 0; вывод валиден по `test-inventory.schema.json` |
| **BE-36-03** | P0 | Ревизия бэкенда §36.9: категории по всем файлам `ServiceBooking.Tests` и `ServiceBooking.UnitTests`, удаление А/Б, строки реестра, долг Д | QA-36-01 | таблица «Категории по файлам» полная; `diff_inventory` = 0; `route_coverage` без потерь |
| **BE-36-04** | P0 | L1 + L2 (§36.7.1–§36.7.2), по условию L1 | QA-36-01 | каждый переведённый класс зелёный с 3 семенами; `l1` записан; решение по каждому кандидату из таблицы §36.7.1 — в отчёте |
| **BE-36-05** | P0 | L3 (§36.7.3) | QA-36-01 | A/B записан; решение «оставить или откатить» по §36.6.2 |
| **BE-36-06** | P0 (по условию L4) | Разбиение длинных классов §36.7.4 | BE-36-03, 04, 05 | каждая часть ≤ `W*`/4 по замеру; ID те же; старые фильтры работают |
| **BE-36-07** | P1 (по условию L5) | Реальные ожидания §36.7.5 | BE-36-04 | тесты из топ-20 без фиксированных задержок ≥ 2 с; утверждения не изменены |
| **BE-36-08** | P1 | Матрицы → юнит §36.9.6 (US-36-03) | BE-36-03 | строки реестра «перенесён в юнит»; `diff_inventory` = 0 |
| **BE-36-09** | P1 | Трейты `Area` на всех классах (§36.10.1), `TestAreas.cs`, guard `CY36-01` | BE-36-06 (новые классы) | guard зелёный; `--filter "Area=orders"` запускает ожидаемые классы |
| **BE-36-10** | P0 (L6) / P1 (L7) | P локально (L6), порядок классов (L7) по условиям | BE-36-04…07 | выбранный P — 3 прогона зелёные; бюджет сходится; `xunit.runner.json` обновлён |
| **BE-36-11** | P1 | Разделы `docs/testing-isolation.md` бэкенда (§36.16); L8, только по условию | все BE | разделы написаны; строка `SEEDED_TEMPLATE` исправлена |

### §36.13.2 Frontend (frontend-developer)

| ID | P | Что | Зависит от | Готово, когда |
|---|---|---|---|---|
| **FE-36-01** | P0 | Ревизия vitest §36.9: категории по всем 203 файлам, удаление А/Б, подраздел «Реестр — фронтенд» | QA-36-01 | таблица категорий полная; `diff_inventory --suite vitest` = 0 |
| **FE-36-02** | P1 | L10, L11, тяжёлые файлы (§36.8) | FE-36-01 | медиана vitest ≤ 60 % `b0` (US-36-05) или записано, почему нет; 3 прогона зелёные, дважды — с пустым и заполненным ключом капчи |
| **FE-36-03** | P1 | `test-area.mjs`, `npm run test:area`, guard `CY36-02`, правка префиксов в `test-areas.json`, раздел фронта в `docs/testing-isolation.md` | FE-36-01 | guard зелёный; `npm run test:area -- orders` запускает все файлы `goods/src` |

### §36.13.3 QA (qa-engineer)

| ID | P | Что | Зависит от | Готово, когда |
|---|---|---|---|---|
| **QA-36-01** | P0 | Базовая линия: `b0-baseline` на `a1e2259` (3 прогона всех наборов), `b1-instrumented`, `b1-routes`; инвентари `before-*`; гейт G1 (§36.4.4) | BE-36-01, BE-36-02, DO-36-01 | `tools/test-timing/results/b0-baseline.{json,md}` и `tools/test-audit/results/before-*.json` закоммичены; окружение записано; вердикт G1 в отчёте |
| **QA-36-02** | P0 | Приёмка ревизии: разбор реестра (категории, защищённые зоны по правилу «утверждение», надгробия), `diff_inventory` по трём наборам, `route_coverage`, дифф продуктовых проектов пуст (§36.3) | BE-36-03, 08, FE-36-01 | все сверки с кодом 0 приложены к отчёту; реестр готов к показу заказчику до мерджа (блок заказчика, п. 3) |
| **QA-36-03** | P0 | Итоговый замер `final` в окружении `b0`; стабильность: 5 полных прогонов бэкенда с разными семенами (один с P=1), 3 прогона vitest; одновременный прогон из двух worktree; CI зелёный; ручная M36-01 (Rider «Run» по классу и тесту) | все BE/FE/DO | цели US-36-04, US-36-05, US-36-08 выполнены, либо есть отчёт по стоп-точке G3 |
| **QA-36-04** | P0 | Документы: числа «до/после» в `docs/testing-isolation.md`, пересчёт таблицы префиксов `TEST_CATALOG.md` от `--list-tests`, `CURRENT_STATE.md` §7.1/§7.2 | QA-36-03 | числа совпадают с `final` и инвентарями `after-*` |

### §36.13.4 DevOps (devops-engineer) — отдельный трек

| ID | P | Что | Зависит от | Готово, когда |
|---|---|---|---|---|
| **DO-36-01** | P0 | `tools/test-timing/measure.sh` + `analyze.py` + README (§36.4, §36.28 контракта), включая `--concurrent` (L12) и `--emit-durations` (для L7); эксперименты L12/L13 после рычагов BE | — | на `a1e2259` даёт `report.json`, валидный по `timing-report.schema.json`; блокировка и проверка чужих прогонов работают |
| **DO-36-02** | P0 | CI до: время шагов последних 3 зелёных прогонов `develop` (§36.11 п. 5) | — | таблица «CI: до» в отчёте |
| **DO-36-03** | P0 | CI: одно число P (§36.11 п. 1), Postgres без долговечности (L9) | — | CI зелёный; P задан в одном месте; время функционального шага до/после в отчёте |
| **DO-36-04** | P1 | CI: TRX/JSON, шаг сводки медленных тестов с `::warning::`, артефакт `TestResults`, `thresholds.json`; раздел «CI» и «Как мерить» в `docs/testing-isolation.md` | DO-36-01, DO-36-03 | сводка видна в Summary прогона; превышение порога даёт предупреждение, а не падение |
| **DO-36-05** | P1 (по условию) | Разнесение джобов (§36.11 п. 4), если джоб > 9 мин | DO-36-03, 04 | каждый джоб ≤ 10 мин; ни один шаг не удалён (сверка списков шагов до/после в отчёте); защита веток проверена |
| **DO-36-06** | P0 | Гейт перед мерджем: `git log develop..HEAD --oneline` содержит коммиты по каждой DO-36-xx или явную запись «по условию не понадобилась». То же для BE/FE/QA-ID с кодом | все | список ID ↔ коммиты приложен к отчёту QA-36-03 (урок: воркфлоу реализации терял DevOps-трек) |

### §36.13.5 Ручные проверки

| ID | Шаги | Ожидается |
|---|---|---|
| M36-01 | Rider: «Run» по одному тесту переведённого на `ClassHost` класса, по классу, по вложенному классу разбиения, по всему проекту — при выставленных переменных colima и без новых переменных | всё зелёное, настройка не нужна |
| M36-02 | Rider: группировка по трейтам (`Area`) показывает все классы | нет класса вне области |

---

## §36.14. Параллельность и порядок

```
Ф0 (старт, параллельно):  BE-36-01 ─┐        DO-36-01 ─┐      DO-36-02      DO-36-03
                          BE-36-02 ─┤                  │
                                    └──────► QA-36-01 ◄┘   (b0 на a1e2259, b1, routes, before-инвентари)
                                               │
                                          гейт G1 ── стоп, если «главное время не там» (рычаги ждут заказчика)
                                               │
Ф1 (параллельно, три трека):
  BE:  BE-36-03 (ревизия) ∥ BE-36-05 (L3) → BE-36-04 (L1+L2) → BE-36-06 (L4)* → BE-36-07 (L5)* → BE-36-08 → BE-36-09 → BE-36-10 (L6/L7)* → BE-36-11
  FE:  FE-36-01 (ревизия) → FE-36-02 (L10/L11) → FE-36-03 (области)
  DO:  DO-36-04 (сводка CI) → эксперименты L12/L13 (после BE-36-10) → DO-36-05*
                                               │
Ф2:   QA-36-02 (приёмка ревизии) → QA-36-03 (final, стабильность, CI) → QA-36-04 (документы) → DO-36-06 (гейт git log) → мердж
      * — по условию §36.6
```

- **Последовательно обязательно:** BE-36-01/BE-36-02/DO-36-01 → QA-36-01 → любые изменения тестов. Рычаги бэкенда идут по
  одному, каждый со своим замером (§36.6.2): их выигрыши не складываются «на глаз».
- **Параллельно:** треки BE, FE и DO в Ф1 не делят файлов (§36.12.2). DO-36-02/03 не зависят ни от чего и стартуют сразу.
- **Замеры не параллелятся.** Один полный замер на машине в каждый момент. `measure.sh` сам не даст запустить второй, а чужой
  живой прогон он обнаружит (§36.4.1). Параллельные агенты в Ф1 гоняют частичные прогоны (`--filter`,
  `npm run test:area`), а полные — только через `measure.sh` (Р-35-2).
- BE-36-03 (ревизия) и BE-36-04 могут трогать один файл (например, `CompanyAddressTests.cs`). Внутри трека порядок такой:
  сначала удаление А/Б в файле, потом рычаг.

---

## §36.15. Риски и решения

| Риск (SPEC) | Решение |
|---|---|
| О-35-1 причина не та | гипотезы H1–H6 с метриками (§36.2); решающая таблица по условиям (§36.6); стоп-точки G1, G3, G4 |
| О-35-2 общий хост течёт состоянием | общий хост **только внутри класса** (одна база), правило допуска из шести пунктов (§36.7.1), 3 семени на класс, прогон P=1. Делить хост **между** классами не планируется: тогда разные базы делили бы синглтоны и кеши, а выигрыш над L1 мал |
| О-35-3 параллелизм даёт таймауты | L6 только по условию CPU (§36.6.1); бюджет соединений; 5 прогонов; рекомендация по colima — заказчику |
| О-35-4 разбиение ломает фильтры | вложенные классы: `FullyQualifiedName~CompaniesTests` продолжает находить все части (§36.7.4); актуальные документы обновляет QA-36-04 |
| О-35-5 ложный «дубль» | Б только при совпадении маршрута, роли, тарифа, вида компании, данных и утверждения; тест, который остаётся, указывается в реестре; QA перепроверяет (§36.9.1) |
| О-35-6 учёт расходится с кодом | инвентари от `--list-tests` и `vitest list`, автоматическая сверка с реестром (§36.9.4); таблица префиксов пересчитывается от инвентаря (QA-36-04) |
| Р-35-1 шум colima | медиана трёх, одинаковое окружение в `environment`, `foreignRunsDetected`; порог «оставить рычаг» ≥ max(3 %, 10 с) |
| Р-35-2 параллельные агенты | блокировка `measure.sh`, проверка `TestKit status`; полные прогоны только через `measure.sh` |
| Инструментирование само замедляет прогон | контроль `b1` против `b0` ±5 %; `SERVICEBOOKING_TEST_METRICS=0`; журнал маршрутов только по флагу |
| Быстрый хеш маскирует дефект | тесты стойкость хеша не проверяли и раньше; прод не затронут; `SERVICEBOOKING_TEST_FAST_HASH=0` для диагностики |
| DevOps-задачи потеряются | отдельный трек DO-36-xx, гейт DO-36-06 со сверкой `git log` |
| Удаление защищённого теста | `protectedZone` в инвентаре, `diff_inventory` падает; правило «по утверждению» проверяет QA |
| Разнесение джобов ломает обязательные проверки | id `backend`/`frontend`/`docker-build` не меняются; проверка защиты веток; инструкция владельцу (§36.11 п. 4) |
| Флейк CY24-31 (03:30–04:05 по времени магазина) искажает стабильность | прогоны стабильности не ставить на это окно или записывать падение CY24-31 в этом окне как известное (C25-9). Вне окна он падать не должен (US-36-04) |

---

## §36.16. Документация (T-36-03)

| Документ | Что меняется | Кто |
|---|---|---|
| `docs/testing-isolation.md`, «Коротко» и «Параллелизм» | P по факту, абзац про «выше 6» и «46 с» — числа `final`, самый длинный класс по факту | BE-36-11 (текст), QA-36-04 (числа) |
| там же, новый «Время прогона (цикл 36)» | таблица до/после по трём наборам с окружением из `environment`; ссылка на `tools/test-timing/results/` | QA-36-04 |
| там же, новый «Как мерить» | `measure.sh`, метки, окно замера, чтение `report.md` | DO-36-04 |
| там же, новый «Быстрый контур» | области, `--filter "Area=…"`, `npm run test:area`, прямо: **не заменяет полный прогон перед мерджем** | BE-36-11 (бэкенд), FE-36-03 (фронт) |
| там же, новый «Как писать быстрый функциональный тест» | чего избегать: реальные ожидания, свой хост без нужды (есть `ClassHost` и правило допуска), засев через HTTP там, где хватит фикстуры или прямой записи в БД (`GiveActivePaidPlanAsync`), новый длинный класс. Что переиспользовать | BE-36-11 |
| там же, «Как это устроено в CI» | P одним числом, Postgres без долговечности, сводка медленных тестов, пороги | DO-36-04 |
| там же, таблица переменных | новые переменные §36.1; строка `SERVICEBOOKING_TEST_SEEDED_TEMPLATE` — «не реализована» (или описание, если сделан L8) | BE-36-11 |
| `TEST_CATALOG.md` | раздел «Цикл 36 — ревизия» (реестр, категории, переименования классов); строки CY36-01, CY36-02; строка `ADDR-` исправлена; пересчёт таблицы префиксов и итога | BE-36-02/03, FE-36-01, QA-36-04 |
| `CURRENT_STATE.md` §7.1, §7.2 | числа тестов, команды частичного прогона, P | QA-36-04 |
| `CHANGELOG.md`, `README.md` | по конвенции закрытия цикла (после мерджа) | как обычно в воркфлоу |

Отдельных отчётных файлов нет (Q-36-5). Результаты замеров — машинные JSON/MD в `tools/test-timing/results/`, как в
`tools/bench/cycle22/results`.

---

## §36.17. Трассировка

| SPEC | Задачи |
|---|---|
| US-36-01, T-36-01 | BE-36-01, DO-36-01, QA-36-01, QA-36-03 (повтор), §36.4 |
| US-36-02, T-36-02 | BE-36-02, BE-36-03, FE-36-01, QA-36-02, §36.9 |
| US-36-03 | BE-36-08, §36.9.6 |
| US-36-04 | BE-36-04…07, BE-36-10, QA-36-03, §36.6–§36.7 |
| US-36-05 | FE-36-02, QA-36-03, §36.8 |
| US-36-06 | BE-36-09, FE-36-03, §36.10 |
| US-36-07 | DO-36-04, BE-36-11 (раздел о быстрых тестах), §36.11 п. 3 |
| US-36-08 | DO-36-02, DO-36-03, DO-36-05, §36.11 |
| T-36-03 | BE-36-11, FE-36-03, DO-36-04, QA-36-04, §36.16 |
| T-36-04 | трек DO-36-xx, DO-36-06 |
| О-35-1…6, Р-35-1…2 | §36.15 |
