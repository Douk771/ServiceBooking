# API_CONTRACT — цикл 39 ServiceBooking: «Дома», цикл 2 — услуги-слоты, напоминание накануне заезда

**Разделы §39.20–§39.39.** Решения и механизмы — `ARCHITECTURE_CYCLE39.md` §39.0–§39.19 (включая §39.0a — решения заказчика
по `LEGAL_REVIEW_CYCLE39.md`). **Источник истины по форме** — `contracts/cycle39/openapi.yaml`: при расхождении этого текста
со схемой по форме права схема, по смыслу, порядку проверок и текстам — этот документ. Рядом:
`contracts/cycle39/service-vectors.json` (эталон расчётов), `contracts/cycle39/dom-routes.json` (маршруты dom). Требования —
`SPEC_CYCLE39_STAYS_SLOTS_ICAL.md`. Базовая ревизия — `develop` = `ece8038`.

Контракт цикла 37 (`API_CONTRACT_CYCLE37.md`, `contracts/cycle37/`) действует; здесь — только новое и изменённое.

---

## §39.20. Конвенции и исключения цикла

- Все конвенции §37.20 без изменений: camelCase, enum строками, `DateTime` UTC, даты `YYYY-MM-DD`, деньги — целые рубли;
  400/402/429 — строка `text/plain`; 401/403/404 — пустое тело; все 409 `/api/stays/*` — JSON `{code, message, …}`.
- **Время суток услуги — минуты бизнес-дня.** `businessDate` (дата D) + `startMinute` / `endMinute` — минуты от 00:00 даты D
  (`360` = 06:00, `1560` = 02:00 следующих суток, `1800` = 06:00 следующих суток). Граница бизнес-дня — 06:00 (конфигурация
  сервера). Длительность — целые часы `hours`.
- **Подписи времени собирает сервер** (`label`, `endLabel`, `timeLabel`, `dateLabel`): гостю — календарными датами («пт 15
  янв, 22:00 — сб 16 янв, 01:00»; слово «бизнес-день» не используется, ЮР39-8); персоналу — на бизнес-дне старта («Пт, 15
  янв · 22:00 – 01:00 (сб)»). Фронт свои подписи строит только в редакторах (TS-двойник по векторам `format`).
- Новые 409-DTO: `ServiceRefusalDto` (расчёт, заказ, добавление сеанса), `ServiceOrderGuestConflictDto` (действия гостя над
  заказом), `StayBookingGuestConflictDto` (отмена сеанса брони гостем), `ServiceStaffConflictDto` (действия персонала над
  сеансом), `StaysServiceConflictDto` (кабинет услуг и напоминания). Существующие `StayRefusalDto` и `StaysConflictDto`
  получают новые коды (§39.21).
- Неизвестный токен заказа или брони, чужой `sessionId`/`serviceId` → 404 пустым телом на всех маршрутах.
- **Сумма «к возврату не меньше 0 ₽» не приходит ни в одном ответе**: при нулевом остатке `refund.kind = CostsOnlyUpTo`,
  `refundAtLeastRub = null`, текст — «только фактические расходы, не больше N ₽» (Т39-02).

---

## §39.21. Изменения существующих маршрутов и контракта цикла 37

### §39.21.1 Правки `contracts/cycle37/openapi.yaml` (DO-39-03, один коммит с lint-чисткой и регенерацией)

Дописываются значения перечислений, которые начнут приходить в ответах маршрутов цикла 37 (иначе `Cycle37ContractTests`
упадёт): `StayChargeKind` += `ServiceSlot`, `ServiceItem`; `StayRefusalCode` += `ServiceSlotUnavailable`,
`ServiceSelectionInvalid`; `StaysConflictCode` += `ProviderRequiredForServiceOrders`; `StaysPermission` += `ManageServices`,
`EditServiceContent`, `ManageServiceDates`. Закрытые входы цикла 37 (`StayQuoteInput`, `CreateStayBookingInput`) в cycle37 не
меняются: поле `services` описано только в cycle39 (сервер принимает тело и с ним, и без него).

### §39.21.2 Изменённые ответы и входы (полные новые поля — `openapi.yaml`, тег `shared-changed`)

| Маршрут | Изменение |
|---|---|
| `GET /api/stays/public/companies/{slug}` | в конец: `services[]` (`PublicServiceSummaryDto`: опубликованные неархивные услуги, порядок владельца) и `acceptsServiceOrdersWithoutStay`. У услуги: `canOrderWithoutStay` (= настройка компании ∧ гейт), `priceFromRub` (минимальная цена часа), `url` `/<slug>/uslugi/<serviceSlug>` |
| `GET /api/stays/public/companies/{slug}/houses/{houseSlug}` | в конец: `servicesForStay[]` (`{name, url}` — опубликованные услуги с «доступна для броней домов») |
| `POST /api/stays/public/houses/{houseId}/quote` | вход: необязательный `services[]` (≤ 3). Ответ: `services[]` (`StayQuoteServiceDto` — расчёт каждого сеанса, `ok`, проблемы); `lines` получают строки `ServiceSlot`/`ServiceItem` (`prepayEligible: false`); `totalRub` и `dueAtCheckInRub` включают услуги, `prepayRub` — нет |
| `POST /api/stays/public/houses/{houseId}/bookings` | вход: необязательный `services[]` (≤ 3; по умолчанию пусто — ничего не выбрано); `expectedTotalRub` — итог с услугами. Отказ сеанса → 409 `StayRefusalDto` с `code` `ServiceSlotUnavailable` / `ServiceSelectionInvalid` и `serviceIndex`; **бронь не создаётся** (§39.24.4) |
| `GET /api/stays/bookings/public/{token}` | в конец: `sessions[]` (`PublicBookingSessionDto`), `servicesBlock` (`canAdd`, `cannotAddText`, `hint`), `arrivalReminder` (`{text, sentAtUtc}` или null — снимок, §39.33.4). Строки суммы — с услугами |
| `GET /api/stays/companies/{companyId}` | `myPermissions` — новые права; `settings.acceptServiceOrdersWithoutStay`; `awaitingPaymentCount` включает заказы услуг; `gate`: `NoProviderInfo` теперь и при предоплате 0 % (ЮР39-2) |
| `PUT /api/stays/companies/{companyId}/settings` | в конец тела: `acceptServiceOrdersWithoutStay: boolean | null` (null/отсутствие — не менять, чтобы старый клиент полной заменой не выключил). Включение без полных сведений об исполнителе → 409 `StaysConflictDto` `ProviderRequiredForServiceOrders` «Заполните сведения об исполнителе — без них заказы услуг не принимаются». Менять может только владелец (`ManageCompany`) |
| `GET …/board` | в конец: `services[]`, `serviceCells[]` (§39.30.1); `awaitingPaymentCount` с заказами |
| `GET …/schedule` | в каждый `days[]` — `sessions[]` (`ScheduleSessionDto`, §39.30.4) |
| `GET …/bookings/{bookingId}` | в конец: `sessions[]` (`StaffBookingSessionDto`); `availableActions` += `AddSession` (бронь активна и выезд не наступил); `lines` с услугами |
| `GET /api/profile/export` | в конец: `stayServiceOrders[]`; в `stayBookings[]` — `sessions[]`, `arrivalReminderText` (§39.36) |
| `GET /api/profile/delete-account/preview` | в конец: счётчик `stayServiceOrders` |
| `GET /api/admin/retention/policy` | в конец: `stayServiceOrderUnpaidDays`, `stayServiceOrderPersonalDataDays`, `stayServiceOrderEventDays`, `stayServiceScheduleEventDays` |
| `GET /api/admin/companies` (элемент `Stays`), карточка компании | в конец (P1, US-39-28): `staysServices: {servicesCount, sessionsLast30Days}` |

