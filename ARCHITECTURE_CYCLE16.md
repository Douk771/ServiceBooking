# ARCHITECTURE — цикл 16 ServiceBooking: разбор технического долга (§240–§265)

**Дата: 2026-09-25. Ветка цикла: `cycle/016-tech-debt-cleanup` (создана devops-инженером).**
**Вход:** `SPEC_CYCLE16_TECH_DEBT.md` (688 строк) + `CURRENT_STATE.md` §9 на `9729beb`.
**Выход:** этот файл, `API_CONTRACT_CYCLE16.md` (§270–§284), `contracts/cycle16/openapi.yaml`
(**восьмой** машиночитаемый контракт в репозитории).

⚠️ **Имена файлов цикла — с суффиксом `_CYCLE16_`/`CYCLE16`.** Корневые `SPEC.md` (цикл 13),
`ARCHITECTURE.md`, `API_CONTRACT.md` цикл 16 **не перезаписывает** — это ровно процессный дефект §9 C5,
названный устойчивым. Правки в корневые файлы допускаются только точечные (см. §264).

---

## §240. Рамки цикла и что в нём НЕ проектируется

### §240.1. Это расширение существующей системы, а не новый проект

Стек, структура папок, конвенции и модель данных **остаются прежними** и здесь не пересматриваются:

| Слой | Что есть сегодня | Меняет ли цикл 16 |
|---|---|---|
| Backend | ASP.NET Core 8, EF Core 8, PostgreSQL 16, Identity + JWT | нет |
| Проекты | `ServiceBooking.Core` (сущности/enum) · `.Infrastructure` (EF, миграции) · `.API` (контроллеры, сервисы) · `.TestKit` · `.Tests` (функциональные) · `.UnitTests` · `.LegalKit` | нет |
| Frontend | React 18 + Vite 5 + TypeScript 5 + TanStack Query 5 + Zustand 4 + Tailwind 3 | нет |
| Контракт | REST/JSON, OpenAPI 3.0 в `contracts/cycleNN/openapi.yaml`, типы фронта — `openapi-typescript` | нет (добавляется `cycle16` + сверка в CI) |
| Фоновые работы | `IScheduledTask` + `ScheduledTaskRunner`, состояние в `ScheduledTaskStates` | нет (одно аддитивное поле, §246.3) |
| Правила уничтожения | `IRetentionRule` + `RetentionRuleRunner` + `DataRetentionTask` | нет (изоляция ошибок, §246) |
| CI | GitHub Actions, три джоба: `backend`, `frontend`, `docker-build` | да — добавляются шаги (§248), новых джобов нет |

**Обоснование «ничего не менять».** Цикл разбирает долг; НФТ §7.1 спеки прямо запрещает новое поведение,
кроме TD-03. Любая замена технологии здесь стоила бы дороже всего разбираемого долга и сделала бы
регрессию недоказуемой. Единственная новая внешняя зависимость за цикл — `@redocly/cli` в `devDependencies`
фронтенда (§248.3), и она обоснована отдельно.

### §240.2. Чего этот документ не делает

- **Не сочиняет правовых формулировок.** Тексты отказа и объяснения для пользователя (TD-03) пишет
  `legal-counsel`; архитектура задаёт только **место**, откуда текст берётся (§245.6), и требует, чтобы до
  появления текста работал нейтральный fallback.
- **Не проектирует AV1** (п. 9.8 политики / рубильник геокодера). Вопрос открыт у заказчика; цикл 16 не
  трогает `ADDRESSVERIFICATION__PROVIDER` ни в коде, ни в конфигурации, и **не считает пункт закрытым**.
- **Не трогает TD-01** (красный CI, teardown `RateLimitingTests`) — чинится параллельной сессией.
  Координация, а не проектирование: см. §263.2.
- **Не трогает цикл 15** (карточка бронирования, тарифы) — см. §263.1.

### §240.3. 🔴 Жёсткое ограничение цикла: миграции

Решение заказчика §0-bis п. 3: **ломающие и необратимые миграции запрещены**. Боевые данные близко.

Архитектурный вывод, действующий для всех шести обязательных историй:

> **Цикл 16 не добавляет НИ ОДНОЙ миграции EF Core.**

Это не совпадение, а проектное требование: каждая история ниже решена так, чтобы обойтись без изменения
схемы и без переписывания накопленных строк. Очередь невыкаченных миграций остаётся **22**, а не 23+.
Если разработчик обнаружит, что задача не решается без миграции, — он **останавливается и возвращается к
архитектору**, а не пишет миграцию «по умолчанию» (это прямое следствие R5).

Исключение одно и оно не миграция: `DeleteAccount` пишет в уже существующие колонки в рамках своей
транзакции по запросу самого субъекта (§247.2). Это операция продукта, а не миграция данных.

---

## §241. Ответы на вопросы §8 спеки (R1, R3, R4, R6, R7, R8)

| № | Ответ (кратко) | Где подробно |
|---|---|---|
| **R1** | Гейт — **в одном месте**: резолвер `SubjectScopeResolver` + структурный сторож в тестах, запрещающий появление необъявленного места сопоставления. Точечных проверок в контроллерах нет. **Мест сопоставления не три, а пять** — два из них спека не называет (§245.2). | §245 |
| **R3** | Сверка контрактов: **генерируемые типы — все 6 файлов** (дёшево и объективно), **линт контракта — 4 спеки** (`cycle8` инвариант, `cycle13`, `cycle14`, новый `cycle16`). Остальные 5 — следующим циклом, поимённо. | §248.3 |
| **R4** | Мажор `react-router` v6→v7 **выносится из цикла**. В цикле закрываются две `high` (`axios`, `form-data`) внутри 1.x. `moderate` остаётся **одной** записью реестра с оценкой объёма. | §249 |
| **R5** | ~~закрыт заказчиком~~ → форма TD-05: **запись при удалении + подстановка на чтении**, миграции нет. | §247 |
| **R6** | Цикл 16 не добавляет миграций (§240.3), очередь остаётся 22. Предпроверка перед мерджем — §263.3. | §240.3, §263.3 |
| **R7** | Документы цикла — в корне, с суффиксом `_CYCLE16_`. `docs/history/` цикл **не заводит** (это отдельная работа с переносом ссылок 33 файлов). Конвенция дописывается в `CURRENT_STATE.md` §10.5 state-analyst'ом. | §264 |
| **R8** | Порядок: TD-04 → TD-17 (сухой прогон). Жёсткая зависимость, зафиксирована в плане работ. | §262 |

---

## §242. Что цикл меняет — карта по файлам

Ни один файл ниже не относится к карточке бронирования и тарифам (пересечение с циклом 15 —
см. §263.1).

**Backend (`ServiceBooking.API`)**
```
Services/Subjects/SubjectScope.cs                    НОВЫЙ  (TD-03)
Services/Subjects/SubjectScopeResolver.cs            НОВЫЙ  (TD-03)
Services/Subjects/SubjectGateTexts.cs                НОВЫЙ  (TD-03, fallback-тексты)
Controllers/ProfileController.cs                     правка (TD-03 ×4 метода, TD-05 write-side)
Controllers/BookingsController.cs                    правка (TD-05 read-side, GetHistory)
Controllers/MailingController.cs                     правка (TD-09 валидация, TD-11)
Controllers/{Companies,Services,CompanyAddress,CompanyNotifications}Controller.cs
                                                     правка (TD-11, только CanManageCompany*)
Services/CompanyAccess.cs                            НОВЫЙ  (TD-11)
Services/Scheduling/IScheduledTask.cs                правка (TD-04, аддитивное поле Error)
Services/Scheduling/ScheduledTaskRunner.cs           правка (TD-04, 3 строки)
Services/Scheduling/Tasks/DataRetentionTask.cs       правка (TD-04, изоляция правил)
Services/Retention/IRetentionRule.cs                 правка (TD-04, аддитивное поле Skipped)
Services/Retention/Rules/*.cs                        правка (TD-04, только те, что рано выходят)
Services/Billing/CompanyTransferService.cs           правка (TD-10)
DeploymentSafetyChecks.cs                            правка (TD-08)
DTOs/Profile/ProfileExportDto.cs                     правка (TD-03, одна аддитивная секция)
```

