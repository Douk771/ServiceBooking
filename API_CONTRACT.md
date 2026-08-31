# API_CONTRACT — цикл санации ServiceBooking

**Назначение.** Точка синхронизации backend- и frontend-разработчика на время цикла. Обе стороны работают
по этому файлу, а не по коду друг друга (`ARCHITECTURE.md` §7, таблица «Точки пересечения BE↔FE»).

**Что здесь есть.** Только шесть эндпоинтов, которые цикл реально трогает. Остальной API описан в
`API_DOCUMENTATION.md` и в этом цикле не меняется.

**Источник истины по решениям.** `SPEC.md` §0 (Q1–Q6) и `ARCHITECTURE.md` §3 (границы формата ошибок),
§8 (задачи T-B*/T-F*). Контракт ниже не вводит альтернатив — он лишь фиксирует то, что уже решено, в
терминах запрос/ответ.

**Базовая ревизия.** Коммит `263c661`, ветка `master`. Все ссылки «файл:строки» — по ней.

---

## 0. Общие соглашения (действуют для всех разделов)

### 0.1 Сериализация

| Тип C# | JSON | Пример |
|---|---|---|
| Имена свойств | camelCase (`AddControllers()` по умолчанию) | `masterId`, `startTime` |
| `enum` | **строка**, не число — `JsonStringEnumConverter` (`ServiceBooking.API/Program.cs:16-17`) | `"Confirmed"` |
| `DateOnly` | `"YYYY-MM-DD"` | `"2026-09-01"` |
| `TimeOnly` | `"HH:mm:ss"` | `"10:00:00"` |
| `decimal` | число | `800` |

Базовый путь фронта — `/api` (`frontend/src/api/client.ts:5`), токен подставляется интерцептором
(`client.ts:9-13`); 401 глобально разлогинивает и редиректит на `/login` (`client.ts:15-24`) — это
поведение цикл не трогает ни для одного эндпоинта.

### 0.2 Формат тел ошибок — жёсткая граница цикла

Повторяет `ARCHITECTURE.md` §3.1 и является ответом на риск R3 (`SPEC.md` §9).

| Код | Как формируется в контроллере | Content-Type | Тело | Меняется в цикле? |
|---|---|---|---|---|
| 400 | `BadRequest("текст")` | `text/plain; charset=utf-8` | голая строка | **нет** |
| 400 | автоматическая валидация `[ApiController]` (отсутствует обязательный query-параметр, не парсится `Guid`/`DateOnly`) | `application/problem+json` | `ValidationProblemDetails` от фреймворка | **нет** (поведение фреймворка, циклом не затрагивается) |
| 401 | пайплайн JWT | — | пустое | **нет** |
| 402 | `StatusCode(402, "текст")` | `text/plain; charset=utf-8` | голая строка | **нет** |
| 403 | `Forbid()` | — | **пустое** | **нет** |
| 404 | `NotFound()` / `NotFound("текст")` | — / `text/plain` | пусто / голая строка | **нет** |
| 409 | `Conflict("текст")` | `text/plain; charset=utf-8` | голая строка | **нет** |
| 500 | необработанное исключение | **`application/problem+json`** | `ProblemDetails` + `traceId` | **да, новое** (T-B7) |

Практический вывод для фронта: `getBookingErrorMessage` (`frontend/src/utils/bookingError.ts:13`) читает
`response.data` как строку — и продолжает читать её как строку. Мапперы `frontend/src/utils/*Error.ts`
в связи с введением `ProblemDetails` **не переписываются**.

### 0.3 Тело 500 (новое, появляется в T-B7)

Активно во всех окружениях, кроме `Development` (там работает developer exception page).
Окружение `Testing` (`ServiceBooking.Tests/Infrastructure/CustomWebApplicationFactory.cs:15`) обработчик
**включает** — на этом построен тест ADM-035.

