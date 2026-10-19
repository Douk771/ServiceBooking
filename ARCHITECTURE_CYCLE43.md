# ARCHITECTURE — цикл 43 ServiceBooking: опции мессенджеров (WhatsApp, MAX) — цена из админки, отвязка от тарифов, подключение отдельно от тарифа

**Разделы §43.0–§43.17.** Вход: `SPEC.md` (цикл 43, US-43-01…05, T-43-01…06, А1–А8, ответы заказчика В1–В4 от 19.10.2026),
`CURRENT_STATE.md` (на `12889a7`: §3, §5.16, §5.18, §6.1, §6.2, §6.4, §9.11 C40-7/C40-12/C40-17, §9.13 O-1…O-7), `ARCHITECTURE_CYCLE40.md`,
`contracts/cycle40/` (заморожен, не правится). Код сверен на ветке `cycle/043-messenger-options-pricing` (база `e4670c3`):
`AdminOptionsController`, `AdminBillingController`, `AdminAccountDtoBuilder`, `AdminPlansController`, `ProfileController`,
`OwnerSubscriptionService`, `AdminChannelsBuilder`, `AccountMessagingReader`, `ChannelOptionFunding`, `ChannelOptionAvailability`,
`ChannelPresentation`, `NumbersOverviewBuilder`, `ChannelOptionLog`, `BillingCalculator`, сущности `SubscriptionOption`,
`AccountSubscriptionOption`, `ChannelOptionChangeLog`, `ChannelPaymentLog`, `SubscriptionChangeLog`, `DemoResetTables`, тесты
`Cycle40ChannelsTests`, `Cycle18TrialLifecycleTests`, `OpenApiContractValidatorTests`, фронт `NotificationsAdminTab.tsx`,
`BillingAccountsAdminTab.tsx`, `PlansTab.tsx`, `BillingPage.tsx`, маршруты четырёх приложений. Ветку подготовил devops; архитектор веток не трогает.

**Документы цикла.** По конвенции проекта (`CURRENT_STATE.md` §6.5, §10.5) документы цикла лежат в корне под суффиксом номера цикла.
Корневые `ARCHITECTURE.md`/`API_CONTRACT.md` — документы **цикла 3**, их с цикла 13 никто не заменяет; этот цикл их тоже не трогает.

