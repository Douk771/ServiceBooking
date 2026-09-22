# API_CONTRACT — цикл 8: изоляция тестовой и локальной среды

**Дата: 2026-09-22. Ветка `cycle/08-test-env-isolation`. Архитектура — `ARCHITECTURE_CYCLE8.md`.**

---

## Как читать этот документ

`API_CONTRACT.md` — цикл 3, `API_CONTRACT_CYCLE4.md` — цикл 4, `API_CONTRACT_CYCLE5.md` — цикл 5.
Ни один не переписывается. Нумерация разделов продолжает сквозную (последний занятый — §53 в цикле 5).

**Главное, что нужно знать про этот документ за десять секунд:**

> **Цикл 8 не добавляет, не убирает и не меняет ни одного HTTP-эндпоинта, ни одного поля запроса или
> ответа, ни одного кода ошибки.** Раздел §84 фиксирует это машиночитаемо. Всё остальное в документе —
> контракты **не-HTTP**: переменные окружения, CLI и внутренняя тестовая обвязка. Они настоящие: по
> ним backend, frontend и devops работают параллельно, не видя кода друг друга.

| Раздел | Контракт | Машиночитаемая форма | Кто потребитель |
|---|---|---|---|
| §84 | HTTP-инвариант: что обязано остаться байт-в-байт | `contracts/cycle8/servicebooking-invariant.openapi.yaml` (OpenAPI 3.1) | frontend, QA, `smoke.sh` |
| §85 | переменные окружения рабочей копии | таблица + дефолты в `docker-compose.yml`/`vite.config.ts` | frontend, devops |
| §86 | CLI `ServiceBooking.TestKit` | `contracts/cycle8/testkit-status.schema.json` (JSON Schema 2020-12) | QA, devops, агенты |
| §87 | тестовая обвязка внутри `ServiceBooking.Tests` | сигнатуры C# | backend, QA |
| §88 | чек-лист согласования перед мержем | — | все |

---

## 82. Почему HTTP-контракт в инфраструктурном цикле вообще существует

Потому что цикл трогает то, **через что** контракт проверяется: строку подключения, порты, адреса,
базы. Если после цикла `POST /api/auth/register` начнёт отвечать иначе — это будет замечено не
сразу, а в виде красноты у кого-то третьего. Поэтому инвариант фиксируется отдельным файлом, который
можно предъявить инструменту, а не только глазам.

**Урок §9 L6 `CURRENT_STATE.md` прямо к этому:** у каждого внешнего потребителя API есть своя копия
контракта, и расхождение ловит только проверка против **настоящего образа**. Цикл 8 добавляет к
`deploy/ci/smoke.sh` вторую такую проверку — схему, по которой QA может прогнать
инструмент автоматически, ничего не дописывая руками.

**Как этим пользуется QA (готовым инструментом, без написания тестов):**

```bash
# 1. поднять образ на своём порту (цикл 8 как раз делает это безопасным рядом с чужим стеком)
SB_API_PORT=5010 docker compose up -d --build

# 2. синтаксис и внутренняя связность схемы
npx @redocly/cli lint contracts/cycle8/servicebooking-invariant.openapi.yaml

# 3. живая сверка схемы с реально поднятым API
pipx run schemathesis run contracts/cycle8/servicebooking-invariant.openapi.yaml \
     --base-url http://localhost:5010 --checks all --hypothesis-max-examples 25

# 4. то же в один проход со смоуком (смоук проверяет сценарий, схема — форму)
BASE_URL=http://localhost:5010 bash deploy/ci/smoke.sh
```

Шаг 3 красный = backend и frontend разошлись по форме интерфейса, независимо от того, что написано
в их отчётах. Это и есть предмет раздела.

---

## 83. Сводка изменений HTTP-поверхности за цикл 8

| Категория | Количество |
|---|---|
| новых эндпоинтов | **0** |
| удалённых эндпоинтов | **0** |
| изменённых форм запроса/ответа | **0** |
| изменённых кодов ошибок | **0** |
| BREAKING | **0** |
| новых миграций БД | **0** |
| новых сущностей/полей модели данных | **0** |

