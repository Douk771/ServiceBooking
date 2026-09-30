# ARCHITECTURE — цикл 31 ServiceBooking: общий блок фото, мультизагрузка, блок «Каталог» у салона, компактная шапка карточки

**Разделы §31.0–§31.18**, контракт — `API_CONTRACT_CYCLE31.md` §31.20–§31.29. Нумерация — с префиксом цикла, как у
цикла 29 (A29-5). Сквозные номера циклов 26–27 и диапазон ветки `cycle/028-*` она не пересекает. В коде и документах
ссылаться с именем файла: `ARCHITECTURE_CYCLE31.md §31.5`.

**На входе:**
- корневой `SPEC.md` цикла 31 (Q-31-1…Q-31-10 приняты автономно по колонке «Решение»);
- `CURRENT_STATE.md` на `c19a83c` (§5.7 — фактическое устройство галереи, блока каталога и карточки; §6 — конвенции;
  §7.3 — CI; §9.1 — C31-1…C31-4, C29-1);
- код ветки `cycle/031-shared-photo-uploader` (= `develop` `c19a83c`), сверен по файлам, названным ниже.

Ветку подготовил devops-инженер, архитектор её не трогает. Корневой `SPEC.md` пока не закоммичен (untracked), его
коммит — не домен архитектора.

| Файл | Что в нём | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE31.md` (этот) | решения, механизмы, структура, задачи, параллельность, риски | все |
| `API_CONTRACT_CYCLE31.md` (§31.20–§31.29) | контракт словами: порядок проверок, тексты, смысл полей, лимиты | backend, frontend, QA |
| `contracts/cycle31/openapi.yaml` | **источник истины по форме** (OpenAPI 3.0.3): prism, `openapi-typescript`, schemathesis, redocly, C#-валидатор | backend, frontend, QA, CI |

Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` — документы цикла 3. По конвенции (`CURRENT_STATE.md` §6.5, §10.5) они
не перезаписываются: документы цикла лежат в корне с суффиксом.

---

## §31.0. Что это за цикл для архитектуры

Цикл в основном фронтовый: вёрстка двух общих компонентов, мультизагрузка, перекомпоновка карточки. На сервере меняется
немного, но в местах, где цена ошибки высокая:
1. **Новый маршрут** блока каталога салона и **одно правило видимости**, общее для чек-листа и двух запросов каталога
   (R-3).
2. **Разделение политики частоты** `uploads`, иначе пакет из 10 фото блокирует всё остальное (R-1).
3. **Один текст** в `CatalogListingRules`.

Следствия:
- **Стек не пересматривается.** Новых пакетов, миграций и переменных окружения нет. Два новых ключа конфигурации
  лимитов имеют значения по умолчанию в коде.
- **Работающее не переписывается.** Каждое изменение привязано к названной точке кода. Где спека оставляет выбор
  механизма (R-1…R-7, T-31-01), выбран вариант с наименьшей площадью изменений. Под каждый механизм есть тест, который
  упадёт, если механизм сломается.
- **Бэкенд и фронт связаны в двух точках**, обе закрыты контрактом до начала работ: новый маршрут
  `…/companies/{id}/catalog-listing` и текст пункта `HiddenByOwner`. Фронт работает против prism-мока, бэкенд — против
  схемы (§31.16).

---

## §31.1. Итог решений одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| — | Стек, зависимости, миграции | **Без изменений.** Ни NuGet, ни npm, ни миграций | §31.2 |
| R-1 | Лимит `uploads` против пакета из 10 фото | **Две новые политики:** `company-photos` (POST галереи, 20/мин на пользователя) и `company-photos-edit` (DELETE и PUT order, 60/мин). `uploads` остаётся для логотипа, аватара, заметок, услуг и товаров (10/мин). Пакетный маршрут не вводится | §31.6 |
| R-2 | Порядок при параллельной загрузке | **Параллельной загрузки нет.** Клиент шлёт файлы строго по одному в порядке выбора. Сервер не меняется | §31.6.3, §31.9 |
| R-3 | Одно правило видимости салона | Чистое правило `SalonListingRules.Evaluate` и SQL-двойник `SalonListingQuery.VisibleInSalonCatalog`. Двойником пользуются **оба** запроса каталога (`GetAll`, `GetPublic`). Соответствие правила и SQL доказывается матричным функциональным тестом | §31.5 |
| R-4 | Переключатель вне основной формы | `showInPublicListing` убирается из значений формы `SettingsTab`, поэтому поле не уходит в `PUT /api/companies/{id}`. Сервер это поле по-прежнему принимает (совместимость) | §31.10.3 |
| R-5 | Общий `CompanyCard` на двух сайтах | Перекомпоновка внутри `CompanyCard`, слот `children` сохраняется. `EmbedPage` использует только `CompanyMapLinks`, не карточку. Новый необязательный проп `className` у `CompanyMapLinks` по умолчанию ничего не меняет | §31.11 |
| R-6 | Нет браузерного e2e | Ручные кейсы `M31-`, guard-тест покрытия Tailwind (T-31-01) и vitest на наличие ключевых классов в разметке | §31.7, §31.14 |
| R-7 | Контракт цикла и JSON-копия | `contracts/cycle31/openapi.yaml` и `openapi.json`. Цикл **добавляет скрипт `contracts:json`** (для 26, 29, 31) и шаг CI, который сверяет JSON с YAML. Этим закрыт и C29-1 | §31.12 |
| T-31-01 | Защита сборки стилей goods | (1) `CompanyPhotosSection` переезжает в `src/components/company/`. (2) Список ezbook-страниц, которые импортирует goods, ведётся **в одном модуле** `frontend/goods-shared-sources.js`: из него строятся и `content` Tailwind goods, и регэксп ESLint. (3) Vitest-guard обходит граф импортов goods и падает, если общий файл вне сканируемых путей. Попутно найден и чинится C31-5: `NoticesPage` импортируется goods, но не сканируется Tailwind | §31.7 |
| Q-31-4 / US-31-03 | Плитка на 360 px и без мыши | Сетка `grid-cols-2 sm:grid-cols-3`. «Сделать обложкой» до `md` — иконка с доступным именем, с `md` — текст 10 px. Панель видна всегда на устройствах без hover и при фокусе внутри плитки | §31.8 |
| Q-31-8 / US-31-05 | Общий визуальный блок каталога | `src/components/company/CatalogListingCard.tsx` — чистая вёрстка на пропсах. Обёртки: goods `CatalogListingSection` (существующая) и ezbook `SalonCatalogListingSection` (новая) | §31.10 |
| Открытый вопрос SPEC §7 | Где слот «Открыто / Часы работы» | На `≥ sm` — **правая колонка** шапки, которая тянется на все строки: раскрытые часы растут вниз вдоль левой колонки и не сдвигают кнопки. На узком экране слот стоит под названием | §31.11 |

---

## §31.2. Стек, зависимости, миграции

**Без изменений** (`CURRENT_STATE.md` §1): .NET 8 / ASP.NET Core MVC / EF Core 8 / PostgreSQL 16; React 18 + Vite 5 +
Tailwind 3.4 + react-query 5 + react-hook-form 7; Vitest 3; openapi-typescript 7.13; @redocly/cli 2.54.

| Потребность | Не берём | Берём | Почему |
|---|---|---|---|
| Загрузка нескольких файлов | библиотеки аплоадеров (uppy, react-dropzone) | нативные `<input multiple>` и `DataTransfer.files`, последовательная очередь на `async/await` и axios `onUploadProgress` | Объём задачи — 10 файлов, одна зона. Прецедент в проекте — `NotePhotoUploader` |
| Сопоставить путь с glob в guard-тесте | micromatch, fast-glob (есть только транзитивно) | правило по префиксу каталога и точному пути файла (§31.7.3) | Нет новых зависимостей. Правило проще glob, и его легче объяснить |
| Показ панели без hover | `future.hoverOnlyWhenSupported` в `tailwind.config.js` | произвольный вариант `[@media(hover:hover)_and_(pointer:fine)]:` на одном элементе | Глобальный флаг меняет `hover:` на обоих сайтах целиком |
| e2e вёрстки | Playwright | ручные `M31-` (Q-31-10) | Решение спеки |

Миграций нет: сущности и таблицы не меняются. Новое значение enum `CatalogListingCheckCode.SalonBlocked` в БД не
хранится.

---

## §31.3. Модель данных и DTO

Сущности не меняются. Используемые поля: `Company.IsActive`, `Company.ShowInPublicListing`, `Company.Kind`,
`Company.BillingAccountId`, а также подписка и тариф через `SubscriptionResolver` / `PublicListingQuery`.

