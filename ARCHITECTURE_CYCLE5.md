# ARCHITECTURE — цикл 5 ServiceBooking: биллинг-аккаунт, каталог опций, публичный прайс

> **Редакция 2.1 (2026-09-22).** Написана под `SPEC.md` редакции 2, §0.1 (решения заказчика Р1…Р8).
> Редакция 1 (модель «подписка принадлежит компании») **отменена заказчиком** и достаётся из git;
> что изменилось — §63. **Редакция 2.1** учитывает ответы заказчика на пять вопросов §64: четыре
> приняты как спроектировано, один изменил решение — перенос компании умеет менять ответственного
> (§51, §64 п. 2).
>
> **Как этот файл соотносится с предыдущими.** `ARCHITECTURE.md` (цикл 3, §1–20) и
> `ARCHITECTURE_CYCLE4.md` (цикл 4, §21–40) **остаются в силе и не переписываются**. Этот файл —
> их продолжение, нумерация начинается с **§41**.
>
> **Ветка цикла:** `cycle/07-pricing-model-rework` (devops-нумерация веток; продуктовая нумерация —
> «цикл 5»). Отправная точка — `develop` @ `7ab28b2`.
>
> **Вход:** `SPEC.md` ред. 2 и `CURRENT_STATE.md` @ `7a36543`.
> **Выход для команды:** этот файл + `API_CONTRACT_CYCLE5.md` + `contracts/openapi-cycle5.yaml`.

---

## 41. Принципы цикла

1. **Единица тарификации — биллинг-аккаунт** (Р1). Новая сущность `BillingAccount`; подписка живёт
   на ней. Компания получает **второе** поле — `BillingAccountId` («кто платит»), при этом
   `OwnerUserId` («кто управляет») сохраняется и не меняет смысла. Разведение этих двух смыслов —
   главная работа цикла по риску (§45).
2. **Сегодняшний `AccountSubscription` не выбрасывается и не размножается.** Уникальный индекс
   `AccountSubscriptions.OwnerUserId` уже означает «один платящий аккаунт на человека» — это и есть
   будущий биллинг-аккаунт. Миграция — добавление FK, а не перенос строк (§54).
3. **Гейты не переписываются.** Точка разрешения одна — `SubscriptionResolver`, её результат —
   `EffectivePlan`. Опции **вливаются в** `EffectivePlan` до контроллера;
   `GetEffectivePlansAsync(companyIds)` сохраняет сигнатуру. Ни один существующий
   `if (!plan.AllowX) return 402` не трогается (§44).
4. **Смена смысла поля обязана ломать компиляцию.** `EffectivePlan.MaxEmployees` →
   `AccountMaxEmployees`, `MaxCompanies` → `AccountMaxCompanies`, `GetEffectivePlanForOwnerAsync`
   удаляется. Это не косметика: так «по инерции» остаться на старом смысле физически нельзя (§45.3).
5. **Новый канал рассылки = строка каталога, а не колонка.** Возможности адресуются **строковыми
   ключами** (`notifications.whatsapp`), а не булевыми полями (§44.2). US-76.
6. **Один источник правды об оплате номеров** — количество в опции подписки аккаунта.
   `NotificationChannel.PaidFromUtc/PaidUntilUtc` перестают читаться, `ChannelPaymentLog` перестаёт
   пополняться; обе сущности остаются историей (Р6, R2, §47).
7. **Публикация цен наружу — отдельный рубильник.** Пока `PlatformSetting pricing.public-enabled`
   не включён суперадмином, анонимного прайса не существует (404). Выкат кода физически не может
   опубликовать цены раньше юриста (SPEC §7, П5, R6).
8. **Тексты собирает сервер** (`Services/Billing/BillingTexts.cs`, как `NotificationTexts` цикла 4).
   Сюда же — словарь Р8: слово «биллинг-аккаунт» существует **только** в админских ответах и
   админском UI; во владельческих ответах его нет ни в одном поле и ни в одном тексте (§59, греп 7).
9. **Рост контроллеров останавливается вручную.** Новые эндпоинты идут в новые контроллеры
   (`PricingController`, `BillingController`, `AdminBillingController`), а не в `CompaniesController`
   (635 строк) и `AdminController` (R8).

**Что цикл сознательно не трогает:** слоты и бронирование, расписание, фото, правовой контур,
эксплуатационную обвязку, транспорт уведомлений и адаптер GREEN-API. `Notifications:Provider`
остаётся `logging` (US-74).

---

## 42. Стек: что добавляется

**Ничего.** Ни одного нового NuGet-пакета, ни одного нового npm-пакета в рантайме, ни одного нового
процесса, ни одной новой фоновой задачи, ни одной новой строки в `docker-compose.prod.yml`.

| Потребность цикла | Чем закрывается | Почему не новым инструментом |
|---|---|---|
| Кеш каталога тарифов/опций и публичного прайса | `IMemoryCache` (уже подключён), образец — `PlatformSettings`: **60 с + `InvalidateCache()`** | Redis на стенде с 3,3 ГиБ ОЗУ и одним инстансом — расход памяти и ещё один процесс для дежурства (R10, P0-1). Каталог — десятки строк |
| Расход суммарных лимитов аккаунта | **не кешируется вовсе**, два сгруппированных запроса (§46) | Кеш счётчика, по которому пропускают/не пропускают запись, — это разрешённое превышение лимита. Точность здесь дороже микросекунд |
| Деньги | `decimal(10,2)`, как `Service.Price` | Одна валюта (RUB), целые рубли в UI; денежная библиотека не окупается |
| Журнал изменений | существующий `SubscriptionChangeLog` + 5 колонок | Новый журнал денег = третий источник истории. История циклов 1–4 обязана остаться читаемой одним запросом |
| Журнал смены ответственного | **новая маленькая таблица `CompanyOwnerChangeLog`** | US-64 п. 4 прямо требует: смена ответственного пишется в журнал, **но в журнале подписки записи не появляется**. Смешать их — значит снова связать деньги и управление, ровно то, что цикл разводит |
| Рубильник публикации прайса | существующий `PlatformSetting` + `PlatformSettingChangeLog` | Механизм «параметр платформы с журналом и без пересборки» уже есть и протестирован |
| Машиночитаемый контракт | **OpenAPI 3.0.3 файлом в репозитории** (`contracts/openapi-cycle5.yaml`), Swashbuckle остаётся только для Dev-Swagger | Схема нужна **до** кода, чтобы FE и QA работали параллельно (§57) |
| Типы фронта из контракта | `openapi-typescript` — **devDependency, только генерация типов** | Полноценный кодоген клиента переписал бы все 20 модулей `src/api/*` — цена несоразмерна (R9, §56.3) |

**Протокол остаётся REST/JSON** поверх существующего `axios`-инстанса с `baseURL: '/api'`.
GraphQL/gRPC не рассматриваются: продукт — один SPA + один бэкенд, весь фронт и все тесты построены
на REST-конвенциях циклов 1–4 (форматы ошибок, `PagedResult`, `Optional<T>`). Смена протокола стоила
бы переписывания всего клиентского слоя и всех 449 функциональных тестов ради нуля новых
возможностей.

---

## 43. Модель данных (ответ на SPEC §9 пп. 1, 2)

### 43.1 Четыре понятия SPEC §3.1 → таблицы

| Понятие SPEC | Таблицы | Комментарий |
|---|---|---|
| **Биллинг-аккаунт** | `BillingAccount` (новая) | владелец денег и компаний |
| **Тариф (план)** | `SubscriptionPlanConfig` (расширяется), `PlanOptionRule` (новая) | таблица тарифов **не переезжает**: её читают `SubscriptionResolver`, админка и юнит-тесты |
| **Опция** | `SubscriptionOption` (новая) | каталог; строка = товар |
| **Подписка аккаунта** | `AccountSubscription` (**остаётся**, +1 колонка), `AccountSubscriptionOption` (новая) | что именно оплачено у аккаунта |
| Заявка владельца | `SubscriptionRequest`, `SubscriptionRequestItem` (новые) | US-70, §49 |
| Журнал денег | `SubscriptionChangeLog` (+5 колонок) | один журнал на старое и новое |
| Журнал управления | `CompanyOwnerChangeLog` (новая) | US-64 п. 4 |

### 43.2 Две оси компании — и почему они обе нужны

```
BillingAccount ──1:1── AccountSubscription ──*── AccountSubscriptionOption
      │                                              (Quantity: компании, сотрудники, номера)
      ├──*── Company.BillingAccountId     «кто платит»   → тариф, опции, лимиты
      │           └── Company.OwnerUserId «кто управляет» → права, видимость, роли
      └──*── NotificationChannel.BillingAccountId «номер принадлежит аккаунту»
```

**Правило, которое обязано выдерживаться везде:**
`BillingAccountId` отвечает на вопрос *«по каким правилам живёт»*, `OwnerUserId` — на вопрос
*«кто за это отвечает и кто это видит»*. Ни одно место в коде не имеет права спрашивать одно, имея
в виду другое. Полная ревизия сегодняшних чтений — §45.

Две оси **независимы, но не изолированы**: единственная операция, которая имеет право двигать обе
сразу, — перенос компании между аккаунтами (§51), и она делает это в одной транзакции и по явному
указанию администратора.

### 43.3 Новые сущности

**`BillingAccount`** (`ServiceBooking.Core/Entities/BillingAccount.cs`)

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | Guid | |
| `OwnerUserId` | string, **уникальный индекс** | держатель аккаунта = плательщик. Уникальность фиксирует «один аккаунт на человека» — ровно то, что сегодня даёт уникальный индекс `AccountSubscriptions.OwnerUserId` |
| `Name` | string(100)? | подпись **только для админки** («Аккаунт Иванова»); владельцу не показывается (Р8) |
| `GrandfatheredEmployeeBonus` | int, дефолт 0 | места сотрудников, выданные **миграцией** при переходе «на компанию → суммарно» (§54.4). Не продаётся, в сумму к оплате не входит, виден только админу. Решение заказчика принято (§64 п. 1) |
| `CreatedAtUtc`, `UpdatedAtUtc` | DateTime | |

Аккаунт заводится **по требованию**, а не при регистрации каждого пользователя:
`BillingAccountProvisioner.EnsureAccountAsync(userId)` вызывается при создании первой компании и при
первом назначении подписки. Это сознательное расхождение с буквой SPEC §3.1, подтверждённое
заказчиком, — развёрнутое обоснование в §61 п. 1.

**`SubscriptionOption`** — каталог опций

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | Guid | |
| `Code` | string(64), **уникальный индекс** | машинный код (`extra-companies`, `extra-employees`, `notifications.whatsapp`); после создания не меняется (400) |
| `Name` | string(100) | «Рассылки в WhatsApp» |
| `Description` | string(500)? | человеческий текст для владельца и публичной страницы |
| `Kind` | `OptionKind` | `Toggle` / `Quantity` |
| `CapabilityKey` | string(64)? | **что опция даёт** (§44.2). `null` = ничего не включает (чистая строка прайса) |
| `PricePerMonth` | decimal(10,2)? | **`null` = опция не продаётся** — та же конвенция, что у `notifications.channel.price-per-month` в цикле 4 |
| `UnitName` | string(32)? | «компания», «сотрудник», «номер» — обязателен для `Quantity` (400 без него), запрещён для `Toggle` |
| `MaxQuantity` | int? | потолок при покупке, `null` = без потолка |
| `IsPublic` | bool, дефолт **false** | показывать ли на публичной странице |
| `IsActive` | bool, дефолт true | снята ли с продажи |
| `SortOrder` | int, дефолт 0 | порядок показа |
| `CreatedAtUtc`/`UpdatedAtUtc` | DateTime | |

**`PlanOptionRule`** — доступность опции на тарифе (US-66, SPEC §3.3 п. 7)

`Id`, `PlanConfigId` (FK Cascade), `OptionId` (FK Cascade), `Availability: OptionAvailability`,
`IncludedQuantity int?`; **уникальный индекс `(PlanConfigId, OptionId)`**.

**Отсутствие строки = `Unavailable`** — fail-closed, как решение Q1 цикла 4
(`AllowNotificationChannel = false` у всех планов, включая новые). Новый тариф не начинает молча
продавать опции, которые администратор ему не открывал.

**`AccountSubscriptionOption`** — что оплачено у аккаунта

| Поле | Смысл |
|---|---|
| `Id`, `BillingAccountId` (FK Cascade), `OptionId` (FK Restrict) | **уникальный индекс `(BillingAccountId, OptionId)`** — одна строка на опцию у аккаунта |
| `Quantity int` | ≥1; для `Toggle` всегда 1 |
| `PaidUntilUtc DateTime?` | `null` = «до конца оплаченного периода подписки»; значение = собственный срок |
| `EndsAtUtc DateTime?` | проставляется при отключении/уменьшении: действует **до этой даты** (SPEC §3.3 п. 9), потом строка перестаёт быть активной, но остаётся историей |
| `ActivatedAtUtc`, `ActivatedByUserId` | кто и когда включил |
| `RequestedQuantity int?`, `RequestedAtUtc?`, `RequestedByUserId?` | ожидание подтверждения оплаты (US-70); заполняется заявкой, обнуляется при назначении |

**`SubscriptionRequest`** / **`SubscriptionRequestItem`** — §49.

**`CompanyOwnerChangeLog`** — журнал смены ответственного (US-64 п. 4)

`Id`, `CompanyId` (FK Cascade, индекс), `OldOwnerUserId`, `NewOwnerUserId`, `ChangedByUserId`,
`ChangedAtUtc`, `Comment string(500)?`, `WithTransfer bool` (дефолт false — смена произошла в рамках
переноса компании, §51.3).
**При самостоятельной смене ответственного в журнал подписки не пишется ничего** — это прямое
требование US-64 и главный видимый признак того, что деньги не тронуты.

### 43.4 Изменения существующих таблиц

