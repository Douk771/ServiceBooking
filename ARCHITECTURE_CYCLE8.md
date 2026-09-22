# ARCHITECTURE — цикл 8: изоляция тестовой и локальной среды

**Дата: 2026-09-22. Ветка: `cycle/08-test-env-isolation`. Источник требований — `SPEC.md` (редакция 1,
цикл 8). Источник истины о коде — `CURRENT_STATE.md`.**

---

## Как этот документ соотносится с прежними

`ARCHITECTURE.md` — цикл 3, `ARCHITECTURE_CYCLE4.md` — цикл 4, `ARCHITECTURE_CYCLE5.md` — цикл 5.
Ни один из них **не переписывается**: на них ссылаются десятки комментариев в коде
(`ARCHITECTURE.md §12.2`, `ARCHITECTURE_CYCLE4.md §27`, …). Этот файл — **восьмой слой поверх**,
нумерация разделов продолжает сквозную (последний занятый — §60 в цикле 5), нумерация задач — `T8-*`.

**Оргдолг, не мой домен, но обязан быть сделан первым (SPEC §0, блок ⚠️):**
`git show 7b382d8:SPEC.md > SPEC_CYCLE5_LEGAL.md` + коммит. Иначе ссылки из `CURRENT_STATE.md` и
`LEGAL_REVIEW.md` на «SPEC цикла 5» висячие. Исполнитель — devops-engineer (у архитектора в этой роли
нет git-инструментов). **Это же правило применено к настоящему документу:** цикл 8 не трогает
`ARCHITECTURE.md`/`API_CONTRACT.md`, а кладёт рядом `ARCHITECTURE_CYCLE8.md`/`API_CONTRACT_CYCLE8.md`.

---

## 61. Что цикл меняет и чего не меняет — в одном экране

| Слой | Меняется? | Что именно |
|---|---|---|
| HTTP-контракт API | **нет** | ноль новых/изменённых эндпоинтов. Зафиксировано машиночитаемо — `contracts/cycle8/servicebooking-invariant.openapi.yaml` |
| Модель данных (сущности, миграции) | **нет** | ни одной новой миграции. Цикл не добавляет таблиц и полей |
| Бизнес-логика `ServiceBooking.API` | **почти нет** | одна правка: путь каталога логов становится конфигурируемым (§71.4). Всё остальное — тестовая обвязка |
| `ServiceBooking.Tests` (инфраструктура) | **да, ядро цикла** | `TestDatabaseFixture` переписывается, пять фабрик получают свой набор данных |
| Новый проект `ServiceBooking.TestKit` | **да** | единственное место, где живут ключ прогона, имена баз, защита от сноса, подметальщик, `status` |
| `docker-compose.yml` (dev) | **да** | явное имя проекта, параметризованные порты/том/база |
| `frontend/vite.config.ts` | **да, ~6 строк** | прокси привязывается к тому же файлу окружения |
| `deploy/ci/smoke.sh` | **почти нет** | убрать хардкод `localhost:5000` из шапки-инструкции; `BASE_URL` уже единственная точка |
| `.github/workflows/ci.yml` | **да, немного** | смысл `SERVICEBOOKING_TEST_CONNECTION` уточняется, добавляется проверка пинов образа |
| Боевой контур (`docker-compose.prod.yml`, `deploy/*`, nginx, бэкапы) | **нет** | не трогаем вообще (SPEC §«Чего цикл НЕ трогает») |

**Эталон приёмки не меняется: 591 / 465 / 181 зелёных.** Ни один тест не удаляется и не меняет смысл;
меняются только фабрики, фикстуры и три места, где тест жёстко знал общий ресурс (§71.5).

---

## 62. Принципы цикла и ответ на П1–П3

**П1 подтверждаю и закладываю в решение: Docker обязателен.** Локальный Postgres перестаёт быть
предусловием и перестаёт быть *дефолтом* (§69). Цена: у кого нет Docker — прогон не стартует, но
падает с внятным сообщением и подсказкой про `SERVICEBOOKING_TEST_CONNECTION`, а не молча сносит
чужую базу.

**П2 подтверждаю: параллелизма внутри набора нет.** `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
в `ServiceBooking.Tests/AssemblyInfo.cs` **остаётся как есть**. Это прямо записано в §80 как
запрет-на-соблазн (R7): любая правка, снимающая этот атрибут, — вне цикла 8.

**П3 подтверждаю с уточнением в лучшую сторону:** решение тратит **один** контейнер Postgres на
прогон (не пять), ~120–180 МБ RSS. Два одновременных прогона — ~300 МБ плюс два Ryuk-контейнера по
~10 МБ. Потолок NFR (~1 ГБ) не достигается даже при трёх прогонах.

**Четвёртый принцип, свой:** *«ни одного пятого механизма конфигурации»* (Q5). Цикл не заводит новых
способов настраивать проект. Всё, что появляется, — это переменные в уже существующем формате
`.env`-файла компоуза, плюс одна уже существующая переменная `SERVICEBOOKING_TEST_CONNECTION`.

**Пятый принцип:** *«прогон из Rider работает кнопкой»* (NFR, блокирующее). Поэтому ключ прогона,
подъём контейнера и имена баз вычисляются **внутри тест-процесса**, а не приходят из Makefile,
shell-профиля или переменных окружения. Внешние переменные — только опциональные переопределения.

---

## 63. T8-0 — замер «до». Делается ПЕРВЫМ, до единой правки

Без него критерий US-91 («+20 % максимум») непроверяем — это прямо сказано в SPEC §5 и §0.

```bash
# на HEAD 7b382d8, до любых правок цикла
git switch --detach 7b382d8
dotnet build ServiceBooking.sln -c Release -warnaserror

# три замера подряд, берём МЕДИАНУ (не минимум и не среднее — первый прогон греет кеши)
for i in 1 2 3; do
  /usr/bin/time -p dotnet test ServiceBooking.UnitTests --no-build -c Release 2>&1 | tail -3
done
for i in 1 2 3; do
  /usr/bin/time -p dotnet test ServiceBooking.Tests --no-build -c Release 2>&1 | tail -3
done
```

Что записать в `ARCHITECTURE_CYCLE8.md` §63.1 (таблицу заполняет исполнитель T8-0, **не архитектор**):

| Метрика | Значение на `7b382d8` | Порог цикла |
|---|---|---|
| `dotnet test ServiceBooking.Tests`, локально (медиана 3) | **178 с** (одиночный замер, см. §63.1) | +30 с абсолютных |
| `dotnet test ServiceBooking.UnitTests`, локально | **~1.2–2.6 с** (591/591 зелёных, медиана 3 прогонов) | **0** (набор не должен получить БД вообще) |
| Джоб `backend` в CI, шаг «Functional tests» | _не заполнено_ (нет доступа к вкладке Actions из этой сессии) | +20 % |
| Джоб `backend` целиком | _не заполнено_ (нет доступа к вкладке Actions из этой сессии) | +20 % |
| Пиковая память Docker при прогоне | 0 (контейнеров нет) | ≤ 1 ГБ при двух прогонах |

Время CI берётся из вкладки Actions (длительность шага), а не пересчитывается локально.
**Замер повторяется после T8-B5** и ещё раз перед мержем; обе цифры идут в отчёт цикла.

### 63.1 T8-0 — фактический замер (backend-developer, 2026-09-22)

Ветка `git worktree` на `7b382d8`, `dotnet build -c Release -warnaserror` (0 предупреждений, 0 ошибок,
6 с). Postgres 16-alpine поднят разовым `docker run` на `sbtest`-подобной базе `servicebooking_test`
на порту 55432 (не через compose — только для замера).

- `dotnet test ServiceBooking.UnitTests --no-build -c Release`, 3 прогона подряд: 2.64 с / 1.29 с / 1.22 с
  (первый греет кеши, как и предупреждает §63). **591/591 зелёных** в каждом прогоне.
- `dotnet test ServiceBooking.Tests --no-build -c Release`: **один** прогон (не три — бюджет времени
  этой сессии не позволил взять медиану; при следующем замере после T8-B5 нужно набрать три и заменить
  число медианой). Результат: `не пройдено 2, пройдено 463, всего 465`, **178.01 с** (`real`).
  Два красных теста — **не приобретены цикла 8**, они красные уже на `7b382d8` (baseline); имена не
  зафиксированы в этом замере (лог не сохранён построчно) — если это критично для сравнения после
  T8-B5, стоит перезапустить с сохранением полного вывода. Раз baseline сам не 465/465, отчёт цикла
  должен явно сверять «сколько было красных ДО» со «сколько после», а не только сравнивать общее число.
- CI-цифры («Джоб `backend`, шаг Functional tests» и «джоб целиком») **не сняты**: у этой сессии нет
  доступа к вкладке GitHub Actions. Нужно, чтобы кто-то с доступом (QA/devops) подставил числа из
  последнего зелёного прогона `backend` на `7b382d8` перед приёмкой T8-Q5.

---

## 64. T8-1 — разведка R2. Тоже до выбора объёма

Риск R2 («часть из 465 тестов молча опирается на общее состояние») — единственный, способный
непредсказуемо раздуть цикл. Меряем его **до** правок, дешёвым способом:

```bash
# каждая коллекция/файл по отдельности на ЧИСТОЙ базе — так, как они будут жить после цикла
for f in LegalConsentTests LegalConsentVersionChangeTests NotificationDispatchTests \
         NotificationDispatchExtraTests NotificationQueueingTests NotificationChannelsTests \
         NotificationWebhookUnsubscribeTests RateLimitingTests UploadsStaticFilesTests \
         SchedulerTests AdminTests CompaniesTests BookingsFlowSmokeTests; do
  dropdb --if-exists servicebooking_test
  echo "=== $f"
  dotnet test ServiceBooking.Tests --no-build -c Release --filter "FullyQualifiedName~$f" 2>&1 | tail -3
