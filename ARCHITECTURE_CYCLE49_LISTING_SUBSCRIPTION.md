# ARCHITECTURE — цикл 49 ServiceBooking: видимость салона в каталоге по услугам и сотрудникам + пункт «Подписка» в шапке

**Дата: 2026-11-01. Ветка `cycle/049-listing-services-and-subscription-tab` (код = `develop` @ `d32a5278`; SPEC — `5f583c0b`,
CURRENT_STATE — `c29c9b8d`).** Разделы §49.0–§49.14.
Вводные: `SPEC.md` цикла 49 (US-49-01…06, Q49-1…5, §6 П-1…6, A49-1…5, R49-1…5) и **решение заказчика от 2026-11-01**: все
предположения Q49-1…Q49-5 и допущения §6 приняты как есть. L49-1 (legal-counsel) — до выката, кодирование не блокирует.
Контракт — `API_CONTRACT_CYCLE49_LISTING_SUBSCRIPTION.md` (§49.20–§49.30). Машиночитаемо — `contracts/cycle49/openapi.yaml`.

Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` (цикл 3) не трогаются: по конвенции проекта документы цикла лежат в корне под
суффиксом. Факты о коде сверены по файлам на `5f583c0b`.

**Коротко.** Проект существующий, стек и структура не меняются, миграций нет. Бэкенд меняет одно правило видимости салона,
его SQL-двойник, чек-лист (два новых пункта и поясняющий текст) и ответ `/api/billing/subscription` для «Записи» (список
тарифов). Фронт рисует новые пункты и ✗, сбрасывает кеш блока каталога после правок услуг и сотрудников, добавляет пункт
«Подписка» в шапку и блок «Тарифы «Записи»» на `/billing`. DevOps добавляет отчёт и стоп-условие перед выкатом.

---

## §49.0. Решения одним экраном

| # | Вопрос | Решение | Где |
|---|---|---|---|
| Q49-2/3/4 | Что считается «есть услуга» и «есть сотрудник» | Услуга — `Service.IsActive = true`. Сотрудник — `CompanyMember` с `Role ∈ {Master, CompanyOwner}` и `ProvidesServices = true`. Привязка `MasterService` не нужна. `Client` не считается. Оба предиката — одно определение (`Expression`) в `SalonListingQuery`, им пользуются и каталог, и чек-лист | §49.3 |
| A49-1 | Как фронт узнаёт «владелец хотя бы одного салона» | Уже существующий `GET /api/companies/my` без `kind` (= только активные салоны, роль `CompanyOwner`), общий ключ кеша `['my-companies']`. Запрос только при роли `CompanyOwner` в токене. Нового поля и эндпоинта нет | §49.5 |
| A49-2 | `CompanyDto.publicListingEnabled` | **Значение не меняем** (в горячих анонимных списках не появляются лишние запросы на каждый салон). Правим комментарий в `CompanyDto.cs` и описание в контракте: поле ≠ «в каталоге», истина — `catalog-listing.visible`. В каталожных списках поле и так всегда `true` | §49.3.6 |
| A49-3 | Переключатель конфигурации для нового правила | **Не нужен.** Решение заказчика принимается **до** выката по read-only отчёту (§49.7). Миграций нет, поэтому откат — обычный `rollback.sh` на прежний образ. Если по отчёту пропадают все салоны, выкат механически останавливается. Переключатель удвоил бы правило, его SQL-двойника и матрицу CY31-05 | §49.3.7, §49.7 |
| A49-4 | `availablePlans` для «Записи» | Заполняется в ветке «Записи» `OwnerSubscriptionService.BuildAsync`: `Line = Services ∧ IsActive ∧ IsPublic ∧ ¬IsSystemTrial ∧ ¬IsSystemFree`, порядок как на `/pricing`. Тест цикла 24 (`AvailablePlans = null` для «Записи») переписывается осознанно | §49.6 |
| A49-5 | Как блок «Каталог» узнаёт о правках во вкладках «Услуги»/«Сотрудники» | Две меры: (1) `refetchOnMount: 'always'` у `SalonCatalogListingSection` (вкладки размонтируются при переключении, а `staleTime` по умолчанию 30 с); (2) общий `invalidateSalonCatalogListing(qc, companyId)` в `onSuccess` мутаций услуг и участников | §49.4.3 |
| — | Имена новых кодов | `NoActiveServices`, `NoServiceProviders` — по образцу магазинных `NoWorkingHours`/`NoPublishedProducts`: имя называет отказ, `done = true` — условие выполнено. Добавляются в **конец** общего enum (в БД не хранится) | §49.3.1 |
| — | Поясняющий текст | Новое поле `conditionsText` в общем `CatalogListingDto`, с `JsonIgnore(WhenWritingNull)`: салон отдаёт его всегда, магазин — никогда (схемы магазина строгие, `additionalProperties: false`) | §49.3.5 |
| — | Тариф и опции в одной заявке | Сервер по-прежнему перезаписывает заявку целиком. Экран «Записи» переносит в новую заявку то, что уже было в открытой (тариф ↔ опции) | §49.6.3 |
| US-49-04 | Отчёт перед выкатом | `deploy/checks/cycle49-catalog-report.sql` в precheck `deploy-remote.sh` (как цикл 48) + стоп `cycle49-catalog-empty.sql`, если пропадают все салоны (обход — `CYCLE49_EMPTY_CATALOG_ACCEPTED=true` в `.env` после решения заказчика). Согласие SQL с кодом проверяет функциональный тест | §49.7 |

---

## §49.1. Стек: без изменений

Бэкенд: .NET 8, ASP.NET Core MVC, EF Core 8 + PostgreSQL 16. Фронтенд: React 18, TypeScript, Vite 5, Tailwind 3.4,
TanStack Query 5, vitest. Новых пакетов npm/NuGet, сервисов, переменных окружения приложения нет (единственная новая
переменная — `CYCLE49_EMPTY_CATALOG_ACCEPTED` в `.env` хоста, её читает только `deploy-remote.sh`). Хостинг, nginx, CSP,
порядок выката прежние.

---

## §49.2. Модель данных

**Сущности, таблицы и миграции — без изменений.** Цикл читает уже существующие поля:
- `Service.IsActive`, `Service.CompanyId`;
- `CompanyMember.Role` (`UserRole`: `Client`, `Master`, `CompanyOwner`, `SuperAdmin`), `CompanyMember.ProvidesServices`
  (по умолчанию `true`, у владельца тоже), `CompanyMember.CompanyId`;
- `SubscriptionPlanConfig.Line/IsActive/IsPublic/IsSystemTrial/IsSystemFree/SortOrder/PricePerMonth/MaxCompanies/MaxEmployees/Highlights/Description`.

Данные салонов цикл не меняет: ничего не включает и не выключает за владельца.

**Индексы.** Проверки — коррелированные `EXISTS` по `CompanyId`. В снапшоте есть составной уникальный индекс
`CompanyMembers (CompanyId, UserId)` (ведущая колонка `CompanyId`) и FK-индекс `Services (CompanyId)` по конвенции EF.
BE-49-1 подтверждает оба по `AppDbContextModelSnapshot.cs` и `EXPLAIN` на тестовой базе. Новый индекс ожидаемо не нужен. Если
нужен — только добавочная миграция `CREATE INDEX` (ломающие миграции запрещены).

---

## §49.3. Одно правило видимости салона (US-49-01, US-49-02)

### §49.3.1 `SalonListingRules` (чистое правило)
`Services/Companies/SalonListingRules.cs`:
```csharp
public sealed record SalonListingInput(
    bool IsActive, bool HasActiveService, bool HasServiceProvider, bool AllowedByPlan, bool ShowInPublicListing);
