# API_CONTRACT — цикл 8, ФАЗА 2: параллелизм внутри прогона

**Дата: 2026-09-22. Ветка `cycle/08-test-env-isolation`. Архитектура фазы 2 —
`ARCHITECTURE_CYCLE8_PHASE2.md` (§89–§99).**

> **Это продолжение `API_CONTRACT_CYCLE8.md` в той же сквозной нумерации — §100–§102.**
> Разрешение конфликта нумерации описано в шапке `ARCHITECTURE_CYCLE8_PHASE2.md`: архитектура фазы 2
> заняла §89–§99, контракт — §100–§102, §103+ свободны для цикла 9.
>
> ```bash
> # свести в один документ, когда удобно (операция devops):
> printf '\n' >> API_CONTRACT_CYCLE8.md && cat API_CONTRACT_CYCLE8_PHASE2.md >> API_CONTRACT_CYCLE8.md
> ```

---

## 100. Что фаза 2 меняет в контрактах — и чего не меняет

### 100.1 Сводка

| Категория | Количество |
|---|---|
| новых эндпоинтов | **0** |
| удалённых эндпоинтов | **0** |
| изменённых форм запроса/ответа | **0** |
| изменённых кодов ошибок | **0** |
| BREAKING | **0** |
| новых миграций БД | **0** |
| новых сущностей/полей модели данных | **0** |
| изменённых переменных окружения рабочей копии (§85.1) | **0 изменённых, 2 добавленных** — §101.2 |
| изменённых машиночитаемых схем | **1** — `testkit-status.schema.json` → `schemaVersion: 2` (§102) |

Правка в `ServiceBooking.API` за фазу 2 ровно одна: именованный параметр
`preserveStaticLogger: builder.Environment.IsEnvironment("Testing")` у `builder.Host.UseSerilog`
(`ARCHITECTURE_CYCLE8_PHASE2.md §96`). В средах `Development` и `Production` выражение даёт `false`,
то есть сегодняшнее поведение, дословно. На HTTP не влияет ничем.

### 100.2 `servicebooking-invariant.openapi.yaml` — **не меняется ни строкой**

Правило §84.2 п. 4 («изменение схемы в цикле 8 — красный флаг ревью») действует в фазе 2 **в полную
силу**. Фаза 2 трогает только тестовую обвязку и одну конфигурационную строку логирования;
HTTP-поверхность обязана остаться байт-в-байт. Проверка — та же, что в §82, и её стоит прогнать
ещё раз после T8-P7 (включения параллелизма):

```bash
SB_API_PORT=5010 docker compose up -d --build
npx @redocly/cli lint contracts/cycle8/servicebooking-invariant.openapi.yaml
pipx run schemathesis run contracts/cycle8/servicebooking-invariant.openapi.yaml \
     --base-url http://localhost:5010 --checks all --hypothesis-max-examples 25
BASE_URL=http://localhost:5010 bash deploy/ci/smoke.sh
```

### 100.3 Отмена трёх утверждений §87.3

`API_CONTRACT_CYCLE8.md §87.3` («Что остаётся неизменным») писался при действовавшем П2. Три его
первых буллита **отменены** решением заказчика; остальные остаются верными:

