# ARCHITECTURE — цикл 24 ServiceBooking: «Заказы», цикл 2 «Время, приём, уведомления, тарифы магазинов» (goods.ezbook.ru)

**Разделы §446–§469** (A14: после §445 — максимума в `API_CONTRACT_CYCLE20.md`; пересечений с документами циклов
19–23 нет).

**Вход:**
- `SPEC.md` цикла 24 (согласован заказчиком 2026-09-30, ответы Q-24-1…Q-24-8 в его §0);
- `CURRENT_STATE.md` на `8d4e98d` (блоки 🛒23, 🛒23+);
- код ветки `cycle/024-goods-orders-time-notify` (HEAD `a9ccd3a`);
- документы цикла 23: `ARCHITECTURE_CYCLE23.md` §386–§405, `API_CONTRACT_CYCLE23.md` §406–§425, `contracts/cycle23/`.

Ветку подготовил devops. Архитектор ветки не трогает.

**Документы цикла:**

| Файл | Что | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE24.md` (этот) | решения, модель, структура, задачи, риски | все |
| `API_CONTRACT_CYCLE24.md` (§470–§494) | контракт словами: порядок проверок, тексты, коды, изменения существующих маршрутов | backend, frontend, QA |
| `contracts/cycle24/openapi.yaml` | **источник истины по форме** (OpenAPI 3.0.3): prism-мок, `openapi-typescript`, schemathesis, redocly | backend, frontend, QA, CI |
| `contracts/cycle24/pickup-schedule-vectors.json` | эталонные векторы часов работы, слотов, «как можно скорее», паузы «до конца дня» и правила приёма (границы суток, перерывы, интервал через полночь) | backend (юнит-тесты), QA |

Корневые `ARCHITECTURE.md`/`API_CONTRACT.md` — документы цикла 3. По конвенции они не перезаписываются.
`contracts/cycle23/goods-routes.json` остаётся **единственным** источником маршрутов goods. Новые маршруты
цикла 24 дописываются в него (задача FE-0, §465). Все они лежат под `/cabinet/…`, поэтому новые слова в `reservedSlugs`
не нужны.

---

## §446. Итог решений — ответы на §7 A1–A14 одним экраном

| # | Вопрос SPEC | Решение | Раздел |
|---|---|---|---|
| A1 | Часы, особые дни, пауза, выключатель | Недельные часы — `ShopSettings.WorkingHoursJson` (jsonb, читается и пишется целиком одним `PUT`; `null` = «часы не заданы» → магазин не принимает). Особые дни — таблица `ShopSpecialDays (CompanyId, Date)`. Интервал через полночь хранится как `end ≤ start` и относится ко **дню начала**. Пауза — `ShopSettings.PausedUntilUtc`, выключатель — `ShopSettings.OrdersStopped`, «кто и когда» — снимок на той же строке. **Фоновых задач нет:** пауза снимается сама, потому что правило сравнивает `PausedUntilUtc` с «сейчас» при каждом вызове | §449 |
| — | Правило приёма (US-24-04) | Та же единственная чистая функция `ShopOrderingGate.Evaluate`, вход расширен до `ShopGateInput` (компания, настройки, расписание, тариф линейки «Заказы», счётчик месяца, «сейчас»). Входы собирает один класс `ShopGateLoader`. Q-24-4: закрытое «сейчас» блокирует только «как можно скорее» | §450 |
| A2 | Время получения на заказе | `Order.PickupKind`, `PickupDate` (день выдачи), `PickupStartUtc`, `PickupEndUtc?`. У «как можно скорее» `PickupStartUtc` = создание + время приготовления (ориентир). Слоты считает чистая `PickupSchedule`. Сервер перепроверяет выбор при создании. Индекс `(CompanyId, PickupDate, PickupStartUtc)` — выборка «всё к слоту X» для листа сборки цикла 3 | §451 |
| A3 | Номер в пределах дня выдачи | Счётчик `OrderDailyCounters` переключается с даты создания на **дату выдачи** (колонка переименовывается в `PickupDate`). Уникальный индекс `(CompanyId, PickupDate, Number)` заменяет `(CompanyId, BusinessDate, Number)`. У существующих заказов `PickupDate = BusinessDate`, продолжение нумерации не ломается. Если персонал меняет дату выдачи (US-24-09), заказ получает **новый номер** новой даты, это пишется в журнал | §451.4 |
| A4 | Доступность на дату | `Product.AvailableWeekdaysMask` (7 бит, по умолчанию 127 = все дни). Меню на дату — `ShopDailyMenus` + `ShopDailyMenuItems`. «Закончилось» со сроком — `Product.SoldOutForDate` (`null` = «до отмены», дата = «на сегодня»). Отметка «на сегодня» **гаснет вычислением**, фоновой задачи нет. Старые отметки становятся «до отмены» без переноса данных. `CatalogAvailability.Evaluate` получает дату выдачи | §452 |
| A5 | Экран заказов | Сортировка по `PickupStartUtc`. Новый массив `preorders` (принятые с датой выдачи позже сегодняшней, сгруппированы по датам). В день выдачи предзаказы переходят в колонки **без фоновой задачи**: смена `businessDate` уже делает опрос «изменённым». Дешёвый опрос по `OrdersRevision` сохраняется. Состояние приёма приходит и в «пустом» ответе, лишних запросов нет. Отдельный лёгкий `ordering-status` опрашивается раз в 60 с | §453 |
| A6 | goods как приложение | Свои `frontend/goods/public/manifest.webmanifest` (standalone) и `sw.js` (копия ezbook без `fetch` и Cache API). `PushSubscription.Site` (`CompanyKind`, по умолчанию `Services`) — подписки ezbook и goods независимы. VAPID-пара та же. Смоук goods проверяет наличие standalone-манифеста и `sw.js` вместо отсутствия манифеста. В CSP goods `worker-src 'self' blob:` уже есть | §454 |
| A7 | Push сотрудникам | Переиспользуются `StaffPushNotification` (+ `OrderId`) и `StaffPushDispatchTask`. Флаг — `CompanyNotificationSettings.StaffPushEnabled` магазина: его пишет новый магазинный маршрут, а `CompanyPushSettingsController` для магазина остаётся закрытым. Получатели — участники магазина (`IsStaffRole`) с подписками `Site = Orders`. Права перепроверяются при отправке. Период диспетчера — 10 с (`ScheduledTasks:TickSeconds = 10`) | §455 |
| A8 | Web-push покупателю без аккаунта | Новые `OrderPushSubscription` (подписка привязана к заказу, создаётся только по токену заказа) и `CustomerOrderPushNotification`, задача `customer-order-push-dispatch`. Ключи шифруются тем же `SecretProtector`. Лимиты — до 5 подписок на заказ и политика `order-push`. Срок хранения — `Retention:OrderPushSubscriptionDays` **[legal L16]** | §456 |
| A9 | Мессенджер покупателю | `OutboundNotification` (+ `OrderId`), тот же диспетчер, маршрутизация, опт-аут и журнал доставки. Тексты фиксированные — `OrderNotificationTexts`. Ссылки — только из `PublicSiteLinks`, включая ссылку отписки: платформенный `https://ezbook.ru/u/<token>`, отписка по номеру общая для платформы. `NotificationTemplateValidator.OwnDomain` не меняется, он проверяет только шаблоны салонов | §457 |
| A10 | Открыть маршруты для магазина | **Салонные контроллеры `CompanyNotifications`/`CompanyPushSettings` для магазина НЕ открываются.** Всё нужное goods даёт новый `GET/PUT /api/shops/{id}/notification-settings`, который пишет те же строки `CompanyNotificationSettings`. Открывается одно: назначение канала магазину (`POST /api/notification-channels/{id}/companies`). В матрице изоляции цикла 23 меняется одна строка | §457.3 |
| A11 | Финансирование канала магазина | Опция `notifications.whatsapp` принадлежит **аккаунту**. Номер оплачен, если опция оплачена и её допускает тариф **хотя бы одной** линейки — «Записи» или «Заказы» (вторая учитывается, только если у аккаунта есть магазин). Считается в одном месте — `SubscriptionResolver`. У аккаунтов без магазинов результат прежний | §457.4 |
| A12 | Тарифы магазинов | `SubscriptionPlanConfig.Line` (`CompanyKind`) + поля линейки «Заказы». **Отдельная таблица `OrdersSubscriptions`** (1:1 с аккаунтом); `AccountSubscriptions` = линейка «Записи» без изменений, поэтому ~30 существующих мест чтения подписки не трогаются. Бесплатный уровень — системный бесплатный тариф линейки (засевается миграцией, редактируется в админке). Месячный счётчик — `OrderMonthlyUsages (BillingAccountId, Month)`, жёсткий лимит проверяется в транзакции создания. `AccountUsageReader` считает по типу компании | §459 |
| A13 | Бот MAX персоналу (цикл 25) | Модель не мешает: получатели-сотрудники выбираются в одном месте (`OrderNotificationPlanner`). Цель «чат MAX сотрудника» ляжет рядом с «push-устройством» — отдельной таблицей привязки по образцу `PhoneVerificationSession` | §469 |
| A14 | Нумерация | §446–§469 (этот документ), §470–§494 (контракт) | — |

---

## §447. Стек: новых зависимостей — ноль

| Потребность | Чем закрываем | Почему не новое |
|---|---|---|
| Часы, слоты, пауза | чистые функции C# (`PickupSchedule`, `ShopScheduleRules`) + `TimeZoneInfo` (как в `ShopClock`) | NodaTime избыточен: в зонах РФ нет перехода на летнее время, `ShopClock.DayBoundsUtc` уже есть |
| «Сейчас» в тестах | существующий `INotificationClock` (подменяется в `NotificationDispatchTestFactory`) — единый источник «сейчас» для всего нового кода цикла | вторую абстракцию часов не вводим; `Microsoft.Extensions.TimeProvider.Testing` не нужен |
| Web-push | `IWebPushSender` (`LibWebPushSender`, пакет цикла 9), `SecretProtector`, те же VAPID-ключи | — |
| Мессенджер | транспорты GREEN-API, `NotificationRouting`, `NotificationGate`, `NotificationDispatchTask` | — |
| Быстрая доставка push | период задачи 10 с (`ScheduledTasks:TickSeconds`) | очередь или брокер (Redis, RabbitMQ) — лишняя стоимость хостинга при ≤ 100 магазинах |
| Манифест и service worker goods | статические файлы в `frontend/goods/public/` | `vite-plugin-pwa`/Workbox принесли бы кеширование, а оно запрещено (R11 цикла 9) |

**Масштаб и продажа как сервиса.** Всё новое состояние лежит в БД: пауза, счётчики месяца и номеров, очереди. В
процессе ничего не хранится, поэтому второй экземпляр API ничему не помешает. Диспетчеры уже защищены «in-flight»
меткой (`LastAttemptAtUtc`) и уникальными ключами идемпотентности: два экземпляра не отправят одно сообщение дважды.

---

## §448. Модель данных

Все сущности лежат в `ServiceBooking.Core/Entities/`, конфигурация — в `AppDbContext`. Перечисления хранятся числом и
только дописываются. **Миграция цикла одна** — `Cycle24OrdersTimeNotifyTariffs`. Её создаёт закреплённым `dotnet-ef`
8.0.11 **один разработчик одним коммитом** в начале цикла (BE-M, §465). Урок C23-4: после каждого мерджа `develop`
поверх ветки Designer-снимок миграции пересобирается, `deploy/ci/check-migration-snapshots.sh` обязан быть зелёным.

### §448.1 Изменения существующих таблиц

**`ShopSettings`.** У всех новых NOT NULL колонок **обязателен DB-default** (`HasDefaultValue`): `OrderEventLog`
делает `INSERT … ON CONFLICT` с явным списком старых колонок, и без дефолтов этот INSERT упадёт.

| Колонка | Тип | Дефолт | Смысл |
|---|---|---|---|
| `WorkingHoursJson` | jsonb? | null | недельные часы (§449.1); **null = часы не заданы** → приёма нет |
| `OrdersStopped` | bool | false | «Не принимаем, пока не включу» |
| `PausedUntilUtc` | timestamptz? | null | пауза до этого момента; момент в прошлом = паузы нет |
| `AcceptanceChangedAtUtc` | timestamptz? | null | P1 «кто и когда» |
| `AcceptanceChangedByUserId` | text? | null | |
| `AcceptanceChangedByName` | varchar(200)? | null | снимок имени; при удалении аккаунта → «Удалённый пользователь» |
| `AsapEnabled` | bool | true | US-24-05 |
| `ScheduledEnabled` | bool | false | |
| `SlotStepMinutes` | int | 15 | 15 / 30 / 60 |
| `PreorderDays` | int | 0 | 0–14 |
| `MinPrepMinutes` | int | 15 | 0–180 |
| `CustomerWebPushEnabled` | bool | true | US-24-18 |
| `CustomerMessengerEnabled` | bool | false | US-24-18 |

