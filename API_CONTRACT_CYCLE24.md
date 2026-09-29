# API_CONTRACT — цикл 24 ServiceBooking: «Заказы», цикл 2 «Время, приём, уведомления, тарифы магазинов»

**Разделы §470–§494.** Точка синхронизации backend-, frontend-, devops-инженера и QA на время цикла. Решения —
`ARCHITECTURE_CYCLE24.md` (§446–§469), требования — `SPEC.md` цикла 24.

**Источник истины по ФОРМЕ** — `contracts/cycle24/openapi.yaml`. Здесь — то, чего схема не выражает: порядок проверок,
тексты, какие коды когда, изменения поведения существующих маршрутов. При расхождении по форме прав YAML, по смыслу —
этот файл.

```bash
npx @stoplight/prism mock contracts/cycle24/openapi.yaml --port 4024              # фронт — мок
cd frontend && npm run types:api:cycle24                                          # -> src/types/api-cycle24.generated.ts
schemathesis run contracts/cycle24/openapi.yaml --base-url http://localhost:5000 --checks all   # QA
npx @redocly/cli lint --config ../contracts/redocly.yaml ../contracts/cycle24/openapi.yaml       # CI
```

Второй машиночитаемый артефакт — `contracts/cycle24/pickup-schedule-vectors.json`: эталон часов, слотов, «как можно
скорее», паузы «до конца дня» и правила приёма. Его читает юнит-тест бэкенда, по нему сверяется QA.

**Базовая ревизия** — `8d4e98d`. Существующий API — `API_DOCUMENTATION.md` и контракты циклов 3–23 (для goods —
`API_CONTRACT_CYCLE23.md` §406–§425, `contracts/cycle23/openapi.yaml`).

---

## §470. Конвенции

Действуют **без изменений** все конвенции `API_CONTRACT_CYCLE23.md` §406:

- camelCase; enum — строкой; `DateTime` — ISO-8601 UTC; `DateOnly` — `"YYYY-MM-DD"`.
- 400/402/429 — голая строка по-русски. 401/403 — пустое тело. 404 — пустое тело, не оракул.
- Все 409 **домена заказов** — JSON (`OrderRefusalDto`, `OrderConflictDto`, `CatalogConflictDto`). 409 существующих
  не-заказных маршрутов (биллинг, push, каналы) — строка.
- Текст, который видит человек, собирает сервер, фронт печатает его как есть.

Дополнительно:

- **Время суток во входе и выходе** — строка `"HH:mm"` (локальное время магазина), шаг 5 минут. **Моменты** — UTC.
  Подписи вроде «к 12:30» и «Открыто до 21:00» собирает сервер по часовому поясу магазина.
- **День недели** — `DayOfWeek` строкой: `Monday … Sunday`.
- **Время без ведущего нуля в текстах** («в 9:00», «до 21:00», «≈ к 1:15»), с ведущим нулём в полях `"HH:mm"` и в
  подписях слотов («09:00–09:15»).
- **Текущий рабочий день** — день, к которому относится идущий сейчас интервал. Интервал через полночь относится ко
  дню начала: в сб 01:00 при часах «пт 18:00–03:00» текущий рабочий день — пятница. Вне такого «хвоста» это
  календарная дата магазина.
- Перечисления только дописываются. Фронт на неизвестный `code` показывает `message`.

---

## §471. Сводка маршрутов

| Маршрут | Новый/изм. | Доступ | Rate limit | 451 |
|---|---|---|---|---|
| `GET /api/shops/{id}` | изм.: `+pickupSettings, workingHoursSet, acceptance, openState, notAcceptingCode, setupChecklist, orderLimit, productLimit` | персонал | — | глоб. |
| `GET/PUT /api/shops/{id}/working-hours` | новый | GET персонал, PUT владелец | — | PUT владельч. |
| `GET /api/shops/{id}/special-days`, `PUT/DELETE …/special-days/{date}` (P1) | новый | GET персонал, запись владелец | — | запись владельч. |
| `PUT /api/shops/{id}/pickup-settings` | новый | владелец | — | владельч. |
| `PUT /api/shops/{id}/acceptance` | новый | персонал | — | глоб. |
| `GET /api/shops/{id}/ordering-status` | новый | персонал | `order-board` | глоб. |
| `GET /api/shops/{id}/pickup-slots?date=` | новый | персонал | — | глоб. |
| `GET/PUT /api/shops/{id}/notification-settings` | новый | GET персонал, PUT владелец | — | PUT владельч. |
| `GET /api/shops/{id}/daily-menus`, `GET/PUT/DELETE …/daily-menus/{date}`, `POST …/{date}/copy` (P1) | новый | персонал | — | глоб. |
| `PUT /api/shops/{id}/categories/{categoryId}/weekdays` (P1) | новый | владелец | — | владельч. |
| `GET/POST …/products`, `PUT …/products/{id}` | изм.: `availableWeekdays`, `weekdaysLabel`, `soldOut`; лимит тарифа | как в ц. 23 | — | как в ц. 23 |
| `PUT …/products/{id}/sold-out` | изм.: `scope` | персонал | — | глоб. |
| `GET /api/shops/{id}/order-board` | изм.: `preorders`, `acceptance`, сортировка | персонал | `order-board` | глоб. |
| `GET …/orders/{id}` и все действия персонала | изм.: `pickup`, `notifyByMessenger`, `messenger`, `ChangePickup` в `availableActions` | персонал | — | глоб. |
| `PUT …/orders/{id}/pickup` (P1) | новый | персонал | — | глоб. |
| `GET /api/storefront/{slug}?date=` | изм. | все | `storefront` | глоб. (вошедший) |
| `GET /api/storefront/{slug}/pickup-slots?date=` | новый | все | `storefront` | глоб. (вошедший) |
| `POST /api/storefront/{slug}/quote` | изм.: `pickup` | все | `storefront` | глоб. (вошедший) |
| `POST /api/storefront/{slug}/orders` | изм.: `pickup`, `notifyByMessenger`, новый порядок проверок | все | `order-create` | глоб. (вошедший) |
| `GET /api/orders/public/{token}`, `POST …/cancel` | изм.: `pickup`, `notifications` | все | `order-public` | глоб. (вошедший) |
| `POST /api/orders/public/{token}/push-subscription`, `POST …/push-subscription/remove` | новый | все | `order-push` | — |
| `GET /api/orders/my` | изм.: `pickup` | вошедший | — | глоб. |
| `GET /api/push/config`, `GET/POST /api/push/subscriptions` | изм.: `site` | вошедший | как было | как было |
| `POST /api/notification-channels/{id}/companies` | изм.: магазин разрешён | владелец | — | владельч. |
| `GET /api/notification-channels/offer`, `POST /api/notification-channels` | изм.: `allowedByPlan` по двум линейкам | владелец | — | как было |
| `GET /api/billing/subscription?line=` | изм. | владелец аккаунта | — | как было |
| `POST /api/billing/subscription/request` | изм.: `line` | владелец аккаунта | — | как было |
| `GET/POST /api/admin/plans`, `PUT /api/admin/plans/{id}`, `PUT …/system-free`, `PUT …/system-trial` | изм.: линейка и поля «Заказов» | SuperAdmin | — | глоб. |
| `GET /api/admin/billing-accounts/{id}`, `PUT …/subscription`, `GET /api/admin/subscription-requests` | изм.: `line`, `ordersSubscription` | SuperAdmin | — | глоб. |
| `GET /api/pricing` | изм.: только тарифы «Записей» | все | как было | — |
| `POST /api/shops`, `POST /api/companies`, `POST /api/companies/{id}/members` | изм.: лимиты внутри линейки | как было | — | как было |
| `GET /api/companies/{id}/notification-settings` (салон) | изм.: `enabledTypes` — только типы записи | как было | — | как было |
| `GET /api/profile/export`, `POST /api/profile/delete-account` | изм.: поля заказа | вошедший | как было | allow-list |

