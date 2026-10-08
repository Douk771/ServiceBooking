# ARCHITECTURE — цикл 37 ServiceBooking: «Дома», цикл 1 — посуточная аренда домов в Шерегеше (dom.ezbook.ru)

**Разделы §37.0–§37.19.** Вход: `SPEC_CYCLE37_STAYS_HOUSES.md` (ответы заказчика на Q1–Q5 от 2026-10-08 — перед §0-bis),
`LEGAL_REVIEW_CYCLE37.md` (предварительный обзор legal-counsel) и решения заказчика по нему от 2026-10-08 (§37.0a),
`CURRENT_STATE.md` (шапка на `f60da0d`), код `develop` = `0f9b9fb`. Ветка цикла — `cycle/037-stays-houses` (подготовлена
devops; архитектор веток не трогает). Образец вертикали — циклы 23–25 («Заказы»): `ARCHITECTURE_CYCLE23.md`,
`ARCHITECTURE_CYCLE24.md`, `ARCHITECTURE_CYCLE34.md`.

**Документы цикла:**

| Файл | Что | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE37.md` (этот) | решения, модель, механизмы, структура, задачи, риски | все |
| `API_CONTRACT_CYCLE37.md` (§37.20–§37.39) | контракт словами: маршруты, права, порядок проверок, коды, тексты, изменения существующих маршрутов | backend, frontend, QA |
| `contracts/cycle37/openapi.yaml` | **источник истины по форме** (OpenAPI 3.0.3): prism-мок, `openapi-typescript`, schemathesis, redocly, `OpenApiContract` | backend, frontend, QA, CI |
| `contracts/cycle37/dom-routes.json` | маршруты dom и политика адреса компании «Дома» и дома (формат, резерв слов) | backend (embedded), frontend (тест), QA |
| `contracts/cycle37/stay-vectors.json` | эталон расчёта: деньги, цена ночи, возврат, доступность дат — один набор векторов для юнит-тестов C# и TS | backend, frontend, QA |

Корневые `ARCHITECTURE.md`/`API_CONTRACT.md` — документы цикла 3, по конвенции не перезаписываются.

---

## §37.0a. Решения заказчика по юридическому обзору (2026-10-08) — кодировать по ним, а не по букве SPEC

| # | Решение | Что меняется против SPEC | Где в этом документе |
|---|---|---|---|
| ЮР-1 | Шаблоны отмены — три законных из §6.2 обзора: **«Стандартный»** (по умолчанию), **«Гибкий»**, **«Без удержаний»**. Удержание — не больше стоимости **первой ночи** и только в день заезда / при незаезде. Гостю показывается **«к возврату не меньше X ₽»**. Слова «задаток», «невозвратный», «депозит» в текстах dom не используются | SPEC §4.8 (Мягкий/Средний/Строгий, 50 %/0 %) **не реализуется** ни в каком виде, даже конфигурацией | §37.6.4, §37.13.4 |
| ЮР-2 | У дома «Вид объекта» (закрытый список) и «Номер в реестре» + ссылка на запись; номер виден на карточке. Дома без номера **публикуются**: при публикации владелец даёт заверение по тексту `StayRegistryOwnerNotice` («жилое помещение, не средство размещения» либо номер реестра). Заверение хранится с датой, автором и версией текста | новое: поля дома, таблица заверений, проверка при публикации | §37.2.3, §37.6.6 |
| ЮР-3 | Сведения об исполнителе у компании «Дома»: статус, наименование/ФИО, ИНН, ОГРН/ОГРНИП, адрес для претензий. ФИО владельца-физлица (и плательщика НПД без ИП) гостю — **только на странице его брони**, публично — статус и ИНН | новое: поля в `StaysSettings`, снимок в брони, обязательны для броней с предоплатой (Т37-03) | §37.2.2, §37.10.3 |
| ЮР-4 | Коды доступа в мессенджеры и push по умолчанию **не отправляются**: уходит только ссылка на страницу брони. Владелец может включить «отправлять текст целиком» с предупреждением `StayCheckInInfoOwnerNotice` | US-37-28 «отправляется по каналам» — по умолчанию ссылкой | §37.12.3 |
| ЮР-5 | Комментарий гостя горничной по умолчанию **скрыт**; владелец включает настройкой | SPEC US-37-08 «видит комментарий» — по умолчанию нет | §37.9 |
| ЮР-6 | Подтверждения оплаты — 90 дней от **более поздней** из дат (выезд, конечный статус); брони «Снята: не оплачена» — обезличивание через **30 дней**; прочие ПДн брони и журнал — 3 года; факт оплаты (сумма, кто и когда подтвердил, отметка об удалении файла) остаётся в брони. В интерфейсе гостя — **«подтверждение оплаты»**, не «чек» | US-37-32 «90 дней после выезда» уточнено; сущность и маршруты называются `payment-proofs` | §37.8, §37.13.3 |
| ЮР-7 | Ссылка на бронь на непроверенный номер — риск принят письменно, верификации номера нет | — (подтверждает Р10) | §37.18 |
| ЮР-8 | Туристический налог — предупреждение `StayTouristTaxNotice` в трёх местах (страница дома под ценой, форма брони, страница брони). Паспортные данные в комментариях, блокировках и текстах заселения запрещены — подсказки в UI (`StayGuestCommentNotice`, `StayMigrationOwnerNotice`) | новые блоки текста | §37.13.4 |
| ЮР-9 | Остальные требования Т37-01…Т37-15 и ключи текстов `Stay*` из §15–§16 обзора — в задачах (§37.17) | — | §37.13.4, §37.17 |

**Q5 (заказчик): никаких отдельных механизмов активации.** В коде нет флага `Stays:Enabled`, нет «включения вертикали
командой»: тарифы и город сеются миграцией, всё, что есть в цикле, работает сразу после деплоя. «Стендом» dom делает
только то, что реальных владельцев не приглашают до ответов живого юриста (Т37-15). Единственное условие в
инфраструктуре — смоук dom в деплое срабатывает, когда на машине уже установлен vhost dom (§37.15.3): это проверка
готовности окружения, а не выключатель продукта.

---

## §37.0. Итог решений — ответы на §7 A1–A14 одним экраном

| # | Вопрос SPEC | Решение | Раздел |
|---|---|---|---|
| A1 | Модель | 15 новых таблиц: `StaysSettings` (1:1 с компанией), `Houses`, `HousePhotos`, `HousePricePeriods`, `HouseRegistryAttestations`, `HouseBlocks`, `HouseBlockEvents`, **`HouseOccupancies`** (единая занятость), `StayBookings`, `StayBookingCharges` (строки суммы), `StayBookingEvents`, `StayPaymentProofs`, `StayGuestPushSubscriptions`, `StayGuestPushNotifications`, `StaysSubscriptions`. Деньги — **целые рубли** (`int`, суффикс `Rub`). Снимки цен ночей, правил, реквизитов, исполнителя, принятых версий текстов — в брони | §37.2 |
| A2 | Вид компании | `CompanyKind.Stays = 2` (дописан в конец). Неизменяем. Салонная защита `CompanyKindGuard.RejectShop*` обобщается до «всё, что не салон» — **одна правка закрывает весь закрытый перечень §389.2 цикла 23**. Плюс аудит всех бинарных развилок `Kind == Orders ? … : …` (~25 мест) и страж-тест, запрещающий новые. Услуги-слоты цикла 2 ложатся **внутрь** компании «Дома» новыми таблицами; модуль «Бани» в той же компании — отдельная таблица модулей позже, вид компании для этого не меняется | §37.3 |
| A3 | Двойная бронь | **Исключающее ограничение PostgreSQL** на `HouseOccupancies`: `EXCLUDE USING gist ("HouseId" WITH =, daterange("StartDate","EndDate",'[)') WITH &&) WHERE ("ReleasedAtUtc" IS NULL)` (расширение `btree_gist`). Одна таблица для всех источников занятости → бронь, блокировка, ручная бронь и будущий iCal физически не могут пересечься. Поверх — advisory-lock `stay-house:{houseId}` для правил, которые ограничение не выражает (разрыв, «ленивое» снятие истёкших удержаний) | §37.5 |
| A4 | Адреса dom | `dom.ezbook.ru/` — каталог; `/<slug>` — компания; `/<slug>/<houseSlug>` — дом; `/b/<token>` — бронь; кабинет — `/cabinet/...`. Slug — общее пространство платформы (существующий уникальный индекс), формат и резерв — `contracts/cycle37/dom-routes.json` (embedded, `StaysSlugPolicy`) | §37.4 |
| A5 | Третий фронтенд | Третье приложение **в том же npm-пакете**: `frontend/dom/`, `vite.dom.config.ts`, `tsconfig.dom.json`, `tailwind.dom.config.js`. `goods-shared-sources.js` обобщается в `frontend/shared-sources.js` (один список общих страниц для goods и dom). Сборка — `dist/__dom/` в том же релизе (`merge-site-dist.mjs goods dom`). Правовые страницы, гейты, вход, регистрация — общие из `frontend/src`, без копий | §37.14 |
| A6 | Источник под iCal | `HouseOccupancies.Source` = `PlatformBooking` / `OwnerBlock` / `ExternalCalendar` (значение 2 зарезервировано). Импорт цикла 2 добавит таблицу фидов и nullable-колонку `ExternalFeedId` — добавочно, без ломки | §37.5.5 |
| A7 | Услуги к брони | Сумма брони — **строки** `StayBookingCharges` (`Kind`: ночи, доп. места, собаки, манеж, ручной итог; в цикле 2 — слот услуги, позиция услуги) с флагом `PrepayEligible`. Итог = Σ строк, предоплата = округление(Σ строк с `PrepayEligible` × %). Цикл 2 добавляет строки и FK `ServiceSlotBookingId?` — формула не меняется | §37.2.4, §37.6 |
| A8 | Таймеры | Три `IScheduledTask`: `stays-hold-expiry` (полоса `realtime`, 15 с), `stays-scheduled-messages` (`main`, 60 с: «осталось 10 минут», напоминание накануне, информация к заселению), `stays-guest-push-dispatch` (`realtime`, 10 с). Однократность — отметки-колонки в брони с условным `UPDATE … WHERE отметка IS NULL` + уникальный ключ идемпотентности в очередях. Время — `IStaysClock` (подменяется в тестах) | §37.7 |
| A9 | Подтверждения оплаты | Приватное хранилище (`FileStorage.SavePrivateAsync`, на машине в РФ). PDF — проверка сигнатуры `%PDF-`, хранится как есть, отдаётся только `attachment` + `sandbox`-CSP. JPEG/PNG/WebP — через конвейер изображений (перекодирование = удаление метаданных). **HEIC не принимается сервером**: `accept` формы без HEIC заставляет iOS Safari самому конвертировать в JPEG (отклонение от низкорискового предположения SPEC — §37.18). До 10 МБ, до 3 файлов. Удаление — правило retention (ЮР-6) | §37.8 |
| A10 | Роли | Сотрудник = `CompanyMember.Role = Master` (как у goods) + новая колонка **`StaffPosition`** (`Manager` / `Housekeeper`). Права — одна таблица `Services/Stays/StaysAccess.cs` (`StaysPermission`). Горничная — только `ViewSchedule` | §37.9 |
| A11 | Тарифы | Своя линейка `Line = Stays`, своя таблица `StaysSubscriptions` (как `OrdersSubscriptions`), поле `SubscriptionPlanConfig.MaxHouses`. 4 тарифа сеются миграцией с фиксированными Id. Триал — тариф с фиксированным Id (не `IsSystemTrial`, чтобы не трогать 29 мест салонного триала), однократность — существующие `TrialGrants`/`TrialPhoneRegistrations` с новой колонкой `Line`. Приём броней решает одна чистая функция `StaysBookingGate` — **не `AllowOnlineBooking`** | §37.10 |
| A12 | Шерегеш | В справочнике нет → строка миграцией (`Шерегеш`, `Кемеровская область`, `Asia/Novokuznetsk`), идемпотентно по `(Name, Region)`. Компании «Дома» создаются только в нём (сервер подставляет сам) | §37.11.1 |
| A13 | Каталог | Без кеша на занятость: один запрос по `HouseOccupancies` за даты (индекс), цены — в памяти по периодам. Кешируется на 30 с только «база» (опубликованные дома, компании, гейт). Создание брони всегда проверяет по БД под замком — кеш не может дать двойную бронь | §37.11 |
| A14 | Уведомления гостю | Те же очереди: `OutboundNotification` (+`StayBookingId`), `StaffPushNotification` (+`StayBookingId`), `StaffMaxMessage` (+`StayBookingId`); push гостю — своя пара таблиц по образцу goods. Единая точка «что произошло» — `StayBookingEventLog` → `StayNotificationPlanner`. Витрина/демо: `ShowcaseOutboundGuard` вызывается в планировщике так же, как у заказов | §37.12 |

---

## §37.1. Стек: новых зависимостей — ноль

Проект существующий, стек не выбирается заново. Вертикаль — модуль монолита (Р1), третий фронтенд — третья сборка того
же пакета (Р2).

| Потребность | Чем закрываем | Почему не новое |
|---|---|---|
| Бэкенд модуля | ASP.NET Core 8 + EF Core 8 + PostgreSQL 16, конвенции проекта (толстые контроллеры + чистые правила в `Services/Stays/`) | Р1 |
| Гарантия «нет двух броней на ночь» | исключающее ограничение PostgreSQL + `btree_gist` (contrib, есть в `postgres:16` и `postgres:16-alpine`, расширение «trusted» с PG 13) | ограничение уровня БД держит инвариант для **всех** путей записи, включая будущий импорт iCal; блокировка одна этого не даёт |
| Автообновление шахматки и страницы брони | HTTP-опрос + счётчик ревизий (`StaysSettings.BookingsRevision`), как доска заказов | SSE/SignalR отвергнуты в цикле 23 (§397) по тем же причинам |
| QR | `QRCoder` (уже есть, `ShopQrCode`) | — |
| Фото домов | `ImageUploadService` + существующий профиль галереи компании | одна реализация конвейера |
| Подтверждения оплаты | `FileStorage` private + `ImageProcessor`; PDF — только проверка сигнатуры | PDF-библиотека на сервере не нужна: файл не разбирается |
| Капча | `CaptchaService` + `SmartCaptcha.tsx` | — |
| Время | `IStaysClock` по образцу `INotificationClock` | `TimeProvider` + `FakeTimeProvider` потребовал бы новый NuGet в тесты |
| Тексты правовых блоков | `LegalTextKey` вне `All` + запасной текст на фронте | конвенция §6.3 |

**Масштаб и продажа как сервиса.** Нагрузка §6 (500 домов, 30 броней/ч) — копейки для одной машины. В модуле нет
состояния в памяти процесса, кроме 30-секундного кеша базы каталога: второй экземпляр API возможен без липких сессий.
Онлайн-оплата позже — только с зачислением **напрямую** на счёт компании (обзор §7 п. 2): модель брони уже хранит
предоплату как факт, а не как деньги платформы.

---

## §37.2. Модель данных (A1)

Сущности — `ServiceBooking.Core/Entities/`, конфигурация — `AppDbContext`. Перечисления хранятся **числом**, только
дописыванием в конец. Даты ночей — `date` (`DateOnly`), моменты — `timestamptz` UTC, время суток — `time` (`TimeOnly`).
Деньги — `int` рублей (SPEC §4.4 «целые рубли»; отклонение от конвенции `decimal(10,2)` осознанное: ни одна сумма
вертикали не бывает дробной, а целое исключает класс ошибок округления).

### §37.2.1 Изменения существующих таблиц

| Таблица | Изменение | Зачем |
|---|---|---|
| `Companies` | нет колонок. `Kind = 2` (`Stays`) — новое значение | A2 |
| `CompanyMembers` | `StaffPosition int NULL` → `StaffPosition { Manager = 0, Housekeeper = 1 }`. Не NULL только при `Role = Master` в компании `Stays` — проверяется кодом (CHECK не видит вида компании) | A10 |
| `SubscriptionPlanConfigs` | `MaxHouses int NULL` (только `Line = Stays`; null = без ограничения) | A11 |
| `TrialGrants` | `Line int NOT NULL DEFAULT 0`. Частичный уникальный индекс «один триал на аккаунт» → `(BillingAccountId, Line) WHERE Source <> SuperAdminOverride` | триал линейки «Дома» |
| `TrialPhoneRegistrations` | `Line int NOT NULL DEFAULT 0`. Уникальный индекс `(PhoneKeyHash)` → `(Line, PhoneKeyHash)` | однократность на номер **в линейке** |
| `OutboundNotifications` | `StayBookingId uuid NULL` FK → `StayBookings` `SetNull`; CHECK «не больше одного из `BookingId`/`OrderId`/`StayBookingId`» | A14 |
| `StaffPushNotifications` | `StayBookingId uuid NULL` FK `SetNull`; тот же CHECK | A14 |
| `StaffMaxMessages` | `StayBookingId uuid NULL` FK `SetNull` | A14 |
| `PushSubscriptions` | нет колонок; `Site = 2` (`Stays`) для устройств, включённых на dom | §37.12 |
| `Cities` | строка «Шерегеш» (данные) | A12 |
| `SubscriptionPlanConfigs` | 4 строки тарифов «Дома» (данные, §37.10.1) | A11 |

### §37.2.2 `StaysSettings` — настройки компании «Дома», 1:1, PK = `CompanyId`

Строка создаётся в транзакции создания компании (как `ShopSettings`). Читатели трактуют отсутствие строки как дефолты.

| Поле | Тип | Дефолт | Ограничения / смысл |
|---|---|---|---|
| `CompanyId` | uuid PK, FK → Companies `Restrict` | | |
| `CheckInTime`, `CheckOutTime` | time | 14:00 / 12:00 | шаг 30 мин; `CheckOutTime ≤ CheckInTime` (CHECK) |
| `MinNights`, `MaxNights` | int | 1 / 30 | 1…30, 1…90, `Min ≤ Max` (CHECK) |
| `HorizonDays` | int | 365 | 30…730 |
| `AllowGapFill` | bool | false | §4.3 «закрыть разрыв» |
| `AllowSameDayCheckIn` | bool | true | |
| `HoldMinutes` | int | 30 | 10…180 |
| `PrepayPercent` | int | 30 | 0…100 |
| `PaymentDetails` | varchar(1000) NULL | | реквизиты; **никогда** не попадают в публичные DTO (Т37-04) |
| `PaymentPurpose` | varchar(200) NULL | | назначение платежа |
| `CancellationPolicy` | int → `StayCancellationPolicy { Standard = 0, Flexible = 1, NoDeductions = 2 }` | Standard | ЮР-1 |
| `DogFeeRub`, `CotFeeRub` | int | 0 / 0 | за единицу за ночь, 0…100 000 |
| `CheckInInfoSendTime` | time | 09:00 | |
| `CheckInInfoText` | varchar(2000) NULL | | общий текст к заселению |
| `CheckInInfoSendFullText` | bool | **false** | ЮР-4: false — в мессенджер уходит только ссылка |
| `ArrivalReminderEnabled` | bool | true | напоминание накануне 18:00 |
| `HousekeeperSeesGuestComment` | bool | **false** | ЮР-5 |
| `GuestWebPushEnabled` | bool | true | push гостю со страницы брони |
| `GuestMessengerEnabled` | bool | false | WhatsApp/MAX гостю (нужен оплаченный канал) |
| `StaffMaxEnabled` | bool | true | MAX персоналу (как `ShopSettings.StaffMaxEnabled`) |
| `ProviderStatus` | int NULL → `StayProviderStatus { Organization = 0, IndividualEntrepreneur = 1, SelfEmployed = 2, Individual = 3 }` | | ЮР-3 |
| `ProviderName` | varchar(300) NULL | | наименование или ФИО |
| `ProviderInn` | varchar(12) NULL | | `InnValidator`: 10 цифр у организации, 12 — у остальных |
| `ProviderOgrn` | varchar(15) NULL | | обязателен у `Organization` (13) и `IndividualEntrepreneur` (15) |
| `ProviderClaimsAddress` | varchar(500) NULL | | адрес для претензий |
| `BookingsRevision` | bigint | 0 | счётчик для опроса шахматки; пишут **только** `StayBookingEventLog` и `HouseBlockWriter` |
| `UpdatedAtUtc`, `UpdatedByUserId?` | | | |

«Показывать в каталоге» — существующий `Company.ShowInPublicListing` (создаётся `true`, как у магазина).
Телефон для гостей, название, описание, логотип, адрес компании, ссылки на карты — существующие поля `Company`.

### §37.2.3 Дома

**`House`**

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | uuid | |
| `CompanyId` | uuid FK `Restrict` | |
| `Slug` | varchar(50) | уникален внутри компании `(CompanyId, Slug)`; формат — `dom-routes.json → houseSlugPattern` |
| `Name` | varchar(100) | |
| `Description` | varchar(4000) NULL | |
| `Capacity` | int | 1…50 |
| `ExtraBedsEnabled` | bool | |
| `ExtraBedsMax` | int | 1…10 (значим при `ExtraBedsEnabled`) |
| `ExtraBedPriceRub` | int | ≥ 0 |
| `DogsForbidden` | bool | |
| `HasCot` | bool | |
| `AmenitiesMask` | bigint | биты `HouseAmenity` (append-only, справочник — §37.11.3) |
| `Address` | varchar(500) NULL | null → показывается адрес компании |
| `YandexMapsUrl`, `TwoGisUrl` | varchar(500) NULL | `MapLinkValidation` |
| `CheckInInfoText` | varchar(2000) NULL | текст к заселению дома |
| `PriceMode` | int → `HousePriceMode { Constant = 0, ByDates = 1 }` | |
| `ConstantPriceRub` | int NULL | 1…1 000 000; хранится и в режиме `ByDates` (не стирается при переключении) |
| `ObjectKind` | int NULL → `HouseObjectKind { GuestHouse = 0, OtherAccommodation = 1, Residential = 2 }` | ЮР-2 |
| `RegistryNumber` | varchar(32) NULL | ЮР-2; формат не навязывается (обзор: «сверить»), `^[0-9A-Za-zА-Яа-яЁё\-]{5,32}$` |
| `RegistryUrl` | varchar(500) NULL | только `https://` |
| `IsPublished` | bool | |
| `Position` | int | порядок у компании |
| `ArchivedAtUtc` | timestamptz NULL | архив: не публикуется, не считается в тарифе |
| `CreatedAtUtc`, `UpdatedAtUtc` | | |