Гейт `StaysBookingGate` (ЮР39-2): сведения об исполнителе обязательны при любой предоплате — для домов это значит, что
компания с предоплатой 0 % без сведений получает `acceptingBookings: false` (`reasonCode: NoProviderInfo`).

---

## §39.22. Услуги анонимно (политика `stays-public` — 120/мин на IP)

### §39.22.1 `GET /api/stays/public/companies/{slug}/services/{serviceSlug}` → `PublicServiceDto`

Нет компании / не «Дома» / нет услуги / не опубликована → 404. Услуга в архиве или компания заблокирована → 200 с
`available: false`, `notAvailableText` «Услуга недоступна для бронирования». Нет тарифа или гейт закрыт → `acceptingBookings:
false`, `notAcceptingText` «Бронирование временно недоступно».

Содержит: галерея, описание, `minHours`/`maxHours`/`stepMinutes`, `priceTable[]` (подписи гостевым форматом — «Пт 18:00 — 02:00
(ночь на сб) — 2 000 ₽/ч»), активные позиции, `bufferMinutes` (только при «показывать время на подготовку»), `standalone`
(`ordering` — заказ без проживания включён; `prepayPercent`, `cancellationPolicy`, `cancellationBoundaryHours`,
`cancellationSummary` — только при предоплате; `holdMinutes`; `notOrderingText` «Можно добавить к брони дома»), `company`,
`provider` (публичная часть, ЮР-3), `today` (текущий бизнес-день), `timeZoneId`. **Нет** туристического налога (Т39-15),
реквизитов, занятых интервалов и имён.

### §39.22.2 `GET /api/stays/public/services/{serviceId}/availability?from=&days=&houseId=&checkIn=&checkOut=`

`days` 1…31 (14 по умолчанию), `from` по умолчанию — текущий бизнес-день. Режим «к проживанию» (форма брони дома): все три
параметра `houseId`, `checkIn`, `checkOut` — тогда учитываются только старты внутри проживания (время заезда и выезда —
настройки компании) и `AvailableForHouseBookings`. 400: «Неверный период», «Укажите дом и обе даты проживания». → `days[]`
`{businessDate, label, hasStarts}`.

### §39.22.3 `GET /api/stays/public/services/{serviceId}/starts?date=&houseId=&checkIn=&checkOut=` → `ServiceStartsDto`

Старты бизнес-дня `date` по `ServiceSlotCalculator` (`ARCHITECTURE_CYCLE39.md` §39.4): `starts[]` `{startMinute, startUtc,
label («22:00», «00:30 (ночь на сб)»), maxHours, options[] {hours, endLabel}}` — от `minHours` до `maxHours` старта; пусто →
`reason` (`DateInPast` / `BeyondHorizon` / `Closed` / `NoStarts`) и `noStartsText` «На эту дату свободного времени нет».
Занятое время, зазоры, причины занятости не раскрываются. Удержанные (неистёкшие) сеансы — заняты; истёкшие — свободны.

### §39.22.4 `POST /api/stays/public/services/{serviceId}/quote` → `ServiceQuoteDto` (всегда 200)

Вход `PublicServiceQuoteInput` (выбор + необязательный режим «к проживанию»). 400 — только форма (§39.31). Проблемы
(`problems[]`, коды §39.25) не ошибка: суммы 0 при `StartUnavailable`, `NoPriceForHours`, `HoursOutOfRange`. Ответ: `time`,
`hourPrices[]` (подпись начала часа с календарной датой), `lines[]` (`Service` — «Баня · 2 ч», `Item` — «Веник берёзовый × 2»),
`serviceAmountRub`, `itemsAmountRub`, `totalRub`; для заказа с предоплатой — `prepayPercent`, `prepayRub`, `dueOnSiteRub`,
`holdMinutes`, `cancellationSummary`; без предоплаты и в режиме «к проживанию» — `prepayRub = 0`, `payOnSiteText` «Оплата на
месте, в компании. Через сервис оплата не производится».

### §39.22.5 `POST /api/stays/public/services/{serviceId}/orders` — заказ без проживания (US-39-11)

Аноним или вошедший. Политика `stay-service-create`: 5 в час на IP (аноним), 20 в час на пользователя. Тело
`CreateServiceOrderInput`.

**Порядок проверок** (первый отказ):
1. Форма → 400 строкой (§39.31).
2. Услуга: нет / не опубликована / в архиве / компания не «Дома» → 404. Компания заблокирована → 409 `NotAcceptingBookings`
   (`reasonCode: CompanyBlocked`).
