# ARCHITECTURE — цикл 19 ServiceBooking: лимиты задаются только тарифом; удаление заготовки геокодера

**Разделы §380–§396.** Ветка цикла — `cycle/019-tariff-limits-geocoder-cleanup` (её подготовил
devops-engineer; ветки, коммиты и мёржи — не зона архитектора). Отправная точка — `develop` = `e3774c1`.

**Вход:** `SPEC_CYCLE19_TARIFF_LIMITS_GEOCODER.md` цикла 19. Раздел «✅ Ответы заказчика (2026-09-28)» **приоритетнее** предположений
и §7 SPEC там, где они расходятся. `CURRENT_STATE.md` §0.4 (А и Б) — факты по коду, сверенные на
входе. Действующее ограничение: **ломающие миграции запрещены с 25.09.2026**, CI проверяет снапшоты
миграций (`Migrations snapshot drift`, `Designer snapshots are monotonic`).

**Выход:** этот документ, `API_CONTRACT_CYCLE19.md` (§400–§416) и машиночитаемая схема
**`contracts/cycle19/openapi.yaml`** — источник истины по форме интерфейса.

⚠️ **Почему документы названы `*_CYCLE19.md`, а корневые `ARCHITECTURE.md`/`API_CONTRACT.md` не
тронуты.** Корневые файлы — это документы **цикла 3**, а не цикла 18: цикл 18 уже лежит в
`ARCHITECTURE_CYCLE18.md`/`API_CONTRACT_CYCLE18.md`, архивировать там нечего. На разделы корневых
файлов ссылается сам код: `grep -rn "ARCHITECTURE.md §\|API_CONTRACT.md §"` по `*.cs|*.ts|*.tsx|*.yml|*.sh`
даёт **250 ссылок в 99 файлах** (`EffectivePlan`, `AdvisoryLock`, пагинация, health, `deploy/*.sh`…).
Перезапись корня увела бы все эти ссылки в чужой документ. Это ловушка §9 **C5**, которую проект
уже проходил пять раз. Конвенция проекта — суффикс `_CYCLE<N>` (§10.5), так делали циклы 4–18.

✅ **Служебная задача SPEC §4 (архив спеки цикла 18) по факту закрыта** коммитом `56eddd8`:
`SPEC_CYCLE18_TRIAL_PLAN.md` лежит в корне, а `ARCHITECTURE_CYCLE18.md`, `API_CONTRACT_CYCLE18.md` и
`LEGAL_REVIEW_CYCLE18.md` ссылаются на него (19 ссылок) и не содержат ни одной ссылки на `SPEC.md`.
Остаток — задача **D0** (§390): одна ссылка «`SPEC.md` §5» в
`ServiceBooking.Tests/Tests/Cycle18TrialPlanTests.cs:181` подразумевает спеку цикла 18. Плюс
побайтная сверка `git show e3774c1:SPEC.md | cmp - SPEC_CYCLE18_TRIAL_PLAN.md`: у меня нет shell,
проверить её я не могу.

---

## §380. Главное в восьми строках

1. **Лимит = поле тарифа + бонус аккаунта.** Сотрудники — `MaxEmployees + GrandfatheredEmployeeBonus`,
   компании — `MaxCompanies`. Формула живёт в **одном** месте (`AccountLimitFormula`), её зовут
   резолвер, проверка превышения при назначении тарифа и разбор 402.
2. **Опция-лимит** — опция с `CapabilityKey ∈ {employees, companies}` (после trim и нижнего регистра).
   Признак считает только сервер, по ключу, а не по `code`. Строки таких опций, их правил, покупок и
   заявок **не удаляются и не переписываются**, их просто никто больше не читает как действующие и
   никто не пишет.
3. **Новой колонки бонуса компаний нет** (ответ заказчика 2). Отсутствие переноса делает миграцию
   независимой от содержимого БД. Безопасность обеспечивает **гейт выката**: если на бою найдётся
   хоть одна незавершённая покупка опции-лимита, выкат останавливается **до любых изменений** на
   машине, с перечнем строк в логе деплоя (§385).
4. **Миграция цикла — одна, только данные, идемпотентная**: `IsActive = false` у опций-лимитов. Ни
   одного `Drop*`, ни одной удалённой строки. Приложение прошлой версии работает поверх новой схемы.
5. **Пять колонок геокодера остаются в БД** и в модели EF — **теневыми свойствами** без CLR-полей.
   Снапшот не меняется, `DropColumn` не генерируется, из кода к колонкам не дотянуться.
6. **Геокодер удаляется целиком**: `Services/Geo/**`, маршрут `lookup`, DI, HTTP-клиент, стартовая
   проверка, конфигурация, фронтовые кнопка/кандидаты/статус, тесты. **Остаются**
   `PUT /api/companies/{id}/address` (`verify` игнорируется), правовой гейт `address/notice` и
   политика лимитов `address-verify` **под прежним именем**.
7. **Фронт не фильтрует опции сам — решает сервер.** Это защищает правила `extra-*` в минуты выката,
   когда новый фронт уже отдаётся, а API ещё старый (§386.2).
8. **Ни одной новой зависимости**, ни одного нового маршрута, два осознанных ломающих изменения
   формы ответа для нашего же фронта (`API_CONTRACT_CYCLE19.md` §416).

---

## §381. Стек: без изменений

Стек не выбирается заново: это расширение работающего продукта (ASP.NET Core 8 + EF Core/Npgsql +
PostgreSQL, React + Vite + TypeScript + TanStack Query, контракт OpenAPI 3.0.3, генерация типов
`openapi-typescript`, линт `@redocly/cli`). Цикл **только удаляет** код и упрощает формулу. Новых
пакетов нет ни в `*.csproj`, ни в `frontend/package.json`. Именованный HTTP-клиент `yandex-geocoder`
удаляется вместе с сервисами. Отдельного NuGet-пакета у геокодера не было.

Протокол — прежний REST/JSON, поэтому машиночитаемая форма контракта — OpenAPI
(`contracts/cycle19/openapi.yaml`) по образцу циклов 13–18.

---

## §382. Ответы на открытые вопросы §7 SPEC и на развилки

| № §7 SPEC | Решение | Где |
|---|---|---|
| 1. Бонус компаний | Колонки **нет** (ответ заказчика 2 отменяет «колонка нужна всё равно»). Миграция ничего не переносит, поэтому от содержимого БД не зависит. Недопустимое состояние (есть незавершённая покупка) ловит гейт выката | §383.1, §385 |
| 2. Судьба строк `extra-*` | Опции — `IsActive = false` (миграция). Правила `PlanOptionRule`, строки `AccountSubscriptionOption`, `RequestedOptionsJson` — **не трогаются никогда**. Полный список поверхностей, где опция не должна всплывать, — §386.1 | §383.2, §386 |
| 3. Идентификация | По `CapabilityKey` с нормализацией (`Trim().ToLowerInvariant()`), единый класс `RetiredLimitOptions`. Запрет новых опций с такими ключами — 400 в `POST/PUT /api/admin/options`, правка выведенной — 409 | §384.1, контракт §403 |
| 4. Упрощение резолвера | 6-аргументная перегрузка `Resolve` удаляется (компилятор находит всех вызывающих), слагаемое Σ покупок уходит. Тесты сложения **переписываются** на «покупки игнорируются». Инвариант «лимит не уменьшился» стоит под тестом гейта (LIM19-020) | §384, §391 |
| 5. Чек-лист C18-1 и §4.28 п. 0 | Формулировка «правило по каждой опции, **кроме опций-лимитов** (их в матрице больше нет)». Правит devops в `DEPLOY.md`, итоговый `CURRENT_STATE.md` — в конце цикла | §394 |
| 6. Контракт | Новый `contracts/cycle19/openapi.yaml`. `contracts/cycle13/openapi.yaml` остаётся **историческим** документом (линтуется, как раньше, помечается комментарием). Генерат `api-cycle13.generated.ts` и скрипт `types:api:cycle13` **удаляются**, фронт берёт `AddressNoticeResultDto` из `api-cycle19.generated.ts`. `API_DOCUMENTATION.md` §4.16 — короткий подраздел «новое в цикле 19» (как делал цикл 17) | §388.5, §394 |
| 7. Пять колонок | Теневые свойства EF в `AppDbContext`, CLR-свойства и перечисление `AddressPrecision` удаляются. Проверка — `DriftProbe` пуст, снапшот не меняется | §383.3 |
| 8. Политика `address-verify` | **Имя не меняется** → боевые переопределения действуют без правки окружения. Меняется только текст 429 | §388.2 |
| 9. `AddressVerifyField` | Переименовывается в `CompanyAddressField`, геокодерная часть вырезается, гейт и сохранение остаются. Тест переименовывается, кейсы сохранения и гейта сохраняются | §388.1, FE-2 |
| 10. Стартовые проверки | `ValidateAddressVerification` и её вызов удаляются. Все места запуска перечислены. Тест ADDR-030 проверяет старт со старыми «роняющими» значениями | §388.4 |
| 11. Боевые данные неизвестны | Ни одно решение не зависит от ответа. Гейт и отчёт работают **через штатный деплой**, без доступа к машине (TD16-4) | §385 |

**Решения, которые SPEC отдаёт architect'у явно:**
- `PUT /api/admin/plans/{id}` с правилом `extra-*` → **молча игнорируется (200)**, сохранённое правило
  не трогается. 4xx сломал бы сохранение тарифа из закэшированной старой вкладки, которая шлёт всю
  матрицу целиком, а у самого правила после цикла нет эффекта.