```csharp
// ServiceBooking.API/Services/Shops/CatalogListingRules.cs — enum дополняется В КОНЕЦ (значения — строки в JSON).
public enum CatalogListingCheckCode
{
    ShopBlocked, NoWorkingHours, NoPublishedProducts, NotAllowedByPlan, HiddenByOwner,
    SalonBlocked // Cycle 31 (ARCHITECTURE_CYCLE31.md §31.5): salon checklist only; the shop route never emits it.
}

// Без изменений формы — салонный маршрут отдаёт ТОТ ЖЕ record, что магазинный:
// DTOs/Catalog/CatalogDtos.cs
public sealed record CatalogListingDto(
    bool ShowInCatalog, bool AllowedByPlan, bool Visible, string StatusText, string? NotAllowedByPlanText, List<CatalogListingCheckDto> Checklist);
public sealed record CatalogListingInputDto(bool? ShowInCatalog);
// DTOs/Orders/OrderDtos.cs — CatalogConflictDto(CatalogConflictCode Code, string Message) переиспользуется для 409 «тариф».
```

Новых полей в существующих DTO нет. `CompanyDto` не меняется.

---

## §31.4. Текст пункта магазина (US-31-04, C31-2)

- `CatalogListingRules.HiddenByOwnerText = "Показ включен в настройках"`. Коды, `Done`, `Evaluate`, `StatusText`
  не меняются.
- `ServiceBooking.UnitTests/CatalogListingRulesTests.cs:26` — `InlineData` на новую строку. Дополнительно: при
  `ShowInPublicListing = true` и `false` текст пункта одинаковый, меняется только `Done`.
- Комментарий у константы (по-английски): текст описывает **условие**, а не состояние, как остальные пункты
  (`ARCHITECTURE_CYCLE31.md §31.4`).
- В `API_DOCUMENTATION.md` старой строки нет (проверено поиском). Нужна пометка «Цикл 31 (unreleased)» в описании
  `catalog-listing` магазина и новый абзац про маршрут салона (§31.16, BE-7).

---

## §31.5. Показ салона в каталоге: одно правило (US-31-05, R-3, C31-4)

### §31.5.1 Чистое правило — `ServiceBooking.API/Services/Companies/SalonListingRules.cs` (новый)

```csharp
namespace ServiceBooking.API.Services.Companies;

/// What SalonListingRules decides from — a plain value, identical for the checklist and (via its SQL twin) the catalog.
public sealed record SalonListingInput(bool IsActive, bool AllowedByPlan, bool ShowInPublicListing);

public static class SalonListingRules
{
    public const string SalonBlockedText = "Салон заблокирован администратором";
    public const string VisibleStatusText = "Салон виден в каталоге ezbook.ru";
    public const string HiddenStatusText = "Салона сейчас нет в каталоге";
    public const string NotAllowedByPlanHintText = "Показ в каталоге не входит в ваш тариф — повысьте тариф, чтобы включить";
    public const string MissingValueText = "Не указано, показывать ли салон в каталоге";
    // Item texts shared with the shop checklist — ONE copy each: CatalogListingRules.NotAllowedByPlanText,
    // CatalogListingRules.HiddenByOwnerText.

    /// Visible ⇔ active ∧ allowed by plan ∧ owner opted in. Checklist: SalonBlocked (only if !active, never done) →
    /// NotAllowedByPlan (only if !allowed, never done) → HiddenByOwner (always, done = ShowInPublicListing).
    public static CatalogListingResult Evaluate(SalonListingInput input);
    public static string StatusText(bool visible);
}
```

Форма результата — существующий `CatalogListingResult(Visible, Checklist)`. Как у магазина: `Visible ⇔ checklist.All(Done)`.

### §31.5.2 SQL-двойник — `ServiceBooking.API/Services/Companies/SalonListingQuery.cs` (новый)

```csharp
public static class SalonListingQuery
{
    /// The SQL twin of SalonListingRules.Evaluate(...).Visible: Kind = Services ∧ IsActive ∧ ShowInPublicListing ∧
    /// PublicListingQuery.WhereAllowsPublicListing(db, nowUtc). ⚠️ Changing one side without the other breaks CY31-05.
    public static IQueryable<Company> VisibleInSalonCatalog(this IQueryable<Company> companies, AppDbContext db, DateTime nowUtc);
}
```

**Места вызова (закрытый список):**
- `CompaniesController.GetAll`: вместо `Where(c => c.IsActive && c.ShowInPublicListing && c.Kind == Services)` плюс
  фильтра в памяти `.Where(c => plans[c.Id].AllowPublicListing)` — `db.Companies.AsNoTracking().VisibleInSalonCatalog(db,
  DateTime.UtcNow)`. `plans` по-прежнему нужен для `MapToDto`, фильтр в памяти удаляется.
- `CompaniesController.GetPublic`: вместо `Where(...) .WhereAllowsPublicListing(db, now)` — `.VisibleInSalonCatalog(db, now)`.
  Фильтры города и поиска, пагинация — без изменений.

**Цена правки `GetAll`:** тарифный фильтр переезжает из памяти в SQL, тот же, что уже работает в `GetPublic` с цикла 9.
Результат совпадает, пока `PublicListingQuery` согласован с `SubscriptionResolver`. Это согласование уже требует
комментарий `PublicListingQuery`, и теперь его ловит CY31-05: чек-лист считает тариф через `SubscriptionResolver`,
каталог — через SQL. Существующие тесты каталога (циклы 9, 23) должны пройти без правок ожиданий. Если какой-то из них
упадёт, это расхождение `PublicListingQuery` с резолвером, а не ошибка рефакторинга: чинить двойник, не тест.

### §31.5.3 Маршрут — `ServiceBooking.API/Controllers/CompanyCatalogListingController.cs` (новый)

Отдельный контроллер, по прецеденту `CompanyPhotosController` (саб-ресурс со своими маршрутами). В `CompaniesController`
(около 500 строк) не кладём.

```csharp
[ApiController]
[Route("api/companies/{id:guid}/catalog-listing")]
[Authorize]
public class CompanyCatalogListingController(AppDbContext db, SubscriptionResolver subscriptionResolver) : ControllerBase
{
    [HttpGet]                         public Task<ActionResult<CatalogListingDto>> Get(Guid id, CancellationToken ct);
    [HttpPut] [RequiresOwnerTerms]    public Task<ActionResult<CatalogListingDto>> Put(Guid id, CatalogListingInputDto input, CancellationToken ct);
}
```

Порядок действий (контракт §31.21–§31.22):
1. `var company = await db.Companies.FindAsync([id], ct)`. Если `company is null` или
   `!await CompanyAccess.CanManageCompanyAsync(db, User, id)` → `NotFound()`. Один и тот же ответ, не оракул.
2. `CompanyKindGuard.RejectShop(company.Kind)` → 409 строкой. Это вызов закрытого списка §389.2: дописать маршрут в
   комментарий `CompanyKindGuard` («salon-only routes»).
3. PUT: `input?.ShowInCatalog is not { } show` → `BadRequest(SalonListingRules.MissingValueText)`.
4. PUT: `var allowed = (await subscriptionResolver.GetEffectivePlanAsync(id)).AllowPublicListing`. Если `show && !allowed` →
   `Conflict(new CatalogConflictDto(CatalogConflictCode.CatalogListingNotAllowedByPlan, SalonListingRules.NotAllowedByPlanHintText))`.
5. PUT: `company.ShowInPublicListing = show; await db.SaveChangesAsync(ct)`. Advisory lock не нужен: пишется одна
   колонка без check-then-act по другим строкам.
6. Сборка ответа (общий приватный метод): `allowed` через `SubscriptionResolver.GetEffectivePlanAsync`, затем
   `SalonListingRules.Evaluate(new(company.IsActive, allowed, company.ShowInPublicListing))` и
   `new CatalogListingDto(show, allowed, verdict.Visible, StatusText, allowed ? null : NotAllowedByPlanHintText, checklist)`.

Лимита частоты нет, как у маршрута магазина: GET дешёвый, PUT закрыт правами.

### §31.5.4 Что не меняется

- Правило видимости (вариант A, Q-31-1): новых условий (услуги, мастера) нет.
- `PUT /api/companies/{id}` и поле `ShowInPublicListing` в `UpdateCompanyDto` (§31.23 контракта).
- `CompanyDto.PublicListingEnabled` (= опция ∧ тариф, без `IsActive`). Это поле кабинета, не каталога, его не трогаем.

---

## §31.6. Галерея на сервере: лимиты частоты (R-1) и порядок (R-2)

### §31.6.1 Политики — `ServiceBooking.API/Startup/RateLimitingExtensions.cs`

