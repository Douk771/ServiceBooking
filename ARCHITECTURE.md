# ARCHITECTURE — цикл санации ServiceBooking (ревизия 2, скоуп после аудита)

**Вход:** `SPEC.md` ревизии 2 (решения Q1–Q12 в §0 — окончательные, открытых вопросов к заказчику нет),
`CURRENT_STATE.md` (коммит `263c661`), `CODE_REVIEW.md` (аудит, 12 блокеров).
**Тип работ:** изменение существующей кодовой базы. Стек не выбирается и не меняется.
**Ветка:** `sanitation-cycle`, база — `e6b746c`.
**Baseline, который нельзя ухудшать:** `dotnet test ServiceBooking.Tests` → 230/230; `dotnet build ServiceBooking.sln`
→ 0 ошибок (2 предупреждения остаются до цикла B); `npx tsc --noEmit` во `frontend/` → чисто.

**Что изменилось относительно предыдущей редакции этого документа.** SPEC переписан целиком: цикл A
перестал быть уборкой техдолга и стал закрытием эксплуатируемых обходов бизнес-гейтов и межарендных
утечек. Из документа **удалены** разделы про фронтовый тест-раннер (Vitest — уехал в цикл B, US-21),
про удаление мёртвого кода (US-01, цикл B) и утверждение «`WorkingHoursController.CanManage` вообще
не меняется» (оно описывало уязвимость A5 как норму). **Сохранены без пересмотра:** отдельный проект
юнит-тестов бэкенда (§1), выделение чистых функций `SubscriptionResolver.Resolve` и `BookingFilters`
(§2.4, §2.5), жёсткая граница формата тел ошибок и устройство глобального обработчика (§6),
Swagger + fail-fast с найденным пробелом в `docker-compose.prod.yml` (§7), вынос строки подключения
тестовой БД в переменную окружения и workflow GitHub Actions (§8).

Документ закрывает все восемь вопросов из §12 SPEC и даёт план, по которому backend- и
frontend-разработчик работают параллельно по `API_CONTRACT.md`, не читая код друг друга.

Раздел §14 — противоречия и пробелы, найденные в SPEC при проектировании. Читать обязательно:
три из них меняют состав работ.

---

## 0. Принципы этого цикла

1. **Конвенции §6 `CURRENT_STATE` не пересматриваются.** Primary constructors, `record`-DTO, ручной
   `MapToDto`, 402 для тарифных гейтов, приватные асинхронные предикаты прав внутри контроллеров,
   транзакция + `pg_advisory_xact_lock` для check-then-act, комментарии «почему» на английском;
   на фронте — слой `src/api/<домен>.ts`, react-query, мапперы ошибок в `src/utils/*Error.ts`.
2. **Изменения прав только сужают доступ.** Ни одна задача цикла не открывает данные тому, кто
   их сегодня не видит. Единственное расширение выдачи — `phone`/`email` в `GET /api/masters/clients`
   (US-22), и это осознанное снятие недоделанного правила по решению Q10.
3. **Рефакторинг = перенос, а не переписывание.** Там, где логика выносится в чистую функцию, тело
   переносится дословно; ревьюер сверяет диффом посимвольно (R5).
4. **Новых слоёв, папок и абстракций не заводим.** Ни репозиториев, ни MediatR, ни AutoMapper,
   ни `IPermissionService` с интерфейсом и регистрацией в DI. Новые файлы кладутся в существующие
   каталоги (`ServiceBooking.API/Services/`).
5. **Новых библиотек в цикле A нет вовсе.** Ни на бэкенде (версии в новом тестовом проекте совпадают
   с `ServiceBooking.Tests/ServiceBooking.Tests.csproj:12-23`), ни на фронте (`package.json` не
   получает ни одной новой зависимости — раннер уехал в цикл B).
6. **Формат тел ответов для 400/402/403/404/409 не меняется** (R3), и это ограничение
   распространяется на **все новые коды цикла**: новые 400/403/409 отдают голую строку `text/plain`
   либо пустое тело. `ProblemDetails` вводится исключительно для необработанных исключений (500).
7. **Ломающие миграции схемы допустимы** (решение Q12): сервис не в продакшене, боевых данных нет.
   Строки, мешающие применить миграцию, удаляются внутри самой миграции. Отдельного этапа переноса
   данных, скриптов и ручного разбора с владельцами **не проектируем**.

---

## 1. Тестовый инструментарий

### 1.1 Бэкенд: отдельный проект `ServiceBooking.UnitTests`

**Решение (ответ на §12.8 SPEC): остаётся в силе — отдельный проект в solution, а не коллекция внутри
`ServiceBooking.Tests`.** Сокращение скоупа его не отменяет: объём чистой логики в цикле A даже вырос
(`SlotCalculator` теперь обслуживает и валидацию).

Обоснование (не изменилось):
- `ServiceBooking.Tests` физически привязан к БД: `TestDatabaseFixture.InitializeAsync`
  (`ServiceBooking.Tests/Infrastructure/TestDatabaseFixture.cs:18-34`) дропает базу и поднимает хост
  до первого теста коллекции, а `AssemblyInfo.cs` отключает параллелизм на всю сборку. DoD п. 4
  требует прогона **при остановленной PostgreSQL** — в одной сборке это недостижимо.
- Требование «одна документированная команда» выполняется буквально: `dotnet test ServiceBooking.UnitTests`.
- Проект не ссылается на `Microsoft.AspNetCore.Mvc.Testing` — физически не может поднять
  `WebApplicationFactory`.

Файл: `ServiceBooking.UnitTests/ServiceBooking.UnitTests.csproj`

| Свойство | Значение | Почему |
|---|---|---|
| `TargetFramework` | `net8.0` | как везде |
| `ImplicitUsings` / `Nullable` | `enable` | конвенция §6 |
| `IsTestProject` | `true` | |
| PackageReference | `xunit` 2.5.3, `xunit.runner.visualstudio` 2.5.3, `Microsoft.NET.Test.Sdk` 17.8.0, `FluentAssertions` 6.12.1, `coverlet.collector` 6.0.0 | ровно те же версии, что в `ServiceBooking.Tests.csproj:13-22` — новых пакетов нет |
| **Нет** пакета | `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.Design` | гарантия, что юнит-тест не превратится в интеграционный |
| ProjectReference | `..\ServiceBooking.API\ServiceBooking.API.csproj` | чистые функции живут в `ServiceBooking.API/Services/` |
| `<Using Include="Xunit" />` | да | как в существующем проекте |

Структура:
```
ServiceBooking.UnitTests/
├── ServiceBooking.UnitTests.csproj
├── SlotCalculatorTests.cs            ← расчёт слотов + валидация слота (один и тот же код)
├── SubscriptionResolverRulesTests.cs
└── BookingFiltersTests.cs
```

**Маркировка тестов.** `[Fact, TestCase("UNIT-NNN")]` **не используется** — решение прежнее и в силе.
Причины: (а) `TestCaseAttribute` лежит в `ServiceBooking.Tests/Infrastructure/TestCaseAttribute.cs`,
и переиспользование потребовало бы либо ссылки на проект с `Mvc.Testing`, либо копии атрибута;
(б) `TEST_CATALOG.md` — каталог сценариев API («PREFIX-NNN» → HTTP-вызов), юнит-кейсы в эту модель
не ложатся. Имена методов самодокументирующие: `IsSlotAllowed_StartsInsideBreak_ReturnsFalse`.
В `TEST_CATALOG.md` добавляется раздел «Юнит-тесты» с составом наборов и командами запуска.

**Команды (идут в `README.md` и `TEST_CATALOG.md`):**
```bash
dotnet test ServiceBooking.UnitTests                                    # < 10 с, PostgreSQL не нужна
dotnet test ServiceBooking.UnitTests --collect:"XPlat Code Coverage"    # cobertura для проверки 80 %
dotnet test ServiceBooking.Tests                                        # функциональные, нужна PostgreSQL
```

**Покрытие — отчёт, а не гейт.** Порог 80 % по файлам `SlotCalculator.cs`/`SlotService.cs`/
`SubscriptionResolver.cs`/`BookingFilters.cs` проверяется ревьюером по cobertura-отчёту
`coverlet.collector`. Гейт в CI не ставим: data collector порогов не поддерживает, а `coverlet.msbuild` —
новый пакет, которого принцип §0.5 не разрешает.

### 1.2 Фронтенд: тест-раннера в цикле A нет

Раздел про Vitest + jsdom + `frontend/vitest.config.ts` из предыдущей редакции **удалён целиком**:
US-21 уехал в цикл B (§8.1 SPEC). Практические следствия, которые надо держать в голове:

- `frontend/package.json`, `vite.config.ts`, `tsconfig.json` в цикле A **не изменяются вовсе** —
  это критерий готовности всех фронтовых задач;
- фронтовые правки цикла (US-19, `companyId` в слотах, `companyName` в «Моих записях») не покрыты
  автотестами и закрываются ручным чек-листом QA — зафиксировано риском R11;
- когда раннер появится в цикле B, решение из предыдущей редакции (отдельный `vitest.config.ts`,
  без глобалов, `coverage.include` сужен) остаётся валидным — его текст сохранён в git-истории
  этого файла и переиспользуется без пересмотра.

---

## 2. Общие механизмы, которые вводит цикл

Четыре из шести историй S1 нуждаются в одном и том же кирпиче — «вызывающий действительно работает
в этой компании». Ещё две нуждаются в одном и том же правиле сетки слотов. Оба кирпича проектируются
здесь **один раз**, чтобы не расползлись копиями.

### 2.1 Единый предикат членства `CompanyMembership` (ответ на §12.1 SPEC)

**Решение: общий *сервис* прав по-прежнему не вводим, но вводим один общий *запрос*.**

Ревизия 1 решила не унифицировать, потому что правки прав были точечными. Сейчас проверка
«вызывающий состоит в компании с ролью X» нужна в пяти местах: `BookingsController.Create`,
`BookingsController.GetSlots`, `BookingsController.GetOccupied`, `WorkingHoursController.CanManage`,
`ScheduleTemplateController.CanManage`. Пять копий одного SQL-предиката, каждая из которых может
разъехаться, — это ровно та болезнь, от которой мы лечим `GetStats`/`ReportsController` (B5).
При этом ограничение SPEC жёсткое: унификация **не должна** превращать `AnyAsync(cm => …)` в загрузку
роли в память.

Новый файл `ServiceBooking.API/Services/CompanyMembership.cs`, namespace `ServiceBooking.API.Services`:

```csharp
/// <summary>
/// The single SQL definition of "this caller actually works in this company". Everything stays a
/// server-side EXISTS — no membership row is ever materialised, so this is not a permission service,
/// just one query with one name. Callers keep their own private CanManage/CanManageCompany predicates
/// (project convention, CURRENT_STATE §6) and delegate the membership half of the rule here.
/// </summary>
public static class CompanyMembership
{
    /// <summary>Member with a role that can act on behalf of the business (Master or CompanyOwner).</summary>
    public static Task<bool> IsStaffAsync(AppDbContext db, Guid companyId, string userId) =>
        db.CompanyMembers.AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == userId &&
            (cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner));

    /// <summary>Member with the CompanyOwner role.</summary>
    public static Task<bool> IsOwnerAsync(AppDbContext db, Guid companyId, string userId) =>
        db.CompanyMembers.AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == userId &&
            cm.Role == UserRole.CompanyOwner);
}
```

Свойства решения:
- **статический класс, не сервис.** В DI не регистрируется, `AppDbContext` передаётся параметром —
  ни новой абстракции, ни изменения конструкторов контроллеров, ни правки `Program.cs`;
- **запрос остаётся запросом.** `AnyAsync` транслируется в `EXISTS`, как и сегодня;
- **сфера применения ограничена.** Существующие `ServicesController.CanManageCompany` (`:82-92`) и
  `CompaniesController.CanManageCompany` в цикле A **не переводятся** на этот класс: их диффы должны
  остаться минимальными (US-09 — удаление одной строки, R1). Унификация всех шести копий
  (§9 P2-11 `CURRENT_STATE`) остаётся отложенной.

Роль `Client` не проходит `IsStaffAsync` намеренно: это закрывает гипотезу E2 аудита в тех местах,
которые цикл трогает, не расширяя скоуп на `MastersController`.

### 2.2 `SlotCalculator`: одно правило для выдачи слотов и для валидации (ответ на §12.2 SPEC)

