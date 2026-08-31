# ARCHITECTURE — цикл санации ServiceBooking

**Вход:** `SPEC.md` (решения Q1–Q6 в §0 — окончательные), `CURRENT_STATE.md` (коммит `263c661`).
**Тип работ:** изменение существующей кодовой базы. Стек не выбирается и не меняется.
**Baseline, который нельзя ухудшать:** `dotnet test ServiceBooking.Tests` → 230/230; `dotnet build ServiceBooking.sln`
→ 0 ошибок; `npx tsc --noEmit` во `frontend/` → чисто.

Документ отвечает на семь открытых вопросов из §10 SPEC и даёт план работ, по которому
backend- и frontend-разработчик работают параллельно.

---

## 0. Принципы этого цикла

1. **Правки точечные, конвенции §6 CURRENT_STATE не пересматриваются.** Primary constructors, `record`-DTO,
   ручной `MapToDto`, 402 для тарифных гейтов, приватные предикаты прав внутри контроллеров, транзакция +
   `pg_advisory_xact_lock` для check-then-act, слой `src/api/<домен>.ts`, react-query, мапперы ошибок
   в `src/utils/*Error.ts`.
2. **Рефакторинг = перенос, а не переписывание.** Там, где логика выносится в чистую функцию, тело
   переносится дословно; ревьюер сверяет диффом посимвольно (R5).
3. **Новых слоёв, папок и абстракций не заводим.** Ни репозиториев, ни MediatR, ни AutoMapper,
   ни общего `IPermissionService`. Новые файлы кладутся в уже существующие каталоги.
4. **Новая библиотека в цикле ровно одна** — тестовый раннер фронтенда (+ его окружение и репортер покрытия).
   На бэкенде новых NuGet-пакетов нет: версии в новом тестовом проекте совпадают с
   `ServiceBooking.Tests/ServiceBooking.Tests.csproj:12-23`.
5. **Формат тел ответов для кодов 400/402/403/404/409 не меняется** (R3). `ProblemDetails` вводится
   исключительно для необработанных исключений (500).

---

## 1. Тестовый инструментарий

### 1.1 Бэкенд: отдельный проект `ServiceBooking.UnitTests`

**Решение (ответ на §10.2 SPEC): отдельный проект в solution, а не коллекция внутри `ServiceBooking.Tests`.**

Обоснование:
- `ServiceBooking.Tests` физически привязан к БД: `TestDatabaseFixture.InitializeAsync`
  (`ServiceBooking.Tests/Infrastructure/TestDatabaseFixture.cs:18-34`) дропает базу и поднимает хост
  **до первого теста коллекции**, а `AssemblyInfo.cs` отключает параллелизм на всю сборку. Даже класс без
  атрибута `[Collection("Api")]` не спасает: DoD п. 3 требует прогона **при остановленной PostgreSQL**, а
  `dotnet test ServiceBooking.Tests` в этом случае упадёт на инициализации фикстуры соседних классов.
- Требование «одна документированная команда запуска» выполняется буквально: `dotnet test ServiceBooking.UnitTests`.
- Проект не ссылается на `Microsoft.AspNetCore.Mvc.Testing` — физически не может поднять `WebApplicationFactory`.

Файл: `ServiceBooking.UnitTests/ServiceBooking.UnitTests.csproj`

| Свойство | Значение | Почему |
|---|---|---|
| `TargetFramework` | `net8.0` | как везде |
| `ImplicitUsings` / `Nullable` | `enable` | конвенция §6 |
| `IsTestProject` | `true` | |
| PackageReference | `xunit` 2.5.3, `xunit.runner.visualstudio` 2.5.3, `Microsoft.NET.Test.Sdk` 17.8.0, `FluentAssertions` 6.12.1, `coverlet.collector` 6.0.0 | ровно те же версии, что в `ServiceBooking.Tests.csproj:13-22` — новых фреймворков нет (US-02) |
| **Нет** пакета | `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.Design` | гарантия, что юнит-тест не превратится в интеграционный |
| ProjectReference | `..\ServiceBooking.API\ServiceBooking.API.csproj` | чистые функции живут в `ServiceBooking.API/Services/`; транзитивно приходят `Core` и `Infrastructure` |
| `<Using Include="Xunit" />` | да | как в существующем проекте |

Структура:
```
ServiceBooking.UnitTests/
├── ServiceBooking.UnitTests.csproj
├── SlotCalculatorTests.cs
├── SubscriptionResolverRulesTests.cs
└── BookingFiltersTests.cs
```

**Маркировка тестов.** `[Fact, TestCase("UNIT-NNN")]` **не используется** — это осознанное решение
и явное обоснование, которого требует US-02 п. 7. Причины: (а) `TestCaseAttribute` лежит в
`ServiceBooking.Tests/Infrastructure/TestCaseAttribute.cs`, и переиспользование потребовало бы либо
ссылки на проект с `Mvc.Testing`, либо копии атрибута; (б) `TEST_CATALOG.md` — каталог **сценариев API**
(«PREFIX-NNN» → HTTP-вызов), юнит-кейсы в эту модель не ложатся. Имена методов самодокументирующие:
`Calculate_SlotOverlappingBooking_IsExcluded`. В `TEST_CATALOG.md` добавляется короткий раздел
«Юнит-тесты» с составом наборов и командами запуска, без построчного каталога.

**Команды (идут в `README.md` и `TEST_CATALOG.md`):**
```bash
dotnet test ServiceBooking.UnitTests                       # < 10 с, PostgreSQL не нужна
dotnet test ServiceBooking.UnitTests --collect:"XPlat Code Coverage"   # cobertura для проверки 80%
dotnet test ServiceBooking.Tests                           # функциональные, нужна PostgreSQL
```

**Покрытие бэкенда — отчёт, а не гейт.** Ответ на §10.7 SPEC: порог 80 % по файлам
`SlotService.cs`/`SlotCalculator.cs`/`SubscriptionResolver.cs`/`BookingFilters.cs` проверяется ревьюером по
cobertura-отчёту `coverlet.collector`. Гейт в CI не ставим: `coverlet.collector` (data collector) порогов
не поддерживает, а `coverlet.msbuild` — это новый пакет, которого §6 SPEC не разрешает. Для чистых функций,
у которых каждая ветка описана отдельным кейсом, потеря невелика.

### 1.2 Фронтенд: Vitest + jsdom, конфиг отдельным файлом

**Решение: Vitest 2.x + jsdom, конфигурация в новом `frontend/vitest.config.ts`; `vite.config.ts`,
`tsconfig.json` и `npm run build` не трогаются вообще.**

Это прямой ответ на риск R6 («раннер конфликтует с `tsc && vite build`»). Разбор:

| Вариант | Оценка |
|---|---|
| Блок `test: {}` внутри `vite.config.ts` (через `defineConfig` из `vitest/config`) | **Отклонён.** Продакшн-сборка начинает импортировать `vitest/config` на этапе чтения конфига. `deploy/deploy-remote.sh:13-14` делает `npm ci && npm run build` **на боевом сервере**; любой прогон с `--omit=dev` или `NODE_ENV=production` сломает деплой |
| Отдельный `frontend/vitest.config.ts` | **Принят.** Полная изоляция: `vite build` о раннере не знает. Это не «второй сборочный конфиг» в смысле US-02 — `tsconfig.json` остаётся один, второго `tsconfig.build.json` не появляется |

`frontend/vitest.config.ts` (содержимое задаёт задача T-F2):
```
test: {
  environment: 'jsdom',                       // нужен authStore: zustand persist пишет в localStorage
  include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
  coverage: {
    provider: 'v8',
    include: [ 'src/utils/bookingError.ts', 'src/utils/companyError.ts', 'src/utils/memberError.ts',
               'src/utils/planError.ts', 'src/utils/bookingRules.ts', 'src/utils/time.ts',
               'src/store/authStore.ts' ],
    thresholds: { lines: 80, perFile: true },
  },
}
```
`coverage.include` намеренно сужен до покрываемых модулей — US-02 п. 4 прямо запрещает мерить процент по
всему репозиторию (4 500 строк TSX). Здесь порог **является гейтом**: он встроен в раннер, ничего не стоит
и падает в CI (ответ на §10.7 SPEC — на фронте да, на бэкенде нет).

