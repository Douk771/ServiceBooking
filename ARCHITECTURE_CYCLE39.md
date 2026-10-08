# ARCHITECTURE — цикл 39 ServiceBooking: «Дома», цикл 2 — услуги-слоты, напоминание накануне заезда, хвосты цикла 37

**Разделы §39.0–§39.19.** Вход: `SPEC_CYCLE39_STAYS_SLOTS_ICAL.md` (ответы заказчика на Q1–Q3 от 2026-10-08),
`LEGAL_REVIEW_CYCLE39.md` и решения заказчика по нему (§39.0a), `ARCHITECTURE_CYCLE37.md`, `API_CONTRACT_CYCLE37.md`,
`contracts/cycle37/`, `CURRENT_STATE.md` (шапка на `a7e3168`; §5.12, §9.7), код ветки `cycle/039-stays-slots-ical`
(`9d8da8f` = `develop` `ece8038` + SPEC). Ветку подготовил devops; архитектор веток не трогает.

**Документы цикла:**

| Файл | Что | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE39.md` (этот) | решения, модель, механизмы, структура, задачи, риски | все |
| `API_CONTRACT_CYCLE39.md` (§39.20–§39.39) | контракт словами: маршруты, права, порядок проверок, коды, тексты, правки существующих маршрутов | backend, frontend, QA |
| `contracts/cycle39/openapi.yaml` (+ `openapi.json` генератом) | **источник истины по форме** новых и изменённых маршрутов (OpenAPI 3.0.3; линтуется чисто и с `contracts/redocly.yaml`, и профилем по умолчанию) | backend, frontend, QA, CI |
| `contracts/cycle39/service-vectors.json` | эталон: бизнес-день, окна, цена часов, деньги сеанса, доступные старты, пересечение с зазором, возврат, формат времени, шаблон напоминания | backend (C#), frontend (TS), QA |
| `contracts/cycle39/dom-routes.json` | маршруты dom цикла 39 (надмножество cycle37): страница услуги, страница отдельного сеанса, резерв слов в пространстве адресов домов | backend (embedded), frontend (тест), QA |

`iCal` — цикл 40; здесь только соблюдение SPEC §11.1 (§39.0 A39-14, §39.15.4).

---

## §39.0a. Решения заказчика по юридическому обзору (2026-10-08) — кодировать по ним, а не по букве SPEC

Заказчик принял рекомендации `LEGAL_REVIEW_CYCLE39.md` по Q-L39-1…Q-L39-6. Требования Т39-01…Т39-17 обзора (§13) разнесены
по задачам §39.17 (колонка «Т39»).

| # | Решение | Что меняется против SPEC | Где |
|---|---|---|---|
| ЮР39-1 (Q-L39-1, Т39-01…03) | Шаблоны отмены отдельного сеанса: **«Без удержаний»** (по умолчанию) и **«Расходы на подготовку»** — позже рубежа компания вправе удержать **только фактические расходы**, не больше стоимости **первого часа** (по снимку). Рубеж — настройка услуги, по умолчанию **12 ч** до начала, допустимо **3…24 ч** (валидатор конфигурации на старте). Текст «к возврату не меньше 0 ₽» **не формируется никогда**: при нулевом остатке — формула «только фактические расходы, не больше N ₽» | «Стандартный» SPEC §4.8 (24 ч / первый час) **не реализуется ни в каком виде**, в том числе конфигурацией | §39.8 |
| ЮР39-2 (Q-L39-2, Т39-09) | Сведения об исполнителе обязательны **всегда**, независимо от предоплаты — для отдельных сеансов **и для домов** с предоплатой 0 % (закрывает §37.19 п. 5) | `StaysBookingGate`: `NoProviderInfo` больше не зависит от процента предоплаты | §39.7.6 |
| ЮР39-3 (Q-L39-3, Т39-13) | Push с текстом напоминания: белый список подстановок подтверждён; свободный текст владельца — через **жёсткий фильтр строк** (≥ 4 цифр подряд вне подстановок, ссылка, e-mail, телефон, слова «код / пароль / Wi-Fi / вайфай / ключниц / сейф / домофон» — строка выпадает); переключатель по умолчанию **выключен**, включение — с текстом `StayReminderPushOwnerNotice` и записью в историю | запасной «отдельный короткий push-шаблон» SPEC A39-8 не нужен | §39.11 |
| ЮР39-4 (Т39-12) | Шаблон напоминания: обязательный текст `StayReminderTemplateOwnerNotice` у поля; **диалог подтверждения** при признаках кодов доступа (в мессенджере и на странице — не запрет); мягкие предупреждения на «штраф / неустойк / не возвращ», «паспорт», номера карт и паспортов; **отказ** на «задаток / невозвратный / депозит» | — | §39.11 |
| ЮР39-5 (Т39-11) | Снимок напоминания на странице брони содержит имя гостя — **стирается при обезличивании** брони; входит в выгрузку данных | — | §39.13 |
| ЮР39-6 (Q-L39-4, Т39-05, Т39-06) | 69-ФЗ: позиции по умолчанию **0**, услуга в форме брони **не выбрана**; сеанс, добавленный персоналом, — с обязательным **основанием** («по просьбе гостя»: телефон / лично / мессенджер), уведомлением гостю (§12.7 обзора), блоком на странице брони и бесплатной отменой | у сеанса — колонки основания и автора | §39.2.4, §39.7.3 |
| ЮР39-7 (Т39-07, Т39-08) | Отдельный сеанс — свой текст `StayServiceBookingNotice` и свои условия `StayServiceBookingTerms`; своя галочка мессенджера; добавление к брони — короткая строка `StayServiceAddNotice` + новая (запасная) редакция `StayBookingNotice` и пункт 3а `StayBookingTerms` | ключи вне `All`, запасные тексты на фронте | §39.13.4 |
| ЮР39-8 (Т39-04) | Сеанс через полночь — гостю **всегда двумя календарными датами** («пт 15 янв, 22:00 — сб 16 янв, 01:00»); слово «бизнес-день» и «часы 6…30» в текстах для гостя **не используются** | формат SPEC §4.9 остаётся **только для персонала** | §39.3.4 |
| ЮР39-9 (Т39-10) | Retention сеансов — по ЮР-6 с отсчётом **от конца сеанса**; горничной — имя, время, дом, позиции; без телефона, сумм, подтверждений | — | §39.13 |
| ЮР39-10 (Т39-15, Т39-16) | `StayTouristTaxNotice` на странице услуги и отдельного сеанса не показывается; в брони дома — уточнённая редакция «на стоимость проживания»; у описания услуги — `StayServiceSafetyOwnerNotice`; полей о здоровье нет | — | §39.13.4 |
| Q-L39-5, Q-L39-6 | Рубеж 12 ч; поле «Правила посещения» — позже | — | — |

Цикл ничего не меняет в статусе вертикали: dom — стенд, реальных владельцев не приглашаем до вычитки живым юристом (C37-1,
Т37-15). Все новые правовые тексты — черновые (ключи вне `LegalTextKey.All`, запасной текст на фронте, как Т37-13).
Т39-17 (правки Политики, Соглашения с компанией, D4) — задача legal-counsel/заказчика, **не кода**.

---

## §39.0. Итог решений — ответы на §7.1 SPEC (A39-1…A39-14) одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| A39-1 | Модель | 11 новых таблиц. **Сеанс** (`StayServiceSessions`) — одна сущность для обоих видов: «занятость услуги + снимок цены», у него **ровно один родитель**: бронь дома (`StayBookingId`) **или** заказ услуги без проживания (`StayServiceOrderId`), CHECK `num_nonnulls(...) = 1`. **Отдельный сеанс** = заказ `StayServiceOrders` (конверт: токен, статус, удержание, предоплата, снимки, согласия) + один сеанс. Статус заказа — **тот же `StayBookingStatus`**, переходы — **тот же `StayStateMachine`**. «Бронь без дома» отвергнута: `StayBookings.HouseId` и даты остаются NOT NULL, ~60 мест цикла 37 не трогаются. Общие механизмы (подтверждения оплаты, push гостя, очереди уведомлений) получают второго владельца — nullable `StayServiceOrderId` + CHECK «ровно один» | §39.2 |
| A39-2 | `SlotCalculator` | **Не обобщаем.** Новая чистая `ServiceSlotCalculator` по тому же принципу: `Calculate` (старты + максимум часов) и `IsStartAllowed` **поверх** `Calculate`. Салонный калькулятор живёт в `TimeOnly` одних суток без цены, зазора и длительности на выбор; его обобщение — правка салонной записи, которую SPEC запрещает | §39.4 |
| A39-3 | Двойная бронь | `EXCLUDE USING gist ("ServiceId" WITH =, tstzrange("StartUtc","OccupiedUntilUtc",'[)') WITH &&) WHERE ("ReleasedAtUtc" IS NULL)`. Время — реальные моменты: полночь и граница бизнес-дня для ограничения не существуют (подтверждено, векторы `overlap`). Зазор — **снимок** в сеансе; смена зазора услуги действует на новые сеансы. Advisory-lock `stay-service:{serviceId}` встроен в единый порядок блокировок; ленивое снятие чужого родителя, чей замок стоит раньше в порядке, — только `pg_try_advisory_xact_lock` | §39.5 |
| A39-4 | Таймер, лимиты | Сеанс в брони своего таймера не имеет — судьба по брони (каскад в той же транзакции через нового единственного `StayBookingReleaser`). Заказ — тот же `HoldMinutes`, **та же задача** `stays-hold-expiry` (второй проход), та же гонка «таймер / файл». Лимиты по номеру — **раздельные счётчики** (`Stays:Services:PhoneLimits`) под **общим** замком номера | §39.7 |
| A39-5 | Типы уведомлений | Ёмкость маски — ложная проблема: салонная `EnabledTypeMask` применяется **только** к `BookingTypes` (0…6). Закрываем латентную дыру (`CompanyNotificationsController.BuildMask` сдвигает любой тип из запроса; в C# `1 << 32 == 1`) фильтром по `BookingTypes` + страж-тест; перечисление растёт дальше 31 без маски. Новые значения **27…38**, 39…40 — циклу 40. `StayNotificationPlan.IsStayType` (зашитые 16…26) → `NotificationTypeCatalog` | §39.9 |
| A39-6 | Шахматка, график | Общая ревизия `StaysSettings.BookingsRevision`. В шахматке — группа «Услуги» (ячейка: число и «с 16:00», «до 01:00»). «День услуг» — маршрут `service-day` (минуты бизнес-дня, полоса подготовки, перенос зазора за 06:00 в следующий день). График — в конец `ScheduleDayDto` массив `sessions[]` одной формы для всех ролей | §39.10 |
| A39-7 | Бизнес-день | Время в модели — **минуты от 00:00 даты D** в `[B, B + 1440]`, `B = Stays:Services:BusinessDayStartMinute = 360` (конфигурация). Окна — `StartMinute/EndMinute`, правила цены — `FromHour/ToHour` (6…30). Чистые `BusinessClock` и `ServiceTimeFormat` (гостю — календарные даты, персоналу — формат SPEC §4.9); C# + TS по векторам | §39.3 |
| A39-8 | Напоминание | Поля в `StaysSettings` + журнал `StaysReminderTemplateChanges`. Чистый `ArrivalReminderTemplate` (валидация, рендер в трёх режимах, фильтр push). **NULL-шаблон идёт прежним кодом** — байт-в-байт гарантирован конструкцией. Снимок текста страницы — колонка брони, пишется задачей и без каналов | §39.11 |
| A39-9 | Адреса dom | Услуга: `/<slug>/uslugi/<serviceSlug>` + резерв `uslugi` (и слов календарей) в адресах **домов** (`reservedHouseSlugs`). Отдельный сеанс: `/s/<token>` (`s` уже в `reservedSlugs`) | §39.14.1 |
| A39-10 | Права | `StaysPermission` += `ManageServices` (владелец), `EditServiceContent`, `ManageServiceDates` (владелец, управляющий). Сеансы — `ViewBookings`/`ManageBookings`/`ViewSchedule`. Напоминание — `ManageCompany` | §39.12 |
| A39-11 | Контракт | Свой `contracts/cycle39/openapi.yaml`: новые маршруты целиком + изменённые маршруты цикла 37 с частичными DTO (`additionalProperties: true`). В `contracts/cycle37/openapi.yaml` — только дописывание значений перечислений, которые начнут приходить в ответах cycle37, и чистка под lint (US-39-22) | §39.15.2 |
| A39-12 | Витрина, демо | 11 новых таблиц — в `ShowcaseOwnership.NeverWritten` | §39.2.6 |
| A39-13 | Retention, права субъекта | 4 новых правила + 3 расширенных; снимок напоминания стирается при обезличивании; выгрузка — `stayServiceOrders[]` и `sessions[]` в бронях | §39.13 |
| A39-14 | Задел iCal | Ночи пишет только `HouseOccupancyWriter`; каскад «бронь → сеансы» идёт через `StayBookingReleaser`, который сначала вызывает писателя ночей; сеансы в `HouseOccupancies` не пишутся; `ExternalCalendar = 2` не занят; слова календарей зарезервированы; `/api/stays/ical/` не занят; бенчмарк параметризован | §39.15.4 |

---

## §39.1. Стек: новых зависимостей — ноль

Цикл расширяет модуль «Дома» (ASP.NET Core 8 + EF Core 8 + PostgreSQL 16; фронт dom — третье приложение npm-пакета).
Новых пакетов нет: `tstzrange` и `EXCLUDE` по нему работают на уже включённом `btree_gist` (нужен для `=` по `uuid`
внутри gist), фильтр текста — `System.Text.RegularExpressions`, форматирование — `StayTime`/`StaysTexts`. Нагрузка §6
SPEC (≤ 50 сеансов в день на платформу, ≤ 3 услуги на компанию) не требует ни кеша, ни отдельного сервиса: расписание
услуги на 14 дней — 4 индексных запроса и расчёт в памяти. Масштаб «как сервис»: состояния в памяти процесса цикл не
добавляет; второй экземпляр API возможен на тех же условиях, что в цикле 37 (C37-11).

---

## §39.2. Модель данных (A39-1)

Сущности — `ServiceBooking.Core/Entities/`, конфигурация — `AppDbContext`. Конвенции §37.2 без изменений: перечисления —
числом, только дописыванием; деньги — `int` рублей (`*Rub`); моменты — `timestamptz` UTC; даты — `date`.
**Время суток услуги — `int` минут от 00:00 даты бизнес-дня** (§39.3), а не `time`: `TimeOnly` не выражает «02:00
следующих суток».

### §39.2.1 Почему сеанс — одна сущность, а отдельный заказ — конверт

| Вариант | Цена | Решение |
|---|---|---|
| «Бронь без дома»: отдельный сеанс = `StayBooking` с `HouseId = NULL` | `HouseId`, даты, `Nights`, CHECK дат, инвариант §37.2.8-3 «активная бронь ⇔ занятость ночей» — NOT NULL и читаются ~60 местами цикла 37. Каждое получает ветку «а если без дома» — регресс в хрупкой зоне C37-9 | отвергнут |
| Две независимые сущности «сеанс в брони» и «отдельный сеанс» | два ограничения БД на одну услугу не держат пересечение **между** видами | отвергнут |
| **Сеанс = занятость услуги + снимок** (одна таблица, одно `EXCLUDE`), родитель — бронь **или** заказ; заказ — конверт с жизненным циклом брони | ~30 колонок конверта повторяют форму `StayBooking` (прецедент — `StayGuestPushSubscription` как копия формы `OrderPushSubscription`); **код** переиспользуется: `StayStateMachine`, `StaysBookingGate`, `PublicStayToken`, `StayPaymentProofService.ReadAsync`, очереди, push, `StayTime`, тексты | **принят** |

### §39.2.2 Изменения существующих таблиц

| Таблица | Изменение | Зачем |
|---|---|---|
| `StaysSettings` | `AcceptServiceOrdersWithoutStay bool NOT NULL DEFAULT false`; `ArrivalReminderTime time NOT NULL DEFAULT '18:00'` (08:00…22:00, шаг 30 — кодом); `ArrivalReminderTemplate varchar(700) NULL` (NULL = текст по умолчанию); `ArrivalReminderPushText bool NOT NULL DEFAULT false` | Р39-7, US-39-19/20 |
| `StayBookings` | `ArrivalReminderPageText varchar(1200) NULL` — снимок текста для страницы брони (Р39-17); `ArrivalReminderSentAtUtc timestamptz NULL` — момент снимка (отличается от `ArrivalReminderQueuedAtUtc`: та ставится и при выключенном напоминании) | §39.11 |
| `StayBookingCharges` | `ServiceSessionId uuid NULL` FK → `StayServiceSessions` `Restrict`; индекс. `StayChargeKind` += `ServiceSlot = 5`, `ServiceItem = 6`; CHECK `NOT ("Kind" IN (5,6) AND "PrepayEligible")` | A7 цикла 37 |
| `StayBookingEvents` | `ServiceSessionId uuid NULL` FK `SetNull`; `StayBookingEventKind` += `ServiceSessionAdded = 11`, `ServiceSessionCancelledByGuest = 12`, `ServiceSessionCancelledByOwner = 13`, `ServiceSessionsReleased = 14`, `ArrivalReminderSent = 15` | журнал брони (SPEC §4.7) |
| `StayPaymentProofs` | `StayBookingId` → **NULL**; `StayServiceOrderId uuid NULL` FK `Restrict`; CHECK `num_nonnulls("StayBookingId","StayServiceOrderId") = 1`; индекс | общий механизм файлов |
| `StayGuestPushSubscriptions`, `StayGuestPushNotifications` | то же: `StayBookingId` → NULL, `StayServiceOrderId` NULL FK `Cascade`, CHECK «ровно один»; к upsert-индексу `(StayBookingId, Endpoint)` — частичный `(StayServiceOrderId, Endpoint) WHERE "StayServiceOrderId" IS NOT NULL` | push со страницы заказа |
| `OutboundNotifications`, `StaffPushNotifications`, `StaffMaxMessages` | `StayServiceOrderId uuid NULL` FK `SetNull`; CHECK «не больше одного из `BookingId`/`OrderId`/`StayBookingId`/`StayServiceOrderId`» — **пересоздаётся** | A14 цикла 37 |
| `PublicArea` (enum) | `+ StayServices` | фото услуг |
| `NotificationType` (enum) | `+ 27…38` (§39.9.1) | |

Цена `StayBookingId → NULL` в трёх сущностях цикла 37: тип `Guid` → `Guid?`; компилятор показывает все места (≈ 12:
`StayPaymentProofService`, `StayGuestPushQueue`, `StaysGuestPushDispatchTask`, два правила retention, `StayPersonalData`,
`StayDtoMapper`, тесты), правка — `.Value` там, где владелец заведомо бронь. Поведение цикла 37 не меняется; регресс — CY37-*.

### §39.2.3 Услуга и её настройки

**`StayService`** (`StayServices`)

| Поле | Тип | Смысл |
|---|---|---|
| `Id`, `CompanyId` (FK `Restrict`) | uuid | |
| `Slug` | varchar(50) | уникален `(CompanyId, Slug)`; `serviceSlugPattern` из `contracts/cycle39/dom-routes.json`; предлагается из названия (`SlugTransliterator`, суффиксы `-2`) |
| `Name` | varchar(100) | |
| `Description` | varchar(2000) NULL | |
| `MinHours`, `MaxHours` | int | 1…12 (2), `MinHours`…12 (6); CHECK `1 <= MinHours <= MaxHours <= 12` |
| `StepMinutes` | int | 30 / 60 (60); CHECK `IN (30, 60)` |
| `BufferMinutes` | int | 0…240, кратно 15 (30); CHECK |
| `ShowBufferToGuests` | bool | false (Р39-3) |
| `MinLeadMinutes` | int | 0…2880, кратно 30 (60) |
| `StandalonePrepayPercent` | int NULL | NULL = «нет», 1…100 |
| `CancellationPolicy` | int → `StayServiceCancellationPolicy { NoDeductions = 0, PreparationCosts = 1 }` | NoDeductions (ЮР39-1) |
| `CancellationBoundaryHours` | int | 12; границы — `Stays:Services:CancellationBoundaryHours` (Min 3, Max 24) |
| `AvailableForHouseBookings` | bool | true |
| `IsPublished` | bool | false |
| `Position` | int | порядок |
| `ArchivedAtUtc` | timestamptz NULL | архив; CHECK `NOT ("IsPublished" AND "ArchivedAtUtc" IS NOT NULL)` |
| `CreatedAtUtc`, `UpdatedAtUtc` | | |

Индексы: уникальный `(CompanyId, Slug)`; `(CompanyId, Position)`. Техпотолок — 20 услуг на компанию (`Stays:Services:MaxPerCompany`).

**`StayServicePhoto`** — форма `HousePhoto` с `ServiceId` (FK `Cascade`), `PublicArea.StayServices`, ≤ 10 (`Stays:Services:MaxPhotos`).

**`StayServiceWeeklyWindow`** — `Id`, `ServiceId` (FK `Cascade`), `DayOfWeek int` (ISO: 1 = Пн … 7 = Вс — день недели
**бизнес-дня**), `StartMinute`, `EndMinute`. CHECK `DayOfWeek BETWEEN 1 AND 7`, `EndMinute > StartMinute`. Правила §39.3.3
— кодом (`ServiceScheduleRules`). «Закрыто» = нет строк.

**`StayServiceDateOverride`** — `Id`, `ServiceId` (FK `Cascade`), `BusinessDate date`, `IsClosed bool`,
`WindowsJson jsonb` (`[{ "startMinute": 1080, "endMinute": 1560 }]`, пусто при `IsClosed`), `Comment varchar(300) NULL`
(запасной путь «закрыть время с комментарием», если ручной сеанс P1 не успеваем), `UpdatedAtUtc`, `UpdatedByUserId`.
Уникальный `(ServiceId, BusinessDate)`. Удаление строки = «вернуть как в шаблоне».

**`StayServiceScheduleEvent`** — журнал расписания (US-39-03): `Id`, `ServiceId`, `CompanyId`, `Kind` →
`StayServiceScheduleEventKind { WeeklyTemplateChanged = 0, DateOverrideSet = 1, DateOverrideRemoved = 2 }`,
`BusinessDate date NULL`, `OccurredAtUtc`, `ActorUserId`, `ActorNameSnapshot`, `BeforeJson`, `AfterJson`. Пишет только
`ServiceScheduleWriter` (вместе с ревизией).

**`StayServicePriceRule`** — `Id`, `ServiceId` (FK `Cascade`), `DaysMask int` (бит 0 = Пн … бит 6 = Вс; 1…127),
`FromHour int` (6…29), `ToHour int` (`FromHour + 1`…30), `PriceRub int` (1…100 000), `CreatedAtUtc`, `UpdatedAtUtc`.
CHECK диапазонов. Непересечение — кодом под замком услуги (`ServicePriceRules`). ≤ 50 правил на услугу.

**`StayServiceItem`** — `Id`, `ServiceId` (FK `Cascade`), `Name varchar(100)`, `PriceRub int` (0…100 000; 0 =
«бесплатно»), `MaxPerSession int` (1…50, 10), `IsActive bool`, `Position int`. ≤ 20 на услугу. Удаление строки разрешено
всегда: сеансы хранят снимок.

### §39.2.4 Сеанс и заказ услуги

**`StayServiceSession`** (`StayServiceSessions`) — **единственное** место занятости услуги. Пишет только `ServiceSessionWriter`.

| Поле | Тип | Смысл |
|---|---|---|
| `Id`, `CompanyId`, `ServiceId` (FK `Restrict`) | uuid | |
| `StayBookingId` | uuid NULL FK `Restrict` | сеанс в брони дома |
| `StayServiceOrderId` | uuid NULL FK `Restrict` | отдельный сеанс (один на заказ) |
| `BusinessDate` | date | снимок бизнес-дня старта |
| `StartMinute` | int | минута бизнес-дня старта |
| `Hours` | int | 1…12 |
| `StartUtc`, `EndUtc` | timestamptz | `EndUtc = StartUtc + Hours ч` |
| `BufferMinutesSnapshot` | int | зазор на момент создания |
| `OccupiedUntilUtc` | timestamptz | `EndUtc + зазор` — правая граница занятости |
| `State` | int → `StayServiceSessionState { Active = 0, CancelledByGuest = 1, CancelledByOwner = 2, ReleasedWithBooking = 3, ReleasedWithOrder = 4 }` | |
| `ReleasedAtUtc` | timestamptz NULL | не NULL ⇔ `State <> Active` (CHECK) |
| `ServiceNameSnapshot` | varchar(100) | |
| `HourPricesJson` | jsonb | `[{ "startMinute": 1380, "priceRub": 2000 }, …]` |
| `ItemsJson` | jsonb | `[{ "itemId", "name", "unitPriceRub", "quantity", "amountRub" }]` — только позиции с количеством > 0 |
| `ServiceAmountRub`, `ItemsAmountRub`, `TotalRub` | int | |
| `AddedByKind` | int → `StayActorKind` | гость / покупатель / персонал |
| `AddedByUserId`, `AddedByNameSnapshot` | NULL | |
| `RequestBasis` | int NULL → `StayServiceRequestBasis { Phone = 0, InPerson = 1, Messenger = 2 }` | обязательно, если добавил персонал (ЮР39-6); CHECK `("AddedByKind" = 2) = ("RequestBasis" IS NOT NULL)` |
| `AddNoticeVersion` | varchar(64) NULL | версия `StayServiceAddNotice` в момент выбора гостем (Т39-05) |
| `IdempotencyKey` | uuid NULL | уникальный `(StayBookingId, IdempotencyKey) WHERE "IdempotencyKey" IS NOT NULL` |
| `Version` | int | действия персонала над сеансом в брони |
| `StatusReason` | varchar(300) NULL | причина отмены компанией (гость видит) |
| `CreatedAtUtc` (= момент выбора), `UpdatedAtUtc` | | |

- **`EXCLUDE USING gist ("ServiceId" WITH =, tstzrange("StartUtc", "OccupiedUntilUtc", '[)') WITH &&) WHERE ("ReleasedAtUtc" IS NULL)`** — имя `EX_StayServiceSessions_NoOverlap` (`migrationBuilder.Sql`, как в цикле 37). `OccupiedUntilUtc` — хранимая колонка, а не генерируемая: `timestamptz + interval` в PostgreSQL не `IMMUTABLE`.
- CHECK `num_nonnulls("StayBookingId", "StayServiceOrderId") = 1`, `"EndUtc" > "StartUtc"`, `"OccupiedUntilUtc" >= "EndUtc"`, `"Hours" BETWEEN 1 AND 12`, `("ReleasedAtUtc" IS NULL) = ("State" = 0)`.
- Уникальный частичный `(StayServiceOrderId) WHERE "StayServiceOrderId" IS NOT NULL`.
- Индексы: `(ServiceId, StartUtc) WHERE "ReleasedAtUtc" IS NULL`, `(StayBookingId)`, `(CompanyId, BusinessDate)`.

**`StayServiceOrder`** (`StayServiceOrders`) — конверт отдельного сеанса, форма `StayBooking` §37.2.4 без полей дома:

| Группа | Поля |
|---|---|
| Ключи | `Id`, `CompanyId`, `ServiceId` (FK `Restrict`), `PublicToken varchar(64)` (уникальный; `PublicStayToken.Generate()` — 256 бит), `IdempotencyKey uuid` (уникальный `(CompanyId, IdempotencyKey)`) |
| Статус | `Status` → **`StayBookingStatus`**, `Version`, `IsManual`, `HoldExpiresAtUtc NULL`, `TerminalAtUtc NULL`, `StatusReason varchar(300) NULL` |
| Гость | `GuestKind` (`StayActorKind`), `GuestUserId text NULL` FK `SetNull`, `GuestName varchar(100) NULL`, `GuestPhone varchar(20) NULL`, `Comment varchar(500) NULL`, `RequestBasis NULL` (ручной заказ) |
| Деньги | `ServiceAmountRub`, `ItemsAmountRub`, `TotalRub`, `PrepayPercentSnapshot`, `PrepayRub`, `DueOnSiteRub` |
| Снимки | `CancellationPolicySnapshot`, `CancellationBoundaryHoursSnapshot`, `TimeZoneIdSnapshot`, `PaymentDetailsSnapshot`, `PaymentPurposeSnapshot`, `ProviderSnapshotJson` |
| Тексты и согласия | `ConsentPrivacyVersion`, `ConsentTermsVersion`, `ConsentAcceptedAtUtc`, `BookingNoticeVersion` (`StayServiceBookingNotice`), `BookingTermsVersion` (`StayServiceBookingTerms`), `CancellationTermsVersion` (`StayServiceCancellationTerms`), `NotifyByMessenger`, `MessengerConsentVersion`, `MessengerConsentAtUtc` |
| Факт оплаты | `PaymentConfirmedAtUtc`, `PaymentConfirmedByUserId`, `PaymentConfirmedByNameSnapshot`, `PaymentProofsPurgedAtUtc` |
| Отметки | `HoldReminderQueuedAtUtc`, `PersonalDataErased`, `CreatedAtUtc`, `UpdatedAtUtc` |

Индексы: `(PublicToken)` уник., `(CompanyId, IdempotencyKey)` уник., `(CompanyId, Status)`, `(HoldExpiresAtUtc) WHERE "Status" = 0`,
`(GuestPhone, CreatedAtUtc) WHERE "GuestPhone" IS NOT NULL`, `(GuestUserId, CreatedAtUtc)`.

**`StayServiceOrderEvent`** — журнал заказа, единственный писатель `StayServiceOrderEventLog` (ревизия + планировщик, как
`StayBookingEventLog`). Поля — как `StayBookingEvent` с `StayServiceOrderId`. `Kind` → `StayServiceOrderEventKind
{ Created = 0, PaymentProofUploaded = 1, PaymentProofViewed = 2, PaymentConfirmed = 3, HoldExpired = 4, PaymentRejected = 5,
CancelledByGuest = 6, CancelledByOwner = 7, PaymentProofsPurged = 8, PersonalDataErased = 9 }`.

**`StaysReminderTemplateChange`** — история настройки напоминания (US-39-20, Т39-12/13): `Id`, `CompanyId`,
`ChangedAtUtc`, `ChangedByUserId`, `ChangedByNameSnapshot`, `PreviousTime`, `NewTime`, `PreviousTemplate NULL`,
`NewTemplate NULL`, `PreviousPushText`, `NewPushText`, `OwnerNoticeVersion varchar(80)` (версия
`StayReminderTemplateOwnerNotice` или `fallback:<sha256>`), `PushNoticeVersion varchar(80) NULL` (при включении push),
`CodeMarkersConfirmed bool`, `CodeMarkersHit varchar(200) NULL` (что нашёл сервер, как `AdMarkersHit` у салонов).
Append-only, хранится, пока существует компания.

### §39.2.5 Миграция — одна (`Cycle39StaysServices`), закреплённым `dotnet-ef` 8.0.11, один разработчик (BE-39-M)

Состав: колонки §39.2.2 (с дефолтами — компании цикла 37 получают 18:00, NULL-шаблон, push выкл., заказы выкл.);
пересоздание CHECK «не больше одного владельца» в трёх очередях; nullable + CHECK у трёх таблиц цикла 37; 11 таблиц;
`EX_StayServiceSessions_NoOverlap` — `migrationBuilder.Sql`; данные: домам с адресом из `reservedHouseSlugs` —
`UPDATE "Houses" SET "Slug" = "Slug" || '-dom' WHERE "Slug" IN (...)` (на стенде ожидается 0 строк).
`Down()`: обратные шаги; ⚠️ упадёт, если уже есть файлы или push-подписки заказов (`StayBookingId IS NULL`) — `DEPLOY.md` §28,
как C37-6. **R39-6:** при мерже с циклом 38 миграция пересобирается поверх влитой, значения перечислений сверяются.

### §39.2.6 Инварианты модели (QA проверяет тестами)

1. Строки новых таблиц существуют только у компаний `Kind = Stays` (матрица изоляции §37.3.2 дополняется маршрутами цикла).
2. Два неосвобождённых сеанса одной услуги с пересечением `[StartUtc, OccupiedUntilUtc)` не существуют (БД).
3. Сеанс в брони `Active` ⇒ бронь в `Held`/`AwaitingPaymentCheck`/`Confirmed`; бронь в конечном статусе ⇒ все её сеансы
   `≠ Active` — **в той же транзакции** (`StayBookingReleaser`).
4. Сеанс заказа `Active` ⇔ заказ не в конечном статусе.
5. Деньги брони: `Σ StayBookingCharges.AmountRub = TotalRub`; строки `ServiceSlot/ServiceItem` — `PrepayEligible = false`;
   `PrepayRub` при добавлении/отмене сеанса **не меняется**; `DueAtCheckInRub = TotalRub − PrepayRub`.
6. Деньги заказа: `TotalRub = ServiceAmountRub + ItemsAmountRub`; `PrepayRub = RoundHalfUp(ServiceAmountRub × % / 100)`;
   `DueOnSiteRub = TotalRub − PrepayRub`.
7. Каждое изменение сеанса в брони — событие `StayBookingEvent` (с `ServiceSessionId`); заказа — `StayServiceOrderEvent`;
   то и другое поднимает `BookingsRevision`.
8. Правила цены одной услуги не пересекаются; окна одного бизнес-дня не пересекаются и лежат в `[B, B+1440]`.
9. Новые таблицы — в `ShowcaseOwnership.NeverWritten` (иначе падает `ShowcaseOwnershipCoverageTests`).

---

## §39.3. Бизнес-день и время после полуночи (A39-7)

### §39.3.1 Хранение

- **Бизнес-день D** = `[D 00:00 + B, D+1 00:00 + B)` по поясу компании, `B = Stays:Services:BusinessDayStartMinute`
  (360 = 06:00). Конфигурация, а не настройка компании: граница — свойство модели; разные границы у разных компаний
  умножили бы ошибки R39-3.
- Время суток услуги — **минуты от 00:00 даты D**, диапазон `[B, B + 1440]`. «02:00 следующих суток» пятницы = `1560`.
  Окно — `StartMinute < EndMinute`; правило цены — часы `FromHour < ToHour` в `[B/60, B/60 + 24]`.
- Отвергнуто: `TimeOnly` (не выражает 24:00+), «начало + длительность» (неудобно для пересечений и редактора), смещение
  0…1440 от 06:00 (18:00 = 720 читалось бы неестественно в API и логах).
- Валидатор на старте (`DeploymentSafetyChecks.ValidateStaysServices`): `B` ∈ 0…720, кратно 60. **Менять `B` при наличии
  данных нельзя** (правила 18…30 при `B = 300` станут невалидными) — строка в `DEPLOY.md` §28.

### §39.3.2 `BusinessClock` — одна чистая функция в обе стороны (C# `Services/Stays/Services/BusinessClock.cs`, TS `dom/src/utils/businessClock.ts`)

```
BusinessDateOf(tz, utc)              → (DateOnly date, int minute)  // local = utc→tz; date = Date(local − B мин); minute = (local − date 00:00) в минутах
ToUtc(tz, DateOnly date, int minute) → DateTime utc                 // StayTime.ToUtc(tz, date, 00:00) + minute мин
TodayBusinessDate(tz, nowUtc)        → DateOnly                     // «сегодня» для расписания услуг (в 01:00 субботы — пятница)
DayOfWeekIso(DateOnly date)          → 1…7                          // день недели бизнес-дня = день недели даты D
```

Все остальные функции (окна, цены, старты, формат) получают уже `(date, minute)` и `utc` и сами поясов не считают.
Векторы — `service-vectors.json → businessDay` (полночь, 05:59, 06:00, 31 дек → 1 янв, конец месяца).

### §39.3.3 Валидация окон и правил на границах (`ServiceScheduleRules`, `ServicePriceRules` — чистые; векторы `windows`)

| Проверка | Отказ (тексты — контракт §39.31) |
|---|---|
| `StartMinute`, `EndMinute` кратны 30 | 400 «Время — с шагом 30 минут» |
| `B ≤ Start < End ≤ B + 1440` | 400 «Окно должно уложиться с 06:00 до 06:00 следующего дня» |
| ≤ 3 окна на день | 400 «Не больше трёх окон в день» |
| окна дня не пересекаются (касание допустимо) | 400 «Окна {a} и {b} ({день}) пересекаются» |
| правило: хотя бы один день, `B/60 ≤ From < To ≤ B/60 + 24` | 400 «Выберите дни недели» / «Часы — с 06:00 до 06:00 следующего дня» |
| правила не пересекаются | 409 `PriceRuleOverlap` с `conflictingRule` |

### §39.3.4 Формат времени — `ServiceTimeFormat` (тексты собирает сервер; TS-двойник — для редактора и выбора)

| Кому | Случай | Текст (векторы `format`) |
|---|---|---|
| **гость** (ЮР39-8; все каналы, `{Услуги}`, push) | в одних сутках | «пт 15 янв, 18:00 — 21:00» |
| | через полночь | «пт 15 янв, 22:00 — сб 16 янв, 01:00» |
| | старт после полуночи | «сб 16 янв, 00:30 — 02:30» |
| | смена года | «чт 31 дек, 23:00 — пт 1 янв, 01:00» |
| гость, список стартов выбранной даты | старт после полуночи | «00:30 (ночь на сб)»; дата выбора — «пт 15 янв»; слово «бизнес-день» не используется |
| гость, таблица цен | правило | «Пт 18:00 — 02:00 (ночь на сб) — 2 000 ₽/ч» |
| **персонал** (кабинет, push/MAX персоналу) | формат SPEC §4.9 | «Пт, 15 янв · 22:00 – 01:00 (сб)», «Пт, 15 янв · 00:30 (ночь на сб) – 02:30» |
| редактор окон и цен | окно, правило | «18:00 – 02:00 (след. дня)» |

---

## §39.4. Доступные старты — `ServiceSlotCalculator` (A39-2)

### §39.4.1 Почему не обобщаем `SlotCalculator`

Салонный калькулятор (`Services/SlotCalculator.cs`) — сетка 30 минут одних суток в `TimeOnly`, обрывается до 24:00, без
зазора, цены по часу, длительности на выбор и проверки «внутри проживания»; входы — рабочие часы мастера. Чтобы он закрыл
услуги, пришлось бы поменять его сигнатуру и тип времени и перепроверять салонную запись (регресс циклов 3–6). SPEC
требует, чтобы салонная запись не менялась. Берём **принцип** (одна функция для показа и проверки, проверка поверх
показа), а не код.

### §39.4.2 Сигнатура (`Services/Stays/Services/ServiceSlotCalculator.cs`, чистая, без EF)

```
ServiceStarts Calculate(ServiceSlotInput input)
  input: businessDate, timeZoneId, nowUtc, horizonDays,
         service { minHours, maxHours, stepMinutes, bufferMinutes, minLeadMinutes (0 у персонала) },
         windows[]   (окна этого бизнес-дня: ручная дата или шаблон — выбирает ServiceScheduleResolver),
         priceRules[],
         occupied[]  { startUtc, occupiedUntilUtc } — активные сеансы услуги, пересекающие [D+B−24ч, D+B+48ч),
                     уже без «истёкших удержаний» родителя,
         stayRange?  { fromUtc, toUtc } — сеанс в брони: дата заезда + время заезда … дата выезда + время выезда (снимок)
  → { reason?: DateInPast | BeyondHorizon | Closed | NoStarts, starts[] { startMinute, startUtc, maxHours } }

