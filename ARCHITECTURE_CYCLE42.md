# ARCHITECTURE — цикл 42 ServiceBooking: «Бани» — бронирование бань на bani.ezbook.ru

**Разделы §42.0–§42.18.** Вход: `SPEC_CYCLE42_BANI.md` (US-42-01…27, A42-1…13, раздел «Решения заказчика от 09.10.2026» —
**приоритетнее** текста SPEC), `LEGAL_REVIEW_CYCLE42.md` (§12, Т42-01…17 — обязательны), `BRIEF_CYCLE42_BANI.md`,
`CURRENT_STATE.md` (шапка на `5bcd015`; §2, §3, §5.12, §5.14, §5.15, §6, §9.10), `ARCHITECTURE_CYCLE39.md`,
`API_CONTRACT_CYCLE39.md`, `contracts/cycle39/`, код ветки `cycle/042-bani` (сверено: `CompanyKind`, `CompanyKindGuard`,
`StaysAccess*`, `StaysBookingGate`, `StaysPlanResolver`, `StaysTrialService`, `PublicSiteLinks`, контроллеры `Controllers/Stays/`,
`CompanyCreationService`, `AdminBillingController`, `frontend/shared-sources.js`, `scripts/merge-site-dist.mjs`,
`deploy/nginx/dom.ezbook.conf`, `deploy/deploy-remote.sh`, `.github/workflows/ci.yml`, `frontend/dom/src/components/services/`).
Ветку подготовил devops; архитектор веток не трогает.

**Документы цикла:**

