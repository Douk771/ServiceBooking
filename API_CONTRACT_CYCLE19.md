# API_CONTRACT — цикл 19 ServiceBooking: лимиты только в тарифе, геокодер удалён

**Разделы §400–§416.** Машиночитаемая схема — **`contracts/cycle19/openapi.yaml`**: она источник
истины по **форме**, этот документ — по **смыслу**, порядку проверок и точным текстам. Решения и их
обоснование — `ARCHITECTURE_CYCLE19.md` (§380–§396). Требования — `SPEC_CYCLE19_TARIFF_LIMITS_GEOCODER.md` цикла 19; раздел
«✅ Ответы заказчика (2026-09-28)» приоритетнее предположений.

**Базовая ревизия.** `develop` = `e3774c1`, ветка `cycle/019-tariff-limits-geocoder-cleanup`. Все
ссылки «файл:строки» — по ней.

**Почему файл называется `*_CYCLE19.md`.** Корневой `API_CONTRACT.md` — документ **цикла 3**, на его
разделы ссылается код (`grep -rn "API_CONTRACT.md §"` находит десятки мест). Конвенция проекта —
суффикс цикла (как у циклов 4–18).

---

## §400. Конвенции (без изменений) и два понятия цикла

Без изменений: camelCase; enum — строками; 4xx — голая строка `text/plain` по-русски; 401/403 и
404 без явного тела — пустое тело; текст для человека собирает сервер.

**Опция-лимит** (в коде — `RetiredLimitOptions`). Опция каталога, у которой `CapabilityKey` после
`Trim()` и `ToLowerInvariant()` равен `employees` или `companies`. На сиде это `extra-employees` и
`extra-companies`. Признак вычисляет **только сервер** и **только по `capabilityKey`**, не по `code`.
В ответах API признака «выведена» у самих опций нет: такие опции просто не попадают ни в один список.

**Формула лимита** (единственная в коде — `AccountLimitFormula`):

| | Было (до цикла 19) | Стало |
|---|---|---|
| Сотрудники | `MaxEmployees` тарифа + Σ купленных `employees` + `GrandfatheredEmployeeBonus` | `MaxEmployees` тарифа + `GrandfatheredEmployeeBonus` |
| Компании | `MaxCompanies` тарифа + Σ купленных `companies` | `MaxCompanies` тарифа |

`null` в поле тарифа = без ограничения, бонус к нему не прибавляется. Без годной подписки базой
служит Free (1/1), как и раньше. `PlanOptionRule.IncludedQuantity` в лимит не входил и не входит.

---

## §401. Сводная таблица изменений

| Маршрут | Что меняется | Ломающее? |
|---|---|---|
| `GET /api/admin/options` | опции-лимиты не возвращаются | нет (форма та же) |
| `POST /api/admin/options` | `capabilityKey` employees/companies → **400** | нет (новый отказ на ранее допустимое значение) |
| `PUT /api/admin/options/{id}` | то же **400**; правка выведенной опции → **409** | нет |
| `DELETE /api/admin/options/{id}` | без изменений (выведенная → 204, идемпотентно) | нет |
| `GET /api/admin/option-capabilities` | из списка ушли `companies` и `employees` | нет |
| `POST /api/admin/plans`, `PUT /api/admin/plans/{id}` | правило по выведенной опции молча игнорируется; сохранённое не удаляется; `options`/`optionCoverage` в ответе без опций-лимитов | нет |
| `GET /api/admin/plans` | `options`/`optionCoverage` без опций-лимитов | нет |
| `PUT /api/admin/billing-accounts/{id}/subscription` | опция-лимит → **400**; проверка превышения по новой формуле | нет |
| `GET /api/admin/billing-accounts`, `…/{id}` | лимиты по новой формуле; `options`/`totalMonthlyPrice` без опций-лимитов; у заявки `items[].retired`, `retiredOptionsNotice` | нет (добавочные поля) |
| `GET /api/admin/subscription-requests` | `items[].retired`, `retiredOptionsNotice`; оценка цены без выведенных строк | нет (добавочные поля) |
| `GET /api/billing/subscription` | `options`/`availableOptions`/`totalMonthlyPrice` без опций-лимитов; заявка с `retired`/`retiredOptionsNotice` | нет (добавочные поля) |
| `POST /api/billing/subscription/request` | опция-лимит → **400**; ответ — `SubscriptionRequestDto` с новыми полями | нет |
| `GET /api/pricing`, `GET /api/admin/pricing/preview` | опций-лимитов нет при любых данных | нет |
| `GET /api/profile` (блок `plan`) | лимиты по новой формуле; `totalMonthlyPrice`/`optionCount` без опций-лимитов | нет |
| 402 при добавлении сотрудника, 402 при переносе компании | новый текст без призыва купить опцию | текст (см. §411) |
| `PUT /api/companies/{id}/address` | `verify` игнорируется; ответ `{ company }` — **без `verification`** | **да, для ответа** (§413.2) |
| `POST /api/companies/address/lookup` | **удалён** → 404 всегда | нет на бою (там он и так отвечал 404) |
| `CompanyDto` (все маршруты) | **удалены** `addressVerification`, `addressPoint` | **да** (§413.4) |
| 429 политики `address-verify` | новый текст | текст |