«персонал» = владелец, сотрудник, SuperAdmin. Порядок проверок прав — как в цикле 23: токен → существование и тип
(салон/нет → 404) → роль (403).

---

## §472. `ShopManageDto` — добавочные поля (`GET /api/shops/{id}`)

| Поле | Смысл |
|---|---|
| `pickupSettings` | `{asapEnabled, scheduledEnabled, slotStepMinutes, preorderDays, minPrepMinutes}` |
| `workingHoursSet` | часы работы заданы |
| `acceptance` | `ShopAcceptanceDto` (§475) |
| `openState` | `ShopOpenStateDto` (§473.4) |
| `acceptingOrders` / `notAcceptingReason` | прежние поля; теперь это итог правила приёма, `notAcceptingReason` — **текст для владельца** (§473.5) |
| `notAcceptingCode` | `ShopNotAcceptingCode` или null |
| `setupChecklist` | `[{code, text, done}]`; в цикле 24 один пункт `WorkingHours` — «Задайте часы работы». Пока есть невыполненные пункты, показывать блок «Чтобы начать принимать заказы:» |
| `orderLimit` | `OrderLimitDto`: `{used, limit, monthLabel, warningLevel: None \| Warning80 \| Reached, text}`. `limit = null` — без ограничения, тогда `text = null` |
| `productLimit` | действующий лимит товаров: тариф, но не больше технического потолка 1000. Для подсказки «42 из 50» в каталоге |

`settings` (`PUT …/settings`) **не меняется**: время получения меняется своим маршрутом §474.

---

## §473. Часы работы

### §473.1 `GET /api/shops/{id}/working-hours` → `WorkingHoursDto`

`{ isSet, days: [7 × {dayOfWeek, dayLabel: "пн", intervals: [{start, end, crossesMidnight}], text}] }` — всегда
семь дней, от понедельника.

- `text` — «09:00–14:00, 15:00–21:00», «18:00–03:00 (до утра)» или «выходной».
- При `isSet: false` у всех дней пустые интервалы и `text` «не заданы».

### §473.2 `PUT /api/shops/{id}/working-hours` (владелец)

Тело `WorkingHoursInput { days: [{dayOfWeek, intervals: [{start: "HH:mm", end: "HH:mm"}]}] }`:

- не больше 7 элементов, день не повторяется;
- день не передан или `intervals: []` — выходной;
- `end ≤ start` означает «через полночь»: `"18:00"`–`"03:00"`; `"22:00"`–`"00:00"` — до полуночи.

Ответ — `WorkingHoursDto`. Изменение действует на новые заказы, уже созданные не трогаются.

| Проверка | 400 |
|---|---|
| строка не `HH:mm` / вне 00:00–23:55 | «Укажите время в формате ЧЧ:ММ» |
| не кратно 5 минутам | «Время указывается с шагом 5 минут» |
| повтор дня / неизвестный день | «День недели указан дважды» / «Неизвестный день недели» |
| > 3 интервалов в дне | «В дне не больше трёх интервалов» |
| `start = end` | «Интервал не может быть нулевой длины» |
| интервалы не по порядку, пересекаются или соприкасаются | «Интервалы должны идти по порядку и не пересекаться» |
| через полночь переходит не последний интервал | «Через полночь может переходить только последний интервал дня» |
| «хвост» после полуночи заходит на первый интервал следующего дня | «Часы после полуночи пересекаются с часами следующего дня» |

Все дни выходные — допустимо: `isSet: true`, но магазин не принимает заказы (`NoPickupTimeAvailable`).

### §473.3 Особые дни (P1)

- `GET …/special-days?from=&to=` → `SpecialDayDto[]` `{date, label, isClosed, intervals, text}`. По умолчанию — от
  сегодня до +90 дней.
- `PUT …/special-days/{date}` тело `{isClosed, intervals?, confirmConflicts?}`:
  - `date` — от сегодня до +90 дней, иначе 400 «Дата — от сегодня до 90 дней вперёд»;
  - интервалы проверяются по правилам §473.2; также проверяется, что «хвост» предыдущего дня не заходит на новые часы;
  - есть активные заказы (`New/Accepted/Ready`) с датой выдачи `date` и временем вне новых часов, а
    `confirmConflicts ≠ true` → **409** `CatalogConflictDto { code: "ScheduleConflictsWithOrders", message: «На этот
    день уже есть заказы вне новых часов — свяжитесь с покупателями или отмените заказы», conflictingOrders: [...] }`.
    Фронт показывает список (номер, время, статус, имя, телефон-ссылка) и с согласия пользователя повторяет запрос с
    `confirmConflicts: true`. Заказы **не меняются**.
- `DELETE …/special-days/{date}` → 204, идемпотентно. Дата возвращается к недельному расписанию.

### §473.4 Состояние «открыто/закрыто» — `ShopOpenStateDto { isOpen, text, opensAtUtc?, closesAtUtc? }`