**`Products`:**
- `AvailableWeekdaysMask int NOT NULL DEFAULT 127` — бит 0 = понедельник … бит 6 = воскресенье (ISO). CHECK
  `"AvailableWeekdaysMask" BETWEEN 0 AND 127`. Маска 0 допустима: «ни в какой день», товар виден только в меню на дату.
- `SoldOutForDate date NULL` — для отметки «на сегодня»: день магазина, на который она поставлена.

**`Orders`:**

| Колонка | Тип | Бэкфилл существующих строк | Смысл |
|---|---|---|---|
| `PickupKind` | int NOT NULL DEFAULT 0 | `Asap` (0) | `PickupKind { Asap = 0, Slot = 1 }` |
| `PickupDate` | date NOT NULL | `= "BusinessDate"` | день выдачи — рабочий день магазина, к которому относится интервал (у интервала через полночь — день начала) |
| `PickupStartUtc` | timestamptz NOT NULL | `= "CreatedAtUtc"` | начало слота или ориентир «как можно скорее» (создание + время приготовления) |
| `PickupEndUtc` | timestamptz? | null | конец слота; у Asap null |
| `NotifyByMessenger` | bool NOT NULL DEFAULT false | false | выбор покупателя (US-24-20) |
| `MessengerConsentVersion` | varchar(32)? | null | **[legal L9]** версия текста галочки, если он есть в манифесте |
| `MessengerConsentAtUtc` | timestamptz? | null | |

Индексы:
- **удалить** уникальный `(CompanyId, BusinessDate, Number)`;
- **создать** уникальный `(CompanyId, PickupDate, Number)`;
- создать обычный `(CompanyId, PickupDate, PickupStartUtc)` — для доски и листа сборки цикла 3;
- создать обычный `(CompanyId, BusinessDate)` — для отчётов «создано за день».

Порядок в миграции: колонки добавляются nullable → бэкфилл `UPDATE` → `ALTER … SET NOT NULL` → индексы.
`BusinessDate` сохраняет смысл «день создания».

**`OrderDailyCounters`:** `RenameColumn("BusinessDate" → "PickupDate")`. Строки существующих дней остаются верными:
до цикла дата выдачи у всех заказов совпадала с датой создания.

**`PushSubscriptions`:** `Site int NOT NULL DEFAULT 0` (`CompanyKind`: `Services` = ezbook, `Orders` = goods), индекс
`(UserId, Site)`. Уникальность `Endpoint` остаётся: у разных origin разные регистрации service worker, а значит и
разные endpoint.

**`StaffPushNotifications`:** `OrderId uuid NULL` FK → Orders `SetNull`, индекс `(OrderId)`.

**`OutboundNotifications`:** `OrderId uuid NULL` FK → Orders `SetNull`, индекс `(OrderId)`. У строк заказа
`BookingId = null`, а `VisitStartUtc` означает «**крайний срок актуальности**» = момент постановки +
`Orders:CustomerMessageTtlMinutes` (120). Колонка NOT NULL и участвует в индексе диспетчера, поэтому новую не
заводим, а документируем смысл в сущности.

**`SubscriptionPlanConfigs`:**
- новые колонки: `Line int NOT NULL DEFAULT 0` (`CompanyKind`), `MaxProductsPerShop int NULL`,
  `MaxOrdersPerMonth int NULL`, `AllowOrders bool NOT NULL DEFAULT true`;
- у линейки «Заказы» существующие поля означают: `MaxCompanies` = «Макс. магазинов», `MaxEmployees` = «Макс.
  участников» (владелец считается, конвенция цикла 7);
- частичный уникальный индекс `IsSystemFree` **пересоздаётся** как `(Line) WHERE "IsSystemFree"` — по одному
  системному бесплатному тарифу на линейку;
- `IsSystemTrial` не меняется: пробный период есть только у «Записей», это проверяется в коде.

**`BillingAccounts`:** `RequestedLine int NULL` (null = «Записи»). Заявка на аккаунт по-прежнему одна (§459.5).

**`SubscriptionChangeLogs`:** `Line int NOT NULL DEFAULT 0`.

### §448.2 Новые сущности

**`ShopSpecialDay`** — PK `(CompanyId, Date)`, FK Companies `Restrict`. Поля: `IsClosed bool`,
`IntervalsJson jsonb?` (формат интервалов §449.1; null при `IsClosed`), `UpdatedAtUtc`, `UpdatedByUserId?`. Горизонт —
90 дней вперёд. Прошедшие строки не удаляются (история для цикла 3), но читаются только строки в горизонте.

**`ShopDailyMenu`** — `Id`, `CompanyId` FK `Restrict`, `Date date`, `UpdatedAtUtc`, `UpdatedByUserId?`; уникальный
индекс `(CompanyId, Date)`.
**`ShopDailyMenuItem`** — PK `(DailyMenuId, ProductId)`, FK на меню `Cascade`, FK Products `Restrict` (товары
удаляются мягко, FK не мешает). Индекс `(ProductId)`.

**`OrderPushSubscription`** — web-push покупателя, привязанный к заказу.

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | Guid, генерируется приложением **до** вставки | входит в AAD шифрования (как у `PushSubscription`) |
| `OrderId` | Guid FK Orders `Cascade` | |
| `Endpoint` | varchar(500) | |
| `P256dhCiphertext`, `AuthCiphertext` | text | `SecretProtector`, ключ `Notifications:EncryptionKey`, AAD `order-push-subscription:{Id}` |
| `KeyId` | varchar(16)? | |
| `CreatedAtUtc`, `LastSuccessAtUtc?`, `ConsecutiveFailures` | | |

Уникальный индекс `(OrderId, Endpoint)`, индекс `(CreatedAtUtc)`. Ключи не отдаются ни в одном DTO.

**`CustomerOrderPushNotification`** — очередь web-push покупателю, форма `StaffPushNotification` без `UserId`:
- `Id`, `OrderId?` FK `SetNull`, `CompanyId` FK `Restrict`, `SubscriptionId?` FK → OrderPushSubscriptions `SetNull`;
- `Type NotificationType`, `Payload varchar(1000)`, `Status`, `Reason?`, `ReasonDetail?`;
- `AttemptCount`, `LastAttemptAtUtc?`, `NextAttemptAtUtc?`, `ExpiresAtUtc` (= постановка + 2 ч), `SentAtUtc?`,
  `CreatedAt`;
- `IdempotencyKey varchar(200)` — уникальный, `{type}:{orderEventId}:{subscriptionId}`;
- частичный индекс диспетчера `(ExpiresAtUtc, CreatedAt) WHERE "Status" = 0` — копия индекса `StaffPushNotifications`.

**`OrdersSubscription`** — подписка линейки «Заказы»: `Id`, `BillingAccountId` (уникальный, FK `Cascade`),
`PlanConfigId?` (FK `SetNull`), `PaidUntil timestamptz?`, `IsActive bool`, `CreatedAtUtc`, `UpdatedAtUtc`,
`UpdatedByUserId?`.

**`OrderMonthlyUsage`** — PK `(BillingAccountId, Month date)`, где `Month` — 1-е число месяца по поясу магазина, в
котором создан заказ. Поля: `Count int`, `Warned80AtUtc?`, `Warned100AtUtc?`. ПДн нет.

### §448.3 Данные в миграции

1. Системный бесплатный тариф линейки «Заказы» с **фиксированным Guid** (константа `OrdersFreePlan.SeedId`):
   - `Name = "Заказы · Бесплатно"`, `Line = 1`, `IsSystemFree = true`, `PricePerMonth = 0`;
   - `MaxCompanies = 1`, `MaxEmployees = 2` (владелец + 1 сотрудник, §468 п. 3), `MaxProductsPerShop = 50`,
     `MaxOrdersPerMonth = 150`;
   - `AllowOrders = true`, `AllowNotificationChannel = true`, `AllowOnlineBooking = false`, `IsPublic = false`,
     `IsActive = true`.

   Числа утверждает заказчик (US-24-25), администратор меняет их без деплоя.
2. `PlanOptionRules` для этого тарифа: `INSERT … SELECT` правила `Extra` по опции с `Code = 'notifications.whatsapp'`,
   **если такая опция есть**. На чистой БД опции нет — правило не создаётся, ошибки нет.
3. `Down()` удаляет всё созданное и возвращает старый уникальный индекс номеров. Индекс не восстановится, если к
   моменту отката уже есть предзаказы с совпадающими номерами. Поэтому откат после выпуска предзаказов требует
   ручной проверки — это записано в `DEPLOY.md` (задача DO-4).

### §448.4 Инварианты (проверяются тестами)

1. Все инварианты §388.4 цикла 23 в силе.
2. Номер уникален в пределах дня выдачи: `(CompanyId, PickupDate, Number)`.
3. `PickupKind = Slot` ⇔ `PickupEndUtc != null`; `PickupStartUtc < PickupEndUtc`.
4. `OrderMonthlyUsage.Count` ≤ `MaxOrdersPerMonth` тарифа на момент каждой вставки. Гонку закрывает строковая
   блокировка счётчика (§459.4).
5. Строки `ShopSpecialDay`, `ShopDailyMenu`, `OrderPushSubscription` существуют только у компаний `Kind = Orders`.
6. У строки `OutboundNotification`/`StaffPushNotification` заполнено не больше одного из полей `BookingId`/`OrderId`.
7. Подписка `PushSubscription.Site = Services` никогда не получает строк заказов, `Site = Orders` — строк записей.

---

## §449. Время магазина: часы, особые дни, пауза, выключатель (A1, US-24-01…03)

### §449.1 Формат часов

`WorkingHoursJson` (и `IntervalsJson` особого дня) — JSON, который канонизирует сервер:

```json
{ "days": { "Monday": [ { "start": 540, "end": 780 }, { "start": 840, "end": 1260 } ],
            "Friday": [ { "start": 1080, "end": 1620 } ] } }
```

- Значения — минуты от начала того дня, в который начинается интервал. `end > 1440` — интервал через полночь (пт
  18:00–03:00 = 1080–1620).
- Нет ключа дня — выходной.
- API принимает и отдаёт `"HH:mm"` (`API_CONTRACT_CYCLE24.md` §473). Во входе `end ≤ start` означает «через полночь»,
  `"00:00"` в конце — «до полуночи».
- Правила проверяет `ShopScheduleRules.Validate` (чистая функция). Нарушение → 400 строка:
  - шаг 5 минут;
  - 0–3 интервала в дне;
  - интервалы по возрастанию, не пересекаются;
  - через полночь может переходить только последний интервал дня, длительность ≤ 24 ч;
  - «хвост» после полуночи не пересекается с первым интервалом следующего дня. С учётом особого дня следующей даты это
    проверяется при сохранении особого дня.

### §449.2 `PickupSchedule` — вычисления (чистая, `Services/Shops/PickupSchedule.cs`)

Вход: `ShopScheduleSnapshot` (зона, недельные часы, особые дни в горизонте), `PickupSettings`, `nowUtc`.

| Метод | Результат |
|---|---|
| `IntervalsFor(DateOnly day)` | интервалы рабочего дня в UTC (особый день переопределяет неделю) |
| `CurrentWorkingDay(now)` | рабочий день, чей интервал содержит `now`, с учётом вчерашнего «хвоста» через полночь; иначе — календарная дата магазина |
| `OpenState(now)` | `{ isOpen, text, closesAtUtc?, opensAtUtc? }` — «Открыто до 21:00», «Перерыв до 14:00», «Закрыто, откроемся завтра в 9:00» (правила текста — контракт §473.4) |
| `Asap(now)` | доступно ⇔ `AsapEnabled` ∧ открыто ∧ `now + prep ≤ конец текущего интервала`; `readyAtUtc = now + prep`; день выдачи = `CurrentWorkingDay` |
| `Slots(day, now, forStaff)` | сетка от начала каждого интервала с шагом `SlotStepMinutes`, слот целиком внутри интервала. Для покупателя `start ≥ now + prep`, для персонала `end > now`. Покупателю — только при `ScheduledEnabled`, персоналу — всегда |
| `Dates(now)` | при `ScheduledEnabled`: дни от `CurrentWorkingDay(now)` до сегодня + `PreorderDays`, без выходных и закрытых особых дней; у каждого `hasSlots`, `reasonText`. Без `ScheduledEnabled` — **пустой список** |
| `PauseEndOfDay(now)` | конец последнего интервала текущего рабочего дня, если он ещё впереди (в том числе в перерыве и в «хвосте» через полночь); иначе — ближайшая локальная полночь |
| `Validate(selection, now, forStaff)` | `Ok(PickupDate, StartUtc, EndUtc?)` или `Problem(code, text)` |