3. Заказ без проживания выключен → 409 `ServiceOrdersDisabled` «Компания не принимает заказы услуг без проживания».
4. Заказ с `(CompanyId, idempotencyKey)` уже есть → **200** с ним.
5. Аноним: капча → 400 «Подтвердите, что вы не робот»; телефон → 400 «Введите номер телефона в формате +7 (900) 000-00-00».
   Вошедший — номер аккаунта (поле тела игнорируется).
6. Гейт → 409 `NotAcceptingBookings` с `reasonCode` (`NoPlan`, `OverHouseLimit`, `NoPaymentDetails` — только при предоплате,
   `NoProviderInfo` — всегда) и `message` «Бронирование временно недоступно».
7. Лимиты номера (под замком номера): удержанных заказов ≥ 2 на платформе или ≥ 1 в компании → 429 «Слишком много
   неоплаченных заказов. Оплатите или отмените текущий заказ»; ≥ 10 за сутки → 429 «Слишком много заказов с этого номера.
   Попробуйте позже».
8. Под замком услуги: выбор (`IsStartAllowed`) → 409 `ServiceRefusalDto` с кодом §39.25 (текст по `Diagnose`); итог ≠
   `expectedTotalRub` → 409 `PriceChanged` с `quote`. Повтор — с тем же ключом и новым итогом.
9. Успех → **201** `CreateServiceOrderResponse`: `token`, `orderUrl` (`https://dom.ezbook.ru/s/<token>`), `order`. Статус
   `Held` (предоплата > 0, таймер `HoldMinutes` компании) или `Confirmed`.
10. Гонка БД (ограничение занятости услуги) → 409 `SlotTaken`; гонка ключа → 200 с существующим.

---

## §39.23. Отдельный сеанс по ссылке `/s/<token>` (политика `stay-public`; файлы — `stay-proof`; push — `stay-push`)

### §39.23.1 `GET /api/stays/service-orders/public/{token}` → `PublicServiceOrderDto`

Опрос 15 с (SPEC ≤ 30 с), немедленно при `visibilitychange`. Поля: `status`, `displayStatus` (`Completed` — `Confirmed` после
конца сеанса), `statusText`, `serverTimeUtc`, `holdExpiresAtUtc` (только `Held`), `service`, `company` (адрес, карты,
телефон), `provider` (`ProviderFullDto` — снимок; ФИО физлица только здесь, ЮР-3), `time` (гостевой формат), `items[]`,
`lines[]`, `hourPrices[]`, суммы, `payment` (реквизиты, назначение, сумма — в `Held`/`AwaitingPaymentCheck`/`Confirmed` при
предоплате; иначе null), `paymentConfirmedAtUtc`, `paymentProofs[]`, `proofs`, `cancellation` (`policy`, `summary`,
`canCancel`, `refund` §39.27, `cannotCancelText`), `guestName`, `guestPhoneMasked`, `comment`, `statusReason`, `outcomeText`,
`notifications`, `availableActions[]` (`AttachProof`, `Cancel`).

### §39.23.2 `POST …/{token}/payment-proofs` (multipart `file`)

Как §37.26.2: 400 — те же тексты файла; 409 `ServiceOrderGuestConflictDto`: `ProofNotAllowed` «Заказ уже {статус} —
подтверждение оплаты не нужно», `ProofLimitReached` «Можно приложить не больше 3 файлов», `HoldExpired` «Время на оплату
истекло, заказ снят. Если вы уже оплатили — свяжитесь с компанией: <телефон>» (заказ в теле — уже `ExpiredUnpaid`). Успех →
201; первый файл: `Held → AwaitingPaymentCheck`.

### §39.23.3 `GET …/{token}/payment-proofs/{proofId}` — как §37.26.3.

### §39.23.4 `POST …/{token}/cancel` (тело `{}`)

Активный статус и `serverNow < startUtc` → 200 (`CancelledByGuest`, время свободно, персонал уведомлён). Иначе 409
`CancelNotAllowed` с актуальным заказом: «Сеанс уже начался — по вопросам свяжитесь с компанией: <телефон>» / «Заказ уже
отменён». Истёкшее удержание доснимается в этом же запросе, `message` — как `HoldExpired`. Перед вызовом фронт показывает
`cancellation.refund.text` и «возврат делает компания; свяжитесь: <телефон>».

### §39.23.5 Push — `POST …/{token}/push-subscription`, `…/push-subscription/remove` — как §37.26.5 (тексты 409 —
«Компания отключила уведомления о бронях», «Заказ завершён — уведомления не нужны», «Уведомления временно недоступны»).

---

## §39.24. Услуги к проживанию по ссылке брони `/b/<token>`

### §39.24.1 `GET /api/stays/bookings/public/{token}/services` → `BookingServicesDto`

`canAdd` — бронь `Held`/`AwaitingPaymentCheck`/`Confirmed`, момент выезда не наступил, гейт компании (без предоплаты) открыт,
активных сеансов < 5. Иначе `cannotAddText`: «Бронь завершена», «Время выезда наступило», «Бронирование временно
недоступно», «К брони можно добавить не больше 5 услуг». `hint` при `Held` — «Сеанс сохранится, если бронь будет оплачена».
`services[]` — опубликованные услуги с «доступна для броней домов» и `dates[]` — бизнес-дни проживания с `hasStarts`.

### §39.24.2 `GET …/{token}/services/{serviceId}/starts?date=` → `ServiceStartsDto` (в пределах проживания, `minLead` услуги).

### §39.24.3 `POST …/{token}/services/{serviceId}/quote` (`ServiceSelectionInput`) → `ServiceQuoteDto` (оплата на месте).

### §39.24.4 `POST …/{token}/sessions` (`AddSessionInput`) — добавить сеанс (US-39-09)