**Тесты**
```
ServiceBooking.Tests/Infrastructure/LegalDocumentsTestFactory.cs   правка (TD-02, ReloadLegalNow)
ServiceBooking.Tests/Tests/LegalConsentVersionChangeTests.cs       правка (TD-02, −11 Task.Delay)
ServiceBooking.Tests/Tests/LegalPricingGateTests.cs                правка (TD-02, −7 Task.Delay)
ServiceBooking.Tests/Tests/GuestDataGateTests.cs                   НОВЫЙ  (TD-03)
ServiceBooking.Tests/Tests/BookingHistoryAnonymizationTests.cs     НОВЫЙ  (TD-05)
ServiceBooking.Tests/Tests/RetentionRuleIsolationTests.cs          НОВЫЙ  (TD-04)
ServiceBooking.UnitTests/SubjectPhoneGateInvariantTests.cs         НОВЫЙ  (TD-03, структурный сторож)
ServiceBooking.UnitTests/DeploymentSafetyChecksTests.cs            правка (TD-08, случай Staging)
```

**Frontend**
```
frontend/package.json                       правка (TD-06 версии, TD-07 скрипты + @redocly/cli)
frontend/src/hooks/useExportData.ts         правка (TD-03, признак гейта)
frontend/src/components/profile/GuestDataGateNotice.tsx   НОВЫЙ (TD-03)
frontend/src/pages/ProfilePage.tsx          правка (TD-03, врезка)
frontend/src/pages/DeleteAccountPage.tsx    правка (TD-03, врезка)
frontend/src/types/api-cycle16.generated.ts НОВЫЙ (генерируется)
```

**Инфраструктура и документы**
```
.github/workflows/ci.yml                    правка (TD-06 audit, TD-07 две сверки)
contracts/cycle16/openapi.yaml              НОВЫЙ
contracts/redocly.yaml                      НОВЫЙ (конфигурация линта)
API_DOCUMENTATION.md                        правка (TD-12) — backend-developer
DEPLOY.md                                   правка (TD-13, раздел 18) — devops-engineer
DEPLOY-windows.md, frontend/public/web.config  правка/удаление (TD-14) — devops-engineer
docs/testing-isolation.md                   правка (TD-15) — devops-engineer
docs/subject-rights.md                      НОВЫЙ (TD-03, компенсирующий путь)
```

---

## §243. Модель данных

**Цикл не меняет модель данных.** Ни одной новой сущности, ни одной новой колонки, ни одной миграции
(§240.3). Ниже — только те существующие сущности, чьё *поведение* затрагивается, и инварианты, которые
цикл обязан сохранить.

### §243.1. Сущности, вокруг которых работает TD-03

```
AppUser
  Id              string   PK (Identity)
  PhoneNumber     string?  канонический (PhoneNormalizer), одновременно UserName
  PhoneNumberConfirmed bool ЗЕРКАЛО, не источник истины
  DeletedAtUtc    DateTime? надгробие (строка не удаляется)

VerifiedPhone                       ← ИСТОЧНИК ИСТИНЫ «номер подтверждён»
  UserId  string  FK AppUser
  Phone   string  канонический
  Method  enum (MaxBot)
  VerifiedAtUtc DateTime

Booking            .ClientId string? | .GuestPhone string?   ← сшивание «сущность ↔ телефон»
ClientNote         .ClientId string? | .GuestPhone string?   ← сшивание
ClientNotePhoto    → через ClientNote                        ← сшивание
ClientHealthNote   .ClientId string? | .GuestPhone string?   ← сшивание, СПЕЦКАТЕГОРИЯ
NotificationOptOut .Phone    string  (только телефон)        ← сшивание
OutboundNotification .RecipientUserId string? | .RecipientPhone string?  ← сшивание
PhoneVerificationSession .UserId string? | .CanonicalPhone string        ← сшивание
```

🔴 **Инвариант TD-03 (новый, центральный для цикла):**

> Ветка сопоставления **по строке телефона** применяется в контексте аккаунта тогда и только тогда, когда
> в `VerifiedPhones` есть строка `(UserId = текущий аккаунт, Phone = текущий `AppUser.PhoneNumber`)`.
> Ветка по `ClientId`/`UserId`/`RecipientUserId` **не гейтится никогда**.

**Почему источник истины — `VerifiedPhone`, а не зеркало `PhoneNumberConfirmed`.** Зеркало пишется
`PhoneVerificationWriter`, но исторически могло разойтись (в дереве есть миграция
`20260924074741_ResetUnverifiedPhoneNumberConfirmedMirror` — то есть расхождение уже случалось однажды).
Решение доступа к спецкатегориям ПДн не может опираться на производное поле. Цена — один дешёвый
индексированный `EXISTS` на вызов в четырёх редких методах; `ProfileController.Export` и так делает
полтора десятка запросов. Зеркало остаётся тем, чем было: полем для отображения (`ProfileDto.phoneVerified`,
`MasterClientDto.phoneVerified`) — это **не гейт** (R15 цикла 14 в силе).

### §243.2. Сущности TD-04/TD-05

```
ScheduledTaskState   Name PK · LastStartedAtUtc · LastFinishedAtUtc · LastDurationMs
                     · LastSucceeded bool · LastSummary string? · LastError string?
BookingEvent         Id · BookingId · CompanyId · Kind · OccurredAtUtc
                     · ActorKind (Client|Guest|Staff|SuperAdmin|System)
                     · ActorUserId string? · ActorNameSnapshot string?(200) · ActorRoleSnapshot
```

Колонок хватает для обеих историй: `LastSucceeded`/`LastError` уже есть (TD-04 наконец начинает ими
пользоваться), `ActorUserId` + `AppUser.DeletedAtUtc` дают TD-05 путь на чтении без backfill.

---

## §244. Конвенции API, действующие без изменений

Повторено здесь, потому что TD-12 (документация) и TD-07 (сверка) обязаны описывать именно это:

1. JSON — `camelCase`; enum — строками (имена членов C#, PascalCase); `DateTime` — ISO-8601 UTC;
   `DateOnly` — `YYYY-MM-DD`; `TimeOnly` — `HH:mm:ss`.
2. Тела осознанных ошибок **400/409/429** — **голая строка** (`text/plain`) по-русски.
3. **401 и 403 — пустое тело.** `ProblemDetails` не используется.
4. 🔴 **404 и 409 без явного тела — ПУСТОЕ тело у всех маршрутов всех циклов** (`Program.cs:170`,
   `SuppressMapClientErrors = true`). Это и есть AV8: поведение сменилось у всего API сразу, а
   `API_DOCUMENTATION.md` об этом не знает. TD-12 обязан сказать это **как общее правило**, а не как
   особенность цикла 13.
5. 451 — «требуется принять новую редакцию документов» (гейт `LegalConsentFilter`).
   `GET /api/profile/export` — в allow-list гейта и остаётся в нём (право на копию своих данных не может
   зависеть от принятия новой редакции).
6. 402 — тарифный гейт; в маршрутах этого цикла не появляется.

---

## §245. TD-03 — полный гейт сшивания гостевых сущностей (главная история цикла)

*Покрывает V1 / §9.10. Решение заказчика: ПОЛНАЯ форма. Узкий гейт отклонён.*

### §245.1. Угроза, дословно

Посторонний регистрирует аккаунт на чужой номер (регистрация владения номером не проверяет — решение Р1
неизменно). Дальше `ProfileController.Export` отдаёт ему чужие визиты, карту салонов и **расшифрованные
сведения о здоровье**, а `ProfileController.DeleteAccount` — **физически уничтожает** их.
Цикл 16 закрывает **доступ**, а не регистрацию.

### §245.2. 🔴 Где сопоставление живёт на самом деле — мест пять, а не три

Спека называет три места (`ProfileController` ×2, `MastersController`). Перепроверка по коду на `9729beb`
даёт другую картину — и именно она объясняет, почему точечный гейт брать нельзя:

| # | Место | Что делает по телефону | Гейт | Риск сегодня |
|---|---|---|---|---|
| 1 | `ProfileController.Export` (`:81–:187`) | читает bookings, notes, photos, **healthNotes**, operators, opt-out, verifiedPhone | **ДА** | чтение чужих спецкатегорий |
| 2 | `ProfileController.DeleteAccount` (`:378–:477`) | `RemoveRange` notes/healthNotes/verifiedPhones/sessions, анонимизация bookings и notifications | **ДА** | **уничтожение** чужих спецкатегорий |
| 3 | `ProfileController.RevokeSalonConsentAsync` (`:858–:897`) | по `phone` аккаунта **удаляет** `ClientNotePhotos` и `ClientHealthNotes` одной компании | **ДА** | ⚠️ **спекой не назван**; уничтожение чужих спецкатегорий |
| 4 | `ProfileController.ApplyOrPreviewRevokeEffectsAsync` (`:933–:976`) | по `canonicalPhone` **удаляет** `ClientHealthNotes` во всех компаниях + `revoke-preview` показывает их количество | **ДА** | ⚠️ **спекой не назван**; уничтожение + оракул количества |
| 5 | `MastersController.GetClients` (`:87–:139`) | группирует записи компании: `ClientId != null` и `ClientId == null && GuestPhone != null` — **двумя непересекающимися ветками** | **НЕТ** | не применимо, см. §245.3 |
| 6 | `ClientConsentsController.ResolveClientAsync` (`:43–:73`) | ищет клиента компании по `clientKey` (телефон или userId) | **НЕТ** | не применимо, см. §245.3 |

Четвёртое и пятое места (№ 3 и № 4 в таблице) — ровно тот риск, о котором предупреждает R1: они
существуют **уже сегодня** и точечная правка двух методов их бы не закрыла. Оба разрушительны:
`revoke-preview` вдобавок сообщает неподтверждённому аккаунту **число** чужих заметок о здоровье.

### §245.3. Почему `MastersController.GetClients` и `ClientConsentsController` гейт НЕ получают

Требование спеки — «приведено к тому же правилу либо письменно объяснено, почему там гейт не нужен».
Объяснение:

- **Там нет сшивания.** `GetClients` не склеивает гостевую личность с аккаунтом: зарегистрированные
  клиенты группируются по `ClientId`, гости — по `GuestPhone`, ветки непересекающиеся. Один и тот же
  человек видится персоналу как две строки — это существующее поведение, и цикл его не меняет.
- **Нет «телефона вызывающего».** Угроза V1 — это «мой неподтверждённый номер открывает мне чужие
  гостевые строки». В обоих местах вызывающий — **персонал компании**, а данные — операционные данные
  **этой же компании** о человеке, который у неё записывался. Оператором этих данных является сама
  компания (так и сказано в тексте выгрузки, `ProfileController.cs:200`).
- **Доступ уже ограничен по-другому:** `IsStaffAsync(companyId)` + «есть запись в этой компании».
- Если завтра `GetClients` начнёт объединять две ветки в одну строку «это один и тот же человек» —
  **это станет шестым местом и потребует гейта**. Чтобы это не прошло незамеченным, оба места
  получают маркер-комментарий и покрываются сторожем §245.5.

### §245.4. Решение: один резолвер, ноль точечных проверок

**Новый файл `ServiceBooking.API/Services/Subjects/SubjectScope.cs`:**

```csharp
/// Область «данные этого субъекта» для account-scoped запросов.
/// GuestMatchPhone == null означает: ветка сопоставления по строке телефона НЕ ПРИМЕНЯЕТСЯ ВОВСЕ.
public readonly record struct SubjectScope(string UserId, string? OwnPhone, string? GuestMatchPhone)
{
    public bool GateApplied => OwnPhone is not null && GuestMatchPhone is null;
}
```

**`SubjectScopeResolver`** (scoped, DI): `Task<SubjectScope> ForAccountAsync(AppUser user, CancellationToken ct)`
— один `EXISTS` по `VerifiedPhones (UserId, Phone)`; при попадании `GuestMatchPhone = user.PhoneNumber`,
иначе `null`.

**Почему это самая дешёвая правка из возможных.** Все пятнадцать предикатов уже написаны в форме
`x.ClientId == userId || (canonicalPhone != null && x.GuestPhone == canonicalPhone)` — они **сами
вырождаются в безопасную ветку**, когда телефон `null`. Значит правка в каждом из четырёх методов — это
замена одного локального присваивания на два именованных:

```csharp
// было
var canonicalPhone = user.PhoneNumber;

// стало
var scope = await subjects.ForAccountAsync(user, ct);
var ownPhone       = scope.OwnPhone;        // строки САМОГО аккаунта (его контакт)
var guestMatchPhone = scope.GuestMatchPhone; // ветка сопоставления по телефону; null = гейт закрыт
```

🔴 **Правило распределения (разработчик обязан пройти по каждому предикату руками, а не заменить
переменную глобально):**

| Сравнение | Какой переменной | Почему |
|---|---|---|
| `Booking.GuestPhone ==` | `guestMatchPhone` | гостевая сущность |
| `ClientNote.GuestPhone ==` | `guestMatchPhone` | гостевая сущность |
| `ClientNotePhoto → ClientNote.GuestPhone ==` | `guestMatchPhone` | гостевая сущность |
| `ClientHealthNote.GuestPhone ==` | `guestMatchPhone` | 🔴 спецкатегория |
| `NotificationOptOut.Phone ==` | `guestMatchPhone` | строка найдена ТОЛЬКО по телефону |
| `OutboundNotification.RecipientPhone ==` | `guestMatchPhone` | строка гостя (`RecipientUserId == null`) |
| `VerifiedPhone.Phone ==` | `guestMatchPhone` | иначе неподтверждённый аккаунт снесёт подтверждение **владельца** номера |
| `PhoneVerificationSession.CanonicalPhone ==` | `guestMatchPhone` | то же |
| `ExportProfileDto(... user.PhoneNumber ...)` | `ownPhone` | собственный контакт аккаунта, показывать обязаны |
| `ConsentLedger.HistoryAsync(subject, knownPhone)` | `guestMatchPhone` | салонные `PhotoConsent`/`HealthDataConsent` ищутся по телефону |
| `VerifiedPhone.UserId ==` / `*.ClientId ==` / `RecipientUserId ==` | **не трогать** | ветка по идентификатору не гейтится никогда |

Единая формулировка правила (её и проверяет сторож): **строка, которую можно найти только по строке
телефона, в контексте аккаунта доступна только при подтверждённом номере.**

### §245.5. Структурный сторож — то, что закрывает «четвёртое место появится незамеченным»

`ServiceBooking.UnitTests/SubjectPhoneGateInvariantTests.cs` — текстовый скан исходников (прецеденты в
проекте есть: шаг CI про `public/sw.js`, `CommittedLegalArtifactTests`):

- Скан `ServiceBooking.API/Controllers/*.cs` и `ServiceBooking.API/Services/**/*.cs` на токены
  `GuestPhone ==`, `RecipientPhone ==`, `.Phone == ` , `CanonicalPhone ==`.
- Каждое вхождение обязано иметь на той же или предыдущей строке маркер
  `// SUBJECT-PHONE-GATE: <одно из: gated | staff-scoped | not-account-scoped> — <причина>`.
- Тест падает с **именем файла и номером строки** и текстом: «новое сопоставление по телефону: пометьте
  маркером или проведите через SubjectScopeResolver, см. ARCHITECTURE_CYCLE16.md §245».
- Базовый список разрешённых вхождений **коммитится вместе с тестом**; добавление нового требует
  осознанного действия, а не проходит молча.

Цена — один юнит-тест без БД (быстрый джоб `Unit tests` в CI и так первый). Выгода — пункт V1 физически
не может вернуться тем же способом.

### §245.6. Компенсирующий путь для пользователя без MAX (обязателен)

Полный гейт отнимает у честного пользователя без MAX доступ к его собственным гостевым данным. Обязанности
цикла:

1. **Выгрузка честно сообщает о гейте.** В `ProfileExportDto` добавляется **одна аддитивная секция**
   `guestDataGate` (форма — `API_CONTRACT_CYCLE16.md` §273):
   `{ applied: bool, reason: "PhoneNotVerified" | null, subjectRequestPath: "/subject-request" }`.
   🔴 Секция **не сообщает, существуют ли гостевые данные** — только что правило применено. Это прямое
   следствие Р3 цикла 14 (эндпоинт не должен становиться оракулом «на этом номере есть записи»).
2. **UI объясняет, почему пусто, и куда идти.** Компонент `GuestDataGateNotice` на `ProfilePage` и
   `DeleteAccountPage`. Условие показа — **только `profile.phoneVerified === false`**, никогда не
   «данные не нашлись»: иначе фронт сам станет оракулом.
3. **Текст пишет `legal-counsel`, а не команда.** Место хранения — `uiTexts` в `legal.json`
   (механизм уже есть: `LegalDocumentProvider`, хук `useLegalText`), ключ `guestDataGateNotice`.
   Это даёт юристу правку текста **без релиза кода** и без миграции.
   До появления ключа работает нейтральный fallback из `SubjectGateTexts` — экран не должен быть пустым:
   *«Эти сведения доступны после подтверждения номера телефона. Если подтвердить номер невозможно,
   направьте обращение субъекта персональных данных — ответ по закону даётся в установленный срок.»*
   Fallback заменяется текстом юриста до мерджа цикла.
4. **Путь через форму обращений проверен end-to-end и описан.** Новый `docs/subject-rights.md`:
   форма `/subject-request` → `POST /api/subject-requests` (анонимный, капча, лимит `subject-request`,
   срок `SubjectRequests:ResponseWorkingDays` = 10 рабочих дней) → `Accepted` с `reference` → обработка в
   `/admin` → вкладка «Обращения субъектов». QA проходит путь целиком и записывает `reference` в отчёт.
   ⚠️ Форма отвечает **байт в байт одинаково** независимо от того, известен ли номер системе
   (`SubjectRequestsController` §50.1) — это свойство **нельзя нарушить**, добавляя «удобные» подсказки.
5. **Число затронутых аккаунтов** — в отчёт цикла. Запрос только на чтение, выполняется devops на боевой
   машине, вывод — одно число:

```sql
SELECT count(*) FROM (
  SELECT u."Id"
  FROM "AspNetUsers" u
  WHERE u."PhoneNumber" IS NOT NULL
    AND u."DeletedAtUtc" IS NULL
    AND NOT EXISTS (SELECT 1 FROM "VerifiedPhones" v
                    WHERE v."UserId" = u."Id" AND v."Phone" = u."PhoneNumber")
    AND (EXISTS (SELECT 1 FROM "Bookings" b          WHERE b."GuestPhone" = u."PhoneNumber")
      OR EXISTS (SELECT 1 FROM "ClientNotes" n       WHERE n."GuestPhone" = u."PhoneNumber")
      OR EXISTS (SELECT 1 FROM "ClientHealthNotes" h WHERE h."GuestPhone" = u."PhoneNumber"))
) t;
```

### §245.7. Журнал попытки

При `scope.GateApplied` каждый из четырёх методов пишет **одну** запись уровня Information:

```
guest-data gate applied: userId={UserId} endpoint={Endpoint}
```

Ни телефона, ни счётчиков, ни названий компаний (НФТ §7.4). Маскирование телефонов цикла 3 остаётся в силе;
здесь телефон просто не логируется вовсе.

### §245.8. Граница истории

- Регистрация на чужой номер **не запрещается** (Р1 неизменно) — закрывается доступ, который она давала.
- Поведение для аккаунта с **подтверждённым** номером — **дословно прежнее**. Это основной предмет
  регрессии: QA прогоняет весь существующий набор `PROF-`/`PHV-` на подтверждённом аккаунте.
- `§9 V1` по итогам цикла **переписывается**, а не помечается закрытым (остаётся остаточный риск:
  регистрация на чужой номер по-прежнему возможна).
- 📄 `legal-counsel` обязателен **до кода**: отказ отдать субъекту его собственные ПДн до подтверждения
  номера должен быть обоснован и описан (ст. 14, ст. 20 152-ФЗ, срок ответа). Механику гейта команда
  проектирует сама; тексты — нет.

---

## §246. TD-04 — изоляция правил уничтожения

*Покрывает N9-6; смежно 24f.*

### §246.1. Дефект

`DataRetentionTask.ExecuteAsync` (`:43–:54`) перечисляет правила в одном `foreach` **без обработки
исключений**. Исключение в раннем правиле (сегодня `BookingEventRule` — девятое из 16) выбрасывается
наружу, и `ScheduledTaskRunner` (`:126–:132`) ловит его так, что:
- последующие правила **не выполняются вовсе** — уничтожение ПДн по всем остальным срокам молча не
  происходит;
- `summary` остаётся `null`, и `SetFinishedAsync` **затирает `LastSummary`** — в
  `GET /api/admin/scheduled-tasks` пропадает даже информация о том, что успело отработать.

### §246.2. Решение

`DataRetentionTask` перестаёт выбрасывать исключение правила наружу:

```
foreach (var rule in rules)
{
    ct.ThrowIfCancellationRequested();          // бюджет времени — по-прежнему наружу
    try {
        var outcome = await rule.ApplyAsync(ctx, ct);
        ... агрегирование, как сегодня
    }
    catch (OperationCanceledException) { throw; }   // бюджет времени/остановка хоста — НЕ гасим
    catch (Exception ex) {
        failed.Add(rule.Name);
        logger.LogError(ex, "retention rule {Rule} failed", rule.Name);
        lines.Add($"retention[{mode}] {rule.Name}: FAILED ({ex.GetType().Name})");
    }
}
```

Сводка прогона явно перечисляет три ведра (требование спеки):
```
retention[dry] ok=13 skipped=2 failed=1 | <строка каждого правила> | failed: booking-event
```
- **ok** — правило отработало;
- **failed** — бросило исключение, имя названо;
- **skipped** — «не настроено» (срок = 0): правило само сообщает это новым **аддитивным** полем.

### §246.3. Два аддитивных поля, ноль ломающих правок

```csharp
// Services/Retention/IRetentionRule.cs
public readonly record struct RetentionOutcome(string Name, int Scanned, int Affected, string Summary)
{
    /// true — правило не настроено (срок хранения 0) и сознательно ничего не делало.
    public bool Skipped { get; init; }
}

// Services/Scheduling/IScheduledTask.cs
public readonly record struct ScheduledTaskOutcome(int Scanned, int Affected, long BytesFreed, string Summary)
{
    /// Непустое значение = прогон завершился с ошибкой, но СВОДКА ВСЁ РАВНО ЕСТЬ.
    public string? Error { get; init; }
}
```

`ScheduledTaskRunner` (3 строки): `succeeded = outcome.Error is null; error = outcome.Error;`
`summary = outcome.Summary;` — сводка сохраняется **и при ошибке**. Оба поля — `init`-свойства поверх
позиционного конструктора: **ни один существующий вызов не правится** (`StaffPushDispatchTask`,
`PhotoRetentionCleanupTask`, `NotificationDispatchTask`, `ChannelHealthTask` — без изменений).

Форма ответа `GET /api/admin/scheduled-tasks` **не меняется** (`ScheduledTaskStatusDto` уже несёт
`lastSucceeded`, `lastSummary`, `lastError`); меняются только значения, и в лучшую сторону.
`RETENTION_DRY_RUN=true` по умолчанию — без изменений.

### §246.4. Тест

`RetentionRuleIsolationTests`: тестовый хост регистрирует `IRetentionRule`-заглушку, бросающую исключение,
между двумя настоящими правилами. Проверяется: следующее правило отработало; в `LastSummary` есть имя
упавшего правила и строки обоих соседей; `LastSucceeded == false`; `LastError` не пуст. Без `Task.Delay` —
задача вызывается напрямую (прецедент `RunStaffPushDispatchPassAsync`, `e1b80e9`).

---

## §247. TD-05 — имя удалённого клиента уходит из журнала записи, без миграции

*Покрывает N9-10. Ограничение R5/§0-bis п. 3: переписывать накопленные строки миграцией НЕЛЬЗЯ.*

### §247.1. Форма решения — два слоя, ноль миграций

| Слой | Что делает | Что покрывает |
|---|---|---|
| **запись** | `DeleteAccount` в своей транзакции заменяет `ActorNameSnapshot` на надгробие | все будущие удаления |
| **чтение** | `GET /api/bookings/{id}/history` подставляет надгробие, если актёр — удалённый аккаунт | **все уже накопленные строки** — без backfill |

Миграции нет ни одной; накопленные строки массово не переписываются.

### §247.2. Слой записи

Внутри существующей транзакции `DeleteAccount`, сразу после шага 3 (анонимизация `bookingsToAnonymize`):

```
надгробие = "Удалённый пользователь"    // ровно та же форма, что уже пишется в Reviews.ReviewerName
                                        // и AppUser.FirstName/LastName ("Удалённый" + "пользователь")

события, где ActorKind == Client && ActorUserId == userId              → ActorNameSnapshot = надгробие
события, где ActorKind == Guest && BookingId ∈ bookingsToAnonymize     → ActorNameSnapshot = надгробие
```

Второе правило берёт **ровно тот набор записей, который гейт TD-03 уже разрешил трогать** — никакой
отдельной логики сопоставления по телефону здесь нет и быть не должно (иначе это стало бы шестым местом
§245.2). Это не миграция: правка строк своего субъекта по его собственному запросу на удаление, той же
природы, что уже существующая анонимизация `Booking`/`OutboundNotification`.

`ActorRoleSnapshot`, `ActorKind`, `ActorUserId`, `OccurredAtUtc`, `Kind` **не трогаются** — журнал остаётся
append-only по составу событий.

### §247.3. Слой чтения

`BookingsController.GetHistory` — один дополнительный запрос на вызов (идентификаторов ≤ числа событий
одной записи):

```
deletedActorIds = db.Users.Where(u => actorIds.Contains(u.Id) && u.DeletedAtUtc != null).Select(u => u.Id)
```
При построении `BookingEventActorDto`: если `ActorKind == Client && ActorUserId ∈ deletedActorIds` →
имя и `label` строятся из надгробия, независимо от того, что лежит в колонке.
`BookingEventTexts.ActorLabel` получает уже подменённое значение — правится вызов, не сама функция.

Это закрывает строки, накопленные **до** цикла, и защищает от появления нового пути чтения журнала.

### §247.4. ФИО сотрудников не трогаются

`ActorKind ∈ { Staff, SuperAdmin, System }` — **вне обеих правок**. Это другой субъект и другой срок
хранения (D1/TD-18); конкретное число даёт `legal-counsel`, а не команда.

### §247.5. Остаточный риск — назвать честно, не закрывать молча

Гостевые события (`ActorKind == Guest`, `ActorUserId == null`), оставшиеся от аккаунтов, **удалённых до
этого цикла**, слоем чтения не находятся: связи с пользователем у них нет. Найти их можно только
переписывающей миграцией по телефону — что запрещено §0-bis п. 3. Поэтому:
- в отчёте цикла считается их число (SQL: события `ActorKind = 1` у записей с `ClientDeleted = true`);
- в `CURRENT_STATE.md` §9 N9-10 остаётся **одна** переформулированная запись с этим остатком и указанием,
  что закрывается он отдельным решением заказчика о разовой правке данных.

### §247.6. Тест

`BookingHistoryAnonymizationTests`: клиент создаёт и переносит запись → персонал видит его имя в
`/history` → клиент удаляет аккаунт → персонал видит надгробие; в той же записи имя мастера **осталось**.
Второй кейс — строка, записанная напрямую в БД с именем и `ActorUserId` уже удалённого аккаунта (имитация
до-цикловых данных): чтение отдаёт надгробие **без** правки строки (проверить, что колонка в БД не
изменилась — это доказательство отсутствия backfill).

---

## §248. TD-07 — CI ловит расхождение контракта (и TD-06 попутно)

*Покрывает T8-5, V6, L6. Ответ на R3.*

### §248.1. Дефект

В `ci.yml` (прочитан целиком) нет ни регенерации типов, ни линта контрактов. Семь спек в `contracts/**` и
шесть закоммиченных `src/types/api-cycle*.generated.ts` не сверяет никто. Этим классом дефекта вкладка
«Тарифы» была сломана с цикла 7 по цикл 15.

### §248.2. Сверка генерируемых типов — полный объём (все 6 файлов)

Новый шаг в джобе `frontend`, сразу после `npm ci`, **до** линта:

```yaml
- name: API types must match committed contracts (TD-07)
  run: |
    npm run types:api && npm run types:api9 \
      && npm run types:api:cycle10 && npm run types:api:cycle11 \
      && npm run types:api:cycle13 && npm run types:api:cycle14 \
      && npm run types:api:cycle16
    git diff --exit-code -- src/types \
      || { echo "::error::src/types/*.generated.ts разошлись с contracts/**/openapi.yaml. Запустите npm run types:api* и закоммитьте результат (ARCHITECTURE_CYCLE16.md §248.2)"; exit 1; }
```

Полный объём здесь безопасен: файлы уже закоммичены и генерируются детерминированно одной и той же
версией `openapi-typescript` (`^7.13.0` зафиксирована в `package-lock.json`). Если шаг покажет
предсуществующее расхождение — это **находка цикла**, её чинят регенерацией и записывают в отчёт.

Новый скрипт в `frontend/package.json`:
`"types:api:cycle16": "openapi-typescript ../contracts/cycle16/openapi.yaml -o src/types/api-cycle16.generated.ts"`.

### §248.3. Линт контрактов — объём выбран явно (ответ на R3)

Включать все восемь спек сразу нельзя: почти наверняка красный CI на предсуществующих расхождениях, и
первый же «срочный» PR шаг отключит. Объём цикла 16:

| Спека | В цикле 16 | Почему |
|---|---|---|
| `contracts/cycle8/servicebooking-invariant.openapi.yaml` | ✅ | это и есть T8-5 дословно |
| `contracts/cycle13/openapi.yaml` | ✅ | самый свежий крупный контракт |
| `contracts/cycle14/openapi.yaml` | ✅ | описывает включённую в бою подсистему |
| `contracts/cycle16/openapi.yaml` | ✅ | свой контракт линтуется с рождения |
| `openapi-cycle6.yaml` (корень), `cycle7`, `cycle9`, `cycle10`, `cycle11` | ⛔ следующий цикл | старые спеки, вероятны накопленные расхождения; включение — отдельная работа с починкой |

🔴 **Записано явно, чтобы следующий цикл не считал, что проверено всё: пять спек НЕ линтуются.**

Инструмент — `@redocly/cli` (dev-зависимость фронтенда, версия пинуется), конфигурация —
новый `contracts/redocly.yaml` с `extends: [minimal]` и явным перечнем поднятых правил. Почему он:
локальный (без сети), понимает OpenAPI 3.0, уже назван в шапке `contracts/cycle14/openapi.yaml` как
ожидаемый инструмент, и как dev-зависимость **не влияет** на `npm audit --omit=dev` из TD-06.

```yaml
- name: Lint API contracts (TD-07, объём — ARCHITECTURE_CYCLE16.md §248.3)
  run: npx @redocly/cli lint --config ../contracts/redocly.yaml
         ../contracts/cycle8/servicebooking-invariant.openapi.yaml
         ../contracts/cycle13/openapi.yaml
         ../contracts/cycle14/openapi.yaml
         ../contracts/cycle16/openapi.yaml
```

### §248.4. Проверка «от обратного» — обязательна

QA один раз вносит намеренное расхождение (лишнее поле в одном `*.generated.ts` и битая ссылка `$ref` в
одной спеке), убеждается, что CI краснеет с понятным сообщением, откатывает. Результат — в отчёт цикла со
ссылкой на номер прогона. Без этого шаг считается непроверенным.

### §248.5. Инструмент QA поверх схемы

Машиночитаемая схема — источник истины для автоматической проверки «backend и frontend реально сошлись»:
```
schemathesis run contracts/cycle16/openapi.yaml --base-url http://localhost:5000/api --checks all
npx @stoplight/prism mock contracts/cycle16/openapi.yaml --port 4010   # фронт работает без бэкенда
```
Это не заменяет функциональные тесты и не добавляется в CI в этом цикле (нужен поднятый стенд) — это
инструмент приёмки QA.

---

## §249. TD-06 — уязвимости зависимостей фронтенда (ответ на R4)

- `axios ^1.7.7` → последняя `1.x` (`^1.12.x` на момент написания); `form-data` подтягивается транзитивно
  через `axios` и закрывается тем же обновлением. Обе `high` уходят.
- 🔴 **`react-router`/`react-router-dom` v6→v7 в цикл НЕ берётся.** Мажор трогает все маршруты и `App.tsx`
  с гейтом согласий (`ConsentGate`, `LegalUpdateBanner`, `OwnerTermsGateModal`) — это отдельная работа с
  собственной приёмкой, а не пункт гигиены. Если `moderate` (open redirect ×2) не закрывается внутри 6.x,
  в отчёте фиксируется оценка объёма мажора, и пункт остаётся **одной** записью реестра вместо пяти.
- Порог CI выбран под это решение — **`--audit-level=high`**, `moderate` не блокирует:

```yaml
- name: npm audit (production deps only, high and above)
  run: npm audit --omit=dev --audit-level=high
```

Шаг ставится **после** `npm ci` и **до** `npm run build`, чтобы падение было дешёвым.
После обновления обязаны быть зелёными: `npm run lint`, `npx tsc --noEmit`, `npm run test:run`,
`npm run build`.

---

## §250. TD-02 — тесты перестают ждать по стенным часам

*Покрывает T8-1, AV3, V3.*

### §250.1. Причина, а не симптом

18 из 30 `Task.Delay` в наборе — это ожидание **одного и того же** механизма: `LegalDocumentProvider`
перечитывает манифест не чаще `Legal:ReloadSeconds` (в тестах 1 с), поэтому тесты спят 1200 мс
(`LegalConsentVersionChangeTests`, 11 штук) и 2500 мс (`LegalPricingGateTests`, 7 штук) — около 30 секунд
чистого сна на прогон и ровно тот флейк, что ловится под нагрузкой.

### §250.2. Решение — детерминированный вызов, без правки продукта

У `LegalDocumentProvider` **уже есть** публичный `LoadAtStartup()` (`:98`), который берёт замок и
перечитывает манифест мимо окна кэша (используется fail-fast'ом `Program.cs`). Тестам нужен только доступ
к нему:

```
LegalDocumentsTestFactory.ReloadLegalNow():
   1) выставить файлам манифеста и контента ЗАВЕДОМО РАЗНЫЕ mtime
      (File.SetLastWriteTimeUtc(path, baseline + (++_tick) * 1s))
   2) Services.GetRequiredService<LegalDocumentProvider>().LoadAtStartup()
```

Шаг 1 обязателен: `TryReload` (`:152`) сравнивает `max(mtime)` с предыдущим и **молча выходит**, если
значение совпало; на файловой системе с грубой гранулярностью два write подряд дали бы одинаковый mtime и
тест стал бы зелёным по ложной причине. Явно выставленный mtime делает переход детерминированным.

Каждый `await Task.Delay(1200/2500)` заменяется на `_factory.ReloadLegalNow()` сразу после
`WriteManifest(...)`/`ResetToDefault()`. Продукт не меняется ни строкой — это ровно приём `e1b80e9`
(`disableAutomaticTicking` + `RunStaffPushDispatchPassAsync`), применённый к другому механизму.

### §250.3. Приёмка

- `Task.Delay` в `ServiceBooking.Tests`: **30 → не более 12**, число названо в отчёте.
- Для каждого оставшегося записано, почему он остаётся (кандидаты на «остаётся»: `NTF-D01`/`NTF-D02`,
  сканирующие `Pending` по всей платформе, и ожидания в `SchedulerTests`, где предмет теста — сам таймер).
- Два одновременных прогона на одной машине (`git worktree`) — оба зелёные.
- Регрессионная ценность не ослаблена: утверждения о версии/статусе/причине остались точными; ни один
  `Should()` не смягчён.
- Время полного прогона **не деградирует** (ожидаемо −25…30 с).

⚠️ **Координация:** параллельная сессия чинит `RateLimitingTests`/`ServiceBooking.TestKit`. Файлы TD-02
(`LegalDocumentsTestFactory`, два `Legal*Tests`) с ними не пересекаются — если пересечение всё же
возникнет, разводится **до** мерджа (§263.2).

---

## §251–§255. Желательные истории

### §251. TD-08 — проверки конфигурации вне Production
`DeploymentSafetyChecks.ValidateSecrets` / `ValidateTrustedNetworksConfigured` срабатывают во **всех**
окружениях, кроме `Development` (сегодня — только `Production`, а развёрнут именно не-Production контур).
Правка — условие запуска, не сами проверки. `DeploymentSafetyChecksTests` дополняется случаем `Staging`.
⚠️ Джоб `docker-build` поднимает контейнер с `ASPNETCORE_ENVIRONMENT=Production` — он остаётся зелёным по
построению; риск — локальные запуски в окружении `Testing`: `appsettings.Testing.json` обязан иметь
валидные значения, иначе весь функциональный набор перестанет подниматься. **Проверить это первым делом.**

### §252. TD-09 — валидация DTO
`MailingController.SendMailDto` (`:73`) получает атрибуты (`[Required]`, `[StringLength]`); 400 приходит в
конвенции проекта (§244.2 — голая строка; `InvalidModelStateResponseFactory` в `Program.cs` уже приводит
ошибки модели к ней, это **не** `ProblemDetails`). То же для `Slug`, `Bio`, `Comment` из §9.29.
Запрет удалить последнего владельца — либо сделан, либо явно оставлен с причиной в отчёте.
Форма ответа не меняется → правки контракта не требуется.

### §253. TD-10 — перенос компании снимает очередь
`CompanyTransferService` при переносе компании между биллинг-аккаунтами переводит **неотправленные**
(`Status == Pending`) `OutboundNotifications` этой компании в `Cancelled` с существующей причиной.
Уже отправленные строки и журнал не трогаются. Покрывается тестом. Миграции нет.

### §254. TD-11 — `CanManageCompany` в одном месте
🔴 **Находка при чтении кода: копий пять, и они НЕ одинаковые.**

| Контроллер | SuperAdmin проходит | Через `CompanyMembership` |
|---|---|---|
| `CompaniesController:906` | да | да |
| `ServicesController:124` | да | да |
| `CompanyAddressController:194` | да | да |
| `MailingController:63` | да | **нет — своя копия запроса** |
| `CompanyNotificationsController:492` | **НЕТ** | да |

Наивное объединение **изменило бы права**: SuperAdmin получил бы доступ к настройкам уведомлений компании.
НФТ §7.1 это запрещает. Поэтому общий хелпер получает флаг явно:

```csharp
// Services/CompanyAccess.cs
public static Task<bool> CanManageCompanyAsync(
    AppDbContext db, ClaimsPrincipal user, Guid companyId, bool superAdminBypass = true);
```
`CompanyNotificationsController` зовёт его с `superAdminBypass: false` и комментарием, что это
**сохранение существующего поведения**, а не недосмотр. `MailingController` переходит на общий запрос
(поведение идентично). Приёмка: **существующие тесты авторизации зелёные без правок** — ни один тест не
подправляется «под новое поведение», это и есть доказательство.

Вне объёма: `BookingsController.CanManageBookingAsync` (`:891`) — другое правило (назначенный мастер +
владелец), `WorkingHoursController.CanManage` / `ScheduleTemplateController.CanManage` — тоже другое
правило (мастер сам себе + владелец). Их не трогать.

### §255. TD-12 — внешняя документация API
Ведёт **backend-developer**, дополняя существующий `API_DOCUMENTATION.md` (новый файл не заводится).
Обязательный минимум: шесть push-эндпоинтов и `staff-push-settings` (цикл 9); `GET /api/companies/public`;
три эндпоинта адреса и два новых поля `CompanyDto` (цикл 13); 🔴 общее правило пустого тела 404/409
(§244.4) **как свойство всего API**, а не особенность одного цикла; гейт TD-03 у выгрузки и удаления.
`docs/**` проверяется на расхождения с этим списком; найденное правится или перечисляется в отчёте.

---

## §256–§258. При наличии времени

**§256. TD-15 — гигиена.** Удалить/перенести `frontend/design_handoff_site_redesign/` и пустой
`frontend/src/components/auth/`; из двух `.example` прод-конфига оставить актуальный. В
`docs/testing-isolation.md` (строки 184, 373) устранить противоречие: документ объявляет
`TESTCONTAINERS_RYUK_DISABLED` запрещённой, практика на colima её требует (B11), а T8-3 описывает рабочий
вариант **без** отключения Ryuk. В документе остаётся **один** рекомендованный рецепт и явное описание,
чем плох второй. Решение принимает devops-engineer.
Сюда же — необязательный «храповик» размера контроллеров (§265.2).

**§257. TD-16 — живой проход подтверждения телефона.** Человек с MAX; фиксируется фактический формат
`vcf_info` (hex или base64) и реальная форма `message_created` с вложением `contact`. Планируется с QA и
заказчиком; архитектурой не закрывается.

**§258. TD-17 — сухой прогон правил уничтожения.** 🔴 **Только после TD-04** (ответ на R8): до изоляции
первое же исключение оборвёт проход и даст неполную картину, которую легко принять за «всё чисто».
`RETENTION_DRY_RUN=true`, журнал прогона разбирается и прикладывается к отчёту. Боевой прогон в цикле
**не включается**.

---

## §259. Нефункциональные требования — как они обеспечены архитектурно

| НФТ спеки | Как обеспечено |
|---|---|
| 1. Нового поведения, кроме TD-03, нет | §254 (флаг `superAdminBypass`), §246.3 (`init`-поля), §251 (только условие запуска) |
| 2. Три подряд зелёных прогона CI | §262 (этап 5), решается процессом, не кодом |
| 3. Время прогона не деградирует | §250 (−~30 с), новые шаги CI — в других джобах/секундах |
| 4. ПДн не попадают в логи | §245.7 (телефон не логируется вовсе), §246.2 (логируется имя правила, не строки) |
| 5. Миграции добавочные и обратимые | §240.3 — **миграций нет ни одной** |
| 6. Обратная совместимость API | §245.6 (секция аддитивна), §246.3 (форма DTO не меняется), §247 (форма `BookingEventActorDto` не меняется), и всё это теперь проверяет §248 |
| 7. Доступность | §245.6 п. 2: текст гейта — обычный текстовый блок, связанный с кнопкой выгрузки через `aria-describedby`, читается скринридером как остальные сообщения об ошибках и называет следующий шаг |
| 8. Документы под суффиксом `_CYCLE16_` | шапка, §264 |

---

## §260. Технические риски цикла и решения по ним

| № | Риск | Решение |
|---|---|---|
| **A1** | 🔴 TD-03 отнимает у честного пользователя без MAX доступ к собственным данным | Компенсирующий путь обязателен и входит в приёмку (§245.6, 5 пунктов). Без него история **не считается закрытой** |
| **A2** | 🔴 Гейт превращает эндпоинт в оракул «на этом номере есть данные» | Ответ и UI зависят **только** от `phoneVerified`, никогда от наличия данных (§245.6 пп. 1–2). Проверяется тестом: ответ для аккаунта с гостевыми данными и без них байт в байт одинаков |
| **A3** | 🔴 Шестое место сопоставления появится незамеченным | Структурный сторож §245.5 с коммитнутым allow-list и маркерами |
| **A4** | Гейт опирается на зеркало и разъезжается с истиной | Источник истины — `VerifiedPhones`, зеркало не используется для решения (§243.1) |
| **A5** | TD-05 «случайно» превращается в переписывающую миграцию | Запрет §240.3 + слой чтения (§247.3) + тест, доказывающий, что колонка в БД не изменилась (§247.6) |
| **A6** | TD-04 меняет поведение прогона, который ни разу не работал в бою | Порядок TD-04 → TD-17 (R8), dry-run по умолчанию не трогается |
| **A7** | Линт контрактов краснеет на предсуществующем и его отключат | Объём сужен и записан явно (§248.3); пять спек — следующим циклом |
| **A8** | Мажор `react-router` затягивает цикл | Вынесен из цикла решением архитектора (§249) |
| **A9** | TD-08 ломает локальный прогон (`Testing` — не `Development`) | Проверяется первым действием истории (§251) |
| **A10** | Две сессии правят `ServiceBooking.TestKit` | Файлы не пересекаются; сверка перед мерджем (§263.2) |
| **A11** | Хранение секретов | Цикл **не добавляет ни одного секрета**. `.env` не трогается. Мастер-ключ, VAPID, `PHONEVERIFY_EXTERNAL_KEY` — вне объёма; TD-13 только **описывает** порядок ротации |
| **A12** | Масштабирование | Цикл не меняет нагрузочный профиль. Добавлено: один `EXISTS` на вызов четырёх редких методов (§245.4) и один запрос по `Users` на открытие истории записи (§247.3). Оба — по индексированным ключам |
| **A13** | AV1 (геокодер и п. 9.8 политики) | **Не закрывается и не трогается.** Значение `ADDRESSVERIFICATION__PROVIDER` на бою не подтверждено; проверяет заказчик. Цикл не изменяет ни код адреса, ни конфигурацию |

**Авторизация** отдельной работы не требует: модель прав (JWT + роли + `CompanyMembership`) не меняется;
TD-11 её только собирает в одном месте **без изменения поведения** (§254).

---

## §261. Разбиение работ: что можно делать параллельно, что последовательно

**Роли:** BE = backend-developer, FE = frontend-developer, DO = devops-engineer, QA = qa-engineer,
LC = legal-counsel, SA = state-analyst.

### §261.1. Задачи

| ID | Задача | Роль | Зависит от | Файлы (площадь конфликта) |
|---|---|---|---|---|
| **T1** | `SubjectScope` + `SubjectScopeResolver` + регистрация в DI | BE | — | `Services/Subjects/*` (новые) |
| **T2** | Гейт в 4 методах `ProfileController` + журнал попытки | BE | T1 | `ProfileController.cs` |
| **T3** | Секция `guestDataGate` в `ProfileExportDto` + fallback-тексты | BE | T1 | `DTOs/Profile/*`, `Services/Subjects/SubjectGateTexts.cs` |
| **T4** | Сторож `SubjectPhoneGateInvariantTests` + маркеры в 6 местах | BE | T2 | `ServiceBooking.UnitTests/*`, маркеры в контроллерах |
| **T5** | Функциональные тесты `GuestDataGateTests` | BE | T2, T3 | `ServiceBooking.Tests/Tests/*` (новый файл) |
| **T6** | TD-05 слой записи (`DeleteAccount`) | BE | T2 | `ProfileController.cs` |
| **T7** | TD-05 слой чтения (`GetHistory`) + тесты | BE | — | `BookingsController.cs` |
| **T8** | TD-04: `Error`/`Skipped`, изоляция, раннер, тест | BE | — | `Services/{Scheduling,Retention}/**` |
| **T9** | TD-02: `ReloadLegalNow` + замена 18 `Task.Delay` | BE или QA | — | `ServiceBooking.Tests/{Infrastructure,Tests}/Legal*` |
| **T10** | TD-11: `CompanyAccess` + 5 контроллеров | BE | — | 5 контроллеров, только приватные методы |
| **T11** | TD-09: валидация DTO | BE | — | `MailingController.cs` и др. |
| **T12** | TD-10: очередь при переносе компании | BE | — | `Services/Billing/CompanyTransferService.cs` |
| **T13** | TD-08: проверки конфигурации вне Production | BE | — | `DeploymentSafetyChecks.cs`, `UnitTests` |
| **F1** | `GuestDataGateNotice` + врезки на `ProfilePage`/`DeleteAccountPage` | FE | контракт §273 | `components/profile/*`, 2 страницы |
| **F2** | `useExportData`: признак гейта, объяснение вместо «пусто» | FE | контракт §273 | `hooks/useExportData.ts` |
| **F3** | TD-06: обновление `axios`, проверка `react-router`, 4 зелёных команды | FE | — | `package.json`, `package-lock.json` |
| **F4** | Скрипт `types:api:cycle16` + регенерация всех 6 файлов типов | FE | `contracts/cycle16` | `package.json`, `src/types/*` |
| **F5** | `@redocly/cli` + `contracts/redocly.yaml` | FE или DO | — | `package.json`, `contracts/redocly.yaml` |
| **D1** | CI: `npm audit`, сверка типов, линт контрактов | DO | F3, F4, F5 | `.github/workflows/ci.yml` |
| **D2** | TD-13: раздел 18 `DEPLOY.md` | DO | — | `DEPLOY.md` |
| **D3** | TD-14: `DEPLOY-windows.md` / `web.config` | DO | — | 2 файла |
| **D4** | TD-15: гигиена + `docs/testing-isolation.md` | DO | — | `docs/`, `frontend/` |
| **D5** | Подсчёт затронутых аккаунтов (SQL §245.6 п. 5) | DO | — | только чтение боевой БД |
| **Q1** | Проверка CI «от обратного» (§248.4) | QA | D1 | временная правка, откатывается |
| **Q2** | End-to-end путь обращения субъекта + `docs/subject-rights.md` | QA | F1 | `docs/subject-rights.md` |
| **Q3** | Три подряд зелёных прогона CI на ветке цикла | QA | всё | — |
| **Q4** | TD-17: сухой прогон правил уничтожения | QA + DO | **T8** | отчёт |
| **L1** | Тексты TD-03: обоснование отказа, `uiTexts.guestDataGateNotice` | LC | — | `legal-drafts/legal.json` |
| **B1** | TD-12: `API_DOCUMENTATION.md` | BE | — | 1 файл |
| **S1** | TD-19: учётная правка реестра §9 | SA | конец цикла | `CURRENT_STATE.md` |

### §261.2. Что идёт параллельно

**Волна 0 (стартует сразу, ни от чего не зависит):**
`L1` (тексты — начинать первыми, они на критическом пути TD-03) · `T7` · `T8` · `T9` · `T10` · `T11` ·
`T12` · `T13` · `F3` · `F5` · `D2` · `D3` · `D4` · `D5` · `B1`.

**Волна 1 (backend и frontend идут параллельно по контракту):**
- BE: `T1` → `T2` → (`T3`, `T4`, `T5`, `T6`)
- FE: `F1`, `F2` — **по `API_CONTRACT_CYCLE16.md` §273 и `contracts/cycle16/openapi.yaml`, не дожидаясь
  бэкенда**. До готовности `T3` фронт работает против мок-сервера:
  `npx @stoplight/prism mock contracts/cycle16/openapi.yaml --port 4010`.
- Единственная общая точка — форма секции `guestDataGate`; она зафиксирована схемой до начала работ,
  поэтому блокировки нет.

**Волна 2 (после своих зависимостей):** `F4` (нужен `contracts/cycle16`) → `D1` → `Q1`.
`Q2` — после `F1`. `Q4` — строго после `T8`.

**Волна 3 (закрытие):** `Q3` (три зелёных прогона) → сверка с диффом цикла 15 (§263.1) → `S1`.

### §261.3. Что строго последовательно (и почему)

1. `T1 → T2` — резолвер должен существовать до правки методов.
2. `T2 → T4` — сторож коммитится вместе с уже расставленными маркерами, иначе первый же прогон красный.
3. `T2 → T6` — TD-05 использует множество `bookingsToAnonymize`, уже отфильтрованное гейтом.
4. `F3/F4/F5 → D1` — шаги CI нельзя включать раньше, чем они начнут проходить.
5. `D1 → Q1` — проверять «от обратного» нечего, пока шага нет.
6. **`T8 → Q4`** — ответ на R8.
7. `L1 → закрытие TD-03` — код можно писать с fallback-текстом, но **закрывать историю без текста юриста
   нельзя**.

### §261.4. Разведение конфликтов по файлам

`ProfileController.cs` трогают `T2`, `T3`, `T6` — **это один разработчик, последовательно**, не три.
`ci.yml` трогает только DO (`D1`). `package.json` трогают `F3`, `F4`, `F5` — один разработчик, один
коммит. Никакие два исполнителя не редактируют один файл одновременно; при необходимости — свой
`git worktree` на роль (урок T8-8).

---

## §262. Порядок этапов цикла

```
0. LC начинает тексты (L1) · DO считает затронутые аккаунты (D5)
1. Волна 0: независимые истории (T7–T13, F3, F5, D2–D4, B1)   ← самый широкий параллелизм
2. TD-03: T1 → T2 → T3/T4/T5/T6 ‖ FE F1/F2 по контракту (prism)
3. contracts/cycle16 → F4 → D1 → Q1 (проверка от обратного)
4. Q2 (end-to-end путь субъекта) · Q4 (сухой прогон, ТОЛЬКО после T8)
5. Q3: три подряд зелёных прогона CI на ветке цикла
6. Сверка с фактическим диффом цикла 15 (§263.1)
7. S1: учётная правка реестра §9 в CURRENT_STATE.md
```

---

## §263. Координация с параллельными работами

### §263.1. Цикл 15 (карточка бронирования, тарифы)
Идёт в другой сессии; на origin не опубликован. Цикл 16 **не трогает** карточку бронирования и тарифы.
Вероятные точки пересечения по файлам: `BookingsController.cs` (цикл 16 правит **только** `GetHistory`,
§247.3), `frontend/src/pages/ProfilePage.tsx`, `package.json`.
🔴 **Перед мерджем цикла 16 в `develop` — обязательная сверка с фактическим диффом цикла 15.**
`CURRENT_STATE.md` цикла 16 станет частично устаревшим в момент вливания цикла 15 — это ожидаемо.

### §263.2. Параллельная починка TD-01
Чинит `RateLimitingTests` / teardown `TestDatabaseLease.DropAsync`, площадь — `ServiceBooking.TestKit`.
Цикл 16 туда не заходит: TD-02 работает в `ServiceBooking.Tests/Infrastructure/LegalDocumentsTestFactory.cs`
и двух `Legal*Tests`. Если пересечение возникнет — развести **до** мерджа.
🔴 **Красный `backend` на `develop` не является регрессией цикла 16**, и QA не должен трактовать его так.

### §263.3. Очередь из 22 невыкаченных миграций (R6)
Цикл 16 не добавляет миграций (§240.3), очередь остаётся 22. Статус деплоя стенда по B1 неизвестен —
это **не** вопрос цикла 16, но перед выкатом devops сверяет фактическую версию схемы на машине.

---

## §264. Где живут документы цикла (ответ на R7)

- Документы цикла 16 — **в корне**, с суффиксом: `SPEC_CYCLE16_TECH_DEBT.md`, `ARCHITECTURE_CYCLE16.md`,
  `API_CONTRACT_CYCLE16.md`, `contracts/cycle16/openapi.yaml`.
- Корневые `SPEC.md` (цикл 13), `ARCHITECTURE.md`, `API_CONTRACT.md` **не перезаписываются**.
- `docs/history/` цикл **не заводит**: перенос 33 корневых документов требует обновления ссылок в том же
  коммите (§9 C5) — это самостоятельная работа, и делать её в цикле, где чинится V1, значит смешать
  переименования с содержательными правками.
- Точечные правки в корневые файлы допускаются и должны быть перечислены в отчёте: ссылка на §245 из
  `ARCHITECTURE.md` §7 (выгрузка/удаление) и упоминание гейта в `API_CONTRACT.md` §8/§9.
- Конвенцию «документы цикла — с суффиксом, корень не занимать» дописывает SA в `CURRENT_STATE.md` §10.5.

---

## §265. Рекомендации следующему циклу (не входят в объём 16)

1. **§9.19 — рост контроллеров.** `CompaniesController` 635 → 1117 строк, суммарно 10 601. Разбиение —
   отдельный цикл с собственной приёмкой. **Правило, вводимое сейчас: новые эндпоинты в шесть выросших
   контроллеров не добавляются** (цикл 16 сам его соблюдает — он не добавляет ни одного эндпоинта).
2. **Храповик размера контроллеров** (необязательно, TD-15): тест с коммитнутой таблицей «файл → потолок
   строк» на сегодняшних значениях. Рост становится красным CI, а не наблюдением через семь циклов.
3. **Линт оставшихся пяти контрактов** (§248.3) — с починкой найденного.
4. **`dotnet format` в CI** (§9.25) — отдельным коммитом в цикле, который тестов не трогает.
5. **Мажор `react-router` v6→v7** (§249) — с пересмотром всех маршрутов и гейта согласий.
6. **N9-13**: переименование опции `notifications.whatsapp` — это миграция данных, значит только вне
   запрета §0-bis п. 3 и отдельным решением.