Ни одного нового маршрута. Ни одного нового обязательного поля во входе.

---

## §402. `GET /api/admin/options`

Ответ `200 { options: AdminOptionDto[] }` — форма цикла 7 без изменений. Список **не содержит**
опций-лимитов при любом `IsActive`/`IsPublic`/цене. `subscribedAccounts` у оставшихся опций считается
как раньше.

Из этого списка фронт строит матрицу «тариф × опция» (`PlansTab`) и строки опций в окне назначения
подписки (`BillingAccountsAdminTab`). **Собственного фильтра по `capabilityKey` фронт не заводит**
(почему — `ARCHITECTURE_CYCLE19.md` §386.2).

## §403. `POST /api/admin/options`, `PUT /api/admin/options/{id}`

Порядок проверок для **PUT**:
1. 404 — опции нет;
2. **409 (новое)** — сохранённый `CapabilityKey` этой опции выведенный:
   `Опция «{name}» выведена из оборота: лимиты сотрудников и компаний задаются только тарифом. Изменить её нельзя.`;
3. 400 — «Код опции менять нельзя.» (как было);
4. прежняя валидация (`ValidateOptionInput`), в которую добавлена **новая первая проверка по
   `capabilityKey`** — значение, которое после trim и нижнего регистра равно `employees` или
   `companies`, даёт 400:
   `Возможности «employees» и «companies» нельзя продавать опцией: лимиты сотрудников и компаний задаются только полями тарифа «Макс. сотрудников» и «Макс. компаний».`

**POST**: та же 400 (в составе `ValidateOptionInput`), затем прежний 409 «Опция с кодом … уже
существует.». Свободный текст в `capabilityKey` по-прежнему разрешён для любых других значений.

`DELETE /api/admin/options/{id}` не меняется. На выведенную опцию он отвечает 204 (она и так
`IsActive = false` после миграции).

## §404. `GET /api/admin/option-capabilities`

`200 { capabilities: [{ key, kind, name }] }`. После цикла `key` ∈ {`notifications.whatsapp`,
`analytics`, `online-payment`}. Записи `companies` и `employees` удалены из
`OptionCapabilityCatalog.Known`.

## §405. Тарифы: `GET /api/admin/plans`, `POST /api/admin/plans`, `PUT /api/admin/plans/{id}`

**Вход (`AdminPlanInput.options`)** — полная матрица, как с цикла 7, плюс два правила:
1. элемент с `optionId` выведенной опции **молча отбрасывается**. Ответ 200, правило не создаётся и
   не меняется. Так закэшированная старая вкладка, которая шлёт всю матрицу целиком, продолжает
   сохранять тариф;