| Ситуация | `text` |
|---|---|
| сейчас внутри интервала | «Открыто до 21:00» (конец текущего интервала) |
| между интервалами одного рабочего дня | «Перерыв до 14:00» |
| закрыто, откроется сегодня | «Закрыто, откроемся в 9:00» |
| закрыто, откроется завтра | «Закрыто, откроемся завтра в 9:00» |
| закрыто, откроется в ближайшие 6 дней | «Закрыто, откроемся в пн в 9:00» |
| закрыто, откроется позже (≤ 14 дней) | «Закрыто, откроемся 12 окт в 9:00» |
| не откроется в ближайшие 14 дней | «Закрыто» |
| часы не заданы | «Часы работы не заданы» |

`text` передаёт состояние словами. Цвет и иконка на фронте — только дополнение (доступность, SPEC §6).

### §473.5 Итог правила приёма — `ShopNotAcceptingCode` и тексты

| `notAcceptingCode` | Покупателю (`StorefrontDto.notAcceptingReason`, отказ 409) | Владельцу/персоналу (`ShopManageDto.notAcceptingReason`, `ordering-status.ownerText`) |
|---|---|---|
| `Blocked` | «Магазин недоступен» | «Магазин заблокирован администратором» |
| `NoWorkingHours` | «Магазин пока не принимает заказы» | «Задайте часы работы — без них магазин не принимает заказы» |
| `Stopped` | «Магазин временно не принимает заказы» | «Приём заказов выключен» |
| `Paused` | «Магазин временно не принимает заказы — до 13:30» | «Пауза до 13:30» |
| `NotAllowedByPlan` | «Магазин временно не принимает заказы» | «Ваш тариф не включает приём заказов» |
| `MonthlyLimitReached` | «Магазин временно не принимает заказы» | «Лимит заказов на октябрь исчерпан (150 из 150). Новые заказы примутся с 1 ноября или после смены тарифа» |
| `NoPickupTimeAvailable` | открыто — «Сегодня уже не успеем приготовить заказ»; закрыто — `openState.text` | то же |

Если пауза кончается в другой день — «— до 3 окт 9:00». Порядок проверок — `ARCHITECTURE_CYCLE24.md` §450.
Эталоны — `pickup-schedule-vectors.json`.

---

## §474. Время получения — настройки (US-24-05)

`PUT /api/shops/{id}/pickup-settings` (владелец), тело — все пять полей: `{asapEnabled, scheduledEnabled,
slotStepMinutes, preorderDays, minPrepMinutes}`. Ответ — `ShopManageDto`. Изменения действуют на новые заказы.

| Проверка | 400 |
|---|---|
| оба варианта выключены | «Включите хотя бы один вариант времени получения» |
| `slotStepMinutes ∉ {15, 30, 60}` | «Шаг слотов — 15, 30 или 60 минут» |
| `preorderDays ∉ 0…14` | «Предзаказ — от 0 до 14 дней вперёд» |
| `minPrepMinutes ∉ 0…180` | «Время приготовления — от 0 до 180 минут» |

---

## §475. Пауза и выключатель (US-24-03)

`PUT /api/shops/{id}/acceptance` (персонал), тело `{ mode: "Accepting" | "Paused" | "Stopped", pause?:
"Minutes15" | "Minutes30" | "Hour1" | "EndOfDay" }`. `Paused` без `pause` → 400 «Укажите длительность паузы».
Ответ — `ShopAcceptanceDto`:

```json
{ "mode": "Paused", "pausedUntilUtc": "2026-09-30T10:30:00Z",
  "statusText": "Пауза до 13:30", "changedAtUtc": "2026-09-30T09:58:00Z", "changedByName": "Анна",
  "changedText": "Изменено: Анна, 12:58" }
```

- `statusText`: «Принимаем заказы» / «Пауза до 13:30» / «Не принимаем, пока не включите».
- `mode` вычисляется: истекшая пауза отдаётся как `Accepting`, без записи в БД.
- `EndOfDay` — до конца последнего интервала текущего рабочего дня, если он ещё впереди (в том числе в перерыве и в
  «хвосте» через полночь). Иначе — до ближайшей локальной полуночи.
- `changedText` (P1) — «кто и когда» в нейтральной форме «Изменено: {имя}, {Н:мм}»: пол в тексте не угадывается.

---

## §476. `GET /api/shops/{id}/ordering-status` — лёгкий статус для экрана заказов

`ShopOrderingStatusDto { acceptingOrders, notAcceptingCode, customerText, ownerText, openState, acceptance, asap,
scheduledAvailable, orderLimit, workingHoursSet }`. Фронт опрашивает раз в **60 с** и сразу после своих `PUT`.
Политика `order-board`.

---

## §477. Витрина, слоты, проверка корзины

### §477.1 `GET /api/storefront/{slug}?date=YYYY-MM-DD` — добавочные поля `StorefrontDto`

| Поле | Смысл |
|---|---|
| `date` | дата выдачи, на которую отдан ассортимент. По умолчанию — текущий рабочий день |
| `dateNotice` | не null, если запрошенная дата недоступна: «На 12 окт заказать нельзя — показан ассортимент на сегодня». Сервер отдаёт сегодняшний ассортимент, а не 400 |
| `openState` | §473.4 |
| `workingHours` | `{ lines: [{dayLabel: "пн–пт", text: "09:00–21:00"}] }` — готовые строки, одинаковые дни сервер склеивает |
| `pickup` | `PickupOptionsDto { asapEnabled, scheduledEnabled, asap: {available, readyAtUtc, text}?, dates: [{date, label, hasSlots, reasonText}] }` |
| `notAcceptingCode` | §473.5 |
| `customerNotifications` | `{ webPushOffered, messengerOffered }` — показывать ли кнопку браузерных уведомлений на странице заказа и галочку мессенджера при оформлении |

- Товары, которые **в эту дату не продаются** (меню, дни недели), **не отдаются**. Остальные недоступные отдаются с
  `available: false` и `unavailableReason`: `SoldOut` («Закончилось»), `InsufficientStock` («Закончилось»),
  `ShopNotAccepting`.
- `asap.text`:
  - доступно — «≈ к 13:20»;
  - открыто, но не успеваем — «Сегодня уже не успеем приготовить заказ»;
  - закрыто или перерыв — `openState.text`.

  `asap = null`, если `asapEnabled = false`.
- `dates` — от текущего рабочего дня до сегодня + `preorderDays`. При `scheduledEnabled = false` список **пустой**:
  выбор слота не предлагается. Выходные и закрытые особые дни в список **не входят**.
  - `label`: «Сегодня», «Завтра», «пт 2 окт».
  - `reasonText` у рабочей даты без слотов: «Сегодня уже не успеем приготовить — выберите другой день».