Индексы: уникальный `(CompanyId, Slug)`; `(CompanyId, Position)`; частичный `(CompanyId) WHERE "IsPublished" AND "ArchivedAtUtc" IS NULL`.
CHECK: `NOT ("IsPublished" AND "ArchivedAtUtc" IS NOT NULL)`.

**`HousePhoto`** — `Id`, `HouseId` FK `Cascade`, `CompanyId`, `Url`, `ThumbnailUrl`, `Position`, `CreatedAtUtc`.
Публичная область `PublicArea.Houses` (дописать в конец enum). Профиль — тот же, что у `CompanyPhoto`. До 15 на дом
(`Stays:MaxPhotosPerHouse`).

**`HousePricePeriod`** — `Id`, `HouseId` FK `Cascade`, `StartDate date`, `EndDate date` (**включительно**), `PriceRub int`
(1…1 000 000), `CreatedAtUtc`, `UpdatedAtUtc`.
- Длинные периоды (`StartDate < EndDate`) не пересекаются: `EXCLUDE USING gist ("HouseId" WITH =, daterange("StartDate","EndDate",'[]') WITH &&) WHERE ("StartDate" < "EndDate")`.
- Однодневные (`StartDate = EndDate`) уникальны по `(HouseId, StartDate) WHERE "StartDate" = "EndDate"` и **могут** лежать
  внутри длинного — переопределяют его цену (SPEC §4.4).
- CHECK `"StartDate" <= "EndDate"`, длина ≤ 731 день.

**`HouseRegistryAttestation`** (ЮР-2) — append-only, не удаляется (доказательство заверения).

| Поле | Тип | Смысл |
|---|---|---|
| `Id`, `HouseId` (FK `Restrict`), `CompanyId` | | |
| `ObjectKind` | int | снимок на момент заверения |
| `RegistryNumber`, `RegistryUrl` | NULL | снимок |
| `NoticeVersion` | varchar(80) | версия текста `StayRegistryOwnerNotice` из манифеста; если текста в манифесте ещё нет — `fallback:<sha256 запасного текста>` (запасной текст — константа сервера `StaysTexts.RegistryNoticeFallback`; кабинет показывает ровно его из `HouseManageDto.registryNotice`) |
| `AttestedAtUtc`, `AttestedByUserId` | | |
| `IpAddress` | varchar(45) NULL | как у `ConsentRecord` |

### §37.2.4 Занятость, бронь, суммы, журнал

**`HouseOccupancy`** — **единственное** место занятости дома (A3, A6). Пишет **только** `HouseOccupancyWriter`.

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | uuid | |
| `CompanyId`, `HouseId` | uuid FK `Restrict` | |
| `StartDate` | date | первая ночь |
| `EndDate` | date | **дата выезда** (полуинтервал `[StartDate, EndDate)`), CHECK `EndDate > StartDate` |
| `Source` | int → `OccupancySource { PlatformBooking = 0, OwnerBlock = 1, ExternalCalendar = 2 }` | 2 — цикл 2 |
| `StayBookingId` | uuid NULL FK `Restrict` | при `Source = 0` |
| `HouseBlockId` | uuid NULL FK `Restrict` | при `Source = 1` |
| `HoldExpiresAtUtc` | timestamptz NULL | денормализация: не null ⇔ бронь в статусе «Удержана». Нужна публичному календарю и каталогу без join |
| `ReleasedAtUtc` | timestamptz NULL | не null = период больше не занимает ночи (конечный статус брони, удалённая блокировка) |
| `CreatedAtUtc` | | |

- **`EXCLUDE USING gist ("HouseId" WITH =, daterange("StartDate","EndDate",'[)') WITH &&) WHERE ("ReleasedAtUtc" IS NULL)`**
  — имя `EX_HouseOccupancies_NoOverlap`.
- CHECK `("Source" = 0) = ("StayBookingId" IS NOT NULL)`, `("Source" = 1) = ("HouseBlockId" IS NOT NULL)`.
- Уникальные частичные `(StayBookingId) WHERE "StayBookingId" IS NOT NULL`, `(HouseBlockId) WHERE "HouseBlockId" IS NOT NULL`.
- Индекс для выборок `(HouseId, EndDate) WHERE "ReleasedAtUtc" IS NULL`.

**`StayBooking`**

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | uuid | |
| `CompanyId`, `HouseId` | uuid FK `Restrict` | |
| `PublicToken` | varchar(64) | 32 случайных байта base64url (256 бит), уникальный индекс; хранится открыто (прецедент R-6 цикла 23) |
| `Status` | int → `StayBookingStatus { Held = 0, AwaitingPaymentCheck = 1, Confirmed = 2, ExpiredUnpaid = 3, PaymentRejected = 4, CancelledByGuest = 5, CancelledByOwner = 6 }` | append-only |
| `Version` | int | concurrency token (действия персонала с `expectedVersion`) |
| `IsManual` | bool | ручная бронь из кабинета (P1) |
| `CheckInDate`, `CheckOutDate` | date | CHECK `CheckOutDate > CheckInDate`, `CheckOutDate − CheckInDate ≤ 366` |
| `Nights` | int | = разность (хранится для отчётов и CHECK) |
| `Adults`, `Children`, `Dogs` | int | ≥1, ≥0, 0…20 |
| `NeedCot` | bool | |
| `ExtraBeds` | int | посчитано сервером |
| `ArrivalTime` | time NULL | null = «не знаю» |
| `GuestKind` | int → `StayActorKind` (`Guest` / `Customer` / `Staff` — ручная бронь) | |
| `GuestUserId` | text NULL FK → AspNetUsers `SetNull` | |
| `GuestName` | varchar(100) NULL | null после обезличивания |
| `GuestPhone` | varchar(20) NULL | канонический; null после обезличивания или у ручной брони без телефона |
| `Comment` | varchar(500) NULL | |
| `HoldExpiresAtUtc` | timestamptz NULL | только для `Held` |
| `TotalRub`, `PrepayRub`, `DueAtCheckInRub` | int | итог, предоплата, остаток |
| `PrepayPercentSnapshot` | int | |
| `NightPricesJson` | jsonb | `[{ "date": "2026-12-30", "priceRub": 4500 }, …]` — снимок цены каждой ночи |
| `CancellationPolicySnapshot` | int | ЮР-1 |
| `CheckInTimeSnapshot`, `CheckOutTimeSnapshot` | time | |
| `TimeZoneIdSnapshot` | varchar(64) | пояс компании на момент брони — все сроки брони считаются по нему |
| `PaymentDetailsSnapshot`, `PaymentPurposeSnapshot` | NULL | реквизиты на момент брони |
| `ProviderSnapshotJson` | jsonb NULL | `{status, name, inn, ogrn, claimsAddress}` на момент брони (ЮР-3) |
| `ConsentPrivacyVersion`, `ConsentTermsVersion`, `ConsentAcceptedAtUtc` | NULL | снимок как у гостевой записи |
| `BookingNoticeVersion`, `BookingTermsVersion`, `CancellationTermsVersion` | varchar(64) NULL | версии `StayBookingNotice`, `StayBookingTerms`, `StayCancellationTerms`, если они есть в манифесте |
| `NotifyByMessenger` | bool | галочка `StayMessengerConsent` (Т37-12) |
| `MessengerConsentVersion`, `MessengerConsentAtUtc` | NULL | |
| `IdempotencyKey` | uuid | уникальный `(CompanyId, IdempotencyKey)` |
| `StatusReason` | varchar(300) NULL | причина отклонения/отмены владельцем (гость видит) |
| `PaymentConfirmedAtUtc`, `PaymentConfirmedByUserId`, `PaymentConfirmedByNameSnapshot` | NULL | **факт оплаты** — остаётся после удаления файлов (ЮР-6) |
| `TerminalAtUtc` | timestamptz NULL | момент перехода в конечный статус |
| `HoldReminderQueuedAtUtc`, `ArrivalReminderQueuedAtUtc`, `CheckInInfoReleasedAtUtc` | NULL | отметки однократности (§37.7.3) |
| `PaymentProofsPurgedAtUtc` | NULL | «подтверждения удалены по сроку хранения» |
| `PersonalDataErased` | bool | обезличено (удаление аккаунта / retention) |
| `CreatedAtUtc`, `UpdatedAtUtc` | | |

Индексы: уникальные `(PublicToken)`, `(CompanyId, IdempotencyKey)`; `(CompanyId, Status)`, `(HouseId, CheckInDate)`,
`(GuestUserId, CreatedAtUtc)`, частичные `(GuestPhone, CreatedAtUtc) WHERE "GuestPhone" IS NOT NULL` (лимиты, выгрузка),
`(HoldExpiresAtUtc) WHERE "Status" = 0` (задача снятия), `(CheckInDate) WHERE "Status" = 2` (сообщения, график).

**`StayBookingCharge`** — строки суммы (A7). `Id`, `StayBookingId` FK `Cascade`, `Position`, `Kind` →
`StayChargeKind { Nights = 0, ExtraBeds = 1, Dogs = 2, Cot = 3, ManualTotal = 4 }` (цикл 2 допишет `ServiceSlot = 5`,
`ServiceItem = 6`), `Label varchar(200)` (собирает сервер: «3 ночи», «Доп. место × 2»), `Quantity int`, `UnitPriceRub int`
(у `Nights` — 0, сумма из `NightPricesJson`), `NightsCount int`, `AmountRub int`, `PrepayEligible bool` (true только у
`Nights`). `ManualTotal` — только у ручной брони с исправленным итогом: единственная строка «Итог изменён вручную»
вместо расчётных (US-37-24). Инвариант: `Σ AmountRub = TotalRub`; `PrepayRub = RoundHalfUp(Σ AmountRub(PrepayEligible) × % / 100)`
(у ручной брони `PrepayRub = 0`); проверка перед `SaveChanges`, как у `BookingService`.

**`StayBookingEvent`** — журнал, append-only, единственный писатель `StayBookingEventLog`.
`Id`, `StayBookingId` FK `Cascade`, `CompanyId`, `Kind` → `StayBookingEventKind { Created = 0, PaymentProofUploaded = 1,
PaymentProofViewed = 2, PaymentConfirmed = 3, HoldExpired = 4, PaymentRejected = 5, CancelledByGuest = 6,
CancelledByOwner = 7, CheckInInfoReleased = 8, PaymentProofsPurged = 9, PersonalDataErased = 10 }`, `OccurredAtUtc`,
`ActorKind` → `StayActorKind { Guest = 0, Customer = 1, Staff = 2, SuperAdmin = 3, System = 4 }`, `ActorUserId?`,
`ActorNameSnapshot?`, `FromStatus?`, `ToStatus?`, `Reason?` (300), `DetailsJson?` (jsonb: id файла, причина «по сроку»).
Индексы `(StayBookingId, OccurredAtUtc)`, `(OccurredAtUtc)`.