**Решение: одна чистая функция `Calculate`, а валидация слота — проверка вхождения запрошенного
времени в её результат. Второго правила не появляется физически, а не «по договорённости».**

US-03 из «удобства тестирования» стал предусловием безопасности (§6.6 SPEC): серверная валидация в
`Create`/`Reschedule` обязана применять то же правило, что и выдача слотов. Альтернатива —
самостоятельный предикат «слот допустим» — даёт два места, где написаны границы окна, перерывы и шаг
сетки; они разъедутся так же, как разъехались два отчёта (B5). Стоимость выбранного варианта — расчёт
списка максимум из 48 элементов в памяти на одну запись; это ничто на фоне трёх запросов, которые
метод и так делает.

Новый файл `ServiceBooking.API/Services/SlotCalculator.cs`:

```csharp
public record TimeRange(TimeOnly Start, TimeOnly End);

public static class SlotCalculator
{
    public const int StepMinutes = 30;

    /// <summary>Pure slot grid: no DB access, no EF types. The single source of truth for
    /// "which start times this master can be booked at on this date".</summary>
    public static List<TimeSlotResult> Calculate(
        int serviceDurationMinutes,
        TimeOnly? workStart, TimeOnly? workEnd,      // null = no schedule row for this date
        IReadOnlyList<TimeRange> breaks,
        IReadOnlyList<TimeRange> bookings,
        bool allowWithoutSchedule);

    /// <summary>The booking-time check. Deliberately implemented ON TOP of Calculate rather than as a
    /// second predicate: a separate implementation of "inside working hours, not in a break, on the
    /// 30-minute grid" is exactly how GetStats and ReportsController drifted apart (audit B5).</summary>
    public static bool IsSlotAllowed(
        TimeOnly requestedStart,
        int serviceDurationMinutes,
        TimeOnly? workStart, TimeOnly? workEnd,
        IReadOnlyList<TimeRange> breaks,
        IReadOnlyList<TimeRange> bookings,
        bool allowWithoutSchedule)
        => Calculate(serviceDurationMinutes, workStart, workEnd, breaks, bookings, allowWithoutSchedule)
            .Any(s => s.Start == requestedStart);
}
```

Правила переноса (обязательны, проверяются на ревью диффом):
1. Тело цикла `SlotService.cs:34-49` переносится **дословно**: условие `while (current + duration <= end)`,
   защита от переполнения суток `if (current + duration >= TimeSpan.FromDays(1)) break;` (`SlotService.cs:37`),
   пересечения `b.StartTime < slotEnd && b.EndTime > slotStart` (`:42-43`), шаг `+= 30 мин` (`:48`).
   Комментарий про `TimeOnly` и 24:00 переезжает вместе с кодом.
2. `workStart is null` → `TimeSpan.Zero`, `workEnd is null` → `TimeSpan.FromHours(24)` — точная копия
   `SlotService.cs:31-32`.
3. Правило «нет расписания и `allowWithoutSchedule = false` → пустой список» живёт **в калькуляторе**.
   В `SlotService.GetAvailableSlotsAsync` ранний возврат (`SlotService.cs:22`) **сохраняется** как
   оптимизация — он экономит запрос за бронями; рядом обязателен комментарий на английском:
   guard duplicates the rule intentionally to skip the bookings query; the authoritative rule lives
   in `SlotCalculator`.
4. **Дату в прошлом калькулятор не знает и знать не должен.** Сегодня `GET /api/bookings/slots`
   возвращает прошедшие слоты, а отсекает их фронт (`BookingModal.tsx:83`, `ManualBookingModal.tsx:100`).
   Добавление «сейчас» внутрь `Calculate` изменило бы публичное поведение выдачи слотов, что US-03 п. 3
   прямо запрещает. Поэтому проверка «не в прошлом» — **отдельный явный шаг в контроллере**, до вызова
   калькулятора (§3.2, шаг 8). Это не второе правило сетки: оно про другое измерение.

`SlotService.GetAvailableSlotsAsync` после правки: те же три запроса в том же порядке
(`Services.FindAsync`, `WorkingHours` + `Include(Breaks)`, `Bookings`), плюс **новый обязательный
параметр `Guid companyId`**, который добавляется в предикат `WorkingHours` (находка A4,
`SlotService.cs:18-21`). Публичная сигнатура метода расширяется одним параметром, `record TimeSlotResult`
не меняется.

Регрессионная сетка: BK-001..BK-026 (`ServiceBooking.Tests/Tests/BookingsFlowSmokeTests.cs`), особенно
BK-024..BK-026 (ветка `manual`). Прогон обязателен **до** и **после** рефакторинга, в одном PR.

Юнит-кейсы `Calculate` (перенос из ревизии 1 + границы): сетка 30 мин внутри окна; пересечение с бронью;
пересечение с перерывом; нет расписания + `allow=false` → `[]`; `allow=true` → сетка от 00:00 без выхода
за сутки; услуга длиннее окна → `[]`; услуга 45 мин на 30-минутной сетке — фиксируем фактическое
поведение как известное ограничение (§9 P2-9 `CURRENT_STATE`).
Юнит-кейсы `IsSlotAllowed` (новые, US-13 п. 4): точное попадание в слот → `true`; 10:07 (не на сетке) →
`false`; внутрь перерыва → `false`; за границей окна → `false`; на занятое время → `false`;
нет расписания + `allow=false` → `false`; нет расписания + `allow=true` + 03:00 → `true`.

### 2.3 Занятость мастера считается по всем компаниям — намеренно (решение Q9)

Правило, которое цикл трогает в трёх местах (`GetOccupied`, конфликт-чек в `Create` и в `Reschedule`)
и **не меняет ни в одном**: пересечения ищутся по `MasterId + Date` **без `CompanyId`**. Мастер-совместитель
физически один, и запись в компании A обязана блокировать то же время в компании B.

При этом расписание (`WorkingHours`) скоупится по компании (A4, §2.2). Асимметрия намеренная и
неочевидная, поэтому в коде она фиксируется комментарием на английском в каждом из трёх мест — иначе
следующий читающий «починит» это как утечку:

```csharp
// Occupancy is deliberately NOT scoped by company: a master who works for two businesses is still one
// person, so a booking made in company A must block the same time in company B. Working hours ARE
// scoped by company (a master can keep different schedules) — the asymmetry is intentional.
```

Это же обоснование дословно попадает в `API_DOCUMENTATION.md` (раздел `GET /api/bookings/occupied`).

### 2.4 `SubscriptionResolver` → статический `Resolve` (без изменений)

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
(`.Include(s => s.PlanConfig)`, `:77-80`) и в цикле вызывает `Resolve(sub, now)`. Новый класс не заводим:
метод остаётся в том же файле рядом с `EffectivePlan`.

Юнит-кейсы — 7 (подписки нет; `sub.IsActive = false`; `PaidUntil` в прошлом; `PaidUntil = null` + активна;
`PlanConfig = null`; **`PlanConfig.IsActive = false` → Free**; батч по нескольким владельцам —
последний остаётся в функциональном наборе).

### 2.5 Фильтр «предстоящие» → `BookingFilters` (без изменений, ответ на §12.7 SPEC)

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

**Что считаем «сейчас» (ответ на §12.7 SPEC — подтверждаем ревизию 1):** `DateTime.UtcNow`, из него
`DateOnly.FromDateTime` и `TimeOnly.FromDateTime`. Обоснование: (а) весь остальной код уже живёт на
`DateTime.UtcNow` (`SubscriptionResolver.cs:75`, `Booking.CreatedAt`), вводить второй источник времени
в цикле, где таймзоны осознанно отложены, нельзя; (б) для клиента в UTC+3 UTC-время отстаёт, то есть
запись остаётся во вкладке «Предстоящие» чуть дольше — безопасный режим отказа. В коде — комментарий
с этой аргументацией.

**Тот же `DateOnly.FromDateTime(DateTime.UtcNow)` — «сегодня» для проверки «дата не в прошлом»
в `Create`/`Reschedule`** (§3.2 шаг 8). Один источник времени на весь цикл.

### 2.6 Отзыв `SecurityStamp` (US-17, ответ на §12.5 SPEC)

**Решение: claim в токене + сверка внутри уже существующего `OnTokenValidated`, без единого
дополнительного запроса к БД.**

`OnTokenValidated` (`Program.cs:93-113`) уже загружает пользователя (`userManager.FindByIdAsync`,
`:102`), чтобы перечитать роли. `AppUser.SecurityStamp` приходит той же строкой — значит сверка
бесплатна.

1. `ServiceBooking.API/Services/TokenService.cs:16-24` — в список claim'ов добавляется
   `new("sstamp", user.SecurityStamp ?? "")`. Имя короткое и не пересекается ни с одним существующим
   (`sub`, `phone`, `given_name`, `family_name`, `jti`, `email`, роли).
2. `Program.cs`, внутри `OnTokenValidated`, **после** `if (user is null)` (`:103`) и **до** перечитки
   ролей:
   ```csharp
   // A JWT lives up to 7 days, so changing a leaked password must invalidate tokens issued before it.
   // ASP.NET Identity already rotates SecurityStamp on ChangePasswordAsync/SetUserNameAsync; we compare
   // the stamp baked into the token with the current one. `user` is already loaded for the role refresh
   // below, so this costs no extra query.
   var stamp = principal.FindFirstValue("sstamp");
   if (stamp is null || stamp != user.SecurityStamp) { context.Fail("Token has been revoked"); return; }
   ```
3. **Совместимость со старыми токенами.** Токен без claim'а `sstamp` отклоняется (`stamp is null`).
   Это допустимо: сервис не в продакшене (Q12), а в тестах токены выдаются штатным логином.
   Отдельного «переходного релиза» не проектируем — это прямое следствие решения Q12.
4. Фронт не правится: `client.ts:15-24` уже разлогинивает по 401. Но правка обязана ехать
   **согласованно с US-19 п. 2** (§5.2): после US-17 401 на защищённом эндпоинте становится штатным
   сценарием «токен отозван», и редирект там должен остаться — исключаются только `/auth/login`
   и `/auth/register`.

Регрессия: все 230 тестов логинятся штатно и получают claim, поэтому не задеты.
`ChangePhone` меняет `UserName` через `SetUserNameAsync` — штамп ротируется, эффект тот же.

---

## 3. `POST /api/bookings` и `PATCH /{id}/reschedule` — ядро цикла (US-13, US-05, US-03)

Самая рискованная часть работы: один метод, четыре находки (A1, A2, A3 + подмножество C2), реально
используемый гостевой поток и намеренно расширенное коммитом `0c8755d` поведение ручной записи.

### 3.1 Кто такой «персонал»

Сегодня (`BookingsController.cs:54-55`) «персонал» = «аутентифицирован и прислал `guestName`».
После правки:

```csharp
var isManualBooking = !string.IsNullOrEmpty(dto.GuestName);
var isStaff = isAuthenticated &&
    (User.IsInRole("SuperAdmin") || await CompanyMembership.IsStaffAsync(db, dto.CompanyId, userId!));
var isStaffManualBooking = isManualBooking && isStaff;

// Anyone who supplies guest details without actually working here is not staff — they are a guest with
// an account, and they go through the exact same gates a guest does (self-booking toggle, captcha,
// tariff). This is the A1 bypass: previously `guestName` alone was enough to skip all four.
var isGuestPath = !isAuthenticated || (isManualBooking && !isStaff);
```

`isGuestPath` — ключевая формулировка: она даёт критерий приёмки US-13 п. 2 буквально и **не меняет
поведение для обычного авторизованного клиента** (у него нет `guestName` → путь тот же, что сегодня:
только тарифный гейт).

### 3.2 Порядок проверок в `Create` после правки