done
```

Результат — список `TestCase`-идентификаторов, краснеющих в одиночку. **Ожидания архитектора,
которые разведка должна подтвердить или опровергнуть:**

- `NTF-D*` (коллекция `"NotificationDispatch"`) — зелёные в одиночку (их фабрика сама сидит всё, что
  ей нужно); риск обратный — они краснеют **вместе** с остальными.
- `NTF-W*/U*/L*` — `NTF-L001` читает `logs/app-*.json` и потому чувствителен к соседям (§71.4).
- `LEG-036` — вероятностный сторож гонки, краснота не считается находкой разведки (§9.15).
- `PROF-016/017`, `UPL-001/002` — зависят от `wwwroot`, а не от базы.

Если красных в одиночку больше **десяти** — это сигнал заказчику: объём US-87 больше, чем предполагала
спека, и надо резать (предложение по резу — в §81).

---

## 65. Стек: что добавляется и почему именно это

### 65.1 Добавляется

| Что | Версия | Куда | Зачем |
|---|---|---|---|
| `Testcontainers.PostgreSql` | 3.10.0 | `ServiceBooking.TestKit` | подъём Postgres **из тест-процесса**, случайный порт, авто-уборка через Ryuk |
| `Npgsql` | уже в дереве (транзитивно через `Npgsql.EntityFrameworkCore.PostgreSQL`) | `ServiceBooking.TestKit` | `CREATE DATABASE`/`DROP DATABASE`, чтение метаданных для подметальщика — прямой ADO, без EF |
| Проект `ServiceBooking.TestKit` | net8.0, `OutputType=Exe` | новый, в `ServiceBooking.sln` | библиотека **и** CLI одновременно |

**Больше ничего.** Ни Makefile, ни `just`, ни `docker-compose.test.yml`, ни новый раннер, ни
Playwright, ни Respawn, ни xunit v3.

### 65.2 Почему Testcontainers, а не `docker run` из bash

| Вариант | Почему отклонён |
|---|---|
| bash-обёртка `scripts/test.sh` поднимает Postgres и зовёт `dotnet test` | **убивает запуск из Rider** — R4, блокирующее требование NFR. Кнопка «Run» зовёт `dotnet test` напрямую, обёртку не зовёт никто |
| `docker-compose.test.yml` + `depends_on` | то же самое плюс второй compose-проект, который тоже надо разводить по имени |
| `Testcontainers` | процесс сам поднимает контейнер → кнопка в Rider работает без настройки. Случайный порт хоста → Q4 закрыт для тестов бесплатно. Ryuk → авария (Ctrl+C, kill -9) убирает за собой без подметальщика |
| Респавн/транзакционный откат вместо базы-на-прогон | не решает задачу: проблема не «грязная база», а **две базы одного имени у двух процессов**. И потребовал бы правки сотен тестов (П2) |
| Одна база, схема-на-прогон (`search_path`) | EF Core миграции жёстко привязаны к схеме `public` в 36 миграциях; переписывание миграций — риск несопоставимый с выигрышем |

### 65.3 Чем платим за Testcontainers

1. **Ryuk-контейнер** (`testcontainers/ryuk`) поднимается рядом. Отключать (`TESTCONTAINERS_RYUK_DISABLED`)
   **запрещено** — именно он закрывает US-86 для аварийно прерванного прогона.
2. **Docker Desktop на macOS стартует контейнер медленнее Linux** (R1). Замер T8-0 → T8-B5 покажет
   реальную цену; ожидание ~3–6 с на прогон при прогретом образе.
3. **Образ должен быть в кеше.** Первый прогон на чистой машине тянет `postgres:16-alpine` (~90 МБ).
   Документируется в README как разовое предусловие; в CI образ и так тянется.

---

## 66. Q1 — единица изоляции. Ответ: **сервер на прогон, база на слот**

Это двухуровневое решение, и оба уровня нужны по разным причинам.

```
машина
 └── прогон #1  (run key = a3f19c7b)                 ← контейнер Postgres, случайный порт хоста
      ├── sbtest_a3f19c7b_template   (миграции накачены один раз)
      ├── sbtest_a3f19c7b_api        ← слот "api":      CustomWebApplicationFactory,
      │                                                  RateLimitTestFactory, NotificationTestFactory,
      │                                                  UploadsStaticFilesTestFactory
      ├── sbtest_a3f19c7b_legal      ← слот "legal":    LegalDocumentsTestFactory
      └── sbtest_a3f19c7b_dispatch   ← слот "dispatch": NotificationDispatchTestFactory
 └── прогон #2  (run key = 5e0bd214)                 ← свой контейнер, свой порт, свои четыре базы
```

**Верхний уровень — контейнер на прогон — решает конфликт прогонов** (US-83, US-84): разные прогоны
физически не видят друг друга, версия СУБД одинакова, порт выбирает ОС.

**Нижний уровень — база на слот — закрывает §9.24d и US-87** в той части, где разделение данных
чистое и бесплатное:

- слот `dispatch` даёт то, ради чего пункт 24d и написан: `NotificationDispatchTask` сканирует
  **все** `Pending`-строки платформы, и теперь в его базе нет ничьих строк, кроме его собственных.
  Фильтрация по своему телефону (лечение цикла 4) остаётся в тестах как второй рубеж, но перестаёт
  быть единственным;
- слот `legal` снимает гонку сидирования суперадмина манифестом со случайной версией — причина,
  описанная в длинном комментарии `LegalDocumentsTestFactory`. Отдельный телефон суперадмина
  (коммит `7a36543`) остаётся, но перестаёт быть **единственной** защитой;
- слот `api` держит остальные четыре сотни тестов, которые уже умеют не мешать друг другу
  (уникальные телефоны/слаги через `ApiTestBase.Unique*`).

**Почему не база на коллекцию.** Коллекций всего две (`"Api"`, `"NotificationDispatch"`), и это не
совпадает с реальной границей конфликта: `LegalDocumentsTestFactory` живёт внутри `"Api"`, но
конфликтует именно как отдельный владелец данных. Слот — это **владелец данных**, а не коллекция
xUnit. Слот объявляется фабрикой, а не тестом.

**Почему не база на фабрику (пять баз).** `RateLimitTestFactory`, `NotificationTestFactory` и
`UploadsStaticFilesTestFactory` создаются **по экземпляру на тест** — это десятки хостов за прогон.
Отдельная база на каждый экземпляр = десятки `CREATE DATABASE` и десятки прогонов сидирования
(роли + суперадмин + города) — прямой удар по бюджету +20 %. Конфликтов данных у них не
зафиксировано ни одного: они разведены конфигурацией хоста, а не содержимым базы. Если разведка T8-1
покажет обратное — слот добавляется одной строкой в `TestSlot` (§66.2), это заложенная точка роста.

### 66.1 Схема имён — единственный источник

Всё в `ServiceBooking.TestKit/TestDatabaseNaming.cs`:

```
sbtest_<runkey>_<slot>
  runkey : ^[0-9a-f]{8}$        — §67
  slot   : ^[a-z][a-z0-9]{0,11}$ — "template" | "api" | "legal" | "dispatch"