```
Пункты по порядку (SPEC US-49-02):
1. `SalonBlocked` — только если `!IsActive`, `done = false`;
2. `NoActiveServices` — всегда, `done = HasActiveService`, текст `SalonListingRules.HasServicesText` = «Есть услуги»;
3. `NoServiceProviders` — всегда, `done = HasServiceProvider`, текст `SalonListingRules.HasServiceProvidersText` = «Есть сотрудники, оказывающие услуги»;
4. `NotAllowedByPlan` — только если `!AllowedByPlan`, `done = false`, текст `CatalogListingRules.NotAllowedByPlanText` (общий);
5. `HiddenByOwner` — всегда, `done = ShowInPublicListing`, текст `CatalogListingRules.HiddenByOwnerText` (общий).

`Visible = checklist.All(done)`. Новая константа `SalonListingRules.ConditionsText` = «Салон появится в каталоге ezbook.ru,
только когда выполнены все условия ниже.» (П-2; окончательную формулировку утверждает заказчик, правка — одна константа).

В `CatalogListingCheckCode` (`Services/Shops/CatalogListingRules.cs`) в **конец** добавляются `NoActiveServices`,
`NoServiceProviders` с комментарием «Cycle 49: salon checklist only; the shop route never emits it». Магазинное правило
`CatalogListingRules` не меняется.

### §49.3.2 Предикаты — одно определение
`Services/Companies/SalonListingQuery.cs` получает два публичных выражения:
```csharp
public static readonly Expression<Func<Service, bool>> ActiveService = s => s.IsActive;
public static readonly Expression<Func<CompanyMember, bool>> ServiceProvider =
    m => m.ProvidesServices && (m.Role == UserRole.Master || m.Role == UserRole.CompanyOwner);
```
Их используют **обе** стороны: SQL-двойник (§49.3.3) и контроллер чек-листа (§49.3.4). Вторых копий условий в коде нет.

### §49.3.3 SQL-двойник
```csharp
public static IQueryable<Company> VisibleInSalonCatalog(this IQueryable<Company> companies, AppDbContext db, DateTime nowUtc)
{
    var activeServices = db.Services.Where(ActiveService);
    var providers = db.CompanyMembers.Where(ServiceProvider);
    return companies
        .Where(c => c.Kind == CompanyKind.Services && c.IsActive && c.ShowInPublicListing)
        .Where(c => activeServices.Any(s => s.CompanyId == c.Id))
        .Where(c => providers.Any(m => m.CompanyId == c.Id))
        .WhereAllowsPublicListing(db, nowUtc);
}
```
EF Core встраивает захваченные `IQueryable` как коррелированные `EXISTS` в один SQL-запрос (тот же приём, что
`PublicListingQuery` с `SubscriptionUsability.UsableAt`). Вызывающие (`CompaniesController.GetAll`, `GetPublic`) не меняются:
город, поиск, `Count`, `Skip/Take` по-прежнему в SQL. Комментарий ⚠️ «правило и двойник меняются вместе» обновляется:
теперь пять условий.

### §49.3.4 Контроллер чек-листа
`CompanyCatalogListingController.Build` → `BuildAsync(Company company, bool allowed, CancellationToken ct)`:
```csharp
var hasService  = await db.Services.AsNoTracking().Where(s => s.CompanyId == company.Id).AnyAsync(SalonListingQuery.ActiveService, ct);
var hasProvider = await db.CompanyMembers.AsNoTracking().Where(m => m.CompanyId == company.Id).AnyAsync(SalonListingQuery.ServiceProvider, ct);
```
и `SalonListingRules.Evaluate(new SalonListingInput(company.IsActive, hasService, hasProvider, allowed, company.ShowInPublicListing))`.
GET и PUT вызывают `BuildAsync`. Порядок проверок, права, коды ответов, 409 только по тарифу — без изменений. PUT
по-прежнему пишет одну колонку.

### §49.3.5 DTO
`DTOs/Catalog/CatalogDtos.cs`:
```csharp
public sealed record CatalogListingDto(
    bool ShowInCatalog, bool AllowedByPlan, bool Visible, string StatusText, string? NotAllowedByPlanText, List<CatalogListingCheckDto> Checklist,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ConditionsText = null);