```
 0. 400  автоматическая валидация [ApiController] (Range/MaxLength в CreateBookingDto)
 1. 404  "Company not found"                       ← поднято из ветки !isAuthenticated (US-05)
 2. --   вычисление isStaff / isGuestPath (§3.1)
 3. 403  Forbid()                                   ← [isGuestPath] !company.AllowSelfBooking
 4. 400  "Captcha required for guest booking"       ← [isGuestPath] капча включена, токена нет
 5. 400  "Invalid captcha"                          ← [isGuestPath] капча не прошла
 6. 400  "Name and phone are required for guest booking" ← [isGuestPath] нет guestName/guestPhone
 7. 402  "Online booking requires a paid subscription."  ← !AllowOnlineBooking && !isStaffManualBooking
 8. 404  "Service not found"
 9. 400  "Service does not belong to this company"  ← НОВОЕ (A2)
10. 400  "Service is not available"                 ← НОВОЕ (A2), service.IsActive == false
11. 400  "Master is not a staff member of this company" ← НОВОЕ (A2), CompanyMembership.IsStaffAsync
12. 409  "Time slot is no longer available"         ← НОВОЕ (A3): дата+время в прошлом
--- BeginTransaction + AdvisoryLock("booking-slot:{masterId}:{date}") ---
13. 409  "Time slot is no longer available"         ← валидация слота (см. §3.3)
14. 201  Created + BookingDto + Location
```

Обязательные свойства порядка:
- Шаг 1 идёт **до** шага 7 — критерий US-05 п. 2 (несуществующая компания даёт 404, а не 402);
- шаги 9–11 идут **после** 402: тарифный гейт остаётся первым платным рубежом, как сегодня;
- шаги 12–13 отдают **дословно тот же текст**, что и существующий конфликт-чек. Ни одного нового
  текста 409 в цикле не появляется (R3): `bookingError.ts:23-24` продолжает показывать
  «Это время уже занято. Выберите другой слот.», что верно и для нерабочего времени, и для прошлого.
- Новые тексты 400 (шаги 9–11) намеренно **не содержат подстрок** `captcha`, `name`, `phone` — иначе
  они провалятся в чужую ветку `bookingError.ts:28-31`. Это проверяется ревьюером явно.

Попутные внутренние правки того же метода (на контракт не влияют):
- `company` загружается один раз и переиспользуется в проверке предоплаты (`:100-101`) и в
  `bookingCompany` (`:141`) — уходят два лишних `FindAsync` (находка D11);
- дубль `using ServiceBooking.Core.Entities;` (`BookingsController.cs:10`, CS0105) удаляется —
  файл всё равно переписывается (единственное исключение из «US-01 в цикле B», §8.1 SPEC).

### 3.3 Валидация слота внутри лока

Внутри транзакции, после взятия `pg_advisory_xact_lock` (существующий ключ
`booking-slot:{masterId}:{date}`, `BookingsController.cs:125-126`):

```
bookings = SELECT StartTime, EndTime FROM Bookings
           WHERE MasterId = dto.MasterId AND Date = dto.Date AND Status <> Cancelled
           -- НЕ скоупится по компании: §2.3

if (isStaffManualBooking):
    // Q7: staff may book any free time, no schedule/breaks/grid rule applies.
    conflict = bookings.Any(b => b.StartTime < slotEnd && b.EndTime > dto.StartTime)
    if (conflict) → 409
else:
    wh = SELECT ... FROM WorkingHours
         WHERE MasterId = dto.MasterId AND CompanyId = dto.CompanyId AND Date = dto.Date
           AND IsWorking   -- Include(Breaks)
    ok = SlotCalculator.IsSlotAllowed(dto.StartTime, service.DurationMinutes,
                                      wh?.StartTime, wh?.EndTime, wh?.Breaks ?? [], bookings,
                                      allowWithoutSchedule: false)
    if (!ok) → 409
```

Почему так:
- **клиентский и гостевой путь** получает ровно то правило, которое ему выдал
  `GET /api/bookings/slots` (US-03 п. 2): `IsSlotAllowed` — это `Calculate(...)`, тот же код;
  проверка пересечения с бронями входит в `Calculate` и потому отдельного конфликт-чека не требует;
- **персонал** получает только «не в прошлом» (шаг 12) и «не пересекается» (Q7) — существующий
  конфликт-чек сохраняется дословно, включая выражение `b.StartTime < slotEnd && b.EndTime > dto.StartTime`;
- обе ветки живут **внутри одного лока и одной транзакции**, поэтому TOCTOU-гарантия,
  подтверждённая аудитом как корректная (раздел F `CODE_REVIEW.md`), не ослабевает;
- «влезает в сутки» отдельной проверкой не вводится: для клиента это делает калькулятор
  (`SlotService.cs:37`), для персонала — `slotEnd = dto.StartTime.AddMinutes(duration)` на `TimeOnly`
  переполняется по кругу и даст `EndTime < StartTime`; чтобы этого не было, шаг 12 дополняется
  условием `dto.StartTime.ToTimeSpan() + duration >= TimeSpan.FromDays(1) → 409`. Один и тот же 409,
  тот же текст.

### 3.4 `PATCH /api/bookings/{id}/reschedule` — правило персонала, а не клиента

**Решение архитектора, расходящееся с буквой SPEC (см. §14.1): перенос записи валидируется по
правилу персонала (не в прошлом + нет пересечения), а не против `WorkingHours`.**

Обоснование:
- эндпоинт **staff-only**: `Reschedule` (`BookingsController.cs:296-332`) пускает только через
  `CanManageBookingAsync` (`:357-362`) — назначенный мастер, `CompanyOwner` компании записи или
  `SuperAdmin`. Клиент перенести запись не может вообще (у него есть только `Cancel`, `:342`);
- значит, по решению Q7 к этому эндпоинту применяется **та же смягчённая валидация**, что к ручной
  записи персонала: любое незанятое время, кроме прошлого;
- альтернатива (полная валидация против расписания) **сломала бы рабочий экран**: `RescheduleModal`
  строит сетку `generateTimeGrid()` 08:00–21:00 (`RescheduleModal.tsx:18-25, 48`) и знает только
  занятость (`getOccupied`), но не рабочие часы. Мастер, у которого окно 10:00–19:00, увидел бы
  доступные 08:00 и 21:00 и получал 409 без объяснения. Чинить это пришлось бы переводом модалки
  на `GET /api/bookings/slots` — а это уже переписывание UI переноса, которого нет ни в одной истории.

Итог: `Reschedule` получает **только** шаг 12 (не в прошлом, не переполняет сутки) плюс существующий
конфликт-чек, оба внутри уже существующей транзакции с локом (`:312-322`). Тест `BK-041` из §4.1 п. 9
SPEC переформулируется с «перенос в нерабочее время → 409» на «перенос в прошедшую дату → 409».

### 3.5 Валидация DTO (подмножество C2)

`ServiceBooking.API/DTOs/Services/ServiceDto.cs:13-20` — `CreateServiceDto`:
`[Range(1, 1440)] int DurationMinutes`, `[Range(0, 1_000_000)] decimal Price`, `[MaxLength(200)] string Name`,
`[MaxLength(2000)] string? Description`.
`ServiceBooking.API/DTOs/Bookings/BookingDto.cs:30-42` — `CreateBookingDto`:
`[MaxLength(2000)] Notes`, `[MaxLength(200)] GuestName`, `[MaxLength(32)] GuestPhone`,
`[MaxLength(256)] GuestEmail`.

Атрибуты на позиционных параметрах `record` пишутся как `[property: Range(...)]` — иначе они
попадают на параметр конструктора и `[ApiController]` их не увидит. Это единственная неочевидная
деталь задачи; она проверяется тестом `SVC-015` (`durationMinutes = 0` → 400), а не глазами.

Гипотеза E1 аудита (при `DurationMinutes = 0` конфликт-чек вырождается и допускает неограниченное
число записей на одно время) **воспроизводится отдельным кейсом до правки** и остаётся в наборе
после — это единственный способ доказать, что правка закрыла именно её.

---

## 4. Расписание: права, скоуп компании, устойчивость (US-04)

### 4.1 Починка предиката — первый шаг истории

`WorkingHoursController.CanManage` (`:98-106`) и `ScheduleTemplateController.CanManage` (`:122-130`) —
две дословные копии. Обе правятся одинаково:

```csharp
private async Task<bool> CanManage(string masterId, Guid companyId, string requesterId)
{
    if (User.IsInRole("SuperAdmin")) return true;
    // Being the master is not enough on its own: without the membership check any authenticated user
    // could pass their own id with an arbitrary companyId and write themselves working hours inside a
    // company they have nothing to do with (audit A5). It also revokes access as soon as a master is
    // removed from the company.
    if (requesterId == masterId) return await CompanyMembership.IsStaffAsync(db, companyId, requesterId);
    return await CompanyMembership.IsOwnerAsync(db, companyId, requesterId);
}
```

Что это меняет:
- `PUT`/`DELETE /api/workinghours`, `PUT /api/schedule-template`, `POST /api/schedule-template/apply`
  с чужим `companyId` → `403` (было `200`/`204`);
- уволенный мастер (`RemoveMember` не трогает `WorkingHours` — §8.1 SPEC) теряет доступ к своему
  расписанию в этой компании (критерий US-04 п. 3);
- `GET /api/workinghours` получает **тот же** предикат в начале метода (`:25`) — то, что ревизия 1
  описывала как «переиспользовать существующий предикат», теперь безопасно.

Предыдущая редакция этого документа (§2.4) утверждала, что `CanManage` не меняется, а
`API_CONTRACT.md` §1.2 описывал сквозной проход мастера как намеренное свойство. **Оба утверждения
отменены**; в новом `API_CONTRACT.md` соответствующий абзац отсутствует, а в сводной таблице
ломающих изменений появились строки на `PUT`/`DELETE`/`apply`.

### 4.2 Атомарность `Upsert` и уникальный индекс (B3)

`Upsert` (`WorkingHoursController.cs:41-80`) — check-then-act без транзакции, при том что конвенция
§6 `CURRENT_STATE` этого требует и в `Bookings.Create` сделано правильно. Правка по образцу
`BookingsController.cs:125-126`:

```csharp
await using var transaction = await db.Database.BeginTransactionAsync();
await AdvisoryLock.AcquireAsync(db, $"working-hours:{dto.MasterId}:{dto.CompanyId}:{dto.Date:O}");
// ... существующий find-or-create ...
await db.SaveChangesAsync();
await transaction.CommitAsync();
```

Ключ лока новый и добавляется в перечень ключей в `CURRENT_STATE`-конвенции при следующем снимке
(сам `CURRENT_STATE.md` в цикле не редактируется — DoD §10.1 п. 10).

Та же транзакция — вокруг `ScheduleTemplateController.Put` (delete-then-insert, `:45-61`) с ключом
`schedule-template:{masterId}:{companyId}` и вокруг `Apply` (`:77-118`) с ключом
`working-hours:{masterId}:{companyId}` (лок на пару, а не на дату: `Apply` пишет диапазон).

Плюс **уникальный индекс** в `AppDbContext` (`ServiceBooking.Infrastructure/Data/AppDbContext.cs:53-57`):

```csharp
builder.Entity<WorkingHours>(e =>
{
    e.HasOne(wh => wh.Master).WithMany(u => u.WorkingHours).HasForeignKey(wh => wh.MasterId);
    e.HasOne(wh => wh.Company).WithMany().HasForeignKey(wh => wh.CompanyId);
    e.HasIndex(wh => new { wh.MasterId, wh.CompanyId, wh.Date }).IsUnique();   // ← НОВОЕ
});
```

Индекс — это то, что делает правило неломаемым независимо от кода: без него
`ScheduleTemplateController.Apply:88` (`existing.ToDictionary(wh => wh.Date)`) навсегда падает
`ArgumentException` → 500 на каждую попытку применить шаблон.

### 4.3 Потолок диапазона `Apply` (B4)

`Apply` (`ScheduleTemplateController.cs:66-120`) в начале метода, **после** `CanManage`:
```csharp
if (to < from) return BadRequest("Invalid date range: 'to' must not be earlier than 'from'.");
if (to.DayNumber - from.DayNumber > 366)
    return BadRequest("Date range is too large: at most 366 days can be applied at once.");
```
366, а не 365 — год с високосным февралём применяется одним вызовом, что и есть штатный сценарий.

### 4.4 Скоуп компании в `SlotService` (A4)

`SlotService.GetAvailableSlotsAsync` получает параметр `companyId` и добавляет его в предикат
(`SlotService.cs:18-21`). Это тянет обязательный `companyId` в `GET /api/bookings/slots`, что нужно
и US-13 п. 6 (без него негде проверить членство для `manual=true`) — параметр вводится **один раз**
и описан в `API_CONTRACT.md` §1.

