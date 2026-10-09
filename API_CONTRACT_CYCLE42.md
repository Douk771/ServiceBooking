# API_CONTRACT — цикл 42 ServiceBooking: «Бани» (bani.ezbook.ru)

**Разделы §42.20–§42.39.** Решения и механизмы — `ARCHITECTURE_CYCLE42.md` §42.0–§42.18. **Источник истины по форме** —
`contracts/cycle42/openapi.yaml`: при расхождении этого текста со схемой по форме права схема; по смыслу, порядку проверок и
текстам — этот документ. Рядом: `contracts/cycle42/bani-routes.json` (маршруты bani, резерв слов), `contracts/cycle42/bani-vectors.json`
(эталон новых чистых функций). Эталон слотового движка — `contracts/cycle39/service-vectors.json`, **не меняется**. Требования —
`SPEC_CYCLE42_BANI.md` (+ «Решения заказчика от 09.10.2026»), правовые — `LEGAL_REVIEW_CYCLE42.md` §12 (Т42-01…17). Базовая ревизия —
ветка `cycle/042-bani` (от `develop` после мерджа цикла 39, `5bcd015` + закрывающие документы).

Контракты циклов 37 и 39 (`API_CONTRACT_CYCLE37.md`, `API_CONTRACT_CYCLE39.md`, `contracts/cycle37/`, `contracts/cycle39/`)
действуют. Здесь — только новое (`/api/baths/*`) и изменённое.

---

## §42.20. Конвенции и исключения цикла

- Все конвенции §37.20 и §39.20 без изменений: camelCase, enum строками, `DateTime` UTC, даты `YYYY-MM-DD`, деньги — целые
  рубли; 400/402/429 — строка `text/plain`; 401/403/404 — пустое тело; все 409 `/api/baths/*` — JSON `{code, message, …}`.
  Время суток ресурса — минуты бизнес-дня (360 = 06:00, 1560 = 02:00 следующих суток).
- **Вид компании `Baths`** (`CompanyKind`, число 3). Маршруты `/api/baths/*` видят **только** компании `Baths`: салон, магазин,
  «Дома», несуществующая или чужая компания — **404 пустым телом** (без оракула). Маршруты `/api/stays/*` по-прежнему видят
  **только** `Stays`: банная компания, её ресурс и её бронь по `/api/stays/*` — 404. Токен брони бани по
  `/api/stays/service-orders/public/{token}` — 404, и наоборот.
- **Одна реализация на две вертикали.** Маршруты ресурсов, броней и «Дня услуг» под `/api/baths/*` — те же действия, что
  маршруты услуг цикла 39 под `/api/stays/*` (общий базовый контроллер, `ARCHITECTURE_CYCLE42.md` §42.4). Форма ответа —
  формы цикла 39 (+ поля цикла 42), коды и тексты — цикла 39, **кроме слов для гостя** (§42.36: «бронь» вместо «заказ»).
- **Термин для гостя — «бронь»** (Q-L42-5). В API имена прежние (`service-orders`, `orderUrl`, `order`), в текстах bani,
  которые собирает сервер, слова «заказ» нет.
- **Ресурс = `StayServices`**. В API bani путь `…/services/{serviceId}` — это ресурс (баня, сауна, чан, фурако).
- **Режима «к проживанию» у bani нет**: параметры `houseId`, `checkIn`, `checkOut` маршрутов цикла 39 у `/api/baths/*` не
  описаны и **игнорируются**, если переданы. `availableForHouseBookings` ресурса бани всегда `false`.
- **Пометка местного времени** (Т42-08): ответы о ресурсе и брони несут `cityName` и `localTimeNote` («Время местное,
  Шерегеш»). Фронт показывает её рядом с выбором времени, в форме и на странице брони; в сообщения гостю её добавляет сервер.
- Неизвестный токен, чужой `serviceId`/`sessionId`/`itemId` → 404 пустым телом на всех маршрутах.
- Сумма «к возврату не меньше 0 ₽» не приходит никогда (Т39-02 действует и для бань).
- Политики частоты — существующие (`stays-public`, `stay-service-create`, `stay-public`, `stay-proof`, `stay-push`,
  `stays-board`), новых нет; новые публичные маршруты каталога — `stays-public` (120/мин на IP).

---

## §42.21. Изменения существующих маршрутов

Поведение ezbook, goods и dom остаётся прежним, **кроме** строк, помеченных «⚠ меняет dom» — это требования юриста
(D3 раздел 7а касается и услуг «Домов») и общая реализация; см. `ARCHITECTURE_CYCLE42.md` §42.18 «Отклонения».