| Файл | Что | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE43.md` (этот) | решения, модель, механизмы, структура, задачи, риски | все |
| `API_CONTRACT_CYCLE43.md` (§43.20–§43.34) | контракт словами: маршруты, порядок проверок, коды, тексты, изменения существующих маршрутов | backend, frontend, QA |
| `contracts/cycle43/openapi.yaml` (+ `openapi.json` генератом, DO-43-01) | **источник истины по форме**: 6 новых маршрутов и 4 существующих, у которых меняется форма (OpenAPI 3.0.3) | backend, frontend, QA, CI |
| `contracts/cycle43/messenger-vectors.json` | эталон новых чистых правил: строка мессенджера (сумма и статус), «продаётся ли» с причиной, последствия смены цены, расчёт продления, кнопка владельца | backend (C#), QA |

`contracts/cycle40/openapi.yaml` и `contracts/cycle40/channel-vectors.json` **не меняются** (§43.4.5 объясняет, почему правка векторов не нужна).

---

## §43.0a. Решения заказчика (19.10.2026) — кодировать по ним

| # | Решение | Следствие для архитектуры |
|---|---|---|
| В1 = А | Мессенджеры от тарифа не зависят: одна цена за мессенджер на аккаунт | Правила `PlanOptionRules` для `notifications.whatsapp`/`notifications.max` никто не читает и не пишет; матрица тарифов их не показывает (§43.5) |
| В2 | Чинятся и админка, и «Подписка» владельца (включая US-43-05) | Блок «Мессенджеры» в карточке биллинг-аккаунта (§43.6) и блок у владельца во всех четырёх линейках (§43.8) |
| В3 | Смена цены не трогает оплаченные периоды, уведомлений не шлём | Цена хранится только в каталоге; периоды — даты в строке опции; пересчётов нет (§43.3) |
| В4 | Риск открытия продаж MAX вводом цены принят | Экран цены показывает серверные последствия до сохранения; порядок выката — `DEPLOY.md` §33 (§43.11) |

Правовая часть (SPEC §7) — отдельный трек `legal-counsel`, кодирование не блокирует. Что ждёт юриста — §43.15.

---

## §43.0. Итог решений — ответы на А1–А8 одним экраном

| # | Вопрос SPEC | Решение | Где |
|---|---|---|---|
| А1 | Как менять только цену | **Отдельный узкий ресурс** `/api/admin/messenger-options`: `GET` (обе опции + журнал), `POST …/{transport}/price/preview` (последствия без записи), `PUT …/{transport}/price` (цена и, по желанию, активность + обязательная причина + ожидаемые старые значения). Полный `PUT /api/admin/options/{id}` остаётся как есть (совместимость), но начинает писать журнал и сбрасывать кеши | §43.3 |
| А2 | Где журнал цены и сумма подтверждения | **Новая append-only таблица** `SubscriptionOptionPriceChangeLogs` (журнал цены). Сумма подтверждения — **три nullable-колонки** в существующем `ChannelOptionChangeLogs` (`PricePerMonth`, `Months`, `Amount`) + заполнение существующего `ChannelPaymentLogs.Amount`. Всё добавочное | §43.2 |
| А3 | Единое правило суммы и статуса | **Одна чистая функция** `MessengerLineRule` (`Services/Billing/`), поверх неизменного `ChannelOptionFunding`. Её зовут все четыре места (список и карточка аккаунта, профиль, «Подписка»). `channel-vectors.json` не правится: «оплачено» и «продаётся» не меняются; новый эталон — `contracts/cycle43/messenger-vectors.json` | §43.4 |
| А4 | Наследная строка без своей даты | Правило О-40-5 **сохраняется** (оно внутри `ChannelOptionFunding`). Такая строка — «Оплачено до {срок подписки „Записей“}» или «Оплата закончилась {дата}», сумма — цена опции, пока оплачено. Данные не мигрируются; первое продление из карточки даёт строке свою дату. Счёт таких строк на бою — отчёт DO-43-03 | §43.4.2 |
| А5 | Сумма в месяц на экранах линеек | **Отдельно.** Мессенджеры убираются из `options[]` и `totalMonthlyPrice` каждой линейки (инвариант cycle7 «итог = тариф + Σ options» сохраняется) и выводятся одним блоком `messengers` с собственным `monthlyTotal` и пометкой «общая для всех ваших компаний». Из-за этого минимальная часть US-43-05 (строки статуса и цены) становится P0; кнопка перехода — P1 | §43.8 |
| А6 | Строки цен на `/pricing` других линеек | **Не добавляются.** `/api/pricing` («Записи») и подписка — как в цикле 40. Публичная сетка «Заказов» мессенджеры не показывает (второй публичный сайт продаж — отдельное решение с юристом); «Дома» и «Бани» публичной сетки не имеют. Цену в кабинете любой линейки владелец видит в блоке `messengers` | §43.8.4 |
| А7 | Отключение «сейчас» | **Через существующее правило:** `EndsAtUtc = now` у строки опции. `ChannelOptionFunding` уже считает такую строку неоплаченной, дальше маршрутизация и гейт работают как при истёкшей оплате. Новых веток нет | §43.6.4 |
| А8 | Подключение без номера | **Проверено по коду:** `ChannelPresentation.WizardStep` для оплаченного транспорта без живого канала возвращает `Terms` (`!hasChannel`), то есть шаг оплаты пропускается. Заявка владельца на номере закрывается оплатой: `requestNewer = RequestedAtUtc > LastPaymentAt`, а продление ставит `ActivatedAtUtc = now`. Закрепляется тестами CY43-48/49 | §43.6.5 |

Решения сверх А1–А8 (Д-1…Д-6):
- **Д-1.** Подключить или продлить мессенджер (из карточки аккаунта и «Подтвердить оплату») можно, только если он **продаётся** целиком: открыт, активен, цена задана, оферта `TermsOwner` опубликована. В SPEC названы три условия. Четвёртое (оферта) добавлено, чтобы было одно правило `ChannelOptionAvailability.Sellable` и админ не «продавал» по неопубликованной оферте (§43.16 п. 1).
- **Д-2.** Ни одного нового значения в существующих перечислениях. Журнал опций берёт `ChannelOptionChangeSource.AdminBillingAccount` (0) для подключения и продления из карточки аккаунта и `AdminOptionEnded` (5) для отключения. История подписки берёт `SubscriptionChangeKind.Options` (2). Старый код после отката читает эти строки без сюрпризов.
- **Д-3.** Все записи денег по мессенджеру идут через один сервис `MessengerPaymentService` под замком `billing-account:{accountId}` — тем же, что у формы подписки и «Подтвердить оплату» (US-43-03 кр. 10).
- **Д-4.** Сохранение цены защищено от гонки двух админов: запрос несёт значения, от которых считались последствия (`expectedPricePerMonth`, `expectedIsActive`); расхождение — 409.
- **Д-5.** Продление в карточке аккаунта несёт `expectedPricePerMonth`: если цена изменилась между показом расчёта и нажатием, ответ 409 с новой ценой. Сумма в журнале всегда равна показанной.
- **Д-6.** Форма подписки (тариф) строки мессенджеров **молча игнорирует** во всех линейках: не создаёт, не продлевает, не завершает, 400/409 по ним не отвечает.

---

## §43.1. Стек: новых зависимостей — ноль

Расширение существующей системы: ASP.NET Core 8 + EF Core 8 + PostgreSQL, React + Vite + TanStack Query (`CURRENT_STATE.md` §1).
Новых NuGet/npm-пакетов, переменных окружения, фоновых задач и сервисов нет. Протокол — REST, машиночитаемый контракт — OpenAPI
3.0.3 (как во всех циклах). Почему ничего не добавляем: задача — починка админских и владельческих экранов поверх готовой модели
цикла 40. Любая новая инфраструктура только добавила бы риск выката на живые данные.

---

## §43.2. Модель данных — только добавления (C40-17, NFR SPEC §5)

### §43.2.1 Новая таблица `SubscriptionOptionPriceChangeLogs` (append-only журнал цены опции)

Сущность `ServiceBooking.Core/Entities/SubscriptionOptionPriceChangeLog.cs`, единственный писатель — `OptionPriceLog.Write`
(`Services/Billing/OptionPriceLog.cs`, по образцу `ChannelOptionLog`: только добавляет строку в трекер вызывающего).

| Колонка | Тип | Null | Смысл |
|---|---|---|---|
| `Id` | uuid PK | нет | |
| `OptionId` | uuid FK → `SubscriptionOptions.Id`, `ON DELETE RESTRICT` | нет | опция (каталог не удаляется физически) |
| `OptionCode` | varchar(64) | нет | снимок кода |
| `OldPricePerMonth` | numeric(10,2) | да | цена до (`NULL` — «не задана») |
| `NewPricePerMonth` | numeric(10,2) | да | цена после |
| `OldIsActive` / `NewIsActive` | boolean | нет | активность до/после |
| `Source` | int | нет | `OptionPriceChangeSource`: `MessengerPriceScreen = 0`, `OptionsCatalogApi = 1` (новое перечисление, хранится числом, только дописывание) |
| `ActivePaidAccounts` | int | да | снимок числа аккаунтов с действующей **оплатой** (не пробной) этой опции в момент изменения; `NULL` для опций не-мессенджеров |
| `ChangedByUserId` | text | да | без FK, как во всех журналах |
| `ChangedAtUtc` | timestamptz | нет | |
| `Comment` | varchar(500) | да | причина; в пути экрана обязательна, в пути старого API может быть `NULL` |

Индекс `IX_SubscriptionOptionPriceChangeLogs_OptionId_ChangedAtUtc`. Регистрация в `ShowcaseOwnership.NeverWritten`
(«цикл 43: журнал цены опции; витрина и демо его не пишут»). В `DemoResetTables.Kept` таблица **не** входит: демо-сброс её очищает.
Это допустимо: она ссылается на сохраняемую `SubscriptionOptions`, а не наоборот, и `KeptTablesReferencingWiped` остаётся пустым.

### §43.2.2 Новые колонки существующей таблицы `ChannelOptionChangeLogs` (все NULL)

| Колонка | Тип | Заполняется |
|---|---|---|
| `PricePerMonth` | numeric(10,2) NULL | цена за месяц на момент подключения/продления (из карточки аккаунта и «Подтвердить оплату») |
| `Months` | int NULL | число месяцев |
| `Amount` | numeric(10,2) NULL | `Months × PricePerMonth` |

У строк триала, отключения и старых строк — `NULL`. Плюс существующая колонка `ChannelPaymentLogs.Amount` (была всегда `NULL`)
заполняется суммой при «Подтвердить оплату».

### §43.2.3 Что не меняется

- Перечисления `ChannelOptionChangeSource`, `SubscriptionChangeKind`, `NotificationTransport` — без новых значений (Д-2).
- `SubscriptionOptions`, `AccountSubscriptionOptions`, `PlanOptionRules` (включая строки мессенджеров — П8),
  `SubscriptionPlanConfigs.AllowNotificationChannel`, `ChannelCompanyAssignments`, настройка `notifications.channel.price-per-month` —
  ни схема, ни данные. Удаление хвостов — отдельный цикл (C40-17).
- Данные миграцией не меняются: ни цен, ни строк. На бою после выката ничего не меняется, пока админ не введёт цену.

### §43.2.4 Миграция — одна: `Cycle43MessengerOptionPricing`, один разработчик (BE-43-M)

Создаётся закреплённым `dotnet-ef` 8.0.11, метка времени позже `20261009120658_Cycle42Baths`. Up: `CreateTable` +
`AddColumn ×3`. Down: `DropTable` + `DropColumn ×3`. Down теряет журнал цены и суммы, поэтому по `DEPLOY.md` §33 схему назад
не откатывают: откатывают только код. Накат → откат → накат на отдельной базе проверяет CY43-81 (образец CY42-140).

### §43.2.5 Совместимость с откатом кода на `e4670c3` (NFR SPEC §5)

| Что пишет новый код | Как это читает старый код | Итог |
|---|---|---|
| Строка `AccountSubscriptionOptions` мессенджера без канала (подключение из карточки аккаунта) | `AccountMessagingReader` уже умеет «оплачено, номера нет»; `PlanOptionRule`-путь админки покажет её как раньше («Недоступно на текущем тарифе» — старое поведение) | работает |
| `EndsAtUtc` у строки (отключение) | `ChannelOptionFunding` и все запросы `EndsAtUtc == null \|\| > now` | работает |
| Строки `ChannelOptionChangeLogs` с источниками 0 и 5 и новыми колонками | EF старой модели колонки не знает и их не читает | работает |
| `SubscriptionChangeLogs` с `ChangeKind = Options` | история подписки показывает как обычное изменение опций | работает |
| `SubscriptionOptionPriceChangeLogs` | старый код таблицу не знает | работает |
| `SubscriptionOptions.PricePerMonth` у MAX задана | это и было «рычагом запуска» цикла 40 (`DEPLOY.md` §31 п. 3) | работает |

### §43.2.6 Инварианты (QA проверяет тестами)

1. Журнал цены и журнал опций append-only: ни один маршрут их не правит и не удаляет.
2. Каждое изменение `PricePerMonth` или `IsActive` у опции мессенджера по любому пути (`PUT …/price`, `POST|PUT|DELETE /api/admin/options`)
   даёт ровно одну строку `SubscriptionOptionPriceChangeLogs` в той же транзакции.
3. Каждое подключение, продление и отключение мессенджера аккаунта даёт одну строку `ChannelOptionChangeLogs` и одну
   `SubscriptionChangeLogs` в той же транзакции (C40-7 закрывается для обоих путей).
4. Ни один путь записи не создаёт, не меняет и не удаляет `PlanOptionRules` мессенджеров.

---

## §43.3. Цена мессенджера в админке (US-43-01, А1, T-43-01)

### §43.3.1 Маршруты — новый контроллер `Controllers/AdminMessengerOptionsController.cs` (`[Route("api/admin")]`, `[Authorize(Roles = "SuperAdmin")]`)

| Маршрут | Что |
|---|---|
| `GET /api/admin/messenger-options` | обе опции: цена, активность, «открыто», итог «Продаётся / Не продаётся: причина», число аккаунтов с действующей оплатой, последние 50 записей журнала + тексты-подсказки (`planMatrixNote`, `priceHint`) |
| `POST /api/admin/messenger-options/{transport}/price/preview` | последствия изменения (тексты сервера), без записи |
| `PUT /api/admin/messenger-options/{transport}/price` | сохранить цену (и активность), журнал, сброс кешей |

`{transport}` — `WhatsApp` или `Max` (имена `NotificationTransport`, как в cycle40). Разбор — вручную строкой (без учёта регистра),
неизвестное значение — 404 с пустым телом (не 400 от модели ASP.NET). Тела запросов, ответы, тексты — `API_CONTRACT_CYCLE43.md` §43.22–§43.24.

### §43.3.2 Запись цены — порядок в `PUT`

1. Права (атрибут) → разбор транспорта (404) → валидация тела (400, тексты §43.22.4): цена `null` или 0…99 999 999,99 и не больше двух
   знаков (`decimal.Round(p, 2) == p`); `comment` после `Trim` 1…500 символов; «ничего не меняется» — 400.
2. Транзакция + `AdvisoryLock.AcquireAsync(db, "subscription-option:{optionId}")`; строка опции читается **из БД**, не из кеша. Нет строки → 409 «Опция MAX отсутствует в каталоге».
3. Сверка `expectedPricePerMonth` / `expectedIsActive` с текущими значениями: расхождение → 409 (Д-4).
4. `PricePerMonth` (+ `IsActive`, если передано не `null`), `UpdatedAtUtc = now`. Остальные поля строки **не трогаются** (US-43-01 кр. 2).
5. `ActivePaidAccounts` — §43.3.4; `OptionPriceLog.Write(… Source = MessengerPriceScreen …)`.
6. `SaveChanges` → `Commit` → **после коммита** `PricingCatalogCache.Invalidate()` и `AccountMessagingReader.InvalidateCatalogCache(cache)`.
7. Ответ — `AdminMessengerOptionDto` в свежем состоянии (тот же построитель, что у `GET`).

### §43.3.3 Последствия — чистое правило `MessengerPriceChangeRules.Consequences` (`Services/Billing/`)

Вход: транспорт, (цена, активность) до и после, `open`, `termsOwnerPublished`, `activePaidAccounts`. Выход: `startsSelling`,
`stopsSelling`, `isZeroPrice`, список текстов в фиксированном порядке (тексты — `API_CONTRACT_CYCLE43.md` §43.23.3):
1. стало продаваемым → «MAX начнёт продаваться: …»; перестало → «Продажи MAX остановятся. …»;
2. обе цены заданы и различаются → «Оплата MAX действует у N аккаунтов: их оплаченные периоды не меняются. …» (или «Аккаунтов с действующей оплатой MAX нет»);
3. новая цена 0 → «Цена 0 ₽: MAX будет подключаться бесплатно» (П5 — экран переспрашивает именно этим текстом);
4. цена задана, но мессенджер всё равно не продаётся → «MAX не продаётся: {причина}. Цена начнёт действовать, когда это изменится».

`preview` и `PUT` считают последствия одним правилом. `preview` нужен, чтобы экран показал подтверждение до записи (US-43-01 кр. 6).
Сервер не требует, чтобы `preview` вызывался: защита от рассинхрона — `expected*` (Д-4).

### §43.3.4 «Число аккаунтов с действующей оплатой»

Аккаунты, у которых строка опции есть, `GrantedByTrial = false` и `ChannelOptionFunding.Evaluate(...).Paid = true`. Способ: выбрать
`BillingAccountId` всех строк опции с `EndsAtUtc IS NULL OR EndsAtUtc > now` и прогнать через `AccountMessagingReader.LoadAsync`
(4 запроса на любой набор). Аккаунтов с мессенджером десятки, а SQL-«двойник» правила не нужен. Если их станет тысячи — риск R43-11.

### §43.3.5 Сброс кешей (T-43-01, O-5)

Один помощник `MessengerCatalogCaches.Invalidate(PricingCatalogCache, IMemoryCache)` (`Services/Billing/`) вызывается после коммита в:
`PUT …/messenger-options/{t}/price`, `POST /api/admin/options`, `PUT /api/admin/options/{id}`, `DELETE /api/admin/options/{id}`.
Других кешей цены нет: `MessengerAddonsProvider` читает БД, `/api/pricing` — через `PricingCatalogCache`,
`CustomerMessagingOfferService` (30 с) от цены не зависит. Кеш — `IMemoryCache` процесса. На бою один контейнер API,
поэтому «следующий запрос видит новую цену» выполняется. Несколько экземпляров — риск R43-6.

### §43.3.6 Старые пути

- `PUT /api/admin/options/{id}` (полная замена) **остаётся**. Если у опции мессенджера меняются `PricePerMonth` или `IsActive`, пишется
  строка журнала с `Source = OptionsCatalogApi` и `Comment = NULL`. Плюс сброс кешей. Форма маршрута не меняется.
- `POST /api/admin/options`, `DELETE /api/admin/options/{id}` — сброс кешей. Для `DELETE` опции мессенджера — строка журнала.
- `channelPricePerMonth` в `PUT /api/admin/platform-settings` принимается и игнорируется, как сейчас (US-43-01 кр. 10). Фронт его больше не шлёт (FE-43-1).

### §43.3.7 Где это на экране (US-43-01 кр. 1, 9)

Админка → вкладка уведомлений → блок «Подключение мессенджеров». Строка на мессенджер: переключатель «открыто для владельцев»
(существующий), цена или «не задана», активность, итог одной строкой (`saleText` сервера), кнопка «Изменить цену».
Форма: цена (пусто = снять), «Опция активна», «Причина изменения» (обязательно). Дальше — подтверждение со списком
`consequences` из `preview` и кнопка «Сохранить». Под формой — журнал (последние записи из `GET`). Две старые подсказки
заменяются текстом `priceHint` сервера.

---

## §43.4. Одно правило суммы и статуса строки мессенджера (US-43-02, А3, А4, T-43-02)

### §43.4.1 `MessengerLineRule` — чистый статический класс (`Services/Billing/MessengerLineRule.cs`)

```
MessengerLine Evaluate(MessengerLineFacts f)
MessengerLineFacts(Transport, OptionRowFacts? Row, DateTime? AccountTrialEndsAtUtc, LegacySubscriptionFacts Subscription,
                   decimal? PricePerMonth, DateTime? RequestedAtUtc /* заявка первого живого номера транспорта */, DateTime NowUtc)