| Файл | Что | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE42.md` (этот) | решения, модель, механизмы, структура, задачи, риски | все |
| `API_CONTRACT_CYCLE42.md` (§42.20–§42.39) | контракт словами: маршруты, порядок проверок, коды, тексты, изменения существующих маршрутов | backend, frontend, QA |
| `contracts/cycle42/openapi.yaml` (+ `openapi.json` генератом, DO-42-03) | **источник истины по форме** 68 новых маршрутов `/api/baths/*` и изменённых маршрутов (OpenAPI 3.0.3) | backend, frontend, QA, CI |
| `contracts/cycle42/bani-routes.json` | маршруты bani, политика адресов компании и ресурса, резерв слов — встраивается в API | backend, frontend, QA |
| `contracts/cycle42/bani-vectors.json` | эталон новых чистых функций: напоминание, число гостей, фильтр позиций, тексты владельца, гейт с единицей «ресурс» | backend (C#), frontend (TS), QA |

Слотовый движок, его эталон (`contracts/cycle39/service-vectors.json`) и его ограничения БД **не меняются**.

---

## §42.0a. Решения заказчика (09.10.2026) и требования юриста — кодировать по ним

**Q42-1…16** — приняты рекомендации SPEC: (а) отдельный вид компании «Бани»; (б) город из справочника; тарифы как у «Домов»,
триал 14 дней, без бесплатного уровня; (а) один заказ — одна услуга; (б) число гостей без доплаты; Q42-6…16 — по
рекомендациям §0.2 SPEC. **LEGAL_REVIEW_CYCLE42.md** принят: Q-L42-1 (а) — запрет в D3 + мягкий фильтр названий позиций с
подтверждением; Q-L42-2 — товарный знак «EZBOOK» вне цикла; Q-L42-3 — одна редакция D1–D4 на все вертикали (не код);
Q-L42-4 — канал мессенджера в триале «Бань» есть; Q-L42-5 — слово для гостя «бронь».

| Т42 | Требование | Где закрыто |
|---|---|---|
| Т42-01 | Нейтральная запасная подстановка компании в гостевых текстах услуг (и «Домов») | §42.11.3, FE-42-7 |
| Т42-02 | Свои ключи `BathBookingNotice`/`BathBookingTerms` с числом гостей, снимок версий в брони, вне `All` | §42.11.3, BE-42-4, FE-42-4 |
| Т42-03 | `/privacy`, `/terms`, `/pdn-consent`, `/data-request` на bani | §42.12.2, FE-42-2 |
| Т42-04 | `BathPublicContactsNotice`; bani не запрашивает ключи домов (тест) | §42.11.3, FE-42-8 |
| Т42-05 | Мягкий фильтр позиций с подтверждением и журналом; `BathPositionsOwnerNotice` | §42.8.1, BE-42-3, FE-42-6 |
| Т42-06 | Число гостей «включая детей», 1…вместимость, цена не зависит; текст у вместимости; в выгрузке | §42.7, BE-42-3/4/6 |
| Т42-07 | Подсказка безопасности + фраза о лечебном эффекте | §42.11.3, FE-42-7 |
| Т42-08 | «Время местное, <город>» на странице ресурса, в форме, на странице брони, в сообщениях | §42.9.4, §42.10, BE-42-5, FE-42-4 |
| Т42-09 | «Забронировать ещё»: без ПДн в URL, галочка выключена, позиции 0, гости не переносятся | §42.10.4, FE-42-4 |
| Т42-10 | Своя версия условий триала, снимок/хеш в `TrialGrants`; **после окончания тарифа управление бронями доступно** | §42.5.4, §42.5.7, BE-42-2, QA |
| Т42-11 | Guard главной/FAQ bani и текстов гостя — списки §10 обзора | §42.12.4, FE-42-8 |
| Т42-12 | Мягкие предупреждения в описании ресурса и позициях | §42.8.2, BE-42-3 |
| Т42-13 | Текст `18-company-photo-people-notice` у загрузки фото комплекса и ресурсов | §42.12.2, FE-42-5/6 |
| Т42-14 | `/api/pricing` не отдаёт «Бани»; сетки цен на главной нет | §42.5.1, BE-42-2 (тест), FE-42-8 |
| Т42-15 | Напоминание — фиксированный текст без рекламы; мессенджер — по галочке; push без ПДн | §42.9.5, BE-42-5 |
| Т42-16 | Новых хранилищ вне РФ не вводить | §42.1 (новых нет) |
| Т42-17 | Правки D1–D4 и РКН — до первого реального владельца | не код; §42.13.4 (строка в `DEPLOY.md`), §42.17 |

Статус вертикали: bani — **только стенд под `noindex`**, реальных владельцев бань не приглашать до вычитки живым юристом и
публикации правок D1–D4 (R42-3, Т42-17). Все новые правовые тексты — черновые (ключи вне `LegalTextKey.All`, запасной текст на
фронте, как Т37-13/Т39-07).

---

## §42.0. Итог решений — ответы на §7.1 SPEC (A42-1…A42-13) одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| A42-1 | Как выразить «Бани» | **Новое значение `CompanyKind.Baths = 3`** (дописыванием) — и вид компании, и линейка тарифа (`SubscriptionPlanConfig.Line` уже типа `CompanyKind`), и сайт устройства (`PushSubscription.Site = 3`), и ключ однократности триала (`TrialGrants.Line`). Разница вертикалей — **данными**: `CompanyKindTraits` (все 4 вида) и `SlotVertical` (2 «слотовые» вертикали). Ветвления «вид == X ? … : …» запрещены стражем (расширяется на `Stays`) | §42.3 |
| A42-2 | Движок без копий | Таблицы цикла 39 (`StayServices`, `StayServiceSessions`, `StayServiceOrders`…) и `StaysSettings` переиспользуются **как есть** для компаний `Baths` — без переименований и без вторых таблиц. Контроллеры услуг цикла 39 становятся **абстрактными базами** с двумя наследниками: прежний (`api/stays/…`, строки эталона маршрутов те же) и банный (`api/baths/…`). Сервисы получают вид компании параметром только в точках поиска; всё остальное выводится из `company.Kind`. Пути записи сеансов, порядок замков, `EXCLUDE` — не трогаются | §42.4 |
| A42-3 | Гейт приёма бани | Та же `StaysBookingGate.Evaluate`, но лимит выражен **единицей вертикали** (`Unit = House | Resource`): у бань — опубликованные ресурсы аккаунта, `OverResourceLimit`; реквизиты — по предоплате ресурса; исполнитель — всегда. Ручная бронь гейт не проверяет | §42.5.2 |
| A42-4 | Фронтенд | Четвёртое приложение `frontend/bani`. Общее для услуг (выбор времени, форма и страница брони, вкладки кабинета, «День услуг», карточка, ручная бронь, утилиты, API) **переносится** в `src/components/slots/`, `src/utils/slots/`, `src/api/slots.ts`; различия — через `SlotVerticalProvider` (API-префикс, адреса, правовые ключи, слова). В dom остаются тонкие реэкспорты — правка dom минимальна, тесты dom без правки ожиданий | §42.12 |
| A42-5 | Адреса bani | `/`, `/:slug` (комплекс), `/:slug/:resourceSlug` (ресурс), `/s/:token`, `/bookings`, `/cabinet/…`; файл `contracts/cycle42/bani-routes.json` (встраивается в API — `BathsSlugPolicy`); адрес компании — общее пространство платформы, свой резерв | §42.10.1 |
| A42-6 | Ссылки и заголовки | `PublicSiteLinks` строит адреса **по виду компании** (новый `PublicSites:BathsBaseUrl`, по умолчанию `https://bani.ezbook.ru`); заголовок push гостю — из вертикали («EZBOOK Бани»); сервис-воркер bani — копия dom с общей таблицей маршрутизации | §42.9 |
| A42-7 | Каталог | `GET /api/baths/catalog` — карточка = ресурс; «база» (опубликованные ресурсы + гейт компаний) кешируется на 30 с; фильтр даты (P1) — пакетная загрузка окон/правил/занятости кандидатов (4 запроса) + `ServiceSlotCalculator` в памяти, без N+1 | §42.10.2 |
| A42-8 | Вместимость и гости | `StayServices.Capacity int NULL` (1…30), `StayServiceOrders.GuestsCount int NULL` (снимок). Правило — данными: вместимость задана ⇒ число гостей обязательно; у услуг «Домов» вместимости нет. Векторы цикла 39 не меняются (цена от гостей не зависит) | §42.7 |
| A42-9 | Тарифы | Своя таблица `BathsSubscriptions` (как `StaysSubscriptions`), 4 тарифа с фиксированными Id (`0c42ba70-…`), `SubscriptionPlanConfigs.MaxResources`, триал однократен **в линейке**; `StaysPlanResolver`/`StaysTrialService` обобщаются по линейке; админка и кабинет подписки — линейка `Baths` | §42.5 |
| A42-10 | «Мои брони» | Маршрута списка заказов услуг гостя нет (у dom — только брони домов). Новый `GET /api/baths/service-orders/my` с гейтом по подтверждённому номеру (`SUBJECT-PHONE-GATE`) | §42.10.5 |
| A42-11 | Напоминание | Новый `NotificationType.ServiceGuestSessionReminder = 41` (39–40 — цикл 40); третий проход существующей задачи `stays-scheduled-messages`; чистая `SessionReminderPolicy` по векторам; настройка `StaysSettings.ServiceReminderHours` (null = выключено — у «Домов» выключено) | §42.9.5 |
| A42-12 | Витрина, демо | Демо нет. Новые таблицы — в `ShowcaseOwnership.NeverWritten` | §42.2.6 |
| A42-13 | Контракт | `contracts/cycle42/openapi.yaml` + `bani-routes.json` + `bani-vectors.json`; `service-vectors.json` цикла 39 не меняется | шапка |

---

## §42.1. Стек: новых зависимостей — ноль

Цикл расширяет существующую платформу: ASP.NET Core 8 + EF Core 8 + PostgreSQL 16 (бэкенд, БД, аккаунты, уведомления,
правовой контур — общие), React 18 + Vite 5 + TypeScript + TanStack Query + Tailwind (четвёртое приложение того же npm-пакета).
Новых пакетов NuGet/npm, внешних сервисов, хранилищ и переменных окружения (кроме необязательной `PublicSites__BathsBaseUrl` с
безопасным значением по умолчанию) нет. Почему так, а не отдельный сервис «Бани»: движок слотов цикла 39 уже закрывает ~80 %
брифа; отдельный сервис означал бы вторую копию движка, второй контур ПДн и вторую ответственность за двойную бронь — именно
это SPEC запрещает. Нагрузка §6 SPEC (до 150 ресурсов, 300 броней в сутки) — на порядок ниже возможностей одной машины;
каталог с фильтром даты — 4 индексных запроса и расчёт в памяти. Масштабирование «как сервис» не меняется против цикла 39:
состояния в памяти процесса цикл не добавляет, кроме 30-секундного кеша «базы» каталога (как у dom); второй экземпляр API
возможен на тех же условиях (C37-11). Новых хранилищ вне РФ нет (Т42-16).

---

## §42.2. Модель данных

Конвенции §37.2/§39.2 без изменений: перечисления — числом, только дописыванием; деньги — `int` рублей; моменты —
`timestamptz` UTC; время суток ресурса — минуты бизнес-дня. **Существующие таблицы, столбцы и значения не переименовываются.**

### §42.2.1 Вид компании — `CompanyKind.Baths = 3` (дописать в `ServiceBooking.Core/Enums/CompanyKind.cs`)

Компания «Бани» — обычная `Company` (`Kind = 3`): общие `Name`, `Slug` (общее пространство адресов), `Description`, `Address`,
`Phone`, `CityId`, `TimeZoneId` + `TimeZoneIsManual` (город из справочника, пояс — по городу, как у салона), `LogoUrl`,
`YandexMapsUrl`, `TwoGisUrl`, `ShowInPublicListing`, `IsActive`, `BillingAccountId`. Фото комплекса — общая галерея
`CompanyPhotos` (до 10). Персонал — `CompanyMembers` с `StaffPosition` (`Manager` / `Housekeeper`).

**Настройки вертикали — та же `StaysSettings` (1:1 с компанией)**: у банной компании используются `HoldMinutes`,
`HorizonDays`, реквизиты и назначение платежа, сведения об исполнителе `Provider*`, флаги уведомлений (`GuestWebPushEnabled`,
`GuestMessengerEnabled`, `StaffMaxEnabled`), `HousekeeperSeesGuestComment`, `BookingsRevision`, `AcceptServiceOrdersWithoutStay`
(всегда `true`) и новая `ServiceReminderHours`. Столбцы домов (`CheckInTime`, ночи, сборы, информация к заселению,
напоминание накануне заезда) у бани остаются со значениями по умолчанию и нигде не читаются (маршрутов домов у бани нет).
Почему не своя таблица `BathsSettings`: все сервисы движка цикла 39 (создание заказа, гейт, удержание, подтверждения оплаты,
ревизия «Дня услуг», уведомления) читают `StaysSettings`; вторая таблица = вторая ветка в каждом из них, то есть копия.

### §42.2.2 Новые таблицы (2)

**`BathsSubscriptions`** — подписка линейки «Бани», форма `StaysSubscriptions` один-в-один: `Id`, `BillingAccountId` (FK
`Cascade`, уникальный индекс), `PlanConfigId` (FK `SetNull`, NULL), `PaidUntil timestamptz NULL`, `IsActive bool`,
`CreatedAtUtc`, `UpdatedAtUtc`, `UpdatedByUserId`. Нет строки — нет тарифа (бесплатного уровня нет). Почему отдельная таблица,
а не столбец `Line` в `StaysSubscriptions`: уникальный индекс `(BillingAccountId)` пришлось бы пересоздать как `(BillingAccountId,
Line)` — это правка существующей схемы с недетерминированным откатом (две строки на аккаунт); прецедент — `OrdersSubscriptions`
и `StaysSubscriptions` по таблице на линейку.

**`StayServiceItemConfirmations`** — журнал подтверждений «Это не алкоголь и не табак» (Т42-05, по образцу
`HouseRegistryAttestations`), append-only: `Id`, `CompanyId`, `ServiceId` (FK `Cascade`), `ItemId uuid NULL` (FK `SetNull`;
NULL — позиция удалена), `ItemNameSnapshot varchar(100)`, `MarkersHit varchar(200)` (найденные основы через запятую),
`NoticeKey varchar(64)` (`BathPositionsOwnerNotice`), `NoticeVersion varchar(80)` (версия из манифеста или
`fallback:<sha256 ключа>`), `ConfirmedByUserId text`, `ConfirmedByNameSnapshot varchar(200)`, `ConfirmedAtUtc`, `IpAddress
varchar(64) NULL`. Индекс `(ServiceId, ConfirmedAtUtc)`. Пишет только `ServiceItemWriter` (§42.8.1). Хранится, пока
существует компания (как `StaysReminderTemplateChanges`), правила retention нет.

### §42.2.3 Новые столбцы существующих таблиц (все NULL или с DEFAULT — добавочные)

| Таблица | Столбец | Смысл |
|---|---|---|
| `SubscriptionPlanConfigs` | `MaxResources int NULL` | лимит опубликованных ресурсов **только** у тарифов `Line = Baths`; null — без ограничения (как `MaxHouses` у «Домов») |
| `StayServices` | `Capacity int NULL` + CHECK `CK_StayServices_Capacity`: `"Capacity" IS NULL OR "Capacity" BETWEEN 1 AND 30` | вместимость «до N человек»; у услуг «Домов» NULL |
| `StayServiceOrders` | `GuestsCount int NULL` + CHECK `CK_StayServiceOrders_GuestsCount`: `IS NULL OR BETWEEN 1 AND 30` | число гостей (снимок при создании, не меняется) |
| `StayServiceOrders` | `SessionReminderAtUtc timestamptz NULL` | момент постановки напоминания перед сеансом (однократность; ставится и без доступных каналов) |
| `StaysSettings` | `ServiceReminderHours int NULL` + CHECK `IS NULL OR BETWEEN 1 AND 24` | напоминание перед сеансом за N часов; NULL — выключено (у всех существующих компаний «Дома» — NULL) |

### §42.2.4 Новые значения перечислений (только дописывание в конец)

| Перечисление | Значение | Где хранится |
|---|---|---|
| `CompanyKind` | `Baths = 3` | `Companies.Kind`, `SubscriptionPlanConfigs.Line`, `TrialGrants.Line`, `TrialPhoneRegistrations.Line`, `PushSubscriptions.Site`, `SubscriptionChangeLogs.Line`, `BillingAccounts.RequestedLine` — все уже `int`, схема не меняется |
| `NotificationType` | `ServiceGuestSessionReminder = 41` | `OutboundNotifications.Type` и др. (39–40 — резерв цикла 40; при мерже с циклом 40 сверить, R42-10) |
| `StayServiceOrderEventKind` | `SessionReminderSent = 10` | `StayServiceOrderEvents.Kind` |
| `NotAcceptingReason` (C#, не хранится) | `OverResourceLimit` | ответы API |
| `StaysServiceConflictCode` (строки) | `ServiceNoCapacity`, `ItemRestrictedConfirmationRequired` | ответы API |
| `LegalTextKey` (константы вне `All`) | `BathBookingNotice`, `BathBookingTerms`, `BathPublicContactsNotice`, `BathPositionsOwnerNotice`, `BathCapacityOwnerNotice`, `BathPaymentProofNotice`, `BathPaymentRequisitesOwnerNotice`, `BathOwnerCancelNotice` | — |

`NotificationTypeCatalog`: тип 41 — в `ServiceTypes` (салонная маска его не видит, §39.9.1); `NotificationTexts.TypeText` — текст
«Напоминание перед сеансом» (страж перебирает enum).

### §42.2.5 Данные миграцией (идемпотентно, по фиксированным Id)

- Четыре тарифа `Line = 3` (`StaysPlans`-подобный статический класс `BathsPlans` в `Core/Entities/BathsSubscription.cs`):
  `0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b01` «Одна баня» 200 ₽, `MaxResources = 1`; `…9b02` «До 3 бань» 500 ₽, 3; `…9b03` «Без
  ограничения» 1000 ₽, NULL; `…9b04` «Пробный период «Бани»» 0 ₽, NULL, `IsSystemTrial = false` (индекс `IX_SubscriptionPlanConfigs_IsSystemTrial` уникален по таблице и занят триалом «Записи»; триал «Бань» ищется по `BathsPlans.TrialSeedId`, как триал «Дома»). У всех `IsPublic = false`,
  `IsActive = true`, `AllowNotificationChannel = true`. Цены — начальные, по образцу «Домов» (§42.17 п. 1).
- `PlanOptionRules` опции `notifications.whatsapp` для всех четырёх (включая триал — Q-L42-4) с той же доступностью, что у
  платных «Домов».
- Город: новых строк нет (справочник 91 + Шерегеш; R42-11).

### §42.2.6 Миграция — одна (`Cycle42Baths`), закреплённым `dotnet-ef` 8.0.11, один разработчик (BE-42-M)

Up — **только добавления**: 2 таблицы, 5 столбцов с CHECK, данные §42.2.5. Ни одного `DROP`, `ALTER … TYPE`, `RENAME`,
пересоздания индекса или CHECK существующих таблиц. Новые таблицы — в `ShowcaseOwnership.NeverWritten` (`BathsSubscriptions`
— «cycle 42: the «Бани» vertical has no showcase», `StayServiceItemConfirmations` — то же).

`Down()`: удаляет данные линейки 3 (`TrialGrants`/`TrialPhoneRegistrations` с `Line = 3`, `PlanOptionRules` и
`SubscriptionPlanConfigs` тарифов `0c42ba70-…`), обе таблицы и пять столбцов. **Компании `Kind = 3`, устройства `Site = 3`,
строки журналов с `Line = 3` и брони/ресурсы этих компаний в общих таблицах `Down()` не трогает** (как C37-6): после отката
кода без них приложение упадёт на первом `switch` по виду (`UnreachableException`). Поэтому откат при наличии банных компаний
— только с `pg_dump` и после ручного удаления/деактивации компаний `Kind = 3` (строка в `DEPLOY.md` §32). Накат → откат →
накат на пустых данных проверяет `CY42-140` (отдельная база, как CY39-140).

**R42-10:** если цикл 40 (iCal) вольётся раньше, миграция `Cycle42Baths` пересоздаётся поверх его миграции (Designer-снимок и
монотонность — CI), значения `NotificationType` и `StayServiceOrderEventKind` сверяются.

### §42.2.7 Инварианты модели (QA проверяет тестами)

1. Строки `StayServices`/`StayServiceOrders`/`StayServiceSessions` бывают только у компаний `Stays` и `Baths`
   (`SlotVerticals.IsSlotKind`); у `Baths` нет `Houses`, `StayBookings`, `HouseBlocks` (маршрутов нет — матрица изоляции).
2. У ресурса бани `AvailableForHouseBookings = false`; у сеанса бани `StayBookingId IS NULL` (родитель — только заказ).
3. `Capacity IS NULL` ⇒ `GuestsCount IS NULL` у новых броней этого ресурса; `Capacity` задана ⇒ `1 ≤ GuestsCount ≤ Capacity`
   на момент создания (кодом; БД держит только 1…30).
4. Опубликованный ресурс бани имеет `Capacity` (публикация без неё — 409).
5. `BathsSubscriptions.PlanConfig.Line = Baths` (кодом при назначении и в резолвере, как у «Домов»).
6. Все инварианты §39.2.6 (пересечения, родитель сеанса, деньги, журналы, ревизия) — без изменений и для бань.

### §42.2.8 Чего в модели нет (сознательно)

Новых таблиц сеансов, заказов, расписаний, цен, позиций, фото ресурсов, журналов заказов — **нет** (переиспользуются таблицы
цикла 39). Новой таблицы настроек компании — нет (`StaysSettings`). Типа ресурса для фильтра (Q42-12), парильщика (Q42-13),
доплаты за человека (Q42-5 (в)), корзины (Q42-4 (б)) — нет.

---

## §42.3. Вертикаль как данные: `CompanyKindTraits` и `SlotVertical` (A42-1, A42-2)

### §42.3.1 Почему новый вид, а не флаг внутри «Домов»

Флаг (`Kind = Stays` + «это баня») означал бы второй дискриминатор во **всех** местах, где сейчас достаточно `Kind`: каталоги,
гейт, тарифная линейка (`SubscriptionPlanConfig.Line` — `CompanyKind`), однократность триала (`TrialGrants.Line`), сайт
устройства, страж ветвлений. Новое значение `CompanyKind` ложится на всё это без изменения схемы (все столбцы уже `int`), а
изоляция вертикалей сводится к уже существующему правилу «маршрут видит только свой вид». Цена — аудит существующих
ветвлений по виду (§42.3.4): без него банная компания пойдёт по пути салона (например, в лимит мест «Записи»).

### §42.3.2 `CompanyKindTraits` — одна таблица свойств вида (`Services/Companies/CompanyKindTraits.cs`, чистый)

`switch` по всем значениям с `UnreachableException` по умолчанию; новое значение без строки роняет тест
`CompanyKindTraitsTests` (перебирает `Enum.GetValues<CompanyKind>()`).

| Свойство | Services | Orders | Stays | Baths |
|---|---|---|---|---|
| `IsSalon` (записи, мастера, расписание салона) | ✓ | — | — | — |
| `SalonRefusalText` (409 салонных маршрутов) | null | «Это магазин: …» | «Это компания «Дома»: …» | «Это компания «Бани»: записи, услуги и расписание для неё недоступны.» |
| `VisibleOnSalonPublicPage` (`GET /api/companies/{slug}`) | ✓ | ✓ (как сейчас) | ✓ (как сейчас) | **—** (404) |
| `HasCompanyGallery` (`/api/companies/{id}/photos*`) | ✓ | ✓ | — | ✓ |
| `UsesStaffPositions` (`StaffPosition`, только роль `Master`, потолок `Stays:MaxStaffPerCompany`, MAX персоналу без банщика/горничной) | — | — | ✓ | ✓ |
| `HasSalonSeatLimit` (лимит мест «Записи») | ✓ | — (свой лимит «Заказов») | — | — |
| `CityFixed` (город задаёт сервер) | — | — | ✓ (Шерегеш) | — |
| `SiteBaseUrl` (через `PublicSiteLinks`) | ezbook | goods | dom | **bani** |
| `CompanyPageUrl(slug)` | `/company/{slug}` | `/{slug}` | `/{slug}` | `/{slug}` |
| `TariffLine` (линейка подписки) | Services | Orders | Stays | Baths |
| `KindLabel` (админка, тексты биллинга) | «Записи» / «Салон» | «Заказы» / «Магазин» | «Дома» | «Бани» |
| `PhotoOwnerLabel` (`CompanyPhotoTexts`) | … | … | «компании «Дома»» | «компании «Бани»» |

### §42.3.3 `SlotVertical` — две слотовые вертикали (`Services/Slots/SlotVertical.cs`, чистый record + `SlotVerticals`)

`SlotVerticals.Stays`, `SlotVerticals.Baths`, `SlotVerticals.Find(CompanyKind) → SlotVertical?`, `Get(kind)` (бросает для
не-слотовых), `IsSlotKind(kind)`. Поля:

| Поле | Stays | Baths |
|---|---|---|
| `Kind`, `ApiPrefix` | `Stays`, `api/stays` | `Baths`, `api/baths` |
| `Unit` (единица лимита тарифа) | `House` (дома, `MaxHouses`) | `Resource` (опубликованные `StayServices` компаний `Baths` аккаунта, `MaxResources`) |
| `TrialPlanId`, `TrialTerms` (версия, текст, хеш), `TrialDaysOption` | `StaysPlans.TrialSeedId`, `StaysTrialTerms`, `Stays:TrialDays` | `BathsPlans.TrialSeedId`, `BathsTrialTerms` (`baths-2026-10-09`), `Baths:TrialDays` (14) |
| `HasHouses` (режим «к проживанию», `AvailableForHouseBookings`, сеансы в брони дома) | ✓ | — (флаг ресурса всегда `false`) |
| `StandaloneOrdersAlwaysOn` | — (настройка компании) | ✓ (`AcceptServiceOrdersWithoutStay = true` с создания, маршрута смены нет) |
| `RequiresCapacityToPublish` | — | ✓ |
| `PublishLimit` (402 при публикации ресурса) | нет (услуги «Домов» не лимитированы) | ✓ (`billing-account` → тариф → счётчик ресурсов) |
| `ResourceSlugPolicy` | `StaysSlugPolicy.IsValidServiceSlug` (без резерва) | `BathsSlugPolicy` (`bani-routes.json`: шаблон + `reservedResourceSlugs`) |
| `CompanySlugPolicy` | `StaysSlugPolicy` (`dom-routes.json`) | `BathsSlugPolicy` |
| `ResourcePagePath(companySlug, slug)` | `/{c}/uslugi/{s}` | `/{c}/{s}` |
| `LegalKeys` (снимки версий в брони) | `StayServiceBookingNotice`, `StayServiceBookingTerms`, `StayServiceCancellationTerms` | `BathBookingNotice`, `BathBookingTerms`, `StayServiceCancellationTerms` |
| `Wording` (§42.4.4) | `ServiceWording.Stays` (прежние строки байт-в-байт) | `ServiceWording.Baths` («бронь», «EZBOOK Бани», пометка местного времени) |
| `SessionBarLabel` («День услуг») | дом или «без проживания» (как сейчас) | «{имя}, {N} чел.» / «Бронь» |
| `CatalogService` для сброса кеша | `StaysCatalogService` | `BathsCatalogService` |

### §42.3.4 Аудит существующих ветвлений по виду (BE-42-K, обязательная часть цикла)

Каждое место ниже переводится на `CompanyKindTraits`/`SlotVertical` или получает явную ветку `Baths` в `switch`. Список сверен
grep'ом по `CompanyKind.Stays` на ветке; исполнитель повторяет grep (`CompanyKind\.`, `kind ==`, `Kind ==`, `is CompanyKind`) и
закрывает **все** найденные места, а не только этот список.

| Место | Сейчас | Для `Baths` |
|---|---|---|
| `CompanyKindGuard.RefusalTextFor` | `switch` 3 вида | + текст «Бани» (`CompanyKindTraits.SalonRefusalText`) |
| `CompaniesController.GetBySlug` | любой вид | `VisibleOnSalonPublicPage = false` → 404 |
| `CompaniesController.GetKindsSummary` | 3 вида | + `baths` |
| `CompaniesController.Update` (город «Домов») | `Kind == Stays` | без изменений (у бани город меняется) |
| `CompanyPhotosController` (4 места), `CompaniesController:475` | отказ `Stays` | по `HasCompanyGallery` |
| `CompanyPhotoTexts` | `switch` | + «компании «Бани»» |
| `CompanyMembersController` (156, 201, 208, 289, 315) | `Kind == Stays` | по `UsesStaffPositions`; тексты по виду; лимит мест салона — по `HasSalonSeatLimit` |
| `StaffMaxLinkService` (57, 79) | `Stays && не горничная` | по `UsesStaffPositions` |
| `CompanyCreationService` | `isStays/isShop/isSalon` | + ветка вертикали: `SlotVerticals.Find(kind)`; город — `CityFixed` ? из конфигурации : из запроса |
| `CompanyTransferService` (189, 268) | особый путь `Stays` | тот же путь для `Baths` (лимитов мест нет) |
| `PublicSiteLinks` | `switch` 3 вида, методы «Домов» | + `BathsBaseUrl`; методы по виду (§42.4.5) |
| `PushController` (`siteUrls`) | 3 сайта | + `baths` |
| `OwnerSubscriptionService` (40, 266…315) | `BuildStaysAsync` | `BuildSlotLineAsync(vertical)` для `Stays` и `Baths` (§42.5.5) |
| `AdminBillingController` (232, 234, 496, 532…586, 747, 777) | ветки `Stays` | ветки по `SlotVertical` (таблица подписки, триал, счётчик единиц) |
| `AdminPlansController` (45, 107, 158, 299, 349…363) | `MaxHouses` только `Stays`, защита триала «Домов» | + `MaxResources` только `Baths`, защита триала «Бань», подписчики из `BathsSubscriptions` |
| `AdminAccountDtoBuilder:148-150` | блок `staysSubscription` | + `bathsSubscription` |
| `AdminController:552` | сброс кеша dom | + `BathsCatalogService.InvalidateBase()` для `Baths` |
| `BillingTexts:70` | 3 линейки | + «Бани» |
| `NotificationChannelsController:626` | `switch` | + `Baths` (тот же текст, что `Stays`) |
| `ChannelEligibility`, `SubscriptionResolver:195-249` | «Записи» ∨ «Заказы» ∨ «Дома» | + «Бани» — обобщить до списка линеек-дополнений (§42.5.6) |
| `StaysAccessResolver:30` | `Kind == Stays` | параметр `CompanyKind kind` (по умолчанию `Stays` — у всех существующих вызовов) |
| `ServiceSlotService:53`, `ServiceCatalogService:89`, поиск заказа по токену | `Kind == Stays` | параметр `kind` от контроллера-наследника |
| `StaysPlanResolver`, `StaysTrialService`, `StaysCompanyService.EvaluateGateAsync` | только «Дома» | по `SlotVertical` компании (§42.5) |
| `StaysCatalogService`, `StaysHousePageService`, `StayBookingCreationService`, `StaysCompaniesController.GetMine` | `Kind == Stays` | **без изменений** (дома — только «Дома») |
| фронт `src/`: `companyKind.ts`, `useWebPush.ts` (`hasStays`), `api/push.ts` (`PushSite`), `api/plans.ts`, `api/adminBilling.ts`, `api/admin.ts`, `BillingPage.tsx`, `CompanyPage.tsx:91`, `EmbedPage.tsx:35,54`, `types/index.ts` | `'Stays'` | + `'Baths'` (FE-42-7) |

**Стражи (BE-42-K):**
- `CompanyKindBranchGuardTests` — регэксп расширяется на тернарник по `CompanyKind.Stays` и `CompanyKind.Baths` (`CompanyKind\.(Orders|Stays|Baths)\s*\?`);
- новый `CompanyKindExhaustiveTests` — для каждого публичного статического метода, принимающего `CompanyKind`, из списка
  (`CompanyKindTraits.*`, `CompanyKindGuard.RefusalTextFor`, `PublicSiteLinks.SiteBaseUrl/CompanyPageUrl`, `CompanyPhotoTexts`,
  `BillingTexts.*Line*`, `SlotVerticals.Find`) — вызов со **всеми** значениями enum без исключения;
- функциональная матрица изоляции `Cycle42KindIsolationTests` (`CY42-01…09`): компания каждого из 4 видов × представительные
  маршруты каждой вертикали (`/api/companies/{slug}`, салонный маршрут, `/api/shops/*`, `/api/stays/public/companies/{slug}`,
  `/api/stays/companies/{id}`, `/api/stays/public/services/{id}/starts`, `/api/stays/service-orders/public/{token}`, все
  префиксы `/api/baths/*`) → ожидаемый ответ (200 / 404 / 409 текст) — таблица в тесте.

---

## §42.4. Слотовый движок цикла 39 без копий (A42-2)

### §42.4.1 Контроллеры: абстрактная база + два наследника

Логика контроллеров цикла 39 (порядок проверок, коды, тексты, работа с файлами, push-подписки) переносится в абстрактные
базы `Controllers/Slots/*Base.cs` с `protected abstract SlotVertical Vertical { get; }`. Наследники — **тонкие** (атрибуты
класса + `Vertical` + конструктор):

| База (`Controllers/Slots/`) | Действия | Наследник «Дома» (прежнее имя, прежний `[Route]`) | Наследник «Бани» (`Controllers/Baths/`) |
|---|---|---|---|
| `SlotServicesPublicControllerBase` | страница ресурса, availability, starts, quote, orders (5) | `StaysServicesPublicController` `api/stays/public` | `BathsServicesPublicController` `api/baths/public` |
| `SlotServiceOrdersPublicControllerBase` | get, payment-proofs POST/GET, cancel, push ×2 (6) | `StayServiceOrdersPublicController` `api/stays/service-orders` | `BathServiceOrdersPublicController` `api/baths/service-orders` (+ `my`) |
| `SlotServicesCabinetControllerBase` | 27 действий кабинета услуг (§39.26, §39.28) | `StaysServicesController` | `BathsServicesController` `api/baths/companies/{companyId:guid}/services` |
| `SlotSessionsControllerBase` | service-day, starts, availability, quote, список, ручная, карточка, confirm, reject, cancel, файл (11) | `StaysServiceSessionsController` (+ `bookings/{bookingId}/sessions` — **остаётся только в наследнике «Домов»**) | `BathsSessionsController` `api/baths/companies/{companyId:guid}` |
| `SlotCompanySettingsControllerBase` | payment-details, provider, slug, qr, notification-settings GET/PUT (6) | `StaysCompaniesController` (прочие его действия — на месте) | `BathsCompaniesController` (+ create, my, slug-check, trial ×2, карточка, settings, schedule, revision) |
| — | каталог, города, страница комплекса (3) | — | `BathsPublicController` `api/baths` |

Правила, чтобы эталон маршрутов «Домов» не изменился ни на строку (это и есть проверка регресса, BE-42-7):
- **атрибуты класса** (`[ApiController]`, `[Route]`, `[Authorize]`) — только на наследниках, ровно как сейчас у классов цикла 39;
  база — `abstract`, без атрибутов класса;
- **атрибуты действий** (`[HttpGet(…)]`, `[EnableRateLimiting]`, `[RequiresOwnerTerms]`, `[DemoForbidden]`,
  `[RequestSizeLimit]`) — на действиях базы, как сейчас;
- имена наследников «Домов» и их пространство имён не меняются.

Ответы, которые различаются по вертикали (карточка компании `StaysCompanyManageDto` vs `BathsCompanyManageDto`), база
получает через `protected abstract Task<ActionResult> ManageResultAsync(Company, StaysMyRole, CancellationToken)`.

### §42.4.2 Сервисы: вид — параметром только в точках поиска

Сервисы цикла 39 (`ServiceSlotService`, `ServiceCatalogService`, `ServiceOrderCreationService`, `ServiceOrderTransitionService`,
`ServiceOrderProofService`, `ServiceDayService`, `ServiceDtoMapper`, `StaysAccessResolver`) получают `CompanyKind kind` **только**
там, где ищут компанию/ресурс/бронь по внешнему идентификатору (slug, `serviceId`, токен, `companyId`) — отсюда изоляция:
`FirstOrDefaultAsync(c => … && c.Kind == kind)`. Дальше вертикаль выводится из найденной компании
(`SlotVerticals.Get(company.Kind)`): тексты, ссылки, единица гейта, правовые ключи. Фоновые пути (таймер удержания,
напоминания, диспетчер push, retention, выгрузка) идут по данным и вертикаль берут из `company.Kind` — параметра у них нет.

Совместимость: у всех существующих вызовов `kind` = `CompanyKind.Stays` (параметр со значением по умолчанию только у
`StaysAccessResolver`, чтобы не трогать ~40 вызовов цикла 37; у остальных — обязательный, компилятор покажет все места).

### §42.4.3 Хрупкая зона C39-10 — что НЕ меняется

- Единственные писатели: сеансы — `ServiceSessionWriter`, расписание — `ServiceScheduleWriter`, журнал заказа —
  `StayServiceOrderEventLog`, каскад брони — `StayBookingReleaser`. Новых путей записи сеансов **нет**: бронь бани создаётся
  тем же `ServiceOrderCreationService.CreateAsync`, ручная — тем же путём ручного заказа.
- `EX_StayServiceSessions_NoOverlap`, advisory-lock `stay-service:{serviceId}`, порядок замков §39.5.2 — без изменений.
- **Единственное добавление в порядок замков:** публикация ресурса бани берёт `billing-account:{accountId}` **первым** (до
  `stay-service:{id}`, если путь публикации его берёт). Других путей, где `billing-account` берётся после замка услуги, нет
  (создание компании, триал, назначение тарифа — только `billing-account` и `company-slug`). Взаимоблокировка исключена.
- Число гостей пишется в строку заказа в той же вставке; на занятость и цену не влияет.
- Регресс: CY39-90…104 и CY37-60…75 целиком + параллельные тесты бань (`CY42-50…55`).

### §42.4.4 Тексты вертикали — `ServiceWording` (BE-42-W)

Гостевые и персональные строки движка, где есть слова вертикали («заказ», «заказ услуги», заголовок push «ezbook · Дома»),
выносятся в record `ServiceWording` (`Services/Slots/ServiceWording.cs`): статусы и `outcomeText` (`ServiceTexts.OutcomeText`),
тексты возврата для конечного статуса, `OrderAlreadyCancelled`, `ServiceOrderDone`, `HoldExpired`, `ProofNotAllowed`,
`TooManyHeldOrders`, `TooManyOrdersPerDay`, `RefundTerminal`, события журнала для персонала, тексты уведомлений
(`ServiceNotificationTexts` — персоналу, мессенджер, push), заголовок push гостю, функция пометки местного времени.
`ServiceWording.Stays` возвращает **прежние строки байт-в-байт** (прежние константы становятся ссылками на него — тесты цикла 39
не меняются); `ServiceWording.Baths` — слова «бронь», заголовок «EZBOOK Бани», суффикс « (время местное, {город})» к
гостевому времени в сообщениях. Страж `BathsWordingGuardTests`: все строки `ServiceWording.Baths` (на примерных данных) без
«заказ», «дом», «прожив», «заселен», «заезд», «бизнес-день», «задаток», «невозвратн», «депозит», «туристическ».

### §42.4.5 Адреса — `PublicSiteLinks` по виду

`SiteBaseUrl(Baths)` ← `PublicSites:BathsBaseUrl` (по умолчанию `https://bani.ezbook.ru`, пустое значение = по умолчанию).
Новые методы по виду: `ServiceOrderPageUrl(kind, token)` (`/s/{token}`), `CabinetServiceSessionUrl(kind, companyId, sessionId)`,
`ResourcePageUrl(kind, companySlug, serviceSlug)` (через `SlotVertical.ResourcePagePath`), `SlotSubscriptionUrl(kind)`
(`/cabinet/subscription`). Прежние методы «Домов» (`StayServiceOrderPageUrl` и др.) остаются обёртками с `Stays` — их
вызывающие не меняются. Все места, где ссылка строится для **брони/ресурса неизвестной вертикали** (планировщик уведомлений,
выгрузка ПДн, `CreateServiceOrderResponse.orderUrl`, `ServiceManageDto.publicUrl`), переходят на методы по виду.

---

## §42.5. Тарифная линейка «Бани», гейт, триал (A42-3, A42-9)

### §42.5.1 Линейка

`Line = Baths`, таблица `BathsSubscriptions`, тарифы §42.2.5, `MaxResources`. Цены и лимиты меняет суперадмин без деплоя;
системный триал удалить или выключить нельзя (409, как `StaysTrialPlanText`). `IsPublic = false`; `PricingCatalogCache`
фильтрует по `Line ∈ {Services, Orders}` — линейка «Бани» в `/api/pricing` не попадает (тест `CY42-28`, Т42-14).

### §42.5.2 Гейт — единица вертикали

`StaysBookingGate.Evaluate(…, int publishedUnits, int? maxUnits, …, GateUnit unit)`: проверки и порядок прежние; текст и код
превышения лимита — по единице: `House` → `OverHouseLimit` и прежний текст байт-в-байт; `Resource` → `OverResourceLimit`
«Опубликовано {N} {ресурс/ресурса/ресурсов} при лимите {M}: гости не могут бронировать. Снимите лишние ресурсы с публикации
или смените тариф» (векторы `gate`). `StaysCompanyService.EvaluateGateAsync(company, settings, prepay)` берёт план, счётчик
и единицу из вертикали компании. **Предоплата для гейта компании «Бани»** (чек-лист, карточка кабинета, «база» каталога,
страница комплекса) — максимум `StandalonePrepayPercent ?? 0` опубликованных неархивных ресурсов: так «нет реквизитов» видно
владельцу заранее; для брони — предоплата конкретного ресурса (как в цикле 39).

### §42.5.3 План линейки — `StaysPlanResolver` → обобщение по вертикали

`StaysPlanResolver` получает методы с вертикалью: `GetForAccountAsync(SlotVertical, accountId)` читает `StaysSubscriptions` или
`BathsSubscriptions`, `Resolve` проверяет `plan.Line == vertical.Kind` и `IsTrial` по `vertical.TrialPlanId`; `MaxUnits` —
`MaxHouses` или `MaxResources`; `CountPublishedUnitsAsync(vertical, accountId)` — дома или ресурсы. Прежние методы «Домов»
остаются обёртками (вызывающие цикла 37 не меняются). `Warning(...)` — тексты с единицей вертикали (§42.28 контракта).
Имя класса не меняется (минимум правок), в комментарии — что он теперь обслуживает обе линейки без бесплатного уровня.

### §42.5.4 Триал

`StaysTrialService` → методы с вертикалью (`TrialGrants.Line`, `TrialPhoneRegistrations.Line`, `TrialPlanId`, условия).
`BathsTrialTerms` (`Services/Baths/BathsTrialTerms.cs`): версия `baths-2026-10-09`, текст — черновик §11.7 обзора с `{N}` и
фразой Q-L42-4 «Сообщения гостям через подключённый канал WhatsApp или MAX доступны весь пробный период.», хеш SHA-256,
пометка «DRAFT until a lawyer reads it». `TrialGrant.TermsVersion/TermsTextSha256/TermsShownAtUtc/TermsAcknowledgedAtUtc` —
как у «Домов» (Т42-10). `MailingWindowDays = 0` (механика окна рассылок — салонная). Однократность — в линейке:
`TrialGrants (BillingAccountId, Line)` и `TrialPhoneRegistrations (Line, PhoneKeyHash)` — уникальные индексы уже с линейкой
(цикл 37), новых индексов не нужно.

### §42.5.5 Кабинет подписки и админка

- `GET /api/billing/subscription?line=Baths` — `OwnerSubscriptionService.BuildSlotLineAsync(SlotVertical)` (общий для «Домов» и
  «Бань»; блок `Stays` или `Baths`, `AvailablePlans` с `MaxHouses` или `MaxResources`, поле другой линейки опускается
  `WhenWritingNull`). `POST …/subscription/request` с `line: Baths`.
- Админка: фильтр линейки, `MaxResources` (только `Baths`), блок `bathsSubscription` в карточке аккаунта, назначение линейки
  `Baths` (`AdminBillingController.AssignOrdersSubscriptionAsync` → обобщается по `SlotVertical` для `Stays` и `Baths`:
  таблица подписки, счётчик единиц, защита триала), очередь заявок (текущий тариф линейки), фильтр компаний `kind=Baths`,
  статистика компании (P1, US-42-04: `bathsStats {resourcesCount, ordersLast30Days}`).

### §42.5.6 Опция канала уведомлений

`ChannelEligibility.IsAllowedAsync` и `SubscriptionResolver` (оплаченные номера, §457.4 цикла 24) сейчас знают «Записи» ∨
«Заказы» ∨ «Дома» отдельными параметрами (`PaidNumbers(…, hasStays, staysUsable, staysAvailability)`). Обобщить до списка
«дополнительных линеек» `IReadOnlyList<LineChannelGrant(line, hasCompanies, planUsable, availability)>` — для `Orders`, `Stays`,
`Baths` — с юнит-тестами `PaidNumbers` (прежние кейсы байт-в-байт + кейсы «Бань»). Результат для аккаунтов без банных
компаний не меняется.

### §42.5.7 После окончания тарифа или триала (Т42-10)

Гейт проверяется **только** при публичном создании брони (`orders`), расчёте (флаг `acceptingBookings`) и в каталоге;
публикация ресурса проверяет тариф. Все действия персонала над принятыми бронями (подтвердить/отклонить оплату, отменить,
«День услуг», карточка, файлы, расписание банщика, ручная бронь) от тарифа **не зависят** — так уже устроен цикл 39, цикл это
фиксирует тестами `CY42-60…63` (триал истёк → бронь `AwaitingPaymentCheck` подтверждается, отменяется, видна в «Дне услуг»).

---

## §42.6. Роли: администратор и банщик (Q42-9)

Таблица прав `StaysAccess` не меняется: `Manager` = «Администратор», `Housekeeper` = «Банщик» (подписи — фронт bani и тексты
`CompanyMembersController` по виду). Банщик (`ViewSchedule`) видит только `GET …/schedule` и карточку компании без настроек;
ответ расписания — закрытая схема без телефонов, сумм, файлов, реквизитов, журнала, статуса оплаты (`CY42-70`).
Комментарий гостя банщику — только при `HousekeeperSeesGuestComment = true` (ЮР-5). Уведомления банщику не идут (планировщик
уже адресует только владельцу и `Manager`). MAX-карточка персонала — владельцу и администратору (`UsesStaffPositions`).

---

## §42.7. Вместимость и число гостей (A42-8, Q42-5 (б), Т42-06)

- Хранение: `StayServices.Capacity` (1…30), `StayServiceOrders.GuestsCount` (снимок). Чистая `GuestsCountRules.Validate(guests,
  capacity)` (векторы `guests`): capacity null → значение игнорируется (null); иначе обязательно 1…capacity.
- Правило — **данными**, без ветки по виду: у услуг «Домов» вместимости нет (dom её не задаёт) → число гостей не спрашивается
  и не хранится, поведение dom не меняется.
- Публикация ресурса бани требует вместимость (`SlotVertical.RequiresCapacityToPublish` → 409 `ServiceNoCapacity`).
- Цена, предоплата, возврат от числа гостей не зависят — `service-vectors.json` не меняется.
- Видимость: гостю — «до N человек» на карточке, странице ресурса, в форме («Сколько человек придёт, включая детей»); в брони —
  на странице брони, в карточке персонала, в расписании банщика, в выгрузке ПДн. При обезличивании число остаётся.
- Изменить число гостей после создания нельзя (как и время — только отмена и новая бронь).

---

## §42.8. Позиции: фильтр алкоголя и табака, тексты владельца (Т42-05, Т42-12)

### §42.8.1 Мягкий фильтр с подтверждением — одна реализация для «Бань» и услуг «Домов»

- `RestrictedItemFilter.Match(text) → string[]` (чистый, векторы `restrictedItems`): основы ищутся с начала слова, без учёта
  регистра, ё = е. Список основ — `bani-vectors.json` `stems` (константа в коде, тест сверяет с файлом).
- Путь сохранения позиции (общий для обоих префиксов): совпадения есть и `confirmRestricted != true` → 409
  `ItemRestrictedConfirmationRequired` с `markers` и `noticeText`; с подтверждением — сохранение и строка
  `StayServiceItemConfirmations` в той же транзакции через `ServiceItemWriter` (новый единственный писатель позиций и
  журнала подтверждений; прежний код сохранения позиций переезжает в него без изменения поведения).
- Почему и для «Домов»: проект D3 7а.4 (`LEGAL_REVIEW_CYCLE42.md` §11.6) распространяется на услуги «Домов»; один путь
  сохранения позиций дешевле и честнее, чем развилка по виду. Цена для dom: новый 409 у позиций с такими словами (dom
  получает диалог вместе с общей вкладкой «Позиции», §42.12.1). Это отклонение «поведение dom меняется» — §42.18 п. 3.
- Суперадминский механизм «скрыть позицию» (обзор §9.1) — **не в цикле**: долг (C42 в `CURRENT_STATE.md`).

### §42.8.2 Тексты владельца — `OwnerTextChecks.Check(text)` (векторы `ownerText`)

Описание ресурса/услуги и названия позиций: отказ 400 на «задаток/невозвратн/депозит» (жёстко — «нигде», обзор §10);
мягкие `warnings`/`contentWarnings` (не блокируют сохранение): условия отмены, обязательные доплаты, обещания пользы для
здоровья, паспорт, номер карты, паспортные данные. Тоже для обоих префиксов (§42.18 п. 3).

---

## §42.9. Уведомления, ссылки, push, напоминание (A42-6, A42-11)

### §42.9.1 Ссылки
Все абсолютные ссылки в уведомлениях о бронях бань — `https://bani.ezbook.ru/…` через `PublicSiteLinks` по виду компании
(§42.4.5): гостю — `/s/<token>`, персоналу — `/cabinet/<companyId>/service-sessions/<sessionId>`. Push гостю несёт
относительный `url` `/s/<token>` (подписка сделана с bani — сервис-воркер bani откроет свой сайт).

### §42.9.2 Типы и каналы
Типы цикла 39 для заказов (28, 29, 30 персоналу; 31…36 гостю) — без новых значений, кроме 41 (§42.9.5). Планировщик
`StayNotificationPlanner.OnOrderEventAsync` берёт `ServiceWording` и ссылки из вертикали компании заказа. Каналы и флаги —
как в цикле 39: push гостю (если подписан и `GuestWebPushEnabled`), мессенджер гостю (галочка брони, `GuestMessengerEnabled`,
оплаченный канал), push и MAX персоналу (`StaffMaxEnabled`, `STAFFMAX_ENABLED`). Реквизиты — только в мессенджер этой брони.
Витрина/демо — `ShowcaseOutboundGuard`.

### §42.9.3 Push гостю
Заголовок «EZBOOK Бани» (Q42-7), тексты без ПДн, адресов, сумм и токена (Т42-15). Сервис-воркер
`frontend/bani/public/sw.js` — копия `dom/public/sw.js` (без `fetch` и Cache API), маршрутизация нажатия — общая таблица
`src/test/workerRouting.ts` дополняется строкой bani.

### §42.9.4 Пометка местного времени (Т42-08)
`ServiceWording.Baths.GuestTimeSuffix(cityName)` → « (время местное, {город})» добавляется к гостевому времени в
сообщениях мессенджера; в DTO — `localTimeNote` «Время местное, {город}» (фронт показывает рядом с выбором, в форме, на
странице брони — всегда, без сравнения поясов: проще и честнее). У «Домов» суффикса нет (байт-в-байт).

### §42.9.5 Напоминание перед сеансом (P1, US-42-23, режется первым — R42-5)
- Настройка: `StaysSettings.ServiceReminderHours` (null — выключено; у бани с создания 3; маршрут `PUT /api/baths/companies/{id}/settings`
  `sessionReminderEnabled` + `sessionReminderHours`). У «Домов» маршрута нет → у них выключено.
- Чистая `SessionReminderPolicy` (`Services/Slots/SessionReminderPolicy.cs`, векторы `sessionReminder`): `Moment(start, N, tz)` и
  `Decide(moment, created, now, start, tz) → Send | Wait | Skip`.
- Задача `stays-scheduled-messages` (существующая, `main`, 60 с) — **третий проход**: брони `Confirmed`, `SessionReminderAtUtc IS
  NULL`, компании с `ServiceReminderHours IS NOT NULL`, начало сеанса **позже текущего момента и не дальше 25 ч** (индексный
  отбор по сеансу), пачками по 50. `Send` → условный `UPDATE … SET "SessionReminderAtUtc" = now WHERE "Id" = … AND
  "SessionReminderAtUtc" IS NULL` (однократность) → событие `SessionReminderSent` (журнал + ревизия) → постановка в доступные
  каналы (мессенджер — только при галочке брони; push — если подписан). Отметка ставится и без каналов: блок на странице брони
  появляется всегда. `Wait` и `Skip` → **ничего не пишется**: бронь выпадает из отбора, когда начало сеанса прошло, поэтому
  повторные проверки одной брони ограничены сутками. Текст фиксированный (без редактора), без рекламы.
- Страница брони: `sessionReminder {sentAtUtc, text}` (не null ⇔ `SessionReminderAtUtc` не null) — текст собирается на лету из
  снимков брони (ресурс, время гостевым форматом, компания), имени гостя в нём нет — стирать при обезличивании нечего.

---

## §42.10. Публичная часть: адреса, каталог, комплекс (A42-5, A42-7, A42-10)

### §42.10.1 Адреса bani — `contracts/cycle42/bani-routes.json`
`/` — главная (шаблон `ServiceLanding` с панелью), `/:slug` — комплекс, `/:slug/:resourceSlug` — ресурс, `/s/:token` — бронь,
`/bookings` — «Мои брони», `/cabinet/…`, правовые страницы. `BathsSlugPolicy` (по образцу `StaysSlugPolicy`, ресурс
`bani-routes.json`): адрес компании — шаблон, длина, резерв `reservedSlugs`, префикс `primer-`, занятость среди **всех**
компаний без учёта регистра (под `company-slug`); адрес ресурса — `resourceSlugPattern` + `reservedResourceSlugs`, уникален в
компании (существующий индекс `(CompanyId, Slug)` таблицы `StayServices`). Резерв слов «бани» в адресах домов
(`reservedHouseSlugs`: `bani`, `banya`) — уже есть с цикла 39.

### §42.10.2 Каталог — `BathsCatalogService` (по образцу `StaysCatalogService`)
- «База» (кеш 30 с, `IMemoryCache`, сброс — `InvalidateBase()`): опубликованные неархивные ресурсы компаний `Baths`
  (`IsActive`, `ShowInPublicListing`) + обложка + `priceFromRub` (`MIN(PriceRub)` правил) + гейт компании. Гейт для пачки
  компаний — пакетно: подписки аккаунтов (один запрос `BathsSubscriptions`), счётчики опубликованных ресурсов по аккаунтам
  (один `GROUP BY`), настройки (`StaysSettings` одним запросом); `StaysBookingGate.Evaluate` в памяти с предоплатой ресурса.
- Фильтр города — по базе в памяти. Фильтр даты (P1) — `ServiceSlotService.HasStartsBatchAsync(scopes, date)`: окна, ручные
  даты, правила цены, активные сеансы (с родителем для «истёкшего удержания») кандидатов — 4 запроса `WHERE "ServiceId" =
  ANY(@ids)`, затем `ServiceSlotCalculator.Calculate` на каждого кандидата в памяти. Цель — p95 < 500 мс на 150 ресурсах
  (замер — DO-42-07, P2).
- Сброс базы: публикация/снятие/архив/удаление ресурса бани, блокировка компании (`AdminController`), смена
  `showInCatalog`, назначение тарифа (`AdminBillingController`, триал «Бань») — плюс 30-секундный TTL.

### §42.10.3 Страница комплекса
`GET /api/baths/public/companies/{slug}` (`BathsPublicController`): компания, галерея, публичная часть исполнителя, гейт с
предоплатой-максимумом, ресурсы карточками той же формы, что каталог.

### §42.10.4 «Забронировать ещё в этом комплексе» (Т42-09)
`bookAgainUrl` = `/<companySlug>` без параметров. Вошедшему имя и телефон подставляет форма из аккаунта (как сейчас);
анониму — из `sessionStorage` вкладки (`bani:guest` — имя и телефон, пишется при успешном создании брони, живёт до закрытия
вкладки). Галочка мессенджера — снова `false`, позиции — 0, число гостей — не подставляется. Уведомление о данных, условия и
правило отмены показываются заново (это новая форма).

### §42.10.5 «Мои брони»
`GET /api/baths/service-orders/my` (§42.26 контракта): выборка по `GuestUserId` и, при подтверждённом номере, по `GuestPhone`
через `SubjectScopeResolver` с маркером `// SUBJECT-PHONE-GATE:`; только компании `Baths`; 12 месяцев.

---

## §42.11. ПДн, retention, правовые тексты (A42-12)

### §42.11.1 Права субъекта
Выгрузка (`SubjectDataExporter.stayServiceOrders[]`) уже охватывает все заказы услуг — добавляются `guestsCount`, `site` и ссылка
по виду. Удаление аккаунта и обезличивание (`StayPersonalData.Erase` для заказов) — без изменений (число гостей остаётся).
Отзыв согласия `ProviderDelivery` — уже по всем активным заказам номера. Тесты на банной компании — `CY42-80…85`.

### §42.11.2 Retention
Правила цикла 39 работают по таблицам, а не по виду компании, — бани охвачены без правок. Доказательство — функциональные тесты
каждого правила на брони компании `Baths` (сухой прогон + реальный). Маскирование пути в логах приложения
(`LoggingExtensions.MaskSensitiveRequestPath`) — + префикс `/api/baths/service-orders/public/`.

### §42.11.3 Правовые тексты (Т42-01, -02, -04, -07)
- Новые ключи (константы `LegalTextKey` **вне `All`**): `BathBookingNotice`, `BathBookingTerms`, `BathPublicContactsNotice`,
  `BathPositionsOwnerNotice`, `BathCapacityOwnerNotice`, `BathPaymentProofNotice`, `BathPaymentRequisitesOwnerNotice`,
  `BathOwnerCancelNotice`. Запасные тексты — `frontend/bani/src/utils/baniTexts.ts` по черновикам `LEGAL_REVIEW_CYCLE42.md` §11.0–§11.5
  (подстановка компании — `data-legal-value|when|unless`, без «ТРЕБУЕТСЯ ТЕКСТ»). Переиспользуются: `StayServiceCancellationTerms`,
  `StayServiceCommentNotice`, `StayServiceCancellationOwnerNotice`, `StayMessengerConsent`, `StayServiceSafetyOwnerNotice`.
- Снимки версий в брони — ключи вертикали (§42.3.3); версия null, пока ключа нет в манифесте (как у «Домов»).
- Т42-01: в `dom/src/utils/stayTexts.ts` для `StayServiceBookingNotice` и `StayServiceBookingTerms` константы `COMPANY`/`COMPANY_CAP`
  заменяются нейтральными `COMPANY_SERVICE`/`COMPANY_SERVICE_CAP` («компанией, которая оказывает эту услугу»).
- Т42-07: новая запасная редакция `StayServiceSafetyOwnerNotice` (+ фраза о лечебном эффекте и медицинских услугах) — общая.
- Т42-04: тест фронта bani — модули bani не ссылаются на запрещённые ключи (список §42.37.3 контракта).
- `legal-drafts/` цикл не правит; правки D1–D4 и РКН — задача заказчика/legal-counsel (Т42-17).

---

## §42.12. Фронтенд (A42-4)

### §42.12.1 Перенос общего кода услуг из dom в `frontend/src` (FE-42-1a, FE-42-1b)

**Почему переносом, а не копией:** SPEC и задача запрещают вторую копию движка; ESLint запрещает импорт `dom/` из других
приложений. **Почему через реэкспорт-прокладки:** dom импортирует эти модули из ~40 мест; прокладка (`export * from '@/…'`)
оставляет импорты dom прежними и сводит правку dom к одной строке на модуль.

| Было (`frontend/dom/src/`) | Стало (общее) | В dom |
|---|---|---|
| `utils/{businessClock, serviceTimeFormat, serviceMoney, serviceWindows, serviceDay, serviceForms, serviceOrderForm, serviceSelection, paymentProof, idempotency, serverClock}.ts` (+ тесты) | `src/utils/slots/*` (тесты переезжают с прежними ID; `serviceVectors.test.ts` читает тот же `contracts/cycle39/service-vectors.json`) | прокладки |
| `utils/stayError.ts` (общая часть: `httpStatus`, `plainBody`, `readConflict`, `getStayErrorMessage`) | `src/utils/slots/slotError.ts` | прокладка (house-специфичное — остаётся) |
| `api/{publicServices, serviceOrders, staysServices}.ts`, сеансовая часть `staysBoard.ts` | `src/api/slots.ts` — фабрика `createSlotApi(prefix: '/stays' | '/baths')` → `{ publicServices, orders, cabinet, sessions }` | `export const publicServicesApi = slotApiStays.publicServices` и т. п. — прежние имена |
| `components/{StatePanels, StayNotice, Stepper, CopyButton, HoldCountdown, ProofUploader, ProofFileButton, ProviderBlock, LinkButton, GuestPushCard}.tsx`, `components/cabinet/{formParts, PaymentCard, ProviderCard, QrDialog}.tsx` | `src/components/slots/ui/*` (`StayNotice` → `SlotNotice` с тем же API) | прокладки |
| `components/services/{ServiceTimePicker, ServiceQuoteSummary, ServiceOrderPanel, ServiceTermsModal, CancelSessionDialog, ConfirmDialog}.tsx`, `cabinet/*`, `staff/{ServiceDayView, SessionsList, BasisSelect, ManualServiceOrderDialog}.tsx` (+ тесты) | `src/components/slots/services/*` | прокладки |
| тела страниц `ServicePage`, `ServiceOrderPage`, `cabinet/{ServicesPage, ServiceCreatePage, ServiceEditPage, ServiceDayPage, ServiceSessionPage}` | `src/components/slots/views/*View.tsx` (компоненты, не страницы — правило `src/components/**`) | страницы dom — обёртки `<XxxView />` |
| `hooks/{useStayGuestPush, useStayText, useMediaQuery}` | `src/hooks/slots/*` (`useLegalText(key, fallbacks)`) | прокладки |

**Остаются в dom** (специфика домов): `StayServicesBlock`, `BookingSessionsBlock`, `ArrivalReminderBlock`, `StaffAddServiceDialog`,
`BoardServicesGroup`, всё о домах, ночах, шахматке, напоминании накануне заезда.

**Вертикаль — контекст `SlotVerticalProvider`** (`src/components/slots/SlotVerticalContext.tsx`):
`{ kind: 'Stays' | 'Baths'; api: SlotApi; paths: { resourcePage(companySlug, slug), orderPage(token), companyPage(slug),
cabinetResource(companyId, id), cabinetSession(companyId, id), cabinetDay(companyId, date) }; legal: { keys, fallbacks };
words: { resource, resources, resourcesTitle, booking, myBookings, … }; features: { stayMode, capacity, houseBookingsToggle,
photoPeopleNotice } }`. dom оборачивает приложение в `staysVertical` (значения дают **ровно прежние** строки и адреса), bani — в
`bathsVertical`. Компоненты не знают вида — читают контекст. Без провайдера компонент падает с понятной ошибкой (тест).

**Критерий готовности переноса:** `npm run test:area -- stays` зелёный **без изменения ожиданий** в тестах dom (меняются только
пути импортов в перенесённых тестах); `tsc -p tsconfig.dom.json`, `npm run build:dom` и ESLint чисты; эталон разметки
(`landingReference.ts`) не тронут. Для dom новые элементы (поле вместимости, диалог фильтра позиций) появляются только по
`features` и 409 сервера.

### §42.12.2 Приложение `frontend/bani` (FE-42-2…FE-42-6)

```
frontend/bani/
├── index.html                 noindex (meta robots), <div id="root">, манифест, иконки
├── public/                    sw.js (копия dom, без fetch/Cache API), manifest.webmanifest («EZBOOK Бани»), favicon.svg/.ico, icon-192/512, apple-touch-icon
└── src/
    ├── main.tsx, BaniApp.tsx  маршруты bani-routes.json; SlotVerticalProvider(bathsVertical); UpdateBanner; BaniNavbar/BaniFooter («EZBOOK Бани»)
    ├── vertical.ts            bathsVertical (api = createSlotApi('/baths'), paths, legal keys, words, features)
    ├── api/                   bathsPublic.ts (catalog, cities, company), bathsCompanies.ts, bathsOrders.ts (my)
    ├── landing/               baniLanding.ts (LandingPanelConfig, backdrop 'none'), baniFaq.ts (6–8 вопросов), configs.guard.test.ts
    ├── components/catalog/    BathSearchPanel (город, дата P1; URL — единственное общее состояние), BathCatalog, BathResourceCard
    ├── hooks/                 useCatalogFilters (city, date, page)
    ├── pages/                 CatalogPage (одна строка <ServiceLanding …/>), CompanyPage, ResourcePage, OrderPage, MyBookingsPage, ProfilePage, NotFoundPage
    ├── pages/cabinet/         CabinetHomePage, CreateCompanyPage, CompanyLayout, DayPage, OrdersPage, SessionPage, ResourcesPage, ResourceCreatePage,
    │                          ResourceEditPage, SchedulePage (банщик), SettingsPage, StaffPage, NotificationsPage, LinkPage, SubscriptionPage
    ├── utils/                 baniTexts.ts (запасные правовые тексты), baniVectors.test.ts, guestStorage.ts (sessionStorage, Т42-09)
    ├── guestCopy.guard.test.ts, baniRoutes.test.ts, sharedSources.guard.test.ts, legalKeys.guard.test.ts
    └── types.ts               реэкспорты из api-cycle39/42.generated.ts
```

- **Главная** (US-42-11): `ServiceLanding` с `layout: 'panel'`, `backdrop: 'none'` (Q42-14 при Q42-2 (б)), слот `heroAside` —
  `BathSearchPanel`, `catalog` — `BathCatalog`. Секции шаблонные, своих `main/section/h1/h2` нет. Тарифов, медиа, слов «бесплатн»,
  «гарантир», «лучш» и др. нет (guard, Т42-11). Бренд в шапке, подвале, манифесте, `<title>` — «EZBOOK Бани» одним написанием.
- **Комплекс, ресурс, форма** (US-42-12…15): общие `ServiceView`, `ServiceTimePicker`, `ServiceOrderPanel` (+ поле «Сколько человек
  придёт, включая детей» 1…N по `features.capacity`, по умолчанию пусто), «Время местное, {город}», `BathBookingNotice` под
  кнопкой, `BathBookingTerms`, капча, галочка мессенджера выключена. Время гостю — календарными датами (сервер).
- **Бронь** `/s/:token` (US-42-16): общий `ServiceOrderView` + блок «Напоминание», «Забронировать ещё в этом комплексе»
  (§42.10.4), push — `GuestPushCard` («уведомления на телефон», заголовок задаёт сервер).
- **Кабинет** (US-42-01…10, 19…21): создание компании с `CityCombobox` (общий) + «Вашего города пока нет в списке — напишите в
  поддержку» + соглашение + триал; главный экран — «День услуг» (общий `ServiceDayView`, на телефоне — список) + счётчик
  «Ожидают проверки оплаты» + список броней; ресурсы — общие вкладки с полем «Вместимость» (`BathCapacityOwnerNotice`),
  подсказками R42-1, диалогом подтверждения позиций (409 `ItemRestrictedConfirmationRequired` + `BathPositionsOwnerNotice`),
  показом `contentWarnings`; настройки — профиль (общий `PUT /api/companies/{id}`: название, описание, адрес с
  `BathPublicContactsNotice`, телефон, карты, город и пояс), фото комплекса (общий `CompanyPhotosSection` + текст
  «люди в кадре», Т42-13), реквизиты (`BathPaymentRequisitesOwnerNotice`), исполнитель, настройки брони и напоминания,
  уведомления; персонал (должности «Администратор»/«Банщик»); банщику — только «Расписание»; подписка — общий `BillingPage`
  с `line="Baths"`. Шахматки, домов, заездов в bani нет.
- **Автообновление** — опрос `GET …/revision` (≤ 30 с, при `visibilitychange` — сразу), при смене — инвалидация запросов «Дня»,
  списка и карточки.

### §42.12.3 Общий код ezbook, который меняется (FE-42-7)
`utils/companyKind.ts` (+ `Baths` «Бани», фильтр админки), `api/push.ts` (`PushSite` + `'Baths'`), `hooks/useWebPush.ts` (`hasBaths`,
`siteUrls.baths`), `api/plans.ts`/`api/adminBilling.ts`/`api/admin.ts` (линейка и вид `Baths`, `maxResources`, `bathsSubscription`),
`pages/BillingPage.tsx` (`line="Baths"`: «Ваша подписка — EZBOOK Бани», «Тариф действует на все ваши бани сразу; он ограничивает
число опубликованных ресурсов.», «Тарифы «Бань»»), админские экраны тарифов/аккаунтов/компаний, карточка «Ваши бани — на
bani.ezbook.ru» в кабинете ezbook по `kinds-summary` (P2), `types/index.ts` (+ `Baths` в `PushSiteUrls`/`PushConfigCompany`),
`src/test/workerRouting.ts` (+ bani). `CompanyPage.tsx`/`EmbedPage.tsx` — `Baths` трактуется как «не салон» (сервер всё равно
отвечает 404). Плюс правки запасных текстов dom (Т42-01, Т42-07).

### §42.12.4 Тесты фронта (FE-42-8 и в каждой задаче)
- `bani/src/guestCopy.guard.test.ts` — тексты для гостя (`baniTexts.ts`, конфиг главной, FAQ, `words` вертикали, строки страниц):
  нет «бизнес-день», «6…30»/«25:00», «чек» о файлах, «дом», «бронь дома», «проживание», «заселение», «заезд», «заказ»,
  «задаток», «невозвратный», «депозит», «штраф», «неустойк», «не возвращается», «туристический налог».
- `bani/src/landing/configs.guard.test.ts` — нет `className`/JSX в конфиге, нет `/pricing`, «тариф», «₽», «руб», «бесплатн»,
  «гарантир», «лучш», «самые», «№ 1», «провер», «мгновенн», «онлайн-оплат», «оплата картой», «бесплатная отмена», «аренд»,
  «лечебн», «оздоров», «детокс», «иммунитет» (Т42-11).
- `bani/src/legalKeys.guard.test.ts` — запрещённые ключи «Домов» не упоминаются в `bani/src` (Т42-04).
- `bani/src/baniRoutes.test.ts` — маршруты `BaniApp` = `spaRoutes` + страницы компании/ресурса; первый сегмент — в резерве.
- `bani/src/sharedSources.guard.test.ts` — как у dom.
- `bani/src/utils/baniVectors.test.ts` — векторы `guests`, `restrictedItems`, `ownerText`.
- Перенесённые тесты общих компонентов — в `src/components/slots/**` (прежние ID), область `stays` расширяется префиксами
  `src/components/slots/`, `src/utils/slots/`, `src/hooks/slots/`, `src/api/slots`, `bani/src/` (`contracts/cycle36/test-areas.json`).

---

## §42.13. DevOps: vhost, сборка, CI, деплой

### §42.13.1 vhost `deploy/nginx/bani.ezbook.conf` (DO-42-01) — по образцу `dom.ezbook.conf`, маскирование **с первого коммита** (C39-6)
- `server_name bani.ezbook.ru`; `root /var/www/ezbook/current/__bani`; `access_log /var/log/nginx/bani.access.log bani_masked`.
- Имена `map`/`log_format`/именованных групп — **уникальные** (`$bani_safe_uri`, `$bani_safe_referer`, `bani_masked`,
  `bani_uri_prefix`, `bani_uri_rest`, `bani_ref_prefix`, `bani_ref_rest`): nginx не стартует при повторе имён других vhost.
- Маска пути: `^(?<bani_uri_prefix>/(?:api/baths/service-orders/public|api/stays/service-orders/public|api/stays/bookings/public|s|b)/)[^/?]+(?<bani_uri_rest>.*)$`
  → `${bani_uri_prefix}MASKED${bani_uri_rest}` (свои и чужие токены — на случай запроса не на тот хост); `Referer`
  `^(?<bani_ref_prefix>https?://[^/]+/(?:s|b)/)[^/?#]+(?<bani_ref_rest>.*)$`.
- `client_max_body_size 11M`; `X-Robots-Tag "noindex, nofollow"` на уровне server и в **каждом** location с `add_header`;
  `sw.js` и `manifest.webmanifest` — `no-cache` (+ `application/manifest+json`); `/assets/` — `immutable`; SPA `try_files … /index.html`
  с `no-cache`, `X-Frame-Options DENY`, CSP как у dom (SmartCaptcha); вебхуки и отписка — 404 (`/api/notifications/provider-webhook/`,
  `/api/notifications/unsubscribe/`, `/api/phone-verification/max/webhook/`); `/uploads/` и `/api/` — прокси на `127.0.0.1:5000`.
- `deploy/nginx/test-bani-masking.sh` — по образцу `test-dom-masking.sh` (запросы `/s/SECRET`, `/api/baths/service-orders/public/SECRET/cancel`,
  `/api/stays/service-orders/public/SECRET`, Referer `/s/SECRET`).
- `ezbook.conf`: `location ^~ /__bani/ { return 404; }`; `demo.visit.ezbook.conf` (root тоже `current`): `/__bani/` и
  недостающий `/__dom/` → 404. `dom.ezbook.conf`: маска дополняется `api/baths/service-orders/public` (симметрично);
  `test-dom-masking.sh` — + один запрос.

### §42.13.2 Сборка (DO-42-02) — четвёртое приложение того же npm-пакета
`vite.bani.config.ts` (копия dom: `root: 'bani'`, `outDir: '../dist-bani'`, алиасы `@bani`, `@`, порт `SB_BANI_WEB_PORT` = 5176),
`tsconfig.bani.json` (`@bani/*`, include `bani/src`, `src`), `tailwind.bani.config.js` (+ `.d.ts`, `appTailwindContent('bani')`);
`package.json`: `dev:bani`, `build:bani` (`tsc -p tsconfig.bani.json && vite build --config vite.bani.config.ts`),
`types:api:cycle42`, `build:release` = `… && npm run build:bani && node scripts/merge-site-dist.mjs goods dom bani`;
`shared-sources.js`: `SECONDARY_APPS = ['goods', 'dom', 'bani']`; `eslint.config.js`: блок `bani/src/**` (только общие страницы,
нельзя `@/App`, `@dom`, `@goods`), а dom/goods/ezbook не импортируют `bani/`; `ignores` += `dist-bani/**`; `sw.js` bani — в
блок service worker; `vitest.config.ts`: include `bani/src/**`, алиас `@bani`; `.env.dev.example`: `SB_BANI_WEB_PORT=5176` и
подсказка `# PublicSites__BathsBaseUrl=http://localhost:5176`; `.claude/launch.json` — `frontend-bani`; `contracts/cycle36/test-areas.json`
— префиксы §42.12.4. В той же задаче — **каркас** `frontend/bani` (index.html с noindex, `main.tsx` с заглушкой, `public/` с
манифестом, `sw.js` и иконками — временно копии иконок dom с другим цветом фона, замена — по желанию заказчика), чтобы CI был
зелёным с первого дня, а FE наполнял приложение.

### §42.13.3 CI (DO-42-03) — `.github/workflows/ci.yml`
`redocly lint` (+ `../contracts/cycle42/openapi.yaml` в список), `npm run types:api:cycle42` и `git diff --exit-code` генерата,
`contracts-to-json.mjs` += `cycle42` и сверка `openapi.json`, `Type-check bani` (`tsc -p tsconfig.bani.json`), проверка `sw.js` —
+ `bani/public/sw.js`, «Smoke test (bani build in dist/__bani)» (`smoke-frontend.sh` с `SMOKE_PROFILE=bani`, порт 4176 —
профиль добавить в скрипт: index с `<div id="root">`, иконки, манифест, `sw.js`, meta robots), «Check bani nginx log masking».
`contracts/redocly.yaml` — комментарий-перечень + cycle39, cycle42. `openapi.json` cycle42 генерирует DO в этой задаче.

### §42.13.4 Деплой (DO-42-04, DO-42-05)
- `deploy/deploy-remote.sh`: смоук bani по образцу dom — `BANI_HOST` (`bani.ezbook.ru`), `BANI_SMOKE` (1, `0` — аварийное
  выключение), `BANI_VHOST` (`/etc/nginx/sites-enabled/bani.ezbook.conf`); если vhost установлен — `current/__bani/index.html`
  обязателен, `https://bani.ezbook.ru/` и `/api/health/ready` через локальный nginx — 200, иначе деплой падает; если не
  установлен — предупреждение и пропуск. `rollback.sh` — проверить, что переключение `current` не требует правки (все сайты
  в одном релизе).
- `DEPLOY.md` **§32** (§31 занят циклом 40, R42-10): DNS `A bani.ezbook.ru`;
  установка vhost (копирование поверх **сносит 443-блок certbot** → сразу `certbot --nginx -d bani.ezbook.ru`); `nginx -t`;
  SmartCaptcha — домен в консоли Яндекса; `PublicSites__BathsBaseUrl` — не нужен (значение по умолчанию), задавать только для
  стенда с другим адресом; миграция `Cycle42Baths` (только добавления) и её откат (только с `pg_dump` и после удаления компаний
  `Kind = 3`); `BusinessDayStartMinute` не менять (общий с «Домами», C39-11); `noindex` снимается строкой только по решению
  заказчика; **реальные бани не приглашать до юриста и публикации D1–D4 (Т42-17)**; цены тарифов — начальные, сверить с
  заказчиком; ручные проверки `M42-*` после выката. Ручные шаги (DNS, certbot, vhost, консоль капчи) — за заказчиком.
- `.env.production.example` — закомментированная строка `PublicSites__BathsBaseUrl`. `DeploymentSafetyChecks` — `BathsBaseUrl`
  в проде (если задан) обязан быть `https://`.

---

## §42.14. Структура проекта — что добавляется

```
ServiceBooking.Core/
├── Entities/  BathsSubscription.cs (+ BathsPlans), StayServiceItemConfirmation (в StayService.cs); StayService (+Capacity),
│              StayServiceOrder (+GuestsCount, +SessionReminderAtUtc), StaysSettings (+ServiceReminderHours), SubscriptionPlanConfig (+MaxResources)
└── Enums/     CompanyKind (+Baths), NotificationType (+41), StaysEnums.StayServiceOrderEventKind (+10), LegalTextKey (+8 вне All)

ServiceBooking.Infrastructure/Migrations/   *_Cycle42Baths.cs

ServiceBooking.API/
├── Controllers/Slots/      SlotServicesPublicControllerBase, SlotServiceOrdersPublicControllerBase, SlotServicesCabinetControllerBase,
│                           SlotSessionsControllerBase, SlotCompanySettingsControllerBase   (тела перенесены из Controllers/Stays)
├── Controllers/Stays/      прежние классы — наследники баз (те же имена, атрибуты, маршруты)
├── Controllers/Baths/      🆕 BathsPublicController, BathsServicesPublicController, BathServiceOrdersPublicController,
│                           BathsServicesController, BathsSessionsController, BathsCompaniesController
├── DTOs/Baths/             🆕 BathsDtos.cs (каталог, комплекс, компания, настройки, расписание банщика, мои брони, ревизия)
├── Services/Companies/     CompanyKindTraits (🆕), CompanyKindGuard (+Baths), CompanyCreationService (+вертикаль), CompanyPhotoTexts
├── Services/Slots/         🆕 SlotVertical + SlotVerticals, ServiceWording (Stays/Baths), GuestsCountRules, RestrictedItemFilter,
│                           OwnerTextChecks, SessionReminderPolicy, ServiceItemWriter
├── Services/Baths/         🆕 BathsSlugPolicy (bani-routes.json), BathsTrialTerms, BathsCatalogService, BathsCompanyService (карточка,
│                           чек-лист, настройки), BathsScheduleService (расписание банщика), SessionReminderService (третий проход задачи)
├── Services/Stays/         StaysAccessResolver (+kind), StaysBookingGate (+GateUnit), StaysPlanResolver (+вертикаль), StaysTrialService
│                           (+вертикаль), StaysCompanyService.EvaluateGateAsync (по вертикали), StayNotificationPlanner (wording/ссылки по виду)
├── Services/Stays/Services/ ServiceSlotService (+kind, +HasStartsBatchAsync), ServiceCatalogService (+kind, +capacity), ServiceOrderCreationService
│                           (+kind, +guestsCount, +ключи вертикали), ServiceDtoMapper (+guestsCount, cityName, localTimeNote, sessionReminder,
│                           bookAgainUrl), ServiceDayService (метка полосы), ServiceTexts/ServiceNotificationTexts (через ServiceWording)
├── Services/PublicSites/   PublicSitesOptions (+BathsBaseUrl), PublicSiteLinks (+по виду)
├── Services/Billing/       OwnerSubscriptionService (+Baths), ChannelEligibility, SubscriptionResolver (список линеек), BillingTexts
├── Services/Scheduling/Tasks/StaysScheduledMessagesTask (+третий проход), Services/Notifications/NotificationTypeCatalog (+41)
├── Controllers/            AdminBillingController, AdminPlansController, AdminAccountDtoBuilder, AdminController, CompaniesController,
│                           CompanyMembersController, CompanyPhotosController, PushController, NotificationChannelsController (по §42.3.4)
├── Startup/                LoggingExtensions (маска /api/baths/service-orders/public/), DeploymentSafetyChecks (+BathsBaseUrl), DI
├── ServiceBooking.API.csproj  embedded ../contracts/cycle42/bani-routes.json
└── appsettings.json        Baths:TrialDays=14, Baths:CatalogCacheSeconds=30, PublicSites:BathsBaseUrl

ServiceBooking.UnitTests/   BaniVectorsTests, CompanyKindTraitsTests, CompanyKindExhaustiveTests, CompanyKindBranchGuardTests (+Stays/Baths),
                            BathsSlugPolicyTests, BathsWordingGuardTests, ServiceWordingStaysBytesTests, PaidNumbersLinesTests, StaysAccessTests (без изменений)
ServiceBooking.Tests/       Infrastructure/Cycle42TestBase.cs; Tests/Cycle42KindIsolationTests, Cycle42CompanyTests, Cycle42TariffTests,
                            Cycle42ResourcesTests, Cycle42ItemFilterTests, Cycle42PublicTests, Cycle42OrderTests, Cycle42ConcurrencyTests,
                            Cycle42AfterTrialTests, Cycle42ScheduleShapeTests, Cycle42PrivacyTests, Cycle42NotificationsTests,
                            Cycle42ReminderTests, Cycle42ContractTests, Cycle42MigrationRollbackTests; OpenApiContractValidatorTests [InlineData("cycle42")]

frontend/   src/components/slots/** , src/utils/slots/** , src/hooks/slots/** , src/api/slots.ts (перенос, §42.12.1);
            bani/** (§42.12.2); vite.bani.config.ts, tsconfig.bani.json, tailwind.bani.config.js(+.d.ts); src/types/api-cycle42.generated.ts;
            shared-sources.js, eslint.config.js, vitest.config.ts, package.json, scripts/merge-site-dist.mjs (без правки кода — только аргумент)
contracts/cycle42/  openapi.yaml, openapi.json (генерат), bani-routes.json, bani-vectors.json
deploy/     nginx/bani.ezbook.conf, nginx/test-bani-masking.sh, nginx/{ezbook,dom.ezbook,demo.visit.ezbook}.conf, nginx/test-dom-masking.sh,
            deploy-remote.sh, ci/smoke-frontend.sh (профиль bani)
.github/workflows/ci.yml, DEPLOY.md §32, .env.dev.example, .env.production.example
```

`Cycle22RouteTable.golden.txt` — **+68 строк** `api/baths/*`, ни одной изменённой (§42.39 контракта).

---

## §42.15. Разбивка работ и параллельность

Контракт (`openapi.yaml`, `bani-routes.json`, `bani-vectors.json`) готов **до** кода — фронт стартует на prism-моке
(`npx @stoplight/prism mock contracts/cycle42/openapi.yaml --port 4042`) в день 1; формы, повторённые из цикла 39, — в
`api-cycle39.generated.ts` и моке `contracts/cycle39/openapi.yaml` (порт 4039).

### §42.15.1 Backend

| # | Задача | Т42 / US | Зависит от | Параллельно с |
|---|---|---|---|---|
| BE-42-M | Сущности, перечисления (`CompanyKind.Baths`, `NotificationType` 41, событие 10, ключи), `AppDbContext`, **одна миграция** `Cycle42Baths` (§42.2.6), `BathsPlans`, `ShowcaseOwnership.NeverWritten` — **один разработчик, один коммит** | — | — | BE-42-P, BE-42-K |
| BE-42-P | Чистые классы + юнит-тесты по `bani-vectors.json`: `GuestsCountRules`, `RestrictedItemFilter`, `OwnerTextChecks`, `SessionReminderPolicy`, `StaysBookingGate` (+`GateUnit`), `CompanyKindTraits`, `SlotVertical`/`SlotVerticals`, `BathsSlugPolicy` (+ embedded `bani-routes.json`) | 05, 06, 12 | — (`CompanyKind.Baths` — из BE-42-M или временно локально) | всё |
| BE-42-K | Аудит ветвлений по виду (§42.3.4: все места + повторный grep), `CompanyKindGuard`/`CompanyPhotoTexts`/`PublicSiteLinks` (+`BathsBaseUrl`, методы по виду), `CompaniesController` (404 салонной страницы, `kinds-summary`, галерея), `CompanyMembersController`, `StaffMaxLinkService`, `CompanyTransferService`, `PushController`, `NotificationChannelsController`; стражи `CompanyKindBranchGuardTests` (+Stays/Baths), `CompanyKindExhaustiveTests`, `CompanyKindTraitsTests` | 26 | BE-42-M, BE-42-P | BE-42-V |
| BE-42-V | **Рефакторинг без изменения поведения:** базы `Controllers/Slots/*Base` (§42.4.1), параметр `kind` в точках поиска сервисов (§42.4.2), `StaysAccessResolver(kind)`. Готовность: эталон маршрутов **не изменился**, CY37-*/CY39-* и `Cycle39ContractTests` зелёные без правки | — | BE-42-M | BE-42-K, BE-42-W, BE-42-P |
| BE-42-W | `ServiceWording` (Stays — байт-в-байт, Baths — «бронь», «EZBOOK Бани», пометка времени), перевод `ServiceTexts`/`ServiceNotificationTexts`/`StayNotificationTexts.GuestPushTitle`/планировщика на вертикаль; `ServiceWordingStaysBytesTests`, `BathsWordingGuardTests` | 08, 15 | BE-42-M | BE-42-V |
| BE-42-1 | Компания «Бани»: `CompanyCreationService` (+вертикаль, город, `StaysSettings` с `AcceptServiceOrdersWithoutStay=true`, `ServiceReminderHours=3`), `BathsCompaniesController` (create, my, slug-check, карточка, settings, schedule, revision + наследование payment-details/provider/slug/qr/notification-settings), `BathsCompanyService` (DTO, чек-лист), `BathsScheduleService` (закрытая форма банщика) | 10, US-42-01/02/19/21 | BE-42-V, BE-42-K | BE-42-2, BE-42-3 |
| BE-42-2 | Линейка «Бани»: `StaysPlanResolver`/`StaysTrialService` по вертикали, `BathsTrialTerms`, `StaysCompanyService.EvaluateGateAsync` по вертикали (+ предоплата-максимум), `GET|POST /api/baths/trial`, `OwnerSubscriptionService` (line Baths), админка (`AdminPlansController` `MaxResources`, защита триала; `AdminBillingController` назначение/очередь/лимит; `AdminAccountDtoBuilder`), `ChannelEligibility` + `SubscriptionResolver` (список линеек, `PaidNumbers`), тест `/api/pricing` без «Бань» | 10, 14, US-42-03/04 | BE-42-V, BE-42-P | BE-42-1, BE-42-3 |
| BE-42-3 | Ресурсы: `BathsServicesController` (наследник), `Capacity` в setup/DTO, публикация (`ServiceNoCapacity`, 402 под `billing-account`), адрес ресурса по `BathsSlugPolicy`, `AvailableForHouseBookings=false`; `ServiceItemWriter` + фильтр позиций + `StayServiceItemConfirmations` (оба префикса); `OwnerTextChecks` в content/items (оба префикса) | 05, 06, 12, US-42-05…10 | BE-42-V, BE-42-P | BE-42-1, BE-42-4 |
| BE-42-4 | Публичное: `BathsPublicController` (каталог с кешем «базы» и фильтром даты `HasStartsBatchAsync`, города, комплекс), `BathsServicesPublicController`/`BathServiceOrdersPublicController` (наследники), `guestsCount` в создании (`GuestsCountRules`), ключи текстов вертикали в снимках, поля DTO (`capacity`, `cityName`, `localTimeNote`, `bookAgainUrl`), `GET …/service-orders/my` (P1, `SUBJECT-PHONE-GATE`), маска пути в логах | 02, 06, 08, 09, US-42-11…18 | BE-42-V, BE-42-W, BE-42-3 (capacity) | BE-42-5 |
| BE-42-5 | Уведомления и ссылки по вертикали (планировщик заказов, push гостю «EZBOOK Бани», персоналу — ссылки bani), `BathsSessionsController` (наследник; метка полосы «Дня»; ручная бронь + `guestsCount`); **напоминание** (P1): тип 41, событие 10, третий проход `stays-scheduled-messages`, `sessionReminder` в DTO | 08, 15, US-42-19/20/22/23 | BE-42-W, BE-42-4 | BE-42-6 |
| BE-42-6 | ПДн: выгрузка (`guestsCount`, `site`, ссылка по виду), удаление/обезличивание/отзыв на банной компании, retention на банной компании (тесты), `DeploymentSafetyChecks` (`BathsBaseUrl`), админская статистика (P1) | 06, US-42-25, US-42-04 | BE-42-4 | BE-42-5 |
| BE-42-7 | В конце: `Cycle22RouteTable.golden.txt` (+68, без изменённых), `OpenApiContractValidatorTests [InlineData("cycle42")]`, `Cycle42ContractTests` (формы ответов по `contracts/cycle42`, закрытая форма расписания), `API_DOCUMENTATION.md` (раздел «Бани»), запись в `CURRENT_STATE.md`-долг C42-* | — | всё BE | — |