**Решение по совместимости (ответ на §12.3 SPEC): параметр вводится сразу обязательным, BE и FE
едут в одном PR.** Опциональный параметр с fallback означал бы, что на один релиз в проде живёт
код, который при отсутствии `companyId` не может проверить членство для `manual=true` — то есть
дыра A1 остаётся открытой на весь релиз. Это неприемлемо для цикла, чья цель — закрыть A1.
Риск R14 закрывается тем, что репозиторий и деплой одни: `deploy/deploy-remote.sh` выкладывает
API и SPA вместе, «разъехаться» версиям физически негде.

---

## 5. Изоляция данных компании, деньги и отчёты

### 5.1 Комиссия переезжает на членство (US-15, B1)

`CompanyMember` (`ServiceBooking.Core/Entities/CompanyMember.cs`) получает
`public decimal CommissionPercent { get; set; } = 0;`.

Места чтения/записи, которые правятся:

| Файл:строка | Было | Стало |
|---|---|---|
| `CompaniesController.cs:378` | `member.User.CommissionPercent = Math.Clamp(...)` | `member.CommissionPercent = Math.Clamp(...)` |
| `CompaniesController.cs:381` | `Ok(new { member.UserId, member.User.CommissionPercent })` | `Ok(new { member.UserId, member.CommissionPercent })` |
| `CompaniesController.cs:121` (`GetMembers`) | `cm.User.CommissionPercent` | `cm.CommissionPercent` |
| `CompaniesController.cs:366` (`AddMember`) | `user.CommissionPercent` | `member.CommissionPercent` (всегда 0 у нового участника — как и сегодня по смыслу) |
| `ReportsController.cs:44-52` | группировка по `b.Master`, комиссия из `master.CommissionPercent` | комиссия из `CompanyMember` **этой** компании |

`ReportsController` после правки: перед группировкой одним запросом подтягивается словарь комиссий
компании, за которую строится отчёт —
`await db.CompanyMembers.Where(cm => cm.CompanyId == companyId).ToDictionaryAsync(cm => cm.UserId, cm => cm.CommissionPercent)`,
и `master.CommissionPercent` заменяется на `commissions.GetValueOrDefault(master.Id)`. Один
дополнительный запрос на отчёт, N+1 не появляется.

`AppUser.CommissionPercent` (`ServiceBooking.Core/Entities/AppUser.cs:10`) **не удаляется** в цикле A
и остаётся источником для миграции; удаление — цикл B, отдельной миграцией.

**Побочный эффект, которого нет в SPEC (см. §14.2).** Поле читают ещё два места, и после правки они
начнут показывать 0 навсегда:
- `ProfileController.cs:103` → `ProfileDto.CommissionPercent` → `frontend/src/pages/ProfilePage.tsx:174`
  («мастер видит свою комиссию»);
- `AdminController.cs:68` → `AdminUserDto.CommissionPercent` (в UI админки не отображается — проверено).

Решение: **поля DTO не удаляем** (правило «DTO не меняются»), но **убираем показ** комиссии в
`ProfilePage.tsx` — задача T-F4. Оставить в интерфейсе число, которое навсегда стало нулём, значит
завести ровно тот дефект «систематически врёт пользователю», который цикл лечит в US-18. Оба поля
помечаются в `API_DOCUMENTATION.md` как legacy и удаляются в цикле B вместе с `AppUser.CommissionPercent`.

### 5.2 Услуги участника (US-16, B2)

`CompaniesController.UpdateMemberServices` (`:127-153`) — перед любыми изменениями:

```csharp
var requested = serviceIds.Distinct().ToList();
var validCount = await db.Services.CountAsync(s => s.CompanyId == id && requested.Contains(s.Id));
if (validCount != requested.Count)
    return BadRequest("One or more services do not belong to this company.");
```
Проверка стоит **до** `RemoveRange` (`:146`), поэтому правка атомарна по критерию US-16 п. 1:
при ошибке ни одна связка не создана и не удалена. Несуществующий `Guid` не попадает в `validCount`
и даёт то же 400 вместо 500 от FK.

### 5.3 Отчёты компании (US-18, B5, решение Q11)

`CompaniesController.GetStats` (`:398-477`):
1. сигнатура — `[FromQuery] DateTime? from, [FromQuery] DateTime? to`; при `null` →
   `BadRequest("Both 'from' and 'to' are required.")`; при `to < from` →
   `BadRequest("Invalid date range: 'to' must not be earlier than 'from'.")`;
2. `var fromDate = DateOnly.FromDateTime(from.Value); var toDate = DateOnly.FromDateTime(to.Value);`
3. фильтр `:410` — `b.CreatedAt >= from && b.CreatedAt <= to` → `b.Date >= fromDate && b.Date <= toDate`;
4. `dailyRevenue` (`:456-464`) группируется по `b.Date` — **как есть, не трогаем**;
5. `newClientsCount` (`:419-425`): `FirstDate = g.Min(b => b.CreatedAt)` → `g.Min(b => b.Date)`,
   сравнение с `fromDate`/`toDate`. Это не написано в SPEC явно (§14.3), но обязательно: иначе
   внутри одного ответа остаётся вторая семантика даты — ровно та болезнь, которую история лечит;
6. `DateTime.SpecifyKind(...)` (`:404-405`) удаляется вместе с фильтром по `CreatedAt` — он был нужен
   только Npgsql для сравнения `timestamptz`.

`ReportsController.cs:40` уже фильтрует по `Date` и не меняется — после правки два отчёта в одном UI
дают одинаковые цифры.

### 5.4 Правило «контакты 24 часа» снимается (US-22, C6, решение Q10)

`MastersController.GetClients`: удаляются `cutoff` (`:26`), обе строки `showContact` (`:50`, `:77`)
и четыре тернарных подстановки (`:58-59`, `:84-85`) — `phone` и `email` отдаются мастеру всегда.
На фронте — подпись «Доступен 24 ч после визита» в `frontend/src/pages/MasterClientsPage.tsx:78`
вместе с обслуживающей её разметкой. Никакого нового ограничения взамен не вводится.

### 5.5 Миграции EF Core (ответ на §12.4 SPEC)

Цикл вводит **три** изменения схемы. Все применяются автоматически при старте
(`await db.Database.MigrateAsync()` в `Program.cs`) — отдельного ручного шага нет.
Порядок обязателен и обеспечивается порядком мёржа задач:

| # | Миграция | Задача | Содержание `Up` | `Down` |
|---|---|---|---|---|
| 1 | `AddCompanyMemberCommission` | T-B10 | `AddColumn CompanyMembers.CommissionPercent decimal(18,2) NOT NULL DEFAULT 0` + `Sql("UPDATE \"CompanyMembers\" cm SET \"CommissionPercent\" = u.\"CommissionPercent\" FROM \"AspNetUsers\" u WHERE u.\"Id\" = cm.\"UserId\";")` | `DropColumn` |
| 2 | `DeduplicateWorkingHours` | T-B4 | `Sql("DELETE FROM \"WorkingHours\" wh USING \"WorkingHours\" dup WHERE wh.\"MasterId\" = dup.\"MasterId\" AND wh.\"CompanyId\" = dup.\"CompanyId\" AND wh.\"Date\" = dup.\"Date\" AND wh.\"Id\" < dup.\"Id\";")` | пустой (`Down` ничего не восстанавливает — так и пишется в комментарии) |
| 3 | `AddWorkingHoursUniqueIndex` | T-B4 | `CreateIndex(..., unique: true)` | `DropIndex` |

Свойства:
- **№ 1 переносит данные внутри миграции** — не отдельным скриптом. Совместители получают одно и то же
  значение во всех членствах; ручного разбора с владельцами нет (решение Q12 и §7 SPEC, преамбула).
  Это снимает риск R12 в его исходной формулировке;
- **№ 2 и № 3 едут одной задачей и в этом порядке**, поэтому индекс не может упасть на дубликатах.
  Риск R13 закрывается кодом, а не регламентом; ручного прогона D1 перед деплоем не требуется;
- `Down` у каждой миграции присутствует и проверяется командой `dotnet ef migrations remove` на
  чистой dev-базе (DoD п. 6). У № 2 `Down` пустой намеренно — удалённые дубликаты не восстанавливаются,
  и это записано комментарием в теле миграции;
- диагностические запросы D1–D10 (§7 SPEC) остаются **справочными**: их прогон на dev-стенде полезен
  для написания регрессионных кейсов на реальных данных, но ни одну задачу не блокирует.

---

## 6. Глобальный обработчик исключений (US-05, риск R3)

### 6.1 Жёсткая граница (без изменений, распространяется на все новые коды)

| Код | Кто отдаёт | Формат сейчас | Формат после цикла |
|---|---|---|---|
| 400 | `BadRequest("...")` | `text/plain`, голая строка | **без изменений** (включая новые 400 из US-13/US-16/US-18) |
| 400 | `BadRequest(createResult.Errors.Select(e => e.Description))` (`CompaniesController.cs:341`) | JSON-массив строк | **без изменений** (`memberError.ts:23-27` его разбирает) |
| 400 | автоматическая валидация `[ApiController]` (новые `[Range]`/`[MaxLength]`) | `application/problem+json`, `ValidationProblemDetails` | **без изменений** (поведение фреймворка) |
| 402 | `StatusCode(402, "...")` | `text/plain` | **без изменений** |
| 403 | `Forbid()` | пустое тело | **без изменений** |
| 404 | `NotFound()` / `NotFound("...")` | пусто / `text/plain` | **без изменений** |
| 409 | `Conflict("...")` | `text/plain` | **без изменений** |
| 500 | ничего (исключение улетает наружу) | пусто / developer page | **`application/problem+json`** |

`getBookingErrorMessage` (`frontend/src/utils/bookingError.ts:13`) читает `response.data` как строку —
и продолжает читать её как строку. **Мапперы `src/utils/*Error.ts` в связи с этой историей
не переписываются.**

### 6.2 Механика (без изменений)

В `ServiceBooking.API/Program.cs`, сразу после `var app = builder.Build();` (`:132`) и **первым**
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

Явные ограничения реализации: `builder.Services.AddProblemDetails()` **не добавляем**,
`UseStatusCodePages*` **не добавляем** (второе — единственное, что реально переписало бы тела
существующих 4xx). `detail`/stack trace нет **никогда**. Регистрация инлайном в `Program.cs`,
каталога `Middleware/` не заводим.

### 6.3 Новый источник честного 500 для теста ADM-035 (ответ на §12.6 SPEC)

Прежний источник — `PUT /api/admin/owners/{id}/subscription` с несуществующим `ownerUserId` — чинится
в US-08 п. 3 (400/404 вместо 500). Новый источник, оставшийся в коде и **не входящий в скоуп цикла**:

**`POST /api/services` от `SuperAdmin` с несуществующим `companyId`.**
`ServicesController.Create` (`:27-48`) не проверяет существование компании; `CanManageCompany` (`:82-92`)
для `SuperAdmin` возвращает `true` сразу (`:86`), и выполнение доходит до `SaveChangesAsync` (`:45`),
где падает FK `Services.CompanyId → Companies`. Это уже зафиксировано как известное ограничение
в `API_CONTRACT.md` §8.3 предыдущей редакции и остаётся вне скоупа US-09 (история — только про роль
`Master`).

Тест **ADM-035 переезжает** из `AdminTests.cs` в `ServicesTests.cs` под номером **SVC-016**
(`Create_BySuperAdmin_UnknownCompany_Returns500ProblemJson`): 500, `content-type: application/problem+json`,
`traceId` непустой. Номер ADM-035 в `TEST_CATALOG.md` освобождается с пометкой «перенесён в SVC-016».

---

## 7. Гигиена деплоя: Swagger и fail-fast (US-10, риск R4) — без изменений

### 7.1 Swagger только в Development

- `builder.Services.AddEndpointsApiExplorer()` + весь блок `AddSwaggerGen(...)` (`Program.cs:18-50`)
  оборачиваются в `if (builder.Environment.IsDevelopment()) { ... }`;
- `app.UseSwagger()` / `app.UseSwaggerUI(...)` (`Program.cs:134-139`) — в `if (app.Environment.IsDevelopment())`;
- в `Testing` Swagger выключается вместе с Production. Безопасно: ни один тест `/swagger` не дёргает;
  перед мёржем задача обязана повторить `grep -rn "swagger" ServiceBooking.Tests/`;