MessengerLine(Status, StatusText, MonthlyPrice, CountsInTotal, PaidUntil, IsTrial, EndsAt, Payment)
(decimal Monthly, bool Counts) Amount(TransportPayment payment, OptionRowFacts? row, decimal? price)   // часть Evaluate, отдельно — для профиля
```

Алгоритм (первое совпадение). `payment = ChannelOptionFunding.Evaluate(Row, AccountTrialEndsAtUtc, Subscription, NowUtc)` — **без изменений**.
`requestNewer = RequestedAtUtc ≠ null ∧ (payment.LastPaymentAt = null ∨ RequestedAtUtc > payment.LastPaymentAt)` — та же формула, что в `NumbersOverviewBuilder.FactsOf`.

| # | Условие | `Status` | `StatusText` | `MonthlyPrice` | `CountsInTotal` |
|---|---|---|---|---|---|
| 1 | `payment.Paid ∧ Row.EndsAtUtc ≠ null` | `Ending` | «Отключается {EndsAt}» | триал — 0, иначе `Price ?? 0` | да |
| 2 | `payment.Paid ∧ payment.IsTrial` | `Trial` | «Пробный период до {Row.PaidUntilUtc}», без своей даты — «Пробный период» (окно рассылок не началось) | 0 | да (0) |
| 3 | `payment.Paid` | `Paid` | «Оплачено до {payment.PaidUntil}», без даты — «Оплачено» | `Price ?? 0` | да |
| 4 | `requestNewer` | `PendingPayment` | «Ожидает подтверждения оплаты» | 0 | нет |
| 5 | `Row = null` | `NotConnected` | «Не подключён» | 0 | нет |
| 6 | `payment.IsTrial` | `Expired` | «Пробный период закончился {payment.PaidUntil}» / «Пробный период закончился» | 0 | нет |
| 7 | иначе | `Expired` | «Оплата закончилась {payment.PaidUntil}» / «Оплата закончилась» | 0 | нет |

Даты — `MessengerTexts.FormatDate` (московское время, `dd.MM.yyyy`). Тексты — константы и методы `MessengerTexts`, под стражем
запрещённых слов (§43.10). «Недоступно на текущем тарифе» для мессенджеров не возвращается никогда (US-43-02 кр. 4).
Отключённая строка (`EndsAtUtc ≤ now`) по `ChannelOptionFunding` не оплачена, и `PaidUntil = EndsAt`, поэтому получает строку 6/7:
«Оплата закончилась {дата отключения}» (А7).

### §43.4.2 Наследная строка (А4)

Строка без своей даты и не триальная (`PaidUntilUtc = NULL`, `GrantedByTrial = false`) по `ChannelOptionFunding` живёт по подписке «Записей»
(О-40-5). Это правило **не меняется**. Подписка годна → строка 3 «Оплачено до {sub.PaidUntil}» (или «Оплачено»), сумма — цена. Подписка
кончилась → строка 7 «Оплата закончилась {sub.PaidUntil}». Раньше админка показывала такую строку как «Подключена»/«Недоступно…», то есть
текст у части строк поменяется. Масштаб даст отчёт DO-43-03 (блок «наследные строки»), оценка до выката обязательна (R43-5).

### §43.4.3 Кто зовёт правило (и только его)

| Место | Откуда факты | Новых запросов |
|---|---|---|
| `GET /api/admin/billing-accounts` → `totalMonthlyPrice` | `messagingByAccount` (уже грузится: `AccountMessagingReader.LoadAsync` по странице) | 0 |
| `GET /api/admin/billing-accounts/{id}` → `messengers[]`, `messengersMonthlyTotal`, `totalMonthlyPrice` | `messagingReader.ForAccountAsync` (уже вызывается) | 0 |
| `GET /api/profile` → `plan.totalMonthlyPrice` | уже загруженные строки опций, подписка и `account.TrialEndsAtUtc` → `ChannelOptionFunding` → `MessengerLineRule.Amount` | 0 |
| `GET /api/billing/subscription?line=…` → `messengers` | `messagingReader.ForAccountAsync` **один раз** на сборку (сейчас вызывается дважды: `AccountHasWorkingNumberAsync`, `NumbersAsync`; BE-43-2 сводит к одному) | −1 |

Чтобы читатель отдавал всё нужное, у записей добавляются поля **в конец** с умолчаниями (конвенция §6.1):
- `TransportState(..., bool Working, OptionRowFacts? Row = null)` — факты строки; `BuildTransport` их заполняет;
- `TransportOptionState(bool Open, bool Sellable, decimal? PricePerMonth, bool IsActive = false, bool TermsOwnerPublished = false)`.

### §43.4.4 Формулы итогов

- Карточка аккаунта: `totalMonthlyPrice = plan.pricePerMonth + Σ options[].pricePerMonth + messengersMonthlyTotal`, где
  `options[]` — **без мессенджеров** (они переехали в `messengers[]`), `messengersMonthlyTotal = Σ messengers[].monthlyPrice` по `countsInTotal`.
- Список аккаунтов и профиль: тот же итог (тариф «Записей» + не-мессенджерные опции по правилам тарифа + мессенджеры по `MessengerLineRule`).
- «Подписка» владельца любой линейки: `totalMonthlyPrice = plan.pricePerMonth + Σ options[].pricePerMonth` (инвариант cycle7 сохранён,
  мессенджеров в `options[]` нет), мессенджеры — `messengers.monthlyTotal` отдельно (А5).
- Не-мессенджерные опции (сейчас продаваемых нет) считаются по правилам тарифа **как раньше** (US-43-02 кр. 6).

### §43.4.5 Почему `contracts/cycle40/channel-vectors.json` не правится (конвенция §6.1)

Конвенция требует правки векторов вместе с правилами «оплачено» (`ChannelOptionFunding`), «продаётся» (`ChannelOptionAvailability.Sellable`),
маршрутизации, согласия, отображения номера и шага мастера. Цикл 43 ни одно из них не меняет. Новое правило `MessengerLineRule`
**потребляет** «оплачено», а новая `ChannelOptionAvailability.Explain` (причина «не продаётся») обязана совпадать с `Sellable`.
Это проверяет юнит-тест на векторах раздела `availability` замороженного файла (чтение, без правки). Эталон нового —
`contracts/cycle43/messenger-vectors.json`. Фронт эти правила не вычисляет (берёт готовые поля), поэтому TS-двойник не нужен.

---

## §43.5. Мессенджеры вне тарифов (US-43-02 кр. 1, 2; US-43-03 кр. 6; Д-6)

### §43.5.1 Тарифы — `AdminPlansController`

- `POST|PUT /api/admin/plans`: в `ApplyOptionRulesAsync` строки `options[]` с опцией мессенджера **отбрасываются** до записи, а существующие
  правила мессенджеров **никогда не удаляются**, даже если их нет в запросе. Механизм тот же, что у выведенных опций-лимитов (`RetiredLimitOptions`,
  §405 цикла 19). Ошибки нет (US-43-02 кр. 2).
- `GET /api/admin/plans`: `options[]` тарифа — без правил мессенджеров; `optionCoverage` считает только опции, к которым правила тарифа
  применимы. Текст при `total = 0` — «Опций, зависящих от тарифа, нет». Форма ответа не меняется.
- Признак «к опции правила тарифа не применяются» — `ChannelOptionLog.IsChannelOption(code)` (одно место).

### §43.5.2 Форма подписки — `AdminBillingController.AssignSubscription` и `AssignOrdersSubscriptionAsync`

- Строки `options[]` с опцией мессенджера **отбрасываются** сразу после проверки «опции найдены». Дальше они не участвуют ни в
  валидации количества, ни в правилах тарифа, ни в записи. `CheckChannelOptionLinesAsync` больше не вызывается (метод удаляется вместе
  с `IsUnchangedChannelOptionLine`, если других вызовов нет).
- Цикл «завершить отсутствующие строки» мессенджеры не трогает (условие `!IsChannelOption` уже есть).
- `OldOptionsSummary`/`NewOptionsSummary` строятся без мессенджеров.
- Итог: 400 «Для опций WhatsApp и MAX укажите дату окончания оплаты» больше не возникает ни при каком теле (US-43-03 кр. 6).

### §43.5.3 Фронт

- `PlansTab.tsx` строит матрицу из `GET /api/admin/options` (форма этого маршрута заморожена в cycle19 с `additionalProperties: false`,
  поэтому её **не** расширяем). Id опций мессенджеров берёт из `GET /api/admin/messenger-options` (`items[].optionId`) и исключает их.
  Вместо них — строка `planMatrixNote` сервера («Мессенджеры WhatsApp и MAX подключаются отдельно от тарифа. Цена — во вкладке уведомлений»).
  Если опций не осталось, матрица целиком заменяется этой строкой.
- `BillingAccountsAdminTab.tsx`: каталог формы фильтруется так же. Карточка аккаунта мессенджеров в `options[]` больше не содержит.

---

## §43.6. Мессенджер аккаунту отдельно от тарифа (US-43-03, US-43-04, А7, А8, Д-1, Д-3, Д-5)

### §43.6.1 `MessengerPaymentService` (scoped, `Services/Billing/MessengerPaymentService.cs`) — единственный писатель денег мессенджера

```
Task<MessengerQuote> QuoteAsync(Guid accountId, NotificationTransport t, int months, CancellationToken ct)
Task<MessengerWriteResult> ExtendAsync(Guid accountId, NotificationTransport t, int months, string? comment, decimal? expectedPricePerMonth,
                                       string adminUserId, Guid? channelId /* путь карточки номера */, CancellationToken ct)