Единственная правка в `ServiceBooking.API` за цикл — конфигурационный ключ `Logs:Directory`
(`ARCHITECTURE_CYCLE8.md §71.4`), влияющий на путь файла логов и **ничего** не меняющий в HTTP.
Дефолт `"logs"` сохраняет сегодняшнее поведение дословно.

---

## 84. HTTP-инвариант цикла · `contracts/cycle8/servicebooking-invariant.openapi.yaml`

### 84.1 Что вошло в схему и почему именно это

Схема **не** описывает весь API (его полное описание — `API_CONTRACT.md` + `_CYCLE4` + `_CYCLE5` +
`API_DOCUMENTATION.md` + живой Swagger). Она описывает **подмножество, которое цикл 8 может сломать
и обязан не сломать**:

| Эндпоинт | Почему в схеме |
|---|---|
| `GET /api/health/live` | первый шаг `smoke.sh`; ломается, если стек поднялся не на том порту |
| `GET /api/health/ready` | доказывает, что миграции накатились — а цикл переделывает именно то, **куда** они накатываются |
| `GET /api/legal/documents` | `smoke.sh` читает отсюда версии; фабрики получают свои копии манифеста (§71.3) — если копия окажется битой, ломается здесь |
| `POST /api/auth/register` | единственный сквозной сценарий смоука; завязан на сидирование, а сидирование теперь идёт в три базы вместо одной |
| `POST /api/auth/login` | вход суперадмина — ровно то место, где менялись телефоны (§71.1) |
| `POST /api/profile/avatar` | `Storage:PublicRoot` переезжает во временный каталог — проверка, что загрузка и резолвинг корня целы |
| `GET /uploads/{path}` | прокси Vite и статика; меняется и `vite.config.ts`, и `PublicRoot` |

Всё, что цикл не может задеть (записи, компании, услуги, админка, каналы), в схему намеренно
**не** включено: инвариант, который никто не собирается ломать, не нужно охранять, а лишние пути
делают `schemathesis` шумным.

### 84.2 Правила работы со схемой

1. **Схема — производная от кода, а не наоборот.** Если `schemathesis` покажет расхождение,
   разбирательство начинается с вопроса «кто прав» и **не** заканчивается автоматической правкой
   схемы. В цикле 8 правильный ответ известен заранее: прав код на `7b382d8`, схема снята с него.
2. **Версии документов в схему не зашиты.** `GET /api/legal/documents` описан по форме
   (`type`/`version`/`gate`/…), но ни одно значение `version` не закреплено — иначе повторится
   ровно тот дефект §9 L6, который цикл обязан не воспроизвести.
3. **Схема не заменяет `smoke.sh`.** Схема проверяет форму, смоук — сценарий (SkiaSharp реально
   грузится, файл реально отдаётся). Нужны обе.
4. **Изменение схемы в цикле 8 — красный флаг ревью.** Любой коммит, трогающий
   `servicebooking-invariant.openapi.yaml`, обязан объяснить, какое требование цикла этого
   потребовало.

### 84.3 Авторизация в схеме

`bearerAuth` (JWT в `Authorization: Bearer <token>`), как и сегодня. Схема не вводит новых схем
аутентификации и не описывает выдачу токена иначе, чем `POST /api/auth/register|login`.

---

## 85. Контракт переменных окружения рабочей копии

Это **основной контракт между frontend, backend и devops в этом цикле**. Frontend пишет
`vite.config.ts` по нему, devops — `docker-compose.yml`, backend — ничего (тестам файл не нужен),
и никто никого не ждёт.

### 85.1 Переменные