### §477.2 `GET /api/storefront/{slug}/pickup-slots?date=` → `PickupSlotsDto`

`{ date, label, slots: [{startUtc, endUtc, label: "12:00–12:15"}], asap: AsapOptionDto?, reasonText? }`.

- `asap` — только для текущего рабочего дня.
- Дата вне `dates` → 200 с пустыми `slots` и `reasonText`: «В этот день магазин не работает» / «На эту дату заказать
  нельзя».
- Нет `date` → 400 «Укажите дату».
- p95 < 200 мс.

Для персонала — `GET /api/shops/{id}/pickup-slots?date=` той же формы, но **без** учёта времени приготовления, паузы
и `scheduledEnabled`: слот годен, если его конец позже «сейчас». Используется при смене времени (§481).

### §477.3 `PickupSelectionInput`

`{ kind: "Asap" }` или `{ kind: "Slot", date: "2026-10-02", slotStartUtc: "2026-10-02T09:00:00Z" }`.
`slotStartUtc` должен **точно** совпасть с `startUtc` одного из слотов даты `date`. Нет `date` или `slotStartUtc` у
`Slot` → 400 «Укажите дату и время получения».

### §477.4 `POST /api/storefront/{slug}/quote` — добавочное

Тело `QuoteInput { items, pickup? }`; `pickup` не передан = `Asap`. `QuoteDto` получает:

- `pickupDate` — дата выдачи, на которую проверена доступность;
- `pickupProblem` — `{code: "PickupTimeUnavailable", message}` или null (тексты §478.2);
- у строк — новую проблему `NotAvailableOnDate` («В этот день не продаётся»).

`hasProblems = true` при любой проблеме строки **или** `pickupProblem`. Фронт блокирует «Оформить», пока
`hasProblems` или `!acceptingOrders`. При смене даты фронт повторяет `quote` и подсвечивает строки с причиной
(US-24-12).

---

## §478. `POST /api/storefront/{slug}/orders` — оформление (изменения)

### §478.1 Порядок проверок (заменяет таблицу `API_CONTRACT_CYCLE23.md` §413.1)

| # | Проверка | Ответ |
|---|---|---|
| 1 | Модель (как в ц. 23); у `pickup` вида Slot нет даты/времени | 400 строка |
| 2 | Магазин по slug; салон/нет | 404 |
| 3 | **Повтор `idempotencyKey`** в этом магазине | **200** + тот же `CreateOrderResponse` (проверка перенесена выше правила приёма) |
| 4 | Правило приёма (§473.5, пункты 1–7) | 409 `ShopNotAcceptingOrders` + `notAcceptingCode` + `message` = текст покупателю |
| 5 | Корзина пуста / > 50 строк | 409 `EmptyCart` / `TooManyLines` |
| 6 | **Время получения** недопустимо | 409 `PickupTimeUnavailable` (тексты §478.2); заказ не создаётся с другим временем |
| 7–10 | Строгий режим, капча и телефон гостя, лимит по телефону | как в ц. 23 (409/400/429) |
| 11 | Позиции: количество, **доступность на дату выдачи**, остаток, цены | 409 `PriceChanged` / `ItemsUnavailable` + все `problems` (новая причина `NotAvailableOnDate`) |
| 12 | **Месячный лимит** исчерпан (проверяется в транзакции) | 409 `ShopNotAcceptingOrders`, `notAcceptingCode: MonthlyLimitReached`, «Магазин временно не принимает заказы» |
| — | Успех | **201** `{ order: PublicOrderDto, orderUrl }` |

`CreateOrderInput` получает два необязательных поля:

- `pickup` — не передан = `Asap`;
- `notifyByMessenger` — по умолчанию false; учитывается, только если `messengerOffered`.

### §478.2 Тексты `PickupTimeUnavailable`

| Случай | `message` |
|---|---|
| `Asap`, но «как можно скорее» выключено | «„Как можно скорее“ в этом магазине недоступно — выберите время» |
| `Asap`, магазин закрыт или не успеваем до закрытия | «Сейчас не успеем приготовить заказ — выберите время» |
| `Slot`, но заказ ко времени выключен | «Заказ ко времени в этом магазине недоступен» |
| слот не существует, уже прошёл, не успеваем приготовить или изменились часы | «Это время уже недоступно — выберите другое» |
| дата вне горизонта или выходной | «На эту дату заказать нельзя — выберите другую» |

На `PickupTimeUnavailable` фронт перезапрашивает `pickup-slots` и снова показывает выбор времени вместе с `message`.
Корзина и `idempotencyKey` сохраняются.

### §478.3 Галочка мессенджера — [legal L9]

- Текст берётся из `GET /api/legal/texts/OrderMessengerConsent`.
- На 404 фронт показывает строку из SPEC: «Присылать статус заказа в MAX/WhatsApp на номер +7 (…)». Номер — маской:
  введённый гостем или номер аккаунта.
- По умолчанию галочка **выключена**.
- Сервер пишет `MessengerConsentVersion` (версию текста, если он есть) и время.

### §478.4 Строка про предзаказ — [legal L13]

Фронт показывает текст под выбором времени, **только если** `GET /api/legal/texts/OrderPreorderNotice` = 200. На 404 —
ничего. Команда текст не придумывает.

---

## §479. Заказ у покупателя (изменения)

- `PublicOrderDto.pickup` — `OrderPickupDto { kind, date, startUtc, endUtc?, dueUtc, text, isPreorder, isOverdue }`.
  Варианты `text` (те же на экране заказов):
  - «Как можно скорее (≈ 13:20)»;
  - «К 12:30» (сегодня);
  - «Завтра, к 12:30»;
  - «пт 2 окт, к 12:30».
- `PublicOrderDto.notifications` — `{ webPush: { available, publicKey, unavailableText }, messengerRequested }`.
  `available = false` в трёх случаях, `unavailableText` для каждого:

  | Случай | `unavailableText` |
  |---|---|
  | магазин выключил web-push | null — кнопку не показывать |
  | push выключен на платформе | «Уведомления в браузере пока не включены на платформе» |
  | заказ в конечном статусе | null |

  Недоступность на устройстве (iOS без экрана «Домой», запрет разрешения) фронт определяет общей
  `utils/pushAvailability.ts` с текстами циклов 9 и 21 и добавляет: «В следующий раз выберите при оформлении сообщения
  в MAX/WhatsApp».