**`StayPaymentProof`** — `Id`, `StayBookingId` FK `Restrict`, `CompanyId`, `StorageKey varchar(200) NULL` (null после
удаления), `ContentType` (`application/pdf` | `image/jpeg` | `image/png` | `image/webp`), `SizeBytes int`,
`UploadedAtUtc`, `PurgedAtUtc NULL`. Имя файла клиента **не хранится** (бывает «чек Иванов.pdf»).

### §37.2.5 Блокировки

**`HouseBlock`** — `Id`, `CompanyId`, `HouseId` FK `Restrict`, `StartDate`, `EndDate` (дата «по» — **дата выезда**,
полуинтервал, как у занятости), `Kind` → `HouseBlockKind { Repair = 0, Personal = 1, Other = 2 }`, `Comment varchar(300) NULL`,
`CreatedAtUtc`, `CreatedByUserId`, `UpdatedAtUtc`, `DeletedAtUtc NULL` (мягкое удаление: строка нужна журналу).
**`HouseBlockEvent`** — `Id`, `HouseBlockId`, `CompanyId`, `Kind` → `HouseBlockEventKind { Created = 0, Updated = 1,
Deleted = 2 }`, `OccurredAtUtc`, `ActorUserId`, `ActorNameSnapshot`, `BeforeJson?`, `AfterJson?`. Пишет только
`HouseBlockWriter` (вместе с занятостью и ревизией).

### §37.2.6 Push гостю и подписка линейки

- **`StayGuestPushSubscription`** — копия формы `OrderPushSubscription` с `StayBookingId` (ключи зашифрованы, AAD
  `"stay-guest-push-subscription:{Id}"`).
- **`StayGuestPushNotification`** — копия формы `CustomerOrderPushNotification` с `StayBookingId`, `SubscriptionId` →
  `StayGuestPushSubscriptions`; ключ `"{type}:{eventOrMarker}:{subscriptionId}"` уникален; `ExpiresAtUtc = очередь + 2 ч`.
- **`StaysSubscription`** — копия формы `OrdersSubscription` (`BillingAccountId` уникален, `PlanConfigId`, `PaidUntil`,
  `IsActive`, `CreatedAtUtc`, `UpdatedAtUtc`, `UpdatedByUserId`). Нет строки = нет тарифа (бесплатного уровня нет, Q2).

### §37.2.7 Миграции — две, закреплённым `dotnet-ef` 8.0.11, один разработчик в начале трека (BE-37-M)

1. **`Cycle37SharedChanges`** — только добавочное и данные:
   `HasPostgresExtension("btree_gist")` (в модели → и в снапшоте); колонки §37.2.1; пересоздание двух уникальных индексов
   триала с колонкой `Line`; CHECK «не больше одного FK» у очередей; строка «Шерегеш» (идемпотентный `INSERT … WHERE NOT
   EXISTS` по `(Name, Region)`, `SearchName` — по правилу `CitySearch.Normalize`, как `ExpandCityDirectory`); 4 тарифа
   §37.10.1 с фиксированными Guid (`StaysPlans.*SeedId`) + `PlanOptionRules` канала по образцу §448.3 цикла 24.
2. **`Cycle37StaysTables`** — 15 таблиц §37.2.2–§37.2.6 с индексами, FK и CHECK; три `EXCLUDE`-ограничения —
   `migrationBuilder.Sql(...)` (EF их не моделирует, дрейф снапшота они не создают).

`Down()` удаляет созданное и возвращает прежние индексы триала. ⚠️ `Down` первой миграции упадёт, если уже выданы триалы
«Дома» на номер, у которого есть триал «Записи» (дубль `PhoneKeyHash` в старом индексе) — записать в `DEPLOY.md`
(DO-37-03), как C24-10.

### §37.2.8 Инварианты модели (QA проверяет тестами)

1. Строки `StaysSettings`, `Houses`, `StayBookings`, `HouseBlocks`, `HouseOccupancies` существуют **только** у компаний
   `Kind = Stays`; ни одна строка записи/заказов не создаётся у `Stays` (матрица §37.3.2).
2. У одного дома нет двух неосвобождённых периодов занятости с общей ночью (ограничение БД + параллельные тесты §37.5.4).
3. Бронь в статусе `Held`/`AwaitingPaymentCheck`/`Confirmed` ⇔ её строка занятости не освобождена; конечный статус ⇔
   освобождена (в одной транзакции, пишет `HouseOccupancyWriter`).
4. `HouseOccupancies.HoldExpiresAtUtc` не null ⇔ бронь `Held`, и он равен `StayBookings.HoldExpiresAtUtc`.
5. Каждое изменение `StayBooking` — с событием `StayBookingEvent` в той же транзакции и `BookingsRevision += 1`.
6. Суммы: §37.2.4 (строки = итог, предоплата по одной функции).
7. Опубликованный дом имеет заверение `HouseRegistryAttestation` с теми же `ObjectKind`/`RegistryNumber`, что сейчас у
   дома (§37.6.6).

### §37.2.9 Точки расширения под цикл 2 — без миграций-ломок

| Будущее | Как ляжет | Что сделано сейчас |
|---|---|---|
| iCal-импорт | таблица `HouseExternalFeeds`, nullable `HouseOccupancies.ExternalFeedId`; импорт пишет строки `Source = ExternalCalendar` через тот же `HouseOccupancyWriter`; конфликт с бронью платформы = нарушение `EXCLUDE` → запись конфликта фида, а не 500 | источник в занятости, единственный писатель, ограничение уже держит все источники |
| iCal-экспорт | маршрут `GET /api/stays/public/houses/{id}/calendar.ics?key=` по `HouseOccupancies` | все занятые периоды — одна таблица |
| Услуги-слоты (баня, чан, фурако) | таблицы `StayServices` (ресурс компании), `StayServiceSlotRules`, `StayServiceSlotBookings` со **своим** `EXCLUDE` по `tstzrange` ресурса (тот же приём, что §37.5); строки `StayBookingCharges.Kind = ServiceSlot/ServiceItem` + `ServiceSlotBookingId?` | сумма — строками, предоплата — по флагу строки (A7); `StaysSettings` 1:1 принимает флаг «услуги без проживания» |
| Модуль «Бани» в той же компании | таблица `CompanyModules (CompanyId, Module)`; `Kind` компании не меняется; гейты модулей — свои чистые функции | вид компании проверяется только в `CompanyKindGuard`/`StaysAccess`, а не размазан |
| Онлайн-оплата (ЮKassa) | `StayPayments` (провайдер, сумма, статус) рядом с `StayPaymentProofs`; переход `Held → Confirmed` по вебхуку — новая строка таблицы переходов | статусная машина — чистая функция с таблицей |

---

## §37.3. Вид компании «Дома» и изоляция вертикалей (A2, US-37-01)

### §37.3.1 Механизм

- `CompanyKind { Services = 0, Orders = 1, Stays = 2 }`. Задаётся только `CompanyCreationService` (параметр `kind`),
  сеттера в DTO нет нигде, админка тоже не меняет.
- **`CompanyKindGuard` обобщается.** `RejectShopAsync`/`RejectShop` переименовываются в `RejectNonSalonAsync`/
  `RejectNonSalon` (вызовов ~20, правятся одним коммитом) и отказывают **любому** виду, кроме `Services`, текстом по виду:
  - `Orders` — прежний `ShopRefusalText` (байт-в-байт, тесты цикла 23 не меняются);
  - `Stays` — `StaysRefusalText` = «Это компания «Дома»: записи, услуги и расписание для неё недоступны.»
  Одна правка закрывает все салонные маршруты закрытого перечня §389.2 цикла 23 (услуги, расписание, отзывы, отчёты,
  рассылка, клиенты, уведомления салона, согласия клиентов).
- Маршруты заказов (`/api/shops/*`, `/api/storefront/*`) уже проверяют `Kind == Orders` и для `Stays` отвечают 404 —
  проверить тестом матрицы, правок не нужно.
- Маршруты «Домов» (`/api/stays/*`) — `CompanyKindGuard.CheckAsync(db, id, Stays)`: `WrongKind`/`NotFound` → **404**
  (не оракул), как у магазина.

### §37.3.2 Закрытый перечень мест, где «компания = салон или магазин» (BE-37-1 обязан пройти все; QA — табличный тест)

**Аудит бинарных развилок.** Найдено (grep `CompanyKind.Orders ?`, `isShop`, `!= CompanyKind.`) — каждое место
переписывается на явный `switch` по трём видам с `_ => throw new UnreachableException()`:

| Место | Что делает сейчас | Что делать для `Stays` |
|---|---|---|
| `PublicSiteLinks.SiteBaseUrl/CompanyPageUrl` | `Orders ? goods : ezbook` | `Stays → StaysBaseUrl`, страница `{Stays}/{slug}`; новые методы `StayBookingPageUrl(token)`, `StaysCabinetBookingUrl(companyId, bookingId)`, `StaysSubscriptionUrl()`, `HousePageUrl(companySlug, houseSlug)` |
| `CompanyCreationService` (`isShop`) | салонная или магазинная ветка | третья ветка: `StaysSlugPolicy`, город = Шерегеш, `StaysSettings`, лимиты **не** салонные; попытка триала (§37.10.2) |
| `CompanyMembersController.AddMember` | `Orders ? shopSeats : salon plan` | `Stays`: роль только `Master`, `position` обязателен; салонный лимит мест **не применяется**; техпотолок `Stays:MaxStaffPerCompany` = 30 → 409 строка |
| `CompanyMembersController` салонные маршруты (`provides-services`, `services`, `commission`) | `RejectShopAsync` | через обобщённый guard |
| `CompanyPhotosController`, `CompanyPhotoTexts.Noun` | галерея салона и магазина | галерея компании у «Домов» **не используется** (фото — у домов): запись в галерею и `photo-usage` для `Stays` → 409 строкой «У компании «Дома» фото добавляются к домам.»; `GET …/photos` → `[]` |
| `CompaniesController.Update` (`PUT /api/companies/{id}`) | пояс магазина — только по городу | `Stays` — как магазин (пояс по городу; `ShopTimeZoneChangePolicy` обобщить до `CityTimeZoneChangePolicy`, при наличии броней смена смещения → 409); город меняться не может (только Шерегеш) → 400 «Город компании «Дома» — Шерегеш» |
| `CompaniesController` списки `GET /api/companies`, `/public` | `Kind == Services` | без изменений |
| `GET /api/companies/my`, `/member` (`?kind=`) | по умолчанию `Services` | `?kind=Stays` поддерживается `CompanyKindQuery` автоматически |
| `GET /api/companies/kinds-summary` | `Services`, `Orders` | + `stays` (в конец record) |
| `GET /api/companies/{slug}` | отдаёт и магазин с `publicUrl` | для `Stays` — тот же DTO с `kind = Stays`, `publicUrl` dom (ezbook и goods переадресуют) |
| `AdminController` компании | `?kind=` | `Stays` в фильтре; `publicUrl` через `PublicSiteLinks` |
| `CompanyTransferService` | имя тарифа по `Orders ?` | лимит домов целевого аккаунта по `StaysPlanResolver` (опубликованные дома переносимой компании + аккаунта ≤ `MaxHouses`, иначе 409 с текстом) |
| `NotificationChannelsController` (текст «уже привязан») | `Orders ? магазин : салон` | третий текст «Компания уже привязана…» |
| `OwnerSubscriptionService`, `BillingController` (`line`) | две линейки | третья ветка `Stays` (§37.10.4) |
| `AdminBillingController` (`AssignSubscription`, заявки) | `Line != Services`, `RequestedLine != Orders` | ветка `Stays` → пишет `StaysSubscriptions` |
| `AdminPlansController` | проверки по линейке, `IsSystemTrial` | `Line = Stays` разрешена; поле `maxHouses`; удалить/деактивировать сеянный триал «Домов» нельзя (409 строка), как системные |
| `PricingCatalogBuilder` (`/api/pricing`) | `Line == Services` | без изменений (тарифы «Домов» публично не показываются, L10) |
| `SubscriptionResolver` (`PaidNotificationNumbers`), `ChannelEligibility` | салоны ∨ магазины | `∨ (accountHasStays ∧ staysPlan.AllowNotificationChannel ∧ опция оплачена)` — у аккаунтов без «Домов» результат бит-в-бит прежний (юнит + функциональный регресс) |
| `PushSubscriptionWriter`, `PushController` (`allSites`, `siteUrls`) | два сайта | `Site = Stays`, порядок `Services → Orders → Stays`, `siteUrls.stays` |
| `StaffMaxLinkService` | eligible: сотрудник магазина | eligible: сотрудник магазина **или** «Дома» (кроме `Housekeeper`) |
| `TrialActivationService`, `TrialStateReader`, `AdminAccountDtoBuilder` | триал «Записи» | все чтения `TrialGrants`/`TrialPhoneRegistrations` получают `Line == Services` (6 мест, §37.10.2) |
| `ShowcaseGraph/Dataset`, `DemoBoardTicker`, `DemoController` | витрина салонов и магазинов | без изменений; новые таблицы — в `ShowcaseOwnership.NeverWritten` (иначе падает `ShowcaseOwnershipCoverageTests`); `DemoResetTables` строится из модели — новые таблицы попадают в `TRUNCATE` сами |
| `SubjectDataExporter`, `AccountDeletionService`, `ProfileConsentsController` | записи и заказы | + брони домов (§37.13.1) |
| `ShopOrderingGate`, `GoodsCatalogService`, `StorefrontController` | `Kind != Orders` → не магазин | без изменений (Stays для них «не существует») |

**Страж от новых развилок** — юнит-тест `CompanyKindBranchGuardTests` (по образцу `SubjectPhoneGateInvariantTests`):
сканирует `ServiceBooking.API/**/*.cs` регэкспом `CompanyKind\.Orders\s*\?|kind\s*==\s*CompanyKind\.Orders\s*\?|!=\s*CompanyKind\.(Orders|Services)`
и падает на совпадении вне allow-list (`CompanyKindQuery.cs`, тестовые фабрики). Новые развилки пишутся только `switch`.

**Фоновые задачи салонов и заказов** (`NotificationScheduler`, `photo-retention-cleanup`, `channel-health`,
`trial-lifecycle`) не трогаются: их порождают записи, заметки, каналы и салонные подписки — у `Stays` ни одной такой
сущности нет (инвариант §37.2.8-1).

### §37.3.3 Изменения фронтов ezbook и goods (весь перечень)

1. ezbook `CompanyPage`, `EmbedPage`: переадресация на `company.publicUrl` для **любого** `kind !== 'Services'` (сейчас
   `=== 'Orders'`). `EmbedPage` для `Stays` — «У этой компании нет онлайн-записи», как для магазина.
2. goods `StorefrontPage`: при 404 витрины — `GET /api/companies/{slug}`; `kind === 'Stays'` → `location.replace(publicUrl)`.
3. Кабинеты ezbook (`CabinetPage`) и goods (`CabinetHomePage`): строка «Ваши дома управляются на dom.ezbook.ru» при
   `kinds-summary.stays.count > 0`; на dom — зеркальные строки про ezbook и goods.
4. Админка (`AdminPage`, компании): фильтр «Дома», колонка «Тип» — «Дома»; планы — линейка «Дома» с полем «Макс. домов»;
   биллинг-аккаунт — блок подписки «Дома».
5. Типы: `CompanyKind = 'Services' | 'Orders' | 'Stays'` (рукописные `src/types/index.ts` + генерат цикла 37).
6. `src/utils/staffPushTexts.ts`, `src/test/workerRouting.ts` — третий сайт (§37.12.2).

---

## §37.4. Адреса dom и slug (A4, US-37-09)

- **Схема:** `https://dom.ezbook.ru/` — каталог (US-37-05); `/<slug>` — страница компании; `/<slug>/<houseSlug>` — дом;
  `/b/<token>` — бронь; `/cabinet/...` — кабинет. Короткие адреса — для QR и объявлений на Авито.
- **Пространство slug компании — общее** (существующий уникальный индекс `Companies.Slug`). Политика `StaysSlugPolicy`
  (новая, по образцу `SlugPolicy`): тот же формат и длина, резерв — `contracts/cycle37/dom-routes.json → reservedSlugs`
  (embedded resource). Применяется **только** к `Kind = Stays`. Проверка занятости — без учёта регистра под lock
  `company-slug` (как магазин); префикс витрины `primer-` запрещён.
- **Slug дома** — уникален в компании, `houseSlugPattern` из того же файла (длина 2–50), резерва нет (второй сегмент
  ничему не мешает). Предлагается сервером из названия (`SlugTransliterator`, суффиксы `-2`, `-3`), владелец меняет.
