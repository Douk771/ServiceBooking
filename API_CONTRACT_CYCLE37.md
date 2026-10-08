# API_CONTRACT — цикл 37 ServiceBooking: «Дома», цикл 1 (dom.ezbook.ru)

**Разделы §37.20–§37.39.** Решения и механизмы — `ARCHITECTURE_CYCLE37.md` §37.0–§37.19 (включая §37.0a — решения
заказчика по `LEGAL_REVIEW_CYCLE37.md`). **Источник истины по форме** — `contracts/cycle37/openapi.yaml`: при
расхождении этого текста со схемой по форме права схема, по смыслу, порядку проверок и текстам — этот документ. Рядом:
`contracts/cycle37/dom-routes.json` (маршруты dom, резерв слов, формат адресов) и `contracts/cycle37/stay-vectors.json`
(эталон расчётов). Требования — `SPEC_CYCLE37_STAYS_HOUSES.md`. Базовая ревизия — `develop` = `0f9b9fb`.

Корневой `API_CONTRACT.md` — документ цикла 3, не перезаписывается.

---

## §37.20. Конвенции (действуют без изменений) и исключения цикла

- camelCase; enum — строками (имена членов C#); `DateTime` — ISO-8601 UTC (`…Z`); даты ночей — `"YYYY-MM-DD"`; время
  суток — `"HH:mm"`; **деньги — целые рубли** (`integer`, поля с суффиксом `Rub`).
- Осознанные 400/402/429 — **голая строка `text/plain`** по-русски (тексты — §37.36). 401/403 — пустое тело; 404 без
  явного тела — пустое тело и **неотличимо** от «не существует».
- 451 — гейты правовых документов, как сейчас; владельческие действия — `[RequiresOwnerTerms]`.
- **Исключение, как у заказов (цикл 23):** все 409 вертикали — **JSON** с машиночитаемым `code` и готовым русским
  `message` (фронт ветвится по `code`, печатает `message`):
  - `StayRefusalDto` — отказ при расчёте и создании брони;
  - `StayGuestConflictDto` — действия гостя по ссылке (подтверждение оплаты, отмена) с актуальной бронью;
  - `StayStaffConflictDto` — действия персонала (`VersionMismatch`, `InvalidTransition`) с актуальной карточкой;
  - `StaysConflictDto` — кабинет (адреса, дома, цены, публикация, блокировки, уведомления).
  Исключения из исключения: 409 салонных маршрутов для компании «Дома» и 409 общих маршрутов персонала — **строка**
  (§37.21), как у магазина; отказ триала — `StaysTrialOutcomeDto`.
- Текст, который видит человек (статусы, причины, отказы, суммы возврата, подписи строк суммы), собирает **сервер**.
- Все сроки брони считаются по поясу компании, зафиксированному в брони; «сегодня» в публичных DTO — поле `today`.
- Анонимные маршруты вертикали — под `/api/stays/public/…` и `/api/stays/bookings/public/{token}…`; кабинетные —
  `/api/stays/companies/{companyId}/…`.

---

## §37.21. Изменения существующих маршрутов

### §37.21.1 Салонные маршруты и компания «Дома»

Все маршруты закрытого перечня §389.2 цикла 23, которые сейчас отвечают магазину 409, для компании `Kind = Stays`
отвечают **409 `text/plain`**: «Это компания «Дома»: записи, услуги и расписание для неё недоступны.» Для магазина текст
прежний (байт-в-байт). Маршруты записи в галерею компании (`POST|DELETE|PUT order /api/companies/{id}/photos`,
`GET …/photo-usage`) для «Домов» → 409 строкой «У компании «Дома» фото добавляются к домам.» `GET /api/companies/{id}/photos`
→ `[]`.

Маршруты магазина (`/api/shops/*`, `/api/storefront/*`, `/api/orders/*`) с id/slug компании «Дома» → 404 (как с
салоном). Маршруты `/api/stays/*` с id салона или магазина → 404.

### §37.21.2 Компании

| Маршрут | Изменение |
|---|---|
| `GET /api/companies/my`, `/member` | `?kind=Stays` поддерживается; без параметра — `Services`, как раньше |
| `GET /api/companies/kinds-summary` | в конец `CompanyKindsSummaryDto` — `stays: {count, siteUrl}` |
| `GET /api/companies/{slug}` | для «Домов» — тот же `CompanyDto` с `kind: "Stays"` и `publicUrl` = `https://dom.ezbook.ru/<slug>` (ezbook и goods переадресуют) |
| `GET /api/companies`, `/public` | без изменений (только салоны) |
| `PUT /api/companies/{id}` | для «Домов»: пояс — только по городу (ручной `timeZoneId` ≠ поясу города → 400 «Часовой пояс компании задаётся городом»); `cityId` ≠ Шерегеш → 400 «Город компании «Дома» — Шерегеш»; остальные поля профиля (название, описание, телефон, e-mail, ссылки на карты) — как у магазина. `bookingHorizonDays`, `allowSelfBooking`, `requirePrepayment`, `clientRescheduleMinHours` для «Домов» игнорируются |
| `PUT /api/companies/{id}/address`, `POST …/logo` | без изменений, работают для «Домов» |
| `GET /api/admin/companies` | `?kind=Stays`; колонка `kind` = `Stays`, `publicUrl` dom |
| `PUT /api/admin/companies/{id}` (`isActive`) | без изменений; заблокированная компания «Дома» не принимает брони и не видна в каталоге |

### §37.21.3 Персонал

- `POST /api/Companies/{id}/members` — в конец `AddMemberDto` поле **`position`** (`Manager` | `Housekeeper`, null по
  умолчанию). Для компании «Дома»: `role` должен быть `Master` (иначе 400 «В компанию «Дома» можно добавить только
  сотрудника.»), `position` обязателен (иначе 400 «Укажите должность: управляющий или горничная.»); салонный лимит мест
  не применяется; сотрудников ≥ `Stays:MaxStaffPerCompany` (30) → 409 строкой «В компании не может быть больше 30
  сотрудников.» Для салона и магазина `position` должен быть null (иначе 400 «Должность задаётся только сотрудникам
  компании «Дома».»).
- `MemberDto` — в конец поле `position` (null у салонов, магазинов и владельца).
- **Новый** `PUT /api/Companies/{id}/members/{memberId}/position` `{position}` — `[Authorize]`, `[RequiresOwnerTerms]`,
  владелец компании или SA; не «Дома» → 409 строкой (общий текст §37.21.1); владелец (`CompanyOwner`) → 400 «Должность
  владельца не меняется.»; 204.
- `DELETE …/members/{memberId}` — без изменений (доступ пропадает сразу).

### §37.21.4 Биллинг и тарифы

- `GET /api/billing/subscription?line=Stays` — `OwnerSubscriptionDto`, в конец блок `stays` (`StaysSubscriptionBlockDto`:
  `housesPublished`, `maxHouses`, `isTrial`, `trialEndsAtUtc`, `warningLevel` — `None|TrialEnding3d|TrialEnding1d|Expired|
  NoPlan|OverLimit`, `text`); `availablePlans` — активные тарифы линейки без триала, у каждого в конце `maxHouses` (null — без
  ограничения; у тарифов других линеек всегда null); `trial` (салонный) = null.
- `POST /api/billing/subscription/request` — `line: "Stays"`; тариф другой линейки → 400; правило «одна заявка на аккаунт»
  прежнее.
- `GET|POST /api/admin/plans`, `PUT /api/admin/plans/{id}` — `line: "Stays"` допустим; поле `maxHouses` (в конец входа и
  DTO; для других линеек должно быть null → 400). Удалить/деактивировать тариф с Id `StaysPlans.TrialSeedId` → 409 строкой
  «Пробный тариф линейки «Дома» нельзя удалить или выключить.»
- `PUT /api/admin/billing-accounts/{id}/subscription` — `line: "Stays"` пишет `StaysSubscription`; карточка аккаунта
  (`GET /api/admin/billing-accounts/{id}`) — в конец блок `staysSubscription` (`planId`, `planName`, `paidUntil`, `isActive`,
  `housesPublished`, `maxHouses`).
- `GET /api/pricing` — без изменений (линейка «Дома» не попадает).

### §37.21.5 Push

- `GET /api/push/config` — `siteUrls` получает `stays`; при `allSites=true` компании трёх видов, порядок
  `Services → Orders → Stays`; у компании `kind`.
- `POST /api/push/subscriptions` — `site: "Stays"` допустим (устройства, включённые на dom). Лимит — на пару (пользователь,
  сайт), как сейчас.
- Тело push сотрудникам о бронях домов — §37.33.

---

## §37.22. Каталог и публичные страницы (аноним, политика `stays-public` — 120/мин на IP)

### §37.22.1 `GET /api/stays/public/amenities` → `HouseAmenityDto[]` (`{code, label}`), порядок — как в справочнике.

### §37.22.2 `GET /api/stays/public/catalog`

Query: `checkIn`, `checkOut` (оба или ни одного), `guests` (1…60, по умолчанию 1), `maxPricePerNight` (целое > 0),
`page` (≥ 1), `pageSize` (1…50, по умолчанию 20).

- Без дат: все опубликованные дома компаний, которые активны, показываются в каталоге и принимают брони по гейту; `guests`
  ≤ вместимость + доп. места; `priceFromRub` — минимальная цена ночи; `maxPricePerNight` — по ней.
- С датами: только дома, где все ночи свободны (удержанные — заняты, истёкшие удержания — свободны), выполнены правила
  длительности с учётом разрыва, у всех ночей есть цена, гости помещаются; в элементе `totalRub` (стоимость **ночей**),
  `averageNightRub`, `nights`; фильтр цены — по `averageNightRub`.
- Сортировка: по цене за ночь ↑ (`averageNightRub` или `priceFromRub`), затем по названию дома.
- 400 строкой: «Укажите обе даты: заезд и выезд», «Дата выезда должна быть позже даты заезда», «Неверный формат даты»,
  «Число гостей — от 1 до 60». Даты, не проходящие правила конкретного дома, — не ошибка, такой дом просто не попадает.

Ответ — `PagedResult<StayCatalogItemDto>`: `houseId`, `url` (относительный путь `/<companySlug>/<houseSlug>`),
`houseName`, `companyName`, `coverUrl`, `coverThumbUrl`, `capacity`, `extraBedsMax` (0, если выключены), `dogsForbidden`,
`address`, `registryNumber`, `priceFromRub`, `totalRub`, `averageNightRub`, `nights` (последние три — только с датами).

### §37.22.3 `GET /api/stays/public/companies/{slug}`

Query: те же `checkIn`, `checkOut`, `guests` (необязательны). Нет компании / не «Дома» → 404. Заблокированная → 200 с
`available: false` и `notAvailableText` «Страница недоступна» без домов (SPEC US-37-26). Работает при выключенном показе
в каталоге.

`PublicStaysCompanyDto`: `id`, `slug`, `name`, `logoUrl`, `description`, `phone`, `available`, `notAvailableText`,
`acceptingBookings`, `notAcceptingText` («Бронирование временно недоступно» — Q2), `provider` (`ProviderPublicDto`,
§37.22.5), `houses` (`StayCatalogItemDto[]`, опубликованные неархивные, порядок владельца; с датами — те же правила, но
недоступный дом не скрывается, а приходит с `availableForDates: false`), `today`.

### §37.22.4 `GET /api/stays/public/companies/{slug}/houses/{houseSlug}`

Нет дома / не опубликован и не в архиве → 404. Архивный или компания заблокирована → 200 с `available: false`,
`notAvailableText` «Дом недоступен для бронирования».

`PublicHouseDto`: `id`, `slug`, `name`, `description`, `photos[]` (`url`, `thumbnailUrl`), `capacity`, `extraBeds`
(`enabled`, `max`, `priceRub`), `dogsForbidden`, `dogFeeRub`, `hasCot`, `cotFeeRub`, `amenities[]` (`HouseAmenityDto`),
`address` (дома или компании), `yandexMapsUrl`, `twoGisUrl`, `registry` (`objectKind`, `objectKindLabel`,
`registryNumber`, `registryUrl` — null, если вид не указан), `company` (`slug`, `name`, `phone`, `logoUrl`, `url`),
`provider` (`ProviderPublicDto`), `rules` (`checkInTime`, `checkOutTime`, `minNights`, `maxNights`, `horizonDays`,
`allowGapFill`, `allowSameDayCheckIn`, `holdMinutes`, `prepayPercent`, `cancellationPolicy`, `cancellationSummary` — текст
сервера), `priceFromRub`, `acceptingBookings`, `notAcceptingText`, `available`, `notAvailableText`, `today`, `timeZoneId`.

**Никогда не содержит** реквизитов для оплаты (Т37-04), телефонов и имён гостей, статусов чужих броней.

### §37.22.5 `ProviderPublicDto` (ЮР-3)

| `status` | Публично | Только на странице брони и в кабинете владельца (`ProviderFullDto`) |
|---|---|---|
| `Organization`, `IndividualEntrepreneur` | `statusLabel`, `name`, `inn`, `ogrn`, `claimsAddress` | то же |
| `SelfEmployed`, `Individual` | `statusLabel` («Плательщик налога на профессиональный доход» / «Физическое лицо»), `inn`; `name`, `ogrn`, `claimsAddress` = null | + `name`, `claimsAddress` |
| не заполнено | весь объект null | null |

### §37.22.6 `GET /api/stays/public/houses/{houseId}/calendar`

Query: `from`, `to` (даты, `to` > `from`, не больше 400 дней; по умолчанию — с сегодня на горизонт компании). Нет дома /
не опубликован → 404. 400 строкой: «Неверный период календаря».

`HouseCalendarDto`: `houseId`, `today`, `from`, `to`, `minNights`, `maxNights`, `allowGapFill`, `allowSameDayCheckIn`,
`lastNight` (последняя бронируемая ночь по горизонту), `days[]` — на **каждую** дату `[from, to)`: `date`, `state`, `priceRub`
(цена ночи, если есть; показ в ячейке — P1, поле есть всегда).

| `state` | Когда | Показ |
|---|---|---|
| `Free` | ночь свободна и у неё есть цена, в пределах горизонта | «Свободно» |
| `MayFreeUp` | ночь занята бронью в статусе «Удержана» с неистёкшим таймером | «Возможно освободится» |
| `Occupied` | бронь «Ожидает проверки оплаты» / «Подтверждена», блокировка, внешний календарь | «Занято» |
| `Unavailable` | нет цены, прошлое, за горизонтом | «Недоступно» |

Причины занятости, имена, статусы не раскрываются. Состояние **ночи** D относится к ночи, начинающейся в D. Выбор диапазона
на фронте — функции `stayRules.ts` по векторам; сервер всё равно перепроверяет (§37.24).

---

## §37.23. Расчёт: `POST /api/stays/public/houses/{houseId}/quote` (аноним, `stays-public`)

Тело `StayQuoteInput`: `checkIn`, `checkOut`, `adults` (≥ 1), `children` (≥ 0), `dogs` (0…20), `needCot`. Нет дома / не
опубликован → 404. 400 строкой — только форма (§37.36). **Всегда 200**, ничего не резервирует.

`StayQuoteDto`: `ok` (нет проблем), `problems[]` (`{code, message}` — коды §37.25.2, в том же порядке проверок),
`nights`, `nightPrices[]` (`{date, priceRub}`), `lines[]` (`StayChargeLineDto`: `kind`, `label`, `quantity`,
`unitPriceRub`, `nights`, `amountRub`, `prepayEligible`), `extraBeds`, `totalRub`, `prepayPercent`, `prepayRub`,
`dueAtCheckInRub`, `averageNightRub`, `holdMinutes`, `checkInTime`, `checkOutTime`, `cancellationPolicy`,
`cancellationSummary`, `acceptingBookings`, `notAcceptingText`. При проблемах, мешающих расчёту (нет цены, неверные даты,
гости не помещаются), суммы = 0, `lines` пусты.

Правило сумм — `ARCHITECTURE_CYCLE37.md` §37.6.3 и `stay-vectors.json → money`.

---

## §37.24. Создание брони гостем: `POST /api/stays/public/houses/{houseId}/bookings`

Аноним или вошедший. Политика `stay-create`: 5 в час на IP (аноним), 20 в час на пользователя. Тело `CreateStayBookingInput`:

| Поле | Правило |
|---|---|
| `checkIn`, `checkOut`, `adults`, `children`, `dogs`, `needCot` | как у quote |
| `guestName` | 1–100 после trim, обязательно |
| `guestPhone` | обязателен для анонима (российский, `PhoneNormalizer`); у вошедшего **игнорируется** — берётся номер аккаунта |
| `arrivalTime` | `"HH:mm"` с шагом 30 мин от времени заезда до 23:30, или null («не знаю») |
| `comment` | ≤ 500 |
| `notifyByMessenger` | bool, по умолчанию false (отдельная галочка `StayMessengerConsent`, Т37-12); учитывается, только если компания предлагает мессенджер, иначе молча false |
| `expectedTotalRub` | итог, который видел гость (`quote.totalRub`) |
| `idempotencyKey` | uuid, генерируется при открытии формы, живёт до успеха |
| `captchaToken` | обязателен для анонима, если капча включена на сервере |

**Порядок проверок и ответы** (первый отказ):

1. Форма → 400 строкой (§37.36).
2. Дом по `houseId`: нет, не опубликован (в том числе архив), компания не «Дома» → 404. Компания заблокирована → 409
   `NotAcceptingBookings` (`reasonCode: CompanyBlocked`).
3. Бронь с `(CompanyId, idempotencyKey)` уже есть → **200** `CreateStayBookingResponse` с ней (без капчи и лимитов).
4. Аноним: капча неверна → 400 «Подтвердите, что вы не робот»; телефон не российский → 400 «Введите номер телефона в формате
   +7 (900) 000-00-00».
5. Гейт компании → 409 `NotAcceptingBookings` с `reasonCode` (`NoPlan`, `OverHouseLimit`, `NoPaymentDetails`,
   `NoProviderInfo`) и `message` «Бронирование временно недоступно» (подробность для владельца — в кабинете, не гостю).
6. Лимиты номера (под замком номера): удержаний этого номера на платформе ≥ 2 или в этой компании ≥ 1 → **429** «Слишком
   много неоплаченных броней. Оплатите или отмените текущую бронь»; броней этого номера за 24 ч ≥ 10 → 429 «Слишком много
   броней с этого номера. Попробуйте позже». Лимит IP (политика) → 429 «Слишком много попыток. Попробуйте позже».
7. Под замком дома: правила дат и гостей → 409 `StayRefusalDto` с кодом §37.25.2 (первый по порядку); итог ≠
   `expectedTotalRub` → 409 `PriceChanged` с `quote` (новый расчёт). Гость подтверждает и повторяет запрос **с тем же
   `idempotencyKey`** и новым `expectedTotalRub`.
8. Успех → **201** `CreateStayBookingResponse`: `token`, `bookingUrl` (абсолютный `https://dom.ezbook.ru/b/<token>`),
   `booking` (`PublicStayBookingDto`). Статус `Held` (предоплата > 0) или `Confirmed` (0 %).
9. Гонка БД (ограничение занятости) → 409 `DatesUnavailable`; гонка идемпотентности → 200 с существующей.

Снимки, которые сервер пишет в бронь, — `ARCHITECTURE_CYCLE37.md` §37.2.4.

---

## §37.25. Отказы бронирования — `StayRefusalDto`

### §37.25.1 Форма

`{ code, message, reasonCode?, quote? }` — `quote` только у `PriceChanged`, `reasonCode` только у `NotAcceptingBookings`.

### §37.25.2 Коды (порядок = порядок проверки)

| `code` | `message` (сервер, дословно) |
|---|---|
| `InvalidDates` | «Дата выезда должна быть позже даты заезда» |
| `CheckInInPast` | «Дата заезда уже прошла» |
| `SameDayNotAllowed` | «Заезд в день бронирования недоступен — выберите дату с завтрашнего дня» |
| `BeyondHorizon` | «Бронирование открыто до {дд.мм.гггг}» |
| `MaxNightsExceeded` | «Максимальный срок проживания — {N} {ночей}» |
| `DatesUnavailable` | «Эти даты уже заняты. Выберите другие» |
| `MinNightsNotMet` | «Минимальный срок проживания — {N} {ночей}» |
| `TooManyGuests` | «В доме помещается не больше {N} гостей» (с доп. местами — «… {N} гостей, включая доп. места») |
| `DogsNotAllowed` | «В этом доме нельзя проживать с собаками» |
| `CotNotAvailable` | «В этом доме нет детской кроватки» |
| `NoPriceForNights` | «На часть выбранных ночей нет цены — выберите другие даты» |
| `NotAcceptingBookings` | «Бронирование временно недоступно» |
| `PriceChanged` | «Стоимость изменилась: {итог} ₽. Проверьте и подтвердите бронь ещё раз» |

---

## §37.26. Страница брони по ссылке (аноним, токен — секрет доступа)

Неизвестный токен → 404 пустым телом на всех маршрутах раздела. Политика `stay-public` — 120/мин на IP.

### §37.26.1 `GET /api/stays/bookings/public/{token}` → `PublicStayBookingDto`

| Поле | Смысл |
|---|---|
| `status`, `displayStatus` (`Completed` — вычисляемый), `statusText` | крупно, текстом |
| `serverTimeUtc`, `holdExpiresAtUtc` | обратный отсчёт (только `Held`) |
| `house` | `name`, `url`, `coverUrl`, `address`, `yandexMapsUrl`, `twoGisUrl` — **сразу**, с момента создания |
| `company` | `name`, `phone`, `url` |
| `provider` | `ProviderFullDto` (снимок на момент брони; ФИО физлица — только здесь, ЮР-3) |
| `checkInDate`, `checkOutDate`, `checkInTime`, `checkOutTime`, `nights`, `adults`, `children`, `dogs`, `needCot`, `extraBeds`, `arrivalTime` | |
| `guestName`, `guestPhoneMasked` (`+7 (9••) •••-••-12`), `comment` | |
| `lines[]`, `nightPrices[]`, `totalRub`, `prepayPercent`, `prepayRub`, `dueAtCheckInRub` | снимок |
| `payment` | `details`, `purpose`, `amountRub` — в статусах `Held`, `AwaitingPaymentCheck`, `Confirmed`; в конечных — null |
| `paymentConfirmedAtUtc` | |
| `paymentProofs[]` | `id`, `contentType`, `sizeBytes`, `uploadedAtUtc`, `purged` |
| `proofs` | `canAttach`, `maxCount` (3), `maxBytes` (10 485 760), `acceptedTypes` |
| `cancellation` | `policy`, `summary` (текст правила), `canCancel`, `refund` (`StayRefundViewDto`: `kind` — `NothingPaid`/`Full`/`Partial`, `refundAtLeastRub`, `maxDeductionRub`, `text` — «К возврату не меньше X ₽…», считается на `serverTimeUtc`), `cannotCancelText` («Время заезда наступило — по вопросам отмены свяжитесь с компанией: <телефон>») |
| `statusReason` | причина отклонения/отмены владельцем |
| `outcomeText` | готовый текст конечного статуса (обзор §15.3: «Оплата не подтверждена… обязана вернуть деньги или восстановить бронь…», «Компания отменила бронь… предоплата должна быть возвращена полностью… право на возмещение убытков», «Время на оплату истекло… если вы успели оплатить — свяжитесь с компанией») |
| `checkInInfo` | `{companyText, houseText}` — только после выпуска (архитектура §37.7.4), иначе null |
| `notifications` | `webPush` (`available`, `publicKey`), `messengerSelected` |
| `availableActions[]` | `AttachProof`, `Cancel` |

### §37.26.2 `POST /api/stays/bookings/public/{token}/payment-proofs` (multipart, поле `file`)

Политика `stay-proof` (6 за 10 мин на токен и 20 в час на IP). Порядок:
1. Файл: нет → 400 «Выберите файл»; > 10 МБ → 400 «Файл больше 10 МБ»; тип по сигнатуре не PDF/JPEG/PNG/WebP → 400 «Можно
   приложить PDF, JPEG, PNG или WebP»; повреждённое изображение → 400 «Файл повреждён или это не изображение»; мало места →
   400 «На сервере закончилось место. Попробуйте позже.»
2. Статус конечный или `Confirmed` → 409 `ProofNotAllowed` («Бронь уже {статус} — подтверждение оплаты не нужно»).
3. Уже 3 файла → 409 `ProofLimitReached` («Можно приложить не больше 3 файлов»).
4. `Held` и таймер истёк (в том числе гонка с задачей) → 409 **`HoldExpired`**: «Время на оплату истекло, бронь снята. Если вы
   уже оплатили — свяжитесь с компанией: <телефон>». Бронь в теле — уже `ExpiredUnpaid`.
5. Успех → **201** `PublicStayBookingDto` (первый файл переводит `Held → AwaitingPaymentCheck`, таймер исчезает).

`StayGuestConflictDto`: `{code, message, booking: PublicStayBookingDto}`.

### §37.26.3 `GET /api/stays/bookings/public/{token}/payment-proofs/{proofId}`

Файл этой брони. Чужой/удалённый `proofId` → 404. Заголовки: `Cache-Control: private, no-store`,
`X-Content-Type-Options: nosniff`, `Content-Security-Policy: sandbox`; PDF — `Content-Disposition: attachment;
filename="payment-proof.pdf"`, изображения — `inline`.

### §37.26.4 `POST /api/stays/bookings/public/{token}/cancel` (тело `{}`)

- `Held`, `AwaitingPaymentCheck`, `Confirmed` и `serverNow` < момента заезда → **200** `PublicStayBookingDto`
  (`CancelledByGuest`, даты свободны, персонал уведомлён).
- Иначе → 409 `CancelNotAllowed` с актуальной бронью (`message` — `cannotCancelText` или «Бронь уже отменена»). Удержание с
  истёкшим таймером (в том числе ещё не снятое задачей) сервер в этом же запросе переводит в «Снята: не оплачена», и `message` —
  «Время на оплату истекло, бронь снята. Если вы уже оплатили — свяжитесь с компанией: <телефон>» (как `HoldExpired` §37.26.2);
  тот же текст — `cannotCancelText` страницы брони, пока задача не дошла до удержания.
Отмена без версии: сервер перечитывает бронь и решает по текущему статусу. Фронт перед вызовом показывает диалог с
`cancellation.refund.text` и строкой «возврат делает компания; свяжитесь: <телефон>».

### §37.26.5 Push гостя

- `POST …/{token}/push-subscription` `{endpoint, keys: {p256dh, auth}, deviceLabel?}` → 204. Политика `stay-push` (20/ч на
  IP). Upsert по `(StayBookingId, Endpoint)`, не больше 5 на бронь (старая вытесняется). 409 строкой: «Компания отключила
  уведомления о бронях», «Бронь завершена — уведомления не нужны», «Уведомления временно недоступны».
- `POST …/{token}/push-subscription/remove` `{endpoint}` → 204 идемпотентно.

### §37.26.6 `GET /api/stays/bookings/my` (auth, P1)

Брони аккаунта (`GuestUserId`), активные сверху, затем прошедшие за 12 месяцев; гостевые брони на тот же номер не
подтягиваются. `StayMyBookingDto[]`: `bookingUrl`, `houseName`, `companyName`, `checkInDate`, `checkOutDate`, `status`,
`displayStatus`, `statusText`, `totalRub`.

---

## §37.27. Кабинет: компания «Дома»

Все маршруты — `[Authorize]`. Права — `StaysPermission` (`ARCHITECTURE_CYCLE37.md` §37.9); нет членства → 404; есть
членство, нет права → 403 пустым телом. Маршруты с правом `ManageCompany`/`ManageHouses` — `[RequiresOwnerTerms]`.

| Маршрут | Право | Ответ |
|---|---|---|
| `POST /api/stays/companies` | любой вошедший | 201 `StaysCompanyCreatedDto` |
| `GET /api/stays/companies/my` | — | `StaysCompanyListItemDto[]` (все компании «Дома», где пользователь участник) |
| `GET /api/stays/slug-check?name=&slug=&companyId=` | — | `StaysSlugCheckDto` `{suggested, available, conflict?}` — 200 всегда |
| `GET /api/stays/trial` | — | `StaysTrialStateDto` (`offered`, `eligible`, `refusalCode`, `message`, `termsVersion`, `termsText`, `durationDays`, `endsAtUtc`) |
| `POST /api/stays/trial` `{termsVersion}` | владелец аккаунта | 200 `StaysTrialOutcomeDto`; отказ — 409 `StaysTrialOutcomeDto` с `granted: false` |
| `GET /api/stays/companies/{companyId}` | `ViewCabinet` или `ViewSchedule` | `StaysCompanyManageDto` |
| `PUT /api/stays/companies/{companyId}/settings` | `ManageCompany` | `StaysCompanyManageDto` |
| `PUT /api/stays/companies/{companyId}/payment-details` | `ManageCompany` | `StaysCompanyManageDto` |
| `PUT /api/stays/companies/{companyId}/provider` | `ManageCompany` | `StaysCompanyManageDto` |
| `PUT /api/stays/companies/{companyId}/slug` | `ManageCompany` | `StaysCompanyManageDto`; 409 `StaysConflictDto` (`SlugInvalid`/`SlugReserved`/`SlugTaken`) |
| `GET /api/stays/companies/{companyId}/qr` | `ViewCabinet` | `image/png` |
| `GET|PUT /api/stays/companies/{companyId}/notification-settings` | GET `ViewCabinet`, PUT `ManageCompany` | `StaysNotificationSettingsDto` |

### §37.27.1 Создание — `StaysCompanyCreateInput`

`name` (1–200), `slug` (необязательно — иначе предлагается из названия), `description` (≤ 2000), `phone` (российский,
обязательно — телефон для гостей), `ownerTermsVersion` (обязательно), `trialTermsVersion` (необязательно).

Порядок (как `CompanyCreationService`): соглашение (400 / 503 / 409 строками — тексты существующие) → название (400
«Укажите название») → телефон (400 «Введите номер телефона в формате +7 (900) 000-00-00») → адрес по `StaysSlugPolicy` (409
`StaysConflictDto`) → город Шерегеш (503 «Справочник городов не готов») → lock аккаунта → адрес под lock `company-slug` →
создание (`StaysSettings` по умолчанию, `ShowInPublicListing = true`) → согласие `TermsOwner` → новый токен → **после
коммита** попытка триала, если передан `trialTermsVersion`. Число компаний «Дома» отдельно не ограничено. Ответ
`StaysCompanyCreatedDto`: `company` (`StaysCompanyManageDto`), `token`, `trial` (`StaysTrialOutcomeDto` или null).

### §37.27.2 `StaysCompanyManageDto`

`id`, `name`, `slug`, `description`, `phone`, `email`, `logoUrl`, `address`, `yandexMapsUrl`, `twoGisUrl`, `cityName`,
`timeZoneId`, `isActive`, `showInCatalog`, `publicUrl`, `myRole` (`Owner`/`Manager`/`Housekeeper`/`SuperAdmin`),
`myPermissions[]`, `settings` (`StaysSettingsDto` — для `Housekeeper` null), `paymentDetails` (`PaymentDetailsDto` —
только при `ManageCompany`, иначе null), `provider` (`ProviderFullDto` — только при `ManageCompany`), `gate`
(`{accepting, reasonCode, reasonText}` — текст для владельца, напр. «Заполните реквизиты для оплаты — без них гости не
могут бронировать с предоплатой»), `checklist[]` (`{code, done, text}`: `ProfileFilled`, `PaymentDetails`, `ProviderInfo`,
`HousePublished`, `Plan`), `plan` (`StaysPlanSummaryDto`: `planName`, `isTrial`, `paidUntilUtc`, `maxHouses`,
`housesPublished`, `warningLevel`, `text`), `awaitingPaymentCount` (null для горничной).

### §37.27.3 Настройки — `PUT …/settings`, тело `StaysSettingsDto` (полная замена)

| Поле | Проверка → 400 строкой |
|---|---|
| `checkInTime`, `checkOutTime` | «Время — с шагом 30 минут»; «Время выезда не может быть позже времени заезда» |
| `minNights` 1…30, `maxNights` 1…90 | «Минимум ночей — от 1 до 30», «Максимум ночей — от 1 до 90», «Минимум не может быть больше максимума» |
| `horizonDays` 30…730 | «Горизонт бронирования — от 30 до 730 дней» |
| `allowGapFill`, `allowSameDayCheckIn` | — |
| `holdMinutes` 10…180 | «Время на оплату — от 10 до 180 минут» |
| `prepayPercent` 0…100 | «Предоплата — от 0 до 100 %» |
| `cancellationPolicy` | `Standard` / `Flexible` / `NoDeductions` |
| `dogFeeRub`, `cotFeeRub` 0…100 000 | «Сумма — от 0 до 100 000 ₽» |
| `checkInInfoSendTime` | шаг 30 мин |
| `checkInInfoText` ≤ 2000 | «Текст к заселению — не длиннее 2000 символов» |
| `checkInInfoSendFullText`, `arrivalReminderEnabled`, `housekeeperSeesGuestComment`, `showInCatalog` | — |

Изменения действуют на **новые** брони. Предупреждение «при 0 % гости бронируют без оплаты» — фронт (Q3).

### §37.27.4 Реквизиты — `PUT …/payment-details` `{paymentDetails, paymentPurpose}`

`paymentDetails` ≤ 1000 (пусто допустимо — тогда при предоплате > 0 гейт закрыт), `paymentPurpose` ≤ 200. 400: «Реквизиты —
не длиннее 1000 символов», «Назначение платежа — не длиннее 200 символов».

### §37.27.5 Исполнитель — `PUT …/provider` `{status, name, inn, ogrn, claimsAddress}`

`status` обязателен; `name` 1–300; `inn` — `InnValidator` (10 цифр у `Organization`, 12 — у остальных): 400 «Неверный
ИНН»; `ogrn` — 13 цифр у `Organization`, 15 у `IndividualEntrepreneur` (обязателен), у остальных — null: 400 «Укажите ОГРН»
/ «Укажите ОГРНИП» / «Неверный ОГРН» / «ОГРН указывается только для организации и ИП»; `claimsAddress` 1–500 («Укажите
адрес для претензий»). Полная замена.

### §37.27.6 Уведомления — `StaysNotificationSettingsDto` / `StaysNotificationSettingsInput`

`staffPushEnabled` (→ `CompanyNotificationSettings.StaffPushEnabled`), `staffMaxEnabled`, `guestWebPushEnabled`,
`guestMessengerEnabled`, `deliveryMode`, `priorityTransport` (как у магазина); только чтение: `messengerAvailable` (есть
назначенный оплаченный канал). `guestMessengerEnabled: true` без канала → 409 `StaysConflictDto` `MessengerUnavailable`
«Подключите канал WhatsApp или MAX, чтобы отправлять сообщения гостям».

---

## §37.28. Кабинет: дома

Префикс `/api/stays/companies/{companyId}/houses`. Фото — политики `company-photos` (загрузка) и `company-photos-edit`.

| Маршрут | Право | Тело → ответ |
|---|---|---|
| `GET …/houses` | `ViewCabinet` или `EditHouseContent` | `HouseListItemDto[]` (включая архивные с `isArchived`) |
| `POST …/houses` | `ManageHouses` | `HouseCreateInput` `{name, capacity}` → 201 `HouseManageDto` (неопубликован, `Constant` без цены, slug из названия) |
| `PUT …/houses/order` | `ManageHouses` | `{ids[]}` (полный список неархивных) → `HouseListItemDto[]` |
| `GET …/houses/{houseId}` | `EditHouseContent` | `HouseManageDto` |
| `DELETE …/houses/{houseId}` | `ManageHouses` | 204; есть хоть одна бронь → 409 `HouseHasBookings` «У дома есть брони — его можно только архивировать» |
| `PUT …/houses/{houseId}/setup` | `ManageHouses` | `HouseSetupInput` `{name, slug, capacity, extraBedsEnabled, extraBedsMax, extraBedPriceRub, dogsForbidden, hasCot}` → `HouseManageDto` |
| `PUT …/houses/{houseId}/content` | `EditHouseContent` | `HouseContentInput` `{description, amenities[], address, yandexMapsUrl, twoGisUrl, checkInInfoText}` → `HouseManageDto` |
| `PUT …/houses/{houseId}/pricing` | `ManageHouses` | `HousePricingInput` `{mode, constantPriceRub}` → `HouseManageDto` |
| `GET …/houses/{houseId}/price-periods` | `ManageHouses` | `?includePast=false` → `PricePeriodDto[]` |
| `POST …/houses/{houseId}/price-periods` | `ManageHouses` | `PricePeriodInput` `{startDate, endDate, priceRub}` → 201 `PricePeriodDto` |
| `PUT|DELETE …/houses/{houseId}/price-periods/{periodId}` | `ManageHouses` | → `PricePeriodDto` / 204 |
| `PUT …/houses/{houseId}/registry` | `ManageHouses` | `HouseRegistryInput` `{objectKind, registryNumber, registryUrl, attestation?}` → `HouseManageDto` |
| `POST …/houses/{houseId}/publish` | `ManageHouses` | `HousePublishInput` `{attestation: {accepted: true, noticeVersion}}` → `HouseManageDto` |
| `POST …/houses/{houseId}/unpublish` | `ManageHouses` | `{}` → `HouseManageDto` |
| `POST …/houses/{houseId}/archive` | `ManageHouses` | `{}` → `HouseManageDto` (снимается с публикации; брони живут) |
| `POST …/houses/{houseId}/photos` | `EditHouseContent` | multipart `file` → 201 `HousePhotoDto`; 16-е фото → 409 `PhotoLimitReached` «У дома может быть не больше 15 фото» |
| `PUT …/houses/{houseId}/photos/order` | `EditHouseContent` | `{ids[]}` → `HousePhotoDto[]` (первое — обложка) |
| `DELETE …/houses/{houseId}/photos/{photoId}` | `EditHouseContent` | 204 |
| `GET …/houses/{houseId}/qr` | `ViewCabinet` | `image/png` (ссылка на страницу дома) |

**Проверки (400 строкой):** название 1–100 («Укажите название дома»); вместимость 1…50 («Вместимость — от 1 до 50»); доп.
места 1…10, цена 0…100 000; slug дома — `houseSlugPattern` («Адрес дома — латиница, цифры и дефис, 2–50 символов»), занят в
компании → 409 `SlugTaken`; описание ≤ 4000; адрес ≤ 500; ссылки карт — тексты `MapLinkValidation`; удобства — только коды
справочника («Неизвестное удобство»); текст к заселению ≤ 2000; цена 1…1 000 000 («Цена — от 1 до 1 000 000 ₽»);
`Constant` без `constantPriceRub` у опубликованного дома → 409 `NoPrice`; период: `startDate ≤ endDate` («Дата окончания не
может быть раньше начала»), длина ≤ 731 день («Период — не длиннее двух лет»); номер реестра — 5–32 символа (буквы, цифры,
дефис), ссылка — `https://`, ≤ 500 («Ссылка на запись в реестре должна начинаться с https://»).

**Пересечение периодов** → 409 `PricePeriodOverlap`: «Период пересекается с {дд.мм.гггг}–{дд.мм.гггг} ({цена} ₽)»,
`conflictingPeriod` (`PricePeriodDto`). Однодневный период внутри длинного — допустим; два однодневных на одну дату — 409
`PricePeriodOverlap`.

**Публикация** — `StaysConflictDto` в порядке: `HouseArchived` «Дом в архиве», `NoPrice` «Задайте цену: постоянную или хотя
бы один период на будущие даты», `ObjectKindRequired` «Укажите вид объекта», `RegistryNumberRequired` «Для гостевого дома и
средства размещения укажите номер в реестре», `AttestationRequired` «Подтвердите сведения о доме» (нет `attestation` или
`accepted ≠ true`); лимит тарифа → **402** строкой «Тариф «{план}» позволяет опубликовать {N} {дом/дома/домов}. Снимите дом с
публикации или смените тариф.» Нет действующего тарифа → 402 «Выберите тариф, чтобы публиковать дома». Успех пишет
`HouseRegistryAttestation` (вид, номер, ссылка, `noticeVersion`, время, автор, IP). **Изменение реестра опубликованного
дома** без `attestation` → 409 `AttestationRequired`.

`HouseManageDto`: `id`, `slug`, `name`, `description`, `capacity`, `extraBeds` (`enabled`, `max`, `priceRub`),
`dogsForbidden`, `hasCot`, `amenities[]` (коды), `address`, `yandexMapsUrl`, `twoGisUrl`, `checkInInfoText`, `priceMode`,
`constantPriceRub`, `registry` (`objectKind`, `registryNumber`, `registryUrl`, `lastAttestation` — `{attestedAtUtc,
attestedByName, objectKind, registryNumber}` или null), `registryNotice` (`{version, text}` — текст для диалога заверения;
`text` — **простой текст, не HTML** (абзацы — `\n`; фронт выводит его как текст, не через `innerHTML`), заверяется ровно он;
`version` = `fallback:<sha256 hex от text>` — в цикле 37 всегда так (текст `StayRegistryOwnerNotice` юриста показывается отдельно
блоком `StayNotice` рядом с полями и в версию заверения не входит; если он появится в манифесте — `text` останется plain-text,
`version` станет версией манифеста, формат поля не меняется), `isPublished`, `isArchived`, `position`, `photos[]` (`HousePhotoDto`:
`id`, `url`, `thumbnailUrl`, `position`), `publicUrl`, `uncoveredDates[]` (режим `ByDates`: диапазоны будущих дат без цены
на горизонте, `{startDate, endDate}`), `publishProblems[]` (коды публикации, которые сейчас не выполнены), `hasBookings`.

---

## §37.29. Кабинет: шахматка и блокировки

Политика `stays-board` — 120/мин на пользователя.

### §37.29.1 `GET /api/stays/companies/{companyId}/board?from=&days=&sinceRevision=` (`ViewBookings`)

`from` — дата (по умолчанию сегодня), `days` 7…62 (по умолчанию 30). Совпала ревизия и тот же `today` → `{changed: false,
revision, serverTimeUtc, today}` без массивов. Иначе `StaysBoardDto`: `changed: true`, `revision`, `serverTimeUtc`, `today`,
`from`, `days`, `houses[]` (`{id, name, isPublished, isArchived}` — неархивные + архивные с бронями в окне), `items[]`
(`BoardItemDto`: `kind` — `Booking`/`Block`/`External`, `id`, `houseId`, `startDate`, `endDate` (дата выезда), `state` —
`Held`/`AwaitingPaymentCheck`/`Confirmed`/`Block`/`External`, `stateText`, `label` (имя гостя / тип блокировки),
`holdExpiresAtUtc`, `blockKind`, `needsAction` (true у `AwaitingPaymentCheck`), `comment` (комментарий блокировки — только у
`Block`, чтобы диалог правки не терял его; у броней и внешних периодов null)), `awaitingPaymentCount`. Истёкшие, но не
снятые удержания и конечные брони в `items` не попадают.

### §37.29.2 Блокировки (`ManageBlocks`)

- `POST /api/stays/companies/{companyId}/blocks` `HouseBlockInput` `{houseId, startDate, endDate, kind, comment}` →
  201 `HouseBlockDto`. `endDate` — **дата «по» как дата выезда** (первая свободная дата); фронт показывает «ночи с … по …».
- `PUT …/blocks/{blockId}` — то же тело → `HouseBlockDto`; `DELETE …/blocks/{blockId}` → 204.
- 400 строкой: «Дата окончания должна быть позже начала», «Комментарий — не длиннее 300 символов», «Блокировка — не длиннее
  366 ночей», «Нельзя блокировать прошедшие даты» (начало < сегодня; при правке — только для новых ночей).
- 409 `StaysConflictDto`: `BlockConflictsWithBooking` «Даты заняты бронью {имя гостя}, {дд.мм}–{дд.мм}» + `conflicts[]`
  (`{bookingId, checkInDate, checkOutDate, guestName, status}`); `BlockOverlapsBlock` «Даты пересекаются с другой
  блокировкой» (соприкосновение допустимо); `HouseArchived` «Дом в архиве — блокировать даты нельзя» (только POST).
- `HouseBlockDto`: `id`, `houseId`, `startDate`, `endDate`, `kind`, `kindText`, `comment`, `createdByName`, `createdAtUtc`,
  `updatedAtUtc`.

Массовая блокировка нескольких домов — P1, отдельного маршрута нет (фронт шлёт по одному запросу).

---

## §37.30. Кабинет: брони и проверка оплаты

### §37.30.1 Список — `GET /api/stays/companies/{companyId}/bookings` (`ViewBookings`, `stays-board`)

Query: `status` (повторяемый; по умолчанию `AwaitingPaymentCheck`), `houseId`, `from`, `to` (по дате заезда), `page`,
`pageSize` (≤ 50). Порядок: для `AwaitingPaymentCheck` — по времени первого файла **от старых к новым**; иначе — по дате
заезда ↓. `PagedResult<StaffStayBookingListItemDto>`: `id`, `houseId`, `houseName`, `checkInDate`, `checkOutDate`, `nights`,
`guestName`, `guestPhone`, `status`, `displayStatus`, `statusText`, `totalRub`, `prepayRub`, `holdExpiresAtUtc`,
`firstProofUploadedAtUtc`, `isManual`, `createdAtUtc`.

### §37.30.2 Карточка — `GET …/bookings/{bookingId}` (`ViewBookings`) → `StaffStayBookingCardDto`

`id`, `version`, `status`, `displayStatus`, `statusText`, `holdExpiresAtUtc`, `house` (`id`, `name`), даты и время, гости и
опции, `guestName`, `guestPhone` (полный), `guestKind`, `comment`, строки и суммы, `cancellationPolicy`,
`ownerCancelRefundText` («Гостю нужно вернуть предоплату полностью: X ₽»), `paymentProofs[]` (метаданные),
`paymentConfirmed` (`{atUtc, byName}`), `paymentProofsPurgedAtUtc`, `statusReason`, `isManual`, `availableActions[]`
(`ConfirmPayment`, `RejectPayment`, `Cancel`), `events[]` (журнал: `occurredAtUtc`, `kind`, `text`, `actorText`, `reason` —
P1 показа), `messages[]` (P1: отправленные гостю уведомления — `type`, `channel`, `status`, `createdAtUtc`).

### §37.30.3 Действия (`ManageBookings`)

| Маршрут | Тело | Из статуса | Успех |
|---|---|---|---|
| `POST …/bookings/{bookingId}/confirm-payment` | `{expectedVersion}` | `AwaitingPaymentCheck` | `Confirmed`; выпуск информации к заселению, если срок наступил |
| `POST …/bookings/{bookingId}/reject-payment` | `{expectedVersion, reason}` | `AwaitingPaymentCheck` | `PaymentRejected`, даты свободны |
| `POST …/bookings/{bookingId}/cancel` | `{expectedVersion, reason}` | `Held`, `AwaitingPaymentCheck`, `Confirmed` | `CancelledByOwner`, даты свободны |

- `reason` 1–300 после trim → иначе 400 «Укажите причину — гость её увидит».
- Версия не совпала → 409 `StayStaffConflictDto` `VersionMismatch` «Бронь уже изменена — проверьте актуальное состояние»;
  недопустимый переход → 409 `InvalidTransition` «Действие недоступно в статусе «{статус}»». В теле — актуальная
  `StaffStayBookingCardDto`; действие **не применено**.
- Успех → 200 `StaffStayBookingCardDto`.

### §37.30.4 Файл подтверждения — `GET …/bookings/{bookingId}/payment-proofs/{proofId}` (`ViewBookings`)

Как §37.26.3, плюс событие `PaymentProofViewed` (не чаще раза в 10 мин на пару сотрудник × файл). Удалённый по сроку → 404.

### §37.30.5 Ручная бронь (P1) — `ManageBookings`

- `POST …/bookings/quote` `StaffStayQuoteInput` `{houseId, checkIn, checkOut, adults, children, dogs, needCot}` → `StayQuoteDto`
  (минимум, максимум и горизонт не действуют; гейт тарифа не проверяется).
- `POST …/bookings` `ManualStayBookingInput` `{houseId, checkIn, checkOut, adults, children, dogs, needCot, guestName,
  guestPhone?, notifyGuest, totalOverrideRub?, comment?}` → 201 `StaffStayBookingCardDto` (`Confirmed`, `isManual`,
  `prepayRub` = 0). Занятость — те же проверки и ограничение БД (409 `StayRefusalDto`). `notifyGuest` без телефона → 400
  «Чтобы уведомить гостя, укажите телефон». Мессенджер-уведомления гостю ручной брони не отправляются никогда (нет снимка согласия гостя, Т37-12), `notifyByMessenger` = false; флаг `notifyGuest` принимается ради совместимости и на отправку не влияет. `totalOverrideRub` 0…10 000 000 — заменяет расчётные строки одной строкой
  `ManualTotal` «Итог изменён вручную».

---

## §37.31. График уборок и заездов — `GET /api/stays/companies/{companyId}/schedule?from=&days=` (`ViewSchedule`)

`from` — по умолчанию сегодня, `days` 1…15 (по умолчанию 15: сегодня, завтра и ещё 13). В график попадают брони
`Confirmed` и `AwaitingPaymentCheck`.

`StaysScheduleDto`: `today`, `days[]` (`{date, label («Сегодня», «Завтра», «пт 2 янв»), departures[], arrivals[]}`):
- `departures[]`: `bookingId`, `houseId`, `houseName`, `checkOutTime`, `sameDayTurnover` (bool);
- `arrivals[]`: `bookingId`, `houseId`, `houseName`, `checkInTime`, `arrivalTime`, `guestName`, `adults`, `children`,
  `extraBeds`, `dogs`, `needCot`, `comment` (**null**, если пользователь — горничная и у компании выключен
  `housekeeperSeesGuestComment`), `paymentUnconfirmed` (true у `AwaitingPaymentCheck`), `sameDayTurnover`,
  `turnoverText` («Выезд и заезд в один день — уборка 12:00–14:00»).

Форма одна для всех ролей и **не содержит** телефонов, сумм, файлов, реквизитов, статусов кроме `paymentUnconfirmed`.

---

## §37.32. Тарифы и триал «Домов» — тексты

- 402 при публикации — §37.28. 402 больше нигде в вертикали не используется.
- `StaysTrialOutcomeDto`: `granted`, `refusalCode` (`TrialNotOffered`, `TrialAlreadyActive`, `AlreadyOnPaidPlan`,
  `TrialAlreadyUsed`, `PhoneNotVerified`, `PhoneVerificationUnavailable`, `TrialPhoneAlreadyUsed`,
  `TrialUniquenessCheckUnavailable`, `TrialTermsVersionMismatch`), `message` (тексты `TrialLegalNotices` — существующие),
  `endsAtUtc`.
- Плашки триала: `warningLevel` + `text` в `StaysPlanSummaryDto` и блоке `stays` подписки: «Пробный период закончится через 3
  дня — {дата}», «… завтра», «Пробный период закончился. Гости не могут бронировать, пока вы не выберете тариф», «Тариф не
  выбран…», «Опубликовано {N} домов при лимите {M}: гости не могут бронировать. Снимите лишние дома с публикации или смените
  тариф».

---

## §37.33. Уведомления — тела и тексты (форма; дословные тексты — `StaysTexts`/`StayNotificationTexts`)

- **Push сотруднику** (`StaffPushPayload`, как цикл 33): `title` — название компании; `body` — «Новая бронь · «{дом}» ·
  {дд мес} – {дд мес} · {N} ночей» / «Приложено подтверждение оплаты · «{дом}» · {даты}» / «Гость отменил бронь · «{дом}» ·
  {даты}»; `tag` `s-<bookingId>`; `url` абсолютный `https://dom.ezbook.ru/cabinet/<companyId>/bookings/<bookingId>`. Без
  имени и телефона гостя.
- **Push гостю**: `title` «ezbook · Дома»; `body` — «Статус вашей брони изменился» (по умолчанию), «Осталось 10 минут, чтобы
  приложить подтверждение оплаты», «Информация к заселению готова», «Завтра заезд — откройте бронь»; `tag` `sg-<bookingId>`;
  `url` `/b/<token>`. Никаких имён, телефонов, адресов, кодов (Т37-08).
- **Мессенджер гостю** — с названием компании и ссылкой на бронь; «бронь создана» — дополнительно сумма предоплаты, срок и
  реквизиты (только этой брони); «оплата подтверждена» — адрес и даты; «отклонена/отменена» — причина и телефон компании;
  «напоминание» — дата и время заезда, адрес, остаток к оплате; «информация к заселению» — по умолчанию только ссылка
  (ЮР-4), при `checkInInfoSendFullText` — тексты целиком. В конце — строка отписки (`PublicSiteLinks.UnsubscribeUrl`).
- Ссылки — только `PublicSiteLinks`.

---

## §37.34. Персональные данные — изменения существующих маршрутов

- `GET /api/profile/export` — в конец JSON секция `stayBookings[]` (`ARCHITECTURE_CYCLE37.md` §37.13.1): `companyName`,
  `houseName`, `checkInDate`, `checkOutDate`, `status`, `adults`, `children`, `dogs`, `needCot`, `arrivalTime`,
  `guestName`, `guestPhone`, `comment`, `totalRub`, `prepayRub`, `statusReason`, `bookingUrl`, `paymentProofs[]`
  (`uploadedAtUtc`, `contentType`, `sizeBytes`, `purged`), `events[]` (видимые гостю). Гостевые брони — только при
  подтверждённом номере.
- `GET /api/profile/delete-account/preview` — в конец счётчик `stayBookings`; `POST /api/profile/delete-account` —
  обезличивание броней домов (§37.13.1).
- `GET /api/profile/consents/revoke-preview`, `POST …/revoke` (`ProviderDelivery`) — охватывают брони домов (число активных
  броней с мессенджером — в конец превью).
- `GET /api/admin/retention/policy` (SuperAdmin) — в конец `RetentionPolicyDto` шесть сроков «Домов» из той же конфигурации
  `Retention:*`, что читают правила (`ARCHITECTURE_CYCLE37.md` §37.13.3): `stayPaymentProofDays`, `stayUnpaidBookingDays`,
  `stayBookingPersonalDataDays`, `stayBookingEventDays`, `stayGuestPushSubscriptionDays`, `stayGuestPushNotificationDays`
  (целые дни; схема цикла 20 — `additionalProperties: true`).

---

## §37.35. Частота запросов — новые политики (`RateLimits`, `appsettings.Testing.json` поднимает все до 10000)

| Политика | Где | Лимит | Текст 429 |
|---|---|---|---|
| `stays-public` | amenities, catalog, companies/{slug}, houses/{slug}, calendar, quote | 120/мин на IP | «Слишком много запросов. Попробуйте через минуту» |
| `stay-create` | создание брони гостем | 5/ч на IP (аноним), 20/ч на пользователя | «Слишком много попыток. Попробуйте позже» |
| `stay-public` | GET брони по токену, файл по токену, cancel | 120/мин на IP | как `stays-public` |
| `stay-proof` | загрузка подтверждения | 6 за 10 мин на токен **и** 20/ч на IP (цепочка) | «Слишком много загрузок. Попробуйте позже» |
| `stay-push` | push-подписка гостя (обе операции) | 20/ч на IP | «Слишком много попыток. Попробуйте позже» |
| `stays-board` | board, bookings list | 120/мин на пользователя | «Слишком много запросов. Попробуйте через минуту» |

Лимиты по номеру — в коде (§37.24 п. 6), значения `Stays:PhoneLimits` (`MaxHeldPerPhone` 2, `MaxHeldPerPhonePerCompany` 1,
`MaxCreatedPerPhonePerDay` 10).

---

## §37.36. Тексты 400 формы брони и расчёта (фронт показывает дословно у поля)

| Текст | Поле |
|---|---|
| «Укажите даты заезда и выезда» | даты |
| «Неверный формат даты» | даты |
| «Взрослых — от 1 до 30» | взрослые |
| «Детей — от 0 до 30» | дети |
| «Собак — от 0 до 20» | собаки |
| «Укажите имя» / «Имя — не длиннее 100 символов» | имя |
| «Введите номер телефона в формате +7 (900) 000-00-00» | телефон |
| «Время прибытия — от времени заезда до 23:30 с шагом 30 минут» | время прибытия |
| «Комментарий — не длиннее 500 символов» | комментарий |
| «Подтвердите, что вы не робот» | капча |
| «Нужен ключ запроса — обновите страницу» | (нет `idempotencyKey`) |

---

## §37.37. Маршруты dom и адреса — `contracts/cycle37/dom-routes.json`

`slugPattern`, `slugMinLength`/`MaxLength` (как у goods), `houseSlugPattern` (`^[a-z0-9]+(?:-[a-z0-9]+)*$`, 2–50),
`companyPagePattern` `/:slug`, `housePagePattern` `/:slug/:houseSlug`, `bookingPagePattern` `/b/:token`,
`catalogQueryParams`, `houseQueryParams`, `spaRoutes`, `reservedSlugs`. Добавить маршрут dom можно **только** вместе со
словом в `reservedSlugs`.

---

## §37.38. Эталон расчётов — `contracts/cycle37/stay-vectors.json`

Разделы `money` (StayMoney.Quote), `nightPrice` (HousePricing.PriceFor), `refund` (StayRefund.Compute), `stay`
(StayRules.CheckStay; в кейсе `settings` дополняет базовые настройки, `occupancies` заменяет базовые периоды). Юнит-тесты C#
(`ServiceBooking.UnitTests`) и vitest (`dom/src/utils/*.test.ts`) читают **один** файл. Правка правила = правка векторов в
том же коммите.

---

## §37.39. Сводка новых маршрутов (для `Cycle22RouteTable.golden.txt`)

**61 новый маршрут**: 60 под `/api/stays/*` и один общий `PUT /api/Companies/{id}/members/{memberId}/position`.

| Группа | Маршрутов | Авторизация |
|---|---|---|
| `/api/stays/public/…` (amenities, catalog, companies/{slug}, …/houses/{houseSlug}, houses/{houseId}/calendar, quote, bookings) | 7 | anon (`bookings` — anon или auth) |
| `/api/stays/bookings/public/{token}…` (get, payment-proofs POST/GET, cancel, push-subscription, push-subscription/remove) | 6 | anon |
| `/api/stays/bookings/my` | 1 | auth |
| `/api/stays/companies` (POST), `/companies/my`, `/slug-check`, `/trial` (GET, POST) | 5 | auth |
| `/api/stays/companies/{companyId}` (GET), `settings`, `payment-details`, `provider`, `slug`, `qr`, `notification-settings` (GET, PUT) | 8 | auth + `StaysPermission` |
| `…/houses…` (список, создание, порядок, карточка, удаление, setup, content, pricing, периоды ×4, registry, publish, unpublish, archive, фото ×3, qr) | 20 | auth + `StaysPermission` |
| `…/board`, `…/blocks` (POST, PUT, DELETE) | 4 | auth + `StaysPermission` |
| `…/bookings…` (список, ручная, quote, карточка, confirm, reject, cancel, файл) | 8 | auth + `StaysPermission` |
| `…/schedule` | 1 | auth + `ViewSchedule` |

Методы, operationId (префикс `stays`, уникальны) и атрибуты — `openapi.yaml`.
