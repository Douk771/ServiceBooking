# ServiceBooking API — документация

Эта документация описывает REST API платформы ServiceBooking — SaaS-сервиса онлайн-записи на услуги (салоны красоты, барбершопы, мастера маникюра и т.п.). Документ основан на фактическом исходном коде контроллеров, DTO, сущностей и перечислений проекта по состоянию на момент написания.

## Содержание

1. [Обзор](#1-обзор)
2. [Аутентификация](#2-аутентификация)
3. [Ключевые бизнес-концепции](#3-ключевые-бизнес-концепции)
4. [Справочник эндпоинтов](#4-справочник-эндпоинтов)
5. [Сквозные сценарии использования](#5-сквозные-сценарии-использования)
6. [Справочник кодов ответа](#6-справочник-кодов-ответа)
7. [Известные ограничения и незавершённая функциональность](#7-известные-ограничения-и-незавершённая-функциональность)

---

## 1. Обзор

ServiceBooking — это платформа для онлайн-записи клиентов на услуги: стрижки, маникюр, массаж и другие услуги, оказываемые "мастерами" (специалистами) в рамках "компаний" (салонов/барбершопов). Компания создаёт каталог услуг, назначает мастеров, настраивает их рабочее время, а клиенты (зарегистрированные или гости) бронируют свободные слоты.

### Роли пользователей

В системе четыре роли (`ServiceBooking.Core.Enums.UserRole` и одноимённые роли ASP.NET Identity, создаваемые при старте приложения):

| Роль | Как получают | Что могут делать (кратко) |
|---|---|---|
| **Client** | Назначается автоматически при регистрации (`POST /api/auth/register`) | Бронировать услуги (от своего имени или как гость), просматривать свои записи, оставлять отзывы после завершённых визитов, редактировать свой профиль |
| **Master** | Добавляется владельцем компании (или SuperAdmin) через `POST /api/companies/{id}/members` с `Role="Master"` | Управляет своим рабочим временем, видит и обрабатывает свои записи (завершить/неявка/перенос/отметить оплаченным), ведёт список своих клиентов и заметки о них (общие для всех мастеров компании) |
| **CompanyOwner** | Присваивается автоматически при создании компании (`POST /api/companies`) | Управляет профилем компании, участниками (мастерами), услугами, расписанием, шаблонами расписания, рассылками, отчётами по мастерам; может назначать роли Master/CompanyOwner другим пользователям |
| **SuperAdmin** | Создаётся при старте приложения из конфигурации (`SuperAdmin:Email` / `SuperAdmin:Password`) или назначается вручную другим SuperAdmin | Полный доступ ко всей платформе: статистика, список компаний и пользователей, изменение подписок компаний, изменение ролей любого пользователя, тарифные планы, просмотр всех записей |

Один и тот же пользователь может одновременно иметь несколько ролей (например, быть Client и, после создания компании, также CompanyOwner).

### Базовый URL и формат

- Базовый путь всех эндпоинтов: **`/api`** (например, `http://localhost:5000/api/auth/login`).
- Формат тела запроса/ответа — JSON. ASP.NET Core по умолчанию сериализует C#-свойства в **camelCase** (например, C#-свойство `FirstName` в JSON становится `"firstName"`), даже там, где в этой документации для наглядности C#-типы называются в PascalCase.
- Перечисления (`BookingStatus`, `PaymentStatus`, `SubscriptionPlan` и т.д.) сериализуются как **строки** (в `Program.cs` подключён `JsonStringEnumConverter`), а не как числа — например, `"status": "Confirmed"`, а не `"status": 1`.
- Действует политика CORS, разрешающая фронтенду (по умолчанию `http://localhost:5173`) обращаться к API с передачей credentials.

### Аутентификация

Используется **JWT Bearer**-аутентификация. Токен выдаётся при регистрации (`POST /api/auth/register`) или входе (`POST /api/auth/login`) и живёт **7 дней** (`expires: DateTime.UtcNow.AddDays(7)` в `TokenService`). Для защищённых эндпоинтов токен передаётся в заголовке:

```
Authorization: Bearer <token>
```

### Интерактивная документация

В любой момент, когда приложение запущено, доступна **Swagger UI по адресу `/swagger`** — там можно интерактивно исследовать все эндпоинты, посмотреть точные схемы запросов/ответов и выполнить запросы прямо из браузера (в схему авторизации нужно вставить только сам токен, без слова `Bearer`).

---

## 2. Аутентификация

### 2.1. Регистрация — `POST /api/auth/register`

**Доступ:** анонимный.

Создаёт нового пользователя Identity, автоматически присваивает ему роль `Client` и сразу возвращает JWT. **Идентификатор аккаунта — номер телефона** (`phone` кладётся в `UserName`, у которого в Identity есть уникальный индекс, — так обеспечивается уникальность телефона). Email **необязателен**; отдельного шага подтверждения телефона/email пока нет — пользователь может пользоваться API сразу после регистрации.

**Тело запроса** (`RegisterDto`):

| Поле | Тип | Обязательное | Примечание |
|---|---|---|---|
| `firstName` | string | да | |
| `lastName` | string | да | |
| `phone` | string | да | идентификатор аккаунта, должен быть уникален |
| `password` | string | да | минимум 8 символов (см. правила пароля ниже) |
| `email` | string? | нет | если передан — должен быть валидным email (`[EmailAddress]`) |

**Пример запроса:**

```bash
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
        "firstName": "Иван",
        "lastName": "Петров",
        "phone": "+79991234567",
        "password": "Passw0rd1",
        "email": "ivan.petrov@example.com"
      }'
```

**Успешный ответ `200 OK`** (`AuthResponseDto`) — `email` может быть `null`, если не передавался:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...signature",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "phone": "+79991234567",
  "email": "ivan.petrov@example.com",
  "firstName": "Иван",
  "lastName": "Петров",
  "roles": ["Client"]
}
```

**Ошибки:**

| Код | Причина |
|---|---|
| `400 Bad Request` | Не пройдена модельная валидация (пустой `firstName`/`lastName`/`phone`, `password` короче 8 символов, либо передан некорректный по формату `email`) — стандартный ASP.NET Core `ValidationProblemDetails` |
| `400 Bad Request` | Телефон уже занят другим пользователем, либо пароль не проходит встроенные правила ASP.NET Identity — тело ответа: массив объектов Identity-ошибок вида `{"code": "DuplicateUserName", "description": "..."}` (дубликат телефона приходит именно как `DuplicateUserName`, т.к. `UserName == phone`) |

### 2.2. Вход — `POST /api/auth/login`

**Доступ:** анонимный.

**Тело запроса** (`LoginDto`):

| Поле | Тип | Обязательное |
|---|---|---|
| `phone` | string | да |
| `password` | string | да |

**Пример запроса:**

```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{ "phone": "+79991234567", "password": "Passw0rd1" }'
```

**Успешный ответ `200 OK`** — тот же формат `AuthResponseDto`, что и при регистрации, но `roles` отражает **текущий** набор ролей пользователя на момент входа.

**Ошибки:**

| Код | Причина |
|---|---|
| `400 Bad Request` | Не пройдена модельная валидация (пустые поля) |
| `401 Unauthorized` | Пользователь с таким телефоном не найден — тело: строка `"Invalid credentials"` |
| `401 Unauthorized` | Неверный пароль — то же самое сообщение `"Invalid credentials"` |
| `401 Unauthorized` | Аккаунт временно заблокирован после серии неудачных попыток (см. ниже) — API **не** сообщает клиенту отдельно о блокировке, возвращается всё то же generic-сообщение `"Invalid credentials"` |

### 2.3. Правила пароля

В `Program.cs` явно переопределены два параметра ASP.NET Identity:

- `RequiredLength = 8` — минимум 8 символов;
- `RequireNonAlphanumeric = false` — специальный символ **не обязателен**.

Остальные параметры Identity оставлены **по умолчанию** (в коде не переопределены), а значит продолжают действовать:

- `RequireDigit = true` — минимум одна цифра;
- `RequireLowercase = true` — минимум одна строчная буква;
- `RequireUppercase = true` — минимум одна заглавная буква.

Итог: пароль должен быть не короче 8 символов и содержать хотя бы одну цифру, одну строчную и одну заглавную букву латиницы (спецсимвол не требуется).

### 2.4. Блокировка после неудачных попыток входа

- `MaxFailedAccessAttempts = 5` — после 5 неудачных попыток входа подряд аккаунт блокируется.
- `DefaultLockoutTimeSpan = 15 минут` — блокировка длится 15 минут, по истечении которых счётчик неудачных попыток сбрасывается автоматически при следующей успешной проверке.
- Блокировка включена явно per-call: `signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true)`.
- Пока аккаунт заблокирован, **любые** попытки входа (даже с правильным паролем) возвращают `401 Unauthorized` с тем же сообщением `"Invalid credentials"` — отдельного кода/сообщения о блокировке нет.

### 2.5. Использование токена

Полученный `token` нужно передавать в заголовке `Authorization` для всех защищённых эндпоинтов:

```bash
curl http://localhost:5000/api/bookings/my \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..."
```

Если заголовок отсутствует или токен некорректен/просрочен — `401 Unauthorized`. Если пользователь авторизован, но не обладает нужной ролью для конкретного эндпоинта (`[Authorize(Roles = "...")]`) — `403 Forbidden`.

---

## 3. Ключевые бизнес-концепции

Этот раздел объясняет неочевидные бизнес-правила, зашитые в код — их невозможно понять просто по именам полей DTO.

### 3.1. Тарифные планы подписки

**Подписка — свойство аккаунта владельца, а не отдельной компании.** Таблица `AccountSubscription` хранит одну запись на `OwnerUserId` (создателя компаний), со ссылкой `PlanConfigId` на `SubscriptionPlanConfig` (гибкая конфигурация тарифа с полями `AllowOnlineBooking`, `AllowMailing`, `AllowAnalytics`, `AllowPublicListing`, `AllowOnlinePayment`, `MaxEmployees`, `MaxCompanies`, `PricePerMonth` и т.д. — управляется через `GET/POST/PUT/DELETE /api/admin/plans`). Все компании одного владельца используют один и тот же тариф. `SubscriptionResolver.GetEffectivePlanAsync(companyId)` разрешает эффективный план компании через её владельца: **если у владельца нет записи подписки вообще, либо подписка неактивна (`IsActive=false`), либо истёк срок оплаты (`PaidUntil` в прошлом) — используется ограничительный baseline `EffectivePlan.Free`** (`AllowOnlineBooking=false`, `MaxEmployees=1`, `MaxCompanies=1`, остальные платные флаги выключены; единственное исключение — `AllowPublicListing=true`, чтобы компании без подписки не пропадали из общего каталога).

Две опции устроены как «двойной выключатель» — работают только когда включены и владельцем компании (флаг компании), и тарифом (флаг плана):

- **Отображение в общем списке** — `Company.ShowInPublicListing` (владелец) × `AllowPublicListing` (тариф). `GET /api/companies` (публичный каталог) показывает компанию только при обоих `true`; прямая ссылка `GET /api/companies/{slug}` и списки владельца (`/my`, `/member`) работают всегда. Вычисляемое поле `CompanyDto.PublicListingEnabled` отражает итог.
- **Онлайн-оплата (предоплата)** — `Company.RequirePrepayment` (владелец) × `AllowOnlinePayment` (тариф). `POST /api/bookings` создаёт запись со статусом `PaymentStatus.Pending` только при обоих `true`; итог — в `CompanyDto.PrepaymentEnabled`.

Ключевое правило гейтинга функциональности видно в `POST /api/bookings`:

- **Любая онлайн-самозапись — как гостевая (анонимная), так и авторизованным клиентом от своего имени — блокируется, если эффективный план компании не даёт `AllowOnlineBooking`** (это и есть Free по умолчанию): сервер вернёт `402 Payment Required` с сообщением `"Online booking requires a paid subscription."`. Проверка **не** ограничена только гостевым сценарием — единственное исключение из неё это ручная запись сотрудника (см. §3.4): `if (!effectivePlan.AllowOnlineBooking && !isStaffManualBooking) return 402`.
- Если у компании есть активная подписка с `AllowOnlineBooking=true`, и гостевая, и авторизованная онлайн-запись проходят (при прочих условиях — см. ниже про `AllowSelfBooking`).
- Если у владельца есть подписка с истёкшим сроком (`PaidUntil` в прошлом), это тоже резолвится в Free целиком, то есть **любая** новая онлайн-запись (не только гостевая) блокируется той же проверкой и тем же сообщением.

Чтобы клиентское приложение не вело гостя в тупик (кнопка «Записаться» → форма → `402` на последнем шаге), публичный `CompanyDto` содержит вычисляемое поле **`onlineBookingEnabled`** — оно `true`, только когда `AllowSelfBooking = true` **и** эффективный план владельца даёт `AllowOnlineBooking = true`, то есть в точности когда онлайн-запись реально пройдёт (и для гостя, и для авторизованного клиента). Интерфейс должен ориентироваться на этот флаг, а не показывать нерабочую кнопку записи, которая обречена получить `402`.

### 3.2. `Company.AllowSelfBooking`

Независимо от тарифного плана, у каждой компании есть флаг `AllowSelfBooking` (по умолчанию `true`). Если он выставлен в `false`, **любая гостевая (неаутентифицированная) запись блокируется** ещё до проверки плана подписки — сервер вернёт `403 Forbidden`. Это отдельная, независимая проверка: даже компания на плане Pro не примет анонимную запись, если `AllowSelfBooking=false`. На аутентифицированные записи (в том числе ручные записи мастера) этот флаг не влияет.

### 3.3. `Company.RequirePrepayment` и предоплата

Если у компании включён флаг `RequirePrepayment`, то **неручные** (то есть созданные клиентом самостоятельно — от своего имени или как гость) записи создаются со статусом оплаты `PaymentStatus.Pending` вместо `PaymentStatus.NotRequired`. Реального платёжного шлюза в системе нет — это чисто ручной процесс подтверждения: мастер, владелец компании или SuperAdmin позже вызывает `PATCH /api/bookings/{id}/mark-paid`, чтобы вручную проставить `PaymentStatus.Paid`.

Важный нюанс: **ручные записи** (см. ниже) всегда создаются с `PaymentStatus.NotRequired`, независимо от `RequirePrepayment` — предоплата актуальна только для самостоятельных записей клиентов.

### 3.4. Ручные записи (Manual bookings) vs самостоятельные записи клиента

Если при создании записи (`POST /api/bookings`) передано непустое поле `GuestName`, запись считается **ручной** (`isManualBooking = true`) — это сценарий "мастер записывает пришедшего клиента". В этом случае `ClientId` **никогда** не привязывается к записи, даже если запрос выполнен от имени аутентифицированного пользователя (например, мастер, который сам является Client). Это осознанно различает два сценария:

- мастер записывает стороннего (возможно, незарегистрированного) клиента по имени/телефону — `ClientId = null`, данные в `GuestName`/`GuestPhone`/`GuestEmail`;
- клиент бронирует сам себя, будучи аутентифицированным, **не** указывая `GuestName` — тогда `ClientId` заполняется его собственным `userId`.

### 3.5. Окно видимости контактов клиента (24 часа)

В `GET /api/masters/clients` телефон и email клиента показываются мастеру только в течение **24 часов после окончания последнего визита**. Как только с момента окончания последней записи (`Date` + `EndTime`) проходит более 24 часов, поля `phone`/`email` в ответе становятся `null` — при этом сама карточка клиента (имя, дата последнего визита, число визитов, заметки) остаётся видимой. Это относится как к зарегистрированным клиентам, так и к гостям (группировка по номеру телефона).

### 3.6. Модель комиссии мастера

У каждого мастера (`AppUser.CommissionPercent`, по умолчанию `0`) задан процент, который он зарабатывает с выручки по **завершённым** записям. Отчёт `GET /api/reports/masters` разбивает суммарную выручку (`TotalAmount`, сумма цен услуг завершённых записей) на:

- `MasterEarnings = Round(TotalAmount * CommissionPercent / 100, 2)` — доля мастера;
- `CompanyEarnings = TotalAmount - MasterEarnings` — доля компании (салона).

`CommissionPercent` устанавливается **только владельцем компании** (или SuperAdmin) через `PUT /api/companies/{id}/members/{memberId}/commission` — сам мастер задать себе процент не может. `GET /api/profile` по-прежнему показывает текущее значение (только для чтения); эндпоинт `PUT /api/profile` его больше не принимает. Значение обрезается в диапазон `[0, 100]` через `Math.Clamp`.

### 3.7. Назначение ролей

- При регистрации (`POST /api/auth/register`) всегда присваивается только роль **Client**.
- Роль **CompanyOwner** присваивается автоматически при создании компании (`POST /api/companies`) — если у пользователя ещё не было этой роли, она добавляется.
- Роль **Master** (или повторно CompanyOwner) назначается владельцем компании (или SuperAdmin) через `POST /api/companies/{id}/members`.
  - Участник добавляется **по номеру телефона** (`phone`); email необязателен. Если пользователь с указанным телефоном **ещё не существует**, для него автоматически создаётся аккаунт (телефон считается подтверждённым — `PhoneNumberConfirmed = true`) со сгенерированным паролем: `"Sb" + последние 6 цифр телефона`, дополненным справа символами `'0'` до длины не менее 8 символов. Например, для `+7 999 123‑45‑67` → пароль `Sb234567`. Владелец должен сам передать новому мастеру его телефон и этот временный пароль (API его не рассылает).
  - **CompanyOwner может назначать только роли `Master` или `CompanyOwner`** — попытка передать любое другое значение `role` (включая `SuperAdmin` или `Client`) заблокирована на уровне приложения (`403 Forbidden`).
  - Только **SuperAdmin** может назначить роль `SuperAdmin` (и вообще любую произвольную роль — проверка `CanAssignRole` для SuperAdmin всегда возвращает `true`).
- Общая смена ролей пользователя (`PUT /api/admin/users/{id}/roles`) доступна только SuperAdmin и **полностью заменяет** список ролей пользователя (сначала удаляются все текущие роли, затем добавляются переданные).

### 3.8. Роли в JWT и их проверка на каждом запросе

`TokenService.GenerateToken` добавляет claim'ы `ClaimTypes.Role` из списка ролей пользователя **на момент генерации токена** (при регистрации или логине) — сам токен остаётся снимком ролей на момент выпуска. Однако при проверке токена на каждом защищённом запросе обработчик `OnTokenValidated` (см. `Program.cs`) перечитывает **текущий** список ролей пользователя из базы через `UserManager.GetRolesAsync` и заменяет им claim'ы ролей в `ClaimsPrincipal` запроса. Поэтому если роль пользователя меняется после выпуска токена (владелец назначил его мастером, SuperAdmin изменил список ролей или отозвал роль), это отражается **немедленно** на следующем же запросе с уже выданным токеном — повторный вход не требуется. Это касается и отзыва прав: если у пользователя забрали роль (например, разжаловали SuperAdmin), доступ, завязанный на неё, пропадает сразу же, а не только через 7 дней при истечении токена. Единственное, что при этом не проверяется живьём — это `Email`/`FirstName`/`LastName`/`Sub` в самом токене: они остаются такими, какими были на момент выпуска, до следующего входа.

---

## 4. Справочник эндпоинтов

Далее эндпоинты сгруппированы по предметной области (контроллеру). Для каждого указаны: метод и путь, требования к доступу, параметры, пример запроса, пример успешного ответа и исчерпывающий список возможных ошибок.

Замечание о неаутентифицированном доступе: если ниже написано «анонимный», это означает, что метод **не** помечен `[Authorize]` — но если заголовок `Authorization` всё же передан с валидным токеном, контроллер может (в отдельных случаях) вести себя иначе (например, `POST /api/bookings` различает гостевые и аутентифицированные запросы).

### 4.1. Auth (`/api/auth`)

См. раздел [2. Аутентификация](#2-аутентификация) — `POST /api/auth/register`, `POST /api/auth/login`.

---

### 4.2. Bookings (`/api/bookings`)

#### `GET /api/bookings/occupied`

**Доступ:** анонимный.

**Query-параметры:** `masterId` (string, обязателен), `date` (`DateOnly`, формат `yyyy-MM-dd`, обязателен).

Возвращает занятые интервалы времени мастера на указанную дату (все записи кроме отменённых).

```bash
curl "http://localhost:5000/api/bookings/occupied?masterId=6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4&date=2026-07-20"
```

**Ответ `200 OK`** (`OccupiedRangeDto[]`):

```json
[
  { "start": "10:00:00", "end": "10:30:00" },
  { "start": "13:00:00", "end": "14:00:00" }
]
```

Ошибок, специфичных для этого эндпоинта, нет — при некорректном `masterId`/`date` (не парсится) сработает автоматическая `400`-валидация связывания модели ASP.NET Core; при отсутствии записей возвращается пустой массив.

#### `GET /api/bookings/slots`

**Доступ:** анонимный.

**Query-параметры:** `masterId` (string), `serviceId` (Guid), `date` (`DateOnly`).

Возвращает список свободных слотов длительностью, равной `DurationMinutes` услуги, с шагом перебора 30 минут, с учётом рабочего времени мастера (`WorkingHours`), перерывов (`ScheduleBreak`) и уже существующих записей.

```bash
curl "http://localhost:5000/api/bookings/slots?masterId=6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4&serviceId=b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e&date=2026-07-20"
```

**Ответ `200 OK`** (`TimeSlotResult[]`):

```json
[
  { "start": "09:00:00", "end": "09:40:00" },
  { "start": "09:30:00", "end": "10:10:00" }
]
```

Если услуга не найдена, либо на указанную дату у мастера нет записи `WorkingHours` с `IsWorking=true`, эндпоинт возвращает **пустой массив** `[]` (не ошибку).

#### `POST /api/bookings`

**Доступ:** анонимный **или** аутентифицированный (поведение различается).

**Тело запроса** (`CreateBookingDto`):

| Поле | Тип | Обязательное | Примечание |
|---|---|---|---|
| `companyId` | Guid | да | |
| `serviceId` | Guid | да | |
| `masterId` | string | да | |
| `date` | DateOnly | да | |
| `startTime` | TimeOnly | да | |
| `notes` | string? | нет | |
| `guestName` | string? | условно | если указано — запись считается **ручной** (см. §3.4); обязательно для гостевого сценария |
| `guestPhone` | string? | условно | обязательно для гостевого (неаутентифицированного) сценария |
| `guestEmail` | string? | нет | |
| `captchaToken` | string? | условно | токен Yandex SmartCaptcha; требуется для гостевого бронирования, когда капча активна (задан `SmartCaptcha:SecretKey` **или** окружение — Production) |

**Пример запроса (аутентифицированный клиент бронирует сам себя):**

```bash
curl -X POST http://localhost:5000/api/bookings \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "serviceId": "b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e",
        "masterId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
        "date": "2026-07-20",
        "startTime": "09:00:00",
        "notes": "Первый визит"
      }'
```

**Пример запроса (гостевая запись):**

```bash
curl -X POST http://localhost:5000/api/bookings \
  -H "Content-Type: application/json" \
  -d '{
        "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "serviceId": "b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e",
        "masterId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
        "date": "2026-07-20",
        "startTime": "09:00:00",
        "guestName": "Мария Сидорова",
        "guestPhone": "+380671234567",
        "guestEmail": "maria@example.com"
      }'
```

**Успешный ответ `201 Created`** (заголовок `Location` указывает на `GET /api/bookings/{id}`, тело — `BookingDto`):

```json
{
  "id": "c47ac10b-58cc-4372-a567-0e02b2c3d479",
  "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "companyName": "Салон \"Мираж\"",
  "serviceId": "b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e",
  "serviceName": "Стрижка мужская",
  "masterId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
  "masterName": "Алина Ковальчук",
  "clientId": null,
  "clientName": "Мария Сидорова",
  "clientPhone": "+380671234567",
  "clientEmail": "maria@example.com",
  "date": "2026-07-20",
  "startTime": "09:00:00",
  "endTime": "09:40:00",
  "status": "Confirmed",
  "paymentStatus": "NotRequired",
  "notes": null,
  "createdAt": "2026-07-18T10:15:00Z"
}
```

**Ошибки** (проверяются строго в этом порядке для гостевого сценария):

| Код | Причина |
|---|---|
| `404 Not Found` | (только для гостевого запроса) компания с `companyId` не найдена — `"Company not found"` |
| `403 Forbidden` | (только для гостевого запроса) `Company.AllowSelfBooking = false` — см. §3.2 |
| `400 Bad Request` | (только для гостевого запроса) капча активна (см. `captchaToken` выше), но `captchaToken` не передан — `"Captcha required for guest booking"` |
| `400 Bad Request` | `captchaToken` передан, но не прошёл серверную валидацию Yandex SmartCaptcha (`status != "ok"`, недоступность сервиса валидации или сетевая ошибка — во всех случаях fail-closed) — `"Invalid captcha"` |
| `400 Bad Request` | (только для гостевого запроса) не заполнены `guestName` и/или `guestPhone` — `"Name and phone are required for guest booking"` |
| `402 Payment Required` | у компании есть подписка с истёкшим `PaidUntil` — `"Subscription expired. New bookings are not allowed."` |
| `402 Payment Required` | запрос неаутентифицирован и план компании — Free — `"Online booking requires a paid subscription."` (см. §3.1) |
| `404 Not Found` | услуга (`serviceId`) не найдена — `"Service not found"` |
| `409 Conflict` | выбранный слот уже занят другой (неотменённой) записью этого мастера — `"Time slot is no longer available"` |
| `400 Bad Request` | не пройдена автоматическая валидация модели (например, отсутствуют обязательные поля `companyId`/`serviceId`/`masterId`/`date`/`startTime`) |

Примечание: если запрос аутентифицирован, существование `companyId` явно не проверяется (проверка `Company not found` — только в гостевой ветке); при несуществующем `companyId` в аутентифицированном сценарии ошибка целостности данных проявится на уровне БД.

#### `GET /api/bookings/{id}`

**Доступ:** любой аутентифицированный пользователь, но с проверкой прав на **конкретную** запись: разрешено, если вызывающий — клиент этой записи (`ClientId == userId`), назначенный мастер (`MasterId == userId`), SuperAdmin, либо CompanyOwner компании, к которой относится запись.

```bash
curl http://localhost:5000/api/bookings/c47ac10b-58cc-4372-a567-0e02b2c3d479 \
  -H "Authorization: Bearer $TOKEN"
```

**Ответ `200 OK`** — `BookingDto` (см. пример выше).

**Ошибки:**

| Код | Причина |
|---|---|
| `401 Unauthorized` | отсутствует/невалиден токен |
| `404 Not Found` | запись с таким `id` не найдена |
| `403 Forbidden` | вызывающий не является клиентом/мастером/владельцем компании/SuperAdmin для этой записи |

#### `GET /api/bookings/my`

**Доступ:** любой аутентифицированный пользователь.

Возвращает все записи, где `ClientId == userId` (то есть только записи, сделанные **самостоятельно**, не ручные записи мастера от имени этого пользователя как гостя), отсортированные по дате/времени по убыванию.

```bash
curl http://localhost:5000/api/bookings/my -H "Authorization: Bearer $TOKEN"
```

**Ответ `200 OK`** — массив `BookingDto`. Ошибка: `401 Unauthorized`, если токен не передан/невалиден.

#### `GET /api/bookings/client`

**Доступ:** любой аутентифицированный пользователь.

**Query-параметры:** `status` (string?, опционально) — по сути имя значения `BookingStatus` (`Pending`/`Confirmed`/`Cancelled`/`Completed`/`NoShow`).

```bash
curl "http://localhost:5000/api/bookings/client?status=Completed" -H "Authorization: Bearer $TOKEN"
```

**Ответ `200 OK`** — массив `BookingDto`, отфильтрованный по статусу. **Важный нюанс:** если `status` передан, но не соответствует ни одному значению перечисления `BookingStatus` (`Enum.TryParse` не проходит), фильтр **молча игнорируется** — возвращаются вообще все записи клиента без фильтрации, ошибка `400` не возникает.

#### `GET /api/bookings/master`

**Доступ:** роли `Master` или `CompanyOwner` (`[Authorize(Roles = "Master,CompanyOwner")]`). Обратите внимание: пользователь с **единственной** ролью `SuperAdmin` (без `Master`/`CompanyOwner`) получит `403 Forbidden` уже на уровне атрибута авторизации — исключения для SuperAdmin здесь нет.

**Query-параметры:** `date` (`DateOnly?`, нижняя граница по дате, опционально), `to` (`DateOnly?`, верхняя граница, опционально).

Возвращает записи, где `MasterId == userId` (то есть только собственные записи вызывающего как мастера; если CompanyOwner сам не назначен мастером ни у одной услуги, этот запрос вернёт пустой список).

```bash
curl "http://localhost:5000/api/bookings/master?date=2026-07-18&to=2026-07-25" \
  -H "Authorization: Bearer $MASTER_TOKEN"
```

**Ответ `200 OK`** — массив `BookingDto`, отсортированный по дате/времени по возрастанию.

**Ошибки:** `401 Unauthorized` (нет токена), `403 Forbidden` (роль не входит в `Master,CompanyOwner`).

#### `PATCH /api/bookings/{id}/complete`

**Доступ:** роли `Master`, `CompanyOwner` или `SuperAdmin`, и дополнительно — вызывающий должен быть назначенным мастером записи (`MasterId == userId`), SuperAdmin, либо CompanyOwner компании этой записи.

```bash
curl -X PATCH http://localhost:5000/api/bookings/c47ac10b-58cc-4372-a567-0e02b2c3d479/complete \
  -H "Authorization: Bearer $MASTER_TOKEN"
```

**Успешный ответ:** `204 No Content`.

**Ошибки:**

| Код | Причина |
|---|---|
| `401 Unauthorized` | нет токена |
| `403 Forbidden` | роль не входит в `Master,CompanyOwner,SuperAdmin`, либо роль подходящая, но вызывающий не мастер/владелец записи |
| `404 Not Found` | запись не найдена |
| `400 Bad Request` | запись уже отменена (`Status == Cancelled`) — `"Booking is cancelled"` |

#### `PATCH /api/bookings/{id}/mark-paid`

**Доступ:** аналогично `.../complete` (роли `Master,CompanyOwner,SuperAdmin` + владение записью). Устанавливает `PaymentStatus = Paid` — не проверяет предыдущий статус (идемпотентно, можно вызвать повторно).

```bash
curl -X PATCH http://localhost:5000/api/bookings/c47ac10b-58cc-4372-a567-0e02b2c3d479/mark-paid \
  -H "Authorization: Bearer $MASTER_TOKEN"
```

**Успешный ответ:** `204 No Content`.

**Ошибки:** `401 Unauthorized`, `403 Forbidden` (роль/владение), `404 Not Found` (запись не найдена).

#### `PATCH /api/bookings/{id}/noshow`

**Доступ:** аналогично `.../complete`. Устанавливает `Status = NoShow`.

```bash
curl -X PATCH http://localhost:5000/api/bookings/c47ac10b-58cc-4372-a567-0e02b2c3d479/noshow \
  -H "Authorization: Bearer $MASTER_TOKEN"
```

**Успешный ответ:** `204 No Content`.

**Ошибки:** `401`, `403` (роль/владение), `404 Not Found`, `400 Bad Request` — если запись уже отменена (`"Booking is cancelled"`).

#### `PATCH /api/bookings/{id}/reschedule`

**Доступ:** любой аутентифицированный пользователь (`[Authorize]` без ограничения по ролям), но с дополнительной проверкой владения: разрешено только назначенному мастеру записи, SuperAdmin, либо CompanyOwner компании этой записи. **Клиент, оформивший запись, не может перенести её сам** через этот эндпоинт (проверка идёт только по `MasterId`/владельцу компании, `ClientId` не учитывается).

**Тело запроса** (`RescheduleDto`): `date` (DateOnly), `startTime` (TimeOnly).

```bash
curl -X PATCH http://localhost:5000/api/bookings/c47ac10b-58cc-4372-a567-0e02b2c3d479/reschedule \
  -H "Authorization: Bearer $MASTER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "date": "2026-07-21", "startTime": "11:00:00" }'
```

**Успешный ответ:** `204 No Content`.

**Ошибки:**

| Код | Причина |
|---|---|
| `401 Unauthorized` | нет токена |
| `403 Forbidden` | вызывающий не мастер/владелец компании/SuperAdmin |
| `404 Not Found` | запись не найдена |
| `400 Bad Request` | запись уже отменена или завершена — `"Cannot reschedule a cancelled or completed booking"` |
| `409 Conflict` | новый слот пересекается с другой (неотменённой) записью того же мастера — `"Time slot is no longer available"` |

#### `PATCH /api/bookings/{id}/cancel`

**Доступ:** любой аутентифицированный пользователь (`[Authorize]`), с проверкой: разрешено клиенту записи (`ClientId == userId`), назначенному мастеру (`MasterId == userId`) или SuperAdmin. **В отличие от других операций** (complete/noshow/mark-paid/reschedule), здесь **не** предусмотрена проверка на CompanyOwner компании — если владелец компании лично не является мастером записи, он не может отменить её через этот эндпоинт.

**Тело запроса:** сырая JSON-строка (необязательная причина отмены), например `"Клиент попросил отменить"` — параметр объявлен как `[FromBody] string? reason`, поэтому тело запроса должно быть **JSON-строкой**, а не объектом.

```bash
curl -X PATCH http://localhost:5000/api/bookings/c47ac10b-58cc-4372-a567-0e02b2c3d479/cancel \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '"Клиент попросил перенести на другую дату"'
```

**Успешный ответ:** `204 No Content`.

**Ошибки:** `401 Unauthorized`, `403 Forbidden` (не клиент/не мастер/не SuperAdmin), `404 Not Found`.

---

### 4.3. Companies (`/api/companies`)

#### `GET /api/companies`

**Доступ:** анонимный. Возвращает все **активные** (`IsActive=true`) компании.

```bash
curl http://localhost:5000/api/companies
```

**Ответ `200 OK`** (`CompanyDto[]`):

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "Салон \"Мираж\"",
    "slug": "mirage-salon",
    "description": "Салон красоты в центре города",
    "logoUrl": null,
    "address": "ул. Central, 10",
    "phone": "+380441234567",
    "email": "info@mirage.example",
    "allowSelfBooking": true,
    "requirePrepayment": false,
    "onlineBookingEnabled": true
  }
]
```

`onlineBookingEnabled` — вычисляемое поле: `true`, только если онлайн-запись реально пройдёт для **любого** способа записи (гостевого или авторизованного клиента), т.е. `allowSelfBooking = true` **и** эффективный план владельца компании (см. §3.1, `SubscriptionResolver`) даёт `AllowOnlineBooking = true`. Это в точности повторяет гейт `POST /api/bookings`, так что клиент может заранее решить, показывать ли рабочую кнопку записи или предложить войти/дождаться оплаты подписки владельцем.

#### `GET /api/companies/my`

**Доступ:** аутентифицированный. Возвращает активные компании, где вызывающий — **CompanyOwner**.

```bash
curl http://localhost:5000/api/companies/my -H "Authorization: Bearer $TOKEN"
```

**Ответ:** `200 OK`, `CompanyDto[]`. Ошибка: `401 Unauthorized`.

#### `GET /api/companies/member`

**Доступ:** аутентифицированный. Возвращает все активные компании, где вызывающий состоит участником с **любой** ролью (Master или CompanyOwner).

```bash
curl http://localhost:5000/api/companies/member -H "Authorization: Bearer $TOKEN"
```

**Ответ:** `200 OK`, `CompanyDto[]`. Ошибка: `401 Unauthorized`.

#### `GET /api/companies/{slug}`

**Доступ:** анонимный.

```bash
curl http://localhost:5000/api/companies/mirage-salon
```

**Ответ `200 OK`** — `CompanyDto`. **Ошибка:** `404 Not Found`, если компания с таким `slug` не найдена либо неактивна (`IsActive=false`).

#### `GET /api/companies/{id}/masters`

**Доступ:** анонимный (публичный список мастеров для витрины бронирования).

**Query-параметры:** `serviceId` (Guid?, опционально) — фильтр по мастерам, привязанным к этой услуге через `MasterService`.

Особое поведение: если для указанной услуги **вообще нет** ни одной записи в `MasterService` (никто явно не привязан), эндпоинт **не возвращает пустой список**, а откатывается к показу **всех** мастеров компании (fallback).

```bash
curl "http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6/masters?serviceId=b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e"
```

**Ответ `200 OK`** (`MasterPublicDto[]`):

```json
[
  { "userId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4", "firstName": "Алина", "lastName": "Ковальчук", "avatarUrl": null, "bio": "Стаж 5 лет" }
]
```

#### `GET /api/companies/{id}/members`

**Доступ:** аутентифицированный + должен быть CompanyOwner этой компании или SuperAdmin (иначе `403 Forbidden`).

```bash
curl http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6/members \
  -H "Authorization: Bearer $OWNER_TOKEN"
```

**Ответ `200 OK`** (`MemberDto[]`):

```json
[
  {
    "id": "d2a1b3c4-1111-2222-3333-444455556666",
    "userId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
    "firstName": "Алина",
    "lastName": "Ковальчук",
    "email": "alina@example.com",
    "avatarUrl": null,
    "role": "Master",
    "bio": "Стаж 5 лет",
    "serviceIds": ["b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e"],
    "commissionPercent": 35.0
  }
]
```

**Ошибки:** `401 Unauthorized`, `403 Forbidden`.

#### `PUT /api/companies/{id}/members/{memberId}/services`

**Доступ:** CompanyOwner компании или SuperAdmin.

**Тело запроса:** `List<Guid>` — полный новый список `serviceId`, которые может выполнять данный мастер. Полностью заменяет предыдущие привязки (`MasterService`) в рамках этой компании.

```bash
curl -X PUT http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6/members/d2a1b3c4-1111-2222-3333-444455556666/services \
  -H "Authorization: Bearer $OWNER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '["b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e"]'
```

**Успешный ответ:** `204 No Content`.

**Ошибки:** `401 Unauthorized`, `403 Forbidden`, `404 Not Found` (участник с `memberId` не найден в данной компании).

#### `PUT /api/companies/{id}/members/{memberId}/commission`

**Доступ:** CompanyOwner компании или SuperAdmin — **сам мастер задать себе процент не может** (см. §3.6). Проверка та же `CanManageCompany`, что и у `/services` выше — в отличие, например, от `WorkingHoursController`, здесь **нет** исключения "сам мастер может управлять собой": `requesterId == member.UserId` не даёт доступа.

**Тело запроса** (`UpdateMemberCommissionDto`): `commissionPercent` (decimal) — обрезается в диапазон `[0, 100]` через `Math.Clamp`.

```bash
curl -X PUT http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6/members/d2a1b3c4-1111-2222-3333-444455556666/commission \
  -H "Authorization: Bearer $OWNER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "commissionPercent": 35.0 }'
```

**Успешный ответ `200 OK`**: `{ "userId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4", "commissionPercent": 35.0 }`

**Ошибки:** `401 Unauthorized`, `403 Forbidden` (не владелец/не SuperAdmin, включая самого мастера), `404 Not Found` (участник с `memberId` не найден в данной компании).

#### `POST /api/companies`

**Доступ:** любой аутентифицированный пользователь (создание компании открыто для всех — так пользователь становится CompanyOwner).

**Тело запроса** (`CreateCompanyDto`): `name` (string), `slug` (string, уникальный), `description`/`address`/`phone`/`email` (string?), `allowSelfBooking` (bool, по умолчанию `true`).

```bash
curl -X POST http://localhost:5000/api/companies \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "name": "Салон \"Мираж\"",
        "slug": "mirage-salon",
        "description": "Салон красоты в центре города",
        "address": "ул. Central, 10",
        "phone": "+380441234567",
        "email": "info@mirage.example",
        "allowSelfBooking": true
      }'
```

**Успешный ответ `201 Created`** (`Location` → `GET /api/companies/{slug}`), тело — `CompanyDto` (см. пример в §4.3 `GET /api/companies`). После создания вызывающему автоматически добавляется роль `CompanyOwner` (если её ещё не было), а сам он становится участником новой компании с ролью `CompanyOwner`.

**Ошибки:** `401 Unauthorized`, `409 Conflict` — если `slug` уже занят другой компанией (`"Slug already taken"`), `400 Bad Request` — автоматическая валидация модели (например, отсутствует `name`/`slug`).

#### `PUT /api/companies/{id}`

**Доступ:** CompanyOwner компании или SuperAdmin.

**Тело запроса** (`UpdateCompanyDto`) — все поля опциональны (`null` = не менять): `name`, `description`, `address`, `phone`, `email`, `allowSelfBooking`, `requirePrepayment`.

```bash
curl -X PUT http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6 \
  -H "Authorization: Bearer $OWNER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "requirePrepayment": true }'
```

**Успешный ответ `200 OK`** — обновлённый `CompanyDto`.

**Ошибки:** `401 Unauthorized`, `404 Not Found` (компания не найдена), `403 Forbidden` (не владелец/SuperAdmin).

#### `POST /api/companies/{id}/members`

**Доступ:** CompanyOwner компании или SuperAdmin, **и** вызывающий должен иметь право назначить именно эту роль (`CanAssignRole` — см. §3.7): CompanyOwner может назначать только `Master`/`CompanyOwner`, SuperAdmin — любую роль.

**Тело запроса** (`AddMemberDto`): `phone` (string), `firstName` (string), `lastName` (string), `role` (string — имя значения `UserRole`), `bio` (string?), `email` (string?, необязательно).

```bash
curl -X POST http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6/members \
  -H "Authorization: Bearer $OWNER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "phone": "+79991234567",
        "firstName": "Алина",
        "lastName": "Ковальчук",
        "role": "Master",
        "bio": "Стаж 5 лет"
      }'
```

Если пользователя с таким телефоном ещё нет, он создаётся автоматически со сгенерированным паролем (см. §3.7). **Владельцу компании стоит отдельно сообщить новому мастеру его телефон и сгенерированный пароль** — API их никуда не отправляет.

**Успешный ответ `200 OK`** (не `201` — эндпоинт использует `Ok(...)`, без заголовка `Location`), тело — `MemberDto`:

```json
{
  "id": "d2a1b3c4-1111-2222-3333-444455556666",
  "userId": "7c3e9f21-aaaa-bbbb-cccc-dddddddddddd",
  "firstName": "Алина",
  "lastName": "Ковальчук",
  "phone": "+79991234567",
  "email": null,
  "avatarUrl": null,
  "role": "Master",
  "bio": "Стаж 5 лет",
  "serviceIds": [],
  "commissionPercent": 0.0
}
```

Новый участник всегда получает `commissionPercent: 0` — процент затем задаётся владельцем отдельным вызовом `PUT /api/companies/{id}/members/{memberId}/commission` (см. выше).

**Ошибки:**

| Код | Причина |
|---|---|
| `401 Unauthorized` | нет токена |
| `403 Forbidden` | вызывающий не CompanyOwner/SuperAdmin данной компании |
| `403 Forbidden` | вызывающий не имеет права назначить запрошенную `role` (CompanyOwner пытается назначить, например, `SuperAdmin`) |
| `400 Bad Request` | не удалось создать нового пользователя (ошибки Identity при автосоздании аккаунта) |
| `409 Conflict` | этот пользователь уже состоит участником данной компании — `"User is already a member"` |
| `500 Internal Server Error` (необработанное исключение) | если `role` — не одно из `Client`/`Master`/`CompanyOwner`/`SuperAdmin` (например, опечатка), `Enum.Parse<UserRole>` выбрасывает исключение — сервер не перехватывает его в чистый `400` |

#### `DELETE /api/companies/{id}/members/{memberId}`

**Доступ:** CompanyOwner компании или SuperAdmin.

```bash
curl -X DELETE http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6/members/d2a1b3c4-1111-2222-3333-444455556666 \
  -H "Authorization: Bearer $OWNER_TOKEN"
```

**Успешный ответ:** `204 No Content`. **Ошибки:** `401`, `403`, `404 Not Found` (участник не найден).

#### `GET /api/companies/{id}/stats`

**Доступ:** CompanyOwner компании или SuperAdmin.

**Query-параметры:** `from` (DateTime), `to` (DateTime) — диапазон по `Booking.CreatedAt` (не по дате визита).

```bash
curl "http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6/stats?from=2026-07-01T00:00:00Z&to=2026-07-31T23:59:59Z" \
  -H "Authorization: Bearer $OWNER_TOKEN"
```

**Ответ `200 OK`** (произвольный объект, не именованный DTO):

```json
{
  "totalRevenue": 12500.0,
  "bookingsCount": 42,
  "completedCount": 35,
  "cancelledCount": 3,
  "newClientsCount": 8,
  "masterStats": [
    { "masterId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4", "masterName": "Алина Ковальчук", "bookingsCount": 20, "revenue": 6000.0 }
  ],
  "popularServices": [
    { "serviceId": "b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e", "serviceName": "Стрижка мужская", "count": 25 }
  ],
  "dailyRevenue": [
    { "date": "2026-07-05", "revenue": 800.0 }
  ]
}
```

`totalRevenue` считается только по **завершённым** записям (`Status == Completed`). `newClientsCount` — число клиентов, у которых **самая первая** запись в этой компании (по всей истории) попадает в интервал `[from, to]`.

**Ошибки:** `401 Unauthorized`, `403 Forbidden`.

---

### 4.4. Services (`/api/services`)

#### `GET /api/services`

**Доступ:** анонимный.

**Query-параметры:** `companyId` (Guid, обязателен).

```bash
curl "http://localhost:5000/api/services?companyId=3fa85f64-5717-4562-b3fc-2c963f66afa6"
```

**Ответ `200 OK`** (`ServiceDto[]`, только активные услуги `IsActive=true`):

```json
[
  { "id": "b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e", "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "name": "Стрижка мужская", "description": null, "durationMinutes": 40, "price": 350.0, "imageUrl": null }
]
```

#### `POST /api/services`

**Доступ:** аутентифицированный; должен быть CompanyOwner **или Master** этой компании, либо SuperAdmin. (Обратите внимание: в отличие от эндпоинтов управления компанией/рассылками/отчётами, здесь **мастера тоже могут** создавать/редактировать/удалять услуги своей компании — не только владелец.)

**Тело запроса** (`CreateServiceDto`): `companyId` (Guid), `name` (string), `description` (string?), `durationMinutes` (int), `price` (decimal), `imageUrl` (string?).

```bash
curl -X POST http://localhost:5000/api/services \
  -H "Authorization: Bearer $OWNER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "name": "Стрижка мужская",
        "description": "Классическая мужская стрижка",
        "durationMinutes": 40,
        "price": 350.0
      }'
```

**Успешный ответ `200 OK`** (не `201` — используется `Ok(...)`) — `ServiceDto`.

**Ошибки:** `401 Unauthorized`, `403 Forbidden` (не владелец/мастер/SuperAdmin этой компании), `400 Bad Request` (не пройдена модельная валидация).

#### `PUT /api/services/{id}`

**Доступ:** аналогично `POST` — CompanyOwner/Master компании, к которой принадлежит услуга, либо SuperAdmin.

**Тело запроса:** тот же `CreateServiceDto` (полная замена `name`/`description`/`durationMinutes`/`price`; `imageUrl` заменяется только если передан не `null`).

```bash
curl -X PUT http://localhost:5000/api/services/b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e \
  -H "Authorization: Bearer $OWNER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "name": "Стрижка мужская (премиум)", "description": null, "durationMinutes": 50, "price": 450.0 }'
```

**Успешный ответ `200 OK`** — обновлённый `ServiceDto`.

**Ошибки:** `401 Unauthorized`, `404 Not Found` (услуга не найдена), `403 Forbidden`.

#### `DELETE /api/services/{id}`

**Доступ:** аналогично. Это **мягкое** удаление — устанавливает `IsActive=false`, запись не удаляется физически (сохраняется для истории записей `Booking`, которые ссылаются на неё).

```bash
curl -X DELETE http://localhost:5000/api/services/b1eac1b1-4b2e-4b3b-9f2a-2f6e1a1c2d3e \
  -H "Authorization: Bearer $OWNER_TOKEN"
```

**Успешный ответ:** `204 No Content`. **Ошибки:** `401`, `404 Not Found`, `403 Forbidden`.

---

### 4.5. Working Hours (`/api/workinghours`)

Класс-контроллер помечен `[Authorize]` — **любой** аутентифицированный пользователь (независимо от роли).

#### `GET /api/workinghours`

**Доступ:** любой аутентифицированный пользователь. **Проверки владения на чтение нет** — любой залогиненный пользователь может запросить рабочее время произвольного мастера, зная его `masterId`/`companyId`.

**Query-параметры:** `masterId` (string), `companyId` (Guid), `from` (DateOnly), `to` (DateOnly).

```bash
curl "http://localhost:5000/api/workinghours?masterId=6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4&companyId=3fa85f64-5717-4562-b3fc-2c963f66afa6&from=2026-07-20&to=2026-07-26" \
  -H "Authorization: Bearer $TOKEN"
```

**Ответ `200 OK`** (`WorkingHoursDto[]`):

```json
[
  {
    "id": "e1f2a3b4-0000-1111-2222-333344445555",
    "masterId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
    "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "date": "2026-07-20",
    "startTime": "09:00:00",
    "endTime": "18:00:00",
    "isWorking": true,
    "breaks": [ { "id": "f2a3b4c5-0000-1111-2222-333344445555", "startTime": "13:00:00", "endTime": "14:00:00" } ]
  }
]
```

**Ошибки:** `401 Unauthorized`.

#### `PUT /api/workinghours`

**Доступ:** должен быть выполнено одно из условий: SuperAdmin; сам мастер (`requesterId == masterId`, то есть мастер редактирует **своё** расписание); либо CompanyOwner указанной компании.

**Тело запроса** (`UpsertWorkingHoursDto`): `masterId` (string), `companyId` (Guid), `date` (DateOnly), `isWorking` (bool), `startTime`/`endTime` (TimeOnly), `breaks` (`UpsertBreakDto[]`, каждый — `startTime`/`endTime`). Если запись на эту дату/мастера/компанию уже существует — она обновляется (перерывы полностью замещаются), иначе создаётся новая.

```bash
curl -X PUT http://localhost:5000/api/workinghours \
  -H "Authorization: Bearer $MASTER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "masterId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
        "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "date": "2026-07-20",
        "isWorking": true,
        "startTime": "09:00:00",
        "endTime": "18:00:00",
        "breaks": [ { "startTime": "13:00:00", "endTime": "14:00:00" } ]
      }'
```

**Успешный ответ `200 OK`** — `WorkingHoursDto`.

**Ошибки:** `401 Unauthorized`, `403 Forbidden` (не сам мастер / не владелец компании / не SuperAdmin), `400 Bad Request` (модельная валидация).

#### `DELETE /api/workinghours/{id}`

**Доступ:** аналогично `PUT` (SuperAdmin, сам мастер записи, либо CompanyOwner компании).

```bash
curl -X DELETE http://localhost:5000/api/workinghours/e1f2a3b4-0000-1111-2222-333344445555 \
  -H "Authorization: Bearer $MASTER_TOKEN"
```

**Успешный ответ:** `204 No Content`. **Ошибки:** `401`, `404 Not Found`, `403 Forbidden`.

---

### 4.6. Schedule Templates (`/api/schedule-template`)

**Доступ ко всем трём эндпоинтам:** аутентифицированный пользователь, который является **самим указанным мастером** (`masterId` совпадает с вызывающим), **владельцем указанной компании** (`CompanyOwner` по `companyId`), либо `SuperAdmin`. Любой другой аутентифицированный пользователь получает `403 Forbidden`. Эта проверка (`CanManage`) идентична той, что уже используется в `WorkingHoursController`.

> Ранее эти три эндпоинта требовали только `[Authorize]` без проверки владения — любой залогиненный пользователь мог прочитать или перезаписать чужое расписание. Это было исправлено; подробности — в разделе [7. Известные ограничения](#7-известные-ограничения-и-незавершённая-функциональность), где зафиксирована история находки.

#### `GET /api/schedule-template`

**Query-параметры:** `masterId` (string), `companyId` (Guid).

```bash
curl "http://localhost:5000/api/schedule-template?masterId=6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4&companyId=3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -H "Authorization: Bearer $TOKEN"
```

**Ответ `200 OK`**:

```json
[
  { "id": "aa11bb22-0000-1111-2222-333344445555", "dayOfWeek": 1, "isWorking": true, "startTime": "09:00:00", "endTime": "18:00:00" },
  { "id": "aa11bb22-0000-1111-2222-333344445556", "dayOfWeek": 7, "isWorking": false, "startTime": "00:00:00", "endTime": "00:00:00" }
]
```

`dayOfWeek` — ISO-нумерация: `1` = понедельник, ..., `7` = воскресенье.

**Ошибки:** `401 Unauthorized` (анонимный вызов), `403 Forbidden` (вызывающий не является ни указанным мастером, ни владельцем компании, ни SuperAdmin). Если шаблона нет — возвращается пустой массив.

#### `PUT /api/schedule-template`

**Тело запроса** (`PutTemplateRequest`): `masterId` (string), `companyId` (Guid), `days` (`DayTemplate[]`: `dayOfWeek` (int, 1–7), `isWorking` (bool), `startTime`/`endTime` (TimeOnly)). Полностью заменяет весь шаблон для этой пары мастер/компания.

```bash
curl -X PUT http://localhost:5000/api/schedule-template \
  -H "Authorization: Bearer $MASTER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "masterId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
        "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "days": [
          { "dayOfWeek": 1, "isWorking": true, "startTime": "09:00:00", "endTime": "18:00:00" },
          { "dayOfWeek": 2, "isWorking": true, "startTime": "09:00:00", "endTime": "18:00:00" },
          { "dayOfWeek": 7, "isWorking": false, "startTime": "00:00:00", "endTime": "00:00:00" }
        ]
      }'
```

**Успешный ответ:** `200 OK` (пустое тело).

**Ошибки:** `401 Unauthorized`, `403 Forbidden`, `400 Bad Request` (модельная валидация).

#### `POST /api/schedule-template/apply`

**Query-параметры:** `masterId` (string), `companyId` (Guid), `from` (DateOnly), `to` (DateOnly).

Применяет ранее сохранённый недельный шаблон к диапазону конкретных дат, создавая/перезаписывая соответствующие записи `WorkingHours` (существующие записи на эти даты обновляются, отсутствующие — создаются).

```bash
curl -X POST "http://localhost:5000/api/schedule-template/apply?masterId=6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4&companyId=3fa85f64-5717-4562-b3fc-2c963f66afa6&from=2026-08-01&to=2026-08-31" \
  -H "Authorization: Bearer $MASTER_TOKEN"
```

**Успешный ответ `200 OK`**: `{ "message": "Template applied successfully." }`

**Ошибки:** `401 Unauthorized`, `403 Forbidden`, `400 Bad Request` — если для этой пары мастер/компания шаблон вообще не сохранён — `"No template found for this master/company."`

---

### 4.7. Masters — управление клиентами (`/api/masters`)

Контроллер помечен `[Authorize]` на уровне класса.

#### `GET /api/masters/clients`

**Доступ:** аутентифицированный пользователь, который является участником (`CompanyMember`, любая роль) указанной компании — иначе `403 Forbidden`.

**Query-параметры:** `companyId` (Guid, обязателен).

Возвращает объединённый список клиентов (зарегистрированных — сгруппированных по `ClientId`, и гостевых — сгруппированных по номеру телефона), у которых были записи к этому мастеру в этой компании. Контактные данные (`phone`/`email`) видны только если с момента окончания **последнего** визита прошло менее 24 часов (см. §3.5) — иначе оба поля возвращаются как `null`. Для каждого клиента также возвращается `bookingSummaries` — полная история его визитов к этому мастеру (дата, услуга, статус), отсортированная от новых к старым; именно её разворачивает карточка клиента на странице «Клиенты» при клике.

Поле `notes` — **общие в рамках компании** заметки о клиенте: показываются заметки, оставленные **любым** мастером этой компании об этом клиенте (`ClientNote.CompanyId == companyId`), а не только заметки вызывающего. Это осознанно: когда клиент впервые приходит к новому мастеру, тот видит контекст, оставленный коллегами. Заметки не пересекаются между разными компаниями.

```bash
curl "http://localhost:5000/api/masters/clients?companyId=3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -H "Authorization: Bearer $MASTER_TOKEN"
```

**Ответ `200 OK`**:

```json
[
  {
    "clientId": "9d8e7f6a-1111-2222-3333-444455556666",
    "guestPhone": null,
    "name": "Мария Сидорова",
    "phone": "+380671234567",
    "email": "maria@example.com",
    "lastVisitDate": "2026-07-18",
    "totalVisits": 3,
    "notes": ["Аллергия на аммиак"],
    "bookingSummaries": [
      { "date": "2026-07-18", "serviceName": "Стрижка мужская", "status": "Completed" },
      { "date": "2026-07-01", "serviceName": "Окрашивание", "status": "Completed" },
      { "date": "2026-06-15", "serviceName": "Стрижка мужская", "status": "NoShow" }
    ]
  },
  {
    "clientId": null,
    "guestPhone": "+380509876543",
    "name": "Олег Гость",
    "phone": null,
    "email": null,
    "lastVisitDate": "2026-07-10",
    "totalVisits": 1,
    "notes": [],
    "bookingSummaries": [
      { "date": "2026-07-10", "serviceName": "Стрижка мужская", "status": "Completed" }
    ]
  }
]
```

(Во втором примере с момента окончания визита прошло больше 24 часов, поэтому `phone`/`email` скрыты; `bookingSummaries` от этого не зависит и показывается всегда.)

**Ошибки:** `401 Unauthorized`, `403 Forbidden` (вызывающий не состоит участником данной компании).

#### `POST /api/masters/clients/notes`

**Доступ:** аутентифицированный пользователь, который является участником (`CompanyMember`, любая роль) указанной компании (`companyId`) — иначе `403 Forbidden`. Заметка записывается с автором `MasterId = userId` и привязывается к компании `CompanyId = companyId`, попадая в её общую историю клиента (см. `GET /api/masters/clients` выше).

**Тело запроса** (`AddNoteRequest`): `companyId` (Guid, обязателен), `clientId` (string?), `guestPhone` (string?), `note` (string, обязательно).

```bash
curl -X POST http://localhost:5000/api/masters/clients/notes \
  -H "Authorization: Bearer $MASTER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "clientId": "9d8e7f6a-1111-2222-3333-444455556666", "note": "Аллергия на аммиак" }'
```

**Успешный ответ `200 OK`**: `{ "id": "b1b2b3b4-0000-1111-2222-333344445555" }`

**Ошибки:** `401 Unauthorized`, `403 Forbidden` (вызывающий не участник указанной компании), `400 Bad Request` (не передано `note`).

#### `DELETE /api/masters/clients/notes/{id}`

**Доступ:** удалить заметку может только её **автор** (`MasterId == userId`) — заметки хоть и видны всем мастерам компании (см. `GET /api/masters/clients`), но правит/удаляет каждый только свои. Если заметка существует, но принадлежит другому мастеру, эндпоинт вернёт `404 Not Found` (а не `403 Forbidden`), так как запрос к БД уже включает фильтр по `MasterId`.

```bash
curl -X DELETE http://localhost:5000/api/masters/clients/notes/b1b2b3b4-0000-1111-2222-333344445555 \
  -H "Authorization: Bearer $MASTER_TOKEN"
```

**Успешный ответ:** `204 No Content`. **Ошибки:** `401 Unauthorized`, `404 Not Found` (заметка не найдена либо принадлежит другому мастеру).

---

### 4.8. Reviews (`/api/reviews`, `/api/companies/{companyId}/reviews`)

#### `POST /api/reviews`

**Доступ:** любой аутентифицированный пользователь.

**Тело запроса** (`CreateReviewRequest`): `bookingId` (Guid), `rating` (int, **проверяется на диапазон 1–5** сервером), `comment` (string?).

```bash
curl -X POST http://localhost:5000/api/reviews \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "bookingId": "c47ac10b-58cc-4372-a567-0e02b2c3d479", "rating": 5, "comment": "Отличный мастер!" }'
```

**Успешный ответ `200 OK`**: `{ "id": "e5e6e7e8-0000-1111-2222-333344445555" }`

**Ошибки:**

| Код | Причина |
|---|---|
| `401 Unauthorized` | нет токена |
| `400 Bad Request` | `rating` вне диапазона 1–5 — `"Rating must be between 1 and 5."` |
| `404 Not Found` | запись (`bookingId`) не найдена |
| `400 Bad Request` | запись ещё не завершена (`Status != Completed`) — `"Booking is not completed."` |
| `403 Forbidden` | у записи указан `ClientId`, отличный от вызывающего (нельзя оставить отзыв за чужую запись) |
| `409 Conflict` | отзыв на эту запись уже существует — `"Review already exists for this booking."` |

Замечание: если у записи `ClientId == null` (это была гостевая/ручная запись), проверка владения `booking.ClientId != null && booking.ClientId != userId` не срабатывает — то есть **любой** аутентифицированный пользователь технически может оставить отзыв за гостевую запись (нет привязки к конкретному человеку, которого можно было бы проверить).

#### `GET /api/reviews/can-review`

**Доступ:** любой аутентифицированный пользователь. Возвращает список `bookingId` завершённых записей текущего клиента, на которые он ещё **не** оставил отзыв.

```bash
curl http://localhost:5000/api/reviews/can-review -H "Authorization: Bearer $TOKEN"
```

**Ответ `200 OK`**: `["c47ac10b-58cc-4372-a567-0e02b2c3d479"]`

**Ошибки:** `401 Unauthorized`.

#### `GET /api/companies/{companyId}/reviews`

**Доступ:** анонимный (публичные отзывы компании для витрины).

```bash
curl http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6/reviews
```

**Ответ `200 OK`**:

```json
[
  {
    "id": "e5e6e7e8-0000-1111-2222-333344445555",
    "rating": 5,
    "comment": "Отличный мастер!",
    "reviewerName": "Иван Петров",
    "masterName": "Алина Ковальчук",
    "serviceName": "Стрижка мужская",
    "createdAt": "2026-07-18T12:00:00Z"
  }
]
```

Если компания с таким `companyId` не существует — возвращается пустой массив `[]` (без `404`).

---

### 4.9. Mailing / Рассылки (`/api/companies/{id}/mail`)

Контроллер помечен `[Authorize]` на уровне класса.

#### `POST /api/companies/{id}/mail`

**Доступ:** CompanyOwner компании или SuperAdmin.

**Тело запроса** (`SendMailDto`): `subject` (string), `message` (string).

Собирает список email всех клиентов, у которых есть хотя бы одна запись в этой компании (`ClientId != null`), и **сохраняет запись о рассылке** (`MailLog`) с числом получателей. **Реальная отправка email не выполняется** — фактических писем никто не получит (см. §7).

```bash
curl -X POST http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6/mail \
  -H "Authorization: Bearer $OWNER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "subject": "Летняя акция", "message": "Скидка 15% на все услуги до конца августа!" }'
```

**Успешный ответ `200 OK`**: `{ "recipientCount": 8, "message": "Рассылка поставлена в очередь" }`

**Ошибки:** `401 Unauthorized`, `403 Forbidden`.

#### `GET /api/companies/{id}/mail`

**Доступ:** CompanyOwner компании или SuperAdmin. Возвращает историю рассылок (сущности `MailLog`), отсортированные по дате отправки (убывание).

```bash
curl http://localhost:5000/api/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6/mail \
  -H "Authorization: Bearer $OWNER_TOKEN"
```

**Ответ `200 OK`**:

```json
[
  {
    "id": "cc11dd22-0000-1111-2222-333344445555",
    "companyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "subject": "Летняя акция",
    "message": "Скидка 15% на все услуги до конца августа!",
    "sentById": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "recipientCount": 8,
    "sentAt": "2026-07-18T12:30:00Z",
    "company": null,
    "sentBy": null
  }
]
```

**Ошибки:** `401 Unauthorized`, `403 Forbidden`.

---

### 4.10. Reports (`/api/reports`)

#### `GET /api/reports/masters`

**Доступ:** `[Authorize(Roles = "CompanyOwner,SuperAdmin")]` на уровне класса, плюс для не-SuperAdmin — проверка, что вызывающий является CompanyOwner именно указанной компании.

**Query-параметры:** `companyId` (Guid), `from` (DateOnly), `to` (DateOnly) — диапазон по `Booking.Date` (дате визита, не создания).

Считает только **завершённые** (`Completed`) записи, группирует по мастеру и делит выручку на долю мастера/компании по `CommissionPercent` (см. §3.6).

```bash
curl "http://localhost:5000/api/reports/masters?companyId=3fa85f64-5717-4562-b3fc-2c963f66afa6&from=2026-07-01&to=2026-07-31" \
  -H "Authorization: Bearer $OWNER_TOKEN"
```

**Ответ `200 OK`** (`MasterReportDto[]`, отсортировано по `totalAmount` по убыванию):

```json
[
  {
    "masterId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
    "masterName": "Алина Ковальчук",
    "commissionPercent": 40.0,
    "bookingsCount": 20,
    "totalAmount": 6000.0,
    "masterEarnings": 2400.0,
    "companyEarnings": 3600.0
  }
]
```

**Ошибки:** `401 Unauthorized`, `403 Forbidden` (роль не подходит, либо не владелец указанной компании).

---

### 4.11. Profile (`/api/profile`)

Контроллер помечен `[Authorize]` на уровне класса — оперирует только данными самого вызывающего пользователя.

#### `GET /api/profile`

```bash
curl http://localhost:5000/api/profile -H "Authorization: Bearer $TOKEN"
```

**Ответ `200 OK`** (`ProfileDto`):

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "ivan.petrov@example.com",
  "firstName": "Иван",
  "lastName": "Петров",
  "avatarUrl": null,
  "commissionPercent": 0.0,
  "roles": ["Client"]
}
```

`commissionPercent` здесь — **только для чтения**: устанавливается владельцем компании через `PUT /api/companies/{id}/members/{memberId}/commission` (см. §4.3), а не самим пользователем.

**Ошибки:** `401 Unauthorized`, `404 Not Found` (крайне маловероятный случай — пользователь из валидного токена не найден в БД, например, был удалён).

#### `PUT /api/profile`

**Тело запроса** (`UpdateProfileDto`): `firstName` (string), `lastName` (string). Комиссию через этот эндпоинт задать нельзя — поле `commissionPercent` намеренно отсутствует в `UpdateProfileDto`.

```bash
curl -X PUT http://localhost:5000/api/profile \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "firstName": "Иван", "lastName": "Петров" }'
```

**Успешный ответ `200 OK`** — обновлённый `ProfileDto` (значение `commissionPercent` в ответе не меняется этим вызовом).

**Ошибки:** `401 Unauthorized`, `404 Not Found`, `400 Bad Request` (ошибка Identity при обновлении, например конфликт `UserName`).

#### `POST /api/profile/change-password`

**Тело запроса** (`ChangePasswordDto`): `currentPassword` (string), `newPassword` (string, должен соответствовать правилам пароля — см. §2.3).

```bash
curl -X POST http://localhost:5000/api/profile/change-password \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "currentPassword": "Passw0rd1", "newPassword": "NewPassw0rd2" }'
```

**Успешный ответ:** `204 No Content`.

**Ошибки:** `401 Unauthorized`, `404 Not Found`, `400 Bad Request` — неверный текущий пароль, либо новый пароль не проходит правила Identity (сообщение — либо конкретная Identity-ошибка, либо, если её нет, generic `"Неверный текущий пароль"`).

---

### 4.12. Admin (`/api/admin`)

Весь контроллер помечен `[Authorize(Roles = "SuperAdmin")]` — доступ **только** для SuperAdmin. Для любого другого аутентифицированного пользователя — `403 Forbidden`; для неаутентифицированного — `401 Unauthorized`.

#### `GET /api/admin/stats`

```bash
curl http://localhost:5000/api/admin/stats -H "Authorization: Bearer $ADMIN_TOKEN"
```

**Ответ `200 OK`** (`AdminStatsDto`):

```json
{
  "totalCompanies": 12,
  "totalUsers": 340,
  "totalBookings": 5210,
  "completedBookings": 4890,
  "totalRevenue": 812500.0
}
```

#### `GET /api/admin/users`

**Query-параметры:** `search` (string?, опционально — подстрока по email/firstName/lastName).

```bash
curl "http://localhost:5000/api/admin/users?search=ivan" -H "Authorization: Bearer $ADMIN_TOKEN"
```

**Ответ `200 OK`** (`AdminUserDto[]`):

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "ivan.petrov@example.com",
    "firstName": "Иван",
    "lastName": "Петров",
    "avatarUrl": null,
    "commissionPercent": 0.0,
    "createdAt": "2026-05-01T10:00:00Z",
    "roles": ["Client"],
    "ownedCompanyCount": 0,
    "planConfigId": null,
    "planName": "Free",
    "paidUntil": null,
    "subscriptionActive": true
  }
]
```

Тариф здесь — тот же аккаунт-уровневый `AccountSubscription`, что и в `GET /api/admin/companies` (см. §3.1) — управление подпиской живёт на вкладке «Пользователи» именно потому, что подписка привязана к владельцу, а не к отдельной компании.

#### `PUT /api/admin/users/{id}/roles`

**Тело запроса:** `List<string>` — новый **полный** список ролей пользователя (полностью заменяет текущий: сначала удаляются все текущие роли, затем присваиваются переданные).

```bash
curl -X PUT http://localhost:5000/api/admin/users/3fa85f64-5717-4562-b3fc-2c963f66afa6/roles \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '["Client", "Master"]'
```

**Успешный ответ:** `204 No Content`.

**Ошибки:** `401`, `403`, `404 Not Found` (пользователь не найден), `400 Bad Request` — если хотя бы одна из переданных строк роли не существует (`{ "message": "Unknown role(s): ..." }`). Эта проверка (`RoleManager.RoleExistsAsync`) выполняется **до** удаления текущих ролей пользователя, поэтому неудачный запрос ничего не меняет — старые роли остаются нетронутыми.

```bash
curl -X PUT http://localhost:5000/api/admin/users/3fa85f64-5717-4562-b3fc-2c963f66afa6/roles \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '["NotARealRole"]'
# → 400 Bad Request: { "message": "Unknown role(s): NotARealRole" }
```

#### `GET /api/admin/companies`

**Query-параметры:** `search` (string?, опционально — по имени/email компании).

```bash
curl "http://localhost:5000/api/admin/companies" -H "Authorization: Bearer $ADMIN_TOKEN"
```

**Ответ `200 OK`** (`AdminCompanyDto[]`):

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "Салон \"Мираж\"",
    "slug": "mirage-salon",
    "email": "info@mirage.example",
    "phone": "+380441234567",
    "isActive": true,
    "createdAt": "2026-01-15T09:00:00Z",
    "memberCount": 3,
    "bookingCount": 512,
    "ownerUserId": "6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4",
    "ownerEmail": "owner@mirage.example",
    "planConfigId": "dd11ee22-0000-1111-2222-333344445555",
    "planName": "Basic",
    "paidUntil": "2026-12-31T00:00:00Z",
    "subscriptionActive": true
  }
]
```

Подписка резолвится через владельца (`ownerUserId`) — если у нескольких компаний один и тот же владелец, у них будут одинаковые `planConfigId`/`planName`/`paidUntil`/`subscriptionActive` (см. §3.1). При отсутствии подписки у владельца `planConfigId` — `null`, а `planName` — строка `"Free"`.

#### `PUT /api/admin/owners/{ownerUserId}/subscription`

Назначает/обновляет тариф **аккаунта владельца** (не отдельной компании) — покрывает сразу все компании, которыми он владеет. Если у владельца ещё не было записи `AccountSubscription`, она создаётся; иначе обновляется (upsert), а не дублируется.

**Тело запроса** (`UpdateSubscriptionDto`): `planConfigId` (Guid?, `null` = сброс на Free), `paidUntil` (DateTime?), `isActive` (bool), `comment` (string?).

```bash
curl -X PUT http://localhost:5000/api/admin/owners/6a9c1e2d-3f4b-4a5c-8d6e-7f8091a2b3c4/subscription \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "planConfigId": "dd11ee22-0000-1111-2222-333344445555", "paidUntil": "2027-01-01T00:00:00Z", "isActive": true, "comment": "Оплачено по счёту #4521" }'
```

Каждый вызов также добавляет запись в `SubscriptionChangeLog` (старое/новое значение плана, срока и активности, кто изменил) — историю можно посмотреть через `GET /api/admin/owners/{ownerUserId}/subscription-history` (новые изменения первыми).

`paidUntil` нормализуется в UTC на сервере перед сохранением — это оправданно тем, что обычный `<input type="date">` в браузере шлёт «голую» дату без времени/зоны (`"2026-08-01"`), а Npgsql требует `Kind=Utc` для колонки `timestamp with time zone`.

**Успешный ответ:** `204 No Content`. **Ошибки:** `401`, `403`, `400 Bad Request` (модельная валидация).

#### `PUT /api/admin/companies/{id}`

**Тело запроса** (`AdminUpdateCompanyDto`): `name` (string), `isActive` (bool), `allowSelfBooking` (bool) — упрощённое административное редактирование (в отличие от `PUT /api/companies/{id}`, здесь нет частичного обновления — все три поля обязательны и перезаписываются целиком).

```bash
curl -X PUT http://localhost:5000/api/admin/companies/3fa85f64-5717-4562-b3fc-2c963f66afa6 \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "name": "Салон \"Мираж\"", "isActive": false, "allowSelfBooking": true }'
```

**Успешный ответ:** `204 No Content`. **Ошибки:** `401`, `403`, `404 Not Found` (компания не найдена).

#### `GET /api/admin/bookings`

**Query-параметры (все опциональны):** `companyId` (Guid?), `from` (DateOnly?), `to` (DateOnly?), `status` (`BookingStatus`?). Возвращает не более **500** последних записей (`Take(500)`), отсортированных по дате/времени по убыванию.

```bash
curl "http://localhost:5000/api/admin/bookings?status=Completed&from=2026-07-01&to=2026-07-31" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

**Ответ `200 OK`** (`AdminBookingDto[]`):

```json
[
  {
    "id": "c47ac10b-58cc-4372-a567-0e02b2c3d479",
    "companyName": "Салон \"Мираж\"",
    "serviceName": "Стрижка мужская",
    "masterName": "Алина Ковальчук",
    "clientName": "Мария Сидорова",
    "clientPhone": "+380671234567",
    "date": "2026-07-20",
    "startTime": "09:00:00",
    "endTime": "09:40:00",
    "status": "Completed",
    "price": 350.0
  }
]
```

Если у записи нет `Client` (гостевая запись), `clientName` заполняется как `"Гость"` (жёстко закодированное значение на русском, единственное такое место во всём API — все остальные fallback-значения в коде на английском, `"Guest"`).

#### `GET /api/admin/plans`

Возвращает список тарифных конфигураций `SubscriptionPlanConfig`, отсортированных по цене.

```bash
curl http://localhost:5000/api/admin/plans -H "Authorization: Bearer $ADMIN_TOKEN"
```

**Ответ `200 OK`**:

```json
[
  {
    "id": "dd11ee22-0000-1111-2222-333344445555",
    "name": "Pro",
    "planKey": "pro",
    "pricePerMonth": 999.0,
    "maxMasters": null,
    "allowOnlineBooking": true,
    "allowMailing": true,
    "allowAnalytics": true,
    "description": "Полный доступ ко всем функциям",
    "isActive": true,
    "notifyDaysBefore": 7,
    "createdAt": "2026-01-01T00:00:00Z"
  }
]
```

#### `POST /api/admin/plans`

**Тело запроса:** сущность `SubscriptionPlanConfig` целиком (не отдельный DTO) — но поля `id` и `createdAt` из тела запроса **игнорируются** и переустанавливаются сервером (`Guid.NewGuid()` / `DateTime.UtcNow`).

```bash
curl -X POST http://localhost:5000/api/admin/plans \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
        "name": "Enterprise",
        "planKey": "enterprise",
        "pricePerMonth": 2999.0,
        "maxMasters": null,
        "allowOnlineBooking": true,
        "allowMailing": true,
        "allowAnalytics": true,
        "description": "Для сетей салонов",
        "isActive": true,
        "notifyDaysBefore": 14
      }'
```

**Успешный ответ `200 OK`** — созданный `SubscriptionPlanConfig` (с новым `id`).

**Ошибки:** `401 Unauthorized`, `403 Forbidden`, `400 Bad Request` (модельная валидация).

**Важно:** тарифные конфигурации, создаваемые здесь (`SubscriptionPlanConfig`), **являются** источником истины для гейтинга — `SubscriptionResolver` резолвит эффективный план компании через подписку её владельца (`AccountSubscription.PlanConfigId`) и читает флаги (`AllowOnlineBooking`, `AllowMailing`, `AllowAnalytics`, `MaxEmployees`, `MaxCompanies`) отсюда напрямую — см. §3.1. Правка конфигурации плана (например, включение `AllowOnlineBooking`) немедленно меняет поведение для всех аккаунтов, подписанных на этот план.

#### `PUT /api/admin/plans/{id}`

**Тело запроса:** тот же `SubscriptionPlanConfig` (без `id`/`createdAt` — не изменяются).

```bash
curl -X PUT http://localhost:5000/api/admin/plans/dd11ee22-0000-1111-2222-333344445555 \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "name": "Pro", "planKey": "pro", "pricePerMonth": 1199.0, "maxMasters": null, "allowOnlineBooking": true, "allowMailing": true, "allowAnalytics": true, "description": "Обновлённая цена", "isActive": true, "notifyDaysBefore": 7 }'
```

**Успешный ответ `200 OK`** — обновлённый план. **Ошибки:** `401`, `403`, `404 Not Found`.

#### `DELETE /api/admin/plans/{id}`

Мягкое удаление — `IsActive=false`.

```bash
curl -X DELETE http://localhost:5000/api/admin/plans/dd11ee22-0000-1111-2222-333344445555 \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

**Успешный ответ:** `204 No Content`. **Ошибки:** `401`, `403`, `404 Not Found`.

---

## 5. Сквозные сценарии использования

Во всех примерах используется базовый адрес `http://localhost:5000`. Переменные вида `$TOKEN`, `$OWNER_TOKEN` и т.д. — это токены, полученные из предыдущих шагов сценария (в реальном терминале их нужно либо вручную скопировать, либо извлечь через `jq -r .token`).

### 5.1. Онбординг владельца салона

```bash
# 1. Регистрация владельца
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{ "firstName": "Наталья", "lastName": "Гриценко", "email": "natalia@mirage.example", "password": "Passw0rd1", "phone": "+380671112233" }'
# → сохранить .token как $OWNER_TOKEN, .userId как $OWNER_ID

# 2. Создание компании (автоматически присваивает роль CompanyOwner)
curl -X POST http://localhost:5000/api/companies \
  -H "Authorization: Bearer $OWNER_TOKEN" -H "Content-Type: application/json" \
  -d '{ "name": "Салон \"Мираж\"", "slug": "mirage-salon", "address": "ул. Central, 10", "phone": "+380441234567", "email": "info@mirage.example", "allowSelfBooking": true }'
# → сохранить .id как $COMPANY_ID

# 3. Добавление мастера (email ещё не зарегистрирован — аккаунт создастся автоматически с паролем "Alina123")
curl -X POST http://localhost:5000/api/companies/$COMPANY_ID/members \
  -H "Authorization: Bearer $OWNER_TOKEN" -H "Content-Type: application/json" \
  -d '{ "email": "alina@example.com", "firstName": "Алина", "lastName": "Ковальчук", "role": "Master", "bio": "Стаж 5 лет" }'
# → сохранить .userId как $MASTER_ID и .id как $MEMBER_ID; сообщить Алине email + пароль "Alina123" отдельно (по телефону/лично) — API их не рассылает

# 4. Создание услуги
curl -X POST http://localhost:5000/api/services \
  -H "Authorization: Bearer $OWNER_TOKEN" -H "Content-Type: application/json" \
  -d "{ \"companyId\": \"$COMPANY_ID\", \"name\": \"Стрижка мужская\", \"description\": \"Классическая мужская стрижка\", \"durationMinutes\": 40, \"price\": 350.0 }"
# → сохранить .id как $SERVICE_ID

# 5. Привязка услуги к мастеру (нужен memberId из шага 3, поле id ответа AddMember)
curl -X PUT http://localhost:5000/api/companies/$COMPANY_ID/members/$MEMBER_ID/services \
  -H "Authorization: Bearer $OWNER_TOKEN" -H "Content-Type: application/json" \
  -d "[\"$SERVICE_ID\"]"

# 6. Установка процента комиссии мастера (только владелец — сам мастер этого сделать не может)
curl -X PUT http://localhost:5000/api/companies/$COMPANY_ID/members/$MEMBER_ID/commission \
  -H "Authorization: Bearer $OWNER_TOKEN" -H "Content-Type: application/json" \
  -d '{ "commissionPercent": 35.0 }'

# 7. Установка рабочего времени мастера на конкретный день
curl -X PUT http://localhost:5000/api/workinghours \
  -H "Authorization: Bearer $OWNER_TOKEN" -H "Content-Type: application/json" \
  -d "{ \"masterId\": \"$MASTER_ID\", \"companyId\": \"$COMPANY_ID\", \"date\": \"2026-07-20\", \"isWorking\": true, \"startTime\": \"09:00:00\", \"endTime\": \"18:00:00\", \"breaks\": [{ \"startTime\": \"13:00:00\", \"endTime\": \"14:00:00\" }] }"

# 8. (Опционально) Настройка недельного шаблона расписания и его применение на месяц вперёд
curl -X PUT http://localhost:5000/api/schedule-template \
  -H "Authorization: Bearer $OWNER_TOKEN" -H "Content-Type: application/json" \
  -d "{ \"masterId\": \"$MASTER_ID\", \"companyId\": \"$COMPANY_ID\", \"days\": [
        { \"dayOfWeek\": 1, \"isWorking\": true, \"startTime\": \"09:00:00\", \"endTime\": \"18:00:00\" },
        { \"dayOfWeek\": 2, \"isWorking\": true, \"startTime\": \"09:00:00\", \"endTime\": \"18:00:00\" },
        { \"dayOfWeek\": 7, \"isWorking\": false, \"startTime\": \"00:00:00\", \"endTime\": \"00:00:00\" }
      ] }"

curl -X POST "http://localhost:5000/api/schedule-template/apply?masterId=$MASTER_ID&companyId=$COMPANY_ID&from=2026-08-01&to=2026-08-31" \
  -H "Authorization: Bearer $OWNER_TOKEN"
```

### 5.2. Клиентский сценарий бронирования

```bash
# 1. Регистрация клиента (можно пропустить и бронировать как гость — см. §5.4)
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{ "firstName": "Иван", "lastName": "Петров", "email": "ivan.petrov@example.com", "password": "Passw0rd1" }'
# → сохранить .token как $CLIENT_TOKEN

# 2. Просмотр доступных слотов на дату
curl "http://localhost:5000/api/bookings/slots?masterId=$MASTER_ID&serviceId=$SERVICE_ID&date=2026-07-20"

# 3. Создание записи
curl -X POST http://localhost:5000/api/bookings \
  -H "Authorization: Bearer $CLIENT_TOKEN" -H "Content-Type: application/json" \
  -d "{ \"companyId\": \"$COMPANY_ID\", \"serviceId\": \"$SERVICE_ID\", \"masterId\": \"$MASTER_ID\", \"date\": \"2026-07-20\", \"startTime\": \"09:00:00\" }"
# → сохранить .id как $BOOKING_ID

# 4. (Позже, после того как мастер отметит запись завершённой) — оставить отзыв
curl http://localhost:5000/api/reviews/can-review -H "Authorization: Bearer $CLIENT_TOKEN"
curl -X POST http://localhost:5000/api/reviews \
  -H "Authorization: Bearer $CLIENT_TOKEN" -H "Content-Type: application/json" \
  -d "{ \"bookingId\": \"$BOOKING_ID\", \"rating\": 5, \"comment\": \"Отлично!\" }"
```

### 5.3. Рабочий день мастера

```bash
# 1. Вход мастера
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{ "email": "alina@example.com", "password": "Alina123" }'
# → сохранить .token как $MASTER_TOKEN

# 2. Просмотр сегодняшних записей
curl "http://localhost:5000/api/bookings/master?date=2026-07-20&to=2026-07-20" \
  -H "Authorization: Bearer $MASTER_TOKEN"

# 3. Завершить одну запись
curl -X PATCH http://localhost:5000/api/bookings/$BOOKING_ID/complete \
  -H "Authorization: Bearer $MASTER_TOKEN"

# 4. Отметить неявку клиента по другой записи
curl -X PATCH http://localhost:5000/api/bookings/$OTHER_BOOKING_ID/noshow \
  -H "Authorization: Bearer $MASTER_TOKEN"

# 5. Перенести ещё одну запись на другое время
curl -X PATCH http://localhost:5000/api/bookings/$THIRD_BOOKING_ID/reschedule \
  -H "Authorization: Bearer $MASTER_TOKEN" -H "Content-Type: application/json" \
  -d '{ "date": "2026-07-21", "startTime": "11:00:00" }'

# 6. Добавить приватную заметку о клиенте
curl -X POST http://localhost:5000/api/masters/clients/notes \
  -H "Authorization: Bearer $MASTER_TOKEN" -H "Content-Type: application/json" \
  -d '{ "clientId": "9d8e7f6a-1111-2222-3333-444455556666", "note": "Просит не использовать средства с аммиаком" }'
```

### 5.4. Гостевое (walk-in) бронирование без аккаунта

```bash
# Компания на плане Free и AllowSelfBooking=true — гостевая запись всё равно заблокирована планом
curl -i -X POST http://localhost:5000/api/bookings \
  -H "Content-Type: application/json" \
  -d "{ \"companyId\": \"$FREE_COMPANY_ID\", \"serviceId\": \"$SERVICE_ID\", \"masterId\": \"$MASTER_ID\",
       \"date\": \"2026-07-20\", \"startTime\": \"09:00:00\",
       \"guestName\": \"Олег Гость\", \"guestPhone\": \"+380509876543\" }"
# → 402 Payment Required: "Online booking requires a paid subscription."

# Та же компания переведена SuperAdmin-ом на план Basic/Pro — гостевая запись проходит успешно
curl -X PUT http://localhost:5000/api/admin/companies/$FREE_COMPANY_ID/subscription \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{ "plan": "Basic", "paidUntil": "2027-01-01T00:00:00Z", "isActive": true, "notes": null }'

curl -i -X POST http://localhost:5000/api/bookings \
  -H "Content-Type: application/json" \
  -d "{ \"companyId\": \"$FREE_COMPANY_ID\", \"serviceId\": \"$SERVICE_ID\", \"masterId\": \"$MASTER_ID\",
       \"date\": \"2026-07-20\", \"startTime\": \"09:00:00\",
       \"guestName\": \"Олег Гость\", \"guestPhone\": \"+380509876543\" }"
# → 201 Created

# Если у компании AllowSelfBooking=false, ЛЮБОЙ гостевой запрос блокируется независимо от плана
curl -X PUT http://localhost:5000/api/companies/$COMPANY_ID \
  -H "Authorization: Bearer $OWNER_TOKEN" -H "Content-Type: application/json" \
  -d '{ "allowSelfBooking": false }'

curl -i -X POST http://localhost:5000/api/bookings \
  -H "Content-Type: application/json" \
  -d "{ \"companyId\": \"$COMPANY_ID\", \"serviceId\": \"$SERVICE_ID\", \"masterId\": \"$MASTER_ID\",
       \"date\": \"2026-07-20\", \"startTime\": \"09:00:00\",
       \"guestName\": \"Олег Гость\", \"guestPhone\": \"+380509876543\" }"
# → 403 Forbidden (даже если план — Pro)
```

### 5.5. Операции SuperAdmin

По умолчанию (из `appsettings.json`) SuperAdmin создаётся при первом старте приложения с email `admin@servicebooking.com` и паролем `Admin12345` (значения читаются из конфигурации `SuperAdmin:Email` / `SuperAdmin:Password` — в реальном окружении их следует сменить/задать через переменные окружения).

```bash
# 1. Вход SuperAdmin
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{ "email": "admin@servicebooking.com", "password": "Admin12345" }'
# → сохранить .token как $ADMIN_TOKEN

# 2. Просмотр общей статистики платформы
curl http://localhost:5000/api/admin/stats -H "Authorization: Bearer $ADMIN_TOKEN"

# 3. Изменение тарифного плана владельца компании (подписка привязана к аккаунту, не к компании)
curl -X PUT http://localhost:5000/api/admin/owners/$OWNER_ID/subscription \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{ "planConfigId": "dd11ee22-0000-1111-2222-333344445555", "paidUntil": "2027-01-01T00:00:00Z", "isActive": true, "comment": "Годовая подписка" }'

# 4. Просмотр и изменение ролей пользователя
curl "http://localhost:5000/api/admin/users?search=alina" -H "Authorization: Bearer $ADMIN_TOKEN"
curl -X PUT http://localhost:5000/api/admin/users/$MASTER_ID/roles \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '["Client", "Master", "CompanyOwner"]'

# 5. Создание нового тарифного плана
curl -X POST http://localhost:5000/api/admin/plans \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{ "name": "Enterprise", "planKey": "enterprise", "pricePerMonth": 2999.0, "maxMasters": null,
       "allowOnlineBooking": true, "allowMailing": true, "allowAnalytics": true,
       "description": "Для сетей салонов", "isActive": true, "notifyDaysBefore": 14 }'
```

---

## 6. Справочник кодов ответа

| Код | Когда встречается |
|---|---|
| `200 OK` | Успешное чтение или обработка запроса, где не требуется возвращать заголовок `Location` (большинство `GET`, а также многие `POST`/`PUT`, использующие `Ok(...)` вместо `CreatedAtAction(...)`, например `POST /api/companies/{id}/members`, `POST /api/services`) |
| `201 Created` | Успешное создание ресурса через `CreatedAtAction` — только `POST /api/bookings` и `POST /api/companies` |
| `204 No Content` | Успешная операция без тела ответа: большинство `PATCH`/`DELETE`, а также некоторые `PUT` (например, `PUT /api/companies/{id}/members/{memberId}/services`, `PUT /api/admin/*`) |
| `400 Bad Request` | Не пройдена модельная валидация ASP.NET Core (автоматически для любого контроллера с `[ApiController]`); бизнес-валидация не проходит (например, "Name and phone are required for guest booking", неверный текущий пароль, ошибки Identity при регистрации/создании пользователя, запись уже отменена при попытке завершить/отметить неявку, `rating` вне диапазона 1–5 при создании отзыва, неизвестное имя роли в `PUT /api/admin/users/{id}/roles`) |
| `401 Unauthorized` | Заголовок `Authorization` отсутствует или токен невалиден/просрочен на защищённом эндпоинте; неверные учётные данные при входе; аккаунт временно заблокирован после 5 неудачных попыток входа |
| `402 Payment Required` | Гостевая запись при плане подписки Free (`Online booking requires a paid subscription.`); попытка создать новую запись при просроченной подписке (`Subscription expired. New bookings are not allowed.`) — оба случая только в `POST /api/bookings` |
| `403 Forbidden` | Пользователь аутентифицирован, но не обладает нужной ролью (`[Authorize(Roles=...)]`) или не проходит проверку владения (не CompanyOwner/мастер/SuperAdmin для данного ресурса, включая `ScheduleTemplateController`); гостевая запись при `Company.AllowSelfBooking=false`; попытка назначить роль, на которую нет прав (CompanyOwner пытается назначить SuperAdmin); попытка оставить отзыв за чужую (привязанную к другому клиенту) запись |
| `404 Not Found` | Ресурс с указанным идентификатором не найден (компания, услуга, запись, участник компании, тарифный план, пользователь и т.д.); компания не найдена при попытке гостевого бронирования |
| `409 Conflict` | Выбранный временной слот уже занят другой записью (создание/перенос записи); `slug` компании уже занят; отзыв на эту запись уже существует; пользователь уже состоит участником компании |
| `500 Internal Server Error` | Необработанное исключение — единственный задокументированный явный случай: передача в `POST /api/companies/{id}/members` значения `role`, не входящего в `Client`/`Master`/`CompanyOwner`/`SuperAdmin` (падает `Enum.Parse<UserRole>`) |

---

## 7. Известные ограничения и незавершённая функциональность

Ниже — честный список того, что в текущей реализации либо не доведено до промышленного уровня, либо содержит пробелы, о которых должна знать интегрирующая сторона.

1. **Нет реального платёжного шлюза.** Поле `Company.RequirePrepayment` и статус `PaymentStatus.Pending` — это чисто организационный флаг: клиент, оформляя запись, не проводит оплату через API. Подтверждение оплаты — полностью ручная операция (`PATCH /api/bookings/{id}/mark-paid`), которую выполняет мастер/владелец/SuperAdmin, полагаясь на оплату, полученную вне системы (наличными, переводом и т.п.).

2. **Капча — Yandex SmartCaptcha с поведением fail-closed.** Гостевое бронирование защищено через серверную валидацию Yandex SmartCaptcha (`CaptchaService`, `POST https://smartcaptcha.cloud.yandex.ru/validate`, успех — `status == "ok"`). Логика: если задан `SmartCaptcha:SecretKey` — токен обязателен и проверяется по-настоящему; отсутствие ключа означает пропуск капчи **только вне Production** (Development/Testing, для удобства локальной разработки), а в **Production** без ключа гостевое бронирование **отклоняется** (fail-closed — тихого обхода нет). Любая ошибка (нет токена, `status != "ok"`, недоступность сервиса валидации) трактуется как провал. Клиентский виджет на фронтенде подключается через переменную окружения `VITE_SMARTCAPTCHA_SITEKEY`: если она не задана — виджет не отображается (dev), если задана — рендерится и отдаёт одноразовый токен в `POST /api/bookings`. **Для продакшена нужно задать серверный `SmartCaptcha:SecretKey` и клиентский `VITE_SMARTCAPTCHA_SITEKEY` (ключи из Yandex Cloud SmartCaptcha).**

3. **`GET /api/workinghours` не проверяет владение при чтении** — любой аутентифицированный пользователь может запросить рабочее время произвольного мастера/компании (в отличие от `PUT`/`DELETE` этого же контроллера, где проверка есть, и в отличие от `ScheduleTemplateController`, где такая же проверка теперь тоже есть на всех трёх эндпоинтах — см. п. 3 в списке "Исправлено" ниже). Риск ниже (только чтение), но стоит иметь в виду.

4. **Рассылки (`Mailing`) не отправляют реальные письма.** `POST /api/companies/{id}/mail` лишь подсчитывает получателей и сохраняет запись `MailLog` — интеграции с почтовым провайдером (SMTP/SendGrid и т.п.) в коде нет.

5. ~~Выручка/отчёты по мастерам считались через join на текущую `Service.Price`~~ — исправлено, см. п. 5 в списке "Исправлено" ниже (`Booking.Price` теперь хранит цену на момент создания записи).

6. ~~Смена ролей не применяется мгновенно~~ — исправлено, см. п. 6 в списке "Исправлено" ниже.

7. **Автосозданный пароль мастера нигде не отправляется пользователю.** При автоматическом создании аккаунта в `POST /api/companies/{id}/members` сгенерированный пароль возвращается только в виде самого факта создания аккаунта (сам пароль в ответе API не фигурирует, а email с паролем не отправляется — п. 4 выше). Договорённость о передаче пароля новому мастеру целиком лежит на владельце компании (не автоматизирована).

### Исправлено

Эти пункты были найдены в ходе первого прохода по коду и с тех пор устранены (код, тесты и этот документ обновлены):

1. **Пробел в авторизации `ScheduleTemplateController`** — все три эндпоинта (`GET`/`PUT /api/schedule-template`, `POST /api/schedule-template/apply`) раньше требовали только `[Authorize]`, без проверки принадлежности. Любой залогиненный пользователь мог прочитать или полностью перезаписать чужой шаблон расписания. Добавлена проверка `CanManage` (тот же паттерн, что и в `WorkingHoursController`): доступ имеет сам мастер, владелец компании или SuperAdmin — остальным `403 Forbidden`. См. §4.6.
2. **`PUT /api/admin/users/{id}/roles` не проверял результат `IdentityResult`** — при неизвестном имени роли `AddToRolesAsync` бросало необработанное `InvalidOperationException` (500), причём **после** того, как текущие роли пользователя уже были удалены, то есть неудачный запрос мог оставить пользователя без единой роли. Теперь имена ролей проверяются через `RoleManager.RoleExistsAsync` до какого-либо изменения, а результаты `RemoveFromRolesAsync`/`AddToRolesAsync` проверяются явно — `400 Bad Request` вместо 500, без побочных эффектов при отказе. См. §4.12.
3. **Отзывы не валидировали диапазон рейтинга** — `rating` в `CreateReviewRequest` принимал любое целое число. Теперь значения вне диапазона 1–5 отклоняются с `400 Bad Request`. См. §4.8.
4. **Гонка (TOCTOU) при create/count-then-act проверках** — в нескольких местах подсчёт текущего состояния и последующая запись выполнялись без транзакции и без блокировки, так что два параллельных запроса могли оба пройти проверку и оба записать, обходя ожидаемое ограничение:
   - **Создание и перенос записи** — проверка свободного слота (`AnyAsync`) и вставка/обновление записи могли пропустить двойное бронирование одного слота у одного мастера, несмотря на ожидаемый `409 Conflict`.
   - **Лимиты тарифа `MaxEmployees`/`MaxCompanies`** — подсчёт участников компании (`POST /api/companies/{id}/members`) и подсчёт компаний владельца (`POST /api/companies`) могли пропустить создание сверх лимита, несмотря на ожидаемый `402 Payment Required`.

   Во всех случаях теперь используется общий приём: транзакция БД плюс Postgres advisory lock (`pg_advisory_xact_lock`) по ключу конкретного ограничения (мастер+дата для слота; владелец для лимита компаний; компания для лимита сотрудников) перед проверкой — конкурентные запросы на одно и то же ограничение сериализуются, и лимит/слот не может быть превышен. См. `Services/AdvisoryLock.cs`, `BookingsController.Create`/`Reschedule`, `CompaniesController.Create`/`AddMember`.
5. **Выручка и комиссии считались по текущей цене услуги, а не по цене на момент записи** — `Booking` не хранил цену, а отчёты (`GET /api/reports/masters`, `GET /api/companies/{id}/stats`, `GET /api/admin/stats`, `GET /api/admin/bookings`) джойнили на `Service.Price` в момент запроса отчёта. Если владелец менял цену услуги позже, суммы по уже завершённым и оплаченным записям задним числом менялись. Добавлено поле `Booking.Price` — снимок цены услуги на момент создания записи; все перечисленные эндпоинты теперь читают его вместо `Service.Price`. Существующие записи забэкфиллены текущей ценой их услуги в миграции `AddBookingPriceSnapshot`.
6. **Роли, "запечённые" в JWT, не отражали смену ролей до повторного входа** — после `PUT /api/admin/users/{id}/roles` или добавления в компанию через `POST /api/companies/{id}/members` старый токен продолжал действовать со старыми ролями вплоть до истечения (до 7 дней) — включая случай **отзыва** роли (например, разжалованный SuperAdmin сохранял доступ). Теперь JWT-аутентификация на каждом запросе (`OnTokenValidated` в `Program.cs`) перечитывает актуальные роли пользователя из базы через `UserManager.GetRolesAsync` и подменяет ими claim'ы ролей в `ClaimsPrincipal` — авторизация всегда отражает текущее состояние БД, повторный вход для этого больше не требуется (хотя сам JWT по-прежнему содержит роли на момент выпуска — просто они больше не единственный источник истины при проверке `[Authorize(Roles=...)]`).
7. **Промокоды и подарочные сертификаты удалены из проекта** — обе функции (`PromoCode`/`GiftCertificate`, контроллеры, таблицы БД, флаг `AllowPromoCodes` у тарифа) были вырезаны целиком как невостребованный функционал, а не оставлены в недоработанном виде.