2. сохранённое правило по выведенной опции **не удаляется** даже при его отсутствии в `options`.
   Правила `PlanOptionRule` по опциям-лимитам после цикла вообще никто не пишет. Они остаются в
   БД для отчёта заказчику (`ARCHITECTURE_CYCLE19.md` §385.4, часть (а)).

Существование `optionId` проверяется как раньше: несуществующий → 400 «Опция(и) не найдены: …».
Выведенная опция существует и в эту ошибку не попадает.

**Выход (`AdminPlanDto`)**: `options` — правила кроме `Unavailable`, **без опций-лимитов**.
`optionCoverage.configured` и `optionCoverage.total` считаются **без опций-лимитов**. Текст «В тариф
включено {configured} из {total} опций каталога» после выката покажет меньшие числа, например
«1 из 1» вместо «1 из 3». Это ожидаемо: чек-лист C18-1 после цикла требует правило только по
**оставшимся** опциям.

`maxEmployees`/`maxCompanies` — без изменений; это единственный источник лимита.

## §406. `PUT /api/admin/billing-accounts/{accountId}/subscription`

Порядок проверок (новое — п. 8):
1. 404 аккаунт; 2. 400 `paidUntil` в прошлом; 3. 400 нет `paidUntil` у платного тарифа;
4. 409 «Заявка уже обработана.»; 5. 404 «Тариф не найден.»; 6. 409 `TrialPlanNotAssignableHere` (JSON);
7. 400 «Одна или несколько опций не найдены.»;
8. **400 (новое)** — в `options` есть выведенная опция:
   `Опция «{name}» больше не подключается: лимиты сотрудников и компаний задаются только тарифом.`
   (`{name}` — первой встреченной такой опции). Ничего не сохраняется;
9. 400 количество; 10. 409 «Опция недоступна на выбранном тарифе.»;
11. 409 превышение лимита без `confirmLimitOverflow` (тексты без изменений).

**Проверка превышения (п. 11)** считает по формуле §400 для **назначаемого** тарифа (или Free, если
`planId = null`):
- компании: `companiesUsed > MaxCompanies` → 409;
- сотрудники: `employeesUsed > MaxEmployees + GrandfatheredEmployeeBonus` → 409;
- `null` → проверки нет. Количества опций из запроса **не прибавляются**.

Контрольные примеры (обязательные тесты): тариф 5 сотрудников, бонус 2, занято 7 → 200; занято 8 → 409.

**Побочные эффекты записи**: строки `AccountSubscriptionOption` выведенных опций этот маршрут **не
трогает никогда** — ни при добавлении, ни при закрытии «опций, не попавших в запрос»
(`EndsAtUtc`). Сводка опций в журнале (`OldOptionsSummary`/`NewOptionsSummary`) строится без
выведенных опций. Старые записи журнала не переписываются и показывают `extra-*` под их именами.

Ответ 200 — карточка аккаунта (§407).

## §407. Карточка и список биллинг-аккаунтов, очередь заявок

**`GET /api/admin/billing-accounts/{accountId}`** (и ответ §406):
- `employeesLimit`/`companiesLimit` — по формуле §400 для действующего тарифа;
- `options` — подключённые опции **без опций-лимитов**; `totalMonthlyPrice` — тоже без них;
- `grandfatheredEmployeeBonus`/`grandfatheredEmployeeBonusText` — без изменений;
- `pendingRequest` — `SubscriptionRequestDto` с новыми полями (§408).

**`GET /api/admin/billing-accounts`** (список): `employeesLimit`/`companiesLimit` по новой формуле,
`totalMonthlyPrice` без опций-лимитов. Остальное без изменений.

**`GET /api/admin/subscription-requests`**: у каждого элемента
- `items[]` получает **`retired: boolean`** (обязательное);
- новое поле **`retiredOptionsNotice: string | null`** — тот же текст, что в §408;
- `estimatedMonthlyPrice` — **без** строк `retired = true`.