«Сегодня» для витрины, меню и «закончилось» — `CurrentWorkingDay(now)`. Почти всегда это календарная дата магазина
(`ShopClock.BusinessDate`). Исключение — «хвост» интервала через полночь: заказ «как можно скорее» в пт 01:00 (по
календарю уже суббота) относится к **пятнице** (US-24-01, «относится ко дню начала»). Эталоны —
`contracts/cycle24/pickup-schedule-vectors.json`, их читает юнит-тест бэкенда (как `order-money-vectors.json`).

### §449.3 Пауза и выключатель (US-24-03)

- `PUT /api/shops/{id}/acceptance` — владелец и сотрудник. Каждое изменение записывает снимок «кто/когда».

  | `mode` | Результат |
  |---|---|
  | `Accepting` | `OrdersStopped = false`, `PausedUntilUtc = null` |
  | `Paused` + `pause ∈ {Minutes15, Minutes30, Hour1, EndOfDay}` | `PausedUntilUtc = now + …` или `PauseEndOfDay(now)`, `OrdersStopped = false` |
  | `Stopped` | `OrdersStopped = true`, `PausedUntilUtc = null` |
- **Автовозобновление вычисляемое:** `PausedUntilUtc ≤ now` значит, что пауза кончилась. Фоновой задачи нет,
  требование «не позже чем через минуту» выполняется с запасом (0 с). `mode` в ответах вычисляется так же.
- Пока приём остановлен, новые заказы любого вида не создаются (Q-24-4). Уже созданные заказы и доска работают.
- **Смена приёма не трогает `OrdersRevision`.** Инвариант «ревизию пишет только `OrderEventLog`» сохраняется:
  состояние приёма приходит в каждом ответе доски, включая пустой (§453).

### §449.4 Особые дни (US-24-02, P1)

`PUT /api/shops/{id}/special-days/{date}` (владелец). Если есть активные заказы (`New/Accepted/Ready`) с
`PickupDate = date` и временем вне новых часов, а `confirmConflicts ≠ true` → 409 `ScheduleConflictsWithOrders` со
списком. Фронт показывает список и повторяет запрос с `confirmConflicts: true`. Заказы не меняются.

### §449.5 Магазины цикла 23 на стенде

После выката у них `WorkingHoursJson = null`, и они перестают принимать заказы. Этого требует SPEC US-24-01 (низкий
риск). В кабинете появляется список «Чтобы начать принимать заказы: задайте часы работы»
(`ShopManageDto.setupChecklist`).

---

## §450. Правило приёма — `ShopOrderingGate` v2 (US-24-04)

```csharp
public sealed record ShopGateInput(
    Company Company, ShopSettings Settings, ShopScheduleSnapshot Schedule,
    OrdersPlan Plan, int OrdersThisMonth, DateTime NowUtc);

public sealed record ShopGateResult(
    bool Accepting, ShopNotAcceptingCode? Code, string? ReasonText,
    AsapOption Asap, bool ScheduledAvailable, ShopOpenState OpenState, ShopAcceptanceState Acceptance);

public static ShopGateResult Evaluate(ShopGateInput input);
```

Порядок проверок (первая сработавшая даёт `Code`/`ReasonText`, тексты — контракт §473.5):

1. `Kind != Orders` или `!IsActive` → `Blocked`.
2. `WorkingHoursJson == null` → `NoWorkingHours`.
3. `OrdersStopped` → `Stopped`.
4. `PausedUntilUtc > now` → `Paused` (в тексте «до 13:30»).
5. `!Plan.AllowOrders` → `NotAllowedByPlan`.
6. `Plan.MaxOrdersPerMonth != null ∧ OrdersThisMonth ≥ лимита` → `MonthlyLimitReached`.
7. Нет ни «как можно скорее», ни одного слота в горизонте → `NoPickupTimeAvailable` (текст берётся из `OpenState`).
8. Иначе `Accepting = true`.

При 5–6 покупатель видит общий текст «Магазин временно не принимает заказы». Владельцу `ordering-status` и
`ShopManageDto` отдают точную причину.

**`ShopGateLoader`** (`Services/Shops/ShopGateLoader.cs`) — единственное место, которое собирает вход:
- строка `ShopSettings`;
- особые дни в горизонте (один запрос по диапазону);
- тариф линейки «Заказы» (`OrdersPlanResolver`);
- счётчик месяца (PK-lookup).

Всего 3–4 индексных запроса. Загрузчик вызывают витрина, `pickup-slots`, `quote`, создание заказа,
`GET /api/shops/{id}` и `ordering-status`. **Больше ни одно место не решает, принимает ли магазин.**

Цикл 23 передавал в `CatalogAvailability` флаг `shopAccepting`. Теперь это `gate.Accepting`, поэтому поведение
«товар серый, если магазин не принимает» сохраняется.

---

## §451. Время получения и номер (A2, A3, US-24-05…09)

### §451.1 Выбор покупателя

`PickupSelectionInput { kind: Asap | Slot, date?, slotStartUtc? }` передаётся в корзине (`localStorage`
`goods-cart:<slug>` получает поле `pickup`), в `quote` и при создании заказа.
- `Slot` требует `date` (рабочий день) и `slotStartUtc`, который **точно** совпадает с началом слота из `Slots(date)`.
- Нет `pickup` при создании = `Asap`, чтобы старый фронт не ломался в окне выката.

### §451.2 Перепроверка при создании (дополнение к порядку §395.2 цикла 23)

Порядок проверок (контракт §478.1):
1. Модель.
2. Магазин (404).
3. **Идемпотентность** — 200 с существующим заказом. Проверка перенесена раньше правила приёма: повтор уже
   созданного заказа во время паузы должен вернуть заказ, а не отказ.
4. Правило приёма — 409 `ShopNotAcceptingOrders` + `notAcceptingCode`.
5. Корзина пуста или слишком велика.
6. **Время** — `PickupSchedule.Validate`, 409 `PickupTimeUnavailable`. Заказ не создаётся с другим временем.
7. Строгий режим.
8. Капча и телефон гостя.
9. Лимит по телефону.
10. Транзакция:
    - lock `shop-stock` (если включён учёт остатков);
    - позиции и доступность **на `PickupDate`**;
    - **счётчик месяца** (§459.4);
    - номер на `PickupDate`;
    - снимки и `OrderEventLog` (+ ревизия + постановка уведомлений, §458);
    - commit.

Проверка времени стоит **до** капчи, чтобы одноразовый токен капчи не сгорал на отказе по времени.

### §451.3 Ориентир «как можно скорее» и сортировка

- `PickupStartUtc = CreatedAtUtc + MinPrepMinutes` (снимок настройки на момент создания).
- Доска и лист сборки сортируют по `PickupStartUtc`, при равенстве — по `CreatedAtUtc`.
- `dueUtc = PickupEndUtc ?? PickupStartUtc` — от него считается метка «просрочен».

### §451.4 Номер (A3)

`OrderNumberAllocator.NextAsync(companyId, pickupDate)`. SQL тот же, меняется только имя колонки. Уникальный индекс
`(CompanyId, PickupDate, Number)` — страховка.

**Смена времени персоналом (US-24-09, P1)** — `PUT …/orders/{id}/pickup`:
- только статусы `New/Accepted`, с `expectedVersion`;
- можно выбрать любой слот рабочего дня в горизонте с `end > now` или «как можно скорее», если магазин открыт.
  Пауза не мешает, время приготовления не учитывается;
- если меняется `PickupDate`, в той же транзакции выдаётся **новый номер** новой даты;
- событие `PickupChanged` получает `ChangesJson {before:{kind,date,start,end,number}, after:{…}}` и `Comment`;
- покупатель получает уведомление `OrderPickupChanged`. Тексты журнала и уведомления называют новый номер.

### §451.5 Лист сборки цикла 3 — выборка уже возможна

```sql
SELECT oi."ProductId", oi."NameSnapshot", SUM(oi."QuantityOrdered")
FROM "Orders" o JOIN "OrderItems" oi ON oi."OrderId" = o."Id"
WHERE o."CompanyId" = @shop AND o."PickupDate" = @d AND o."PickupStartUtc" >= @from AND o."PickupStartUtc" < @to
  AND o."Status" IN (0,1,2)
GROUP BY oi."ProductId", oi."NameSnapshot";
```

---

## §452. Доступность на дату (A4, US-24-10…13)

### §452.1 `CatalogAvailability.Evaluate` v2 — одна чистая функция

```
Evaluate(product, category, shopTracksStock, freeStock, shopAccepting, DateOnly pickupDate, DailyMenuLookup menu)
  Deleted → Unpublished → CategoryHidden
  → NotOnThisDate   ⇐ menu.Exists(pickupDate) ? !menu.Contains(pickupDate, product.Id)
                                              : !WeekdayMask.Allows(product.AvailableWeekdaysMask, pickupDate)
  → SoldOut         ⇐ product.IsSoldOut && (product.SoldOutForDate == null || product.SoldOutForDate == pickupDate)
  → InsufficientStock (как в цикле 23)
  → ShopNotAccepting
  → Available
```

- `ProductAvailability.NotOnThisDate` дописывается в перечисление.
- Витрина **не отдаёт** товары `NotOnThisDate`, остальные недоступные отдаёт серыми.
- Проблема корзины и создания — `OrderProblemReason.NotAvailableOnDate` («В этот день не продаётся»).
- Правка заказа персоналом доступность не проверяет (как в цикле 23).

### §452.2 «Закончилось» со сроком (Q-24-5)

- `PUT …/products/{id}/sold-out { isSoldOut, scope? }`:

  | Запрос | `IsSoldOut` | `SoldOutForDate` |
  |---|---|---|
  | `scope = Today` | true | сегодня магазина (`CurrentWorkingDay`) |
  | `scope = UntilCancelled` | true | null |
  | `isSoldOut = false` | false | null |

  **Нет `scope` → `UntilCancelled`** — поведение контракта цикла 23 для старого фронта. Фронт goods всегда шлёт
  `scope`, по умолчанию выбран `Today`.
- Снятие «в начале следующего дня» **вычисляемое**: отметка с `SoldOutForDate` раньше сегодняшнего дня ни на что не
  действует. В DTO она выглядит как «нет отметки» (`soldOut: null`, `isSoldOut: false`), а строка в БД остаётся до
  следующего переключения. Требование «в пределах 5 минут» выполнено, фоновой задачи нет.
- Старые отметки цикла 23 (`IsSoldOut = true`, `SoldOutForDate = null`) — это уже «до отмены», миграция данных не нужна.

### §452.3 Меню на дату (US-24-11)

- Календарь: от сегодня до сегодня + max(`PreorderDays`, 7). Запись на прошедшую дату → 400.
- `GET …/daily-menus/{date}`: если меню есть — его состав; если нет — **предзаполнение** (Q-24-6): неудалённые
  товары, у которых маска разрешает день недели этой даты. Поле `exists` различает эти два случая.
- `PUT` — полный список `productIds` (только неудалённые товары магазина, иначе 400). Создаёт или заменяет меню.
- `DELETE` возвращает дату к правилу дней недели.
- `POST …/copy` (P1) — копия состава с другой даты.
- Правят владелец и сотрудник (`ShopPermission.ManageAvailability`). Созданные заказы меню не меняет.
- Витрина читает меню одной выборкой: `ShopDailyMenus` на дату + её `ShopDailyMenuItems` (≤ 1000 строк).
  p95 < 300 мс сохраняется.

### §452.4 Дни недели товара (US-24-10)

- `ProductInput.availableWeekdays` — массив `DayOfWeek`. Не передан → все семь дней, как в цикле 23 (тело `PUT` —
  полная замена).