```
HTTP/1.1 500 Internal Server Error
Content-Type: application/problem+json
```
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "An unexpected error occurred.",
  "status": 500,
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```
Полей `detail`/`exception`/stack trace нет **никогда** (US-05 п. 3). `traceId` непустой.
Для фронта 500 попадает в ветку `default` всех трёх мапперов — текст «Произошла ошибка. Попробуйте снова.»
остаётся прежним. Правок на фронте не требуется.

---

## 1. `GET /api/workinghours` — US-04, задача T-B5

Файл: `ServiceBooking.API/Controllers/WorkingHoursController.cs:18-33`, предикат прав — `:98-106`.

### 1.1 Текущее поведение

**Доступ:** `[Authorize]` на классе (`WorkingHoursController.cs:14`). Никакой проверки принадлежности
в `Get` нет — **любой залогиненный** пользователь читает расписание любого мастера.

**Query-параметры** (все обязательны, `:19-23`):

| Имя | Тип | Обяз. | Примечание |
|---|---|---|---|
| `masterId` | `string` | да | Identity-Id мастера |
| `companyId` | `Guid` | да | |
| `from` | `DateOnly` | да | включительно |
| `to` | `DateOnly` | да | включительно |

**200 OK** — массив `WorkingHoursDto`, отсортирован по `date` по возрастанию (`:29`), перерывы приходят
через `Include` (`:26`):

```json
[
  {
    "id": "e1f2a3b4-0000-1111-2222-333344445555",
    "masterId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
    "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "date": "2026-09-01",
    "startTime": "10:00:00",
    "endTime": "19:00:00",
    "isWorking": true,
    "breaks": [{ "id": "…", "startTime": "13:00:00", "endTime": "14:00:00" }]
  }
]
```

**Коды сейчас:** `200` (в т.ч. пустой массив, если записей нет — WH-001), `401` анонимный,
`400` от автоматической валидации при отсутствии/непарсибельности параметров. **`403` не возвращается никогда.**

### 1.2 Целевое поведение

Параметры, форма тела и код успеха **не меняются**. В начало метода добавляется тот же предикат, что уже
защищает `PUT` (`:39`) и `DELETE` (`:91`):

```
401 (анонимный)  →  403 (не имеет права)  →  200
```

`403` возвращается, если вызывающий **не** является ни одним из:
- `SuperAdmin` (`CanManage`, `:100`);
- самим мастером `masterId` (`:101`);
- участником компании `companyId` с ролью `CompanyOwner` (`:102-105`).

Тело 403 — **пустое** (`Forbid()`), как везде в проекте.

Свойство предиката, которое сохраняется намеренно и должно быть отражено в `API_DOCUMENTATION.md`:
мастер, запрашивающий **свой** `masterId`, получает 200 при любом `companyId`, в том числе чужом
(проверка `requesterId == masterId` идёт раньше проверки членства). Это ровно то же поведение, что
у `PUT`/`DELETE` сегодня; расширять предикат в этом цикле не требуется (`ARCHITECTURE.md` §2.4).

### 1.3 Дельта

| Что | Было | Стало |
|---|---|---|
| Проверка принадлежности | отсутствует | `CanManage(masterId, companyId, userId)` |
| Посторонний залогиненный | `200` + расписание | **`403`**, тело пустое |
| Мастер о себе | `200` | `200` (без изменений) |
| Владелец о своём мастере | `200` | `200` (без изменений) |
| Анонимный | `401` | `401` (без изменений) |
| Форма `WorkingHoursDto` | — | **не меняется** |

### 1.4 Ломающее изменение? Да для API, нет для фронта

**Меняется код ответа** (200 → 403) для сценария «посторонний читает чужое расписание». Форма тела успеха
не меняется.

Потребители:
- слой запросов — `frontend/src/api/workingHours.ts:30-32` (`workingHoursApi.get`);
- единственный вызов — `frontend/src/pages/owner/ScheduleTab.tsx:276-280`, `masterId` берётся из
  `selfMasterId` (мастер, `CabinetPage.tsx:146`) либо из списка участников своей компании
  (`ScheduleTab.tsx:262-271`).

**Править фронт не нужно.** Оба живых сценария (мастер о себе, владелец о своём мастере) остаются 200.
Маппера ошибок у этого вызова нет и заводить его в цикле не требуется: `ScheduleTab` использует только
`data`/`isLoading` (`:276`), при ошибке покажет пустой месяц. Остаточный риск R7 закрывается ручным
чек-листом DoD п. 8, а не кодом.

### 1.5 Тесты, фиксирующие новое поведение

`ServiceBooking.Tests/Tests/WorkingHoursTests.cs`, номера заданы `SPEC.md` §5.1 п. 6:

| ID | Сценарий | Ожидание |
|---|---|---|
| WH-011 | посторонний залогиненный | `403` |
| WH-012 | мастер компании запрашивает **чужого** мастера той же компании | `403` |
| WH-013 | мастер запрашивает себя | `200` |
| WH-014 | владелец запрашивает мастера своей компании | `200` |

Регрессия: WH-001..WH-010 остаются зелёными; BK-тесты слотов не задеты (гость этот эндпоинт не зовёт).

---

## 2. `POST /api/bookings` — US-05, задача T-B7

Файл: `ServiceBooking.API/Controllers/BookingsController.cs:45-149`.

### 2.1 Текущее поведение

**Доступ:** атрибута `[Authorize]` нет — эндпоинт принимает и гостя, и залогиненного.

**Тело запроса** — `CreateBookingDto` (`ServiceBooking.API/DTOs/Bookings/BookingDto.cs:30-42`):

```json
{
  "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "serviceId": "aa11bb22-0000-1111-2222-333344445555",
  "masterId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
  "date": "2026-09-01",
  "startTime": "10:00:00",
  "notes": null,
  "guestName": "Иван",
  "guestPhone": "+79990000000",
  "guestEmail": null,
  "captchaToken": "…"
}
```
`startTime` фронт нормализует до `HH:mm:ss` перед отправкой (`frontend/src/api/bookings.ts:26`;
в цикле выносится в `toApiTime`, задача T-F6, **тело запроса при этом побайтово то же**).

**201 Created** — `BookingDto` + заголовок `Location` на `GET /api/bookings/{id}`
(`CreatedAtAction`, `:147`). Поля `BookingDto` — `DTOs/Bookings/BookingDto.cs:5-24` (`price` и
`companySlug` в нём **отсутствуют**, см. §3.5).

**Порядок проверок и коды сейчас:**

| # | Условие | Код | Тело (`text/plain`) | Строка |
|---|---|---|---|---|
| 1 | гость (`userId is null`) + компании нет | `404` | `Company not found` | `:60-61` |
| 2 | гость + `company.AllowSelfBooking == false` | `403` | пусто | `:62` |
| 3 | гость + капча включена, токена нет | `400` | `Captcha required for guest booking` | `:69-70` |
| 4 | гость + капча не прошла | `400` | `Invalid captcha` | `:73-74` |
| 5 | гость без `guestName`/`guestPhone` | `400` | `Name and phone are required for guest booking` | `:77-78` |
| 6 | тариф не даёт онлайн-запись и это не ручная запись сотрудника | `402` | `Online booking requires a paid subscription.` | `:85-87` |
| 7 | услуги нет | `404` | `Service not found` | `:89-90` |
| 8 | слот занят | `409` | `Time slot is no longer available` | `:128-134` |

**Дефект, который чинит цикл:** проверки 1–2 находятся **внутри** `if (!isAuthenticated)` (`:58-79`).
Для **залогиненного** вызывающего несуществующий `CompanyId` не отсекается нигде: выполнение доходит до
`SaveChangesAsync` (`:137`) и падает нарушением FK → **500 с пустым телом** (вне Development).

### 2.2 Целевое поведение

Тело запроса, тело успешного ответа (`BookingDto`), код `201` и заголовок `Location` — **без изменений**.

Проверка существования компании поднимается в самое начало метода, до тарифного гейта
(`ARCHITECTURE.md` §3.3). Порядок после правки — **один для гостя и для залогиненного**:

```
404 «Company not found»
  → [только гость] 403 AllowSelfBooking → 400 captcha → 400 name+phone
  → 402 тарифный гейт
  → 404 «Service not found»
  → 409 конфликт слота
