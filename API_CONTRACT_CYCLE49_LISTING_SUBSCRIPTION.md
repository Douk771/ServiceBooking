# API_CONTRACT — цикл 49 ServiceBooking: видимость салона в каталоге по услугам и сотрудникам + пункт «Подписка»

**Дата: 2026-11-01. Ветка `cycle/049-listing-services-and-subscription-tab` (код = `develop` @ `d32a5278`).** Разделы §49.20–§49.30.
Решения — `ARCHITECTURE_CYCLE49_LISTING_SUBSCRIPTION.md` (§49.0–§49.14). Требования — `SPEC.md` цикла 49 (решение заказчика
2026-11-01: Q49-1…Q49-5 и §6 приняты как есть).
**Машиночитаемо (источник истины по форме):** `contracts/cycle49/openapi.yaml` (+ генерат `contracts/cycle49/openapi.json`, BE-49-0).
При расхождении по ФОРМЕ прав yaml, по смыслу и текстам — этот файл.

Корневой `API_CONTRACT.md` (цикл 3) не трогается — по конвенции проекта документы цикла лежат в корне под суффиксом.

---

## §49.20. Что меняется одним списком

| Маршрут | Что | Форма |
|---|---|---|
| `GET\|PUT /api/companies/{id}/catalog-listing` | +2 кода пунктов (`NoActiveServices`, `NoServiceProviders`), +поле `conditionsText` (всегда) | **меняется** (добавочно) |
| `GET /api/companies`, `GET /api/companies/public` | салон без активной услуги или без оказывающего услуги участника не попадает в список и в `total` | прежняя, меняется состав |
| `GET /api/billing/subscription` (без `line` / `line=Services`) | `availablePlans` — список тарифов «Записи» вместо `null`; `pendingRequest.estimatedMonthlyPrice` при заявке на тариф — от цены запрошенного тарифа | **меняется** (добавочно) |
| `GET\|PUT /api/shops/{shopId}/catalog-listing` | ничего; `conditionsText` в ответе **нет** | прежняя |
| `GET /api/companies/my` | ничего; новый потребитель — шапка `Navbar` | прежняя |
| `POST /api/billing/subscription/request` | ничего; новый вызывающий — блок «Тарифы «Записи»» | прежняя |
| `GET /api/billing/subscription?line=Orders\|Stays\|Baths` | ничего | прежняя |

Новых маршрутов, миграций, переменных окружения и пакетов нет. Конвенции ошибок прежние: 400 и осознанные 409 — `text/plain`,
401/404 — пустое тело, 403 демо-стенда — `text/plain`, 409 тарифа в блоке каталога — JSON `{code, message}`.

---

## §49.21. `GET|PUT /api/companies/{id}/catalog-listing` — блок «Каталог ezbook.ru» салона

### §49.21.1 Права и порядок проверок — без изменений (цикл 31)
- GET: 401 → 451 → 404 (нет компании или вызывающий не владелец и не SuperAdmin — ответ один) → 409 `text/plain` (магазин,
  `CompanyKindGuard`) → 200.
- PUT: 401 → 451 (`RequiresOwnerTerms`) → 415/400 (фреймворк) → 404 → 409 `text/plain` (магазин) → 400 `text/plain`
  «Не указано, показывать ли салон в каталоге» → 409 JSON `{code: "CatalogListingNotAllowedByPlan", message}` → 200.
- **409 по-прежнему только из-за тарифа.** Включить показ без услуг и без оказывающих услуги можно: ответ 200,
  `showInCatalog: true`, `visible: false`, невыполненные пункты — в `checklist`.

### §49.21.2 Ответ `SalonCatalogListingDto`

| Поле | Тип | Значение |
|---|---|---|
| `showInCatalog` | bool | `Company.ShowInPublicListing` |
| `allowedByPlan` | bool | эффективный тариф разрешает показ |
| `visible` | bool | все пункты `done` ⇔ салон есть в `GET /api/companies` и `GET /api/companies/public` |
| `statusText` | string | «Салон виден в каталоге ezbook.ru» / «Салона сейчас нет в каталоге» (без изменений) |
| `notAllowedByPlanText` | string \| null | без изменений |
| **`conditionsText`** | string, **всегда** | «Салон появится в каталоге ezbook.ru, только когда выполнены все условия ниже.» |
| `checklist` | 3–5 пунктов `{code, text, done}` | см. §49.21.3 |