- Метку «пн, ср, пт» для списка собирает сервер: `ProductDto.weekdaysLabel`, `null` при всех днях.
- P1: `PUT …/categories/{id}/weekdays` проставляет маску всем неудалённым товарам категории.

---

## §453. Экран заказов (A5, US-24-07)

- `GET …/order-board?sinceRevision=&businessDate=` — правило «пустого» ответа прежнее. **В любом ответе** (и при
  `changed: false`) есть `acceptance`: он берётся из той же строки `ShopSettings`, что уже читается ради ревизии,
  лишних запросов нет.
- Полный ответ:
  - `newOrders` — все `New`, **включая предзаказы** с меткой даты;
  - `accepted` — `Accepted` с `PickupDate ≤ сегодня`;
  - `ready` — все `Ready`;
  - `preorders` — `Accepted` с `PickupDate > сегодня`, группы `{date, label, orders}` по возрастанию даты;
  - `completedToday` — как в цикле 23.

  Внутри колонок порядок — по `PickupStartUtc`, затем по `CreatedAtUtc`.
- В день выдачи предзаказ сам переходит в `accepted`: у первого опроса нового дня другой `businessDate`, поэтому
  приходит полный ответ. Фоновой задачи нет.
- `StaffOrderCardDto.pickup` содержит:
  - `text` — «Как можно скорее (≈ 13:20)», «К 12:30» или «пт 2 окт, к 12:30»;
  - `isPreorder`, `dueUtc`;
  - `isOverdue` — на момент ответа.

  Между полными ответами фронт пересчитывает просрочку сам: `serverNow > dueUtc`, где `serverNow` — `serverTimeUtc`
  плюс прошедшее время. Метка показывается текстом «Просрочен», не только цветом.
- `GET /api/shops/{id}/ordering-status` — лёгкий маршрут: итог `ShopGateLoader`, `OpenState` и лимит месяца. Доска
  опрашивает его раз в 60 с ради плашки «Закрыто / лимит исчерпан / не заданы часы».
- Нагрузка: 1000 экранов / 60 с ≈ 17 rps × 4 PK-запроса — незаметно рядом с 200 rps доски.

---

## §454. goods как приложение (A6, US-24-14)

| Что | Где | Правило |
|---|---|---|
| Манифест | `frontend/goods/public/manifest.webmanifest` | `name: "ezbook · Заказы"`, `short_name: "Заказы"`, `start_url: "/cabinet"`, `scope: "/"`, `display: "standalone"`, `theme_color`/`background_color` из палитры, `icons` 192/512 (новые `icon-192.png`, `icon-512.png` goods) |
| `index.html` goods | `frontend/goods/index.html` | `<link rel="manifest" href="/manifest.webmanifest">`, `<link rel="apple-touch-icon" …>` (файл уже есть), `meta apple-mobile-web-app-capable`, `theme-color` |
| Service worker | `frontend/goods/public/sw.js` | копия `frontend/public/sw.js` с другими значениями по умолчанию (`title: 'Новый заказ'`, `url: '/cabinet'`). **Никакого `fetch`-обработчика и Cache API** — CI-grep расширяется на этот файл |
| Регистрация | `useWebPush` (общий хук ezbook, `frontend/src/hooks/useWebPush.ts`) | новые опции `{ site: 'Services' \| 'Orders', keepBrowserSubscription?: boolean }`, по умолчанию `Services`/`false` — ezbook не меняется. goods передаёт `site: 'Orders'`, `keepBrowserSubscription: true` (§456.3) |
| nginx goods | `deploy/nginx/goods.ezbook.conf` | `location = /sw.js { add_header Cache-Control "no-cache" always; …заголовки безопасности… }` и `location = /manifest.webmanifest` с `default_type application/manifest+json` — по образцу `ezbook.conf`. CSP уже содержит `worker-src 'self' blob:`, манифест покрыт `default-src 'self'` |
| iOS | общие `utils/pushAvailability.ts` + `PushUnavailableNotice.tsx` (циклы 9, 21) | тексты и порядок причин те же; инструкция «добавьте на экран Домой» — та же |

**Что НЕ меняется на ezbook:**
- `frontend/public/sw.js`, манифест, `index.html`, `ezbook.conf`, профиль смоука ezbook;
- `GET /api/push/subscriptions` и `GET /api/push/config` по умолчанию отвечают как раньше, но **только по сайту
  `Services`**. Устройства goods в «Мои устройства» на ezbook не видны. Магазины, попавшие в `push/config` ezbook в
  цикле 23, оттуда исчезают — это исправление утечки, а не регресс.

---

## §455. Push сотрудникам магазина (A7, US-24-15, US-24-16)

- **Подписка устройства:** `POST /api/push/subscriptions` с `site: "Orders"`.
  - `PushSubscriptionWriter` считает потолок `MaxSubscriptionsPerUser` и вытесняет самую старую подписку **внутри пары
    (UserId, Site)**, чтобы устройства goods не вытесняли устройства ezbook.
  - Переназначение endpoint другому пользователю — как в цикле 9.
  - Список и удаление устройств — `GET /api/push/subscriptions?site=Orders`, `DELETE …/{id}`.
  - Раздел «Уведомления на это устройство» на goods — `/cabinet/devices`.
- **Постановка** идёт через `OrderNotificationPlanner` (§458) в транзакции события.
  - Получатели — участники магазина с ролью `Master`/`CompanyOwner`, кроме `order.CustomerUserId`. Подписки —
    `Site = Orders`.
  - Флаг — `CompanyNotificationSettings.StaffPushEnabled` магазина; если он выключен, строк ноль.
  - Типы — `StaffOrderCreated` (при ручном и автоприёме) и `StaffOrderCancelledByCustomer` (P1).
  - Ключ — `{type}:{orderEventId}:{userId}:{subscriptionId}`; `ExpiresAtUtc = now + 1 ч`.
- **Тело push** — контракт сервера с service worker (`API_CONTRACT_CYCLE24.md` §486): `{title, body, tag:
  "o-<orderId>", url: "/cabinet/<shopId>/orders?order=<orderId>"}`. Текст **без имени и телефона** покупателя
  **[legal L10, L11]**: «Новый заказ № 27 · к 12:30 · 3 позиции · ≈ 540 ₽».
- **Отправка:** `StaffPushDispatchTask` без изменения логики прав. Перед каждой строкой вызывается
  `CompanyMembership.IsStaffAsync`, поэтому удалённый из магазина сотрудник перестаёт получать push сразу. Правка одна —
  тема `o-{OrderId}`, если строка про заказ.
- **Салонная постановка** (`StaffPushScheduler`, 2 места) получает фильтр `.Where(s => s.Site == CompanyKind.Services)`.
  Без него мастер, который ещё и сотрудник магазина, получил бы push о записи в service worker goods со ссылкой
  `/my-bookings`, которой на goods нет.
- **Своевременность** (SPEC §6: поставлен не позже 5 с, доходит ≤ 30 с):
  - постановка идёт в транзакции создания (0 с);
  - `ScheduledTasks:TickSeconds` 60 → **10**; `ScheduledTasks:staff-push-dispatch:PeriodSeconds = 10`,
    `customer-order-push-dispatch:PeriodSeconds = 10`. Остальные задачи работают по своим периодам;
  - BE-3 обязан проверить, что `ScheduledTaskRunner` при коротком тике не запускает одну задачу параллельно самой себе
    (флаг выполнения в `ScheduledTaskState`). Если такой защиты нет — дописать её.

---

## §456. Web-push покупателю без аккаунта (A8, US-24-21)

### §456.1 Подписка

- `POST /api/orders/public/{token}/push-subscription { endpoint, keys: {p256dh, auth}, deviceLabel? }` — анонимно,
  политика `order-push` (20/ч на IP).
  - Неизвестный токен → 404 (не оракул).
  - Upsert по `(OrderId, Endpoint)`.
  - Больше `Orders:MaxPushSubscriptionsPerOrder` (5) → вытесняется самая старая.
  - Отказы 409 строкой: магазин выключил web-push (`CustomerWebPushEnabled = false`); заказ в конечном статусе;
    подсистема push выключена на платформе (`Notifications:StaffPush:Provider != web-push`).
- `POST /api/orders/public/{token}/push-subscription/remove { endpoint }` — 204 идемпотентно. Используется POST, а не
  DELETE с телом: тело DELETE теряют прокси и не допускают линтеры OpenAPI.
- `PublicOrderDto.notifications.webPush = { available, publicKey }`. `publicKey` — VAPID public key, это
  **единственный** анонимный путь ключа в браузер. `available = false`, если web-push выключен магазином или
  платформой либо заказ в конечном статусе.

### §456.2 Постановка и отправка

- `OrderNotificationPlanner` для событий, о которых уведомляют покупателя (§458), ставит по одной строке
  `CustomerOrderPushNotification` на каждую подписку заказа, если `CustomerWebPushEnabled`.
- `CustomerOrderPushDispatchTask` (`customer-order-push-dispatch`, 10 с) повторяет форму `StaffPushDispatchTask`:
  in-flight метка, `WebPushResponseClassifier`, при `Gone` подписка удаляется. Перед отправкой задача перепроверяет:
  - `CustomerWebPushEnabled`; если выключено — `Skipped` с новой причиной `CustomerPushDisabledByShop`;
  - наличие подписки.

  Общий код отправки можно вынести в `WebPushDelivery`. Это необязательно; если выносить — только вместе с
  регрессионными тестами салонного диспетчера.
- Тело: `{title: "<Название магазина>", body: "Заказ № 27 готов к выдаче", tag: "co-<orderId>", url: "/o/<token>"}`.
  **[legal L10]** Имени и телефона в теле нет. Токен лежит только внутри зашифрованного payload (RFC 8291) и в
  push-сервис браузера в открытом виде не попадает.

### §456.3 Один браузер — и сотрудник, и покупатель

У браузера на origin goods **одна** push-подписка (одна регистрация service worker). Если сотрудник оформил заказ
себе, его endpoint окажется в обеих таблицах — это нормально. Поэтому на goods **нельзя вызывать
`PushSubscription.unsubscribe()` в браузере**: «Отключить уведомления о заказе» и «Отключить это устройство» удаляют
только серверную строку. Для этого в `useWebPush` есть опция `keepBrowserSubscription: true` (§454).

### §456.4 Срок хранения — [legal L16]

- Правило `order-push-subscriptions`: удаляются `OrderPushSubscription` заказов, чей `CompletedAtUtc` старше
  `Retention:OrderPushSubscriptionDays` (**7**). Это технический срок: после конечного статуса данные не нужны. Юрист
  может поменять его конфигом.
- Правило `customer-order-push-notifications`: строки очереди старше `Retention:CustomerOrderPushNotificationDays`
  (**90**).

Оба правила регистрируются поимённо в `data-retention`, как остальные.

---

## §457. Мессенджер покупателю (A9–A11, US-24-18…20, US-24-22, US-24-23)

### §457.1 Постановка

`OrderMessageScheduler` (`Services/Orders/Notifications/`) вызывается из `OrderNotificationPlanner` и пишет строки
`OutboundNotification` (`OrderId`, `BookingId = null`). Сообщение ставится, если одновременно выполнено:
`order.NotifyByMessenger` ∧ `ShopSettings.CustomerMessengerEnabled` на момент события ∧ телефон есть (не обезличен).
Дальше — **тот же конвейер**, что у салонов, без копий:

- `NotificationGate.Evaluate` проверяет опт-аут номера, согласие `ProviderDelivery` у покупателя с аккаунтом,
  `PaidNotificationNumbers`, наличие канала и оплату.
  - **Одна правка в gate:** битовая маска `EnabledTypeMask` проверяется **только для типов записи**
    (`NotificationTypeCatalog.IsBookingType`). Включение сообщений о заказах определяет флаг магазина — иначе
    сохранение салонной формы с маской могло бы молча их выключить.
  - Порог `MinLeadMinutes` касается только `Reminder` и заказам не мешает («окно тишины», SPEC US-24-22).
- `NotificationRouting.SelectTargets` + `CompanyNotificationSettings.DeliveryMode/PriorityTransport` магазина.
- Ключ идемпотентности — `{type}:order:{orderEventId}:{transport}`: одно событие — одно сообщение в канал.
- `VisitStartUtc` = постановка + `Orders:CustomerMessageTtlMinutes` (120). Если сообщение не ушло за 2 часа, строка
  становится `Expired` с новой причиной `OrderMessageOutdated`: при `OrderId != null` диспетчер ставит её вместо
  `VisitAlreadyStarted`. `RefreshVisitStartTimesAsync` работает только по `BookingId` и строки заказов не трогает.