```

Тексты всех тел — **дословно прежние**, включая `Company not found` (тот же текст для гостя и для
залогиненного: контракт не расходится по ветвям, US-05 п. 1).

Дополнительно: любое необработанное исключение этого (и любого другого) эндпоинта отдаёт `500`
с телом `ProblemDetails` из §0.3.

### 2.3 Дельта

| Сценарий | Было | Стало |
|---|---|---|
| **Залогиненный, `companyId` не существует** | `500`, тело пустое (FK-нарушение на `SaveChangesAsync`) | **`404`**, `text/plain` `Company not found` |
| Залогиненный владелец без тарифа + `companyId` не существует | `402` не достигался (падало 500) | **`404`**, не `402` (порядок проверок обязателен, US-05 п. 2) |
| Гость, `companyId` не существует | `404` `Company not found` | без изменений |
| Все остальные коды 400/402/403/409 и их тексты | — | **без изменений** |
| Успешный ответ | `201` + `BookingDto` | без изменений |
| Необработанное исключение | `500`, пустое тело | `500`, `application/problem+json` |

Внутренняя оптимизация, на контракт не влияющая: уже загруженный `company` переиспользуется в проверке
предоплаты (`:100-101`) и в `bookingCompany` (`:141`) — лишний `FindAsync` уходит.

### 2.4 Ломающее изменение? Формально да (500 → 404), для фронта — нет

**Меняется код ответа** для одного сценария, тело успеха не трогается.

Потребители:
- слой запросов — `frontend/src/api/bookings.ts:23-27` (`bookingsApi.create`);
- маппер ошибок — `frontend/src/utils/bookingError.ts`;
- показ ошибки — `frontend/src/components/booking/BookingModal.tsx:318` и
  `frontend/src/components/booking/ManualBookingModal.tsx:410`.

**Править фронт не нужно, и это проверяется:**
- `404` уже обработан — `bookingError.ts:26` → «Услуга или компания не найдена.» Пользователь получает
  осмысленный текст там, где раньше видел «Произошла ошибка».
- `500` в `switch` не входит и попадает в `default` (`:33-34`) — желаемое поведение.
- Тела 400/402/403/409 остаются строками, поэтому проверки `lower.includes('captcha')` (`:28`),
  `includes('name')`/`includes('phone')` (`:30`) и `includes('expired')` (`:18`) продолжают работать.

В PR T-B7 обязателен явный пункт: тела 400/402/403/404/409 не изменились, мапперы `src/utils/*Error.ts`
не правились (R3).

### 2.5 Тесты, фиксирующие новое поведение

`ServiceBooking.Tests/Tests/BookingsFlowSmokeTests.cs` (BK-027/BK-028 — `SPEC.md` §5.2 п. 6 и
`ARCHITECTURE.md` T-B7), `ServiceBooking.Tests/Tests/AdminTests.cs` (ADM-035):

| ID | Сценарий | Ожидание |
|---|---|---|
| BK-027 | залогиненный `POST /api/bookings` с несуществующим `companyId` | `404`, тело содержит `Company not found` |
| BK-028 | то же, но вызывающий — владелец **без** тарифа | `404`, **не** `402` (порядок проверок) |
| ADM-035 | `PUT /api/admin/owners/{неизвестный}/subscription` (стабильный источник честного 500) | `500`, `content-type: application/problem+json`, `traceId` непустой |
| CO-066 | попутно в той же задаче: `SuperAdmin` добавляет участника с ролью `"Bogus"` (`CompaniesController.cs:347`) | `400` вместо `500` |

Регрессия: BK-001..BK-026 целиком, особенно гостевая ветка BK-004..BK-009 и `manual` BK-024..BK-026.

---

## 3. `GET /api/bookings/client` — US-06 и US-07, задача T-B8

Файл: `ServiceBooking.API/Controllers/BookingsController.cs:197-218`.

### 3.1 Текущее поведение

**Доступ:** `[Authorize]` (`:198`), любой залогиненный. Возвращает записи, где `ClientId == userId`
(`:207`) — то есть только самостоятельные записи клиента, ручные записи мастера от его имени сюда не
попадают.

**Query-параметры:** `status` (`string?`, опционально, `:199`).

**Фильтрация сейчас (`:209-210`):** `Enum.TryParse<BookingStatus>(status, out var s)` — **без**
`ignoreCase`. Следствия:

| `status` | Что происходит сегодня |
|---|---|
| не передан / пустой | фильтр не применяется, возвращается всё |
| `Confirmed`, `Cancelled`, `Completed`, `NoShow`, `Pending` | фильтрует по статусу |
| `cancelled`, `completed` (нижний регистр) | **не парсится → фильтр молча игнорируется**, возвращается всё |
| `upcoming` | **не парсится → фильтр молча игнорируется**, возвращается всё ← дефект US-07 |
| `3` (числовое значение enum) | парсится → фильтрует по `Completed` |
| любой мусор | фильтр молча игнорируется, `400` не возникает |

**200 OK** — массив `BookingDto`, сортировка `date` desc, затем `startTime` desc (`:212`).

Фронт сегодня шлёт `upcoming` для вкладки «Предстоящие» (`frontend/src/pages/ClientBookingsPage.tsx:35`),
поэтому вкладка показывает **все** записи подряд.

### 3.2 Целевое поведение

Тело успешного ответа, сортировка и набор полей `BookingDto` — **без изменений**.

Разбор `status` переезжает в чистую функцию `BookingFilters.TryParseClientStatus`
(`ARCHITECTURE.md` §2.3), правило «предстоящие» — в `BookingFilters.Upcoming(today, nowTime)`.

| `status` | Целевое поведение |
|---|---|
| не передан / пустой | всё, без фильтра (как сейчас) |
| `upcoming` / `Upcoming` / `UPCOMING` (регистронезависимо) | только `Confirmed` **или** `Pending` **и** `date > today` **или** (`date == today` **и** `startTime > nowTime`) |
| имя `BookingStatus` в любом регистре: `Completed`, `cancelled`, `NOSHOW` | фильтр по статусу |
| числовое значение enum (`3`) | фильтр по статусу — поведение сохраняется, `Enum.TryParse` его принимает |
| всё остальное | **`400`**, `text/plain` |

**Тело 400 фиксируется контрактом** (голая строка, как все прочие 400 проекта):

```
Unknown status filter. Expected: upcoming, Pending, Confirmed, Cancelled, Completed, NoShow.
```

**Что считается «сейчас»:** `DateTime.UtcNow`, из него `DateOnly.FromDateTime` → `today` и
`TimeOnly.FromDateTime` → `nowTime`; оба уезжают в SQL параметрами. Обоснование и ссылка на отложенные
таймзоны — `ARCHITECTURE.md` §2.3. Практическое следствие для фронта: для клиента в UTC+3 запись
остаётся во вкладке «Предстоящие» чуть дольше положенного — это осознанный безопасный режим отказа.

**Граница:** сегодняшняя запись со `startTime` **ровно равным** `nowTime` в «предстоящие` **не** входит
(строгое `>`, `SPEC.md` §5.3 «предположение: граница по дате+времени»).

### 3.3 Дельта

| Что | Было | Стало |
|---|---|---|
| `status=upcoming` | `200`, **все** записи | `200`, только будущие `Confirmed`/`Pending` |
| `status=garbage` | `200`, все записи | **`400`** + текст |
| `status=cancelled` (нижний регистр) | `200`, все записи (парсинг не прошёл) | `200`, отфильтровано по `Cancelled` |
| `status=Completed`/`Cancelled` | фильтрует | без изменений |
| `status` не передан | всё | без изменений |
| Форма `BookingDto`, сортировка | — | **не меняется** |

### 3.4 Ломающее изменение? Для API да (новый 400), для фронта нет

**Появляется новый код ответа 400** там, где раньше был 200. Форма тела успеха не меняется.

Потребители:
- слой запросов — `frontend/src/api/bookings.ts:50-51` (`bookingsApi.getClientBookings`);
- вызов — `frontend/src/pages/ClientBookingsPage.tsx:35-40`.

**Править фронт не нужно.** Фронт отправляет ровно четыре значения (`undefined`, `upcoming`, `Completed`,
`Cancelled`, `ClientBookingsPage.tsx:35`) — ни одно из них не даёт 400. Вкладка «Предстоящие» просто
начинает работать. Маппера ошибок у этого вызова нет и в цикле не заводится.

Остаточный (принятый) риск: если когда-нибудь фронт пошлёт неизвестный `status`, `ClientBookingsPage`
покажет пустой список без сообщения — страница читает только `data`/`isLoading` (`:37-40`) и ошибку не
рендерит. Заводить маппер под невозможный сегодня сценарий в этом цикле не будем.

### 3.5 Что здесь делает US-06 (решение Q3) — и чего он **не** делает

`BookingDto` (`ServiceBooking.API/DTOs/Bookings/BookingDto.cs:5-24`) не содержит `Price` и `CompanySlug`.
TS-тип объявляет их опциональными (`frontend/src/types/index.ts:64,78`), из-за чего блок цены
(`ClientBookingsPage.tsx:126-130`) и кнопка «Записаться снова» (`:155-163`) не отрисовываются никогда.

**Контрактная дельта US-06 = ноль.** Бэкенд не трогается: `BookingDto` и `MapToDto`
(`BookingsController.cs:364-368`) остаются как есть (`SPEC.md` §5.3 п. 5). Правится только фронт (T-F1):
из TS-типа удаляются `companySlug` и `price`, мёртвые ветки UI вырезаются. После этого TS-тип
**совпадает** с фактическим DTO — это и есть цель истории.

### 3.6 Тесты, фиксирующие новое поведение

Функциональные — `ServiceBooking.Tests/Tests/BookingsFlowSmokeTests.cs` (BK-029/BK-030 заданы
`SPEC.md` §5.3 п. 4, BK-031 добавлен `ARCHITECTURE.md` T-B8):

| ID | Сценарий | Ожидание |
|---|---|---|
| BK-029 | `status=upcoming` при наличии прошлой и будущей записи | `200`, только будущая `Confirmed` |
| BK-030 | `status=Completed` и `status=Cancelled` | `200`, работает как раньше (регрессия) |
| BK-031 | `status=garbage` | `400` + текст из §3.2 |

Юнит — `ServiceBooking.UnitTests/BookingFiltersTests.cs` (~12 кейсов, перечень в `ARCHITECTURE.md` §2.3):
`TryParseClientStatus` на `null`/пусто/`upcoming`/`Upcoming`/`Completed`/`cancelled`/`garbage`;
`Upcoming(...).Compile()` на завтра/вчера/сегодня-позже/сегодня-раньше/сегодня-**ровно**/`Cancelled`/`Completed`.