Task<MessengerWriteResult> DisableAsync(Guid accountId, NotificationTransport t, MessengerDisableWhen when, string comment,
                                        string adminUserId, CancellationToken ct)
```

Чистые части — `MessengerChargeRules` (`Services/Billing/`): база продления, сумма, тексты расчёта, выбор «Подключить/Продлить»,
допустимость отключения, тексты подтверждения отключения. Покрыты векторами `quote` и `disable`.

### §43.6.2 Продление и подключение — порядок `ExtendAsync`

1. Транзакция + `AdvisoryLock.AcquireAsync(db, "billing-account:{accountId}")` — тот же ключ, что у формы подписки и «Подтвердить оплату» (US-43-03 кр. 10).
2. Строка каталога опции — из БД. Нет → 409 «Опция MAX отсутствует в каталоге».
3. `ChannelOptionAvailability.Explain(open, isActive, price, termsOwnerPublished)` — не продаётся → 409 с текстом причины (§43.25.4) (Д-1, US-43-03 кр. 3, US-43-04 кр. 1).
4. `expectedPricePerMonth` передан и ≠ цене → 409 «Цена MAX изменилась: теперь {P}. Проверьте сумму и повторите» (Д-5).
5. Текущее состояние транспорта аккаунта — `AccountMessagingReader.ForAccountAsync` (свежие строки; каталог из кеша не используется для решения).
6. База = `max(now, payment.PaidUntil ?? now)`, новая дата = `база.AddMonths(months)`. Это правило «Подтвердить оплату» с одной уточнённой
   деталью: у триальной строки без своей даты конец берётся из `ChannelOptionFunding` (окончание триала аккаунта), а не из
   `AccountSubscriptions.PaidUntil`. Обычно это одна и та же дата (§43.16 п. 7). У отключённой строки (`EndsAtUtc ≤ now`) `payment.PaidUntil = EndsAt`,
   поэтому база — «сейчас»: отключённый остаток не возвращается.
7. Строка опции: создать (`Quantity = 1`, без канала) или обновить: `PaidUntilUtc = новая`, `EndsAtUtc = null`, `ActivatedAtUtc = now`,
   `ActivatedByUserId`, `GrantedByTrial = false` (пробная выкупается), `Requested* = null`.
8. Журналы (одна транзакция): `ChannelOptionLog.Write(Source = channelId ≠ null ? AdminChannelCard : AdminBillingAccount, …, PricePerMonth, Months, Amount)`;
   `SubscriptionChangeLogs` (`ChangeKind = Options`, `Line = Services`, `Old/NewPlanConfigId` = текущий тариф «Записей» без изменения, `Old/NewPaidUntil` —
   даты мессенджера, `Old/NewOptionsSummary` — «MAX: оплачено до …», `Comment` — «Продление MAX на 3 мес. (3 мес. × 490 ₽ = 1470 ₽). {комментарий}»);
   при `channelId ≠ null` ещё `ChannelPaymentLogs` (как сейчас, + `Amount`).
9. Коммит. Ответ — карточка аккаунта (путь аккаунта) или карточка номера (путь «Подтвердить оплату»).

`ChannelOptionLog.Write` получает три необязательных параметра в конец сигнатуры (`pricePerMonth`, `months`, `amount`).

### §43.6.3 Расчёт до подтверждения — `QuoteAsync`

Та же логика шагов 2–6 без записи и без замка. Ответ: `amountText` «3 мес. × 490 ₽ = 1470 ₽», `newPaidUntilText`, `actionText`
(«Подключить на 3 мес.» / «Продлить на 3 мес.»), `pricePerMonth` — его фронт возвращает как `expectedPricePerMonth`. Не продаётся → 409
с тем же текстом, что вернёт продление. Им пользуются оба окна: карточка аккаунта и «Подтвердить оплату» в карточке номера (US-43-04 кр. 3).

### §43.6.4 Отключение (P1, US-43-03 кр. 7–9, А7)

- `when = Now` → `EndsAtUtc = now`. По `ChannelOptionFunding` строка сразу не оплачена → маршрутизация её не выбирает, гейт отвечает
  `NotOnPaidPlan`/`NoUsableChannel`, очередь живёт по правилам истёкшей оплаты. Нового кода в рассылках нет.
- `when = PeriodEnd` → `EndsAtUtc = payment.PaidUntil`. Допустимо, только если транспорт оплачен и у оплаты есть дата; иначе 409.
- Пробная строка: `Now` — да (кр. 9); `PeriodEnd` — только если у неё есть своя дата.
- Нечего отключать (строки нет или она уже не оплачена) → 409. `PeriodEnd` при уже назначенном отключении → 409 «Отключение уже назначено на {дата}».
- Журналы: `ChannelOptionChangeLogs` (`AdminOptionEnded`, `Old/NewEndsAtUtc`, комментарий), `SubscriptionChangeLogs` (`Options`, «Отключение MAX с {дата}. {комментарий}»).
- Денег не возвращаем. Текст подтверждения — с сервера в строке карточки (`disableNowConfirmText`, `disablePeriodEndConfirmText`):
  «Деньги не возвращаются. Сообщения клиентам в MAX перестанут уходить с {дата}».
- Повторное подключение после отключения — обычное продление; оно снимает `EndsAtUtc`.

### §43.6.5 Подключение без номера (А8)

Продление создаёт строку опции без канала. Владелец открывает мастер → `WizardStep(paid, hasChannel: false)` = `Terms` → `POST` канала
с `paymentRequest = false` → QR. Номер в состоянии «заявка на оплату» (`RequestedAtUtc` установлен): после продления `ActivatedAtUtc = now >
RequestedAtUtc` → `requestNewer = false`, транспорт оплачен → шаг `Terms`/`Qr`, статус «Ожидает подтверждения оплаты» уходит.
Код мастера не меняется. Сценарии закрепляют тесты CY43-48 и CY43-49.

### §43.6.6 Блок «Мессенджеры» в карточке аккаунта (US-43-03 кр. 1, 3)

`AdminAccountDtoBuilder` добавляет в конец ответа `messengers` (ровно две строки: WhatsApp, MAX — всегда, чтобы была видна причина,
почему подключить нельзя) и `messengersMonthlyTotal`. Строка: статус и текст (§43.4), цена в месяц или «Цена не задана», сумма в итоге,
оплачено до, номер (`None`/`AwaitingQr`/`Bound` + маска телефона), итог продажи, `canExtend` + `extendActionText` + `extendDisabledText`,
`canDisable` + тексты подтверждения. Номер: первый живой канал транспорта (`TransportState.Primary`); `NotConnected`/`Connecting` →
`AwaitingQr`, иные состояния → `Bound`. Форма — `contracts/cycle43/openapi.yaml` `AdminMessengerLineDto`.

### §43.6.7 «Подтвердить оплату» в карточке номера (US-43-04)

`AdminChannelsBuilder.ConfirmPaymentAsync` становится тонкой обёрткой над `MessengerPaymentService.ExtendAsync(…, channelId)`. Проверки
`Replaced` и «нет аккаунта» остаются перед вызовом. Месяцы остаются 1…12 (форма cycle40), у карточки аккаунта — 1…24 (SPEC).
Новое необязательное поле тела `expectedPricePerMonth` (добавочное). Результат и журналы совпадают с путём аккаунта (кр. 2), плюс
существующий `ChannelPaymentLogs` с суммой (кр. 4).

---

## §43.7. Права и безопасность

- Все новые маршруты — `[Authorize(Roles = "SuperAdmin")]`, проверка на сервере; 401/403 — пустое тело (US-43-01 кр. 8).
- Телефоны в новых ответах — только `PhoneDisplayMask.Mask` (как в карточке аккаунта).
- Каждое изменение денег — в журнале с автором (§43.2.6).
- Логи: `LogInformation` «Messenger {Transport} price changed by {UserId}: {Old} → {New}» и «Messenger {Transport} extended for account {AccountId} by {UserId}: {Months} × {Price}» — без телефонов.
- Тексты: страж запрещённых слов (§43.10).

---

## §43.8. «Подписка» владельца (US-43-05, А5, А6)

### §43.8.1 Сервер — `OwnerSubscriptionService` (все четыре линейки)

- `options[]` и `totalMonthlyPrice` — **без** строк мессенджеров (А5). `availableOptions` — как сейчас (мессенджеры уже исключены).
  Других продаваемых опций нет, поэтому `availableOptions` на практике пуст.
- Новое поле в конец `OwnerSubscriptionDto`: `Messengers` (`OwnerMessengersBlockDto?`). `null`, если ни одна строка не показывается.
  Строка показывается, если мессенджер продаётся, или у аккаунта есть строка опции, или живой номер этого транспорта.
- Блок: `title` «Сообщения клиентам в мессенджеры», `note` «Общие для всех ваших компаний», `lines[]`, `monthlyTotal`, `monthlyTotalText`
  («Мессенджеры: 490 ₽/мес»), `conditionsUrl`/`conditionsLabel` (как у `messengerAddonsNote`).
- Строка: статус и текст (§43.4), цена (`priceText` — только если продаётся или оплачено), сумма, `footnote` (у WhatsApp —
  `MessengerTexts.WhatsAppFootnote`), `action`, `actionText`, `targetCompanyId`, `noCompanyText`.

### §43.8.2 Кнопка — правило `MessengerChargeRules.OwnerAction` (векторы `ownerAction`)

| Условие | `action` | `actionText` |
|---|---|---|
| рассылки платформы выключены, или не продаётся, или статус `Paid`/`Trial`/`Ending`/`PendingPayment` | `null` | `null` |
| статус `NotConnected` | `Connect` | «Подключить» |
| статус `Expired`, не триал | `Renew` | «Продлить» |
| статус `Expired`, триал | `Connect` | «Подключить» |

`targetCompanyId` — первая по `CreatedAt` компания аккаунта **вида этой линейки**. Нет такой → `targetCompanyId = null`,
`noCompanyText` «Сначала создайте компанию» (только когда `action ≠ null`). «Продлить» для ещё оплаченного транспорта **не**
предлагается: правило мастера `CanRequestPayment` (замороженные векторы cycle40) не принимает заявку на оплаченный не-пробный
транспорт. Отклонение от SPEC — §43.16 п. 2.

### §43.8.3 Фронт — общий `BillingPage` (четыре сайта)

- Новый компонент `src/components/billing/MessengersBlock.tsx` выводит `messengers`. Кнопка — ссылка, которую строит сайт: у `BillingPage`
  новый необязательный проп `notificationsHref(companyId, transport)`. ezbook — `/owner/company/{id}?tab=notifications&connect={t}`
  (по умолчанию). goods — `/cabinet/{id}/notifications?connect={t}`. dom и bani — `/cabinet/{id}/notifications?connect={t}`
  (пробрасывают свои `SubscriptionPage`).
- `NumbersBlock` (общий) при `?connect=WhatsApp|Max` открывает `ConnectWizard` этого транспорта, если в `overview` есть строка транспорта.
  После открытия параметр убирается из URL (`replace`).
- Блок «Доступные опции» не рисуется при пустом `availableOptions` (кр. 3). `MessengerAddonLines` на `BillingPage` убирается: его роль
  берёт блок. На `/pricing` (`PlanCard`) остаётся.

### §43.8.4 Публичные цены (А6)

`/api/pricing` — без изменений (строки цен мессенджеров «Записей»). `GET /api/pricing/orders` мессенджеры **не** получает. Причина:
это был бы новый публичный канал продаж на втором сайте, а он требует сверки текста оферты и Приложения № 1 для «Заказов» (§43.15,
вопрос Ю-6). «Дома» и «Бани» публичной сетки не имеют (`IsPublic = false`). Открытый вопрос Q43-1 (§43.17).

---

## §43.9. Фронтенд — что и где

| Файл | Изменение |
|---|---|
| `frontend/package.json` | скрипт `types:api:cycle43` → `src/types/api-cycle43.generated.ts` (генерат, руками не правится) |
| `src/types/index.ts` | реэкспорт новых типов из генерата |
| `src/api/adminBilling.ts` | `messengerOptions()`, `previewMessengerPrice(t, body)`, `updateMessengerPrice(t, body)`, `messengerQuote(accountId, t, months)`, `extendMessenger(accountId, t, body)`, `disableMessenger(accountId, t, body)` |
| `src/api/notificationChannels.ts` (админский метод confirm-payment) | необязательный `expectedPricePerMonth` |
| `src/pages/admin/MessengerOptionsPanel.tsx` (+ `messengerOptionsHelpers.ts`, тесты) | блок цены во вкладке уведомлений (§43.3.7); ключ react-query `['admin','messenger-options']`; после сохранения — `invalidateQueries` этого ключа, `['admin','billing-accounts']`, `['pricing']` |
| `src/pages/admin/NotificationsAdminTab.tsx` | встраивает панель; две старые подсказки → `priceHint`; не шлёт `channelPricePerMonth`; окно «Подтвердить оплату» — строка расчёта из `messengerQuote` (по `billingAccountId` карточки номера) + `expectedPricePerMonth`; 409 — текст сервера дословно |
| `src/pages/admin/PlansTab.tsx` | матрица без мессенджеров + `planMatrixNote` (§43.5.3) |
| `src/pages/admin/BillingAccountsAdminTab.tsx` + `billingAccountsHelpers.ts` | каталог формы без мессенджеров; карточка — новый `AccountMessengersBlock` |
| `src/pages/admin/AccountMessengersBlock.tsx` (+ тест) | две строки, «Подключить/Продлить на N мес.» (поле 1…24, комментарий, расчёт `quote` при смене N, подтверждение), «Отключить» (P1: выбор «сейчас / по окончании», обязательный комментарий, текст подтверждения сервера) |
| `src/components/billing/MessengersBlock.tsx` (+ тест) | блок владельца (§43.8.3); общий каталог `src/components` уже в `shared-sources.js` |
| `src/pages/BillingPage.tsx` | блок, проп `notificationsHref`, скрытие пустого `availableOptions`, без `MessengerAddonLines` |
| `src/components/notifications/NumbersBlock.tsx` | открытие мастера по `?connect=` |
| `goods/…/SubscriptionPage.tsx`, `dom/…/SubscriptionPage.tsx`, `bani/…/SubscriptionPage.tsx` | `notificationsHref` своего сайта |

Правила (§6.2): фронт ничего не считает — суммы, статусы, кнопки, причины, последствия и тексты приходят с сервера; деньги
форматирует только там, где сервер прислал число без текста (`utils/money.ts`). Поля с подписями, ошибки привязаны к полям,
модалки закрываются через `useOverlayDismiss`, всё доступно с клавиатуры. Импорты внутри `src/` — только относительные.

---

## §43.10. Тесты

**Юнит (`ServiceBooking.UnitTests/Cycle43/`, без БД):** `MessengerLineRuleVectorsTests` (раздел `line`), `ChannelOptionAvailabilityExplainTests`
(раздел `sale` + сверка с разделом `availability` замороженного `contracts/cycle40/channel-vectors.json`: `Explain(...).Sellable == Sellable(...)`),
`MessengerPriceChangeRulesTests` (`consequences`, `priceValidation`), `MessengerChargeRulesTests` (`quote`, `disable`, `ownerAction`),
`OptionPriceLogTests`. Страж `MessengerTextsForbiddenWordsTests` расширяется: кроме констант проверяет выходы новых методов на
выборке входов из векторов.

**Функциональные (`ServiceBooking.Tests/Tests/Cycle43*.cs`, `TestCase("CY43-…")`, описание — раздел «Цикл 43» `TEST_CATALOG.md`):**

| ID | Что |
|---|---|
| CY43-01…05 | цена: 401/403 не-суперадмину; 400 на отрицательную, > 99 999 999,99, три знака, пустую/длинную причину, «ничего не меняется»; 404 на неизвестный транспорт; сохранение меняет только цену и активность (остальные поля строки байт-в-байт) |
| CY43-06 | мгновенная видимость: сразу после `PUT` — `GET /api/notification-channels/overview` (цена, `canRequestPayment`), `/api/pricing` (строка MAX), `/api/billing/subscription` (`messengers`), карточка аккаунта — новые значения |
| CY43-07 | `preview`: три вида последствий (начнёт продаваться / остановятся / N аккаунтов), цена 0, «не продаётся по иной причине» |
| CY43-08 | гонка двух админов: `expected*` устарели → 409, журнал — одна строка |
| CY43-09 | старый `PUT /api/admin/options/{id}` по опции MAX → строка журнала `OptionsCatalogApi` + сброс кеша (мастер видит цену без ожидания) |
| CY43-10 | `channelPricePerMonth` в `PUT /api/admin/platform-settings` принимается без ошибки |
| CY43-20…27 | **матрица сумм и статусов:** 2 транспорта × 4 линейки × {пробная без даты, пробная с датой, оплаченная, истёкшая, наследная при годной и истёкшей подписке, отключается, ожидает подтверждения}. Список аккаунтов, карточка, `GET /api/profile`, «Подписка» — суммы совпадают, статус-тексты админки и владельца совпадают, «Недоступно на текущем тарифе» нет нигде |
| CY43-28 | аккаунт без тарифа «Записей» (только «Бани») с оплаченным MAX: карточка и список — цена в сумме (И-4) |
| CY43-30, 31 | тарифы: `PUT /api/admin/plans` с правилами мессенджеров → 200, правила в БД не изменились и не удалены; `GET /api/admin/plans` их не отдаёт, `optionCoverage` без мессенджеров |
| CY43-32, 33 | форма подписки (все линейки): строки мессенджеров с датой и без → 200, строки опций не созданы, не продлены, не завершены |
| CY43-40…47 | продление из карточки: новая строка без канала; продление от `max(now, конец)`; выкуп пробной; наследная строка; 409 по четырём причинам; 409 при смене цены; журналы (`ChannelOptionChangeLogs` с ценой, месяцами, суммой + `SubscriptionChangeLogs`); два параллельных продления = два продления |
| CY43-48, 49 | (А8) после подключения без номера `overview.wizardStep = Terms`; номер в заявке → после продления `paymentPending = false` |
| CY43-50…53 | (P1) отключение сейчас → не оплачено, `NotificationRouting` не выбирает транспорт, статус «Оплата закончилась {сегодня}»; по окончании → «Отключается {дата}»; пробная; 409 «нечего отключать» и «уже назначено» |
| CY43-60…64 | «Подтвердить оплату»: без цены → 409, неактивна → 409, оферта не опубликована → 409; результат = путь аккаунта; сумма в `ChannelOptionChangeLogs` и `ChannelPaymentLogs.Amount`; `SubscriptionChangeLogs` пишется (C40-7) |
| CY43-70…74 | блок владельца: нет блока, когда нечего показать; строки и кнопки по правилу `ownerAction`; «Сначала создайте компанию»; `options[]` и `totalMonthlyPrice` линейки без мессенджеров (инвариант cycle7) во всех четырёх линейках |
| CY43-80 | ответы всех маршрутов §43.21 по `OpenApiContract.Load("cycle43")` |
| CY43-81 | миграция `Cycle43MessengerOptionPricing`: накат → откат → накат |

**Существующие тесты, которые меняются из-за намеренной смены поведения** (запись в реестр `TEST_CATALOG.md` по правилам цикла 36,
категория «замена: поведение изменено SPEC цикла 43»):
- `Cycle40ChannelsTests` CY40-ADM-06 (форма подписки с MAX) — ожидание «не 400 и не завершает» остаётся верным. Arrange через
  «Подтвердить оплату» требует цены MAX (Д-1): в arrange задать цену и опубликованную оферту.
- `Cycle40ChannelsTests` CY40-ADM-07 и CY40-CTR-01 — то же: arrange задаёт цену MAX.
- `Cycle18TrialLifecycleTests` CY18L-23 — выкуп пробной строки через форму подписки больше не работает (Д-6). Переписать на
  `POST …/messengers/WhatsApp/extend` (открыть WhatsApp и задать цену в arrange); проверяемый смысл (выкуп снимает `GrantedByTrial`,
  повторная выдача триала строку не трогает) сохраняется.
- `Cycle22RouteTable.golden.txt` — +6 строк (новые маршруты), существующие строки не меняются.
- `OpenApiContractValidatorTests` — `[InlineData("cycle43")]`.

**Фронт (vitest, рядом с кодом):** `MessengerOptionsPanel.test.tsx` (форма, подтверждение со списком последствий, текст 409 дословно,
блок не виден без данных), `PlansTab.test.tsx` (+ матрица без мессенджеров и с пояснением), `billingAccountsHelpers.test.ts` (+ фильтр),
`AccountMessengersBlock.test.tsx`, `MessengersBlock.test.tsx`, `BillingPage.test.tsx` (+ блок, пустые опции скрыты), `NumbersBlock`
(+ `?connect=`). Новые файлы — под префиксами областей `contracts/cycle36/test-areas.json` (существующие префиксы покрывают `src/pages/admin`, `src/components`).

**Ручные M43 (гейт выката, вердикты — в `TEST_CATALOG.md`):** M43-01 ввести цену MAX на стенде → строка на `/pricing` и шаг оплаты в
мастере; M43-02 снять цену → продажи остановлены, оплаченный аккаунт продолжает слать; M43-03 подключить MAX аккаунту без номера →
владелец проходит мастер без шага оплаты, получает QR; M43-04 блок «Мессенджеры» в «Подписке» на четырёх сайтах, кнопка открывает
мастер; M43-05 отчёт DO-43-03 до и после выката совпадает по строкам опций.

---

## §43.11. DevOps, выкат, откат

- **CI (DO-43-01):** `contracts/cycle43/openapi.yaml` — в список `redocly lint` (`.github/workflows/ci.yml` + комментарий в `contracts/redocly.yaml`);
  `scripts/contracts-to-json.mjs` — `cycle43` → `contracts/cycle43/openapi.json` (его читает `OpenApiContract.Load`); сверка генерата
  `api-cycle43.generated.ts` (как у cycle40/42). Сборка `-warnaserror` — без изменений.
- **`DEPLOY.md` §33 (DO-43-02):** 1) до выката — отчёт DO-43-03 на бою (сохранить вывод); 2) выкат: миграция только добавляет, данные
  не меняются, до ввода цены поведение продаж не меняется; 3) после выката — отчёт ещё раз, строки опций должны совпасть;
  4) **порядок запуска продаж MAX:** опубликовать новую редакцию D3 и Приложения № 1 (юрист, C40-12) → проверить переключатель
  «MAX — открыто для владельцев» → ввести цену на экране (подтверждение покажет «MAX начнёт продаваться»); 5) остановить продажи — снять
  цену или закрыть переключатель; 6) откат — только код (`rollback.sh`), схему не откатывать; 7) известное изменение текстов наследных строк (§43.4.2).
- **Отчёт (DO-43-03):** `deploy/checks/cycle43-messenger-options.sql` — только чтение: строки каталога двух опций (цена, `IsActive`), значения
  `notifications.option.*.open`, строки опций по транспорту × {пробная без даты, пробная с датой, своя дата в будущем/прошлом, наследная,
  `EndsAtUtc` задан}, аккаунты с оплаченным мессенджером без подписки «Записей» (И-4), число `PlanOptionRules` мессенджеров (должно не меняться).
- **Стенд (DO-43-04):** выкат ветки по кнопке, прогон M43 с заказчиком. Память проекта: `build-app-implement` пропускает DevOps-трек —
  до мерджа сверить DO-43-01…04 с `git log` ветки.

---

## §43.12. Структура — что добавляется и меняется

```
ServiceBooking.Core/
  Entities/SubscriptionOptionPriceChangeLog.cs            (новый)
  Entities/SubscriptionOption.cs, AccountSubscriptionOption.cs  (комментарии к факту — T-43-04, O-6)
  Entities/ChannelOptionChangeLog.cs                      (+PricePerMonth, Months, Amount)
  Enums/OptionPriceChangeSource.cs                        (новый: MessengerPriceScreen=0, OptionsCatalogApi=1)