- `deploy/nginx/ezbook.conf:34-41` — блок `location /swagger/` удаляется целиком;
- XML-документация (`GenerateDocumentationFile`) остаётся включённой.

### 7.2 Fail-fast — строго `IsProduction()`

Блок добавляется после `var builder = WebApplication.CreateBuilder(args);` (`:14`) и **до** первого
чтения `Jwt:Key` (`:68`):

| Ключ | Условие падения | Сообщение |
|---|---|---|
| `Jwt:Key` | пусто, длина < 32, или равен `CHANGE_ME_TO_A_LONG_SECRET_KEY_AT_LEAST_32_CHARS` (`appsettings.json:13`) | `Jwt:Key is missing, too short (<32 chars) or still the placeholder. Set Jwt__Key in .env.` |
| `SuperAdmin:Password` | пусто или равен `Admin12345` (`appsettings.json:21`) | `SuperAdmin:Password is missing or still the placeholder. Set SuperAdmin__Password in .env.` |
| `SuperAdmin:Phone` | равен `+70000000000` | предупреждение в лог, **не** падение |

Исключение бросается до `builder.Build()`. `CustomWebApplicationFactory` использует окружение
`Testing` → блок не срабатывает → 230 тестов не задеты.

### 7.3 Найденный при проектировании пробел, без которого fail-fast роняет прод

`docker-compose.prod.yml:25-34` **не пробрасывает `SuperAdmin__Password` в контейнер вообще** — есть
`SuperAdmin__Phone=${SUPERADMIN_PHONE}` (`:31`), пароля нет. Сегодня боевой контейнер поднимается с
`Admin12345` из `appsettings.json`, а после включения fail-fast не поднимется вовсе, сколько бы
переменных ни лежало в `.env`.

Поэтому «один коммит» из решения Q4 включает **пять** файлов:
1. `ServiceBooking.API/Program.cs` — fail-fast + Swagger;
2. `docker-compose.prod.yml` — строка `- SuperAdmin__Password=${SUPERADMIN_PASSWORD}`;
3. `.env.production.example` — `SUPERADMIN_PASSWORD=CHANGE_ME` + предупреждение про `JWT_KEY`;
4. `DEPLOY.md` — шаг «перед деплоем добавить `SUPERADMIN_PASSWORD` и настоящий `JWT_KEY` в `.env` на VPS,
   затем `docker compose -f docker-compose.prod.yml config` для проверки подстановки»;
5. `deploy/nginx/ezbook.conf` — удаление `location /swagger/`.

Проверка на стенде до продового деплоя (DoD п. 11): контейнер без `SUPERADMIN_PASSWORD` не поднимается
и пишет причину; с корректным `.env` — поднимается.

---

## 8. CI на GitHub Actions (US-11, сужен)

Файл: `.github/workflows/ci.yml`. Триггеры: `push` в `master` и `sanitation-cycle`, `pull_request`.
`concurrency: { group: ci-${{ github.ref }}, cancel-in-progress: true }`. `timeout-minutes: 15` на job.

### 8.1 Строка подключения — в переменную окружения (без изменений)

```csharp
// ServiceBooking.Tests/Infrastructure/TestDatabaseFixture.cs
public static readonly string ConnectionString =
    Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_CONNECTION")
    ?? "Host=localhost;Database=servicebooking_test;Username=postgres;Password=";
```
`CustomWebApplicationFactory.cs:21` начинает использовать `TestDatabaseFixture.ConnectionString`
вместо своего литерала (сегодня строка продублирована в двух файлах). Локальный запуск не ломается:
переменная не задана → прежний литерал.

### 8.2 Джобы

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
`dotnet build ServiceBooking.sln --no-restore -c Release` →
`dotnet test ServiceBooking.UnitTests --no-build -c Release` (быстрый, без БД, падает раньше) →
`dotnet test ServiceBooking.Tests --no-build -c Release`.

**`frontend`** (`ubuntu-latest`, `defaults.run.working-directory: frontend`):
`actions/setup-node@v4` (`node-version: 20`, `cache: npm`, `cache-dependency-path: frontend/package-lock.json`)
→ `npm ci` → `npx tsc --noEmit` → `npm run build`.
Шага `npm run test:run` **нет** — раннер уехал в цикл B (§6.7 SPEC).

`-warnaserror` в цикле A **не включаем**: удаление мёртвого кода (US-01) уехало в цикл B, сборка
по-прежнему даёт 2 предупреждения, и CI стал бы красным на первом же PR. Включается в цикле B.

Джобы независимы и идут параллельно; workflow красный, если упал любой шаг (проверяется намеренно
сломанным тестом в черновом PR). Секретов нет: БД эфемерная, к боевой базе CI не ходит.
Ориентир: бэкенд ~4 мин, фронт ~2 мин → укладываемся в 10.

---

## 9. Состояние дерева после цикла A

```
ServiceBooking.sln                     5 проектов (Core, Infrastructure, API, Tests, UnitTests)
ServiceBooking/                        ← остаётся (US-01 в цикле B)
.github/workflows/ci.yml               ← НОВЫЙ
ServiceBooking.API/
├── Program.cs                         + sstamp в OnTokenValidated; + UseExceptionHandler;
│                                        + fail-fast; Swagger под IsDevelopment
├── Controllers/
│   ├── BookingsController.cs          порядок проверок Create; членство персонала; принадлежность
│   │                                  service/master; валидация слота; companyId в GetSlots;
│   │                                  [Authorize]+права в GetOccupied; upcoming; − дубль using
│   ├── WorkingHoursController.cs      + починка CanManage; + CanManage в Get; + транзакция и лок
│   ├── ScheduleTemplateController.cs  + починка CanManage; + транзакция и лок; + потолок диапазона
│   ├── ReviewsController.cs           :29 → if (booking.ClientId != userId) return Forbid();
│   ├── ServicesController.cs          − ветка Master в CanManageCompany
│   ├── CompaniesController.cs         + фильтр ролей в GetMasters; TryParse роли; комиссия из членства;
│   │                                  валидация serviceIds; GetStats по Date + 400
│   ├── ReportsController.cs           комиссия из CompanyMember этой компании
│   ├── MastersController.cs           − cutoff/showContact (правило «24 ч» снято)
│   └── AdminController.cs             + 409 в DeletePlan; + 400/404 в UpdateSubscription
├── Services/
│   ├── SlotService.cs                 + companyId в запросе WorkingHours; вызов калькулятора
│   ├── SlotCalculator.cs              ← НОВЫЙ, чистый (Calculate + IsSlotAllowed)
│   ├── CompanyMembership.cs           ← НОВЫЙ, один EXISTS-предикат членства
│   ├── BookingFilters.cs              ← НОВЫЙ, чистый
│   ├── SubscriptionResolver.cs        + static Resolve(sub, nowUtc)
│   └── TokenService.cs                + claim sstamp
├── DTOs/Services/ServiceDto.cs        + [Range]/[MaxLength] в CreateServiceDto
├── DTOs/Bookings/BookingDto.cs        + [MaxLength] в CreateBookingDto
ServiceBooking.Core/Entities/CompanyMember.cs   + CommissionPercent
ServiceBooking.Infrastructure/
├── Data/AppDbContext.cs               + уникальный индекс (MasterId, CompanyId, Date)
└── Migrations/                        + 3 миграции (§5.5)
ServiceBooking.Tests/                  ~+40 функциональных кейсов; SVC-003/007 инвертированы;
                                       BK-024..BK-026 пересмотрены; ADM-035 → SVC-016
ServiceBooking.UnitTests/              ← НОВЫЙ проект (~35 тестов, < 10 с, без БД)
frontend/
├── src/api/bookings.ts                + companyId в getSlots
├── src/components/booking/*.tsx       три модалки передают companyId
├── src/api/client.ts                  401 от /auth/login и /auth/register не разлогинивает
├── src/components/layout/Navbar.tsx   queryClient.clear() при выходе
├── src/pages/MyBookingsPage.tsx       + onError в мутациях; + название компании в карточке
├── src/pages/owner/ScheduleTab.tsx    + onError
├── src/pages/owner/CompanyManagePage.tsx + onError
├── src/pages/admin/PlansTab.tsx       + onError, + planError.ts
├── src/utils/planError.ts             ← НОВЫЙ маппер
├── src/pages/MasterClientsPage.tsx    − подпись «Доступен 24 ч после визита»
├── src/pages/ProfilePage.tsx          − строка комиссии (следствие US-15)
└── package.json / vite.config.ts / tsconfig.json   ← НЕ ИЗМЕНЯЮТСЯ
docker-compose.prod.yml                + SuperAdmin__Password
.env.production.example                + SUPERADMIN_PASSWORD
deploy/nginx/ezbook.conf               − location /swagger/
DEPLOY.md, README.md, TEST_CATALOG.md, API_DOCUMENTATION.md   обновляются в тех же PR
```

---

## 10. Порядок работ и зависимости

Волна с удалением мёртвого кода из предыдущей редакции **исчезла** (US-01 → цикл B), волна фронтовых
юнит-тестов — тоже. Вместо них появились волна фундамента (§2) и волна историй S1.

```
Волна 0 — фундамент (без него нельзя начинать S1)      ║  Фронт идёт параллельно с волны 0
  T-B1  CompanyMembership + починка CanManage (US-04)  ║   T-F1  US-19: onError / 401 login / cache
  T-B2  проект ServiceBooking.UnitTests                ║
          │                                            ║
Волна 1 — максимально параллельная                     ║
  T-B3  SlotCalculator + companyId в SlotService       ║   T-F2  companyId в getSlots + 3 модалки
  T-B4  US-04: лок, уникальный индекс, потолок Apply   ║          (по контракту, до мёржа T-B3)
  T-B5  US-14 отзывы                                   ║   T-F3  US-20 п.5: companyName в «Мои записи»
  T-B6  US-17 SecurityStamp                            ║   T-F4  ProfilePage: убрать комиссию
  T-B7  US-09 услуги + валидация CreateServiceDto      ║          (по контракту, до мёржа T-B10)
  T-B8  US-12 ролевой фильтр в masters                 ║
  T-B9  US-08 PlanConfig.IsActive + 409/400            ║
  T-B10 US-15 комиссия на членстве (+ миграция 1)      ║
  T-B13 US-22 снятие правила «24 ч»                    ║
          │                                            ║
Волна 2 — общие файлы, строго последовательно           ║
  T-B11 US-16 услуги участника        (CompaniesController, после T-B10)
  T-B12 US-18 отчёты компании         (CompaniesController, после T-B11)
  T-B14 US-13 + US-05 в Create        (BookingsController, после T-B1 и T-B3)
  T-B15 US-07 upcoming + BookingFilters (BookingsController, после T-B14)
  T-B16 US-20 GetOccupied              (BookingsController, после T-B15)
          │                                            ║
Волна 3   └──────────────┬─────────────────────────────╨────────┘
  T-B17 строка подключения тестовой БД в env-переменную
  T-B18 .github/workflows/ci.yml
          │
Волна 4 — последняя, отдельным коммитом, перед выкладкой
  T-B19 Swagger только в Development + fail-fast + .env/DEPLOY/nginx
```

**Жёсткие последовательные связи (всё остальное — параллельно):**

| Связь | Причина |
|---|---|
| T-B1 → T-B4 | обе правят `WorkingHoursController`/`ScheduleTemplateController`; T-B1 меняет предикат, T-B4 — тела методов |
| T-B1 → T-B14, T-B16 | обе используют `CompanyMembership.IsStaffAsync`; без него в `Create` нельзя отличить персонал от клиента |
| T-B2 → T-B3, T-B9, T-B15 | юнит-тестам нужен проект, куда их класть |
| T-B3 → T-B14 | `Create` вызывает `SlotCalculator.IsSlotAllowed`, которого до T-B3 не существует |
| T-B10 → T-B11 → T-B12 | три соседних правки в `CompaniesController.cs`; параллельная работа даст конфликты в одном файле |
| T-B14 → T-B15 → T-B16 | три правки в `BookingsController.cs` (`Create`, `GetClientBookings`, `GetOccupied`) |
| T-B2, T-B17 → T-B18 | CI ссылается на состав solution и на обе команды тестов |
| T-B19 после всего | Q4 требует одного коммита с `.env`/`DEPLOY.md`; он же самый опасный (R4) и едет последним |
| T-B3 ↔ T-F2 | **мёржатся вместе** (§4.4): обязательный `companyId` без синхронного фронта ломает публичную запись |