bool IsStartAllowed(input, startMinute, hours)
  => Calculate(input).starts.Any(s => s.StartMinute == startMinute && s.MaxHours >= hours && hours >= service.minHours)
```

Алгоритм: `today = TodayBusinessDate(tz, now)`; дата вне `[today, today + horizonDays − 1]` → пусто с причиной. Для
каждого окна, для каждого `s = окно.Start + k × step`, `s < окно.End`: `h` растёт от 1 до `maxHours`, пока одновременно:
`s + 60h ≤ окно.End`; `[sUtc, sUtc + h + buffer)` не пересекает ни один `occupied` (реальное время — полночь и 06:00
невидимы; зазор может выйти за конец окна); у часа `h` есть цена (§39.6.1); при `stayRange` — `[sUtc, sUtc + h) ⊂ stayRange`;
`sUtc ≥ nowUtc + minLead`. Все условия монотонны по `h`, поэтому `maxHours` — первый провал минус один; старт доступен при
`maxHours ≥ minHours`. Сортировка — по `startMinute` (старты после полуночи — в конце списка того же дня).

`ServiceSlotCalculator.Diagnose(input, startMinute, hours)` выбирает **текст** отказа для 409 (первый провал по порядку
`DateInPast → BeyondHorizon → HoursOutOfRange → OutsideStay → TooEarly → StartUnavailable → NoPriceForHours → SlotTaken`),
но **решает** всегда `IsStartAllowed` — правило «показали ⇒ сервер примет» (SPEC §4.3).

Календарь дат (`availability`, 14 дней, p95 < 500 мс): 4 запроса (окна шаблона, ручные даты, правила цены, активные сеансы
диапазона ± 1 день) + 14 вызовов `Calculate` в памяти; возвращается только `hasStarts` по дате. Векторы — `starts`.

---

## §39.5. Гарантия от двойной брони сеанса (A39-3) — главный риск цикла

### §39.5.1 Три уровня, как у ночей (§37.5.1)

1. **Ограничение БД** `EX_StayServiceSessions_NoOverlap` — `23P01` → `ServiceSessionWriter.IsOverlapViolation` → 409
   `SlotTaken`. `tstzrange` по реальным моментам: «пт 23:00–01:00» и «пт 00:30 (ночь на сб)–02:30» — один отрезок времени;
   «пт 04:00–06:00 + 30 мин» и «сб 06:00» пересекаются на `[23:00Z, 23:30Z)`. Границы суток и бизнес-дня для ограничения не
   существуют — подтверждено (векторы `overlap` + функциональные тесты §39.5.4).
2. **Advisory-lock `stay-service:{serviceId}`** — сериализует всё, что **занимает** время услуги: создание заказа,
   добавление сеанса к брони (гостем и персоналом), бронь дома с сеансами, правки расписания и цен услуги. Под ним —
   правила, которые ограничение не выражает (окна, цены, лимит сеансов на бронь, ленивое снятие). **Освобождение**
   (отмена, каскад, таймер) замок услуги **не берёт**: освобождение не нарушает ограничение, а читающий под замком в
   худшем случае видит сеанс ещё занятым.
3. **Оптимистичная версия**: `StayServiceOrder.Version` (действия персонала над отдельным сеансом),
   `StayServiceSession.Version` (отмена сеанса в брони персоналом); `StayBooking.Version` растёт при добавлении и отмене
   сеанса — сумма брони изменилась, карточка персонала обязана перечитаться.

### §39.5.2 Единый порядок блокировок (расширение C37-9)

```
stay-guest-phone:{phone}      (только создание гостем: бронь дома, заказ услуги — общий ключ, раздельные счётчики)
→ stay-house:{houseId}        (всё, что меняет ночи; бронь дома с сеансами; добавление сеанса к брони)
→ stay-service:{serviceId} …  (несколько — по возрастанию Guid как строки)
→ stay-booking-proofs:{id} / stay-service-order-proofs:{id}   (только загрузка подтверждения)
→ строка StayBookings / StayServiceOrders (FOR UPDATE или условный UPDATE)
→ строки HouseOccupancies
→ строки StayServiceSessions (+ StayBookingCharges)
→ строка StaysSettings (ревизия — всегда последней, через журналы)
```

| Путь | Порядок |
|---|---|
| бронь дома с сеансами (US-39-10) | телефон → дом → услуги ↑ → вставки → ревизия |
| добавление сеанса к брони (гость, персонал) | дом брони → услуга → строка брони `FOR UPDATE` (перепроверка «активна, выезд не наступил, ≤ 5 сеансов») → вставки → ревизия |
| заказ услуги | телефон → услуга → вставки → ревизия |
| отмена сеанса в брони (гость, персонал) | строка брони `FOR UPDATE` → строка сеанса → строки суммы → ревизия (дом и услугу не берёт) |
| конечный статус брони (пути цикла 37) | дом → строка брони → ночи → **сеансы (каскад)** → ревизия |
| переходы заказа, таймер заказа, файл | (proofs-lock) → строка заказа → сеанс → ревизия |
| правка расписания / цен / настроек услуги | услуга → ревизия |

**Ленивое снятие под замком услуги.** Истёкшее удержание родителя, чей сеанс мешает новому старту:
- **заказ** — снимается тем же условным `UPDATE`, что задача (строка заказа идёт после замка услуги — порядок соблюдён);
- **бронь дома** — её снятие требует замка **дома**, который стоит **раньше** услуги. Берём его
  `AdvisoryLock.TryAcquireAsync` (неблокирующий, уже есть): получилось — `StayBookingTransitionService.ExpireAsync`
  (освобождает ночи и сеансы); не получилось — время считается занятым, 409 `SlotTaken` (задача снимет ≤ 15 с). Ожидания
  нет — взаимоблокировка невозможна. Замок своего же дома (бронь дома с сеансами) входится повторно.

Чтение (старты, календарь, шахматка) считает свободным сеанс, у родителя которого `Status = Held` и
`HoldExpiresAtUtc ≤ now` (LEFT JOIN брони и заказа; таймер в сеанс не денормализуется — меньше инвариантов и ни одной
правки пути загрузки подтверждения цикла 37).

### §39.5.3 Каскад «бронь → сеансы» — `StayBookingReleaser` (единственная точка)

Три пути цикла 37, ведущие бронь в конечный статус (`StayBookingTransitionService.StaffActionAsync`, `CancelByGuestAsync`,
`ExpireAsync`), сейчас зовут `HouseOccupancyWriter.ReleaseBookingAsync`. Вызов заменяется на
`StayBookingReleaser.ReleaseAsync(booking, now)`:
1. `HouseOccupancyWriter.ReleaseBookingAsync` (ночи — по-прежнему единственный писатель; цикл 40 повесит сюда применение
   внешней занятости);
2. `ServiceSessionWriter.ReleaseForBookingAsync(bookingId, now)` — `UPDATE … SET "State" = 3, "ReleasedAtUtc" = now
   WHERE "StayBookingId" = … AND "ReleasedAtUtc" IS NULL`;
3. если сеансы были — событие `ServiceSessionsReleased` (`DetailsJson: {sessionIds}`) в журнал брони; уведомлений нет (их
   несёт сам конечный статус).

Страж-тест (по образцу `CompanyKindBranchGuardTests`): `ReleaseBookingAsync(` вызывается только из `StayBookingReleaser`.

### §39.5.4 Тесты (обязательны, SPEC §6 «Целостность»)

- N = 20 параллельных заказов одного старта → создан ровно 1, остальные 409 `SlotTaken`.
- Параллельно «пт 23:00–01:00» и «пт 00:30 (ночь на сб) – 02:30» (разные пути: заказ ↔ добавление к брони) → ровно один.
- «пт 04:00–06:00», зазор 30 мин, и «сб 06:00» → второй отклонён и расчётом, и БД (тестовый двойник писателя без замка).
- Параллельно: бронь дома с сеансом ↔ заказ на тот же старт ↔ добавление сеанса к другой брони; бронь дома с двумя услугами
  ↔ две брони с теми же услугами в обратном порядке — без 40P01.
- Конечный статус брони ↔ одновременное добавление сеанса к ней: сеанс либо не создан, либо создан и освобождён каскадом —
  «активный сеанс у неактивной брони» не существует.
- Гонка «таймер / подтверждение оплаты» заказа (−1 с, 0, +1 с, параллельно) → один исход.
- Ленивое снятие: истёкший заказ и истёкшая бронь дома на нужном времени → новый сеанс создан сразу; бронь, чей дом
  заблокирован другим запросом, → 409 `SlotTaken` без ожидания.

---

## §39.6. Цена и деньги — чистые функции

### §39.6.1 `ServicePricing.HourPrices(rules, businessDate, startMinute, hours) → int?[]`

Час `k` (0…hours−1) начинается в минуту `m = startMinute + 60k` бизнес-дня старта. Цена — правило с битом
`DayOfWeekIso(businessDate)` и `FromHour × 60 ≤ m < ToHour × 60`; нет правила → `null` → старт с таким часом недоступен.
Час 23–24 и час 0–1 сеанса пятницы — оба по правилам **пятницы** (Р39-16). Старт 18:30 — часы 18:30, 19:30, 20:30, каждый
по правилу, содержащему его начало. Сумма — всегда целые рубли.

### §39.6.2 `ServiceMoney.Quote(hourPrices, items, prepayPercent?) → ServiceQuoteResult`

```
Стоимость сеанса = Σ hourPrices
Позиции          = Σ quantity × unitPriceRub       (quantity 0…MaxPerSession; только активные позиции; по умолчанию 0)
Итог             = Стоимость сеанса + Позиции
Предоплата       = prepayPercent ? (Стоимость сеанса × % + 50) / 100 : 0    (позиции не входят; половина — вверх)
На месте         = Итог − Предоплата
firstHourRub     = hourPrices[0]                    (верхняя граница удержания, §39.8)
```

Строки (`lines`): одна строка услуги с суммой часов и подписью гостевым форматом времени («Баня · пт 15 янв, 22:00 — сб 16
янв, 01:00 · 2 ч»); `hourPrices[]` отдельно для разбивки (если цены часов разные — фронт группирует подряд идущие часы с
одной ценой, подписи часов — с календарной датой); позиции «Веник берёзовый × 2». Для сеанса в брони — `StayBookingCharges`:
`Kind = ServiceSlot` (одна строка на сеанс), `ServiceItem` (по позиции), `PrepayEligible = false`.

**Бронь дома + сеансы:** `TotalRub += Итог сеанса`, `DueAtCheckInRub += Итог сеанса`, `PrepayRub` не меняется (формула
предоплаты цикла 37 по `PrepayEligible` та же). Отмена сеанса в брони — строки суммы сеанса удаляются, итог и остаток
уменьшаются (история — в снимке сеанса и журнале). У брони с `ManualTotal` строки услуг добавляются рядом с ручной строкой —
инвариант Σ строк = итог сохраняется.

`PriceChanged`: гость присылает `expectedTotalRub`; сервер пересчитал иначе → 409 с новым расчётом (как §37.24 п. 7).
Векторы — `price`, `money`.

---

## §39.7. Жизненный цикл (A39-4)

### §39.7.1 Заказ услуги без проживания (US-39-11) — `POST /api/stays/public/services/{serviceId}/orders`

1. Форма → 400 строкой (контракт §39.24).
2. Услуга: нет / не опубликована / в архиве / компания не «Дома» → 404. Компания заблокирована → 409 `NotAcceptingBookings`.
3. `AcceptServiceOrdersWithoutStay = false` → 409 `ServiceOrdersDisabled`.
4. Идемпотентность `(CompanyId, IdempotencyKey)` → 200 с существующим заказом (до капчи и лимитов).
5. Гость: вошедший — номер аккаунта; аноним — капча, `PhoneNormalizer`.
6. Гейт: `StaysBookingGate.Evaluate(..., prepayPercent: service.StandalonePrepayPercent ?? 0, ...)` (исполнитель — всегда,
   ЮР39-2) → 409 `NotAcceptingBookings` с `reasonCode`.
7. Транзакция → замок номера → повторная проверка ключа → `ServiceOrderThrottle` (удержанных заказов номера ≥ 2 на
   платформе или ≥ 1 в компании; ≥ 10 созданий за сутки) → 429.
8. Замок услуги → ленивое снятие (§39.5.2) → окна, правила, занятость → `IsStartAllowed` (иначе `Diagnose` → 409) →
   `ServiceMoney.Quote` → `expectedTotalRub ≠ TotalRub` → 409 `PriceChanged`.
9. Заказ (токен, снимки, версии текстов, согласие мессенджера; `Held` + таймер `HoldMinutes` при предоплате > 0, иначе
   `Confirmed`) + сеанс (`ServiceSessionWriter.Add`) + `StayServiceOrderEventLog.Append(Created)` → `SaveChanges` → commit.
   `23P01` → 409 `SlotTaken`; гонка ключа → 200 с победителем. p95 < 800 мс: ~12 SQL, замок услуги — шаги 8–9.

### §39.7.2 Сеанс к брони гостем (US-39-09) — `POST /api/stays/bookings/public/{token}/sessions`

1. Форма → 400. 2. Бронь по токену → 404. 3. Идемпотентность `(StayBookingId, IdempotencyKey)` → 200 с бронью.
4. Услуга той же компании, опубликована, не в архиве, `AvailableForHouseBookings` → иначе 409 `ServiceNotAvailableForStays`.
5. Гейт компании с `prepayPercent = 0` (блокировка, тариф, исполнитель) → 409 `NotAcceptingBookings`.
6. Транзакция → замок дома брони → замок услуги → строка брони `FOR UPDATE`: статус активный и `now <` момента выезда →
   иначе 409 `BookingNotActive`; активных сеансов ≥ 5 → 409 `TooManySessions`.
7. Ленивое снятие → `IsStartAllowed` со `stayRange` из снимка брони и `minLead` услуги → `Quote` (без предоплаты) →
   `PriceChanged`.
8. Сеанс (`AddedByKind` = Guest/Customer, `AddNoticeVersion`), строки суммы, итог и остаток брони, `Version++`, событие
   `ServiceSessionAdded` → 201 `PublicStayBookingDto`. Если бронь `Held` — подсказка «Сеанс сохранится, если бронь будет
   оплачена» (сервер, `servicesBlock.hint`).

Капчи нет (ссылка — доступ); политика частоты `stay-session-add` (10 в час на токен и 30 в час на IP).

### §39.7.3 Сеанс к брони персоналом (US-39-15, ЮР39-6) — `POST …/bookings/{bookingId}/sessions`

Право `ManageBookings`. Тело как у гостя плюс **`requestBasis`** (`Phone` / `InPerson` / `Messenger`, обязательно → иначе
400 «Укажите, как гость попросил услугу»). Отличия: `minLead = 0`; неопубликованная (не архивная) услуга допустима; гейт
тарифа не проверяется (как ручная бронь). Те же замки и правила. Событие `ServiceSessionAdded` с автором и основанием;
гостю — `StayGuestSessionAdded` текстом «по вашей просьбе…» (§12.7 обзора) по доступным каналам; на странице брони у
такого сеанса — блок «По вашей просьбе к брони добавлена услуга… Если вы этого не просили — отмените здесь, без
последствий». Позиции в форме персонала — по умолчанию 0.

**Ручной отдельный сеанс (P1)** — `POST …/service-sessions`: заказ сразу `Confirmed`, `IsManual`, предоплата 0, телефон
необязателен, `requestBasis` обязателен. Мессенджер гостю ручного заказа **не отправляется** (нет снимка согласия гостя,
Т37-12 — как ручная бронь цикла 37, отклонение от буквы SPEC US-39-15); персонал передаёт ссылку `/s/<token>` сам.

### §39.7.4 Бронь дома с сеансами (US-39-10, P1)

`StayQuoteInput` и `CreateStayBookingInput` получают необязательный `services[]` (≤ 3; по умолчанию пусто — ничего не
выбрано, ЮР39-6). Создание: после шагов 1–7 §37.7.1 под замком дома — замки услуг по возрастанию Guid → для каждого
выбора `IsStartAllowed` (`stayRange` из запрошенных дат и времени компании; уже принятые сеансы этого запроса добавляются
в `occupied`) → `expectedTotalRub` = ночи + опции + услуги. Любой отказ → 409 `StayRefusalDto` с кодом
`ServiceSlotUnavailable` / `ServiceSelectionInvalid` и `serviceIndex` — **бронь не создаётся** («или всё, или ничего»);
гость выбирает другое время или бронь без услуги. Путь US-39-09 работает независимо.

### §39.7.5 Отмены

| Что | Кто | Условие | Итог |
|---|---|---|---|
| сеанс в брони | гость по ссылке | `now < StartUtc`, сеанс `Active` | `CancelledByGuest`, строки суммы удалены, персонал уведомлён; иначе 409 `CancelNotAllowed` |
| сеанс в брони | владелец, управляющий (`ManageBookings`) | `expectedVersion` сеанса, причина 1–300 | `CancelledByOwner`; гость — мессенджер + push (§12.6 обзора: «оплата за сеанс не вносилась») |
| заказ | гость по ссылке | как бронь: активный статус и `now < StartUtc`; истёкшее удержание — доснимается | `CancelledByGuest`, сеанс `ReleasedWithOrder`, сумма возврата — §39.8 |
| заказ | персонал | `expectedVersion` заказа | `ConfirmPayment` / `RejectPayment` / `CancelByOwner` — таблица `StayStateMachine`; конечный статус освобождает сеанс в той же транзакции |

После старта гость не отменяет: «Сеанс уже начался — по вопросам свяжитесь с компанией: <телефон>».

### §39.7.6 Гейт приёма (ЮР39-2)

`StaysBookingGate.Evaluate`: проверка `NoProviderInfo` выполняется **при любой** предоплате (строка
`if (prepayPercent > 0 && !ProviderComplete(...))` → `if (!ProviderComplete(...))`); `NoPaymentDetails` — по-прежнему только
при предоплате > 0. Затрагивает дома с 0 %: на стенде такие компании без сведений перестанут принимать брони (чек-лист
кабинета уже показывает пункт `ProviderInfo`). Включение `AcceptServiceOrdersWithoutStay` без полных сведений → 409
`ProviderRequiredForServiceOrders`. Тесты цикла 37, ожидающие приём при 0 % без исполнителя, обновляются (запись реестра
QA, категория «изменённое требование»).

### §39.7.7 Таймеры и фоновые задачи — новых задач нет

| Задача | Что добавляется |
|---|---|
| `stays-hold-expiry` (`realtime`, 15 с) | второй проход: заказы `Held` с `HoldExpiresAtUtc ≤ now` пачками по 50 → `ServiceOrderHoldExpirer` (условный `UPDATE` §37.5.3 на `StayServiceOrders`, освобождение сеанса, событие, уведомление) |
| `stays-scheduled-messages` (`main`, 60 с) | (а′) «осталось 10 минут» для заказов (`HoldMinutes ≥ 20`, без файлов, отметка `HoldReminderQueuedAtUtc`); (б) напоминание накануне — время из `StaysSettings.ArrivalReminderTime` вместо константы `ReminderTime`, снимок текста страницы и событие `ArrivalReminderSent` (§39.11.5) |
| `stays-guest-push-dispatch` (`realtime`, 10 с) | строки с владельцем-заказом (url `/s/<token>`) |

Вид «Завершён» заказа — вычисляемый: `Confirmed` и `now ≥ EndUtc`.

### §39.7.8 Лимиты (значения — конфигурация)

| Что | Значение | Где |
|---|---|---|
| удержанных заказов на номер (платформа / компания), созданий за сутки | 2 / 1 / 10 | `Stays:Services:PhoneLimits`, под замком номера |
| создание заказа с IP / пользователем | 5 / 20 в час | политика `stay-service-create` |
| добавлений сеанса к брони | 10 в час на токен + 30 в час на IP | политика `stay-session-add` |
| активных сеансов на бронь; сеансов в форме брони | 5; 3 | `Stays:Services:MaxSessionsPerBooking`, `MaxSessionsInBookingForm` |
| публичные чтения услуг (страница, календарь, старты, расчёт) | 120/мин на IP | существующая `stays-public` |
| страница заказа, файл, отмена / загрузка / push | как у брони | существующие `stay-public`, `stay-proof`, `stay-push` |

Превышение — 429 строкой; телефоны в логах маскируются (`LogMasking`).

---

## §39.8. Возврат отдельного сеанса (ЮР39-1, Т39-01…03) — `ServiceRefund.Compute`

Вход: статус заказа, шаблон и рубеж (снимки), `PrepayRub`, `firstHourRub` (из `HourPricesJson[0]`), `StartUtc`, момент,
кто отменяет. Рубеж — **реальное время**: `StartUtc − boundaryHours`.

| Случай | `kind` | `refundAtLeastRub` | Текст (сервер, `StaysTexts`) |
|---|---|---|---|
| конечный статус | `NothingPaid` | 0 | «Заказ уже завершён — отменять нечего» |
| `Held` или `PrepayRub = 0` | `NothingPaid` | 0 | «Сеанс не оплачен — отмена без последствий» |
| отменяет компания | `Full` | `PrepayRub` | «Предоплата возвращается полностью: X ₽» |
| `NoDeductions` | `Full` | `PrepayRub` | то же |
| `PreparationCosts`, момент < рубежа | `Full` | `PrepayRub` | «По правилу сеанса вам должны вернуть всю предоплату — X ₽» |
| `PreparationCosts`, момент ≥ рубежа, `X = Prepay − min(Prepay, firstHour) > 0` | `PartialAtLeast` | `X` | «К возврату не меньше X ₽. Компания вправе удержать только фактические расходы на подготовку — не больше N ₽» |
| то же, `X = 0` | `CostsOnlyUpTo` | **null** | «Компания вправе удержать только фактические расходы на подготовку, не больше N ₽. Остальное она обязана вернуть» |

`refundAtLeastRub = null` закрепляет формой (контракт), что «не меньше 0 ₽» показать нельзя. Конфигурация
`Stays:Services:CancellationPolicies:PreparationCosts:MaxDeductionHours` (= 1) и
`Stays:Services:CancellationBoundaryHours {Min: 3, Max: 24, Default: 12}`; валидатор `DeploymentSafetyChecks.ValidateStaysServices`
не пропускает `MaxDeductionHours ∉ {0, 1}`, `Max > 24`, `Min < 1`, `Min > Max`, неизвестные имена шаблонов (никакого
«Standard»). Тест запрещённых слов (`StaysTextsForbiddenWordsTests`, `stayTexts.test.ts`) расширяется на новые тексты
(Т39-14). Векторы — `refund`. Сеанс в брони и заказ без предоплаты — «отмена без последствий», текстов о плате за неявку нет
(Т39-03).

---

## §39.9. Уведомления (A39-5, US-39-17)

### §39.9.1 Новые `NotificationType` (дописать в конец; салонная маска их не видит)

| Значение | Тип | Кому | Каналы |
|---|---|---|---|
| 27 | `StaffStaySessionAdded` | владелец, управляющие | push, MAX |
| 28 | `StaffServiceOrderCreated` | владелец, управляющие | push, MAX |
| 29 | `StaffServiceOrderPaymentProofUploaded` | владелец, управляющие | push, MAX |
| 30 | `StaffServiceSessionCancelledByGuest` | владелец, управляющие | push, MAX |
| 31 | `ServiceGuestOrderCreated` | гость заказа | мессенджер (ссылка, сумма, реквизиты, срок) |
| 32 | `ServiceGuestHoldExpiring` | гость заказа | мессенджер, push |
| 33 | `ServiceGuestHoldExpired` | гость заказа | мессенджер, push |
| 34 | `ServiceGuestConfirmed` | гость заказа | мессенджер, push |
| 35 | `ServiceGuestPaymentRejected` | гость заказа | мессенджер, push |
| 36 | `ServiceGuestCancelledByOwner` | гость заказа | мессенджер, push |
| 37 | `StayGuestSessionAdded` | гость брони | мессенджер (если включено у брони); push — только если сеанс добавил персонал |
| 38 | `StayGuestSessionCancelledByOwner` | гость брони | мессенджер, push |
| 39, 40 | — | — | **оставлены циклу 40** (комментарий в enum; при мерже с циклом 38 значения сверяются, R39-6) |

«Новая бронь дома с сеансами» — существующий `StaffStayCreated`, к тексту дописывается « · услуги: N» **только при N > 0**
(байт-в-байт прежний текст без услуг).

**Маска.** `CompanyNotificationsController.BuildMask` и чтение маски фильтруют по `NotificationTypeCatalog.BookingTypes`
(сейчас `BuildMask` принимает любой тип из тела запроса, а сдвиг `1 << 32` в C# даёт 1 — значение ≥ 32 перевернуло бы
салонный бит). Страж-тест: для каждого `NotificationType` вне `BookingTypes` маска и `NotificationGate` не используют сдвиг;
`NotificationTypeCatalog` получает список `ServiceTypes`, `IsStayType` включает их; `StayNotificationPlan.IsStayType`
(зашитые 16…26) удаляется в пользу каталога. `NotificationTexts.TypeText` — тексты всех новых (тест перебирает enum).

### §39.9.2 Механика

- Сеансы в брони — через существующий `StayBookingEventLog` → `StayNotificationPlanner.OnEventAsync` (новые `Kind` в
  `StayNotificationPlan.ForEvent`), очереди с `StayBookingId`.
- Заказы — `StayServiceOrderEventLog` → `StayNotificationPlanner.OnOrderEventAsync` (тот же класс, общий `DeliverAsync` над
  «субъектом уведомления»), очереди с `StayServiceOrderId`; ключ идемпотентности строк — `{type}:so:{eventId|marker}:{…}`.
- Первой строкой — `ShowcaseOutboundGuard.IsSuppressed` (как сейчас). Горничной не идёт ничего.
- Тексты — `StayNotificationTexts` (новые методы `ServiceStaffPush/ServiceStaffMax/ServiceGuestPush/ServiceMessenger`):
  персоналу — формат персонала, без имени и телефона гостя, ссылка `PublicSiteLinks.StaysCabinetServiceSessionUrl`;
  гостю — гостевой формат (ЮР39-8); push гостю — `title` «ezbook · Дома», `body` без ПДн и адресов («Статус вашей брони
  изменился», «Осталось 10 минут, чтобы приложить подтверждение оплаты», «Сеанс добавлен к брони — откройте бронь»),
  `tag` `so-<orderId>` / `sg-<bookingId>`, `url` `/s/<token>` / `/b/<token>` — только в зашифрованной нагрузке.
  Реквизиты — только в мессенджер этого заказа (Т37-04).
- Отмена компанией без предоплаты — текст §12.6 обзора («оплата за сеанс не вносилась»); с предоплатой — «предоплата
  возвращается полностью… право на возмещение убытков» (§12.1).
- На бою (R39-7): push гостю живой, мессенджер гостю выключен флагом компании, MAX персоналу выключен `STAFFMAX_ENABLED`.

---

## §39.10. Шахматка, «День услуг», график (A39-6)

- **Ревизия общая** — `StaysSettings.BookingsRevision`. Поднимают: `StayBookingEventLog` (в том числе события сеансов),
  `StayServiceOrderEventLog`, `ServiceScheduleWriter` (окна видны в «Дне услуг»), правки услуги (публикация, архив).
  Опрос шахматки (`changed: false` — один PK-lookup) не меняется.
- **Шахматка** (`GET …/board`) — в конец `StaysBoardDto`: `services[]` (`id, name, isPublished, isArchived`; архивные — если
  есть сеансы в окне) и `serviceCells[]` (`serviceId, businessDate, count, firstStartLabel` «с 16:00»,
  `crossesMidnightLabel` «до 01:00» | null, `needsAction`). Сеанс — в ячейке **бизнес-дня старта**. Удержанные
  неистёкшие учитываются (как брони); истёкшие и освобождённые — нет. `awaitingPaymentCount` += заказы
  `AwaitingPaymentCheck`.
- **«День услуг»** — `GET …/service-day?date=` (`ViewBookings`): `axis {fromMinute, toMinute}` (от первого начала окна/сеанса
  до последнего конца, в пределах `[B, B+1440]`; полночь — `1440`), по услугам: `windows[]`, `closed`, `bars[]`
  (`kind`: `Session` / `Buffer` / `CarryOverBuffer`; `sessionId`, `startMinute`, `endMinute`, `label` — дом или «без
  проживания», `state`, `stateText`, `needsAction`). Зазор, уходящий за `B + 1440`, обрезается в этом дне и приходит
  следующему дню полосой `CarryOverBuffer` «подготовка после вчерашнего сеанса». На телефоне фронт рисует те же данные
  списком.
- **График** (`GET …/schedule`) — в конец `ScheduleDayDto`: `sessions[]` на **бизнес-дне старта**: `sessionId`,
  `serviceName`, `timeLabel` (персонала, «22:00 – 01:00 (сб)»), `preparedUntilLabel` (конец + зазор, «01:30 (сб)»),
  `houseName` | null («без проживания»), `guestName`, `items[] {name, quantity}`, `comment` (null, если горничная и
  `HousekeeperSeesGuestComment = false`), `paymentUnconfirmed`. Попадают активные сеансы, родитель которых
  `AwaitingPaymentCheck`/`Confirmed`. Форма одна для всех ролей и **не содержит** телефона, сумм, подтверждений, статуса
  оплаты (только пометка) — контрактный тест формы (Т39-10).
- **Карточка брони персонала** — в конец `StaffStayBookingCardDto`: `sessions[]` и действие `AddSession` в
  `availableActions`.

---

## §39.11. Напоминание накануне заезда (A39-8, US-39-19/20, ЮР39-3/4/5)

### §39.11.1 Хранение и права

`StaysSettings.ArrivalReminderEnabled` (существующий переключатель, маршрут `settings`), новые `ArrivalReminderTime`,
`ArrivalReminderTemplate` (NULL = по умолчанию), `ArrivalReminderPushText`. Время, шаблон и push — отдельный маршрут
`GET|PUT …/arrival-reminder` (`ManageCompany`), чтобы полная замена `settings` старыми клиентами не стирала шаблон. Каждое
сохранение — строка `StaysReminderTemplateChanges` (что было, что стало, кто, версии текстов-предупреждений, подтверждение
маркеров кодов).

### §39.11.2 Подстановки — одна таблица (`ArrivalReminderTemplate.Placeholders`)

| Подстановка | Значение | Пусто, если | Мессенджер | Страница | Push |
|---|---|---|---|---|---|
| `{Компания}` | название компании | — | да | да | да |
| `{Дом}` | название дома | — | да | да | да |
| `{ДатаЗаезда}`, `{ДатаВыезда}` | «15 янв» | — | да | да | да |
| `{Ночей}` | «3 ночи» | — | да | да | да |
| `{ВремяЗаезда}`, `{ВремяВыезда}` | «14:00» из снимка брони | — | да | да | да |
| `{Услуги}` | активные сеансы брони гостевым форматом через «; » («Баня, пт 15 янв, 22:00 — сб 16 янв, 01:00») | сеансов нет | да | да | да |
| `{ИмяГостя}` | имя из брони | обезличено | да | да | **нет** |
| `{Адрес}` | адрес дома или компании | адреса нет | да | да | **нет** |
| `{ТелефонКомпании}` | телефон компании | нет телефона | да | да | **нет** |
| `{КОплатеПриЗаселении}` | «12 500 ₽» (`StaysTexts.Rub`) | остаток = 0 | да | да | **нет** |
| `{СсылкаНаБронь}` | `PublicSiteLinks.StayBookingPageUrl` | — | да | **нет** | **нет** |

### §39.11.3 Шаблон по умолчанию и байт-в-байт

`ArrivalReminderTemplate == null` → мессенджер получает текст **прежним кодом** `StayNotificationTexts.Messenger(...,
StayGuestArrivalReminder)`, push — прежний «Завтра заезд — откройте бронь». Регресс-тест сравнивает с текстами цикла 37
байт-в-байт. В редакторе показан эквивалентный построчный шаблон `ArrivalReminderTemplate.Default`:

```
{Компания}: завтра заезд в «{Дом}» — {ДатаЗаезда} с {ВремяЗаезда}.
Адрес: {Адрес}.
К оплате при заселении: {КОплатеПриЗаселении}.
Бронь: {СсылкаНаБронь}
```

Сохранение текста, равного `Default` (после нормализации переводов строк), хранится как NULL; «Вернуть текст по умолчанию»
— NULL. Страж-тест: рендер `Default` в режиме мессенджера, со склейкой строк пробелом, равен прежнему тексту (защита от
расхождения двух представлений). Страница брони при NULL-шаблоне показывает рендер `Default` в режиме страницы (без
строки ссылки).

### §39.11.4 Рендер — чистый `ArrivalReminderTemplate.Render(template, facts, mode)`

1. Шаблон делится на строки (`\n`; `\r\n` нормализуется). Разметка не интерпретируется — текст как есть.
2. Строка, в которой есть подстановка, **запрещённая в режиме**, выпадает целиком.
3. Строка, в которой есть подстановка с **пустым значением**, выпадает целиком (так `Default` повторяет «адрес и остаток —
   только если есть»).
4. **Только push — жёсткий фильтр (ЮР39-3):** строка выпадает, если в её **тексте владельца** (строка с вырезанными
   подстановками) есть: ≥ 4 цифр подряд с пробелами/дефисами между ними или без (`\d(?:[\s\-]?\d){3,}`); ссылка
   (`https?://`, `www.`, `\b[\p{L}\d-]+\.(ru|рф|com|net|org|su|io)\b`); e-mail; телефон (`(\+7|8)[\s\-(]*\d` и шире — по
   правилу цифр); слова-маркеры (`\bкод`, `парол`, `wi-?fi`, `вай-?фай`, `ключниц`, `сейф`, `домофон`, без учёта регистра).
   Значения подстановок фильтру не подлежат (время «14:00» и даты строки не роняют).
