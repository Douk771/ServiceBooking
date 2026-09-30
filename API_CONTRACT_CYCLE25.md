# API_CONTRACT — цикл 25 ServiceBooking: «Заказы», цикл 3 «Аналитика, каталог по городу и MAX для персонала»

**Разделы §520–§542.** Точка синхронизации backend-, frontend-, devops-инженера и QA на время цикла. Решения —
`ARCHITECTURE_CYCLE25.md` (§495–§519), требования — `SPEC.md` цикла 25.

**Источник истины по ФОРМЕ** — `contracts/cycle25/openapi.yaml`. Здесь — то, чего схема не выражает: порядок проверок,
тексты, какие коды когда, изменения поведения существующих маршрутов. При расхождении по форме прав YAML, по смыслу —
этот файл.

```bash
npx @stoplight/prism mock contracts/cycle25/openapi.yaml --port 4025              # фронт — мок
cd frontend && npm run types:api:cycle25                                          # -> src/types/api-cycle25.generated.ts
schemathesis run contracts/cycle25/openapi.yaml --base-url http://localhost:5000 --checks all   # QA
npx @redocly/cli lint --config ../contracts/redocly.yaml ../contracts/cycle25/openapi.yaml       # CI
```

**Базовая ревизия** — `f739382`. Существующий API — `API_DOCUMENTATION.md` §4.20 и контракты циклов 23–24
(`API_CONTRACT_CYCLE23.md` §406–§425, `API_CONTRACT_CYCLE24.md` §470–§494, `contracts/cycle23/`, `contracts/cycle24/`).
Маршруты циклов 23–24, не перечисленные в §521, не меняются.

---

## §520. Конвенции

Действуют **без изменений** все конвенции `API_CONTRACT_CYCLE24.md` §470 и `API_CONTRACT_CYCLE23.md` §406:

- camelCase; enum — строкой; `DateTime` — ISO-8601 UTC; `DateOnly` — `"YYYY-MM-DD"`; время суток — `"HH:mm"`
  (локальное время магазина).
- 400/402/429 — голая строка по-русски. 401/403 — пустое тело. 404 — пустое тело, не оракул.
- 409 домена магазина/заказов — JSON (`CatalogConflictDto`). 409 аккаунтных маршрутов (`api/staff-max`) — строка.
- Текст, который видит человек (итоги, подписи периодов, доли, «по состоянию на…», состояние в каталоге), собирает
  сервер; фронт печатает его как есть.

Дополнительно в цикле 25:

- **Телефон покупателя никогда не передаётся в пути или query-строке** новых маршрутов. Поиск по покупателю — только в
  теле `POST …/order-history`. Карточка покупателя адресуется `customerRef` (id заказа).
- **«Сегодня» = текущий рабочий день магазина** (§470 цикла 24: интервал через полночь относится ко дню начала) — во
  всех новых маршрутах и во всех текстах уведомлений (T-25-04).
- **Периоды отчётов считает сервер** (§522). Фронт передаёт пресет (и `from`/`to` у `Custom`), сервер возвращает
  разрешённые даты и подпись.
- Деньги — `number` (рубли с копейками, как в заказах). Весовые количества — целые **граммы**, штучные — штуки; текст
  количества (`quantityText`) собирает сервер («3 шт», «1,2 кг», «650 г»).

---

## §521. Сводка маршрутов

| Маршрут | Новый/изм. | Доступ | Rate limit | 451 |
|---|---|---|---|---|
| `GET /api/staff-max` | новый | вошедший | — | глоб. |
| `POST /api/staff-max/link-sessions` | новый | вошедший, участник ≥ 1 активного магазина | `staff-max-link` 10/ч на пользователя | глоб. |
| `DELETE /api/staff-max/link` | новый | вошедший | — | глоб. |
| `GET/PUT /api/shops/{id}/notification-settings` | изм.: `staffMaxEnabled`, `staffMaxAvailable`, `staffMaxUnavailableText` | GET персонал, PUT владелец | — | PUT владельч. |
| `POST /api/shops/{id}/order-history` | новый | персонал | `shop-reports` 120/мин на пользователя | глоб. |
| `GET /api/shops/{id}/summary` | новый | владелец, SuperAdmin | `shop-reports` | глоб. |
| `GET /api/shops/{id}/picklist` | новый | персонал | `shop-reports` | глоб. |
| `GET /api/shops/{id}/customers/{customerRef}` | новый | персонал | `shop-reports` | глоб. |
| `GET/PUT /api/shops/{id}/customers/{customerRef}/note` | новый | персонал | `shop-reports` | глоб. |
| `GET /api/shops/{id}/catalog-listing` | новый | персонал | — | глоб. |
| `PUT /api/shops/{id}/catalog-listing` | новый | владелец | — | владельч. |
| `GET /api/goods/catalog` | новый | все (анонимно) | `goods-catalog` 120/мин по IP | — |
| `POST /api/phone-verification/max/webhook/{token}` | изм. поведения: апдейты с payload `sm1.` и `bot_stopped` (§523.4) | MAX | как было | — |
| `GET /api/orders/my`, `GET /api/orders/public/{token}` | изм. поведения: `pickup.text` от рабочего дня (T-25-04) | как было | как было | как было |
| `POST/PUT …/products`, `PUT …/categories/{id}/weekdays` | изм.: повтор дня недели → 400 (T-25-03) | как было | — | как было |
| `GET/POST/PUT /api/admin/plans` | изм. смысла: `allowPublicListing` у тарифа «Заказов» = показ в каталоге goods | SuperAdmin | — | глоб. |