### §49.21.3 Пункты — строго в этом порядке

| # | `code` | `text` (константа сервера) | Когда в списке | `done` |
|---|---|---|---|---|
| 1 | `SalonBlocked` | «Салон заблокирован администратором» | только если `Company.IsActive = false` | всегда `false` |
| 2 | **`NoActiveServices`** | «Есть услуги» | **всегда** | есть ≥ 1 `Service` салона с `IsActive = true` |
| 3 | **`NoServiceProviders`** | «Есть сотрудники, оказывающие услуги» | **всегда** | есть ≥ 1 `CompanyMember` салона с `Role ∈ {Master, CompanyOwner}` и `ProvidesServices = true` |
| 4 | `NotAllowedByPlan` | «Показ в каталоге не входит в ваш тариф» | только если тариф не разрешает | всегда `false` |
| 5 | `HiddenByOwner` | «Показ включен в настройках» | всегда | `showInCatalog` |

- Имя кода называет **отказ** (как у магазина `NoWorkingHours`/`NoPublishedProducts`), `done = true` — условие выполнено.
- Участник с ролью `Client` не выполняет пункт 3, даже при `ProvidesServices = true`. Привязка мастера к услуге (`MasterService`)
  не требуется. Владелец — такой же участник: владелец-одиночка с галочкой выполняет пункт 3.
- В чек-листе нет имён сотрудников и названий услуг.

### §49.21.4 Примеры

Новый салон сразу после создания (владелец с галочкой «Оказывает услуги» по умолчанию, услуг нет, владелец включил показ):
```json
{
  "showInCatalog": true, "allowedByPlan": true, "visible": false,
  "statusText": "Салона сейчас нет в каталоге", "notAllowedByPlanText": null,
  "conditionsText": "Салон появится в каталоге ezbook.ru, только когда выполнены все условия ниже.",
  "checklist": [
    { "code": "NoActiveServices", "text": "Есть услуги", "done": false },
    { "code": "NoServiceProviders", "text": "Есть сотрудники, оказывающие услуги", "done": true },
    { "code": "HiddenByOwner", "text": "Показ включен в настройках", "done": true }
  ]
}
```
Заблокированный салон на тарифе без показа, всё остальное есть, показ выключен:
```json
{
  "showInCatalog": false, "allowedByPlan": false, "visible": false,
  "statusText": "Салона сейчас нет в каталоге",
  "notAllowedByPlanText": "Показ в каталоге не входит в ваш тариф — повысьте тариф, чтобы включить",
  "conditionsText": "Салон появится в каталоге ezbook.ru, только когда выполнены все условия ниже.",
  "checklist": [
    { "code": "SalonBlocked", "text": "Салон заблокирован администратором", "done": false },
    { "code": "NoActiveServices", "text": "Есть услуги", "done": true },
    { "code": "NoServiceProviders", "text": "Есть сотрудники, оказывающие услуги", "done": true },
    { "code": "NotAllowedByPlan", "text": "Показ в каталоге не входит в ваш тариф", "done": false },
    { "code": "HiddenByOwner", "text": "Показ включен в настройках", "done": false }
  ]
}
```

### §49.21.5 Свежесть
Ответ считается на каждый запрос (кеша на сервере нет). Добавили или включили услугу, включили «Оказывает услуги» — следующий
GET отдаёт новое состояние, и салон в тот же момент появляется в обоих каталогах. Обратное тоже верно.

---

## §49.22. `GET|PUT /api/shops/{shopId}/catalog-listing` — без изменений
Форма, коды и тексты циклов 25/31. Поля `conditionsText` в ответе **нет** (сервер не пишет `null`-значение этого поля —
`JsonIgnore(WhenWritingNull)`), новые коды не появляются. `ShopCatalogListingDto` в `cycle49` — `additionalProperties: false`,
это проверяет CY49-B4.

---

## §49.23. `GET /api/companies`, `GET /api/companies/public` — новый состав
- В списках ровно те салоны, у которых `SalonCatalogListingDto.visible = true` (одно правило, §49.3 архитектуры).
- `cityId`, `search`, `page`, `pageSize`, сортировка (`Name`, затем `Id`) и клампинг — как в цикле 9. Условия применяются
  в SQL до `Count`/`Skip`/`Take`, поэтому `total` и `hasNext` согласованы с новым правилом.