Политика `stay-session-add` (10 в час на токен + 30 в час на IP). Порядок: форма (400) → бронь по токену (404) →
идемпотентность `(бронь, ключ)` → 200 с бронью → услуга (не той компании → 404; не опубликована / архив / не для броней →
409 `ServiceNotAvailableForStays` «Эту услугу нельзя добавить к брони») → гейт (409 `NotAcceptingBookings`) → под замками
дома и услуги: бронь не активна или выезд наступил → 409 `BookingNotActive` «Бронь завершена — добавить услугу нельзя»; ≥ 5
сеансов → 409 `TooManySessions` «К брони можно добавить не больше 5 услуг»; выбор → 409 коды §39.25 (`OutsideStay` — «Время
должно быть в пределах проживания: с {дата заезда} {время} до {дата выезда} {время}»); итог ≠ `expectedTotalRub` → 409
`PriceChanged`. Успех → **201** `PublicStayBookingDto` (с `sessions`). Гонка БД → 409 `SlotTaken`. Капчи нет.

**Бронь дома с сеансами (US-39-10, P1)** — `POST /api/stays/public/houses/{houseId}/bookings` с `services[]`: те же проверки
выбора для каждого элемента после проверок §37.24 (п. 1–7); отказ → 409 `StayRefusalDto` `ServiceSlotUnavailable` «Это время
уже занято: {услуга}, {время}. Выберите другое время или бронируйте без услуги» / `ServiceSelectionInvalid` (текст кода
§39.25) с `serviceIndex`. Ни бронь, ни сеансы не создаются.

### §39.24.5 `POST …/{token}/sessions/{sessionId}/cancel` (тело `{}`)

Сеанс `Active` и `serverNow < startUtc` → 200 (`CancelledByGuest`, строки суммы удалены, персонал уведомлён). Иначе 409
`StayBookingGuestConflictDto` `CancelNotAllowed` с бронью: «Сеанс уже начался — по вопросам свяжитесь с компанией: <телефон>»
/ «Сеанс уже отменён». Денег за сеанс в брони не вносилось — отмена без последствий (Т39-03).

---

## §39.25. Отказы выбора сеанса — `ServiceRefusalDto` и `problems[]`

Форма: `{code, message, reasonCode?, quote?}` — `quote` только у `PriceChanged`, `reasonCode` только у `NotAcceptingBookings`.
Порядок проверки выбора — `ServiceSlotCalculator.Diagnose`.

| `code` | `message` (сервер, дословно) |
|---|---|
| `ServiceOrdersDisabled` | «Компания не принимает заказы услуг без проживания» |
| `ServiceNotAvailableForStays` | «Эту услугу нельзя добавить к брони» |
| `NotAcceptingBookings` | «Бронирование временно недоступно» |
| `DateInPast` | «Эта дата уже прошла» |
| `BeyondHorizon` | «Бронирование открыто до {дд.мм.гггг}» |
| `HoursOutOfRange` | «Длительность — от {min} до {max} {часов}» |
| `OutsideStay` | «Время должно быть в пределах проживания: с {дата} {время} до {дата} {время}» |
| `TooEarly` | «Забронировать можно не позже чем за {N} до начала» (N — «1 час», «30 минут», «2 часа») |
| `StartUnavailable` | «Это время недоступно. Выберите другое» |
| `NoPriceForHours` | «На часть выбранного времени нет цены — выберите другое время» |
| `SlotTaken` | «Это время уже занято. Выберите другое» |
| `ItemUnavailable` | «Позиция «{название}» больше недоступна» |
| `ItemQuantityExceeded` | ««{название}» — не больше {N} на сеанс» |
| `BookingNotActive` | «Бронь завершена — добавить услугу нельзя» |
| `TooManySessions` | «К брони можно добавить не больше 5 услуг» |
| `PriceChanged` | «Стоимость изменилась: {итог} ₽. Проверьте и подтвердите ещё раз» |

---

## §39.26. Кабинет: услуги (`[Authorize]`; права `ARCHITECTURE_CYCLE39.md` §39.12; не участник — 404, нет права — 403)

| Маршрут | Право | Тело → ответ |
|---|---|---|
| `GET …/services` | `EditServiceContent` или `ManageServiceDates` | `ServiceListItemDto[]` (включая архивные; `publishProblems[]`, `hasSessions`) |
| `POST …/services` | `ManageServices` | `{name}` → 201 `ServiceManageDto` (не опубликована, адрес из названия, без цен и окон, настройки по умолчанию §4.6 SPEC); 21-я → 409 `ServiceLimitReached` «У компании может быть не больше 20 услуг» |
| `PUT …/services/order` | `ManageServices` | `{ids[]}` (полный список неархивных) → `ServiceListItemDto[]`; 400 «Список услуг не совпадает с текущим» |
| `GET …/services/{serviceId}` | `EditServiceContent` или `ManageServiceDates` | `ServiceManageDto` |
| `DELETE …/services/{serviceId}` | `ManageServices` | 204; есть сеансы → 409 `ServiceHasSessions` «У услуги есть сеансы — её можно только архивировать» |
| `PUT …/{serviceId}/setup` | `ManageServices` | `ServiceSetupInput` → `ServiceManageDto` |
| `PUT …/{serviceId}/content` | `EditServiceContent` | `{description}` → `ServiceManageDto` |
| `POST …/{serviceId}/publish` / `unpublish` / `archive` | `ManageServices` | `{}` → `ServiceManageDto` |
| `POST …/{serviceId}/photos` | `EditServiceContent` | multipart → 201 `ServicePhotoDto`; 11-е → 409 `PhotoLimitReached` «У услуги может быть не больше 10 фото»; политика `company-photos` |
| `PUT …/{serviceId}/photos/order`, `DELETE …/photos/{photoId}` | `EditServiceContent` | как у дома |
| `GET …/{serviceId}/price-rules` | `ManageServices` | `PriceRulesDto` (`rules[]`, `matrix[]` 7 × 24 — часы 6…29 с подписями «01:00 (след. дня)», `priceRub`, `inWindowWithoutPrice`) |
| `POST …/price-rules`, `PUT|DELETE …/price-rules/{ruleId}` | `ManageServices` | `PriceRuleInput` → `PriceRulesDto` (POST — 201) |
| `GET|POST …/items`, `PUT …/items/order`, `PUT|DELETE …/items/{itemId}` | `ManageServices` | `ServiceItemInput` → `ServiceItemDto`; 21-я → 409 `ItemLimitReached` «У услуги может быть не больше 20 позиций» |