Фронтовых юнит-тестов эта история не порождает.

---

## 4. `POST /api/services`, `PUT /api/services/{id}`, `DELETE /api/services/{id}` — US-09 (Q1), задача T-B6

Файл: `ServiceBooking.API/Controllers/ServicesController.cs`, предикат — `:82-92`.

### 4.1 Текущее поведение

Общий предикат `CanManageCompany(companyId)` (`:82-92`) для всех трёх методов:
`SuperAdmin` → да (`:86`); иначе — есть ли строка `CompanyMembers` с ролью
**`CompanyOwner` или `Master`** (`:88-91`).

**`POST /api/services`** (`:27-48`) — `[Authorize]`.
Тело — `CreateServiceDto` (`DTOs/Services/ServiceDto.cs:13-20`):
```json
{ "companyId": "…", "name": "Стрижка", "description": null, "durationMinutes": 45, "price": 800, "imageUrl": null }
```
Ответ — **`200 OK`** (не 201) с `ServiceDto` (`:47`):
```json
{ "id": "…", "companyId": "…", "name": "Стрижка", "description": null, "durationMinutes": 45, "price": 800, "imageUrl": null }
```
Коды: `200`, `401` анонимный, `403` если `CanManageCompany` ложно.
Существующая особенность: `404` на несуществующую компанию **не возвращается** — обычный пользователь
получает `403` (строки `CompanyMembers` нет), `SuperAdmin` доходит до нарушения FK и получает `500`.
Цикл это **не чинит** (вне скоупа US-09); после T-B7 такой 500 просто станет `problem+json`.

**`PUT /api/services/{id}`** (`:50-67`) — `[Authorize]`. Тело — тот же `CreateServiceDto`.
Порядок: `404` если услуги нет (`:55`) → `403` если нет прав (`:56`) → `200` + обновлённый `ServiceDto`.
`imageUrl` обновляется только если пришёл не-`null` (`:62`). Обратите внимание: `404` проверяется **до**
прав — посторонний узнаёт о существовании услуги; поведение существующее (SVC-008), цикл его не меняет.

**`DELETE /api/services/{id}`** (`:69-80`) — `[Authorize]`.
`404` → `403` → `204 No Content`. Удаление **мягкое**: `IsActive = false` (`:77`), после чего услуга
пропадает из публичного `GET /api/services?companyId=` (SVC-010).

**`GET /api/services?companyId=`** (`:16-25`) — **анонимный**, отдаёт только `IsActive` услуги.

### 4.2 Целевое поведение

Из `CanManageCompany` удаляется ровно одно условие — `|| cm.Role == UserRole.Master` (`:91`).
Ветка `SuperAdmin` (`:86`) сохраняется. Больше в контроллере ничего не меняется.