«персонал» = владелец, сотрудник, SuperAdmin. Порядок проверок прав — как в цикле 23: токен → существование и тип
(салон/нет → 404) → роль (403 пустым телом).

---

## §522. Отчётные периоды — `ReportPeriodPreset`

Вход (query у GET, поля тела у POST): `period` (по умолчанию см. маршрут), `from`, `to` (только при `Custom`).

| `period` | Даты (по `PickupDate`, включительно) | `label` (пример при рабочем дне 30 сен 2026) |
|---|---|---|
| `Today` | рабочий день | «Сегодня, 30 сен» |
| `Yesterday` | рабочий день − 1 | «Вчера, 29 сен» |
| `Last7Days` | −6 … рабочий день | «7 дней: 24–30 сен» |
| `Last30Days` | −29 … рабочий день | «30 дней: 1–30 сен» |
| `ThisMonth` | весь месяц рабочего дня | «Сентябрь 2026» |
| `LastMonth` | весь прошлый месяц | «Август 2026» |
| `Custom` | `from` … `to` | «15 авг – 30 сен 2026» |

Ответ — `ReportPeriodDto { preset, from, to, label, days }`.

| Проверка | Ответ 400 |
|---|---|
| `Custom` без `from` или `to` | «Укажите начало и конец периода» |
| `to < from` | «Конец периода раньше начала» |
| длина > 366 дней | «Период — не длиннее 366 дней» |
| `from`/`to` переданы без `Custom` | игнорируются (не ошибка) |

---

## §523. Сообщения персоналу в MAX

### §523.1 `GET /api/staff-max` → `StaffMaxStatusDto`

```json
{ "available": true, "canLink": true, "unavailableText": null,
  "eligible": true,
  "status": "Linked", "statusText": "Подключено 30.09.2026",
  "linkedAtUtc": "2026-09-30T09:12:00Z", "stoppedAtUtc": null,
  "pendingSession": null,
  "shops": [ { "shopId": "…", "name": "Шаурма на Ленина", "staffMaxEnabled": true } ],
  "pollIntervalSeconds": 2 }
```

| Поле | Смысл |
|---|---|
| `available` | сообщения включены на платформе (`Notifications:StaffMax:Enabled` ∧ бот MAX) |
| `canLink` | можно получить ссылку (дополнительно — вебхук бота подписан) |
| `unavailableText` | при `!available`: «Сообщения в MAX пока не включены на платформе»; при `available ∧ !canLink`: «Подключение к MAX временно недоступно, попробуйте позже» |
| `eligible` | вызывающий — владелец или сотрудник хотя бы одного активного магазина. `false` → фронт **не показывает блок** |
| `status` | `NotLinked` \| `Pending` (есть незавершённая неистёкшая сессия) \| `Linked` \| `StoppedInMax` |
| `statusText` | «Не подключено» · «Ждём подтверждения в MAX…» · «Подключено 30.09.2026» · «Отключено: бот остановлен в MAX» |
| `pendingSession` | `{ sessionId, expiresAtUtc }` при `Pending` — чтобы после перезагрузки страницы фронт продолжил опрос (ссылку заново не отдаём: payload не хранится) |
| `shops` | магазины, из которых будут приходить сообщения, с флагом магазина |

Идентификатор чата MAX, `ChatKey` и любые его производные **не отдаются никогда**.

### §523.2 `POST /api/staff-max/link-sessions` → 201 `StaffMaxLinkSessionDto`

Тела нет. Порядок проверок:

| # | Проверка | Ответ |
|---|---|---|
| 1 | токен | 401 |
| 2 | лимит `staff-max-link` | 429 «Слишком много запросов — подождите минуту» |
| 3 | не участник ни одного активного магазина | 409 «Подключить MAX могут владельцы и сотрудники магазинов» |
| 4 | `!available` | 409 «Сообщения в MAX пока не включены на платформе» |
| 5 | `!canLink` | 409 «Подключение к MAX временно недоступно, попробуйте позже» |
| 6 | создать сессию (прежние незавершённые — удалить) | 201 |

```json
{ "sessionId": "…", "deepLink": "https://max.ru/<бот>?start=sm1.…", "webLink": "https://web.max.ru/<бот>?start=sm1.…",
  "qrPngBase64": "iVBORw0…", "expiresAtUtc": "2026-09-30T09:22:00Z", "ttlSeconds": 600, "pollIntervalSeconds": 2 }
```

- `qrPngBase64` может быть `null` (сбой генерации QR не блокирует).
- Фронт: на телефоне — кнопка-ссылка `deepLink` и вторая «Открыть в веб-версии MAX» (`webLink`); на компьютере — QR и
  ссылка текстом (как подтверждение телефона). Затем опрос `GET /api/staff-max` каждые `pollIntervalSeconds`, пока
  `status = Pending` и не истёк `expiresAtUtc`. После истечения — «Ссылка устарела» и кнопка «Получить новую».