| Маршрут | Изменение |
|---|---|
| `GET /api/companies/{slug}` (публичная страница салона) | компания `Baths` → **404** (как «нет такой») — `CompanyKindTraits.VisibleOnSalonPublicPage = false`. Для `Stays` — как прежде |
| Салонные маршруты закрытого списка §389.2 (мастера, расписание, записи, услуги салона, `catalog-listing` и т. д.) | компания `Baths` → 409 `text/plain` «Это компания «Бани»: записи, услуги и расписание для неё недоступны.» (`CompanyKindGuard.RejectNonSalon`) |
| Маршруты заказов goods (`/api/shops/*`, `/api/orders/*`) | компания `Baths` → 404, как салон и «Дома» |
| `/api/stays/*` | компания `Baths` → 404 (как и раньше для не-«Домов»; добавлен тест матрицы) |
| `GET /api/companies/kinds-summary` | в конец: `baths: {count, siteUrl}` |
| `GET /api/push/config`, `GET /api/push/subscriptions`, `POST /api/push/subscribe` | параметр/поле `site` принимает `Baths`; `siteUrls` получает `baths` (`https://bani.ezbook.ru`); лимит устройств — на пару (пользователь × сайт), как прежде |
| `PUT /api/companies/{id}` (профиль компании) | у `Baths` город и пояс **меняются** (как у салона: город — из справочника, пояс — по городу или вручную). Запрет смены города — у `Stays`, как прежде. **У `Baths`: 409**, пока есть будущие брони (Held, AwaitingPaymentCheck, Confirmed): «Нельзя сменить город или часовой пояс, пока есть будущие брони — …» (I-2) |
| `GET|POST|PUT|DELETE /api/companies/{id}/photos*` (галерея компании) | `Baths` **разрешена** (фото комплекса, до 10, `CompanyPhotosSection`); `Stays` — как прежде отказ `GalleryRefusalText` |
| `POST /api/companies/{id}/members`, `PUT …/members/{memberId}/position` | у `Baths` — только роль `Master` с должностью `Manager` («Администратор») или `Housekeeper` («Банщик»); тексты 400: «В компанию «Бани» можно добавить только сотрудника.», «Укажите должность: администратор или банщик.»; потолок — `Stays:MaxStaffPerCompany` (30), тарифного лимита мест нет |
| `GET /api/billing/subscription?line=Baths` | новая линейка: `line = "Baths"`, блок `baths` (`resourcesPublished`, `maxResources`, `isTrial`, `trialEndsAtUtc`, `warningLevel`, `text`), `availablePlans` — активные тарифы линейки без триала, у каждого `maxResources` (у других линеек поля нет). Блок `baths` у других линеек отсутствует (`WhenWritingNull`). 404 — у пользователя нет компаний (как прежде) |
| `POST /api/billing/subscription/request` | `line: "Baths"` — заявка на тариф линейки; текст «уже есть заявка» называет линейку «Бани» |
| `GET /api/admin/plans`, `POST|PUT /api/admin/plans…` | линейка `Baths` в фильтре и при создании; поле `maxResources` (int ≥ 1 или null) — **только** для `Baths` (иначе 400 «Лимит ресурсов задаётся только тарифам линейки «Бани»»); системный триал «Бань» нельзя удалить или выключить — 409 (как у «Домов») |
| `GET /api/admin/billing-accounts/{id}`, `PUT …/subscription` | в карточке — блок `bathsSubscription` (как `staysSubscription`: тариф, оплачено до, активна, опубликовано ресурсов, лимит); назначение `line: "Baths"` пишет `BathsSubscriptions`; превышение лимита ресурсов — 409 с подтверждением `confirmLimitOverflow`, как у «Домов»; очередь заявок показывает текущий тариф линейки «Бани» |
| `GET /api/admin/companies?kind=Baths`, карточка компании | фильтр по виду `Baths`; в карточке (P1, US-42-04) — `bathsStats: {resourcesCount, ordersLast30Days}` |
| `GET /api/stays/public/companies/{slug}/services/{serviceSlug}` | в конец: `cityName`, `localTimeNote`; `capacity` — только если задана (у услуг «Домов» отсутствует) |
| `POST /api/stays/public/services/{serviceId}/orders` | вход принимает `guestsCount` (у услуг «Домов» вместимости нет — значение игнорируется) |
| `GET /api/stays/service-orders/public/{token}` | в конец: `cityName`, `localTimeNote`, `sessionReminder` (всегда null у «Домов» — напоминание выключено), `bookAgainUrl` (null у «Домов»); `guestsCount` — только если задан |
| `GET …/service-sessions/{sessionId}` (dom) | `guestsCount` — только если задан (у «Домов» поля нет) |
| `PUT /api/stays/companies/{id}/services/{serviceId}/setup` | вход принимает `capacity` (dom не шлёт; null — вместимость не используется) |
| ⚠ меняет dom: `POST|PUT /api/stays/companies/{id}/services/{serviceId}/items…` | мягкий фильтр алкоголя и табака: 409 `ItemRestrictedConfirmationRequired` → повтор с `confirmRestricted: true` (§42.30). В ответе позиции — `warnings[]` |
| ⚠ меняет dom: `PUT /api/stays/companies/{id}/services/{serviceId}/content` | 400 «Не используйте слова «задаток», «невозвратный», «депозит»» на описании; в ответе — `contentWarnings[]` (мягкие, §42.30) |
| `GET /api/profile/export` | в элементах `stayServiceOrders[]`: `guestsCount` (Т42-06), `site` («Дома»/«Бани»), `orderUrl` — на сайт вертикали (`https://bani.ezbook.ru/s/…` для бань) |
| `GET /api/legal/texts/{key}` | новые ключи вне `LegalTextKey.All` (§42.37.3) отвечают 404, пока текста нет — фронт показывает запасной |

---

## §42.22. Каталог (публично, политика `stays-public`)

### §42.22.1 `GET /api/baths/catalog?cityId=&date=&page=&pageSize=` → `BathsCatalogPageDto`

- В выдаче — ресурсы, у которых: ресурс опубликован и не в архиве; компания `Baths`, `IsActive`, `ShowInPublicListing`
  (настройка «Показывать в каталоге»); гейт компании открыт **с предоплатой этого ресурса** (§42.34.3). Порядок: город
  (по названию), компания (по названию), ресурс (по `Position`).
- `cityId` — фильтр по городу компании; несуществующий или неактивный город → 400 «Город не найден».
- `date` (P1) — оставить ресурсы, у которых в этот бизнес-день есть хотя бы один доступный старт (та же `ServiceSlotCalculator`,
  что на странице ресурса, с упреждением ресурса и занятостью на сейчас). Дата в прошлом → 400 «Эта дата уже прошла»;
  неверный формат → 400 «Неверный формат даты». Даты за горизонтом компании дают пустой результат для её ресурсов.
- `pageSize` — 1…50 (по умолчанию 12). `page` за пределами — пустая страница с верным `totalCount`.
- Пусто: `emptyText` — «В этом городе пока нет бань» (без даты) или «На эту дату свободного времени нет» (с датой).
- Карточка: название ресурса и комплекса, адрес, город, обложка (первое фото ресурса, иначе первое фото комплекса, иначе
  null), `priceFromRub` (минимальная цена правила), `minHours`, `capacity`, `url` `/<companySlug>/<resourceSlug>`. Занятых
  интервалов, имён, реквизитов в ответе нет.
- Кеш: «база» (опубликованные ресурсы + гейт компаний) — 30 с, сбрасывается при публикации, снятии, архивации, удалении
  ресурса, блокировке компании и смене «показывать в каталоге». Фильтр даты кешем не покрывается.