5. Подстановки заменяются; пустые строки подряд схлопываются.
6. Длина: мессенджер — тело ≤ 1000 вместе со строкой «Бронь: <ссылка>» (добавляется в конец, если в шаблоне нет
   `{СсылкаНаБронь}`) и строкой отписки (`AppendUnsubscribeLine` — как сейчас). Превышение: сначала сокращаются значения
   `{Адрес}`, `{Компания}`, `{Дом}`, `{Услуги}`, `{ИмяГостя}` (каждое до 20 символов + «…», по очереди); если всё ещё
   длиннее — тело режется до бюджета с «…», ссылка на бронь выносится отдельной последней строкой, отписка — после неё.
   Ссылка и отписка не режутся никогда. Страница — ≤ 1000 тем же правилом без ссылки. Push — ≤ 180 с «…»
   (`StaffPushPayloadJson.Prefix` — суррогатные пары не режутся); пусто после фильтра → прежний фиксированный push.
7. Push уходит с текстом, только если `ArrivalReminderPushText = true` **и** шаблон не NULL; иначе — прежний фиксированный.
   Заголовок — «ezbook · Дома»; `url` `/b/<token>` — только в зашифрованной нагрузке; в очереди и логах маскируется.

Векторы — `reminderTemplate` (выпадение строк, фильтр push, длины, NULL-шаблон).