```csharp
// Cycle 31 (ARCHITECTURE_CYCLE31.md §31.6): the gallery leaves the shared "uploads" window. A 10-photo batch must
// not starve "make cover"/delete (C31-3), and gallery traffic must not starve logo/avatar/product uploads either.
o.AddPolicy("company-photos", ctx => UserWindowPolicy(ctx, "company-photos", defaultPermitLimit: 20, defaultWindowMinutes: 1));
o.AddPolicy("company-photos-edit", ctx => UserWindowPolicy(ctx, "company-photos-edit", defaultPermitLimit: 60, defaultWindowMinutes: 1));
```

- `OnRejected`: добавить явную ветку `"uploads" or "company-photos" or "company-photos-edit" => "Too many uploads. Try
  again in a minute."`. Сейчас `uploads` обслуживается веткой `_`. Явная ветка страхует от того, что кто-то поменяет
  значение по умолчанию и незаметно сменит тело галереи. Текст тот же.
- `CompanyPhotosController`: `Upload` → `[EnableRateLimiting("company-photos")]`, `Delete` и `Reorder` →
  `[EnableRateLimiting("company-photos-edit")]`. Остальные шесть мест `uploads` не трогать.
- `appsettings.json` → `RateLimits`: `"company-photos": { "PermitLimit": 20, "WindowMinutes": 1 }`,
  `"company-photos-edit": { "PermitLimit": 60, "WindowMinutes": 1 }` (документирующие значения, совпадают с кодом).
- `appsettings.Testing.json` → те же ключи с `10000`, как у всех политик: иначе остальные функциональные тесты начнут
  ловить 429.
- `.env.production.example` и `docker-compose.prod.yml` не меняются: значения по умолчанию в коде.

**Почему не пакетный маршрут.** Пакет потребовал бы `RequestSizeLimit` 50 МБ на запрос, ответа с ошибками по каждому
файлу (новый формат ошибок в проекте, где 4xx — голая строка), обработки до 10 изображений SkiaSharp в одном запросе
(пик памяти) и частичного коммита. Две политики решают критерий «10 фото и сразу обложка без 429» без нового формата.

**Почему не только «клиент ждёт».** Последовательная клиентская загрузка с паузами упёрлась бы в 10/мин: пакет из 10
фото — минута ожидания, а обложка — ещё минута.

**Бюджет (критерий US-31-02):** пустая галерея, 10 фото — 10 из 20 окна `company-photos`. «Сделать обложкой» и
удаление — 2 из 60 окна `company-photos-edit`. 429 нет. В запасе ещё 10 загрузок в ту же минуту (повторы после ошибок,
US-31-08).

### §31.6.2 Что ослабляется (NFR «Безопасность», назвать в CHANGELOG)

Суммарный бюджет обработки изображений на пользователя: было 10/мин, стало 10 (`uploads`) + 20 (галерея). Остальные
барьеры галереи не меняются (контракт §31.25). Злоупотребление ограничено ещё и лимитом 10 фото на компанию: загружать
больше 10 новых фото в одну компанию нельзя, а удаление требует прав владельца.

### §31.6.3 Порядок новых фото (R-2)

Сервер не меняется: `Position = count` под `pg_advisory_xact_lock("company-photos:{id}")`. Порядок гарантирует клиент:
следующий файл отправляется только после ответа на предыдущий (§31.9). Параллельные запросы из одной вкладки не
поддерживаются намеренно. Если позже понадобится параллельность, порядок придётся передавать явно (поле позиции в
форме) — это отдельный цикл.

---

## §31.7. Сборка стилей goods видит общие компоненты (T-31-01, C31-1)

### §31.7.1 Переезд галереи

`frontend/src/pages/owner/CompanyPhotosSection.tsx` → **`frontend/src/components/company/CompanyPhotosSection.tsx`**
(`git mv`, вместе с `CompanyPhotosSection.test.tsx`). Путь `src/components/**` уже сканируют **оба** конфига Tailwind.
Это устраняет C31-1 по построению.
- Импорты внутри файла: `../../api/…`, `../ui/…`, `../../hooks/…`, `../../utils/…`, `../../store/…`. Только
  относительные (C26-2).
- ezbook `CompanyManagePage.tsx`: `import { CompanyPhotosSection } from '../../components/company/CompanyPhotosSection'`.
- goods `SettingsPage.tsx`: `import { CompanyPhotosSection } from '@/components/company/CompanyPhotosSection'`.
- Реэкспорта со старого пути **нет**: второй путь к тому же компоненту — это то, от чего цикл уходит.
- Цена: два импорта, путь теста, строка в ESLint-списке. Риск — только ошибка пути, её ловят `tsc` и `build:release`.

### §31.7.2 Единый список общих ezbook-модулей — `frontend/goods-shared-sources.js` (новый) + `goods-shared-sources.d.ts`

```js
// ARCHITECTURE_CYCLE31.md §31.7 — the ONE list of ezbook sources goods is allowed to import (ESLint) and that the goods
// Tailwind build scans (content). Both configs are generated from here; the guard test (§31.7.3) proves the list is complete.
export const GOODS_SHARED_EZBOOK_PAGES = [
  'LegalDocumentPage', 'SubjectRequestPage', 'ConsentsPage', 'LoginPage', 'RegisterPage',
  'NoticesPage', 'BillingPage', 'owner/NotificationsSection',
] // paths under src/pages, no extension
export const GOODS_SCANNED_EZBOOK_DIRS = ['components', 'utils', 'hooks'] // under src/, scanned by goods Tailwind
export const GOODS_MARKUP_FREE_EZBOOK_DIRS = ['api', 'store', 'types'] // under src/, must contain no .tsx
export function goodsTailwindContent() { /* ['./goods/index.html', './goods/src/**/*.{js,ts,jsx,tsx}', ...dirs, ...pages] */ }
export function goodsAllowedPagesRegex() { /* the current eslint regex, built from GOODS_SHARED_EZBOOK_PAGES */ }
```

- `tailwind.goods.config.js`: `content: goodsTailwindContent()`.
- `eslint.config.js`: регэксп и сообщение `no-restricted-imports` для `goods/src/**` строятся из списка.
  `owner/CompanyPhotosSection` из списка **уходит**: он больше не страница.
- **C31-5 (новая находка при проектировании, ✔ по коду):** `goods/src/GoodsApp.tsx` импортирует `@/pages/NoticesPage`,
  ESLint его разрешает, но в `content` `tailwind.goods.config.js` его нет. Это тот же класс дефекта, что C31-1.
  Генерация `content` из списка чинит его автоматически.
- Добавление `src/utils/**` и `src/hooks/**` в `content` goods: утилиты, достижимые из goods, могут возвращать строки
  классов. Цена — несколько лишних классов в CSS goods, их объём пренебрежимо мал.
- `.d.ts` рядом нужен, чтобы TS-тест импортировал модуль без `allowJs`.

### §31.7.3 Guard-тест — `frontend/goods/src/sharedSources.guard.test.ts` (новый, vitest, `node:fs`)

1. Корни — все не-тестовые `*.ts/*.tsx` в `frontend/goods/src/`.
2. Обход графа статических и динамических импортов (`import … from '…'`, `export … from '…'`, `import('…')`,
   `import '…'`). Разрешение: `@/` → `frontend/src/`, `@goods/` → `frontend/goods/src/`, `./` и `../` — относительно
   файла. Расширения `''`, `.ts`, `.tsx`, `/index.ts`, `/index.tsx`. Пакеты и не-JS ресурсы (`.css`, `.svg`)
   пропускаются.
3. Каждый достигнутый файл внутри `frontend/src/` обязан попасть в одно из правил:
   - лежит в `src/{GOODS_SCANNED_EZBOOK_DIRS}/…`;
   - равен `src/pages/{GOODS_SHARED_EZBOOK_PAGES}.tsx`;
   - лежит в `src/{GOODS_MARKUP_FREE_EZBOOK_DIRS}/…` **и** имеет расширение `.ts`.

   Иначе тест падает с сообщением: «`<путь>` импортируется goods, но не сканируется `tailwind.goods.config.js` —
   перенесите его в `src/components/` или впишите в `frontend/goods-shared-sources.js`».
4. Отдельные `it`:
   - каждый элемент `GOODS_SHARED_EZBOOK_PAGES` существует как файл;
   - в `GOODS_MARKUP_FREE_EZBOOK_DIRS` нет ни одного `.tsx`;
   - `default export` из `tailwind.goods.config.js` имеет `content`, глубоко равный `goodsTailwindContent()`: ловит
     ручную правку конфига в обход списка.
5. Если на первом прогоне тест найдёт другие файлы верхнего уровня `src/` (например, `src/queryClient.ts`), их
   вписывают в список явно, отдельной константой `GOODS_SHARED_EZBOOK_FILES`. Решает фронтенд по факту прогона.

Этот тест — единственная автоматическая защита от повторения C31-1. Вид плитки подтверждают только `M31-01…03` (R-6).