### §42.22.2 `GET /api/baths/catalog/cities` → `{items: [{id, name, region, resourcesCount}]}`

Города, где сейчас в каталоге есть хотя бы один ресурс (по той же «базе»), по названию. Пусто — `items: []`.

---

## §42.23. Страница комплекса — `GET /api/baths/public/companies/{slug}` → `BathsPublicCompanyDto`

- Нет компании с таким адресом / не `Baths` → 404. Заблокирована (`IsActive = false`) → 404 (как у «Домов»).
- Содержит: название, описание, адрес, город и пояс, телефон, логотип, ссылки на карты, **фото комплекса** (галерея компании),
  `provider` — публичная часть сведений об исполнителе (статус и ИНН; ФИО физлица не показывается, ЮР-3), `acceptingBookings`
  и `notAcceptingText` «Бронирование временно недоступно», `resources[]` — опубликованные неархивные ресурсы карточками
  (`BathResourceCardDto`) в порядке владельца.
- Реквизитов, тарифа, причин закрытого гейта в ответе нет.

---

## §42.24. Ресурс анонимно (база — §39.22; политика `stays-public`, создание — `stay-service-create`)

### §42.24.1 `GET /api/baths/public/companies/{slug}/services/{serviceSlug}` → `PublicServiceDto` (+ поля цикла 42)

Как §39.22.1, с отличиями:
- 404: нет компании / не `Baths` / нет ресурса / не опубликован. Архив или блокировка компании → 200 с `available: false`.
- `standalone.ordering` у бани всегда `true` (`notOrderingText` — null).
- `capacity` — «до N человек»; `cityName`, `localTimeNote` — «Время местное, {город}».
- Нет туристического налога (Т39-15), реквизитов, занятых интервалов, имён.

### §42.24.2 `GET …/services/{serviceId}/availability?from=&days=`, `GET …/services/{serviceId}/starts?date=`

Как §39.22.2 и §39.22.3 (без режима «к проживанию»). 404 — ресурс не бани или не опубликован.

### §42.24.3 `POST …/services/{serviceId}/quote` — `BathServiceQuoteInput` → `ServiceQuoteDto` (всегда 200)

Как §39.22.4. Число гостей в расчёт не входит (цена от него не зависит, Q42-5 (б)).

### §42.24.4 `POST /api/baths/public/services/{serviceId}/orders` — `CreateBathOrderInput`

**Порядок проверок** (первый отказ) — §39.22.5 с изменениями:
1. Форма → 400 строкой (§39.31) **плюс число гостей** (`bani-vectors.json` `guests`): ресурс с вместимостью и `guestsCount`
   нет → 400 «Укажите, сколько человек придёт»; вне 1…capacity → 400 «Число гостей — от 1 до {capacity}».
2. Ресурс: нет / не опубликован / архив / компания не `Baths` → 404. Компания заблокирована → 409 `NotAcceptingBookings`.
3. **Проверки `ServiceOrdersDisabled` у бани нет** — заказ без проживания включён всегда (у компании `Baths`
   `StaysSettings.AcceptServiceOrdersWithoutStay = true` с создания и не меняется).
4. Идемпотентность → 200 с существующей бронью. 5. Аноним: капча, телефон. 6. Гейт → 409 `NotAcceptingBookings`
   (`reasonCode`: `CompanyBlocked`, `NoPlan`, `OverResourceLimit`, `NoPaymentDetails` — только при предоплате ресурса,
   `NoProviderInfo` — всегда). 7. Лимиты номера (общие с «Домами»: 2 удержанных на платформе, 1 в компании, 10 в сутки)
   → 429 — тексты §42.36.2. 8. Под замком ресурса — выбор, `PriceChanged`. 9. Успех → **201** `CreateBathOrderResponse`
   (`orderUrl` — `https://bani.ezbook.ru/s/<token>`). 10. Гонка БД → 409 `SlotTaken`.
- В брони сохраняются снимки версий текстов **бани**: `BookingNoticeVersion` ← `BathBookingNotice`, `BookingTermsVersion` ←
  `BathBookingTerms`, `CancellationTermsVersion` ← `StayServiceCancellationTerms` (null, пока ключа нет в манифесте —
  как у «Домов»).
- Галочка мессенджера — по умолчанию `false`; позиции по умолчанию 0 (69-ФЗ).

---

## §42.25. Бронь по ссылке `/s/<token>` — `/api/baths/service-orders/public/{token}…` (база — §39.23)

Маршруты, тела, коды — как §39.23.1…§39.23.5; токен брони «Домов» → 404. Отличия ответа `PublicServiceOrderDto`:
- `guestsCount`, `cityName`, `localTimeNote`;
- `sessionReminder` — `{sentAtUtc, text}` после постановки напоминания (§42.36.4), иначе null;
- `bookAgainUrl` — `/<companySlug>` (кнопка «Забронировать ещё в этом комплексе»). **Имя и телефон в адрес не попадают**
  (Т42-09): вошедшему их подставляет аккаунт, анониму — фронт из `sessionStorage` этой вкладки;
- тексты статусов, возврата, отказов — словом «бронь» (§42.36.1);
- push-подписка: заголовок push — «EZBOOK Бани», `url` `/s/<token>` — только в зашифрованной нагрузке.

---

## §42.26. «Мои брони» (P1) — `GET /api/baths/service-orders/my` (`[Authorize]`) → `MyBathOrdersDto`

- Брони компаний `Baths`, у которых `GuestUserId` = пользователь, **или** (если у пользователя есть подтверждённый номер)
  `GuestPhone` = этот номер — тот же гейт, что у выгрузки (`SUBJECT-PHONE-GATE`, маркер в коде обязателен,
  `SubjectPhoneGateInvariantTests`). Обезличенные брони не попадают.
- Окно — 12 месяцев по началу сеанса. Сначала активные (`Held`, `AwaitingPaymentCheck`, `Confirmed` до конца сеанса) по
  началу ↑, затем прочие по началу ↓. `orderUrl` — относительный `/s/<token>`.
- Брони «Домов» здесь не показываются (у dom свой `GET /api/stays/bookings/my`).

---

## §42.27. Компания: создание, список, адрес, пробный период (`[Authorize]`)

