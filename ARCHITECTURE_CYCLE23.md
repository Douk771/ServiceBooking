# ARCHITECTURE — цикл 23 ServiceBooking: «Заказы», цикл 1 «Ядро» (goods.ezbook.ru)

**Разделы §386–§405.** Вход: `SPEC.md` цикла 23 (согласован заказчиком 2026-09-30, ответы на §0: Q1, Q2, Q4, Q5 —
«Рекомендую», **Q3 отклонён** — выключателя приёма в цикле 1 нет), `CURRENT_STATE.md` на `169534a`, код `develop` =
`169534a`. Ветка цикла — `cycle/023-goods-orders-core` (подготовлена devops; архитектор веток не трогает).

**Документы цикла:**

| Файл | Что | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE23.md` (этот) | решения, модель, структура, задачи, риски | все |
| `API_CONTRACT_CYCLE23.md` (§406–§425) | контракт словами: порядок проверок, тексты, коды, изменения существующих маршрутов | backend, frontend, QA |
| `contracts/cycle23/openapi.yaml` | **источник истины по форме** (OpenAPI 3.0.3) — prism-мок, `openapi-typescript`, schemathesis, redocly | backend, frontend, QA, CI |
| `contracts/cycle23/goods-routes.json` | маршруты goods и политика адреса магазина (формат, резерв слов) | backend (embedded), frontend (тест), QA |
| `contracts/cycle23/order-money-vectors.json` | эталон денежной арифметики — один набор векторов для юнит-тестов бэкенда и фронта | backend, frontend |

⚠️ **Почему не корневые `ARCHITECTURE.md`/`API_CONTRACT.md`:** по конвенции проекта это документы цикла 3, которые
**не перезаписываются** (`CURRENT_STATE.md` §2, дерево репозитория); документы циклов лежат в корне как
`ARCHITECTURE_CYCLE<N>.md` / `API_CONTRACT_CYCLE<N>.md`, машиночитаемые контракты — `contracts/cycle<N>/`.

**Попутно (SPEC §10–§11).** Архив `SPEC_CYCLE22_REFACTORING_DEAD_CODE.md` создан, не закоммичен — фиксирует первый
коммит цикла (devops/аналитик). Перенаправление ссылок на спеку цикла 22 (C5) в этот документ не входит.

---

## §386. Итог решений — ответы на §7 A1–A12 одним экраном

| # | Вопрос SPEC | Решение | Раздел |
|---|---|---|---|
| A1 | Модель заказов | 7 новых таблиц: `ShopSettings` (1:1 с компанией), `ProductCategories`, `Products`, `Orders`, `OrderItems`, `OrderEvents`, `OrderDailyCounters`. Количества — **целые** (штуки / граммы). Снимки названия, цены, единицы, правил магазина — в заказе. **Резерв не хранится, а вычисляется** из активных заказов (дрейф невозможен по построению) | §388 |
| A2 | Тип компании | `Company.Kind` (`Services`=0 / `Orders`=1), неизменяемый. Единый `CompanyKindGuard`, закрытый перечень точек проверки, `?kind=` на кабинетных списках **со значением по умолчанию `Services`** — фронт ezbook не трогается, магазины из него исчезают сами | §389 |
| A3 | Адрес магазина | `https://goods.ezbook.ru/<slug>` (корень), заказ — `/o/<token>`. Slug общий с салонами (существующий уникальный индекс). Формат и резерв слов — `goods-routes.json`, применяются **только к магазинам** (поведение ezbook не меняется) | §390 |
| A4 | Домены и ссылки | Секция конфигурации `PublicSites` + единственный класс `PublicSiteLinks` (`CompanyPageUrl`, `OrderPageUrl`, `SiteBaseUrl(kind)`). Им же воспользуются уведомления цикла 2 | §391 |
| A5 | Второй фронтенд | **Второе приложение в том же npm-пакете `frontend/`** (каталог `frontend/goods/`), общие модули импортируются напрямую из `frontend/src`. Один `package-lock`, одна копия React, один `npm ci`. Отдельный пакет `frontend-goods/` отвергнут — §399.1 | §399 |
| A6 | Раздельный вход | Получается сам: токен в `localStorage` своего origin (ключ `auth-store`), `/api` проксируется на том же домене → CORS не участвует. Ни одного origin-зависимого места в бэкенде для goods нет; бот MAX никуда не возвращает (фронт опрашивает статус) | §400 |
| A7 | Автообновление и звук | **Опрос**, не поток: доска — каждые 5 с с дешёвым «ничего не менялось» по счётчику `OrdersRevision` (один PK-lookup); страница заказа — каждые 10 с. SSE отвергнут (EventSource не шлёт `Authorization` → токен в URL → в логах). Звук — Web Audio после нажатия «Включить звук»; таймер опроса — в Web Worker; Wake Lock | §397 |
| A8 | Конкурентность | Остатки: транзакция + `pg_advisory_xact_lock('shop-stock:{companyId}')` на всё, что **уменьшает свободный остаток**; резерв вычисляемый. Статусы: `Order.Version` (EF concurrency token) + `expectedVersion` в каждом действии персонала → 409 `VersionMismatch` с актуальным заказом | §394, §396 |
| A9 | Гейт тарифа | **Не `AllowOnlineBooking`.** Одна чистая функция `ShopOrderingGate.Evaluate` — в цикле 1 «компания типа Orders и активна». Цикл 2 дописывает в неё часы работы, паузу и возможность тарифа. Лимиты компаний/сотрудников — существующие, общие на аккаунт | §392.3 |
| A10 | Роль сотрудника | **Переиспользуем `CompanyMember` с `UserRole.Master`** (в интерфейсе goods — «Сотрудник»). Владелец — `CompanyOwner`. Права магазина — одна таблица в `ShopAccess`; в магазин через API добавляется только `Master` | §392 |
| A11 | ПДн | Выгрузка, удаление аккаунта, отзыв согласий расширяются на заказы по образцу записей, через `SubjectScope` (гейт цикла 16). Правило уничтожения `order-personalization` со сроком `Retention:OrderPersonalDataDays` = **0 до заключения юриста** (ничего не удаляет). Снимок версий документов — на заказе | §398 |
| A12 | Номер заказа | Таблица `OrderDailyCounters (CompanyId, BusinessDate) → LastNumber`, выдача атомарным `INSERT … ON CONFLICT DO UPDATE … RETURNING` в транзакции заказа. Сутки — по часовому поясу магазина. Уникальный индекс `(CompanyId, BusinessDate, Number)` — страховка | §396.5 |
| — | nginx, TLS, деплой, CI | Отдельный vhost `goods.ezbook.ru` (текст — §401.1), свой сертификат certbot. **Один релизный каталог на оба фронта** (`releases/<ts>/` = ezbook, `releases/<ts>/__goods/` = goods) — `ssh-deploy-wrapper.sh` и `rollback.sh` не меняются, откат атомарен для обоих сайтов | §401 |

---

## §387. Стек: новых зависимостей — ноль, и вот почему

Проект существующий, стек не выбирается заново. Задача цикла — модуль внутри монолита (Р1) и второй фронтенд с общей
дизайн-системой (Р2). Ни то, ни другое не требует новой технологии:

| Потребность | Чем закрываем | Почему не новое |
|---|---|---|
| Бэкенд модуля заказов | тот же ASP.NET Core 8 + EF Core 8 + PostgreSQL 16, те же конвенции (толстые контроллеры + чистые помощники в `Services/`) | Р1: «модуль внутри ServiceBooking»; общие аккаунты, биллинг, согласия |
| Автообновление экрана заказов | HTTP-опрос + счётчик ревизий | SignalR/SSE — второй транспорт, состояние в процессе, настройка буферизации nginx, токен в query у EventSource. При нагрузке §6 (≤ 1 000 экранов) опрос даёт ≤ 200 rps PK-lookup'ов — это копейки для одной машины (§397.2) |
| QR-код для печати | **уже подключённый `QRCoder` 1.6.0** (`PngByteQRCode`, цикл 14) | браузерная QR-библиотека расширила бы красный `npm audit` (§9 D5) |
| Фото товара | существующий `ImageUploadService` + новый `ImageProfile` | конвенция: одна реализация конвейера на все точки загрузки |
| Капча | существующий `CaptchaService` + `SmartCaptcha.tsx` | — |
| Подтверждение телефона | существующая подсистема цикла 14 (сценарий US-14-16 «подтвердить свой текущий номер») | — |
| Звук | Web Audio API (генерация сигнала осциллятором) | ни аудиофайлов, ни библиотек |
| Второй фронтенд | второй `vite build` того же пакета с отдельным конфигом | §399.1 |
| TLS | certbot, как у `ezbook.ru` и `errors.ezbook.ru` | — |

**Взгляд на продажу как сервиса и масштаб.** Опрос без состояния в процессе масштабируется горизонтально без
липких сессий; счётчик ревизий живёт в БД, а не в памяти. Если в будущем понадобится второй экземпляр API, в
модуле заказов нет ни одного in-memory состояния, которое помешает (единственное — кеши `IMemoryCache` существующих
подсистем). Отдельный managed-сервис (очереди, pub/sub) на этой нагрузке — лишняя стоимость хостинга.

---

## §388. Модель данных (A1)

Все сущности — `ServiceBooking.Core/Entities/`, конфигурация — `AppDbContext`. Все перечисления хранятся **числом**
(конвенция проекта) и помечены append-only. Деньги — `decimal(10,2)`; количества — `int` в базовой единице.

### §388.1 `Company` — одна новая колонка

| Колонка | Тип | Смысл |
|---|---|---|
| `Kind` | `int NOT NULL DEFAULT 0` → `CompanyKind { Services = 0, Orders = 1 }` | тип компании. Все существующие строки — `Services` без бэкфилла (дефолт колонки). **Сеттер в DTO нет нигде**, включая админку: тип задаётся только `CompanyCreationService` при создании |

### §388.2 Новые сущности

**`ShopSettings`** — настройки магазина, 1:1, **PK = `CompanyId`** (приём `CompanyNotificationSettings`). Строка
создаётся в той же транзакции, что и магазин; читатели всё равно трактуют отсутствие строки как дефолты.

| Поле | Тип | Дефолт | Смысл |
|---|---|---|---|
| `CompanyId` | Guid PK, FK → Companies `Restrict` | | |
| `CustomerMode` | `ShopCustomerMode { Anyone=0, VerifiedPhoneOnly=1 }` | Anyone | US-23-10 |
| `AcceptanceMode` | `OrderAcceptanceMode { Manual=0, Auto=1 }` | Manual | |
| `AllowCustomerCancel` | bool | true | |
| `TrackStock` | bool | false | |
| `OrdersRevision` | bigint | 0 | счётчик изменений заказов магазина для дешёвого опроса (§397.1). Пишет **только** `OrderEventLog` |
| `SellerLegalForm` | `LegalEntityForm?` (существующее перечисление) | null | **[legal L2]** |
| `SellerLegalName` | varchar(300)? | null | **[legal L2]** |
| `SellerInn` | varchar(12)? | null | **[legal L2]**, формальная проверка `InnValidator` |
| `SellerOgrn` | varchar(15)? | null | **[legal L2]** |
| `SellerLegalAddress` | varchar(500)? | null | **[legal L2]** |
| `UpdatedAtUtc`, `UpdatedByUserId?` | | | |

**`ProductCategory`**

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | Guid | |
| `CompanyId` | Guid FK `Restrict` | |
| `Name` | varchar(100) | |
| `Position` | int | порядок; уплотняется сервером |
| `IsHidden` | bool | товары скрытой категории покупатель не видит |
| `CreatedAtUtc`, `UpdatedAtUtc` | | |