итого ≤ 29 символов при лимите Postgres в 63 — запас есть
```

Префикс `sbtest_` (а не `servicebooking_test_`) выбран нарочно: **ни одно существующее имя базы в
проекте не начинается с `sbtest_`**, поэтому «дропаем только то, что начинается с `sbtest_`» —
проверяемое утверждение, а не надежда.

### 66.2 Слоты в коде

```csharp
// ServiceBooking.Tests/Infrastructure/TestSlot.cs
public static class TestSlot
{
    public const string Api      = "api";
    public const string Legal    = "legal";
    public const string Dispatch = "dispatch";
    public static readonly string[] All = [Api, Legal, Dispatch];
}
```

Все три базы создаются **сразу после накатывания шаблона**, а не лениво: детерминированно, и
`CREATE DATABASE ... TEMPLATE` гарантированно выполняется, пока к шаблону никто не подключён (§68).

---

## 67. Q2 — откуда берётся ключ прогона

**Ответ: вычисляется один раз внутри тест-процесса, статикой; переменная окружения — только
переопределение.**

```csharp
// ServiceBooking.TestKit/TestRunKey.cs
public static class TestRunKey
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var external = Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_RUN_KEY");
        if (!string.IsNullOrWhiteSpace(external))
            return Normalize(external);                  // CI может задать свой, чтобы логи сходились
        return Guid.NewGuid().ToString("N")[..8];        // 8 hex — 4 млрд вариантов, коллизий нет
    }
}
```

**Почему статика, а не переменная окружения по умолчанию:**

- `dotnet test ServiceBooking.Tests` поднимает **один** процесс testhost на сборку; все пять фабрик,
  обе коллекции и фикстуры живут в нём. Статика видна всем — Q2 закрыт без единой внешней настройки;
- **это и есть ответ на R4**: Rider жмёт ту же кнопку `dotnet test` в том же процессе. Ничего
  настраивать не надо, ни в shell-профиле, ни в конфигурации запуска;
- подметальщик — **другой** процесс и ключ текущего прогона не знает и знать не должен: он работает
  с метаданными на стороне Docker/Postgres (§70), а не с переменной.

**Печать ключа (US-83, третий критерий).** `TestRunEnvironment` при инициализации делает
`Console.WriteLine` **первой строкой**:

```
[sb-test] run=a3f19c7b  mode=container  server=localhost:54312  databases=sbtest_a3f19c7b_{api,legal,dispatch}
```

и дублирует в `TestResults/sb-test-run.json` (машиночитаемо, для CI-артефакта и для QA).

**Ограничение, которое надо знать:** запуск двух тест-проектов одной командой
(`dotnet test ServiceBooking.sln`) даёт **два** процесса и, соответственно, два ключа.
`ServiceBooking.UnitTests` базы не касается вовсе (§74.2), так что практического эффекта нет, но в
документации это оговаривается.

---

## 68. Шаблонная база — как бюджет +20 % остаётся выполнимым

Наивная схема «три базы × `MigrateAsync` по 36 миграций» утроила бы самую дорогую часть старта.
Вместо этого:

1. `CREATE DATABASE sbtest_<key>_template` — пусто;
2. один `AppDbContext.Database.MigrateAsync()` против шаблона (та же цена, что сегодня у
   единственной базы), **соединение закрывается явно**;
3. `CREATE DATABASE sbtest_<key>_api TEMPLATE sbtest_<key>_template` ×3 — это файловая копия
   каталога на стороне Postgres, доли секунды на пустой схеме;
4. хосты стартуют как сегодня; их собственный `MigrateAsync()` в `Program.cs` видит применённые
   миграции и становится быстрой проверкой метаданных — **`Program.cs` не меняется**;
5. сидирование ролей/суперадмина/городов остаётся **за хостом**, как сегодня, и происходит в каждой
   базе своё — именно это и нужно (у каждого слота свой суперадмин, §71.1).

SPEC §3 разрешала шаблонную базу «только если получится бесплатно». Здесь она получается бесплатно и
работает не на ускорение, а на **сохранение** текущей скорости при утроении числа баз.

**Аварийный выключатель:** `SERVICEBOOKING_TEST_NO_TEMPLATE=1` → каждая база мигрируется
самостоятельно. Нужен ровно на один случай: если `CREATE DATABASE ... TEMPLATE` упрётся в чужие
соединения на внешнем сервере CI.

---

## 69. Q8 + US-85 — дефолтная строка удаляется, появляется fail-fast

### 69.1 Дефолт удаляется

Строка `"Host=localhost;Database=servicebooking_test;Username=postgres;Password="` из
`TestDatabaseFixture` **удаляется целиком**. Рекомендация аналитика принята: именно она делает
«снести чужую базу» поведением по умолчанию.

Новый порядок разрешения источника сервера:

| Условие | Режим | Что происходит |
|---|---|---|
| задан `SERVICEBOOKING_TEST_CONNECTION` | `external` | сервер берётся оттуда; контейнер **не поднимается** (путь CI сохранён — US-84) |
| не задан, Docker доступен | `container` | Testcontainers поднимает `postgres:16-alpine` на случайном порту |
| не задан, Docker недоступен | **отказ** | понятное сообщение (ниже), ни одного разрушительного действия |

Сообщение при отказе — текст фиксируется как контракт (его будет грепать приёмка):

```
[sb-test] Не могу подготовить базу для прогона.
  Docker недоступен: <текст ошибки от docker>
  Варианты:
    1) запустить Docker Desktop и повторить — это штатный путь;
    2) указать свой сервер PostgreSQL:
       SERVICEBOOKING_TEST_CONNECTION="Host=localhost;Port=5432;Username=postgres;Password=..."
       (имя базы в строке игнорируется, прогон создаёт свои базы sbtest_<ключ>_<слот>)
  Подробности: docs/testing-isolation.md
```

### 69.2 Семантика `SERVICEBOOKING_TEST_CONNECTION` уточняется, значение CI остаётся рабочим

Переменная становится **строкой подключения к серверу**, а не к базе. Компонент `Database` из неё
используется только как «куда подключиться, чтобы выполнить `CREATE DATABASE`», и **всегда
переписывается на `postgres`**. Поэтому сегодняшнее значение CI
(`Host=localhost;Database=servicebooking_test;Username=postgres;Password=postgres`) продолжает
работать без правки — но в `ci.yml` его всё равно упрощаем до `Database=postgres`, чтобы не вводить в
заблуждение (T8-O3).

### 69.3 Защита от сноса — чистая функция, юнит-тесты (US-85)

`ServiceBooking.TestKit/TestDatabaseNaming.cs`, покрывается
`ServiceBooking.UnitTests/TestDatabaseNamingTests.cs` рядом с `DeploymentSafetyChecksTests`:

```csharp
/// <summary>Единственное место, решающее «эту базу можно удалить».
/// Никакой другой код в репозитории не имеет права вызывать DROP DATABASE / EnsureDeletedAsync.</summary>
public static class TestDatabaseNaming
{
    public static readonly string[] NeverDrop =
        ["postgres", "template0", "template1", "servicebooking", "servicebooking_test"];

    public static bool IsDisposable(string databaseName);        // ^sbtest_[0-9a-f]{8}_[a-z][a-z0-9]{0,11}$ И не в NeverDrop
    public static void EnsureDisposable(string databaseName);    // иначе TestSafetyException с текстом ниже
    public static void EnsureOwnedByThisRun(string databaseName); // сегмент runkey == TestRunKey.Current
}
```

Три инварианта, которые проверяют юнит-тесты:

1. **Положительные:** `sbtest_a3f19c7b_api`, `sbtest_00000000_template` → `true`.
2. **Отрицательные, обязательные к красноте:** `servicebooking`, `servicebooking_test`, `postgres`,
   `template1`, `SBTEST_A3F19C7B_API` (регистр), `sbtest_zzz_api` (не hex),
   `sbtest_a3f19c7b_api; DROP DATABASE servicebooking` (инъекция), `""`, `null`,
   `sbtest_a3f19c7b_` (пустой слот), имя длиннее 63.
3. **Не-снос по чужому ключу:** `EnsureOwnedByThisRun("sbtest_deadbeef_api")` при текущем ключе
   `a3f19c7b` → бросает.

Текст исключения (US-85, «называет: какую базу увидел, какой признак ожидался, что сделать»):

```
[sb-test] Отказ: прогон попытался удалить базу "servicebooking", не являющуюся одноразовой тестовой.
  Ожидался формат: sbtest_<8 hex — ключ прогона>_<слот>, и имя вне списка защищённых
    (postgres, template0, template1, servicebooking, servicebooking_test).
  Что сделать: проверьте SERVICEBOOKING_TEST_CONNECTION — с цикла 8 эта переменная задаёт СЕРВЕР,
    а не базу; имя базы прогон выбирает сам. Ничего не удалено.