### §39.11.5 Проверки при сохранении (`ArrivalReminderTemplate.Validate`)

| Что | Мера |
|---|---|
| длина > 700 | 400 «Текст напоминания — не длиннее 700 символов» |
| неизвестная подстановка (`\{[^{}\n]*\}` не из таблицы) | 400 «Неизвестная подстановка {X}. Можно: {Компания}, {Дом}, …» |
| «задаток», «невозвратн», «депозит» | 400 «Не используйте слова «задаток», «невозвратный», «депозит»» |
| маркеры кодов (слова §39.11.4 п. 4 или ≥ 4 цифр вне подстановок) | 409 `ReminderConfirmationRequired` с `markers[]` и текстом `StayCheckInInfoOwnerNotice`; повтор с `confirmCodeMarkers: true` сохраняет и пишет `CodeMarkersConfirmed`/`CodeMarkersHit` в историю (ЮР39-4) |
| «штраф», «неустойк», «не возвращ», «паспорт», 13–19 цифр (карта), серия + номер паспорта | мягкие предупреждения `warnings[]` в ответе и в предпросмотре — не отказ |
| время: шаг 30 мин, 08:00…22:00 | 400 «Время — с 08:00 до 22:00 с шагом 30 минут» |
| включение push (`pushTextEnabled: true` при прежнем false) без `pushNoticeVersion` | 400 «Подтвердите, что прочитали предупреждение о push» |