---

## §31.8. Плитка галереи (US-31-01, US-31-03, Q-31-4)

Эталон — текущий вид ezbook на десктопе. Меняется только то, что нужно для узкого экрана и доступа без мыши.

| Элемент | Классы / поведение |
|---|---|
| Сетка | `grid grid-cols-2 sm:grid-cols-3 gap-3`, скелетон — так же |
| Бейдж «Обложка» | без изменений: `absolute top-1.5 left-1.5 text-[10px] font-medium bg-ink text-cream px-2 py-0.5 rounded-full` |
| Панель | `absolute inset-x-0 bottom-0 flex items-center justify-center gap-1 p-1.5 bg-gradient-to-t from-ink/70 to-transparent rounded-b-xl transition-opacity motion-reduce:transition-none opacity-100 [@media(hover:hover)_and_(pointer:fine)]:opacity-0 group-hover:opacity-100 group-focus-within:opacity-100` |
| Стрелки, «Удалить» | без изменений (`w-6 h-6`, 24 px, `aria-label`) |
| «Сделать обложкой» | `aria-label="Сделать обложкой"` всегда. До `md` — только `Icon name="star-outline" size={12}` (`md:hidden`), с `md` — текст `<span className="hidden md:inline">Сделать обложкой</span>`. Класс кнопки `h-6 min-w-6 px-1.5 inline-flex items-center justify-center rounded-full bg-white/90 text-[10px] font-medium disabled:opacity-40`. Шрифт 10 px, не 9 (SPEC) |

**Почему именно так (расчёт, проверяется в `M31-02`):**
- 360 px, 3 колонки: ширина плитки около 85 px. Четыре кнопки с текстом — около 170 px. Не помещаются.
- 360 px, 2 колонки: плитка около 134 px, кнопки иконками — 4 × 24 + 3 × 4 = 108 px. Помещаются.
- С `md` (768 px) три колонки дают плитку не меньше 180 px, текстовая кнопка помещается.
- `sm` (640–767 px): три колонки, кнопки иконками, плитка около 179 px.

**Специфичность:** медиа-вариант даёт селектор `(0,1,0)` внутри `@media`, `group-hover:` и `group-focus-within:` —
`(0,2,0)` и выше. Поэтому наведение и фокус показывают панель и на устройствах с мышью.

**Результат:**
- на телефоне и планшете (нет hover) панель видна всегда;
- на десктопе панель появляется при наведении, как раньше;
- при Tab на кнопку панели — `focus-within` — она становится видимой (US-31-03).

Цели касания на плитке — 24 px с промежутком 4 px, этого требует NFR. На телефоне плитка из двух колонок, так что
промежутки не сжимаются.

---

## §31.9. Мультизагрузка на клиенте (US-31-02, US-31-07, US-31-08)

### §31.9.1 Чистая логика — `frontend/src/utils/photoBatch.ts` (новый)

```ts
export const PHOTO_MAX_PHOTOS = 10            // заменяет локальную MAX_PHOTOS в компоненте (дубль сервера, C31-3 — остаётся одна копия на фронте)
export const PHOTO_MAX_BYTES = 5 * 1024 * 1024
export const PHOTO_ACCEPT = 'image/jpeg,image/png,image/webp'
export type PhotoRejectReason = 'type' | 'size'
export interface PhotoBatchPlan {
  accepted: File[]                                          // в порядке выбора, не больше remainingSlots
  rejected: { file: File; reason: PhotoRejectReason }[]     // отсев до отправки — строка статуса у файла
  overLimit: File[]                                         // не поместились — ОДНО сообщение
}
export function planPhotoBatch(files: readonly File[], remainingSlots: number): PhotoBatchPlan
export function photoRejectText(reason: PhotoRejectReason): string
//   'size' → 'Файл больше 5 МБ'; 'type' → 'Формат не поддерживается: нужен JPEG, PNG или WEBP'
export function overLimitText(files: readonly File[]): string
//   `Не добавлено ${n} фото: в галерее не больше 10 фото` + ': ' + имена через запятую
export function isTransientUploadError(err: unknown): boolean  // 429 | нет response | status >= 500
```

Правила:
- Тип: `file.type` из списка. Если `file.type === ''`, проверяется расширение `.jpg/.jpeg/.png/.webp` без учёта
  регистра. Размер: `file.size > PHOTO_MAX_BYTES` → `size`.
- Сначала отсев по типу и размеру, потом обрезка по остатку. Отсеянные файлы места не занимают.
- `remainingSlots = max(0, PHOTO_MAX_PHOTOS − photos.length)` берётся из кэша галереи на момент старта пакета.

### §31.9.2 Очередь — `frontend/src/components/company/usePhotoBatchUpload.ts` (новый хук)

```ts
type PhotoUploadStatus = 'queued' | 'uploading' | 'done' | 'error'
interface PhotoUploadItem { key: string; name: string; status: PhotoUploadStatus; error?: string; progress?: number; transient?: boolean }
export function usePhotoBatchUpload(companyId: string, opts: {
  onUploaded: (photo: CompanyPhoto) => void   // upsert в кэш по id
  onBatchSettled: () => void                  // invalidate + onChanged, один раз на пакет
}): {
  items: PhotoUploadItem[]; overLimitMessage: string | null; running: boolean
  done: number; total: number                 // «Загружено N из M»: M — число отправляемых файлов
  start: (files: File[], remainingSlots: number) => void
  retryFailed: () => void                     // US-31-08: только transient
  clear: () => void
}
```

- Цикл `for … of` с `await companyPhotosApi.upload(...)`: **строго по одному**, в порядке выбора (R-2).
- Ошибка — `getUploadErrorMessage(err)` у файла, `transient = isTransientUploadError(err)`, цикл продолжается.
- Прогресс (US-31-07): `companyPhotosApi.upload(companyId, file, onProgress?)`. Необязательный третий параметр
  прокидывается в axios `onUploadProgress` как `Math.round(loaded / total * 100)`. Существующие вызовы не меняются.
- Размонтирование во время пакета: флаг `mounted` в ref. После размонтирования следующий файл не стартует, `setState`
  не вызывается. Текущий запрос не отменяется: отмена и загрузка в фоне — вне цикла (SPEC §3).
- Новый `start` при `running` игнорируется (кнопка выбора и зона в это время недоступны). Список прошлого пакета
  заменяется новым (критерий «до закрытия или новой загрузки»).

### §31.9.3 Изменения в `CompanyPhotosSection`

- `<input type="file" multiple accept={PHOTO_ACCEPT} aria-describedby="company-photo-people-notice">`. Берутся все
  `e.target.files` и `e.dataTransfer.files` (`Array.from`), `e.target.value = ''` — как сейчас.
- Кнопка: «Выбрать фото» (было «Выбрать файл»), `loading={batch.running}`.
- `busy = batch.running || removeMut.isPending || reordering`. Стрелки, «Сделать обложкой», «Удалить» и зона выбора
  недоступны на время пакета.
- `onUploaded(photo)`: `qc.setQueryData(['company-photos', companyId], (old) => upsertById(old, photo))`. Порядок —
  по `position`, повтор с тем же `id` не добавляется. Счётчик `N / 10` и плитки растут по мере загрузки.
- `onBatchSettled()`: существующий `invalidate()` (`['company-photos', id]`, `['company']`, `onChanged?.()` —
  goods сбрасывает `['storefront']`). Один раз на пакет, а не на файл.
- Под зоной — панель статуса пакета, если `items.length > 0` или есть `overLimitMessage`:
  - `<p role="status" aria-live="polite">Загружено {done} из {total}</p>` — меняется только на завершении файла. На
    проценты объявления нет;
  - `<ul>` без `aria-live`: имя файла (`break-all`, до 2 строк) и статус «в очереди» / «загружается» (+ `NN %` или
    полоса `<div role="presentation">`, US-31-07) / «готово» / «ошибка: {текст}» (`text-danger`);
  - `overLimitMessage` — одной строкой `text-danger`;
  - кнопки «Повторить неудавшиеся» (только если есть `transient`-ошибки и пакет не идёт, US-31-08) и «Скрыть» (`clear`).
- Правовая подсказка `CompanyPhotoPeopleNotice` и диалог SuperAdmin — без изменений. Прежняя строка `error` остаётся
  только для удаления и перестановки.
- Превью выбранных файлов **не делаем**: `URL.createObjectURL` не создаётся, освобождать нечего (NFR
  «Производительность»).

---

## §31.10. Блок «Каталог» — общий визуальный компонент и блок салона (US-31-05, R-4)

### §31.10.1 `frontend/src/components/company/CatalogListingCard.tsx` (новый, чистая вёрстка)