- Текст — `OrderNotificationTexts`: фиксированный, не редактируется, `NotificationTemplateValidator` не участвует.
  Пример: «{Магазин}: заказ № 27 принят, к 12:30. {orderUrl}» + строка отписки.
  - `orderUrl` = `PublicSiteLinks.OrderPageUrl(token)`.
  - Отписка — новый `PublicSiteLinks.UnsubscribeUrl(token)` = `{ServicesBaseUrl}/u/{token}`. Это та же строка, что у
    салонов: отписка по номеру общая для платформы, страница `/u/` есть только на ezbook.ru, а nginx goods её
    намеренно не проксирует.
  - Салонный `NotificationScheduler` не трогаем, у него остаётся `OwnDomain`: результат тот же, риск регресса нулевой.
- Сбой постановки не ломает действие над заказом: постановка только добавляет строки в ту же транзакцию, сетевых
  вызовов нет. Сбой отправки виден только в журнале.

### §457.2 Выбор покупателя (US-24-20) — [legal L9]

- `CreateOrderInput.notifyByMessenger`, по умолчанию false. Учитывается, только если магазин предлагает мессенджер
  (`StorefrontDto.customerNotifications.messengerOffered`); иначе молча false.
- Снимок: `MessengerConsentVersion` — версия текста `LegalTextKey.OrderMessengerConsent` из манифеста,
  `MessengerConsentAtUtc = now`. Константа добавляется **вне `LegalTextKey.All`** (как `OrderCheckoutNotice`), чтобы
  fail-fast не блокировал деплой.
- Подтверждение номера не требуется (Q-24-3).
- Если текста нет (404), фронт показывает строку из SPEC: «Присылать статус заказа в MAX/WhatsApp на номер +7 (…)».
  Галочка по умолчанию выключена.

### §457.3 Маршруты для магазина (A10)

| Что нужно goods | Как | Салонный маршрут |
|---|---|---|
| флаги web-push и мессенджера покупателям, push сотрудникам, режим доставки, приоритетный транспорт | **новый** `GET/PUT /api/shops/{id}/notification-settings` (GET — персонал, PUT — владелец) пишет `ShopSettings.Customer*` и `CompanyNotificationSettings.{StaffPushEnabled, DeliveryMode, PriorityTransport}` | `CompanyNotificationsController`, `CompanyPushSettingsController` — **остаются закрытыми** для магазина (409 цикла 23) |
| заявка на канал, QR, статус, оферта, риск, тест, замена | существующие `api/notification-channels/*`: они уровня аккаунта и требуют только «вы владелец хотя бы одной компании», магазин подходит | без изменений |
| назначить канал магазину | `POST /api/notification-channels/{id}/companies` — **убирается** `CompanyKindGuard.RejectShop`. Текст 409 «уже привязан» зависит от типа («Магазин уже привязан к другому номеру этого мессенджера») | матрица изоляции: строка удаляется |
| статус доставки в карточке (P1) | `StaffOrderCardDto.messenger` — последняя строка `OutboundNotifications` по `OrderId` | — |

- `messengerAvailable` у магазина = есть назначенный магазину канал, и он оплачен (`ChannelFundingReader`).
- `PUT … customerMessengerEnabled: true` без такого канала → 409 `MessengerUnavailable`.
- **Выключение** флага не удаляет выбор покупателей. Новые события не ставятся, а уже стоящие строки с `OrderId`
  диспетчер перепроверяет и пропускает с причиной `MessengerDisabledByShop`.

### §457.4 Финансирование (A11)

В `SubscriptionResolver.GetEffectivePlansForAccountsAsync` меняется одно — расчёт `PaidNotificationNumbers`:

```
paid = optionRow оплачена (PaidUntilUtc) ∧ (
         IsOptionCurrentlyPaid(servicesSubUsable, …, servicesPlanRule)
       ∨ (accountHasShops ∧ IsOptionCurrentlyPaid(ordersResolved.Usable, …, ordersPlanRule)) )
```

- `ordersResolved.Usable = true` и для бесплатного уровня (системного бесплатного тарифа линейки).
- Добавляется один пакетный запрос `Companies.Any(Kind = Orders)` по аккаунтам.
- **У аккаунтов без магазинов результат бит-в-бит прежний.** Это проверяют юнит-тест `Resolve` на старых данных и
  функциональный регресс салонного канала.
- Этим числом пользуются все читатели: диспетчер, `ChannelFundingReader`, настройки салона. Поэтому «номер оплачен»
  везде означает одно и то же.

`NotificationChannelsController.GetOffer/Create`: `AllowedByPlan = servicesPlan.AllowNotificationChannel ∨
(accountHasShops ∧ ordersPlan.AllowNotificationChannel)` через новый `ChannelEligibility.IsAllowedAsync(accountId)`.

---

## §458. Конвейер уведомлений о заказе — единственная точка «что произошло»

`OrderEventLog.AppendAsync` — единственный писатель журнала и ревизии — **в конце** вызывает
`OrderNotificationPlanner.OnEventAsync(order, orderEvent, ct)`. Отдельных вызовов из сервисов переходов нет, поэтому
забыть уведомление невозможно.

| Событие (`OrderEventKind`) | Персоналу (push) | Покупателю (web-push + мессенджер) | Владельцу аккаунта |
|---|---|---|---|
| `Created` | `StaffOrderCreated` | `OrderAccepted`, если `toStatus = Accepted` (автоприём) | — |
| `Accepted` | — | `OrderAccepted` | — |
| `MarkedReady` | — | `OrderReady` | — |
| `Rejected` | — | `OrderRejected` (с причиной) | — |
| `CancelledByShop` | — | `OrderCancelledByShop` (с причиной) | — |
| `CancelledByCustomer` | `StaffOrderCancelledByCustomer` (P1) | — | — |
| `Edited` | — | `OrderEditedByShop` («итог ≈ …») | — |
| `PickupChanged` (новое) | — | `OrderPickupChanged` | — |
| `Issued`, `NotPickedUp` | — | — | — |
| (счётчик месяца пересёк 80 % / 100 %) | — | — | `OwnerOrderLimitWarning` (§459.6) |

- Планировщик разделён на две части:
  - **чистая** — `OrderNotificationPlan.For(event, order, settings)`: какие типы и каким адресатам. Юнит-тест
    покрывает всю таблицу;
  - часть с БД — кто участники, какие подписки и каналы. Она только добавляет строки и `SaveChanges` не вызывает
    (конвенция `NotificationScheduler`).
- Новые члены `NotificationType` (append, биты маски 7–15): `StaffOrderCreated = 7`,
  `StaffOrderCancelledByCustomer = 8`, `OrderAccepted = 9`, `OrderReady = 10`, `OrderRejected = 11`,
  `OrderCancelledByShop = 12`, `OrderEditedByShop = 13`, `OrderPickupChanged = 14`, `OwnerOrderLimitWarning = 15`.
  - `NotificationTexts.TypeText` получает тексты для всех (юнит-тест цикла 9 перебирает все значения).
  - `DefaultTemplates.For` их **не** получает: шаблоны только салонные.
- **Обязательная правка салонного экрана:** `CompanyNotificationsController.BuildSettingsDtoAsync` строит
  `enabledTypes` из маски по **всем** значениям перечисления. Без фильтра `NotificationTypeCatalog.BookingTypes`
  салонный GET начал бы отдавать `OrderAccepted` и остальные новые типы. Нужен юнит- или функциональный тест
  «салонные `enabledTypes` не изменились».
- Новые `NotificationReason` (append): `OrderMessageOutdated`, `CustomerPushDisabledByShop`, `MessengerDisabledByShop`.
- Новые `OrderEventKind` (append): `PickupChanged = 9`. `OrderAction` (append): `ChangePickup`.

---

## §459. Тарифы магазинов (A12, блок F, Q-24-7, Q-24-8)

### §459.1 Почему отдельная таблица подписки

`AccountSubscriptions` читается напрямую примерно в 30 местах: резолвер, триал, админка, перенос, публичный каталог,
диагностика, правовые уведомления. Если добавить в эту таблицу колонку `Line` с уникальностью `(BillingAccountId,
Line)`, пришлось бы править **каждое** из этих мест — иначе любой `FirstOrDefault` брал бы случайную из двух строк.
Отдельная `OrdersSubscriptions` оставляет «Записи» бит-в-бит прежними (R24-4). Цена — второй, небольшой путь
назначения в админке и свой резолвер. Это дешевле и безопаснее ревизии 30 мест.

### §459.2 `OrdersPlanResolver` (`Services/Billing/OrdersPlanResolver.cs`)

```csharp
public sealed record OrdersPlan(
    Guid? PlanId, string PlanName, bool IsFreeTier, bool Usable,
    bool AllowOrders, int? MaxShops, int? MaxSeats, int? MaxProductsPerShop, int? MaxOrdersPerMonth,
    bool AllowNotificationChannel);

public static OrdersPlan Resolve(OrdersSubscription? sub, SubscriptionPlanConfig? systemFreeOrders, DateTime nowUtc); // чистая
public Task<Dictionary<Guid, OrdersPlan>> GetForAccountsAsync(IEnumerable<Guid> accountIds);                          // пакетно
public Task<OrdersPlan> GetForCompanyAsync(Guid companyId);
```

- Подписка годна (`IsActive` ∧ (`PaidUntil` null ∨ ≥ now) ∧ тариф `IsActive` ∧ `Line = Orders`) → действует её тариф.
- Иначе — системный бесплатный тариф линейки. Если его нет — константа `OrdersPlan.FallbackFree` с числами сида §448.3.
- Пустое значение лимита означает «без ограничения».
- `MaxProductsPerShop` дополнительно ограничен техническим потолком `Orders:MaxProductsPerShop` (1000).

### §459.3 Лимиты «внутри линейки» (Q-24-7 — меняет Q5 цикла 23)

| Лимит | Где проверяется | Чем считается |
|---|---|---|
| магазины | `CompanyCreationService` (kind = Orders) под lock `billing-account:{id}` | `AccountUsageReader.GetAsync(ids, CompanyKind.Orders).CompaniesUsed` vs `OrdersPlan.MaxShops` → 402 строка |
| салоны | `CompanyCreationService` (kind = Services) | `GetAsync(ids, Services)` vs `EffectivePlan.AccountMaxCompanies` (**магазины больше не занимают место салона**) |
| участники | `CompanyMembersController.Add` — по типу компании, в которую добавляют | `SeatsUsed` своей линейки vs `MaxSeats`/`AccountMaxEmployees` → 402 строка |
| товары | `ShopCatalogController`, создание товара | неудалённые товары магазина vs `min(MaxProductsPerShop, 1000)` → 409 `ProductLimitReached` с текстом тарифа |
| заказы в месяц | `ShopOrderingGate` (мягкий отказ заранее) + счётчик в транзакции (жёсткий) | §459.4 |
| перенос компании | `CompanyTransferService` | лимиты целевого аккаунта **той линейки**, к которой относится компания |

- `AccountUsageReader.GetAsync(ids, CompanyKind kind = CompanyKind.Services)` получает `WHERE c."Kind" = {kind}`. Все
  существующие вызовы без аргумента начинают считать только салоны; у аккаунтов без магазинов число то же.
- Превышение после смены тарифа ничего не выключает, только запрещает добавлять новое (конвенция цикла 18).
- `OverLimit*` в DTO подписки считаются по линейке.

### §459.4 Месячный счётчик — жёсткий лимит без гонки

Счётчик увеличивается в транзакции создания — **после** проверки позиций и **до** выдачи номера:

```sql
INSERT INTO "OrderMonthlyUsages" ("BillingAccountId","Month","Count") VALUES (@acc, @month, 1)
ON CONFLICT ("BillingAccountId","Month") DO UPDATE SET "Count" = "OrderMonthlyUsages"."Count" + 1
RETURNING "Count", "Warned80AtUtc", "Warned100AtUtc";
```

- `Count > MaxOrdersPerMonth` → откат всей транзакции → 409 `ShopNotAcceptingOrders` с `notAcceptingCode:
  MonthlyLimitReached`; покупатель видит «Магазин временно не принимает заказы».