- `number` меняется, если магазин перенёс заказ на другую дату (US-24-09). Страница показывает актуальный номер.
- `MyOrderSummaryDto.pickup` — то же.

### §479.1 `POST /api/orders/public/{token}/push-subscription`

Тело `{ endpoint, keys: {p256dh, auth}, deviceLabel? }`. Формат и тексты 400 — как у `POST /api/push/subscriptions`
цикла 9. Разрешение браузера фронт запрашивает **только по нажатию**.

| Ситуация | Ответ |
|---|---|
| создано / обновлено | 201 / 200 `{ subscribed: true, subscriptionCount }` |
| неизвестный токен | 404 |
| магазин выключил web-push | 409 строка «Магазин не отправляет уведомления в браузер» |
| заказ в конечном статусе | 409 строка «Заказ уже завершён — уведомления по нему не приходят» |
| push выключен на платформе | 409 строка «Уведомления в браузере пока не включены на платформе.» |
| > 5 подписок на заказ | 201, самая старая молча вытеснена |
| частота (`order-push`, 20/ч на IP) | 429 «Слишком много запросов — подождите минуту» |

### §479.2 `POST /api/orders/public/{token}/push-subscription/remove`

Тело `{ endpoint }` → 204, идемпотентно: если строки нет — тоже 204. Неизвестный токен → 404. Отдельный POST вместо
DELETE с телом — чтобы не зависеть от прокси, которые отбрасывают тело DELETE. **На goods фронт НЕ вызывает
`PushSubscription.unsubscribe()` в браузере** (`ARCHITECTURE_CYCLE24.md` §456.3).

---

## §480. Экран заказов (изменения `GET /api/shops/{id}/order-board`)

- `acceptance` (`ShopAcceptanceDto`) приходит **в каждом ответе**, включая `changed: false`.
- Полный ответ:
  - `newOrders` — все `New`, **включая предзаказы**;
  - `accepted` — `Accepted` с датой выдачи не позже сегодня;
  - `ready` — все `Ready`;
  - `preorders` — `[{date, label, orders}]`: `Accepted` с датой выдачи позже сегодня, по возрастанию даты;
  - `completedToday` — как было.

  Внутри колонок порядок — по `pickup.startUtc`, затем по `createdAtUtc`.
- Карточка:
  - крупно `pickup.text` (у предзаказа дата уже в тексте);
  - `pickup.isOverdue` — на момент ответа. Между полными ответами фронт пересчитывает просрочку сам: `serverNow >
    pickup.dueUtc` при статусе New/Accepted/Ready. Метка — текст «Просрочен», не только цвет.
- Предзаказ переходит в `accepted` сам в день выдачи: у первого опроса нового дня другой `businessDate` → полный ответ.
- Звук и выделение нового — как в цикле 23: на новые id в `newOrders` (или в `accepted` при автоприёме). Переход
  заказа из `preorders` в `accepted` звуком **не** сопровождается.

---

## §481. Карточка заказа и смена времени

- `StaffOrderCardDto` получает поля:
  - `pickup`;
  - `notifyByMessenger`;
  - `messenger` (P1, US-24-23): `{ requested, status, statusText, attemptedAtUtc }` — последняя строка журнала
    доставки по заказу. `status` — существующий `NotificationStatus`, `statusText` — существующий
    `NotificationTexts.StatusText`.
- `availableActions` получает `ChangePickup` для `New` и `Accepted`.
- `PUT …/orders/{orderId}/pickup` (P1), тело `{ expectedVersion, pickup: PickupSelectionInput, comment? (≤ 500) }` →
  200 `StaffOrderDto`.

| Проверка | Ответ |
|---|---|
| статус не New/Accepted | 409 `InvalidTransition` + `order` |
| версия устарела | 409 `VersionMismatch` + `order` |
| слот не из `GET …/pickup-slots` персонала / Asap при закрытом магазине | 409 `PickupTimeUnavailable` «Это время недоступно — выберите другое» + `order` |
| успех | событие `PickupChanged` (журнал: «Время получения: к 12:30 → к 14:00», при смене даты — ещё «номер № 5 → № 12»), уведомление покупателю |

---

## §482. Каталог: дни недели, «закончилось» со сроком, меню на дату

### §482.1 Товар

- `ProductInput.availableWeekdays: DayOfWeek[]`:
  - не передан → все 7 дней;
  - пустой массив допустим: товар продаётся только по меню на дату;
  - повтор или неизвестный день → 400 «Неизвестный день недели».
- `ProductDto` получает:
  - `availableWeekdays`;
  - `weekdaysLabel`: «пн, ср, пт»; null при всех днях; «только по меню» при пустом массиве;
  - `soldOut`: `{ scope: "Today" | "UntilCancelled", date, text: "нет на сегодня" | "нет до отмены" }` или null.
- Смысл существующих полей уточняется:
  - `isSoldOut` — действует ли отметка **сейчас**; истёкшая «на сегодня» даёт `false` и `soldOut: null`;
  - `availableToCustomers` — доступность на **сегодня**.
- Лимит товаров:
  - сверх `productLimit` → 409 `ProductLimitReached` «По тарифу «Заказы · Бесплатно» в магазине может быть не больше
    50 товаров»;
  - технический потолок 1000 — прежний текст «В магазине уже 1000 товаров».
- P1: `PUT …/categories/{categoryId}/weekdays` `{ weekdays }` → `ProductDto[]` товаров категории.

### §482.2 `PUT …/products/{id}/sold-out`

Тело `{ isSoldOut, scope? }`.

- `scope` не передан при `isSoldOut: true` → `UntilCancelled` (совместимость с циклом 23). Фронт goods **всегда** шлёт
  `scope`; по умолчанию в диалоге выбрано `Today`.
- Повторный вызов меняет срок.
- `isSoldOut: false` снимает отметку.

### §482.3 Меню на дату

| Маршрут | Тело / ответ |
|---|---|
| `GET …/daily-menus?from=&to=` | `DailyMenuCalendarDto { from, to, days: [{date, label, hasMenu, productCount}] }`. По умолчанию — от сегодня до сегодня + max(`preorderDays`, 7) |
| `GET …/daily-menus/{date}` | `DailyMenuDto { date, label, exists, productIds, products: [{productId, name, categoryName, isPublished, inMenu, allowedByWeekdays}] }`. При `exists: false` `productIds` и `inMenu` **предзаполнены** товарами, которые разрешены днём недели (Q-24-6) |
| `PUT …/daily-menus/{date}` | `{ productIds }` — до 1000, без повторов, только неудалённые товары магазина → `DailyMenuDto` с `exists: true` |
| `DELETE …/daily-menus/{date}` | 204, идемпотентно. Дата возвращается к правилу дней недели |
| `POST …/daily-menus/{date}/copy` (P1) | `{ sourceDate }` → `DailyMenuDto`. У исходной даты нет меню → 400 «На эту дату меню нет» |