| Вызывающий | POST | PUT | DELETE | GET |
|---|---|---|---|---|
| `SuperAdmin` | `200` | `200` | `204` | `200` |
| `CompanyOwner` своей компании | `200` | `200` | `204` | `200` |
| **`Master` своей компании** | **`403`** | **`403`** | **`403`** | `200` (без изменений) |
| Посторонний залогиненный | `403` | `403` | `403` | `200` |
| Анонимный | `401` | `401` | `401` | `200` |

Тела ответов, коды успеха (`200`/`200`/`204`) и форма `ServiceDto`/`CreateServiceDto` — **без изменений**.
Тело 403 — пустое.

Гарантии, которые контракт фиксирует явно:
- `PUT` мастером не меняет услугу в БД (`name`/`price`/`durationMinutes` прежние);
- `DELETE` мастером не снимает `IsActive` — услуга остаётся в публичном списке;
- `GET /api/services` **не затрагивается вообще** — сценарий `ManualBookingModal`
  (`frontend/src/components/booking/ManualBookingModal.tsx`, шаг выбора услуги) продолжает работать
  у мастера (US-09 п. 5).

### 4.3 Дельта

| Что | Было | Стало |
|---|---|---|
| `Master` → `POST /api/services` | `200` + созданная услуга | **`403`**, услуга не создана |
| `Master` → `PUT /api/services/{id}` | `200` + изменённая услуга | **`403`**, поля не изменены |
| `Master` → `DELETE /api/services/{id}` | `204`, `IsActive=false` | **`403`**, `IsActive` остаётся `true` |
| Владелец / `SuperAdmin` | как есть | без изменений |
| `GET /api/services` | публичный | без изменений |
| Форма `ServiceDto` / `CreateServiceDto` | — | **не меняется** |

### 4.4 Ломающее изменение? Да для API, нет для фронта

**Меняется код ответа** (200/204 → 403) для роли `Master`. Форма тел не меняется.

Потребители:
- слой запросов — `frontend/src/api/services.ts:16-19` (`create`/`update`/`delete`);
- единственный UI CRUD услуг — `ServicesTab` внутри
  `frontend/src/pages/owner/CompanyManagePage.tsx`, роут `/owner/company/:id` защищён
  `ProtectedRoute roles={['CompanyOwner','SuperAdmin']}` (`frontend/src/App.tsx:64-68`);
- ссылка на страницу есть только в списке **своих** компаний (`CabinetPage.tsx:79`);
- в кабинете мастера вкладки «Услуги» нет.

**Править фронт не нужно.** Через интерфейс мастер сегодня физически не может дойти до этих вызовов;
маппера ошибок у `servicesApi` нет и заводить его не требуется. Остаточный риск (прямые вызовы API)
закрыт решением Q1 заказчика; откат — одна строка предиката (R1).

Обязательная ручная проверка (DoD п. 8): вход мастером → `ManualBookingModal` → услуги компании видны,
ручная запись создаётся.

### 4.5 Тесты, фиксирующие новое поведение

`ServiceBooking.Tests/Tests/ServicesTests.cs`. Два теста **инвертируются в том же PR** — это единственные
тесты цикла, меняющие смысл (`SPEC.md` §6 п. 2):

| ID | Было | Стало |
|---|---|---|
| SVC-003 (`ServicesTests.cs:49-61`) | `Create_ByMaster_Succeeds` → `200` | `Create_ByMaster_ReturnsForbidden` → `403` |
| SVC-007 (`ServicesTests.cs:105-118`) | `Update_ByMaster_UpdatesFields` → `200` | `Update_ByMaster_ReturnsForbidden` → `403` + проверка через `GET /api/services?companyId=`, что имя и цена не изменились |
| SVC-013 | — | новый: `Delete_ByMaster_ReturnsForbidden` → `403` + услуга осталась в публичном списке |
| SVC-014 | — | новый: `GetByCompany_ByMaster_StillWorks` → `200`, мастер читает услуги компании |

Регрессия: SVC-002/006/010 (владелец), SVC-004/009/011 (посторонний → 403), SVC-005 (анонимный → 401),
SVC-008 (`404` до прав) — все зелёные без правок.

---

## 5. `GET /api/companies/{id}/masters` — US-12 (Q6), задача T-B9

Файл: `ServiceBooking.API/Controllers/CompaniesController.cs:74-99`.

### 5.1 Текущее поведение

**Доступ:** анонимный (атрибута авторизации нет, `:75-76`).

**Параметры:** `id` (`Guid`, путь), `serviceId` (`Guid?`, query, опционально).

**Логика (`:78-94`):**
1. `memberQuery` = все `CompanyMembers` компании `id`, у которой `Company.IsActive` — **по роли не фильтруется** (`:78-80`).
2. Если передан `serviceId`: берутся `MasterServices.MasterId` для этой услуги (`:84-87`); если список
   непустой — `memberQuery` сужается по нему (`:90-91`). Если привязок нет **ни у кого** — fallback:
   показываются все (`:89`).
3. Материализация (`:94`) и маппинг в `MasterPublicDto`.

**200 OK** — массив `MasterPublicDto` (`DTOs/Companies/CompanyDto.cs:56-62`):
```json
[{ "userId": "…", "firstName": "Анна", "lastName": "П.", "avatarUrl": null, "bio": "Колорист" }]
```
Несуществующая или неактивная компания → `200` и **пустой массив** (не `404`).

Дефект: участник с ролью `Client` (`UserRole.Client == 0`) попадает в публичный список.

### 5.2 Целевое поведение

В `memberQuery` (`:78-80`), **до** материализации, добавляется ролевой фильтр:
`cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner`.

Всё остальное сохраняется дословно: фильтр по `serviceId` и его fallback работают **поверх** ролевого
фильтра; владелец из списка не исчезает (он часто и есть единственный мастер); коды ответов, форма
`MasterPublicDto` и поведение на несуществующей компании (`200 []`) не меняются.

### 5.3 Дельта

| Что | Было | Стало |
|---|---|---|
| Участник с ролью `Client` | попадает в список | **не попадает** |
| Участники `Master` / `CompanyOwner` | попадают | без изменений |
| `serviceId` + fallback «привязок нет ни у кого» | работает | без изменений (применяется поверх ролевого фильтра) |
| Код ответа | `200` | `200` |
| Форма `MasterPublicDto` | — | **не меняется** |