| Таблица | Изменение | Почему аддитивно |
|---|---|---|
| `Company` | **+`BillingAccountId Guid`** (FK Restrict, индекс, NOT NULL после backfill); **+альтернативный ключ `(Id, BillingAccountId)`** (§43.6). `OwnerUserId` — **без изменений** | вся видимость и права продолжают работать на старом поле |
| `AccountSubscription` | **+`BillingAccountId Guid`** (FK Cascade, **уникальный индекс**). `OwnerUserId` остаётся колонкой-историей: бизнес-логика его больше не читает, но он нужен сверке миграции (§54.5) и разбору инцидентов | таблица, план, срок, `IsActive` — те же; миграция подписок сводится к проставлению FK |
| `NotificationChannel` | **+`BillingAccountId Guid`** (FK Restrict, индекс); **+альтернативный ключ `(Id, BillingAccountId)`**. `OwnerUserId` остаётся как «кто завёл и управляет номером». `PaidFromUtc/PaidUntilUtc` — **историчны**, не читаются (§47) | номер принадлежит аккаунту (Р6), человек — только управляет им |
| `ChannelCompanyAssignment` | **+`BillingAccountId Guid`** + два композитных FK (§43.6) | делает «чужая компания на чужом номере» невозможной на уровне БД |
| `SubscriptionPlanConfig` | **+6 колонок**: `Description string(1000)?`, `Highlights string(500)?`, `IsPublic bool = false`, `SortOrder int = 0`, `IsSystemFree bool = false`. `MaxEmployees`/`MaxCompanies` **остаются колонками, но меняют смысл на «суммарно по аккаунту»** (Р4, Р5) | ни одна колонка не удаляется; смысл `MaxEmployees` меняется — единственное нетождественное место миграции (§54.4) |
| `SubscriptionChangeLog` | **+5 колонок**: `BillingAccountId Guid?` (индекс), `CompanyId Guid?` (индекс; заполняется только у `CompanyTransferred`), `ChangeKind` (дефолт `Legacy = 0`), `OldOptionsSummary string(500)?`, `NewOptionsSummary string(500)?`. `OwnerUserId` старых строк не трогается | история циклов 1–4 читается тем же запросом |

`ChannelPaymentLog` — **без изменений схемы**, переходит в статус «историческая таблица, новые строки
не пишутся» (прецедент — `NotificationChannel.ContactEmail`).

- **`IsSystemFree`** — ровно одна строка `SubscriptionPlanConfig` может быть `true` (частичный
  уникальный индекс `WHERE "IsSystemFree"`). Это «Бесплатный» тариф из П4: публикуемая строка
  прайса, которая **обязана совпадать** с поведением по умолчанию (`MaxCompanies = 1`,
  `MaxEmployees = 1`; публикация подтверждена заказчиком, §64 п. 5). Его нельзя удалить и
  деактивировать (409), `PricePerMonth` обязан быть 0 (400). Если строки нет — резолвер падает на
  константу `EffectivePlan.Free`, которая **остаётся в коде** как последний рубеж.

> **Решение и его цена.** Состав опций пишется в журнал **текстовым снимком**
> («WhatsApp ×1, Доп. сотрудники ×3»), а не нормализованной таблицей строк журнала. Журнал читает
> человек (админка, разбор спора), а не код; нормализация добавила бы таблицу и join ради запроса,
> которого ни у кого нет. Цена: по журналу нельзя автоматически восстановить состояние на дату.
> Если такое требование появится — это отдельная таблица снимков, а не переделка этой.

### 43.5 Новые перечисления (значения фиксируются сразу, append-only с этого момента)

```
OptionKind                 Toggle = 0, Quantity = 1
OptionAvailability         Unavailable = 0, Included = 1, Extra = 2
SubscriptionStatus         Free = 0, Active = 1, Expired = 2
SubscriptionRequestStatus  Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3
SubscriptionChangeKind     Legacy = 0, Plan = 1, Options = 2, Payment = 3,
                           CompanyTransferred = 4, Migration = 5
ChannelFundingState        Funded = 0, Unfunded = 1, NotPaid = 2
```

`Legacy = 0` не случайно: колонка добавляется к существующим строкам со значением по умолчанию 0,
и это значение должно честно означать «строка из до-цикла-5, вид изменения неизвестен».
`PayerChanged` из прошлой редакции **удалён из перечисления до его выпуска**: смены плательщика как
события подписки больше не существует (Р2), а смена ответственного пишется в другой журнал.

### 43.6 Ко-тенантность номера и компании — на уровне БД (SPEC §3.3 п. 5)

«Номер принадлежит аккаунту и обслуживает **только его** компании» — правило, которое легко забыть
в редко исполняемом коде переноса (R5). Поэтому оно фиксируется схемой, а не только проверкой:

- `Companies` получает альтернативный ключ `(Id, BillingAccountId)`;
- `NotificationChannels` получает альтернативный ключ `(Id, BillingAccountId)`;
- `ChannelCompanyAssignments` получает колонку `BillingAccountId` и **два составных FK**:
  `(CompanyId, BillingAccountId) → Companies(Id, BillingAccountId)` и
  `(ChannelId, BillingAccountId) → NotificationChannels(Id, BillingAccountId)`.

EF Core выражает это `HasForeignKey(a => new { a.CompanyId, a.BillingAccountId })` +
`HasPrincipalKey(c => new { c.Id, c.BillingAccountId })`.

**Следствие, на которое рассчитываем:** перенос компании в другой аккаунт (US-77) физически не может
пройти, пока не удалена строка назначения на номер. Забыть этот шаг нельзя — база не даст. Это
дешевле любого теста на редкий сценарий.

**Цена:** два уникальных индекса на существующих таблицах и один столбец на маленькой таблице
назначений; миграция обязана убедиться, что кросс-аккаунтных назначений сегодня нет (§54.2) —
иначе создание FK упадёт на выкате.

**`CompanyMember` эти FK не касаются** — у членства нет `BillingAccountId`. Это важно для §51:
совмещённый перенос со сменой ответственного пишет строки `CompanyMembers` без оглядки на
композитные ключи, и единственное ограничение на порядок операций остаётся прежним — снять с номера
раньше, чем сменить `BillingAccountId`.

---

## 44. Разрешение возможностей — главное решение цикла

### 44.1 `EffectivePlan` остаётся валютой гейтов

```
Company ──► BillingAccountId ──► AccountSubscription + AccountSubscriptionOption[] + PricingCatalog + now
                                              │
                                              ├─► Dictionary<string,int> Capabilities
                                              │
                                              └─► EffectivePlan (тот же record, те же гейты)
```

`SubscriptionResolver.GetEffectivePlansAsync(IEnumerable<Guid> companyIds)` — **сигнатура не
меняется**, меняется реализация. Все существующие вызовы в `CompaniesController`,
`BookingsController`, `ReportsController`, `MailingController`, `ClientNotePhotosController` и
`PhotoQuota` продолжают работать без правок. Это и есть механика US-73: путь от данных до гейта
остался один.

**Новая форма `EffectivePlan`** (два переименования — намеренно ломающие компиляцию, §41 п. 4):

```csharp
public record EffectivePlan(
    bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics,
    bool AllowPublicListing, bool AllowOnlinePayment,
    int? AccountMaxEmployees,        // ← было MaxEmployees «на компанию»; теперь СУММАРНО по аккаунту
    int? AccountMaxCompanies,        // ← было MaxCompanies; смысл тот же, имя честнее
    int? PhotoQuotaMb, PhotoRetention PhotoRetention,
    bool AllowNotificationChannel,   // тариф ДОПУСКАЕТ покупку номеров (Extra/Included)
    int PaidNotificationNumbers,     // сколько номеров ОПЛАЧЕНО аккаунтом (0 = ни одного)
    IReadOnlyDictionary<string,int> Capabilities); // все ключи, включая ещё не выпущенные каналы
```

`EffectivePlan.Free` остаётся константой: `AccountMaxEmployees: 1, AccountMaxCompanies: 1,
PaidNotificationNumbers: 0`, `AllowPublicListing: true` — сегодняшнее поведение дословно.

Методы резолвера:

| Было | Стало | Почему |
|---|---|---|
| `GetEffectivePlansAsync(companyIds)` | **без изменений** | опора всего цикла |
| `GetEffectivePlanAsync(companyId)` | **без изменений** | внутри идёт через `BillingAccountId` |
| `GetEffectivePlanForOwnerAsync(ownerUserId)` | **удаляется** | «план человека» — это ровно та модель, которую цикл отменяет. Компилятор найдёт все 6 вызовов (§45.1) |
| `GetEffectivePlansForOwnersAsync(ownerUserIds)` | **удаляется** | заменяется на `GetEffectivePlansForAccountsAsync` |
| — | **`GetEffectivePlanForAccountAsync(Guid accountId)`** 🆕 | нужен `CompaniesController.Create`, экрану владельца и переносу (§51) |
| — | **`GetEffectivePlansForAccountsAsync(IEnumerable<Guid>)`** 🆕 | нужен админской сводке каналов цикла 4 |
| `Resolve(AccountSubscription?, DateTime)` | заменяется чистой функцией `BillingCalculator.Calculate` (§44.3); `SubscriptionResolverRulesTests` переписываются (B5-3) | вся арифметика опций — без БД |

### 44.2 Возможность — это строковый ключ, а не колонка

`Services/Billing/CapabilityKeys.cs`:

```
булевы:   online-booking · mailing · analytics · public-listing · online-payment
числовые: employees · companies · photo-quota-mb
          notifications.whatsapp · notifications.max · notifications.sms   ← значение = ЧИСЛО ОПЛАЧЕННЫХ НОМЕРОВ
```

Канал рассылки — **числовой** ключ, а не булев, и это принципиально: единица оплаты — номер (Р6),
поэтому «оплачено 2 номера» должно выражаться числом, а не флагом. Булево «можно ли вообще
пользоваться WhatsApp» — производное: `Capabilities["notifications.whatsapp"] >= 1`.

`Services/Billing/PlanCapabilityMap.cs` — единственное место, где колонки тарифа превращаются в ключи:

| Колонка `SubscriptionPlanConfig` | Ключ |
|---|---|
| `AllowOnlineBooking` | `online-booking` |
| `AllowMailing` | `mailing` |
| `AllowAnalytics` | `analytics` |
| `AllowPublicListing` | `public-listing` |
| `AllowOnlinePayment` | `online-payment` |
| `MaxEmployees` | `employees` (число, `null` = без ограничения) |
| `MaxCompanies` | `companies` (число, `null` = без ограничения) |
| `PhotoQuotaMb` | `photo-quota-mb` |
| `AllowNotificationChannel` | **не даёт номеров**: определяет только, `Extra` или `Unavailable` правило `notifications.whatsapp` при сидировании (§54.3) |

Опция с `CapabilityKey = "notifications.max"` и `UnitName = "номер"` продаётся, считается в сумме,
показывается на публичной странице и в админке **без единой правки схемы и гейтов** — это и есть
приёмка US-76. Отправлять в MAX она, разумеется, не начнёт: для этого нужен член
`NotificationTransport` и адаптер. Поштучная тарификация (будущий SMS) добавляется той же строкой
каталога с другим `UnitName` («сообщение») и **не требует** менять модель — но считать сообщения
цикл не умеет и не делает вид, что умеет (Р7).

**Неизвестный ключ** (опечатка или ключ из будущего): опция продаётся и считается в сумме, но ничего
не включает. Админка показывает предупреждение (`GET /api/admin/option-capabilities` отдаёт список
известных ключей) и **не запрещает** ввод — иначе завести опцию под ещё не выпущенный транспорт
станет невозможно без деплоя.

### 44.3 Чистая функция — вся логика цикла, тестируемая без БД

`Services/Billing/BillingCalculator.cs`:

```csharp
public static AccountBilling Calculate(
    AccountSubscriptionSnapshot? subscription,      // план + PaidUntil + IsActive + GrandfatheredEmployeeBonus
    IReadOnlyList<AccountOptionSnapshot> options,   // OptionId, Quantity, PaidUntil, EndsAt
    PricingCatalog catalog,                         // тарифы, опции, правила — из кеша
    DateTime nowUtc);

public sealed record AccountBilling(
    EffectivePlan Plan,                 // ← то, что идёт в гейты всех компаний аккаунта
    SubscriptionStatus Status,          // Free | Active | Expired
    Guid? PlanId, string PlanName, decimal PlanPrice,
    IReadOnlyList<BilledOption> Options,
    decimal TotalPerMonth,              // PlanPrice + Σ(price × quantity) по Extra-опциям
    DateTime? PaidUntil,
    int? ExpiresInDays,                 // US-68, от NotifyDaysBefore тарифа
    bool IsExpiringSoon);
```

**Правила, зашитые в `Calculate` (все — из SPEC §3.3, покрыты юнит-тестами):**

1. Подписка «годна», если `IsActive && (PaidUntil == null || PaidUntil >= now)` **и** её
   `PlanConfig.IsActive` — правило цикла 1 сохраняется дословно.
2. Негодная подписка → системный бесплатный тариф (или константа `EffectivePlan.Free`).
   Опции при этом гасятся вместе с ней: опция не может пережить тариф, на котором куплена.
3. Опция активна, если: строка есть, `Quantity > 0`, `(EndsAtUtc == null || EndsAtUtc >= now)`,
   `(PaidUntilUtc ?? subscription.PaidUntil) >= now` (или обе `null`), и правило тарифа для неё —
   `Included` или `Extra`. **Правило `Unavailable` гасит оплаченную опцию** — иначе понижение тарифа
   молча оставило бы возможность, за которую тариф не отвечает.
4. `Included` — количество `IncludedQuantity ?? 1`, цена **0**. `Extra` — количество из строки
   аккаунта, цена `option.PricePerMonth × Quantity`.
5. **Опция с `PricePerMonth == null` не продаётся, но уже оплаченная строка продолжает действовать
   до конца своего оплаченного периода** (цена считается как 0). Сознательная асимметрия с правилом
   деактивированного тарифа: снятие опции с продажи — действие каталога, а не клиента, и отбирать у
   платящего оплаченное посреди периода — ровно та авария, ради устранения которой затеян цикл.
   Асимметрия названа явно, чтобы её не «починили» как баг.