- **Предложение адреса компании:** `GET /api/stays/slug-check?name=&slug=` (как `GET /api/shops/slug-check`).
- **Смена slug** (компании или дома): владелец; предупреждение о старых ссылках и QR; редиректа нет (SPEC, низкий риск).
- **QR:** `GET …/qr` (компания) и `GET …/houses/{houseId}/qr` (дом) — `QRCoder.PngByteQRCode`, уровень Q, ~2000 px,
  содержимое — `publicUrl` / страница дома (переиспользовать `ShopQrCode`, переименовав в `PublicQrCode`).
- Правило «маршрут dom = слово в резерве» держат два теста: юнит бэкенда (первый сегмент каждого `spaRoutes` ∈
  `reservedSlugs`) и vitest `dom/src/domRoutes.test.ts` (каждый маршрут `DomApp` есть в `spaRoutes`).
- **API:** все анонимные маршруты вертикали — под `/api/stays/public/…` и `/api/stays/bookings/public/{token}…`, кабинетные —
  `/api/stays/companies/{companyId:guid}/…`: публичный `…/public/companies/{slug}` не пересекается с кабинетным путём по Id
  (правило redocly `no-ambiguous-paths`).

---

## §37.5. Занятость и гарантия от двойной брони (A3, A6) — главный риск продукта

### §37.5.1 Три уровня защиты

1. **Ограничение БД** `EX_HouseOccupancies_NoOverlap` — последний и непробиваемый рубеж: при любой ошибке кода, гонке или
   новом пути записи (ручная бронь, блокировка, будущий iCal) PostgreSQL отказывает с `23P01`. `HouseOccupancyWriter`
   ловит `PostgresException { SqlState: "23P01" }` и превращает его в 409 `DatesUnavailable`/`BlockConflictsWithBooking`.
2. **Advisory-lock `stay-house:{houseId}`** (транзакционный, `AdvisoryLock.AcquireAsync`) — сериализует всё, что
   **меняет занятость** дома: создание брони (гость и персонал), создание/изменение блокировки, снятие удержания задачей и
   «лениво». Под ним выполняются правила, которые ограничение не выражает: минимум ночей с учётом разрыва (нужны соседние
   периоды), проверка «у всех ночей есть цена», лимиты. Перевод в «Ожидает проверки оплаты» и подтверждение оплаты
   занятость не меняют — лок дома им не нужен.
3. **Оптимистичная блокировка `StayBooking.Version`** — действия персонала над одной бронью (подтвердить, отклонить,
   отменить) с `expectedVersion` → 409 `VersionMismatch` с актуальной бронью.

**Порядок блокировок везде один:** `stay-guest-phone:{телефон}` (только создание гостем, лимиты) → `stay-house:{houseId}`
→ строка `StayBookings` → строки `HouseOccupancies` → строка `StaysSettings` (ревизия, через журнал). Взаимоблокировок нет.

### §37.5.2 Истёкшее, но не обработанное удержание (US-37-21)

Строка занятости удержанной брони остаётся неосвобождённой до перехода брони в «Снята: не оплачена», а ограничение не
умеет «now()». Поэтому:
- **Чтение** (календарь, каталог, quote, шахматка): период с `HoldExpiresAtUtc ≤ now` считается свободным — условие в
  запросе `("ReleasedAtUtc" IS NULL AND ("HoldExpiresAtUtc" IS NULL OR "HoldExpiresAtUtc" > @now))`.
- **Создание брони** под `stay-house:{houseId}` первым шагом делает **ленивое снятие**: все брони этого дома в `Held` с
  `HoldExpiresAtUtc ≤ @now`, пересекающие запрошенные даты, переводятся тем же кодом, что и задача
  (`StayHoldExpirer.ExpireAsync(bookingIds)`: статус, освобождение занятости, событие `HoldExpired`, уведомление гостю).
  Новая бронь не ждёт фоновой задачи.

### §37.5.3 Гонка «истёк таймер / приложено подтверждение оплаты» — один исход

Оба пути — условный `UPDATE` одной строки брони с **одним** источником времени (`IStaysClock.UtcNow`, передаётся
параметром):

```sql
-- подтверждение оплаты (первый файл)
UPDATE "StayBookings" SET "Status" = 1, "HoldExpiresAtUtc" = NULL, "Version" = "Version" + 1, ...
WHERE "Id" = @id AND "Status" = 0 AND "HoldExpiresAtUtc" > @now;
-- снятие (задача и ленивое)
UPDATE "StayBookings" SET "Status" = 3, "TerminalAtUtc" = @now, "Version" = "Version" + 1, ...
WHERE "Id" = @id AND "Status" = 0 AND "HoldExpiresAtUtc" <= @now;
```

В READ COMMITTED второй `UPDATE` ждёт строковую блокировку первого и перепроверяет `WHERE` на новой версии строки — статус
уже не `Held`, затронуто 0 строк. Исходов ровно два, и они взаимоисключающие: файл принят **или** «Время на оплату истекло».
Файл обрабатывается (проверка, перекодирование) **до** открытия транзакции; запись файла на диск — до транзакции, при
отказе файл удаляется (порядок «файл → коммит → удаление при откате», как у фото). В той же транзакции, что и перевод в
`AwaitingPaymentCheck`, `HouseOccupancies.HoldExpiresAtUtc` обнуляется (инвариант §37.2.8-4).

### §37.5.4 Тесты (обязательны, SPEC §6 «Целостность»)

- N = 20 параллельных броней на одни даты одного дома → создана ровно 1, остальные 409 `DatesUnavailable`.
- Параллельно: бронь гостя ↔ блокировка владельца; бронь гостя ↔ ручная бронь — ровно одна сторона успешна.
- Отключённый замок (тестовый двойник `HouseOccupancyWriter` без lock) + параллельные вставки → ограничение БД всё равно
  не пускает вторую (доказывает уровень 1 отдельно от уровня 2).
- С поддельными часами: загрузка подтверждения и задача снятия на границе `HoldExpiresAtUtc` (−1 с, 0, +1 с; параллельно)
  → ровно один исход, журнал согласован.
- Истёкшее, но не снятое удержание: новая бронь на те же даты создаётся сразу, старая — «Снята: не оплачена».

### §37.5.5 Источник занятости под iCal (A6)

`Source = ExternalCalendar` зарезервирован. Публичный календарь для внешних периодов — «Занято»; шахматка — полоса
«Внешний календарь» (`BoardItemKind.External`). Импорт цикла 2 пишет через `HouseOccupancyWriter` и сам решает, что
делать с `23P01` (конфликт фида).

---

## §37.6. Цена, деньги, возврат, доступность — чистые функции (R37-7)

Все — `Services/Stays/` (статические, без EF и HTTP), каждая покрыта табличными юнит-тестами **и** векторами
`contracts/cycle37/stay-vectors.json`; фронт держит TS-двойники в `dom/src/utils/` с тем же файлом векторов (как
`order-money-vectors.json` цикла 23). При расхождении прав сервер — фронт показывает предварительный расчёт только до
ответа `quote`. В векторах раздела `stay` поле кейса `settings` **дополняет** базовые настройки, `occupancies` —
**заменяет** базовые периоды.

### §37.6.1 `HousePricing.PriceFor(house, periods, date) → int?`

- `Constant` → `ConstantPriceRub`.
- `ByDates` → однодневный период на дату, если есть; иначе длинный период, содержащий дату; иначе `null` («Нет цены»).
- Цена ночи — по дате **начала** ночи (Р6).

### §37.6.2 `StayRules.CheckStay(input) → StayCheck` — доступность и правила длительности

Вход: даты, сегодня по поясу компании, настройки §4.3, занятые периоды дома (уже без истёкших удержаний), флаг «ручная
бронь». Порядок отказов (первый найденный, коды — `API_CONTRACT_CYCLE37.md` §37.25):
`InvalidDates` (выезд ≤ заезда) → `CheckInInPast` → `SameDayNotAllowed` → `BeyondHorizon` (последняя **ночь** > сегодня +
горизонт − 1) → `MaxNightsExceeded` → `DatesUnavailable` (пересечение) → `MinNightsNotMet`.
- Разрыв (§4.3): бронь короче минимума допустима, только если `AllowGapFill` и **есть период, оканчивающийся ровно в дату
  заезда, и период, начинающийся ровно в дату выезда**. Промежуток у «сегодня» и у горизонта разрывом не считается —
  выполняется само: это не периоды занятости.
- Ручная бронь: минимум, максимум и горизонт не действуют (US-37-24), пересечение — действует.
- Отдельно `GuestRules.Check(adults, children, dogs, needCot, house)` → `TooManyGuests` / `DogsNotAllowed` /
  `CotNotAvailable`; число доп. мест = `max(0, гостей − вместимость)`.
- «Нет цены» — проверка `HousePricing` после правил дат → `NoPriceForNights`.

### §37.6.3 `StayMoney.Quote(...) → StayQuote`

```
Ночи        = Σ PriceFor(ночь)                         (любая ночь без цены → отказ NoPriceForNights)
Доп. места  = extraBeds × ExtraBedPriceRub × nights
Собаки      = dogs × DogFeeRub × nights
Манеж       = needCot ? CotFeeRub × nights : 0         (строка есть и при 0 — «бесплатно»)
Итог        = Σ строк
Предоплата  = (Σ строк с PrepayEligible × PrepayPercent + 50) / 100   (целочисленно; половина — вверх; eligible только «Ночи»)
К оплате при заселении = Итог − Предоплата
```

Результат — строки (для `StayBookingCharges`), цены ночей, итог, предоплата, остаток, `averageNightRub = (Ночи + n/2) / n`
целочисленно (для каталога), `firstNightRub`.

### §37.6.4 `StayRefund.Compute(...) → StayRefundView` (ЮР-1, Т37-01)

Вход: статус брони, шаблон (снимок), `PrepayRub`, `firstNightRub` (из `NightPricesJson[0]`), дата заезда, время заезда
(снимок), пояс (снимок), момент отмены, кто отменяет.

| Случай | Результат |
|---|---|
| статус `Held` или `PrepayRub = 0` (денег нет) | `kind = NothingPaid`, сумма 0, текст «Бронь не оплачена — отмена без последствий» |
| отменяет владелец (любой активный статус) | `kind = Full`, `refundAtLeastRub = PrepayRub` |
| `NoDeductions` | `Full` |
| `Standard`, момент < 00:00 даты заезда по поясу брони | `Full` |
| `Standard`, момент ≥ 00:00 даты заезда | `kind = Partial`, `refundAtLeastRub = PrepayRub − min(PrepayRub, firstNightRub)`, `maxDeductionRub = min(...)` |
| `Flexible`, момент < время заезда в дату заезда | `Full` |
| `Flexible`, момент ≥ время заезда | `Partial` (та же формула) |

`AwaitingPaymentCheck` и `Confirmed` считаются оплаченными. Сервер отдаёт **сумму и готовый текст** «К возврату не меньше
X ₽…» (`StaysTexts`); фронт формулировок не сочиняет. Тексты шаблонов для гостя — правовой ключ `StayCancellationTerms`
(запасной текст — `StaysTexts`, по черновику §15.3 обзора). Слова «задаток / невозвратный / депозит» запрещены — юнит-тест
сканирует `StaysTexts` и запасные тексты фронта. Значения шаблонов — **конфигурация** `Stays:CancellationPolicies`
(граница «00:00 дня заезда» / «время заезда», `maxDeductionNights: 1`), но валидатор конфигурации на старте
(`DeploymentSafetyChecks.ValidateStaysPolicies`) **не пропускает** удержание больше одной ночи и границу раньше дня заезда —
черновики SPEC нельзя включить даже конфигом.

### §37.6.5 `StayStateMachine` — таблица §4.7 SPEC

| Действие | Из | В | Кто |
|---|---|---|---|
| (создание гостем) | — | `Held` (предоплата > 0) / `Confirmed` (= 0, Q3) | гость |
| (создание вручную, P1) | — | `Confirmed` | владелец, управляющий |
| `AttachProof` (первый файл) | `Held` (до таймера) | `AwaitingPaymentCheck` | гость по ссылке |
| `AttachProof` (следующие, до 3) | `AwaitingPaymentCheck` | (тот же) | гость |
| `Expire` | `Held` (после таймера) | `ExpiredUnpaid` | система |
| `ConfirmPayment` | `AwaitingPaymentCheck` | `Confirmed` | владелец, управляющий |
| `RejectPayment` (причина обязательна) | `AwaitingPaymentCheck` | `PaymentRejected` | владелец, управляющий |
| `CancelByGuest` | `Held`, `AwaitingPaymentCheck`, `Confirmed` — до момента заезда | `CancelledByGuest` | гость по ссылке |
| `CancelByOwner` (причина обязательна) | `Held`, `AwaitingPaymentCheck`, `Confirmed` | `CancelledByOwner` | владелец, управляющий |

Конечные: `ExpiredUnpaid`, `PaymentRejected`, `CancelledByGuest`, `CancelledByOwner`. «Завершена» — вычисляемый вид
(`Confirmed` и момент ≥ дата выезда + время выезда по поясу брони). `AvailableActions(status, now, …)` отдаётся в DTO —
фронт не вычисляет доступность кнопок. Подписи статусов и журнал — `StaysTexts` (сервер собирает).

### §37.6.6 Публикация дома — `HousePublishRules.Check(...)`

Отказы в порядке: архив → `HouseArchived`; нет цены (`Constant` без `ConstantPriceRub` или `ByDates` без периода,
оканчивающегося не раньше сегодня) → `NoPrice`; нет `ObjectKind` → `ObjectKindRequired`; `ObjectKind ∈ {GuestHouse,
OtherAccommodation}` и пустой `RegistryNumber` → `RegistryNumberRequired`; нет заверения в запросе (или `accepted ≠ true`)
→ `AttestationRequired`; лимит тарифа → **402** строкой (US-37-10).
Трактовка ЮР-2: «дома без номера публикуем» = дом вида **«жилое помещение»** публикуется без номера под заверением;
гостевой дом / иное средство размещения требует номер (иначе заверение «либо номер реестра» не имеет смысла). Если
заказчик имел в виду «публиковать любой вид без номера» — это одна строка в `HousePublishRules` (открытый вопрос §37.19 п. 1).
Изменение `ObjectKind`/`RegistryNumber`/`RegistryUrl` у **опубликованного** дома требует нового заверения в том же
запросе, иначе 409 `AttestationRequired` (инвариант §37.2.8-7).

---

## §37.7. Жизненный цикл брони, таймеры, фоновые задачи (A8)

### §37.7.1 Создание брони гостем — порядок шагов

Коды и тексты каждого шага — `API_CONTRACT_CYCLE37.md` §37.24–§37.25. Почему порядок такой:
1. Модель запроса (400 строки).
2. Дом по `houseId`: нет / не опубликован (в том числе архив) / компания не «Дома» → 404. Компания заблокирована → 409
   `NotAcceptingBookings` (`CompanyBlocked`).
3. **Идемпотентность** `(CompanyId, IdempotencyKey)` → 200 с существующей бронью (до капчи — повтор не упирается в
   одноразовую капчу).
4. Гость: вошедший — телефон аккаунта (поле тела игнорируется), имя редактируемое; аноним — капча `CaptchaService`,
   `PhoneNormalizer.TryNormalizeRussian`.
5. `StaysBookingGate.Evaluate` (тариф, лимит домов, реквизиты, исполнитель) → 409 `NotAcceptingBookings` с `reasonCode`.
6. Транзакция → lock `stay-guest-phone:{phone}` → `StayPhoneThrottle` (2 удержания на платформе, 1 в компании, 10 броней
   в сутки на номер; значения — `Stays:PhoneLimits`) → 429 строкой.
7. Lock `stay-house:{houseId}` → ленивое снятие (§37.5.2) → занятость дома (± соседние периоды) → `StayRules.CheckStay`,
   `GuestRules`, `StayMoney.Quote` → любые отказы 409 JSON; `expectedTotalRub ≠ TotalRub` → 409 `PriceChanged` с новым
   расчётом. Бронь **не создаётся** с другими данными.
8. Токен, снимки (цены ночей, строки, правило отмены, время, пояс, реквизиты, исполнитель, версии текстов, согласие
   мессенджера), статус `Held` (`HoldExpiresAtUtc = now + HoldMinutes`) или `Confirmed` (предоплата 0).
9. `HouseOccupancyWriter.Add` → `StayBookingEventLog.Append(Created)` (+ревизия, + планировщик уведомлений) →
   `SaveChanges` → commit. `23P01` → 409 `DatesUnavailable`; гонка идемпотентности (уникальный индекс) → перечитать и
   вернуть 200.

p95 < 800 мс: ~10 SQL-команд, lock дома держится только шаги 7–9.

### §37.7.2 Страница брони и автообновление