**Где лежат тесты:** рядом с тестируемым модулем, `src/**/<имя>.test.ts`. Плюсы: попадают в
`tsconfig.json:23` (`include: ["src"]`) и проверяются `tsc` вместе с остальным кодом — это ловит дрейф
типов в тестах; в `dist` не попадают, потому что Rollup собирает только достижимое из `index.html`
(ни один продовый модуль тестовый файл не импортирует).

**Глобалы не включаем.** В каждом тесте явные `import { describe, it, expect } from 'vitest'`.
Тогда `tsconfig.json` **не меняется вовсе** (не нужен `"types": ["vitest/globals"]`), а `noUnusedLocals`/
`strict` действуют на тесты наравне с продовым кодом.

**Зависимости (`frontend/package.json`, секция `devDependencies`):** `vitest`, `jsdom`, `@vitest/coverage-v8`.
`@testing-library/react` и `@testing-library/jest-dom` в этом цикле **не ставим**: компонентные тесты
модалок вынесены за скоуп (§7 SPEC), а ни один из перечисленных в US-02 модулей не рендерит React.
Добавление TL позже — одна строка `npm i -D` плюс `plugins: [react()]` в `vitest.config.ts`, конфиг менять
не придётся.

**Скрипты (`frontend/package.json:6-10`):**
```json
"test": "vitest",
"test:run": "vitest run",
"test:coverage": "vitest run --coverage"
```
`build` (`tsc && vite build`), `dev`, `preview` — без изменений.

---

## 2. Выделение чистой логики (E2/E3, ответ на §10.1 SPEC)

Общее правило: **чистая функция получает уже загруженные данные и «сейчас» параметром**; всё, что ходит в
`AppDbContext`, остаётся в существующем классе. Ни один тип из `Microsoft.EntityFrameworkCore` в сигнатуры
чистых функций не попадает. POCO-сущности из `ServiceBooking.Core` использовать можно — они анемичные
и конструируются в тесте одним `new`.

### 2.1 `SlotService` → `SlotCalculator` (риск R5, наивысший)

Новый файл `ServiceBooking.API/Services/SlotCalculator.cs`, namespace `ServiceBooking.API.Services`:

```csharp
public record TimeRange(TimeOnly Start, TimeOnly End);

public static class SlotCalculator
{
    public const int StepMinutes = 30;

    public static List<TimeSlotResult> Calculate(
        int serviceDurationMinutes,
        TimeOnly? workStart, TimeOnly? workEnd,     // null = расписания на дату нет
        IReadOnlyList<TimeRange> breaks,
        IReadOnlyList<TimeRange> bookings,
        bool allowWithoutSchedule);
}
```

Правила переноса (обязательны к соблюдению, проверяются на ревью диффом):
1. Тело цикла `SlotService.cs:34-49` переносится **дословно**: условие `while (current + duration <= end)`,
   защита от переполнения суток `if (current + duration >= TimeSpan.FromDays(1)) break;` (`SlotService.cs:37`),
   пересечения `b.StartTime < slotEnd && b.EndTime > slotStart` (`:42-43`), шаг `+= 30 мин` (`:48`).
   Комментарий про `TimeOnly` и 24:00 переезжает вместе с кодом.
2. `workStart is null` → `TimeSpan.Zero`, `workEnd is null` → `TimeSpan.FromHours(24)` — точная копия
   `SlotService.cs:31-32`.
3. Правило «нет расписания и `allowWithoutSchedule = false` → пустой список» живёт **в калькуляторе**
   (единственный источник истины). В `SlotService.GetAvailableSlotsAsync` ранний возврат
   `SlotService.cs:22` **сохраняется** как оптимизация — он экономит запрос за бронями. Рядом обязателен
   комментарий на английском: guard duplicates the rule intentionally to skip the bookings query;
   the authoritative rule lives in `SlotCalculator`.

`SlotService.GetAvailableSlotsAsync` после правки: те же три запроса в том же порядке (`Services.FindAsync`,
`WorkingHours` + `Include(Breaks)`, `Bookings`), маппинг в `TimeRange` и один вызов `SlotCalculator.Calculate`.
Публичная сигнатура метода и `record TimeSlotResult` не меняются → `BookingsController.GetSlots`
(`BookingsController.cs:30-43`) и регистрация в `Program.cs:128` не трогаются.

Регрессионная сетка: BK-001..BK-026 (`ServiceBooking.Tests/Tests/BookingsFlowSmokeTests.cs`), особенно
BK-024..BK-026 (ветка `manual`). Прогон обязателен **до** и **после** рефакторинга, в одном PR с новыми
юнит-тестами.

Юнит-кейсы (все 8 из таблицы §4.2 SPEC + границы): сетка 30 мин внутри окна; пересечение с бронью;
пересечение с перерывом; отменённая бронь в список `bookings` просто не попадает (тест фиксирует, что
фильтрация статуса — ответственность `SlotService`, и проверяется BK-тестом); нет расписания + `allow=false`
→ `[]`; `allow=true` → сетка от 00:00 без выхода за сутки; услуга длиннее окна → `[]`; услуга 45 мин на
30-минутной сетке — **фиксируем фактическое поведение** как известное ограничение (§7 SPEC, P2-9).

### 2.2 `SubscriptionResolver` → статический `Resolve`

Минимальный диф: в `ServiceBooking.API/Services/SubscriptionResolver.cs` появляется статический метод,
а строки 86-89 заменяются его вызовом.

```csharp
/// <summary>Pure plan-resolution rule: no DB access, "now" is passed in so it can be unit-tested.</summary>
public static EffectivePlan Resolve(AccountSubscription? sub, DateTime nowUtc)
{
    var usable = sub is not null && sub.IsActive && (!sub.PaidUntil.HasValue || sub.PaidUntil >= nowUtc);
    return usable && sub!.PlanConfig is { IsActive: true }      // ← НОВОЕ: PlanConfig.IsActive (US-08)
        ? EffectivePlan.FromConfig(sub.PlanConfig)
        : EffectivePlan.Free;
}
```
`GetEffectivePlansForOwnersAsync` (`SubscriptionResolver.cs:72-92`) сохраняет батчевую загрузку
(`.Include(s => s.PlanConfig)`, `SubscriptionResolver.cs:77-80`) и в цикле вызывает `Resolve(sub, now)`.
Новый класс не заводим: метод остаётся в том же файле рядом с `EffectivePlan`, это соответствует
«узкоспециализированные помощники в `Services/`».

Юнит-кейсы — ровно 7 из таблицы §4.2 SPEC (подписки нет; `sub.IsActive = false`; `PaidUntil` в прошлом;
`PaidUntil = null` + активна; `PlanConfig = null`; **`PlanConfig.IsActive = false` → Free**; батч по нескольким
владельцам — последний проверяется через `GetEffectivePlansForOwnersAsync` и потому остаётся в
функциональном наборе, юнит-версия покрывает вызов `Resolve` по каждому элементу).

### 2.3 Фильтр «предстоящие» → `BookingFilters` (ответ на §10.5 SPEC)

Проблема из US-07: `Enum.TryParse<BookingStatus>("upcoming")` возвращает `false`, и фильтр молча не
применяется (`BookingsController.cs:209`).

Новый файл `ServiceBooking.API/Services/BookingFilters.cs`:

```csharp
public enum ClientStatusFilterKind { All, Upcoming, ByStatus }
public readonly record struct ClientStatusFilter(ClientStatusFilterKind Kind, BookingStatus Status);

public static class BookingFilters
{
    /// <summary>Pure parser for GET /api/bookings/client?status=. False = unknown value → 400.</summary>
    public static bool TryParseClientStatus(string? status, out ClientStatusFilter filter);

    /// <summary>
    /// The "upcoming" rule as an expression so EF Core and the unit test share the exact same code
    /// (compile it in tests, translate it to SQL in the controller) — no duplicated predicate to drift.
    /// </summary>
    public static Expression<Func<Booking, bool>> Upcoming(DateOnly today, TimeOnly nowTime) =>
        b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Pending)
             && (b.Date > today || (b.Date == today && b.StartTime > nowTime));
}
```