```

**Где стоит проверка.** Единственные два места, вызывающие `DROP DATABASE` во всём репозитории — оба
в `TestKit.TestDatabaseLease`:

- `DropAsync` — для teardown **своего** прогона (той же строкой первым делом `EnsureOwnedByThisRun`,
  которая по построению требует совпадения ключа базы с `TestRunKey.Current` **текущего процесса**);
- `DropLeakedAsync` — для подметальщика (§70.3), который по определению работает из **другого**
  процесса со своим ключом и убирает за чужими прогонами, поэтому `EnsureOwnedByThisRun` для него
  в принципе не может пройти. `DropLeakedAsync` проверяет только `EnsureDisposable` (имя формально
  одноразовое) — безопасность обеспечивается тем, что вызывающая сторона (`Sweeper`) сама применяет
  классификацию dead/alive/undetermined до вызова, а не тем, что удаляется «своя» база.

(Ранняя редакция этого раздела описывала только `DropAsync` с `EnsureOwnedByThisRun` и для
подметальщика — это делало `sweep --apply` неработоспособным в принципе: TestSafetyException на
каждой базе, exit 3, ничего не удаляется. Зафиксировано ревью цикла 8 как блокирующая находка №1.)

`EnsureDeletedAsync` из `TestDatabaseFixture` **удаляется** и в приёмке грепается на отсутствие:

```bash
grep -rn "EnsureDeletedAsync\|EnsureDeleted(" --include=*.cs . | grep -v TestKit/   # должно быть пусто
```

### 69.4 Ограничение, которое честно называю

Защита по **имени** не защищает от «строка подключения указывает на боевой хост». Если оператор
подставит боевой хост, прогон создаст там базы `sbtest_*` и удалит только их — боевая база уцелеет,
но на боевом сервере появится мусор и нагрузка. Полная защита («это не боевой хост») требовала бы
списка боевых хостов в репозитории, чего мы не хотим. Остаточный риск фиксируется в `CURRENT_STATE.md §9`
одной строкой. Смягчение — дешёвое и входит в объём: в режиме `external` прогон печатает
`server=<host>:<port> user=<user>` первой строкой (§67), так что «я гоню тесты не туда» видно сразу.

---

## 70. Q3 + US-86 — уборка и подметальщик

Три рубежа, от самого надёжного к самому последнему.

### 70.1 Рубеж 1 — штатное завершение

`TestRunEnvironment` считает ссылки: каждая коллекционная фикстура берёт слот, последняя
освобождённая роняет всё. В режиме `container` — `container.DisposeAsync()` (вместе с ним исчезают и
все базы). В режиме `external` — `DROP DATABASE ... WITH (FORCE)` по каждому слоту и шаблону через
`TestDatabaseLease.DropAsync` (§69.3). Работает и при красных тестах: падение теста не отменяет
`DisposeAsync` фикстуры.

### 70.2 Рубеж 2 — аварийное завершение: Ryuk

Ctrl+C, `kill -9`, таймаут CI, падение testhost — `DisposeAsync` не вызывается. Это закрывает Ryuk:
Testcontainers поднимает `testcontainers/ryuk`, держит с ним TCP-соединение на всё время процесса и
регистрирует у него фильтр по меткам. Рвётся соединение — Ryuk убивает помеченные контейнеры сам,
через ~10 секунд. **Для режима `container` этого достаточно: ни одного ручного действия не нужно.**

`TESTCONTAINERS_RYUK_DISABLED=true` объявляется запрещённой настройкой; приёмка грепает, что её нет
ни в `ci.yml`, ни в документации.

### 70.3 Рубеж 3 — подметальщик, для режима `external` и для «Ryuk тоже убили»

```bash
dotnet run --project ServiceBooking.TestKit -- sweep            # СУХОЙ прогон, ничего не удаляет
dotnet run --project ServiceBooking.TestKit -- sweep --apply    # удаляет
dotnet run --project ServiceBooking.TestKit -- sweep --apply --max-age 30m
```

Сухой прогон по умолчанию — приём уже принят в проекте (`data-retention`, цикл 5), R3 закрыт им же.

**Формальный критерий «мёртвости» (Q3) — конъюнкция, а не «или»:**

*Для контейнера:*
1. несёт метку `com.servicebooking.test=1` **и** `com.servicebooking.test.run-key` (наш);
2. **и** нет процесса с PID из метки `com.servicebooking.test.host-pid`
   (`Process.GetProcessById` → `ArgumentException`);
3. **и** метка `com.servicebooking.test.started-at` старше `--max-age` (по умолчанию **2 часа**).

*Для базы на внешнем сервере:*
1. имя проходит `TestDatabaseNaming.IsDisposable` (§69.3);
2. **и** `pg_stat_activity` не показывает **ни одного** подключения к ней;
3. **и** метаданные в `COMMENT ON DATABASE` (пишутся при создании, JSON:
   `{"runKey","host","pid","startedAtUtc","workdir"}`) старше `--max-age`;
4. **и** `runKey` ≠ ключу любого живого процесса (проверяется п. 2 + п. 3; при
   недоступности PID-проверки — только по возрасту).

Пункт 3 в обоих случаях — **страховка от повторного использования PID**: PID переиспользуется
операционной системой, и «PID жив» может означать чужой процесс. Два часа — с большим запасом больше
любого прогона (сегодня ~несколько минут) и меньше рабочего дня.

**Ресурс, о котором нельзя сказать наверняка** (метки есть, PID жив, возраст мал, соединений нет) —
**не трогаем никогда**, но печатаем отдельной секцией:

```
[sb-sweep] Мёртвые (будут удалены с --apply):
  container sb-pg-5e0bd214  age=4h12m pid=88123(нет)     
  database  sbtest_5e0bd214_api  age=4h12m conns=0
[sb-sweep] Живые (не трогаю):
  container sb-pg-a3f19c7b  age=0h03m pid=91004(жив)
[sb-sweep] Неопределённые (не трогаю, проверьте руками):
  database  sbtest_11ff22aa_legal  age=0h40m conns=0  runKey чужой, процесса не видно
```

Секция «неопределённые» — это то, что превращает «подметальщик снёс чужой прогон» (R3) из аварии в
строчку вывода. Удалять её содержимое можно только точечно: `sweep --apply --run-key 11ff22aa`.

### 70.4 US-86, третий критерий: «подметальщик работает, рядом идёт живой прогон»

Проверяется на приёмке буквально: запустить `sweep --apply` во время полного прогона и убедиться,
что прогон зелёный и его контейнер/базы на месте. Это тест-кейс QA, а не автотест (автотест этого
класса потребовал бы Docker внутри юнит-набора — прямо запрещено §74.2).

---

## 71. US-87 + Q6 — конец общим ресурсам внутри прогона

Единая точка настройки хоста, которую сегодня пять фабрик копируют построчно:

```csharp
// ServiceBooking.Tests/Infrastructure/TestHostSettings.cs  — НОВЫЙ
public static class TestHostSettings
{
    /// <param name="slot">какой базе слота принадлежит хост (TestSlot.*)</param>
    /// <param name="factoryTag">короткий уникальный тег типа фабрики: "api"|"ratelimit"|"legal"|"ntf"|"dispatch"|"uploads"</param>
    public static TestHostIdentity Apply(IWebHostBuilder builder, string slot, string factoryTag);
}

public sealed record TestHostIdentity(
    string ConnectionString,   // база слота
    string SuperAdminPhone,    // свой у каждого factoryTag
    string SuperAdminEmail,
    string SuperAdminPassword, // общий, не секрет
    string PublicRoot,         // $TMPDIR/sb-test/<runkey>/<factoryTag>/public
    string PrivateRoot,        // .../private
    string StateRoot,          // .../state
    string LogDirectory,       // .../logs
    string LegalRoot);         // копия App_Data/legal или собственный манифест
```

Это убирает ~60 строк дублирования из пяти фабрик и делает «правилом то, что было исключением».

### 71.1 Суперадмин — свой у каждого типа фабрики

Приём `LegalDocumentsTestFactory` (коммит `7a36543`) становится правилом. Телефоны распределяются
детерминированно, чтобы тест мог их предсказать:

| factoryTag | SuperAdmin:Phone | SuperAdmin:Email |
|---|---|---|
| `api` | `+70000000001` (не меняем — на него завязаны хелперы) | `superadmin@test.local` |
| `ratelimit` | `+70000000002` | `superadmin-ratelimit@test.local` |
| `ntf` | `+70000000003` | `superadmin-ntf@test.local` |
| `uploads` | `+70000000004` | `superadmin-uploads@test.local` |
| `dispatch` | `+70000000005` | `superadmin-dispatch@test.local` |
| `legal` | `+70000099999` (оставляем, чтобы не трогать работающее) | `superadmin-legal-isolated@test.local` |

**Следствие для тестов, обязательное к правке (иначе 465 не сойдётся):**
`ApiTestBase.LoginAsSuperAdminAsync()` и `NotificationTestBase.LoginAsSuperAdminAsync()` сегодня
логинятся литералом `"+70000000001"`. Обе становятся:

```csharp
protected Task<AuthResponseDto> LoginAsSuperAdminAsync() =>
    LoginAsync(Factory.Identity.SuperAdminPhone, Factory.Identity.SuperAdminPassword);