- Строковая блокировка счётчика сериализует заказы **одного аккаунта** только на время короткого хвоста транзакции.
- Порядок блокировок везде один: `shop-stock:{shop}` → счётчик месяца (аккаунт) → счётчик номеров (магазин, день) →
  строка `ShopSettings` (ревизия). Взаимоблокировок нет.
- Считаются **созданные** заказы: отмена и отклонение место не возвращают.
- `Month` — первое число месяца по поясу магазина, в котором создаётся заказ.
- Смена тарифа посреди месяца сразу меняет лимит, счётчик сохраняется.
- Параллельный тест: при `limit − 1` одновременно создаются N = 20 заказов → принят ровно 1.

### §459.5 Подписка, заявки, админка

- **Владелец:** `GET /api/billing/subscription?line=Orders` отдаёт ту же форму `OwnerSubscriptionDto`, собранную для
  линейки:
  - тариф;
  - опции аккаунта — общие для обеих линеек, с пометкой в тексте;
  - `totalMonthlyPrice` = тариф линейки + опции;
  - `usage` по магазинам и местам;
  - новый блок `orders`: заказов в месяце, лимит, уровень предупреждения, лимит товаров;
  - `availablePlans` — активные тарифы линейки для заявки **[legal L14]**. Их видит только вошедший владелец,
    публичной витрины цен «Заказов» нет;
  - `trial` = null.

  Без `line` ответ прежний. `OwnerSubscriptionService.BuildAsync(account, line)` параметризуется; ветка `Services`
  сохраняет текущий код без изменений.
- **Заявка:** `POST /api/billing/subscription/request` + `line`.
  - Тариф другой линейки → 400.
  - На аккаунт по-прежнему **одна** заявка (`BillingAccount.Requested*` + `RequestedLine`). Пока ждёт заявка другой
    линейки, новая → 409 строка.
  - Опции в заявке — полный набор опций **аккаунта**. Опция допустима, если её разрешает тариф хотя бы одной линейки
    (правило §457.4).
- **Админ:**
  - `GET/POST/PUT /api/admin/plans` + `line` (задаётся только при создании; попытка сменить → 409 строка) и поля
    линейки;
  - `PUT /api/admin/plans/{id}/system-free` — внутри линейки;
  - системный триал — только у `Services`;
  - `PUT /api/admin/billing-accounts/{id}/subscription` + `line`: `Orders` пишет `OrdersSubscription`; тариф другой
    линейки → 400; `confirmLimitOverflow` считается по линейке; в журнал пишется `SubscriptionChangeLog.Line`;
  - карточка аккаунта получает блок `ordersSubscription`;
  - список заявок — поле `line`;
  - счётчики подписчиков в `AdminPlansController` (удаление и деактивация тарифа, 3 места) учитывают и
    `OrdersSubscriptions`.
- **Публичная витрина цен** (`PricingCatalogBuilder`, `GET /api/pricing`): `Where(p => p.Line == Services)`
  **[legal L14]**.

### §459.6 Предупреждения 80 % / 100 %

- Если после инкремента `Count ≥ ceil(0.8 × limit)` и `Warned80AtUtc` пусто, в той же транзакции:
  - ставится `Warned80AtUtc = now`;
  - владельцу аккаунта (`BillingAccount.OwnerUserId`) уходит push `OwnerOrderLimitWarning` на его подписки
    `Site = Orders`. `CompanyId` — магазин, в котором пересечён порог; url `/cabinet/subscription`.
- При `Count = limit` — то же с `Warned100AtUtc`.
- Кабинет: `ShopManageDto.orderLimit` и `ordering-status` отдают `{used, limit, monthLabel, warningLevel:
  None|Warning80|Reached, text}`. Плашка видна на экране заказов и в кабинете.

### §459.7 Переход стенда (US-24-28)

Магазины без `OrdersSubscription` сразу оказываются на бесплатном уровне из сида. Превышение (например, 2 магазина при
лимите 1) замораживает добавление, данные не трогаются. **[legal L14]**

---

## §460. Права (`ShopAccess`) и изоляция

| Действие | Owner | Staff | SuperAdmin | Разрешение |
|---|---|---|---|---|
| пауза / выключатель, `ordering-status` | ✓ | ✓ | ✓ | `ManageAcceptance` (новое) |
| меню на дату, «закончилось» со сроком | ✓ | ✓ | ✓ | `ManageAvailability` (новое; «закончилось» переезжает из `ManageStock`) |
| смена времени получения заказа | ✓ | ✓ | ✓ | `ManageOrders` |
| часы, особые дни, настройки времени, дни недели товаров | ✓ | — | ✓ | `ManageShop` |
| уведомления магазина (PUT), канал | ✓ | — | ✓ | `ManageShop` |
| просмотр часов и настроек уведомлений (GET) | ✓ | ✓ | ✓ | `ViewShop` |
| подписка goods (`/api/billing/*`) | владелец аккаунта | — | — | существующее правило биллинга |

Матрица изоляции цикла 23 (`CompanyKindIsolationTests`):
- удаляется строка `POST /api/notification-channels/{id}/companies`;
- добавляются обратные проверки: все новые маршруты `/api/shops/{id}/…` с id салона → 404. Маршруты
  `…/push-subscription*` салона не касаются — у записей нет токенов заказа;
- салонные `CompanyNotifications`/`CompanyPushSettings` для магазина → 409, как было.

---

## §461. Персональные данные и места под юриста

| # | Что ждёт | Заглушка цикла 24 | Куда ляжет заключение |
|---|---|---|---|
| L9 | галочка мессенджера: нужна ли, текст, умолчание; передача номера провайдеру; роли | галочка выключена по умолчанию; текст — `LegalTextKey.OrderMessengerConsent` (вне `All`), fallback — формулировка SPEC; снимок версии в заказе | текст в `legal.json` → ключ в `All`; при необходимости `ConsentRecord` с новым `ConsentSource.OrderMessenger` (append) |
| L10 | состав web-push | номер, магазин, время, число позиций, сумма — без имени и телефона | `OrderNotificationTexts` — одна правка |
| L11 | push на личных устройствах сотрудников | то же, что L10 | то же |
| L12 | оферта канала для магазинов | существующая оферта (`TermsOwner`, `ChannelOffer`) принимается при заявке, как у салона | тексты `legal-drafts/` |
| L13 | строка про предзаказ рядом со временем | **строки нет**; место под `LegalTextKey.OrderPreorderNotice` (вне `All`): фронт показывает текст, только если `GET /api/legal/texts/OrderPreorderNotice` = 200 | текст в манифест |
| L14 | условия тарифов, остановка приёма по лимиту, гейт витрины цен | тарифы «Заказов» не в публичном каталоге; `availablePlans` только в кабинете; покупателю — общий текст «временно не принимает» | тексты + флаг показа витрины |
| L15 | бот MAX сотрудникам (цикл 25) | — | — |
| L16 | сроки: push-подписки заказа, журнал доставки, выбор канала | подписки — 7 дней после конечного статуса; очередь customer push — 90 дней; журнал доставки — **существующие** правила `OutboundNotification` (BE-6 проверяет, что они не фильтруют по `BookingId`); выбор канала — часть заказа, живёт по `order-personalization` (сейчас 0 = не удаляется) | конфиг `Retention:*` |

**Выгрузка субъекта** (`SubjectDataExporter`, секция `orders`) получает:
- `pickup` (вид, дата, время);
- `notifyByMessenger`;
- `messengerConsentVersion`/`At`;
- `webPushSubscriptions` (число и даты создания, без endpoint).

Строки журнала доставки по заказам попадают в существующую секцию уведомлений (фильтр проверить).

**Удаление аккаунта:** у обезличиваемых заказов удаляются `OrderPushSubscriptions`, `NotifyByMessenger = false`.
Снимок согласия остаётся — правовые данные не удаляются. `ShopSettings.AcceptanceChangedByName` удаляемого сотрудника
→ «Удалённый пользователь».

Сторож `SubjectPhoneGateInvariantTests`: новые сравнения по телефону получают маркер `// SUBJECT-PHONE-GATE:`.

---

## §462. Фронтенд

### §462.1 goods (`frontend/goods/`)

| Экран / модуль | Что добавляется |
|---|---|
| `/:slug` витрина | блок «Когда заберёте»: «как можно скорее (≈ к 13:20)» / дата → слоты (`GET …/pickup-slots`); состояние «Открыто до 21:00»; часы работы; `?date` меняет ассортимент; плашка «не принимает» с причиной; клавиатура и читалка экрана: радиогруппы, `aria-live` для смены ассортимента |
| корзина / оформление | `pickup` в корзине; `quote` с `pickup` → строки `NotAvailableOnDate` выделены с причиной, «Оформить» заблокировано; галочка мессенджера (если `messengerOffered`); ветки `PickupTimeUnavailable` (выбрать заново) и `ShopNotAcceptingOrders` (текст с сервера) |
| `/o/:token` | время получения; кнопка «Уведомлять о статусе в этом браузере» (разрешение браузера — только по нажатию; на iOS без экрана «Домой» — объяснение и совет выбрать мессенджер); «Отключить» |
| `/orders` | время получения в списке |
| `/cabinet/:shopId/orders` | время на карточке, сортировка, «Предзаказы» по датам, «Просрочен», панель приёма (кнопки ≥ 44×44, оставшееся время, «Возобновить», «кто и когда» — P1), плашка `ordering-status` (опрос 60 с), «Изменить время» (P1), статус доставки в мессенджер (P1), быстрые «нет на сегодня / до отмены» |
| `/cabinet/:shopId/hours` 🆕 | недельные часы (1–3 интервала, шаг 5 мин, «через полночь»), особые дни (P1, с подтверждением конфликтов), настройки времени получения, чек-лист «Чтобы начать принимать заказы» |
| `/cabinet/:shopId/menu` 🆕 | календарь дат, меню на дату (предзаполнено, переключатели), «Удалить меню», «Скопировать с даты» (P1) |
| `/cabinet/:shopId/catalog` | дни недели в карточке товара, метка `weekdaysLabel`, «применить ко всей категории» (P1), диалог срока «закончилось» |
| `/cabinet/:shopId/notifications` 🆕 | web-push и мессенджер покупателям, push сотрудникам, режим доставки; подключение номера — **общие компоненты каналов ezbook** (`ChannelRequestModal` и др.) через `api/notification-channels` с назначением на магазин |
| `/cabinet/devices` 🆕 | «Уведомления на это устройство»: включить, список устройств (`site=Orders`), отключить любое; объяснение недоступности (`PushUnavailableNotice`), инструкция для iOS |
| `/cabinet/subscription` 🆕 | «Ваша подписка» — общий компонент экрана ezbook с `line="Orders"`: тариф, опции, итог, «заказов в этом месяце: 120 из 150», заявка из `availablePlans` |
| кабинет, общее | плашки лимита 80/100 %; тексты 402/409 лимитов — как есть с сервера |

- Типы — только из генерата `src/types/api-cycle24.generated.ts` (`npm run types:api:cycle24`). Для изменённых DTO
  goods переходит на типы cycle24, генерат cycle23 остаётся для неизменённых.
- В `contracts/cycle23/goods-routes.json` → `spaRoutes` добавляются `/cabinet/:shopId/hours`, `/cabinet/:shopId/menu`,
  `/cabinet/:shopId/notifications`, `/cabinet/devices`, `/cabinet/subscription`. Первый сегмент `cabinet` уже в резерве.

### §462.2 ezbook (`frontend/src/`) — весь перечень

1. `hooks/useWebPush.ts` — опции `site`, `keepBrowserSubscription` (по умолчанию прежнее поведение); `api/push` —
   параметр `site`.
2. Экран подписки (`BillingPage` и его части) — проп `line` (по умолчанию `Services`), чтобы goods переиспользовал
   компонент. Без пропа рендер прежний.
3. Админка:
   - вкладка тарифов — линейка (фильтр, выбор при создании), поля «Макс. товаров в магазине», «Заказов в месяц на
     аккаунт», «Приём заказов»;
   - «системный бесплатный» — внутри линейки;
   - карточка аккаунта — блок «Подписка «Заказы»» и назначение с `line`;
   - список заявок — колонка «Линейка».
4. Больше на ezbook ничего не меняется. Салон не видит ни одного нового элемента.

---

## §463. Инфраструктура, CI, деплой, конфигурация

### §463.1 nginx goods (`deploy/nginx/goods.ezbook.conf`)