6. Числовые ключи **складываются**: `employees = plan.MaxEmployees + Σ quantity + account.GrandfatheredEmployeeBonus`;
   `companies = plan.MaxCompanies + Σ quantity`; `notifications.whatsapp = Σ quantity`.
   Если базовое значение `null` (без ограничения) — остаётся `null`, докупка ничего не меняет.
7. Булевы ключи — **ИЛИ**: тариф дал или опция дала.
8. `GrandfatheredEmployeeBonus` участвует в лимите, но **не участвует в `TotalPerMonth`** и не
   показывается владельцу отдельной строкой: для него это просто «сотрудников 12 из 15».

### 44.4 Никакого N+1 на разрешении плана (требование SPEC §6)

Один вызов `GetEffectivePlansAsync(companyIds)` = **две** выборки из БД:

1. `Companies ⋈ AccountSubscriptions ⋈ SubscriptionPlanConfigs` по `Company.Id IN (…)` →
   `(companyId, billingAccountId, planConfigId, paidUntil, isActive, bonus)`;
2. `AccountSubscriptionOptions` по `BillingAccountId IN (…)`.

Каталог (тарифы + опции + правила) — целиком из `IMemoryCache` (60 с, §48). Сегодня там один запрос;
прибавка на списках `/api/companies`, `/api/companies/my`, `/api/admin/companies` — **ровно один
запрос на список**, не на компанию. Аккаунтов на странице меньше, чем компаний (мультифилиал
схлопывается), так что вторая выборка на практике короче первой.

---

## 45. Ревизия чтений `Company.OwnerUserId` (ответ на SPEC §9 п. 2, риск R1)

Это главная работа цикла по риску. Ниже — **полный** инвентарь сегодняшних чтений (получен
`rg "OwnerUserId" ServiceBooking.API/`, 2026-09-22, ревизия `7ab28b2`). Каждая строка имеет вердикт;
строк без вердикта не остаётся — это и есть ответ на вопрос «как гарантировать, что ни одно место не
осталось на старом поле по инерции».

### 45.1 Деньги — переезжают на `BillingAccountId`

| Файл:строка | Сегодня | Становится |
|---|---|---|
| `SubscriptionResolver.cs:71` | `Companies.Where(Id==companyId).Select(OwnerUserId)` | `Select(BillingAccountId)` |
| `SubscriptionResolver.cs:85,88,93` | пачка компаний → владельцы → планы | пачка компаний → аккаунты → планы (§44.4) |
| `SubscriptionResolver.cs:103–120` | `GetEffectivePlansForOwnersAsync` | `GetEffectivePlansForAccountsAsync(Guid[])` |
| `NotificationScheduler.cs:105` | `GetEffectivePlanForOwnerAsync(company.OwnerUserId)` | `GetEffectivePlanAsync(company.Id)` |
| `CompanyNotificationsController.cs:38,61,123` | то же, ×3 | `GetEffectivePlanAsync(company.Id)` |
| `NotificationChannelsController.cs:86,143` | `GetEffectivePlanForOwnerAsync(userId)` для проверки «тариф допускает канал» | `GetEffectivePlanForAccountAsync(accountOf(userId))` |
| `NotificationChannelsController.cs:45,50,97,325,385,629` | `channel.OwnerUserId` как «чей номер» | остаётся как «кто управляет номером», **но принадлежность и оплата** — `channel.BillingAccountId` (§47) |
| `CompaniesController.cs:220–225` | `GetEffectivePlanForOwnerAsync(userId)` + `Count(c.OwnerUserId == userId)` для лимита компаний | `GetEffectivePlanForAccountAsync(accountId)` + `Count(c.BillingAccountId == accountId)` |
| `AdminController.cs:84–105` | сводка пользователей: подписки и число компаний по `OwnerUserId` | по `BillingAccountId` аккаунта пользователя |
| `AdminController.cs:160–175` | список компаний админки: подписка по `c.OwnerUserId` | по `c.BillingAccountId` |
| `AdminController.cs:205–242` | назначение подписки владельцу | переезжает в `AdminBillingController` на `accountId`; старый маршрут → **410** (§50, контракт §54) |
| `NotificationDispatchTask.cs:97,125` | планы по `channel.OwnerUserId` | по `channel.BillingAccountId` |

### 45.2 Права и видимость — **не трогаются**

| Файл:строка | Что делает | Почему остаётся |
|---|---|---|
| `ProfileController.cs:206` | `Companies.AnyAsync(c.OwnerUserId == userId)` — «он вообще владелец?» | вопрос про управление, не про деньги |
| `ProfileController.cs:302` | каналы пользователя по `channel.OwnerUserId` | «номера, которыми я управляю» |
| `NotificationChannelsController.cs:436` | `company.OwnerUserId != userId → 403` | проверка прав |
| `NotificationChannelsController.cs:629` | `channel.OwnerUserId == userId` | проверка прав на номер |
| `AdminController.cs:525–531` | показ email владельца в списке компаний | отображение ответственного |
| `AdminController.cs:290–353` | смена ответственного (US-64) | §50 |
| `CompaniesController.cs:239` | `OwnerUserId = userId` при создании | остаётся **и** дополняется `BillingAccountId = accountId` |
| `ChannelHealthTask.cs:204,393` | `ownerUserId` в логах | диагностика; дополняется `billingAccountId` |
| `AppDbContext.cs:51,96,97,103,200,201` | FK и индексы | остаются; добавляются новые (§43.4) |

### 45.3 Три механизма, которые не дают «остаться на старом поле по инерции»

1. **Удаление методов.** `GetEffectivePlanForOwnerAsync` / `GetEffectivePlansForOwnersAsync`
   удаляются. Все 6 вызовов не скомпилируются; пропустить невозможно.
2. **Переименование полей `EffectivePlan`.** `MaxEmployees → AccountMaxEmployees`,
   `MaxCompanies → AccountMaxCompanies`. Тесты и контроллеры используют именованные аргументы —
   компилятор находит каждый. Смена смысла поля без смены имени была бы тихой бомбой: код
   продолжил бы читаться правильно и работать неправильно.
3. **Смена сигнатуры гейта уведомлений.** `NotificationGate.Evaluate` получает новый обязательный
   параметр `bool channelIsFunded` вместо внутреннего чтения `ChannelPaymentState.Of(channel, now)`
   (§47.2) — каждый вызывающий обязан явно сказать, откуда он взял оплату.
4. **Грепы приёмки** (§59) — страховка, а не основной механизм.

---

## 46. Суммарные лимиты аккаунта и производительность (ответ на SPEC §9 п. 5, риск R11)

Суммарные лимиты теперь нужны в трёх местах: в гейте добавления сотрудника, в гейте создания
компании и на экранах (US-65, US-69). Наивная реализация даёт N+1 на `/api/companies`,
`/api/companies/my`, `/api/admin/companies`.

### 46.1 `Services/Billing/AccountUsageReader.cs` — два сгруппированных запроса, и всё

```csharp
public sealed record AccountUsage(Guid BillingAccountId, int CompaniesUsed, int SeatsUsed);

Task<Dictionary<Guid, AccountUsage>> GetAsync(IEnumerable<Guid> accountIds);   // 1 запрос
Task<Dictionary<Guid, int>>          GetCompanySeatsAsync(IEnumerable<Guid> companyIds); // 1 запрос
```

```sql
-- GetAsync: расход по аккаунтам страницы, один проход
SELECT c."BillingAccountId",
       COUNT(DISTINCT c."Id")        AS companies_used,
       COUNT(cm."Id")                AS seats_used
FROM "Companies" c
LEFT JOIN "CompanyMembers" cm ON cm."CompanyId" = c."Id"
WHERE c."BillingAccountId" = ANY(@accountIds)
GROUP BY c."BillingAccountId";
```

Второй запрос (`GROUP BY cm."CompanyId"` по компаниям страницы) даёт `employeeCount` для карточки
компании. Тот же приём, что `GetReviewAggregatesAsync` и `GetCitiesAsync` цикла 4 — прибавка к
списку **два запроса, а не 2N**.

Индексы: новый `Companies(BillingAccountId)`; существующего уникального
`CompanyMembers(CompanyId, UserId)` достаточно как префикса для группировки — новый индекс не нужен.

### 46.2 Горячий анонимный путь не платит ничего

`accountSeatsUsed`, `accountSeatsLimit`, `canAddEmployee` в `CompanyDto` заполняются **только там,
где вызывающий управляет компанией** — `/api/companies/my`, `/api/companies/member-of`,
`/api/admin/companies`, `GET /api/companies/{id}` для менеджера. На публичном
`GET /api/companies` (каталог салонов, самый горячий и анонимный список) эти поля приходят `null`, и
`AccountUsageReader` там **не вызывается вовсе**: анонимный посетитель не гасит кнопок.
Это, а не кеш, — главный ответ на R11.

### 46.3 Почему расход лимитов не кешируется

Счётчик, по которому принимается решение «пропустить запись или нет», не может быть устаревшим:
60-секундный кеш здесь означает 60 секунд, в течение которых лимит можно превысить. Оба запроса —
индексные группировки по десяткам строк. **Кешируется только каталог** (§48), он меняется раз в
месяц и не участвует в проверке лимита.

### 46.4 Гейты

| Гейт | Сегодня | Становится |
|---|---|---|
| `CompaniesController.AddMember` | `Count(members of company) >= plan.MaxEmployees` | `usage.SeatsUsed(account) >= plan.AccountMaxEmployees`; текст 402 — `BillingTexts.SeatLimitReached(used, planIncluded, purchased, bonus)`: «Занято 8 из 8 мест: 5 включено в тариф «Базовый», 3 докуплено. Лимит общий на все ваши точки.» |
| `CompaniesController.Create` | `Count(companies of owner) >= plan.MaxCompanies` | `usage.CompaniesUsed(account) >= plan.AccountMaxCompanies`; текст — `BillingTexts.CompanyLimitReached(...)` |
| `POST /api/admin/companies/{id}/transfer` | — | тот же расчёт для **принимающего** аккаунта, с поправкой на нового ответственного, если он добавляется в штат (§51.2) |

Коды не меняются: 402, как сегодня. Фронт гасит кнопку заранее по `canAddEmployee` (§53.1).

---

## 47. Один источник правды об оплате номеров (ответ на SPEC §9 пп. 3, 4; риск R2)

### 47.1 Оплачено N номеров, заведено M — детерминированное правило (новый вопрос §9 п. 4)

`N` = `EffectivePlan.PaidNotificationNumbers` (количество в опции `notifications.whatsapp` подписки
аккаунта). `M` = число «живых» строк `NotificationChannel` аккаунта (состояние ≠ `Replaced`).

**Правило: оплату получают N номеров, заведённых раньше остальных.**
Порядок — `CreatedAt ASC`, при равенстве — `Id ASC` (устойчивая сортировка, не зависящая от текущего
состояния канала и от порядка выборки). Первые N — `Funded`, остальные — `Unfunded`. При `N = 0` все
номера аккаунта — `NotPaid`.

```csharp
// Services/Billing/ChannelFunding.cs — чистая функция, юнит-тесты без БД
public static IReadOnlyDictionary<Guid, ChannelFundingState> Rank(
    IReadOnlyList<NotificationChannel> accountChannels, int paidNumbers);
```

**Почему «раньше заведённые», а не любой другой вариант:**

| Вариант | Почему отвергнут |
|---|---|
| новые вытесняют старые | владелец заводит второй номер — и **работающий** первый молча умирает вместе с очередью сообщений клиентам. Ровно та авария, ради устранения которой затеян цикл |
| администратор выбирает руками | новый экран, новое поле состояния и новый способ забыть его проставить; «редко исполняемый код» — риск R5 |
| все номера выключаются | наказание пользователя за расхождение бухгалтерии, которое он не создавал |
| «первый подключённый» (по `ConnectedAtUtc`) | состояние канала меняется само (переподключение, `Replaced`), и множество оплаченных номеров начинает «плавать» между запросами — недетерминированно |

Правило **стабильно**: добавление, отключение и переподключение номера не меняет того, какие номера
оплачены. Меняет только покупка/уменьшение количества и удаление старого номера.

**Что делает `Unfunded`-номер:** ничего не отправляет (гейт блокирует с существующей причиной
`NotOnPaidPlan`), но **не теряет** назначенные компании, не меняет своё состояние подключения и не
удаляется. Как только администратор увеличит количество — номер оживёт без переподключения.

**Текст для пользователя — обязательное требование, а не украшение.** Это единственное место цикла,
где платформа сама решает, чей номер замолчит, поэтому `fundingText` (`BillingTexts.FundingText`)
обязан содержать три вещи: **что происходит, почему именно так и что сделать**. Без слова
«биллинг-аккаунт» (Р8):

| Состояние | Текст |
|---|---|
| `Funded` (и номеров не больше, чем оплачено) | «Номер оплачен и работает.» |
| `Funded`, но `M > N` | «Номер работает: оплачен 1 номер из 2 заведённых, и он подключён раньше второго.» |
| `Unfunded` | «Этот номер не отправляет сообщения: у вас оплачен 1 номер, а заведено 2. Работает тот, что подключён раньше (+7 999 ***-**-45). Чтобы включить и этот, подключите ещё одну «Рассылку в WhatsApp» — или удалите лишний номер.» |
| `NotPaid` | «Рассылки не отправляются: опция «Рассылки в WhatsApp» не оплачена. Чтобы включить, подключите её в разделе «Ваша подписка».» |

Приёмка US-77/Р6 включает проверку: в тексте `Unfunded` названы **и причина (сколько оплачено против
скольких заведено), и какой номер работает вместо этого, и оба способа это исправить**.

**Что видит администратор:** в карточке аккаунта — «оплачено 1, заведено 2», в списке каналов —
`fundingState` по каждому номеру, отсортированному в том же порядке, в каком распределяется оплата.