**Точки пересечения BE↔FE (обе стороны читают `API_CONTRACT.md`, не код друг друга):**

| # | Изменение бэкенда | Что делает фронт |
|---|---|---|
| 1 | `GET /api/bookings/slots` — обязательный `companyId` | **T-F2:** `frontend/src/api/bookings.ts:18-20` получает параметр; `BookingModal.tsx:63,79` передаёт `company.id`; `ManualBookingModal.tsx:88,96` — `selectedCompany!.id`. Мёржится одним PR с T-B3 |
| 2 | `GET /api/bookings/occupied` → 401/403 | Изменений нет: единственный потребитель — `RescheduleModal` в кабинете мастера (`MyBookingsPage.tsx:297`), вызов всегда авторизован |
| 3 | `DELETE /api/admin/plans/{id}` → 409 с количеством | **T-F1** (в составе US-19): новый `src/utils/planError.ts` + показ ошибки в `PlansTab.tsx:97-100, 182-189` |
| 4 | `PUT /api/admin/owners/{id}/subscription` → 400 на неактивный план | **T-F1:** в `SubscriptionModal` (`AdminPage.tsx:51-95`) текущий неактивный план помечается «(неактивен)» и остаётся выбранным |
| 5 | `PUT /api/companies/{id}/members/{memberId}/commission` — значение per-company | **T-F4:** убрать показ комиссии в `ProfilePage.tsx:174`. `CompanyManagePage.tsx:175` не правится — там комиссия и так из `MemberDto` |
| 6 | `POST /api/bookings` — новые 400/403/402 для «клиента с guestName» | Изменений нет: через UI такой запрос не формируется; 400 попадает в `bookingError.ts:32` (`serverMsg`), 403/402 — в существующие ветки |
| 7 | `GET /api/workinghours` и `PUT`/`DELETE` → 403 | Изменений нет: `ScheduleTab.tsx:250,259,271` работает со своей компанией. Закрывается ручным чек-листом (R7/R15) |
| 8 | `POST/PUT/DELETE /api/services` → 403 мастеру | Изменений нет: CRUD услуг живёт только в `CompanyManagePage`, роут защищён `roles={['CompanyOwner','SuperAdmin']}` |
| 9 | `GET /api/bookings/client?status=upcoming` начинает фильтровать | Изменений нет: `ClientBookingsPage.tsx:35` уже шлёт `upcoming` |
| 10 | `GET /api/companies/{id}/stats` → 400 без `from`/`to` | Изменений нет: `DashboardTab.tsx:73-74` всегда передаёт оба |
| 11 | `GET /api/masters/clients` отдаёт `phone`/`email` всегда | **T-F1/T-B13:** убрать подпись «Доступен 24 ч после визита» (`MasterClientsPage.tsx:78`) |
| 12 | Любой защищённый эндпоинт → 401 по старому токену | Изменений нет, но **согласовано** с US-19 п. 2: редирект по 401 остаётся везде, кроме `/auth/login` и `/auth/register` |

---

## 11. Задачи

Формат: **ID · название** — файлы · зависимости · тесты · критерий готовности.
Каждая задача = один PR. `TEST_CATALOG.md` и `API_DOCUMENTATION.md` обновляются **в том же PR**, что и код.

### 11.1 Backend

**T-B1 · `CompanyMembership` + починка предиката `CanManage`** (US-04 ч. 1, находка A5)
- Файлы: новый `ServiceBooking.API/Services/CompanyMembership.cs` (§2.1);
  `ServiceBooking.API/Controllers/WorkingHoursController.cs:98-106` (новое тело `CanManage`) и
  `:25` (вызов `CanManage` в начале `Get`);
  `ServiceBooking.API/Controllers/ScheduleTemplateController.cs:122-130` (то же тело).
- Зависимости: нет. **Делать первой.**
- Тесты: `WH-011` посторонний читает → 403; `WH-012` мастер компании про чужого мастера → 403;
  `WH-013` мастер про себя в своей компании → 200; `WH-014` владелец про своего мастера → 200;
  `WH-015` мастер `PUT` в компанию, где не состоит → 403 и строки в БД нет;
  `WH-016` уволенный мастер правит своё расписание → 403;
  `ST-011` `GET /api/schedule-template` чужой компании → 403; `ST-012` `apply` в чужую компанию → 403.
- Готово, когда: WH-001..WH-010 и существующие `ST-` зелёные; BK-тесты слотов не задеты;
  `API_DOCUMENTATION.md` §4.5 переписан (403 у `GET`, сужение `PUT`/`DELETE`).

**T-B2 · Проект `ServiceBooking.UnitTests`** (US-02)
- Файлы: `ServiceBooking.UnitTests/ServiceBooking.UnitTests.csproj` (§1.1), запись в `ServiceBooking.sln`;
  раздел «Юнит-тесты: как запускать» в `README.md` и `TEST_CATALOG.md`.
- Зависимости: нет.
- Тесты: один заглушечный `Fact` (заменяется реальными в T-B3).
- Готово, когда: `dotnet test ServiceBooking.UnitTests` проходит **при остановленной PostgreSQL** и
  укладывается в 10 с; `dotnet test ServiceBooking.Tests` не изменился.

**T-B3 · `SlotCalculator` + `companyId` в `SlotService`** (US-03, US-04 п. 5, риск R5)
- Файлы: новый `ServiceBooking.API/Services/SlotCalculator.cs`;
  `ServiceBooking.API/Services/SlotService.cs:13-52` (параметр `companyId`, предикат `WorkingHours`,
  вызов калькулятора); `ServiceBooking.API/Controllers/BookingsController.cs:30-43`
  (`GetSlots` получает обязательный `[FromQuery] Guid companyId`);
  новый `ServiceBooking.UnitTests/SlotCalculatorTests.cs`.
- Зависимости: **T-B2**. **Мёржится вместе с T-F2.**
- Тесты: ~15 юнит-кейсов (§2.2, `Calculate` + `IsSlotAllowed`); функциональный `BK-043`
  (мастер в двух компаниях, расписание только в A: слоты для B пусты, для A — сетка).
- Готово, когда: `dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~BookingsFlowSmokeTests"`
  зелёный **до и после** правки (оба прогона приложены к PR); `record TimeSlotResult` не изменился;
  в PR — дифф цикла с пометкой «перенос без изменений».

**T-B4 · US-04 ч. 2: атомарность, уникальный индекс, потолок диапазона** (B3, B4)
- Файлы: `WorkingHoursController.cs:41-80` (транзакция + лок);
  `ScheduleTemplateController.cs:45-61` и `:66-120` (транзакция + лок, валидация `from`/`to`);
  `ServiceBooking.Infrastructure/Data/AppDbContext.cs:53-57` (уникальный индекс);
  две миграции — `DeduplicateWorkingHours`, `AddWorkingHoursUniqueIndex` (§5.5, именно в этом порядке).
- Зависимости: **T-B1**.
- Тесты: `WH-017` два параллельных `Upsert` одного дня → одна строка (проверка прямым запросом
  через scope); `ST-013` `apply` с диапазоном 1000 дней → 400; `ST-014` `apply` с `to < from` → 400.
- Готово, когда: миграции применяются на базе с искусственно созданным дубликатом; `Down` проверен
  `dotnet ef migrations remove` на чистой базе; существующие `WH-`/`ST-` зелёные.

**T-B5 · US-14: отзыв только от владельца записи** (A6)
- Файлы: `ServiceBooking.API/Controllers/ReviewsController.cs:29` — условие становится
  `if (booking.ClientId != userId) return Forbid();`, рядом комментарий «почему» на английском
  (guest bookings carry no client identity, so nobody can prove they were the one who visited).
- Зависимости: нет.
- Тесты: `RV-006` посторонний к завершённой **гостевой** записи → 403, отзыв не создан;
  `RV-007` владелец записи → 201 (регрессия); `RV-008` посторонний к чужой клиентской → 403.
- Готово, когда: существующие `RV-` зелёные; `GET /api/reviews/can-review` не тронут;
  `API_DOCUMENTATION.md` (раздел отзывов) отражает, что гостевые записи отзыв не принимают.

**T-B6 · US-17: отзыв токенов по `SecurityStamp`** (A7)
- Файлы: `ServiceBooking.API/Services/TokenService.cs:16-24` (claim `sstamp`);
  `ServiceBooking.API/Program.cs:95-113` (сверка в `OnTokenValidated`, §2.6).
- Зависимости: нет.
- Тесты: `AU-020` токен, выданный до `POST /api/profile/change-password`, → 401 на защищённом
  эндпоинте; `AU-021` новый токен → 200; `AU-022` то же после `change-phone`.
- Готово, когда: все 230 существующих тестов зелёные (они логинятся штатно); в PR отмечено, что
  дополнительного запроса к БД не добавлено (`user` уже загружен для перечитки ролей).

**T-B7 · US-09: услуги меняет только владелец + валидация `CreateServiceDto`** (Q1, US-13 п. 7)
- Файлы: `ServiceBooking.API/Controllers/ServicesController.cs:88-91` — убрать
  `|| cm.Role == UserRole.Master`; `ServiceBooking.API/DTOs/Services/ServiceDto.cs:13-20` — атрибуты
  через `[property: …]` (§3.5).
- Зависимости: нет.
- Тесты — **инвертируются существующие в этом же PR**: `ServicesTests.cs:49-61` `SVC-003` →
  `Create_ByMaster_ReturnsForbidden`; `:105-118` `SVC-007` → `Update_ByMaster_ReturnsForbidden`
  + проверка через `GET /api/services?companyId=`, что имя и цена не изменились;
  новые `SVC-013` (delete мастером → 403, услуга осталась в публичном списке),
  `SVC-014` (мастер читает услуги → 200, сценарий `ManualBookingModal`),
  `SVC-015` (`durationMinutes = 0` → 400) и **`SVC-016`** (`SuperAdmin` создаёт услугу в
  несуществующей компании → 500 `application/problem+json`, §6.3 — добавляется после мёржа T-B14).
- Готово, когда: SVC-002/006/010 зелёные; из `API_DOCUMENTATION.md` убрано упоминание мастера как
  редактора услуг; воспроизведён и закрыт кейс E1 (`durationMinutes = 0` → несколько записей на одно время).

**T-B8 · US-12: ролевой фильтр в публичном списке мастеров** (Q6)
- Файлы: `CompaniesController.cs:78-80` — в `memberQuery` добавить
  `&& (cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner)` **до** материализации (`:94`).
- Зависимости: нет (правит другую часть `CompaniesController`, чем T-B10..T-B12; мёржить по очереди).
- Тесты: `CO-067` участник с ролью `Client` (создаётся напрямую через scope) в списке отсутствует,
  `Master`/`CompanyOwner` присутствуют.
- Готово, когда: CO-012/013/014 зелёные.

**T-B9 · US-08: `PlanConfig.IsActive` + запрет удаления тарифа с подписчиками** (Q2)
- Файлы: `ServiceBooking.API/Services/SubscriptionResolver.cs` (новый `static Resolve`, замена
  строк 86-89); `AdminController.cs:321-329` (`DeletePlan` → 409 с количеством) и `:138-179`
  (`UpdateSubscription` → 400 на неактивный план, 404 на несуществующие `ownerUserId`/`planConfigId`,
  **до** `SaveChangesAsync`); новый `ServiceBooking.UnitTests/SubscriptionResolverRulesTests.cs`.
- Зависимости: **T-B2**.
- Тесты: 7 юнит-кейсов (§2.4); `ADM-032` (409 + количество), `ADM-033` (400 на неактивный план),
  `ADM-034` (владелец на неактивном плане → Free), `ADM-036` (404 на несуществующий `ownerUserId`).
- Готово, когда: 31 существующий `ADM-` тест зелёный; в PR отмечено, что ADM-035 (500) переезжает
  в SVC-016 (§6.3).

**T-B10 · US-15: комиссия принадлежит членству** (B1)
- Файлы: `ServiceBooking.Core/Entities/CompanyMember.cs` (+`CommissionPercent`);
  `CompaniesController.cs:121, 366, 378, 381`; `ReportsController.cs:35-55`;
  миграция `AddCompanyMemberCommission` с переносом данных (§5.5).