**Одобрение заявки с выведенными строками.** Отдельного поведения на сервере нет. Окно одобрения
строит строки опций из `GET /api/admin/options` (§402), где выведенных опций нет. Поэтому
выведенные строки в тело `PUT …/subscription` не попадают. Суперадмин видит `retiredOptionsNotice`
в окне **до** подтверждения (FE-3). Если закэшированный фронт всё-таки пришлёт такую строку, он
получит 400 из §406 п. 8. Отклонение заявки (`POST …/reject`) не меняется.

## §408. Владелец: `GET /api/billing/subscription`, `POST /api/billing/subscription/request`

**`GET /api/billing/subscription`**:
- `options` (подключённые) и `availableOptions` (можно запросить) — **без опций-лимитов**;
- `totalMonthlyPrice` — без них;
- `usage.employeesLimit`/`usage.companiesLimit` и тексты `employeesText`/`companiesText` — по
  формуле §400;
- `pendingRequest` (`SubscriptionRequestDto`) — **два добавочных поля**:
  - `items[].retired: boolean` — `true` у строки про опцию-лимит (заявка подана до выката);
  - `retiredOptionsNotice: string | null` — если есть хотя бы одна такая строка:
    `В заявке есть опции, которые больше не подключаются: «{имя1}», «{имя2}». Лимиты сотрудников и компаний задаются только тарифом, поэтому при одобрении заявки эти опции применены не будут.`
    иначе `null`;
  - `estimatedMonthlyPrice` — без строк `retired = true`.
- `overLimitCompanies`/`overLimitEmployees`/`overLimitText` (цикл 18, Т4) — считаются от лимитов по
  новой формуле. Условие «только при фактическом Free» не меняется.

**`POST /api/billing/subscription/request`**. Новая проверка — после «Опция … не найдена», до
проверки переключателя: опция-лимит в `options` → **400**
`Опция «{name}» больше не подключается: лимиты сотрудников и компаний задаются только тарифом.`
Ничего не сохраняется, прежняя заявка остаётся как была. Ответ 200 — `SubscriptionRequestDto` с
полями выше (у новой заявки `retiredOptionsNotice` всегда `null`).

`RequestedOptionsJson` старых заявок **не переписывается**: пометка `retired` вычисляется при
чтении.

## §409. Витрина: `GET /api/pricing`, `GET /api/admin/pricing/preview`

Форма без изменений. `options` **никогда** не содержит опций-лимитов — даже активных, публичных и с
ценой. `plans[].includedEmployees`/`includedCompanies` — ровно поля тарифа (как и было).

## §410. Профиль: блок `plan` в `GET /api/profile`

Форма без изменений. `maxEmployees`/`maxCompanies` — по формуле §400. `totalMonthlyPrice` и
`optionCount` — без опций-лимитов.

## §411. Тексты отказов по лимитам (402)

| Где | Код | Текст после цикла |
|---|---|---|
| `POST /api/companies` сверх лимита компаний | 402 | `Открыто {used} из {limit} точек, доступных на вашем тарифе.` (**без изменений**) |
| `POST /api/companies/{id}/members` сверх лимита мест | 402 | `Занято {used} из {limit} мест — столько включено в тариф «{planName}». Лимит общий на все ваши точки. Чтобы добавить сотрудника, выберите тариф с большим лимитом в разделе «Ваша подписка».` |
| перенос компании (`CompanyTransferService`), лимит принимающего аккаунта | 402 | `На тарифе «{planName}» — {limit} {компания/компании/компаний}, занято {used}. Чтобы принять ещё одну, назначьте принимающему аккаунту тариф с большим лимитом компаний.` |

`{limit}` в тексте про места уже включает бонус аккаунта: владельцу бонус отдельной строкой не
называется, как и раньше (решение цикла 7, §54.4). Слова «докуплено» и призывов «подключите опцию
«Дополнительные сотрудники»/«Дополнительная компания»» больше нет нигде. Флаг
`CompanyDto.canAddEmployee` и `accountSeatsLimit` считаются по формуле §400.