### 47.2 Что меняется в гейте уведомлений

`NotificationGate.Evaluate` теряет чтение оплаты канала и получает её параметром:

```csharp
// БЫЛО                                          // СТАЛО
if (!plan.AllowNotificationChannel)              if (plan.PaidNotificationNumbers == 0)
    return Block(NotOnPaidPlan);                     return Block(NotOnPaidPlan);
…                                                …
if (ChannelPaymentState.Of(channel, nowUtc)      if (!channelIsFunded)
        != ChannelPaymentStatus.Paid)                return Block(NotOnPaidPlan);
    return Block(NoUsableChannel);
```

`channelIsFunded` считает вызывающий (`NotificationScheduler`, `NotificationDispatchTask`) через
`ChannelFunding.Rank` — на пачке каналов, без дополнительного запроса на сообщение.
`NotificationReason` **не трогается** (значения append-only): `NotOnPaidPlan` честно покрывает оба
случая «аккаунт не платил» и «этот номер сверх оплаченных». `ChannelPaymentState`/`ChannelPaymentStatus`
остаются в коде только для показа исторических данных в админке; в гейте их вызовов не остаётся —
проверяется грепом (§59).

### 47.3 Что происходит с полями канала

| Что | Решение |
|---|---|
| `NotificationChannel.PaidFromUtc/PaidUntilUtc` | колонки остаются, **не пишутся и не читаются** бизнес-логикой (прецедент `ContactEmail`). Удаление — уборочная миграция следующего цикла, чтобы разбор «кто и до какого числа был оплачен» ещё год оставался возможен |
| `ChannelPaymentLog` | таблица остаётся, новые строки **не пишутся** (US-73: историю не удаляем) |
| `POST /api/admin/notification-channels/{id}/payment` | **410 Gone** с указанием замены (`PUT /api/admin/billing-accounts/{accountId}/subscription`) |
| `POST /api/notification-channels` (заявка владельца) | остаётся, но теряет платёжный смысл: `RequestedAtUtc` означает «хочу завести номер», деньги — через заявку на опцию (§49) |
| `ChannelDto.paymentState/paidFrom/paidUntil` | **вычисляются из подписки аккаунта**: `paymentState` — из `ChannelFundingState`; `paidUntil` — срок опции `notifications.whatsapp`; `paidFrom` → всегда `null`, deprecated. Форма ответа не меняется, меняется источник |
| проверка при `Connect` (`NotificationChannelsController:148`) | `ChannelPaymentState.Of(...)` → `ChannelFunding` по аккаунту: подключить можно только оплаченный номер |

### 47.4 Смена ответственного и номер (US-64 п. 3) — отмена поведения цикла 4

Блок `AdminController.UpdateCompanyOwner:328–344` (удаление `ChannelCompanyAssignment` и отмена
очереди сообщений) **удаляется целиком**. Обоснование: номер принадлежит аккаунту, а не человеку;
смена ответственного не выводит компанию из аккаунта, значит и с номера снимать её не за что.
Это осознанная отмена решения цикла 4 (§25.3 `ARCHITECTURE_CYCLE4.md`), и она требует **теста,
который был бы красным до цикла 5**: после смены ответственного назначение на номер существует, а
`OutboundNotifications` в статусе `Pending` остаются `Pending` (B5-9, US-64).

То же поведение **сохраняется** там, где оно по-прежнему верно: при переносе компании в другой
аккаунт (§51) назначение удаляется и очередь отменяется — компания уходит из аккаунта, которому
принадлежит номер. **И это верно даже тогда, когда перенос заодно меняет ответственного** (§51.3):
решающим остаётся уход из аккаунта, а не смена человека.

---

## 48. Каталог, кеш и публикация прайса (ответ на SPEC §9 п. 8)

`Services/Billing/PricingCatalogCache.cs`:

- ключ `pricing:catalog` — все тарифы + опции + правила доступности, **60 с** TTL;
- ключ `pricing:public` — **готовый JSON-ответ публичного прайса** + его `ETag`, тот же TTL;
- `Invalidate()` вызывается **сразу после** любой админской записи в тариф, опцию, правило или
  `PlatformSetting` — иначе повторится дефект цикла 4 (кеш отдавал старую цену минуту после
  сохранения, ср. `PlatformSettings.InvalidateCache`).

**`ETag`** — `W/"<sha256(payload)[0..16]>"`. `GET /api/pricing` с `If-None-Match` отдаёт **304** без
тела. Это и есть ответ на «страница не должна мигать при перезагрузке»: ответ стабилен байт-в-байт,
пока администратор ничего не менял, а react-query с `staleTime: 60_000` не дёргает сервер между
переходами.

**Рубильник публикации.** `PlatformSetting pricing.public-enabled` (`"true"`/`"false"`, дефолт —
**ключа нет = выключено**). Пока выключен:
- `GET /api/pricing` → **404** (не 403 и не пустой список: снаружи не должно быть видно, что ресурс
  существует и просто пуст);
- блок на главной не рисуется (фронт скрывает его по 404, без отдельного флага);
- `GET /api/admin/pricing/preview` работает всегда — администратор и юрист смотрят страницу до
  публикации.

Это техническая реализация П5/R6: **выкат кода не публикует цены**. Включение — отдельное действие
суперадмина после вычитки юристом, и оно попадает в `PlatformSettingChangeLog`.

Публичный ответ собирается из **отдельных** DTO (`PublicPlanDto`, `PublicOptionDto`), а не из
админских: внутренние поля (`IsActive`, `CapabilityKey`, `IncludedQuantity`, идентификаторы правил) в
них **физически отсутствуют** — по образцу `ChannelDto`, где токенов провайдера нет в модели ответа,
а не «не заполняются». Фильтр: `IsActive && IsPublic` для тарифа;
`IsActive && IsPublic && PricePerMonth != null` для опции.

Бесплатный тариф — обычная публикуемая строка прайса с лимитами 1 компания / 1 сотрудник
(П4, подтверждено заказчиком, §64 п. 5): публикуемый прайс и код обязаны говорить одно и то же.

**Единица измерения на витрине (US-71 п. 3)** приходит с сервера готовой строкой
`unitPriceText`: «дополнительная компания — 490 ₽/мес», «дополнительный сотрудник — 290 ₽/мес»,
«номер для рассылок — 690 ₽/мес». Фронт её не собирает — требование «формулировок без единицы быть
не должно» держится контрактом, а не бдительностью вёрстки.

Rate limit для `/api/pricing` **не заводится**: ответ отдаётся из памяти и не ходит в БД, новая
политика — лишняя настройка и лишняя память (R10). Понадобится — это строка в nginx, а не код.

---

## 49. Заявки (ответ на SPEC §9 п. 6)

**Один механизм на всё, что владелец хочет купить, и он привязан к аккаунту, а не к компании.**

`SubscriptionRequest`: `Id`, `BillingAccountId` (FK), `RequestedByUserId`, `DesiredPlanId Guid?`,
`Status`, `EstimatedMonthlyPrice decimal` (снимок суммы, которую владельцу показали, — чтобы спор
«мне показывали другую цену» решался данными), `Comment string(500)?`, `CreatedAtUtc`,
`ResolvedAtUtc?`, `ResolvedByUserId?`, `ResolutionComment string(500)?`.
`SubscriptionRequestItem`: `Id`, `RequestId` (Cascade), `OptionId`, `Quantity`.

**Частичный уникальный индекс `(BillingAccountId) WHERE "Status" = 0`** — у аккаунта не может быть
двух ожидающих заявок. Повторная отправка **переписывает** ожидающую (`PUT`-семантика на
`POST`-маршруте, 200 вместо 201 — описано в контракте). Это и есть «повторная заявка не плодит
дубликатов» (US-70) без проверок в коде — правило держится индексом, как `ChannelCompanyAssignment`
в цикле 4.

Заявка **ничего не включает**. Администратор видит очередь
(`GET /api/admin/subscription-requests`), открывает аккаунт и назначает подписку одним действием
(US-67). `requestId` передаётся в назначении — тогда заявка закрывается как `Approved` в той же
транзакции.

Пока заявка `Pending`, опции из неё показываются владельцу со статусом `PendingPayment`, а в строках
`AccountSubscriptionOption` заполняются `RequestedQuantity`/`RequestedAtUtc` — чтобы состояние
«просили, но не оплачено» читалось из одной строки, без join'а с заявками на каждом экране.

Заявка цикла 4 на номер (`NotificationChannel.RequestedAtUtc`) остаётся техническим маркером «хочу
завести номер» и **перестаёт быть заявкой на деньги**; в интерфейсе владельца «Подключить» ведёт в
один и тот же диалог заявки на опцию.

---

## 50. Смена ответственного за компанию (US-64) — лечение исходной боли

`PUT /api/admin/companies/{id}/owner`. Что делает после цикла:

1. меняет `Company.OwnerUserId`, переносит роль `CompanyOwner` в `CompanyMembers`, вызывает
   `IdentityRoleSync` для обоих — **без изменений**, как сегодня;
2. **не трогает** `Company.BillingAccountId`, подписку, опции, срок, лимиты;
3. **не трогает** `ChannelCompanyAssignment` и очередь сообщений (§47.4) — удаление старого блока;
4. пишет строку в `CompanyOwnerChangeLog` (кто, когда, откуда, куда; `WithTransfer = false`);
5. **не пишет** ничего в `SubscriptionChangeLog` — US-64 п. 4 прямо этого требует.

**Приёмочный тест (не режется):** компания на платном тарифе с опциями → смена ответственного →
все семь флагов `CompanyDto`, `maxEmployees`, `paidUntil`, `planName` совпадают до и после; новый
ответственный не получает 402 ни на одном сценарии; назначение на номер существует; `Pending`
сообщения остались `Pending`.

Смысл: тариф физически не может слететь, потому что менять нечего — подписка привязана к аккаунту,
а аккаунт компании не изменился.

**Шаги 1 и 4 вынесены в `Services/Billing/CompanyOwnerWriter.cs`** — один класс, которым пользуются
оба эндпоинта: этот и совмещённый перенос (§51.3). Так поведение смены ответственного не может
разойтись между двумя маршрутами (роли, демоция старого владельца, `IdentityRoleSync`, журнал).

---

## 51. Перенос компании в другой биллинг-аккаунт (US-77, ответ на SPEC §9 п. 7)

`POST /api/admin/companies/{companyId}/transfer`, только `SuperAdmin`.
Предпросмотр: `GET /api/admin/companies/{companyId}/transfer/preview?targetBillingAccountId=…&newOwnerUserId=…`.

### 51.0 Одна операция — два сценария (решение заказчика, §64 п. 2)

В теле переноса есть **необязательное** поле `newOwnerUserId`:

| Что передали | Что происходит | Сценарий |
|---|---|---|
| `newOwnerUserId` не задан | меняется только `Company.BillingAccountId` — плательщик. Ответственный остаётся прежним | перекладывание точки между **своими** аккаунтами: управляющий тот же, счёт другой |
| `newOwnerUserId` задан | `BillingAccountId` **и** `OwnerUserId` меняются **в одной транзакции** | продажа филиала постороннему: и деньги, и управление переходят покупателю за одно действие |

Обе оси по-прежнему независимы (§43.2) — просто у администратора появилась одна операция, которая
двигает их согласованно. Разделять это на два последовательных вызова было бы хуже: между ними
существует состояние «компания уже оплачивается покупателем, но управляет ею продавец», и если
второй вызов не дойдёт (закрыли вкладку, упала сеть), это состояние останется навсегда и никем не
будет замечено.

### 51.1 Правило валидации нового ответственного

`newOwnerUserId` обязан пройти три проверки; первые две — сегодняшние проверки
`UpdateCompanyOwner`, третья — новая:

1. пользователь существует → иначе **400** «Пользователь не найден»;
2. это не надгробие удалённого аккаунта (`DeletedAtUtc is null`, `ARCHITECTURE.md` §7.4) → иначе
   **400**: иначе `SuperAdmin` отдал бы компанию тому, кто никогда не сможет войти;
3. **пользователь уже связан с принимающим аккаунтом** — одним из двух способов:
   - он **держатель** принимающего аккаунта (`BillingAccount.OwnerUserId == newOwnerUserId`), **или**
   - он уже **участник** (`CompanyMembers`) хотя бы одной компании принимающего аккаунта;

   иначе **409** с текстом: «Мария Сидорова не связана с принимающим аккаунтом. Сделайте её
   держателем этого аккаунта, добавьте сотрудником в любую его компанию — или выполните перенос без
   смены ответственного и смените ответственного отдельно.»

**Почему именно это правило.** Нужна проверка, которая закрывает единственную реальную опасность
(одним админским действием отдать оплаченную компанию человеку, не имеющему отношения к тому, кто за
неё платит) и при этом не ломает оба законных сценария:

- **продажа филиала**: покупатель — держатель своего аккаунта B, условие (а) выполняется само.
  Это подавляющий случай, и он не требует никаких предварительных действий;
- **наёмный управляющий внутри холдинга**: держатель B — финдиректор, а точкой управляет менеджер,
  которого B уже нанял в другую свою компанию; условие (б) выполняется.

Отвергнутые варианты:

| Вариант правила | Почему не он |
|---|---|
| любой существующий пользователь (как сегодня в `PUT .../owner`) | ровно тот риск, ради которого US-77 сделали операцией `SuperAdmin`: опечатка в идентификаторе отдаёт чужую оплаченную компанию постороннему, и это ничем не ловится. В `PUT .../owner` это терпимо (деньги не двигаются), в переносе — нет |
| **только** держатель принимающего аккаунта | ломает холдинг с наёмным управляющим: администратор вынужден сначала сделать управляющего держателем аккаунта, то есть плательщиком, чего никто не хотел |
| участник **переносимой** компании | самый узкий и самый бесполезный: покупатель филиала обычно ещё не работает в нём |
| отдельная проверка «подтвердите галочкой» | подтверждение вместо правила — способ сделать опасное действие рутинным |