| Переменная | Дефолт | Формат | Читает |
|---|---|---|---|
| `SB_PROJECT_NAME` | `servicebooking` | `^[a-z0-9][a-z0-9_-]{0,30}$` | `docker-compose.yml` → `name:`, имя тома; `TestKit status` |
| `SB_DB_PORT` | `5432` | порт 1–65535 | `docker-compose.yml` (публикация Postgres) |
| `SB_DB_NAME` | `servicebooking` | `^[a-z][a-z0-9_]{0,62}$` | `docker-compose.yml` (`POSTGRES_DB`, строка подключения api) |
| `SB_API_PORT` | `5000` | порт | `docker-compose.yml` (публикация api), **`vite.config.ts`** |
| `SB_WEB_PORT` | `5173` | порт | **`vite.config.ts`** (`server.port`), `AllowedOrigins` в compose |
| `SB_VOLUME_SUFFIX` | пусто | `^[a-z0-9_-]{0,20}$` | имя тома Postgres |
| `SB_GLITCHTIP_PORT` | `8000` | порт | `docker-compose.glitchtip.yml` (необязательная задача) |
| `VITE_API_TARGET` | не задана | URL | **`vite.config.ts`** — приоритетное переопределение, механизм цикла 3 **сохраняется** |
| `SERVICEBOOKING_TEST_CONNECTION` | не задана | строка подключения Npgsql | `ServiceBooking.Tests` |
| `SERVICEBOOKING_TEST_RUN_KEY` | не задана | `^[0-9a-f]{8}$` после нормализации | `ServiceBooking.TestKit` |
| `SERVICEBOOKING_TEST_NO_TEMPLATE` | не задана | `1`/пусто | `ServiceBooking.TestKit` — аварийный выключатель шаблонной базы |

### 85.2 Где живут значения

- **`.env` в корне рабочей копии** — необязательный, gitignored, читается `docker compose`
  автоматически и `vite.config.ts` через `loadEnv(mode, '..', ['VITE_','SB_'])`.
- **`.env.dev.example`** — закоммичен, содержит все переменные с дефолтами и комментариями.
  Заглушки, ни одного реального секрета (NFR «Безопасность»).
- **Ни одна переменная не обязательна.** Свежий клон без `.env`: `docker compose up` и
  `npm run dev` работают ровно как сегодня, на 5432/5000/5173.

### 85.3 Приоритет (нормативно)

```
1. переменная процесса  (export VITE_API_TARGET=... ; export SB_API_PORT=...)
2. значение из .env рабочей копии
3. встроенный дефолт из таблицы §85.1
```

Frontend обязан реализовать этот порядок буквально: `process.env.VITE_API_TARGET` побеждает
`.env`-овый `VITE_API_TARGET`, а тот — вычисленный из `SB_API_PORT` адрес.

### 85.4 Поведение при конфликте портов — нормативно

| Ситуация | Требуемое поведение |
|---|---|
| `SB_WEB_PORT` занят соседним Vite | Vite занимает следующий свободный и **печатает адрес**. `strictPort` ставить **запрещено** (US-89) |
| `SB_API_PORT` занят | `docker compose up` падает с ошибкой публикации порта — это правильно и не маскируется. Диагностика — `TestKit status` (§86) |
| `SB_DB_PORT` занят | то же |
| порт тестового Postgres | конфликта не существует: порт выбирает ОС (`ARCHITECTURE_CYCLE8.md §73.1`) |

### 85.5 Что frontend обязан **не** делать

- не читать `.env.production`, `.env` боевой машины, `appsettings.*.json`;
- не заводить `frontend/.env.local` как обязательный — он остаётся личным переопределением;
- не ставить `server.strictPort`;
- не трогать `vitest.config.ts` (он намеренно отдельный; 181 тест прокси не касается).

---

## 86. Контракт CLI `ServiceBooking.TestKit`

### 86.1 Команды

```
dotnet run --project ServiceBooking.TestKit -- status [--json]
dotnet run --project ServiceBooking.TestKit -- sweep  [--apply] [--max-age <dur>] [--run-key <key>] [--json]
dotnet run --project ServiceBooking.TestKit -- doctor [--json]
```

| Команда | Что делает | Меняет состояние? |
|---|---|---|
| `status` | US-90: ключ рабочей копии, имя compose-проекта, порты и их занятость, имя dev-базы, живые тестовые контейнеры и базы с отметкой «мой/чужой» | **нет, никогда** |
| `sweep` | US-86: перечисляет ресурсы по трём категориям — `dead` / `alive` / `undetermined` | **нет** без `--apply` |
| `sweep --apply` | удаляет **только** категорию `dead` | да |
| `doctor` | проверяет предусловия: Docker доступен, образ Postgres в кеше, .NET SDK, свободные порты | **нет** |

- `--max-age` — длительность вида `30m`, `2h`, `1d`. Дефолт `2h`.
- `--run-key` — точечное удаление конкретного прогона; **единственный** способ тронуть категорию
  `undetermined`.
- Без `--apply` любая команда безопасна при любом состоянии машины, включая отсутствующий Docker.