### §42.27.1 `POST /api/baths/companies` (`[RequiresOwnerTerms]`) — `BathsCompanyCreateInput` → 201 `BathsCompanyCreatedDto`

Порядок проверок (первый отказ):
1. Соглашение владельца: нет версии → 400 «Для создания компании нужно принять соглашение с владельцем.»; нет документа →
   503; версия устарела → 409 строкой (как салон и «Дома»).
2. Название 1…200 → 400 «Укажите название». Описание ≤ 2000 → 400 «Описание — не длиннее 2000 символов». Адрес ≤ 300 →
   400 «Адрес — не длиннее 300 символов». Телефон → 400 «Введите номер телефона в формате +7 (900) 000-00-00».
3. Адрес компании: не задан — предлагается из названия (`SlugTransliterator`, резерв `bani-routes.json`); задан —
   `BathsSlugPolicy` → 409 `BathsConflictDto` `SlugInvalid` / `SlugReserved` / `SlugTaken` (тексты `ShopTexts`).
4. Город: нет `cityId` → 400 «Укажите город комплекса»; нет или неактивен → 400 «Город не найден». Пояс — по городу
   (`CompanyTimeZoneResolver`), меняется потом через `PUT /api/companies/{id}`.
5. Транзакция под `billing-account:{id}` → `company-slug` (повтор проверки адреса). Лимита числа компаний у линейки нет.
6. Создаются `Company { Kind = Baths, ShowInPublicListing = true }`, членство владельца, строка `StaysSettings`
   (`AcceptServiceOrdersWithoutStay = true`, `ServiceReminderHours = 3`, `ArrivalReminderEnabled = false`, остальное — по
   умолчанию). Ответ — со свежим токеном.
7. После коммита, если передан `trialTermsVersion`, — попытка триала линейки «Бани» (§42.27.4); отказ триала создание не
   отменяет (`trial.granted = false` с причиной).

### §42.27.2 `GET /api/baths/companies/my` → `BathsCompanyListItemDto[]`

Компании `Baths`, где пользователь — владелец или сотрудник (не клиент), по названию. `awaitingPaymentCount` — брони в
`AwaitingPaymentCheck`; у банщика — null.

### §42.27.3 `GET /api/baths/slug-check?name=&slug=&companyId=` → `BathsSlugCheckDto` (всегда 200)

Как `GET /api/stays/slug-check`, но по `bani-routes.json`.

### §42.27.4 `GET|POST /api/baths/trial`

Как `GET|POST /api/stays/trial` (§37.27), линейка `Baths`: тариф «Пробный период «Бани»» (фиксированный Id, §42.34.1),
`TrialGrants.Line = Baths`, `TrialPhoneRegistrations.Line = Baths`; однократно на аккаунт и на подтверждённый номер **в
линейке**; триалы «Записи», «Заказов», «Домов» на него не влияют. Условия — `BathsTrialTerms` (версия `baths-2026-10-10`,
текст — `LEGAL_REVIEW_CYCLE42.md` §11.7 + фраза о канале сообщений, §42.34.2), хеш и время показа/принятия — в `TrialGrant`.
Коды отказов и тексты — как у «Домов», с линейкой «Бани»: `TrialNotOffered`, `TrialAlreadyActive`, `AlreadyOnPaidPlan`,
`TrialAlreadyUsed`, `PhoneNotVerified`, `PhoneVerificationUnavailable`, `TrialUniquenessCheckUnavailable`,
`TrialPhoneAlreadyUsed`, `TrialTermsVersionMismatch`.

---

## §42.28. Компания: кабинет (`[Authorize]`; не участник — 404, нет права — 403)

| Маршрут | Право | Тело → ответ |
|---|---|---|
| `GET /api/baths/companies/{id}` | `ViewCabinet` или `ViewSchedule` | `BathsCompanyManageDto` (у банщика `settings`, `paymentDetails`, `provider` — null, `awaitingPaymentCount` — null) |
| `PUT …/{id}/settings` | `ManageCompany`, `[RequiresOwnerTerms]` | `BathsSettingsDto` (полная замена) → `BathsCompanyManageDto`. 400: «Горизонт бронирования — от 30 до 730 дней», «Время на оплату — от 10 до 180 минут», «Напоминание — за 1…24 часа до начала». `showInCatalog` пишет `Company.ShowInPublicListing` и сбрасывает кеш каталога |
| `PUT …/{id}/payment-details` | `ManageCompany` | как §37.27 → `BathsCompanyManageDto` |
| `PUT …/{id}/provider` | `ManageCompany` | как §37.27 (ЮР-3, `InnValidator`) → `BathsCompanyManageDto` |
| `PUT …/{id}/slug` | `ManageCompany` | `BathsSlugPolicy`, 409 `BathsConflictDto` → `BathsCompanyManageDto`; `[DemoForbidden]` |
| `GET …/{id}/qr` | `ViewCabinet` | PNG, адрес `https://bani.ezbook.ru/<slug>` |
| `GET|PUT …/{id}/notification-settings` | `ViewCabinet` / `ManageCompany` | как §37.27; включение мессенджера гостю без оплаченного транспорта аккаунта → 409 `MessengerUnavailable` «Подключите канал WhatsApp или MAX, чтобы отправлять сообщения гостям». Ответ — как у «Домов» после цикла 40: `messengerAvailable` = оплачен хотя бы один транспорт аккаунта; добавлены `messagingActive`, `deliveryChoiceVisible`, `priorityWarning` (nullable). 402 «недоступно на тарифе» и 400 на неоплаченный приоритет не бывает. Назначения компании на канал у «Бань» нет (общий `/api/notification-channels/{id}/companies` отвечает 410 для любых компаний, цикл 40) |
| `GET …/{id}/schedule?from=&days=` | `ViewSchedule` | §42.33 |
| `GET …/{id}/revision` | `ViewCabinet` или `ViewSchedule` | `{revision}` — `StaysSettings.BookingsRevision` |