Ошибки 400:

- «Дата — от сегодня до {N} дней вперёд» (прошедшие даты не редактируются);
- «Товар не найден»;
- «Товар в меню повторяется».

Меню не меняет уже созданные заказы.

---

## §483. Уведомления магазина — `GET/PUT /api/shops/{id}/notification-settings`

`ShopNotificationSettingsDto`:

```json
{ "staffPushEnabled": true, "customerWebPushEnabled": true, "customerMessengerEnabled": false,
  "deliveryMode": "PriorityChannel", "priorityTransport": "Max",
  "messengerAvailable": false, "messengerUnavailableText": "Подключите номер для сообщений покупателям",
  "platformPushEnabled": false,
  "channels": [ { "channelId": "…", "transport": "Max", "phoneMasked": "+7 (9**) ***-**-67",
                  "stateText": "Подключён", "isConnected": true, "funded": true, "fundingText": "Оплачен до 31.10.2026" } ] }
```

- `channels` — каналы, назначенные **этому магазину**.
- `messengerAvailable` = у магазина есть назначенный и оплаченный канал. Если нет:
  - нет канала — «Подключите номер для сообщений покупателям»;
  - канал не оплачен — «Номер не оплачен — оставьте заявку на опцию в разделе «Подписка»».
- `PUT` (владелец): три флага и необязательные `deliveryMode`, `priorityTransport`.
  - `customerMessengerEnabled: true` при `messengerAvailable: false` → 409 `CatalogConflictDto { code:
    "MessengerUnavailable", message: «Сначала подключите и оплатите номер для сообщений покупателям» }`.
  - `priorityTransport` не среди оплаченных каналов магазина → 400 «Приоритетный канал должен быть среди оплаченных
    каналов магазина».
- Подключение номера — существующие маршруты `api/notification-channels/*` без изменений: заявка, оферта, риск, QR,
  статус, тест, замена.
- Назначение магазину — `POST /api/notification-channels/{id}/companies { companyId: <shopId>, warningAcknowledged }`.
  Для магазина маршрут **больше не отвечает 409 «Это магазин…»**. Текст 409 «уже привязан» для магазина — «Магазин
  уже привязан к другому номеру этого мессенджера».
- `GET /api/notification-channels/offer` → `allowedByPlan` = тариф «Записей» разрешает канал **или** у аккаунта есть
  магазин **и** его разрешает тариф «Заказов». Та же проверка у `POST /api/notification-channels`: иначе 402
  «Подключение канала недоступно на вашем тарифе».
- Салонные маршруты для магазина по-прежнему отвечают 409: `GET/PUT /api/companies/{id}/notification-settings`,
  шаблоны, журнал, `staff-push-settings`.

---

## §484. Push сотрудникам: `api/push` (изменения)

| Маршрут | Изменение |
|---|---|
| `GET /api/push/config?site=` | `site` = `Services` (по умолчанию) \| `Orders`. `companies` — только компании этого типа, где вызывающий участник; для магазинов `staffPushEnabled` берётся из настроек магазина. В ответе появляется `site` |
| `GET /api/push/subscriptions?currentEndpoint=&site=` | только устройства этого сайта (по умолчанию `Services`) |
| `POST /api/push/subscriptions` | тело `+ site` (по умолчанию `Services`). Потолок 10 и вытеснение считаются внутри пары (пользователь, сайт) |
| `DELETE /api/push/subscriptions/{id}`, `…/current` | без изменений |

Фронт ezbook `site` не шлёт — поведение прежнее. goods всегда шлёт `site=Orders`.

---

## §485. Подписка и тарифы

### §485.1 Владелец

- `GET /api/billing/subscription?line=Orders` → `OwnerSubscriptionDto` той же формы, что у ezbook, плюс добавочные
  поля:
  - `line`;
  - `orders: OrdersUsageDto { ordersThisMonth, ordersLimit, monthLabel, text: «Заказов в этом месяце: 120 из 150»,
    warningLevel, productsPerShopLimit, allowOrders }`;
  - `availablePlans: [{planId, name, pricePerMonth, description, highlights, limitsText}]` **[legal L14]**.

  Что меняется в существующих полях при `line=Orders`:
  - `usage.companies*` — магазины, `usage.employees*` — участники магазинов;
  - `trial` = null;
  - опции — опции аккаунта, общие для обеих линеек.

  Без `line` или с `line=Services` ответ прежний плюс `line: "Services"`, `orders: null`, `availablePlans: null`.
  404 — у вызывающего нет аккаунта (как было).
- `POST /api/billing/subscription/request` — тело `+ line` (по умолчанию `Services`). Ответ `SubscriptionRequestDto` +
  `line`.

| Проверка | Ответ |
|---|---|
| тариф другой линейки | 400 «Этот тариф из другой линейки» |
| ждёт заявка **другой** линейки | 409 «У вас уже есть заявка на смену тарифа «Записи» — отмените её или дождитесь решения» (или «…«Заказы»…») |
| ждёт заявка той же линейки | 200, перезапись (как было) |
| опцию не допускает ни одна линейка | 400 (существующий текст) |

### §485.2 Лимиты (402 строки, `BillingTexts`)

- Магазины: «По тарифу «{тариф}» можно открыть не больше {n} {магазин|магазина|магазинов}. Чтобы открыть ещё,
  смените тариф в разделе «Подписка»».
- Участники магазинов: «По тарифу «{тариф}» в магазинах может быть не больше {n} участников, включая владельца».
- Лимиты салонов — прежние тексты. Магазины больше не занимают места салонов, и наоборот.

### §485.3 Администратор

- `AdminPlanInput` получает поля:
  - `line` — `Services` по умолчанию; попытка сменить линейку при `PUT` → 409 «Линейку тарифа менять нельзя»;
  - `maxProductsPerShop`;
  - `maxOrdersPerMonth` — `null` = без ограничения;
  - `allowOrders`.

  `AdminPlanDto` отдаёт те же поля. У тарифа `line = Orders` поля салона (`allowOnlineBooking`, `photoQuotaMb` и т. д.)
  сохраняются, но ни на что не влияют.