- Повторное подключение при `Linked` разрешено: новый чат **заменяет** прежний (Q-25-3).

### §523.3 `DELETE /api/staff-max/link` → 204

Идемпотентно (204 и без привязки). Удаляет привязку и незавершённые сессии. Сообщения прекращаются сразу, включая
стоящие в очереди.

### §523.4 Вебхук бота (поведение, форма не меняется)

`POST /api/phone-verification/max/webhook/{token}` отвечает как в цикле 14 (200 всегда при верном токене).

| Апдейт | Поведение |
|---|---|
| `bot_started`, payload начинается с `sm1.` | привязка персонала (`ARCHITECTURE_CYCLE25.md` §498.2), ответ бота — §525.2 |
| `bot_started`, любой другой payload | **как в цикле 14** (подтверждение телефона) |
| `bot_stopped` | все привязки этого чата → «Отключено: бот остановлен в MAX»; ответа в чат нет |
| прочее | как в цикле 14 (лог) |

Подписка вебхука: `update_types = [bot_started, message_created, bot_stopped]`; если платформа не примет — повтор со
старым списком из двух типов.

---

## §524. Уведомления магазина — `GET/PUT /api/shops/{id}/notification-settings` (изменения)

`ShopNotificationSettingsDto` цикла 24 + три поля:

| Поле | Смысл |
|---|---|
| `staffMaxEnabled` | флаг магазина «Сообщения сотрудникам в MAX» (`ShopSettings.StaffMaxEnabled`, по умолчанию `true`) |
| `staffMaxAvailable` | сообщения в MAX включены на платформе |
| `staffMaxUnavailableText` | при `!staffMaxAvailable`: «Сообщения в MAX пока не включены на платформе» |

`ShopNotificationSettingsInput` + необязательный `staffMaxEnabled` (`null`/не передан — **не менять**; фронт цикла 24
продолжает работать). Сохранить `staffMaxEnabled` можно и при `staffMaxAvailable = false` (настройка магазина
независима от рубильника). Выключение останавливает и стоящие в очереди сообщения магазина. Остальное — как §483
цикла 24.

---

## §525. Тексты MAX (фиксированные, собирает сервер) — [legal L15]

### §525.1 Сообщения сотрудникам

Одна строка события + строка ссылки. Ссылка — абсолютная, только из `PublicSiteLinks`. **Имени и телефона покупателя
нет.** Время — от рабочего дня магазина (T-25-04): «к 12:30» сегодня, «пт 2 окт, к 12:30» — другой день, «как можно
скорее» — ASAP. Название магазина — до 60 символов (длиннее — «…»).

| Тип | Текст |
|---|---|
| `StaffOrderCreated` | `Новый заказ № 27 · к 12:30 · 3 позиции · ≈ 540 ₽ · Шаурма на Ленина`<br>`Открыть: https://goods.ezbook.ru/cabinet/<shopId>/orders?order=<orderId>` |
| `StaffOrderCancelledByCustomer` | `Покупатель отменил заказ № 27 (к 12:30) · Шаурма на Ленина`<br>`Открыть: https://goods.ezbook.ru/cabinet/<shopId>/orders?order=<orderId>` |
| `OwnerOrderLimitWarning` (P1) | `Использовано 80 % лимита заказов: 120 из 150 заказов в октябре` / `Лимит заказов исчерпан: 150 из 150 в октябре. Новые заказы не принимаются до 1 ноября или смены тарифа`<br>`Подписка: https://goods.ezbook.ru/cabinet/subscription` |

«≈» — как в push: итог нового заказа всегда ориентир. Числительное позиций — «позиция/позиции/позиций».

### §525.2 Ответы бота при подключении (нейтральные до заключения L15)

| Случай | Текст |
|---|---|
| подключено | `Готово: сюда будут приходить новые заказы и отмены покупателями из магазинов: «Шаурма на Ленина», «Пекарня». Отключить можно в кабинете goods.ezbook.ru → «Уведомления на это устройство» или остановив этого бота.` |
| уже подключено этой ссылкой | `Этот чат уже подключён к заказам ezbook.` |
| ссылка устарела / неизвестна | `Ссылка устарела. Получите новую в кабинете goods.ezbook.ru → «Уведомления на это устройство».` |
| функция выключена | `Сообщения о заказах в MAX пока не включены.` |
| нет магазинов | `Вы больше не состоите ни в одном магазине — подключать нечего.` |

Список магазинов в тексте — до 5 названий, дальше «и ещё N».

---

## §526. История заказов — `POST /api/shops/{id}/order-history`

Тело `OrderHistoryQuery`:

```json
{ "period": "Last7Days", "from": null, "to": null,
  "statuses": ["Issued", "NotPickedUp"], "customer": "1234",
  "amountFrom": 100, "amountTo": null, "number": null,
  "sort": "PickupDesc", "page": 1 }
```