- **`CompanyDto.publicListingEnabled` (A49-2):** значение не меняется (= `showInPublicListing ∧ тариф разрешает`), но поле
  больше не означает «салон в каталоге». Клиенты, которым нужна истина о каталоге, читают `catalog-listing.visible`.
  Фронт это поле не показывает; новых потребителей у него быть не должно.
- Страница салона по slug (`GET /api/companies/{slug}`) и запись от каталога не зависят — без изменений.
- Каталоги «Заказов», «Домов», «Бань» не затронуты.

---

## §49.24. `GET /api/companies/my` — без изменений, новый потребитель
Шапка «Записи» вызывает его **без** `kind` (= только салоны, роль `CompanyOwner`, `Company.IsActive = true`). Правило показа
пункта «Подписка» — §49.27.1. Ответ, права и коды прежние.

---

## §49.25. `GET /api/billing/subscription` (линия «Записи») — `availablePlans`

### §49.25.1 Что меняется
- `line` = `"Services"` (как и раньше). `availablePlans` — **массив** (раньше `null`), может быть пустым.
- Состав: тарифы `SubscriptionPlanConfig` с `Line = Services ∧ IsActive ∧ IsPublic ∧ ¬IsSystemTrial ∧ ¬IsSystemFree`.
  Порядок: `SortOrder`, затем `PricePerMonth` (как на `/pricing`).
- Элемент — `AvailablePlanDto`: `planId`, `name`, `pricePerMonth`, `description` (nullable), `highlights[]` (строки
  `Highlights`, разбитые по `\n`, не больше 10), `limitsText`. Полей `maxHouses`/`maxResources` нет.
- `limitsText` = `BillingTexts.ServicesPlanLimitsText(MaxCompanies, MaxEmployees)`:
  - `до {n} точки|точек|точек` или `точки без ограничения`;
  - ` · `;
  - `до {m} сотрудника|сотрудников|сотрудников` или `сотрудники без ограничения`.
  Пример: «до 1 точки · до 3 сотрудников», «до 5 точек · сотрудники без ограничения».
- `pendingRequest.estimatedMonthlyPrice` на экране «Записи»: если в открытой заявке есть тариф (`desiredPlanId ≠ null`),
  база расчёта — цена **запрошенного** тарифа; иначе — текущего (как раньше). Экраны других линий и админка считают
  по-прежнему.
- Остальные поля ответа (тариф, статус, пробный период, опции, мессенджеры, заявка, `canRequestChanges = true`) — прежние.

### §49.25.2 Какой тариф «текущий» (правило для фронта)
Тариф из `availablePlans` — текущий, если `status = "Active"` **и** `planId = plan.id`. При `Expired` текущего нет, у
истёкшего тарифа есть кнопка «Запросить» (продление). Во время пробного периода `plan.id` — id пробного тарифа, которого в
списке нет, поэтому текущего тоже нет.

### §49.25.3 Пример фрагмента
```json
{
  "status": "Active", "statusText": "Оплачено до 30.11.2026",
  "plan": { "id": "6c1a…", "name": "Салон", "description": null, "pricePerMonth": 1490, "includes": ["Онлайн-запись"] },
  "canRequestChanges": true,
  "trial": null,
  "line": "Services",
  "availablePlans": [
    { "planId": "6c1a…", "name": "Салон", "pricePerMonth": 1490, "description": "Для одной точки",
      "highlights": ["Онлайн-запись"], "limitsText": "до 1 точки · до 5 сотрудников" },
    { "planId": "9e70…", "name": "Сеть", "pricePerMonth": 3900, "description": null,
      "highlights": [], "limitsText": "точки без ограничения · сотрудники без ограничения" }
  ],
  "pendingRequest": null
}
```

---

## §49.26. `POST /api/billing/subscription/request` — без изменений, новые правила вызова

### §49.26.1 Сервер (как есть)
- Тело: `{ planId?, options?, comment?, line? }`. Экран «Записи» `line` не передаёт.
- 400 `text/plain`: «Указанный тариф не найден или неактивен.», «Этот тариф из другой линейки», «Комментарий не может быть
  длиннее 500 символов.», ошибки опций. 404 — нет аккаунта. 403 `text/plain` — демо-стенд. 409 `text/plain` — открыта
  заявка другой линии («У вас уже есть заявка на смену тарифа «…» — отмените её или дождитесь решения»).