Текст `StayReminderTemplateOwnerNotice` у поля — всегда (его версия пишется в историю каждого сохранения).

### §39.11.6 Отправка и снимок (задача `stays-scheduled-messages`)

`reminderAt = StayTime.ToUtc(TimeZoneIdSnapshot, CheckInDate − 1, settings.ArrivalReminderTime)` — **текущее** время
настройки на каждом проходе (новое время действует на все неотправленные). Остальное — как сейчас: `Confirmed`, `now ≥
reminderAt` (бронь, подтверждённая позже, получает сразу), `now <` момента заезда, однократность — условный `UPDATE`
`ArrivalReminderQueuedAtUtc`. Если напоминание включено — в той же транзакции: `ArrivalReminderPageText` = рендер режима
страницы, `ArrivalReminderSentAtUtc = now`, событие `ArrivalReminderSent`, затем постановка в каналы, какие есть. Снимок
ставится **и тогда, когда ни один канал не доступен** (Р39-17). Текст фиксируется в момент постановки: правка шаблона
после этого не меняет ни сообщение в очереди, ни снимок. Страница брони показывает блок «Напоминание о заезде» с
`ArrivalReminderPageText`, когда он не NULL.

### §39.11.7 Предпросмотр

`POST …/arrival-reminder/preview {template, pushTextEnabled, bookingId?}` → три варианта (`messenger`, `page`, `push`) с
длиной каждого, `droppedLines[]` для push (номер строки и причина: `ForbiddenPlaceholder`, `EmptyValue`, `Digits`, `Link`,
`Email`, `Phone`, `CodeWord`), `warnings[]`, `errors[]` (без сохранения). Данные — пример (константа сервера); с `bookingId`
(P1) — ближайшая реальная бронь компании (владельцу доступна и так). Фронт текстов не собирает — показывает ответ.