### 5.4 Ломающее изменение? Нет

Код ответа и форма тела не меняются — меняется только состав массива. Потребители:
- слой запросов — `frontend/src/api/companies.ts:55-56` (`companiesApi.getMasters`);
- публичная страница компании и шаги выбора мастера в `BookingModal`/`ManualBookingModal`.

**Править фронт не нужно.** Список просто перестаёт содержать не-сотрудников.

Эксплуатационное требование перед деплоем (не блокирует разработку, `SPEC.md` §5.7): прогнать по боевой БД
`SELECT * FROM "CompanyMembers" WHERE "Role" = 0;` — если строки найдутся, разобрать каждую руками.
Штатным путём такая строка не создаётся: `CanAssignRole` (`CompaniesController.cs:492`) разрешает
владельцу только `Master`/`CompanyOwner`.

### 5.5 Тесты, фиксирующие новое поведение

`ServiceBooking.Tests/Tests/CompaniesTests.cs` (номер задан `ARCHITECTURE.md` T-B9; `SPEC.md` §5.7 п. 4
оставляет его как `CO-xxx`):

| ID | Сценарий | Ожидание |
|---|---|---|
| CO-067 | участник с ролью `Client` (создаётся напрямую через scope — штатным путём такой строки не бывает) | в ответе **отсутствует**; `Master` и `CompanyOwner` присутствуют |

Регрессия: CO-012 (`:372`, список мастеров), CO-013 (`:387`, фильтр по `serviceId`),
CO-014 (`:408`, fallback без привязок) — зелёные без правок.

---

## 6. `DELETE /api/admin/plans/{id}` — US-08 (Q2), задачи T-B4 (BE) и T-F7 (FE)

Файл: `ServiceBooking.API/Controllers/AdminController.cs:321-329`.

### 6.1 Текущее поведение

**Доступ:** `[Authorize(Roles = "SuperAdmin")]` на классе (`AdminController.cs:14`).

**Параметры:** `id` (`Guid`, путь). Тела запроса нет.

**Логика (`:322-328`):** найти `SubscriptionPlanConfig`; если нет — `404` (пустое тело);
иначе `IsActive = false`, `SaveChanges`, **`204 No Content`**. Подписчики не проверяются.

**Коды сейчас:** `204`, `401`, `403`, `404`.

Практический дефект (`SPEC.md` §5.4): «удалённый» тариф продолжает действовать, потому что
`SubscriptionResolver` (`ServiceBooking.API/Services/SubscriptionResolver.cs:86-89`) проверяет только
`sub.IsActive` и `sub.PaidUntil`, но **не** `PlanConfig.IsActive`.

### 6.2 Целевое поведение

Порядок проверок:

```
401 / 403 (не SuperAdmin)
  → 404 плана нет
  → 409 есть хотя бы одна AccountSubscription с этим PlanConfigId и IsActive = true
  → 204 мягкое удаление, как сейчас
```

**Тело 409 — `Conflict(...)`, `text/plain; charset=utf-8`, голая строка** (формат для 409 не меняется,
§0.2). Контракт фиксирует шаблон:

```
Plan has 3 active subscriber(s). Move them to another plan first.
```

где `3` — точное количество `AccountSubscription` с `PlanConfigId == id` и `IsActive == true`.
Число — **единственная переменная часть**; фронт достаёт его регуляркой по первой группе цифр
(`/(\d+)/`) и обязан корректно отработать её отсутствие.

Одновременно (T-B4, та же задача) `SubscriptionResolver` начинает учитывать `PlanConfig.IsActive`:
подписка «годна», только если `PlanConfig is { IsActive: true }` — иначе `EffectivePlan.Free`. На форму
ответов **этого** эндпоинта это не влияет, но влияет на `onlineBookingEnabled` в
`GET /api/companies/{slug}` и на 402 в `POST /api/bookings` для владельца, сидящего на деактивированном
плане. Именно поэтому 409 обязателен: он делает такую ситуацию недостижимой (Q2, риск R2).

### 6.3 Дельта

| Что | Было | Стало |
|---|---|---|
| План **без** активных подписчиков | `204`, `IsActive=false` | без изменений |
| **План с ≥1 активной подпиской** | `204`, тариф «удалён», но продолжал работать у подписчиков | **`409`** + `text/plain` с количеством; `IsActive` **не** снимается |
| Плана нет | `404` | без изменений |
| Не `SuperAdmin` | `401`/`403` | без изменений |

### 6.4 Ломающее изменение? **Да, и это единственное, требующее правок фронта**

**Появляется новый код ответа 409** там, где раньше всегда был `204`.

Потребители:
- слой запросов — `frontend/src/api/plans.ts:23` (`plansApi.deactivate`);
- мутация — `frontend/src/pages/admin/PlansTab.tsx:97-100` (`deactivateMut`) — сегодня у неё **нет**
  обработчика ошибки, `onSuccess` просто инвалидирует кеш, то есть 409 будет проглочен молча и админ
  решит, что тариф деактивирован;
- кнопка «Деактивировать» — `PlansTab.tsx:182-189`.

**Что делает фронт (T-F7):**
1. Новый маппер `frontend/src/utils/planError.ts` по конвенции `src/utils/*Error.ts` —
   именованный экспорт `getPlanErrorMessage(error: unknown): string`:

   | Статус / условие | Возвращаемый текст |
   |---|---|
   | `409` + в теле есть число `N` | `На этом тарифе есть активные подписчики (N). Сначала переведите их на другой тариф.` |
   | `409` без числа в теле | `На этом тарифе есть активные подписчики. Сначала переведите их на другой тариф.` |
   | `404` | `Тариф не найден` |
   | всё остальное, включая отсутствие `response` | `Не удалось деактивировать тариф` |

   Как и в остальных мапперах, тело читается как строка: `typeof error.response.data === 'string'`.
2. Показ `getPlanErrorMessage(deactivateMut.error)` рядом с кнопкой «Деактивировать»
   (`PlansTab.tsx:182-189`), когда `deactivateMut.isError`.

T-F7 кодируется **параллельно** T-B4, по этому разделу, без ожидания мёржа бэкенда
(`ARCHITECTURE.md` §7, жёсткая связь T-B4 → T-F7 существует только для интеграционной проверки).