| Поле | Правило |
|---|---|
| `period` | по умолчанию `Last7Days` (§522) |
| `statuses` | пусто/не передано — все восемь |
| `customer` | ≤ 100 символов. **Телефон**, если после удаления пробелов, `+`, `(`, `)`, `-` остались только цифры и их ≥ 4: поиск подстроки цифр в номере. Иначе **имя**: ≥ 2 символов, подстрока без учёта регистра. Меньше — 400. Обезличенные заказы не находятся |
| `amountFrom`/`amountTo` | по итогу заказа (у выданного — фактический, у остальных — ориентир); ≥ 0; `amountFrom > amountTo` → 400 |
| `number` | точный номер, 1…9999; ищется **внутри периода**, совпадения разных дней — все |
| `sort` | `PickupDesc` (по умолчанию) \| `PickupAsc` — по времени получения |
| `page` | ≥ 1; страница 50 строк; за пределами — пустой `items` |

Ответ `OrderHistoryPageDto`:

```json
{ "period": { "preset": "Last7Days", "from": "2026-09-24", "to": "2026-09-30", "label": "7 дней: 24–30 сен", "days": 7 },
  "items": [ { "orderId": "…", "number": 27, "pickupDate": "2026-09-30", "pickupStartUtc": "…",
               "pickupText": "30 сен, к 12:30", "status": "Issued", "statusText": "Выдан",
               "customerName": "Анна", "customerPhoneMasked": "+7 (···) ···-12-34",
               "itemCount": 3, "total": 540.00, "totalIsApproximate": false, "personalDataErased": false } ],
  "page": 1, "pageSize": 50, "totalCount": 128, "issuedCount": 101, "issuedAmount": 54300.00,
  "summaryText": "Найдено 128 заказов, выдано на 54 300 ₽", "emptyText": null }
```

- `pickupText` — дата и время по поясу магазина: «30 сен, к 12:30» (слот), «30 сен, ≈ 12:20» (ASAP).
- Обезличенный заказ: `customerName = null`, `customerPhoneMasked = null`, `personalDataErased = true`; фронт пишет
  «Данные покупателя удалены» и не даёт перейти в карточку покупателя.
- Пусто: `items = []`, `totalCount = 0`, `emptyText` — «За выбранный период заказов нет» (без фильтров, кроме
  периода) или «По этим условиям заказов нет» (есть фильтры).
- Полный телефон — только в существующей карточке заказа `GET …/orders/{orderId}`.

| 400 | Текст |
|---|---|
| период | §522 |
| покупатель короче минимума | «Введите не меньше 2 букв имени или 4 цифр телефона» |
| сумма | «Сумма не может быть отрицательной» · «Сумма «от» больше суммы «до»» |
| номер | «Номер заказа — число от 1 до 9999» |
| страница | «Номер страницы — от 1» |

---

## §527. Сводка — `GET /api/shops/{id}/summary?period=&from=&to=&top=&compare=`

Доступ: владелец, SuperAdmin. Сотрудник → **403** (пустое тело). `period` по умолчанию `Today`; `top` = `Amount`
(по умолчанию) \| `Quantity`; `compare` — P1, по умолчанию `false`.

```json
{ "period": { "preset": "Today", "from": "2026-09-30", "to": "2026-09-30", "label": "Сегодня, 30 сен", "days": 1 },
  "ordersTotal": 42, "inProgress": 5, "inProgressText": "из них в работе: 5",
  "issuedCount": 30, "issuedAmount": 16250.00, "paymentNote": "Оплата на месте, платформа её не видит",
  "averageCheck": 541.67, "averageCheckText": "541,67 ₽",
  "terminalCount": 37,
  "cancellations": [
    { "status": "Rejected", "label": "Отклонён магазином", "count": 1, "share": 0.027, "shareText": "3 %" },
    { "status": "CancelledByShop", "label": "Отменён магазином", "count": 2, "share": 0.054, "shareText": "5 %" },
    { "status": "CancelledByCustomer", "label": "Отменён покупателем", "count": 3, "share": 0.081, "shareText": "8 %" } ],
  "notPickedUp": { "status": "NotPickedUp", "label": "Не забран", "count": 1, "share": 0.027, "shareText": "3 %" },
  "top": { "sort": "Amount", "items": [
    { "productId": "…", "name": "Борщ", "unit": "Piece", "quantity": 24, "quantityText": "24 шт", "amount": 7200.00, "isDeleted": false },
    { "productId": "…", "name": "Сыр", "unit": "Weight", "quantity": 3450, "quantityText": "3,45 кг", "amount": 2415.00, "isDeleted": false } ] },
  "days": null, "previous": null }
```

- Все показатели — по `PickupDate` в периоде (Q-25-4).
- Доли — от `terminalCount` (заказы периода в конечных статусах); при `terminalCount = 0` — `share = null`,
  `shareText = "—"`.
- `averageCheck` = `issuedAmount / issuedCount` по правилам денег заказа; при 0 выданных — `null`, текст «—».
- `top.items` — до 10, только выданные заказы. `Quantity`: штучные сравниваются в штуках, весовые — в кг. Имя —
  текущее у существующего товара, у удалённого — из последнего заказа (`isDeleted = true`).
- **P1 `days`** (при `days > 1`): `[{ date, label: "пн 28 сен", orders, issuedCount, issuedAmount }]` по возрастанию;
  при `days = 1` — `null`.