```ts
export interface CatalogListingView {
  showInCatalog: boolean; allowedByPlan: boolean; visible: boolean; statusText: string
  notAllowedByPlanText?: string | null; checklist: { code: string; text: string; done: boolean }[]
}
export function CatalogListingCard(props: {
  title: string              // 'Каталог goods.ezbook.ru' | 'Каталог ezbook.ru'
  switchLabel: string        // 'Показывать магазин в каталоге goods.ezbook.ru' | 'Показывать салон в каталоге ezbook.ru'
  headingId: string
  headingAs?: 'h2' | 'h3'    // goods h2 (как сейчас), ezbook h3 (соседние карточки SettingsTab — h3); вид одинаковый
  data?: CatalogListingView
  isLoading: boolean
  loadError?: string | null  // текст готовит обёртка своим маппером
  onRetry: () => void
  saving: boolean
  saveError?: string | null
  onToggle: (next: boolean) => void
}): JSX.Element
```

- Разметка и `data-testid` переносятся из `goods/src/components/CatalogListingSection.tsx` **как есть**:
  `role="region"` + `aria-labelledby`, `input type="checkbox" role="switch"` внутри `label` (доступное имя —
  `switchLabel`), `listing-not-allowed`, `listing-status` (`role="status"`), `listing-check` с `data-done`, `✓/○`
  `aria-hidden` + `sr-only` «— выполнено / — не выполнено».
- Загрузка и ошибка рисуются примитивами `src/components/ui` (скелетон `h-14 bg-cream-deep animate-pulse rounded-xl`,
  ошибка `text-danger` + `Button size="sm" variant="secondary"` «Повторить»). goods-компоненты `StatePanels`
  недоступны из `src/` (ESLint). Состояния загрузки и ошибки в goods визуально немного изменятся (§31.18, О-31-6).
- Импортов из `goods/` нет. Файл в `src/components/**`, поэтому его классы видят оба конфига Tailwind.
- **Откат переключателя при ошибке** (US-31-05): контролируемый `checked={data.showInCatalog}`, то есть всегда
  серверное значение. Оптимистичного обновления нет, на время `saving` переключатель недоступен. При ошибке обёртка
  перечитывает запрос. Так ведёт себя и блок магазина.

### §31.10.2 Обёртки

- goods `CatalogListingSection.tsx` — остаётся (экспорт, путь, 4 теста), внутри рендерит `CatalogListingCard`,
  данные — `catalogListingApi`, ошибки — `getGoodsErrorMessage`.
- ezbook **`frontend/src/pages/owner/SalonCatalogListingSection.tsx`** (новый):
  - API — **`frontend/src/api/companyCatalogListing.ts`** (новый): `get(companyId)`,
    `put(companyId, showInCatalog)` → `CatalogListingDto` из генерата цикла 31;
  - ключ запроса `['company-catalog-listing', companyId]`, `retry: false`;
  - PUT `onSuccess`: `setQueryData(key, dto)` + `invalidateQueries(['my-companies'])` (`CompanyDto.showInPublicListing`
    у других экранов). `onError`: `invalidateQueries(key)`;
  - ошибки — новый маппер **`frontend/src/utils/catalogListingError.ts`**:
    - `response.data` — объект со строковым `message` → `message` (409 «тариф»);
    - `response.data` — непустая строка при 400/409 → строка;
    - 404 → «Компания не найдена»;
    - иначе `fallback` («Не удалось загрузить настройку каталога.» / «Не удалось сохранить настройку каталога.»).

### §31.10.3 `SettingsTab` (`frontend/src/pages/owner/CompanyManagePage.tsx`)

- Из `values` формы удаляется `showInPublicListing`, из fieldset «Запись» — блок чекбокса «Показывать компанию в общем
  списке» и подпись про тариф (строки около 983–998). `updateMut.mutate({...d, …})` поэтому больше не содержит
  `showInPublicListing` (R-4). Серверная часть и тип `UpdateCompanyDto` в `api/companies.ts` не трогаются.
- `<SalonCatalogListingSection companyId={companyId} />` — отдельной карточкой **сразу после** карточки основной формы,
  перед `CompanyPhotosSection`. Отступы — как у соседних карточек.
- `CabinetPage` (создание компании) оставляет свой чекбокс: это вне цикла.

---

## §31.11. Компактная шапка `CompanyCard` (US-31-06, R-5)

### §31.11.1 Порядок в DOM (для чтения скринридером и тестов)

`[галерея?]` → логотип → `h1` → описание? → слот `children`? → группа действий? → строка сведений?

Контейнер ряда — CSS grid. Визуальную раскладку задают классы, порядок DOM не меняется:

| Элемент | `< sm` (360 px) | `≥ sm` |
|---|---|---|
| сетка ряда | `grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3 px-4 pb-5 pt-5` | `sm:grid-cols-[auto_minmax(0,1fr)_auto] sm:gap-x-5 sm:px-6 sm:pb-6 sm:pt-7` |
| логотип | `col-start-1 row-start-1` (+ `-mt-[52px]` при галерее, как сейчас) | `sm:row-span-4` |
| `h1` | `col-start-2 row-start-1 self-center`, `text-[24px] sm:text-[30px] break-words` | то же |
| слот | `col-start-2` под `h1` | `sm:col-start-3 sm:row-start-1 sm:row-span-4 sm:max-w-[280px] sm:justify-self-end`: правая колонка на всю высоту, раскрытые часы растут вниз и не двигают левую колонку |
| описание | `col-span-2` | `sm:col-start-2 sm:col-span-1`, `max-w-[520px]` |
| действия | `col-span-2 flex flex-wrap items-center gap-2` | `sm:col-start-2` |
| сведения | `col-span-2 flex flex-col gap-1` | `sm:col-start-2 sm:flex-row sm:flex-wrap sm:gap-x-4 sm:gap-y-1` |

Классы — ориентир. Фронтенд вправе поменять конкретные утилиты, если выполнено:
- порядок DOM сохранён;
- на 1280 px у `/myasnoy` блок контактов занимает не больше 2 строк;
- на 360 px нет горизонтальной прокрутки;
- пустых обёрток нет.

### §31.11.2 Группа действий (`data-testid="company-card-actions"`)

- Рендерится, только если есть телефон **или** хоть одна ссылка карт.
- Телефон — ссылка `tel:` (или `span`, если `telHref` вернул `null`) с классом **`COMPANY_ACTION_LINK_CLASS`**.
  `aria-label={`Позвонить ${formatPhone(phone)}`}` сохраняется.
- `COMPANY_ACTION_LINK_CLASS` — новый файл `frontend/src/components/company/companyActionLink.ts`. Строка классов
  ссылок карт переносится туда **дословно** и дополняется фокус-обводкой `focus-visible:outline focus-visible:outline-2
  focus-visible:outline-gold focus-visible:outline-offset-2`. `CompanyMapLinks` использует ту же константу: одна
  высота (44 px) и один стиль у телефона и карт.
- `CompanyMapLinks` получает необязательный проп `className?: string`, который дописывается к обёртке
  `flex flex-wrap items-center gap-1`. В шапке передаётся `className="contents"`: ссылки становятся элементами общей
  flex-строки и переносятся вместе с телефоном. `EmbedPage` и `OrderPage` пропа не передают, их раскладка не меняется.
  Доступные имена, `href`, `target`, `rel`, отсутствие логотипов не трогаются. `CompanyMapLinks.test.tsx` — без правок
  ожиданий.

### §31.11.3 Строка сведений (`data-testid="company-card-meta"`)

- Рендерится, только если есть адрес **или** e-mail.
- Адрес: `map-pin` + `publicAddress(...)`. E-mail: ссылка `mailto:` + `mail`. У обоих `min-w-0 break-words`,
  `text-[13.5px] text-ink-soft`.

### §31.11.4 Слот и пустые поля

- Слот рендерится в обёртке `data-testid="company-card-slot"` только при truthy `children`: `false` из `CompanyPage`
  обёртку не создаёт.
- Описание — только если непустое. Карточка с одним названием — логотип и `h1`, без пустых строк.
- Данные и поведение содержимого слотов (`open-state`, «Запись только через мастера») не меняются. `z-10` у ряда и
  наезд логотипа на галерею сохраняются (§204 цикла 13).

### §31.11.5 `CompanyCardSkeleton`

Та же сетка: квадрат логотипа 64 px, полоса заголовка `h-7 sm:h-8 w-2/3`, строка из трёх «таблеток» `h-11 w-32
rounded-full` и полоса сведений `h-4 w-1/2`. Высота совпадает с типичной карточкой, скачка при загрузке нет.

### §31.11.6 Что не затронуто (R-5)