**Проверки (400 строкой):** название 1–100 «Укажите название услуги»; адрес — `serviceSlugPattern` «Адрес услуги — латиница,
цифры и дефис, 2–50 символов», занят в компании → 409 `SlugTaken` «Такой адрес уже есть у другой услуги»; описание ≤ 2000
«Описание — не длиннее 2000 символов»; часы «Минимум часов — от 1 до 12», «Максимум часов — от минимума до 12»; шаг «Шаг
старта — 30 или 60 минут»; зазор «Время на подготовку — от 0 до 240 минут с шагом 15»; минимальное время до начала «Минимальное
время до начала — от 0 до 48 часов с шагом 30 минут»; предоплата «Предоплата — от 1 до 100 % или без предоплаты»; рубеж
«Срок для полного возврата — от {min} до {max} часов до начала»; позиция «Название позиции — от 1 до 100 символов», «Цена — от
0 до 100 000 ₽», «Максимум на сеанс — от 1 до 50»; правило цены — тексты §39.31.

**Публикация** — `StaysServiceConflictDto` в порядке: `ServiceArchived` «Услуга в архиве»; `ServiceNoPrice` «Добавьте хотя бы
одно правило цены»; `ServiceNoWindows` «Задайте свободное время: недельный шаблон или окна на даты в пределах горизонта».
**Пересечение правил цены** → 409 `PriceRuleOverlap` «Правило пересекается с «{подпись правила}» ({цена} ₽/ч)» +
`conflictingRule`. Больше 50 правил → 409 `PriceRuleLimitReached` «У услуги может быть не больше 50 правил цены».

Предупреждение «нет предоплаты» и тексты `StayServiceCancellationOwnerNotice`, `StayServiceSafetyOwnerNotice` — фронт
(запасные тексты).

---

## §39.27. Возврат отдельного сеанса — `ServiceRefundViewDto` (ЮР39-1)

`{kind, refundAtLeastRub, maxDeductionRub, text}`; правило — `ARCHITECTURE_CYCLE39.md` §39.8, векторы `refund`.

| `kind` | `refundAtLeastRub` | `text` (сервер) |
|---|---|---|
| `NothingPaid` | 0 | «Сеанс не оплачен — отмена без последствий» / «Заказ уже завершён — отменять нечего» |
| `Full` | предоплата | «По правилу сеанса вам должны вернуть всю предоплату — X ₽» (отмена компанией — «Предоплата возвращается полностью: X ₽») |
| `PartialAtLeast` | X > 0 | «К возврату не меньше X ₽. Компания вправе удержать только фактические расходы на подготовку — не больше N ₽» |
| `CostsOnlyUpTo` | **null** | «Компания вправе удержать только фактические расходы на подготовку, не больше N ₽. Остальное она обязана вернуть» |

`cancellationSummary` шаблонов: «Без удержаний: отмена до начала — вся предоплата возвращается»; «Расходы на подготовку:
отмена не позднее чем за {N} ч до начала — вся предоплата; позже — компания вправе удержать только фактические расходы на
подготовку, не больше стоимости первого часа». Полные тексты — `StayServiceCancellationTerms` (черновик §12.1 обзора).

---

## §39.28. Кабинет: расписание услуги

| Маршрут | Право | Тело → ответ |
|---|---|---|
| `GET …/{serviceId}/weekly-schedule` | `ManageServices` или `ManageServiceDates` | `WeeklyScheduleDto` (7 дней, окна с подписями) |
| `PUT …/{serviceId}/weekly-schedule` | `ManageServices` | `WeeklyScheduleInput` (ровно 7 дней, ≤ 3 окна) → `ScheduleSaveResultDto` |
| `GET …/{serviceId}/date-overrides?month=YYYY-MM` | `ManageServiceDates` | `ServiceMonthDto` (каждый бизнес-день месяца: `source` `Template`/`Override`/`Closed`, окна, комментарий, кто и когда менял) |
| `PUT …/{serviceId}/date-overrides/{date}` | `ManageServiceDates` | `DateOverrideInput` (`closed` или окна; комментарий ≤ 300) → `ScheduleSaveResultDto` |
| `DELETE …/{serviceId}/date-overrides/{date}` | `ManageServiceDates` | → `ScheduleSaveResultDto` (день по шаблону) |

- Окна — минуты бизнес-дня; проверки и тексты — §39.31 (векторы `windows`). Ручная дата в прошлом → 400 «Нельзя менять
  прошедшие даты».
- Сохранение **не отменяет** сеансы: `outsideSessions[]` (активные будущие сеансы, вышедшие за окна) и `warningText` «{N}
  {сеанс/сеанса/сеансов} вне нового расписания остаются в силе».
- Каждое изменение — журнал `StayServiceScheduleEvent` (кто, когда, до/после) и ревизия шахматки.

---

## §39.29. Кабинет: сеансы

### §39.29.1 Список и карточка

- `GET …/service-sessions?status=&serviceId=&from=&to=&page=&pageSize=` (`ViewBookings`, политика `stays-board`) — по
  умолчанию заказы `AwaitingPaymentCheck` (от старого первого файла к новому); с `status` других значений — по времени начала
  ↓; `from`/`to` — бизнес-дни. Сеансы в брони приходят, если `status` не задан и задан период. `StaffServiceSessionPage`.
- `GET …/service-sessions/{sessionId}` (`ViewBookings`) → `StaffServiceSessionCardDto` (оба вида): время (формат
  персонала), часы и цены, позиции, суммы, `preparedUntilLabel`, статус, гость (телефон полный), комментарий, бронь дома,
  `addedBy` (кто и по какому основанию), файлы, факт оплаты, `availableActions`, журнал.

### §39.29.2 Действия (`ManageBookings`, `expectedVersion` = `card.version`)

| Маршрут | Тело | Применимо | Успех |
|---|---|---|---|
| `POST …/{sessionId}/confirm-payment` | `{expectedVersion}` | заказ `AwaitingPaymentCheck` | `Confirmed` |
| `POST …/{sessionId}/reject-payment` | `{expectedVersion, reason}` | заказ `AwaitingPaymentCheck` | `PaymentRejected`, время свободно |
| `POST …/{sessionId}/cancel` | `{expectedVersion, reason}` | заказ в активном статусе **или** сеанс брони `Active` | `CancelledByOwner`; гость уведомлён |