```
Салонный контроллер передаёт `SalonListingRules.ConditionsText`. `ShopsController.BuildCatalogListingAsync` не меняется
(значение по умолчанию `null` → поле не пишется). Приём уже есть в проекте: `AvailablePlanDto.MaxHouses/MaxResources`.

### §49.3.6 `CompanyDto.publicListingEnabled` (A49-2)
Комментарий в `DTOs/Companies/CompanyDto.cs` («This is what GET /api/companies actually filters on») заменяется на: «= owner
opt-in ∧ plan allows. Since cycle 49 this is NOT catalog presence (the catalog also needs an active service and a service
provider) — the truth is GET …/catalog-listing `visible`». Значение и сборка (`CompanyDtoAssembler`) не меняются. Почему не
приводим к новому правилу: `CompanyDto` собирают семь мест, включая анонимный горячий список; две лишние проверки на каждый
салон ради поля, которое фронт не показывает, — чистая цена без пользы.

### §49.3.7 Почему без переключателя (A49-3)
- Решение заказчика по Q49-1 принимается **до** выката по отчёту §49.7, а не после: «включить позже» не нужно.
- Миграций нет: откат — прежний образ через `rollback.sh`, данные не трогаются.
- Худший случай (пропадают все салоны) механически останавливает выкат (§49.7.2).
- Цена переключателя — два варианта правила, два варианта SQL-двойника и двойная матрица согласованности. Это ровно та
  хрупкость, от которой правило собрали в одно место в цикле 31 (C31-4).

### §49.3.8 Производительность
`GET /api/companies/public`: +2 коррелированных `EXISTS` по индексам в том же запросе (и в `Count`, и в странице). N+1 нет,
загрузки в память нет. `catalog-listing`: +2 лёгких `AnyAsync` на запрос владельца. Порог «заметно не выросло» проверяет
QA-49-1: `EXPLAIN ANALYZE` до и после на тестовой базе с ≥ 1 000 салонов (план — `Index Scan`/`Index Only Scan` в подзапросах).

---

## §49.4. Фронт: блок «Каталог ezbook.ru» (US-49-02)

### §49.4.1 `CatalogListingCard` (общий с goods)
`src/components/company/CatalogListingCard.tsx`:
- `CatalogListingView` + `conditionsText?: string | null`;
- абзац `conditionsText` (`text-sm text-ink-soft`, `data-testid="listing-conditions"`) — между `statusText` и списком, только
  если строка непустая (у магазина его нет);
- «○» → «✗» у невыполненного пункта, цвет `text-danger` (контраст на `bg-white` ≥ 4,5 : 1 — проверить в M49-02), `aria-hidden`
  и `sr-only` без изменений.

### §49.4.2 `SalonCatalogListingSection`
Тип — `SalonCatalogListingDto` из `api-cycle49.generated.ts` (переключение реэкспорта в `src/types/index.ts`). Разметка не
меняется: поле `conditionsText` уходит в карточку через `data`.

### §49.4.3 Свежесть блока (A49-5)
- `useQuery({ queryKey: ['company-catalog-listing', companyId], …, retry: false, refetchOnMount: 'always' })`.
- Новый модуль `src/pages/owner/salonCatalogListingCache.ts`:
  `export const invalidateSalonCatalogListing = (qc: QueryClient, companyId: string) => qc.invalidateQueries({ queryKey: ['company-catalog-listing', companyId] })`.
- Вызов в `onSuccess` мутаций `owner/CompanyManagePage.tsx`:
  - `ServicesTab`: `createMut`, `updateMut` (в том числе включение/выключение услуги), `deleteMut`;
  - карточка участника: `providesMut`;
  - `MembersTab`: `addMut`, `removeMut`.
  `imageMut`, `saveMut`, `commissionMut` не влияют на условия и не трогаются.

---

## §49.5. Пункт «Подписка» в шапке (US-49-05, A49-1)

### §49.5.1 Откуда признак «владелец салона»
Новый хук `src/hooks/useOwnsSalon.ts`:
```ts
export function useOwnsSalon(): boolean {
  const { isAuthenticated, hasRole } = useAuthStore()
  const enabled = isAuthenticated() && hasRole('CompanyOwner')
  const { data } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy, enabled, staleTime: 5 * 60_000 })
  return enabled && (data?.length ?? 0) > 0
}
```
- `GET /api/companies/my` без `kind` уже отдаёт ровно «активные салоны, где я `CompanyOwner`». Владелец только магазина,
  дома или бани получает `[]`, хотя роль `CompanyOwner` в токене у него есть (R49-5).
- Ключ общий с кабинетом: на страницах кабинета второго запроса нет. Создание салона (`CreateCompanyDialog`) кеш уже
  инвалидирует, выход (`clearUserCache`) — чистит.
- Без роли `CompanyOwner` запрос не уходит: гость, клиент, мастер без своего салона и SuperAdmin без своего салона (П-4) не
  платят лишним запросом.
- Почему не новое поле в токене или `/auth/me`: роли в токене общие на все линии (`IdentityRoleSync`). Новое поле потребовало бы
  менять выдачу токена и его обновление после создания или передачи салона. Это авторизационная поверхность ради пункта меню.
  Видимость пункта — удобство, а не защита: `/billing` по-прежнему закрыт ролями, сервер отвечает 404 без аккаунта.

### §49.5.2 `Navbar.tsx`
- `const ownsSalon = useOwnsSalon()`.
- `NavLink to="/billing"` «Подписка» сразу после «Кабинет» — в широкой шапке (`navLinkClass`) и в мобильном меню
  (`mobileLinkClass`, `onClick={closeMenu}`). Активное состояние: `className={({ isActive }) => …}` с `text-gold-dark`;
  `aria-current="page"` `NavLink` ставит сам.
- Ширина 768–1280 px: контейнер широкой шапки `gap-7` → `gap-5 lg:gap-7`. На 768 px при максимальном наборе пунктов
  (владелец-SuperAdmin) шапка не переносится — M49-01.
- Прежние входы на `/billing` (профиль, `/pricing`, уведомления) не трогаются.

---

## §49.6. `/billing`: тарифы «Записи» (US-49-06, A49-4)

### §49.6.1 Бэкенд — `OwnerSubscriptionService.BuildAsync(account)` (ветка «Записи»)
- Список:
  ```csharp
  var availablePlans = (await db.SubscriptionPlanConfigs.AsNoTracking()
          .Where(p => p.Line == CompanyKind.Services && p.IsActive && p.IsPublic && !p.IsSystemTrial && !p.IsSystemFree)
          .OrderBy(p => p.SortOrder).ThenBy(p => p.PricePerMonth).ToListAsync())
      .Select(p => new AvailablePlanDto(p.Id, p.Name, p.PricePerMonth, p.Description,
          /* highlights: тот же split, что у «Заказов», Take(PricingCatalogBuilder.MaxHighlights) */,
          BillingTexts.ServicesPlanLimitsText(p.MaxCompanies, p.MaxEmployees)))
      .ToList();
  ```
  и в конструкторе ответа `Line: nameof(CompanyKind.Services), AvailablePlans: availablePlans`.
- `BillingTexts.ServicesPlanLimitsText(int? companies, int? employees)` — по образцу `OrdersPlanLimitsText`, склонение через
  `ShopTimeTexts.Plural`: «до N точки|точек|точек» / «точки без ограничения» · «до M сотрудника|сотрудников|сотрудников» /
  «сотрудники без ограничения». Слова «точка» и «сотрудник» — те же, что в `CompaniesTextFor`/`EmployeesTextFor` этого экрана.
- Оценка заявки: в этой ветке `BuildPendingRequestDto(account, …, currentPlanPrice: account.RequestedPlan?.PricePerMonth ?? planDto.PricePerMonth, …)`.
  Так «Ожидаемый итог» заявки на тариф считается от запрошенного тарифа, как и обещает комментарий в самом
  `BuildPendingRequestDto`. Ветки других линий и `AdminBillingController` не трогаются (§49.13 п. 3).
- `BillingController` не меняется: заявка на тариф «Записи» уже принимается (`PlanId`, проверка линии, блокировка аккаунта,
  перезапись, сигнал оператору цикла 48).

### §49.6.2 Фронт — `src/pages/BillingPage.tsx` (только `line === 'Services'`)
- `document.title` для «Записи» — «Ваша подписка — ezbook · Записи».
- Условие блока тарифов: `data.availablePlans?.length` (без `isLine`); заголовок для «Записи» — «Тарифы «Записи»». Разметка
  элемента — общая с другими линиями. Лучше вынести её в `src/components/billing/AvailablePlansBlock.tsx` с пропсами
  `title`, `plans`, `currentPlanId`, `pendingPlanId`, `canRequest`, `onRequest`, `pendingPlanIdInFlight`. `src/components` уже
  входит в сборку goods, список `goods-shared-sources.js` не меняется. Для «Заказов», «Домов», «Бань» блок рисуется как сейчас:
  без метки текущего и без подтверждения.
- Текущий: `data.status === 'Active' && p.planId === data.plan.id` → метка «Ваш тариф», без кнопки.
  Запрошенный: `same && pr.desiredPlanId === p.planId` → «Заявка отправлена», без кнопки.
- Подтверждение при пробном периоде: локальное состояние `confirmPlanId`. Если `trial?.planChangeNotice` не пуст, «Запросить»
  только выставляет `confirmPlanId`, под тарифом показываются плашка с notice (те же классы, что у опций), «Отправить заявку»
  и «Отменить».
- Сборка тела заявки — API §49.26.2. Мутация тарифа для «Записи» не передаёт `line`. Ветка для других линий
  (`line: isBaths ? 'Baths' : isStays ? 'Stays' : 'Orders'`, `options: []`) остаётся как есть.
- Карточка «Заявка на рассмотрении» при `!isLine && pr.desiredPlanName` — строка «Тариф: {desiredPlanName}» над списком опций.
- Типы: `src/api/billing.ts` — патч `OwnerSubscriptionCycle49Patch` (`availablePlans` для «Записи», `pendingRequest.line`) из
  `api-cycle49.generated.ts`. Генераты 19/24 не трогаются.

### §49.6.3 Почему тариф и опции не теряют друг друга
Сервер хранит одну заявку на аккаунт и при повторе перезаписывает её целиком (`RequestedPlanId` и `RequestedOptionsJson`).
Менять это на сервере — значит менять контракт цикла 7, админку и три других экрана. Клиент «Записи» сам переносит в новую
заявку то, что уже было в открытой той же линии. Заявка другой линии не переносится: сервер отвечает 409, текст сервера
печатается (как сейчас).

---

## §49.7. Отчёт и стоп перед выкатом (US-49-04, Q49-1, R49-1)

### §49.7.1 Отчёт
`deploy/checks/cycle49-catalog-report.sql` — только `SELECT`. Состав — API §49.29: итоги «сейчас / после / пропадут» и
список пропадающих (`name`, `slug`, причина), без контактов и ФИО. Если в `Companies` есть признак витринной компании —
отдельной колонкой (DO сверяет по снапшоту). Запуск — функция `check_cycle49_catalog` в `deploy/deploy-remote.sh` рядом с
`check_cycle48_slugs`: до любых изменений на хосте, fail-closed на ошибке `psql`, печать в лог на каждом выкате. Результат
devops-engineer прикладывает к отчёту выката, решение заказчика записывается в DEPLOY.md §36 (M49-05).
**Порядок для этого выката:** отчёт запускается вручную на бою (тот же файл через `docker compose exec -T postgres psql …`)
**до** выката, заказчик смотрит его и решает. Только потом запускается выкат.

### §49.7.2 Стоп
`deploy/checks/cycle49-catalog-empty.sql` возвращает строку, если `visible_now > 0 ∧ visible_after = 0`. Тогда
`deploy-remote.sh` отказывает с текстом «Новое правило каталога скрывает все салоны — нужно решение заказчика (DEPLOY.md §36)»,
пока в `.env` нет `CYCLE49_EMPTY_CATALOG_ACCEPTED=true`. Главная «Записи» при `total = 0` прячет блок каталога, поэтому такой
выкат заметен всем.

### §49.7.3 Согласие SQL с кодом
CY49-D1 — функциональный тест в `ServiceBooking.Tests`: засевает салоны по всем причинам, выполняет текст обоих SQL-файлов
через соединение тестовой базы. Затем сравнивает `slug`-множества: «после» = `db.Companies.VisibleInSalonCatalog(...)`;
«сейчас» = то же без двух `EXISTS`, то есть `Kind/IsActive/ShowInPublicListing` + `WhereAllowsPublicListing`.

---

## §49.8. Структура проекта — что меняется

```
ServiceBooking.API/
  Services/Shops/CatalogListingRules.cs        enum CatalogListingCheckCode: + NoActiveServices, NoServiceProviders (в конец)
  Services/Companies/SalonListingRules.cs      SalonListingInput (5 полей), пункты 2–3, HasServicesText, HasServiceProvidersText, ConditionsText
  Services/Companies/SalonListingQuery.cs      ActiveService, ServiceProvider, два EXISTS в VisibleInSalonCatalog
  Controllers/CompanyCatalogListingController.cs  BuildAsync с двумя AnyAsync, ConditionsText
  DTOs/Catalog/CatalogDtos.cs                  CatalogListingDto + ConditionsText (WhenWritingNull)
  DTOs/Companies/CompanyDto.cs                 только комментарий у PublicListingEnabled
  Services/Billing/OwnerSubscriptionService.cs ветка «Записи»: availablePlans, оценка заявки от запрошенного тарифа
  Services/Billing/BillingTexts.cs             ServicesPlanLimitsText