---

## §39.12. Права (A39-10, US-39-26)

`StaysPermission` += `ManageServices`, `EditServiceContent`, `ManageServiceDates` (дописать в конец). Таблица
`StaysAccess` (одна) и тест «каждое право × каждая должность».

| Действие | Право | Владелец | Управляющий | Горничная |
|---|---|---|---|---|
| «Принимать заказы услуг без проживания» | `ManageCompany` (маршрут `settings`) | ✓ | — | — |
| услуги: создание, архив, удаление, настройки (часы, шаг, зазор, предоплата, отмена), шаблон расписания, цены, позиции, публикация, порядок, адрес | `ManageServices` | ✓ | — | — |
| услуги: описание, фото | `EditServiceContent` | ✓ | ✓ | — |
| ручные даты расписания | `ManageServiceDates` | ✓ | ✓ | — |
| список услуг в кабинете (чтение карточки) | `EditServiceContent` или `ManageServiceDates` | ✓ | ✓ | — |
| «День услуг», сеансы, карточка, журнал, контакты, файлы | `ViewBookings` | ✓ | ✓ | — |
| подтверждение/отклонение оплаты, отмена, добавление к брони, ручной заказ | `ManageBookings` | ✓ | ✓ | — |
| сеансы в графике | `ViewSchedule` | ✓ | ✓ | ✓ |
| время, шаблон напоминания, push | `ManageCompany` | ✓ | — | — |

Не участник — 404; участник без права — 403 пустым телом; владельческие маршруты — `[RequiresOwnerTerms]`.

---

## §39.13. Персональные данные, retention, правовые тексты (A39-13, US-39-27)

### §39.13.1 Права субъекта

- **Выгрузка** (`SubjectDataExporter`): в конец — `stayServiceOrders[]` (заказы аккаунта и, при подтверждённом номере,
  гостевые на номер; маркер `// SUBJECT-PHONE-GATE:`): компания, услуга, время гостевым форматом, часы, позиции, суммы,
  статус, имя, телефон, комментарий, причина, подтверждения оплаты (метаданные), `orderUrl`, видимые гостю события.
  В `stayBookings[]` каждой брони — `sessions[]` и `arrivalReminderText` (снимок, Т39-11).
- **Удаление аккаунта** (`AccountDeletionService`): заказы → `GuestUserId`, `GuestName`, `GuestPhone`, `Comment` = null,
  `PersonalDataErased`; файлы подтверждений удаляются; у броней дополнительно `ArrivalReminderPageText = null`
  (`StayPersonalData.Erase` — одна функция для удаления и retention). Сеансы ПДн не содержат — расписание владельца цело.
- **Отзыв согласия** `ProviderDelivery`: `NotifyByMessenger = false` и у активных заказов номера.
- `SubjectPhoneGateInvariantTests` — шаблоны новых выборок по `GuestPhone`.

### §39.13.2 Маскирование

`LoggingExtensions.MaskSensitiveRequestPath` — префикс `/api/stays/service-orders/public/`; nginx dom — `map` получает
`/s/` и `/api/stays/service-orders/public/` (DO-39-01); `Referer` с `/s/` — тоже.

### §39.13.3 Retention (файлы в `Services/Retention/Rules/` + регистрация поимённо; сроки — конфигурация, сухой прогон)

| Правило | Что | Срок |
|---|---|---|
| `stay-payment-proofs` (расширяется) | файлы подтверждений заказов | `StayPaymentProofDays` (90) от max(`EndUtc` сеанса, `TerminalAtUtc`) |
| `stay-service-order-unpaid-personalization` (новое) | заказы `ExpiredUnpaid` → обезличивание | `Retention:StayServiceOrderUnpaidDays` = 30 от `TerminalAtUtc` |
| `stay-service-order-personalization` (новое) | прочие заказы после конца сеанса / конечного статуса | `Retention:StayServiceOrderPersonalDataDays` = 1095 от max(`EndUtc`, `TerminalAtUtc`) |
| `stay-service-order-events` (новое) | журнал заказов | `Retention:StayServiceOrderEventDays` = 1095 |
| `stay-service-schedule-events` (новое) | журнал расписания услуг (ПДн сотрудников) | `Retention:StayServiceScheduleEventDays` = 1095 |
| `stay-unpaid-personalization`, `stay-booking-personalization` (расширяются) | при обезличивании брони — стирать `ArrivalReminderPageText` | прежние |
| `stay-guest-push-subscriptions/notifications` (расширяются) | строки заказов | прежние |

`GET /api/admin/retention/policy` — в конец четыре новых срока. Сеансы в брони — вместе с бронью. История шаблона
напоминания — данные компании, без правила.

### §39.13.4 Правовые тексты (Т39-07, ключи `LegalTextKey` вне `All`, запасной текст на фронте `dom/src/utils/stayTexts.ts`)

| Ключ | Где | Запасной текст — по черновику |
|---|---|---|
| `StayServiceBookingNotice` | под «Забронировать» отдельного сеанса | §12.2 обзора |
| `StayServiceBookingTerms` | страница услуги, страница заказа | §12.12 обзора (ядро) |
| `StayServiceCancellationTerms` | страница услуги, форма, страница заказа, диалог отмены | §12.1 |
| `StayServiceAddNotice` | у «Добавить» (форма брони, страница брони, диалог персонала не нужен) | §12.3 |
| `StayServiceCancellationOwnerNotice` | у выбора шаблона отмены (кабинет) | §12.4 |
| `StayServiceCommentNotice` | под комментарием к заказу | §12.5 |
| `StayReminderTemplateOwnerNotice` | у поля «Текст напоминания» | §12.8 |
| `StayReminderPushOwnerNotice` | у переключателя push | §12.9 |
| `StayServiceSafetyOwnerNotice` | у описания услуги | §12.10 |
| `StayBookingNotice` (новая запасная редакция) | + «заказанные услуги и их позиции» в перечне данных | §4.2 обзора |
| `StayBookingTerms` (новая запасная редакция) | + пункт 3а «Услуги к проживанию» | §12.12 |
| `StayTouristTaxNotice` (новая запасная редакция) | «налог начисляется на стоимость проживания» | §12.11; на странице услуги и заказа **не показывается** |

Снимки версий в заказе — §39.2.4; в сеансе в брони — `AddNoticeVersion`. Шаг CI «маркер ТРЕБУЕТСЯ ТЕКСТ» не должен
срабатывать: запасные тексты — нейтральные черновики из обзора.

---

## §39.14. Фронтенд dom (US-39-01…16, 19, 20)

### §39.14.1 Адреса и маршруты (`contracts/cycle39/dom-routes.json`)

| Маршрут | Экран | Доступ |
|---|---|---|
| `/:slug/uslugi/:serviceSlug` | страница услуги: галерея, описание (+ правила посещения — текстом владельца), таблица цен, минимум часов, позиции, правило отмены (при предоплате), подготовка (если показ включён), форма заказа (если заказ без проживания включён), иначе «Можно добавить к брони дома» | все |
| `/s/:token` | страница отдельного сеанса (опрос 15 с, реквизиты, отсчёт, подтверждения, отмена, push, «Об исполнителе») | все |
| `/cabinet/:companyId/services`, `/services/new`, `/services/:serviceId` | список, создание, карточка-вкладки «Описание», «Расписание» (шаблон + календарь ручных дат), «Цены» (правила + предпросмотр «день × час»), «Позиции», «Правила» (часы, шаг, зазор, предоплата, отмена, публикация) | `EditServiceContent` / `ManageServiceDates` / `ManageServices` |
| `/cabinet/:companyId/service-day/:date` | «День услуг» (шкала; на телефоне — список) | `ViewBookings` |
| `/cabinet/:companyId/service-sessions/:sessionId` | карточка сеанса | `ViewBookings` |

Изменения существующих экранов: страница компании (блок «Услуги»), страница дома («К проживанию можно добавить: …»),
форма брони (свёрнутый блок «Добавить к проживанию», ничего не выбрано — P1), страница брони (блок «Услуги к проживанию»,
блок «Напоминание о заезде», блок «Добавлено по вашей просьбе»), шахматка (группа «Услуги», счётчик), список «Ожидают
проверки оплаты» (вкладка «Сеансы»), карточка брони (сеансы, «Добавить услугу» с выбором основания), график (сеансы),
настройки («Принимать заказы услуг без проживания»; раздел «Информация к заселению» — время, шаблон с кнопками подстановок,
переключатель push с предупреждением, предпросмотр трёх каналов, диалог подтверждения маркеров, «Вернуть по умолчанию»),
профиль (MAX-карточка, US-39-21).