`reason` 1–300 → иначе 400 «Укажите причину — гость её увидит». Версия не совпала → 409 `ServiceStaffConflictDto`
`VersionMismatch` «Сеанс уже изменён — проверьте актуальное состояние»; недопустимый переход → `InvalidTransition` «Действие
недоступно в статусе «{статус}»». В теле — актуальная карточка.

`GET …/{sessionId}/payment-proofs/{proofId}` — как §37.30.4 (событие просмотра не чаще раза в 10 минут на пару).

### §39.29.3 Добавление к брони и ручной заказ

- `GET …/services/{serviceId}/starts?date=&bookingId=` (`ManageBookings`) — старты для персонала: `minLead = 0`,
  неопубликованная (не архивная) услуга допустима; с `bookingId` — в пределах проживания.
- `POST …/service-sessions/quote` (`ManageBookings`) — `StaffServiceQuoteInput` → `ServiceQuoteDto`.
- `POST …/bookings/{bookingId}/sessions` (`ManageBookings`) — `StaffAddSessionInput` (**`requestBasis` обязателен**:
  `Phone` / `InPerson` / `Messenger`; иначе 400 «Укажите, как гость попросил услугу»). Порядок и коды — как §39.24.4, но
  гейт тарифа не проверяется. Успех → 201 `StaffStayBookingCardDto` (с `sessions`); гостю — сообщение «по вашей просьбе» и
  блок на странице брони (§39.33.4).
- `POST …/service-sessions` (P1, `ManageBookings`) — `ManualServiceOrderInput` → 201 `StaffServiceSessionCardDto`: заказ
  сразу `Confirmed`, `isManual`, предоплата 0; телефон необязателен; мессенджер гостю не отправляется (нет согласия, Т37-12).

---

## §39.30. Шахматка, «День услуг», график

### §39.30.1 `GET …/board` — группа «Услуги»

`services[]` (`{id, name, isPublished, isArchived}`: неархивные + архивные с сеансами в окне), `serviceCells[]` — по одной на
(услуга, бизнес-день) с активными сеансами: `count`, `firstStartLabel` «с 16:00», `crossesMidnightLabel` «до 01:00» (если
последний сеанс дня кончается после полуночи) или null, `needsAction` (есть заказ `AwaitingPaymentCheck`). При `changed:
false` массивов нет.

### §39.30.2 `GET …/service-day?date=` (`ViewBookings`, политика `stays-board`) → `ServiceDayDto`

`axis` (`fromMinute` — минимальное начало окна/сеанса/переходящего зазора, `toMinute` — максимальный конец, в пределах
360…1800; `midnightMinute` 1440), по каждой неархивной услуге (и архивной с сеансами в этот день): `windows[]`, `closed`,
`bars[]`: `Session` (`label` — дом или «без проживания», `stateText`, `needsAction`), `Buffer` («подготовка», от конца сеанса
до конца зазора, обрезается по 1800), `CarryOverBuffer` («подготовка после вчерашнего сеанса» — зазор сеанса предыдущего
бизнес-дня, ушедший за 06:00). Удержанные заказы и брони — со `stateText` «Ждёт оплаты».

### §39.30.3 Автообновление — `sinceRevision` шахматки (общая ревизия), опрос ≤ 30 с; «День услуг» перечитывается при смене ревизии.

### §39.30.4 `GET …/schedule` — `ScheduleSessionDto` в `days[].sessions[]`

`sessionId`, `serviceName`, `timeLabel` «22:00 – 01:00 (сб)», `preparedUntilLabel` «01:30 (сб)», `houseName` (null — «без
проживания»), `guestName`, `items[] {name, quantity}`, `comment` (null, если пользователь — горничная и у компании выключен
`housekeeperSeesGuestComment`), `paymentUnconfirmed`. Схема **закрыта** (`additionalProperties: false`): телефона, сумм,
файлов, статуса оплаты нет вовсе. В график — активные сеансы на бизнес-дне старта, родитель `AwaitingPaymentCheck` /
`Confirmed`.

---

## §39.31. Тексты 400 (фронт показывает дословно у поля)

| Текст | Где |
|---|---|
| «Выберите дату», «Неверный формат даты» | выбор |
| «Выберите время начала» / «Время — с шагом 30 минут» | `startMinute` |
| «Укажите число часов» | `hours` |
| «Количество — от 0 до {N}» | позиции |
| «Укажите имя» / «Имя — не длиннее 100 символов» | имя |
| «Введите номер телефона в формате +7 (900) 000-00-00» | телефон |
| «Комментарий — не длиннее 500 символов» | комментарий |
| «Подтвердите, что вы не робот» | капча |
| «Нужен ключ запроса — обновите страницу» | `idempotencyKey` |
| «Окно должно уложиться с 06:00 до 06:00 следующего дня» | окно |
| «Окна {a} и {b} ({день}) пересекаются» | окна |
| «Не больше трёх окон в день» | окна |
| «Начало окна должно быть раньше конца» | окно |
| «Выберите дни недели» | правило цены |
| «Часы — с 06:00 до 06:00 следующего дня» | правило цены |
| «Цена за час — от 1 до 100 000 ₽» | правило цены |
| «Укажите, как гость попросил услугу» | `requestBasis` |
| «Укажите причину — гость её увидит» | `reason` |
| «Нельзя менять прошедшие даты» | ручная дата |
| «Укажите дом и обе даты проживания» | режим «к проживанию» |
| напоминание — §39.33.5 | |

---

## §39.32. Частота запросов — новые политики (`RateLimits`; `appsettings.Testing.json` поднимает до 10000)