ServiceBooking.UnitTests/
  SalonListingRulesTests.cs                    таблица истинности 32 строки (CY49-B1)
  Cycle49/ServicesPlanLimitsTextTests.cs       CY49-B8
ServiceBooking.Tests/Tests/
  Cycle49CatalogListingTests.cs                CY49-B2…B5
  Cycle49BillingPlansTests.cs                  CY49-B6, B7
  Cycle49CatalogReportSqlTests.cs              CY49-D1 (владеет DO)
  Cycle31CatalogListingTests.cs, Cycle24TariffTests.cs, … — правки фикстур/ожиданий (§49.9.2)
contracts/cycle49/openapi.yaml, openapi.json   контракт цикла (json — генерат BE-49-0)
frontend/
  package.json                                 types:api:cycle49
  scripts/contracts-to-json.mjs                + 'cycle49'
  src/types/api-cycle49.generated.ts           генерат
  src/types/index.ts                           SalonCatalogListingDto → cycle49
  src/api/billing.ts                           OwnerSubscriptionCycle49Patch
  src/components/company/CatalogListingCard.tsx  conditionsText, ✗
  src/pages/owner/SalonCatalogListingSection.tsx refetchOnMount
  src/pages/owner/salonCatalogListingCache.ts  invalidateSalonCatalogListing (новый)
  src/pages/owner/CompanyManagePage.tsx        вызовы инвалидации в 6 мутациях
  src/hooks/useOwnsSalon.ts                    новый
  src/components/layout/Navbar.tsx             пункт «Подписка», gap
  src/components/billing/AvailablePlansBlock.tsx новый (вынос разметки, по желанию FE)
  src/pages/BillingPage.tsx                    «Записи»: блок тарифов, подтверждение, сборка заявки, title, строка тарифа в заявке