ServiceBooking.Infrastructure/
  Data/AppDbContext.cs                                    (DbSet + конфигурация)
  Migrations/2026102xxxxxxx_Cycle43MessengerOptionPricing(.Designer).cs + снапшот
ServiceBooking.API/
  Controllers/AdminMessengerOptionsController.cs          (новый: GET, preview, PUT price)
  Controllers/AdminAccountMessengersController.cs         (новый: quote, extend, disable; Route api/admin, SuperAdmin)
  Controllers/AdminOptionsController.cs                   (журнал + сброс кешей)
  Controllers/AdminPlansController.cs                     (мессенджеры вне матрицы)
  Controllers/AdminBillingController.cs                   (итог списка; форма игнорирует мессенджеры)
  Controllers/AdminAccountDtoBuilder.cs                   (options без мессенджеров, messengers[], итоги)
  Controllers/ProfileController.cs                        (итог по правилу)
  DTOs/Billing/MessengerDtos.cs                           (новый: admin/owner DTO и входы цикла 43)
  DTOs/Billing/OwnerBillingDtos.cs                        (+ Messengers в конец OwnerSubscriptionDto)
  Services/Billing/MessengerLineRule.cs                   (новый, чистый)
  Services/Billing/MessengerPriceChangeRules.cs           (новый, чистый: валидация, последствия)
  Services/Billing/MessengerChargeRules.cs                (новый, чистый: расчёт, отключение, кнопка владельца)
  Services/Billing/MessengerPaymentService.cs             (новый, scoped)
  Services/Billing/MessengerOptionsReader.cs              (новый, scoped: GET экрана цены, activePaidAccounts, журнал)
  Services/Billing/OptionPriceLog.cs                      (новый: единственный писатель журнала цены)
  Services/Billing/MessengerCatalogCaches.cs              (новый: один сброс двух кешей)
  Services/Billing/ChannelOptionLog.cs                    (+3 необязательных параметра)
  Services/Billing/OwnerSubscriptionService.cs            (4 линейки: options без мессенджеров, блок, один ForAccountAsync)
  Services/Notifications/Funding/ChannelOptionAvailability.cs (+ Explain, MessengerSaleReason)
  Services/Notifications/AccountMessagingReader.cs        (TransportState.Row, TransportOptionState.IsActive/TermsOwnerPublished)
  Services/Notifications/AdminChannelsBuilder.cs          (ConfirmPaymentAsync → MessengerPaymentService)
  Services/Notifications/MessengerTexts.cs                (новые тексты)
  Services/Showcase/ShowcaseOwnership.cs                  (+ таблица)
  Extensions/ApplicationServicesExtensions.cs             (регистрация трёх scoped-сервисов)