- 200 — `SubscriptionRequestDto` (открытая заявка). **Повтор перезаписывает открытую заявку той же линии целиком**: и
  `planId`, и `options`.
- Сервер не проверяет, публичный ли тариф и не пробный ли он: экран просто не предлагает такие тарифы (§49.13 архитектуры, п. 1).

### §49.26.2 Клиент «Записи» обязан собрать заявку так, чтобы тариф и опции не потеряли друг друга
Пусть `pr = data.pendingRequest` и `same = pr != null && pr.line === 'Services'`.
- **Заявка на тариф** (кнопка «Запросить» у тарифа `P`):
  `{ planId: P.planId, options: same ? pr.items.filter(i => !i.retired).map(i => ({ optionId: i.optionId, quantity: i.quantity })) : [] }`.
- **Заявка на опции** («Отправить заявку» в «Доступных опциях»):
  `{ options: <выбранный набор, как сейчас>, ...(same && pr.desiredPlanId ? { planId: pr.desiredPlanId } : {}) }`.
- Если `pr` есть, но `pr.line ≠ 'Services'`, клиент ничего не переносит; сервер ответит 409, текст печатается как есть.
- Ответ 200 клиент не разбирает: инвалидирует `['owner-subscription']` и показывает «Заявка на рассмотрении» из перечитанного ответа.

---

## §49.27. Контракт фронта (без HTTP)

### §49.27.1 Пункт «Подписка» в шапке `Navbar`
- Видим ⇔ пользователь вошёл ∧ в токене есть роль `CompanyOwner` ∧ `GET /api/companies/my` (без `kind`, ключ кеша
  `['my-companies']`) вернул непустой массив.
- Пока запрос идёт или упал — пункт **не** показывается. Пользователям без роли `CompanyOwner` запрос не отправляется.
- Ссылка `NavLink` на `/billing`, текст «Подписка», стоит сразу после «Кабинет» и в широкой шапке, и в мобильном меню.
  На `/billing` у ссылки `aria-current="page"` и активный класс. В мобильном меню — `onClick={closeMenu}`, высота ≥ 44 px
  (класс `mobileLinkClass` с `py-3`; если по замеру меньше 44 px — добавить `min-h-[44px]` этому пункту).
- Матрица: гость ✗, клиент ✗, мастер без своего салона ✗, владелец только магазина/дома/бани ✗ (роль есть, массив пуст),
  SuperAdmin без своего салона ✗ (роли `CompanyOwner` нет — П-4), владелец салона ✓, мастер салона A и владелец салона B ✓,
  SuperAdmin со своим салоном ✓.

### §49.27.2 Блок «Каталог …» (`CatalogListingCard`, общий для салона и магазина)
- `CatalogListingView` получает необязательное `conditionsText?: string | null`. Если строка непустая — абзац с ней стоит
  между `statusText` и списком пунктов (`data-testid="listing-conditions"`). У магазина поля нет — абзаца нет.
- Невыполненный пункт — «✗» (было «○»), выполненный — «✓». `sr-only` « — выполнено» / « — не выполнено» без изменений.
  Изменение видно и у магазина goods (П-3).
- Тексты пунктов, статус и пояснение печатаются как пришли.

### §49.27.3 Экран `/billing` линии «Записи»
- `document.title` = «Ваша подписка — ezbook · Записи». Другие линии — без изменений.
- Блок «Тарифы «Записи»» (`data-testid="available-plans"`, заголовок `h2` «Тарифы «Записи»») — если `availablePlans` непуст.
  Место — то же, где у других линий (после карточки «Заявка отклонена», перед «Заявка на рассмотрении»).
- У каждого тарифа: название, цена в месяц (`formatMonthlyPrice`), описание, пункты, `limitsText`. Текущий тариф (§49.25.2) —
  метка «Ваш тариф», без кнопки. Тариф, совпадающий с `pendingRequest.desiredPlanId` (при `line = Services`), — метка
  «Заявка отправлена», без кнопки.