- `EmbedPage` (`/embed/:slug`) карточку не использует, только `CompanyMapLinks` без нового пропа.
- `HomePage` использует свою локальную функцию `CompanyCard` (каталог) — другой компонент.
- Нижние блоки `CompanyPage` и `StorefrontPage` не трогаются.
- Новых запросов нет, `CompanyCardData` не меняется.

---

## §31.12. Контракты и CI (R-7, C29-1)

| Что | Где | Кто |
|---|---|---|
| `contracts/cycle31/openapi.yaml` | готов (этот цикл) | архитектор |
| скрипт `"contracts:json"` | `frontend/package.json`: `redocly bundle ../contracts/cycle26/openapi.yaml --ext json -o ../contracts/cycle26/openapi.json && …cycle29… && …cycle31…`. Если флаги у установленной версии `@redocly/cli` другие, подобрать эквивалент, не меняя смысла | backend (BE-6) |
| `contracts/cycle31/openapi.json` | сгенерировать `npm run contracts:json` и закоммитить. Перегенерированные JSON 26 и 29 закоммитить в том же коммите, если форматирование отличается от ручных. Затем прогнать CY29-30…33 | backend (BE-6) |
| скрипт `"types:api:cycle31"` | `openapi-typescript ../contracts/cycle31/openapi.yaml -o src/types/api-cycle31.generated.ts` | frontend (FE-0) |
| `ci.yml`, шаг `Lint API contracts` (строка около 163) | + `../contracts/cycle31/openapi.yaml` | backend (BE-6) |
| `ci.yml`, шаг сверки генератов (строки около 203–209) | + `npm run types:api:cycle31` и файл в `git diff --exit-code` | backend (BE-6) |
| `ci.yml`, **новый** шаг `Contract JSON bundles must match YAML` | `npm run contracts:json && git diff --exit-code -- ../contracts/cycle26/openapi.json ../contracts/cycle29/openapi.json ../contracts/cycle31/openapi.json`, при расхождении — `::error::` с командой | backend (BE-6) |
| `contracts/redocly.yaml` | дописать 26, 29, 31 в комментарий-перечень | backend (BE-6) |

---

## §31.13. Фронт: типы

- `CatalogListingDto` для салона — `C31['schemas']['SalonCatalogListingDto']` из `src/types/api-cycle31.generated.ts`,
  экспорт в `src/types/index.ts` (`SalonCatalogListingDto`).
- goods `CatalogListingDto` остаётся на цикле 25. Общий компонент принимает структурный `CatalogListingView`, `code`
  там — `string`, поэтому оба DTO подходят без приведения.

---

## §31.14. Тесты и QA

### §31.14.1 Функциональные (`ServiceBooking.Tests`, `[TestCase("CY31-xx")]`)

Файл `Cycle31CatalogListingTests.cs` (своя фикстура — своя БД):

| ID | Проверяет |
|---|---|
| CY31-01 | владелец, салон активен, тариф разрешает, показ включён → 200, `visible`, один пункт `HiddenByOwner` «Показ включен в настройках» `done`, статус «Салон виден…», `notAllowedByPlanText = null` |
| CY31-02 | показ выключен → `visible = false`, тот же текст `done = false`, «Салона сейчас нет в каталоге» |
| CY31-03 | тариф не разрешает (план `AllowPublicListing = false` с действующей подпиской, по образцу тестов каталога цикла 9) → пункты `[NotAllowedByPlan, HiddenByOwner]`, `allowedByPlan = false`, подпись про тариф |
| CY31-04 | салон неактивен → первый пункт `SalonBlocked` «Салон заблокирован администратором» |
| **CY31-05** | **матрица 2 × 2 × 2** (`IsActive` × тариф × показ): для каждого салона `visible` из GET совпадает с присутствием в `GET /api/companies` **и** в `GET /api/companies/public?pageSize=100`. Это доказательство «одного правила» (R-3) |
| CY31-06 | PUT `true` → 200, салон появляется в обоих каталогах. PUT `false` → исчезает. Повтор — 200 |
| CY31-07 | PUT `true` на тарифе без показа → 409 JSON `code = CatalogListingNotAllowedByPlan`, колонка не изменилась. PUT `false` там же → 200 |
| CY31-08 | PUT `{}` → 400 `text/plain` «Не указано, показывать ли салон в каталоге» |
| CY31-09 | посторонний пользователь: GET и PUT → 404, так же как для случайного GUID. Аноним → 401 |
| CY31-10 | SuperAdmin → GET и PUT 200 |
| CY31-11 | владелец магазина с id магазина → GET и PUT 409 `ShopRefusalText`, `ShowInPublicListing` не изменился |
| CY31-12 | мастер салона (не владелец) → 404 |
| CY31-13 | `PUT /api/companies/{id}` с `showInPublicListing` меняет флаг (совместимость). Без поля — не трогает флаг, выставленный через новый маршрут (R-4) |
| CY31-14 | магазин: `GET /api/shops/{id}/catalog-listing` — пункт `HiddenByOwner` с текстом «Показ включен в настройках» при `done = true` и при `done = false`; коды не изменились |

Файл `Cycle31GalleryRateLimitTests.cs`: хост `RateLimitTestFactory` с новыми параметрами `uploadsPerUserPerMinute`,
`companyPhotosPermitLimit`, `companyPhotosEditPermitLimit`. В тестах — продовые значения 10 / 20 / 60 (у фабрики
свой `factoryTag`, см. `TestHostSettings.Apply`).

| ID | Проверяет |
|---|---|
| CY31-20 | 10 разных фото подряд → все 201. Затем в той же минуте `PUT …/photos/order` (обложка) → 200 и `DELETE` одного фото → 204. Ни одного 429 (критерий US-31-02) |
| CY31-21 | после 10 фото галереи `POST /api/companies/{id}/logo` → 200: окно `uploads` не тронуто |
| CY31-22 | 21-я загрузка в галерею за окно → 429 `text/plain` «Too many uploads. Try again in a minute.» |
| CY31-23 | 61-е изменение (DELETE/PUT order) → 429. 11-я загрузка логотипа → 429: `uploads` жив |
| CY31-24 | порядок: 3 последовательные загрузки → `position` 0, 1, 2 в порядке отправки. Обложка не меняется при догрузке в непустую галерею |

Файл `Cycle31ContractConformanceTests.cs` (валидатор `OpenApiContract.Load("cycle31")`):

| ID | Запрос | Операция контракта |
|---|---|---|
| CY31-30 | `GET /api/companies/{id}/catalog-listing` — три состояния (ок, тариф, блок) | 200 `SalonCatalogListingDto` |
| CY31-31 | `PUT /api/companies/{id}/catalog-listing` 200 и 409 (JSON) | 200, 409 `application/json` |
| CY31-32 | `GET /api/shops/{shopId}/catalog-listing` | 200 `ShopCatalogListingDto` (strict) |
| CY31-33 | `POST /api/companies/{id}/photos` 201 и повтор 200 | `CompanyPhotoDto` |
| CY31-34 | `PUT /api/companies/{id}/photos/order` | 200 массив |

Эталон `Cycle22RouteTable.golden.txt`:
- +2 строки `api/companies/{id:guid}/catalog-listing` (GET; PUT с `RequiresOwnerTerms`);
- у трёх строк галереи `ratelimit: company-photos` или `company-photos-edit`.

Обновление эталона — осознанная часть BE-3/BE-4.

### §31.14.2 Юнит (`ServiceBooking.UnitTests`)

- `SalonListingRulesTests` (новый): таблица истинности 8 строк. Для каждой — `Visible`, коды и порядок пунктов, `Done`,
  тексты, `StatusText`. Отдельно: тексты `NotAllowedByPlan` и `HiddenByOwner` — ссылки на константы
  `CatalogListingRules` (`ReferenceEquals` строк или равенство константе).
- `CatalogListingRulesTests`: новая строка, текст не зависит от `ShowInPublicListing`.

### §31.14.3 Vitest