### §42.15.2 Frontend

| # | Задача | Т42 / US | Зависит от | Параллельно с |
|---|---|---|---|---|
| FE-42-0 | `types:api:cycle42` + генерат; `bani/src/utils/baniVectors.test.ts`; `baniRoutes.test.ts` на `bani-routes.json` | 06 | DO-42-02 | всё |
| FE-42-1a | Перенос утилит, API-фабрики `createSlotApi`, хуков и типов в `src/{utils,api,hooks}/slots` с прокладками в dom (§42.12.1) — **без изменения поведения**, тесты dom без правки ожиданий | — | — | FE-42-2, FE-42-3 |
| FE-42-1b | Перенос компонентов и тел страниц услуг в `src/components/slots/**`, `SlotVerticalProvider` + `staysVertical` в dom; dom-страницы — обёртки; тесты dom без правки ожиданий; новые возможности по `features` (вместимость, диалог фильтра позиций, `contentWarnings`, «люди в кадре») | 05, 06, 12, 13 | FE-42-1a | FE-42-2, FE-42-3 |
| FE-42-2 | Оболочка bani: `BaniApp` (маршруты), `BaniNavbar`/`BaniFooter` («EZBOOK Бани»), вход/регистрация/профиль/согласия/уведомления/правовые страницы (общие), «Устройства и уведомления» (`PushSite = 'Baths'`), `UpdateBanner`, `sw.js` + `workerRouting`, `bathsVertical` | 03 | DO-42-02, FE-42-1a | FE-42-3 |
| FE-42-3 | Главная: `baniLanding.ts`, `baniFaq.ts`, `BathSearchPanel` (город, дата P1, URL), `BathCatalog`, `BathResourceCard`; страница комплекса | 11, US-42-11/12 | FE-42-2 | FE-42-4, FE-42-5 |
| FE-42-4 | Ресурс и бронь: страница ресурса (общий `ServiceView` + вместимость, «время местное»), форма (число гостей, `BathBookingNotice`, `BathBookingTerms`, капча, галочка), страница брони (общий `ServiceOrderView` + «Напоминание», «Забронировать ещё» с `sessionStorage`), «Мои брони» (P1) | 02, 06, 08, 09, US-42-13…17 | FE-42-1b, FE-42-2 | FE-42-5 |
| FE-42-5 | Кабинет: список компаний, создание (город, «города нет», соглашение, триал), `CompanyLayout` по `myPermissions`, настройки (профиль + `BathPublicContactsNotice`, фото комплекса + «люди в кадре», реквизиты, исполнитель, бронирование и напоминание, уведомления), персонал (Администратор/Банщик), ссылка/QR, подписка (`BillingPage line="Baths"`) | 04, 10, 13, US-42-01…04 | FE-42-2, FE-42-7 (BillingPage) | FE-42-4, FE-42-6 |
| FE-42-6 | Кабинет: ресурсы (общие вкладки + вместимость + подсказки R42-1), «День услуг» главным экраном (+ счётчик и список броней), карточка брони, ручная бронь (+ число гостей), «Расписание» банщика (закрытая форма, 360 px), автообновление по `revision` | 05, 12, US-42-05…10, 19…21 | FE-42-1b, FE-42-5 | FE-42-4 |
| FE-42-7 | Общий ezbook: `Baths` в `companyKind`, `PushSite`, `useWebPush`, `plans`/`adminBilling`/`admin` API и экранах (линейка, `maxResources`, блок подписки, фильтр компаний), `BillingPage` (line Baths), `types/index.ts`, карточка «Ваши бани» (P2); dom: Т42-01 (нейтральная подстановка), Т42-07 (подсказка безопасности) | 01, 07 | — | всё |
| FE-42-8 | Стражи: `guestCopy.guard`, `configs.guard`, `legalKeys.guard`, `sharedSources.guard` bani; `baniTexts.ts` — запасные тексты §11.0–§11.5 | 02, 04, 11 | FE-42-3, FE-42-4 | FE-42-6 |