## §412. Совместимость на время выката (что получит закэшированный фронт)

| Кто | Шлёт | Получит |
|---|---|---|
| старая вкладка «Тарифы» | полную матрицу с правилами `extra-*` | 200, правила `extra-*` проигнорированы, остальное сохранено |
| старое окно назначения подписки | строки опций из нового `GET /api/admin/options` (без `extra-*`) | 200; при ручной подстановке `extra-*` — 400 с текстом |
| старый `BillingPage` | заявку из нового `availableOptions` (без `extra-*`) | 200 |
| старое поле адреса | `PUT …/address` с `verify: true` | 200 `{ company }`. Старый код читает `result.verification` → `undefined`, все его чтения защищены `saveVerification && …`, экран не ломается |
| старое поле адреса | читает `company.addressVerification?.available` | `undefined` → «проверка недоступна», кнопка не рисуется |
| старый `CabinetPage` после создания компании | `PUT …/address` с `verify: true`, ошибки глотает | 200, адрес не меняется (тот же текст) |
| кто угодно | `POST /api/companies/address/lookup` | 404 (на бою он и был 404) |

Обратное окно (новый фронт при ещё старом API, пока `deploy-remote.sh` перезапускает контейнер) —
`ARCHITECTURE_CYCLE19.md` §386.2 и §387. Все новые поля фронт читает как необязательные.

## §413. Адрес компании

### §413.1 Что НЕ меняется

- `POST /api/companies` пишет `Address` как прежде.
- `PUT /api/companies/{id}` принимает `address` (`null` = не трогать), как прежде.
- `POST /api/companies/address/notice` — форма, коды, запись в `ConsentRecord`
  (`ConsentSource.AddressForm`, `LegalTextKey.PublicAddressNotice`) без изменений.
- Политика лимитов **`address-verify`** на `PUT …/address` и `POST …/address/notice` — **имя не
  меняется**, значения по умолчанию прежние (30 за 60 мин), ключи конфигурации
  `RateLimits:address-verify:*` (и боевые переопределения `RATELIMITS__ADDRESS-VERIFY__*`, если
  есть) действуют как прежде. Меняется только текст 429:
  `Слишком много попыток изменить адрес. Повторите позже.`
- Ссылки в карты (`yandexMapsUrl`, `twoGisUrl`) и поиск компании по адресу — без изменений.

### §413.2 `PUT /api/companies/{id}/address` — BREAKING для формы ответа

Вход: `{ "address": string (≤ 300), "verify"?: boolean }`. `verify` **принимается и игнорируется**.
Порядок ответов: 401 → 404 (компании нет) → 403 (не владелец и не суперадмин) → 400 (> 300
символов) → 200. Адрес пишется побайтно; пустая строка → `null`.

Ответ **200: `{ "company": CompanyDto }`**. Поля `verification` **больше нет**. Пять колонок
проверки адреса (`AddressVerifiedInputKey`, `AddressVerifiedAt`, `AddressPrecision`,
`AddressLatitude`, `AddressLongitude`) этот маршрут **не пишет**. Раньше он их обнулял при каждом
сохранении, теперь они остаются нетронутыми в БД.

### §413.3 `POST /api/companies/address/lookup` — удалён

Контроллерного действия нет. Ответ **404 с пустым телом** при любой конфигурации, в том числе при
`AddressVerification:Provider=yandex` и при любых значениях `CacheHours`/`MaxCandidates` в окружении.

### §413.4 `CompanyDto` — BREAKING (поле удалено)

Из `CompanyDto` удалены **`addressVerification`** и **`addressPoint`**, во всех маршрутах, которые
отдают компанию (`/api/companies`, `/public`, `/my`, `/member`, `/{slug}`, `POST`, `PUT /{id}`,
`/{id}/logo`, `PUT /{id}/address`). Единственный потребитель — наш фронт. Во внешнем справочнике
`API_DOCUMENTATION.md` этих полей не было. В схеме это отмечено `x-removed-properties`. Отсутствие
проверяет функциональный тест **ADDR-032**.