**Чек-лист** «Гости не могут бронировать, потому что…» (`checklist[]`, порядок фиксирован):
`ProfileFilled` «Заполните название, адрес и телефон для гостей»; `PaymentDetails` «Заполните реквизиты для оплаты — у
ресурса «{название}» есть предоплата» (выполнен, если ни у одного опубликованного ресурса нет предоплаты или реквизиты
заполнены); `ProviderInfo` «Заполните сведения об исполнителе»; `ResourcePublished` «Опубликуйте хотя бы одну баню или
купель»; `Plan` «Выберите тариф или активируйте пробный период».

**`gate`** — `StaysBookingGate` с единицей «ресурс» и предоплатой = максимум `StandalonePrepayPercent` опубликованных
неархивных ресурсов (§42.34.3). **`plan`** — `BathsPlanSummaryDto` с `warningLevel` (`None`, `TrialEnding3d`,
`TrialEnding1d`, `Expired`, `NoPlan`, `OverLimit`) и текстом сервера:
«Пробный период закончится через 3 дня — {дата}», «Пробный период закончится завтра», «Пробный период закончился. Гости не
могут бронировать, пока вы не выберете тариф», «Тариф не выбран. Гости не могут бронировать, пока вы не выберете тариф или не
активируете пробный период», «Опубликовано {N} {ресурс/ресурса/ресурсов} при лимите {M}: гости не могут бронировать. Снимите
лишние ресурсы с публикации или смените тариф».

Профиль (название, описание, адрес, телефон, e-mail, логотип, карты, город и пояс) — общий `PUT /api/companies/{id}`; фото
комплекса — общие маршруты галереи `/api/companies/{id}/photos*` (§42.21).

---

## §42.29. Ресурсы (кабинет) — `/api/baths/companies/{companyId}/services…` (база — §39.26, §39.28)

Маршруты, права, тела, коды, тексты — как §39.26 и §39.28. Отличия для бани:

| Где | Отличие |
|---|---|
| `POST …/services` | `availableForHouseBookings = false`; адрес ресурса из названия — с резервом `reservedResourceSlugs` (`bani-routes.json`) |
| `PUT …/{serviceId}/setup` | вход `ServiceSetupInputCycle42`: `capacity` 1…30 или null (400 «Вместимость — от 1 до 30 человек»); `availableForHouseBookings` игнорируется (всегда false); адрес ресурса — `bani-routes.json` (`resourceSlugPattern`, `reservedResourceSlugs` → 409 `SlugInvalid` «Адрес ресурса — латиница, цифры и дефис, 2–50 символов; служебные слова заняты»). Ответ — `ServiceManageDto` + `capacity`, `contentWarnings`; `publicUrl` — `https://bani.ezbook.ru/<companySlug>/<resourceSlug>` |
| `POST …/{serviceId}/publish` | порядок: `ServiceArchived` → `ServiceNoPrice` → `ServiceNoWindows` → **`ServiceNoCapacity`** «Укажите вместимость — сколько человек может находиться одновременно» → **тариф**: нет действующего тарифа или триала → **402** «Выберите тариф или активируйте пробный период, чтобы опубликовать баню»; опубликованных ресурсов аккаунта (все компании `Baths`) ≥ лимита → **402** «Тариф «{план}» позволяет опубликовать {N} {ресурс/ресурса/ресурсов}». Проверка тарифа — под замком `billing-account:{accountId}` (взят **до** любых замков ресурса) |
| `POST …/unpublish`, `…/archive` | снятие и архив тариф не проверяют; снижение тарифа ничего не снимает с публикации (гейт закрывает приём) |
| `PUT …/{serviceId}/content` | описание: 400 «Не используйте слова «задаток», «невозвратный», «депозит»»; мягкие `contentWarnings[]` (§42.30.2) |
| `POST|PUT …/items` | фильтр позиций (§42.30.1) |
| Лимиты | 20 ресурсов на компанию, 10 фото, 20 позиций, 50 правил цены — конфигурация `Stays:Services:*`, общая с «Домами» |

Подсказки владельцу — фронт, запасные тексты (§42.37.3): у вместимости `BathCapacityOwnerNotice`, у позиций
`BathPositionsOwnerNotice`, у описания `StayServiceSafetyOwnerNotice` (новая редакция, Т42-07), у правила отмены
`StayServiceCancellationOwnerNotice`, у создания ресурса — «Один ресурс — одна парная или одна купель…» и «Если эта баня
уже есть у вас в „Домах“, не заводите её второй раз — сервис не увидит пересечения» (R42-1).

---

## §42.30. Позиции и тексты владельца — общая проверка для «Бань» и услуг «Домов»

### §42.30.1 Мягкий фильтр алкоголя и табака (Т42-05, Q-L42-1 (а))

`POST …/items` и `PUT …/items/{itemId}` (оба префикса — `/api/baths/…` и `/api/stays/…`):
1. Форма → 400 (§39.26). Название с «задаток/невозвратный/депозит» → 400 «Не используйте слова «задаток», «невозвратный»,
   «депозит»».
2. `RestrictedItemFilter.Match(name)` (`bani-vectors.json` `restrictedItems`) нашёл основы **и** `confirmRestricted` не `true`
   → **409** `StaysServiceConflictDto`:
   `{code: "ItemRestrictedConfirmationRequired", message: "Похоже на алкоголь или табак: {основы через запятую}. Через сервис их продавать нельзя. Если это другая позиция — подтвердите", markers: [...], noticeText: <текст BathPositionsOwnerNotice или запасной>}`.
3. Повтор с `confirmRestricted: true` → сохранение **и** строка журнала `StayServiceItemConfirmations` (кто, когда,
   название-снимок, найденные основы, версия показанного текста). Без совпадений `confirmRestricted` игнорируется.
4. При `PUT` с тем же названием, что уже подтверждено, повторное подтверждение не требуется (сравнение по нормализованному
   названию и `itemId`).
5. Ответ позиции — `ServiceItemDto` + `warnings[]` (мягкие по названию, §42.30.2).

### §42.30.2 Тексты владельца — `OwnerTextChecks` (Т42-12, `bani-vectors.json` `ownerText`)