`GET /api/stays/bookings/public/{token}` — опрос каждые **15 с** (SPEC: ≤ 30 с), немедленно при `visibilitychange`.
Обратный отсчёт таймера — от `holdExpiresAtUtc` и `serverTimeUtc` ответа (смещение часов телефона вычитается).
Шахматка: `GET …/board?sinceRevision=` — `changed: false` по `BookingsRevision` (один PK-lookup), опрос 15 с.

### §37.7.3 Фоновые задачи

| Задача | Полоса, период | Что делает | Однократность |
|---|---|---|---|
| `stays-hold-expiry` | `realtime`, 15 с, `MaxRunMinutes` 1 | брони `Held` с `HoldExpiresAtUtc ≤ now` пачками по 50: на каждую — транзакция, lock дома, `StayHoldExpirer` | условный `UPDATE` §37.5.3; задержка ≤ 15 с + тик 1 с ≪ 1 мин (US-37-21) |
| `stays-scheduled-messages` | `main`, 60 с | (а) «осталось 10 минут» — `Held`, без файлов, `HoldMinutes ≥ 20`, `now ≥ HoldExpiresAt − 10 мин`; (б) напоминание накануне — `Confirmed`, настройка вкл., местное время ≥ (заезд − 1 день) 18:00 и < момента заезда; (в) информация к заселению — §37.7.4 | `UPDATE … SET <отметка> = @now WHERE "Id" = @id AND <отметка> IS NULL` + постановка в той же транзакции; ключи идемпотентности строк очереди уникальны |
| `stays-guest-push-dispatch` | `realtime`, 10 с | рассылка `StayGuestPushNotifications` (форма `CustomerOrderPushDispatchTask`: in-flight метка, `WebPushResponseClassifier`, `Gone` → удаление подписки) | статус строки |

Регистрация поимённо в `ApplicationServicesExtensions`, секции `ScheduledTasks:<имя>` в `appsettings.json`, в
`appsettings.Testing.json` выключены (тесты вызывают `ExecuteAsync` напрямую с поддельными часами). Время «сегодня», 18:00
и 09:00 — по `TimeZoneIdSnapshot` брони. Пропущенный срок (сервер лежал) — сообщение уходит при первом проходе, если ещё
актуально (до момента заезда), иначе отметка ставится без постановки.

### §37.7.4 Информация к заселению — правило выпуска `CheckInInfoRelease.IsDue`

Выпуск = `Confirmed` ∧ есть текст (компании или дома) ∧ местное «сейчас» ≥ дата заезда + `CheckInInfoSendTime` ∧ < дата
выезда + время выезда. Выпускают: задача (в) **и** `ConfirmPayment` / создание с предоплатой 0, если срок уже наступил
(US-37-28). Выпуск = отметка `CheckInInfoReleasedAtUtc` + событие + постановка сообщения. С этого момента страница брони
показывает **текущие** тексты (изменение после выпуска видно на странице, повторно не отправляется).

---

## §37.8. Подтверждения оплаты: приём, хранение, отдача, удаление (A9, ЮР-6)