Правило «маршрут dom = слово в резерве» (цикл 37) дополняется: второй сегмент `uslugi` — в `reservedHouseSlugs`
(юнит-тест бэкенда `StaysSlugPolicy` отказывает дому с таким адресом; vitest `domRoutes.test.ts` сверяет маршруты).

### §39.14.2 Код

- API-модули: `dom/src/api/staysServices.ts` (кабинет), `publicServices.ts`, `serviceOrders.ts` (по токену),
  `guestBookings.ts` (+ сеансы), `staysBoard.ts` (+ `serviceDay`, сеансы персонала), `staysCompanies.ts` (+ напоминание).
- Типы — **только генерат** `src/types/api-cycle39.generated.ts` (`npm run types:api:cycle39`), для существующих DTO —
  `api-cycle37.generated.ts` (после дописывания перечислений).
- Утилиты с векторами (`dom/src/utils/*.test.ts` читают `contracts/cycle39/service-vectors.json`): `businessClock.ts`,
  `serviceTimeFormat.ts` (редактор и подписи выбора), `serviceMoney.ts` (предварительный расчёт до ответа `quote`),
  `serviceWindows.ts` (валидация ввода «02:00 → след. дня»). Возврат, напоминание — **только сервер** (тексты), TS-двойников
  нет.
- Выбор времени: шаг 1 — дата (бизнес-день, подпись «пт 15 янв»; недоступные — текстом), шаг 2 — старты (кнопки ≥ 44×44,
  старты после полуночи в конце с «(ночь на сб)»), шаг 3 — часы (минимум…`maxHours` старта, конец — гостевым форматом),
  шаг 4 — позиции (по умолчанию 0); клавиатура и `aria-label`, статусы — текстом.
- Область vitest `stays` уже покрывает `dom/src/`; перенос `staffMax` — §39.15.1.

---

## §39.15. Хвосты цикла 37 (блок F)

### §39.15.1 MAX-карточка персонала в профиле dom (US-39-21, C37-4) — цена переноса

`goods/src/components/staffMax/StaffMaxCard.tsx` (+ тест) зависит от модулей goods: `goods/src/api/staffMax.ts`,
`../StatePanels`, `utils/orderError.getGoodsErrorMessage`, `utils/reports.shouldPollStaffMax`, типов
`StaffMaxLinkSessionDto`/`StaffMaxStatusDto` из `goods/src/types`. Переезжает:

| Было (goods) | Стало (общее) |
|---|---|
| `components/staffMax/StaffMaxCard.tsx` + тест | `src/components/staffMax/StaffMaxCard.tsx` + тест (ID тестов прежние) |
| `api/staffMax.ts` | `src/api/staffMax.ts` (goods — реэкспорт или прямой импорт `@/api/staffMax`) |
| `utils/reports.shouldPollStaffMax` | `src/components/staffMax/staffMaxPolling.ts` (goods `reports.ts` реэкспортирует) |
| типы `StaffMax*Dto` | `src/types/index.ts` (реэкспорт из `api-cycle25.generated.ts`) |
| `getGoodsErrorMessage`, `StatePanels` | пропсы карточки `getErrorMessage` и слоты состояний; goods передаёт свои, dom — свои |

`frontend/shared-sources.js` и `contracts/cycle36/test-areas.json` (запись «перенос») обновляются; ESLint-границы не
меняются (общий код — в `src/components`). Риск — регресс goods: его тесты (`GoodsProfilePage.test.tsx`,
`ShopNotificationsPage.test.tsx`, `StaffMaxCard.test.tsx`) должны пройти **без правки ожиданий**. В dom `ProfilePage`
показывает карточку владельцу и управляющему любой компании «Дома» (по `GET /api/stays/companies/my`, роль ≠ горничная);
при `STAFFMAX_ENABLED=false` — тот же текст недоступности, что у goods.

### §39.15.2 Контракт cycle37 без замечаний `redocly lint` (US-39-22, C37-3)

Правка `contracts/cycle37/openapi.yaml` (задача DO-39-03, один коммит с регенерацией `openapi.json` и
`api-cycle37.generated.ts`):
1. `info.license.url` (правило `info-license-url` профиля `recommended`);
2. **у каждой операции** явный `security`: кабинетные — `[{ bearerAuth: [] }]` (уже есть), анонимные — `security: []`
   (`security-defined`);
3. у каждой операции — хотя бы один 4xx-ответ (`operation-4xx-response`);
4. `nullable` рядом с `$ref` уже обёрнут в `allOf` — проверить, что нигде нет `$ref` с соседями (`no-$ref-siblings`);
5. неиспользуемые компоненты удалить (`no-unused-components`);
6. дописать перечисления: `StayChargeKind` += `ServiceSlot, ServiceItem`; `StayRefusalCode` += `ServiceSlotUnavailable,
   ServiceSelectionInvalid`; `StaysConflictCode` += `ProviderRequiredForServiceOrders`; `StaysPermission` +=
   `ManageServices, EditServiceContent, ManageServiceDates`; `StayDisplayStatus` — без изменений.
Цель: `npx @redocly/cli lint ../contracts/cycle37/openapi.yaml` (профиль по умолчанию) и с `--config
../contracts/redocly.yaml` — 0 ошибок и 0 предупреждений; осознанно отключённое правило — только в `contracts/redocly.yaml`
с комментарием. `contracts/cycle39/openapi.yaml` написан под те же правила с рождения; CI линтует оба; в
`contracts-to-json.mjs` и `package.json` (`types:api:cycle39`) — `cycle39`.

### §39.15.3 p95 каталога на 500 домах (US-39-23, C37-5)

`tools/bench/cycle39/` по образцу `cycle25`: `seed.py` — засев **через HTTP API** локального стенда (100 компаний, 500 домов
обоих режимов цен, брони, блокировки, по 0–3 услуги с окнами через полночь, ценами и сеансами); параметры
`--companies --houses --bookings-per-house --services-per-company --external-occupancy-per-house` (последний — заглушка
под цикл 40: засев строк `Source = ExternalCalendar` прямым SQL, пока нет API). `bench39.py` — сценарии: каталог без дат,
с датами (выходные, Новый год), с фильтром цены; календарь дома; страница компании; `availability` услуги на 14 дней;
`starts` на дату. Отчёт `tools/bench/cycle39/results/` (p50/p95/p99, число запросов, железо). Цели §6 SPEC; при промахе —
отчёт и предложение архитектору, не «готово».

### §39.15.4 Задел под цикл 40 (A39-14, SPEC §11.1)

1. Ночи — только `HouseOccupancyWriter`; каскад — `StayBookingReleaser` (п. 1 — ночи через писателя) + страж-тест.
2. `OccupancySource.ExternalCalendar = 2` не используется; шахматка и календарь показывают его как прежде.
3. `NotificationType` 39, 40 оставлены (§39.9.1).
4. `reservedHouseSlugs` содержит `kalendar`, `kalendari`, `calendar`, `ical`; `reservedSlugs` уже содержит `ical`,
   `calendar`; маршрутов `/api/stays/ical/*` цикл не создаёт.
5. Бенчмарк параметризован (§39.15.3).
6. Сеансы — в своей таблице, не в `HouseOccupancies`.

### §39.15.5 Документы (US-39-24, P2)

`DEPLOY.md` §28 (факт выката dom, push гостю включён), `API_DOCUMENTATION.md` (развести `4.21`, раздел «Дома»: услуги,
напоминание), `TEST_CATALOG.md` (абзац про кеш каталога; раздел «Цикл 39» `CY39-*` и ручные `M39-*` с вердиктами).

---

## §39.16. Структура проекта — что добавляется

```
ServiceBooking.Core/
├── Entities/  StayService, StayServicePhoto, StayServiceWeeklyWindow, StayServiceDateOverride, StayServiceScheduleEvent,
│              StayServicePriceRule, StayServiceItem, StayServiceSession, StayServiceOrder, StayServiceOrderEvent,
│              StaysReminderTemplateChange; StaysSettings (+4), StayBooking (+2), StayBookingCharge (+ServiceSessionId),
│              StayBookingEvent (+ServiceSessionId), StayPaymentProof / StayGuestPushSubscription / StayGuestPushNotification
│              (StayBookingId?, +StayServiceOrderId), OutboundNotification / StaffPushNotification / StaffMaxMessage (+StayServiceOrderId)
└── Enums/     StaysEnums.cs: StayChargeKind (+2), StayBookingEventKind (+5), StayServiceSessionState, StayServiceCancellationPolicy,
               StayServiceOrderEventKind, StayServiceRequestBasis, StayServiceScheduleEventKind; NotificationType (+27…38),
               PublicArea (+StayServices), LegalTextKey (+9 констант вне All)

ServiceBooking.Infrastructure/Migrations/   *_Cycle39StaysServices.cs (EXCLUDE через Sql)

ServiceBooking.API/
├── Controllers/Stays/
│   ├── StaysServicesController.cs          кабинет: услуги, фото, расписание, ручные даты, цены, позиции, публикация
│   ├── StaysServiceSessionsController.cs   service-day, service-sessions (список, карточка, действия, файл, quote, ручной), starts персонала,
│   │                                       bookings/{id}/sessions
│   ├── StaysServicesPublicController.cs    public: companies/{slug}/services/{serviceSlug}, services/{id}/availability|starts|quote|orders
│   ├── StayServiceOrdersPublicController.cs service-orders/public/{token}… (страница, файлы, отмена, push)
│   ├── StayBookingsPublicController.cs     (+ {token}/services, …/starts, …/quote, {token}/sessions, …/cancel)
│   ├── StaysCompaniesController.cs         (+ arrival-reminder GET/PUT, preview, history; settings +acceptServiceOrdersWithoutStay)
│   ├── StaysBoardController.cs             (board +services, schedule +sessions)
│   └── StaysPublicController.cs            (company +services, house +servicesForStay, quote/bookings +services)
├── DTOs/Stays/Services*.cs                  по контракту cycle39
├── Services/Stays/Services/                 🆕 подпапка вертикали услуг
│   ├── чистые: BusinessClock, ServiceTimeFormat, ServiceScheduleRules, ServicePriceRules, ServicePricing, ServiceMoney,
│   │           ServiceSlotCalculator, ServiceRefund, ServicePublishRules, ArrivalReminderTemplate, ServiceTexts
│   └── с БД:   ServiceCatalogService (страница услуги, таблица цен), ServiceScheduleResolver, ServiceScheduleWriter,
│               ServiceSessionWriter, ServiceOrderCreationService, ServiceSessionAddService (гость/персонал/форма брони),
│               ServiceOrderTransitionService, ServiceOrderHoldExpirer, ServiceOrderThrottle, StayServiceOrderEventLog,
│               ServiceDayService, ServiceDtoMapper, ArrivalReminderService (настройки, история, превью, снимок)
├── Services/Stays/                          StayBookingReleaser (🆕), StaysBookingGate (исполнитель всегда), StaysAccess (+3),
│                                            StayBookingTransitionService (вызов Releaser), StayPaymentProofService (владелец-заказ),
│                                            StayNotificationPlan/Planner/Texts (+сеансы, +заказы), StaysBoardService, StaysScheduleService,
│                                            StayBookingCreationService (+services[]), StayPersonalData (+снимок), StaysOptions (+Services)
├── Services/Scheduling/Tasks/               StaysHoldExpiryTask (+заказы), StaysScheduledMessagesTask (+заказы, время, снимок),
│                                            StaysGuestPushDispatchTask (+заказы)
├── Services/Retention/Rules/                +4 правила, 4 расширения
├── Services/Notifications/                  NotificationTypeCatalog (+ServiceTypes); CompanyNotificationsController.BuildMask (фильтр)
├── Startup/                                 RateLimitingExtensions (+stay-service-create, +stay-session-add), LoggingExtensions (маска),
│                                            DeploymentSafetyChecks (+ValidateStaysServices)
└── appsettings*.json                        Stays:Services, Retention:StayService*, RateLimits

ServiceBooking.UnitTests/   ServiceVectorsTests (все разделы service-vectors.json), ServiceSlotCalculatorTests, ServiceRefundTests,
                            ArrivalReminderTemplateTests (байт-в-байт NULL-шаблона, фильтр push), StaysAccessTests (+3),
                            NotificationMaskGuardTests, StayBookingReleaserGuardTests, StaysTextsForbiddenWordsTests (+),
                            DeploymentSafetyChecks (+Services), StaysSlugPolicyTests (+reservedHouseSlugs)
ServiceBooking.Tests/       Tests/Cycle39*: ServicesCabinetTests, ServiceScheduleTests, ServicePricingTests, ServiceOrderTests,
                            ServiceSessionInBookingTests, ServiceConcurrencyTests (§39.5.4), ServiceHoldExpiryRaceTests, ServiceBoardTests,
                            ServiceScheduleViewTests (горничная), ArrivalReminderTests, ServicesNotificationsTests, ServicesSubjectDataTests,
                            ServicesKindIsolationTests, Cycle39ContractTests (OpenApiContract по cycle39)

frontend/   dom/src/pages/{ServicePage, ServiceOrderPage}, dom/src/pages/cabinet/{ServicesPage, ServiceCreatePage, ServiceEditPage,
            ServiceDayPage, ServiceSessionCardPage}; dom/src/components/services/* (выбор времени, редактор окон, правила цен,
            позиции, «День услуг», блок услуг брони); dom/src/components/cabinet/ArrivalReminderCard.tsx;
            dom/src/utils/{businessClock, serviceTimeFormat, serviceMoney, serviceWindows}.ts + тесты;
            src/components/staffMax/ (перенос); src/types/api-cycle39.generated.ts
contracts/cycle39/  openapi.yaml, openapi.json (генерат), service-vectors.json, dom-routes.json
contracts/cycle37/  openapi.yaml (lint + перечисления), openapi.json (генерат)
tools/bench/cycle39/  seed.py, bench39.py, results/
deploy/nginx/dom.ezbook.conf (маска /s/), .github/workflows/ci.yml (lint/types/json cycle39), DEPLOY.md §28
```

`Cycle22RouteTable.golden.txt` — **58 новых маршрутов** (сводка — `API_CONTRACT_CYCLE39.md` §39.39).

---

## §39.17. Разбивка работ и параллельность

Контракт (`openapi.yaml` + `service-vectors.json` + `dom-routes.json`) готов **до** кода — фронтенд стартует на prism-моке
(`npx @stoplight/prism mock contracts/cycle39/openapi.yaml --port 4039`) в день 1.

### §39.17.1 Backend