- Заявка до выката с `extra-*` → при чтении строки помечаются `retired`, сервер отдаёт готовый текст
  `retiredOptionsNotice`, оценка цены их не считает. Окно одобрения строит строки из каталога без
  опций-лимитов, поэтому они не применяются. Суперадмин видит предупреждение до подтверждения. Нет
  500 и нет молчаливой потери (контракт §407–§408).
- Способ «не терять молча» для купленных `extra-*` → **явный отказ при выкате** (§385), а не перенос
  в бонус.
- Отчёт для заказчика (US-19-05) → печатается в лог деплоя тем же шагом (§385.4).
- Разбивка лимита у владельца: «бонус» владельцу отдельной строкой **не называется**, как и сегодня
  (решение цикла 7, §54.4: бонус «вливается» в «включено в тариф»). Предположение SPEC «виден так
  же, как сегодня» выполняется буквально. Суперадмин видит бонус в карточке аккаунта, как и раньше.
  Строки «докуплено» больше нет нигде.

---

## §383. Модель данных

### §383.1 Что не меняется

**Ни одной новой сущности, колонки или индекса.** Колонку `BillingAccount.GrandfatheredCompanyBonus`
не вводим (ответ заказчика 2). `GrandfatheredEmployeeBonus` остаётся как есть, её смысл не
меняется: выдаётся миграциями и суперадмином, владельцу не называется, переживает переход на Free.

Сущности без изменений: `SubscriptionPlanConfig` (`MaxEmployees`/`MaxCompanies` — единственный
источник лимита), `SubscriptionOption`, `PlanOptionRule`, `AccountSubscriptionOption`,
`BillingAccount` (включая `RequestedOptionsJson`), `SubscriptionChangeLog`.

### §383.2 Опции-лимиты в данных

| Таблица | Что с ней делает цикл | Кто пишет после цикла |
|---|---|---|
| `SubscriptionOptions` (строки `extra-*`) | миграция ставит `IsActive = false`. Остальные поля не трогаются | никто: `PUT` → 409, `POST` с таким ключом → 400 |
| `PlanOptionRules` по опциям-лимитам | ничего | никто: `ApplyOptionRulesAsync` пропускает их при записи и не удаляет |
| `AccountSubscriptionOptions` по опциям-лимитам | ничего | никто: `AssignSubscription` их не создаёт, не продлевает и не закрывает |
| `BillingAccounts.RequestedOptionsJson` | ничего (пометка `retired` вычисляется при чтении) | владелец новой заявкой без опций-лимитов (400 на них) |
| `SubscriptionChangeLogs` | ничего, старые сводки с `extra-*` читаются как есть | новые сводки строятся без опций-лимитов |

**Инвариант для ревью:** в коде приложения нет ни одной записи в строки `PlanOptionRule` и
`AccountSubscriptionOption`, чья опция — опция-лимит. Тесты LIM19-005/006/019 это проверяют.

### §383.3 Пять колонок проверки адреса → теневые свойства

Из `ServiceBooking.Core/Entities/Company.cs` удаляются CLR-свойства `AddressVerifiedInputKey`,
`AddressVerifiedAt`, `AddressPrecision`, `AddressLatitude`, `AddressLongitude` вместе с комментарием
цикла 13. Файл `ServiceBooking.Core/Enums/AddressPrecision.cs` удаляется.

В `ServiceBooking.Infrastructure/Data/AppDbContext.cs` (конфигурация `Company`) строка
`e.Property(c => c.AddressVerifiedInputKey).HasMaxLength(300);` заменяется на пять теневых свойств,
**типы — ровно как в снапшоте** (`AppDbContextModelSnapshot.cs` стр. 942–956):

```csharp
// Цикл 19 (ARCHITECTURE_CYCLE19.md §383.3): пять колонок бывшей проверки адреса по карте (цикл 13).
// Код их НЕ читает и НЕ пишет; они держатся в модели теневыми свойствами только для того, чтобы EF
// не сгенерировал DropColumn (ломающие миграции запрещены с 25.09.2026). Физическое удаление —
// отдельным циклом после проверки боевых данных. Не превращать обратно в CLR-свойства.
e.Property<string?>("AddressVerifiedInputKey").HasMaxLength(300);
e.Property<DateTime?>("AddressVerifiedAt");
e.Property<int?>("AddressPrecision");
e.Property<double?>("AddressLatitude");
e.Property<double?>("AddressLongitude");
```

Комментарий пишется по-русски и не содержит латинских токенов из приёмочного грепа US-19-09 (§388.6).

**Почему теневые, а не оставить CLR-поля.** В обоих вариантах снапшот один и тот же: перечисление
хранится как `int?`, а снапшот не различает CLR- и теневые свойства. Но теневые свойства убирают
колонки из поверхности кода: никакой `company.AddressLatitude` больше не скомпилируется, мёртвое
перечисление уходит, и грепу US-19-09 нечего разрешать в `Core/`. Цена такая же, как у варианта с
CLR-полями.

**Точка останова (обязательная).** Сразу после этой правки backend выполняет
`dotnet ef migrations add Cycle19RetireLimitOptions --project ServiceBooking.Infrastructure --startup-project ServiceBooking.API`.
Сгенерированные `Up()`/`Down()` обязаны быть **пустыми**, а `AppDbContextModelSnapshot.cs` — без
изменений по `Company`. Любой `AlterColumn`/`DropColumn` → стоп, маппинг приводится к снапшоту, и
только потом в пустую миграцию вписывается SQL из §383.4. Это же потом проверяют CI-шаги
`Migrations snapshot drift` и `Designer snapshots are monotonic`.