### §42.15.3 DevOps

| # | Задача | Когда |
|---|---|---|
| DO-42-01 | `deploy/nginx/bani.ezbook.conf` (маскирование с первого коммита), `test-bani-masking.sh`; `ezbook.conf` и `demo.visit.ezbook.conf` — `/__bani/` (и `/__dom/` у demo.visit) → 404; `dom.ezbook.conf` + маска `api/baths/service-orders/public` и строка в `test-dom-masking.sh` | сразу |
| DO-42-02 | Сборка четвёртого приложения (§42.13.2): vite/tsconfig/tailwind, скрипты `package.json`, `build:release` + аргумент `bani` у `merge-site-dist.mjs`, `shared-sources.js`, ESLint, vitest, `.env.dev.example`, `.claude/launch.json`, `test-areas.json`; **каркас** `frontend/bani` (index.html с noindex, заглушка `main.tsx`, `public/` — манифест «EZBOOK Бани», `sw.js`, иконки) | сразу (день 1, до FE-42-0/2) |
| DO-42-03 | CI (§42.13.3): lint/types/json `cycle42`, `contracts-to-json.mjs` + генерат `contracts/cycle42/openapi.json`, type-check bani, `sw.js` bani, смоук сборки bani (`smoke-frontend.sh` профиль `bani`), маскирование bani; `contracts/redocly.yaml` (комментарий) | сразу |
| DO-42-04 | `deploy/deploy-remote.sh` — смоук bani (`BANI_HOST`/`BANI_SMOKE`/`BANI_VHOST`), проверка `rollback.sh`; `.env.production.example` (`PublicSites__BathsBaseUrl` закомментирован) | после DO-42-02 |
| DO-42-05 | `DEPLOY.md` §32 «bani.ezbook.ru — четвёртый сайт» (§42.13.4): DNS, certbot, vhost, SmartCaptcha, миграция и откат, «реальные бани не приглашать», цены тарифов, `M42-*` | сразу (дополнить после BE-42-M) |
| DO-42-06 | Выкат на стенд (по готовности QA): деплой `develop`/ветки по кнопке, инструкция заказчику по ручным шагам, проверка `bani.access.log` на маскирование (`M42-04`) | в конце |
| DO-42-07 (P2) | `tools/bench/cycle42/` — замер p95 каталога bani с фильтром даты на 150 ресурсах (засев через HTTP API локального стенда) | после BE-42-4 |