- Кнопка «Запросить» (`aria-label` «Запросить тариф «{name}»», `min-h-[44px]`) — только при `canRequestChanges`.
  - `trial?.planChangeNotice` пуст → нажатие сразу отправляет заявку (§49.26.2).
  - `trial?.planChangeNotice` не пуст → нажатие открывает под тарифом подтверждение: текст `planChangeNotice` (та же плашка,
    что у опций), кнопки «Отправить заявку» и «Отменить». Заявка уходит только по «Отправить заявку».
- Карточка «Заявка на рассмотрении» на экране «Записи» дополнительно показывает строку «Тариф: {desiredPlanName}», если он не
  `null`. У других линий карточка без изменений.
- Ошибка — `getBillingErrorMessage(err, 'Не удалось отправить заявку.')` в общий `requestError`, как сейчас.

---

## §49.28. Кеши фронта и их сброс

| Ключ | Кто читает | Что меняется |
|---|---|---|
| `['company-catalog-listing', companyId]` | `SalonCatalogListingSection` | `refetchOnMount: 'always'`; сбрасывается после успешных мутаций услуг (создать, изменить, удалить) во вкладке «Услуги» и мутаций участников (добавить, удалить, «Оказывает услуги») во вкладке «Сотрудники» — общей функцией `invalidateSalonCatalogListing(qc, companyId)` |
| `['my-companies']` | кабинет и (новое) шапка | шапка читает с `enabled: isAuthenticated && hasRole('CompanyOwner')`, `staleTime: 5 мин`; при выходе кеш чистит `clearUserCache` (как сейчас); `CreateCompanyDialog` его уже инвалидирует |
| `['owner-subscription']` | `/billing` | без изменений: после заявки — инвалидировать |

---

## §49.29. Отчёт перед выкатом (DO ↔ заказчик, US-49-04)
Файлы `deploy/checks/cycle49-catalog-report.sql` (только чтение) и `deploy/checks/cycle49-catalog-empty.sql` (стоп-условие).
Отчёт печатается в лог выката, без телефонов, e-mail и ФИО.
1. Итоги: `visible_now` (старое правило), `visible_after` (новое правило), `disappearing` (разница).
2. Список пропадающих: `name`, `slug`, `reason` ∈ {«нет активных услуг», «нет оказывающих услуги», «нет активных услуг и
   оказывающих услуги»}, сортировка по `name`.
3. Стоп: `cycle49-catalog-empty.sql` возвращает строку, если `visible_now > 0 ∧ visible_after = 0`. Тогда выкат
   останавливается, пока в `.env` нет `CYCLE49_EMPTY_CATALOG_ACCEPTED=true` (ставит человек после решения заказчика).
Старое правило в SQL — `Kind = 0 ∧ IsActive ∧ ShowInPublicListing ∧ NOT EXISTS (подписка в силе на активном тарифе с
`AllowPublicListing = false`)`; «в силе» = `IsActive ∧ (PaidUntil IS NULL ∨ PaidUntil >= now())` (`SubscriptionUsability`).
Новое — старое ∧ EXISTS активной услуги ∧ EXISTS участника `Role ∈ {Master, CompanyOwner}` с `ProvidesServices`.
Числовые значения enum и имена колонок DO сверяет по `AppDbContextModelSnapshot.cs`. Согласие с кодом доказывает CY49-D1.

---

## §49.30. Совместимость и типы
- Старые клиенты салонного блока: новые коды — новые строки в `code`, лишнее поле `conditionsText`; фронт до цикла 49
  отрисовал бы их корректно (код используется только как `key`).
- Старые клиенты `/billing` «Записи»: ветка `isLine` для «Записи» ложна, поэтому прежняя сборка `availablePlans` игнорирует.
- TS-типы: `npm run types:api:cycle49` → `src/types/api-cycle49.generated.ts`. `SalonCatalogListingDto` в `src/types/index.ts`
  переключается на генерат цикла 49. Генераты циклов 25/31 и типы goods (`goods/src/types.ts`) не меняются: enum магазина
  прежний.
- C#-тест контракта: `OpenApiContract.Load("cycle49")` (нужен `contracts/cycle49/openapi.json`, BE-49-0).
- Версия контракта: `info.version` 49.0.0; правка формы в цикле — повышение минорной версии и регенерация json и типов.