```nginx
    # goods как приложение (ARCHITECTURE_CYCLE24.md §454): service worker без кеша браузера — иначе старый
    # обработчик push переживёт деплой; манифест — с правильным типом для iOS/Android.
    location = /sw.js {
        add_header Cache-Control "no-cache" always;
        add_header Strict-Transport-Security "max-age=15552000" always;
        add_header X-Content-Type-Options "nosniff" always;
        add_header Referrer-Policy "strict-origin-when-cross-origin" always;
    }
    location = /manifest.webmanifest {
        default_type application/manifest+json;
        add_header Cache-Control "no-cache" always;
        add_header Strict-Transport-Security "max-age=15552000" always;
        add_header X-Content-Type-Options "nosniff" always;
        add_header Referrer-Policy "strict-origin-when-cross-origin" always;
    }
```

CSP в `location /` не меняется: `worker-src 'self' blob:` уже есть. На боевой машине vhost goods ведётся руками
(🛒23+), поэтому правку нужно применить и там, а в `DEPLOY.md` §22 зафиксировать сверку серверного файла с
репозиторием.

### §463.2 CI (`.github/workflows/ci.yml`)

1. Шаг grep service worker: к `public/sw.js` добавляется `goods/public/sw.js` (тот же запрет `fetch`/`caches`/`CacheStorage`).
2. `smoke-frontend.sh`, профиль `goods`: **вместо** проверки «манифеста нет» — те же проверки, что у ezbook:
   - манифест отдаётся, парсится, `display` — standalone, есть `start_url`/`scope`/`icons`;
   - `index.html` ссылается на манифест и `apple-touch-icon`;
   - `GET /sw.js` = 200.

   Комментарий в шапке скрипта обновляется.
3. `npm run types:api:cycle24` + `git diff --exit-code src/types/api-cycle24.generated.ts`.
4. redocly lint: `../contracts/cycle24/openapi.yaml` добавляется в шаг CI и в список в `contracts/redocly.yaml`.
5. `bash deploy/ci/check-migration-snapshots.sh` уже есть. Новая миграция должна проходить его зелёной.

### §463.3 Деплой (`deploy/deploy-remote.sh`)

Смоук goods после готовности API дополняется двумя проверками:
- `https://$GOODS_HOST/sw.js` = 200 **и** заголовок `Cache-Control` содержит `no-cache`;
- `https://$GOODS_HOST/manifest.webmanifest` = 200 и `"display": "standalone"`.

Провал → `rollback_hint`, код 1. Аварийное отключение — тот же `GOODS_SMOKE=0`.

### §463.4 Конфигурация (`appsettings.json`, закоммичено — новых переменных окружения нет)

```json
"Orders": { "CustomerMessageTtlMinutes": 120, "MaxPushSubscriptionsPerOrder": 5,
            "SpecialDaysHorizonDays": 90, "DailyMenuMinHorizonDays": 7 },
"Retention": { "OrderPushSubscriptionDays": 7, "CustomerOrderPushNotificationDays": 90 },
"ScheduledTasks": { "TickSeconds": 10,
                    "staff-push-dispatch": { "PeriodSeconds": 10 },
                    "customer-order-push-dispatch": { "Enabled": true, "MaxRunMinutes": 2, "PeriodSeconds": 10 } },
"RateLimits": { "order-push": { "PermitLimit": 20, "WindowMinutes": 60 } }
```

- `appsettings.Testing.json` поднимает лимит `order-push`.
- VAPID-ключи и `Notifications:StaffPush:Provider` — прежние переменные.
- **На стенде push выключен** (🚀20 п. 7). Включение — отдельное решение заказчика (R24-2). Пока push выключен,
  кнопки push на goods показывают «Уведомления на устройство пока не включены на платформе».

---

## §464. Структура проекта — что добавляется

```
ServiceBooking.Core/
├── Entities/  ShopSpecialDay.cs, ShopDailyMenu.cs, ShopDailyMenuItem.cs, OrderPushSubscription.cs,
│              CustomerOrderPushNotification.cs, OrdersSubscription.cs, OrderMonthlyUsage.cs;
│              ShopSettings, Product, Order, PushSubscription, StaffPushNotification, OutboundNotification,
│              SubscriptionPlanConfig, BillingAccount, SubscriptionChangeLog (+колонки §448.1)
└── Enums/     PickupKind.cs; NotificationType, NotificationReason, OrderEventKind, OrderAction,
               OrderProblemReason (+члены в конец); LegalTextKey (+OrderMessengerConsent, OrderPreorderNotice — вне All)

ServiceBooking.Infrastructure/Migrations/  *_Cycle24OrdersTimeNotifyTariffs.cs (+ Designer)

ServiceBooking.API/
├── Controllers/
│   ├── ShopScheduleController.cs      🆕 api/shops/{id}: working-hours, special-days, pickup-settings, acceptance, ordering-status, pickup-slots
│   ├── ShopMenuController.cs          🆕 api/shops/{id}/daily-menus…, categories/{id}/weekdays
│   ├── ShopNotificationsController.cs 🆕 api/shops/{id}/notification-settings
│   ├── ShopCatalogController.cs       sold-out scope, weekdays, лимит тарифа
│   ├── ShopOrdersController.cs        board (preorders, acceptance), orders/{id}/pickup
│   ├── StorefrontController.cs        ?date, pickup-slots, quote/create с pickup и мессенджером
│   ├── PublicOrdersController.cs      push-subscription, push-subscription/remove; pickup/notifications в DTO
│   ├── PushController.cs              site
│   ├── NotificationChannelsController.cs  назначение магазину; ChannelEligibility
│   ├── CompanyNotificationsController.cs  enabledTypes — только типы записи
│   ├── BillingController.cs, AdminBillingController.cs, AdminPlansController.cs, CompanyMembersController.cs  line
├── Services/
│   ├── Shops/     PickupSchedule.cs, ShopScheduleRules.cs, WeekdayMask.cs (чистые); ShopOrderingGate.cs (v2, чистая);
│   │              ShopGateLoader.cs; CatalogAvailability.cs (v2); ShopAccess.cs (+разрешения); DailyMenuService.cs
│   ├── Orders/    OrderNumberAllocator (PickupDate), OrderCreationService, OrderEditService (+pickup), OrderEventLog (+planner),
│   │              OrderMonthlyCounter.cs 🆕, OrderTexts (+тексты времени)
│   ├── Orders/Notifications/  OrderNotificationPlan.cs (чистая), OrderNotificationPlanner.cs, OrderMessageScheduler.cs,
│   │                          OrderNotificationTexts.cs (чистая), OrderStaffPushQueue.cs, CustomerOrderPushQueue.cs
│   ├── Notifications/  NotificationTypeCatalog.cs 🆕 (IsBookingType/IsOrderType), NotificationGate (маска — только запись),
│   │                   PushSubscriptionWriter (site), StaffPushScheduler (фильтр Site), OrderPushSubscriptionWriter.cs 🆕
│   ├── Billing/   OrdersPlanResolver.cs 🆕, ChannelEligibility.cs 🆕, AccountUsageReader (kind), OwnerSubscriptionService (line),
│   │              BillingTexts (+тексты линейки), PricingCatalogBuilder (фильтр Line)
│   ├── SubscriptionResolver.cs         PaidNotificationNumbers по двум линейкам (§457.4)
│   ├── PublicSites/PublicSiteLinks.cs  +UnsubscribeUrl
│   ├── Scheduling/Tasks/  CustomerOrderPushDispatchTask.cs 🆕; StaffPushDispatchTask (тема o-), NotificationDispatchTask (причина для заказов)
│   └── Retention/Rules/   OrderPushSubscriptionRule.cs 🆕, CustomerOrderPushNotificationRule.cs 🆕
└── appsettings*.json

ServiceBooking.UnitTests/  PickupScheduleVectorsTests (pickup-schedule-vectors.json), ShopScheduleRulesTests, ShopOrderingGateV2Tests,
                           CatalogAvailabilityDateTests, WeekdayMaskTests, OrderNotificationPlanTests, OrderNotificationTextsTests,
                           OrdersPlanResolverTests, SubscriptionResolverPaidNumbersTests (регресс «без магазинов = прежнее»),
                           NotificationGateOrderTypesTests, NotificationTypeCatalogTests
ServiceBooking.Tests/      Tests/Cycle24{Schedule,Pickup,Availability,StaffPush,CustomerNotifications,Tariffs,Isolation}Tests.cs,
                           Infrastructure/Cycle24TestBase.cs (подменяемый INotificationClock)

frontend/goods/public/     manifest.webmanifest, sw.js, icon-192.png, icon-512.png
frontend/goods/src/        pages/cabinet/{HoursPage,MenuPage,ShopNotificationsPage,DevicesPage,SubscriptionPage}.tsx,
                           components/pickup/*, components/acceptance/*, api/{schedule,menu,shopNotifications,orderPush}.ts,
                           hooks/useOrderingStatus.ts, utils/{pickupText,weekdays}.ts
frontend/src/              hooks/useWebPush.ts (опции), api/push.ts (site), BillingPage (line), админка тарифов/аккаунтов,
                           types/api-cycle24.generated.ts
contracts/cycle24/         openapi.yaml, pickup-schedule-vectors.json
contracts/cycle23/goods-routes.json   +5 spaRoutes
deploy/nginx/goods.ezbook.conf, deploy/ci/smoke-frontend.sh, deploy/deploy-remote.sh, .github/workflows/ci.yml,
contracts/redocly.yaml, DEPLOY.md §22
```

---

## §465. Разбивка работ и параллельность

Контракт (`openapi.yaml` + векторы) готов **до** кода: фронт стартует в первый же день на
`npx @stoplight/prism mock contracts/cycle24/openapi.yaml --port 4024`.

### §465.1 Backend

| # | Задача | Зависит от | Параллельно с |
|---|---|---|---|
| **BE-M** | Все сущности, перечисления (append), `AppDbContext`, **одна** миграция §448 с бэкфиллом, сменой индексов и сидом тарифа. **Один разработчик, один коммит, первым.** После — `check-migration-snapshots.sh` | — | BE-P |
| **BE-P** | Чистые классы и юнит-тесты: `ShopScheduleRules`, `PickupSchedule` (+ векторы), `ShopOrderingGate` v2 (сигнатура §450 **фиксируется в первый день**), `WeekdayMask`, `CatalogAvailability` v2, `OrderNotificationPlan`, `OrderNotificationTexts`, `OrdersPlanResolver.Resolve`, `NotificationTypeCatalog` | — | всё |
| **BE-1** Время | `ShopGateLoader`; часы, особые дни (P1), настройки времени, пауза и выключатель, `ordering-status`, `pickup-slots` (витрина и персонал); витрина `?date`; `quote` и создание с `pickup` (новый порядок §451.2); номер по `PickupDate`; доска (preorders, сортировка, acceptance); `pickup` в DTO покупателя и персонала; смена времени персоналом (P1) | BE-M, BE-P | BE-2…BE-5 |
| **BE-2** Доступность | дни недели (+ категория, P1), меню на дату (предзаполнение, копия — P1), «закончилось» со сроком; доступность на дату в витрине, `quote` и создании | BE-M, BE-P (`CatalogAvailability` v2) | BE-1, BE-3…5 |
| **BE-3** Push | `PushSubscription.Site` в `PushController`, writer и `StaffPushScheduler`; `OrderNotificationPlanner` + хук в `OrderEventLog`; push сотрудникам; API `OrderPushSubscription`; `CustomerOrderPushDispatchTask`; тема `o-`; тик 10 с и проверка непараллельности задач; правила ретенции | BE-M, BE-P | BE-1, BE-2, BE-4 (планировщик — общий файл: владеет BE-3, BE-4 добавляет цель «мессенджер» после его мерджа) |
| **BE-4** Мессенджер | `OrderMessageScheduler`; правка `NotificationGate` (маска); `NotificationTypeCatalog` + фильтр салонных `enabledTypes`; причины диспетчера; `ShopNotificationsController`; назначение канала магазину; `ChannelEligibility`; `SubscriptionResolver` §457.4; `PublicSiteLinks.UnsubscribeUrl`; согласие L9; статус доставки в карточке (P1) | BE-M; планировщик BE-3; `OrdersPlanResolver` BE-P | BE-1, BE-2, BE-5 |
| **BE-5** Тарифы | линейка тарифа в админке и `system-free`; `OrdersSubscription` и назначение с `line`; заявка с `line`; `OwnerSubscriptionService(line)`; `AccountUsageReader(kind)`; лимиты магазинов, мест, товаров, переноса; месячный счётчик + предупреждения; тариф и счётчик во входе `ShopGateLoader` (интерфейс §450); фильтр публичного каталога; счётчики подписчиков тарифа | BE-M, BE-P | BE-1…BE-4 |
| **BE-6** ПДн и документы | выгрузка, удаление, ретенция (§461), `API_DOCUMENTATION.md` §4.20 (дополнение), `CHANGELOG` — в конце | BE-1…5 | — |