contracts/cycle43/openapi.yaml, openapi.json (генерат), messenger-vectors.json
deploy/checks/cycle43-messenger-options.sql
frontend/… — §43.9
```

---

## §43.13. Разбивка работ и параллельность

Контракт (`openapi.yaml`, `messenger-vectors.json`, `API_CONTRACT_CYCLE43.md`) готов **до** кода. Фронт стартует в день 1 на моке
`npx @stoplight/prism mock contracts/cycle43/openapi.yaml --port 4043`.

### §43.13.1 Backend

| # | Задача | US / T | Зависит от | Параллельно с |
|---|---|---|---|---|
| BE-43-M | Сущность, перечисление, 3 колонки, `AppDbContext`, **одна миграция** `Cycle43MessengerOptionPricing`, `ShowcaseOwnership`; комментарии `SubscriptionOption.cs`/`AccountSubscriptionOption.cs` (T-43-04). Один разработчик, один коммит | T-43-04 | — | BE-43-R, BE-43-3 |
| BE-43-R | Чистые правила + тексты: `MessengerLineRule`, `ChannelOptionAvailability.Explain`, `MessengerPriceChangeRules`, `MessengerChargeRules`, `MessengerTexts`; юнит-тесты по `messenger-vectors.json`; расширение стража слов | 01, 02, 03, 05 | — | BE-43-M, BE-43-3 |
| BE-43-3 | Тарифы и форма: `AdminPlansController` (§43.5.1), `AdminBillingController` форма во всех линейках (§43.5.2) | 02, 03 кр. 6 | — | всё |
| BE-43-1 | Цена: `AdminMessengerOptionsController`, `MessengerOptionsReader`, `OptionPriceLog`, `MessengerCatalogCaches`; журнал и сброс в `AdminOptionsController` | 01, T-43-01 | BE-43-M, BE-43-R | BE-43-2 |
| BE-43-2 | Одно правило: поля `TransportState`/`TransportOptionState`; список и карточка аккаунта (`messengers[]`, итоги), `ProfileController`, `OwnerSubscriptionService` (4 линейки, блок `messengers`, один `ForAccountAsync`) | 02, 05 (сервер), T-43-02 | BE-43-R | BE-43-1 |
| BE-43-4 | `MessengerPaymentService` (quote, extend, disable), `AdminAccountMessengersController`, перевод `ConfirmPaymentAsync`, `SubscriptionChangeLogs` (C40-7), суммы в журналах | 03, 04 | BE-43-M, BE-43-R, BE-43-2 (карточка в ответе) | — |
| BE-43-5 | В конце: `Cycle22RouteTable.golden.txt` (+6), `[InlineData("cycle43")]`, `Cycle43ContractTests` (CY43-80), раздел в `API_DOCUMENTATION.md` | — | всё BE | — |

### §43.13.2 Frontend

| # | Задача | US | Зависит от | Параллельно с |
|---|---|---|---|---|
| FE-43-0 | `types:api:cycle43` + генерат, реэкспорт, методы `adminBilling.ts`/confirm-payment | — | контракт | — |
| FE-43-1 | `MessengerOptionsPanel` во вкладке уведомлений, замена подсказок, без `channelPricePerMonth` | 01 | FE-43-0 | все FE |
| FE-43-2 | `PlansTab` без мессенджеров + пояснение; фильтр каталога формы подписки | 02, 03 кр. 6 | FE-43-0 | все FE |
| FE-43-3 | `AccountMessengersBlock` в карточке аккаунта (подключить/продлить с расчётом; отключить — P1) | 03 | FE-43-0 | все FE |
| FE-43-4 | Окно «Подтвердить оплату»: строка расчёта, `expectedPricePerMonth`, 409 дословно | 04 | FE-43-0 | все FE |
| FE-43-5 | `MessengersBlock` на `BillingPage` (4 сайта, `notificationsHref`), скрытие пустых опций, `NumbersBlock ?connect=` | 05 (строки — P0, кнопка — P1) | FE-43-0 | все FE |

### §43.13.3 DevOps

| # | Задача | Когда |
|---|---|---|
| DO-43-01 | CI: redocly + `contracts:json` + сверка генерата `cycle43` | день 1 |
| DO-43-02 | `DEPLOY.md` §33 (§43.11) | день 1, дополнить после BE-43-M |
| DO-43-03 | `deploy/checks/cycle43-messenger-options.sql` | день 1 |
| DO-43-04 | выкат на стенд, M43 с заказчиком | в конце |

### §43.13.4 QA

QA-43-1 функциональные CY43-* (§43.10); QA-43-2 правка четырёх существующих тестов с записями в реестр; QA-43-3 раздел «Цикл 43»
в `TEST_CATALOG.md` (CY43, M43, реестр); QA-43-4 schemathesis по `contracts/cycle43/openapi.yaml`; QA-43-5 вердикты M43-01…05.

### §43.13.5 Порядок и точки синхронизации

```
День 1:  DO-43-01, DO-43-02, DO-43-03 | BE-43-M, BE-43-R, BE-43-3 | FE-43-0 → FE-43-1…5 по моку
Затем:   BE-43-1 и BE-43-2 (параллельно)
Затем:   BE-43-4
Конец:   BE-43-5, QA-43-1…4, DO-43-04 + M43 (QA-43-5)
```

| Что | Где |
|---|---|
| формы DTO, перечисления, коды | `contracts/cycle43/openapi.yaml` → генерат |
| статусы, суммы, «продаётся», последствия, расчёт, кнопка | `contracts/cycle43/messenger-vectors.json` |
| тексты 400/409 | `API_CONTRACT_CYCLE43.md` — фронт печатает тело ответа |

### §43.13.6 Если не укладываемся (SPEC §8)

Режутся первыми: кнопка «Подключить/Продлить» в блоке владельца и `?connect=` (FE-43-5, часть; строки блока остаются — они нужны
из-за А5) → отключение (BE-43-4 `disable` + FE-43-3 часть; маршрут не публиковать, из контракта не удалять, кнопку скрыть) → суммы в
журнале (колонки остаются, заполнение можно отложить). P0 не режутся: BE-43-M, R, 1, 2, 3, 4 (quote/extend/confirm), FE-43-0…4.

---

## §43.14. Риски и решения

| # | Риск | Решение |
|---|---|---|
| R43-1 | Ввод цены MAX сразу открывает публичные продажи; опечатка видна на `/pricing` (Р1, В4) | Подтверждение с серверными последствиями; цена — по порядку `DEPLOY.md` §33; снять — одним действием; журнал с автором |
| R43-2 | Владелец видит другие числа: мессенджер ушёл из итога линейки в отдельный блок (А5) | Блок с суммой рядом; изменение — в `CHANGELOG`; при урезании строки блока не режутся |
| R43-3 | «Подтвердить оплату» строже (цена, активность, оферта): если на бою оферта `TermsOwner` в черновике, подтверждение WhatsApp/MAX начнёт отвечать 409 | Отчёт DO-43-03 + проверка манифеста перед выкатом; текст 409 говорит причину |
| R43-4 | Форма подписки больше не выдаёт мессенджеры — если кто-то (скрипт, человек) пользовался ею как обходом | Путь — карточка аккаунта; в `DEPLOY.md` §33 и `CHANGELOG` явно |
| R43-5 | Наследные строки (без даты) меняют текст на «Оплачено до…» / «Оплата закончилась…» | Счёт в отчёте до выката; продление из карточки даёт строке свою дату |
| R43-6 | Кеш каталога — память процесса; при нескольких экземплярах API другой экземпляр видит старую цену до 60 с | На бою один контейнер; при масштабировании — распределённый кеш или сигнал (вне цикла) |
| R43-7 | Тестов на область нет (Р3); правка четырёх старых тестов | Матрица CY43-20…27 по всем линейкам и видам строк; реестр замен |
| R43-8 | Гонка «смена цены ↔ продление» | `expectedPricePerMonth` в продлении и `expected*` в смене цены; цена читается под замком аккаунта |
| R43-9 | Триал «Записей» выдаёт MAX без цены при опубликованной оферте (Р2, C40-12) | Поведение сохранено; строка честно показана как «Пробный период», 0 ₽ |
| R43-10 | Откат схемы теряет журнал цены | Схему не откатывать (`DEPLOY.md` §33); откат кода безопасен (§43.2.5) |
| R43-11 | `activePaidAccounts` считается в памяти по всем аккаунтам с опцией | Десятки строк сейчас; при росте — SQL-двойник правила с матричным тестом (§6.1) |
| R43-12 | Правовая сторона смены цены и досрочного отключения не решена (SPEC §7) | Трек юриста §43.15; отключение — P1 и режется вторым; тексты владельцу о смене цены не вводим (В3) |

---

## §43.15. Правовая часть — отдельный трек `legal-counsel` (кодирование не блокирует)

| # | Вопрос юристу (SPEC §7) | Что в коде ждёт ответа | Если ответ «нужно» |
|---|---|---|---|
| Ю-1 | Можно ли менять цену в любой момент; уведомлять ли владельцев с оплатой и за сколько; действует ли старая цена до конца периода (В3) | Ничего: периоды не пересчитываются, уведомлений нет | Отдельная задача: рассылка/плашка владельцу; экран цены добавит последствие «владельцы будут уведомлены» |
| Ю-2 | Нужен ли текст-предупреждение на экране смены цены и в «Подписке» владельца | Тексты последствий на экране цены — внутренние для админа (`API_CONTRACT_CYCLE43.md` §43.23.3); владельцу текста нет | Новый ключ правового текста вне `LegalTextKey.All` + запасной текст, поле в блоке `messengers` (добавочно) |
| Ю-3 | Досрочное отключение без возврата: когда можно, какой текст владельцу | Текст подтверждения для админа «Деньги не возвращаются…»; владелец видит только статус «Отключается …»/«Оплата закончилась …» | Текст владельцу в блоке и в разделе номеров; до ответа отключение использовать только по письменному основанию (`DEPLOY.md` §33) |
| Ю-4 | Хранить ли цену и сумму на момент подтверждения и сколько | Хранятся бессрочно (append-only журналы) | Правило retention в `Services/Retention/Rules/` (отдельная задача) |
| Ю-5 | Опубликована ли на бою редакция D3 и Приложения № 1 под MAX до ввода цены (В4, C40-12) | Порядок в `DEPLOY.md` §33 п. 4 | — |
| Ю-6 | Можно ли показывать цены мессенджеров на публичной сетке «Заказов» (А6) | Не показываются | Добавочное поле `messengerAddons` в `/api/pricing/orders` |

---

## §43.16. Отклонения от буквы SPEC (читать обязательно)

1. **Подключать и продливать можно только продаваемый мессенджер**, то есть нужна ещё и опубликованная оферта (Д-1). SPEC называл три
   условия (US-43-03 кр. 3, US-43-04 кр. 1). Причина: одно правило «продаётся» и правовая чистота.
2. **«Продлить» у владельца — только после окончания оплаты** (§43.8.2). Ещё оплаченный транспорт заявку в мастере не принимает
   (`CanRequestPayment`, замороженные векторы cycle40). Продление до срока делает админ из карточки аккаунта.
3. **Карточка аккаунта показывает обе строки мессенджеров всегда**, даже если мессенджер не продаётся и не подключён: так видно причину
   (US-43-03 кр. 1 + кр. 3).
4. **Добавлены тексты статусов** «Не подключён», «Пробный период закончился {дата}», «Оплачено» и «Оплата закончилась» без даты (для наследных строк).
5. **А5:** мессенджеры ушли из `options[]` и итога линейки у владельца и из `options[]` карточки аккаунта (переехали в `messengers`).
   Минимальная часть US-43-05 стала P0.
6. **Месяцы:** карточка аккаунта 1…24 (SPEC), «Подтвердить оплату» 1…12 (форма cycle40 не меняется).
7. **База продления** у триальной строки без даты — окончание триала аккаунта (`ChannelOptionFunding`), а не `AccountSubscriptions.PaidUntil`. Обычно совпадает.
8. **`GET /api/admin/plans`** не отдаёт правила мессенджеров и не считает их в `optionCoverage` (форма та же).
9. **Активность опции** редактируется на экране цены вместе с ценой (SPEC оставлял это на решение архитектора): иначе причину «опция
   неактивна» нечем исправить из интерфейса.

---

## §43.17. Открытые вопросы заказчику (кодирование не блокируют; по умолчанию — как в скобках)

- **Q43-1.** Показывать ли «+ MAX N ₽/мес» на публичной странице тарифов «Заказов» (нет, до ответа юриста Ю-6).
- **Q43-2.** Нужна ли выдача мессенджера «в подарок» отдельным действием, без временной цены 0 ₽ (нет, П6).
- **Q43-3.** Предупреждать ли владельца в его «Подписке», что оплата мессенджера скоро кончится, и вести ли на продление до срока
  (нет; потребует правки правила мастера и векторов cycle40 — отдельный цикл).