### §383.4 Миграция `Cycle19RetireLimitOptions` (только данные)

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // ARCHITECTURE_CYCLE19.md §383.4. Идемпотентно; ничего не удаляет; строки PlanOptionRules,
    // AccountSubscriptionOptions и RequestedOptionsJson не трогает. Предусловие (нет незавершённых
    // покупок опций-лимитов) проверяет гейт выката §385, а не эта миграция: сорвать её на старте
    // приложения без доступа к машине (TD16-4) нельзя.
    migrationBuilder.Sql("""
        UPDATE "SubscriptionOptions"
        SET "IsActive" = false, "UpdatedAtUtc" = now()
        WHERE lower(btrim("CapabilityKey")) IN ('employees', 'companies')
          AND "IsActive" = true;
        """);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    // Намеренный no-op (тот же довод, что у SeedBillingCatalog): прежнее значение IsActive не
    // сохранено, а опция-лимит, снова ставшая активной, ничего не даёт новому коду и лишь
    // показывается старому.
}
```

**Свойства:** повторный прогон ничего не меняет (`AND "IsActive" = true`). Схема не меняется.
Приложение прошлой версии поверх новой БД работает (читает те же колонки) и лишь перестаёт
показывать `extra-*` на витрине и в выборе владельца, потому что они неактивны. Время — один
`UPDATE` по таблице из единиц строк.

**Нужна ли миграция вообще, если код и так фильтрует по ключу.** Нужна, но как вторая линия. Она
выражает «выведено из оборота» в самих данных для любого читателя БД (отчёт, откат на прошлую
версию). Цена — три строки SQL.

---

## §384. Формула лимита и резолвер

### §384.1 Два новых маленьких класса в `ServiceBooking.API/Services/Billing/`

**`AccountLimitFormula`** — чистая функция без БД:

```csharp
public static class AccountLimitFormula
{
    /// Лимит = поле тарифа + бонус аккаунта (ARCHITECTURE_CYCLE19.md §384). null = без ограничения,
    /// бонус к null не прибавляется. Отрицательный бонус считается нулём.
    public static (int? Employees, int? Companies) Compute(
        int? planMaxEmployees, int? planMaxCompanies, int grandfatheredEmployeeBonus);
}
```

**`RetiredLimitOptions`** — единственное место, где определена «опция-лимит»:

```csharp
public static class RetiredLimitOptions
{
    public static bool IsRetiredCapability(string? capabilityKey);   // Trim + ToLowerInvariant ∈ {employees, companies}
    public static bool IsRetired(SubscriptionOption option);
    // Серверная фильтрация (EF транслирует btrim/lower):
    public static IQueryable<SubscriptionOption> WhereNotRetired(this IQueryable<SubscriptionOption> q);
    public static IQueryable<PlanOptionRule> WhereNotRetired(this IQueryable<PlanOptionRule> q);             // через r.Option
    public static IQueryable<AccountSubscriptionOption> WhereNotRetired(this IQueryable<AccountSubscriptionOption> q); // через o.Option
    // Близнец SQL-гейта §385.2 — «незавершённые строки опций-лимитов» на момент nowUtc:
    public static IQueryable<AccountSubscriptionOption> LiveRetiredRows(AppDbContext db, DateTime nowUtc);
}
```

`CapabilityKeys.Employees`/`Companies` остаются константами, но после цикла их используют **только**
`RetiredLimitOptions` и тесты (грепом проверяется в §395).

### §384.2 `SubscriptionResolver`

- 6-аргументная перегрузка `Resolve(sub, bonus, extraEmployees, extraCompanies, paidNumbers, now)`
  **удаляется**. Остаётся `Resolve(sub, grandfatheredEmployeeBonus, paidNotificationNumbers, nowUtc)`,
  внутри лимиты считаются через `AccountLimitFormula.Compute(basePlan.AccountMaxEmployees,
  basePlan.AccountMaxCompanies, bonus)`. Правило «Free при непригодной подписке/неактивном тарифе» и
  окно рассылок (цикл 18, §333.2) не меняются. `grep -n "IsSystemTrial"` по файлу по-прежнему пуст.
- `GetEffectivePlansForAccountsAsync` загружает из `AccountSubscriptionOptions` **только** строку
  `notifications.whatsapp` (`o.Option.Code == WhatsAppOptionCode`). Суммирование по
  `CapabilityKeys.Employees/Companies` удаляется. Запрос становится легче, а не тяжелее (SPEC §5).
- `IsOptionCurrentlyPaid` остаётся: он нужен для числа оплаченных номеров WhatsApp.
- `EffectivePlan` не меняет форму: `AccountMaxEmployees`/`AccountMaxCompanies` уже содержат итог.

### §384.3 Потребители лимита

| Потребитель | Изменение |
|---|---|
| `CompaniesController` создание компании (402 `CompanyLimitReached`) | кода нет — лимит из резолвера. Текст прежний |
| `CompaniesController` добавление сотрудника (402) | `purchased` удаляется. `BillingTexts.SeatLimitReached(used, planName, limit)` — новая сигнатура и текст (контракт §411). Разбор «тариф/бонус» считается через `AccountLimitFormula` от той же «пригодной» подписки (правило NB-1 сохраняется) |
| `CompaniesController.MapToDto` → `canAddEmployee`, `accountSeatsLimit` | кода нет — из резолвера |
| `AdminBillingController.AssignSubscription` (409 превышения) | `extraEmployeesInRequest`/`extraCompaniesInRequest` удаляются. Новый лимит = `AccountLimitFormula.Compute(plan?.MaxEmployees ?? Free, plan?.MaxCompanies ?? Free, account.GrandfatheredEmployeeBonus)`. **Сегодняшнее** ручное `maxEmployees + GrandfatheredEmployeeBonus` в строке 706 заменяется вызовом формулы, чтобы не было второй копии |
| `CompanyTransferService`/`CompanyTransferCalculator` | кода нет — лимит принимающего аккаунта из резолвера. Меняется текст `TransferRejectedCompanyLimit` |
| `OwnerSubscriptionService` (usage, overLimit цикла 18) | кода нет — из резолвера. Изменения в списках опций — §386 |
| `ProfileController.GetPlanInfoAsync` | кода нет для лимитов. Сумма и `OptionCount` — без опций-лимитов |
| `TrialStateReader` (лимиты триала) | кода нет — из резолвера. Лимиты триала = поля триального тарифа (+ бонус, как и было) |
| `AdminController` CRUD тарифа | лимиты не трогаются. Изменения матрицы — §386 |

### §384.4 Триал (цикл 18) не меняется

`TrialActivationService` материализует только `notifications.whatsapp`. Строки `extra-*` триал
никогда не создавал, код не меняется. Переход «триал → Free» (заморозка, Т1–Т5, C18-8) не
затрагивается: формула лимита стала строже только в части покупок, которых триал не создаёт.
`Cycle18TrialPlanTests`/`Cycle18TrialLifecycleTests` обязаны остаться зелёными **без правки
ожиданий по лимитам**. Единственная ожидаемая правка там — числа `optionCoverage`, если тест их
проверяет (контракт §405).

---

## §385. Купленные `extra-*` на бою: гейт выката вместо переноса

### §385.1 Решение и почему не другое

Заказчик: «купленных `extra-*` на бою нет», но миграция не должна молча терять данные, если строки
найдутся. Рассмотрены три способа:

| Способ | Почему отвергнут / выбран |
|---|---|
| Перенос в бонус (сотрудники → `GrandfatheredEmployeeBonus`, компании → новая колонка) | Заказчик сказал, что колонка и перенос не нужны. Код переноса стал бы мёртвым в единственном реальном окружении. Бессрочный бонус вместо оплаченной покупки — вопрос денег и оферты (SPEC §6 в. 3), его не решают по умолчанию |
| `RAISE EXCEPTION` внутри миграции | Миграции применяются на старте приложения (`MigrateAsync`). Сработавший гейт оставил бы бой в цикле падений, а `deploy-remote.sh` не откатывает сам, только печатает команду `rollback.sh`. **Доступа к машине нет (TD16-4)**, так что это была бы авария без выхода |
| **Проверка в `deploy-remote.sh` до любых изменений на машине** | ✅ Выбрано. Прецедент — `check_legal_manifest` там же: «Nothing has been changed on this host yet — no rollback needed». Отказ виден в логе GitHub Actions, машина не тронута, откатывать нечего |

Итог: утверждение заказчика превращается в **проверяемое предусловие выката**. Если оно верно (как
ожидается), цикл проходит без единой записи в покупки. Если неверно, выкат останавливается, и
решение принимает заказчик (§385.6), а не код.

### §385.2 Что такое «незавершённая покупка опции-лимита» — одно определение

Строка `AccountSubscriptionOption`, у которой:
- опция — опция-лимит (`lower(btrim("CapabilityKey")) IN ('employees','companies')`);
- `"EndsAtUtc" IS NULL OR "EndsAtUtc" > now()` — строка не закрыта;
- `"PaidUntilUtc" IS NULL OR "PaidUntilUtc" >= now()` — оплаченный срок строки не истёк (`NULL` =
  «едет на сроке подписки»).

Состояние подписки и правило текущего тарифа **намеренно не учитываются**. Строка на истёкшей
подписке или при правиле `Unavailable` сегодня в лимит не входит, но **вернётся в него** при
продлении подписки или смене правила. Это отложенное обязательство, и оно противоречит словам
заказчика ровно так же. Гейт строже, чем «считается ли сегодня». Это осознанно: он проверяет
утверждение «покупок нет», а не текущую арифметику.

Определение существует в двух формах. SQL — `deploy/checks/cycle19-retired-limit-options-live.sql`,
**ровно один `SELECT` без psql-метакоманд**, чтобы его мог выполнить тест через Npgsql. LINQ —
`RetiredLimitOptions.LiveRetiredRows(db, now)`. Тест **LIM19-020** выполняет обе формы на одном
наборе данных и требует совпадения результатов.

Столбцы вывода SQL (без персональных данных, лог уходит в GitHub Actions): `billing_account_id`,
`option_code`, `capability_key`, `quantity`, `paid_until_utc`, `ends_at_utc`, `activated_at_utc`.

### §385.3 Шаг в `deploy/deploy-remote.sh` (devops, DO-2)

Новая функция `check_retired_limit_options` вызывается **сразу после** `check_legal_manifest`, до
«Recording rollback point». Поведение:

1. Если сервис `postgres` из `docker-compose.prod.yml` не запущен (первый деплой) — `SKIPPED`,
   `return 0`.
2. Печатает отчёт `deploy/checks/cycle19-limit-options-report.sql` (§385.4) через
   `docker compose -f docker-compose.prod.yml --env-file .env exec -T postgres psql -U postgres -d servicebooking -v ON_ERROR_STOP=1 < …`.
   Ошибка psql → отказ (fail-closed).
3. Выполняет `cycle19-retired-limit-options-live.sql` с `-At -F ' | '`. Пустой вывод → `OK`. Ошибка
   psql → отказ. Непустой вывод → печатает строки и сообщение:
   `ERROR: найдены незавершённые покупки опций «Дополнительные сотрудники/компании» (см. строки выше). Цикл 19 выводит эти опции из оборота; выкат остановлен до решения заказчика — DEPLOY.md §<раздел цикла 19>.`
   и `Nothing has been changed on this host yet — no rollback needed.`, `exit 1`.

Шаг только читает: ничего не пишет и не блокирует. Он остаётся в скрипте до цикла физической
очистки (§393): после выката цикла 19 создать такую строку через API невозможно, поэтому на каждом
следующем выкате гейт проходит мгновенно.

### §385.4 Отчёт для заказчика (US-19-05)

`deploy/checks/cycle19-limit-options-report.sql` — read-only, печатается на **каждом** выкате в лог
деплоя. Шапка через `\echo` допустима: этот файл тест не исполняет.
- **(а)** правила матрицы по опциям-лимитам: `plan_name`, `plan_is_active`, `plan_is_public`,
  `option_code`, `availability` (текстом: Unavailable/Included/Extra), `included_quantity`, и рядом
  `max_employees`/`max_companies` тарифа — чтобы заказчик сверил, где «Включена + кол-во» стояло в
  расчёте на лимит;
- **(б)** все строки `AccountSubscriptionOption` опций-лимитов **в любом состоянии** с меткой
  `live`/`ended` (по §385.2): аккаунт, код, количество, даты.

Бонус в этом цикле **никому не начисляется**. Колонку «кому начислен бонус» из SPEC US-19-05 (б)
заменяет пункт (б) выше: он показывает, что было и в каком оно состоянии. Devops выносит
использование в `DEPLOY.md` (где искать в логе Actions и как повторить запрос вручную, если доступ
к машине появится).

### §385.5 Стартовая сводка в лог (вторая линия)

`RetiredLimitOptionsStartupReport.LogAsync(db, logger)` вызывается в `Program.cs` сразу после
`MigrateAsync`, в том же блоке, где сидятся роли. Запрос — `LiveRetiredRows(db, UtcNow)`. Пусто →
`Information` «Cycle 19: live rows of retired limit options: 0». Не пусто → **`LogError`**
(невозможное после гейта состояние, должно дойти до GlitchTip) со списком
`accountId / optionCode × quantity`. **Старт не прерывается.** Он закрывает окно гонки (строку
создали в старом приложении за секунды между гейтом и рестартом) и любой запуск в обход
`deploy-remote.sh`.

### §385.6 Если гейт сработал

Написано для `DEPLOY.md` (devops):
1. Ничего не делать на машине: она не тронута.
2. Передать заказчику строки из лога (без ПДн — только идентификаторы аккаунтов). Решение за ним:
   (а) дождаться, пока все такие строки истекут (`PaidUntilUtc` в прошлом) или будут закрыты, и
   повторить выкат; (б) заказать хотфикс «перенос в бонус». Это отдельная задача с новой колонкой
   компаний и вопросом к `legal-counsel` (SPEC §6 в. 3), в этот цикл она не входит.
3. Отключать гейт, чтобы «проехать», нельзя: лимиты этих аккаунтов молча уменьшились бы.

---

## §386. Где опция-лимит не должна всплывать

### §386.1 Полный список точек (backend)

| Точка | Файл | Правило |
|---|---|---|
| Каталог в админке | `AdminBillingController.GetOptions` | `WhereNotRetired()` |
| Создание/правка опции | `AdminBillingController.CreateOption/UpdateOption/ValidateOptionInput` | 400 на ключ; 409 на правку выведенной (до «код менять нельзя») |
| Список возможностей | `OptionCapabilityCatalog.Known` | удалить `companies`, `employees` |
| Матрица тарифа — запись | `AdminController.ApplyOptionRulesAsync` | входные элементы по выведенным опциям отбрасываются до записи; при удалении «не присланных» правил выведенные исключаются |
| Матрица тарифа — чтение | `AdminController.MapAdminPlanDto` и **все пять** мест `totalOptionsInCatalog = db.SubscriptionOptions.CountAsync()` | правила — `WhereNotRetired()` на загрузке (`GetPlans`, `CreatePlan`, `UpdatePlan`, `SetSystemFree`, `SetSystemTrial`). Счётчик — `db.SubscriptionOptions.WhereNotRetired().CountAsync()` |
| Назначение подписки | `AdminBillingController.AssignSubscription` | 400 (контракт §406 п. 8). `existingOptions` грузится `WhereNotRetired()`, поэтому строки выведенных опций не создаются, не обновляются и не закрываются |
| Сводки журнала | `AdminBillingController.BuildOptionsSummaryAsync` | `WhereNotRetired()` |
| Карточка и список аккаунтов | `BuildAdminAccountDtoAsync`, `GetBillingAccounts` | `subscribedOptions` — `WhereNotRetired()` (отсюда и суммы) |
| Заявка в карточке и в очереди | `OwnerSubscriptionService.BuildPendingRequestDto`, `GetSubscriptionRequests` | `retired` по строке, `retiredOptionsNotice`, оценка без выведенных. `knownOptions` передаётся **с** выведенными опциями — нужны их имена |
| Экран владельца | `OwnerSubscriptionService.BuildAsync` | `subscribedOptions` и `allOptions` (доступные) — `WhereNotRetired()` |
| Заявка владельца | `BillingController.SubmitRequest` | 400 (контракт §408) |
| Профиль | `ProfileController.GetPlanInfoAsync` | `subscribedOptions` — `WhereNotRetired()` |
| Витрина и предпросмотр | `PricingCatalogBuilder` (фильтр `publicOptions`) | `&& !RetiredLimitOptions.IsRetired(o)` — при любых `IsActive/IsPublic/PricePerMonth` |
| Резолвер | `SubscriptionResolver` | покупки опций-лимитов не читаются вовсе (§384.2) |
| Триал | `TrialActivationService` | без изменений (только WhatsApp) |
| Расчёт цены | `BillingCalculator.MonthlyPriceFor` | без изменений (`IncludedQuantity ?? 1` для WhatsApp как было) |

### §386.2 Фронт не фильтрует опции-лимиты сам — и это требование, а не упрощение

`deploy-remote.sh` переключает символьную ссылку фронта (**новый фронт**) раньше, чем пересобирает и
перезапускает API. Минуту-две новый фронт говорит со **старым** API. Если бы новый `PlansTab` сам
выкидывал `extra-*` из матрицы и сохранял тариф, старый `ApplyOptionRulesAsync` («не присланные
правила удаляются») **стёр бы** правила `extra-*` — ровно данные для отчёта (а). Поэтому фронт
рисует то, что вернул сервер, и отправляет то, что нарисовал. Новый сервер скрывает опции-лимиты и
не удаляет их правила, старый сервер получает их обратно нетронутыми. Грепом в §395 проверяется,
что во `frontend/src` не появилось фильтра по `capabilityKey`.

---

## §387. Совместимость на время выката и отката

| Сочетание | Что происходит | Вред |
|---|---|---|
| новый фронт + старый API (окно выката) | фронт видит `extra-*` в матрице/каталоге (старый сервер их отдаёт) и возвращает их как есть. Нового поля `retiredOptionsNotice` нет — фронт его не рисует (поле необязательное) | нет |
| старый (закэшированный) фронт + новый API | контракт §412: матрица с `extra-*` → 200 и игнор. Адрес с `verify: true` → 200 `{ company }`. Отсутствующие `verification`/`addressVerification` старый код переживает (все чтения защищены) | нет |
| откат: старое приложение + БД после миграции | схема та же. `extra-*` неактивны → не на витрине и не в выборе владельца. Пять колонок на месте. Старый код геокодера включится только при `PROVIDER=yandex`, а на бою его нет | нет |
| повторный выкат цикла 19 | миграция уже применена (no-op). Гейт проходит (новых покупок не бывает) | нет |

---

## §388. Геокодер: что удаляется, что остаётся

### §388.1 Удаляется целиком

**Backend:**
- `ServiceBooking.API/Services/Geo/**` — `IAddressGeocoder.cs`, `LoggingAddressGeocoder.cs`,
  `GeoOptions.cs`, `GeoHandlerFactory.cs`, `AddressLookupService.cs`, `AddressNormalization.cs`,
  `AddressWarnings.cs`, `AddressVerificationState.cs`, `Yandex/YandexAddressGeocoder.cs`,
  `Yandex/YandexGeocodeParser.cs`, `Yandex/YandexGeocoderUrls.cs`;
- `ServiceBooking.Core/Enums/AddressPrecision.cs`;
- `Program.cs`: вызов `DeploymentSafetyChecks.ValidateAddressVerification` (стр. ~142), блок стр.
  ~550–581 (options, фильтры логирования `System.Net.Http.HttpClient.yandex-geocoder.*`,
  `AddHttpClient("yandex-geocoder")`, три регистрации, выбор провайдера,
  `AddScoped<AddressLookupService>`);
- `DeploymentSafetyChecks.ValidateAddressVerification` (стр. ~520–600) целиком;
- `CompanyAddressController.Lookup`, `BuildLookupResult`, `BuildCandidateWarnings`, зависимости
  `AddressLookupService`/`IOptions<GeoOptions>`;
- из `DTOs/Companies/CompanyAddressDtos.cs`: `CompanyAddressVerificationDto`, `GeoPointDto`,
  `LookupAddressDto`, `AddressCandidateDto`, `AddressWarningDto`, `AddressLookupResultDto`,
  `AddressVerificationResultDto`, класс `CompanyAddressMapping`, `using …Services.Geo`;
- из `CompanyDto`: параметры `AddressVerification`, `AddressPoint` (и их комментарии);
- из `CompaniesController`: `IOptions<GeoOptions> geoOptions` в конструкторе, параметр `GeoOptions`
  и `includeAddressPoint` у `MapToDto`, блок построения `addressVerification`/`addressPoint`
  (стр. ~988–1009), аргументы во всех 8 вызовах `MapToDto` (плюс вызов из `CompanyAddressController`);
- конфигурация: секция `AddressVerification` в `appsettings.json` (~198–210) и
  `appsettings.Testing.json` (~31).

**Тесты:** юнит `AddressNormalizationTests`, `AddressVerificationStateTests`, `AddressWarningsTests`,
`CompanyAddressMappingTests`, `YandexGeocodeParserTests`, `YandexGeocoderUrlsTests`; все кейсы
`ValidateAddressVerification` в `DeploymentSafetyChecksTests`; геокодерные кейсы
`AddressVerificationTests.cs` (таблица §391.3).

**Frontend:** `companyAddressApi.lookup`, `AddressLookupParams`; из поля адреса — кнопка «Проверить
адрес», список кандидатов, атрибуция, статус «Подтверждён по карте» / «Не подтверждён», кнопка
«Повторить проверку»; вызов `companyAddressApi.saveAddress(…, true)` в `pages/CabinetPage.tsx`
(стр. 150–157) — **целиком** (адрес уже сохранён `POST /api/companies`); псевдонимы цикла 13 в
`types/index.ts` (стр. 2, 5–17), поля `addressVerification`/`addressPoint` интерфейса `Company`;
`src/types/api-cycle13.generated.ts` и скрипт `types:api:cycle13`.

**Эксплуатация (devops):** пять `AddressVerification__*` из `docker-compose.prod.yml` (~102–118),
блок `ADDRESSVERIFICATION__*` из `.env.production.example` (~184–206). В `ci.yml` геокодера не было,
смок-тест не меняется.

### §388.2 Остаётся и работает как раньше

- **`PUT /api/companies/{id}/address`** (`CompanyAddressController.SaveAddress`). Порядок проверок
  прежний. Адрес пишется побайтно, пустая строка → `null`. **Пять колонок не пишутся** (раньше
  обнулялись). `verify` остаётся в `SaveCompanyAddressDto(string Address, bool Verify = false)` с
  комментарием «принимается и игнорируется». Ответ —
  `CompanyAddressUpdateResultDto(CompanyDto Company)` **без `Verification`**. Имя контроллера и
  маршруты не меняются. Докстринг контроллера переписывается: «адрес компании и правовой гейт
  публичности адреса».
- **`POST /api/companies/address/notice`** — без изменений.
- **Политика `address-verify`** — имя, ключи `RateLimits:address-verify:*`, умолчания 30/60 без
  изменений. Комментарий в `Program.cs` (~753) переписывается («сохранение адреса и гейт»). Текст
  429 в `Program.cs` (~862) → `Слишком много попыток изменить адрес. Повторите позже.` Переименование
  отвергнуто: оно потребовало бы синхронной правки боевого окружения без доступа к машине.
- `POST /api/companies` и `PUT /api/companies/{id}` пишут `Address` как раньше.
- `CompanyMapLinks`, `utils/mapLinks.ts`, `YandexMapsUrl`/`TwoGisUrl`, `MapLinkValidation` — не
  трогаются.
- `PublicAddressNotice.tsx` — только комментарий (стр. 19 упоминает рубильник геокодера).

### §388.3 Поле адреса на фронте: `CompanyAddressField`

`components/company/AddressVerifyField.tsx` → **`components/company/CompanyAddressField.tsx`**
(переименование файла и компонента; тест — `CompanyAddressField.test.tsx`).

Пропсы: `{ companyId: string; initialAddress: string; onSaved: (company: Company) => void }`
(`cityId` и `addressVerification` уходят). Поведение:
- `Input` с подписью «Адрес» (id через `useId`, доступно с клавиатуры, как сейчас);
- кнопка «Сохранить» видна, когда значение изменено (`dirty`);
- «Сохранить» → `PublicAddressNotice`; подтверждение → `companyAddressApi.saveAddress(companyId, value)`
  → `onSaved(result.company)`; отмена гейта — ничего не сохраняется;
- ошибка сохранения — «Не удалось сохранить адрес. Попробуйте ещё раз.» (как сейчас), связана с полем
  через `aria-describedby`;
- пустая строка сохраняется и стирает адрес.

`companyAddressApi.saveAddress(companyId, address)` шлёт `{ address }` **без `verify`** и ждёт
`{ company: Company }`. `SaveCompanyAddressResult` — ручной тип `{ company: Company }`, потому что
`Company` — ручной интерфейс проекта. `AddressNoticeResultDto` берётся из
`api-cycle19.generated.ts`. `CompanyManagePage.tsx` (~стр. 850–860) рендерит `CompanyAddressField`
без `addressVerification`.

### §388.4 Стартовые проверки — все места запуска (урок `baa8da9`)

| Место запуска | Что сделать |
|---|---|
| `Program.cs` | удалить вызов `ValidateAddressVerification` и блок DI |
| `appsettings.json`, `appsettings.Testing.json` | удалить секцию `AddressVerification` |
| тестовые фабрики | `AddressVerificationTestFactory` → **`CompanyAddressTestFactory`** (тег `addr` в `TestHostSettings` сохраняется, комментарий стр. 31 правится). Остаётся только переопределение лимита `address-verify` плюс необязательный словарь «произвольных настроек» для ADDR-030 |
| `docker-compose.prod.yml`, `.env.production.example` | удалить `ADDRESSVERIFICATION__*` (devops) |
| `ci.yml`, дымовой тест `docker-build` | геокодера там не было — без изменений, но devops **проверяет** это грепом |
| боевой `.env` | **не трогаем при выкате**: старые `ADDRESSVERIFICATION__*` никто не читает, а compose их больше не передаёт в контейнер. `DEPLOY.md`: убрать в любой момент после выката, ключ API геокодера (если он был) — удалить и отозвать |

Тест **ADDR-030** поднимает хост со старыми ключами в худших значениях
(`AddressVerification:Provider=yandex`, `CacheHours=99999`, `MaxCandidates=0`, пустой
`Yandex:ApiKey`, `StoreResults=true`) и проверяет, что хост стартовал, `/api/health/ready` = 200, а
`PUT …/address` = 200.

### §388.5 Контракт цикла 13 и генерат

- `contracts/cycle13/openapi.yaml` **не переписывается**: это исторический документ. В начало
  добавляется YAML-комментарий `# ИСТОРИЧЕСКИЙ: геокодер удалён в цикле 19 — см. contracts/cycle19/openapi.yaml`.
  Линт в CI остаётся (схема валидна, выключать нечего).
- `frontend/src/types/api-cycle13.generated.ts` **удаляется**, скрипт `types:api:cycle13` из
  `package.json` удаляется, из шага CI «API types must match committed contracts (TD-07)» удаляется
  `npm run types:api:cycle13`. Иначе CI регенерировал бы удалённый файл и падал на `git diff`.
- Добавляются `types:api:cycle19` → `src/types/api-cycle19.generated.ts`, его сверка в шаге
  «Generated API types must match the contracts» и `../contracts/cycle19/openapi.yaml` в шаге
  «Lint API contracts (TD-07)» (плюс строка в шапке `contracts/redocly.yaml`).

### §388.6 Приёмочный греп US-19-09 и список допустимых совпадений

Команда (только файлы под git, чтобы не ловить `bin/`, `obj/`, `TestResults/`):

```
git grep -n -i -E "geocod|AddressVerification:|ADDRESSVERIFICATION|yandex-geocoder|GeoOptions|AddressLookup" -- \
  'ServiceBooking.*/' 'frontend/src' 'deploy/' '.github/' 'docker-compose*.yml' '.env*.example'