| Файл | Кейсы |
|---|---|
| `src/utils/photoBatch.test.ts` (новый) | отсев по типу (в том числе пустой `type` с расширением `.JPG`), по размеру (`5 МБ` проходит, `5 МБ + 1` нет), обрезка 8 + 5 → 2 принято, 3 `overLimit` в порядке выбора, отсеянные не занимают мест, `overLimitText` с именами, `isTransientUploadError` (429, 503, сеть → да; 400, 403, 413 → нет) |
| `src/components/company/CompanyPhotosSection.test.tsx` (переезд + дополнение) | 10 прежних `it`; `input` с `multiple`; выбор 3 файлов → 3 вызова `upload` **последовательно** (второй не стартует до резолва первого) и в порядке выбора; перетаскивание 2 файлов → оба; 8 фото + 5 файлов → 2 вызова и одно сообщение с тремя именами; неверный тип и размер — ошибка у файла, вызова нет, остальные грузятся; 400 у второго файла — третий всё равно грузится, ошибка показана текстом маппера; повтор (200 с существующим `id`) — плиток не прибавилось; во время пакета стрелки, «Сделать обложкой», «Удалить» и «Выбрать фото» `disabled`; `role="status"` показывает «Загружено 2 из 3»; «Повторить неудавшиеся» повторяет только 429; `aria-describedby` у `input`; у кнопки обложки доступное имя «Сделать обложкой»; у панели есть классы `group-focus-within:opacity-100` и `opacity-100` (страховка US-31-03, R-6) |
| `src/components/company/CatalogListingCard.test.tsx` (новый) | `role="switch"` с именем `switchLabel`; `✓/○` и `sr-only`; тариф запрещает → `disabled` + `listing-not-allowed`; `saving` → `disabled`; `loadError` → кнопка «Повторить» вызывает `onRetry`; `saveError` показан |
| `goods/src/components/CatalogListingSection.test.tsx` | 4 прежних `it` проходят без правок ожиданий (разметку перенесли вместе с `testid`) |
| `src/pages/owner/SalonCatalogListingSection.test.tsx` (новый) | заголовок «Каталог ezbook.ru», подпись «Показывать салон в каталоге ezbook.ru»; клик → `put(id, true)`; 409 JSON → текст `message` и переключатель вернулся к серверному значению; 404 → «Компания не найдена» |
| `src/pages/owner/CompanyManagePage.test.tsx` | чекбокса «Показывать компанию в общем списке» нет; при сохранении формы тело `companiesApi.update` **не содержит** ключа `showInPublicListing`; блок «Каталог ezbook.ru» есть |
| `src/components/company/CompanyCard.test.tsx` (новый, T-31-02) | порядок: `h1` → описание → слот → действия → сведения (`compareDocumentPosition`); полная карточка — телефон `tel:` с именем «Позвонить …», обе ссылки карт внутри `company-card-actions`; без полей — нет `company-card-actions`, `company-card-meta`, `company-card-slot`, описания; `children={false}` → обёртки слота нет; слот рендерит содержимое; скелетон рендерится |
| `goods/src/sharedSources.guard.test.ts` (новый) | §31.7.3 |
| `src/components/company/CompanyMapLinks.test.tsx` | без правок ожиданий, зелёный |

### §31.14.4 Ручные `M31-` (T-31-04, раздел «Цикл 31» в `TEST_CATALOG.md`, формат `M27-`)

Колонки: `Кейс | Шаги | Ожидаемый результат | Критерий`. Размеры — 360 × 740 и 1280 × 800, оба сайта:
- M31-01 — плитка ezbook и goods рядом на 1280: бейдж, панель при наведении, кнопка обложки;
- M31-02 — 360 px: 2 колонки, иконки, ничего не вылезает;
- M31-03 — телефон (touch): панель видна без касания, Tab с клавиатуры показывает панель;
- M31-04 — пакет из 10 фото в пустую галерею, сразу «Сделать обложкой» и удаление — без 429;
- M31-05 — пакет с ошибками: 8 + 5, файл 6 МБ, `.gif`, сеть отключена на середине → «Повторить неудавшиеся»;
- M31-06 — блок каталога салона во всех состояниях, включая «тариф не разрешает» и «заблокирован»;
- M31-07 — «✓ Показ включен в настройках» у магазина;
- M31-08 — шапка `/myasnoy` до и после (скриншоты), полная и без полей, с раскрытыми часами, 360 и 1280;
- M31-09 — страница салона `ezbook.ru/company/:slug` до и после, «Запись только через мастера»;
- M31-10 — `/embed/:slug` не изменился.

---

## §31.15. Структура проекта — что добавляется и меняется

```
ServiceBooking.API/
  Controllers/CompanyCatalogListingController.cs          + новый (GET|PUT api/companies/{id}/catalog-listing)
  Controllers/CompaniesController.cs                      ~ GetAll/GetPublic → SalonListingQuery
  Controllers/CompanyPhotosController.cs                  ~ три атрибута EnableRateLimiting
  Services/Companies/SalonListingRules.cs                 + новый (чистое правило)
  Services/Companies/SalonListingQuery.cs                 + новый (SQL-двойник)
  Services/Companies/CompanyKindGuard.cs                  ~ комментарий: новый салонный маршрут в закрытом списке
  Services/Shops/CatalogListingRules.cs                   ~ HiddenByOwnerText, enum + SalonBlocked
  Startup/RateLimitingExtensions.cs                       ~ две политики, явная ветка OnRejected
  appsettings.json, appsettings.Testing.json              ~ RateLimits: company-photos, company-photos-edit
ServiceBooking.UnitTests/
  SalonListingRulesTests.cs                               +
  CatalogListingRulesTests.cs                             ~
ServiceBooking.Tests/
  Infrastructure/RateLimitTestFactory.cs                  ~ три необязательных параметра
  Tests/Cycle31CatalogListingTests.cs                     +
  Tests/Cycle31GalleryRateLimitTests.cs                   +
  Tests/Cycle31ContractConformanceTests.cs                +
  Tests/Cycle22RouteTable.golden.txt                      ~
contracts/cycle31/openapi.yaml                            + (готов)
contracts/cycle31/openapi.json                            + (генерат contracts:json)
contracts/cycle26|29/openapi.json                         ~ если перегенерация меняет форматирование
contracts/redocly.yaml                                    ~ комментарий
.github/workflows/ci.yml                                  ~ lint, генераты, новый шаг JSON
frontend/
  package.json                                            ~ types:api:cycle31, contracts:json
  goods-shared-sources.js, goods-shared-sources.d.ts      + единый список (T-31-01)
  tailwind.goods.config.js, eslint.config.js              ~ строятся из списка
  src/types/api-cycle31.generated.ts                      + генерат
  src/types/index.ts                                      ~ SalonCatalogListingDto
  src/api/companyPhotos.ts                                ~ upload(..., onProgress?)
  src/api/companyCatalogListing.ts                        +
  src/utils/photoBatch.ts (+ .test.ts)                    +
  src/utils/catalogListingError.ts                        +
  src/components/company/CompanyPhotosSection.tsx (+test) ← переезд из src/pages/owner/, мультизагрузка, плитка
  src/components/company/usePhotoBatchUpload.ts           +
  src/components/company/CatalogListingCard.tsx (+test)   +
  src/components/company/companyActionLink.ts             +
  src/components/company/CompanyCard.tsx (+ новый test)   ~ шапка, скелетон
  src/components/company/CompanyMapLinks.tsx              ~ проп className, общая константа класса
  src/pages/owner/CompanyManagePage.tsx (+test)           ~ импорт галереи, без чекбокса, + блок каталога
  src/pages/owner/SalonCatalogListingSection.tsx (+test)  +
  goods/src/components/CatalogListingSection.tsx          ~ обёртка над CatalogListingCard
  goods/src/pages/cabinet/SettingsPage.tsx                ~ импорт галереи
  goods/src/sharedSources.guard.test.ts                   +
Документы: API_DOCUMENTATION.md (Цикл 31 unreleased), CHANGELOG.md (раздел цикла 31), TEST_CATALOG.md (раздел «Цикл 31»),
           CURRENT_STATE.md — обновляет codebase-analyst после влития.
```

---

## §31.16. Разбивка работ и параллельность

### §31.16.1 Задачи

**Backend**

| ID | Задача | SPEC | Зависит от |
|---|---|---|---|
| BE-1 | `HiddenByOwnerText`, enum `SalonBlocked`, `CatalogListingRulesTests` | US-31-04 | — |
| BE-2 | `SalonListingRules` + `SalonListingQuery` + `SalonListingRulesTests` | US-31-05, R-3 | BE-1 (enum) |
| BE-3 | `CompanyCatalogListingController`, `GetAll`/`GetPublic` на `SalonListingQuery`, комментарий `CompanyKindGuard`, golden (2 строки) | US-31-05 | BE-2 |
| BE-4 | политики `company-photos` / `company-photos-edit`, атрибуты, appsettings, `OnRejected`, golden (3 строки), параметры `RateLimitTestFactory` | US-31-02, R-1 | — |
| BE-5 | функциональные CY31-01…14, CY31-20…24 | T-31-03 | BE-3, BE-4 |
| BE-6 | `contracts:json`, `openapi.json` (26/29/31), CI (lint, генераты, JSON-шаг), `redocly.yaml`; CY31-30…34 | R-7 | BE-3 (для 30–32) |
| BE-7 | `API_DOCUMENTATION.md`: маршрут салона, лимиты галереи, текст пункта — пометки «Цикл 31 (unreleased)»; строка ослабления лимита для CHANGELOG | US-31-04, NFR | BE-3, BE-4 |