Правило **fail-closed и разрешимое**: если оно сработало, у администратора есть два названных прямо
в тексте выхода, и оба — обычные операции продукта. Проверка живёт в
`CompanyTransferService.ValidateNewOwner`, чистая часть покрывается юнит-тестом.

### 51.2 Проверка лимитов принимающего аккаунта

- **Лимит компаний — жёсткий отказ (402).** `usage(B).CompaniesUsed >= plan(B).AccountMaxCompanies`
  → перенос отклонён, ничего не изменено. Это приёмочный критерий US-77: «ничего не обнуляется
  молча». Текст `BillingTexts.TransferRejectedCompanyLimit(...)`: «На тарифе «Базовый» — 2 компании,
  занято 2. Чтобы принять ещё одну, подключите опцию «Дополнительная компания».»
- **Места сотрудников — предупреждение, а не отказ**, и оно проверяемо машиной: без
  `confirmSeatOverflow: true` сервер отвечает 409 с числами, с ним — выполняет перенос. Так
  требование US-77 «администратор видел предупреждение **до** подтверждения» становится свойством
  API, а не обещанием вёрстки. Уже заведённые сотрудники не удаляются и не блокируются; добавлять
  новых нельзя (правило US-67).
- **Новый ответственный может стоить места.** Если `newOwnerUserId` задан и этот человек **ещё не**
  `CompanyMember` переносимой компании, перенос добавит ему членство — то есть займёт **ещё одно**
  место в штате аккаунта B. Расчёт перерасхода обязан это учитывать:

```
seatsAfter = usage(B).SeatsUsed
           + seatsOf(company)
           + (newOwnerUserId задан && он не член этой компании ? 1 : 0)
seatOverflow = plan(B).AccountMaxEmployees is not null && seatsAfter > plan(B).AccountMaxEmployees
```

  Поэтому предпросмотр принимает `newOwnerUserId` тем же необязательным параметром: без него он
  посчитал бы на одно место меньше, чем произойдёт, и предупреждение оказалось бы ложно-успокаивающим.

### 51.3 Порядок операции — одна транзакция

```
BEGIN
  AdvisoryLock("billing-account:{min(accountA, accountB)}")   ← фиксированный порядок по Guid,
  AdvisoryLock("billing-account:{max(accountA, accountB)}")     иначе два встречных переноса = дедлок
  AdvisoryLock("company-members:{companyId}")                 ← берётся ТОЛЬКО при смене
                                                                ответственного; аккаунтные локи
                                                                всегда раньше компанийного (§52)
  1. читаем компанию, аккаунт-источник A, аккаунт-приёмник B (404, если чего-то нет)
  2. B == A            → 409 «компания уже в этом аккаунте»
  3. ValidateNewOwner (§51.1), если newOwnerUserId задан      → 400 / 409
  4. лимит компаний B (§51.2)                                  → 402, ОТКАЗ до любых записей
  5. места сотрудников B с поправкой на нового ответственного  → 409 без confirmSeatOverflow
  6. DELETE ChannelCompanyAssignment для компании (если есть)
     + OutboundNotifications(Pending) → Cancelled / BookingOrAssignmentCancelled
  7. UPDATE Company SET BillingAccountId = B        ← композитный FK (§43.6) не даст сделать раньше п.6
  8. если newOwnerUserId задан: CompanyOwnerWriter — Company.OwnerUserId = newOwner,
     демоция прежнего CompanyOwner-членства до Master, создание/повышение членства нового
  9. SaveChangesAsync; затем IdentityRoleSync для нового и прежнего ответственного
 10. две строки SubscriptionChangeLog (ChangeKind = CompanyTransferred) — в истории A и в истории B
 11. если ответственный сменился: одна строка CompanyOwnerChangeLog с WithTransfer = true
COMMIT
```

**Почему именно такой порядок:**

- **шаги 4–5 раньше любых записей** — отказ обязан быть бесплатным;
- **шаг 6 строго раньше шага 7.** Номер аккаунта A не может обслуживать чужую компанию
  (SPEC §3.3 п. 5), и композитный FK (§43.6) физически не даст сменить `BillingAccountId` при живом
  назначении. Забыть шаг нельзя — упадёт база, а не тест;
- **шаг 8 после шага 7, но это не требование FK, а требование смысла.** `CompanyMembers` не входит
  в композитные ключи (§43.6), поэтому порядок 7↔8 базе безразличен; ставим смену ответственного
  **после** смены плательщика, чтобы в любом промежуточном состоянии внутри транзакции компания уже
  принадлежала тому аккаунту, чьи правила к ней применяются;
- **шаг 9 после `SaveChangesAsync` и внутри транзакции** — конвенция `IdentityRoleSync` из
  `ARCHITECTURE.md` §8.3/§8.4, без изменений;
- **шаги 10 и 11 оба** при совмещённой операции: произошли два события — деньги переехали (журнал
  подписки обоих аккаунтов) и управление сменилось (журнал ответственных). Это **не нарушает**
  US-64: запрет на запись в журнал подписки касается **самостоятельной** смены ответственного
  (`PUT .../owner`, §50), где деньги действительно не двигались. Здесь они двигались.

`WithTransfer = true` в `CompanyOwnerChangeLog` нужен, чтобы при разборе было видно: ответственный
сменился не отдельным решением, а вместе с продажей точки — и чтобы это можно было сопоставить со
строкой `CompanyTransferred` по `CompanyId` и времени.

### 51.4 Что видит владелец B

Компания появляется в его подписке и начинает жить по его тарифу немедленно. Номер — «не подключён»,
владелец B может подключить свой (US-77 п. 3). Владельцу операция переноса недоступна: в интерфейсе
владельца такой кнопки нет вовсе (Р3).

Админский UI обязан показывать, что именно произойдёт с ответственным: если `newOwnerUserId` не
задан — «Ответственный не меняется: компанией продолжит управлять Иван Петров»; если задан —
«Ответственный станет Мария Сидорова; Иван Петров останется сотрудником». Иначе администратор решит,
что перенос «не сработал».

---

## 52. Конкурентность и блокировки

| Операция | Локи (в этом порядке) | Почему |
|---|---|---|
| `POST /api/companies` | `billing-account:{accountId}` | лимит компаний теперь аккаунтный: существующего `owner-companies:{userId}` мало, если у аккаунта появится второй распорядитель. Старый лок **заменяется** новым |
| `POST /api/companies/{id}/members` | `billing-account:{accountId}` → `company-members:{companyId}` | места считаются по аккаунту, а строки пишутся в компанию. Два лока **строго в этом порядке** (аккаунт раньше компании) — иначе два добавления в разные компании одного аккаунта дают дедлок |
| `DELETE /api/companies/{id}/members/{memberId}` | `company-members:{companyId}` (как сегодня) | удаление не может превысить лимит — аккаунтный лок не нужен |
| `PUT /api/admin/billing-accounts/{id}/subscription` | `billing-account:{accountId}` | назначение читает текущий состав опций, пересчитывает и перезаписывает |
| `POST /api/billing/subscription/request` | `billing-account:{accountId}` | создание-или-перезапись ожидающей заявки; частичный уникальный индекс защищает от дубля, лок превращает 500 из БД в нормальный ответ |
| `POST /api/admin/companies/{id}/transfer` | `billing-account:{min}` → `billing-account:{max}` → `company-members:{companyId}` (последний — **только** если задан `newOwnerUserId`) | §51.3. Компанийный лок нужен ровно тогда, когда операция пишет `CompanyMembers`; порядок «аккаунты раньше компании» соблюдён |
| `PUT /api/admin/companies/{id}/owner` | `company-members:{companyId}` (как сегодня) | денег не касается |

**Правило проекта, которое здесь вводится и должно быть записано в код комментарием:**
*аккаунтный лок берётся раньше компанийного, всегда; два аккаунтных лока берутся в порядке
возрастания `Guid`.* Без этого правила дедлок — вопрос времени, и совмещённый перенос (§51.3) —
первая операция, где сходятся оба вида локов.

Чтения (`GET`) локов не берут. Каталог правится редко и из одного экрана — лока на каталог нет,
конкурентная правка разрешается «последний выиграл», что и записывается в журнал.

---

## 53. Что видит фронт (ответ на SPEC §9 п. 10)

### 53.1 `CompanyDto` — +7 полей, ни одно не удаляется

Трёхуровневая конвенция флагов (собственный тумблер / «реально работает» / «сырая возможность
тарифа») **сохраняется целиком**. Добавляются:

| Поле | Зачем |
|---|---|
| `planName: string` | подпись на карточке: владелец с тремя точками видит, что все они на одном тарифе |
| `subscriptionStatus: "Free" \| "Active" \| "Expired"` | плашка US-68 |
| `paidUntil: string \| null` | дата в плашке |
| `employeeCount: int` | сколько сотрудников **в этой компании** (локальная цифра) |
| `accountSeatsUsed: int \| null` | сколько занято **по всем точкам** — иначе «12 из 15» невозможно объяснить на экране компании |
| `accountSeatsLimit: int \| null` | `null` = без ограничения |
| `canAddEmployee: bool \| null` | посчитано сервером по тому же правилу, что 402 |

`null` в трёх последних означает «вызывающий не управляет этой компанией, значение не считалось»
(§46.2) — а не «безлимит».

**Семантическое (не формальное) ломающее изменение:** существующее поле `maxEmployees` теперь
означает **суммарный лимит аккаунта**, а не лимит этой компании. Форма ответа не меняется, поэтому
компилятор фронта промолчит; `MembersTab`, сравнивающий `members.length >= maxEmployees`, начнёт
врать (недосчитает сотрудников других точек и покажет активную кнопку → 402 после клика). Поэтому:
`maxEmployees` помечается **deprecated** в контракте, и все кнопки переводятся на `canAddEmployee`
(задача F5-9, пункт чек-листа §56 контракта).

`billingAccountId` в `CompanyDto` **не появляется** (Р8): владельцу этот идентификатор не нужен и не
показывается. Он есть только в админском `AdminCompanyDto`.

### 53.2 `ProfilePlanDto` — **не ломается**, только дополняется

Аккаунтный тариф снова существует, поэтому все сегодняшние поля остаются на месте и означают ровно
то же самое (в отличие от прошлой редакции, где блок пришлось переписывать):

```json
"plan": {
  "planName": "Базовый", "pricePerMonth": 1490, "isActive": true,
  "paidUntil": "2026-10-31T00:00:00Z", "isExpired": false,
  "allowOnlineBooking": true, "allowMailing": false, "allowAnalytics": true,
  "maxEmployees": 15, "maxCompanies": 4,

  "totalMonthlyPrice": 2480, "currency": "RUB",
  "companiesUsed": 3, "employeesUsed": 12,
  "expiresInDays": 9, "isExpiringSoon": true, "optionCount": 2
}
```

Единственная смена смысла — `maxEmployees` (было «на каждую компанию», стало «суммарно»), та же, что
в §53.1, и с той же пометкой в контракте. Блок «ваш тариф» дополняется **ссылкой на `/billing`** —
ровно то, что просит US-65 («дополняется, а не выбрасывается»).

---

## 54. Миграции и проверка «до/после» (ответ на SPEC §9 п. 9, US-73)

### 54.1 Две миграции + предпроверка

| № | Имя | Что делает |
|---|---|---|
| — | `deploy/checks/billing-precheck.sql` | **до** выката: ищет то, что помешает (кросс-аккаунтные назначения каналов, компании без владельца, дубли подписок). Пустой результат = можно катить |
| 29 | `AddBillingAccounts` | **только схема**: `BillingAccounts`, `SubscriptionOptions`, `PlanOptionRules`, `AccountSubscriptionOptions`, `SubscriptionRequests(+Items)`, `CompanyOwnerChangeLogs`; **nullable** `BillingAccountId` на `Companies`, `AccountSubscriptions`, `NotificationChannels`, `ChannelCompanyAssignments`; 6 колонок на `SubscriptionPlanConfigs`, 5 на `SubscriptionChangeLogs`; индексы |
| 30 | `BackfillBillingAccounts` | **данные + финальные ограничения**: заведение аккаунтов, проставление FK, сид каталога, перенос оплаченных каналов, пересчёт лимита сотрудников; затем `SET NOT NULL`, альтернативные ключи и композитные FK (§43.6) |

Разделение сознательное: ограничения, которые невозможно создать до backfill (NOT NULL, композитные
FK), ставятся в конце 30-й; если она упадёт — схема 29-й уже применена и сервис работает по-старому
(новые колонки просто пусты), а 30-ю можно перепроверить и переписать **новой** миграцией.
`Down` у 30-й — no-op с комментарием: откатывать backfill бессмысленно, исходные данные не удалялись.

**Правило проекта соблюдается:** первую миграцию смёрженного цикла не редактируют; обе — новые
файлы, миграции цикла 4 не трогаются.

### 54.2 Перенос подписок — почти тождественный (US-73 пп. 1–3)

```sql
-- 1. аккаунт каждому, у кого есть компания или подписка
INSERT INTO "BillingAccounts" ("Id","OwnerUserId","GrandfatheredEmployeeBonus","CreatedAtUtc","UpdatedAtUtc")
SELECT gen_random_uuid(), u, 0, now(), now()
FROM (SELECT "OwnerUserId" AS u FROM "Companies"
      UNION SELECT "OwnerUserId" FROM "AccountSubscriptions"
      UNION SELECT "OwnerUserId" FROM "NotificationChannels") s;

-- 2. подписка → аккаунт. Уникальный индекс AccountSubscriptions.OwnerUserId делает это 1:1
UPDATE "AccountSubscriptions" s SET "BillingAccountId" = b."Id"
FROM "BillingAccounts" b WHERE b."OwnerUserId" = s."OwnerUserId";

-- 3. компании и номера → аккаунт своего сегодняшнего владельца
UPDATE "Companies" c            SET "BillingAccountId" = b."Id" FROM "BillingAccounts" b WHERE b."OwnerUserId" = c."OwnerUserId";
UPDATE "NotificationChannels" n SET "BillingAccountId" = b."Id" FROM "BillingAccounts" b WHERE b."OwnerUserId" = n."OwnerUserId";
UPDATE "ChannelCompanyAssignments" a SET "BillingAccountId" = c."BillingAccountId" FROM "Companies" c WHERE c."Id" = a."CompanyId";
```