- **P1 `previous`** (при `compare=true`): `{ period, ordersTotal, issuedCount, issuedAmount, averageCheck,
  ordersDeltaText, issuedCountDeltaText, issuedAmountDeltaText, averageCheckDeltaText }`. Разница — «+12 %», «−3 %»,
  «0 %»; при нулевой базе — «—». Без `compare` — `null`.
- **Проверяемый критерий:** `ordersTotal` и `issuedAmount` за `Today` равны `totalCount` и `issuedAmount` истории с
  тем же периодом без фильтров.

400 — только §522 и «Неизвестный вид сортировки».

---

## §528. Лист сборки — `GET /api/shops/{id}/picklist?date=&from=&to=&includeNew=`

| Параметр | Правило |
|---|---|
| `date` | дата выдачи; по умолчанию — текущий рабочий день. Будущее — не дальше `рабочий день + preorderDays`, иначе 400 «Дата — не позже {дата}» |
| `from`, `to` | `"HH:mm"` локального времени рабочего дня `date`, оба или ни одного (иначе 400 «Укажите начало и конец интервала»). `to ≤ from` — через полночь; `to = "00:00"` — до полуночи. Не переданы — весь день |
| `includeNew` | по умолчанию `true` |

В лист попадают заказы `pickupDate = date` со статусом `Accepted` (и `New` при `includeNew`), чей `pickupStartUtc`
(у ASAP — ориентир) попадает в `[from, to)`. `Ready` и конечные статусы — нет.

```json
{ "shopName": "Шаурма на Ленина", "date": "2026-09-30", "dateLabel": "Среда, 30 сентября",
  "from": "12:00", "to": "12:15", "intervalLabel": "12:00–12:15", "includeNew": true,
  "generatedAtUtc": "2026-09-30T08:42:10Z", "generatedAtText": "по состоянию на 11:42",
  "orderCount": 6,
  "slots": [ { "from": "12:00", "to": "12:15", "label": "12:00–12:15" } ],
  "byProduct": [
    { "productId": "…", "name": "Борщ", "categoryName": "Супы", "unit": "Piece", "totalQuantity": 5,
      "quantityText": "5 шт", "orderCount": 4, "weightBreakdownText": null, "hasUnaccepted": true },
    { "productId": "…", "name": "Сыр", "categoryName": "Гастрономия", "unit": "Weight", "totalQuantity": 2350,
      "quantityText": "2,35 кг", "orderCount": 3, "weightBreakdownText": "3 заказа: 500 г, 1,2 кг, 650 г", "hasUnaccepted": false } ],
  "byTime": [
    { "from": "12:00", "to": "12:15", "label": "12:00–12:15",
      "orders": [ { "orderId": "…", "number": 27, "status": "Accepted", "isUnaccepted": false, "pickupText": "к 12:00",
                    "comment": "без лука", "lines": [ { "name": "Борщ", "unit": "Piece", "quantity": 2, "quantityText": "2 шт", "portionText": "350 мл" } ] } ] } ],
  "emptyText": null }
```

- `slots` — вся сетка дня (шаг магазина, внутри часов, без отсечения прошедших) — для выбора интервала. Выходной —
  пустой массив.
- `byTime`: группы по слотам сетки; ASAP — в слот, содержащий ориентир (`pickupText` «≈ 12:20»); вне сетки — группа
  `label: "Вне расписания"`.
- `isUnaccepted`/`hasUnaccepted` — строки заказов «Новый» (фронт помечает «Не принят»).
- **Имени и телефона покупателя в ответе нет** [legal L18]. Комментарий к заказу — есть.
- Пусто: `orderCount = 0`, `emptyText` — «На 12:00–12:15 заказов нет» / «На 30 сентября заказов нет».

---

## §529. Карточка покупателя — `GET /api/shops/{id}/customers/{customerRef}?page=`

`customerRef` — id **любого** заказа этого покупателя в этом магазине (фронт берёт id открытого заказа). 404
(пустое тело), если заказа нет, он другого магазина или обезличен (`customerPhone = null`).

```json
{ "customerRef": "…", "name": "Анна", "phone": "+79991234567", "phoneDisplay": "+7 999 123-45-67", "phoneVerified": true,
  "ordersTotal": 14, "issuedCount": 11, "issuedAmount": 6120.00, "cancelledByCustomer": 1, "notPickedUp": 1,
  "firstOrderDate": "2026-07-02", "lastOrderDate": "2026-09-30",
  "statsText": "14 заказов · выдано 11 на 6 120 ₽ · отменено покупателем 1 · не забрано 1",
  "orders": { "items": [ /* OrderHistoryRowDto без телефона: customerPhoneMasked = null */ ], "page": 1, "pageSize": 20, "totalCount": 14 },
  "note": { "text": "всегда без лука", "updatedAtUtc": "…", "updatedByName": "Иван Петров", "updatedText": "Изменено: Иван Петров, 30 сен 11:40" } }
```

- Все цифры — **только этот магазин**; заказы аккаунта и гостевые с этим номером сведены. Обезличенные заказы не
  входят.
- `phoneVerified` — только положительная отметка: `true`, если есть заказ из аккаунта с подтверждённым номером; иначе
  `false` (фронт ничего не пишет).
- `name` — из последнего заказа. `statsText` собирает сервер в безличной форме (род покупателя не угадывается);
  нулевые счётчики отмен в текст не входят.