Почему `Expression`, а не два предиката: единственный способ не завести расхождение между «правилом в SQL»
и «правилом в тесте». `today`/`nowTime` вычисляются в контроллере и попадают в SQL параметрами —
`DateOnly.FromDateTime` внутрь выражения не заносим, чтобы не зависеть от трансляции Npgsql.

**Что считаем «сейчас» (ответ на §10.5 SPEC):** `DateTime.UtcNow`, из него `DateOnly.FromDateTime` и
`TimeOnly.FromDateTime`. Обоснование: (а) весь остальной код уже живёт на `DateTime.UtcNow`
(`SubscriptionResolver.cs:75`, `AdminController.cs:161`, `Booking.CreatedAt`), вводить второй источник
времени в цикле, где таймзоны осознанно отложены (§7 SPEC), нельзя; (б) для клиента в UTC+3 UTC-время
**отстаёт**, то есть запись остаётся во вкладке «Предстоящие» чуть дольше — безопасный режим отказа
(лишняя строка в списке вместо исчезнувшего будущего визита). В коде — комментарий с этой аргументацией
и ссылкой на §7 SPEC.

Юнит-кейсы: `TryParseClientStatus` — `null`/пусто → `All`; `"upcoming"` и `"Upcoming"` → `Upcoming`;
`"Completed"`, `"cancelled"` → `ByStatus` (регистронезависимо); `"garbage"` → `false`.
`Upcoming(...)`.Compile() — завтрашняя дата → true; вчерашняя → false; сегодня время позже `nowTime` → true;
сегодня время раньше → false; сегодня время **ровно** `nowTime` → false (граница); статус `Cancelled`/
`Completed` → false.

### 2.4 Что мы намеренно НЕ выделяем: правила прав (ответ на §10.6 SPEC)

**Решение: общий предикат прав в этом цикле не вводим.**

- `ServicesController.CanManageCompany` (`ServicesController.cs:82-92`) правится **удалением одной строки
  условия** `|| cm.Role == UserRole.Master` (`:91`). Это минимально возможный диф и мгновенный откат (R1).
- `WorkingHoursController.CanManage` (`:98-106`) **вообще не меняется** — он лишь начинает вызываться
  из `Get` (`:18-33`).
- Вынести правило в чистую функцию можно только переписав SQL-предикат
  (`AnyAsync(cm => ... && cm.Role == X)`) на «загрузить роль → сравнить в памяти», то есть изменив запрос
  в самом чувствительном месте — правах доступа. Выгода (2–4 юнит-теста) не покрывает риск.
- Правила прав покрываются функциональными тестами, которые и так требуются US-09/US-04:
  SVC-003, SVC-007, SVC-013, SVC-014, WH-011..WH-014.
- Унификация шести копий `CanManageCompany`/`CanManage` (§9 P2-11 CURRENT_STATE) остаётся отложенной (§7 SPEC).

Порог «не менее 30 юнит-тестов бэкенда» достигается без правил прав: SlotCalculator ~12 +
SubscriptionResolver 7 + BookingFilters ~12 = **~31**.

---

## 3. Глобальный обработчик исключений (US-05, ответ на §10.3 SPEC, риск R3)

### 3.1 Жёсткая граница

| Код | Кто отдаёт | Формат сейчас | Формат после цикла |
|---|---|---|---|
| 400 | `BadRequest("...")` | `text/plain`, голая строка | **без изменений** |
| 400 | `BadRequest(createResult.Errors.Select(e => e.Description))` (`CompaniesController.cs:341`) | JSON-массив строк | **без изменений** (`memberError.ts:23-27` его разбирает) |
| 402 | `StatusCode(402, "...")` | `text/plain` | **без изменений** |
| 403 | `Forbid()` | пустое тело | **без изменений** |
| 404 | `NotFound()` / `NotFound("...")` | пусто / `text/plain` | **без изменений** |
| 409 | `Conflict("...")` | `text/plain` | **без изменений** |
| 500 | ничего (исключение улетает наружу) | пусто / developer page | **`application/problem+json`** |

Именно это и есть ответ на R3: `getBookingErrorMessage` (`frontend/src/utils/bookingError.ts:13`)
читает `response.data` как строку — и продолжает читать её как строку. 500 в `switch` не входит и попадает
в `default` («Произошла ошибка. Попробуйте снова.»), что и есть желаемое поведение. **Мапперы
`src/utils/*Error.ts` в связи с этой историей не переписываются.**

### 3.2 Механика

В `ServiceBooking.API/Program.cs`, сразу после `var app = builder.Build();` (`Program.cs:132`) и **первым**
в конвейере — до `UseCors`/`UseStaticFiles` (`:141-142`):

```csharp
// In Development the framework's developer exception page already renders the full exception, so the
// handler is only wired up elsewhere. Everywhere else an unhandled exception must still produce a
// machine-readable body with a correlation id instead of an empty 500 — but ONLY for unhandled
// exceptions: every deliberate 400/402/403/404/409 keeps its existing plain-text body, because the SPA's
// error mappers (frontend/src/utils/*Error.ts) parse response.data as a string.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Type   = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Title  = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError,
            Extensions = { ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier }
        });
    }));
}
```

Явные ограничения реализации:
- **`builder.Services.AddProblemDetails()` не добавляем** и `UseStatusCodePages*` **не добавляем**.
  Первое избыточно (тело пишем сами), второе — единственная штука, которая реально переписала бы тела
  существующих 4xx.
- `detail`/stack trace в теле нет **никогда** (в Development до обработчика дело не доходит) — US-05 п. 3.
- Обработчик активен в окружении `Testing` (оно не Development). Это нужно и полезно: сейчас
  необработанное исключение в `WebApplicationFactory` прилетает в тест исключением, после правки —
  честным HTTP-500, который можно проверить (ADM-035).
- Регистрация — инлайном в `Program.cs`, отдельного `Middleware/`-каталога не заводим: конвейер
  проекта целиком описан в `Program.cs`, это его конвенция.

### 3.3 Где живёт проверка «компания существует» (ответ на §10.4 SPEC)

В `BookingsController.Create` (`BookingsController.cs:45-149`) блок
```csharp
var company = await db.Companies.FindAsync(dto.CompanyId);
if (company is null) return NotFound("Company not found");
```
**поднимается из ветки `if (!isAuthenticated)` (`:58-61`) в самое начало метода**, до тарифного гейта
(`:85-87`). Порядок проверок после правки:

```
404 «Company not found»  →  [гость] 403 AllowSelfBooking / 400 captcha / 400 name+phone
                         →  402 тарифный гейт
                         →  404 «Service not found»
                         →  409 конфликт слота
```
Текст `"Company not found"` для гостя и для авторизованного одинаков — контракт не расходится по ветвям
(US-05 п. 1). Уже загруженный `company` переиспользуется в проверке предоплаты (`:100-101`, лишний
`FindAsync` уходит) и в `bookingCompany` (`:141`). Это устраняет 500 от нарушения FK (§5.3.7 CURRENT_STATE)
без единого `try/catch`.

Попутно (US-05, §5.7 SPEC): `CompaniesController.cs:347` `Enum.Parse<UserRole>(dto.Role)` →
`Enum.TryParse<UserRole>(dto.Role, out var role)`; при `false` → `BadRequest("Unknown role")`.
Для владельца ничего не меняется (мусорную роль раньше отсекал `CanAssignRole` → 403), для `SuperAdmin`
500 превращается в 400.

**Что остаётся источником честного 500 (и потому годится для теста обработчика):**
`PUT /api/admin/owners/{ownerUserId}/subscription` с несуществующим `ownerUserId` — FK
`AccountSubscription.OwnerUserId → AspNetUsers` (`ServiceBooking.Infrastructure/Data/AppDbContext.cs:61`)
падает на `SaveChangesAsync`. В этом цикле **не чиним** (не в скоупе US-08), но покрываем ADM-035:
ответ 500 + `application/problem+json` + непустой `traceId`.

---

## 4. Гигиена безопасности: Swagger и fail-fast (US-10, риск R4)

### 4.1 Swagger только в Development

- `builder.Services.AddEndpointsApiExplorer()` + весь блок `AddSwaggerGen(...)` (`Program.cs:18-50`)
  оборачиваются в `if (builder.Environment.IsDevelopment()) { ... }`.