Подписок **не создаётся** тем, у кого их не было: отсутствие строки = бесплатный тариф, ровно то, что
у них есть сегодня. Три компании одного владельца попадают в **один** аккаунт и продолжают жить по
одной подписке — как сегодня. По сравнению с редакцией 1 (подписка на каждую компанию) миграция не
размножает подписки и не требует сверки «одна строка стала тремя».

**Защита от невозможного состояния.** Перед созданием композитных FK миграция проверяет, что нет
назначений «компания одного аккаунта на номер другого»:

```sql
DO $$ DECLARE bad int; BEGIN
  SELECT count(*) INTO bad FROM "ChannelCompanyAssignments" a
    JOIN "Companies" c ON c."Id" = a."CompanyId"
    JOIN "NotificationChannels" n ON n."Id" = a."ChannelId"
   WHERE c."BillingAccountId" <> n."BillingAccountId";
  IF bad > 0 THEN RAISE EXCEPTION 'Cross-account channel assignments: %, fix before migrating', bad; END IF;
END $$;
```

Падать, а не чинить молча: такие строки означают, что кто-то уже обслуживал чужую компанию, и это
решение человека, а не миграции. `billing-precheck.sql` находит их **до** выката (на стенде их быть
не должно — функция уведомлений не выпущена).

### 54.3 Сид каталога

- **Системный бесплатный тариф**: если строки с сегодняшним поведением нет — создаётся
  `IsSystemFree = true`, `PricePerMonth = 0`, `MaxCompanies = 1`, `MaxEmployees = 1`,
  `AllowPublicListing = true`, остальные флаги `false`, `PhotoQuotaMb = 100`,
  `PhotoRetention = SixMonths` — **дословно `EffectivePlan.Free`** (П4: публикуемый прайс и код
  обязаны говорить одно и то же). `IsPublic = false` до решения администратора.
- **Три опции, все с `PricePerMonth = NULL`** (то есть никому не предлагаются, пока администратор не
  назначит цену):

| Code | Kind | CapabilityKey | UnitName |
|---|---|---|---|
| `extra-companies` | Quantity | `companies` | «компания» |
| `extra-employees` | Quantity | `employees` | «сотрудник» |
| `notifications.whatsapp` | Quantity | `notifications.whatsapp` | «номер» |

- **Правила доступности** для каждого существующего тарифа: `extra-companies` и `extra-employees` →
  `Extra`; `notifications.whatsapp` → `Extra`, если `AllowNotificationChannel = true`, иначе
  `Unavailable` — один в один сегодняшнее поведение.
- **Оплаченные каналы → количество номеров**: для каждого аккаунта
  `quantity = count(каналы с PaidUntilUtc >= now)`, `PaidUntilUtc = max(channel.PaidUntilUtc)`.
  На стенде ожидается ноль строк, но миграция обязана быть корректной, если они появятся.
- `notifications.whatsapp` остаётся `IsPublic = false` и без цены — **US-74 выполняется схемой**,
  а не обещанием.

### 54.4 `MaxEmployees` «на компанию → суммарно» — единственное нетождественное место

Прямое прочтение SPEC US-73: аккаунт обязан получить лимит **не меньше суммы того, что его компании
могли иметь по-старому**, и **не меньше фактически занятого**. Решение заказчиком **принято**
(§64 п. 1): цена ошибки низкая (боевых платящих клиентов нет), тихая миграция важнее.

Нельзя просто умножить `SubscriptionPlanConfig.MaxEmployees` на число компаний: колонка общая для
всех аккаунтов на этом тарифе, и правка испортила бы прайс. Нельзя и выдать опцию
`extra-employees`: как только администратор назначит ей цену, у всех унаследовавших аккаунтов
внезапно вырастет счёт.

**Решение — отдельное поле `BillingAccount.GrandfatheredEmployeeBonus`**, которое участвует в
лимите (§44.3 п. 6) и не участвует в деньгах:

```
base  = plan.MaxEmployees  (или 1, если подписки нет — сегодняшний Free)
bonus = 0,                                   если base IS NULL (безлимит)
        GREATEST(0, base*(companies-1), seatsUsed - base)   иначе
```

- `base*(companies-1)` — буквальная «сумма того, что компании могли иметь»: было `base` на каждую из
  `companies`, стало `base` на аккаунт, недостача = `base*(companies-1)`;
- `seatsUsed - base` — страховка «не меньше фактически занятого» на случай, если данные уже
  расходятся с лимитом (например, лимит понижали после найма);
- владелец одной компании получает `bonus = 0` — **для сегодняшнего большинства не меняется ничего**,
  что и требует Р1.

Поле видно администратору с пояснением («сохранено при переходе на суммарный лимит, 10 мест») и
может быть обнулено вручную, когда аккаунт перейдёт на честно оплаченную опцию. Это делает
нетождественный шаг миграции **видимым и обратимым**, а не растворённым в данных.

### 54.5 Проверка «ни одна компания ничего не потеряла» (главный приёмочный критерий US-73)

`deploy/checks/billing-migration-check.sql` — **read-only** скрипт, одна SQL-выборка на каждую
компанию, показывающая расхождения между «как было» и «как стало»:

- **как было**: `AccountSubscriptions` через сохранённый `s."OwnerUserId" = c."OwnerUserId"` по
  старому правилу (`IsActive && (PaidUntil IS NULL OR >= now) && plan.IsActive`), лимит сотрудников —
  `plan.MaxEmployees` **на эту компанию**;
- **как стало**: `AccountSubscriptions` через `c."BillingAccountId"`, лимит сотрудников —
  `plan.MaxEmployees + bonus + Σ опций` **на аккаунт**.

Скрипт возвращает **пустой результат**, если нет ни одной компании, у которой:
изменился набор булевых возможностей; изменился тариф или срок; стало
`seats_limit_new < seats_used_now`; компания осталась без `BillingAccountId`; номер обслуживает
чужую компанию.

Возможность такой проверки — прямое следствие решения **не удалять `AccountSubscriptions.OwnerUserId`**
(§43.4): сравнение делается одним join'ом, без предварительного снимка и без остановки сервиса.
Прогон на стенде до и после — обязательный пункт чек-листа выката (`DEPLOY.md`), результат
прикладывается к отчёту QA.

Дополнительно — функциональный тест `BillingMigrationTests`: строит «старую» картину (владелец с
тремя компаниями, тариф `MaxEmployees = 5`, 12 сотрудников), прогоняет миграцию на тестовой БД,
сверяет `EffectivePlan` и `canAddEmployee` по каждой компании.

---

## 55. Структура проекта — бэкенд

```
ServiceBooking.Core/
├── Entities/
│   ├── BillingAccount.cs                🆕
│   ├── SubscriptionOption.cs            🆕
│   ├── PlanOptionRule.cs                🆕
│   ├── AccountSubscriptionOption.cs     🆕
│   ├── SubscriptionRequest.cs           🆕
│   ├── SubscriptionRequestItem.cs       🆕
│   ├── CompanyOwnerChangeLog.cs         🆕
│   ├── AccountSubscription.cs           ✏️ +BillingAccountId
│   ├── Company.cs                       ✏️ +BillingAccountId (OwnerUserId НЕ трогаем)
│   ├── NotificationChannel.cs           ✏️ +BillingAccountId
│   ├── ChannelCompanyAssignment.cs      ✏️ +BillingAccountId (ко-тенантность)
│   ├── SubscriptionPlanConfig.cs        ✏️ +6 колонок
│   └── SubscriptionChangeLog.cs         ✏️ +5 колонок
└── Enums/
    ├── OptionKind.cs · OptionAvailability.cs · SubscriptionStatus.cs          🆕
    ├── SubscriptionRequestStatus.cs · SubscriptionChangeKind.cs              🆕
    └── ChannelFundingState.cs                                                🆕

ServiceBooking.API/
├── Controllers/
│   ├── PricingController.cs             🆕 публичный прайс (анонимно)
│   ├── BillingController.cs             🆕 экран владельца + заявки
│   ├── AdminBillingController.cs        🆕 каталог, аккаунты, назначение, очередь, перенос
│   ├── AdminController.cs               ✏️ owner-подписка → 410; смена владельца → §50
│   ├── CompaniesController.cs           ✏️ аккаунтные лимиты, +7 полей CompanyDto, тексты 402
│   ├── NotificationChannelsController.cs ✏️ оплата → funding (§47)
│   └── ProfileController.cs             ✏️ ProfilePlanDto +8 полей
├── DTOs/Billing/                        🆕 Public*, AccountBilling*, AdminPlan*, AdminOption*, Request*, Transfer*
├── Services/Billing/                    🆕
│   ├── CapabilityKeys.cs                известные ключи возможностей
│   ├── PlanCapabilityMap.cs             колонки тарифа ⇄ ключи
│   ├── BillingCalculator.cs             ЧИСТАЯ логика подписки (юнит-тесты)
│   ├── ChannelFunding.cs                ЧИСТОЕ правило «оплачено N, заведено M» (§47.1)
│   ├── AccountUsageReader.cs            расход суммарных лимитов, 2 запроса (§46)
│   ├── BillingAccountProvisioner.cs     EnsureAccountAsync(userId) под локом
│   ├── PricingCatalog.cs                read-model каталога
│   ├── PricingCatalogCache.cs           IMemoryCache 60 с + Invalidate
│   ├── SubscriptionWriter.cs            назначение подписки + журнал (одно место записи)
│   ├── CompanyOwnerWriter.cs            смена ответственного + роли + журнал (§50, §51.3)
│   ├── CompanyTransferService.cs        US-77, одна транзакция (§51)
│   └── BillingTexts.cs                  все русские тексты статусов/причин/402/funding
├── Services/SubscriptionResolver.cs     ✏️ переписан внутри, сигнатуры списков сохранены
└── Services/Notifications/NotificationGate.cs ✏️ +параметр channelIsFunded, минус ChannelPaymentState

ServiceBooking.Infrastructure/Migrations/
├── 2026…_AddBillingAccounts.cs          🆕 схема
└── 2026…_BackfillBillingAccounts.cs     🆕 данные + финальные ограничения

deploy/checks/billing-precheck.sql        🆕 что мешает мигрировать (до выката)
deploy/checks/billing-migration-check.sql 🆕 сверка «до/после» для QA
contracts/openapi-cycle5.yaml             🆕 машиночитаемый контракт
```

Правило цикла 4 «логику — в чистые классы» продолжается: **вся** ценообразующая логика живёт в
`BillingCalculator` + `ChannelFunding` + `PlanCapabilityMap` + `BillingTexts` и покрывается
юнит-тестами без БД. Контроллеры остаются тонкими: прочитать, вызвать, отдать.
`CompanyOwnerWriter` — один класс на два маршрута (§50, §51.3): поведение смены ответственного не
может разойтись между «отдельно» и «в составе переноса».

---

## 56. Структура проекта — фронтенд

```
frontend/src/
├── api/
│   ├── pricing.ts        🆕 публичный прайс
│   ├── billing.ts        🆕 экран владельца + заявки
│   └── adminBilling.ts   🆕 каталог, аккаунты, назначение, очередь, перенос
├── pages/
│   ├── PricingPage.tsx              🆕 публичный маршрут /pricing
│   ├── HomePage.tsx                 ✏️ + блок «Тарифы»
│   ├── ProfilePage.tsx              ✏️ сводка + ссылка на /billing
│   ├── BillingPage.tsx              🆕 /billing — «Ваша подписка» (US-65)
│   ├── admin/PlansTab.tsx           ✏️ редактор тарифа + матрица доступности опций
│   ├── admin/OptionsTab.tsx         🆕 каталог опций
│   ├── admin/BillingAccountsTab.tsx  🆕 аккаунты: подписка, опции, срок, номера, бонус мест
│   ├── admin/BillingRequestsTab.tsx  🆕 очередь заявок + назначение
│   └── admin/CompanyTransferDialog.tsx 🆕 перенос компании (US-77): приёмник + необязательный
│                                          новый ответственный + предпросмотр
├── components/pricing/
│   ├── PricingTeaser.tsx  🆕 блок на главной (3–5 возможностей, ссылка)
│   ├── PlanCard.tsx       🆕
│   ├── OptionRow.tsx      🆕
│   └── PriceSummary.tsx   🆕 «тариф + Σ опций = итог» — формула SPEC §3.1 дословно
├── types/pricing-api.d.ts 🆕 сгенерирован из contracts/openapi-cycle5.yaml
└── utils/billingError.ts  🆕 402/409 → русский текст (по образцу notificationError.ts)
```

### 56.1 Маршруты

`/pricing` — **публичный**, вне `ProtectedRoute`, рядом с `/privacy` и `/terms`; добавляется в
allow-list `LegalConsentFilter` (авторизованный пользователь с непринятой редакцией должен иметь
возможность посмотреть цены — иначе он заперт даже от информации).
`/billing` — под `ProtectedRoute` (роль `CompanyOwner`), заголовок экрана — **«Ваша подписка»**
(Р8: слова «биллинг-аккаунт» на владельческих экранах нет).

### 56.2 Мобильный и доступность (SPEC §6)

Карточки тарифов — вертикальный стек на `<768px`, сетка от `md`. **Таблица сравнения тарифов в MVP
не делается вовсе** — именно она порождает горизонтальный скролл на телефоне; сравнение даётся
карточками с 3–5 пунктами `highlights`. `<title>` и `<h1>` — «Тарифы и цены — ServiceBooking»
(страницу будут слать ссылкой). Суммы — `toLocaleString('ru-RU')` + « ₽/мес».

### 56.3 Типы (риск R9)