```

То же в `NotificationDispatchExtraTests.LoginAsSuperAdminAsync(factory)`. Приёмочный греп:

```bash
grep -rn '"+70000000001"' ServiceBooking.Tests/   # допустим ТОЛЬКО в TestHostSettings.cs
```

### 71.2 Телефоны тестовых данных

`ApiTestBase.UniquePhone()`/`NotificationTestBase.UniquePhone()` уже Guid-производные и внутри
прогона не сталкиваются. **Ничего не меняем** — и важно, что теперь они не сталкиваются и **между**
прогонами, потому что базы разные. Единственная правка: обе копии метода съезжают в
`TestPhones.Unique()` (одна реализация вместо двух), чтобы диапазон `+7000000000x` из §71.1 был
гарантированно недостижим для сгенерированного телефона (генератор даёт `+79…`).

### 71.3 Файловые ресурсы (Q6)

Корень на прогон: `Path.Combine(Path.GetTempPath(), "sb-test", TestRunKey.Current)`. Дальше — по
`factoryTag`. Что куда:

| Ресурс | Сегодня | Становится |
|---|---|---|
| `wwwroot/uploads` | общий репозиторный каталог | `Storage:PublicRoot` = `<run>/<tag>/public` |
| `App_Data/private-uploads` | общий | `Storage:PrivateRoot` = `<run>/<tag>/private` |
| отпечаток ключа `App_Data/state` | общий (в `Testing` проверка no-op, но каталог создаётся) | content-root не трогаем; `DeploymentSafetyChecks.ValidateChannelKeyFingerprint` в `Testing` и так no-op — **правок не требует** |
| `App_Data/legal` | общий; одна фабрика уже копирует во временный | копия во временный каталог **у каждой** фабрики, `Legal:Root` |
| `logs/app-*.json` | общий, относительно CWD процесса | `Logs:Directory` = `<run>/<tag>/logs` — см. §71.4 |

Каталог `<run>` удаляется в `TestRunEnvironment.DisposeAsync` и подметается `sweep` по тому же
критерию возраста.

**Почему `Legal:Root` копируется даже тем фабрикам, которые манифест не правят:** ради US-87
(«результат не зависит от того, какая коллекция стартовала первой») и потому, что копия каталога из
десятка мелких HTML — микросекунды. Исключение — `UploadsStaticFilesTestFactory` с
`contentRootOverride`: у неё уже есть собственная логика указания на репозиторный манифест, её
оставляем как есть (иначе сломаем `UPL-001/002`, чей предмет — именно резолвинг корней).

### 71.4 Логи — единственная правка в `ServiceBooking.API`

`Program.cs` сегодня: `.WriteTo.File(new CompactJsonFormatter(), Path.Combine("logs", "app-.json"), …)` —
путь относительно рабочего каталога процесса, неконфигурируемый.

Становится:

```csharp
var logDirectory = builder.Configuration["Logs:Directory"] is { Length: > 0 } d ? d : "logs";
... .WriteTo.File(new CompactJsonFormatter(), Path.Combine(logDirectory, "app-.json"), …)
```

Дефолт `"logs"` → поведение Development/Production **не меняется ни на байт**; `.gitignore`,
`docker-compose.prod.yml`, ротация, `retainedFileCountLimit` — всё как было. Риск минимальный, но он
есть, поэтому: задача T8-B7 отдельная, и в `docker-build` смоук остаётся проверкой того, что образ
по-прежнему стартует.

**Следствие для теста `NTF-L001`** (`NotificationWebhookUnsubscribeTests.FindLogsDirectory`): он
ищет `logs/` вверх от `AppContext.BaseDirectory`. Становится `Factory.Identity.LogDirectory`.
Побочная выгода — тест перестаёт зависеть от чужих строк и от накопленных за дни файлов (его
собственный комментарий признаёт это слабым местом), и его «baseline length diff» становится не
нужен, но **мы его не убираем** — правка минимальная, смысл теста не меняется.

### 71.5 Полный список мест, где тест сегодня жёстко знает общий ресурс

Это и есть объём US-87 в файлах (проверяется разведкой T8-1, дополняется по её итогам):

1. `ApiTestBase.LoginAsSuperAdminAsync` — литерал телефона;
2. `NotificationTestBase.LoginAsSuperAdminAsync` — литерал телефона;
3. `NotificationDispatchExtraTests.LoginAsSuperAdminAsync(factory)` — литерал телефона;
4. `NotificationWebhookUnsubscribeTests.FindLogsDirectory` — общий каталог логов;
5. `NotificationQueueingTests` (комментарий на строке ~210) — фильтрация «не своих» Pending-строк:
   **оставляем как есть**, слот `dispatch` делает её избыточной, но не вредной;
6. `NotificationTestBase` — `[Collection("Api")]` **остаётся** (его роль «дождаться готовности базы»
   сохраняется, только база теперь слотовая).

---

## 72. Q5 + Q7 — конфигурация: ни одного нового механизма

### 72.1 Q7: точка входа остаётся `dotnet test`

Никакого `make test`, никакого `scripts/test.sh`. Причина — R4/NFR (запуск из Rider кнопкой).
Дополнительные команды (`status`, `sweep`, `doctor`) — **отдельные** `dotnet run`, они не на пути
прогона и потому Rider не ломают.

### 72.2 Q5: один файл окружения на рабочую копию, и тот необязательный

**`.env` в корне рабочей копии** — тот самый файл, который `docker compose` подхватывает сам, без
`--env-file`. Он уже в `.gitignore` (строка 15). Рядом кладётся **закоммиченный**
`.env.dev.example` с документацией переменных.

**Главное свойство: файл не обязателен.** Все переменные в `docker-compose.yml` записаны с
дефолтами (`${SB_API_PORT:-5000}`), поэтому `docker compose up` в свежем клоне работает ровно как
сегодня — это прямое требование US-88.

| Переменная | Дефолт | Кто читает |
|---|---|---|
| `SB_PROJECT_NAME` | `servicebooking` | `docker-compose.yml` → `name:` |
| `SB_DB_PORT` | `5432` | compose (публикация порта Postgres) |
| `SB_DB_NAME` | `servicebooking` | compose (`POSTGRES_DB` + строка подключения api) |
| `SB_API_PORT` | `5000` | compose (публикация порта api), `vite.config.ts` |
| `SB_WEB_PORT` | `5173` | `vite.config.ts` (`server.port`), `AllowedOrigins` в compose |
| `SB_VOLUME_SUFFIX` | пусто | имя тома `postgres_data${SB_VOLUME_SUFFIX}` |
| `SERVICEBOOKING_TEST_CONNECTION` | не задана | `ServiceBooking.Tests` (режим `external`) — **существующая**, не новая |
| `SERVICEBOOKING_TEST_RUN_KEY` | не задана | `TestRunKey` — только для корреляции логов в CI |

**Почему не отдельный файл для тестов.** Тестам файл окружения не нужен вовсе (§67): они
самодостаточны. Второй файл появился бы ради нуля переменных.

**Отношение к уже существующим механизмам** (Q5 требовала это проговорить):

| Существующий механизм | Что с ним |
|---|---|
| `.env` на боевой машине (`--env-file .env` у `docker-compose.prod.yml`) | не пересекается: разные машины, разные compose-файлы, префикс `SB_` в боевом `.env` не встречается. Оговорка попадает в `DEPLOY.md` одной строкой |
| `.env.production.example` | не трогаем |
| `appsettings.Testing.json` | не трогаем: он про поведение приложения (планировщик, лимиты), а не про адреса |
| `frontend/.env`, `VITE_API_TARGET` | сохраняется как приоритетное переопределение (§73.2) |

### 72.3 Приоритет разрешения (фиксируется как контракт, см. `API_CONTRACT_CYCLE8.md §85`)

1. явная переменная процесса (`VITE_API_TARGET`, `SERVICEBOOKING_TEST_CONNECTION`);
2. значение из `.env` рабочей копии;
3. встроенный дефолт.

---

## 73. US-88, US-89, Q4 — dev-стек и фронтенд

### 73.1 Q4: динамические порты у тестов, фиксированные у dev-стека

Вопрос имеет **два разных ответа для двух разных потребителей**, и это не компромисс, а суть:

- **тестовый Postgres — динамический порт** (Testcontainers, порт 0). Его адрес никто руками не
  набирает; конфликт исключён физически; предсказуемость не нужна;
- **dev-стек — фиксированный порт из `.env` с дефолтом**. `localhost:5000` в браузере обязано
  оставаться предсказуемым; «открой адрес из вывода команды» — регресс эргономики.

Диагностика конфликта фиксированных портов — задача `status` (§73.4), а не динамических портов.

### 73.2 `docker-compose.yml` — целевое содержимое

```yaml
# Имя compose-проекта задано ЯВНО, а не выводится из имени каталога — ловушка §9 P0-6
# (два стека схлопнулись по именам сервисов на боевой машине). Переопределяется через SB_PROJECT_NAME
# в .env рабочей копии; см. .env.dev.example.
name: ${SB_PROJECT_NAME:-servicebooking}