Индекс `(CompanyId, Position)`. Удаление — жёсткое, только пустой (проверка в коде + FK `Products.CategoryId`
`Restrict` как страховка; удалённые мягко товары тоже держат FK → при удалении категории их `CategoryId` обнуляется
тем же запросом, потому что они уже не видны нигде).

**`Product`**

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | Guid | |
| `CompanyId` | Guid FK `Restrict` | |
| `CategoryId` | Guid? FK `Restrict` | null → блок «Другое» |
| `Name` | varchar(200) | |
| `Description` | varchar(2000)? | |
| `ImageUrl`, `ThumbnailUrl` | varchar(300)? | **публичный** класс хранения, область `products` (как логотип) |
| `Unit` | `ProductUnit { Piece=0, Weight=1 }` | **неизменяемо после создания** (иначе меняется смысл остатка и количеств) |
| `Price` | decimal(10,2) | за штуку или за 1 кг, 0.01…1 000 000 |
| `PortionText` | varchar(50)? | только Piece: «300 г» |
| `WeightStepGrams` | int? | только Weight, 10…5000, дефолт 100 |
| `MinQuantityGrams` | int? | только Weight, ≥ шаг, кратно шагу, дефолт = шаг |
| `Position` | int | порядок в категории |
| `IsPublished` | bool | |
| `IsSoldOut` | bool | «закончилось» — переключает персонал |
| `CompositionAndAllergens` | varchar(2000)? | **[legal L3]** единственное поле пищевой информации цикла 1; в API — внутри объекта `foodInfo` |
| `StockOnHand` | int? | остаток «на складе» в штуках/граммах; **null = не учитывается** (US-23-17) |
| `DeletedAtUtc` | timestamptz? | мягкое удаление: строка нужна заказам (FK) и отчётам цикла 3 |
| `CreatedAtUtc`, `UpdatedAtUtc` | | |

Индексы: `(CompanyId, CategoryId, Position)`, `(CompanyId)` с фильтром `"DeletedAtUtc" IS NULL`.

**`Order`**

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | Guid | |
| `CompanyId` | Guid FK `Restrict` | |
| `Number` | int | номер за сутки (§396.5) |
| `BusinessDate` | date | сутки магазина по его `TimeZoneId` на момент создания |
| `PublicToken` | varchar(64) | 32 случайных байта, base64url (43 символа). Уникальный индекс. Хранится открыто — §405 R-6 |
| `Status` | `OrderStatus { New=0, Accepted=1, Ready=2, Issued=3, Rejected=4, CancelledByCustomer=5, CancelledByShop=6, NotPickedUp=7 }` | append-only |
| `Version` | int | **concurrency token** EF; +1 на каждое изменение |
| `CustomerKind` | `OrderActorKind` (`Customer` / `Guest`) | |
| `CustomerUserId` | string? FK → AspNetUsers `SetNull` | |
| `CustomerName` | varchar(100)? | null после обезличивания |
| `CustomerPhone` | varchar(20)? | канонический (`PhoneNormalizer`); null после обезличивания |
| `CustomerPhoneVerified` | bool | снимок: заказ сделан с подтверждённым номером |
| `Comment` | varchar(500)? | |
| `AcceptanceModeSnapshot` | `OrderAcceptanceMode` | правила, с которыми создан (US-23-10: изменения настроек — только на новые заказы) |
| `AllowCustomerCancelSnapshot` | bool | |
| `CustomerModeSnapshot` | `ShopCustomerMode` | |
| `EstimatedTotal` | decimal(10,2) | по заказанным количествам; пересчитывается правкой |
| `FinalTotal` | decimal(10,2)? | к оплате; ставится при выдаче |
| `HasWeightItems` | bool | |
| `IsModifiedByShop` | bool | метка «изменён» |
| `StatusReason` | varchar(300)? | причина отклонения/отмены магазином |
| `IdempotencyKey` | uuid | уникальный индекс `(CompanyId, IdempotencyKey)` |
| `ConsentPrivacyVersion?`, `ConsentTermsVersion?`, `ConsentAcceptedAtUtc?` | | снимок согласия **гостя** (как `Booking`, §398.3) |
| `CheckoutNoticeVersion?` | varchar(32) | **[legal L4]** версия строки под кнопкой, если текст уже есть в манифесте |
| `PersonalDataErased` | bool | обезличено (удаление аккаунта / правило уничтожения) — отличает «стёрто намеренно» от порчи данных (приём `Booking.ClientDeleted`) |
| `CreatedAtUtc`, `AcceptedAtUtc?`, `ReadyAtUtc?`, `CompletedAtUtc?`, `UpdatedAtUtc` | | `CompletedAtUtc` — момент перехода в любой конечный статус |

Индексы: уникальные `(CompanyId, BusinessDate, Number)`, `(PublicToken)`, `(CompanyId, IdempotencyKey)`; обычные
`(CompanyId, Status)`, `(CompanyId, CompletedAtUtc)`, `(CustomerUserId, CreatedAtUtc)`, частичный
`(CustomerPhone, CreatedAtUtc) WHERE "CustomerPhone" IS NOT NULL` (лимит по телефону, выгрузка субъекта).

**`OrderItem`**

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | Guid | |
| `OrderId` | Guid FK `Cascade` | |
| `Position` | int | порядок показа |
| `ProductId` | Guid? FK `Restrict` | ссылка на (возможно, мягко удалённый) товар |
| `NameSnapshot` | varchar(200) | |
| `Unit` | `ProductUnit` | снимок |
| `UnitPrice` | decimal(10,2) | снимок цены за штуку/кг |
| `PortionTextSnapshot` | varchar(50)? | |
| `WeightStepGrams` | int? | снимок — правка и выдача валидируют по нему |
| `QuantityOrdered` | int | штуки/граммы |
| `QuantityActual` | int? | выдано фактически (граммы у Weight; у Piece = заказанному) |
| `LineTotalEstimated` | decimal(10,2) | |
| `LineTotalFinal` | decimal(10,2)? | |
| `ReservesStock` | bool | **резервирует ли эта строка остаток** — true, только если при создании/правке у магазина был включён учёт и у товара `StockOnHand != null` (US-23-17: «включение обратно начинает с текущих цифр, без пересчёта прошлого») |

Индексы `(OrderId)`, `(ProductId)`.

**`OrderEvent`** — журнал заказа, append-only, **единственный писатель `OrderEventLog`** (приём `BookingEventLog`).

| Поле | Тип | Смысл |
|---|---|---|
| `Id`, `OrderId` (FK `Cascade`), `CompanyId` (денормализация — проверка доступа и правило уничтожения без join) | | |
| `Kind` | `OrderEventKind { Created=0, Accepted=1, Rejected=2, MarkedReady=3, Issued=4, NotPickedUp=5, CancelledByCustomer=6, CancelledByShop=7, Edited=8 }` | append-only |
| `OccurredAtUtc` | | |
| `ActorKind` | `OrderActorKind { Customer=0, Guest=1, Staff=2, SuperAdmin=3, System=4 }` | |
| `ActorUserId?`, `ActorNameSnapshot?` | | имя сотрудника **снимком**; покупателю не отдаётся |
| `FromStatus?`, `ToStatus?` | | |
| `Reason?` | varchar(300) | |
| `Comment?` | varchar(500) | комментарий магазина к правке — покупатель его видит |
| `ChangesJson?` | jsonb | правка: `[{name, before:{qty,unitPrice,unit}|null, after:{…}|null}]`; выдача: фактические веса и списание остатка |
| `TotalBefore?`, `TotalAfter?` | decimal(10,2) | |
| `VisibleToCustomer` | bool | статусы и правки — да; служебные записи о списании остатка — нет |

Индексы `(OrderId, OccurredAtUtc)`, `(OccurredAtUtc)`.

**`OrderDailyCounter`** — PK `(CompanyId, BusinessDate)`, `LastNumber int`. Строк — одна на магазин в сутки;
ПДн нет; правило уничтожения не нужно (при желании — чистка строк старше 7 дней в цикле 3).

### §388.3 Миграции — две, закреплённым `dotnet-ef` 8.0.11

1. **`AddCompanyKind`** — одна колонка с дефолтом 0. Отдельной миграцией, чтобы BE-1 (изоляция типов) влился раньше
   и не ждал модели заказов.
2. **`AddShopOrders`** — семь таблиц с индексами и FK. Создаётся **одним** разработчиком в самом начале трека
   (задача BE-M, §403), после этого оба бэкенд-трека работают поверх неё, не порождая конкурирующих миграций.

Обе — чисто добавочные. `Down()` удаляет созданное. Проверки дрейфа снапшота (CI) обязаны остаться зелёными.

### §388.4 Инварианты модели (проверяются тестами, §403 QA)

1. Строка `Product`/`ProductCategory`/`Order`/`ShopSettings` существует **только у компании `Kind = Orders`**
   (проверка в каждом писателе + функциональный тест «ни одна точка записи не принимает салон»).
2. Никакая строка `Service`/`Booking`/`WorkingHours`/`Review`/`ClientNote`/`CompanyPhoto`/назначение канала не
   создаётся для `Kind = Orders` (§389.2).
3. Свободный остаток = `StockOnHand − Σ QuantityOrdered` по строкам с `ReservesStock` в заказах со статусом
   `New/Accepted/Ready`. **Хранимого счётчика резерва нет** → «резерв после любой цепочки сходится» по построению;
   тест-инвариант всё равно пишется (§394.4).
4. `StockOnHand ≥ 0` всегда (CHECK-ограничение в миграции).
5. `Σ LineTotalEstimated = EstimatedTotal`; после выдачи `Σ LineTotalFinal = FinalTotal` (проверка перед
   `SaveChanges`, как у `BookingService`).
6. Каждое изменение `Order` сопровождается строкой `OrderEvent` **в той же транзакции**, и `OrderEventLog` в той же
   транзакции делает `OrdersRevision += 1`. Поэтому доска не может «пропустить» изменение.

### §388.5 Точки расширения под циклы 2 и 3 — без миграций-ломок

| Будущее | Как ляжет на модель | Что для этого сделано сейчас |
|---|---|---|
| Время заказа: «как можно скорее» / слот / предзаказ на дату; время приготовления | nullable-колонки `Order.RequestedPickupStartUtc/EndUtc`, `ShopSettings.MinPrepMinutes`, новая таблица `ShopPickupSlotRules` | `BusinessDate` — дата **создания**, номер от неё не зависит от даты выдачи; статусная машина не знает о времени |
| Часы работы, «Пауза приёма», выключатель «Принимаем заказы» (бывш. US-23-13) | таблица `ShopWorkingHours` (по образцу `WeeklyScheduleTemplate`), колонки `ShopSettings.PausedUntilUtc`, `AcceptingOrders` | **вся** логика «принимает ли магазин» — в одной функции `ShopOrderingGate.Evaluate`; витрина и оформление уже читают её результат (`acceptingOrders`, `notAcceptingReason`) |
| Доступность товара по дням недели, меню на дату | таблицы `ProductWeekdayAvailability`, `DailyMenu` + `DailyMenuItem` | правило «доступный товар» — одна чистая функция `CatalogAvailability.IsAvailable(...)` (§393.2) — новые условия дописываются в неё |
| Уведомления о заказах (MAX, web-push, сотрудникам) | новые члены `NotificationType` в конец, очередь — по образцу `StaffPushNotification` с `OrderId?` | ссылки строит `PublicSiteLinks` (§391); `OrderEventLog` — единственное место, где ловится «что произошло» |
| Тарифы магазинов | возможность `AllowOrders` в `SubscriptionPlanConfig` → условие в `ShopOrderingGate` | гейт уже изолирован от `AllowOnlineBooking` |
| История, сводки, лист сборки, карточка покупателя | чтение `Orders`/`OrderItems`/`OrderEvents` | снимки цены/названия/количества, `CompletedAtUtc`, `CustomerPhone` канонический, индексы по статусу и дате |
| Модификаторы/варианты, несколько фото | `ProductOptionGroups`, `ProductImages` | `OrderItem` — снимок, модификаторы лягут отдельной таблицей строк позиции |