- `phone` — E.164 (`+7…`) для ссылки `tel:`, `phoneDisplay` — для показа. Полный номер видит только персонал этого
  магазина (как в карточке заказа).
- `orders` — новые сверху, по 20; `page` ≥ 1.
- `note` — `null`, если заметки нет.

---

## §530. Заметка о покупателе

**`GET /api/shops/{id}/customers/{customerRef}/note`** → `ShopCustomerNoteStateDto { note: ShopCustomerNoteDto | null }`.
Лёгкое чтение для P1 «Есть заметка» в карточке заказа. 404 — как §529.

**`PUT /api/shops/{id}/customers/{customerRef}/note`** — тело `{ "text": "звонить перед выдачей" }` →
`ShopCustomerNoteStateDto`.

| Проверка | Ответ |
|---|---|
| права (`EditCustomerNotes`) | 403 |
| `customerRef` | 404 как §529 |
| `text` после обрезки пробелов длиннее 1000 | 400 «Заметка — не длиннее 1000 символов» |
| `text` пустой/только пробелы/`null` | заметка удаляется → `{ "note": null }` |
| иначе | создать/заменить → `{ "note": {…} }` |

- `updatedByName` — снимок имени автора; у удалённого аккаунта — «Удалённый пользователь».
- Строка-предупреждение под полем — `GET /api/legal/texts/ShopCustomerNoteNotice` **[legal L17]**. На 404 фронт
  показывает fallback: «Заметку видят только сотрудники этого магазина. Покупатель её не видит.»
- Покупатель заметку не видит нигде; в `PublicOrderDto`, `MyOrderSummaryDto` и выгрузке **текста** нет.

---

## §531. Каталог магазинов — `GET /api/goods/catalog?cityId=&openNow=&search=&page=`

Анонимно. `cityId` — id из `GET /api/cities` (нет — все города; неизвестный — пустая страница, не 404). `openNow` —
`true` = только открытые сейчас. `search` — по названию и адресу, без учёта регистра, длиннее 100 символов —
обрезается. `page` ≥ 1 (некорректное значение — как 1), 20 карточек.

```json
{ "items": [
    { "slug": "shaurma-lenina", "path": "/shaurma-lenina", "name": "Шаурма на Ленина", "address": "ул. Ленина, 5",
      "cityName": "Пермь",
      "openState": { "isOpen": true, "text": "Открыто до 21:00", "opensAtUtc": null, "closesAtUtc": "…" },
      "acceptance": "AcceptingNow", "acceptanceText": "Принимает заказы" } ],
  "page": 1, "pageSize": 20, "totalCount": 7,
  "city": { "id": 59, "name": "Пермь", "region": "Пермский край", "label": "Пермь, Пермский край" },
  "emptyText": null }
```

- Виден магазин, если **все** условия: активен и не заблокирован; часы заданы; есть опубликованный товар; тариф
  «Заказов» разрешает показ (`allowPublicListing`); владелец не выключил показ. Невидимые не выдаются ни карточкой,
  ни в `totalCount`, ни поиском.
- `acceptance`: `AcceptingNow` «Принимает заказы» · `PreorderOnly` «Можно заказать заранее» · `NotAccepting`
  «Временно не принимает заказы». Точная причина покупателю не раскрывается.
- Порядок: `AcceptingNow` → `PreorderOnly` → `NotAccepting`, внутри — по названию.
- `openState` — тот же расчёт, что на странице магазина; данные могут отставать до 30 с (кеш).
- `cityName` — всегда (фронт показывает его в режиме «Все города»). Цен, фото, рейтингов, отзывов, расстояний,
  реквизитов нет [legal L19].
- `emptyText`: «В этом городе пока нет магазинов на goods» (город, без фильтров) · «Пока нет магазинов на goods» (все
  города) · «Ничего не найдено» (есть поиск или «Открыто сейчас»).
- Ссылка карточки — `path` (относительная); фронт абсолютных ссылок не строит.

---

## §532. Показ магазина в каталоге

**`GET /api/shops/{id}/catalog-listing`** (персонал) → `CatalogListingDto`:

```json
{ "showInCatalog": true, "allowedByPlan": true, "visible": false,
  "statusText": "Магазина сейчас нет в каталоге",
  "notAllowedByPlanText": null,
  "checklist": [ { "code": "NoWorkingHours", "text": "Задайте часы работы", "done": false },
                 { "code": "NoPublishedProducts", "text": "Опубликуйте хотя бы один товар", "done": true } ] }
```

- `checklist` — только применимые пункты: `ShopBlocked`, `NoWorkingHours`, `NoPublishedProducts`, `NotAllowedByPlan`,
  `HiddenByOwner` (тексты — `ARCHITECTURE_CYCLE25.md` §505.1). `visible = true` ⇔ все `done`.
- `statusText`: «Магазин виден в каталоге goods.ezbook.ru» / «Магазина сейчас нет в каталоге».
- `notAllowedByPlanText` при `!allowedByPlan`: «Показ в каталоге не входит в ваш тариф» — фронт делает переключатель
  неактивным и показывает этот текст.