### 86.2 Коды возврата

| Код | Значение |
|---|---|
| `0` | успех; для `sweep` без `--apply` — успех независимо от того, найдено ли мёртвое |
| `1` | ошибка исполнения (нет доступа к Docker и к серверу одновременно, некорректные аргументы) |
| `2` | `doctor`: предусловия не выполнены (Docker недоступен) — отличается от `1`, чтобы CI мог различить |
| `3` | `sweep --apply`: часть ресурсов удалить не удалось; в JSON — поле `errors[]` |

`status` **никогда** не возвращает ненулевой код из-за состояния машины: «всё занято» — это факт, а
не ошибка.

### 86.3 Машиночитаемый вывод

`--json` печатает **только** JSON в stdout (человекочитаемое — в stderr), по схеме
`contracts/cycle8/testkit-status.schema.json` (JSON Schema 2020-12). Одна схема на все три команды:
корневое поле `command` различает их, `oneOf` по нему задаёт обязательные секции.

Проверяется готовым инструментом:

```bash
dotnet run --project ServiceBooking.TestKit -- status --json > /tmp/st.json
npx ajv-cli validate -s contracts/cycle8/testkit-status.schema.json -d /tmp/st.json --spec=draft2020
```

### 86.4 Пример вывода `status --json`

```json
{
  "command": "status",
  "schemaVersion": 1,
  "generatedAtUtc": "2026-09-22T10:14:03Z",
  "workingCopy": {
    "path": "/Users/ikolomeets/RiderProjects/ServiceBooking",
    "composeProjectName": "servicebooking",
    "devDatabaseName": "servicebooking",
    "envFilePresent": false
  },
  "ports": [
    { "name": "SB_DB_PORT",  "value": 5432, "source": "default", "inUse": true,  "ownedByThisCopy": true },
    { "name": "SB_API_PORT", "value": 5000, "source": "default", "inUse": true,  "ownedByThisCopy": true },
    { "name": "SB_WEB_PORT", "value": 5173, "source": "default", "inUse": false, "ownedByThisCopy": false }
  ],
  "docker": { "available": true, "error": null },
  "testResources": [
    { "kind": "container", "id": "sb-pg-a3f19c7b", "runKey": "a3f19c7b",
      "workdir": "/Users/ikolomeets/RiderProjects/ServiceBooking",
      "startedAtUtc": "2026-09-22T10:11:40Z", "ageSeconds": 143,
      "hostPid": 91004, "hostPidAlive": true, "mine": true, "liveness": "alive" },
    { "kind": "database", "id": "sbtest_5e0bd214_api", "runKey": "5e0bd214",
      "workdir": "/Users/ikolomeets/RiderProjects/ServiceBooking-wt2",
      "startedAtUtc": "2026-09-22T05:58:12Z", "ageSeconds": 15351,
      "hostPid": 88123, "hostPidAlive": false, "connections": 0, "mine": false, "liveness": "dead" }
  ]
}
```

`liveness` принимает ровно три значения: `alive` | `dead` | `undetermined`. Правило вычисления —
`ARCHITECTURE_CYCLE8.md §70.3`, и оно **нормативно**: `dead` только при конъюнкции всех признаков.

---

## 87. Контракт тестовой обвязки (внутри `ServiceBooking.Tests`)

Это контракт между backend-разработчиком, пишущим обвязку, и QA/любым, кто дальше правит тесты.
Он фиксируется здесь, чтобы не выясняться по коду.

### 87.1 Публичная поверхность

```csharp
// ServiceBooking.TestKit
public static class TestRunKey        { public static string Current { get; } }           // ^[0-9a-f]{8}$
public static class TestDatabaseNaming{
    public static readonly string[] NeverDrop;                                            // §69.3
    public static bool IsDisposable(string databaseName);
    public static void EnsureDisposable(string databaseName);                             // иначе TestSafetyException
    public static void EnsureOwnedByThisRun(string databaseName);
}

// ServiceBooking.Tests.Infrastructure
public static class TestSlot          { public const string Api, Legal, Dispatch; public static readonly string[] All; }
public sealed record TestHostIdentity( string ConnectionString, string SuperAdminPhone, string SuperAdminEmail,
                                       string SuperAdminPassword, string PublicRoot, string PrivateRoot,
                                       string StateRoot, string LogDirectory, string LegalRoot );
public static class TestHostSettings  { public static TestHostIdentity Apply(IWebHostBuilder b, string slot, string factoryTag); }
public static class TestPhones        { public static string Unique(); }                  // всегда +79…
```