---

## §389. Тип компании и изоляция двух продуктов (A2, US-23-01…03)

### §389.1 Единый механизм

- `Services/Companies/CompanyKindGuard.cs` — чистая функция и один помощник с БД:
  `Task<CompanyKindCheck> CheckAsync(db, companyId, CompanyKind expected)` → `Ok | NotFound | WrongKind`.
- Отказ «не тот тип» — **409 с голой строкой** (конвенция 4xx для существующих маршрутов не меняется):
  - маршрут записи на магазине: `«Это магазин: записи, услуги и расписание для него недоступны.»`
  - маршрут заказов на салоне: не 409, а **404** — для публичных маршрутов goods салона «не существует» (не оракул);
    для кабинетных маршрутов `/api/shops/{id}/…` — тоже 404 (id салона в магазинном API не адресует ничего).
- Проверка стоит **после** проверки прав (конвенция цикла 6: «сначала права, потом принадлежность»), кроме
  анонимных публичных маршрутов, где прав нет.

### §389.2 Закрытый перечень точек (backend обязан пройти все; QA — табличный тест на каждую)

**Маршруты записи, отказывающие магазину (409):**

| Контроллер | Маршруты |
|---|---|
| `ServicesController` | `POST /api/services` (companyId в теле), `PUT/DELETE /api/services/{id}`, `POST …/image` — у магазина услуг быть не может, проверка на создании достаточна, на остальных — через компанию услуги |
| `CompanyMembersController` | `PUT …/members/{memberId}/provides-services`, `PUT …/members/{memberId}/services`, `PUT …/members/{memberId}/commission` |
| `CompaniesController` | `GET /api/companies/{id}/masters`, `GET /api/companies/{id}/stats`, `GET /api/companies/{id}/photo-usage` |
| `CompanyPhotosController` | `POST/DELETE/PUT …/photos*` (галерея — только салон в цикле 1). `GET …/photos` у магазина — `[]` |
| `WorkingHoursController`, `ScheduleTemplateController` | все пишущие маршруты (companyId в теле/запросе) |
| `BookingsController`, `BookingCreationService` | `POST /api/bookings` (сразу после загрузки компании) |
| `BookingAvailabilityController` | `occupied`, `slots`, `availability` |
| `ReviewsController` | `GET /api/companies/{companyId}/reviews` → `[]`-страница у магазина (анонимный публичный список — не ошибка) |
| `ReportsController`, `MailingController`, `MastersController` (clients, notes) | по companyId |
| `CompanyNotificationsController`, `CompanyPushSettingsController` | все маршруты (уведомления магазинов — цикл 2) |
| `NotificationChannelsController` | `POST /api/notification-channels/{id}/companies` — назначение магазина на канал |
| `ClientConsentsController` | все маршруты |

**Маршруты, общие для обоих типов (без изменений поведения):** `PUT /api/companies/{id}` (профиль, город, зона, ссылки
на карты), `POST /api/companies/{id}/logo`, `CompanyAddressController` (адрес и предупреждение о публичности),
`GET/POST/DELETE /api/companies/{id}/members` — с одним добавлением: для магазина роль в `POST` — только `Master`,
иначе 400 `«В магазин можно добавить только сотрудника.»`.

**Выборки, где магазин не должен протекать в ezbook:**

| Место | Изменение |
|---|---|
| `GET /api/companies`, `GET /api/companies/public` | `Where(c => c.Kind == Services)` (SQL) |
| `GET /api/companies/my`, `GET /api/companies/member` | новый `?kind=`; **не передан → `Services`**. Фронт ezbook параметр не шлёт — магазины из его кабинета уходят без правки кода |
| `GET /api/companies/{slug}` | отдаёт и магазин — с `kind` и `publicUrl` (нужно ezbook для переадресации US-23-02) |
| `GET /api/admin/companies` | `?kind=` (не передан — все), колонки `kind`, `publicUrl` |
| `AdminController.stats` | без изменений (итог по компаниям — все типы); разбивка — не в цикле |
| `SubscriptionDiagnostics` (`GET /api/admin/owners/{id}/subscription`) | без изменений: магазины видны там со своим `onlineBookingEnabled` (служебный маршрут без экрана; §405 R-11) |
| Фоновые задачи уведомлений, `StaffPushScheduler`, `NotificationScheduler`, `photo-retention-cleanup`, `channel-health` | **не меняются**: все они порождаются записью/фото/каналом, а ни одна из этих сущностей у магазина не создаётся (инвариант §388.4-2) |
| Лимиты аккаунта (`AccountUsageReader`, `EffectivePlan.AccountMaxCompanies/Employees`) | **не меняются** — считают компании и участников всех типов (Q5) |
| `CompanyTransferService` | не меняется: перенос магазина между аккаунтами сохраняет тип |

### §389.3 Изменения фронта ezbook (весь их перечень)

1. `CompanyPage` (`/company/:slug`): если `company.kind === 'Orders'` → `window.location.replace(company.publicUrl)`.
2. `EmbedPage` (`/embed/:slug`): для `Orders` — пустой блок без виджета (текст «У этой компании нет онлайн-записи»).
3. `CabinetPage`: строка «Ваши магазины управляются на goods.ezbook.ru» со ссылкой, если
   `GET /api/companies/kinds-summary` → `orders.count > 0`.
4. `AdminPage`, вкладка компаний: колонка «Тип», фильтр «Все / Салоны / Магазины», ссылка «Открыть» — `publicUrl`.
5. `LoginPage`/`RegisterPage`: поддержка `?returnTo=` (только относительный путь, начинающийся с `/` и не с `//`),
   по умолчанию `/` — как сейчас. Нужна goods (§399.3), поведение ezbook без параметра не меняется.
6. Вынос `LegalGuard` из `App.tsx` в `components/legal/LegalGuard.tsx` с пропом `bypassPaths` — чистый перенос.
7. Типы: `Company.kind`, `Company.publicUrl`.

---

## §390. Адрес магазина и slug (A3, US-23-12)

- **Схема:** `https://goods.ezbook.ru/<slug>` — короче для QR и для диктовки по телефону. Страница заказа —
  `/o/<token>`. Остальные маршруты goods — в `goods-routes.json` → `spaRoutes`.
- **Пространство slug общее** с салонами: существующий уникальный индекс `Companies.Slug`. Салон, занявший адрес
  раньше, магазину его не отдаёт (409 `SlugTaken`) — даже если на goods по этому адресу ничего нет. Это осознанно:
  один адрес = одна компания платформы.
- **Политика `SlugPolicy` (только для `Kind = Orders`):** `^[a-z0-9]+(?:-[a-z0-9]+)*$`, длина 3–50, не из
  `reservedSlugs`, сравнение занятости — без учёта регистра (`lower(Slug)`), хранится в нижнем регистре. Для салонов
  валидация на сервере **не вводится** (поведение ezbook не меняется, SPEC §6 «Совместимость»).
- **Резерв слов** читается из `contracts/cycle23/goods-routes.json` (embedded resource — как `legal-routes.json`
  цикла 11). Добавить маршрут goods = добавить слово в резерв **до** выката маршрута; тест фронта сверяет, что
  первый сегмент каждого маршрута `GoodsApp` зарезервирован.
- **Предложение адреса:** `GET /api/shops/slug-check?name=` — транслитерация (ГОСТ 7.79-2000 Б, без диакритики) →
  нормализация → при занятости суффиксы `-2`, `-3`… (до 20 попыток, затем 4 случайных hex).
- **Смена адреса:** `PUT /api/shops/{id}/slug` (владелец), advisory-lock `company-slug`, 409 при занятости/резерве.
  Редиректа со старого адреса нет (SPEC, низкий риск); фронт перед сменой показывает предупреждение «старая ссылка и
  напечатанные QR-коды перестанут работать».
- **QR:** `GET /api/shops/{id}/qr` → PNG из `QRCoder.PngByteQRCode`, уровень коррекции Q, ~2000×2000 px (A4 при
  ~240 dpi без размытия), содержимое — `publicUrl`. Фронт получает blob (как `useAuthedImage`), показывает
  предпросмотр и отдаёт на скачивание.

---

## §391. Домены и ссылки по типу компании (A4, Р6, US-23-03)

**Конфигурация** — новая секция `PublicSites` в закоммиченном `appsettings.json`:

```json
"PublicSites": {
  "ServicesBaseUrl": "https://ezbook.ru",
  "OrdersBaseUrl": "https://goods.ezbook.ru"
}
```

Боевые значения — **дефолт в git**, поэтому новой обязательной переменной окружения нет, CI-смоук образа и `.env`
на машине не меняются. Для dev: `PublicSites__ServicesBaseUrl=http://localhost:5173`,
`PublicSites__OrdersBaseUrl=http://localhost:5174` (строки в `.env.dev.example`).
`DeploymentSafetyChecks.ValidatePublicSites`: вне Development оба значения — абсолютные `https://` без пути и хвоста
`/`; нераспознанное значение роняет старт (конвенция fail-closed). Юнит-тест.

**Единственное место построения ссылок** — `Services/PublicSites/PublicSiteLinks.cs`:

| Метод | Результат |
|---|---|
| `SiteBaseUrl(CompanyKind)` | базовый адрес сайта типа |
| `CompanyPageUrl(Company)` | салон → `{Services}/company/{slug}`; магазин → `{Orders}/{slug}` |
| `OrderPageUrl(string token)` | `{Orders}/o/{token}` |

Использование в цикле 1: `CompanyDto.publicUrl`, `AdminCompanyDto.publicUrl`, `ShopManageDto/StorefrontDto.publicUrl`,
`CreateOrderResponse.orderUrl`, `MyOrderSummaryDto.orderUrl`, QR-код, `kinds-summary.siteUrl`.
Цикл 2 (уведомления о заказах) обязан брать ссылки **только отсюда**. Существующая строка отписки в
`NotificationScheduler` (`https://ezbook.ru/u/…` через `NotificationTemplateValidator.OwnDomain`) касается только
салонов и в цикле 1 не трогается — кандидат на перевод в цикле 2.

---

## §392. Роли, доступ и гейт приёма (A9, A10, US-23-11)

### §392.1 Роль сотрудника — переиспользуем `Master`

Сотрудник магазина = строка `CompanyMember` с `Role = Master`; владелец — `CompanyOwner`. Отвергнута отдельная роль
`ShopStaff`: пришлось бы трогать `CompanyMembership.IsStaffRole`, `IdentityRoleSync`, сид ролей, `AccountUsageReader`
(места сотрудников), админку ролей — ради того же набора прав. Со старой ролью всё это работает без правок:
места считаются, Identity-роль `Master` выдаётся и снимается `IdentityRoleSync`, удалённый из магазина теряет доступ
сразу (проверка членства — на каждом запросе, по БД).

Побочный эффект, признанный допустимым: сотрудник магазина с ролью `Master` на ezbook видит пункты «Мои записи»/
«Кабинет» — там пусто и есть строка «Ваши магазины на goods» (§389.3-3).

### §392.2 Таблица прав — `Services/Shops/ShopAccess.cs` (единственное место)