### §42.15.4 QA

Кейсы `CY42-*` и **ручные `M42-*` с вердиктами** в `TEST_CATALOG.md`, раздел «Цикл 42»:
`CY42-01…09` матрица изоляции 4 видов; `CY42-10…19` компания (создание, город, адрес, настройки, чек-лист, сотрудники);
`CY42-20…29` тарифы, триал (однократность в линейке, на номер), админка, 402 публикации, `/api/pricing` без «Бань»;
`CY42-30…39` ресурсы (вместимость, публикация, фильтр позиций и журнал — оба префикса, тексты владельца);
`CY42-40…49` публичное (каталог, города, дата, комплекс, ресурс, бронь с гостями, «Мои брони» с гейтом номера);
`CY42-50…55` параллельность (N параллельных «Пт 23:00–01:00» и «Пт 00:30–02:30» → ровно одна; «Пт 04:00–06:00» + 30 мин и
«Сб 06:00» → вторая отклонена; без 40P01); `CY42-60…63` после окончания триала управление бронями доступно (Т42-10);
`CY42-70…72` форма расписания банщика (нет телефона, сумм, файлов, статуса оплаты), комментарий по настройке;
`CY42-80…85` ПДн и retention на банной компании; `CY42-90…99` уведомления (ссылки bani, заголовок push, слова «бронь», без ПДн
в push, банщику — ничего) и напоминание (тихие часы, однократность, «создан позже»); `CY42-100…` регресс dom (контракт cycle39,
эталон маршрутов, тексты байт-в-байт); `CY42-140` миграция накат→откат→накат; `CY42-F*` — фронт; schemathesis по
`contracts/cycle42/openapi.yaml`; векторы — один файл для C# и TS. Ручные `M42-*`: выбор времени через полночь на iOS Safari и
Android 360 px; главная на 360/768/1280 без горизонтальной прокрутки; push гостю на реальном телефоне; маскирование токена в
`bani.access.log` на машине; «День услуг» на планшете; VoiceOver на выборе времени и числа гостей.