Каждая из шести фабрик выставляет `public TestHostIdentity Identity { get; }`.

### 87.2 Правила, обязательные к соблюдению в тестах

| Правило | Вместо чего |
|---|---|
| суперадмин — только `Factory.Identity.SuperAdminPhone` | литерала `"+70000000001"` |
| телефон тестовых данных — только `TestPhones.Unique()` | двух копий `UniquePhone()` |
| каталог логов — только `Factory.Identity.LogDirectory` | поиска `logs/` вверх от `AppContext.BaseDirectory` |
| `DROP DATABASE` / `EnsureDeletedAsync` — **нигде**, кроме `TestKit.TestDatabaseLease` | `TestDatabaseFixture.EnsureDeletedAsync` |
| имя базы никогда не пишется литералом | `"servicebooking_test"` |

### 87.3 Что остаётся неизменным (важнее, чем что меняется)

- `[assembly: CollectionBehavior(DisableTestParallelization = true)]` — **остаётся** (П2);
- коллекции `"Api"` и `"NotificationDispatch"` — **остаются** с теми же именами;
- `NotificationTestBase` остаётся в `[Collection("Api")]`;
- `[Fact, TestCase("PREFIX-NNN")]` — маркировка не трогается ни у одного теста;
- `--filter "FullyQualifiedName~..."` продолжает работать (NFR «Совместимость»);
- `appsettings.Testing.json` не меняется;
- смысл и утверждения 465 функциональных тестов не меняются; меняются только адреса ресурсов.

---

## 88. Чек-лист согласования перед мержем цикла

**Backend**
- [ ] `TestDatabaseNamingTests` зелёные; все восемь отрицательных случаев §69.3 красят при инверсии
- [ ] `grep` №1–3, 5 из `ARCHITECTURE_CYCLE8.md §79` чисты
- [ ] 465/465 функциональных, 591/591 юнит
- [ ] юнит-набор зелёный при **остановленном** Docker (T8-Q2)
- [ ] первая строка вывода прогона содержит ключ и имена баз (US-83)
- [ ] замер «после» записан рядом с замером «до» (§63)

**Frontend**
- [ ] `vite.config.ts` реализует приоритет §85.3 буквально
- [ ] `strictPort` не выставлен; поведение «порт занят» проверено руками и описано
- [ ] 181/181 vitest, `tsc --noEmit` чисто, `npm run build` успешно
- [ ] прогон `schemathesis` по §82 против поднятого образа — зелёный
- [ ] ни одного нового обращения к переменным вне таблицы §85.1

**Devops**
- [ ] `SPEC_CYCLE5_LEGAL.md` восстановлен (T8-00)
- [ ] `docker compose up` в **свежем клоне без `.env`** работает как сегодня
- [ ] два стека из двух каталогов подняты одновременно; `down -v` в одном не задел другой
- [ ] `grep` №6, 7, 9 из §79 чисты; `check-image-pins.sh` зелёный
- [ ] `smoke.sh`: `grep` №8 чист, два смоука на разных портах прошли одновременно

**QA**
- [ ] два одновременных полных прогона из двух рабочих копий — оба зелёные, разные имена баз в выводе
- [ ] пять последовательных прогонов — пять зелёных
- [ ] после них `status --json` не содержит ни одного `testResources[]`
- [ ] `sweep --apply` во время живого прогона его не задел
- [ ] `sweep` по умолчанию сухой; категория `undetermined` не удаляется
- [ ] `ajv` валидирует вывод всех трёх команд по `testkit-status.schema.json`
- [ ] `redocly lint` и `schemathesis` по `servicebooking-invariant.openapi.yaml` — зелёные

**Документация**
- [ ] `CURRENT_STATE.md` §7 «Как запускать» не упоминает `localhost:5432` с пустым паролем как предусловие
- [ ] §9 п. 24d закрыт либо переформулирован в остаточный риск
- [ ] урок P0-цикл-4 п. E помечен «причина устранена»
- [ ] R9 (§69.4) внесён в §9 как остаточный риск