```

**Допустимые совпадения — исчерпывающий список:**
1. `ServiceBooking.Infrastructure/Migrations/20260924065320_AddCompanyAddressVerification.cs` и его
   `.Designer.cs` — применённая миграция, не редактируется;
2. `deploy/ci/check-migration-snapshots.sh` — комментарии про эту миграцию и базовую линию C17-8;
3. `ServiceBooking.Tests/Tests/CompanyAddressTests.cs` — **только** один массив-константа
   `LegacyAddressVerificationSettings` (старые ключи, намеренно подаваемые в ADDR-001/ADDR-030) и
   ссылки на него.

Всё остальное — дефект. Отдельно (US-19-06, конвенция §6 🎯 п. 9) до объявления готовности грепом
проверяется, что **нужное на месте**: `HttpPut("{id:guid}/address")` и `HttpPost("address/notice")` в
`CompanyAddressController.cs`, ровно **два** `EnableRateLimiting("address-verify")`, `PublicAddressNotice`
в `CompanyAddressField.tsx`, `confirmNotice` в `api/companyAddress.ts`.

---

## §389. Структура проекта: что добавляется, меняется, удаляется

```
ServiceBooking.API/
  Services/Billing/
    AccountLimitFormula.cs                 + новое (§384.1)
    RetiredLimitOptions.cs                 + новое (§384.1)
    RetiredLimitOptionsStartupReport.cs    + новое (§385.5)
    BillingTexts.cs                        ~ тексты §414 контракта
    OptionCapabilityCatalog.cs             ~ минус employees/companies
    OwnerSubscriptionService.cs            ~ §386.1
    PricingCatalogBuilder.cs               ~ §386.1
    CompanyTransferService.cs              ~ новый текст 402
    CapabilityKeys.cs                      ~ только doc-комментарий (кто использует)
  Services/SubscriptionResolver.cs         ~ §384.2
  Services/DeploymentSafetyChecks.cs       ~ минус ValidateAddressVerification
  Services/Geo/**                          − удаляется
  Controllers/AdminBillingController.cs    ~ §386.1, §384.3
  Controllers/AdminController.cs           ~ §386.1
  Controllers/BillingController.cs         ~ 400 на опцию-лимит
  Controllers/ProfileController.cs         ~ суммы без опций-лимитов
  Controllers/CompaniesController.cs       ~ минус GeoOptions/addressVerification; 402 текст
  Controllers/CompanyAddressController.cs  ~ минус Lookup; SaveAddress без колонок; ответ { company }
  DTOs/Billing/OwnerBillingDtos.cs         ~ SubscriptionRequestItemDto(+Retired), SubscriptionRequestDto(+RetiredOptionsNotice) — ДОБАВЛЯТЬ В КОНЕЦ позиционных записей
  DTOs/Billing/AdminBillingDtos.cs         ~ AdminSubscriptionRequestDto(+RetiredOptionsNotice) (если используется; анонимный объект в GetSubscriptionRequests — поле добавить туда)
  DTOs/Companies/CompanyDto.cs             ~ минус AddressVerification/AddressPoint
  DTOs/Companies/CompanyAddressDtos.cs     ~ остаются SaveCompanyAddressDto, CompanyAddressUpdateResultDto(Company), SubmitAddressNoticeDto, AddressNoticeResultDto
  Program.cs                               ~ минус геокодер; + вызов стартовой сводки; текст 429
  appsettings.json, appsettings.Testing.json ~ минус AddressVerification
ServiceBooking.Core/
  Entities/Company.cs                      ~ минус 5 свойств
  Enums/AddressPrecision.cs                − удаляется
ServiceBooking.Infrastructure/
  Data/AppDbContext.cs                     ~ 5 теневых свойств (§383.3)
  Migrations/2026092?…_Cycle19RetireLimitOptions.cs (+.Designer.cs)  + новое, только данные
  Migrations/AppDbContextModelSnapshot.cs  = не должен измениться по сути (допускается только ProductVersion)
ServiceBooking.UnitTests/
  AccountLimitFormulaTests.cs, RetiredLimitOptionsTests.cs          + новое
  SubscriptionResolverRulesTests.cs, OwnerSubscriptionOverLimitTests.cs, PricingCatalogBuilderTests.cs, DeploymentSafetyChecksTests.cs ~
  AddressNormalization/AddressVerificationState/AddressWarnings/CompanyAddressMapping/YandexGeocodeParser/YandexGeocoderUrls Tests  − удаляются
ServiceBooking.Tests/
  Tests/Cycle19LimitOptionsTests.cs        + новое (LIM19-*)
  Tests/AddressVerificationTests.cs        → Tests/CompanyAddressTests.cs (ADDR-*, §391.3)
  Infrastructure/AddressVerificationTestFactory.cs → Infrastructure/CompanyAddressTestFactory.cs
  Infrastructure/TestHostSettings.cs       ~ комментарий
  (плюс правки CompaniesTests, AdminTests, CompanyTransferTests, ProfileTests, PricingTests, Cycle15PlansTests, Cycle18TrialPlanTests, LegalPricingGateTests, ApiTestBase — §391.2)
contracts/cycle19/openapi.yaml             + готово (architect)
contracts/cycle13/openapi.yaml             ~ одна строка-комментарий
contracts/redocly.yaml                     ~ шапка: шесть спек
deploy/checks/cycle19-retired-limit-options-live.sql   + (backend; один SELECT)
deploy/checks/cycle19-limit-options-report.sql         + (backend)
deploy/deploy-remote.sh                    ~ check_retired_limit_options (devops)
docker-compose.prod.yml, .env.production.example       ~ минус ADDRESSVERIFICATION__* (devops)
.github/workflows/ci.yml                   ~ типы/линт cycle19, минус types:api:cycle13 (devops)
frontend/
  package.json                             ~ минус types:api:cycle13, плюс types:api:cycle19
  src/types/api-cycle19.generated.ts       + генерат
  src/types/api-cycle13.generated.ts       − удаляется
  src/types/index.ts                       ~ минус псевдонимы цикла 13 и 2 поля Company; + псевдонимы цикла 19
  src/api/companyAddress.ts                ~ минус lookup; saveAddress(companyId, address)
  src/components/company/AddressVerifyField(.test).tsx → CompanyAddressField(.test).tsx
  src/components/company/PublicAddressNotice.tsx        ~ комментарий
  src/pages/owner/CompanyManagePage(.test).tsx          ~ новый компонент
  src/pages/CabinetPage.tsx                ~ минус вызов проверки после создания
  src/pages/BillingPage(.test).tsx         ~ retiredOptionsNotice, пометка retired
  src/pages/admin/BillingAccountsAdminTab(.test).tsx    ~ retiredOptionsNotice в окне одобрения и очереди
  src/pages/admin/PlansTab(.test).tsx      ~ подсказка у полей лимитов
```

---

## §390. Задачи и параллельность

Контракт готов, поэтому **backend и frontend стартуют одновременно**. Внутри backend два трека (А —
лимиты, Б — геокодер) независимы. Они пересекаются только в `CompaniesController.cs` (разные места
файла: ~700 и ~960/конструктор) и в `Program.cs`: один разработчик делает их последовательно
отдельными коммитами, два — договариваются о порядке мёржа этих двух файлов.

### Стадия 0 — до кода (devops, отдельный коммит)

- **D0.** Перенаправить «`SPEC.md` §5» → «`SPEC_CYCLE18_TRIAL_PLAN.md` §5» в
  `ServiceBooking.Tests/Tests/Cycle18TrialPlanTests.cs:181`. Прогнать
  `git grep -n "SPEC.md" -- '*Trial*' '*Cycle18*'` и перенаправить остальные ссылки, если они
  подразумевают спеку цикла 18. Сверить `git show e3774c1:SPEC.md | cmp - SPEC_CYCLE18_TRIAL_PLAN.md`.

### Backend — трек А (лимиты)

| Задача | Содержание | Зависит от |
|---|---|---|
| **BE-A1** | `RetiredLimitOptions`, `AccountLimitFormula`, тексты `BillingTexts` (§414 контракта) + юнит-тесты | — |
| **BE-A2** | `SubscriptionResolver` (§384.2); переписать `SubscriptionResolverRulesTests` и прочие юнит-тесты, сломанные удалением перегрузки | A1 |
| **BE-A3** | админка: `AdminBillingController` (опции, возможности, назначение, карточка/список, очередь), `AdminController` (матрица, счётчики), `OptionCapabilityCatalog` | A1 |
| **BE-A4** | владелец: `OwnerSubscriptionService`, `BillingController`, `ProfileController`, DTO заявки (+`Retired`, +`RetiredOptionsNotice`) | A1 |
| **BE-A5** | `PricingCatalogBuilder`, 402-тексты в `CompaniesController`/`CompanyTransferService` | A1, A2 |
| **BE-A6** | миграция `Cycle19RetireLimitOptions` (после B2 — см. точку останова §383.3), SQL-файлы `deploy/checks/cycle19-*.sql`, `RetiredLimitOptionsStartupReport` + вызов в `Program.cs` | A1, **B2** |
| **BE-A7** | адаптировать функциональные тесты, сломанные новым поведением (§391.2) | A2–A5 |

### Backend — трек Б (геокодер)

| Задача | Содержание | Зависит от |
|---|---|---|
| **BE-B1** | удалить `Services/Geo/**`, `Lookup`, DTO, DI/HTTP-клиент/фильтры, `ValidateAddressVerification`, секции `appsettings*`, `GeoOptions` из `CompaniesController`, поля `CompanyDto`; упростить `SaveAddress`; текст 429; удалить геокодерные юнит-тесты | — |
| **BE-B2** | `Company.cs` без 5 свойств, теневые свойства в `AppDbContext`, удалить `AddressPrecision`. **Точка останова §383.3** (пустая миграция) | B1 (чтобы всё компилировалось) |
| **BE-B3** | `AddressVerificationTests.cs` → `CompanyAddressTests.cs`, фабрика, `TestHostSettings`; сохранить кейсы по таблице §391.3, удалить геокодерные | B1 |
| **BE-B4** | докум.: YAML-комментарий в `contracts/cycle13/openapi.yaml`; подраздел «новое в цикле 19» в `API_DOCUMENTATION.md` §4.16 (`items[].retired`, `retiredOptionsNotice`, 400 на опцию-лимит в заявке) | — |

### Frontend

| Задача | Содержание | Зависит от |
|---|---|---|
| **FE-1** | `package.json` (минус `types:api:cycle13`, плюс `types:api:cycle19`), генерат цикла 19, удалить генерат цикла 13, `types/index.ts`. Первой — остальные на ней | контракт |
| **FE-2** | `CompanyAddressField` (§388.3) + тест; `CompanyManagePage` (+тест); `CabinetPage`; `api/companyAddress.ts`; комментарий `PublicAddressNotice` | FE-1 |
| **FE-3** | `BillingAccountsAdminTab`: в окне одобрения (`AssignSubscriptionModal`) и в карточке заявки очереди печатать `request.retiredOptionsNotice` (если не `null`) **до** кнопки подтверждения; строки `items` с `retired` — с пометкой «выведена» и зачёркнутым количеством. Строки опций окна по-прежнему строятся из `GET /api/admin/options` **без своего фильтра** | FE-1 |
| **FE-4** | `BillingPage`: `pendingRequest.retiredOptionsNotice` и пометка `retired` у строк заявки; `OptionRow` не меняется (сервер не отдаёт опции-лимиты) | FE-1 |
| **FE-5** | `PlansTab`: под полями «Макс. сотрудников суммарно (∞)»/«Макс. компаний суммарно (∞)» подсказка «Лимит задаётся только здесь; опций для докупки сотрудников и компаний нет». Матрица — без собственного фильтра (§386.2) | — |

Типы полей `retired`/`retiredOptionsNotice` берутся из `api-cycle19.generated.ts`
(`SubscriptionRequestDto`, `SubscriptionRequestItemDto`, `AdminSubscriptionRequestDtoCycle19`).
Поля в UI — необязательные на чтение (`?? null`), чтобы окно выката со старым API ничего не ломало.

### DevOps

| Задача | Содержание | Зависит от |
|---|---|---|
| **D0** | см. стадию 0 | — |
| **DO-1** | `ci.yml`: минус `types:api:cycle13` в TD-07; плюс `types:api:cycle19` в «Generated API types must match»; плюс `contracts/cycle19/openapi.yaml` в линт; шапка `contracts/redocly.yaml` | FE-1 (скрипт) |
| **DO-2** | `deploy-remote.sh` → `check_retired_limit_options` (§385.3) | BE-A6 (SQL-файлы) |
| **DO-3** | `docker-compose.prod.yml`, `.env.production.example` — минус `ADDRESSVERIFICATION__*` | — |
| **DO-4** | `DEPLOY.md`: §19 переписать в «Геокодер удалён в цикле 19» (переменные не нужны, убрать в любой момент, ключ удалить и отозвать); новый раздел «Выкат цикла 19» (§385.3–§385.6: что печатает шаг, где отчёт, что делать при отказе); формулировка C18-1 / §4.28 п. 0 «правило по каждой опции, кроме опций-лимитов» | DO-2 |

### QA (после мёржа BE и FE в ветку цикла)

- **QA-1** функциональные кейсы `LIM19-*` и новые `ADDR-029…033` (§391) — если BE их не написал;
  правила конвенции §6 🎯 п. 9 (греп «нужное на месте / ненужного нет», §388.6, §395);
- **QA-2** `schemathesis` по `contracts/cycle19/openapi.yaml` (контракт §415) с токенами суперадмина и
  владельца;
- **QA-3** `TEST_CATALOG.md`: удалённые кейсы — поимённо, с пометкой «удалён в цикле 19» (как
  `ADM-017`); число тестов «было → стало» по трём наборам с объяснением разницы.

### Последовательность и точки останова

```
D0 ─┐
    ├─► BE-A1 ─► BE-A2 ─► BE-A5 ─┐
    │        ├─► BE-A3 ──────────┤
    │        └─► BE-A4 ──────────┼─► BE-A7 ─┐
    ├─► BE-B1 ─► BE-B2 ⛔ ─► BE-A6 ─► DO-2 ──┤
    │        └─► BE-B3 ─────────────────────┤
    ├─► FE-1 ─► FE-2, FE-3, FE-4 ; FE-5 ────┼─► QA-1..3
    └─► DO-1 (после FE-1), DO-3, DO-4 ──────┘
⛔ = пустая сгенерированная миграция (§383.3); не пусто — стоп.
```

---

## §391. Тесты

### §391.1 Новые

**Юнит (`ServiceBooking.UnitTests`):**
- `AccountLimitFormulaTests`: 5+2=7; `null`+2=`null`; отрицательный бонус = 0; компании без бонуса.
- `RetiredLimitOptionsTests`: `employees`, `Companies`, `" employees "` → выведена;
  `notifications.whatsapp`, `null`, `employee` → нет.
- `SubscriptionResolverRulesTests` — переписанные кейсы сложения: `Resolve` не имеет параметров
  покупок, лимит = поле + бонус; Free-база при непригодной подписке + бонус.
- `PricingCatalogBuilderTests`: опция-лимит, активная, публичная и с ценой, на витрину не попадает.

**Функциональные (`ServiceBooking.Tests`, `Cycle19LimitOptionsTests`)**, ID `LIM19-`:

| ID | Что проверяет |
|---|---|
| 001 | `GET /api/admin/options` без опций-лимитов (даже активных, публичных, с ценой) |
| 002 | `option-capabilities` без `employees`/`companies` |
| 003 | `POST /api/admin/options` с `employees`, `" Companies "` → 400 (текст); с `analytics` → 201 |
| 004 | `PUT` выведенной опции → 409; `PUT` WhatsApp с ключом `employees` → 400 |
| 005 | `PUT /api/admin/plans/{id}` с правилом `extra-*` в `options` → 200; строка правила в БД не изменилась; `options`/`optionCoverage` в ответе без неё |
| 006 | `PUT` тарифа без правил `extra-*` → сохранённые правила `extra-*` **не удалены** |
| 007 | `PUT …/subscription` с `extra-*` → 400; подписка и строки опций не изменились |
| 008 | превышение: тариф 5, бонус 2, занято 7 → 200; 8 → 409. Тариф 2 компании, занято 3 → 409 |
| 009 | живая строка `extra-employees` (вставлена SQL) **не** увеличивает лимит: `employeesLimit`, `canAddEmployee`, 402 — по «поле + бонус» |
| 010 | 402 добавления сотрудника — новый текст, без «докуплено» и без призыва к опции |
| 011 | 402 создания компании — текст прежний |
| 012 | перенос компании — лимит принимающего по формуле, новый текст 402 |
| 013 | владелец: `options`/`availableOptions`/`totalMonthlyPrice` без опций-лимитов |
| 014 | владелец: заявка с `extra-*` → 400; прежняя заявка не изменилась |
| 015 | заявка «до выката» (`RequestedOptionsJson` с `extra-*` вставлен в БД): у владельца, в карточке и в очереди `retired = true`, `retiredOptionsNotice` — точный текст, оценка без неё; одобрение без `extra-*` → 200; одобрение с ней → 400 |
| 016 | витрина и предпросмотр без опций-лимитов; `includedEmployees/Companies` = поля тарифа |
| 017 | карточка/список аккаунта: лимиты по формуле, `options` и сумма без опций-лимитов |
| 018 | профиль: `maxEmployees`/`maxCompanies` по формуле, `optionCount` без опций-лимитов |
| 019 | `AssignSubscription` не меняет строку `extra-*` (ни `EndsAtUtc`, ни `Quantity`) |
| 020 | **гейт**: набор — без покупок; живая `extra-employees`; живая `extra-companies`; `EndsAtUtc` в прошлом; `PaidUntilUtc` в прошлом; живая при правиле `Unavailable`; живая на тарифе с `null`-лимитом; живая на истёкшей подписке. SQL-файл (прочитан из `deploy/checks/`) и `LiveRetiredRows` возвращают **одно и то же** — ровно живые строки (5 шт.). Для аккаунтов вне результата лимит новой формулы равен лимиту старой (Σ покупок = 0 по построению) |
| 021 | миграция: повторное исполнение SQL из `Up` ничего не меняет; число строк `SubscriptionOptions`/`PlanOptionRules`/`AccountSubscriptionOptions` до и после равно; опции-лимиты `IsActive = false` |
| 022 | стартовая сводка: при живой строке пишется `Error`, хост стартует |

Файл SQL тест находит от корня репозитория тем же способом, каким тесты уже находят файлы вне
проекта. Если общего хелпера нет — подъём от `AppContext.BaseDirectory` до `ServiceBooking.sln`.

**Vitest:**
- `CompanyAddressField.test.tsx` — нет кнопки «Проверить адрес» и статуса; «Сохранить» → гейт →
  `saveAddress(companyId, value)` **с двумя аргументами**; отмена гейта не сохраняет; пустая строка
  сохраняется; ошибка показывается и связана с полем;
- `BillingPage.test.tsx` — `retiredOptionsNotice` печатается как есть; `null` — ничего;
- `BillingAccountsAdminTab.test.tsx` — предупреждение в окне одобрения до подтверждения; строки
  окна строятся из каталога (выведенной там нет — нет и строки);
- `PlansTab.test.tsx` — подсказка у полей лимитов; матрица рисует ровно то, что вернул каталог
  (нет собственного фильтра).

### §391.2 Существующие тесты, которые изменятся (не удалятся)

`SubscriptionResolverRulesTests`, `OwnerSubscriptionOverLimitTests` (если строят `Resolve` с
покупками), `CompaniesTests` (тексты 402 и кейсы с купленными местами), `AdminTests` (матрица),
`CompanyTransferTests` (текст), `ProfileTests`, `PricingTests`, `Cycle15PlansTests`,
`Cycle18TrialPlanTests` (только числа `optionCoverage`, если проверяются), `LegalPricingGateTests`.
**`ApiTestBase`**: хелперы, которые «докупают места» через `PUT …/subscription` с `extra-employees`,
после цикла получат 400. Их нужно переписать на поднятие `MaxEmployees` тарифа или прямую установку
`GrandfatheredEmployeeBonus` в БД. Тесты, проверявшие **сложение с покупками**, переписываются в
«покупка не влияет» (LIM19-009), а не удаляются. `BillingCatalogSeedKeysTests` не меняется
(миграции сида не трогаются).

### §391.3 Судьба кейсов `ADDR-` (`AddressVerificationTests.cs` → `CompanyAddressTests.cs`)

| ID | Было | Стало |
|---|---|---|
| 001 | `Lookup_ProviderLogging_Returns404…` | **сохранён, переписан**: `Lookup_AnyConfiguration_Returns404_RouteRemoved` (в т. ч. при `Provider=yandex`) |
| 002 | `SaveAddress_ProviderLogging_StillSaves_AsUnverified_US140` | **сохранён**: `SaveAddress_Saves_ByteForByte_US140`, ответ без `verification` |
| 003 | `CompanyDto_ProviderLogging_AddressVerificationAvailableIsFalse` | **удалён в цикле 19** (заменён ADDR-032) |
| 004 | `Lookup_Anonymous_Returns401` | **удалён** (маршрута нет; 404 — ADDR-001) |
| 005, 006, 007, 009, 010 | 401 / 403 клиент / 403 мастер / SuperAdmin / 404 | **сохранены без изменений смысла** |
| 008 | `Lookup_WithCompanyId_NonManagerClient_Returns404…` | **удалён** |
| 011–020 | проверка, кеш, `StoreResults`, кандидаты | **удалены** |
| 021 | `PublicGetBySlug_AddressVerification_IsNull…` | **удалён** (заменён ADDR-032) |
| 022 | `SaveAddress_EmptyString_ClearsAddress…` | **сохранён** |
| 023 | `PublicSearch_ByAddress_FindsCompany_BeforeAndAfterVerification_R7` | **сохранён**, переименован в `PublicSearch_ByAddress_FindsCompany_R7` (без части про проверку) |
| 024 | `Lookup_ExceedingPermitLimit_Returns429…` | **удалён**, заменён ADDR-029 |
| 025–028 | `ConfirmNotice_*` | **сохранены без изменений** |
| **029** | — | `SaveAddress_ExceedingPermitLimit_Returns429_WithNewText` |
| **030** | — | `Startup_WithLegacyAddressVerificationSettings_StartsAndSaves` (§388.4) |
| **031** | — | `SaveAddress_VerifyTrue_IsIgnored_FiveColumnsUntouched` (колонки заполняются SQL до вызова и сверяются SQL после) |
| **032** | — | `CompanyDto_HasNoAddressVerificationOrAddressPoint` (`/my`, `/{slug}`, ответ `PUT …/address`) |
| **033** | — | `ConfirmNotice_ExceedingPermitLimit_Returns429` (гейт по-прежнему под политикой) |

---

## §392. Технические риски и решения

| № | Риск | Решение |
|---|---|---|
| Р19-1 | Слова заказчика «покупок нет» неверны | гейт §385 останавливает выкат до изменений; строки в логе; решение — за заказчиком (§385.6) |
| Р19-2 | Покупку создали в старом приложении между гейтом и рестартом | стартовая сводка `LogError` (§385.5). Данные не теряются: строка не тронута; последствие — лимит одного аккаунта; ручной разбор |
| Р19-3 | Новый фронт + старый API в окне выката стирает правила `extra-*` | фронт не фильтрует сам (§386.2); тест FE «матрица = каталог» |
| Р19-4 | Теневые свойства дают расхождение со снапшотом | точка останова §383.3; CI `DriftProbe` + монотонность |
| Р19-5 | `ApplyOptionRulesAsync` удаляет «не присланные» правила выведенных опций | явное исключение + LIM19-006 |
| Р19-6 | Неизвестный потребитель `CompanyDto.addressVerification`/`addressPoint` | греп: только наш фронт; во внешнем справочнике полей не было; ломающее изменение перечислено в контракте §416 |
| Р19-7 | Ключ API геокодера остаётся в боевом `.env` | compose его больше не передаёт в контейнер, код не читает; `DEPLOY.md` — удалить и отозвать. В логах ключ не появлялся (фильтры логирования HTTP-клиента удаляются вместе с клиентом) |
| Р19-8 | Правовые тексты (приложение Б к `13-public-address-notice.html`, «дыра» п. 9.8 политики) описывают передачу адреса геокодеру, которой больше нет | `legal-drafts/` не трогаем (ответ заказчика 5). Текст обещает **больше** обработки, чем есть, — безопасное направление расхождения; вопросы `legal-counsel` — SPEC §6 (в. 3, вторая половина, снимается: переноса нет) |
| Р19-9 | Число тестов падает, и это маскирует потерю нужных | QA перечисляет удалённые поимённо (§391.3); греп «нужное на месте» (§388.6) |
| Р19-10 | Суммы владельца и админа уменьшаются из-за исключения оплачиваемых `extra-*` | невозможно после гейта: незавершённых строк нет |
| Р19-11 | Бонус «вливается» в «включено в тариф», и владелец не видит разбивку | так и было с цикла 7; предположение SPEC «как сегодня» выполнено буквально (§382) |
| Р19-12 | Шаг гейта в `deploy-remote.sh` зависит от имени сервиса и учётки Postgres | используются те же `postgres`/`servicebooking`/`postgres`, что и в `DEPLOY.md` (стр. ~790) для `billing-precheck.sql`; при ошибке psql — отказ (fail-closed), а не пропуск |

---

## §393. Что цикл сознательно не делает

- Не удаляет колонки `Companies.Address*` и строки опций-лимитов, их правил, покупок, заявок —
  отдельный цикл после проверки боевых данных (SPEC «откладывается»). Там же уходит гейт §385.3.
- Не вводит колонку бонуса компаний и не переносит покупки в бонусы.
- Не строит интерфейс правки `GrandfatheredEmployeeBonus`.
- Не прибавляет `IncludedQuantity` из матрицы `extra-*` к полям тарифа (ответ заказчика 3), не правит
  поля тарифов на бою — заказчик делает это сам по отчёту (а).
- Не правит `legal-drafts/` (ответ заказчика 5).
- Не переписывает исторические документы (`ARCHITECTURE_CYCLE13.md`, `API_CONTRACT_CYCLE13.md`,
  `contracts/cycle13/`, `LEGAL_REVIEW.md` §16, старые записи `CHANGELOG.md`), кроме однострочных
  пометок.
- Не меняет политику лимитов `address-verify` ни по имени, ни по значениям.

---

## §394. Документация — кто что правит

| Документ | Что | Кто |
|---|---|---|
| `DEPLOY.md` | §19 → «Геокодер удалён в цикле 19»; раздел выката цикла 19 (гейт, отчёт, отказ); C18-1 / чек-лист триала — «кроме опций-лимитов» | devops (DO-4) |
| `API_DOCUMENTATION.md` §4.16 | подраздел «новое в цикле 19» (владелец: `items[].retired`, `retiredOptionsNotice`, 400 на опцию-лимит) | backend (BE-B4) |
| `contracts/cycle13/openapi.yaml` | строка-комментарий «исторический» | backend (BE-B4) |
| `TEST_CATALOG.md` | `LIM19-*`, `ADDR-029…033`, удалённые `ADDR-` поимённо | QA |
| `README.md` (раздел о продукте), `CHANGELOG.md` (новая запись «Удалено/Изменено») | геокодер удалён; докупки сотрудников/компаний нет; лимит — только тариф | product-analyst («Вызов 2») |
| `ARCHITECTURE_CYCLE13.md`, `API_CONTRACT_CYCLE13.md`, `LEGAL_REVIEW.md` §16 | одна строка «удалено в цикле 19» в начале соответствующего раздела | product-analyst («Вызов 2») |
| `CURRENT_STATE.md` | в конце цикла: §0.4 → итог, C18-1/§4.28 п. 0, §4.25 D, TD16-4 (вопрос о `PROVIDER` снимается), AV1/AV2 | по регламенту цикла |

---

## §395. Инварианты для ревью (проверяются грепом)

1. `git grep -n "CapabilityKeys.Employees\|CapabilityKeys.Companies" -- ServiceBooking.API` → только
   `Services/Billing/RetiredLimitOptions.cs` (и объявление в `CapabilityKeys.cs`).
2. `git grep -n "extraEmployees\|extraCompanies\|extraEmployeesInRequest" -- 'ServiceBooking.*'` → пусто.
3. `grep -n "IsSystemTrial" ServiceBooking.API/Services/SubscriptionResolver.cs` → пусто (как в цикле 18).
4. В новой миграции нет `DropColumn|DropTable|DELETE|RenameColumn|AlterColumn`.
5. `git grep -n "Services.Geo\|IAddressGeocoder\|AddressLookupService" -- 'ServiceBooking.*'` → пусто.
6. `grep -n "Address\(VerifiedInputKey\|VerifiedAt\|Precision\|Latitude\|Longitude\)" ServiceBooking.Core/Entities/Company.cs`
   → пусто; в `AppDbContext.cs` — ровно пять строк `e.Property<…>("Address…")`.
7. Файла `ServiceBooking.Core/Enums/AddressPrecision.cs` нет.
8. `git grep -n -i "capabilityKey" -- frontend/src/pages frontend/src/components` — нет новой логики
   фильтрации (только отображение/типы, если было).
9. Приёмочный греп §388.6 — только разрешённые совпадения; «нужное на месте» — по списку §388.6.
10. `EffectivePlan` не изменил форму; `SubscriptionResolver.Resolve` имеет одну публичную перегрузку.
11. Во всех местах, где `ApplyOptionRulesAsync` удаляет правила, условие исключает опции-лимиты.
12. `AppDbContextModelSnapshot.cs` по `Company` не изменился (diff пуст, допускается `ProductVersion`).

---

## §396. Чек-лист приёмки цикла (сводно по SPEC)

- [ ] US-19-01: матрица без `extra-*`; поля лимитов и «до N» прежние; `PUT plans` с `extra-*` → 200 и
      игнор; `option-capabilities` без двух ключей; `POST/PUT options` → 400; витрина без `extra-*`.
- [ ] US-19-02: выбрать `extra-*` при назначении нельзя (каталог без них); `PUT …/subscription` с ними
      → 400; 409 по формуле (5+2: 7 → 200, 8 → 409); перенос по формуле.
- [ ] US-19-03: миграция не ломающая, идемпотентная; гейт + LIM19-020 доказывают «лимит не
      уменьшился»; 402 прежние по смыслу; `canAddEmployee` по формуле; «куплено» нигде нет; триал
      зелёный.
- [ ] US-19-04: `BillingPage`/`OptionRow` без `extra-*`; заявка с ними → 400; старая заявка — пометка,
      текст, одобрение без них.
- [ ] US-19-05: отчёт (а)/(б) в логе каждого выката; способ описан в `DEPLOY.md`.
- [ ] US-19-06: адрес вводится, сохраняется, стирается; гейт и `ConsentRecord`; 30/ч на тех же
      маршрутах; `POST /api/companies` с адресом; ADDR-002/005–007/009/010/022/023/025–028 зелёные;
      ссылки в карты не тронуты.
- [ ] US-19-07: нет «Проверить адрес», кандидатов, атрибуции, «Подтверждён по карте»; `CabinetPage`
      без вызова проверки.
- [ ] US-19-08: старт без секции `AddressVerification` везде; старт со старыми «роняющими» значениями
      (ADDR-030); `DEPLOY.md` про переменные и ключ.
- [ ] US-19-09: греп §388.6; `lookup` → 404 всегда; генераты/контракты по §388.5; CI зелёный;
      документы по §394.
- [ ] NFR: три набора тестов зелёные; `Migrations snapshot drift` и `Designer snapshots are monotonic`
      зелёные; `redocly lint` и сверка генератов зелёные; schemathesis без расхождений.