### §42.15.5 Порядок и точки синхронизации

```
День 1:  DO-42-01, DO-42-02, DO-42-03, DO-42-05 | BE-42-M, BE-42-P | FE-42-1a, FE-42-7 (по моку)
Затем:   BE-42-V + BE-42-K + BE-42-W (параллельно, все от BE-42-M) | FE-42-0, FE-42-2, FE-42-1b
Затем:   BE-42-1, BE-42-2, BE-42-3 (параллельно) | FE-42-3, FE-42-5
Затем:   BE-42-4 → BE-42-5, BE-42-6 | FE-42-4, FE-42-6, FE-42-8
Конец:   BE-42-7, DO-42-04, QA, DO-42-06 (выкат на стенд)
```

| Что | Где |
|---|---|
| форма DTO, коды 409, перечисления | `contracts/cycle42/openapi.yaml` (+ cycle39 для повторённых форм) → генераты |
| маршруты bani, резерв слов | `contracts/cycle42/bani-routes.json` |
| гости, фильтр позиций, тексты владельца, напоминание, гейт | `contracts/cycle42/bani-vectors.json` |
| бизнес-день, окна, цены, старты, возврат | `contracts/cycle39/service-vectors.json` (без изменений) |
| тексты 400/402/409/429 | `API_CONTRACT_CYCLE42.md` — фронт печатает `response.data` |