`npm run types:api` → `openapi-typescript contracts/openapi-cycle5.yaml -o src/types/pricing-api.d.ts`.
Файл **коммитится**, скрипт запускается вручную при изменении контракта. Для DTO цикла 5 ручные
интерфейсы в `types/index.ts` **запрещены**. В CI проверка не добавляется (лишняя минута на каждый
прогон); расхождение ловит QA контрактным прогоном (§57).

Цена решения: одна devDependency и один сгенерированный файл. Остальные 20 модулей `src/api/*`
остаются на ручных типах — переводить их в этом цикле нельзя, это отдельная уборка.

---

## 57. Машиночитаемый контракт и как им пользуется команда

`contracts/openapi-cycle5.yaml` — **OpenAPI 3.0.3, источник истины по форме интерфейса.**
`API_CONTRACT_CYCLE5.md` объясняет смысл и правила; YAML фиксирует форму. Расхождение между ними —
дефект документа, и правится YAML-первым.

| Роль | Как пользуется |
|---|---|
| frontend | `npx @stoplight/prism mock contracts/openapi-cycle5.yaml --port 4010` → работающий мок **в первый же день**, `VITE_API_TARGET=http://localhost:4010 npm run dev`. Плюс генерация типов (§56.3) |
| backend | пишет контроллеры под схемы; имена полей и коды берёт оттуда, а не из головы |
| QA | контрактный прогон **готовым инструментом, без ручных тестов**: `schemathesis run contracts/openapi-cycle5.yaml --base-url http://localhost:5000 --checks all`. Проверяется, что фактические ответы соответствуют схеме — то есть что BE и FE реально сошлись по форме, а не на словах в отчётах |
| architect | дальнейших уточнений не требуется: всё, что нужно для параллельной работы, — в этих трёх файлах |

Почему OpenAPI, а не GraphQL SDL / `.proto`: протокол, выбранный в §42, — REST/JSON, и схема обязана
описывать именно его. Swashbuckle-генерация из кода не годится: она появляется **после** кода, то
есть ровно тогда, когда параллельная работа уже не нужна.

---

## 58. Задачи и параллельность

**День 0 (сделано):** контракт + OpenAPI. Дальше BE и FE не ждут друг друга.

### Backend

| № | Задача | Зависит от | Даёт |
|---|---|---|---|
| **B5-1** | `BillingAccount` + все новые сущности + `AppDbContext` + миграция схемы `AddBillingAccounts` | — | разблокирует все остальные BE |
| **B5-2** | `PricingCatalog` + кеш + админский CRUD тарифов и опций + матрица доступности | B5-1 | US-66, US-72 |
| **B5-3** | `CapabilityKeys`, `PlanCapabilityMap`, `BillingCalculator`, `ChannelFunding`, переписанный `SubscriptionResolver` + юнит-тесты | B5-1 | ядро; US-73 |
| **B5-4** | `AccountUsageReader` + аккаунтные гейты лимитов + тексты 402 + `BillingAccountProvisioner` | B5-3 | US-69, R11 |
| **B5-5** | Экран владельца `GET /api/billing/subscription` (состав, итог, покрытые компании, предупреждение) | B5-3, B5-4 | US-65, US-68 |
| **B5-6** | Заявки: owner POST/DELETE + админская очередь + reject | B5-1 | US-70 |
| **B5-7** | Назначение подписки одним действием + `SubscriptionWriter` + журнал + лок; админские аккаунты | B5-3 | US-67 |
| **B5-8** | Публичный прайс: `GET /api/pricing`, ETag/304, рубильник, `GET /api/admin/pricing/preview` | B5-2 | US-71, US-72 |
| **B5-9** | US-64: `CompanyOwnerWriter` + `CompanyOwnerChangeLog`; смена ответственного не трогает деньги и номер (удаление блока цикла 4) + тест-регрессия | B5-3 | US-64 |
| **B5-10** | US-77: `CompanyTransferService` — предпросмотр, 402 при нехватке лимита, `confirmSeatOverflow`, **необязательная смена ответственного в той же транзакции** (§51) + негативные тесты | B5-4, **B5-9** (переиспользует `CompanyOwnerWriter`) | US-77 |
| **B5-11** | Снятие второй оси оплаты: `NotificationGate(+channelIsFunded)`, `ChannelDto`, тексты funding, 410 на двух эндпоинтах | B5-3 | Р6, R2, US-74 |
| **B5-12** | `CompanyDto` +7 полей, `ProfilePlanDto` +8 полей | B5-4 | US-65, US-69 |
| **B5-13** | Миграция данных `BackfillBillingAccounts` + оба SQL-скрипта + `BillingMigrationTests` | B5-1, B5-3 | US-73 |

**Параллельно между собой:** B5-2 ‖ B5-3 (после B5-1); затем B5-4 ‖ B5-6 ‖ B5-8 ‖ B5-9 ‖ B5-11;
затем B5-5 ‖ B5-7 ‖ B5-12, и B5-10 **после B5-9** (общий `CompanyOwnerWriter`).
**Строго последовательно:** B5-1 → всё; **B5-13 — последней** (миграция данных пишется, когда схема
и правила окончательны; правило «первую миграцию смёрженного цикла не редактировать» действует).

### Frontend (стартуют в день 0 против Prism-мока, ни одна не ждёт бэкенд)

| № | Задача | Истории |
|---|---|---|
| **F5-0** | `types:api`, генерация типов, `billingError.ts` | инфраструктура |
| **F5-1** | `PricingPage` + маршрут + мобильная вёрстка + `<title>` | US-71 |
| **F5-2** | `PricingTeaser` на главной (скрывается при 404) | US-71 |
| **F5-3** | `/billing` «Ваша подписка»: состав, формула, итог, покрытые компании, «12 из 15» | US-65 |
| **F5-4** | Плашка окончания оплаты + ссылка из профиля + новые поля `ProfilePlanDto` | US-68, US-65 |
| **F5-5** | Кнопки «Подключить/Отключить» + диалог «станет стоить N ₽/мес» + статус заявки | US-70 |
| **F5-6** | Админка: редактор тарифа + матрица доступности + публичность/порядок | US-66, US-72 |
| **F5-7** | Админка: каталог опций (CRUD, тип, цена, единица измерения) | US-66 |
| **F5-8** | Админка: аккаунты — подписка, опции, срок, «оплачено/заведено номеров», бонус мест | US-67 |
| **F5-9** | Гашение «Добавить сотрудника» по `canAddEmployee` + текст «лимит общий на все точки»; снятие `maxEmployees` из арифметики | US-69 |
| **F5-10** | Админка: перенос компании — приёмник, **необязательный новый ответственный**, предпросмотр (включая пересчёт мест с учётом нового ответственного), отказ по лимиту, подтверждение перерасхода | US-77 |
| **F5-11** | Владельческий экран номеров: «оплачен 1 из 2 заведённых», какой работает и что сделать | Р6, §47.1 |

**Точки синхронизации BE↔FE (и только они):** F5-3/F5-4 ↔ B5-5; F5-5 ↔ B5-6; F5-6/F5-7 ↔ B5-2;
F5-8 ↔ B5-7; F5-1/F5-2 ↔ B5-8; F5-9 ↔ B5-12; F5-10 ↔ B5-10; F5-11 ↔ B5-11.
Все — по контракту, не по коду.

### Порядок урезания (SPEC §10)

(1) `companies[]` с разбивкой на экране US-65 → остаётся сводка «12 из 15»;
(2) опция `extra-employees` — каталог опций-количеств остаётся, продаются только компании и номера;
(3) `SubscriptionRequest` целиком (B5-6, F5-5) — «напишите администратору»;
(4) плашка US-68 (`expiresInDays` остаётся в ответе, не рисуется);
(5) `confirmSeatOverflow` → жёсткая проверка без подсказки с числами.
**Не режутся:** B5-1, B5-3, B5-9 (US-64), B5-10 (US-77 целиком, включая смену ответственного —
это половина сценария «точку продали»), B5-11 (US-74), B5-13 (US-73).
`PricingTeaser` (F5-2) режется при сохранении `/pricing`.

---

## 59. Конфигурация, наблюдаемость, приёмочные грепы

**Новых секций `appsettings.json` нет.** Один новый параметр — в `PlatformSetting` (правится
суперадмином, с журналом, без пересборки):

| Ключ | Дефолт при отсутствии | Смысл |
|---|---|---|
| `pricing.public-enabled` | выключено | публичный прайс отдаётся (§48) |

Антиспам-лимита `billing.free-companies-per-account` **нет**: US-75 упразднена, «количество
компаний» снова товар, и лимит бесплатного тарифа — обычное поле каталога (Р4).
Ключ цикла 4 `notifications.channel.price-per-month` остаётся строкой в таблице (история), больше
ничем не управляет и **удаляется из админского экрана**; `notifications.channel.idle-days` работает
как прежде.

**Наблюдаемость — без новых механизмов** (конвенция US-55 п. 8 цикла 4): Serilog/GlitchTip.
На `Information`: назначение подписки (кто, аккаунт, тариф, сумма), перенос компании (откуда, куда,
кем, **сменился ли ответственный**), включение/выключение публикации прайса. На `Warning`:
расхождения `billing-migration-check.sql`, номера в состоянии `Unfunded` при первом обнаружении.

**Приёмочные грепы (для QA и ревью):**

1. `rg "GetEffectivePlanForOwnerAsync|GetEffectivePlansForOwnersAsync" ServiceBooking.API/` — **ноль**.
2. `rg "OwnerUserId" ServiceBooking.API/` — каждое совпадение обязано находиться в таблице §45.2
   (права/видимость). Совпадений из §45.1 остаться не должно.
3. `rg "ChannelPaymentState" ServiceBooking.API/Services/Notifications/` — **ноль**.
4. `rg "PaidUntilUtc" ServiceBooking.API/ -g '!Migrations'` — ни одного чтения в гейтах.
5. `rg "\.MaxEmployees" ServiceBooking.API/` — только в `PlanCapabilityMap` и админских DTO тарифа;
   гейты читают `AccountMaxEmployees`.
6. Ответ `GET /api/pricing` не содержит `capabilityKey`, `isActive`, `includedQuantity`.
7. **Р8:** `rg -i "биллинг-аккаунт|billingAccount" frontend/src/pages/BillingPage.tsx frontend/src/components/pricing/` — **ноль**;
   `rg -i "billingAccountId" ServiceBooking.API/DTOs/Companies/` — **ноль**.
8. `rg "OwnerUserId\s*=" ServiceBooking.API/Services/Billing/` — только в `CompanyOwnerWriter`:
   единственное место, где меняется ответственный, для обоих маршрутов (§50, §51.3).
9. `Notifications:Provider` в закоммиченном конфиге — `logging`; в тестах цикла нет обращений к
   `api.green-api.com` (US-74).

---

## 60. Риски SPEC → ответ архитектуры

| Риск | Ответ |
|---|---|
| **R1** две оси (`OwnerUserId` / `BillingAccountId`) | §45: полный инвентарь всех сегодняшних чтений с вердиктом + три механизма, ломающих компиляцию (удаление методов, переименование полей `EffectivePlan`, новая сигнатура гейта) + грепы приёмки. Единственная операция, двигающая обе оси, — §51, и она делает это в одной транзакции |
| **R2** два источника правды об оплате | §47: проверка удаляется из гейта, эндпоинт оплаты канала → 410, поля канала помечены историческими, `ChannelFunding` — единственное правило |
| **R3** миграция лишает возможностей на `MaxEmployees` | §54.4: `GrandfatheredEmployeeBonus` — видимое, не влияющее на деньги поле; §54.5: SQL-сверка «до/после» возможна именно потому, что `AccountSubscriptions.OwnerUserId` сохранён |
| **R4** случайное включение WhatsApp | Опция сидируется с `PricePerMonth = NULL`, `IsPublic = false`; публичный прайс фильтрует по обоим; рубильник `pricing.public-enabled` выключен; `Provider = logging` |
| **R5** редкий перенос плохо протестирован | §51 + §43.6: композитный FK делает «забыть снять с номера» невозможным на уровне БД; обязательные негативные тесты — 402 по лимиту компаний и 409 по несвязанному новому ответственному |
| **R6** публикация цен без юриста | §48: рубильник по умолчанию выключен — **деплой не публикует цены**; включение в журнале |
| **R7** экран сложнее прежнего | `PriceSummary.tsx` печатает формулу SPEC §3.1 дословно; сервер отдаёт готовые тексты; экран — один запрос |
| **R8** рост контроллеров | Три новых контроллера, ноль новых эндпоинтов в `CompaniesController`; вся логика — в `Services/Billing` |
| **R9** ручные типы фронта | §56.3: генерация типов из контракта для всей новой поверхности |
| **R10** память стенда | §42: ни одного нового процесса и хранилища; кеш — десятки строк в уже существующем `IMemoryCache`; расход лимитов не кешируется вовсе |
| **R11** суммарные лимиты на горячем пути | §46: `AccountUsageReader` — два сгруппированных запроса на список; на анонимном `/api/companies` — **ноль** дополнительных запросов |

**Новые риски, которых нет в SPEC:**

| № | Риск | Что снижает |
|---|---|---|
| A1 | Опечатка в `CapabilityKey` даёт «опцию, которая ничего не включает» | `GET /api/admin/option-capabilities` + предупреждение в админке; свободный ввод сохранён намеренно (§44.2) |
| A2 | Понижение тарифа гасит оплаченную опцию (правило `Unavailable`, §44.3 п. 3) | Явно описано в контракте и в предупреждении администратору при назначении тарифа, на котором опция недоступна |
| A3 | Два-три advisory-лока → дедлок | §52: жёсткий порядок «аккаунты (по возрастанию `Guid`) раньше компании»; правило записано комментарием в коде; совмещённый перенос — первое место, где сходятся оба вида локов |
| A4 | Композитные FK усложняют массовые операции с назначениями | §43.6: цена названа; выигрыш — структурная невозможность обслуживать чужую компанию. Массовых операций с назначениями в продукте нет |
| A5 | `maxEmployees` меняет смысл, не меняя формы (тихое ломающее изменение) | §53.1: поле помечено deprecated, кнопки переводятся на `canAddEmployee`, пункт в чек-листе BE↔FE и тест Vitest |
| A6 | `GrandfatheredEmployeeBonus` станет вечным «тёмным» лимитом | Показан администратору с пояснением, обнуляется вручную; уборка вписана в следующий цикл |
| A7 | Совмещённый перенос (деньги + ответственный) выполняется реже обычного и ломается незаметно | §51.3: обе записи в одной транзакции; `CompanyOwnerWriter` общий с `PUT .../owner`, то есть покрыт тестами US-64; отдельный тест «перенос со сменой ответственного»: оба поля изменились, оба журнала написаны, компания снята с номера |
| A8 | Новый ответственный занимает место в штате приёмника, и предупреждение о перерасходе оказывается ложным | §51.2: формула `seatsAfter` учитывает +1; предпросмотр принимает `newOwnerUserId` — иначе он считал бы не то, что произойдёт |