| Действие | Owner | Staff (`Master`) | SuperAdmin |
|---|---|---|---|
| Экран заказов, карточка, журнал, переходы, правка, выдача | ✓ | ✓ | ✓ |
| «Закончилось», остаток | ✓ | ✓ | ✓ |
| Просмотр каталога и настроек в кабинете, QR | ✓ | ✓ | ✓ |
| Товары, цены, категории, фото, порядок | ✓ | — | ✓ |
| Настройки приёма, реквизиты, адрес магазина, профиль компании, логотип | ✓ | — | ✓ |
| Сотрудники (добавить/удалить) | ✓ | — | ✓ |

Членская половина — `CompanyMembership.IsStaffAsync/IsOwnerAsync` (конвенция). Владельческие действия — с
`[RequiresOwnerTerms]` (владельческий гейт 451, как у компаний). Сотрудник на владельческом маршруте — **403 с
пустым телом**, не 404 (доступ запрещается явно — конвенция цикла 5).

### §392.3 Гейт «магазин принимает заказы» (A9)

`Services/Shops/ShopOrderingGate.cs` — чистая функция:

```
Evaluate(Company company, ShopSettings settings, DateTime nowUtc) → (bool Accepting, string? ReasonText)
```

Цикл 1: `Accepting = company.Kind == Orders && company.IsActive`. Блокировка администратором
(`PUT /api/admin/companies/{id}`, `isActive = false`) = магазин недоступен и не принимает.
**Почему не `AllowOnlineBooking`:** Free выключает онлайн-запись, а тарифов магазинов в цикле 1 нет (Q1) — магазин на
Free не принял бы ни одного заказа. Гейт вызывают: витрина, `quote`, создание заказа, `ShopManageDto`.
Цикл 2 дописывает сюда часы работы, паузу и тариф — **больше ни одно место знать об этом не должно**.

Лимиты (Q1): создание магазина — существующая проверка `AccountMaxCompanies` под lock `billing-account:{id}`;
добавление сотрудника — существующая `AccountMaxEmployees`. Обе переезжают без изменений (§395.1).

---

## §393. Каталог и доступность

### §393.1 Операции

Категории: создание (в конец), переименование/скрытие, порядок (полный список id, уплотнение позиций), удаление только
пустой (409 `CategoryNotEmpty`). Товары: создание (в конец категории), полное обновление (`Unit` неизменяем → 409
`UnitChangeNotAllowed`), мягкое удаление, фото (новый `ImageProfile.ProductImage` 1200 px + `ProductImageThumb`
480 px, публичная область `products`, порядок «новый файл → коммит → удаление старого»), порядок внутри категории.
Порядок применяется под advisory-lock `shop-catalog:{companyId}` (как `company-photos`).
Лимиты: `Orders:MaxProductsPerShop` = 1000, `Orders:MaxCategoriesPerShop` = 100 (409 `ProductLimitReached` /
`CategoryLimitReached`).

### §393.2 Правило «доступный товар» — одна чистая функция

`Services/Shops/CatalogAvailability.cs`:

```
IsAvailable(product, category?, shopTracksStock, freeStock?, shopAccepting) =
    product.DeletedAtUtc == null && product.IsPublished && !product.IsSoldOut
    && (category == null || !category.IsHidden)
    && (!shopTracksStock || product.StockOnHand == null || freeStock >= minQuantity)
    && shopAccepting
```

Её результат — `StorefrontProductDto.available`, `ProductDto.availableToCustomers`, проблемы `quote` и отказ при
создании. Показ на витрине: неопубликованные, удалённые и товары скрытых категорий **не отдаются**; закончившиеся и
не проходящие по остатку отдаются с `available: false` (серым, «Закончилось»).

### §393.3 Чтение витрины — p95 < 300 мс на 300 товаров

Три запроса: компания + настройки + город (одна строка); категории + неудалённые опубликованные товары магазина;
если учёт остатков включён — `Σ резерв` по товарам одним `GROUP BY` (§394.2). Без N+1; `AsNoTracking`.

### §393.4 Списки каталога — без `PagedResult` (осознанное исключение из конвенции)

`GET /api/shops/{id}/products` и `…/categories` — ограниченные коллекции (≤ 1000 / ≤ 100), нужны целиком для
перетаскивания порядка и для поиска на экране заказов. Конвенция «новый список — `PagedResult`» про неограниченные
выборки; прецедент — `GET /api/companies/{id}/masters`.

---

## §394. Остатки и конкурентность (A8, блок E)

### §394.1 Единицы

`StockOnHand` — `int?` в штуках (Piece) или **граммах** (Weight, «кг с точностью до грамма»). null = не учитывается.
Магазинная настройка `TrackStock` — главный выключатель: при `false` не проверяется и не резервируется ничего,
остатки товаров сохраняются как есть.

### §394.2 Резерв — вычисляемый

```sql
SELECT oi."ProductId", SUM(oi."QuantityOrdered")
FROM "OrderItems" oi JOIN "Orders" o ON o."Id" = oi."OrderId"
WHERE o."CompanyId" = @shop AND o."Status" IN (0,1,2) AND oi."ReservesStock"
GROUP BY oi."ProductId"
```

Отмена, отклонение, «не забран» возвращают резерв **сами** — заказ перестаёт быть активным. Выдача списывает со склада
и тоже снимает резерв (заказ конечный).

### §394.3 Сериализация

Всё, что **уменьшает свободный остаток**, идёт в транзакции под `AdvisoryLock.AcquireAsync(db, $"shop-stock:{companyId}")`:
создание заказа (если учёт включён), правка заказа (если растёт количество или добавляется позиция), выдача
(списание), `PUT …/stock`. Внутри лока — пересчёт резерва §394.2 по нужным товарам, проверка, запись. Увеличение
свободного остатка (отмена и пр.) лока не требует. Нагрузка §6 (500 заказов/день на магазин) делает сериализацию по
магазину незаметной. Порядок локов при их совместном взятии: `shop-stock:{id}` → строка `ShopSettings` (через
`OrderEventLog`) — везде одинаковый, взаимоблокировок нет.

Правила:
- Заказ, которому не хватает свободного остатка, не создаётся: 409 `ItemsUnavailable`, у позиции `InsufficientStock`
  и `availableQuantity` (единственное место, где покупатель видит число, — SPEC US-23-17 «сколько не хватает»).
- Правка магазином проверяет рост количества против `free + собственный резерв этой строки`.
- Выдача: `StockOnHand -= QuantityActual` только по строкам с `ReservesStock` и только если у товара сейчас
  `StockOnHand != null`; результат ниже нуля → списание до 0, и в журнал (`ChangesJson`, `VisibleToCustomer = false`)
  пишется «списано N из M, остаток обнулён».
- `PUT …/stock` может выставить `onHand < reserved` (персонал нашёл недостачу) — тогда `free < 0`, товар для
  покупателя «Закончилось», в кабинете — предупреждение. Отрицательным `StockOnHand` не бывает (CHECK).

### §394.4 Тесты (обязательны, SPEC §6)

- Параллельный тест: N = 20 одновременных заказов последней единицы → создан ровно 1 (Testcontainers, реальный
  Postgres, как остальные функциональные).
- Инвариант: случайные цепочки создание → правка → отмена/выдача/«не забран» (без ручной правки остатка) → в конце
  `onHand == onHand_initial − Σ списанного` и `reserved == Σ активных резервирующих строк`.

---

## §395. Создание заказа (блок F)

### §395.1 Создание магазина

`CompaniesController.Create` целиком переезжает в `Services/Companies/CompanyCreationService.cs` (по образцу
`BookingCreationService` цикла 22: те же проверки в том же порядке, те же тексты, та же транзакция и lock
`billing-account:{id}`, тот же `ConsentLedger.GrantAsync(TermsOwner, CompanyCreation)`, тот же новый токен).
Параметр `CompanyKind kind`. `POST /api/companies` вызывает его с `Services`, `POST /api/shops` — с `Orders` плюс
`SlugPolicy` и строка `ShopSettings`. Регресс ezbook ловят существующие тесты создания компании.

### §395.2 Порядок проверок `POST /api/storefront/{slug}/orders`

Точные коды и тексты каждого шага — `API_CONTRACT_CYCLE23.md` §413.1; здесь — почему порядок именно такой.

1. Модель запроса (400 строки): имя 1–100 после trim, комментарий ≤ 500, без повторов `productId`.
2. Магазин по slug: нет или салон → 404; `ShopOrderingGate` → 409 `ShopNotAcceptingOrders`.
3. Размер корзины: пусто → 409 `EmptyCart`; больше `Orders:MaxLines` (50) → 409 `TooManyLines` (JSON — фронт
   показывает их тем же баннером, что и прочие отказы оформления).
4. Идемпотентность: заказ с `(CompanyId, IdempotencyKey)` уже есть → **200** с ним (до капчи и лимитов — повтор
   не должен упираться в одноразовую капчу).
5. Покупатель:
   - `VerifiedPhoneOnly`: нет токена → 409 `LoginRequired`; подсистема подтверждения выключена и номер не подтверждён
     → 409 `PhoneVerificationUnavailable`; номер аккаунта не подтверждён (строка `VerifiedPhones` для
     `(UserId, текущий PhoneNumber)` — источник истины цикла 16, **не** зеркало `PhoneNumberConfirmed`) → 409
     `PhoneVerificationRequired`.
   - `Anyone`, вошедший: телефон = номер аккаунта (поле тела игнорируется).
   - `Anyone`, гость: капча (`CaptchaService`, как у гостевой записи), телефон обязателен,
     `PhoneNormalizer.TryNormalizeRussian`.
6. Лимит по телефону (`OrderPhoneThrottle`, в коде, по БД): активных заказов этого номера в этом магазине
   ≥ `Orders:PhoneLimits:MaxActivePerShop` (5) или созданных за 24 ч на платформе ≥ `MaxPerDay` (20) → 429 строка.
   Лимит по IP — политика `order-create` (§395.4).
7. Транзакция; при `TrackStock` — lock `shop-stock:{id}`.
8. Загрузка товаров, правила количества (`OrderQuantityRules`: штуки 1–99; граммы кратно шагу, ≥ минимума,
   ≤ 10 000), доступность (`CatalogAvailability`), остаток, цена: `expectedUnitPrice != Price` → проблема
   `PriceChanged` с `currentUnitPrice`. Любая проблема → 409 (`PriceChanged`, если все проблемы — цены, иначе
   `ItemsUnavailable`) со **всеми** проблемами сразу. Заказ **не создаётся** с другими данными.
9. Номер (§396.5), токен (`RandomNumberGenerator`, 32 байта, base64url), снимки правил и согласия (§398.3),
   `Status = New` или `Accepted` (автоприём, `AcceptedAtUtc = CreatedAtUtc`).
10. `OrderEventLog.Append(Created, toStatus)` (+ ревизия), `SaveChanges`, commit. Гонка идемпотентности (уникальный
    индекс) → перечитать и вернуть 200 с существующим.

p95 < 800 мс: ~6–8 SQL-команд, лок держится только на время шагов 7–10.

### §395.3 Корзина и подтверждение цены

- Корзина — **только на клиенте**, `localStorage` ключ `goods-cart:<slug>` (`{productId, quantity, unitPriceSeen}`),
  одна корзина на магазин.
- `POST /api/storefront/{slug}/quote` — при открытии корзины и перед кнопкой «Заказать»: построчные цены, суммы
  (`≈` для весовых), проблемы. Ничего не резервирует, всегда 200.
- Изменившаяся цена: фронт показывает новую, покупатель подтверждает, фронт повторяет запрос с новыми
  `expectedUnitPrice` **и тем же `idempotencyKey`**.
- `idempotencyKey` генерируется при открытии экрана оформления, живёт до успеха; кнопка блокируется на время запроса.

### §395.4 Частота запросов — новые политики (`RateLimits`, `[EnableRateLimiting]`)