services:
  postgres:
    image: postgres:16-alpine      # пин сверяется deploy/ci/check-image-pins.sh — §74.3
    environment:
      POSTGRES_DB: ${SB_DB_NAME:-servicebooking}
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "${SB_DB_PORT:-5432}:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 5s
      retries: 5

  api:
    build:
      context: .
      dockerfile: ServiceBooking.API/Dockerfile
    ports:
      - "${SB_API_PORT:-5000}:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=${SB_DB_NAME:-servicebooking};Username=postgres;Password=postgres
      - Jwt__Key=CHANGE_ME_TO_A_LONG_SECRET_KEY_AT_LEAST_32_CHARS
      - Jwt__Issuer=ServiceBooking
      - Jwt__Audience=ServiceBookingClient
      - AllowedOrigins=http://localhost:${SB_WEB_PORT:-5173}
    depends_on:
      postgres:
        condition: service_healthy

volumes:
  postgres_data:
    name: ${SB_PROJECT_NAME:-servicebooking}_postgres_data${SB_VOLUME_SUFFIX:-}
```

Том получает **явное имя**, включающее имя проекта: `docker compose down -v` в копии B не может
задеть данные копии A (US-88, второй критерий). Секретов не добавилось: `POSTGRES_PASSWORD=postgres`
и плейсхолдер `Jwt__Key` — те же заглушки, что и сегодня.

### 73.3 `frontend/vite.config.ts` — US-89

```ts
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  // envDir: '..' — тот же .env рабочей копии, что читает docker compose. Приоритет:
  // VITE_API_TARGET (явное переопределение, механизм из цикла 3 сохраняется) > SB_API_PORT из .env > 5000.
  const env = loadEnv(mode, '..', ['VITE_', 'SB_'])
  const apiPort = env.SB_API_PORT ?? '5000'
  const apiTarget = process.env.VITE_API_TARGET ?? env.VITE_API_TARGET ?? `http://localhost:${apiPort}`
  const webPort = Number(env.SB_WEB_PORT ?? 5173)

  return {
    plugins: [react()],
    server: {
      port: webPort,
      // strictPort НЕ ставим: занятый порт → Vite берёт следующий свободный и печатает адрес.
      // Это и есть поведение, которого требует US-89, второй критерий.
      proxy: {
        '/api':     { target: apiTarget, changeOrigin: true },
        '/uploads': { target: apiTarget, changeOrigin: true },
      },
    },
  }
})
```

`vitest.config.ts` **не трогаем** — он намеренно отдельный, и 181 фронтовый тест прокси не касается.

### 73.4 US-90 — команда «что занято»

```bash
dotnet run --project ServiceBooking.TestKit -- status
dotnet run --project ServiceBooking.TestKit -- status --json     # схема: contracts/cycle8/testkit-status.schema.json
```

Печатает: ключ рабочей копии (`SB_PROJECT_NAME` из `.env` или дефолт), имя compose-проекта, занятые
порты (`SB_DB_PORT`/`SB_API_PORT`/`SB_WEB_PORT` + факт занятости через попытку `bind`), имя dev-базы,
список живых контейнеров `com.servicebooking.test=1` и баз `sbtest_*` с отметкой «мой/чужой»
(мой = `workdir` метки совпадает с текущим каталогом).

**Команда ничего не меняет** — только чтение и попытки `bind` на порт с немедленным закрытием.
Безопасна при любом состоянии машины, включая отсутствующий Docker (тогда секция контейнеров:
`docker недоступен`). Это утверждение — приёмочный критерий, а не пожелание: у `status` нет ни одной
ветки кода, вызывающей `DROP`, `rm`, `stop`.

### 73.5 Q9 — GlitchTip и прочие вспомогательные стеки

На машине разработчика их **нет** (`docker-compose.glitchtip.yml` — инструмент боевой машины).
Решение: в `docker-compose.glitchtip.yml` добавляются **только** `name: ${SB_PROJECT_NAME:-servicebooking}-glitchtip`
и `${SB_GLITCHTIP_PORT:-8000}` — две строки, тот же приём, нулевой риск. В MVP-критерии цикла это не
входит; делается попутно в T8-O2 или откладывается без ущерба.

---

## 74. US-91, US-92 — CI и смоук

### 74.1 `ci.yml`, джоб `backend`

Меняется **минимально**:

```yaml
    env:
      # С цикла 8 это строка подключения к СЕРВЕРУ; имя базы прогон выбирает сам (sbtest_<ключ>_<слот>).
      SERVICEBOOKING_TEST_CONNECTION: "Host=localhost;Database=postgres;Username=postgres;Password=postgres"
      SERVICEBOOKING_TEST_RUN_KEY: "${{ github.run_id }}"   # необязательно; делает логи корреляционными
```

Service-контейнер `postgres:16` остаётся — благодаря этому CI **не** поднимает контейнер из
тест-процесса (Docker-in-Docker не нужен), и путь US-84 «внешний сервер» остаётся рабочим и
проверяемым в CI на каждом коммите. `concurrency` **остаётся** (не наш предмет), но требование
US-91 «два запуска не мешают друг другу и без `concurrency`» выполняется: у каждого запуска свой
раннер, свой service-контейнер и свой ключ прогона.

**Состав проверок не меняется ни на одну:** `-warnaserror`, юнит-тесты первыми, функциональные, lint,
`tsc --noEmit`, vitest, build, `docker-build` + `smoke.sh`.

Добавляется один шаг (быстрый, bash):

```yaml
      - name: Check image pins
        run: bash deploy/ci/check-image-pins.sh
```

### 74.2 Юнит-набор обязан остаться без базы и без Docker

`ServiceBooking.UnitTests` получает `ProjectReference` на `ServiceBooking.TestKit` ради чистых
функций §69.3. Плата: восстанавливается пакет `Testcontainers.PostgreSql`. **Во время выполнения
юнит-тестов ни Docker, ни Postgres не трогаются** — `TestDatabaseNaming` от них не зависит.

Это утверждение проверяется, а не декларируется: T8-Q2 — прогнать `dotnet test ServiceBooking.UnitTests`
при **остановленном** Docker Desktop; 591/591 зелёных, время не выросло (§63).

### 74.3 Версия СУБД «в одном месте» (US-84, третий критерий)

Сегодня `postgres:16` упомянут в трёх местах: `docker-compose.yml` (`16-alpine`), `ci.yml` service
(`16`), `ci.yml` docker-build (`16-alpine`). Плюс появится четвёртое — Testcontainers.

Честное решение: **авторитетное значение — константа `TestInfrastructure.PostgresImage` в
`ServiceBooking.TestKit`**; остальные места её повторяют, и **расхождение ловится автоматически**:

```bash
# deploy/ci/check-image-pins.sh — падает, если мажорная версия postgres расходится между
#   ServiceBooking.TestKit/TestInfrastructure.cs, docker-compose.yml, docker-compose.prod.yml,
#   .github/workflows/ci.yml
```

Проверять на точное совпадение тега нельзя (`16` в service-контейнере GitHub против `16-alpine` в
compose — оба правомерны), поэтому сверяется **мажорная версия**. Это слабее, чем «одно место», и я
это прямо признаю: сделать буквально одно место, читаемое и YAML'ом GitHub Actions, и compose, и C#,
без генерации файлов — нельзя. Автопроверка расхождения даёт то же практическое свойство за 20 строк
bash.

### 74.4 `deploy/ci/smoke.sh` — US-92

В самом скрипте `BASE_URL` **уже** единственная точка настройки (строка 15,
`BASE_URL="${BASE_URL:-http://localhost:8080}"`). Правок в логике не нужно. Что делается:

1. из шапки-комментария (строка 9) убирается пример с жёстким `localhost:5000` → заменяется на
   `BASE_URL=http://localhost:${SB_API_PORT:-5000} deploy/ci/smoke.sh`;
2. то же в `README.md`/`DEPLOY.md`, если там есть `localhost:5000` рядом со смоуком (грепнуть);
3. свойство §9 L6 — «скрипт читает версии документов из ЭТОГО ЖЕ образа, а не носит свою копию» —
   **не трогается ни одной строкой**. Приёмочный греп: в скрипте нет литералов версий документов.
4. два смоука одновременно на разных портах уже независимы (телефон случайный, `mktemp` свой).
   Требование US-92 проверяется прогоном, а не правкой.