Применяется к описанию ресурса (`content`) и названиям позиций. Отказ (400): «задаток», «невозвратн», «депозит».
Мягко (`contentWarnings[]` / `warnings[]`, сохранение не блокирует): `CancellationTermsInText` («штраф», «неустойк», «не
возвращ», «невозврат», «за неявку»), `MandatoryExtraCharge` («доплат», «за человека», «за каждого», «обязательн»),
`HealthClaim` («лечебн», «оздоров», «противопоказаний нет», «полезно при», «детокс», «иммунитет»), `Passport`, `CardNumber`
(13–19 цифр), `PassportNumber`. Тексты предупреждений — `bani-vectors.json` `ownerText.warningTexts`; фронт печатает их
дословно (дублирует их только для показа до отправки).

---

## §42.31. Расписание ресурса

Как §39.28 без отличий (недельный шаблон 1–3 окна, ручные даты, окно до 06:00 следующего дня, журнал, `outsideSessions[]`,
«брони вне нового расписания остаются в силе»). Тексты для владельца о бронях — словом «бронь».

---

## §42.32. Брони персонала, «День услуг» (база — §39.29, §39.30.2)

| Маршрут | Право | Отличия от цикла 39 |
|---|---|---|
| `GET …/service-day?date=` | `ViewBookings` | `label` полосы сеанса — «{имя гостя}, {N} чел.» или «Бронь» (обезличено / имени нет); `CarryOverBuffer` — как прежде |
| `GET …/services/{serviceId}/starts?date=`, `…/availability` | `ManageBookings` | без `bookingId` |
| `POST …/service-sessions/quote` | `ManageBookings` | вход без `bookingId` |
| `GET …/service-sessions?status=&serviceId=&from=&to=` | `ViewBookings` | только брони (`kind = Standalone`); по умолчанию `AwaitingPaymentCheck` |
| `GET …/service-sessions/{sessionId}` | `ViewBookings` | + `guestsCount`; `booking` всегда null |
| `POST …/{sessionId}/confirm-payment`, `…/reject-payment`, `…/cancel` | `ManageBookings` | как §39.29.2 |
| `GET …/{sessionId}/payment-proofs/{proofId}` | `ViewBookings` | как §39.29.2 |
| `POST …/service-sessions` (ручная бронь) | `ManageBookings` | `ManualBathOrderInput` + `guestsCount` (необязателен, 1…capacity → иначе 400 из `guests`); сразу `Confirmed`, `IsManual`, предоплата 0, основание обязательно, мессенджер гостю не отправляется |
| — | — | маршрута `POST …/bookings/{bookingId}/sessions` у бани **нет** |

**После окончания тарифа или триала** (Т42-10) все маршруты этого раздела и §42.29 (кроме публикации) работают: гейт
проверяется только при публичном создании брони и при публикации. Тест обязателен (`CY42-60…63`).

Автообновление: фронт опрашивает `GET …/revision` (≤ 30 с) и перечитывает «День услуг», список и карточку при смене.

---

## §42.33. Расписание банщика — `GET /api/baths/companies/{id}/schedule?from=&days=` → `BathScheduleDto`

- Право `ViewSchedule` (владелец, администратор, банщик). `from` — бизнес-день (по умолчанию сегодня), `days` 1…31
  (по умолчанию 7); иначе 400 «Неверный период».
- `days[]` — каждый бизнес-день периода (`label` «пт 15 янв»), `sessions[]` — активные сеансы с бронью в
  `AwaitingPaymentCheck` / `Confirmed` на бизнес-дне старта, по времени начала. **Удержанные (`Held`) не попадают.**
- `BathScheduleSessionDto` — **закрытая схема** (`additionalProperties: false`): ресурс, время календарными датами,
  `preparedUntilLabel`, имя гостя, `guestsCount`, позиции с количествами, `comment` (null, если пользователь — банщик и у
  компании `housekeeperSeesGuestComment = false`), `paymentUnconfirmed`. **Телефона, сумм, подтверждений оплаты, реквизитов,
  журнала, статуса оплаты нет вовсе** — контрактный тест формы (`CY42-70`).
- Банщику уведомления не отправляются (§42.36).

---

## §42.34. Тарифы линейки «Бани»

### §42.34.1 Тарифы (сеются миграцией, цены и лимиты админ меняет без деплоя)

| Id | Название | Цена в месяц | `MaxResources` | Публичный | Триал |
|---|---|---|---|---|---|
| `0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b01` | «Одна баня» | 200 ₽ | 1 | нет | нет |
| `0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b02` | «До 3 бань» | 500 ₽ | 3 | нет | нет |
| `0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b03` | «Без ограничения» | 1000 ₽ | null | нет | нет |
| `0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b04` | «Пробный период «Бани»» | 0 ₽ | null | нет | **да**, 14 дней (`Baths:TrialDays`) |

Цены — **начальные значения по образцу «Домов»**; окончательные называет заказчик (`ARCHITECTURE_CYCLE42.md` §42.17 п. 1).
`IsPublic = false` у всех: `/api/pricing` линейку не отдаёт (Т42-14). Опция канала `notifications.whatsapp` разрешена строками всех
четырёх планов, но триал мессенджер **не включает** (решение 09.10.2026, Q-L42-4 отменён; строка триала — мёртвые данные).

### §42.34.2 Пробный период — §42.27.4. Текст условий (`BathsTrialTerms.Text(days)`) — черновик юриста §11.7 + фраза
«Сообщения гостям в WhatsApp или MAX в пробный период не включаются: они доступны на платном тарифе.» (решение 09.10.2026). Помечен в коде
«DRAFT until a lawyer reads it».

### §42.34.3 Гейт приёма броней «Бани»

`StaysBookingGate.Evaluate(companyActive, hasActivePlan, publishedUnits, maxUnits, prepayPercent, paymentDetails, provider,
unit)` (`bani-vectors.json` `gate`): компания заблокирована → `CompanyBlocked`; нет действующего тарифа/триала →
`NoPlan`; опубликовано ресурсов аккаунта больше лимита → `OverResourceLimit` (текст — `gate`); предоплата > 0 и реквизиты
пусты → `NoPaymentDetails`; сведения об исполнителе неполны → `NoProviderInfo` (**всегда**, ЮР39-2). Единица и счётчик
— из вертикали (`ARCHITECTURE_CYCLE42.md` §42.3): для `Baths` — опубликованные неархивные ресурсы всех компаний `Baths`
аккаунта; для `Stays` — дома, как прежде (тексты «Домов» без изменений). Предоплата: у брони — `StandalonePrepayPercent`
ресурса; у компании (чек-лист, каталог «базы», страница комплекса) — максимум по опубликованным ресурсам.