| Политика | Где | Лимит |
|---|---|---|
| `storefront` | `GET /api/storefront/{slug}`, `POST …/quote` | 120/мин на IP |
| `order-create` | `POST …/orders` | 20/час на IP анонимно, 60/час на пользователя |
| `order-public` | `GET /api/orders/public/{token}`, `POST …/cancel` | 120/мин на IP |
| `order-board` | `GET /api/shops/{id}/order-board` | 120/мин на пользователя |

Тексты 429 — по-русски, в `OnRejected` (ветвление по политике, как у существующих). `appsettings.Testing.json`
поднимает лимиты, как для прочих политик.

---

## §396. Статусы, правка, выдача, журнал, деньги, номер

### §396.1 Статусная машина — чистая функция `Services/Orders/OrderStateMachine.cs`

| Действие | Из | В | Кто |
|---|---|---|---|
| (создание) | — | New / Accepted | покупатель; Accepted при автоприёме |
| Accept | New | Accepted | персонал |
| Reject | New | Rejected | персонал, причина ≤ 300 необязательна |
| MarkReady | Accepted | Ready | персонал |
| Issue | Ready | Issued | персонал; факт. вес каждой Weight-позиции |
| NotPickedUp | Ready | NotPickedUp | персонал |
| Cancel (магазин) | Accepted, Ready | CancelledByShop | персонал, причина необязательна |
| Cancel (покупатель) | New, Accepted | CancelledByCustomer | по ссылке; только если `AllowCustomerCancelSnapshot` |
| Edit | New, Accepted, Ready | (тот же) | персонал |

Конечные: Issued, Rejected, CancelledByCustomer, CancelledByShop, NotPickedUp. Отката нет.
`AvailableActions(status)` отдаётся в `StaffOrderCardDto.availableActions` — фронт не вычисляет доступность кнопок.
Русские подписи статусов и текст журнала — `Services/Orders/OrderTexts.cs` (сервер собирает, фронт печатает).

### §396.2 Оптимистичная блокировка персонала

Каждое действие персонала несёт `expectedVersion`. Сервис загружает заказ, сравнивает `Version`, применяет, `Version++`
и сохраняет с concurrency token; `DbUpdateConcurrencyException` или несовпадение → 409 `VersionMismatch` +
актуальный `StaffOrderDto` в теле. Недопустимый переход (заказ уже в другом статусе) → 409 `InvalidTransition` с
актуальным заказом. Отмена покупателем не несёт версию: сервер перечитывает и решает по текущему статусу;
если успел стать Ready → 409 `AlreadyReady` («Заказ уже собран, свяжитесь с магазином»); если отмена для заказа
запрещена → 409 `CancelNotAllowed`.

### §396.3 Правка заказа (US-23-24)

`PUT …/orders/{id}/items` — **полный желаемый состав**: строки с `itemId` (новое количество), строки с `productId`
(новая позиция по **текущей** цене каталога; замена = удалить старую + добавить новую). Пустой состав → 409
`LastItemCannotBeRemoved` (пустой заказ не бывает — только «Отклонить»/«Отменить»). Добавлять можно любой
неудалённый товар магазина (персонал знает, что делает; опубликованность и «закончилось» не мешают), остаток —
проверяется. Пересчёт итогов, `ReservesStock` для новых строк по текущему учёту, `IsModifiedByShop = true`, событие
`Edited` с `ChangesJson` (было → стало), `TotalBefore/After`, `Comment` (для покупателя). Сообщений покупателю в
цикле 1 нет — он видит правку на странице заказа.

### §396.4 Деньги (SPEC §6) и выдача (US-23-25)

Правило одно, нормативно в копейках (`contracts/cycle23/order-money-vectors.json`):
- штучный: `lineKop = priceKop × qty`;
- весовой: `lineKop = floor((priceKop × grams + 500) / 1000)` ≡ `Math.Round(price × grams / 1000, 2, AwayFromZero)`;
- итог — сумма строк.

Реализации ровно две: `Services/Orders/OrderMoney.cs` (сервер, источник истины) и `goods/src/utils/orderMoney.ts`
(предварительный показ в корзине до ответа `quote`). Обе проверяются одним файлом векторов. Всё, что сохраняется и
показывается как итог (корзина после `quote`, страница заказа, карточка, итог выдачи), — **числа сервера**.

Выдача: фронт показывает поле фактического веса каждой весовой позиции (заполнено заказанным), вызывает
`POST …/issue-quote` → показывает итог к оплате → `POST …/issue` с теми же количествами. Сервер сохраняет
`QuantityActual`, `LineTotalFinal`, `FinalTotal`, списывает остаток (§394.3). Фактический вес: 1…100 000 г, кратность
шагу **не** требуется (весы показывают любой вес). «Выдан» = «выдан и оплачен на месте»; оплату продукт не отмечает.

### §396.5 Номер заказа (A12)

```sql
INSERT INTO "OrderDailyCounters" ("CompanyId","BusinessDate","LastNumber") VALUES (@c, @d, 1)
ON CONFLICT ("CompanyId","BusinessDate") DO UPDATE SET "LastNumber" = "OrderDailyCounters"."LastNumber" + 1
RETURNING "LastNumber";
```

В той же транзакции, что и вставка заказа: строковая блокировка счётчика сериализует только выдачу номера; откат
транзакции откатывает и номер (дыр нет, кроме откатов после выдачи — допустимо). `BusinessDate` —
`TimeZoneInfo.ConvertTimeFromUtc(now, shop.TimeZoneId).Date`. Уникальный индекс `(CompanyId, BusinessDate, Number)` —
страховка от ошибки кода.

### §396.6 Журнал (US-23-26)

`OrderEventLog.Append(...)` — единственный писатель `OrderEvents`, вызывается в каждой точке изменения заказа в той же
транзакции **до** `SaveChanges` и тем же вызовом делает
`UPDATE "ShopSettings" SET "OrdersRevision" = "OrdersRevision" + 1 WHERE "CompanyId" = @c`.
Автор: `OrderActorResolver` (по образцу `BookingActorResolver`): гость, покупатель, сотрудник (имя снимком),
суперадмин, система. Покупателю отдаются только события с `VisibleToCustomer` и без имён сотрудников.

---

## §397. Автообновление и звук (A7, US-23-20, US-23-23)

### §397.1 Доска персонала

- `GET /api/shops/{id}/order-board?sinceRevision=&businessDate=`: читает `ShopSettings.OrdersRevision` (PK-lookup);
  совпало и сутки те же → `{changed:false, revision, businessDate, serverTimeUtc}` без массивов. Иначе — полная доска:
  активные заказы (`New`, `Accepted`, `Ready`, от старых к новым) + завершённые за текущие сутки магазина.
- Фронт: опрос каждые **5 с** (запас до SPEC «≤ 15 с» с учётом сети и дрожания), при возвращении во вкладку
  (`visibilitychange`) — немедленно. Таймер — в **dedicated Web Worker** (тики `postMessage`): таймеры воркеров не
  попадают под «интенсивное» троттлинг фоновых вкладок Chrome, в отличие от таймеров главного потока.
- Индикатор свежести: «обновлено N с назад»; если последний успешный опрос старше 30 с — красная плашка «Нет связи,
  новые заказы могут не появиться» (сбой сети или усыплённая вкладка видны, а не молчат).
- **Screen Wake Lock** (`navigator.wakeLock.request('screen')`, Chrome/Edge, Safari 16.4+) на экране заказов — планшет
  не гаснет; где API нет — подсказка «отключите автоблокировку экрана».
- Число новых — в `document.title` («(3) Заказы — Шаурма на Ленина»).
- Выделение нового заказа — визуально (рамка/подсветка до первого действия над ним или 60 с), не только звуком.

### §397.2 Нагрузка

100 магазинов × 10 экранов / 5 с = 200 rps. Пустой опрос = проверка токена (1 запрос, цикл 22) + членство (1) +
ревизия (1) — 600 простых индексных запросов/с, приемлемо для одной машины. Непустой — ~3 запроса.
Страница заказа: опрос каждые **10 с** полным `GET /api/orders/public/{token}` (одна строка по уникальному индексу +
позиции + события) — без отдельного «лёгкого» маршрута.

### §397.3 Звук

- При открытии экрана — плашка «Включить звук» (браузеры запрещают звук без жеста). Нажатие создаёт/возобновляет
  `AudioContext` и проигрывает короткий тестовый сигнал; дальше звук работает до закрытия вкладки.
- Сигнал — генерируемый (осциллятор, два тона ~0,6 с), без файлов.
- Играет при появлении в ответе доски заказа, которого не было в прошлом ответе, в колонке «Новые» **или**
  «Принятые» при автоприёме (id-множество хранится в хуке).
- Состояние звука (включён / выключен / заблокирован браузером — `AudioContext.state !== 'running'`) видно на экране
  постоянно.

---

## §398. Персональные данные (A11, US-23-27) и места под юриста

### §398.1 Выгрузка субъекта (`SubjectDataExporter`)

Новая секция `orders`: заказы аккаунта (`CustomerUserId`) и — **только при подтверждённом номере** — гостевые заказы на
`SubjectScope.GuestMatchPhone` (гейт цикла 16). Поля: магазин (название, адрес, реквизиты продавца при наличии),
номер, дата, статус, позиции, итог, имя, телефон, комментарий, причина. Журнал заказа — только события, видимые
покупателю. Магазины с заказами субъекта попадают в существующий перечень операторов выгрузки.

### §398.2 Удаление аккаунта (`AccountDeletionService`)

Правило «владеете компанией → 409» остаётся — магазин тоже компания. В той же транзакции: заказы аккаунта и
(по `SubjectScope`) гостевые заказы на подтверждённый номер → `CustomerUserId = null`, `CustomerName = null`,
`CustomerPhone = null`, `Comment = null`, `PersonalDataErased = true`; события `Customer/Guest` этих заказов →
`ActorNameSnapshot = "Удалённый пользователь"`. Номер, позиции, суммы, статус остаются — учёт магазина не ломается.
Активные заказы при этом **не отменяются**: магазин видит «данные покупателя удалены» и номер заказа.

### §398.3 Согласие и снимок версий

- Гость: сервер пишет на заказ `ConsentPrivacyVersion`, `ConsentTermsVersion`, `ConsentAcceptedAtUtc` из текущего
  снимка документов — **ровно как `BookingCreationService`** для гостевой записи (SPEC US-23-19). Записей
  `ConsentRecord` для гостя в цикле 1 нет (как у записи).
- Все заказы: `CheckoutNoticeVersion` — версия текста интерфейса `orderCheckoutNotice`, если он есть в манифесте.
  Ключ добавляется в `LegalTextKey` как **константа, но НЕ в `LegalTextKey.All`** (приём `guestDataGateNotice`
  цикла 16) — иначе fail-fast `LegalDocumentProvider` заблокирует деплой до появления текста юриста.
  Фронт получает текст существующим `GET /api/legal/texts/orderCheckoutNotice`; на 404 показывает **нейтральную**
  строку со ссылками на `/privacy` и `/terms` (без маркера «ТРЕБУЕТСЯ ТЕКСТ…» — его ловит шаг CI).

### §398.4 Отзыв согласия и обращения

Везде, где `ProfileController`/`ProfileConsentsController` (`ApplyOrPreviewRevokeEffectsAsync`, revoke-preview)
трогают гостевые записи/заметки по телефону, заказы обрабатываются тем же способом и под тем же гейтом `SubjectScope`.
Обращения субъекта (`SubjectRequest`) — без изменений: оператор (суперадмин) отвечает вручную, заказы видны ему в
выгрузке.

### §398.5 Правило уничтожения `order-personalization`