### 74.5 Фейл-фаст прод-конфигурации не ослабляется (NFR «Безопасность»)

`docker-build` по-прежнему стартует образ в `Production` с полным набором обязательных переменных.
Цикл 8 не трогает ни `DeploymentSafetyChecks`, ни список переменных, ни `-e ASPNETCORE_ENVIRONMENT=Production`.
Приёмочный греп — эти строки в `ci.yml` должны остаться дословно.

---

## 75. R6 — соединения, пулы, `max_connections`

Считаем честно. Каждый `WebApplicationFactory`-хост поднимает **свой** Npgsql-пул (дефолт
`MaxPoolSize=100`). `RateLimitTestFactory`/`NotificationTestFactory`/`NotificationDispatchTestFactory`
создаются по экземпляру на тест и диспозятся в конце теста — но сборка пула не мгновенная.

Решения, оба обязательные:

1. **Ограничить пул в тестовой строке подключения.** `TestDatabaseLease` формирует строку с
   `Maximum Pool Size=15;Connection Idle Lifetime=10;Timeout=15`. Пятнадцати достаточно: параллелизма
   внутри набора нет (П2), одновременно живых хостов — единицы.
2. **Поднять потолок в тестовом контейнере.** Команда контейнера:
   `-c max_connections=200 -c fsync=off -c full_page_writes=off -c synchronous_commit=off -c shared_buffers=128MB`.
   Три «off» безопасны по определению (данные одноразовые, крах контейнера = конец прогона) и дают
   заметный выигрыш на накатывании миграций — это ещё один вклад в бюджет +20 %.

Внешний режим (CI): `postgres:16` с дефолтным `max_connections=100`; три базы × 15 = 45 — с запасом.
Менять конфигурацию service-контейнера не требуется.

---

## 76. Структура проекта: что появляется и что меняется

```
ServiceBooking/
├── ServiceBooking.TestKit/                        ← НОВЫЙ проект (net8.0, Exe; и библиотека, и CLI)
│   ├── TestRunKey.cs                              §67
│   ├── TestDatabaseNaming.cs                      §69.3 — чистые функции, покрыты юнит-тестами
│   ├── TestInfrastructure.cs                      §74.3 — PostgresImage, метки, TTL по умолчанию
│   ├── ResourceLabels.cs                          §70.3 — метки контейнера и JSON в COMMENT ON DATABASE
│   ├── TestServerLease.cs                         §69.1 — container | external, резолвинг режима
│   ├── TestDatabaseLease.cs                       §68, §69.3 — шаблон, слоты, DropAsync с охраной
│   ├── Sweeper.cs                                 §70.3
│   ├── EnvStatus.cs                               §73.4
│   └── Program.cs                                 CLI: status | sweep | doctor
│
├── ServiceBooking.Tests/
│   └── Infrastructure/
│       ├── TestRunEnvironment.cs                  ← НОВЫЙ: async-lazy синглтон + счётчик ссылок
│       ├── TestSlot.cs                            ← НОВЫЙ
│       ├── TestHostSettings.cs                    ← НОВЫЙ: единый UseSetting-блок, TestHostIdentity
│       ├── TestPhones.cs                          ← НОВЫЙ: одна реализация UniquePhone на две базы
│       ├── TestDatabaseFixture.cs                 ← ПЕРЕПИСАН: слот "api", без EnsureDeletedAsync
│       ├── LegalDatabaseFixture.cs                ← НОВЫЙ: слот "legal"
│       ├── DispatchDatabaseFixture.cs             ← НОВЫЙ: слот "dispatch" (коллекция существует)
│       ├── CustomWebApplicationFactory.cs         ← правка: через TestHostSettings
│       ├── RateLimitTestFactory.cs                ← правка: через TestHostSettings
│       ├── LegalDocumentsTestFactory.cs           ← правка: слот legal, свои корни
│       ├── NotificationTestFactory.cs             ← правка
│       ├── NotificationDispatchTestFactory.cs     ← правка: слот dispatch
│       ├── UploadsStaticFilesTestFactory.cs       ← правка: аккуратная, см. §71.3
│       ├── ApiTestBase.cs                         ← правка: суперадмин из Identity, телефоны из TestPhones
│       └── NotificationTestBase.cs                ← правка: то же
│
├── ServiceBooking.UnitTests/
│   └── TestDatabaseNamingTests.cs                 ← НОВЫЙ (US-85), рядом с DeploymentSafetyChecksTests.cs
│
├── ServiceBooking.API/Program.cs                  ← ОДНА правка: Logs:Directory (§71.4)
│
├── contracts/cycle8/                              ← НОВЫЙ каталог, машиночитаемые контракты
│   ├── servicebooking-invariant.openapi.yaml      §84 — инвариант HTTP-поверхности
│   └── testkit-status.schema.json                 §86 — вывод `status --json`
│
├── deploy/ci/
│   ├── smoke.sh                                   ← правка шапки (§74.4)
│   └── check-image-pins.sh                        ← НОВЫЙ (§74.3)
│
├── docs/testing-isolation.md                      ← НОВЫЙ: как запускать, как чинить, как подметать
├── .env.dev.example                               ← НОВЫЙ (закоммичен; .env остаётся в .gitignore)
├── docker-compose.yml                             ← правка (§73.2)
├── docker-compose.glitchtip.yml                   ← правка две строки (§73.5), необязательная
├── frontend/vite.config.ts                        ← правка (§73.3)
├── .github/workflows/ci.yml                       ← правка (§74.1)
├── README.md                                      ← раздел «Запуск» (зона devops)
└── CURRENT_STATE.md                               ← §7 «Как запускать», §9.24d, урок P0-цикл-4 E
```

**Чего в структуре НЕ появляется:** `scripts/`, `Makefile`, `docker-compose.test.yml`,
`docker-compose.override.yml`, второй файл окружения.

---

## 77. Задачи: что параллельно, что последовательно

### 77.1 Граф

```
T8-0  замер «до» ─────┐
T8-1  разведка R2 ────┴──► T8-B1 TestKit: ключ, имена, защита ──► T8-B2 аренда сервера/баз
                                     │                                     │
                                     └──► T8-B3 юнит-тесты US-85           ├──► T8-B4 фикстуры и слоты
                                                                           ├──► T8-B5 фабрики и TestHostSettings ──► T8-B6 зелёные 465
                                                                           └──► T8-B8 подметальщик + status
T8-B7 Logs:Directory  (независима, можно в любой момент)
T8-O1 docker-compose  ─┐
T8-F1 vite.config     ─┼─ НЕЗАВИСИМЫЙ БЛОК, идёт параллельно всей ветке T8-B*
T8-O2 glitchtip       ─┘
T8-O3 ci.yml ────────────────────────────────────► после T8-B6
T8-O4 check-image-pins ── независима
T8-O5 smoke.sh шапка ──── независима
T8-D1 документация ─────────────────────────────► после T8-B8 и T8-O1
T8-Q1..Q5 приёмка ──────────────────────────────► после всего
```

### 77.2 Таблица

| ID | Что | Исполнитель | Зависит от | Можно параллельно с |
|---|---|---|---|---|
| **T8-00** | восстановить `SPEC_CYCLE5_LEGAL.md` из `7b382d8` | devops | — | всем |
| **T8-0** | замер «до» (§63) | backend или QA | — | T8-1 |
| **T8-1** | разведка R2 (§64) | backend или QA | — | T8-0 |
| **T8-B1** | `TestKit`: проект, `TestRunKey`, `TestDatabaseNaming`, `ResourceLabels`, `TestInfrastructure` | backend | T8-0 | весь блок O/F |
| **T8-B2** | `TestServerLease` (container/external), `TestDatabaseLease` (шаблон + слоты + охраняемый DROP) | backend | T8-B1 | блок O/F |
| **T8-B3** | `TestDatabaseNamingTests` в `ServiceBooking.UnitTests` (US-85) | backend | T8-B1 | всем |
| **T8-B4** | `TestRunEnvironment`, `TestSlot`, три фикстуры; `EnsureDeletedAsync` удаляется | backend | T8-B2 | блок O/F |
| **T8-B5** | `TestHostSettings`, `TestPhones`; шесть фабрик и две базы тестов переводятся на слоты и свои корни | backend | T8-B4 | блок O/F |
| **T8-B6** | довести набор до 465/465; правки по итогам T8-1 | backend | T8-B5 | блок O/F |
| **T8-B7** | `Logs:Directory` в `Program.cs` + правка `NTF-L001` | backend | — (но мержить после T8-B5) | всем |
| **T8-B8** | `Sweeper`, `EnvStatus`, CLI `status/sweep/doctor` + `testkit-status.schema.json` | backend | T8-B2 | блок O/F |
| **T8-F1** | `vite.config.ts` → `.env` рабочей копии (US-89) | **frontend** | — | всей веткой B |
| **T8-F2** | проверить 181 vitest зелёными; описать поведение «порт 5173 занят» | **frontend** | T8-F1 | всей веткой B |
| **T8-O1** | `docker-compose.yml`: `name`, порты, том, база; `.env.dev.example` | devops | — | всей веткой B |
| **T8-O2** | `docker-compose.glitchtip.yml` (две строки, необязательна) | devops | — | всем |
| **T8-O3** | `ci.yml`: семантика переменной, `SERVICEBOOKING_TEST_RUN_KEY`, шаг проверки пинов | devops | T8-B6 | — |
| **T8-O4** | `deploy/ci/check-image-pins.sh` | devops | — | всем |
| **T8-O5** | шапка `smoke.sh` + грепнуть `localhost:5000` в доках | devops | — | всем |
| **T8-D1** | `docs/testing-isolation.md`, README «Запуск», `CURRENT_STATE.md` §7 / §9.24d / урок E | devops + backend | T8-B8, T8-O1 | — |
| **T8-Q1** | два одновременных прогона из двух рабочих копий — оба зелёные | QA | T8-B6 | — |
| **T8-Q2** | юнит-набор при **остановленном** Docker: 591/591 (§74.2) | QA | T8-B3 | — |
| **T8-Q3** | пять последовательных прогонов; после них `status` пуст | QA | T8-B8 | — |
| **T8-Q4** | `sweep --apply` во время живого прогона — прогон не задет (US-86) | QA | T8-B8 | — |
| **T8-Q5** | замер «после», сверка с §63; два dev-стека одновременно; два смоука на разных портах | QA | всё | — |