| Буллит §87.3 | Статус |
|---|---|
| «`[assembly: CollectionBehavior(DisableTestParallelization = true)]` — **остаётся**» | **ОТМЕНЁН.** Атрибут удаляется целиком в T8-P7 |
| «коллекции `"Api"` и `"NotificationDispatch"` — **остаются** с теми же именами» | **ОТМЕНЁН.** Обе распускаются; каждый тест-класс получает собственную коллекцию xUnit (`ARCHITECTURE_CYCLE8_PHASE2.md §92.1). Имя `"Sequential"` резервируется для классов, возвращённых в последовательный режим |
| «`NotificationTestBase` остаётся в `[Collection("Api")]`» | **ОТМЕНЁН.** Становится `IClassFixture<TestDatabaseFixture>`; обе причины, ради которых он там был (дождаться готовности базы; не гоняться с миграцией), закрываются базой-на-класс |
| «`[Fact, TestCase("PREFIX-NNN")]` — маркировка не трогается ни у одного теста» | **в силе** |
| «`--filter "FullyQualifiedName~…"` продолжает работать» | **в силе** (и это отдельный приёмочный пункт: фильтр по одному классу должен поднимать одну базу) |
| «`appsettings.Testing.json` не меняется» | **в силе** |
| «смысл и утверждения 465 функциональных тестов не меняются» | **в силе и усилено**: фаза 2 не меняет ни одной строки в телах тестов — проверяется диффом, греп № P3 |

---

## 101. Контракт тестовой обвязки после фазы 2 (заменяет §87.1 и дополняет §87.2)

### 101.1 Публичная поверхность

```csharp
// ServiceBooking.TestKit — без изменений относительно §87.1, кроме одного метода
public static class TestRunKey         { public static string Current { get; } }        // ^[0-9a-f]{8}$
public static class TestDatabaseNaming {
    public static readonly string[] NeverDrop;
    public static bool IsDisposable(string databaseName);      // регэксп НЕ менялся: c07 проходит как слот
    public static void EnsureDisposable(string databaseName);
    public static void EnsureOwnedByThisRun(string databaseName);
}

// ServiceBooking.Tests.Infrastructure
public static class TestSlot
{
    public const string Api, Legal, Dispatch;                  // зарезервированы (нужны при срезе скоупа US-95)
    public static string NextForClass();                       // НОВОЕ: "c01".."c999", Interlocked-счётчик
}

public sealed record TestHostIdentity(
    string ConnectionString, string SuperAdminPhone, string SuperAdminEmail, string SuperAdminPassword,
    string PublicRoot, string PrivateRoot, string StateRoot, string LogDirectory, string LegalRoot,
    string ClassSlot,        // НОВОЕ
    string DatabaseName);    // НОВОЕ

public static class TestHostSettings
{
    // БЫЛО: Apply(IWebHostBuilder, string slot, string factoryTag) -> TestHostIdentity
    // СТАЛО: identity строится фикстурой один раз и передаётся фабрике снаружи
    public static void Apply(IWebHostBuilder builder, TestHostIdentity identity, string factoryTag);
    public static TestHostIdentity BuildIdentity(TestDatabaseLease lease, string classSlot);
}

public sealed class TestData(string classSlot)                 // НОВОЕ — единственный генератор (Q12)
{
    public string Phone();                                     // всегда валиден: +79XXXXXXXXX
    public string Email(string prefix = "u");
    public string Slug(string prefix = "company-");
    public string Name(string prefix);
    public string Dir(string purpose);                         // <tmp>/sb-test/<runkey>/<classSlot>/<purpose>
}

public sealed class TestDatabaseFixture : IAsyncLifetime      // БЫЛА коллекционной, СТАЛА классовой
{
    public string ClassSlot { get; }
    public string DatabaseName { get; }
    public TestHostIdentity Identity { get; }
    public CustomWebApplicationFactory Factory { get; }
    public TestData Data { get; }
    // УДАЛЕНО: public static readonly string ConnectionString   ← греп № P2 проверяет отсутствие
}
```

Каждая из шести фабрик принимает `TestHostIdentity` **первым параметром конструктора**. Это и есть
ответ на «как фабрика узнаёт свою базу»: не из статики и не из переменной окружения, а из объекта,
созданного её фикстурой.

### 101.2 Переменные окружения — две добавленные, ни одной изменённой

Таблица §85.1 остаётся верной целиком. Добавляются:

| Переменная | Дефолт | Формат | Читает | Зачем |
|---|---|---|---|---|
| `SERVICEBOOKING_TEST_SEEDED_TEMPLATE` | не задана | `1`/пусто | `ServiceBooking.TestKit` | включает предсидированный шаблон (`ARCHITECTURE_CYCLE8_PHASE2.md §91.6`). По умолчанию **выключено** |
| `SERVICEBOOKING_TEST_ORDER_SEED` | не задана | целое | `ServiceBooking.Tests` | сид случайного порядка тестов внутри класса (US-100, последний критерий). Не задана → порядок по умолчанию |

**Степень параллелизма переменной окружения НЕ получает** — сознательно. Она задаётся
`xunit.runner.json` (дефолт 4) и переопределяется штатным механизмом раннера:

```
dotnet test ServiceBooking.Tests -- xUnit.MaxParallelThreads=2
```

Причина — принцип §62 «ни одного пятого механизма конфигурации»: у xUnit уже есть свой, и заводить
рядом переменную окружения означало бы два источника истины с непонятным приоритетом.

### 101.3 Правила, обязательные к соблюдению в тестах (заменяет §87.2)

| Правило | Вместо чего | Как проверяется |
|---|---|---|
| строка подключения — только `Fixture.Identity.ConnectionString` | `TestDatabaseFixture.ConnectionString` (static) | греп № P2 |
| уникальные значения — только `Fixture.Data.*` | `Guid.NewGuid()` и литералов на месте | греп № P4 |
| суперадмин — только `Factory.Identity.SuperAdminPhone` | литерала `"+70000000001"` | греп № 3 (§79) |
| каталог логов — только `Factory.Identity.LogDirectory` | поиска `logs/` вверх от `AppContext.BaseDirectory` | §71.4 |
| `DROP DATABASE`/`EnsureDeletedAsync` — нигде, кроме `TestKit.TestDatabaseLease` | — | греп № 1 (§79) |
| новый тест-класс **не объявляет** `[Collection]` | привычки «добавь в Api» | ревью; класс без атрибута параллелится сам |
| класс, который параллелить нельзя, объявляет `[Collection("Sequential")]` **с причиной в комментарии** | `[Skip]` | греп № P5 |

---

## 102. Машиночитаемая схема: `testkit-status.schema.json` → `schemaVersion: 2`

Фаза 2 задевает схему в трёх местах. Файл обновлён в этом же цикле
(`contracts/cycle8/testkit-status.schema.json`); совместимость с выводом фазы 1 сохранена —
`schemaVersion` принимает `1` и `2`, поэтому артефакты, снятые до T8-P7, валидируются по той же
схеме.

| Что поменялось | Было | Стало | Почему |
|---|---|---|---|
| `testResource.slot` | закрытый `enum: ["template","api","legal","dispatch",null]` | `pattern: "^(template\|api\|legal\|dispatch\|c[0-9]{2,3})$"` | классовые слоты `c01…c999` (`ARCHITECTURE_CYCLE8_PHASE2.md §91.3`). Закрытый enum сделал бы `status --json` невалидным на первом же параллельном прогоне |
| `testResource.testClass` | — | новое необязательное поле, `string\|null` | связь «слот ↔ тест-класс» для диагностики (§92.2: xUnit не передаёт имя класса в фикстуру, поэтому оно проставляется базовым классом и может отсутствовать) |
| `doctorDocument.checks[].name` | 6 значений | + `"parallel-connection-budget"` | `doctor` обязан отвечать на вопрос «мой P влезает в `max_connections`?» **до** прогона, а не в его начале (§93.4) |
| `schemaVersion` | `const: 1` | `enum: [1, 2]` | обратная совместимость с артефактами фазы 1 |

Проверка не меняется — тем же готовым инструментом, без написания тестов:

```bash
dotnet run --project ServiceBooking.TestKit -- status --json > /tmp/st.json
dotnet run --project ServiceBooking.TestKit -- doctor --json > /tmp/dr.json
dotnet run --project ServiceBooking.TestKit -- sweep  --json > /tmp/sw.json
for f in /tmp/st.json /tmp/dr.json /tmp/sw.json; do
  npx ajv-cli validate -s contracts/cycle8/testkit-status.schema.json -d "$f" --spec=draft2020
done
```

### 102.1 Чек-лист согласования фазы 2 перед мержем (дополняет §88)

**Backend**
- [ ] 465/465 последовательно зелёные **после каждой** из T8-P3…T8-P6 (параллелизм ещё выключен)
- [ ] `grep` № P2, P4 чисты; `TestDatabaseFixture.ConnectionString` не существует
- [ ] `git diff --stat` по `ServiceBooking.Tests/Tests/` содержит только снятые `[Collection]` и 5 объявлений `IClassFixture`
- [ ] fail-fast бюджета соединений срабатывает: искусственно `-- xUnit.MaxParallelThreads=20` против внешнего сервера → отказ с текстом §93.4, ноль созданных баз
- [ ] `--filter "FullyQualifiedName~CompaniesTests"` поднимает **одну** базу и проходит

**QA**
- [ ] реестр причин US-94 заполнен обеими разведками (N_shared и N_class), лежит в §97.3
- [ ] решение по воронке US-95 записано с числом, а не с формулировкой
- [ ] 10 прогонов локально + 3 в CI = 13 зелёных; ни одного `Skip` (греп № P5)
- [ ] M2 ≤ 0,70 × M0 и ≤ 0,75 × M1 локально; в CI ≤ M1 и ≤ 1,20 × M0
- [ ] приёмка фазы 1 повторена при включённом параллелизме (два одновременных прогона)
- [ ] тесты отправщика уведомлений — ноль красных за 10 прогонов (US-98, отдельный пункт)
- [ ] `ajv` валидирует вывод всех трёх команд по обновлённой схеме

**Devops**
- [ ] `xunit.runner.json` в дереве, `CopyToOutputDirectory=PreserveNewest`
- [ ] `ci.yml`: `-- xUnit.MaxParallelThreads=2`; состав проверок не изменился ни на одну
- [ ] `max_connections=300` и `max_locks_per_transaction=128` у тестового контейнера
- [ ] `contracts/cycle8/testkit-status.schema.json` — `schemaVersion` принимает 1 и 2

**Документация**
- [ ] `CURRENT_STATE.md`: остаточный долг перечислен **поимённо и с числами** (какие классы остались последовательными и почему)
- [ ] `CURRENT_STATE.md`: пик памяти при двух одновременных параллельных прогонах назван числом (§93.5)
- [ ] `docs/testing-isolation.md`: раздел «параллелизм», как менять `MaxParallelThreads`, как диагностировать гонку через `P=1`
- [ ] `CURRENT_STATE.md` §9 п. 24d закрыт окончательно (фаза 2 снимает и остаток)