**`PUT /api/shops/{id}/catalog-listing`** (владелец, 451 владельческий) — тело `{ "showInCatalog": true }` →
`CatalogListingDto`. Включить при `!allowedByPlan` → 409 `CatalogConflictDto { code: "CatalogListingNotAllowedByPlan",
message: "Показ в каталоге не входит в ваш тариф" }`. Выключить можно всегда.

Существующие маршруты: `ShopManageDto` не меняется. У магазинов стенда после миграции `showInCatalog = true`; у новых
магазинов — `true`.

---

## §533. Администратор — тарифы линейки «Заказы»

Форма `AdminPlanInput`/`AdminPlanDto` **не меняется**. Меняется смысл поля `allowPublicListing` у тарифа
`line = Orders`: «Показ магазинов в каталоге goods». Системный бесплатный тариф линейки после миграции — `true`.
Админка ezbook показывает поле в форме тарифа «Заказов» с подписью «Показ в каталоге goods». Смена применяется без
деплоя (кеш каталога — до 30 с).

---

## §534. Долги цикла 24, видимые в API

- **T-25-03.** `POST/PUT /api/shops/{id}/products` (`availableWeekdays`) и `PUT …/categories/{id}/weekdays`
  (`weekdays`): повтор дня → 400 «День недели указан дважды». В `contracts/cycle24/openapi.yaml`: `uniqueItems: true`
  у этих массивов; все 400, которые сервер реально отдаёт на маршрутах цикла 24, описаны в `responses`.
- **T-25-04.** `GET /api/orders/my` (`pickup.text`, `pickup.isPreorder`) и тексты уведомлений (push, web-push,
  мессенджер покупателю, MAX персоналу) считают «сегодня» от рабочего дня магазина. Форма не меняется.
- **T-25-01, T-25-02** — внутренние (клиент web-push), API не затрагивают. Подписка push на адрес, который
  разрешается в непубличный IPv4 через NAT64/6to4/IPv4-compatible/IPv4-translated, Teredo или NAT64 local-use, больше
  не доставляется (строка очереди `Failed`, как у прочих непубличных адресов).

---

## §535. Персональные данные

- `GET /api/profile/export` получает две секции:
  - `staffMaxLink: { status, linkedAtUtc, stoppedAtUtc } | null` — **без** идентификатора чата;
  - `shopCustomerNotes: [{ shopName, updatedAtUtc }]` — факт заметки о номере субъекта, **без текста**, только при
    подтверждённом номере аккаунта [legal L17].
- `POST /api/profile/delete-account`: удаляются привязка MAX и сессии; заметки магазинов о номере аккаунта — **только
  при подтверждённом номере**; в заметках, где удаляемый — автор, имя → «Удалённый пользователь». Форма ответа не
  меняется.
- Сроки хранения **[legal L20]** — новые правила в `GET /api/admin/retention/policy`: `staff-max-links` (остановленные
  — 30 дней, сессии — 1 день после истечения), `staff-max-messages` (90 дней), `shop-customer-notes` (пока у магазина
  есть необезличенный заказ с этим номером).

---

## §536. Коды и перечисления (дописано в конец / новые)

- `CatalogConflictCode`: … + `CatalogListingNotAllowedByPlan`.
- Новые перечисления API:
  - `ReportPeriodPreset { Today, Yesterday, Last7Days, Last30Days, ThisMonth, LastMonth, Custom }`;
  - `OrderHistorySort { PickupDesc, PickupAsc }`;
  - `SummaryTopSort { Amount, Quantity }`;
  - `StaffMaxStatus { NotLinked, Pending, Linked, StoppedInMax }`;
  - `CatalogAcceptance { AcceptingNow, PreorderOnly, NotAccepting }`;
  - `CatalogListingCheckCode { ShopBlocked, NoWorkingHours, NoPublishedProducts, NotAllowedByPlan, HiddenByOwner }`.
- Внутренние (журнал доставки): `NotificationReason` + `StaffMaxDisabledByShop`, `StaffMaxChatUnavailable`,
  `StaffMaxNoRecipient`, `StaffMaxPlatformDisabled`, `StaffMaxRateLimited`.

---

## §537. Строки 400/409/429 цикла (сводно)

| Где | Текст |
|---|---|
| периоды (400) | Укажите начало и конец периода · Конец периода раньше начала · Период — не длиннее 366 дней |
| история (400) | Введите не меньше 2 букв имени или 4 цифр телефона · Сумма не может быть отрицательной · Сумма «от» больше суммы «до» · Номер заказа — число от 1 до 9999 · Номер страницы — от 1 |
| сводка (400) | Неизвестный вид сортировки |
| лист сборки (400) | Дата — не позже {d MMMM} · Укажите начало и конец интервала · Укажите время в формате ЧЧ:ММ |
| заметка (400) | Заметка — не длиннее 1000 символов |
| товары, категории (400) | День недели указан дважды |
| MAX (409) | Подключить MAX могут владельцы и сотрудники магазинов · Сообщения в MAX пока не включены на платформе · Подключение к MAX временно недоступно, попробуйте позже |
| каталог (409, JSON) | `CatalogListingNotAllowedByPlan` — Показ в каталоге не входит в ваш тариф |
| 429 `shop-reports`, `goods-catalog`, `staff-max-link` | Слишком много запросов — подождите минуту |