### 77.3 Что здесь для frontend-developer — честно

**Меньше часа работы: T8-F1 и T8-F2.** Цикл инфраструктурный, фронтовой поверхности у него нет.
Чтобы frontend-разработчик не простаивал и не лез в чужой домен, его вклад — это:

1. правка `vite.config.ts` (§73.3) строго по контракту переменных из `API_CONTRACT_CYCLE8.md §85`;
2. подтверждение, что 181 vitest-тест и `tsc --noEmit` не задеты;
3. **проверка контракта против машиночитаемой схемы** — прогнать клиентские вызовы против
   `contracts/cycle8/servicebooking-invariant.openapi.yaml` (§84) и подтвердить, что фронтенд не
   зависит ни от чего, кроме описанного там. Это даёт циклу реальную ценность: инвариант перестаёт
   быть обещанием и становится проверяемым.

Блокировок в обе стороны нет: T8-F1 читает `.env`, который создаёт T8-O1, но дефолты в коде делают
порядок неважным.

---

## 78. Риски и решения

| # | Риск (из SPEC) | Решение в этой архитектуре | Остаточный риск |
|---|---|---|---|
| R1 | Docker Desktop на macOS замедлит цикл разработки | один контейнер на прогон (не на тест), шаблонная база (§68), `fsync=off` (§75), сохранён путь `external` для тех, кто хочет свой сервер | +3–6 с на прогон; меряется T8-Q5 |
| R2 | часть тестов молча опирается на общее состояние | разведка **до** правок (§64), список мест §71.5, слот добавляется одной строкой | объём T8-B6 непредсказуем до T8-1 — это и есть причина делать T8-1 первым |
| R3 | подметальщик удалит базу живого прогона | сухой прогон по умолчанию, конъюнкция трёх признаков, секция «неопределённые» не удаляется никогда (§70.3), Ryuk закрывает 90 % случаев без подметальщика | ручное `sweep --apply --run-key <чужой>` остаётся возможным — это осознанно |
| R4 | прогон из Rider перестанет работать | ключ и контейнер вычисляются внутри процесса (§67), точка входа остаётся `dotnet test` (§72.1), ни одной обязательной переменной окружения | проверяется руками (T8-Q1) — автотестом это не проверить |
| R5 | правки заденут `docker-build`/смоук | в `smoke.sh` меняется только комментарий; свойство «читает версии из образа» грепается (§74.4); прод-фейлфаст не трогается (§74.5) | низкий |
| R6 | два прогона упрутся в `max_connections` | пул ограничен 15, потолок контейнера 200 (§75), посчитано | низкий |
| R7 | цикл расползётся в «заодно ускорим тесты» | `DisableTestParallelization` объявлен неприкосновенным (§62); шаблонная база подана как защита бюджета, а не ускорение | соблазн реален; ловится на код-ревью по диффу `AssemblyInfo.cs` |
| **R8** (мой) | `Logs:Directory` — правка в боевом коде ради тестов | дефолт сохраняет поведение дословно, отдельная задача T8-B7, покрыт `docker-build` | низкий, но это единственная правка вне тестов — назвать вслух на ревью |
| **R9** (мой) | `sbtest_*`-базы на **боевом** сервере, если кто-то подставит его хост | §69.4: защита по имени боевую базу спасает, но мусор создаст; адрес сервера печатается первой строкой | остаточный, фиксируется в `CURRENT_STATE.md §9` |
| **R10** (мой) | Ryuk отключат «чтобы не мешал» | запрет объявлен, `TESTCONTAINERS_RYUK_DISABLED` грепается на отсутствие в приёмке | низкий |

---

## 79. Приёмочные грепы (для code-reviewer и QA)

```bash
# 1. Ни одного EnsureDeleted вне TestKit
grep -rn "EnsureDeletedAsync\|EnsureDeleted(" --include=*.cs . | grep -v "ServiceBooking.TestKit/"

# 2. Ни одной захардкоженной тестовой базы
grep -rn "servicebooking_test" --include=*.cs --include=*.yml --include=*.md . 
# допустимо ТОЛЬКО: TestDatabaseNaming.NeverDrop, текст ошибки, исторические разделы CURRENT_STATE

# 3. Общий суперадмин больше не литерал в тестах
grep -rn '"+70000000001"' ServiceBooking.Tests/       # только TestHostSettings.cs

# 4. Параллелизм не включили (П2)
grep -n "DisableTestParallelization" ServiceBooking.Tests/AssemblyInfo.cs   # = true

# 5. Ryuk не выключили
grep -rn "RYUK_DISABLED" .                            # пусто

# 6. Жёстких портов в закоммиченном compose нет
grep -n '"5432:5432"\|"5000:8080"' docker-compose.yml # пусто

# 7. Имя compose-проекта задано явно
grep -n "^name:" docker-compose.yml                   # есть

# 8. Смоук не носит своей копии контракта (§9 L6 не сломан)
grep -n "privacyAcknowledgedVersion" deploy/ci/smoke.sh   # только в теле, собранном из ответа API
grep -n "2026-" deploy/ci/smoke.sh                        # ни одной версии документа литералом

# 9. Прод-фейлфаст цел
grep -n "ASPNETCORE_ENVIRONMENT=Production" .github/workflows/ci.yml   # есть

# 10. HTTP-контракт не поплыл
npx @redocly/cli lint contracts/cycle8/servicebooking-invariant.openapi.yaml
```

---

## 80. Что цикл сознательно НЕ делает (повтор SPEC §3, с архитектурными причинами)

- **параллелизм внутри набора** — П2; потребовал бы правки сотен тестов, это отдельный цикл;
- **e2e через браузер** — нет предмета;
- **изоляция боевого контура, self-hosted раннер, compose для фронтенда** — вне предмета;
- **покрытие и `dotnet format` в CI** (§9.25) — рядом, но другой предмет;
- **ускорение прогона как цель** — §68 защищает существующую скорость, а не улучшает её;
- **«одно место» для версии Postgres в буквальном смысле** — недостижимо без генерации файлов;
  заменено автопроверкой расхождения (§74.3), это названо явно.

---

## 81. Что нужно от заказчика (ничто не блокирует старт)

1. **Подтвердить П1, П2, П3** — SPEC §0. Архитектура написана в предположении «да» по всем трём;
   «нет» по П1 обнуляет §66 и §69 (тогда изоляция — только именами баз внутри чужого Postgres,
   остальное остаётся в силе).
2. **Если разведка T8-1 даст больше десяти красных в одиночку** — предложение по резу, в порядке
   убывания желательности: (а) добавить слот для проблемной фабрики (дёшево, §66.2); (б) вынести
   проблемные тесты в отдельную коллекцию со своим слотом; (в) урезать US-87 до «свой суперадмин +
   свои файловые корни», отложив остальное. Вариант «пометить как flaky» — не предлагается.
3. **Подтвердить, что остаточный риск R9** (тестовый прогон, направленный на боевой хост, создаст там
   мусор, хотя боевую базу не тронет) — приемлем, либо согласиться на денайлист боевых хостов в
   закоммиченном файле.