| Политика | Где | Лимит | Текст 429 |
|---|---|---|---|
| `stay-service-create` | создание заказа гостем | 5/ч на IP (аноним), 20/ч на пользователя | «Слишком много попыток. Попробуйте позже» |
| `stay-session-add` | добавление сеанса по ссылке брони | 10/ч на токен **и** 30/ч на IP (цепочка) | «Слишком много попыток. Попробуйте позже» |
| `stays-public` (существующая) | страница услуги, availability, starts, quote (в том числе по токену брони) | 120/мин на IP | как прежде |
| `stay-public`, `stay-proof`, `stay-push` (существующие) | маршруты заказа по токену | как у брони | как прежде |
| `stays-board` (существующая) | service-day, service-sessions, предпросмотр напоминания | 120/мин на пользователя | как прежде |

Лимиты номера — `Stays:Services:PhoneLimits` (`MaxHeldPerPhone` 2, `MaxHeldPerPhonePerCompany` 1, `MaxCreatedPerPhonePerDay`
10); тексты — §39.22.5 п. 7.

---

## §39.33. Напоминание накануне заезда

### §39.33.1 `GET …/arrival-reminder` (`ManageCompany`) → `ArrivalReminderSettingsDto`

`enabled` (только чтение — переключатель в `settings`), `time` (по умолчанию «18:00»), `template` (null — по умолчанию),
`isDefault`, `effectiveTemplate`, `defaultTemplate`, `pushTextEnabled` (по умолчанию false), `placeholders[]` (`{token,
description, inMessenger, onPage, inPush}` — таблица `ARCHITECTURE_CYCLE39.md` §39.11.2), `limits` (700 / 1000 / 1000 / 180),
`ownerNotice` (`StayReminderTemplateOwnerNotice` + версия), `pushNotice` (`StayReminderPushOwnerNotice` + версия),
`warnings[]` текущего шаблона.

### §39.33.2 `PUT …/arrival-reminder` (`ManageCompany`, `[RequiresOwnerTerms]`) — `ArrivalReminderInput`

`time`, `template` (null или равный умолчанию → хранится null), `pushTextEnabled`, `confirmCodeMarkers`, `ownerNoticeVersion`
(версия показанного владельцу текста), `pushNoticeVersion` (обязательна при включении push). Порядок: 400 (§39.33.5) →
маркеры кодов без подтверждения → 409 `StaysServiceConflictDto` `ReminderConfirmationRequired` (`markers[]`, `noticeText` —
текст `StayCheckInInfoOwnerNotice`, `message` «В тексте есть похожее на код доступа: {маркеры}. Коды по умолчанию отправляются
только ссылкой. Сохранить всё равно?») → сохранение + строка истории → 200 `ArrivalReminderSettingsDto`.

### §39.33.3 `POST …/arrival-reminder/preview` → `ArrivalReminderPreviewDto` (политика `stays-board`)

Без сохранения: `messenger`, `page` (`{text, length}`), `push` (`{text, length, usesFixedText, dropped[] {line, text,
reason}}`), `warnings[]`, `errors[]` (`TooLong`, `UnknownPlaceholder`, `ForbiddenWords` — без 400, чтобы показывать по мере
ввода), `confirmationRequired`, `markers[]`. Данные — пример сервера; `bookingId` (P1) — реальная бронь компании, иначе 404.

### §39.33.4 Что видит гость

- **Мессенджер** — текст по шаблону; если в шаблоне нет `{СсылкаНаБронь}` — в конце «Бронь: <ссылка>»; строка отписки — всегда
  последней. NULL-шаблон — прежний текст байт-в-байт.
- **Страница брони** — блок «Напоминание о заезде» (`arrivalReminder.text`) с момента отправки, даже если каналов нет; без
  строки ссылки; снимок, а не текущий шаблон.
- **Push** — «ezbook · Дома» / текст по шаблону в режиме push (`ARCHITECTURE_CYCLE39.md` §39.11.4: выпадают строки с именем,
  адресом, телефоном, суммой, ссылкой и строки владельца с ≥ 4 цифрами, ссылкой, e-mail, телефоном, словами «код / пароль /
  Wi-Fi / вайфай / ключниц / сейф / домофон»), ≤ 180 символов. Переключатель выключен, или шаблон NULL, или всё выпало —
  «Завтра заезд — откройте бронь». `url` `/b/<token>` — только в зашифрованной нагрузке.
- **Сеанс, добавленный персоналом** — сообщение (мессенджер, если включён у брони; push, если подписан): «{Компания}: по вашей
  просьбе к брони «{Дом}» добавлена услуга «{Услуга}», {время гостевым форматом}. Оплата на месте. Если вы этого не просили,
  отмените на странице брони — без последствий: {ссылка}» (push — без ссылки и названия компании: «Услуга добавлена к брони —
  откройте бронь»).

### §39.33.5 Тексты 400 напоминания

«Время — с 08:00 до 22:00 с шагом 30 минут»; «Текст напоминания — не длиннее 700 символов»; «Неизвестная подстановка {X}.
Можно: {Компания}, {Дом}, {ДатаЗаезда}, {ДатаВыезда}, {Ночей}, {ВремяЗаезда}, {ВремяВыезда}, {Услуги}, {ИмяГостя}, {Адрес},
{ТелефонКомпании}, {КОплатеПриЗаселении}, {СсылкаНаБронь}»; «Не используйте слова «задаток», «невозвратный», «депозит»»;
«Подтвердите, что прочитали предупреждение о push».

Мягкие предупреждения (`warnings[]`, не отказ): `CancellationTermsInText` — «Условия отмены задаёт выбранный шаблон — другие
условия в тексте напоминания не действуют»; `Passport` — «Не просите гостя прислать фото паспорта: документ при заселении
проверяете вы на месте»; `CardNumber` — «Похоже на номер карты — не отправляйте данные карт в сообщениях»; `PassportNumber` —
«Похоже на паспортные данные — не отправляйте их в сообщениях».

### §39.33.6 `GET …/arrival-reminder/history` (`ManageCompany`, P1 показа) — до 100 строк, новые сверху.

---

## §39.34. Уведомления — тела (дословные тексты — `StayNotificationTexts`)