deploy/
  checks/cycle49-catalog-report.sql, cycle49-catalog-empty.sql   новые
  deploy-remote.sh                             check_cycle49_catalog
.github/workflows/ci.yml                       lint cycle49, contracts json, types cycle49
contracts/redocly.yaml                         комментарий-список + cycle49
DEPLOY.md §36, docs/owner.md, CHANGELOG.md, CURRENT_STATE.md, TEST_CATALOG.md, contracts/cycle36/test-areas.json
```
Не трогаются: `GoodsNavbar`, `DomNavbar`, `BaniNavbar`, `ShopsController`, `BillingController`, `AdminBillingController`,
`CompaniesController`, `CompanyDtoAssembler`, `SubscriptionResolver`, `PublicListingQuery`, БД.

---

## §49.9. Тесты

### §49.9.1 Автотесты цикла (CY49-*), атрибут `TestCase("CY49-…")` у C#

| ID | Где | Что проверяет | Задача |
|---|---|---|---|
| CY49-B1 | `UnitTests/SalonListingRulesTests.cs` | Таблица истинности 2⁵ = 32: `visible` ⇔ все 5 условий; порядок кодов; `SalonBlocked`/`NotAllowedByPlan` есть только при отказе; `NoActiveServices`/`NoServiceProviders`/`HiddenByOwner` есть всегда; тексты = константы; `ConditionsText` — точная строка П-2 | BE-49-1 |
| CY49-B2 | `Tests/Cycle49CatalogListingTests.cs` | Матрица CY31-05 расширенная: active × plan × show × service × provider (32 салона). Для каждого `visible` = присутствие в `GET /api/companies` = присутствие в `GET /api/companies/public` = «все done». CY31-05 остаётся со своим ID, его фикстура дополняется услугой | BE-49-1 |
| CY49-B3 | там же | Частные случаи US-49-01: (а) все услуги `IsActive=false` → нет в каталоге; (б) только `Client` с `ProvidesServices=true` → пункт 3 `done=false`, нет в каталоге; (в) владелец-одиночка, 1 активная услуга, нет `MasterService` → в каталоге; (г) у владельца галочка выключена, у мастера включена → в каталоге; (д) выключили последнюю услугу → пропал при следующем запросе, включили → появился; (е) `total` `GET /api/companies/public` с `cityId` и `search` считает только видимые; (ж) страница по slug отвечает 200 у скрытого салона | BE-49-1 |
| CY49-B4 | там же | Контракт: ответы GET и PUT салона проходят `OpenApiContract.Load("cycle49").AssertResponse`; `conditionsText` присутствует всегда; ответ магазина `GET /api/shops/{id}/catalog-listing` проходит `ShopCatalogListingDto` цикла 49 (поля `conditionsText` нет); PUT `showInCatalog=true` без услуг → 200, `visible=false`; чужой салон → 404, магазин на салонном маршруте → 409 `text/plain`, аноним → 401 | BE-49-1 |
| CY49-B5 | там же | Демо-салоны витрины видны в демо-каталоге (засев `Cycle28*`-генератором → в `GET /api/companies/public`) | BE-49-2 |
| CY49-B6 | `Tests/Cycle49BillingPlansTests.cs` | `GET /api/billing/subscription` «Записи»: в `availablePlans` нет пробного, бесплатного, неактивного, непубличного и чужой линии; порядок `SortOrder`→цена; `limitsText`; нет `maxHouses`/`maxResources`; ответ проходит `OwnerSubscriptionServicesDto` (cycle49); у `line=Orders/Stays/Baths` списки прежние | BE-49-3 |
| CY49-B7 | там же | `POST …/request {planId: <тариф «Записи»>}` → 200, `desiredPlanId`, `line=Services`; `GET` → `estimatedMonthlyPrice` = цена запрошенного тарифа + опции заявки; тариф «Заказов» без `line` → 400 «Этот тариф из другой линейки»; открыта заявка `Orders` → 409 `text/plain`; ответ проходит `SubscriptionRequestDto` (cycle49) | BE-49-3 |
| CY49-B8 | `UnitTests/Cycle49/ServicesPlanLimitsTextTests.cs` | 1/2/5/11/21 точек и сотрудников; `null` → «без ограничения»; разделитель « · » | BE-49-3 |
| CY49-F1 | `src/components/company/CatalogListingCard.test.tsx` | «✗» у невыполненного, «✓» у выполненного, `sr-only`; `listing-conditions` есть при `conditionsText` и стоит до списка; нет при `null`/отсутствии | FE-49-1 |
| CY49-F2 | `src/pages/owner/SalonCatalogListingSection.test.tsx`, `CompanyManagePage.test.tsx` | Повторный монтаж секции делает новый GET даже при свежем кеше; успешные `create/update/delete` услуги, `providesServices`, `add/remove` участника вызывают инвалидацию `['company-catalog-listing', id]` | FE-49-1 |
| CY49-F3 | `src/components/layout/Navbar.test.tsx` | Матрица §49.27.1 API (8 профилей) для широкой шапки и мобильного меню; без роли `CompanyOwner` нет запроса `/companies/my`; клик в мобильном меню закрывает меню; на `/billing` у пункта `aria-current="page"`; пункт стоит после «Кабинет» | FE-49-2 |
| CY49-F4 | `src/pages/BillingPage.test.tsx` | «Записи»: блок «Тарифы «Записи»»; «Ваш тариф» без кнопки при `Active`, с кнопкой при `Expired`; «Заявка отправлена»; без `canRequestChanges` кнопок нет; при `planChangeNotice` заявка уходит только после «Отправить заявку», notice виден до отправки; тело заявки на тариф несёт опции открытой заявки «Записи», тело заявки на опции несёт `planId` открытой заявки; заявка другой линии не переносится; строка «Тариф: …»; `document.title`. Прежние тесты «Заказов»/«Домов»/«Бань» зелёные без правок | FE-49-3 |
| CY49-F5 | CI «Generated API types must match» | `api-cycle49.generated.ts` совпадает со схемой; `tsc` всех приложений зелёный (enum goods не задет) | FE-49-0, DO-49-1 |
| CY49-D1 | `Tests/Cycle49CatalogReportSqlTests.cs` | §49.7.3: SQL-отчёт и стоп совпадают с `VisibleInSalonCatalog` и старым правилом; стоп возвращает строку только при «все пропали» | DO-49-2 |

### §49.9.2 Существующие тесты, которые меняются осознанно (R49-2)
Правило: **чинится фикстура** (салону «для каталога» добавляется активная услуга через `ApiTestBase.CreateServiceAsync`;
владелец с галочкой по умолчанию уже даёт пункт 3), **ожидания не ослабляются**. Кандидаты (поиск по `api/companies/public`,
`"/api/companies"`, `catalog-listing`): `Cycle31CatalogListingTests` (CY31-01…06: ожидаемые списки кодов дополняются
`NoActiveServices`, `NoServiceProviders` в правильных местах; `HaveCount(1)` → 3), `CompaniesTests`, `CompanyPhotosTests`,
`CompanyAddressTests`, `AdminTests`, `SchedulerTests`, `Cycle23ShopsCatalogTests`, `Cycle25CatalogTests`, `Cycle28ShowcaseGeneratorTests`,
`Cycle28ShowcaseGuardsTests`, `Cycle28DemoScenarioTests`, `Cycle28DemoContractTests`, `Cycle37IsolationTests`,
`Cycle42IsolationTests`, `Cycle35DemoScenarioTests`, `Cycle25ReportsTests`. Тест, который проверяет **отсутствие** салона в
каталоге по старой причине, остаётся как есть — он и так зелёный.
- `Cycle24TariffTests.cs:181` (`availablePlans` = `null` для «Записи») → «массив, без пробного и бесплатного». Комментарий
  `OwnerBillingDtos.cs:32` правится так же.
- `SalonListingRulesTests` (8 строк) заменяется CY49-B1.
- goods: `CatalogListingSection.test.tsx`, `CatalogListingCard.test.tsx` — «○» → «✗».
- `CatalogListingRulesTests` (магазин) — без правок.

### §49.9.3 Ручные проверки
M49-01…M49-05 — SPEC §9. Добавляются:
- **M49-06** — `EXPLAIN ANALYZE` `GET /api/companies/public` до и после (QA-49-1, §49.3.8);
- **M49-07** — на копии боя или на бою read-only: отчёт §49.7 совпадает с ручным подсчётом по двум случайным пропадающим салонам.

---

## §49.10. Задачи и параллельность

**Контракт готов:** `contracts/cycle49/openapi.yaml` и `API_CONTRACT_CYCLE49_LISTING_SUBSCRIPTION.md`. После коммита документов
(DO-49-0) все треки стартуют одновременно. FE работает по prism-моку `contracts/cycle49/openapi.yaml` (порт 4049), BE — по
схеме через `OpenApiContract.Load("cycle49")`. Общих файлов у BE и FE нет, кроме `frontend/scripts/contracts-to-json.mjs`
(BE-49-0) и `frontend/package.json` (FE-49-0): разные строки, конфликт слияния тривиален.

### §49.10.1 Backend (backend-developer)

| Задача | Приоритет | Содержание | Владеет файлами | Зависит от |
|---|---|---|---|---|
| **BE-49-0** | P0, первой (маленькая) | `'cycle49'` в `scripts/contracts-to-json.mjs`, генерат `contracts/cycle49/openapi.json` | `frontend/scripts/contracts-to-json.mjs`, `contracts/cycle49/openapi.json` | DO-49-0 |
| **BE-49-1** | P0 | §49.3: enum, `SalonListingRules`, `SalonListingQuery`, контроллер, DTO, комментарий `CompanyDto`. Проверка индексов (§49.2). CY49-B1…B4 | файлы §49.8 из `Services/Shops`, `Services/Companies`, `CompanyCatalogListingController.cs`, `DTOs/Catalog`, `DTOs/Companies/CompanyDto.cs`, `SalonListingRulesTests.cs`, `Cycle49CatalogListingTests.cs` | BE-49-0 (для CY49-B4) |
| **BE-49-2** | P0 | R49-2: прогон `dotnet test ServiceBooking.Tests`, починка фикстур по §49.9.2, CY49-B5. Только фикстуры и ожидания чек-листа, без ослабления | перечисленные в §49.9.2 тесты, `Cycle31CatalogListingTests.cs` | BE-49-1 |
| **BE-49-3** | P0 | §49.6.1: `availablePlans` «Записи», `ServicesPlanLimitsText`, оценка заявки; правка `Cycle24TariffTests` и комментария `OwnerBillingDtos`. CY49-B6…B8 | `OwnerSubscriptionService.cs`, `BillingTexts.cs`, `OwnerBillingDtos.cs` (комментарий), `Cycle49BillingPlansTests.cs`, `UnitTests/Cycle49/*`, `Cycle24TariffTests.cs` | BE-49-0 (для B6/B7). **Независима от BE-49-1** |

Один исполнитель: BE-49-0 → BE-49-1 → BE-49-3 → BE-49-2 (BE-49-2 последней: ловит всё, что сломали 49-1 и 49-3).

### §49.10.2 Frontend (frontend-developer)

| Задача | Приоритет | Содержание | Владеет файлами | Зависит от |
|---|---|---|---|---|
| **FE-49-0** | P0, первой | `types:api:cycle49` в `package.json`, генерат, `src/types/index.ts` (SalonCatalogListingDto), `src/api/billing.ts` (патч цикла 49). Поиск исчерпывающих `switch` по `CatalogListingCheckCode`/`code` во всех приложениях (R49-4) | `frontend/package.json`, `src/types/api-cycle49.generated.ts`, `src/types/index.ts`, `src/api/billing.ts` | DO-49-0 |
| **FE-49-1** | P0 | §49.4: `CatalogListingCard`, `SalonCatalogListingSection`, `salonCatalogListingCache.ts`, инвалидации в `CompanyManagePage.tsx`, правки тестов goods. CY49-F1, F2 | эти файлы и их тесты, `goods/src/components/CatalogListingSection.test.tsx` | FE-49-0 |
| **FE-49-2** | P0 | §49.5: `useOwnsSalon`, `Navbar` (пункт, `gap`), CY49-F3 | `src/hooks/useOwnsSalon.ts`, `src/components/layout/Navbar.tsx`, `Navbar.test.tsx` | — (контракт `/companies/my` прежний) |
| **FE-49-3** | P0 | §49.6.2: `BillingPage` «Записи», `AvailablePlansBlock`, CY49-F4 | `src/pages/BillingPage.tsx`, `src/components/billing/AvailablePlansBlock.tsx`, `BillingPage.test.tsx` | FE-49-0 |

FE-49-1, FE-49-2, FE-49-3 не пересекаются по файлам. Один исполнитель: FE-49-0 → FE-49-1 → FE-49-3 → FE-49-2.

### §49.10.3 DevOps (devops-engineer) — DO-трек

| Задача | Приоритет | Содержание | Владеет файлами | Зависит от |
|---|---|---|---|---|
| **DO-49-0** | P0, первой | Фиксация документов: этот файл, `API_CONTRACT_CYCLE49_LISTING_SUBSCRIPTION.md`, `contracts/cycle49/openapi.yaml` | — | — |
| **DO-49-1** | P0 | CI: `redocly lint` + `../contracts/cycle49/openapi.yaml`; «Contract JSON must match» + `cycle49`; «Generated API types must match» + `npm run types:api:cycle49` и файл в `git diff`; `contracts/redocly.yaml` — строка в комментарии | `.github/workflows/ci.yml`, `contracts/redocly.yaml` | пишется сразу; зелёный после BE-49-0, FE-49-0 |
| **DO-49-2** | P0 (условие выката) | §49.7: два SQL-файла, `check_cycle49_catalog` в `deploy-remote.sh`, CY49-D1 | `deploy/checks/cycle49-*.sql`, `deploy/deploy-remote.sh`, `ServiceBooking.Tests/Tests/Cycle49CatalogReportSqlTests.cs` | — (D1 зелёный после BE-49-1) |
| **DO-49-3** | P0 | `DEPLOY.md` §36 (§49.14) | `DEPLOY.md` | — |

### §49.10.4 QA, право, документация

| Задача | Содержание | Зависит от |
|---|---|---|
| **QA-49-1** | Полный прогон: `dotnet test` (оба набора; Testcontainers + colima — переменные окружения из памяти проекта), `npm run test:run`, `npm run lint`, все `tsc`, `redocly lint` cycle49. По желанию — `schemathesis` по `contracts/cycle49/openapi.yaml`. M49-06. Раздел «Цикл 49» в `TEST_CATALOG.md`, тест-области в `contracts/cycle36/test-areas.json`. M49-01…04 в браузере, если он есть у агента; иначе — «не проверено» | все BE/FE/DO |
| **L49-1** | legal-counsel: нужно ли описать новые условия показа в документах или описании тарифа (SPEC §7). Блокирует **выкат**, не код. Если юрист даст текст для интерфейса — правится константа `ConditionsText` или описание тарифа в админке, без изменения формы | — |
| **DOC-49-1** | `docs/owner.md`: «Каталог ezbook.ru» (5 условий, привязка к услуге не нужна), «Подписка» в шапке, тарифы «Записи» на экране подписки. `CHANGELOG.md`, `CURRENT_STATE.md` (§5.x цикла 49; долг C49-1 из §49.12) | QA-49-1 |
| **M49-05** | Отчёт §49.7 на бою до выката, решение заказчика записано | DO-49-2, до выката |

```
DO-49-0 ─┬─> BE-49-0 ─┬─> BE-49-1 ─┬─> BE-49-2 ─┐
         │            └─> BE-49-3 ─┘            │
         ├─> FE-49-0 ─┬─> FE-49-1 ──────────────┤
         │            └─> FE-49-3 ──────────────┼─> QA-49-1 ─> DOC-49-1 ─> человек: develop ─┐
         ├─> FE-49-2 ───────────────────────────┤                                          ├─> выкат ─> M49-01…04
         └─> DO-49-1, DO-49-2, DO-49-3 ─────────┘           L49-1 ─────────────────────────┤
                                                            M49-05 (отчёт на бою, решение) ┘
```

---

## §49.11. Безопасность, доступность, производительность

- **Права.** `catalog-listing` — владелец или SuperAdmin, чужой — 404 (без изменений). В чек-листе нет имён и названий. Пункт
  «Подписка» — удобство: `/billing` закрыт `ProtectedRoute` с ролями `CompanyOwner`/`SuperAdmin`, сервер без аккаунта отвечает
  404. Ответ `/companies/my` не расширяется. Заявка на тариф проходит те же проверки сервера (линия, активность, демо, блокировка).
- **ПДн.** Цикл ПДн не собирает. Отчёт выката — только `name`/`slug` салонов и причина.
- **Доступность.** ✓/✗ дублированы `sr-only`. «Подписка» — обычная ссылка с видимым фокусом и `aria-current`. Мобильная область
  нажатия ≥ 44 px (`mobileLinkClass`, при нехватке — `min-h-[44px]`). Кнопки «Запросить», «Отправить заявку» и «Отменить» — `min-h-[44px]`. Плашка `planChangeNotice` — текст до кнопки отправки.
- **Производительность.** §49.3.8 (каталог), §49.5.1 (шапка: один запрос на 5 минут и только для владельцев, общий кеш с кабинетом).
  `/billing` «Записи» — +1 запрос к `SubscriptionPlanConfigs` (единицы строк).

---

## §49.12. Риски

| # | Риск | Решение |
|---|---|---|
| R49-1 | Салоны пропадут из каталога сразу после выката | Отчёт до выката и решение заказчика (§49.7.1, M49-05); стоп при «пропали все» (§49.7.2) |
| R49-2 | Старые тесты создают салон «для каталога» без услуги | BE-49-2: чинить фикстуры, не ожидания (§49.9.2) |
| R49-3 | Правило и SQL-двойник разойдутся | Одни выражения на обе стороны (§49.3.2) + CY49-B2 (32 сочетания) + CY49-B3 |
| R49-4 | Расширение общего enum ломает goods | Схема магазина (cycle25/31) не меняется, генераты goods прежние; FE-49-0 ищет исчерпывающие `switch`; `tsc` всех приложений в CI |
| R49-5 | `CompanyOwner` ≠ «владелец салона» | `/companies/my` без `kind` (§49.5.1), CY49-F3 |
| R49-6 | SQL-отчёт разойдётся с кодом | CY49-D1 выполняет настоящий файл отчёта против `VisibleInSalonCatalog` |
| R49-7 | Заявка на тариф затрёт заявку на опции (и наоборот) | Перенос на клиенте (§49.6.3), CY49-F4 |
| R49-8 | Кто-то начнёт опираться на `publicListingEnabled` как на «в каталоге» | Комментарий в коде + описание в контракте (§49.3.6) |
| R49-9 | Шапка переполнится на 768 px | `gap-5 lg:gap-7`, M49-01 |
| R49-10 | Владелец, у которого все салоны заблокированы, не видит «Подписка» | Принято: `/companies/my` их не отдаёт; входы через профиль и уведомления остаются (§49.13 п. 5) |
| R49-11 | Текст L49-1 придёт после кода | Тексты — константы и описание тарифа в админке, форма не меняется |
| C49-1 (долг) | На экранах «Заказов», «Домов», «Бань» и в админке «Ожидаемый итог» заявки на тариф по-прежнему считается от текущей цены | В цикле не чинится (SPEC: экраны других линий не меняются). Записать в CURRENT_STATE §9 |

---

## §49.13. Отклонения от SPEC и уточнения

1. **US-49-06, состав списка.** SPEC: «активные тарифы линии, кроме пробного». Дополнительно исключены **непубличные**
   (`IsPublic = false`, «сняты с продажи», циклы 15/17) и **системный бесплатный** (`IsSystemFree`). Снятый с продажи тариф
   нельзя предлагать к новой заявке. Заявка на бесплатный тариф бессмысленна (счёт на 0 ₽, сигнал оператору «выставить счёт»):
   бесплатный уровень наступает сам. Сервер такие заявки по-прежнему принимает, их просто не предлагает экран.
2. **US-49-06, «текущий тариф помечен».** Текущим считается тариф подписки только при `status = Active`. При `Expired` у
   истёкшего тарифа есть «Запросить» — иначе продлить его с экрана было бы нельзя.
3. **US-49-06, «Заявка на рассмотрении».** На экране «Записи» «Ожидаемый итог» заявки на тариф считается от цены запрошенного
   тарифа, в карточке заявки видно название тарифа. Без этого заявка на тариф выглядела бы как пустая заявка с текущей ценой.
   Другие линии не меняются (C49-1).
4. **US-49-06, «заявки не теряют друг друга».** Достигается на клиенте (§49.6.3); серверная семантика «перезапись целиком» не
   меняется.
5. **US-49-05.** «Владелец хотя бы одного салона» = владелец хотя бы одного **активного** (не заблокированного) салона:
   так отвечает существующий `/companies/my`.
6. **US-49-04.** «Выкат останавливается» реализован механически (стоп в `deploy-remote.sh` + флаг в `.env`), а не только
   строкой в DEPLOY.
7. **US-49-02, коды.** Имена `NoActiveServices`/`NoServiceProviders` (SPEC имён не задавал).

---

## §49.14. Выкат и документация

**DEPLOY.md §36 (DO-49-3):**
- §36.1 Что выкатывается: новое правило каталога салонов, блок тарифов на `/billing`, пункт «Подписка». Без миграций и без
  новых переменных приложения.
- §36.2 Блокеры: (а) отчёт §49.7.1 запущен на бою вручную, результат передан заказчику, решение записано (M49-05);
  (б) ответ legal-counsel по L49-1; (в) при «пропадают все салоны» — `CYCLE49_EMPTY_CATALOG_ACCEPTED=true` только после
  письменного решения заказчика.
- §36.3 Как запустить отчёт вручную: команда `docker compose -f docker-compose.prod.yml --env-file .env exec -T postgres psql -U postgres -d servicebooking -v ON_ERROR_STOP=1 < deploy/checks/cycle49-catalog-report.sql`.
- §36.4 После выката: M49-01…04 на бою (шапка под владельцем салона и владельцем только магазина, блок каталога нового
  салона, заявка на тариф при пробном периоде — тестовым владельцем, затем отозвать).
- §36.5 Откат: `rollback.sh` на прежний образ, данные не менялись. Салоны вернутся в каталог по старому правилу.

Документация после QA — DOC-49-1 (§49.10.4).