### §42.15.6 Если не укладываемся (R42-5)

Режутся первыми (P1/P2), P0 не режутся: US-42-23 напоминание (BE-42-5 часть, вкладка настроек — скрыть поле) → US-42-17 «Мои
брони» (маршрут и страница) → фильтр даты в каталоге (параметр игнорировать, поле панели скрыть) → ближайшая дата со свободным
временем → админская статистика (US-42-04) → ручная бронь (запасной путь — ручная дата с комментарием) → карточка «Ваши бани»
в ezbook → замер DO-42-07.

---

## §42.16. Риски и решения

| # | Риск | Решение |
|---|---|---|
| R42-1 | Одна баня в двух компаниях — БД не поймает двойную бронь | Q42-1 (а); подсказка при создании ресурса и в FAQ; D3 7а.7 (распределение риска); тест не нужен (не проверяемо) |
| R42-2 | Обобщение движка трогает код броней «Домов» (C39-10) | пути записи и замки не меняются (§42.4.3); рефакторинг баз — отдельной задачей BE-42-V с критерием «эталон маршрутов и CY37/CY39 без изменений»; `kind` — только в точках поиска |
| R42-2a (новый) | Банная компания пойдёт по пути салона в непроверенном месте (`if (kind == Stays)`) | `CompanyKindTraits` + аудит §42.3.4 + стражи (тернарники, исчерпывающие `switch`, матрица изоляции) |
| R42-3 | Правовые тексты черновые | ключи вне `All`, запасные тексты; стенд под `noindex`; реальные бани не приглашать (Т42-17); строка в `DEPLOY.md` |
| R42-4 | Тариф без публичной сетки и без онлайн-оплаты | как у «Домов»: заявка → суперадмин; цены — в админке |
| R42-5 | Объём (четвёртое приложение + перенос + линейка + изоляция) | параллельный план, prism-мок с дня 1, порядок урезания §42.15.6 |
| R42-6 | vhost без маскирования → токены в access log | маскирование в файле с первого коммита, скрипт в CI, `M42-04` на машине, строка в `DEPLOY.md` |
| R42-7 | Граница бизнес-дня 06:00 общая и неизменна (C39-11) | не меняем; ресурсу бани с сеансами до 07:00 она не подойдёт — вопрос первым владельцам (§42.17 п. 3) |
| R42-8 | Фейковые брони при предоплате «нет» | капча, лимиты номера и IP (общие), предупреждение владельцу |
| R42-9 | Уведомления на бою: мессенджер гостю выключен флагом, MAX персоналу выключен | всё видно на странице брони; push живой |
| R42-10 | Параллельные циклы 40 (iCal) и следующие | перечисления — следующие свободные значения при мерже; миграция пересобирается поверх; эталон маршрутов, `ci.yml`, `shared-sources.js`, `package.json` (`build:release`), `DEPLOY.md`, `CURRENT_STATE.md`, `TEST_CATALOG.md` — ручной мерж; кто вливается вторым, берёт следующие номера |
| R42-11 | Справочник городов 91 + Шерегеш, добавить из админки нельзя | честный текст «вашего города нет» + контакт; долг цикла 4 остаётся |
| R42-12 (новый) | Перенос компонентов dom в общий код ломает dom | прокладки-реэкспорты, `features` вертикали, тесты dom без правки ожиданий как критерий готовности FE-42-1a/1b, `npm run test:area -- stays` |
| R42-13 (новый) | Фильтр позиций и отказ на «задаток» в описании меняют поведение dom | осознанно (D3 7а.4 — для обеих вертикалей); вынесено заказчику (§42.17 п. 2); при отказе — включить только для `Baths` через `SlotVertical` (одна строка) |
| R42-14 (новый) | `StaysSettings` с полями домов у банной компании — соблазн читать их в бане | маршрутов домов у бани нет; `BathsSettingsDto` не содержит полей домов; ревью |