**Смежная правка того же PR-пакета, на контракт этого эндпоинта не влияющая:**
`PUT /api/admin/owners/{ownerUserId}/subscription` начинает возвращать `400` (`text/plain`) при попытке
назначить несуществующий или неактивный `planConfigId` (`AdminController.cs:138-179`, US-08 п. 3).
Фронт — T-F8: `SubscriptionModal` (`frontend/src/pages/AdminPage.tsx:51-95`) помечает текущий неактивный
план как «(неактивен)» и оставляет его выбранным, чтобы сохранение не сбросило владельца на Free.
Список выбора остаётся `activePlans` (`AdminPage.tsx:59`).

### 6.5 Тесты, фиксирующие новое поведение

Функциональные — `ServiceBooking.Tests/Tests/AdminTests.cs` (номера заданы `SPEC.md` §5.4 п. 5):

| ID | Сценарий | Ожидание |
|---|---|---|
| ADM-032 | `DELETE /api/admin/plans/{id}` для плана с активным подписчиком | `409`, тело содержит количество; `IsActive` плана осталось `true` |
| ADM-033 | `PUT /api/admin/owners/{id}/subscription` с неактивным `planConfigId` | `400` |
| ADM-034 | владелец на неактивном плане | получает `Free` — проверяется через `onlineBookingEnabled` в `GET /api/companies/{slug}` или через `402` на `POST /api/bookings` |

Хелперы: `ApiTestBase.CreateTestPlanConfigAsync` (`ServiceBooking.Tests/Infrastructure/ApiTestBase.cs:186-204`)
+ правка `IsActive` через scope, `SetSubscriptionAsync` (`ApiTestBase.cs:275-307`).
Регрессия: 31 существующий `ADM-` тест зелёный.

Юнит — `ServiceBooking.UnitTests/SubscriptionResolverRulesTests.cs`, 7 кейсов (`SPEC.md` §4.2), из них
ключевой для этого раздела: `PlanConfig.IsActive = false` → `EffectivePlan.Free`.

Фронт — `frontend/src/utils/planError.test.ts` (T-F7): 409 с числом, 409 без числа, 404, `default`.

---

## 7. Сводная таблица ломающих изменений цикла

«Ломающее» = меняется код ответа или форма тела для сценария, который раньше отвечал иначе.

| # | Эндпоинт | Было → Стало | Что меняется | Кто на фронте затронут | Нужна правка фронта? |
|---|---|---|---|---|---|
| 1 | `GET /api/workinghours` | посторонний залогиненный: `200` + расписание → **`403`** (пустое тело) | код ответа | `frontend/src/api/workingHours.ts:30-32`; `frontend/src/pages/owner/ScheduleTab.tsx:276-280` (`selfMasterId` из `CabinetPage.tsx:146`) | **Нет.** Оба живых сценария остаются 200; закрывается ручным чек-листом (R7) |
| 2 | `POST /api/bookings` | залогиненный + несуществующий `companyId`: `500` (пустое тело) → **`404`** `Company not found` (`text/plain`) | код ответа | `frontend/src/api/bookings.ts:23-27`; `frontend/src/utils/bookingError.ts:26` (404 уже обработан); показ — `BookingModal.tsx:318`, `ManualBookingModal.tsx:410` | **Нет.** Пользователь начинает видеть точный текст вместо «Произошла ошибка» |
| 3 | Любой эндпоинт | необработанное исключение: `500` пустое → **`500` `application/problem+json`** | форма тела (**только** для 500) | все мапперы `frontend/src/utils/*Error.ts` — ветка `default` | **Нет.** 500 ни в одном `switch` не разбирается (R3) |
| 4 | `GET /api/bookings/client` | `status=upcoming`: `200` со **всеми** записями → `200` только с будущими; `status=garbage`: `200` со всеми → **`400`** (`text/plain`) | поведение фильтра + новый код | `frontend/src/api/bookings.ts:50-51`; `frontend/src/pages/ClientBookingsPage.tsx:35` | **Нет.** Фронт шлёт только `upcoming`/`Completed`/`Cancelled`/ничего |
| 5 | `POST /api/services` | `Master` своей компании: `200` → **`403`** | код ответа | `frontend/src/api/services.ts:16`; UI — `ServicesTab` в `CompanyManagePage`, роут защищён `App.tsx:64-68` | **Нет.** Через UI мастер туда не попадает |
| 6 | `PUT /api/services/{id}` | `Master` своей компании: `200` → **`403`** | код ответа | `frontend/src/api/services.ts:17-18` | **Нет** |
| 7 | `DELETE /api/services/{id}` | `Master` своей компании: `204` → **`403`** | код ответа | `frontend/src/api/services.ts:19` | **Нет** |
| 8 | `GET /api/companies/{id}/masters` | участник с ролью `Client` исчезает из массива | **только состав массива**; код и форма DTO прежние | `frontend/src/api/companies.ts:55-56` | **Нет** |
| 9 | **`DELETE /api/admin/plans/{id}`** | план с активными подписчиками: `204` → **`409`** + `text/plain` с количеством | код ответа + новое тело | `frontend/src/api/plans.ts:23`; `frontend/src/pages/admin/PlansTab.tsx:97-100` (мутация **молча глотает ошибку**), кнопка `:182-189` | **ДА.** Новый `frontend/src/utils/planError.ts` + показ ошибки (T-F7) |
| 10 | `PUT /api/admin/owners/{id}/subscription` (смежное с №9) | неактивный/несуществующий `planConfigId`: `204` → **`400`** (`text/plain`) | код ответа | `frontend/src/api/admin.ts:89-90`; `SubscriptionModal` в `frontend/src/pages/AdminPage.tsx:51-95` | **ДА, косвенно.** T-F8: пометка «(неактивен)» у текущего плана, чтобы сохранение не сбрасывало на Free |

**Итог для фронта:** из десяти позиций правки требуют ровно две — №9 (`planError.ts` + `PlansTab`) и
№10 (`SubscriptionModal`). Обе относятся к админке и обе описаны задачами T-F7/T-F8.
Ни один маппер ошибок (`bookingError.ts`, `companyError.ts`, `memberError.ts`) в цикле не правится.

---

## 8. Что осталось неизменным намеренно

### 8.1 Формат тел ошибок для 400/402/403/404/409 (риск R3)

Это самое важное ограничение цикла и прямой ответ на R3 (`SPEC.md` §9).

- `BadRequest("текст")`, `StatusCode(402, "текст")`, `NotFound("текст")`, `Conflict("текст")` продолжают
  отдавать **голую строку** `text/plain`. `Forbid()` продолжает отдавать **пустое** тело.