- Зависимости: нет; **блокирует T-B11**.
- Тесты: `CO-068` владелец A ставит комиссию совместителю → в `GET /api/companies/{B}/members`
  значение не изменилось; `RP-006` отчёт компании B считается по её собственной комиссии;
  **пересматриваются** `CompaniesTests.cs:519-524` (проверка комиссии через `GET /api/profile`
  удаляется — значение туда больше не попадает, §5.1) и `ProfileTests.cs:66-79` (assert меняется
  на «поле не изменилось профилем», без привязки к 30).
- Готово, когда: `RP-`/`CO-` зелёные; в PR перечислены оба теста, изменившие смысл;
  `API_DOCUMENTATION.md` §3.6 и разделы `members`/`profile` отражают per-company семантику
  и помечают `ProfileDto.commissionPercent`/`AdminUserDto.commissionPercent` как legacy.

**T-B11 · US-16: услуги участника только своей компании** (B2)
- Файлы: `CompaniesController.cs:127-153` (проверка до `RemoveRange`, §5.2).
- Зависимости: **T-B10** (общий файл).
- Тесты: `CO-069` услуга чужой компании → 400, `MasterServices` не изменились;
  `CO-070` несуществующий `Guid` → 400, не 500.
- Готово, когда: существующие тесты привязки услуг зелёные.

**T-B12 · US-18: отчёты компании по дате визита** (B5, Q11)
- Файлы: `CompaniesController.cs:398-477` (§5.3).
- Зависимости: **T-B11** (общий файл).
- Тесты: `CO-071` запись, созданная вне периода визита, попадает ровно в один период и совпадает
  с `GET /api/reports/masters` за тот же период; `CO-072` вызов без `from`/`to` → 400;
  `CO-073` `to < from` → 400.
- Готово, когда: существующие тесты `GetStats` пересмотрены и перечислены в PR; в `README`/`CHANGELOG`
  отмечено, что цифры отчёта могли измениться и это ожидаемо.

**T-B13 · US-22: снять правило «контакты 24 часа»** (C6, Q10)
- Файлы: `MastersController.cs:26, 50, 58-59, 77, 84-85`.
- Зависимости: нет.
- Тесты: существующие `MS-` зелёные; если какой-то закреплял скрытие контакта — удаляется вместе
  с правилом и это отмечается в `TEST_CATALOG.md`.
- Готово, когда: `phone`/`email` приходят мастеру всегда; фронтовая часть (подпись) закрыта T-F1.

**T-B14 · US-13 + US-05: целостность создания записи** (A1, A2, A3, риск R10 — главная задача цикла)
- Файлы: `BookingsController.cs:45-149` (весь `Create`, §3.1–3.3), `:296-332` (`Reschedule`, §3.4),
  `:10` (дубль `using`); `DTOs/Bookings/BookingDto.cs:30-42` (`[MaxLength]`);
  `CompaniesController.cs:347` (`Enum.TryParse<UserRole>` → 400, находка D3);
  `Program.cs` после `:132` (`UseExceptionHandler`, §6.2).
- Зависимости: **T-B1**, **T-B3**. Блокирует T-B15.
- Тесты: `BK-032` клиент с `guestName` в компанию с `AllowSelfBooking=false` → 403;
  `BK-033` то же в компанию на Free → 402; `BK-034` то же при включённой капче без токена → 400;
  `BK-035` `serviceId` чужой компании → 400, запись не создана; `BK-036` `masterId` не член компании → 400;
  `BK-037` запись в нерабочий день → 409; `BK-038` внутрь перерыва → 409; `BK-039` в прошедшую дату → 409;
  `BK-040` `startTime` 10:07 → 409; `BK-041` **перенос в прошедшую дату** → 409 (переформулирован, §3.4);
  `BK-042` `slots?manual=true` от постороннего аутентифицированного → расписание соблюдается;
  `BK-027` авторизованный `POST` с несуществующим `companyId` → 404; `BK-028` то же у владельца
  без тарифа → 404, **не** 402; `CO-066` `SuperAdmin` добавляет участника с ролью `"Bogus"` → 400.
- **Пересматриваются по смыслу:** `BK-024..BK-026` (ручная запись) — ожидания приводятся к решению Q7:
  персонал ставит запись на любое незанятое время, но участник компании обязателен. Каждый изменённый
  тест перечисляется в PR отдельным пунктом.
- Готово, когда: BK-001..BK-026 зелёные, гостевой поток BK-004..BK-009 не задет; в PR явно написано,
  что тела 400/402/403/404/409 не изменились и мапперы `src/utils/*Error.ts` не правились (R3);
  ручной прогон гостевой записи и ручной записи персонала выполнен и описан.

**T-B15 · US-07: `status=upcoming` и 400 на неизвестный статус**
- Файлы: новый `ServiceBooking.API/Services/BookingFilters.cs`;
  `BookingsController.cs:197-218`; новый `ServiceBooking.UnitTests/BookingFiltersTests.cs`.
- Зависимости: **T-B2**, **T-B14** (общий файл).
- Тесты: ~12 юнит-кейсов (§2.5); `BK-029` (`upcoming` возвращает только будущие `Confirmed`),
  `BK-030` (`Completed`/`Cancelled` как раньше), `BK-031` (`status=garbage` → 400).
- Готово, когда: `GET /api/bookings/client` без `status` возвращает всё, как раньше.

**T-B16 · US-20: доступ к занятости мастера** (E3 аудита, Q9)
- Файлы: `BookingsController.cs:18-28` — `[Authorize]` + проверка «сам мастер, либо персонал компании,
  где состоит этот мастер»; комментарий «почему кросс-компанийно» из §2.3.
  Реализация проверки — один запрос:
  `db.CompanyMembers.AnyAsync(cm => cm.UserId == userId && (cm.Role == Master || cm.Role == CompanyOwner)
   && db.CompanyMembers.Any(m => m.UserId == masterId && m.CompanyId == cm.CompanyId))`.
- Зависимости: **T-B15** (общий файл).
- Тесты: `BK-044` аноним → 401; `BK-045` посторонний залогиненный → 403;
  `BK-046` сам мастер → 200 с интервалами из двух разных компаний в одном ответе;
  `BK-047` владелец компании, где мастер состоит, → 200.
- Готово, когда: `RescheduleModal` работает без правок (ручная проверка); состав `OccupiedRangeDto`
  не изменился.

**T-B17 · Строка подключения тестовой БД — в переменную окружения**
- Файлы: `ServiceBooking.Tests/Infrastructure/TestDatabaseFixture.cs:13-14`;
  `ServiceBooking.Tests/Infrastructure/CustomWebApplicationFactory.cs:21`.
- Зависимости: нет.
- Готово, когда: без переменной результат тот же, что до правки; с переменной — подключается к указанной базе.

**T-B18 · CI на GitHub Actions** (US-11)
- Файлы: новый `.github/workflows/ci.yml`; раздел «CI» в `README.md`.
- Зависимости: **T-B2, T-B17**.
- Тесты: намеренно сломанный тест в черновом PR — workflow красный, затем откат.
- Готово, когда: workflow зелёный на `sanitation-cycle`; полный прогон ≤ 10 мин; `-warnaserror` **не** добавлен.

**T-B19 · US-10: Swagger только в Development + fail-fast** (Q4, R4) — **один коммит, последним**
- Файлы: пять из §7.3.
- Зависимости: **все backend-задачи**.
- Тесты: новых автотестов нет (Production-поведение через `WebApplicationFactory` не воспроизводится
  без изменения окружения тестов — прямой риск для 230 кейсов). Проверка ручная, на стенде.
- Готово, когда: `dotnet test ServiceBooking.Tests` без изменений; `grep -rn "swagger" ServiceBooking.Tests/`
  пуст; ревьюер подтвердил, что все пять файлов в одном коммите.

### 11.2 Frontend

**T-F1 · US-19: молчаливые отказы и гигиена сессии** (C3 + C4 + C5) + подписи, снятые бэкендом
- Файлы: `src/pages/MyBookingsPage.tsx:228-243`, `src/pages/owner/ScheduleTab.tsx:47-63`,
  `src/pages/owner/CompanyManagePage.tsx:53-59, 187-192, 322`, `src/pages/admin/PlansTab.tsx:78-97, 182-189`,
  `src/pages/ClientBookingsPage.tsx:49` — `onError` + текст через мапперы;
  новый `src/utils/planError.ts` (409 с числом → «На этом тарифе есть активные подписчики (N)…»,
  409 без числа, 404, default) — по конвенции `src/utils/*Error.ts`;
  `src/pages/AdminPage.tsx:51-95` — пометка «(неактивен)» у текущего плана;
  `src/api/client.ts:17-21` — 401 от `/auth/login` и `/auth/register` не разлогинивает;
  `src/components/layout/Navbar.tsx:12` — `queryClient.clear()` при выходе;
  `src/pages/MasterClientsPage.tsx:78` — удалить подпись «Доступен 24 ч после визита».
- Зависимости: нет (кодируется по `API_CONTRACT.md`, до мёржа T-B9/T-B13).
- Тесты: автотестов нет (раннер — цикл B, риск R11). Ручной чек-лист: пять сценариев DoD п. 8.
- Готово, когда: `npx tsc --noEmit` чист, `npm run build` проходит; `package.json`, `vite.config.ts`,
  `tsconfig.json` **не изменены** (проверяется пустым диффом).

**T-F2 · `companyId` в запросе слотов**
- Файлы: `src/api/bookings.ts:18-20` — сигнатура
  `getSlots(companyId: string, masterId: string, serviceId: string, date: string, manual = false)`
  и `params: { companyId, masterId, serviceId, date, manual: manual || undefined }`;
  `src/components/booking/BookingModal.tsx:63, 79` (`company.id` уже в пропсах, `:18`);
  `src/components/booking/ManualBookingModal.tsx:88, 96` (`selectedCompany!.id`, ср. `:105`).
  `RescheduleModal` не трогается — он использует `getOccupied`, а не `getSlots`.
- Зависимости: контракт `API_CONTRACT.md` §1; **мёржится вместе с T-B3**.
- Готово, когда: `npx tsc --noEmit` чист; ручной прогон записи гостем, клиентом и персоналом;
  queryKey модалок дополнены `companyId`, чтобы кэш не смешивал компании.

**T-F3 · Название компании в «Моих записях»** (US-20 п. 5)
- Файлы: `src/pages/MyBookingsPage.tsx` — вывести `booking.companyName` в карточке записи.
  Правок API не требуется: `BookingDto.CompanyName` уже есть (`DTOs/Bookings/BookingDto.cs:8`).
- Зависимости: нет.
- Готово, когда: мастер-совместитель видит, к какой компании относится каждая запись; сетка переноса
  названий компаний **не** показывает (критерий US-20 п. 4).

**T-F4 · Убрать показ комиссии в профиле** (следствие US-15, §5.1)
- Файлы: `src/pages/ProfilePage.tsx:174` — блок с `profile?.commissionPercent` удаляется вместе
  с обслуживающей его разметкой. `src/api/profile.ts:23` (поле в TS-типе) **не трогаем** — DTO не меняется.
- Зависимости: контракт `API_CONTRACT.md` §11; логически — после T-B10.
- Готово, когда: мастер не видит в профиле числа, которое перестало обновляться; `tsc` чист.

---

## 12. Риски и как их держит эта архитектура