---

## §42.35. Права и роли

Без изменений таблицы `StaysAccess` (§39.12): владелец — всё; `Manager` («Администратор») — `EditServiceContent`,
`ManageServiceDates`, `ViewBookings`, `ManageBookings`, `ViewSchedule`, `ViewCabinet`; `Housekeeper` («Банщик») —
`ViewSchedule`. Права «домов» у банной компании ни на что не влияют (маршрутов домов у неё нет). Владельческие маршруты —
`[RequiresOwnerTerms]`. Фронт строит меню по `myPermissions`.

---

## §42.36. Тексты и уведомления

### §42.36.1 Слова для гостя (Q-L42-5) — `ServiceWording.Baths`

Все гостевые строки, которые сервер собирает для бань (статусы, `outcomeText`, `cancellation.summary`, тексты возврата,
`cannotCancelText`, 409, 429, сообщения мессенджера и push), используют «бронь» и **не содержат** «заказ», «дом»,
«проживание», «заселение», «заезд», «бизнес-день», «задаток», «невозвратный», «депозит», «туристический налог»
(`BathsWordingGuardTests`). Строки «Домов» не меняются байт-в-байт (`ServiceWording.Stays` = прежние константы).

Примеры замен (полный список — `ServiceWording.Baths` в коде, BE-42-W): «Заказ уже отменён» → «Бронь уже отменена»;
«Заказ уже завершён — отменять нечего» → «Бронь уже завершена — отменять нечего»; «Время на оплату истекло, заказ снят…» →
«Время на оплату истекло, бронь снята…»; «Вы отменили заказ.» → «Вы отменили бронь.»; «Компания отменила заказ.» →
«Компания отменила бронь.»; «Заказ {статус} — подтверждение оплаты не нужно» → «Бронь {статус} — подтверждение оплаты не
нужно»; «Заказ завершён — уведомления не нужны» → «Бронь завершена — уведомления не нужны».

### §42.36.2 429 по номеру (бани)

«Слишком много неоплаченных броней. Оплатите или отмените текущую бронь»; «Слишком много броней с этого номера. Попробуйте
позже». Политика частоты — «Слишком много попыток. Попробуйте позже» (как прежде).

### §42.36.3 Уведомления (типы цикла 39, ссылки и слова — по вертикали компании)

- **Персоналу** (владелец, администраторы; банщику — ничего): push и MAX, без имени и телефона гостя.
  `StaffServiceOrderCreated` — «Новая бронь · {Ресурс}, {время персонала}»; `StaffServiceOrderPaymentProofUploaded` —
  «Приложено подтверждение оплаты · {Ресурс}, {время}»; `StaffServiceSessionCancelledByGuest` — «Гость отменил бронь ·
  {Ресурс}, {время}». `url` — `https://bani.ezbook.ru/cabinet/<companyId>/service-sessions/<sessionId>`, `tag` `ss-<sessionId>`.
- **Гостю** (мессенджер — по галочке брони и флагу компании; push — если подписан): типы 31…36 цикла 39 словами бани:
  создана (ссылка, итог, при предоплате — сумма, срок, реквизиты **этой** брони), «осталось 10 минут», снята по таймеру,
  оплата подтверждена, отклонена, отменена компанией (с предоплатой — «предоплата возвращается полностью; вы вправе требовать
  возмещения убытков», без — «оплата за сеанс не вносилась»). Время — календарными датами **с пометкой** «(время местное,
  {город})». Ссылка — `https://bani.ezbook.ru/s/<token>`. Push: заголовок «EZBOOK Бани», текст «Статус вашей брони
  изменился» / «Осталось 10 минут, чтобы приложить подтверждение оплаты», `tag` `so-<orderId>`, `url` `/s/<token>` — только в
  зашифрованной нагрузке. Ни ПДн, ни адреса, ни сумм, ни токена в видимом тексте push.
- Витрина и демо глушатся `ShowcaseOutboundGuard` (банных витрин нет).

### §42.36.4 Напоминание перед сеансом (P1, US-42-23) — новый `NotificationType.ServiceGuestSessionReminder = 41`

- Для броней `Confirmed` компаний, у которых `StaysSettings.ServiceReminderHours` не null (у бань с создания — 3; у «Домов» —
  null, напоминание выключено и не появляется). Момент и решение — `SessionReminderPolicy` (`bani-vectors.json`
  `sessionReminder`): за N ч до начала, тихие часы 22:00–08:00 по поясу компании, бронь, созданная позже момента, — без
  напоминания, однократно (условный `UPDATE` `SessionReminderAtUtc`).
- Каналы: мессенджер — только при галочке брони и флаге компании, текст: «{Компания}: напоминаем о брони — «{Ресурс}», {время
  гостю} (время местное, {город}). Бронь: {ссылка}» + строка отписки; push — «EZBOOK Бани» / «Скоро ваш сеанс — откройте
  бронь» (без ссылки, адреса и ПДн в видимом тексте); страница брони — `sessionReminder {sentAtUtc, text}` без ссылки:
  блок появляется с момента постановки напоминания, даже если ни один канал не доступен. Без рекламы и призывов (Т42-15).
- Событие брони `StayServiceOrderEventKind.SessionReminderSent = 10` («Отправлено напоминание»), ревизия поднимается.

---

## §42.37. ПДн, маршруты bani, правовые тексты

### §42.37.1 Права субъекта и retention

- Выгрузка (`GET /api/profile/export`): брони бань входят в `stayServiceOrders[]` (по аккаунту и, при подтверждённом номере,
  гостевые) с `guestsCount`, `site`, `orderUrl` сайта вертикали.
- Удаление аккаунта и обезличивание: имя, телефон, комментарий, аккаунт — null; файлы подтверждений — удаляются; **число
  гостей остаётся** (`LEGAL_REVIEW_CYCLE42.md` §6.2 п. 3); `sessionReminder.text` собирается на лету из снимков брони, имени
  гостя в нём нет.