---

## 61. Отступления от SPEC и что нужно от `legal-counsel`

**Отступления (сознательные, с обоснованием; все подтверждены заказчиком — §64):**

1. **Биллинг-аккаунт заводится по требованию, а не при регистрации каждого пользователя.**
   SPEC §3.1 говорит «при регистрации автоматически заводится биллинг-аккаунт». Мы заводим его в
   момент, когда он впервые что-то значит: при создании первой компании или при первом назначении
   подписки (`BillingAccountProvisioner.EnsureAccountAsync`, §43.3).
   **Почему так:** подавляющее большинство зарегистрированных пользователей — клиенты салонов,
   которые никогда не заведут компанию; строка аккаунта у каждого из них — мусор без единого
   потребителя, который при этом придётся мигрировать, бэкапить и учитывать в каждом запросе
   «аккаунты платформы».
   **Почему это безопасно:** для владельца наблюдаемое поведение идентично — к моменту, когда он
   впервые видит хоть что-то про подписку, аккаунт уже существует; получить 404 на
   `GET /api/billing/subscription` может только тот, у кого нет ни одной компании, и он этот экран
   не открывает. Миграция при этом проще: аккаунты заводятся ровно тем, у кого есть компания,
   подписка или номер (§54.2), а не всей таблице пользователей.
   **Расхождение зафиксировано здесь намеренно**, чтобы на ревью оно читалось как решение, а не как
   недосмотр. Решение заказчика — §64 п. 3.
2. **Опция без цены не отбирается у того, кто уже оплатил** (§44.3 п. 5) — SPEC §3.3 п. 8 говорит
   «не предлагается никому», что про продажу, а не про отзыв оплаченного. Асимметрия с правилом
   деактивированного тарифа названа явно.
3. **Рубильник публикации прайса** — в SPEC его нет, есть требование «не публиковать до юриста».
   Флаг превращает организационное требование в техническое ограничение.
4. **`GrandfatheredEmployeeBonus` как отдельное поле** вместо «выдать опцию»: SPEC требует «лимит не
   меньше суммы», но не говорит как. Опция начала бы стоить денег, как только у неё появится цена.
5. **`confirmSeatOverflow` в теле переноса** — SPEC требует «администратор видел предупреждение до
   подтверждения»; без явного флага это требование непроверяемо машиной.
6. **Композитные FK для ко-тенантности** (§43.6) — в SPEC такого требования нет, есть правило
   §3.3 п. 5. Цена названа.
7. **Перенос компании умеет менять ответственного** (§51) — в SPEC US-77 об этом не сказано ничего;
   решение заказчика (§64 п. 2). Правило валидации нового ответственного (§51.1) — наше, и оно
   строже сегодняшнего `PUT .../owner`, потому что здесь вместе с управлением переезжают деньги.
8. **Таблица сравнения тарифов не делается** (§56.2) — прямое следствие требования «без
   горизонтального скролла на телефоне».

**Нужно от `legal-counsel` (до включения `pricing.public-enabled`, не раньше и не позже):**
все шесть вопросов SPEC §7, включая: кто сторона договора, если платит аккаунт, а компаниями
управляют разные люди, и что происходит с оплаченным периодом при переносе компании (в том числе
когда переносом заодно меняется ответственный, §51). Тексты вставляются в `PricingPage` и в
`/terms`; код при этом не меняется — только контент и один параметр платформы.

---

## 62. Карта ответов на открытые вопросы SPEC §9 (их десять)

| Вопрос SPEC §9 | Ответ |
|---|---|
| **1.** `BillingAccount` рядом или переименование `AccountSubscription` | §43.2–43.4: **рядом**. `BillingAccounts` — новая таблица с уникальным `OwnerUserId`; `AccountSubscription` **остаётся** той же таблицей и получает `BillingAccountId` (уникальный). Миграция — проставление FK, строки не переносятся и не размножаются; `SubscriptionChangeLog` не трогается. `AccountSubscriptions.OwnerUserId` сохраняется как колонка-история и как основа сверки «до/после» |
| **2.** Ревизия `Company.OwnerUserId`: деньги vs права | §45: полный инвентарь 30 сегодняшних чтений с вердиктом по каждому (12 переезжают, 18 остаются). Гарантия «никто не остался по инерции» — не дисциплина, а компилятор: удаление `GetEffectivePlanForOwnerAsync`/`ForOwners`, переименование `EffectivePlan.MaxEmployees → AccountMaxEmployees` и `MaxCompanies → AccountMaxCompanies`, новый обязательный параметр `NotificationGate.Evaluate(channelIsFunded)`. Плюс грепы §59 пп. 1–2, 8 |
| **3.** Судьба `PaidFromUtc/PaidUntilUtc` и `ChannelPaymentLog` | §47.3: **колонки остаются, не читаются и не пишутся** (прецедент `ContactEmail`); `ChannelPaymentLog` — историческая таблица; единственный источник правды об оплате — количество в опции `notifications.whatsapp` подписки аккаунта. `ChannelDto` сохраняет форму, но значения вычисляются из подписки. Ответ на «сколько оплачено / сколько заведено» — пара чисел в админской карточке аккаунта |
| **4.** «Номеров заведено больше, чем оплачено» (M > N) | §47.1: **оплату получают N номеров, заведённых раньше** (`CreatedAt ASC`, `Id ASC`). Остальные — `Unfunded`: не отправляют (`NotOnPaidPlan`), но не теряют назначения и не меняют состояние; оживают сразу после докупки. Правило стабильно, детерминировано и объяснимо; текст для пользователя обязан называть причину, работающий номер и два способа это исправить |
| **5.** Где считается расход суммарных лимитов, N+1, кеш | §46: `AccountUsageReader` — **два сгруппированных запроса** на список, независимо от N. Расход **не кешируется** (кеш счётчика лимита = разрешённое превышение); кешируется только каталог, 60 с. На анонимном `/api/companies` поля не считаются вовсе → ноль прибавки на самом горячем пути |
| **6.** Заявка: отдельная сущность или приём цикла 4 | §49: одна сущность `SubscriptionRequest`, привязанная к **аккаунту**; частичный уникальный индекс `WHERE Status = Pending` вместо проверок в коде; повторная отправка = перезапись (200). `NotificationChannel.RequestedAtUtc` перестаёт быть заявкой на деньги — владелец видит один механизм |
| **7.** Атомарность переноса компании, advisory lock | §51.3: одна транзакция, **два аккаунтных лока в порядке возрастания `Guid`**, плюс компанийный лок, если перенос заодно меняет ответственного; последовательность «посчитал → записал» зафиксирована по шагам; снятие с номера обязано идти до смены `BillingAccountId`, и это гарантирует композитный FK (§43.6). Отказ по лимиту компаний — 402 до любых записей |
| **8.** Форма публичного прайса и кеширование | §48: `GET /api/pricing`, отдельные публичные DTO, 60 с кеш + `ETag`/304 (страница не мигает), рубильник `pricing.public-enabled` → 404 при выключенном. Единицы измерения приходят готовой строкой `unitPriceText` (US-71 п. 3) |
| **9.** Порядок миграции и пересчёт `MaxEmployees` | §54: **две миграции** — схема (29) и данные + финальные ограничения (30); `Down` данных — no-op; предпроверка `billing-precheck.sql` до выката. Пересчёт «на компанию → суммарно» — через `BillingAccount.GrandfatheredEmployeeBonus = GREATEST(0, base*(companies-1), seatsUsed - base)`: лимит не уменьшается ни у кого, счёт не растёт ни у кого, шаг виден администратору и обратим |
| **10.** Что показывать в `CompanyDto` | §53.1: +7 полей (`planName`, `subscriptionStatus`, `paidUntil`, `employeeCount`, `accountSeatsUsed`, `accountSeatsLimit`, `canAddEmployee`); три уровня флагов сохраняются; `maxEmployees` меняет смысл на аккаунтный и помечается deprecated — кнопки переводятся на `canAddEmployee`. `billingAccountId` во владельческих DTO не появляется (Р8) |

---

## 63. Что изменилось по сравнению с редакцией 1 этого файла

| Тема | Редакция 1 (отменена) | Редакция 2 / 2.1 |
|---|---|---|
| Единица тарификации | компания (`CompanySubscription`) | **биллинг-аккаунт** (`BillingAccount` + существующий `AccountSubscription`) |
| Новых таблиц | 6 | 8, но подписка не создаётся заново: `AccountSubscriptionOption` заменяет `CompanySubscriptionOption`, добавлены `BillingAccount` и `CompanyOwnerChangeLog` |
| Миграция подписок | одна подписка → N подписок компаний | **проставление FK**, строки не размножаются |
| «Количество компаний» | перестаёт быть товаром, `MaxCompanies` удаляется из `EffectivePlan`, появляется антиспам-`PlatformSetting` | **снова товар** (Р4): `AccountMaxCompanies` остаётся, US-75 упразднена, параметр платформы не нужен |
| Лимит сотрудников | на компанию | **суммарно по аккаунту** + `GrandfatheredEmployeeBonus` для миграции |
| Канал рассылки | опция-переключатель на компании | **опция-количество, единица «номер»**, принадлежит аккаунту + правило funding (§47.1) |
| Смена ответственного | подписка не двигалась, писался `PayerChanged`, компания **снималась с номера** | деньги не трогаются вовсе, **компания остаётся на номере**, журнала подписки нет, появился `CompanyOwnerChangeLog` |
| Перенос компании | отсутствовал | **US-77** целиком: §51, композитный FK, 402/`confirmSeatOverflow` и (ред. 2.1) **необязательная смена ответственного в той же транзакции** с правилом валидации §51.1 |
| `ProfilePlanDto` | ломающее изменение формы | **форма сохраняется**, только дополняется |
| `EffectivePlan` | `MaxCompanies` удаляется | два переименования как инструмент ревизии; `PaidNotificationNumbers` и `Capabilities` добавлены |
| Производительность | +1 запрос на разрешение плана | то же + `AccountUsageReader` (§46) как отдельное решение под R11 |

**Сохранено без изменений:** стек (§42), приём «опции вливаются в `EffectivePlan`, гейты не
трогаются» (§44.1), строковые ключи возможностей (§44.2), рубильник публикации и кеш/ETag (§48),
OpenAPI + `schemathesis` + `deploy/checks` (§54.5, §57), неудаление истории
(`AccountSubscription`, `ChannelPaymentLog`, `SubscriptionChangeLog`, `PlatformSettingChangeLog`),
разбивка BE/FE с параллельностью и Prism-моком (§58).

---

## 64. Решения заказчика по вопросам редакции 2 (все пять получены)

Пять вопросов, вынесенных редакцией 2 «до необратимой миграции», закрыты. Открытых вопросов к
заказчику по модели **не осталось**; единственное внешнее ожидание цикла — `legal-counsel` перед
включением `pricing.public-enabled` (§61).

| № | Вопрос редакции 2 | Решение | Что изменилось в документах |
|---|---|---|---|
| **1** | Формула грандфатеринга мест: владелец трёх точек на тарифе «5 сотрудников» получает суммарный лимит 15 — навсегда, пока админ не обнулит | **Принято как спроектировано.** Обоснование заказчика: боевых платящих клиентов нет, цена ошибки низкая, тихая миграция важнее | Ничего. §54.4 и §43.3 помечены как решённые |
| **2** | Перенос компании не меняет ответственного | **Изменено.** В форме переноса появляется **необязательное** поле «новый ответственный»: задано — `BillingAccountId` и `OwnerUserId` меняются в одной транзакции; не задано — меняется только плательщик. Одна операция покрывает и продажу филиала постороннему, и перекладывание точки между своими аккаунтами | **§51 переписан** (51.0–51.4): два сценария, правило валидации нового ответственного, поправка к расчёту мест, порядок шагов и локов. §52 (лок `company-members`), §50 (общий `CompanyOwnerWriter`), §55, §58 (B5-10 зависит от B5-9), §59 (греп 8), §60 (A7, A8), §61 п. 7, контракт §51 и OpenAPI |
| **3** | Аккаунт заводится по требованию, а не при регистрации (расхождение с SPEC §3.1) | **Принято как спроектировано.** Для пользователя разницы нет, миграция проще | **§61 п. 1 развёрнут в явный абзац** с обоснованием и с указанием, что расхождение зафиксировано намеренно; ссылка из §43.3 |
| **4** | Правило «оплачены раньше заведённые номера» | **Принято.** Требование: текст для пользователя должен объяснять причину и подсказывать, что делать | **§47.1 дополнен таблицей текстов** `fundingText` для всех четырёх состояний и требованием приёмки; контракт §41/§53.6 и OpenAPI |
| **5** | Бесплатный тариф публикуется строкой прайса (1 компания / 1 сотрудник) | **Принято** | §43.4 и §48 помечены как решённые |

**Точка невозврата остаётся прежней — B5-13** (`BackfillBillingAccounts`): после неё
`Company.BillingAccountId` становится `NOT NULL`, появляются композитные FK, а
`GrandfatheredEmployeeBonus` проставлен по формуле §54.4. До неё всё остальное обратимо.