`Services/Retention/Rules/OrderPersonalizationRule.cs`, регистрация поимённо. Заказы в конечном статусе с
`CompletedAtUtc < now − Retention:OrderPersonalDataDays` → те же поля, что §398.2, + `ActorNameSnapshot` событий
покупателя. **`OrderPersonalDataDays = 0` в закоммиченном конфиге = «срок не задан», правило ничего не делает и пишет
это в сводку** (приём `BookingEventDays`). Не fail-fast. Сухой прогон — общий `DryRun: true` задачи `data-retention`.

### §398.6 Маскирование

- Серверный лог: `LoggingExtensions.MaskSensitiveRequestPath` — добавить префикс `/api/orders/public/` (токен в пути).
- nginx goods: собственный `map`/`log_format` для `/o/<token>`, `/api/orders/public/<token>` и `Referer` с `/o/` (§401.1).
- Телефон покупателя по ссылке — `PhoneDisplayMask`; полный номер — только в DTO персонала.
- Сторож `SubjectPhoneGateInvariantTests` (цикл 16) — **дописать шаблон `CustomerPhone ==`** в сканер; все новые
  сравнения по телефону несут маркер `// SUBJECT-PHONE-GATE: …`.

---

## §399. Второй фронтенд (A5, US-23-04…08)

### §399.1 Решение: второе приложение в том же npm-пакете

```
frontend/
├── src/                       ezbook — БЕЗ переноса файлов; общие модули импортируются отсюда
├── goods/                     🆕 goods.ezbook.ru
│   ├── index.html             корневой элемент <div id="root"> (на него смотрит смоук деплоя)
│   ├── public/                favicon.svg, favicon.ico, apple-touch-icon.png (своя иконка «Заказы»)
│   └── src/                   main.tsx, GoodsApp.tsx, pages/, components/, api/, hooks/, utils/
├── vite.config.ts             ezbook — без изменений
├── vite.goods.config.ts       🆕 root: goods/, outDir: ../dist-goods, publicDir: goods/public, порт SB_GOODS_WEB_PORT (5174)
├── tsconfig.json              ezbook — без изменений
├── tsconfig.goods.json        🆕 include: goods/src, src; paths @/* → src/*, @goods/* → goods/src/*
├── tailwind.config.js         ezbook — без изменений (палитра, шрифты — источник дизайн-системы)
├── tailwind.goods.config.js   🆕 presets: [ezbook config], content: goods/**, src/components/**, src/pages/{Legal…}
├── vitest.config.ts           include дополняется goods/src/**/*.test.{ts,tsx}
└── eslint.config.js           + правила no-restricted-imports (§399.4)
```

**Почему не отдельный пакет `frontend-goods/` рядом:** общий код, импортированный из `../frontend/src`, разрешал бы
`react`, `react-query`, `zustand` из **чужого** `node_modules` → две копии React в бандле (хуки ломаются), два
`package-lock.json` с дрейфом версий, два `npm ci` в CI и деплое. Вынос общего кода в workspace-пакет
(`packages/ui`) — рефакторинг импортов всего ezbook, прямой риск R23-4. Решение «одно приложение — один конфиг
сборки, пакет общий» даёт единую дизайн-систему **без единой перемещённой строки ezbook**.

### §399.2 Что goods переиспользует (без копий)

`api/client.ts` (axios, интерцепторы 401/451), `store/{authStore,legalStore,ownerGateStore}`, `queryClient`,
`components/ui/*` (Button, Input, Card, Modal, Badge, Icon, PhoneInput, CityCombobox, Pagination),
`components/legal/*` (ConsentGate, LegalUpdateBanner, OwnerTermsGateModal, **LegalGuard** — после выноса §389.3-6),
`components/phoneVerification/*` + `hooks/usePhoneVerification`, `components/booking/SmartCaptcha.tsx`,
`components/company/{CompanyMapLinks,PublicAddressNotice,CompanyAddressField}` (после цикла 19 — вместо удалённого `AddressVerifyField`), `hooks/{useOverlayDismiss,useLegalText,
useDebouncedValue,useAuthedImage}`, `utils/{money,phone,dateFormat,timezone,authError,legalError,uploadError,memberError}`,
`api/{legal,cities,phoneVerification,consents,companies}` (профиль, логотип, адрес, участники), страницы
`LegalDocumentPage`, `SubjectRequestPage`, `ConsentsPage`, `LoginPage`, `RegisterPage` (с `returnTo`, §389.3-5).

Своё у goods: `GoodsNavbar` («ezbook · Заказы»), `GoodsFooter` (ссылки на правовые страницы goods), страницы, API-модули
`api/{shops,catalog,storefront,orders}.ts`, `utils/{orderMoney,quantityFormat,orderError,catalogError,slug}.ts`, хуки
`useCart`, `useOrderBoardPolling` (+ `boardTimer.worker.ts`), `useNewOrderSound`, `useWakeLock`.
Типы — **из генерата** `src/types/api-cycle23.generated.ts` (`npm run types:api:cycle23`), руками не дублируются.

### §399.3 Маршруты goods (`GoodsApp.tsx`, сверка с `goods-routes.json`)

| Маршрут | Экран | Доступ |
|---|---|---|
| `/` | лендинг: что это, «Открыть магазин», «Войти» (US-23-08) | все |
| `/login`, `/register` | общие страницы ezbook; `returnTo` | все |
| `/:slug` | витрина + корзина (выезжающая панель) + оформление (один экран, US-23-19) | все |
| `/o/:token` | страница заказа, опрос 10 с | все |
| `/orders` | мои заказы (P1) | вошедший |
| `/profile`, `/profile/consents` | минимальный профиль (P1): имя, телефон + подтверждение через MAX, «Мои согласия», выход, ссылка «управление данными — на ezbook.ru» (`kinds-summary.services.siteUrl`) | вошедший |
| `/cabinet` | мои магазины + «Открыть магазин» + строка «Ваши салоны — на ezbook.ru» | вошедший |
| `/cabinet/new` | создание магазина (соглашение, город, адрес с предупреждением, адрес магазина с подсказкой) | вошедший |
| `/cabinet/:shopId/orders` | экран заказов (по умолчанию для сотрудника) | персонал |
| `/cabinet/:shopId/catalog` | каталог; сотруднику — только «закончилось» и остатки | персонал |
| `/cabinet/:shopId/settings` | профиль магазина, логотип, правила приёма, реквизиты [legal] | владелец |
| `/cabinet/:shopId/staff` | сотрудники (существующие `/api/companies/{id}/members`) | владелец |
| `/cabinet/:shopId/link` | адрес, «Скопировать», QR (предпросмотр/скачать), смена адреса | персонал / смена — владелец |
| `/privacy`, `/terms`, `/terms-owner`, `/pdn-consent`, `/channel-risk`, `/data-request` + редиректы `/offer-channel`, `/payment-terms` | общие страницы (Р7, US-23-06) | все |

«Открыть магазин»: невошедший → `/register?returnTo=/cabinet/new` (ссылка «уже есть аккаунт» → `/login?returnTo=…`).
Строгий режим при оформлении: невошедший → `/login?returnTo=/<slug>?checkout=1`, корзина в `localStorage` не теряется.

### §399.4 Границы импорта (ESLint `no-restricted-imports`)

- В `goods/src/**` запрещено: `@/App`, `@/pages/*` **кроме** пяти перечисленных выше общих страниц.
- В `src/**` запрещено: `@goods/*` и любые пути в `goods/`.

### §399.5 Скрипты `package.json`

`dev:goods`, `build:goods` (`tsc -p tsconfig.goods.json && vite build --config vite.goods.config.ts`),
`build:release` (`npm run build && npm run build:goods && node scripts/merge-goods-dist.mjs` — копирует `dist-goods` в
`dist/__goods`), `types:api:cycle23`. **`npm run build` не меняется.**

---

## §400. Раздельный вход (A6, Р3)

- `authStore` хранится в `localStorage` под ключом `auth-store` — хранилище **своё у каждого origin**, поэтому вход и
  выход на goods и ezbook независимы без единой строки кода. Выход на goods не трогает ezbook.
- `/api` на goods проксируется тем же nginx-сервером на тот же бэкенд → запросы same-origin, `AllowedOrigins`/CORS не
  участвуют (менять не нужно).
- JWT — те же `Issuer/Audience`; токен ezbook технически действителен и на goods, но браузер его туда не передаёт.
  Это задел под SSO, не дыра.
- Подтверждение телефона через MAX: сервер отдаёт deep-link `https://max.ru/<бот>?start=…` / QR; бот не возвращает
  пользователя на сайт, фронт **опрашивает** статус сессии (`usePhoneVerification`). `PhoneVerification:Max:PublicBaseUrl`
  — адрес **вебхука** (ezbook.ru), к домену пользователя отношения не имеет.
- Абсолютные ссылки в ответах строит только `PublicSiteLinks` (по типу компании, не по `Host` запроса).
- Web-push подписки — по origin (service worker); на goods service worker'а в цикле 1 нет (SPEC §3).
- **Внешнее условие:** ключ SmartCaptcha привязан к списку доменов в консоли Yandex Cloud — `goods.ezbook.ru` нужно
  добавить туда (задача DO-3, §403.3), иначе капча гостя на goods не отрисуется.

---

## §401. Инфраструктура: nginx, TLS, деплой, CI (US-23-04)

### §401.1 `deploy/nginx/goods.ezbook.conf` (новый файл; текст — к исполнению devops)

```nginx
# goods.ezbook.ru — второй фронтенд (ARCHITECTURE_CYCLE23.md §401). Тот же сервер 83.246.179.104, тот же API.
# Раздаётся из ТОГО ЖЕ релизного каталога, что ezbook: /var/www/ezbook/current/__goods (§401.3) —
# деплой и откат переключают оба сайта одним атомарным symlink'ом.

# Токен заказа — секрет доступа к заказу (имя, маска телефона, состав). Он в пути страницы /o/<token>, в пути API
# /api/orders/public/<token> и в Referer при переходе со страницы заказа. Маскируем все три, по уроку циклов 9/14
# (секрет в пути доезжает до access-log раньше приложения).
map $request_uri $goods_safe_uri {
    ~^(?<goods_uri_prefix>/(?:api/orders/public|o)/)[^/?]+(?<goods_uri_rest>.*)$ "${goods_uri_prefix}MASKED${goods_uri_rest}";
    default $request_uri;
}
map $http_referer $goods_safe_referer {
    ~^(?<goods_ref_prefix>https?://[^/]+/o/)[^/?#]+(?<goods_ref_rest>.*)$ "${goods_ref_prefix}MASKED${goods_ref_rest}";
    default $http_referer;
}
log_format goods_masked '$remote_addr - $remote_user [$time_local] '
                        '"$request_method $goods_safe_uri $server_protocol" '
                        '$status $body_bytes_sent "$goods_safe_referer" "$http_user_agent"';

server {
    listen 80;
    listen [::]:80;
    server_name goods.ezbook.ru;

    root /var/www/ezbook/current/__goods;
    index index.html;
    access_log /var/log/nginx/goods.access.log goods_masked;

    client_max_body_size 6M;   # фото товара — тот же лимит API 5 МБ, что у логотипа

    location /uploads/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # Вебхуки провайдеров и отписка живут на ezbook.ru (PublicBaseUrl) — на goods им делать нечего.
    # Не проксируем вовсе, чтобы секрет из их пути не лёг в goods.access.log немаскированным.
    location /api/notifications/provider-webhook/ { return 404; }
    location /api/notifications/unsubscribe/      { return 404; }
    location /api/phone-verification/max/webhook/ { return 404; }

    location /api/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # SPA: /<slug>, /o/<token>, /cabinet/... — всё на index.html. Виджета-iframe у goods нет → кадрирование запрещено везде.
    location / {
        try_files $uri $uri/ /index.html;
        add_header X-Frame-Options DENY always;
        add_header Content-Security-Policy "default-src 'self'; script-src 'self' https://smartcaptcha.yandexcloud.net; frame-src https://smartcaptcha.yandexcloud.net; connect-src 'self' https://smartcaptcha.yandexcloud.net; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; worker-src 'self' blob:; frame-ancestors 'none'" always;
        # Повторены с уровня server намеренно: add_header не наследуется, если в location есть свой (см. ezbook.conf).
        add_header Strict-Transport-Security "max-age=15552000" always;
        add_header X-Content-Type-Options "nosniff" always;
        add_header Referrer-Policy "strict-origin-when-cross-origin" always;
    }

    add_header Strict-Transport-Security "max-age=15552000" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;

    gzip on;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss text/javascript;
}
```