- Отзыв согласия `ProviderDelivery` выключает мессенджер у активных броней бань этого номера.
- Retention — правила цикла 39 без изменений охватывают брони бань (`stay-payment-proofs`,
  `stay-service-order-unpaid-personalization`, `stay-service-order-personalization`, `stay-service-order-events`,
  `stay-service-schedule-events`, push-правила) — проверяется функциональными тестами на компании `Baths` (`CY42-80…85`).
  Журнал подтверждений позиций `StayServiceItemConfirmations` — данные компании, хранится, пока существует компания (как
  `StaysReminderTemplateChanges`).

### §42.37.2 Маршруты bani — `contracts/cycle42/bani-routes.json`

`/` (главная с каталогом; query `city`, `date`, `page`), `/:slug` (комплекс), `/:slug/:resourceSlug` (ресурс; query `date`),
`/s/:token` (бронь), `/bookings` («Мои брони»), кабинет `/cabinet/…` (список в файле), правовые страницы `/privacy`,
`/terms`, `/terms-owner`, `/pdn-consent`, `/data-request` и др. (Т42-03). Адрес компании — общее пространство платформы,
резерв `reservedSlugs`; адрес ресурса — `resourceSlugPattern` + `reservedResourceSlugs`. Встраивается в API
(`BathsSlugPolicy`) и проверяется тестом фронта `bani/src/baniRoutes.test.ts`.

### §42.37.3 Ключи правовых текстов (вне `LegalTextKey.All`, запасной текст на фронте, Т42-02)

| Ключ | Где на bani | Запасной текст |
|---|---|---|
| `BathBookingNotice` (новый) | под «Забронировать» | `LEGAL_REVIEW_CYCLE42.md` §11.1 |
| `BathBookingTerms` (новый) | страница ресурса, страница брони | §11.2 (пункты 1–7, 9 — как `StayServiceBookingTerms` с нейтральной подстановкой §11.0; 3а и 8 — новые) |
| `BathPublicContactsNotice` (новый) | у адреса и телефона комплекса | §11.3 |
| `BathPositionsOwnerNotice` (новый) | у раздела «Позиции»; `noticeText` 409 фильтра | §11.5 |
| `BathCapacityOwnerNotice` (новый) | у поля «Вместимость» | §11.5 (второй блок) |
| `BathPaymentProofNotice`, `BathPaymentRequisitesOwnerNotice`, `BathOwnerCancelNotice` (новые) | загрузка подтверждения, реквизиты, отмена владельцем | §11.4 |
| `StayServiceCancellationTerms`, `StayServiceCommentNotice`, `StayServiceCancellationOwnerNotice`, `StayMessengerConsent` | как в dom | без изменений |
| `StayServiceSafetyOwnerNotice` | у описания ресурса (и услуги «Домов») | новая запасная редакция + «Не обещайте лечебного или оздоровительного эффекта и не продавайте медицинские услуги…» (Т42-07) |
| `StayServiceBookingNotice`, `StayServiceBookingTerms` (dom) | — | запасная подстановка компании — нейтральная «которая оказывает эту услугу» (Т42-01, §11.0) |

**Сайт bani не запрашивает** `StayPublicContactsNotice`, `StayTouristTaxNotice`, `StayRegistryOwnerNotice`,
`StayMigrationOwnerNotice`, `StayCheckInInfoOwnerNotice`, `StayServiceAddNotice`, `StayReminder*`, `StayBooking*`,
`StayPaymentProofNotice`, `StayPaymentRequisitesOwnerNotice`, `StayOwnerCancelNotice` (Т42-04, тест `CY42-F12`).

---

## §42.38. Эталон расчётов — `contracts/cycle42/bani-vectors.json`

Разделы: `sessionReminder` (`moment`, `decide`), `guests`, `restrictedItems`, `ownerText`, `gate`. C# `BaniVectorsTests` читает
все разделы; vitest `bani/src/utils/baniVectors.test.ts` — `guests`, `restrictedItems`, `ownerText` (фронт показывает подсказку
до отправки). Правка правила = правка векторов в том же коммите. `contracts/cycle39/service-vectors.json` не меняется — бани
ходят по тем же векторам бизнес-дня, цен, стартов, пересечений и возврата.

---

## §42.39. Сводка маршрутов (для `Cycle22RouteTable.golden.txt`)

**68 новых маршрутов** под `/api/baths/*` (методы, `operationId` и тела — `openapi.yaml`):

| Группа | Маршрутов | Авторизация |
|---|---|---|
| каталог, города, страница комплекса | 3 | anon |
| ресурс публично: страница, availability, starts, quote, orders | 5 | anon (`orders` — anon или auth) |
| бронь по токену: get, payment-proofs POST/GET, cancel, push-subscription, push-subscription/remove | 6 | anon |
| `service-orders/my` | 1 | auth |
| компания: create, my, slug-check, trial GET/POST, карточка, settings, payment-details, provider, slug, qr, notification-settings GET/PUT, schedule, revision | 15 | auth + `StaysPermission` |
| ресурсы: список, создание, порядок, карточка, удаление, setup, content, publish, unpublish, archive, фото ×3, правила цены ×4, позиции ×5, расписание ×5 | 27 | auth + `StaysPermission` |
| брони персонала: service-day, starts, availability, quote, список, ручная, карточка, confirm, reject, cancel, файл | 11 | auth + `StaysPermission` |

Существующие маршруты цикла 39 после перехода на общий базовый контроллер **не меняют** строк эталона (то же имя
контроллера, тот же шаблон, те же атрибуты) — это проверка регресса: diff эталона содержит только добавленные строки
`api/baths/*`. Изменённые ответы и входы (§42.21) строк эталона не меняют.

**Замечание для QA к `openapi.yaml`:** операция `ownerSubscriptionBathsCycle42` описывает ответ 200 **только** для
`line=Baths` (частичная схема с обязательным `baths`); ответы других линеек — в `contracts/cycle24/` и `contracts/cycle37/`.
Проверку schemathesis для этой операции запускать с `line=Baths` от владельца банной компании.