- `app.UseSwagger()` / `app.UseSwaggerUI(...)` (`Program.cs:134-139`) — в `if (app.Environment.IsDevelopment())`.
- В `Testing` Swagger выключается вместе с Production. Это безопасно: ни один из 230 тестов
  `/swagger` не дёргает (проверено grep'ом по `ServiceBooking.Tests/`); перед мёржем задача обязана
  повторить проверку `grep -rn "swagger" ServiceBooking.Tests/`.
- `deploy/nginx/ezbook.conf:34-41` — блок `location /swagger/` удаляется целиком вместе с комментарием
  (после п. 1 он ведёт в 404).
- XML-документация (`GenerateDocumentationFile`) остаётся включённой — на неё завязан только Swagger в dev.

### 4.2 Fail-fast — строго `IsProduction()`

Блок добавляется в `Program.cs` **после** `var builder = WebApplication.CreateBuilder(args);` (`:14`) и
**до** первого чтения `Jwt:Key` (`:68`):

```csharp
// Production must never boot on the placeholder secrets committed in appsettings.json: a predictable JWT
// signing key plus a published SuperAdmin password is a platform takeover, not a misconfiguration.
// Scoped to IsProduction() on purpose — Development and the Testing environment used by
// ServiceBooking.Tests/CustomWebApplicationFactory keep working with their own throwaway values.
if (builder.Environment.IsProduction()) { /* собрать список проблем, при непустом — throw InvalidOperationException */ }
```

Проверки (все три обязательны):

| Ключ | Условие падения | Сообщение |
|---|---|---|
| `Jwt:Key` | пусто, длина < 32, или равен `CHANGE_ME_TO_A_LONG_SECRET_KEY_AT_LEAST_32_CHARS` (`ServiceBooking.API/appsettings.json:13`) | `Jwt:Key is missing, too short (<32 chars) or still the placeholder. Set Jwt__Key in .env.` |
| `SuperAdmin:Password` | пусто или равен `Admin12345` (`appsettings.json:21`) | `SuperAdmin:Password is missing or still the placeholder. Set SuperAdmin__Password in .env.` |
| `SuperAdmin:Phone` | равен `+70000000000` (`appsettings.json:19`) | предупреждение в лог, **не** падение (телефон не секрет, а `.env.production.example:13` уже содержит этот дефолт) |

Исключение бросается до `builder.Build()`, поэтому контейнер падает мгновенно и пишет причину в лог.
`CustomWebApplicationFactory` (`ServiceBooking.Tests/Infrastructure/CustomWebApplicationFactory.cs:15`)
использует окружение `Testing` → блок не срабатывает → все 230 тестов не задеты (US-10 п. 4).

### 4.3 Найденный при проектировании пробел, без которого fail-fast гарантированно роняет прод

`docker-compose.prod.yml:25-34` **не пробрасывает `SuperAdmin__Password` в контейнер вообще** — там есть
`SuperAdmin__Phone=${SUPERADMIN_PHONE}` (`:31`), но пароля нет. То есть сегодня боевой контейнер поднимается
с `Admin12345` из `appsettings.json`, а после включения fail-fast — **не поднимется вообще**, сколько бы
переменных ни лежало в `.env`: docker compose отдаёт контейнеру только то, что перечислено в `environment:`.

Поэтому «один коммит» из решения Q4 включает **четыре** файла, а не три:

1. `ServiceBooking.API/Program.cs` — fail-fast + Swagger.
2. `docker-compose.prod.yml` — добавить строку `- SuperAdmin__Password=${SUPERADMIN_PASSWORD}`.
3. `.env.production.example` — добавить `SUPERADMIN_PASSWORD=CHANGE_ME` и комментарий; в блок про
   `JWT_KEY` (`:10`) добавить предупреждение, что значение-плейсхолдер теперь роняет старт.
4. `DEPLOY.md` — шаг «перед деплоем этой версии: добавить `SUPERADMIN_PASSWORD` и настоящий `JWT_KEY`
   в `.env` на VPS, затем `docker compose -f docker-compose.prod.yml config` для проверки подстановки».

Проверка на стенде до продового деплоя (DoD п. 8): контейнер без `SUPERADMIN_PASSWORD` не поднимается и
пишет причину; с корректным `.env` — поднимается.

---

## 5. CI на GitHub Actions (US-11, E7)

Файл: `.github/workflows/ci.yml`. Триггеры: `push` в `master`, `pull_request` (любая база).
`concurrency: { group: ci-${{ github.ref }}, cancel-in-progress: true }`. `timeout-minutes: 15` на job.

### 5.1 Выбор по строке подключения — вариант 2 (переменная окружения)

US-11 п. 3 предлагает два варианта. **Выбран второй: строка подключения выносится в переменную окружения
с сохранением текущего значения по умолчанию.**

Обоснование:
- PostgreSQL как service-контейнер нужна **в обоих** вариантах — они отличаются только тем, как тест
  узнаёт пароль. Вариант 1 требует `POSTGRES_HOST_AUTH_METHOD=trust`, то есть особой, нестандартной
  конфигурации СУБД ради того, чтобы подошёл захардкоженный пустой пароль.
- Строка сегодня **продублирована** в двух файлах: `TestDatabaseFixture.cs:13-14` и
  `CustomWebApplicationFactory.cs:21`. Вынос в одну константу — самостоятельно полезный шаг (дубль уже
  однажды разъедется).
- Локальный запуск не ломается: переменная не задана → используется прежний литерал, ровно как сейчас.

Реализация (задача T-B10, диф ~6 строк):
```csharp
// ServiceBooking.Tests/Infrastructure/TestDatabaseFixture.cs
public static readonly string ConnectionString =
    Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_CONNECTION")
    ?? "Host=localhost;Database=servicebooking_test;Username=postgres;Password=";
```
`CustomWebApplicationFactory.cs:21` начинает использовать `TestDatabaseFixture.ConnectionString`
вместо своего литерала. Больше в тестовой инфраструктуре ничего не меняется.

Запасной вариант, если по какой-то причине переменная не заработает: service-контейнер с
`POSTGRES_HOST_AUTH_METHOD=trust` и полностью нетронутый тестовый код. Переключение — правка одного
блока workflow.

### 5.2 Джобы

**`backend`** (`ubuntu-latest`):
```yaml
services:
  postgres:
    image: postgres:16
    env: { POSTGRES_PASSWORD: postgres, POSTGRES_DB: postgres }
    ports: ['5432:5432']
    options: >-
      --health-cmd pg_isready --health-interval 5s --health-timeout 5s --health-retries 10
env:
  SERVICEBOOKING_TEST_CONNECTION: "Host=localhost;Database=servicebooking_test;Username=postgres;Password=postgres"
```
Шаги: `actions/checkout@v4` → `actions/setup-dotnet@v4` (`8.0.x`) → `actions/cache@v4` для
`~/.nuget/packages` по хешу `**/*.csproj` → `dotnet restore ServiceBooking.sln` →
`dotnet build ServiceBooking.sln --no-restore -c Release` → **`dotnet test ServiceBooking.UnitTests --no-build -c Release`**
(быстрый, без БД, падает раньше) → `dotnet test ServiceBooking.Tests --no-build -c Release`.

`-warnaserror` **добавляется отдельной задачей (T-B11) и только после мёржа E1**, когда сборка реально
даёт 0 предупреждений; иначе CI станет красным на первом же PR и его начнут игнорировать.

**`frontend`** (`ubuntu-latest`, `defaults.run.working-directory: frontend`):
`actions/setup-node@v4` (`node-version: 20`, `cache: npm`, `cache-dependency-path: frontend/package-lock.json`)
→ `npm ci` → `npx tsc --noEmit` → `npm run test:coverage` → `npm run build`.
`test:coverage` вместо `test:run` — так порог 80 % из §1.2 становится реальным гейтом.

Джобы независимы и идут параллельно; workflow красный, если упал любой шаг (проверяется намеренно
сломанным тестом в черновом PR — US-11 п. 5). Секретов в workflow нет: БД эфемерная, пароль `postgres`
задан прямо в манифесте и к боевой базе отношения не имеет (US-11 п. 7).

Ориентир по времени: бэкенд ~3–4 мин (сборка + ~1 мин функциональных), фронт ~2 мин → укладываемся в 10.

`README.md` дополняется разделом «CI»: что проверяется и как воспроизвести локально теми же четырьмя
командами (US-11 п. 8).

---

## 6. Состояние дерева после цикла

```
ServiceBooking.sln                     5 проектов (Core, Infrastructure, API, Tests, UnitTests)
ServiceBooking/                        ← УДАЛЁН (E1)
.github/workflows/ci.yml               ← НОВЫЙ (E7)
ServiceBooking.API/
├── Program.cs                         + fail-fast, + Swagger под IsDevelopment, + UseExceptionHandler
├── Properties/launchSettings.json     порт 5000, без weatherforecast
├── Controllers/
│   ├── BookingsController.cs          − дубль using; 404 до 402; фильтр upcoming; 400 на мусорный status
│   ├── ServicesController.cs          − ветка Master в CanManageCompany
│   ├── WorkingHoursController.cs      + CanManage в Get
│   ├── CompaniesController.cs         + фильтр ролей в GetMasters; TryParse роли
│   └── AdminController.cs             + 409 в DeletePlan; + 400 в UpdateSubscription
├── Services/
│   ├── SlotService.cs                 I/O + вызов калькулятора
│   ├── SlotCalculator.cs              ← НОВЫЙ, чистый
│   ├── SubscriptionResolver.cs        + static Resolve(sub, nowUtc)
│   └── BookingFilters.cs              ← НОВЫЙ, чистый
├── appsettings.Production.json.example ← УДАЛЁН (E1, единственный способ конфигурации — .env)
ServiceBooking.Tests/                  +16 функциональных кейсов, SVC-003/007 инвертированы
ServiceBooking.UnitTests/              ← НОВЫЙ проект (~31 тест, < 10 с, без БД)
frontend/
├── vitest.config.ts                   ← НОВЫЙ (единственный новый конфиг)
├── package.json                       + test / test:run / test:coverage, + vitest, jsdom, @vitest/coverage-v8
├── src/pages/DashboardPage.tsx        ← УДАЛЁН (E1)
├── src/pages/owner/OwnerPage.tsx      ← УДАЛЁН (E1)
├── src/utils/bookingRules.ts          ← НОВЫЙ (canCancelBooking из ClientBookingsPage)
├── src/utils/time.ts                  ← НОВЫЙ (нормализация 'HH:mm' → 'HH:mm:ss')
├── src/utils/planError.ts             ← НОВЫЙ (маппер 409 при удалении тарифа)
└── src/**/*.test.ts                   ← НОВЫЕ (~36 тестов)
docker-compose.prod.yml                + SuperAdmin__Password
.env.production.example                + SUPERADMIN_PASSWORD
deploy/nginx/ezbook.conf               − location /swagger/
DEPLOY.md, README.md, TEST_CATALOG.md, API_DOCUMENTATION.md   обновляются в тех же PR
```

`frontend/design_handoff_site_redesign/` — **оставляем на месте** (предположение SPEC §3 подтверждаем):
каталог в сборку не идёт (`tsconfig.json:23` — `include: ["src"]`, Rollup собирает от `index.html`),
перенос даст нулевую техническую выгоду и лишний шум в диффе. Решение фиксируется строкой в `README.md`.

---

## 7. Порядок работ и зависимости

```
Волна 0 (разблокирует всё, делается первой, параллельно BE/FE)
  T-B1 мёртвый код бэкенда (.sln)          ║  T-F1 мёртвые страницы + мёртвые ветки UI
                │                          ║          │
Волна 1 (инфраструктура тестов)            ║          │
  T-B2 проект ServiceBooking.UnitTests     ║  T-F2 vitest + jsdom + скрипты
                │                          ║          │
Волна 2 (продуктовые правки — максимально параллельная)
  T-B3 SlotCalculator + юниты              ║  T-F3 юниты мапперов ошибок
  T-B4 SubscriptionResolver.Resolve + US-08║  T-F4 юниты authStore
  T-B5 US-04 GET /api/workinghours 403     ║  T-F5 bookingRules.ts + юниты
  T-B6 US-09 услуги только владельцу       ║  T-F6 time.ts + юниты
  T-B7 US-05 404 + глобальный обработчик   ║  T-F7 planError.ts + PlansTab (зависит от T-B4)
  T-B8 US-07 upcoming + BookingFilters     ║  T-F8 SubscriptionModal: неактивный текущий тариф
  T-B9 US-12 фильтр ролей в masters        ║
                │                          ║          │
Волна 3         └──────────┬───────────────╨──────────┘
  T-B10 строка подключения в env-переменную
  T-B11 .github/workflows/ci.yml  (нужны обе команды тестов из волны 1)
                │
Волна 4 (последняя, отдельным коммитом, перед выкладкой)
  T-B12 Swagger только в Development + fail-fast + .env/DEPLOY/nginx
```

**Жёсткие последовательные связи (всё остальное — параллельно):**

| Связь | Причина |
|---|---|
| T-B1 → T-B2 | оба правят `ServiceBooking.sln`; конфликт в `GlobalSection` разбирать дороже, чем подождать |
| T-B2 → T-B3, T-B4, T-B8 | юнит-тестам нужен проект, куда их класть |
| T-F2 → T-F3..T-F7 | тем же и на фронте |
| T-B4 → T-F7 | фронту нужен согласованный контракт 409 (см. `API_CONTRACT.md`, § DELETE /api/admin/plans/{id}); до этого FE работает по контракту, а не по коду |
| T-B7 → T-B8 | обе задачи правят `BookingsController.cs` (`Create` и `GetClientBookings`); правки соседние, но в одном файле |
| T-B1, T-B2, T-F2 → T-B11 | CI ссылается на состав solution и на скрипты npm |
| T-B12 после всего | Q4 требует одного коммита с `.env`/`DEPLOY.md`; он же самый опасный (R4) и должен ехать последним, когда всё остальное уже зелёное |

**Точки пересечения BE↔FE (обе стороны читают `API_CONTRACT.md`, не код друг друга):**

| # | Изменение бэкенда | Что делает фронт |
|---|---|---|
| 1 | `DELETE /api/admin/plans/{id}` → 409 с количеством подписчиков | T-F7: `src/utils/planError.ts` + показ ошибки в `PlansTab.tsx` (сейчас `deactivateMut` (`PlansTab.tsx:97-100`) ошибку не отображает вовсе) |
| 2 | `PUT /api/admin/owners/{id}/subscription` → 400 на неактивный план | T-F8: `SubscriptionModal` (`AdminPage.tsx:58-59, 84-89`) уже фильтрует `activePlans`; добавить пометку текущего неактивного плана, чтобы сохранение молча не сбросило владельца на Free |
| 3 | `GET /api/workinghours` → 403 постороннему | Изменений нет. Проверено: единственные потребители — `ScheduleTab` (`ScheduleTab.tsx:250,259,271`) со своим `selfMasterId` (`CabinetPage.tsx:144-146`) и владелец. Закрывается ручным чек-листом DoD п. 8 (риск R7) |
| 4 | `POST/PUT/DELETE /api/services` → 403 мастеру | Изменений нет: CRUD услуг живёт только в `CompanyManagePage`, роут защищён `roles={['CompanyOwner','SuperAdmin']}`. Обязателен ручной прогон `ManualBookingModal` (мастер читает `GET /api/services`) |
| 5 | `GET /api/bookings/client?status=upcoming` начинает фильтровать | Изменений нет: `ClientBookingsPage.tsx:35` уже шлёт `upcoming`. Вкладка просто начинает работать |
| 6 | `GET /api/companies/{id}/masters` фильтрует роли | Изменений нет |

---

## 8. Задачи

Формат: **ID · название** — файлы · зависимости · тесты · критерий готовности.
Каждая задача = один PR. `TEST_CATALOG.md` и `API_DOCUMENTATION.md` обновляются **в том же PR**, что и код
(§6 п. 9 SPEC).

### 8.1 Backend

**T-B1 · E1: удалить мёртвый код бэкенда** (US-01)
- Файлы: удалить каталог `ServiceBooking/`; `ServiceBooking.sln:4-5` и `:25-28`; удалить
  `ServiceBooking.API/Controllers/BookingsController.cs:10` (дубль `using ServiceBooking.Core.Entities;`,
  CS0105); `ServiceBooking.API/Properties/launchSettings.json` — `applicationUrl` → `http://localhost:5000`
  (профиль `https` → `https://localhost:7016;http://localhost:5000`), убрать `launchUrl: "weatherforecast"`
  во всех трёх профилях (`:16,27,35`); удалить `ServiceBooking.API/appsettings.Production.json.example`.
- Зависимости: нет. Делать первой.
- Тесты: новых нет. `dotnet test ServiceBooking.Tests` = 230/230.
- Готово, когда: `dotnet build ServiceBooking.sln` — **0 ошибок, 0 предупреждений**;
  `docker compose up --build` собирает API (`ServiceBooking.API/Dockerfile:7-13` не трогается — R8);
  `dotnet run --project ServiceBooking.API` отвечает на `http://localhost:5000/api/companies`.

**T-B2 · Проект `ServiceBooking.UnitTests`** (US-02)
- Файлы: `ServiceBooking.UnitTests/ServiceBooking.UnitTests.csproj` (состав — §1.1), запись в
  `ServiceBooking.sln`; раздел «Юнит-тесты: как запускать» в `README.md` и `TEST_CATALOG.md`.
- Зависимости: **T-B1**.
- Тесты: один заглушечный `Fact`, чтобы проверить, что проект собирается и запускается без PostgreSQL
  (заменяется реальными в T-B3).
- Готово, когда: `dotnet test ServiceBooking.UnitTests` проходит **при остановленном** PostgreSQL и
  укладывается в 10 с; `dotnet test ServiceBooking.Tests` не изменился.

**T-B3 · SlotCalculator: вынос чистой логики + юнит-тесты** (US-03, риск R5)
- Файлы: новый `ServiceBooking.API/Services/SlotCalculator.cs`; правка
  `ServiceBooking.API/Services/SlotService.cs:29-51`; новый `ServiceBooking.UnitTests/SlotCalculatorTests.cs`.
- Зависимости: **T-B2**.
- Тесты: ~12 юнит-кейсов (перечень — §2.1). Функциональных не добавляем и не меняем.
- Готово, когда: `dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~BookingsFlowSmokeTests"`
  зелёный **до и после** правки (прогон приложен к PR); публичная сигнатура
  `SlotService.GetAvailableSlotsAsync` и `record TimeSlotResult` не изменились; в PR — дифф цикла
  с пометкой «перенос без изменений».

**T-B4 · US-08: `PlanConfig.IsActive` + запрет удаления тарифа с подписчиками** (Q2)
- Файлы: `ServiceBooking.API/Services/SubscriptionResolver.cs` (новый `static Resolve`, замена строк 86-89);
  `ServiceBooking.API/Controllers/AdminController.cs:321-329` (`DeletePlan` → 409) и `:138-179`
  (`UpdateSubscription` → 400 на неактивный/несуществующий план, до `SaveChangesAsync`);
  новый `ServiceBooking.UnitTests/SubscriptionResolverRulesTests.cs`.
- Зависимости: **T-B2**.
- Тесты: 7 юнит-кейсов (§2.2); функциональные **ADM-032** (удаление плана с активным подписчиком → 409 +
  количество в теле), **ADM-033** (назначение неактивного плана → 400), **ADM-034** (владелец на неактивном
  плане получает Free — проверяется через `onlineBookingEnabled` в `GET /api/companies/{slug}` или 402 на
  `POST /api/bookings`). Хелперы: `ApiTestBase.CreateTestPlanConfigAsync` (`ApiTestBase.cs:186-204`) +
  прямая правка `IsActive` через scope, `SetSubscriptionAsync` (`:275-307`).
- Готово, когда: 31 существующий `ADM-` тест зелёный; `TEST_CATALOG.md` и `API_DOCUMENTATION.md` описывают
  409/400; в PR отмечено, что перед деплоем нужен SQL-прогон из §9 R2 SPEC.

**T-B5 · US-04: `GET /api/workinghours` — проверка принадлежности**
- Файлы: `ServiceBooking.API/Controllers/WorkingHoursController.cs:18-33` — в начало `Get` добавить
  `var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!; if (!await CanManage(masterId, companyId, userId)) return Forbid();`
  Предикат `CanManage` (`:98-106`) **не меняется**.
- Зависимости: нет (файл больше никем в цикле не правится).
- Тесты: **WH-011** посторонний → 403; **WH-012** мастер той же компании про чужого мастера → 403;
  **WH-013** мастер про себя → 200; **WH-014** владелец про мастера своей компании → 200.
  Анонимный → 401 уже покрыт. Существующие WH-001..WH-010 зелёные.
- Готово, когда: BK-тесты слотов зелёные (гость этот эндпоинт не зовёт — US-04 п. 5); `TEST_CATALOG.md`
  и §7 `API_DOCUMENTATION.md` (пункт 3 «Известные ограничения») обновлены.

**T-B6 · US-09 / E5: услуги меняет только владелец** (Q1)
- Файлы: `ServiceBooking.API/Controllers/ServicesController.cs:88-91` — убрать
  `|| cm.Role == UserRole.Master`; ветка `SuperAdmin` (`:86`) сохраняется.
- Зависимости: нет.
- Тесты — **инвертируются существующие**, в этом же PR:
  - `ServiceBooking.Tests/Tests/ServicesTests.cs:49-61` — `SVC-003 Create_ByMaster_Succeeds` →
    `Create_ByMaster_ReturnsForbidden`, ожидание `HttpStatusCode.Forbidden`;
  - `ServiceBooking.Tests/Tests/ServicesTests.cs:105-118` — `SVC-007 Update_ByMaster_UpdatesFields` →
    `Update_ByMaster_ReturnsForbidden` + проверка через `GET /api/services?companyId=`, что имя/цена
    не изменились;
  - новый **SVC-013** `Delete_ByMaster_ReturnsForbidden` + услуга осталась в публичном списке (`IsActive` не снят);
  - новый **SVC-014** `GetByCompany_ByMaster_StillWorks` — мастер читает услуги компании (сценарий
    `ManualBookingModal`, US-09 п. 5).
- Готово, когда: SVC-002/006/010 (владелец) зелёные; `TEST_CATALOG.md` по SVC-003/007/013/014 переписан;
  из `API_DOCUMENTATION.md` (раздел услуг и §7) убрано упоминание мастера как редактора услуг;
  итог прогона ≥ 231 тест, 0 упавших.

**T-B7 · US-05: 404 на несуществующую компанию + глобальный обработчик исключений**
- Файлы: `ServiceBooking.API/Controllers/BookingsController.cs:45-149` (перенос проверки компании,
  переиспользование `company` в `:100-101` и `:141`); `ServiceBooking.API/Program.cs` (после `:132`,
  `UseExceptionHandler` — §3.2); `ServiceBooking.API/Controllers/CompaniesController.cs:347`
  (`Enum.TryParse` → 400).
- Зависимости: нет; **блокирует T-B8** (общий файл).
- Тесты: **BK-027** авторизованный `POST /api/bookings` с несуществующим `CompanyId` → 404 «Company not found»;
  **BK-028** несуществующая компания у владельца **без** тарифа → 404, **не** 402 (порядок проверок,
  US-05 п. 2 — обязателен); **ADM-035** `PUT /api/admin/owners/{неизвестный}/subscription` → 500 с
  `content-type: application/problem+json` и непустым `traceId`; **CO-066** `SuperAdmin` добавляет участника
  с ролью `"Bogus"` → 400.
- Готово, когда: существующие BK/CO зелёные; в PR явно перечислено, что тела 400/402/403/404/409 не
  изменились (R3), и что мапперы `src/utils/*Error.ts` править не потребовалось.

**T-B8 · US-07: `status=upcoming` и 400 на неизвестный статус**
- Файлы: новый `ServiceBooking.API/Services/BookingFilters.cs`;
  `ServiceBooking.API/Controllers/BookingsController.cs:197-218` (`GetClientBookings`);
  новый `ServiceBooking.UnitTests/BookingFiltersTests.cs`.
- Зависимости: **T-B2**, **T-B7**.
- Тесты: ~12 юнит-кейсов (§2.3); функциональные **BK-029** (`upcoming` возвращает только будущие
  `Confirmed`), **BK-030** (`Completed`/`Cancelled` работают как раньше — регрессия),
  **BK-031** (`status=garbage` → 400).
- Готово, когда: `GET /api/bookings/client` без `status` возвращает всё, как раньше;
  `TEST_CATALOG.md` пополнен разделом `GET /api/bookings/client`.

**T-B9 · US-12: фильтр ролей в публичном списке мастеров** (Q6)
- Файлы: `ServiceBooking.API/Controllers/CompaniesController.cs:78-81` — в `memberQuery` добавить
  `&& (cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner)` **до** материализации (`:94`).
  Фильтр по `serviceId` (`:82-93`) и его fallback не трогаются.
- Зависимости: нет (T-B7 правит `:347`, другая часть файла; при одновременной работе — мёржить по очереди).
- Тесты: **CO-067** участник с ролью `Client` (создаётся напрямую в БД через scope, т.к. штатным путём
  такой строки не бывает) в списке не появляется, `Master` и `CompanyOwner` появляются.
- Готово, когда: существующие CO-тесты по `GET /api/companies/{id}/masters` зелёные; в PR отмечено
  требование прогнать перед деплоем `SELECT * FROM "CompanyMembers" WHERE "Role" = 0;` (§5.7 SPEC).

**T-B10 · Строка подключения тестовой БД — в переменную окружения** (подготовка к CI)
- Файлы: `ServiceBooking.Tests/Infrastructure/TestDatabaseFixture.cs:13-14` (константа + чтение
  `SERVICEBOOKING_TEST_CONNECTION`), `ServiceBooking.Tests/Infrastructure/CustomWebApplicationFactory.cs:21`
  (использовать общую константу).
- Зависимости: нет.
- Тесты: новых нет.
- Готово, когда: без переменной окружения `dotnet test ServiceBooking.Tests` даёт тот же результат, что
  и до правки; с переменной — подключается к указанной базе.

**T-B11 · CI на GitHub Actions** (US-11)
- Файлы: новый `.github/workflows/ci.yml`; раздел «CI» в `README.md`.
- Зависимости: **T-B1, T-B2, T-B10, T-F2**.
- Тесты: намеренно сломанный тест в черновом PR — workflow красный (US-11 п. 5), затем откат.
- Готово, когда: workflow зелёный на ветке цикла; полный прогон ≤ 10 мин; отдельным шагом после мёржа E1
  в `dotnet build` добавлен `-warnaserror`.

**T-B12 · US-10: Swagger только в Development + fail-fast** (Q4, риск R4) — **один коммит, последним**
- Файлы: `ServiceBooking.API/Program.cs:18-50, 134-139` + новый блок fail-fast; `docker-compose.prod.yml:25-34`
  (`SuperAdmin__Password`); `.env.production.example`; `DEPLOY.md`; `deploy/nginx/ezbook.conf:34-41`
  (удалить `location /swagger/`).
- Зависимости: **все backend-задачи** (едет последним).
- Тесты: новых автотестов нет (Production-поведение через `WebApplicationFactory` не воспроизводится
  без изменения окружения тестов, а это прямой риск для 230 кейсов). Проверка — ручная, на стенде,
  по DoD п. 8: контейнер без `SUPERADMIN_PASSWORD` не поднимается и пишет причину; с корректным `.env`
  поднимается; `GET /swagger/index.html` на проде → 404.
- Готово, когда: `dotnet test ServiceBooking.Tests` = без изменений (окружение `Testing` не задето);
  `grep -rn "swagger" ServiceBooking.Tests/` пуст; ревьюер подтвердил, что все пять файлов в одном коммите.

### 8.2 Frontend

**T-F1 · E1: мёртвые страницы и мёртвые ветки UI** (US-01 + US-06, Q3)
- Файлы: удалить `frontend/src/pages/DashboardPage.tsx` и `frontend/src/pages/owner/OwnerPage.tsx`;
  в `frontend/src/pages/ClientBookingsPage.tsx` удалить блок цены (`:126-130`) и кнопку «Записаться снова»
  (`:155-163`) вместе с обслуживающей их разметкой; в `frontend/src/types/index.ts` удалить `companySlug`
  (`:64`) и `price` (`:78`) из `Booking`. Редиректы `/dashboard` и `/owner` в `App.tsx:78-79` **сохранить**.
- Зависимости: нет. Делать первой.
- Тесты: нет (юнит-инфраструктуры ещё нет).
- Готово, когда: `grep -rn "DashboardPage\|OwnerPage" frontend/src` пуст;
  `grep -rn "companySlug\|booking.price" frontend/src` не даёт совпадений по типу `Booking`;
  `npx tsc --noEmit` чист, `npm run build` проходит; бэкенд **не тронут** (`BookingDto` и `MapToDto` — как были).

**T-F2 · Инфраструктура тестов фронтенда** (US-02)
- Файлы: новый `frontend/vitest.config.ts` (§1.2); `frontend/package.json` — три скрипта и три
  devDependency; `frontend/package-lock.json`; раздел «Юнит-тесты фронтенда» в `README.md`.
- Зависимости: **T-F1** (чтобы не считать покрытие удаляемого кода).
- Тесты: один smoke-тест, заменяется реальными в T-F3.
- Готово, когда: `npm test` и `npm run test:run` работают; `npm run build` зелёный;
  `frontend/vite.config.ts` и `frontend/tsconfig.json` **не изменены** (проверяется диффом — R6);
  в `dist` нет ни одного файла из тестов.

**T-F3 · Юнит-тесты мапперов ошибок** (US-02)
- Файлы: `frontend/src/utils/bookingError.test.ts`, `companyError.test.ts`, `memberError.test.ts`.
- Зависимости: **T-F2**.
- Тесты: `bookingError` — 9 кейсов, по одному на каждую ветку `switch` (`bookingError.ts:16-35`):
  402 обычный, 402 с `expired`, 403, 409, 404, 400 c `captcha`, 400 c `name`/`phone`, 400 с произвольным
  текстом, `default` (ошибка без `response`). `companyError` — 5 веток (`companyError.ts:15-24`),
  включая 400 с пустым телом. `memberError` — 7 веток (`memberError.ts:15-32`), включая 400 с массивом
  Identity-ошибок (`:23-27`) и 400 со строкой.
- Готово, когда: соответствие «ветка `switch` ↔ тест» проверяемо построчно; покрытие трёх файлов ≥ 80 %.

**T-F4 · Юнит-тесты `authStore`** (US-02)
- Файлы: `frontend/src/store/authStore.test.ts`.
- Зависимости: **T-F2**.
- Тесты: `setAuth` → `isAuthenticated() === true`; `logout` → `false`; `hasRole` для имеющейся и
  отсутствующей роли; `hasRole` при `user === null` не бросает (`authStore.ts:22` — оператор `?.` + `??`).
  Между кейсами состояние сбрасывается через `logout()`.
- Готово, когда: тесты не зависят от порядка выполнения; `localStorage` не течёт между кейсами.

**T-F5 · `canCancelBooking` — вынос и тесты** (US-02)
- Файлы: новый `frontend/src/utils/bookingRules.ts` — именованный экспорт
  `canCancelBooking(b, now = new Date())` (перенос `ClientBookingsPage.tsx:23-27` дословно, плюс параметр
  `now` для детерминированности); импорт в `ClientBookingsPage.tsx`; `frontend/src/utils/bookingRules.test.ts`.
- Зависимости: **T-F2**, **T-F1** (тот же файл).
- Тесты: `Cancelled`/`Completed`/`NoShow` → false; `Confirmed` за 3 часа → true; за 1 час → false;
  **ровно 2 часа → false** (граница, `differenceInHours(...) > 2`).
- Готово, когда: поведение кнопки «Отменить» в UI не изменилось; `tsc` чист.

**T-F6 · Нормализация времени — вынос и тесты** (US-02)
- Файлы: новый `frontend/src/utils/time.ts` — `toApiTime(value: string): string`; использование в
  `frontend/src/api/bookings.ts:26` (`create`) и `:40` (`reschedule`); `frontend/src/utils/time.test.ts`.
- Зависимости: **T-F2**.
- Тесты: `'10:00'` → `'10:00:00'`; `'10:00:00'` без изменений; `'09:30'` → `'09:30:00'`.
- Готово, когда: тело запросов `POST /api/bookings` и `PATCH /api/bookings/{id}/reschedule` побайтово
  такое же, как до правки.

**T-F7 · 409 при деактивации тарифа** (US-08 п. 4)
- Файлы: новый `frontend/src/utils/planError.ts` — `getPlanErrorMessage(error)` по конвенции
  `src/utils/*Error.ts` (409 → «На этом тарифе есть активные подписчики (N). Сначала переведите их на другой
  тариф.»; 404 → «Тариф не найден»; default → «Не удалось деактивировать тариф»); показ ошибки рядом с
  кнопкой «Деактивировать» в `frontend/src/pages/admin/PlansTab.tsx:97-100, 182-189`;
  `frontend/src/utils/planError.test.ts`.
- Зависимости: **T-F2**; контракт — раздел `DELETE /api/admin/plans/{id}` в `API_CONTRACT.md`
  (кодируется параллельно с T-B4, без ожидания её мёржа).
- Тесты: 409 с телом-строкой, содержащей число; 409 без числа (fallback без «(N)»); 404; default.
- Готово, когда: `deactivateMut` перестаёт молча проглатывать ошибку; ручная проверка на плане с подписчиком.

**T-F8 · Неактивный текущий тариф в `SubscriptionModal`** (US-08 п. 4)
- Файлы: `frontend/src/pages/AdminPage.tsx:51-95` — если `owner.planConfigId` указывает на план с
  `isActive === false`, он добавляется в `<select>` отдельной пометкой «(неактивен)» и остаётся выбранным
  по умолчанию; выбрать неактивный план заново нельзя (список выбора по-прежнему `activePlans`, `:59`).
- Зависимости: **T-F2** (для тестов не обязательна — компонентных тестов здесь нет).
- Тесты: автотестов нет (компонентные вне цикла); проверка ручная — открыть подписку владельца,
  сидящего на деактивированном тарифе, и убедиться, что значение не сбрасывается на «Free».
- Готово, когда: сохранение без изменения плана не переводит владельца на Free.

### 8.3 Опционально (E6, только если E1–E5 и E7 закрыты)

Берётся строго в этом порядке, каждый пункт — отдельный PR: **E6-4** (серверное окно отмены записи,
`BookingsController.cs:334-351`) → **E6-1** (`RemoveMember` снимает Identity-роль, если не осталось ни одной
membership с этой ролью) → **E6-3** (бейдж «в разработке» для рассылки/предоплаты/`NotifyDaysBefore`).
E6-3 и E6-4 требуют продуктового решения — до его получения задачи не начинать (§5.8 SPEC).

---

## 9. Риски и как их держит эта архитектура

| # (SPEC) | Риск | Ответ архитектуры |
|---|---|---|
| R1 | Запрет мастеру на услуги ломает чей-то сценарий | Правка — удаление одного условия в `ServicesController.cs:91`; откат = один коммит revert. SVC-014 отдельно фиксирует, что **чтение** услуг мастером не сломано |
| R2 | Учёт `PlanConfig.IsActive` выключает возможности платящему | Q2: 409 на удаление плана с подписчиками (T-B4) делает ситуацию недостижимой. Перед деплоем — SQL-проверка, зафиксирована в PR T-B4 |
| **R3** | `ProblemDetails` ломает мапперы ошибок | §3.1: жёсткая таблица «код → формат». `AddProblemDetails()` и `UseStatusCodePages` **запрещены**. 500 попадает в `default` мапперов — там уже есть корректный текст. Мапперы в T-B7 не правятся |
| **R4** | Fail-fast роняет боевой контейнер | §4.3: найден пробел в `docker-compose.prod.yml` — без него fail-fast роняет прод **гарантированно**. Один коммит из 5 файлов, последняя волна, ручная проверка на стенде до прода |
| **R5** | Рефакторинг слотов меняет ядро продукта | §2.1: перенос дословный, публичная сигнатура не меняется, BK-001..BK-026 прогоняются до и после в одном PR, дифф цикла показывается ревьюеру |
| R6 | Раннер фронта конфликтует со сборкой | §1.2: отдельный `vitest.config.ts`, `vite.config.ts`/`tsconfig.json` не трогаются, глобалы не включаются, тесты в `src/**` не достижимы из `index.html`. Критерий T-F2 — дифф двух файлов пустой |
| R7 | 403 в `GET /api/workinghours` ломает неизвестный вызов UI | Потребители перепроверены по коду (`ScheduleTab.tsx:250,259,271`, `CabinetPage.tsx:144-146`); WH-013/WH-014 фиксируют оба живых сценария; остаток — ручной чек-лист |
| R8 | Удаление Blazor-проекта задевает деплой | `ServiceBooking.API/Dockerfile:7-13` копирует и билдит только 3 csproj; критерий T-B1 включает `docker compose up --build` |
| R9 | Скоуп расползается | §8.3: E6 берётся только после закрытия E1–E5, E7; §7 SPEC не трогаем. Новые находки (например, 500 на неизвестном `ownerUserId` в `UpdateSubscription`) не чинятся, а документируются |
| — | Новый: конфликты в общих файлах | `ServiceBooking.sln` (T-B1→T-B2), `BookingsController.cs` (T-B7→T-B8), `Program.cs` (T-B7→T-B12) сериализованы явно в §7 |

---

## 10. Соответствие Definition of Done (§8 SPEC)

| DoD | Чем закрывается |
|---|---|
| 1. Сборка 0 ошибок / 0 предупреждений | T-B1; в CI закрепляется `-warnaserror` (T-B11) |
| 2. `ServiceBooking.Tests` ≥ 230, 0 упавших; SVC-003/007 инвертированы, SVC-013 добавлен | T-B6 + новые кейсы T-B4/B5/B7/B8/B9 → ожидаемо **246** прогонов |
| 3. Юнит-тесты бэкенда < 10 с, при остановленной PostgreSQL | §1.1, T-B2/B3/B4/B8 (~31 тест) |
| 4. `npm run test:run` < 30 с | §1.2, T-F2..T-F7 (~36 тестов) |
| 5. `npm run build` и `tsc --noEmit` | T-F1, T-F2 (конфиги сборки не тронуты) |
| 6. Мёртвый код удалён | T-B1, T-F1 |
| 7. `docker compose up --build` / prod-сборка | критерий T-B1, повторно T-B12 |
| 8. Ручной чек-лист QA | шесть сценариев распределены: услуги владельца и `ManualBookingModal` — T-B6; расписание мастера/владельца — T-B5; гостевая запись — T-B7/T-B9; «Предстоящие» — T-B8; карточка визита — T-F1 |
| 9. `TEST_CATALOG.md`, `API_DOCUMENTATION.md`, `README` | входят в критерий готовности каждой соответствующей задачи |
| 10. Workflow зелёный / красный на сломанном тесте | T-B11 |
| 11. Q1–Q6 реализованы | Q1→T-B6, Q2→T-B4, Q3→T-F1, Q4→T-B12, Q5→T-B11, Q6→T-B9 |
| 12. `CURRENT_STATE.md` не редактируется | ни одна задача его не трогает |

---

## 11. Что архитектура сознательно НЕ делает

- Не вводит общий сервис прав (§9 P2-11 CURRENT_STATE) — §2.4.
- Не трогает таймзоны (§9 P1-6): «сейчас» для `upcoming` = `DateTime.UtcNow`, с комментарием и ссылкой на §7 SPEC.
- Не делает шаг сетки слотов настраиваемым (§9 P2-9) — юнит-тест лишь **фиксирует** текущие 30 минут.
- Не унифицирует формат ошибок API (R3) — `ProblemDetails` только для 500.
- Не чинит 500 на несуществующем `ownerUserId` в `AdminController.UpdateSubscription` — вместо этого
  использует его как стабильную точку проверки глобального обработчика (ADM-035) и фиксирует
  в `API_DOCUMENTATION.md` §7 как известное ограничение.
- Не переносит `frontend/design_handoff_site_redesign/` — §6.
- Не добавляет `@testing-library/*`: компонентные тесты — следующий цикл (§7 SPEC).