- `PUT /api/admin/plans/{id}/system-free` — один системный бесплатный тариф **в каждой линейке**.
- `PUT …/system-trial` для тарифа «Заказов» → 409 «Пробный период есть только у тарифов «Записи»».
- `PUT /api/admin/billing-accounts/{id}/subscription` — тело `+ line`:
  - `Orders` назначает подписку «Заказов»;
  - тариф другой линейки → 400 «Тариф из другой линейки»;
  - `confirmLimitOverflow` считается по линейке;
  - опции — полный набор опций аккаунта (как было);
  - журнал изменений получает линейку.
- `GET /api/admin/billing-accounts/{id}` получает блок `ordersSubscription { planId, planName, paidUntil, isActive,
  isFreeTier, shopsUsed, shopsLimit, seatsUsed, seatsLimit, ordersThisMonth, ordersLimit }`.
- `GET /api/admin/subscription-requests` — у элементов появляется поле `line`.
- `GET /api/pricing` отдаёт только тарифы линейки «Записи».

---

## §486. Тексты уведомлений (фиксированные, собирает сервер) — [legal L10, L11]

**Тело push** — контракт сервера и service worker'а. Форма та же, что в цикле 9: `{ title, body, tag, url }`, `url`
**относительный**.

| Тип | Кому | `title` | `body` | `tag` | `url` |
|---|---|---|---|---|---|
| `StaffOrderCreated` | персонал | «Новый заказ № 27» | «к 12:30 · 3 позиции · ≈ 540 ₽ · Шаурма на Ленина» (у Asap — «как можно скорее», у предзаказа — «пт 2 окт, к 12:30») | `o-<orderId>` | `/cabinet/<shopId>/orders?order=<orderId>` |
| `StaffOrderCancelledByCustomer` (P1) | персонал | «Покупатель отменил заказ № 27» | «к 12:30 · Шаурма на Ленина» | `o-<orderId>` | то же |
| `OwnerOrderLimitWarning` | владелец аккаунта | «Использовано 80 % лимита заказов» / «Лимит заказов исчерпан» | «120 из 150 заказов в октябре» / «150 из 150 в октябре. Новые заказы не принимаются до 1 ноября или смены тарифа» | `limit-<month>` | `/cabinet/subscription` |
| `OrderAccepted` и остальные типы покупателю | покупатель | название магазина | текст мессенджера (ниже) без названия магазина и ссылки | `co-<orderId>` | `/o/<token>` |

**Мессенджер покупателю.** К каждому тексту добавляются строка ссылки на заказ и строка отписки
`Отказаться от уведомлений: https://ezbook.ru/u/<token>`.

| Тип | Текст |
|---|---|
| `OrderAccepted` | «{Магазин}: заказ № 27 принят. Получение: сегодня к 12:30.» |
| `OrderReady` | «{Магазин}: заказ № 27 готов к выдаче.» |
| `OrderRejected` | «{Магазин}: заказ № 27 отклонён.» + « Причина: …», если указана |
| `OrderCancelledByShop` | «{Магазин}: заказ № 27 отменён магазином.» + « Причина: …», если указана |
| `OrderEditedByShop` | «{Магазин}: магазин изменил заказ № 27, итог ≈ 560 ₽. Подробности по ссылке.» |
| `OrderPickupChanged` | «{Магазин}: изменено время получения заказа № 27 (теперь № 12): пт 2 окт, к 14:00.» — «(теперь № …)» только при смене номера |

Время в тексте мессенджера пишется строчными: «как можно скорее», «сегодня к 12:30», «завтра к 12:30», «пт 2 окт, к
12:30». Ссылка — `PublicSiteLinks.OrderPageUrl(token)`. В текстах **нет имени и телефона покупателя**. Если заказ
отменил сам покупатель, выдан или не забран, покупателю сообщения не отправляются (US-24-22).

---

## §487. Закрытые таблицы кодов (дописано в конец перечислений)

- `OrderRefusalCode`: … + `PickupTimeUnavailable`.
- `OrderConflictCode`: … + `PickupTimeUnavailable`.
- `CatalogConflictCode`: … + `ScheduleConflictsWithOrders`, `MessengerUnavailable`.
- `OrderProblemReason`: … + `NotAvailableOnDate`.
- `OrderAction`: … + `ChangePickup`. `OrderEventKind`: … + `PickupChanged`.
- Новые перечисления:
  - `PickupKind {Asap, Slot}`;
  - `ShopAcceptanceMode {Accepting, Paused, Stopped}`;
  - `PauseDuration`;
  - `SoldOutScope {Today, UntilCancelled}`;
  - `ShopNotAcceptingCode`;
  - `OrderLimitWarningLevel {None, Warning80, Reached}`;
  - `StorefrontUnavailableReason {SoldOut, InsufficientStock, ShopNotAccepting}`.
- `NotificationType` (внутреннее, в API журнала доставки) получает `StaffOrderCreated, StaffOrderCancelledByCustomer,
  OrderAccepted, OrderReady, OrderRejected, OrderCancelledByShop, OrderEditedByShop, OrderPickupChanged,
  OwnerOrderLimitWarning`. Салонный `enabledTypes` их **не** содержит.

---

## §488. Персональные данные

- `GET /api/profile/export`: элемент `orders[]` получает новые поля:
  - `pickup: {kind, date, text}`;
  - `notifyByMessenger`;
  - `messengerConsentVersion`, `messengerConsentAtUtc`;
  - `webPushSubscriptions: {count, createdAtUtc[]}` — без endpoint и ключей.

  Сообщения о заказах попадают в существующую секцию уведомлений.
- `POST /api/profile/delete-account`: у обезличиваемых заказов удаляются push-подписки и сбрасывается
  `notifyByMessenger`. Снимок согласия остаётся. Форма ответа не меняется.
- Сроки хранения **[legal L16]**:
  - push-подписки заказа — 7 дней после конечного статуса (`Retention:OrderPushSubscriptionDays`);
  - очередь web-push покупателям — 90 дней.

  Оба срока видны в `GET /api/admin/retention/policy` как новые правила `order-push-subscriptions` и
  `customer-order-push-notifications`.

---

## §489. Строки 400/409/429 цикла (сводно)