---

## §538. Что фронт обязан делать и чего делать не должен

**Обязан:**

- брать типы из генерата `api-cycle25` для всех DTO этого цикла;
- **не класть телефон и фильтр «покупатель» в адрес страницы**: история — поле `customer` только в теле запроса и в
  `location.state`; карточка — по `customerRef`;
- хранить в адресе истории `period` (и `from`/`to` у `Custom`), `statuses`, `amountFrom`, `amountTo`, `number`, `sort`,
  `page`; в адресе листа сборки — `date`, `from`, `to`, `includeNew`; в адресе сводки — `period`, `from`, `to`, `top`;
- показывать как есть: `summaryText`, `emptyText`, `label`, `shareText`, `averageCheckText`, `quantityText`,
  `weightBreakdownText`, `generatedAtText`, `statsText`, `statusText`, `acceptanceText`, `openState.text`,
  `notAllowedByPlanText`, `unavailableText`;
- скрывать блок MAX при `eligible = false`, пункт «Сводка» — при `myRole = Staff`;
- опрашивать `GET /api/staff-max` только пока `status = Pending`;
- обновлять лист сборки раз в 60 с только при видимой вкладке;
- делать таблицы с `<th scope>`, фильтры — с клавиатуры, состояния — текстом.

**Не должен:**

- считать даты периодов, доли, средний чек, разницу с прошлым периодом, группировку и суммы листа сборки,
  видимость и порядок каталога;
- строить абсолютные ссылки (каталог — `path`);
- показывать полный телефон в списке истории.

---

## §539. Совместимость

- Все поля ответов существующих маршрутов — добавочные; новый входной `staffMaxEnabled` необязателен.
- **Поведенческие изменения, которых требует SPEC:**
  - `/` goods — каталог;
  - магазины стенда видимы в каталоге при выполнении условий;
  - «сегодня» в текстах уведомлений и «Моих заказах» — рабочий день;
  - повторяющиеся дни недели → 400;
  - push не доставляется на непубличные адреса через IPv6-обёртки и не ходит через системный прокси.
- Не изменились: салонный каталог (`GET /api/companies`, `GET /api/companies/public`), отчёты салонов,
  подтверждение телефона через бота, push мастерам.

---

## §540. Приёмка контракта (QA)

1. `redocly lint` чистый; генераты `api-cycle25` и `api-cycle24` совпадают с закоммиченными.
2. `schemathesis` по `contracts/cycle25/openapi.yaml` и `contracts/cycle24/openapi.yaml` на живом бэкенде — без
   несоответствий схеме.
3. Совпадение сводки за день и итога истории.
4. Телефон не встречается ни в одном URL новых маршрутов (проверка логов access/приложения на прогоне истории с
   поиском по телефону).
5. Изоляция: новые `/api/shops/{id}/…` с id салона → 404; `customerRef` чужого магазина → 404; сотрудник → 403 на
   сводку; удалённый сотрудник → 403 сразу.
6. MAX с подделкой отправителя: дедупликация по чату, перепроверки при отправке, устойчивость к сбою MAX.
7. Регресс подтверждения телефона (цикл 14) при включённом `StaffMax`.
8. Каталог: условия видимости, `totalCount` без скрытых, порядок групп, салонные списки без магазинов.
9. Ни одного красного теста в существующих наборах; числа не ниже 2166 / 1030 / 1050.

## §541. Чего в контракте намеренно нет

- Выгрузки истории и сводки в файл (CSV/Excel).
- Сводной карточки покупателя и сводки по всем магазинам аккаунта.
- «Чёрного списка» покупателей.
- Отметок «собрано» в листе сборки, чековой печати 80 мм, ярлыков.
- «Тихих часов» для MAX и отметки у владельца, кто подключил MAX.
- Других мессенджеров для персонала.
- Геопоиска, карты, фото, рейтингов, отзывов, цен в каталоге.
- Ограничения новых функций тарифом (кроме показа в каталоге).

## §542. Карта «экран → маршрут»

| Экран goods | Маршруты |
|---|---|
| `/` и `/city/:cityId` | `GET /api/goods/catalog`, `GET /api/cities` |
| `/cabinet/devices`, блок «Заказы в MAX» | `GET /api/staff-max`, `POST /api/staff-max/link-sessions`, `DELETE /api/staff-max/link` |
| `/cabinet/:shopId/notifications` | `GET/PUT …/notification-settings` |
| `/cabinet/:shopId/history` | `POST …/order-history`, `GET …/orders/{orderId}` + действия |
| `/cabinet/:shopId/summary` | `GET …/summary` |
| `/cabinet/:shopId/picklist` | `GET …/picklist` |
| `/cabinet/:shopId/customers/:customerRef` | `GET …/customers/{customerRef}`, `PUT …/customers/{customerRef}/note`, `GET /api/legal/texts/ShopCustomerNoteNotice` |
| карточка заказа (экран заказов, история) | `GET …/orders/{orderId}`, P1 `GET …/customers/{orderId}/note` |
| `/cabinet/:shopId/settings` | `GET/PUT …/catalog-listing` |
| админка ezbook, тарифы | `GET/POST/PUT /api/admin/plans` |