### §465.2 Frontend

| # | Задача | Зависит от | Параллельно с |
|---|---|---|---|
| **FE-0** | генерат `api-cycle24` + скрипт `types:api:cycle24`; манифест, `sw.js`, иконки, `index.html` goods; маршруты в `goods-routes.json`; опции `useWebPush`/`api/push` (`site`, `keepBrowserSubscription`) | — | всё |
| **FE-1** | часы, особые дни (P1), настройки времени, чек-лист; панель приёма (экран заказов + настройки) | FE-0 | FE-2…8 |
| **FE-2** | каталог: дни недели, метка, категория (P1), срок «закончилось»; страница «Меню на дату» | FE-0 | |
| **FE-3** | витрина: выбор даты и слота, открыто/закрыто, ассортимент на дату, корзина с `pickup`, оформление (ветки отказов, галочка мессенджера), «Мои заказы» | FE-0 | |
| **FE-4** | страница заказа: время получения, web-push покупателя (включить/отключить, iOS) | FE-0 | |
| **FE-5** | экран заказов: время, сортировка, «Предзаказы», «Просрочен», `ordering-status`, «Изменить время» (P1), статус мессенджера (P1); `/cabinet/devices` | FE-0 | |
| **FE-6** | `/cabinet/:shopId/notifications`: флаги + подключение канала (общие компоненты ezbook) | FE-0 | |
| **FE-7** | `/cabinet/subscription` (общий экран с `line`), плашки лимитов | FE-8 п. 2 (проп `line`) — или сразу своей обёрткой | |
| **FE-8** | ezbook: проп `line` у экрана подписки; админка тарифов, аккаунтов, заявок (§462.2) | FE-0 | |

### §465.3 DevOps — отдельными задачами, чтобы не повторить пропуск DO-1/DO-2 цикла 23

| # | Задача | Когда | Готово, если |
|---|---|---|---|
| **DO-1** | `deploy/nginx/goods.ezbook.conf`: `location = /sw.js`, `location = /manifest.webmanifest` (§463.1); применить на сервере; сверить серверный vhost с репозиторием | сразу (файлы можно проверить на заглушке `sw.js`) | на стенде после деплоя `curl -I https://goods.ezbook.ru/sw.js` → 200 + `Cache-Control: no-cache` |
| **DO-2** | CI: grep `goods/public/sw.js`; профиль смоука goods «манифест есть + standalone + `sw.js`»; `types:api:cycle24` + diff; redocly lint cycle24 + `contracts/redocly.yaml` | после FE-0 | CI зелёный на ветке, шаги видны в логе |
| **DO-3** | `deploy/deploy-remote.sh`: смоук goods `sw.js` (200 + no-cache) и манифест (200 + standalone) | после DO-1 | ручной прогон `deploy-remote.sh` на стенде зелёный; без `sw.js` — красный |
| **DO-4** | `DEPLOY.md` §22 «goods как приложение»: service worker, манифест, push (те же VAPID; включение push — решение заказчика), миграция цикла (бэкфилл, смена уникального индекса, сид тарифа, **ограничение отката** §448.3), `ScheduledTasks:TickSeconds = 10` | до мерджа | раздел есть |
| **DO-5** | гигиена миграций: после каждого мерджа `develop` в ветку — пересборка Designer-снимка миграции цикла, `check-migration-snapshots.sh`; `dotnet ef migrations remove` не применять (урок 🛒23) | на каждом мердже | скрипт зелёный |
| **DO-6** | ручной смоук push на реальных устройствах после выката (Android Chrome, iPhone с экрана «Домой», десктоп) — чек-лист вместе с QA (R24-2), только если заказчик включит push на стенде | после выката | чек-лист в `TEST_CATALOG.md` отмечен, или записано «push на стенде выключен» |

### §465.4 QA

- Кейсы `CY24-*` в `TEST_CATALOG.md`.
- Schemathesis по `contracts/cycle24/openapi.yaml`.
- Векторы `pickup-schedule-vectors.json` зелёные.
- Параллельные тесты:
  - месячный лимит: N = 20 одновременных заказов при `limit − 1` → принят 1;
  - номер предзаказа: два заказа на одну дату выдачи из разных дней создания получают разные номера.
- Смена суток доски (подменяемые часы): предзаказ переходит в `accepted`.
- Интервал через полночь, перерыв, особый день.
- Изоляция (§460).
- **Регресс салонов:**
  - push мастеру (только `Site = Services`);
  - `enabledTypes` салона;
  - канал и финансирование аккаунта без магазинов;
  - лимиты салонов;
  - публичный каталог цен.
- Проверка «зелёный прогон ≠ функционал»: до объявления готовности — `grep` классов §464.
- Базовая линия — не ниже 1951 / 934 / 918.

### §465.5 Точки синхронизации BE↔FE

| Что | Где зафиксировано |
|---|---|
| форма DTO, коды 409, перечисления | `contracts/cycle24/openapi.yaml` (генерат — единственный источник типов) |
| тексты 400/402/409/429 и статусов | `API_CONTRACT_CYCLE24.md` (сервер собирает, фронт печатает) |
| тело push (контракт с service worker) | `API_CONTRACT_CYCLE24.md` §486 |
| маршруты goods | `contracts/cycle23/goods-routes.json` |
| правила часов и слотов | `pickup-schedule-vectors.json` (фронт слоты **не считает**, только показывает) |

### §465.6 Если не укладываемся (R24-1)

P1 режутся целиком, в таком порядке:
1. US-24-23 — статус доставки.
2. US-24-16 — push об отмене.
3. US-24-09 — смена времени персоналом.
4. US-24-02 — особые дни.
5. P1-пункты: US-24-03 («кто и когда»), US-24-10 (категория), US-24-11 (копия меню).

P0, включая блок F, не режутся.

---

## §466. Совместимость и выкат

- Для ezbook ломающих изменений формы нет: все новые поля и параметры добавочные, `site`/`line` по умолчанию
  `Services`. Поведение салонов меняется только в смешанных аккаунтах:
  - лимиты считаются внутри линейки (Q-24-7);
  - номер, оплаченный под тарифом «Заказы», считается оплаченным и для салона (§457.4).
- Для goods цикла 23 (фронт и API выкатываются атомарно одним релизом) `pickup`, `scope` у `sold-out` и
  `availableWeekdays` необязательны. Окно «новый API + старый фронт» безопасно.
- Порядок выката: DO-1 на сервере → деплой (миграция применяется на старте) → смоук. Откат после появления
  предзаказов с совпадающими номерами требует ручной проверки (§448.3).

---

## §467. Риски

| # | Риск | Решение |
|---|---|---|
| R-1 (R24-4) | Регресс ezbook в общих подсистемах (push, уведомления, биллинг) | Салонные контроллеры не открываются; `AccountSubscriptions` не трогается; все новые параметры по умолчанию `Services`; явные регрессионные тесты (§465.4). Изменения салонного кода — закрытый список: фильтр `Site` в `StaffPushScheduler`, `enabledTypes`, `NotificationGate` (маска), `SubscriptionResolver` (paid numbers), `ChannelEligibility`, `AccountUsageReader(kind)`, счётчики тарифа, `PricingCatalogBuilder` |
| R-2 (R24-1) | Объём (блок F — P0) | Параллельный план, prism-мок с первого дня, P1 режутся первыми (§465.6) |
| R-3 | Гонка месячного лимита | Счётчик со строковой блокировкой в транзакции + параллельный тест |
| R-4 | Интервалы через полночь, перерывы | Одна чистая `PickupSchedule` + векторы + тесты на границах суток |
| R-5 | Push не доходит за 30 с | Тик 10 с; постановка в транзакции; «доходит» проверяется только вручную (R24-2) |
| R-6 (R24-3) | Сообщение ушло чужому по опечатке в номере | Ограничение продукта (Q-24-3), названо прямо: галочка выключена по умолчанию, в тексте нет имени и телефона, ссылка ведёт на заказ по токену |
| R-7 | Один браузер на goods — и сотрудник, и покупатель | Браузерную подписку на goods не отзываем (§456.3) |
| R-8 | Магазины стенда перестают принимать заказы после выката | Требование SPEC; чек-лист в кабинете |
| R-9 | При коротком тике планировщик запустит задачу параллельно самой себе | BE-3 проверяет защиту раннера и при необходимости дописывает её (§455) |
| R-10 | Откат миграции после предзаказов | `DEPLOY.md` §22, ручная проверка дублей номеров перед `Down()` |
| R-11 | Правовые места (L9–L16) не закрыты | Нейтральные заглушки §461; goods остаётся стендом |

---

## §468. Отклонения от буквы SPEC и решения сверх неё (читать обязательно)

1. **A10: салонные `CompanyNotificationsController`/`CompanyPushSettingsController` для магазина НЕ открываются.**
   Вместо них — магазинный маршрут `notification-settings` поверх тех же строк. Салонные DTO несут тарифные 402,
   шаблоны и маску типов, и их открытие для магазина — лишний риск регресса без выгоды.
2. **Q-24-7: две подписки реализованы отдельной таблицей `OrdersSubscriptions`**, а не второй строкой
   `AccountSubscriptions` (§459.1).
3. **US-24-25 «Сотрудников: 1» засеяно как `MaxEmployees = 2`.** Места считают всех участников, включая владельца
   (конвенция цикла 7), поэтому «1 сотрудник» = владелец + 1. Заказчик меняет числа в админке без деплоя.
4. **Заявка на смену тарифа на аккаунт по-прежнему одна**, с полем `line`. Две одновременные заявки разных линеек не
   поддерживаются — 409 с понятным текстом.
5. **Опции общие для аккаунта** и показываются на экранах обеих линеек. «Итог = тариф + опции» считается по линейке,
   поэтому у смешанного аккаунта опции видны на обоих экранах.
6. **Отписка из сообщений о заказах ведёт на `ezbook.ru/u/…`** — это общая платформенная отписка по номеру (A9).
7. **Отметка «на сегодня» и пауза снимаются вычислением**, а не фоновой задачей. Требования «≤ 5 мин» и «≤ 1 мин»
   выполняются с запасом.
8. **Смена даты выдачи персоналом меняет номер заказа**: US-24-08 требует уникальности в пределах дня выдачи.
   Покупатель получает уведомление с новым номером.
9. **День выдачи для интервала через полночь — день начала интервала** (US-24-01), а не календарная дата после
   полуночи. Ночные заказы одной смены идут в одной нумерации.
10. **Идемпотентный повтор создания заказа проверяется раньше правила приёма**: повтор во время паузы возвращает уже
    созданный заказ.
11. **Месячный лимит считает созданные заказы**, отмена место не возвращает.
12. **Web-push-подписки заказа хранятся 7 дней после конечного статуса** — технический срок до заключения юриста
    (L16), а не «0 = бессрочно»: для этих данных короткий срок безопаснее.
13. **Публичной витрины тарифов «Заказов» нет** ([legal L14]): тарифы видит только вошедший владелец в своём
    кабинете.
14. **Отключение web-push заказа — `POST …/push-subscription/remove`**, а не `DELETE` с телом.

---

## §469. A13 — задел под цикл 25 (бот MAX персоналу): модель не мешает

- Адресаты «сотрудник магазина» выбираются в одном месте — `OrderNotificationPlanner`. Цель «чат MAX» добавится рядом
  с «push-устройствами»:
  - таблица привязки `StaffMessengerLink (UserId, ExternalChatKey, LinkedAtUtc, …)` по образцу
    `PhoneVerificationSession` (deep-link + подтверждение в боте);
  - очередь по форме `StaffPushNotification` или колонка транспорта в ней.

  В перечислении `NotificationType` уже есть `StaffOrderCreated`, новых типов не понадобится.
- Проверка «ещё участник магазина» при отправке и флаг магазина — те же, что у push (§455).
- Правовой вопрос L15 не блокирует модель.