| # | Задача | Т39 | Зависит от | Параллельно с |
|---|---|---|---|---|
| BE-39-P | Чистые классы + юнит-тесты по векторам: `BusinessClock`, `ServiceTimeFormat`, `ServiceScheduleRules`, `ServicePriceRules`, `ServicePricing`, `ServiceMoney`, `ServiceSlotCalculator` (+`Diagnose`), `ServiceRefund` (+валидатор конфигурации), `ServicePublishRules`, `ArrivalReminderTemplate` (валидация, рендер, фильтр, длины), `StaysAccess` (+3), `NotificationTypeCatalog` (+маска, страж) | 01, 02, 03, 04, 12, 13, 14 | — | всё |
| BE-39-M | Сущности, перечисления, `AppDbContext`, **одна миграция** `Cycle39StaysServices`, `ShowcaseOwnership.NeverWritten`, `Guid → Guid?` в трёх сущностях цикла 37 — **один разработчик, один коммит** | — | — | BE-39-P |
| BE-39-1 | Кабинет услуг: CRUD, порядок, адрес, фото, настройки, публикация/архив/удаление, расписание (`ServiceScheduleWriter`, журнал, предупреждение о сеансах вне окон), ручные даты, правила цен (409 пересечение, предпросмотр-матрица), позиции; настройка `acceptServiceOrdersWithoutStay` (+409 без исполнителя) | 16 | BE-39-M | BE-39-2, BE-39-5 |
| BE-39-2 | Занятость услуги и гость: `ServiceSessionWriter` (EXCLUDE → 409), `ServiceScheduleResolver`, публичные страница/календарь/старты/расчёт, `StayBookingReleaser` (+замена трёх вызовов, страж), `StaysBookingGate` (исполнитель всегда), заказ (`ServiceOrderCreationService`, лимиты, капча, ленивое снятие с try-lock), страница заказа, подтверждения оплаты (владелец «заказ»), отмена гостем, `ServiceRefund`, `StayServiceOrderEventLog`; 2 политики частоты | 01, 02, 03, 05, 07, 08, 09 | BE-39-M, BE-39-P | BE-39-1 |
| BE-39-3 | Сеансы в брони: по ссылке (список услуг, старты, расчёт, добавление, отмена), персоналом (основание, уведомление), бронь дома с сеансами (P1), строки суммы, итоги, версия брони | 05, 06 | BE-39-2 | BE-39-4 |
| BE-39-4 | Персонал: шахматка (+услуги), «День услуг», список/карточка сеансов, действия с `expectedVersion`, файлы + событие просмотра, график (+сеансы, форма горничной), ручной заказ (P1), счётчик ожидающих | 10 | BE-39-2 | BE-39-3, BE-39-5 |
| BE-39-5 | Уведомления: 12 типов, планировщик (заказы), тексты (гостевой/персональный формат, §12.6/§12.7 обзора), push гостю заказа, задачи (второй проход таймера, 10 минут, диспетчер) | 04, 06 | BE-39-2 | BE-39-3, BE-39-4 |
| BE-39-6 | Напоминание: маршруты настроек/предпросмотра/истории, история, задача (время из настроек, снимок, событие), страница брони (блок), регресс байт-в-байт | 11, 12, 13 | BE-39-M, BE-39-P | все |
| BE-39-7 | ПДн: выгрузка, удаление, отзыв, 4 новых + расширенные правила retention, маска пути, сторож телефона, `retention/policy`; админка (число услуг и сеансов за 30 дней, US-39-28, P1) | 10, 11 | BE-39-2 | BE-39-5 |
| BE-39-8 | `Cycle22RouteTable.golden.txt`, `contracts/cycle39/openapi.json` (`npm run contracts:json`), `OpenApiContractValidatorTests [InlineData("cycle39")]`, `Cycle39ContractTests`, `API_DOCUMENTATION.md` | — | в конце | — |

### §39.17.2 Frontend

| # | Задача | Т39 | Зависит от | Параллельно с |
|---|---|---|---|---|
| FE-39-0 | `types:api:cycle39` + генерат, утилиты (`businessClock`, `serviceTimeFormat`, `serviceMoney`, `serviceWindows`) + тест по векторам, `domRoutes` на `contracts/cycle39/dom-routes.json`, запасные тексты 9 новых ключей + 3 новые редакции | 04, 07, 14 | — | всё |
| FE-39-1 | Кабинет услуг: список, создание, карточка-вкладки; редактор окон с «(след. дня)» и подсказкой о полуночи, «скопировать на будни/все дни», предупреждение о сеансах вне окон; календарь ручных дат; правила цен + матрица; позиции; правила (предупреждение «нет предоплаты», `StayServiceCancellationOwnerNotice`, `StayServiceSafetyOwnerNotice`); настройка «заказы без проживания» | 01, 16 | FE-39-0 | FE-39-2…5 |
| FE-39-2 | Публичное: блок услуг на странице компании и дома, страница услуги (таблица цен гостевым форматом, позиции, правила отмены, без `StayTouristTaxNotice`), выбор времени (дата → старт → часы → позиции = 0), форма заказа (капча, телефон, комментарий + `StayServiceCommentNotice`, галочка мессенджера, `StayServiceBookingNotice`), страница заказа `/s/:token` | 04, 05, 07, 08, 15 | FE-39-0 | FE-39-3 |
| FE-39-3 | Страница брони: блок «Услуги к проживанию» (добавить/отменить, `StayServiceAddNotice`, подсказка для «Удержана», блок «по вашей просьбе»), блок «Напоминание о заезде»; форма брони — «Добавить к проживанию» (P1, ничего не выбрано) | 05, 06 | FE-39-2 | FE-39-4 |
| FE-39-4 | Персонал: шахматка (группа «Услуги»), «День услуг» (шкала / список на телефоне), карточка сеанса (действия, файлы), «Ожидают проверки» (вкладка сеансов), карточка брони (сеансы, «Добавить услугу» с основанием), график (сеансы, 360 px), ручной заказ (P1) | 06, 10 | FE-39-0 | FE-39-1, FE-39-5 |
| FE-39-5 | Настройки «Информация к заселению»: время, шаблон с кнопками подстановок, `StayReminderTemplateOwnerNotice`, мягкие предупреждения, диалог подтверждения маркеров (409), переключатель push с `StayReminderPushOwnerNotice`, предпросмотр трёх каналов с выпавшими строками, «Вернуть по умолчанию», история (P1) | 11, 12, 13 | FE-39-0 | FE-39-4 |
| FE-39-6 | Перенос `staffMax` в `src/components/staffMax/` + MAX-карточка в профиле dom (US-39-21, P1) | — | — | всё |

### §39.17.3 DevOps

| # | Задача | Когда |
|---|---|---|
| DO-39-01 | `deploy/nginx/dom.ezbook.conf`: маска `/s/<token>`, `/api/stays/service-orders/public/<token>` и `Referer` с `/s/` | сразу |
| DO-39-02 | CI: `redocly lint` cycle39 (+ cycle37 профилем по умолчанию), `types:api:cycle39` + `git diff --exit-code`, `contracts-to-json.mjs` += `cycle39` и сверка JSON | сразу |
| DO-39-03 | US-39-22: чистка `contracts/cycle37/openapi.yaml` (§39.15.2), регенерация `openapi.json` и типов, строка в `contracts/redocly.yaml` | сразу (до BE-39-8) |
| DO-39-04 | `tools/bench/cycle39/` (US-39-23) — скрипты и прогон на локальном стенде, отчёт | после BE-39-2 |
| DO-39-05 | `DEPLOY.md` §28: факт выката dom, push гостю включён, откат миграции (C37-6-подобный риск), «не менять `BusinessDayStartMinute` при данных»; US-39-24 | сразу |

### §39.17.4 QA

Кейсы `CY39-*` в `TEST_CATALOG.md`; schemathesis по `contracts/cycle39/openapi.yaml`; векторы в C# и TS (один файл);
параллельные тесты §39.5.4 (в том числе через полночь и 06:00, 40P01); гонка таймера заказа; права — таблица §39.12 (право ×
должность); форма графика без телефона и сумм (Т39-10); реквизиты не в публичных DTO; «к возврату не меньше 0 ₽» не
появляется ни в одном ответе (Т39-02); тексты без «задаток/невозвратный/депозит» (Т39-14); push напоминания: фильтр строк
и отсутствие имени/адреса/телефона/суммы/токена в видимом тексте (Т39-13); снимок напоминания стирается при
обезличивании (Т39-11); позиции по умолчанию 0 (Т39-05); сеанс персонала без основания — 400, с основанием — уведомление
и блок (Т39-06); исполнитель при 0 % (ЮР39-2); регресс CY37-* целиком (числа не ниже базы); goods после переноса
`staffMax` — без правки ожиданий; ручные `M39-*` с вердиктами: ночной сеанс на 360 px, push напоминания на реальном
телефоне, «День услуг» на планшете, iOS Safari выбор времени; «зелёный прогон ≠ функционал» — grep классов §39.16.

### §39.17.5 Точки синхронизации BE↔FE

| Что | Где |
|---|---|
| форма DTO, коды 409, перечисления | `contracts/cycle39/openapi.yaml` → генерат |
| бизнес-день, окна, цены, деньги, старты, формат | `service-vectors.json` |
| адреса dom, резерв | `contracts/cycle39/dom-routes.json` |
| тексты 400/429 | `API_CONTRACT_CYCLE39.md` — фронт печатает `response.data` |
| тексты возврата, напоминания, подписи времени | сервер (готовые строки в DTO) |

### §39.17.6 Если не укладываемся (R39-1)

Режутся первыми (P1): US-39-10 (сеанс в форме брони — путь через страницу брони остаётся), ручной заказ в US-39-15
(запасной путь — ручная дата с комментарием), US-39-21…23, US-39-28, `{Услуги}` (подстановка остаётся в таблице, значение
пусто → строка выпадает), «ближайшая дата со свободным временем», предпросмотр на реальной брони, история шаблона на
экране (запись истории — P0). P0 не режутся.

---

## §39.18. Риски, решения и отклонения от буквы SPEC

| # | Риск | Решение |
|---|---|---|
| R39-1 | Объём | параллельный план, prism-мок, P1 режутся первыми (§39.17.6) |
| R39-2 | Конкурентность рядом с C37-9 | ограничение БД, единый порядок блокировок §39.5.2, try-lock для «обратного» снятия, освобождение без замка услуги, параллельные тесты на 40P01 |
| R39-3 | Ошибки на границе суток и поясов | одна `BusinessClock`, минуты бизнес-дня, векторы C#/TS (полночь, 06:00, 31 дек), календарные даты гостю |
| R39-4 | Фейковые заказы при «нет предоплаты» | капча, лимиты, заказ без проживания по умолчанию выключен, предупреждение владельцу |
| R39-5 | Текст владельца гостю (коды, имена, реклама) | белый список, жёсткий фильтр push, диалог маркеров, мягкие предупреждения, запрет слов, push выключен по умолчанию, история с подтверждениями |
| R39-6 | Параллельный цикл 38 | значения `NotificationType`, `StayBookingEventKind`, `StaysPermission`, `PublicArea` сверяются при мерже; миграция пересобирается поверх влитой; эталон маршрутов и `ci.yml` — ручной мерж; порядок вливания — согласовать (§39.19) |
| R39-7 | Уведомления на бою | всё видно на страницах брони и заказа; снимок напоминания — без каналов |
| R39-8 (новый) | `Guid → Guid?` у трёх сущностей цикла 37 | компилятор находит все места; CY37-* регресс; CHECK «ровно один владелец» в БД |
| R39-9 (новый) | Исполнитель обязателен и при 0 % (ЮР39-2) — компании стенда без сведений перестанут принимать брони | чек-лист кабинета; сказать заказчику; тесты цикла 37 обновить с записью реестра |
| R39-10 (новый) | Смена `BusinessDayStartMinute` при данных ломает правила и окна | валидатор диапазона; запрет в `DEPLOY.md`; значение — конфигурация, не настройка |
| R39-11 (новый) | Перенос `staffMax` ломает goods | пропсы вместо импортов goods, тесты goods без правки ожиданий |

**Отклонения от буквы SPEC (читать обязательно):**
1. Шаблоны отмены сеанса — ЮР39-1 вместо §4.8 SPEC («Стандартный» не реализуется).
2. Формат времени гостю — календарные даты (ЮР39-8) вместо §4.9 SPEC; формат §4.9 — только персоналу.
3. Сведения об исполнителе — обязательны всегда, и для домов с 0 % (ЮР39-2).
4. Запасной «короткий push-шаблон» (A39-8) не делается — принят фильтр (ЮР39-3).
5. Сеанс, добавленный персоналом, требует основания и уведомляет гостя (ЮР39-6).
6. Ручной отдельный сеанс (P1) мессенджер гостю не отправляет (нет согласия, Т37-12), как ручная бронь цикла 37.
7. Время, шаблон и push напоминания — отдельный маршрут `arrival-reminder`, а не поля `settings` (защита от полной замены).
8. Один заказ без проживания = один сеанс (две услуги — два заказа).
9. Рубеж «Расходов на подготовку» — настройка услуги в пределах 3…24 ч (ЮР39-1), а не только конфигурация.

---

## §39.19. Открытые вопросы к заказчику (кодирование не блокируют; по умолчанию — как в скобках)

1. **Граница бизнес-дня 06:00** (SPEC §0 просит показать): устраивает ли (по умолчанию — да, конфигурация).
2. **Исполнитель при 0 % у домов** (ЮР39-2): компании стенда без сведений перестанут принимать брони сразу после
   деплоя — согласны (по умолчанию — да, по рекомендации юриста)?
3. **Рубеж «Расходов на подготовку»** — настраивает владелец на каждой услуге в пределах 3…24 ч (по умолчанию — так) или
   один на компанию?
4. **Один заказ без проживания — одна услуга** (баня + чан = два заказа и две предоплаты): устраивает ли (по умолчанию — да)?
5. **Push гостю о сеансе, который он добавил сам**, не отправляется (гость и так на странице); о сеансе, добавленном
   персоналом, — отправляется (по умолчанию — так).
6. **Ручной заказ персонала (P1)** — мессенджер гостю не уходит (нет согласия, как у ручной брони); устраивает ли?
7. **Порядок вливания циклов 38 и 39** в `develop` — кто первым (по умолчанию — кто раньше готов; второй пересобирает
   миграцию и сверяет значения перечислений).
8. **Видимость удержанного сеанса**: старт, занятый удержанием, публично просто «недоступен» (SPEC, низкий риск) —
   подтвердить.