**Frontend**

| ID | Задача | SPEC | Зависит от |
|---|---|---|---|
| FE-0 | `types:api:cycle31`, генерат, `SalonCatalogListingDto` в `types/index.ts` | — | контракт (готов) |
| FE-1 | T-31-01: переезд `CompanyPhotosSection`, `goods-shared-sources.{js,d.ts}`, генерация `content`/ESLint, `NoticesPage` (C31-5), guard-тест | T-31-01 | — |
| FE-2 | плитка: сетка, панель, кнопка обложки (§31.8) | US-31-01, US-31-03 | FE-1 (тот же файл) |
| FE-3 | мультизагрузка: `photoBatch.ts`, `usePhotoBatchUpload`, `upload(onProgress)`, UI статуса, тесты. P2 внутри: проценты (US-31-07), «Повторить» (US-31-08) — отдельными коммитами, режутся первыми | US-31-02, 07, 08 | FE-1; с FE-2 — тот же файл, делать одним исполнителем или последовательно |
| FE-4 | `CatalogListingCard` + перевод goods `CatalogListingSection` на него | US-31-05 | — |
| FE-5 | `companyCatalogListing.ts`, `catalogListingError.ts`, `SalonCatalogListingSection`, правка `SettingsTab` (R-4), тесты | US-31-05 | FE-0, FE-4. **Не ждёт BE-3**: prism-мок `contracts/cycle31` |
| FE-6 | шапка `CompanyCard`, `companyActionLink.ts`, проп `className` у `CompanyMapLinks`, скелетон, `CompanyCard.test.tsx` | US-31-06, T-31-02 | — |

**QA / документы**

| ID | Задача | Зависит от |
|---|---|---|
| QA-1 | раздел «Цикл 31» в `TEST_CATALOG.md`: CY31-, M31-01…10 (T-31-04) | формулировки — сразу, прогон — после FE/BE |
| QA-2 | прогон M31 в браузере, скриншоты «до» снять **до** влития FE-6 (со `develop`) | FE-2, FE-3, FE-5, FE-6 |
| QA-3 | CHANGELOG «Не выпущено — цикл 31…», в том числе ослабление лимита (§31.6.2) и C31-5 | все |

### §31.16.2 Что идёт параллельно, а что последовательно

```
День 1 ──► BE-1 → BE-2 → BE-3 ─┐                 FE-0 ─┐
          BE-4 ───────────────┼─► BE-5          FE-1 → FE-2 → FE-3        (один исполнитель: общий файл)
                               └─► BE-6 → BE-7   FE-4 → FE-5 (против prism-мока)
                                                 FE-6                       (полностью независим)
                                                           └────────► QA-2 (после FE-2/3/5/6), QA-3
```

- **Backend и frontend полностью параллельны.** Общих точек две, обе в контракте:
  - маршрут `catalog-listing` салона — FE-5 работает против `prism mock contracts/cycle31/openapi.yaml`;
  - текст пункта магазина — фронт выводит его дословно, от него ничего не зависит.
- Лимиты частоты (BE-4) фронт не видит: для него поведение то же, 429 реже.
- Внутри фронта последовательны только FE-1 → FE-2 → FE-3 (один файл) и FE-4 → FE-5. FE-6 независим.
- Внутри бэкенда: BE-1 → BE-2 → BE-3 (enum и правило). BE-4 независим.
- Интеграционная проверка — после влития BE-3 и FE-5: блок салона против живого API (M31-06), CY31-30…31.

### §31.16.3 Порядок урезания (SPEC §1)

US-31-08 (кнопка «Повторить», FE-3 часть) → US-31-07 (проценты, FE-3 часть) → T-31-04 (M31, QA-1/QA-2) → US-31-03
(медиа-вариант и `group-focus-within` в FE-2; бейдж и сетка остаются). P0 (BE-1…BE-6, FE-0…FE-6 без P2-частей)
не режутся.

---

## §31.17. Риски

| # | Риск | Вероятность / вред | Что делаем |
|---|---|---|---|
| R31-1 | `GetAll` на SQL-фильтре тарифа разойдётся с `SubscriptionResolver` (скрытое отличие `PublicListingQuery`) | низкая / салоны пропадут или появятся в каталоге | CY31-05 сравнивает чек-лист (резолвер) с обоими каталогами; прежние тесты каталога проходят без правок ожиданий. Упавший тест — сигнал чинить двойник |
| R31-2 | Бюджет загрузок вырос (30 обработок в минуту на пользователя) | низкая / нагрузка на CPU | Явно в CHANGELOG и `API_DOCUMENTATION.md`; лимиты настраиваются `RateLimits:company-photos*` без релиза кода; лимит 10 фото на компанию остаётся |
| R31-3 | Guard T-31-01 даст ложное срабатывание на существующих импортах (файлы верхнего уровня `src/`) | средняя / задержка FE-1 | Правило §31.7.3 п. 5: вписать явно; guard сообщает путь и способ исправления |
| R31-4 | Общий `CompanyCard` на ezbook выглядит хуже с его слотом | средняя / жалоба | M31-09 до и после. Слот ezbook — одна таблетка, в правой колонке она компактна. Раскладка настраивается классами без смены DOM |
| R31-5 | `display: contents` у обёртки ссылок карт | низкая / в старых браузерах ссылки встанут отдельной строкой | Обёртка — `div` без роли, ссылки остаются ссылками. Деградация — прежний вид |
| R31-6 | Последовательная загрузка 10 × 5 МБ на мобильном медленная | средняя / ожидание | Прогресс по файлу (US-31-07), статус «Загружено N из M»; UI не блокируется, кроме действий с галереей |
| R31-7 | Перегенерация `openapi.json` 26/29 даст diff форматирования | средняя / шум в PR | Коммитится вместе со скриптом; CY29-30…33 перепрогоняются |
| R31-8 | Конфликт с веткой `cycle/028-…` по корневому `SPEC.md` и CHANGELOG | высокая при влитии / ручной мерж | Разрешать по смыслу (CURRENT_STATE §0), не выбором файла целиком |
| R31-9 | Размер ровно у границы 5 МБ: клиент пропустил, сервер 413 (заголовки multipart) | низкая | 413 маппится в «Файл больше 5 МБ…», ошибка у файла, очередь идёт дальше |

Безопасность и секреты: новых секретов нет. Права не расширяются: маршрут салона доступен владельцу и SuperAdmin,
чужие компании — 404. Сервер остаётся единственным барьером проверки файлов.

---

## §31.18. Отклонения от буквы SPEC и решения сверх неё (читать обязательно)

| # | Что | Почему |
|---|---|---|
| О-31-1 | Заголовок блока и подпись переключателя («Каталог ezbook.ru», «Показывать салон в каталоге ezbook.ru») — константы фронта, а не поля DTO | Так уже у магазина. Правило «тексты формирует сервер» (CURRENT_STATE §6.1) относится к текстам, зависящим от состояния, и к ошибкам. Статус и пункты чек-листа формирует сервер |
| О-31-2 | Чужая компания на новом маршруте — 404, хотя `PUT /api/companies/{id}` отвечает чужому 403 | Прямое требование SPEC для нового маршрута; старый маршрут не меняется |
| О-31-3 | Политика `uploads` расщеплена, суммарный бюджет вырос | R-1, §31.6.2; названо явно |
| О-31-4 | `NoticesPage` добавлен в сборку стилей goods (C31-5) | Тот же дефект, что C31-1; чинится тем же механизмом бесплатно |
| О-31-5 | `GetAll` переведён на SQL-фильтр тарифа | R-3: одно правило вместо трёх мест |
| О-31-6 | Состояния загрузки и ошибки блока каталога в goods теперь рисует общий компонент, не `StatePanels` | Общий компонент не может импортировать goods (ESLint); вид близкий |
| О-31-7 | Скрипт `contracts:json` и CI-шаг JSON закрывают C29-1 целиком | SPEC §3 допускает, если цикл заводит JSON-копию; без скрипта CY31-30…34 читали бы устаревшую схему |
| О-31-8 | Ссылки карт получили фокус-обводку; у `CompanyMapLinks` новый необязательный проп | NFR «фокус-обводки сохранены» — у телефона она была, общий стиль должен её нести. Лицензионные условия не затронуты |
| О-31-9 | `CompanyPhotosSection` переехал в `src/components/company/` | T-31-01: самый надёжный способ, чтобы сборка стилей goods видела компонент |
| О-31-10 | Подпись под недоступным переключателем салона: «Показ в каталоге не входит в ваш тариф — повысьте тариф, чтобы включить» (было «Отображение в общем списке не входит в текущий тариф — повысьте тариф, чтобы включить») | SPEC: «смысл сохраняется». «Общий список» → «каталог», как в заголовке блока |