- **Персоналу** (push + MAX; без имени и телефона гостя; `url` — `https://dom.ezbook.ru/cabinet/<companyId>/service-sessions/<sessionId>`,
  `tag` `ss-<sessionId>`): `StaffStaySessionAdded` «Услуга к брони · «{дом}» · {Услуга}, {время персонала}»;
  `StaffServiceOrderCreated` «Новый заказ услуги · {Услуга}, {время}»; `StaffServiceOrderPaymentProofUploaded` «Приложено
  подтверждение оплаты · {Услуга}, {время}»; `StaffServiceSessionCancelledByGuest` «Гость отменил сеанс · {Услуга}, {время}».
  «Новая бронь…» при сеансах в брони — « · услуги: N» в конце.
- **Гостю заказа** (мессенджер — по своей галочке и флагу компании; push — если подписан): создан (ссылка, итог, при
  предоплате — сумма, срок и реквизиты этого заказа), «осталось 10 минут», снят, оплата подтверждена, отклонена (причина,
  телефон), отменён компанией (с предоплатой — «предоплата возвращается полностью… право на возмещение убытков»; без —
  «оплата за сеанс не вносилась»). Push: «Статус вашего заказа изменился» / «Осталось 10 минут, чтобы приложить подтверждение
  оплаты», `tag` `so-<orderId>`, `url` `/s/<token>`.
- **Гостю брони**: `StayGuestSessionAdded` (§39.33.4), `StayGuestSessionCancelledByOwner` «{Компания}: сеанс «{Услуга}», {время}
  отменён компанией. Причина: {причина}. Оплата за сеанс не вносилась. Телефон компании: {телефон}. {ссылка}» (push —
  «Статус вашей брони изменился»).
- Время в текстах гостю — календарными датами; ссылки — только `PublicSiteLinks` (`StayServiceOrderPageUrl(token)`,
  `StaysCabinetServiceSessionUrl(companyId, sessionId)` — новые методы).

---

## §39.35. Права — `StaysPermission` (дописаны `ManageServices`, `EditServiceContent`, `ManageServiceDates`)

Таблица — `ARCHITECTURE_CYCLE39.md` §39.12. Фронт строит меню по `myPermissions`.

---

## §39.36. Персональные данные — изменения существующих маршрутов

- `GET /api/profile/export`: `stayServiceOrders[]` — `companyName`, `serviceName`, `timeLabel` (гостевой формат), `hours`,
  `items[]`, `totalRub`, `prepayRub`, `status`, `guestName`, `guestPhone`, `comment`, `statusReason`, `orderUrl`,
  `paymentProofs[]` (метаданные), `events[]` (видимые гостю); гостевые заказы — только при подтверждённом номере. В
  `stayBookings[]` — `sessions[]` (`serviceName`, `timeLabel`, `items[]`, `totalRub`, `state`) и `arrivalReminderText`.
- `POST /api/profile/delete-account`: заказы обезличиваются (имя, телефон, комментарий, аккаунт), файлы удаляются; у броней
  стирается `arrivalReminderText` (Т39-11).
- `POST /api/profile/consents/revoke` (`ProviderDelivery`): охватывает активные заказы (число — в конец превью).

---

## §39.37. Маршруты dom и адреса — `contracts/cycle39/dom-routes.json`

Надмножество cycle37: `servicePagePattern` `/:slug/uslugi/:serviceSlug`, `serviceOrderPagePattern` `/s/:token`,
`serviceSlugPattern`, `reservedHouseSlugs` (`uslugi`, `services`, `bani`, `banya`, `kalendar`, `kalendari`, `calendar`,
`ical`), `serviceQueryParams` (`date`), новые `spaRoutes` кабинета. Дом с адресом из `reservedHouseSlugs` → 409 `SlugReserved`
«Этот адрес зарезервирован — выберите другой» (маршрут `setup` дома цикла 37). `StaysSlugPolicy` и `domRoutes.test.ts`
переходят на этот файл.

---

## §39.38. Эталон расчётов — `contracts/cycle39/service-vectors.json`

Разделы: `businessDay` (+`toUtc`), `windows`, `priceRules` (+`labels`), `price`, `money`, `starts`, `overlap`, `refund`
(+`config`), `format` (+`businessDateLabel`), `reminderTemplate` (только бэкенд). Юнит-тесты C# и vitest dom читают **один**
файл; правка правила = правка векторов в том же коммите. Векторы `starts` ST03/ST04 и `overlap` OV01/OV03 — те же сценарии,
что функциональные тесты гонок (§39.5.4 архитектуры).

---

## §39.39. Сводка новых маршрутов (для `Cycle22RouteTable.golden.txt`)

**58 новых маршрутов** под `/api/stays/*`.

| Группа | Маршрутов | Авторизация |
|---|---|---|
| `/api/stays/public/companies/{slug}/services/{serviceSlug}`, `/api/stays/public/services/{serviceId}/availability|starts|quote|orders` | 5 | anon (`orders` — anon или auth) |
| `/api/stays/service-orders/public/{token}…` (get, payment-proofs POST/GET, cancel, push-subscription, push-subscription/remove) | 6 | anon |
| `/api/stays/bookings/public/{token}/services`, `…/services/{serviceId}/starts`, `…/quote`, `…/sessions`, `…/sessions/{sessionId}/cancel` | 5 | anon |
| `…/services` (список, создание, порядок, карточка, удаление, setup, content, publish, unpublish, archive, фото ×3, правила цены ×4, позиции ×5) | 22 | auth + `StaysPermission` |
| `…/services/{serviceId}/weekly-schedule` (GET, PUT), `…/date-overrides` (GET), `…/date-overrides/{date}` (PUT, DELETE) | 5 | auth + `StaysPermission` |
| `…/service-day`, `…/services/{serviceId}/starts`, `…/service-sessions` (GET, POST), `…/quote`, `…/{sessionId}` (карточка, confirm, reject, cancel, файл), `…/bookings/{bookingId}/sessions` | 11 | auth + `StaysPermission` |
| `…/arrival-reminder` (GET, PUT), `…/preview`, `…/history` | 4 | auth + `ManageCompany` |

Изменённых маршрутов цикла 37 — 10 (тег `shared-changed`); эталон маршрутов для них не меняется (атрибуты те же).
Методы, `operationId` и атрибуты — `openapi.yaml`.