## §414. Все новые и изменённые тексты (сервер, `BillingTexts` и `Program.cs`)

| Ключ (C#) | Код | Текст |
|---|---|---|
| `BillingTexts.LimitCapabilityNotSellable` | 400 | Возможности «employees» и «companies» нельзя продавать опцией: лимиты сотрудников и компаний задаются только полями тарифа «Макс. сотрудников» и «Макс. компаний». |
| `BillingTexts.RetiredOptionNotEditable(name)` | 409 | Опция «{name}» выведена из оборота: лимиты сотрудников и компаний задаются только тарифом. Изменить её нельзя. |
| `BillingTexts.RetiredOptionRejected(name)` | 400 | Опция «{name}» больше не подключается: лимиты сотрудников и компаний задаются только тарифом. |
| `BillingTexts.RetiredOptionsInRequestNotice(names)` | поле | В заявке есть опции, которые больше не подключаются: «{имя1}», «{имя2}». Лимиты сотрудников и компаний задаются только тарифом, поэтому при одобрении заявки эти опции применены не будут. |
| `BillingTexts.SeatLimitReached(used, planName, limit)` | 402 | Занято {used} из {limit} мест — столько включено в тариф «{planName}». Лимит общий на все ваши точки. Чтобы добавить сотрудника, выберите тариф с большим лимитом в разделе «Ваша подписка». |
| `BillingTexts.TransferRejectedCompanyLimit(...)` | 402 | На тарифе «{planName}» — {limit} {компания/компании/компаний}, занято {used}. Чтобы принять ещё одну, назначьте принимающему аккаунту тариф с большим лимитом компаний. |
| `Program.cs`, 429 политики `address-verify` | 429 | Слишком много попыток изменить адрес. Повторите позже. |

Имена опций в текстах — `SubscriptionOption.Name` из БД, в «ёлочках». Несколько имён — через
«, » в порядке строк заявки, без повторов.

## §415. Как QA сверяет бэкенд и фронт с контрактом (без ручных тестов)

1. `npx @redocly/cli lint --config contracts/redocly.yaml contracts/cycle19/openapi.yaml` — ноль ошибок.
2. `cd frontend && npm run types:api:cycle19 && git diff --exit-code src/types/api-cycle19.generated.ts`
   — фронт собран по этой схеме. `npx tsc --noEmit` зелёный: фронт реально использует эти типы.
3. `schemathesis run contracts/cycle19/openapi.yaml --base-url http://localhost:5000 --checks all`
   с токеном суперадмина и токеном владельца (два прогона) — ответы сервера по форме совпадают со
   схемой. `additionalProperties: false` у `CompanyAddressUpdateResultDto`,
   `SubscriptionRequestDto`, `SubscriptionRequestItemDto`, `AdminSubscriptionRequestDtoCycle19`,
   `AdminOptionDto`, `CapabilityDto` ловит лишние или оставшиеся поля (в т. ч. `verification`).
4. `POST /api/companies/address/lookup` в схеме описан только кодом 404 — schemathesis падает, если
   маршрут отвечает чем-то другим.
5. Фронт до бэкенда: `npx @stoplight/prism mock contracts/cycle19/openapi.yaml --port 4019`.

## §416. Ломающие изменения цикла — сводно

1. **`CompanyDto` без `addressVerification` и `addressPoint`** (§413.4). Потребитель только наш фронт.
2. **`PUT /api/companies/{id}/address` — ответ без `verification`** (§413.2). Обёртка `{ company }`
   сохранена, чтобы старый фронт не ломался на `result.company`.
3. Тексты 402 (места, перенос) и 429 (`address-verify`) — новые формулировки (§411, §414).

Всё остальное — добавочные поля, новые отказы на данные, которые больше не имеют смысла, и
смысловое сужение списков.