| # (SPEC) | Риск | Ответ архитектуры |
|---|---|---|
| **R10** | Серверная валидация слота ломает ручную запись персонала | §3.3: для персонала правило Q7 (не в прошлом + нет пересечения), правило расписания к нему не применяется вовсе; §3.4: `Reschedule` — staff-only и потому идёт по тому же правилу, `RescheduleModal` не переписывается. `BK-024..BK-026` пересматриваются осознанно, каждый перечислен в PR T-B14 |
| **R14** | Обязательный `companyId` в слотах ломает публичную запись | §4.4: параметр вводится сразу обязательным, T-B3 и T-F2 мёржатся одним PR; опциональный вариант отвергнут, потому что оставлял A1 открытой на релиз. Ручной прогон гостевой записи — критерий готовности T-F2 |
| **R5** | Вынос логики слотов меняет ядро продукта | §2.2: перенос дословный, `TimeSlotResult` не меняется, BK-001..BK-026 прогоняются до и после в одном PR, дифф цикла показывается ревьюеру; валидация реализована **через** `Calculate`, второго правила не существует физически |
| **R12** | Миграция комиссии даёт неверные значения совместителям | §5.5: перенос идёт внутри миграции, значение копируется во все членства (решение Q12); ручного разбора нет. Остаточный эффект — владельцы совместителей увидят своё старое значение и при необходимости изменят его сами; отмечается в `CHANGELOG` |
| **R13** | Уникальный индекс не применяется из-за дубликатов | §5.5: дедупликация — отдельная миграция **перед** индексом, в той же задаче T-B4. Ручного шага перед деплоем нет |
| **R15** | Сужение `CanManage` отрезает мастера без строки `CompanyMembers` | Такой мастер сегодня существует только как след A5 или увольнения; по решению Q12 данные не сохраняются. Диагностика D2 остаётся справочной; сценарии «мастер о себе» и «владелец о мастере» закреплены WH-013/WH-014 |
| **R16** | Отзывы к гостевым записям перестают приниматься | Решение Q8: функциональности нет и сегодня — `GET /api/reviews/can-review` (`ReviewsController.cs:66-71`) отбирает только `b.ClientId == userId`. Правка не меняет ни одного рабочего сценария; тест RV-007 это фиксирует |
| **R3** | `ProblemDetails` ломает мапперы ошибок | §6.1: жёсткая таблица «код → формат», распространённая на **все новые коды**. `AddProblemDetails()` и `UseStatusCodePages` запрещены. Новые 409 переиспользуют существующий текст; новые 400 не содержат подстрок `captcha`/`name`/`phone` |
| **R4** | Fail-fast роняет боевой контейнер | §7.3: найден пробел в `docker-compose.prod.yml`; один коммит из пяти файлов, последняя волна, ручная проверка на стенде |
| **R2** | Учёт `PlanConfig.IsActive` выключает возможности платящему | T-B9: 409 на удаление плана с подписчиками делает ситуацию недостижимой; D9 остаётся справочной диагностикой |
| **R7** | 403 в `GET /api/workinghours` ломает неизвестный вызов UI | Потребители перепроверены (`ScheduleTab.tsx:250,259,271`, `CabinetPage.tsx:144-146`); WH-013/WH-014 фиксируют оба живых сценария |
| **R11** | Фронтовые правки не покрыты автотестами | Ручной чек-лист DoD п. 8; правки локальные (`onError`, один `if` в перехватчике, один `clear()`, один параметр запроса) |
| **R1** | Запрет мастеру на услуги ломает чей-то сценарий | Удаление одного условия (`ServicesController.cs:91`); откат — один revert; SVC-014 фиксирует, что чтение услуг не сломано |
| **R9** | Скоуп расползается | §11: 19 backend- и 4 frontend-задачи, каждая — один PR с явным критерием готовности. Новые находки в ходе работы **документируются в SPEC цикла B**, а не чинятся на ходу. §14 — единственное место, где архитектура расходится со SPEC, и каждое расхождение обосновано |
| — | Конфликты в общих файлах | Сериализованы явно в §10: `BookingsController.cs` (T-B14→T-B15→T-B16), `CompaniesController.cs` (T-B10→T-B11→T-B12), `WorkingHoursController.cs`/`ScheduleTemplateController.cs` (T-B1→T-B4), `Program.cs` (T-B6→T-B14→T-B19) |
| — | **Новый:** claim `sstamp` делает недействительными все ранее выданные токены | Приемлемо по Q12 (не в продакшене). Зафиксировано в `DEPLOY.md`: после выкладки все пользователи стенда должны войти заново |

---

## 13. Соответствие Definition of Done (§10.2 SPEC)

| DoD | Чем закрывается |
|---|---|
| 1. Q7–Q12 закрыты и реализованы | Q7→T-B14 (§3.3), Q8→T-B5, Q9→T-B16, Q10→T-B13+T-F1, Q11→T-B12, Q12→§5.5 (миграции без ручного этапа) |
| 2. `dotnet build` — 0 ошибок | критерий готовности каждой задачи; 2 предупреждения остаются (US-01 в цикле B) |
| 3. `ServiceBooking.Tests` ≥ 230, 0 упавших | ~40 новых кейсов → ожидаемо ~268; изменённые по смыслу: SVC-003, SVC-007, BK-024..BK-026, `CompaniesTests.cs:519-524`, `ProfileTests.cs:66-79`, ADM-035→SVC-016 — перечислены в PR |
| 4. Юнит-тесты < 10 с при остановленной PostgreSQL | §1.1, T-B2/T-B3/T-B9/T-B15 (~35 тестов) |
| 5. `npm run build` и `tsc --noEmit` | T-F1..T-F4 (конфиги сборки не тронуты) |
| 6. Миграции применяются, `Down` проверен | §5.5, критерии T-B4 и T-B10 |
| 7. Диагностика D1–D10 | §5.5: справочная, ни одну задачу не блокирует (решение Q12) |
| 8. Ручной чек-лист QA | распределён: гостевая и ручная запись — T-B14/T-F2; расписание мастера и владельца — T-B1; совместитель — T-B3; смена пароля — T-B6; вход с неверным паролем и смена пользователя — T-F1; отчёты — T-B12; деактивация тарифа — T-B9/T-F1 |
| 9. `TEST_CATALOG.md`, `API_DOCUMENTATION.md`, `README` | входят в критерий готовности каждой задачи |
| 10. Workflow зелёный / красный на сломанном тесте | T-B18 |
| 11. US-10 последним коммитом | T-B19, волна 4 |

---

## 14. Противоречия и пробелы SPEC, найденные при проектировании

Три позиции, где SPEC либо противоречит сам себе, либо требует нереализуемого, либо умалчивает
о последствии. По каждой принято решение; если заказчик решит иначе — меняется только объём
перечисленных задач.

### 14.1 `BK-041` («перенос в нерабочее время → 409») противоречит решению Q7

**Противоречие.** §4.1 п. 4 SPEC требует валидировать слот против расписания и в `Create`, и в
`Reschedule`, а тест `BK-041` закрепляет «перенос в нерабочее время → 409». Но `Reschedule`
(`BookingsController.cs:296-332`) — **staff-only** эндпоинт: он пускает только через
`CanManageBookingAsync` (`:357-362`), клиент переносить записи не может вообще. По решению Q7
персонал ставит запись на любое незанятое время без ограничений по расписанию. Значит два требования
SPEC описывают одно и то же действие противоположным образом.

**Дополнительно требование нереализуемо без переписывания UI:** `RescheduleModal` строит сетку
`08:00–21:00` (`RescheduleModal.tsx:18-25, 48`) из `GET /api/bookings/occupied` и о рабочих часах не
знает. Полная валидация означала бы, что мастер видит доступные 08:00 и 21:00 и получает 409 без
объяснения; починка — перевод модалки на `GET /api/bookings/slots`, которого нет ни в одной истории
и который тянет за собой выбор компании в переносе.

**Решение (§3.4):** `Reschedule` валидируется по правилу персонала — «не в прошлом» + «не пересекается».
`BK-041` переформулируется на «перенос в прошедшую дату → 409». Если заказчик всё же хочет
валидацию против расписания в переносе, это отдельная история цикла B вместе с переработкой
`RescheduleModal`.

### 14.2 US-15 умалчивает о `ProfileDto` и `AdminUserDto`

**Пробел.** §4.4 SPEC перечисляет четыре места, где комиссия переезжает на членство, но
`AppUser.CommissionPercent` читают ещё два: `ProfileController.cs:103` (поле уходит в
`GET /api/profile` и рисуется в `frontend/src/pages/ProfilePage.tsx:174` — «мастер видит свою
комиссию») и `AdminController.cs:68`. После US-15 оба навсегда показывают 0, потому что писать
в `AppUser.CommissionPercent` больше некому.

Побочно ломаются два существующих теста, которых нет в списке «меняющих смысл» (§10.1 п. 3 SPEC):
`ServiceBooking.Tests/Tests/CompaniesTests.cs:519-524` (проверяет, что назначенная владельцем комиссия
видна мастеру в `GET /api/profile`) и `ProfileTests.cs:66-79` (assert `CommissionPercent == 30`).

**Решение (§5.1):** поля DTO не удаляем (правило «DTO не меняются»), но снимаем **показ** комиссии
в `ProfilePage.tsx` (задача T-F4) и помечаем оба поля как legacy в `API_DOCUMENTATION.md` — они
удаляются в цикле B вместе с `AppUser.CommissionPercent`. Оставить в UI навсегда обнулившееся число
означало бы завести ровно тот дефект «систематически врёт пользователю», который цикл лечит в US-18.
Оба теста перечисляются в PR T-B10 как изменившие смысл.

### 14.3 US-18 не упоминает `newClientsCount`, оставляя внутри одного ответа две семантики даты

**Пробел.** §5.2 SPEC требует перевести фильтр и группировку `GetStats` на `Booking.Date`, но
`newClientsCount` (`CompaniesController.cs:419-425`) считается по `Min(b.CreatedAt)` и сравнивается
с тем же `from`/`to`. Если оставить как есть, один и тот же ответ будет содержать выручку по дате
визита и «новых клиентов» по дате создания записи — то есть история закроет расхождение между двумя
отчётами и одновременно оставит расхождение внутри одного.

**Решение (§5.3 п. 5):** `newClientsCount` переводится на `Min(b.Date)`. Формально это выход за букву
критериев приёмки US-18, поэтому вынесено сюда явно; отменяется одной строкой, если заказчик
считает «новым клиентом» того, кто впервые **записался** в период, а не впервые **пришёл**.

### 14.4 Мелкие уточнения, принятые без изменения объёма работ

- **§4.2 п. 1 SPEC** формулирует предикат как «`requesterId == masterId` **и** существует
  `CompanyMembers(companyId, requesterId)`» — без указания роли. Буквальная реализация позволила бы
  участнику с ролью `Client` править себе рабочие часы. Реализуем через `IsStaffAsync`
  (роли `Master`/`CompanyOwner`) — §4.1.
- **§4.1 п. 3 SPEC** предлагает `400` вместо `404` на некорректную комбинацию company/service/master
  («объекты существуют, некорректна их комбинация») — принято дословно, тексты зафиксированы
  в `API_CONTRACT.md` §2.
- **§4.1 п. 5 SPEC** («смягчение для персонала… проверки "влезает в сутки" действуют и для него»)
  реализовано не отдельной проверкой, а условием `startTime + duration >= 24:00 → 409` в том же
  шаге, что «не в прошлом» — §3.3.
- **§6.6 п. 6 SPEC** требует «не менее 30 юнит-тестов бэкенда»: `SlotCalculator` ~15 +
  `SubscriptionResolver` 7 + `BookingFilters` ~12 = **~34**. Порог достигается без юнитов на права
  (их вынос потребовал бы переписать SQL-предикат на загрузку в память, что SPEC запрещает).

---

## 15. Что архитектура сознательно НЕ делает

- **Не вводит сервис прав.** `CompanyMembership` (§2.1) — статический класс с двумя `EXISTS`-запросами,
  не абстракция; существующие `CanManageCompany` в `Services`/`Companies` в цикле A не трогаются.
  Унификация всех шести копий (§9 P2-11 `CURRENT_STATE`) остаётся отложенной.
- **Не трогает таймзоны** (§9 P1-6): «сейчас» везде `DateTime.UtcNow`, один источник на цикл (§2.5).
- **Не делает шаг сетки слотов настраиваемым** (§9 P2-9) — юнит-тесты лишь фиксируют текущие 30 минут.
- **Не унифицирует формат ошибок API** (R3) — `ProblemDetails` только для 500.
- **Не сужает `GET /api/bookings/occupied` по компании** — решение Q9, обоснование зафиксировано
  комментарием в коде (§2.3), чтобы это не «починили» как утечку.
- **Не чинит 500 на `POST /api/services` от `SuperAdmin` с несуществующей компанией** — вне скоупа
  US-09; используется как стабильная точка проверки глобального обработчика (SVC-016, §6.3).
- **Не заводит механизм отзывов для гостей** — решение Q8, и в цикл B он тоже не выносится.
- **Не удаляет `AppUser.CommissionPercent`, мёртвый код, мёртвые ветки UI, `BookingStatus.Pending`,
  N+1 и пагинацию, security-заголовки, обновление зависимостей фронта** — всё это цикл B (§8.1 SPEC),
  без исключений.
- **Не добавляет ни одной новой библиотеки** — ни NuGet, ни npm.