- `BadRequest(createResult.Errors.Select(e => e.Description))` (`CompaniesController.cs:341`) продолжает
  отдавать **JSON-массив строк** — его разбирает `frontend/src/utils/memberError.ts:23-27`.
- Ни одно существующее сообщение об ошибке не переформулировано. Тексты `Company not found`,
  `Service not found`, `Time slot is no longer available`, `Online booking requires a paid subscription.`,
  `Captcha required for guest booking`, `Invalid captcha`,
  `Name and phone are required for guest booking` остаются посимвольно прежними — на них завязаны
  проверки `lower.includes(...)` в `bookingError.ts:18,28,30`.
- **`ProblemDetails` вводится исключительно для необработанных исключений (500).** Реализация — §0.3
  и `ARCHITECTURE.md` §3.2.
- Прямые запреты реализации: **не добавлять** `builder.Services.AddProblemDetails()` и **не добавлять**
  `UseStatusCodePages*` — второе является единственной вещью, которая реально переписала бы тела
  существующих 4xx.
- Следствие: мапперы `frontend/src/utils/bookingError.ts`, `companyError.ts`, `memberError.ts` в этом
  цикле **не переписываются**; их юнит-тесты (T-F3) пишутся под **текущий** формат.

### 8.2 Контракты DTO

| DTO | Файл | Статус |
|---|---|---|
| `BookingDto` | `ServiceBooking.API/DTOs/Bookings/BookingDto.cs:5-24` | **не меняется** (Q3). `Price`/`CompanySlug` не добавляются; `MapToDto` (`BookingsController.cs:364-368`) не трогается |
| `CreateBookingDto` | `BookingDto.cs:30-42` | не меняется |
| `ServiceDto` / `CreateServiceDto` | `DTOs/Services/ServiceDto.cs` | не меняются |
| `WorkingHoursDto` / `BreakDto` | `DTOs/WorkingHours/` | не меняются |
| `MasterPublicDto` | `DTOs/Companies/CompanyDto.cs:56-62` | не меняется |

Правило `SPEC.md` §6 п. 5: поля из DTO не удаляются и не добавляются; коды ответов меняются только там,
где это явный предмет истории (403 в услугах, 404 в бронировании, 409/400 в планах, 403 в расписании,
400 в фильтре статусов).

Единственное изменение TS-типов — **сужение** `Booking` под фактический DTO: из
`frontend/src/types/index.ts:64,78` удаляются `companySlug` и `price` (T-F1, US-06). Бэкенд при этом не
затрагивается вовсе.

### 8.3 Поведение, которое цикл сознательно не чинит

| Место | Поведение | Почему остаётся |
|---|---|---|
| `PUT`/`DELETE /api/services/{id}` | `404` проверяется **до** прав (`ServicesController.cs:55,74`) — посторонний узнаёт о существовании услуги | Существующее поведение, покрыто SVC-008; менять — расширять скоуп US-09 |
| `POST /api/services` | несуществующая компания даёт `403` обычному пользователю и `500` (FK) суперадмину | Вне скоупа US-09; после T-B7 500 хотя бы становится `problem+json` |
| `GET /api/workinghours` | мастер, спрашивающий **свой** `masterId`, получает `200` при любом `companyId` | Точная копия поведения `PUT`/`DELETE`; унификация предикатов отложена (`ARCHITECTURE.md` §2.4) |
| `GET /api/companies/{id}/masters` | несуществующая/неактивная компания → `200 []`, не `404` | Публичный эндпоинт, не раскрывает существование компаний; менять контракт незачем |
| `PUT /api/admin/owners/{ownerUserId}/subscription` | несуществующий `ownerUserId` → `500` (FK `AccountSubscription.OwnerUserId`, `ServiceBooking.Infrastructure/Data/AppDbContext.cs:61`) | **Не чиним осознанно**: это стабильная точка проверки глобального обработчика (ADM-035). Фиксируется в `API_DOCUMENTATION.md` §7 как известное ограничение |
| `GET /api/admin/plans` | продолжает отдавать и неактивные планы | Админке они нужны для истории (US-08 п. 4); фильтрует фронт |
| Все прочие эндпоинты | контракт не меняется | Вне скоупа цикла |

### 8.4 Что не меняется в клиентском слое

- Базовый URL, интерцепторы токена и глобальная обработка 401 — `frontend/src/api/client.ts` не трогается.
- Сигнатуры функций в `frontend/src/api/*.ts` не меняются ни в одном разделе. Единственная правка —
  внутренняя: инлайновая нормализация времени `data.startTime.length === 5 ? ... : ...`
  (`frontend/src/api/bookings.ts:26,40`) заменяется вызовом `toApiTime` из нового
  `frontend/src/utils/time.ts` (T-F6). **Тело запроса при этом побайтово идентично** — это критерий
  готовности задачи, а не изменение контракта.

---

## 9. Чек-лист согласования BE↔FE перед мёржем цикла

1. Тела 400/402/403/404/409 не изменились ни в одном эндпоинте — проверяется диффом контроллеров (R3).
2. `frontend/src/utils/bookingError.ts`, `companyError.ts`, `memberError.ts` не изменены — проверяется
   пустым диффом этих трёх файлов.
3. `frontend/src/utils/planError.ts` разбирает **строку** из `response.data`, а не объект.
4. Текст 409 в `AdminController.DeletePlan` содержит количество подписчиков и совпадает с шаблоном §6.2.
5. Текст 400 в `GetClientBookings` совпадает с §3.2.
6. `API_DOCUMENTATION.md` обновлён по всем шести эндпоинтам в тех же PR, что и код
   (§6 п. 9 `SPEC.md`): §4.5 (`GET /api/workinghours` — 403), раздел бронирований
   (`POST /api/bookings` — порядок 404/402; `GET /api/bookings/client` — `upcoming` и 400 вместо
   «фильтр молча игнорируется», строка 435), раздел услуг и §7 «Известные ограничения» (мастер больше
   не редактор услуг), `GET /api/companies/{id}/masters` (ролевой фильтр),
   `DELETE /api/admin/plans/{id}` (409, строка 1606).
7. `TEST_CATALOG.md` содержит WH-011..WH-014, BK-027..BK-031, SVC-003/007 (переписанные), SVC-013/014,
   CO-066, CO-067, ADM-032..ADM-035.