---

## §42.17. Открытые вопросы заказчику (кодирование не блокируют; по умолчанию — как в скобках)

1. **Цены тарифов «Бань»** (по умолчанию — 200 / 500 / 1000 ₽ и лимиты 1 / 3 / без ограничения, как у «Домов»; меняются в
   админке без деплоя).
2. **Фильтр алкоголя/табака и отказ на «задаток» — и для услуг «Домов»?** (по умолчанию — да, одна проверка: D3 7а.4 относится к
   обеим вертикалям; если нет — включаем только для бань одной строкой `SlotVertical`).
3. **Граница бизнес-дня 06:00** для городских бань с сеансами до 07:00 (по умолчанию — оставить; менять нельзя после появления
   сеансов).
4. **Иконки и цвет приложения «EZBOOK Бани»** на телефоне (по умолчанию — временные на основе иконок dom).
5. **Бренд «EZBOOK» и 168-ФЗ** (Q-L42-2 — вне цикла; по умолчанию — латиница, как решено).

---

## §42.18. Отклонения от буквы SPEC (читать обязательно)

1. **Термин для гостя — «бронь»** (Q-L42-5) вместо «заказ» SPEC: страница брони, «Мои брони», тексты сервера для бань. В API
   имена прежние (`service-orders`).
2. **Пометка «время местное, {город}» показывается всегда**, а не «когда пояс браузера отличается» (Т42-08 допускает оба) —
   проще и не зависит от настроек устройства.
3. **Мягкий фильтр позиций и отказ на «задаток/невозвратный/депозит» в описании и позициях действуют и для услуг «Домов»** —
   одна реализация (D3 7а — общий раздел для бань и услуг «Домов»). Вынесено заказчику (§42.17 п. 2). Прочее поведение dom
   не меняется.
4. **Напоминание перед сеансом — только у «Бань»** (у «Домов» `ServiceReminderHours = NULL`, маршрута настройки нет).
5. **Банная компания на салонной странице ezbook (`GET /api/companies/{slug}`) — 404** (US-42-26), тогда как «Дома» там
   по-прежнему видны (так было, не меняем).
6. **Фото комплекса — общая галерея компании**, а не новая сущность (у «Домов» галерея компании закрыта, у бань — открыта).
7. **Расписание банщика — отдельный маршрут `GET /api/baths/companies/{id}/schedule`** с закрытой формой (у dom расписание
   горничной — часть графика домов).
8. **Ревизия для автообновления — отдельный маршрут `GET …/revision`** (у dom ревизию несёт шахматка, которой у бани нет).