| Что | Решение |
|---|---|
| Где | `FileStorage.SavePrivateAsync(companyId, …)` → ключ `"<companyId>/<guid>.<ext>"`; приватный корень на машине в РФ (обзор §2: внешние хранилища за рубежом запрещены). Публичного URL нет |
| Типы | по сигнатуре байт, не по расширению/Content-Type: PDF (`%PDF-`), JPEG, PNG, WebP. HEIC/HEIF → 400 «Можно приложить PDF, JPEG, PNG или WebP» |
| Изображения | новый `ImageProfile.PaymentProof` (длинная сторона 2400 px, JPEG q85, без обрезки) через `ImageProcessor` — перекодирование снимает EXIF/GPS |
| PDF | хранится как есть, **не разбирается** сервером |
| Размер | `Stays:PaymentProofs:MaxFileBytes` = 10 МБ (новая перегрузка `ImageUploadService.ReadAndProcessAsync(file, profiles, maxBytes)`; глобальный `Uploads:MaxFileBytes` = 5 МБ не меняется); nginx dom `client_max_body_size 11M` |
| Количество | ≤ 3 на бронь (`Stays:PaymentProofs:MaxPerBooking`), 4-й → 409 `ProofLimitReached` |
| Частота | политика `stay-proof` (цепочка: 6 за 10 мин на токен **и** 20 в час на IP) |
| Отдача персоналу | `GET /api/stays/companies/{id}/bookings/{bookingId}/payment-proofs/{proofId}` — право `ViewBookings`; событие `PaymentProofViewed` (имя сотрудника снимком) — не чаще раза в 10 мин на пару (сотрудник, файл), чтобы журнал не засорялся предпросмотром |
| Отдача гостю | `GET /api/stays/bookings/public/{token}/payment-proofs/{proofId}` |
| Заголовки | всегда `Cache-Control: private, no-store`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: sandbox`. PDF — `Content-Type: application/pdf`, **`Content-Disposition: attachment`**. Изображения — `inline` (окно предпросмотра) |
| Фронт | изображение — `useAuthedImage` (blob, токен в заголовке), PDF — скачивание blob. Ссылки с JWT в `<a href>` нет |
| Удаление по сроку | правило retention `stay-payment-proofs`: `now > max(дата выезда + время выезда по поясу брони, TerminalAtUtc) + Retention:StayPaymentProofDays (90)` → файлы удаляются, `StorageKey = null`, `PurgedAtUtc`, у брони `PaymentProofsPurgedAtUtc`, событие `PaymentProofsPurged`. Сумма, кто и когда подтвердил — остаются (ЮР-6). Сухой прогон — общий `DryRun` задачи `data-retention` |
| Удаление аккаунта гостя | файлы его броней удаляются сразу (§37.13.1) |
| Демо | `FileStorage.ClearAllFiles` уже чистит приватный корень |

---

## §37.9. Роли и права (A10, US-37-08)

`CompanyMember.Role = Master` + `StaffPosition` (`Manager`/`Housekeeper`); владелец — `CompanyOwner`. Отдельная роль
Identity отвергнута по причинам §392.1 цикла 23 (сид ролей, `IdentityRoleSync`, места, админка). Побочный эффект
признан: горничная с Identity-ролью `Master` на ezbook видит пустой «Кабинет» и строку «Ваши дома — на dom».

**`Services/Stays/StaysAccess.cs` — единственная таблица прав** (`StaysPermission`), членская половина —
`CompanyMembership` (проверка на каждом запросе по БД → удалённый теряет доступ сразу).

| Разрешение | Что | Владелец | Управляющий | Горничная | SA |
|---|---|---|---|---|---|
| `ManageCompany` | настройки, реквизиты, исполнитель, предоплата, отмена, тариф, персонал, slug, уведомления | ✓ | — | — | ✓ |
| `ManageHouses` | создание, архив, удаление, цены и периоды, доп. места, вид объекта и реестр, публикация, slug дома, порядок | ✓ | — | — | ✓ |
| `EditHouseContent` | описание, фото, удобства, адрес, ссылки на карты, текст к заселению дома | ✓ | ✓ | — | ✓ |
| `ViewBookings` | шахматка, список, карточка, журнал, контакты гостей, файлы подтверждений | ✓ | ✓ | — | ✓ |
| `ManageBookings` | подтвердить/отклонить оплату, отменить, ручная бронь | ✓ | ✓ | — | ✓ |
| `ManageBlocks` | блокировки дат | ✓ | ✓ | — | ✓ |
| `ViewSchedule` | график уборок и заездов | ✓ | ✓ | ✓ | ✓ |
| `ViewCabinet` | карточка компании в кабинете (без реквизитов), QR, ссылка | ✓ | ✓ | — | ✓ |

- Горничная в `GET /schedule` видит: дом, даты и время, «выезд и заезд в один день», имя гостя, взрослые/дети, доп.
  места, собаки, манеж, время прибытия; **комментарий — только при `HousekeeperSeesGuestComment`** (ЮР-5). Не видит:
  телефон, суммы, файлы, реквизиты, журнал, статус оплаты (только пометку «оплата не подтверждена»). Форма графика одна
  для всех ролей и этих полей **не содержит вовсе** (не «пустые поля») — проверяется контрактным тестом.
- Владельческие действия — `[RequiresOwnerTerms]` (451 владельческого гейта). Сотрудник без права — **403 пустым
  телом**; не участник — 404.
- Персонал не лимитируется тарифом; техпотолок 30 (`Stays:MaxStaffPerCompany`).

---

## §37.10. Тарифы и триал (A11, блок K)

### §37.10.1 Линейка «Дома» — данные миграции

| Id (константа `StaysPlans.*`) | Name | Price | `MaxHouses` | `IsPublic` | `AllowNotificationChannel` |
|---|---|---|---|---|---|
| `OneHouseSeedId` | «Один дом» | 200 | 1 | true | true |
| `UpToThreeSeedId` | «До 3 домов» | 500 | 3 | true | true |
| `UnlimitedSeedId` | «Без ограничения» | 1000 | null | true | true |
| `TrialSeedId` | «Пробный период» | 0 | null | false | false |

Все — `Line = Stays`, `IsSystemFree = false`, `IsSystemTrial = false`, `AllowOnlineBooking = false` (салонный флаг для этой
линейки не читается), `MaxCompanies = null`, `MaxEmployees = null`. Цены и лимиты администратор меняет без деплоя.
«Публичность» — только в списке тарифов кабинета владельца; на `/api/pricing` линейка не попадает (L10).

### §37.10.2 Триал

- 14 дней (`Stays:TrialDays`), один раз на подтверждённый номер **в линейке** «Дома» и один раз на аккаунт в линейке.
  Триал «Записи» на это не влияет и наоборот.
- `StaysTrialService.GrantAsync` (по образцу `TrialActivationService`, переиспользует `TrialPhoneKey`, `TrialOptions`,
  порядок проверок §335.2): тариф `TrialSeedId` активен → у аккаунта нет действующей подписки «Дома» → `TrialGrants`
  с `Line = Stays` нет → номер владельца подтверждён (`VerifiedPhones`; подсистема выключена → честный отказ) → ключ номера
  не занят в `TrialPhoneRegistrations` с `Line = Stays` → пишет `StaysSubscription { PlanConfigId = TrialSeedId,
  PaidUntil = now + 14 д }`, `TrialGrant { Line = Stays, TermsVersion, TermsTextSha256 }`, `TrialPhoneRegistration { Line = Stays }`.
- Условия триала (Т1 цикла 18): текст `TrialTermsRegistry` с отдельной редакцией «Дома» **[legal L10]** — до вычитки
  черновая; `GET /api/stays/trial` отдаёт текст и версию, создание компании и кнопка «Активировать» присылают
  `trialTermsVersion`.
- **Активация при создании первой компании** (SPEC K): `POST /api/stays/companies` с `trialTermsVersion` → после коммита
  компании вызывается `GrantAsync`; отказ триала не отменяет создание, ответ содержит `trial: {granted, refusalCode,
  message}`. Кнопка в «Подписке» — `POST /api/stays/trial`.
- Существующий салонный триал: 6 чтений `TrialGrants`/`TrialPhoneRegistrations` получают `Line == Services`
  (`TrialActivationService` ×4, `TrialStateReader`, `AdminAccountDtoBuilder`). Регресс — существующие тесты цикла 18 + новый
  «выданный триал «Домов» не мешает триалу «Записи» на том же номере и наоборот».

### §37.10.3 `StaysBookingGate.Evaluate` — одна чистая функция «компания принимает брони»

```
Evaluate(company, settings, plan, accountPublishedHouses) → (Accepting, ReasonCode?, ReasonText?)
```
Порядок причин: `CompanyBlocked` (`!IsActive`) → `NoPlan` (нет действующей подписки, триал истёк — Q2) →
`OverHouseLimit` (опубликованных неархивных домов **всех** компаний «Дома» аккаунта > `MaxHouses`) →
`NoPaymentDetails` (предоплата > 0 и пусто `PaymentDetails`) → `NoProviderInfo` (Т37-03, при предоплате > 0: статус, имя,
ИНН, адрес; ОГРН у организации и ИП). Гостю — общий текст «Бронирование временно недоступно»; владельцу (кабинет,
чек-лист) — текст причины. Вызывают: страница дома, страница компании, каталог, `quote`, создание брони, кабинет.
Ручная бронь персонала гейт **не** проверяет (кабинет и брони работают при неоплаченном тарифе — Q2). Код
`HouseNotPublished` в перечислении используется только чек-листом кабинета (публичные маршруты неопубликованного дома
отвечают 404).

**Почему не `AllowOnlineBooking`:** это флаг салонной линейки с бесплатным уровнем, у «Домов» бесплатного уровня нет, а
закрытие зависит от числа опубликованных домов на аккаунте — понятия, которого нет у салонов.

### §37.10.4 Подписка, заявки, админка

- `GET /api/billing/subscription?line=Stays` — та же форма `OwnerSubscriptionDto`, блок `stays: {housesPublished,
  maxHouses, isTrial, trialEndsAtUtc, warningLevel: None|TrialEnding3d|TrialEnding1d|Expired|NoPlan|OverLimit, text}`;
  `availablePlans` — активные тарифы линейки, кроме триала.
- `POST /api/billing/subscription/request` с `line = Stays` — существующий механизм одной заявки на аккаунт.
- Админ: `PUT /api/admin/billing-accounts/{id}/subscription` с `line = Stays` пишет `StaysSubscriptions`; карточка
  аккаунта — блок `staysSubscription`; план — поле `maxHouses`.
- Плашки «за 3 дня / за 1 день до конца триала» — вычисляются в DTO, фоновой задачи нет.
- Публикация сверх лимита — 402 строкой (`BillingTexts.HouseLimitReached(planName, max)`); снижение тарифа ничего не
  снимает с публикации, только закрывает приём (гейт) и запрещает публиковать новые.

---

## §37.11. Каталог, город, справочник удобств (A12, A13, US-37-25, US-37-26)

### §37.11.1 Город

Компания «Дома» создаётся с городом «Шерегеш» (сервер ищет по `(Name, Region)` из `Stays:CatalogCity`; нет строки —
503 «Справочник городов не готов», fail-fast на старте не нужен: миграция её сеет). `cityId` в запросе не принимается.

### §37.11.2 Каталог `GET /api/stays/public/catalog`

- **База** (кеш `IMemoryCache` 30 с, ключ — город): опубликованные неархивные дома компаний `Stays`, `IsActive`,
  `ShowInPublicListing`, гейт компании = принимает, с полями карточки, режимом цены, константой и периодами цен, не
  закончившимися до «сегодня − 1» (≤ 500 домов × десятки периодов).
- **Без дат:** все дома базы, цена «от» = min(константа, min цены незакончившихся периодов). Сортировка по цене ↑, затем
  по названию; пагинация `PagedResult` (20).
- **С датами:** один запрос занятости `SELECT "HouseId", "StartDate", "EndDate" FROM "HouseOccupancies" WHERE "HouseId" =
  ANY(@ids) AND "ReleasedAtUtc" IS NULL AND ("HoldExpiresAtUtc" IS NULL OR "HoldExpiresAtUtc" > @now) AND "StartDate" <=
  @checkOut AND "EndDate" >= @checkIn` (включая соседей, касающихся дат, — они нужны правилу разрыва) по индексу
  `(HouseId, EndDate)`; в памяти — `StayRules.CheckStay`, `GuestRules`, `StayMoney.Quote` по каждому дому. Фильтр цены — по
  средней цене ночи. Оценка: 500 домов × ≤ 30 ночей — единицы миллисекунд CPU + 1 индексный запрос → p95 ≪ 500 мс.
- Удержанные даты в каталоге — **заняты** (SPEC); истёкшие удержания — свободны. Расхождение кеша базы (30 с) не даёт
  двойной брони: создание всегда перепроверяет по БД под замком.
- Страница компании `GET /api/stays/public/companies/{slug}` — та же логика по домам одной компании, без кеша.
- Замер p95 на 500 домах — задача BE-37-7 (сид через HTTP API на локальном стенде, по образцу `tools/bench/cycle25`).

### §37.11.3 Справочник удобств

`HouseAmenity` (append-only, бит маски): `Wifi`, `Kitchen`, `Parking`, `Sauna` (баня при доме), `Bbq` (мангал),
`WashingMachine`, `Tv`, `Fireplace`, `GearDryer` (сушилка для снаряжения), `SkiStorage`, `LiftTransfer`, `Dishwasher`,
`Terrace`. Подписи — сервер: `GET /api/stays/public/amenities` → `[{code, label}]` (дизайнер может поправить подписи без
изменения кодов).

---

## §37.12. Уведомления (A14, US-37-29, US-37-30)

### §37.12.1 Единая точка «что произошло»

`StayBookingEventLog.AppendAsync` — единственный писатель журнала и ревизии — в конце вызывает
`StayNotificationPlanner.OnEventAsync(booking, event)`; задача `stays-scheduled-messages` вызывает
`StayNotificationPlanner.OnScheduledAsync(booking, kind)`. Планировщик: чистая часть `StayNotificationPlan.For(...)`
(какие типы и кому — юнит-тест всей таблицы) + часть с БД (кто участники, какие подписки, каналы), `SaveChanges` не
вызывает. Первой строкой — `ShowcaseOutboundGuard.IsSuppressed(company, DemoMode:Enabled)` (как у заказов).

| Событие | Персоналу (push + MAX) | Гостю (мессенджер, если `NotifyByMessenger` ∧ `GuestMessengerEnabled`; web-push, если подписан ∧ `GuestWebPushEnabled`) |
|---|---|---|
| `Created` (`Held` или `Confirmed` при 0 %) | `StaffStayCreated` | `StayGuestCreated` (ссылка, сумма предоплаты, реквизиты, срок) — **реквизиты только в мессенджер этой брони** (Т37-04), в push — нет |
| `PaymentProofUploaded` (первый файл) | `StaffStayPaymentProofUploaded` | — |
| `CancelledByGuest` | `StaffStayCancelledByGuest` | — |
| `HoldExpired` | — | `StayGuestHoldExpired` |
| `PaymentConfirmed` | — | `StayGuestConfirmed` |
| `PaymentRejected` | — | `StayGuestPaymentRejected` |
| `CancelledByOwner` | — | `StayGuestCancelledByOwner` |
| (задача) за 10 мин до конца таймера | — | `StayGuestHoldExpiring` |
| (задача) накануне 18:00 | — | `StayGuestArrivalReminder` |
| `CheckInInfoReleased` | — | `StayGuestCheckInInfo` (§37.12.3) |

- **Новые `NotificationType`** (append, значения 16–26): `StaffStayCreated = 16`, `StaffStayPaymentProofUploaded = 17`,
  `StaffStayCancelledByGuest = 18`, `StayGuestCreated = 19`, `StayGuestHoldExpiring = 20`, `StayGuestHoldExpired = 21`,
  `StayGuestConfirmed = 22`, `StayGuestPaymentRejected = 23`, `StayGuestCancelledByOwner = 24`,
  `StayGuestArrivalReminder = 25`, `StayGuestCheckInInfo = 26`. Битовая маска салона (`EnabledTypeMask`, `int`) для них
  **не используется**: `NotificationTypeCatalog.IsBookingType` их не включает; юнит-тесты «салонные `enabledTypes` не
  изменились» и «ни одно значение ≥ 31 не попадает в сдвиг маски». Ёмкость маски: занято 0–26 из 31 — цикл 2 (услуги)
  должен уложиться в 4 значения или ввести отдельную маску.
- `NotificationTexts.TypeText` — тексты для всех новых (существующий тест перебирает перечисление).
- Новые `NotificationReason` (append): `StayMessageOutdated`, `StayGuestPushDisabled`, `StayMessengerDisabled`.

### §37.12.2 Персоналу

- Получатели: участники компании `CompanyOwner` и `Master` с `StaffPosition = Manager`; горничной уведомления не идут.
  Диспетчеры перепроверяют членство (существующая логика) **и** должность для строк с `StayBookingId`.
- Push — `StaffPushNotification` (+`StayBookingId`), на все устройства пользователя (правило цикла 33), ключ
  `{type}:{eventId}:{userId}:{subscriptionId}`. Тело: `{title: "<Компания>", body: "Новая бронь · «Кедр» · 30 дек – 2 янв ·
  3 ночи", tag: "s-<bookingId>", url: "https://dom.ezbook.ru/cabinet/<companyId>/bookings/<bookingId>"}` — **без имени и
  телефона** гостя. Абсолютный url — `PublicSiteLinks.StaysCabinetBookingUrl`.
- MAX — `StaffMaxMessage` (+`StayBookingId`), флаг `StaysSettings.StaffMaxEnabled`, eligibility — §37.3.2. На бою
  `STAFFMAX_ENABLED=false` (R37-5).
- Включение push персонала — существующий `CompanyNotificationSettings.StaffPushEnabled` компании (маршрут —
  `API_CONTRACT_CYCLE37.md` §37.27.6).

### §37.12.3 Гостю

- **Мессенджер** — `OutboundNotification` (+`StayBookingId`), постановка `StayMessageScheduler` через существующие
  `NotificationGate` (опт-аут, `PaidNotificationNumbers`, канал, оплата), `NotificationRouting`, ключ
  `{type}:stay:{eventId|marker}:{transport}`; `VisitStartUtc` = постановка + 2 ч → `Expired` с `StayMessageOutdated`.
  Текст — `StayNotificationTexts` (фиксированный, не редактируется), ссылка — `PublicSiteLinks.StayBookingPageUrl`,
  отписка — `PublicSiteLinks.UnsubscribeUrl`. Галочка `StayMessengerConsent` — отдельная, по умолчанию выключена (Т37-12).
- **Web-push со страницы брони** — `StayGuestPushSubscriptions`/`StayGuestPushNotifications`. Тело — **без ПДн и
  адресов** (Т37-08): `{title: "ezbook · Дома", body: "Статус вашей брони изменился", tag: "sg-<bookingId>", url: "/b/<token>"}`
  (для «осталось 10 минут» — «Осталось 10 минут, чтобы приложить подтверждение оплаты»; для заселения — «Информация к
  заселению готова»). Токен — только внутри зашифрованного payload. Подписка недоступна: push выключен компанией или
  платформой, бронь в конечном статусе.
- **Информация к заселению (ЮР-4):** по умолчанию (`CheckInInfoSendFullText = false`) мессенджер получает «Информация к
  заселению готова: <ссылка>»; при `true` — текст компании + текст дома целиком. Push — всегда только уведомление-ссылка.
  Полный текст — всегда на странице брони после выпуска.
- Если канал недоступен — гость видит всё на странице брони; бронь от уведомлений не зависит. На бою сейчас
  `NOTIFICATIONS_PROVIDER=logging` и `WEBPUSH_STAFFPUSH_PROVIDER=logging` (R37-5).

---

## §37.13. Персональные данные, retention, правовые тексты (US-37-32, обзор §8, §14–§16)

### §37.13.1 Права субъекта

- **Выгрузка** (`SubjectDataExporter`): секция `stayBookings` — брони аккаунта (`GuestUserId`) и, **только при
  подтверждённом номере**, гостевые брони на этот номер (`SubjectScope`, маркер `// SUBJECT-PHONE-GATE:`). Поля: компания,
  дом, даты, статус, гости, опции, суммы, имя, телефон, комментарий, причина, список подтверждений оплаты (дата, тип,
  размер, удалено ли) и `bookingUrl`, по которому файл открывается. Файл «как файл» в JSON-выгрузку не встраивается —
  открытый вопрос к юристу (§37.19 п. 7). Журнал — только события, видимые гостю, без имён сотрудников.
- **Удаление аккаунта** (`AccountDeletionService`): брони аккаунта и гостевые на подтверждённый номер → `GuestUserId`,
  `GuestName`, `GuestPhone`, `Comment`, `ArrivalTime` = null, `PersonalDataErased = true`; файлы подтверждений удаляются
  (`PurgedAtUtc`); имена в событиях гостя → «Удалённый пользователь». Даты, суммы, статус, занятость — остаются (календарь
  владельца не ломается). Активные брони не отменяются.
- **Отзыв согласия** `ProviderDelivery` — как у заказов: `NotifyByMessenger = false` у активных броней номера, стоящие
  строки диспетчер пропускает с `StayMessengerDisabled`.
- Сторож `SubjectPhoneGateInvariantTests` — дописать шаблон `GuestPhone ==`.
- Витрина: выборки «по номеру» исключают компании `IsShowcase` (как у заказов, хотя витрины «Домов» нет).

### §37.13.2 Маскирование

`LoggingExtensions.MaskSensitiveRequestPath` — префикс `/api/stays/bookings/public/`; nginx dom — свои `map`/`log_format`
для `/b/<token>`, `/api/stays/bookings/public/<token>` и `Referer` с `/b/` (§37.15.1). Телефон гостя по ссылке —
`PhoneDisplayMask`; полный — только в DTO персонала с `ViewBookings`.

### §37.13.3 Правила retention (файл в `Services/Retention/Rules/` + регистрация поимённо; значения — конфигурация)

| Правило | Что | Срок (конфиг) |
|---|---|---|
| `stay-payment-proofs` | файлы подтверждений, §37.8 | `Retention:StayPaymentProofDays` = 90 от max(выезд, конечный статус) |
| `stay-unpaid-personalization` | брони `ExpiredUnpaid` → обезличивание (поля §37.13.1) | `Retention:StayUnpaidBookingDays` = 30 от `TerminalAtUtc` |
| `stay-booking-personalization` | прочие брони в конечном статусе или после выезда → обезличивание | `Retention:StayBookingPersonalDataDays` = 1095 от max(выезд, конечный статус) |
| `stay-booking-events` | журнал брони — по образцу `BookingEventRule` | `Retention:StayBookingEventDays` = 1095 от события |
| `stay-guest-push-subscriptions` | подписки push гостя после конечного статуса/выезда | `Retention:StayGuestPushSubscriptionDays` = 7 |
| `stay-guest-push-notifications` | строки очереди push гостя | `Retention:StayGuestPushNotificationDays` = 90 |

Сообщения мессенджера — существующие правила `NotificationBodyRedactionRule`/`NotificationMetadataDeletionRule` (они по
таблице, не по типу). Блокировки и их журнал ПДн гостя не содержат — без правила. Сухой прогон — общий `DryRun: true` в
репозитории (на бою — решение оператора, как для остальных правил).

### §37.13.4 Правовые тексты (Т37-13) — ключи `LegalTextKey` **вне `All`**, запасной текст на фронте

| Ключ | Где | Запасной текст обязан содержать |
|---|---|---|
| `StayBookingNotice` | под кнопкой «Забронировать» | ссылки на условия бронирования, `/terms`, `/privacy`; «проверьте номер: на него придёт ссылка на бронь» |
| `StayBookingTerms` | страница дома («условия бронирования»), страница брони | — (до текста — нейтральная строка со ссылками) |
| `StayCancellationTerms` | страница дома, форма, страница брони, диалог отмены | тексты трёх шаблонов §15.3 (до вычитки — из `StaysTexts`) |
| `StayPaymentProofNotice` | у реквизитов и кнопки «Приложить подтверждение оплаты» | «не прикладывайте фото паспорта» |
| `StayGuestCommentNotice` | под комментарием | запрет здоровья, паспорта, карт |
| `StayMessengerConsent` | галочка мессенджера | — |
| `StayCheckInInfoOwnerNotice` | у переключателя «Отправлять текст целиком» | — |
| `StayPaymentRequisitesOwnerNotice` | у поля «Реквизиты» | — |
| `StayOwnerCancelNotice` | у «Отменить бронь» и «Отклонить оплату» в кабинете (§15.9 обзора) | — |
| `StayPublicContactsNotice` | у адреса и телефона дома (Т37-14) | — |
| `StayTouristTaxNotice` | под ценой на странице дома, в форме брони, на странице брони (Т37-11) | «цена без туристического налога» |
| `StayMigrationOwnerNotice` | настройки компании, «График», поле комментария блокировки, поля текстов к заселению | запрет паспортных данных |
| `StayRegistryOwnerNotice` | у «Вид объекта»/«Номер в реестре», диалог публикации | — |
| `CompanyPhotoPeopleNotice` (существующий) | загрузка фото дома | — |

- Снимок версий в брони: `BookingNoticeVersion`, `BookingTermsVersion`, `CancellationTermsVersion`,
  `MessengerConsentVersion`, плюс `ConsentPrivacyVersion`/`ConsentTermsVersion` (как гостевая запись).
- Шаг CI «маркер ТРЕБУЕТСЯ ТЕКСТ» не должен срабатывать: запасные тексты — нейтральные.
- `/data-request` и правовые алиасы — на dom (L9). Правки документов D1–D4 (§14 обзора) — задача legal-counsel/заказчика,
  **не кода**; Т37-15: до прихода первого реального владельца.

---

## §37.14. Третий фронтенд — dom.ezbook.ru (A5, US-37-02…05)

### §37.14.1 Решение: третье приложение в том же npm-пакете

```
frontend/
├── src/                        ezbook — общий код (без переноса файлов, кроме §37.14.3)
├── goods/                      goods.ezbook.ru — без изменений, кроме §37.3.3 п. 2–3
├── dom/                        🆕 dom.ezbook.ru
│   ├── index.html              <div id="root"> (на него смотрит смоук)
│   ├── public/                 favicon.svg/.ico, apple-touch-icon.png, icon-192/512.png, manifest.webmanifest, sw.js
│   └── src/                    main.tsx, DomApp.tsx, pages/, components/, api/, hooks/, utils/, types.ts
├── vite.dom.config.ts          🆕 root: dom/, outDir: ../dist-dom, порт SB_DOM_WEB_PORT (5175), алиасы @dom, @
├── tsconfig.dom.json           🆕 include: dom/src, src
├── tailwind.dom.config.js      🆕 presets: [ezbook], content из shared-sources.js
├── shared-sources.js (+.d.ts)  🆕 обобщение goods-shared-sources.js: SHARED_EZBOOK_PAGES, SCANNED/MARKUP_FREE dirs, FILES,
│                               appTailwindContent('goods'|'dom'), appAllowedPagesRegex('goods'|'dom')
├── goods-shared-sources.js     становится реэкспортом из shared-sources.js (импорты goods не меняются)
├── scripts/merge-site-dist.mjs 🆕 node scripts/merge-site-dist.mjs goods dom → dist/__goods, dist/__dom
│                               (заменяет merge-goods-dist.mjs; вызывается только из build:release)
├── vitest.config.ts            include + dom/src/**/*.test.{ts,tsx}
└── eslint.config.js            границы импорта для dom (§37.14.4)
```

Почему не отдельный пакет — §399.1 цикла 23 (две копии React, два lock-файла).

### §37.14.2 Что dom переиспользует (без копий)

`api/client.ts`, `store/{authStore,legalStore,ownerGateStore}`, `queryClient`, `components/ui/*`, `components/legal/*`
(`ConsentGate`, `LegalUpdateBanner`, `OwnerTermsGateModal`, `LegalGuard`), `components/phoneVerification/*` +
`usePhoneVerification`, `components/booking/SmartCaptcha.tsx`, `components/company/{CompanyMapLinks, CompanyLogoMark,
CompanyPhotoGallery, CompanyPhotosSection, usePhotoBatchUpload, PublicAddressNotice}` (галерее и секции фото добавляется
проп-адаптер API: `{list, upload, remove, reorder}` — для дома; поведение салона и магазина не меняется),
`components/push/DevicesAndNotificationsSection`, `hooks/{useWebPush, useOverlayDismiss, useLegalText, useDebouncedValue,
useAuthedImage}`, `utils/{money, phone, dateFormat, timezone, *Error, pushWorker, staffPushTexts, returnTo}`, страницы
`LegalDocumentPage`, `SubjectRequestPage`, `ConsentsPage`, `LoginPage`, `RegisterPage` (`returnTo`), `NoticesPage`,
`BillingPage` (как в goods).

### §37.14.3 Что переезжает из goods в общий код (единственные переносы цикла)

| Файл goods | Куда | Зачем |
|---|---|---|
| `goods/src/hooks/useAppUpdate.ts`, `goods/src/components/UpdateBanner.tsx` | `src/components/sites/` | плашка «Доступна новая версия» (цикл 34) нужна и dom |
| `goods/src/components/staffMax/*` | `src/components/staffMax/` | подключение MAX персонала — то же на dom |

Тесты переезжают вместе с файлами, ID тестов не меняются; `contracts/cycle36/test-areas.json` — префиксы обновить
(запись в реестре цикла 36 — «перенос», не удаление). Остальное, что похоже у goods (страница ссылки и QR, персонал,
подписка), dom пишет сам: в goods это страницы, а не компоненты, и их перенос дороже копии разметки.

### §37.14.4 Границы импорта

- `dom/src/**`: запрещены `@/App`, `@/pages/*` кроме `SHARED_EZBOOK_PAGES`, любые `goods/` и `@goods/*`.
- `goods/src/**`: запрещены `dom/`, `@dom/*` (дописать к существующим).
- `src/**`: запрещены `@dom/*` и пути в `dom/` (как `goods/`).
- Guard-тест `dom/src/sharedSources.guard.test.ts` (логика goods через `shared-sources.js`).

### §37.14.5 Маршруты dom (`DomApp.tsx`, сверка с `dom-routes.json`)

| Маршрут | Экран | Доступ |
|---|---|---|
| `/` | каталог домов Шерегеша (фильтры в query: `checkIn`, `checkOut`, `guests`, `maxPrice`, `page`) + блок «Для владельцев» | все |
| `/:slug` | страница компании | все |
| `/:slug/:houseSlug` | страница дома: галерея, описание, удобства, реестр, условия, `StayTouristTaxNotice`, календарь, форма брони (query `checkIn`, `checkOut`, `adults`, `children`) | все |
| `/b/:token` | страница брони (опрос 15 с), подтверждения оплаты, отмена, push | все |
| `/login`, `/register` | общие страницы (`returnTo`) | все |
| `/bookings` | мои брони (P1) | вошедший |
| `/profile`, `/profile/consents`, `/notices` | минимальный профиль, согласия, уведомления платформы (ссылка «выгрузка и удаление — на ezbook.ru») | вошедший |
| `/cabinet` | мои компании «Дома», «Подключить дома», строки про ezbook и goods | вошедший |
| `/cabinet/new` | создание компании (соглашение, условия триала) | вошедший |
| `/cabinet/subscription` | подписка линейки «Дома» | владелец |
| `/cabinet/:companyId` | вход в компанию: переадресация на шахматку (владелец, управляющий) или на график (горничная) | участник |
| `/cabinet/:companyId/board` | шахматка (по умолчанию для владельца и управляющего); на телефоне — список | `ViewBookings` |
| `/cabinet/:companyId/bookings` | список «Ожидают проверки оплаты» и все брони с фильтром статуса | `ViewBookings` |
| `/cabinet/:companyId/bookings/:bookingId` | карточка брони, файлы, действия, журнал (P1 — журнал) | `ViewBookings` |
| `/cabinet/:companyId/houses`, `/houses/new`, `/houses/:houseId` | дома: список, создание, карточка (вкладки «Описание», «Фото», «Цены», «Реестр и публикация», «Заселение») | `EditHouseContent` / `ManageHouses` |
| `/cabinet/:companyId/schedule` | график уборок и заездов (по умолчанию для горничной) | `ViewSchedule` |
| `/cabinet/:companyId/settings` | профиль, правила, предоплата и реквизиты, исполнитель, отмена, опции, заселение, каталог | `ManageCompany` |
| `/cabinet/:companyId/staff` | персонал с должностями | `ManageCompany` |
| `/cabinet/:companyId/notifications` | push/MAX персонала, сообщения гостю, канал | `ManageCompany` |
| `/cabinet/:companyId/link` | ссылки и QR компании и домов, смена адреса | `ViewCabinet` |
| `/privacy`, `/terms`, `/terms-owner`, `/pdn-consent`, `/channel-risk`, `/data-request`, `/offer-channel`, `/payment-terms` | общие правовые страницы | все |

- Права в интерфейсе — из `GET /api/stays/companies/{id}` → `myPermissions[]` (сервер), меню строится по ним.
- Шапка «ezbook · Дома», `DomNavbar`/`DomFooter` — свои (как у goods).
- Типы — **только генерат** `src/types/api-cycle37.generated.ts`; рукописные — реэкспорт.
- Деньги и расчёты — `dom/src/utils/stayMoney.ts`, `stayRules.ts`, `stayRefund.ts` + тест по `stay-vectors.json`.
- Календарь выбора дат — свой компонент с управлением клавиатурой (стрелки, Enter, Esc), статус ячейки текстом и
  `aria-label`, не только цветом; 44×44 px.
- Загрузка подтверждения: `<input type="file" accept="application/pdf,image/jpeg,image/png,image/webp">` (iOS конвертирует
  HEIC в JPEG сам), очередь до 3 файлов, прогресс — как `usePhotoBatchUpload`. В интерфейсе гостя — «подтверждение
  оплаты (квитанция или скриншот перевода)», не «чек» (Т37-06).
- Vitest: новые файлы dom → новая область `stays` в `contracts/cycle36/test-areas.json` (`frontendPathPrefixes: ["dom/src/"]`).

### §37.14.6 Раздельный вход (Р3) и service worker

- Вход независим: `authStore` в `localStorage` своего origin, `/api` same-origin через nginx dom → CORS не участвует.
- `dom/public/sw.js` — копия goods-воркера (без `fetch`-обработчика и Cache API; CI-grep расширяется на этот файл),
  регистрация **только** через `src/utils/pushWorker.ts`; маршрутизация нажатия — общая таблица `src/test/workerRouting.ts`
  получает третий сайт. `useWebPush({ site: 'Stays', keepBrowserSubscription: true })` — гость и сотрудник в одном
  браузере (§456.3 цикла 24).
- SmartCaptcha: домен `dom.ezbook.ru` в консоли Yandex Cloud (DO-37-03).

---

## §37.15. Инфраструктура: nginx, TLS, деплой, CI (US-37-02)

### §37.15.1 `deploy/nginx/dom.ezbook.conf` (новый файл; текст — к исполнению devops)

По образцу `goods.ezbook.conf` (циклы 23, 24, 34), отличия:

```nginx
# dom.ezbook.ru — третий фронтенд (ARCHITECTURE_CYCLE37.md §37.15). Тот же сервер, тот же API, тот же релизный каталог.
map $request_uri $dom_safe_uri {
    ~^(?<dom_uri_prefix>/(?:api/stays/bookings/public|b)/)[^/?]+(?<dom_uri_rest>.*)$ "${dom_uri_prefix}MASKED${dom_uri_rest}";
    default $request_uri;
}
map $http_referer $dom_safe_referer {
    ~^(?<dom_ref_prefix>https?://[^/]+/b/)[^/?#]+(?<dom_ref_rest>.*)$ "${dom_ref_prefix}MASKED${dom_ref_rest}";
    default $http_referer;
}
log_format dom_masked '$remote_addr - $remote_user [$time_local] "$request_method $dom_safe_uri $server_protocol" '
                      '$status $body_bytes_sent "$dom_safe_referer" "$http_user_agent"';
server {
    server_name dom.ezbook.ru;
    root /var/www/ezbook/current/__dom;
    access_log /var/log/nginx/dom.access.log dom_masked;
    client_max_body_size 11M;            # подтверждение оплаты до 10 МБ (§37.8)
    # Стенд до приглашения реальных владельцев (Q5, R37-1): не индексировать. Снять строкой при запуске.
    add_header X-Robots-Tag "noindex, nofollow" always;
    location /api/notifications/provider-webhook/ { return 404; }
    location /api/notifications/unsubscribe/      { return 404; }
    location /api/phone-verification/max/webhook/ { return 404; }
    location /uploads/ { proxy_pass http://127.0.0.1:5000; ... }
    location /api/     { proxy_pass http://127.0.0.1:5000; ... }
    location = /sw.js  { add_header Cache-Control "no-cache" always; ...security headers... }
    location = /manifest.webmanifest { default_type application/manifest+json; ... }
    location /assets/  { add_header Cache-Control "public, max-age=31536000, immutable" always; ... }
    location / { try_files $uri $uri/ /index.html; add_header Cache-Control "no-cache" always;
                 # CSP как у goods: SmartCaptcha, worker-src 'self' blob:, img-src 'self' data: blob:, frame-ancestors 'none'
                 ... }
}
```

`X-Robots-Tag` и security-заголовки повторяются в каждом `location` с `add_header` (наследование nginx). В
`deploy/nginx/ezbook.conf` — вставка `location ^~ /__dom/ { return 404; }`.

### §37.15.2 TLS и DNS

A-запись `dom.ezbook.ru` → та же машина; отдельный сертификат `certbot --nginx -d dom.ezbook.ru`; HSTS без `preload`.

### §37.15.3 Деплой — один релиз на три сайта, без выката на прод

- Раскладка релиза: `releases/<ts>/` = ezbook, `…/__goods/`, `…/__dom/`; `current` переключает все три атомарно;
  `ssh-deploy-wrapper.sh` и `rollback.sh` не меняются.
- `deploy-staging.yml`, `deploy-production.yml`, артефакт CI — уже собираются `npm run build:release` (он теперь включает dom).
- `deploy-remote.sh` — смоук dom через локальный nginx (`curl --resolve dom.ezbook.ru:443:127.0.0.1 https://dom.ezbook.ru/`
  содержит `<div id="root">`, `/api/health/ready` = 200), **если vhost установлен** (`/etc/nginx/sites-enabled/dom.ezbook.conf`
  существует); иначе — строка-предупреждение «dom vhost not installed, smoke skipped». `DOM_SMOKE=0` — аварийное
  отключение, `DOM_HOST` — переопределение. Так выкат ezbook/goods не зависит от готовности DNS dom.
- **Цикл 37 dom на машину не выкатывает** (решение заказчика: «без выката на прод»; машина одна — она и стенд, и бой).
  DevOps готовит файлы, `DEPLOY.md` §28 и порядок первого выката: (1) DNS, (2) vhost + сертификат, (3) SmartCaptcha-домен,
  (4) деплой `develop`, (5) смоук. Выполняется по отдельному решению заказчика (открытый вопрос §37.19 п. 2). До этого dom
  работает на локальном dev-стенде: `docker compose` + `npm run dev:dom` (порт 5175, прокси `/api`, `/uploads`).
  ⚠️ Обычный деплой `develop` на машину (стенд ezbook/goods) **применит миграции цикла 37** — это безопасно (только
  добавления, город и тарифы), сборка dom при этом ляжет в `__dom/`, но без vhost не будет доступна.
- Конфигурация: `PublicSites:StaysBaseUrl` = `https://dom.ezbook.ru` (дефолт в `appsettings.json` → новой обязательной
  переменной нет), `DeploymentSafetyChecks.ValidatePublicSites` — три адреса; `.env.dev.example` — `SB_DOM_WEB_PORT=5175`,
  `PublicSites__StaysBaseUrl=http://localhost:5175`. Демо: `ValidateDemoMode` не требует `StaysBaseUrl` (dom в демо нет;
  долг C37-1).

### §37.15.4 CI (`.github/workflows/ci.yml`)

Фронт: `npx tsc --noEmit -p tsconfig.dom.json`; `npm run types:api:cycle37` + `git diff --exit-code
src/types/api-cycle37.generated.ts`; `redocly lint ../contracts/cycle37/openapi.yaml` (+ строка в `contracts/redocly.yaml`);
`contracts-to-json.mjs` — `cycle37` в список и шаг сверки JSON; смоук `DIST_DIR=frontend/dist/__dom SMOKE_PROFILE=dom
bash deploy/ci/smoke-frontend.sh` (профиль dom: `index.html`, иконки, `manifest.webmanifest`, `sw.js`); греп `sw.js`
расширяется на `dom/public/sw.js`. Проверка ссылок правового комплекта учитывает dom (тест `dom/src/legalRoutes.test.ts`
против `contracts/cycle11/legal-routes.json`). Бэкенд — без новых шагов (новые тесты входят в существующие проекты;
`btree_gist` есть в сервисе `postgres:16`). `docker-build` — без изменений (новых обязательных переменных нет).

---

## §37.16. Структура проекта — что добавляется

```
ServiceBooking.Core/
├── Entities/  StaysSettings, House, HousePhoto, HousePricePeriod, HouseRegistryAttestation, HouseBlock, HouseBlockEvent,
│              HouseOccupancy, StayBooking, StayBookingCharge, StayBookingEvent, StayPaymentProof,
│              StayGuestPushSubscription, StayGuestPushNotification, StaysSubscription;
│              CompanyMember (+StaffPosition), SubscriptionPlanConfig (+MaxHouses), TrialGrant/TrialPhoneRegistration (+Line),
│              OutboundNotification/StaffPushNotification/StaffMaxMessage (+StayBookingId), StaysPlans (сид-константы)
└── Enums/     CompanyKind (+Stays), StaffPosition, StayBookingStatus, StayBookingEventKind, StayActorKind,
               StayCancellationPolicy, StayChargeKind, StayProviderStatus, HousePriceMode, HouseObjectKind, HouseAmenity,
               HouseBlockKind, HouseBlockEventKind, OccupancySource; NotificationType (+16…26), NotificationReason (+3),
               LegalTextKey (+13 констант вне All), PublicArea (+Houses)

ServiceBooking.Infrastructure/
├── Data/AppDbContext.cs       конфигурация, индексы, CHECK, HasPostgresExtension("btree_gist")
└── Migrations/                *_Cycle37SharedChanges.cs, *_Cycle37StaysTables.cs (EXCLUDE через Sql)

ServiceBooking.API/
├── Controllers/Stays/
│   ├── StaysCompaniesController.cs     api/stays/companies (create, my), slug-check, trial, {id}, settings, payment-details,
│   │                                   provider, slug, qr, notification-settings
│   ├── StaysHousesController.cs        api/stays/companies/{id}/houses…, photos, pricing, price-periods, registry, publish, qr
│   ├── StaysBoardController.cs         board, blocks, schedule
│   ├── StaysStaffBookingsController.cs bookings (список, карточка, действия, файлы, ручная бронь)
│   ├── StaysPublicController.cs        api/stays/public: amenities, catalog, companies/{slug}[/houses/{houseSlug}],
│   │                                   houses/{houseId}/calendar|quote|bookings
│   └── StayBookingsPublicController.cs api/stays/bookings/public/{token}…, api/stays/bookings/my
├── DTOs/Stays/*.cs                     по контракту cycle37
├── Services/Stays/
│   ├── чистые: HousePricing, StayRules, GuestRules, StayMoney, StayRefund, StayStateMachine, HousePublishRules,
│   │           StaysBookingGate, CheckInInfoRelease, StayNotificationPlan, StaysSlugPolicy, StaysTexts,
│   │           StayNotificationTexts, StaysAccess (таблица прав), PublicStayToken
│   ├── с БД:   StaysCompanyService, HouseService, HouseOccupancyWriter, HouseBlockWriter, StayBookingCreationService,
│   │           StayBookingTransitionService, StayHoldExpirer, StayPaymentProofService, StayBookingEventLog,
│   │           StayActorResolver, StayPhoneThrottle, StayNotificationPlanner, StayMessageScheduler,
│   │           StaysCatalogService, StaysBoardService, StaysScheduleService, StaysPlanResolver, StaysTrialService,
│   │           StayDtoMapper, IStaysClock/SystemStaysClock, StaysOptions
├── Services/Scheduling/Tasks/          StaysHoldExpiryTask, StaysScheduledMessagesTask, StaysGuestPushDispatchTask
├── Services/Retention/Rules/           StayPaymentProofRule, StayUnpaidPersonalizationRule, StayBookingPersonalizationRule,
│                                       StayBookingEventRule, StayGuestPushSubscriptionRule, StayGuestPushNotificationRule
├── Services/Companies/                 CompanyKindGuard (обобщён), CompanyCreationService (+Stays)
├── Services/PublicSites/               +StaysBaseUrl, новые методы ссылок
├── Startup/                            RateLimitingExtensions (+6 политик), ApplicationServicesExtensions,
│                                       LoggingExtensions (маска), DeploymentSafetyChecks (+PublicSites, +StaysPolicies)
└── appsettings.json, appsettings.Testing.json   секции Stays, Retention:Stay*, RateLimits, ScheduledTasks

ServiceBooking.UnitTests/   StayMoneyTests/StayRefundTests/StayRulesTests/HousePricingTests (векторы), StayStateMachineTests,
                            HousePublishRulesTests, StaysBookingGateTests, CheckInInfoReleaseTests, StayNotificationPlanTests,
                            StaysSlugPolicyTests (dom-routes.json), StaysAccessTests, StaysTextsForbiddenWordsTests,
                            CompanyKindBranchGuardTests, PublicSiteLinksTests (+Stays), DeploymentSafetyChecks (+Stays)
ServiceBooking.Tests/       Tests/Cycle37*: StaysCompanyTests, HousesTests, PricingTests, StayBookingCreationTests,
                            StayConcurrencyTests, StayHoldExpiryRaceTests, PaymentProofTests, StaffBookingActionsTests,
                            BoardAndBlocksTests, ScheduleTests (горничная), CatalogTests, StaysTariffTrialTests,
                            StaysNotificationsTests, StaysSubjectDataTests, StaysKindIsolationTests (матрица §37.3.2),
                            Cycle37ContractTests (OpenApiContract по cycle37)

frontend/   dom/, vite.dom.config.ts, tsconfig.dom.json, tailwind.dom.config.js, shared-sources.js(+d.ts),
            scripts/merge-site-dist.mjs; src/types/api-cycle37.generated.ts; src/components/sites/, src/components/staffMax/
            (переносы §37.14.3); правки §37.3.3
contracts/cycle37/   openapi.yaml (+openapi.json генератом), dom-routes.json, stay-vectors.json
contracts/cycle36/test-areas.json  область stays, обновлённые префиксы переносов
deploy/nginx/dom.ezbook.conf, deploy/nginx/ezbook.conf (+__dom), deploy/deploy-remote.sh (+смоук dom), deploy/ci/smoke-frontend.sh
(+профиль dom), .github/workflows/ci.yml, .env.dev.example, DEPLOY.md §28
```

`Cycle22RouteTable.golden.txt` — **61 новый маршрут** (60 под `/api/stays/*` и общий
`PUT /api/Companies/{id}/members/{memberId}/position`; сводка по группам — `API_CONTRACT_CYCLE37.md` §37.39, методы —
`openapi.yaml`) с атрибутами; `API_DOCUMENTATION.md` — раздел «Дома».

---

## §37.17. Разбивка работ и параллельность

Контракт (`openapi.yaml` + `dom-routes.json` + `stay-vectors.json`) готов **до** кода — фронтенд стартует на prism-моке
(`npx @stoplight/prism mock contracts/cycle37/openapi.yaml --port 4037`) в день 1.

### §37.17.1 Backend

| # | Задача | Зависит от | Параллельно с |
|---|---|---|---|
| BE-37-P | Чистые классы + юнит-тесты по векторам: `HousePricing`, `StayRules`, `GuestRules`, `StayMoney`, `StayRefund` (+ валидатор конфигурации шаблонов, запрет слов), `StayStateMachine`, `HousePublishRules`, `StaysBookingGate`, `CheckInInfoRelease`, `StayNotificationPlan`, `StaysSlugPolicy`, `StaysAccess`, `StaysTexts` (запасные тексты §15 обзора) | — | всё |
| BE-37-1 | `CompanyKind.Stays`; обобщение `CompanyKindGuard`; аудит развилок §37.3.2 + `CompanyKindBranchGuardTests`; `PublicSites` (третий адрес, методы, fail-fast); `kinds-summary`, `GET /api/companies/{slug}`, админ-фильтр; галерея компании для Stays → 409 | — | BE-37-P, BE-37-M |
| BE-37-M | Сущности, перечисления, `AppDbContext`, **две миграции** (§37.2.7), `ShowcaseOwnership.NeverWritten` — **один разработчик, один коммит** | BE-37-1 (значение enum) | BE-37-P |
| BE-37-2 | Компания «Дома»: создание (третья ветка `CompanyCreationService`, город, `StaysSettings`), `my`, `slug-check`, `{id}` (+чек-лист, `myPermissions`), `settings`, `payment-details`, `provider` (Т37-03), `slug`, QR; персонал (`position`, `AddMember` для Stays, `PUT …/position`); `PUT /api/companies/{id}` для Stays | BE-37-M | BE-37-3, BE-37-5 |
| BE-37-3 | Дома: CRUD (`setup`/`content`), архив/удаление, порядок, фото (`PublicArea.Houses`), цены (режим, периоды с `EXCLUDE` → 409), реестр, заверение и публикация (ЮР-2), QR дома, `amenities` | BE-37-M | BE-37-2, BE-37-4a |
| BE-37-4a | Занятость и бронь гостя: `HouseOccupancyWriter`, `IStaysClock`, `StayPhoneThrottle`, calendar, quote, создание (идемпотентность, капча, гейт, лимиты, ленивое снятие, снимки), страница по токену, подтверждения оплаты (файлы, гонка), отмена гостем с расчётом возврата, `StayBookingEventLog` + ревизия; 6 политик rate limit | BE-37-M, BE-37-P | BE-37-3 |
| BE-37-4b | Персонал: шахматка с ревизией, блокировки (`HouseBlockWriter`), список/карточка/журнал, подтверждение/отклонение/отмена с `expectedVersion`, отдача файлов + событие просмотра, график (форма горничной, ЮР-5), ручная бронь (P1), «Мои брони» (P1) | BE-37-4a | BE-37-5, BE-37-6 |
| BE-37-5 | Тарифы и триал: `StaysSubscription`, `StaysPlanResolver`, `MaxHouses` в планах и админке, `StaysTrialService`, `Line` в триале (6 мест), `OwnerSubscriptionService`/`BillingController`/`AdminBillingController` ветка Stays, `ChannelEligibility`/`PaidNotificationNumbers`, перенос компании | BE-37-M | BE-37-3, BE-37-4 |
| BE-37-6 | Уведомления: `StayNotificationPlanner`, `StayMessageScheduler`, staff push/MAX (+должность), push гостю (подписка, очередь, диспетчер), три задачи, тексты (ЮР-4, Т37-08), `NotificationType`/`Reason` | BE-37-4a | BE-37-4b |
| BE-37-7 | ПДн и retention: выгрузка, удаление, отзыв, 6 правил (ЮР-6), маска пути, сторож `GuestPhone ==`; `StaysCatalogService` (кеш базы) и страница компании, замер p95 на 500 домах | BE-37-4a | BE-37-6 |
| BE-37-8 | `Cycle22RouteTable.golden.txt`, `contracts/cycle37/openapi.json` (`npm run contracts:json`), `OpenApiContractValidatorTests` `[InlineData("cycle37")]`, `API_DOCUMENTATION.md` «Дома» | в конце | — |

### §37.17.2 Frontend

| # | Задача | Зависит от | Параллельно с |
|---|---|---|---|
| FE-37-0 | Каркас dom: `vite.dom.config`, `tsconfig.dom`, tailwind, `shared-sources.js` (обобщение + guard goods зелёный), `merge-site-dist.mjs`, скрипты `dev:dom`/`build:dom`/`build:release`/`types:api:cycle37`, `DomApp`, навбар/подвал, правовые маршруты, `LegalGuard`, ESLint-границы, генерат, область `stays`, переносы §37.14.3, `sw.js`, манифест, иконки | — | всё |
| FE-37-1 | Утилиты и векторы: `stayMoney`, `stayRules`, `stayRefund`, форматирование дат ночей (пояс компании) + тест `stay-vectors.json` | FE-37-0 | всё |
| FE-37-2 | Публичное: каталог с фильтрами в URL, страница компании (блок «Об исполнителе» по ЮР-3), страница дома (галерея, удобства, реестр, условия, `StayTouristTaxNotice`), календарь выбора дат (клавиатура) | FE-37-0, FE-37-1 | FE-37-3…6 |
| FE-37-3 | Форма брони (один экран, гость + капча / вошедший, разбивка, «цена изменилась», идемпотентность, отдельная галочка мессенджера, `StayBookingNotice`, `StayGuestCommentNotice`, `StayTouristTaxNotice`) и страница брони (опрос, отсчёт, реквизиты + «Скопировать», `StayPaymentProofNotice`, подтверждения оплаты до 3, отмена с суммой «не меньше», информация к заселению, push) | FE-37-1 | FE-37-2, FE-37-4 |
| FE-37-4 | Кабинет: список и создание компании (соглашение, триал), настройки (все группы §37.2.2 + исполнитель + подсказки `StayPaymentRequisitesOwnerNotice`, `StayCheckInInfoOwnerNotice`, `StayMigrationOwnerNotice`, предупреждение «0 %»), персонал с должностями, уведомления, ссылка/QR, подписка | FE-37-0 | FE-37-5, FE-37-6 |
| FE-37-5 | Дома: список, карточка-вкладки, фото (адаптер `CompanyPhotosSection`, `CompanyPhotoPeopleNotice`), адрес/телефон с `StayPublicContactsNotice`, цены двух режимов и календарь цен, реестр и диалог публикации с заверением (`StayRegistryOwnerNotice`), архив | FE-37-0 | FE-37-4, FE-37-6 |
| FE-37-6 | Шахматка (сетка/список на телефоне, опрос с ревизией, счётчик «ожидают проверки»), блокировки (`StayMigrationOwnerNotice` у комментария), список и карточка брони (просмотр файлов, действия с `expectedVersion`, `StayOwnerCancelNotice`, журнал P1), график (горничная, 360 px), ручная бронь (P1) | FE-37-0 | FE-37-4, FE-37-5 |
| FE-37-7 | Правки ezbook/goods §37.3.3 (переадресации, строки кабинетов, админка: фильтр, планы `maxHouses`, подписка «Дома» в аккаунте), `staffPushTexts`, `workerRouting` | по контракту — сразу; живая проверка после BE-37-1/BE-37-5 | всё |
| FE-37-8 | Мои брони (P1), профиль dom | FE-37-0 | — |

### §37.17.3 DevOps

| # | Задача | Когда |
|---|---|---|
| DO-37-01 | `deploy/nginx/dom.ezbook.conf`, вставка `__dom` в `ezbook.conf` | сразу |
| DO-37-02 | CI (§37.15.4), `smoke-frontend.sh` профиль dom, смоук dom в `deploy-remote.sh` (по наличию vhost) | после FE-37-0 |
| DO-37-03 | `DEPLOY.md` §28: DNS, vhost, certbot, SmartCaptcha-домен, проверка `CREATE EXTENSION btree_gist` правами пользователя БД боя, порядок первого выката, откат миграций (C24-10-подобный риск §37.2.7); `.env.dev.example` | сразу |
| DO-37-04 | Локальный dev-стенд: `docker compose` + `npm run dev:dom`, засев одной компании «Дома» через HTTP API для ручных проверок | после BE-37-3 |

### §37.17.4 QA

Кейсы `CY37-*` в `TEST_CATALOG.md`; schemathesis по `contracts/cycle37/openapi.yaml`; матрица изоляции §37.3.2 (каждый
салонный маршрут с id компании «Дома» → 409 `StaysRefusalText`, каждый маршрут заказов → 404, каждый маршрут «Домов» с id
салона/магазина → 404); параллельные тесты §37.5.4; гонка таймера с поддельными часами; права ролей — таблица §37.9 (каждое
разрешение × каждая должность); горничная не получает запрещённых полей (контракт формы, ЮР-5); реквизиты не появляются ни
в одном публичном ответе (Т37-04); тексты без «задаток/невозвратный/депозит»; retention в сухом прогоне (ЮР-6); регресс
«салоны и магазины как прежде» — полный прогон, числа не ниже базы цикла 36; смоуки трёх сайтов; ручные M37-* на 360 px и
iOS Safari (календарь, загрузка подтверждения с iPhone — проверка автоконвертации HEIC); «зелёный прогон ≠ функционал» —
grep классов §37.16 до объявления готовности.

### §37.17.5 Точки синхронизации BE↔FE

| Что | Где зафиксировано |
|---|---|
| Форма DTO, коды 409 JSON, enum'ы | `openapi.yaml` → генерат |
| Деньги, цена ночи, возврат, доступность | `stay-vectors.json` |
| Маршруты dom, резерв, формат slug | `dom-routes.json` |
| Тексты отказов-строк (400/402/429) | `API_CONTRACT_CYCLE37.md` §37.24–§37.36 — фронт печатает `response.data` |
| Права в интерфейсе | `myPermissions` в DTO компании |

### §37.17.6 Если не укладываемся (R37-2)

Режутся P1 целиком: US-37-22 (мои брони), US-37-23 (показ журнала — журнал пишется всё равно), US-37-24 (ручная бронь —
вместо неё блокировка с комментарием), отметка «убрано», цена в ячейке календаря, массовая блокировка, утренняя сводка
горничной. P0 не режутся.

---

## §37.18. Риски, решения и отклонения от буквы SPEC

| # | Риск | Решение |
|---|---|---|
| R37-1 | Правовой стоп (L1, реестр) | ЮР-2: поля, заверение, показ номера; dom — стенд до вычитки юристом; `noindex` на vhost |
| R37-2 | Объём цикла | параллельный план, prism-мок, P1 режутся первыми |
| R37-3 | Владелец долго не проверяет оплату | счётчик в шахматке, push о файле; таймера нет (решение заказчика) |
| R37-4 | Боты и фейки | капча, лимиты по IP и номеру под замком номера; при 0 % предупреждение в настройке |
| R37-5 | Уведомления гостю на бою выключены (`logging`) | всё видно на странице брони; сказать заказчику до приглашения владельцев |
| R37-6 | Регресс ezbook/goods от третьего вида | обобщённый guard вместо точечных правок, аудит + страж развилок, матрица, полный прогон, смоуки |
| R37-7 | Тонкие ошибки дат и поясов | чистые функции, векторы, все сроки — по поясу-снимку брони, тесты на границах суток UTC+7 |
| R37-8 (новый) | `CREATE EXTENSION btree_gist` не пройдёт правами пользователя БД боя | расширение trusted; DO-37-03 проверяет до выката; миграция падает целиком и откатывается (нет полу-применённого состояния) |
| R37-9 (новый) | Правка уникальных индексов триала затрагивает действующий механизм цикла 18 | добавочная колонка с дефолтом 0 = поведение «Записей» прежнее; 6 мест чтения — явный фильтр; регресс-тесты цикла 18 |
| R37-10 (новый) | Ссылка на бронь уходит на ошибочный номер (ЮР-7) | риск принят письменно; коды доступа — только ссылкой (ЮР-4); подтверждение перед отменой; 256-битный токен, маскирование в логах |
| R37-11 (новый) | Шаблоны отмены станут незаконными при правке конфигурации | валидатор конфигурации на старте не пропускает удержание > 1 ночи и границу раньше дня заезда |
| R37-12 (новый) | Ёмкость битовой маски `NotificationType` (int) — после цикла 37 свободны 4 значения | новые типы «Домов» маской не пользуются; цикл 2 решает: уложиться или отдельная маска |

**Отклонения от буквы SPEC (читать обязательно):**
1. **Шаблоны отмены** — ЮР-1 вместо §4.8 SPEC.
2. **HEIC не принимается сервером** (SPEC §10: «PDF/JPEG/PNG/WebP/HEIC»). Без декодера HEIC сервер не может снять EXIF/GPS,
   а сотрудник на Windows не откроет файл. iOS Safari сам конвертирует HEIC в JPEG, если `accept` его не перечисляет.
3. **Комментарий гостя горничной** — по умолчанию скрыт (ЮР-5).
4. **Информация к заселению в мессенджер** — по умолчанию ссылкой (ЮР-4).
5. **Сведения об исполнителе обязательны** для приёма броней с предоплатой (Т37-03) — новое условие гейта.
6. **Публикация дома** требует вида объекта и заверения (ЮР-2), для гостевого дома и иного средства размещения — номер.
7. **Галерея фото компании** у «Домов» не используется (фото — у домов); маршруты записи в галерею компании отвечают 409.
8. **Вошедший гость бронирует только на номер аккаунта** (как покупатель goods, §405 п. 3 цикла 23).
9. **Компания «Дома» создаётся только в городе Шерегеш** — `cityId` не принимается (Р12).
10. **Реквизиты** отдаются только по токену брони и в сообщениях этой брони — ни в одном публичном DTO (Т37-04).
11. **Блокировки между собой не пересекаются** (только соприкасаются): одна таблица занятости с ограничением БД; SPEC говорит
    лишь «могут соприкасаться», пересечение двух блокировок — 409 `BlockOverlapsBlock`.

---

## §37.19. Открытые вопросы к заказчику (кодирование не блокируют; по умолчанию — как в скобках)

1. **ЮР-2, трактовка.** Публиковать без номера только дома вида «жилое помещение» (по умолчанию), или любой вид — под
   заверением? Гостевой дом без номера — прямое нарушение 127-ФЗ собственником.
2. **Выкат dom на машину.** Машина одна (стенд = бой). Когда поднимать `dom.ezbook.ru` (DNS, vhost, сертификат) —
   сразу после цикла как закрытый `noindex`-стенд, или только после вычитки юристом (по умолчанию — файлы готовы, выкат
   отдельным решением)?
3. **Уведомления гостю на бою** (R37-5): включать ли GREEN-API / Web Push до приглашения владельцев.
4. **HEIC** — устраивает ли отказ на сервере с автоконвертацией iOS (по умолчанию — да).
5. **Сведения об исполнителе при предоплате 0 %** — тоже обязательны (ст. 9 ЗоЗПП действует независимо от предоплаты)?
   По умолчанию — обязательны только при предоплате > 0 (буква Т37-03).
6. **Формат номера реестра и шаблон ссылки** — пока свободный текст 5–32 символа и любая `https`-ссылка; уточнить после
   сверки юристом.
7. **Выгрузка данных гостя** — файлы подтверждений оплаты в выгрузку не встраиваются (метаданные + ссылка на бронь);
   обзор §8.1 говорит «файл как файл» — нужно ли встраивать (base64 в JSON или архив)?
8. **Тарифы «Домов» на публичной странице цен** — не показываются (L10, нужна вычитка оферты); подтвердить.
9. **Условия триала «Домов»** — нужна редакция текста `TrialTermsRegistry` от юриста; до неё — черновая.