`worker-src 'self' blob:` — для таймера опроса в Web Worker (§397.1). В `deploy/nginx/ezbook.conf` — одна вставка:
`location ^~ /__goods/ { return 404; }` (сборка goods не должна быть доступна по `ezbook.ru/__goods/…`).

### §401.2 TLS

Отдельный сертификат: `certbot --nginx -d goods.ezbook.ru` (добавит блок 443 и редирект 80 → 443). Отдельный, а не
расширение сертификата ezbook: независимое продление, ошибка goods не трогает ezbook. A-запись уже есть
(83.246.179.104). HSTS без `preload`, как у ezbook.

### §401.3 Деплой — один релиз на оба сайта

- Раскладка релиза: `/var/www/ezbook/releases/<ts>/` = сборка ezbook (как сейчас), `/var/www/ezbook/releases/<ts>/__goods/`
  = сборка goods. Одна символическая ссылка `current` → оба сайта переключаются атомарно; `rollback.sh` откатывает оба.
- `ssh-deploy-wrapper.sh`, `rollback.sh` — **без изменений** (формат `upload-release`/`deploy` тот же: tar одного
  каталога `frontend/dist`, внутри которого теперь есть `__goods/`).
- `deploy-staging.yml`, `deploy-production.yml`: шаг сборки → `npm run build:release`. `ci.yml`, шаг артефакта
  `frontend-dist-<sha>` → собирается тем же `build:release` (локальный `deploy/deploy.sh` получает оба сайта).
- `deploy-remote.sh` — после readiness API смоук goods **через локальный nginx**:
  `curl -sf --resolve goods.ezbook.ru:443:127.0.0.1 https://goods.ezbook.ru/` содержит `<div id="root">`, и
  `curl -sf --resolve … https://goods.ezbook.ru/api/health/ready` = 200. Провал → `rollback_hint`, код 1.
  Переменная `GOODS_HOST` (дефолт `goods.ezbook.ru`), `GOODS_SMOKE=0` — аварийное отключение.
- **Порядок первого выката:** (1) vhost + сертификат на машине (руками по новому разделу `DEPLOY.md`),
  (2) SmartCaptcha — домен добавлен, (3) деплой. Иначе смоук goods по построению красный.

### §401.4 CI (`.github/workflows/ci.yml`, джоб `frontend`)

Добавляются шаги: `npx tsc --noEmit -p tsconfig.goods.json`; `npm run types:api:cycle23` + `git diff --exit-code
src/types/api-cycle23.generated.ts`; redocly lint + `../contracts/cycle23/openapi.yaml` (и строка в
`contracts/redocly.yaml`); `npm run build:release` вместо `npm run build`; смоук
`DIST_DIR=frontend/dist/__goods SMOKE_PROFILE=goods bash deploy/ci/smoke-frontend.sh` (профиль goods: `index.html`,
`favicon.ico/svg`, `apple-touch-icon.png`; **без** проверки манифеста — у goods его нет, SPEC §3). `npm run lint` и
`npm run test:run` покрывают goods автоматически (общий пакет). Джоб `backend` — без изменений (новые тесты входят в
существующие проекты). Джоб `docker-build` — без изменений (новых обязательных переменных нет, §391).
Проверка ссылок правового комплекта (`legal check` в CI): цели ссылок — те же относительные маршруты, goods их
все реализует; тест фронта `goods/src/legalRoutes.test.ts` сверяет `GoodsApp` с `contracts/cycle11/legal-routes.json`
(`documents` + `aliases` + `/data-request`).

### §401.5 Переменные и файлы

`appsettings.json`: `PublicSites`, `Orders` (`MaxProductsPerShop` 1000, `MaxCategoriesPerShop` 100, `MaxLines` 50,
`PhoneLimits: {MaxActivePerShop: 5, MaxPerDay: 20}`), `RateLimits` (четыре политики §395.4),
`Retention:OrderPersonalDataDays: 0`. `appsettings.Testing.json`: поднятые лимиты новых политик и телефонных лимитов.
`.env.dev.example`: `SB_GOODS_WEB_PORT=5174`, `PublicSites__*` для dev. Прод-`.env` — **без новых переменных**.

---

## §402. Структура проекта — что добавляется

```
ServiceBooking.Core/
├── Entities/  ShopSettings.cs, ProductCategory.cs, Product.cs, Order.cs, OrderItem.cs, OrderEvent.cs,
│              OrderDailyCounter.cs; Company.cs (+Kind)
└── Enums/     CompanyKind.cs, ShopCustomerMode.cs, OrderAcceptanceMode.cs, ProductUnit.cs, OrderStatus.cs,
               OrderEventKind.cs, OrderActorKind.cs            (все — append-only, хранятся числом)
               LegalTextKey.cs (+константа OrderCheckoutNotice, НЕ в All)

ServiceBooking.Infrastructure/
├── Data/AppDbContext.cs                   конфигурация 7 таблиц, индексы, CHECK StockOnHand >= 0
└── Migrations/  *_AddCompanyKind.cs, *_AddShopOrders.cs

ServiceBooking.API/
├── Controllers/
│   ├── ShopsController.cs                 api/shops: create, my, slug-check, {id}, settings, seller, slug, qr
│   ├── ShopCatalogController.cs           api/shops/{id}: categories, category-order, products, product-order, image, sold-out, stock
│   ├── ShopOrdersController.cs            api/shops/{id}: order-board, orders/{orderId}, accept/reject/ready/issue-quote/issue/not-picked-up/cancel, items
│   ├── StorefrontController.cs            api/storefront/{slug}: get, quote, orders
│   ├── PublicOrdersController.cs          api/orders: public/{token}, public/{token}/cancel, my
│   ├── CompaniesController.cs             Create → CompanyCreationService; kind-фильтры; kinds-summary
│   ├── AdminController.cs                 companies: kind, publicUrl
│   └── (точки CompanyKindGuard — §389.2)
├── DTOs/Shops/*.cs, DTOs/Orders/*.cs      по контракту cycle23
├── Services/
│   ├── Companies/CompanyCreationService.cs, CompanyKindGuard.cs
│   ├── PublicSites/PublicSitesOptions.cs, PublicSiteLinks.cs
│   ├── Shops/     ShopAccess.cs, ShopOrderingGate.cs (чистая), SlugPolicy.cs (чистая, читает goods-routes.json),
│   │              SlugTransliterator.cs (чистая), CatalogAvailability.cs (чистая), SellerInfoRequirements.cs (чистая),
│   │              ProductInfoRequirements.cs (чистая), ShopQrCode.cs, CatalogOrdering.cs
│   ├── Orders/    OrderMoney.cs, OrderStateMachine.cs, OrderQuantityRules.cs, OrderTexts.cs, PublicOrderToken.cs
│   │              (все чистые); OrderCreationService.cs, OrderTransitionService.cs, OrderEditService.cs,
│   │              StockLedger.cs, OrderNumberAllocator.cs, OrderEventLog.cs, OrderActorResolver.cs,
│   │              OrderPhoneThrottle.cs, OrderDtoMapper.cs
│   ├── Retention/Rules/OrderPersonalizationRule.cs
│   ├── Subjects/  SubjectDataExporter.cs, AccountDeletionService.cs (+заказы)
│   ├── ImageProcessor.cs (+ProductImage, ProductImageThumb), DeploymentSafetyChecks.cs (+ValidatePublicSites)
├── Startup/       RateLimitingExtensions (+4 политики), ApplicationServicesExtensions (регистрации),
│                  LoggingExtensions (маска /api/orders/public/)
└── appsettings.json, appsettings.Testing.json

ServiceBooking.UnitTests/   OrderMoneyTests (векторы), OrderStateMachineTests, OrderQuantityRulesTests, SlugPolicyTests
                            (goods-routes.json), SlugTransliteratorTests, CatalogAvailabilityTests, ShopOrderingGateTests,
                            PublicSiteLinksTests, DeploymentSafetyChecks (PublicSites), SubjectPhoneGate (+CustomerPhone)
ServiceBooking.Tests/       Tests/Cycle23/*: ShopsTests, CatalogTests, StorefrontTests, OrderCreationTests,
                            OrderConcurrencyTests, StockInvariantTests, OrderBoardTests, OrderTransitionsTests,
                            CompanyKindIsolationTests (матрица §389.2), OrdersSubjectDataTests

frontend/                   §399.1 (goods/, vite.goods.config.ts, tsconfig.goods.json, tailwind.goods.config.js,
                            scripts/merge-goods-dist.mjs); src/types/api-cycle23.generated.ts;
                            src/components/legal/LegalGuard.tsx; правки §389.3

contracts/cycle23/          openapi.yaml, goods-routes.json, order-money-vectors.json
deploy/nginx/goods.ezbook.conf, deploy/nginx/ezbook.conf (+__goods), deploy/deploy-remote.sh (+смоук goods),
deploy/ci/smoke-frontend.sh (+SMOKE_PROFILE), .github/workflows/{ci,deploy-staging,deploy-production}.yml
```

---

## §403. Разбивка работ и параллельность

Контракт (`openapi.yaml` + два JSON) готов **до** начала кода — frontend стартует на prism-моке в день 1.

### §403.1 Backend

| # | Задача | Зависит от | Параллельно с |
|---|---|---|---|
| BE-1 | `Company.Kind` + миграция `AddCompanyKind`; `CompanyKindGuard` и **все** точки §389.2; фильтры `/companies*`, `?kind=`, `kinds-summary`, админка; `PublicSites` + `PublicSiteLinks` + fail-fast; `CompanyDto.kind/publicUrl` | — | BE-P |
| BE-P | Чистые классы с юнит-тестами: `OrderMoney` (векторы), `OrderStateMachine`, `OrderQuantityRules`, `SlugPolicy` + `SlugTransliterator` (goods-routes.json), `CatalogAvailability`, `ShopOrderingGate`, `OrderTexts`, `PublicOrderToken` | — | всё |
| BE-M | Сущности, перечисления, `AppDbContext`, миграция `AddShopOrders` — **один разработчик, один коммит** | BE-1 (колонка Kind в снапшоте) | BE-P |
| BE-2 | `CompanyCreationService` (перенос `Create`), `POST /api/shops`, `my`, `slug-check`, `{id}`, `settings`, `seller`, `slug`, `qr`; `ShopAccess`; роль `Master`-only в members для магазина | BE-M | BE-3, BE-4a |
| BE-3 | Каталог: категории, товары, порядок, фото (`ImageProfile`), sold-out, stock (`StockLedger`, lock), витрина `GET /api/storefront/{slug}` | BE-M | BE-2, BE-4a |
| BE-4a | Заказ: `quote`, создание (идемпотентность, строгий режим, капча, лимиты, номер, токен, `OrderEventLog` + ревизия), страница по токену, отмена покупателем, «Мои заказы» | BE-M, BE-P; `StockLedger` из BE-3 (интерфейс согласовать в первый день) | BE-3 |
| BE-4b | Персонал: доска с ревизией, карточка/журнал, переходы с `expectedVersion`, правка, `issue-quote`/`issue` со списанием | BE-4a | BE-5 |
| BE-5 | ПДн: выгрузка, удаление, отзыв, `OrderPersonalizationRule`, маска пути в логе, сторож `CustomerPhone ==`; четыре политики rate limit | BE-4a | BE-4b |
| BE-6 | `API_DOCUMENTATION.md` раздел «Заказы», CHANGELOG | в конце | — |