| Где | Текст |
|---|---|
| часы | Укажите время в формате ЧЧ:ММ · Время указывается с шагом 5 минут · День недели указан дважды · Неизвестный день недели · В дне не больше трёх интервалов · Интервал не может быть нулевой длины · Интервалы должны идти по порядку и не пересекаться · Через полночь может переходить только последний интервал дня · Часы после полуночи пересекаются с часами следующего дня |
| особые дни | Дата — от сегодня до 90 дней вперёд |
| время получения | Включите хотя бы один вариант времени получения · Шаг слотов — 15, 30 или 60 минут · Предзаказ — от 0 до 14 дней вперёд · Время приготовления — от 0 до 180 минут |
| пауза | Укажите длительность паузы |
| слоты / выбор | Укажите дату · Укажите дату и время получения |
| меню | Дата — от сегодня до {N} дней вперёд · Товар не найден · Товар в меню повторяется · На эту дату меню нет |
| товары | Неизвестный день недели |
| смена времени | Комментарий — не длиннее 500 символов |
| push заказа (400) | Некорректный адрес подписки (endpoint). · Некорректный ключ подписки (p256dh). · Некорректный ключ подписки (auth). · Слишком длинное название устройства. |
| push заказа (409) | Магазин не отправляет уведомления в браузер · Заказ уже завершён — уведомления по нему не приходят · Уведомления в браузере пока не включены на платформе. |
| уведомления магазина | Приоритетный канал должен быть среди оплаченных каналов магазина |
| биллинг (400/409) | Этот тариф из другой линейки · У вас уже есть заявка на смену тарифа «…» — отмените её или дождитесь решения · Линейку тарифа менять нельзя · Тариф из другой линейки · Пробный период есть только у тарифов «Записи» |
| 429 `order-push` | Слишком много запросов — подождите минуту |

---

## §490. Что фронт обязан делать и чего делать не должен

**Обязан:**

- брать типы из генерата `api-cycle24` для всех DTO, изменённых этим циклом;
- показывать как есть `openState.text`, `notAcceptingReason`, `pickup.text`, `acceptance.statusText`,
  `weekdaysLabel`, `soldOut.text`;
- слать `pickup` в `quote` и при оформлении, хранить его в корзине;
- на `PickupTimeUnavailable` перезапрашивать слоты;
- слать `scope` в `sold-out`;
- на goods слать `site: "Orders"` в `api/push`;
- **не** вызывать `PushSubscription.unsubscribe()` в браузере на goods;
- запрашивать разрешение на уведомления только по нажатию;
- делать кнопки паузы не меньше 44×44 px;
- делать выбор даты и слота радиогруппами с клавиатурой и подписями для читалки экрана.

**Не должен:**

- считать слоты, «как можно скорее», «открыто/закрыто», доступность товара на дату, срок «закончилось», лимиты.
  Единственное, что фронт считает сам, — метка «Просрочен» по `pickup.dueUtc`;
- строить абсолютные ссылки;
- показывать тарифы «Заказов» вне кабинета.

---

## §491. Совместимость

- Все поля ответов добавочные. Новые входные поля необязательны, и их умолчания дают поведение цикла 23:
  - `pickup` → Asap;
  - `scope` → UntilCancelled;
  - `availableWeekdays` → все дни;
  - `site`/`line` → Services.
- **Поведенческие изменения, которых требует SPEC:**
  - магазин без часов работы не принимает заказы;
  - номер уникален в дне выдачи;
  - доступность товара зависит от даты;
  - повтор идемпотентного оформления проверяется раньше правила приёма;
  - лимиты считаются внутри линейки;
  - `api/push` по умолчанию показывает только ezbook;
  - `enabledTypes` салона фильтруется;
  - назначение канала магазину разрешено.
- Не изменились: маршруты записи и их 409 для магазина, салонные маршруты уведомлений для магазина, формы ответов
  ezbook.

---

## §492. Приёмка контракта (QA)

1. `redocly lint` чистый; генерат `api-cycle24` совпадает с закоммиченным.
2. `schemathesis` по `contracts/cycle24/openapi.yaml` на живом бэкенде — без несоответствий схеме.
3. Векторы `pickup-schedule-vectors.json` зелёные в юнит-тестах бэкенда.
4. Параллельные тесты: месячный лимит (N = 20 при `limit − 1` → принят 1); номера в дне выдачи.
5. Изоляция:
   - новые маршруты `/api/shops/{id}/…` с id салона → 404;
   - салонные маршруты уведомлений для магазина → 409;
   - назначение канала магазину → 201.
6. Регресс салона:
   - `enabledTypes`;
   - устройства push (`site` по умолчанию);
   - push мастеру не уходит на подписку goods;
   - финансирование канала аккаунта без магазинов;
   - лимиты салонов;
   - `GET /api/pricing`.
7. Ни одного красного теста в существующих наборах, числа не ниже 1951 / 934 / 918.

## §493. Чего в контракте намеренно нет

- Сообщения персоналу в MAX (US-24-17 → цикл 25).
- Ёмкость слота.
- Напоминания о предзаказе.
- Изменение времени самим покупателем.
- Редактируемые тексты сообщений.
- Пробный период «Заказов».
- Офлайн-режим.
- Автостатус «Не забран».
- Публичная витрина тарифов «Заказов».
- Оплата в продукте.

## §494. Карта «экран → маршрут»

| Экран goods | Маршруты |
|---|---|
| витрина | `GET /api/storefront/{slug}?date`, `GET …/pickup-slots`, `POST …/quote`, `POST …/orders`, `GET /api/legal/texts/OrderMessengerConsent` / `OrderPreorderNotice` |
| страница заказа | `GET /api/orders/public/{token}`, `POST …/push-subscription`, `POST …/push-subscription/remove`, `POST …/cancel` |
| часы и приём | `GET/PUT …/working-hours`, `…/special-days*`, `PUT …/pickup-settings`, `PUT …/acceptance`, `GET /api/shops/{id}` |
| экран заказов | `GET …/order-board`, `GET …/ordering-status`, `PUT …/acceptance`, `GET …/orders/{id}`, `PUT …/orders/{id}/pickup`, `GET …/pickup-slots`, `PUT …/products/{id}/sold-out` |
| каталог и меню | `…/products*`, `…/categories/{id}/weekdays`, `…/daily-menus*` |
| уведомления магазина | `GET/PUT …/notification-settings`, `api/notification-channels/*` |
| устройства | `GET /api/push/config?site=Orders`, `GET/POST/DELETE /api/push/subscriptions*` |
| подписка | `GET /api/billing/subscription?line=Orders`, `POST/DELETE /api/billing/subscription/request` |