### §403.2 Frontend

| # | Задача | Зависит от | Параллельно с |
|---|---|---|---|
| FE-0 | Каркас goods: `vite.goods.config`, `tsconfig.goods`, tailwind-пресет, `GoodsApp`, навбар/подвал, правовые маршруты, `LegalGuard` (вынос), `returnTo` в Login/Register, ESLint-границы, скрипты, генерат `api-cycle23` | — | всё |
| FE-1 | Кабинет: мои магазины, создание, настройки + предупреждение строгого режима, реквизиты [legal], сотрудники, адрес/копировать/QR/смена адреса | FE-0 | FE-2…5 |
| FE-2 | Каталог: категории, товары, фото, порядок; режим сотрудника — «закончилось» и остатки | FE-0 | |
| FE-3 | Витрина, корзина (`localStorage`), `quote`, оформление: гость + капча, вошедший, строгий режим + подтверждение MAX, подтверждение новой цены, идемпотентность | FE-0 | |
| FE-4 | Страница заказа (опрос 10 с, маска, «Магазин изменил заказ», отмена), «Мои заказы» (P1), профиль (P1) | FE-0 | |
| FE-5 | Экран заказов: доска, опрос в воркере, звук, заголовок вкладки, выделение, Wake Lock, свежесть; карточка + журнал (P1); правка; выдача с весом через `issue-quote` | FE-0 | |
| FE-6 | Правки ezbook §389.3 (1–4, 7) | BE-1 для живой проверки; по контракту — сразу | всё |

### §403.3 DevOps

| # | Задача | Когда |
|---|---|---|
| DO-1 | `deploy/nginx/goods.ezbook.conf`, вставка `__goods` в `ezbook.conf`, сертификат, проверка HTTP→HTTPS | сразу (не ждёт кода: goods-vhost можно поднять на пустой `__goods/index.html`) |
| DO-2 | CI (§401.4), `build:release` в двух deploy-workflow, `smoke-frontend.sh` профиль goods, смоук goods в `deploy-remote.sh` | после FE-0 (нужны скрипты) |
| DO-3 | SmartCaptcha — домен goods; `DEPLOY.md` новый раздел (vhost, сертификат, порядок первого выката, проверка); `.env.dev.example` | сразу |

### §403.4 QA

Кейсы `CY23-*` в `TEST_CATALOG.md`; schemathesis по `contracts/cycle23/openapi.yaml`; матрица изоляции типов
(§389.2 — каждый маршрут с id магазина → 409, каждый маршрут заказов с id/slug салона → 404); параллельный тест
последней единицы; инвариант резерва; смоук ezbook + goods после деплоя; проверка «зелёный прогон ≠ функционал»:
`grep` по именам классов §402 до объявления готовности (конвенция цикла 18).

### §403.5 Точки синхронизации BE↔FE

| Что | Где зафиксировано |
|---|---|
| Форма всех новых DTO и кодов 409 | `openapi.yaml` (генерат — единственный источник типов фронта) |
| Правило денег | `order-money-vectors.json` |
| Маршруты goods и резерв слов | `goods-routes.json` |
| Тексты ошибок-строк (400/429) | `API_CONTRACT_CYCLE23.md` §424 — фронт показывает `response.data` как есть |
| `?kind=` по умолчанию `Services` | §389.2 — фронт ezbook **ничего не шлёт** |

### §403.6 Если не укладываемся (R23-1)

Режутся P1 целиком: US-23-07 (профиль goods — вместо него ссылка на профиль ezbook), US-23-22 («Мои заказы»),
US-23-26 (журнал в карточке — журнал всё равно пишется, не показывается). P0 не режутся.

---

## §404. Места, которые ждут `legal-counsel` (§8 L1–L8) — модель не переделывается

| # | Что ждёт | Куда ляжет заключение | Цена изменения |
|---|---|---|---|
| L1 | Роли сторон; нужен ли отдельный документ для покупателя | новый член `LegalDocumentType` **в конец** (напр. `TermsBuyer`) + маршрут в `legal-routes.json`/`goods-routes.json` (резерв `terms-buyer` добавить сразу при решении) + снимок версии на `Order` (`ConsentTermsVersion` уже есть) | манифест + одна колонка при необходимости |
| L2 | Реквизиты продавца, обязательность, где показывать | поля `ShopSettings.Seller*` уже есть; обязательность — `SellerInfoRequirements` (код, как `LegalOptionGuards`); при обязательных — `ShopOrderingGate` отказывает «Заполните реквизиты продавца» (одна строка); показ — блок `seller` на витрине уже в контракте | код + тексты, без миграции |
| L3 | Пищевая информация: состав, аллергены, масса, пищевая ценность, сроки | поля **внутри** `foodInfo` (контракт снаружи не меняется) + nullable-колонки `Products` добавочной миграцией; обязательность — `ProductInfoRequirements` | добавочная миграция |
| L4 | Текст строки под кнопкой, цель обработки, передача магазину, достаточно ли модели гостевой записи | ключ `orderCheckoutNotice` в `legal.json` (`uiTexts`), затем — в `LegalTextKey.All`; `CheckoutNoticeVersion` уже пишется; при необходимости `ConsentRecord` гостя (`SubjectPhone + CompanyId` модель уже поддерживает) с новым `ConsentSource.OrderCheckout` в конец | манифест + несколько строк кода |
| L5 | Сроки хранения ПДн в заказах и журнале | `Retention:OrderPersonalDataDays` (сейчас 0 = не задан); при минимуме по закону — fail-fast в `ValidateRetentionPeriods` | конфиг |
| L6 | Достаточно ли отсылать за выгрузкой/удалением на ezbook | профиль goods уже даёт ссылку; при «недостаточно» — кнопки `export`/`delete-account` на goods (эндпоинты общие, ничего нового на бэкенде) | фронт |
| L7 | Касса и 54-ФЗ | модель ничего не фискализирует; `FinalTotal` — справочная сумма; текст «оплата на месте» — в правовых текстах | тексты |
| L8 | Правки политики/соглашения с компанией: новые цели, категории субъектов, домен goods | `legal-drafts/` → `legal build`; ссылки проверяет существующий `legal check` | тексты |

Правило цикла: тексты, которые видит покупатель, **не придумываются** командой — до заключения используются только
нейтральные fallback'и (§398.3) и уже опубликованные в черновом комплекте документы (R23-3: `isDraft: true`).

---

## §405. Риски, решения и отклонения от буквы SPEC

| # | Риск | Решение |
|---|---|---|
| R-1 (R23-4) | Регресс ezbook из-за правок «компания = салон» | `?kind=` по умолчанию `Services`; ни одного перемещённого файла ezbook; `CompanyCreationService` — дословный перенос под существующими тестами; матрица изоляции + полный прогон; смоук ezbook в деплое |
| R-2 | Перепродажа последней единицы | lock `shop-stock`, вычисляемый резерв, CHECK `≥ 0`, параллельный тест |
| R-3 | Экран заказов «уснул» — заказ не замечен (уведомлений нет до цикла 2, Q4) | таймер в воркере, Wake Lock, индикатор свежести и красная плашка при > 30 с без ответа, звук + визуальное выделение + счётчик во вкладке. Остаточный риск признан: goods после цикла 1 — стенд (Q4) |
| R-4 | Адрес магазина совпадёт с будущим маршрутом goods | резерв слов в `goods-routes.json` с запасом под циклы 2–3; тест «каждый маршрут — в резерве» |
| R-5 | Покупатели без MAX не могут заказать в строгом режиме (R23-2) | предупреждение в настройке обязательно (US-23-10); при выключенной подсистеме подтверждения включить строгий режим нельзя (409 `PhoneVerificationUnavailable`), а на витрине такого магазина вошедший без подтверждения получает понятный отказ. **На стенде подсистема выключена** (`PHONEVERIFY_PROVIDER=stub`) — строгий режим тестируется в функциональных тестах (`PhoneVerificationEnabledFactory`) |
| R-6 | Утечка ссылки на заказ | 256 бит, неперечислимо, 404 без различий, маска телефона, `order-public` rate limit, маскирование в nginx и Serilog, `Referrer-Policy strict-origin-when-cross-origin`. Токен хранится **открыто** (не хешем, в отличие от сессий MAX): «Мои заказы» и будущие уведомления цикла 2 должны его отдавать; компрометация дампа БД и так раскрывает сами заказы |
| R-7 | Капча не работает на goods | домен в консоли SmartCaptcha — DO-3, до первого выката |
| R-8 | Free-лимиты мешают тестировать (Q1): владелец салона на Free не создаст магазин (1 компания), не добавит сотрудника (1 место) | признано решением заказчика; на стенде — триал/тариф, назначенный администратором. В тестах — фабрики с тарифом |
| R-9 | JWT в `localStorage` второго origin — вторая поверхность XSS | та же CSP, что у ezbook, без inline-скриптов; ни одной новой внешней зависимости |
| R-10 | Объём цикла (R23-1) | параллельный план §403, prism-мок, P1 режутся первыми |
| R-11 | Мелкие «протечки» салонной логики в служебных местах (`SubscriptionDiagnostics`, `AdminController.stats`) | признаны: экранов у них нет или цифры корректны для «всех компаний»; разбивка — цикл 3 |
| R-12 | Первый выкат: смоук goods красный, если vhost/сертификат не готовы | порядок первого выката §401.3; `GOODS_SMOKE=0` — аварийный выход |

**Отклонения от буквы SPEC и решения архитектора сверх неё (читать обязательно):**

1. **SPEC US-23-18: «признак "сейчас не принимает заказы", если приём выключен».** Выключателя в цикле 1 нет (Q3),
   поэтому признак срабатывает только у заблокированного магазина. Поле и текст (`acceptingOrders`,
   `notAcceptingReason`) в контракте уже есть — цикл 2 наполнит его без правки фронта.
2. **Покупатель видит `availableQuantity` при нехватке остатка** — единственное место с числом; SPEC требует и
   «покупатель точного остатка не видит», и «видит, сколько не хватает» — второе конкретнее.
3. **Вошедший покупатель в режиме «Любой» заказывает только на номер аккаунта** (поле телефона игнорируется) —
   иначе строгость «номер аккаунта» обходилась бы вводом чужого номера; имя — редактируемое.
4. **Отмена покупателем — по ссылке**, без входа: «кто знает ссылку, тот видит заказ» распространено и на отмену
   (иначе гость не смог бы отменить вовсе). Защита — 256-битный токен и снимок `AllowCustomerCancel`.
5. **В магазин через API добавляется только сотрудник** (`Master`); совладельцы магазина — не в цикле 1.
6. **Единица товара неизменяема после создания** — иначе теряют смысл остаток и количества уже созданных заказов.
7. **Фактический вес при выдаче не обязан быть кратным шагу** — весы показывают любое значение.
8. **Галерея фото компании у магазина недоступна в цикле 1** (409 на запись) — SPEC её не требует; фото есть у товаров.
9. **Имена файлов документов** — `ARCHITECTURE_CYCLE23.md`/`API_CONTRACT_CYCLE23.md` по конвенции проекта (шапка).
10. **Второй фронтенд — `frontend/goods/`, а не соседний пакет `frontend-goods/`** — §399.1.
