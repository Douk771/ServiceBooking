# ARCHITECTURE — цикл 20 ServiceBooking: закрытие правовых вопросов и подготовка комплекта к публикации

**Разделы §400–§419.** Ветка — `cycle/020-legal-closure` (подготовлена devops-engineer; ветки, коммиты
и мёржи — не зона архитектора). Отправная точка кода — `develop` = `e3774c1`; документы цикла — `bac6209`.
Номера §380–§399 намеренно оставлены свободными под параллельный цикл 19, чтобы ссылки двух циклов не
совпали при мёрже.

**Вход (приоритет по порядку):** `LEGAL_DECISIONS_CYCLE20.md` (включая §5 — ответы на П1–П6),
`SPEC_CYCLE20_LEGAL_CLOSURE.md` **§12** (имеет приоритет над §1–11), `LEGAL_REVIEW_CYCLE20.md` (§1 —
вставки, §2 — требования Т20-01…Т20-13, §3 — бланк, §9 — чек-лист публикации), `CURRENT_STATE.md` §0.4,
§6 (конвенции, включая «Конвенции правовых текстов»), §9 (C15-8, C17-4/6/10, C18-8, C18-9, TD16-1/3/5).

**Почему файлы названы `*_CYCLE20.md`.** Корневые `ARCHITECTURE.md`/`API_CONTRACT.md` — документы
цикла 3, на их разделы ссылается код (`ARCHITECTURE.md §7.1`, `§8.4`, `§15.1`). Конвенция проекта
(§10.5 CURRENT_STATE, так же сделано в циклах 16–18): `ARCHITECTURE_CYCLE20.md`,
`API_CONTRACT_CYCLE20.md`, `contracts/cycle20/openapi.yaml`.

**Объём.** US-20-01…US-20-09 и блок А (тексты `legal-drafts/` по `LEGAL_REVIEW_CYCLE20.md` §1,
манифест, `LegalKit build`). **Не входят:** callback, геокодер (его удаляет цикл 19 — не трогать ни
код, ни абзацы о нём), C-13 / форма заявки на канал (отложено, П4), D13, публикация комплекта
(`LegalKit publish` — отдельный шаг после цикла). Все данные на бою тестовые (`LEGAL_DECISIONS` §5).

**Проход 2 (продолжение прерванного запуска).** Документ сверен с SPEC §1–12 и с Т20-01…Т20-13 и
точечно дополнен: правило повторной отметки (§402.2), отзыв через форму обращения (§402.4),
исправлена формулировка §405, Т20-11 в чек-листе devops (§413, П-5), тесты `CY20-B-06` и
`CY20-B-07(f)` и grep-пункт про `Normalize` (§417). Контракт — `API_CONTRACT_CYCLE20.md` (§430–§445) и
`contracts/cycle20/openapi.yaml`.

---

## §400. Главные решения цикла в десяти строках

1. **Отметка «письменное согласие получено» — строка общего журнала согласий `ConsentRecord`**, а не
   новая таблица: `DocumentKey = "HealthDataWrittenConsentForm"`, `Source = PaperForm` (новый член в
   конец перечисления), `Act = Confirmed`, субъект — тот же `ForPhoneInCompany(phone, companyId)`, что
   у салонных согласий. Добавляются две nullable-колонки: `FormId`, `RevokedByUserId`. Так отметка без
   новых читателей попадает в «Мои согласия», в выгрузку, в правило хранения `ConsentRecordRule` и в
   отзыв из профиля (§402).
2. **Поле «здоровье» открывает только эта отметка.** Электронное согласие (салонная форма
   `HealthDataConsent` и цель `PdnConsent/HealthData`) поле больше не открывает; маршрут салонной формы
   `POST …/health-consent` отзывается кодом **410 Gone** (конвенция цикла 7).
3. **Все существующие `ClientHealthNotes` удаляются однократно отдельной миграцией данных** (ответ П1:
   «удалить»; все данные тестовые). Переходного режима нет (§402.6).
4. **Уведомления в кабинете — новый маленький механизм из двух таблиц** `PlatformNotices` +
   `PlatformNoticeAcknowledgements`. `ConsentGate`, `LegalUpdateBanner`, `TrialTerms*` и
   `TemplateAcknowledgementModal` не трогаются (Р5). Маршруты адресата лежат под `/api/legal/notices`,
   то есть **уже** входят в allow-list гейта 451 (префикс `/api/legal/`): уведомление читается и в
   период приостановления, и до принятия новой редакции (§404).
5. **Будущая редакция документа читается до даты вступления через снимок HTML, приложенный к
   уведомлению** (`attachment`, хеш SHA-256, показ в `<iframe sandbox>`). Механизма «отложенной
   публикации» в LegalKit нет, и цикл его не строит (§404.4).
6. **Журнал срабатываний гейта гостевых данных — отдельная таблица `GuestDataGateEvents`** с правилом
   хранения 365 дней. В ней нет ни IP, ни телефона, ни счётчиков (§406).
7. **Предел 24 ч для отмены — одна функция `ClientRescheduleWindow.EffectiveCancelHours`**, которой
   пользуются и `Cancel` (409), и `clientCancelAllowed`, и новое поле `clientCancelMinHours` (§405).
8. **Причина ручного назначения скрытого тарифа — перечисление `SubscriptionChangeReason`** и две
   колонки в `SubscriptionChangeLogs`. Правило «когда причина обязательна» — чистый класс
   `ManualPlanAssignmentPolicy`, по ответу П3 (§403).
9. **LG6 закрывается на трёх путях:** перенос без смены владельца, чей владелец не связан с
   принимающим аккаунтом (409); обязательный флаг `confirmRightsTransfer`; `PUT
   /api/admin/companies/{id}/owner` на несвязанного пользователя (409) (§407).
10. **Порядок «манифест раньше кода» (DEPLOY §10.2c) соблюдается на двух уровнях.** В репозитории
    первым идёт коммит А (тексты, пять новых ключей, `LegalTextKey`, `KnownNames`, сборка). На бою
    devops дополняет живой `legal.json` пятью ключами **до** выката кода. Версии **документов** на бою
    при этом не меняются, поэтому стена 451 при выкате не поднимается. Она поднимется один раз, при
    `publish` (§413).

---

## §401. Стек: ни одной новой зависимости

`ServiceBooking.API.csproj`, `ServiceBooking.Core.csproj`, `ServiceBooking.Infrastructure.csproj`,
`ServiceBooking.LegalKit.csproj` и `frontend/package.json` **не получают новых пакетов** (конвенция
циклов 13–18). Всё закрывается тем, что уже есть:

| Что нужно | Чем закрывается | Почему не новое |
|---|---|---|
| Журнал отметок о бумажном согласии | `ConsentRecord` + `ConsentLedger` (цикл 5) | «одна таблица согласий — один читатель, одна выгрузка» — принцип §44.2 цикла 5 |
| Печать бланка A4 | браузерная печать (`window.print()`) + Tailwind `print:` | генерация PDF на сервере противоречит «бланк на сервере не хранится» и тянет зависимость |
| Подстановка ФИО и реквизитов в бланк | `data-legal-value` + `applyLegalRuntimeValues` (циклы 15/17) | механизм уже покрыт корпусом `contracts/legal/runtime-value-forms.json` |
| Уведомления и ознакомление | EF-сущности + `IRetentionRule` | «уведомлений» у платформы нет, но это две таблицы, а не подсистема |
| Показ будущей редакции | `LegalKit publish --out <временный каталог>` + `<iframe sandbox srcdoc>` | отложенную публикацию в LegalKit цикл не строит |
| Журнал гейта на 1 год | EF-сущность + правило retention | отдельный sink Serilog — вторая система хранения с другой ротацией |
| Хранение и хостинг | та же Postgres, тот же VPS | новых платных сервисов нет, стоимость эксплуатации не растёт |

---

## §402. US-20-01 — бумажное согласие на сведения о здоровье (LG1, Т20-03, Т20-04)

### §402.1 Почему `ConsentRecord`, а не новая таблица

Требования к отметке — кто, когда, в какой компании, для какого клиента, по какой редакции бланка,
отмена с автором — совпадают со схемой `ConsentRecord`. Не хватает двух полей: номера бланка и автора
отзыва. Новая таблица потребовала бы нового читателя «Мои согласия», новой секции выгрузки, нового
правила хранения и новой ветки отзыва из профиля. Именно этого цикл 5 избегал, сводя все согласия в
одну таблицу.

**Отступление от формулировки SPEC, названное явно.** SPEC §7 требует «отметка и её отмена —
append-only, отмена отдельным событием». В `ConsentRecord` отзыв — это **однократная** запись
`RevokedAtUtc`/`RevokeReason`/`RevokedByUserId` в строку отметки. Строка не удаляется, её поля отметки
не меняются никогда, а повторная отметка — новая строка. Цель требования (неизменяемый факт отметки и
автор отмены) достигается. Модель отзыва остаётся той же, что у всех согласий продукта с цикла 5.
Проверка для QA — §417, тест `CY20-B-01e`.

### §402.2 Модель

`ConsentRecord` (+2 колонки, миграция M1):

| Колонка | Тип | Смысл |
|---|---|---|
| `FormId` | `varchar(16)` null | номер бланка `HD-XXXXXXXX`, напечатанный на бумаге. Пусто — салон использовал собственный бланк (D3 п. 8.6.1) |
| `RevokedByUserId` | `varchar(450)` null, **без FK** | кто снял отметку. Пусто — отзыв сделал сам субъект из профиля или правило. FK нет, чтобы удаление аккаунта сотрудника не трогало журнал (как у `ConsentRecord` FK NO ACTION) |

`ConsentSource` — новый член **в конец**: `PaperForm` (после `AddressForm`). Колонка — обычный `int`,
миграции для перечисления не нужно.

Строка отметки: `UserId = null`, `SubjectPhone = <канонический телефон клиента>`, `CompanyId`,
`DocumentKey = LegalTextKey.HealthDataWrittenConsentForm`, `DocumentVersion` = текущая версия uiText
(сверяется с присланной, иначе 409), `DocumentHash` = хеш этого текста, `Purpose = null`,
`Act = Confirmed`, `Source = PaperForm`, `RecordedByUserId = <сотрудник>`, `FormId`,
**`IpAddress = null`, `UserAgent = null`**. Отметка фиксирует утверждение сотрудника о бумаге, её
автор — `RecordedByUserId`. IP сотрудника доказательной силы не добавляет, поэтому не собирается
(минимизация).

Проверка «отметка есть» — `ConsentLedger.CurrentAsync(ForPhoneInCompany(phone, companyId),
HealthDataWrittenConsentForm, purpose: null)`. Это **один** запрос по существующему частичному индексу
`IX_ConsentRecords_CurrentBySubject` (НФТ §7 «не больше одного индексированного запроса»).

**Повторная отметка.** Если живая отметка с тем же `FormId` и той же `DocumentVersion` уже есть, новая
строка не пишется, ответ — текущее состояние (защита от двойного нажатия). Во всех остальных случаях
(другой бланк, новая редакция бланка) пишется новая строка, «текущей» считается последняя
(`ConsentLedger.CurrentAsync`). Снятие отметки снимает **все** живые строки субъекта в компании (§402.4).

### §402.3 Где что меняется в коде

- `ClientConsentsController.HasHealthConsentAsync` → **`HasWrittenHealthConsentAsync`**: только
  отметка `PaperForm`. `PdnConsent/HealthData` и салонная `HealthDataConsent` больше не учитываются.
- `GET …/health-note`: без активной отметки `value` **всегда `null`**, даже если строка в БД есть
  (защита от гонки), `consentRequired: true`, плюс новое поле `writtenConsent`.
- `PUT …/health-note`: без отметки — `400` с `RequiredConsentDto(message,
  "HealthDataWrittenConsentForm")`.
- `DELETE …/health-note`: **разрешён и без отметки.** Удаление уменьшает обработку спецкатегории, а
  отказ в нём держал бы данные дольше. Это осознанное отступление от буквы Т20-03 п. 2 («GET/PUT/DELETE
  только при отметке») в пользу субъекта.
- Новые маршруты (контракт §432): `GET …/health-consent-form`, `POST …/health-written-consent`,
  `POST …/health-written-consent/revoke`.
- `POST …/health-consent` → **410 Gone** с текстом-заменой.
- Права: тот же круг, что у поля — `CompanyMembership.IsStaffAsync`; SuperAdmin → 403 (как у поля
  сейчас). На отметку ставится `[RequiresOwnerTerms]`, как на `PUT health-note`: отметка — заверение
  абонента по D3 п. 12.1 «н». Действий под `OwnerScope` становится 13, а не 12. Это отражено в
  `API_DOCUMENTATION.md` (задача B12).

### §402.4 Отзыв — один каскад на четыре входа

Новый сервис `Services/Legal/WrittenHealthConsentRevoker.cs` (с БД) делает одно и то же для всех
входов: ставит отзыв на **все** живые строки `HealthDataWrittenConsentForm` субъекта в компании (или во
всех компаниях) и **сразу** удаляет `ClientHealthNotes` этого субъекта в этой компании (или во всех) —
в одной транзакции. Вызывается из четырёх мест:

| Вход | Субъект и охват | Гейт цикла 16 |
|---|---|---|
| сотрудник: `POST …/health-written-consent/revoke` (`SubjectWithdrew` / `MarkedByMistake`) | телефон + эта компания | не применяется (`staff-scoped`) |
| клиент: `POST /api/profile/consents/revoke` c `documentKey` = `HealthDataConsent` **или** `HealthDataWrittenConsentForm` + `companyId` | `guestMatchPhone` + эта компания | **да**: без подтверждённого номера — прежний `400`, отзыв не оформляется (TD-03-ter) |
| клиент: отзыв `PdnConsent` целиком или цели `HealthData` | `guestMatchPhone` + **все** компании | **да**: при неподтверждённом номере отметки по телефону не трогаются — как и удаление заметок сейчас |
| правило хранения | — | отметки не трогает (живое согласие не стареет — `ConsentRecordRule`) |

Существующее удаление заметок в `RevokeSalonConsentAsync` и `ApplyOrPreviewRevokeEffectsAsync`
заменяется вызовом сервиса, **маркеры `SUBJECT-PHONE-GATE` сохраняются** на каждой строке сопоставления
телефона (конвенция цикла 16, её проверяет `SubjectPhoneGateInvariantTests`). `RevokeReason` —
фиксированные строки (`"Отзыв, отмеченный сотрудником"`, `"Отметка поставлена ошибочно"`,
`"Отзыв из личного кабинета"`) в `WrittenHealthConsentTexts.cs`.

**Отзыв через форму обращения** (`POST /api/subject-requests`, вид `ConsentWithdrawal`) отдельного
маршрута не получает. Оператор платформы доступа к полю не имеет (SuperAdmin → 403, §48.2 цикла 5) и
по регламенту (блок В, В-4) передаёт отзыв салону. Салон снимает отметку с причиной `SubjectWithdrew`,
и срабатывает тот же каскад. Если у субъекта есть учётная запись с подтверждённым номером, регламент
предлагает ему и отзыв из профиля (вход 2). Суперадминский маршрут снятия отметки цикл не делает: он
открыл бы оператору платформы операцию над спецкатегорией в чужой компании.

### §402.5 Бланк: данные для печати, номер, реквизиты оператора

- **Текст бланка** — uiText `HealthDataWrittenConsentForm` (`legal-drafts/17-health-data-written-consent-form.html`,
  дословно `LEGAL_REVIEW_CYCLE20.md` §3). Фронт печатает раздел «Бланк».
- **Значения** отдаёт `GET …/health-consent-form` (§432.4): `clientFullName` (зарегистрированный — `FirstName
  LastName`; гость — `GuestName` последней записи в этой компании), `companyName`, `companyAddress`
  (`Company.Address`), `operatorFullName`/`operatorAddress`/`operatorInn` (с биллинг-аккаунта компании),
  `formId`, `formPrintedDate`. Ответ с `Cache-Control: no-store`. В лог не пишется ничего из тела.
- **`formId`** генерирует сервер на каждый запрос (`HD-` + 8 символов Crockford base32, чистая функция
  `HealthConsentFormId.New()` + `IsValid()`, юнит-тесты). **Не хранится** до отметки: сгенерированный
  бланк на сервере не сохраняется (US-20-01). Связь «бумага ↔ отметка» — `FormId`, который сотрудник
  подтверждает при отметке (фронт подставляет последний напечатанный). Формат проверяется, существование
  не проверяется — хранить нечего.
- **Пустое значение печатается линией.** `applyLegalRuntimeValues` не меняется. Страница печати
  передаёт вместо `null` константу `BLANK_LINE = '_'.repeat(32)`. Тогда блоки
  `data-legal-when="operatorInn"`/`"companyAddress"` печатаются с линией для заполнения от руки, а не
  исчезают (US-20-01: «реквизиты, которых нет, остаются пустой строкой, а не пропадают»).
- **Реквизиты оператора (Т20-04 п. 3, D1 П-13)** — три nullable-колонки на `BillingAccount` (миграция
  M1): `ConsentOperatorFullName varchar(300)`, `ConsentOperatorAddress varchar(500)`,
  `ConsentOperatorInn varchar(12)`. Заполняет держатель аккаунта (`GET|PUT /api/billing/operator-details`,
  §432.8). Валидация — чистый класс `ConsentOperatorDetailsValidator`: ИНН — 10 или 12 цифр; ФИО не
  принимается с инициалами (`(^|\s)[А-ЯЁA-Z]\.` → 400); длины. Заполнять **не обязательно** (решение
  заказчика): бланк печатается и без них. `operatorDetailsMissing: true` в ответе формы заставляет
  фронт показать подсказку «заполните реквизиты в разделе „Ваша подписка“». Решение «завести
  реквизиты» принято, потому что это дёшево (три колонки и одна форма), а текст D1 П-13 уже описывает
  такой сбор.
- **Новые рантайм-имена** `clientFullName`, `operatorFullName`, `operatorAddress`, `operatorInn`,
  `companyAddress`, `formId`, `formPrintedDate` добавляются **в одном коммите** (коммит А, §412) в
  `RuntimeValueScanner.KnownNames`, `LEGAL_RUNTIME_VALUE_NAMES` и в корпус
  `contracts/legal/runtime-value-forms.json`. В корпусе появляется поле `knownNames` — единственный
  список имён. Тесты обеих сторон (`RuntimeValueFormsCorpusTests.cs`, `legalRuntimeValues.test.ts`)
  сверяют свои наборы с ним. Без этого сборка упадёт на файле 17: сканер не знает новых имён.

### §402.6 Однократная очистка (П1 → «удалить», Т20-03 п. 7)

Отдельная миграция **M2 `Cycle20PurgeHealthNotesWithoutWrittenConsent`**, одна инструкция:
`DELETE FROM "ClientHealthNotes";`. В момент её применения отметок `PaperForm` ещё не существует
физически, поэтому «удалить заметки без отметки» и «удалить все» — одно и то же. Это записано
комментарием в миграции. `Down()` пустой, с комментарием «необратимо по решению заказчика П1». Почему
миграция, а не разовый скрипт: миграции применяются автоматически на старте, на каждом стенде ровно
один раз и в известном порядке. Скрипт devops можно забыть или прогнать дважды.

⚠️ **Предусловие, без которого M2 выкатывать нельзя:** на момент выката реальных пользователей нет
(LEGAL_REVIEW_CYCLE20 Т20-03 п. 7 ⚠️). Проверку делает devops (§413, шаг П-1). Если реальный
пользователь появился, M2 выкатывать нельзя, вопрос возвращается к юристу и заказчику.

### §402.7 Профиль клиента (Т20-03 п. 3)

- `POST /api/profile/consents` с `HealthData` в `purposes` → **400** («согласие даётся в салоне на
  бумажном бланке»). Запись цели в манифесте **не удаляется**: иначе сломается отзыв ранее данных.
- `GET /api/profile/consents` не меняется. Фронт не предлагает новую выдачу `HealthData`. Уже данная
  (живая) выдача показывается только с кнопкой «Отозвать», рядом — раздел «Текст для клиента» uiText
  `HealthDataConsent`.
- История «Мои согласия» (`HistoryAsync` с `knownPhone`) автоматически показывает отметки
  `HealthDataWrittenConsentForm`. Фронту нужна только подпись ключа.

### §402.8 Строка «бланк не фотографируйте»

Т20-03 п. 5: у поля «здоровье» и у загрузки фото заметок — строка «Бланк согласия не фотографируйте и
не загружайте в сервис». Текст юриста, но в манифест он не вынесен. Он живёт во фронтовом
файле-константе `frontend/src/legal/staffNotices.ts` с шапкой «не переписывать без legal-counsel»
(аналог `LegalNotices.cs` для фронта, НФТ §7). Там же — подпись к полю окна отмены (Т20-05 п. 3,
§405) и подсказка суперадмину на экране цены тарифа (US-20-03).

---

## §403. US-20-02 — причина ручного назначения скрытого тарифа (C15-8/C17-4, Т20-01, П3)

### §403.1 Модель

- Перечисление `Core/Enums/SubscriptionChangeReason.cs` — **append-only**, хранится `int`:
  `OperatorErrorCorrection = 1`, `TrialReissue = 2` (0 намеренно не занят: «не указано» — это `null`).
- `SubscriptionChangeLog` (+2 колонки, M1): `ReasonCode int null`, `ReasonDetails varchar(1000) null`.
- Названия для людей — `Services/Billing/SubscriptionChangeReasonTexts.cs` (сервер собирает текст).

### §403.2 Правило — чистый класс `Services/Billing/ManualPlanAssignmentPolicy.cs`

```
RequiresReason(currentPlanId, targetPlanId, targetIsPublic) =
    targetPlanId != null && !targetIsPublic && targetPlanId != currentPlanId      // П3
Validate(reasonCode, reasonDetails, required) → null | текст 400:
    required && reasonCode == null                 → «нужно основание»
    reasonCode == TrialReissue                     → «только через повторную выдачу пробного периода»
    reasonCode == OperatorErrorCorrection && details пусто → «опишите исправляемую ошибку»
    details.Length > 1000                          → «не длиннее 1000 символов»
```

Порядок в `AssignSubscription`: существующие проверки → **отказ для триала (цикл 18, 409
`TrialPlanNotAssignableHere`) остаётся первым** (Р6: триал этой ручкой не назначается ни с какой
причиной) → `ManualPlanAssignmentPolicy` → запись. Причина, присланная там, где она не обязательна,
принимается и пишется в журнал. Неизвестное значение перечисления отсекает биндинг модели (400, текст
через `ModelValidationErrorFormatter`).

`TrialActivationService`: при `TrialGrantMode.SuperAdminOverride` в строку журнала пишутся
`ReasonCode = TrialReissue` и `ReasonDetails = request.Reason`. Обязательный текст `Reason` у
`Billing_RegrantTrialInput` остаётся (US-20-02). Обычная выдача (владелец сам или суперадмин по общему
правилу) и автоматический переход «триал → Free» (`TrialLifecycleTask`) причину не пишут: это не
ручное назначение, а единое правило.

**Проверка Т20-01 п. 4 (grep по записи `PlanConfigId`) выполнена:** путей записи ровно три —
`AdminBillingController.AssignSubscription`, `TrialActivationService.GrantAsync`,
`TrialLifecycleTask` (переход на Free по правилу). Других нет.

⚠️ **Остаточное расхождение текста и кода — вопрос заказчику О-1 (§418).** D3 п. 6.13.15.4 (В-6)
требует основания для **любого** изменения состава подписки без заявки, а П3 и SPEC — только для смены
на **другой скрытый** тариф. Назначение публичного тарифа без `requestId` и изменение опций без заявки
кодом не ограничены. Цикл следует П3 и SPEC. Расширить правило — правка одной функции
`RequiresReason`.

### §403.3 Прочее

- Удаляются неиспользуемые `AssignSubscriptionInput` и `AssignOptionInput`
  (`DTOs/Billing/AdminBillingDtos.cs:65–69`). `grep` подтверждает: других ссылок нет.
  (`AdminSubscriptionChangeLogDto` там же тоже не используется, но в объём не входит.)
- История `GET …/subscription-history` отдаёт `reasonCode`, `reasonTitle`, `reasonDetails`.
- Список оснований для выпадающего списка — `GET /api/admin/subscription-change-reasons` (§433.3).
  Фронт не держит своих подписей.

---

## §404. US-20-03 — уведомления в кабинете с фиксацией ознакомления (C18-8(б), Т20-02, Т20-07)

### §404.1 Модель (M1)

**`PlatformNotices`** — опубликованное уведомление. Не редактируется, только отзывается.

| Колонка | Тип | Смысл |
|---|---|---|
| `Id` | uuid | |
| `Kind` | int — `PlatformNoticeKind` append-only: `PriceChange=0, TermsChange=1, Suspension=2, NewProcessor=3, PhotoRemoved=4, Other=5` | |
| `AudienceType` | int — `NoticeAudienceType`: `AllOwners=0, OwnersOnPlans=1, BillingAccount=2, AllClients=3` | |
| `AudiencePlanIds` | `uuid[]` null | для `OwnersOnPlans` |
| `TargetBillingAccountId` | uuid null, **без FK** | для `BillingAccount` |
| `Title` | varchar(200) | собран сервером для `PriceChange`/`TermsChange`/`PhotoRemoved`, иначе — суперадмином |
| `Body` | text (≤ 4000, проверка в коде) | плоский текст, **снимок** того, что увидит адресат |
| `TemplateVersion` | varchar(64) null | версия шаблона `PlatformNoticeTexts` (для шаблонных видов) |
| `LinkUrl` | varchar(500) null | только относительный путь (`/…`) |
| `AttachmentTitle`, `AttachmentHtml`, `AttachmentSha256` | varchar(200) / text / char(64), null | снимок будущей редакции (§404.4) |
| `EffectiveFrom` | date null | дата вступления |
| `PublishedAtUtc` | timestamptz | = момент создания |
| `VisibleUntilUtc` | timestamptz | `max(Published + 365 дн, (EffectiveFrom ?? Published) + 30 дн)` — D3 п. 4.1.1 («не менее 1 года») и US-20-03 («≥ 30 дней после вступления») |
| `CreatedByUserId` | varchar(450) | суперадмин или `"system"` (`PhotoRemoved`) |
| `RevokedAtUtc`, `RevokedByUserId`, `RevokeReason` | null | однократный отзыв |

Индекс: `(VisibleUntilUtc)`.

**`PlatformNoticeAcknowledgements`** — append-only: `Id uuid`, `NoticeId uuid` (FK → `PlatformNotices`,
**Cascade**), `UserId varchar(450)` (**без FK**: запись — доказательство оператора и, как
`ConsentRecord`, переживает удаление аккаунта до срока хранения), `BillingAccountId uuid null` (для
владельческих видов — аккаунт, от имени которого прочитано), `AcknowledgedAtUtc`. **Уникальный индекс
`(NoticeId, UserId)`**: повторное нажатие новой записи не создаёт. Запись идемпотентна через
`INSERT … ON CONFLICT DO NOTHING` или через перехват `23505`, по образцу цикла 18.

### §404.2 Кто адресат (вычисляется при чтении, не снимком)

| `AudienceType` | Адресат |
|---|---|
| `AllOwners` | держатель любого `BillingAccount` (`BillingAccount.OwnerUserId == caller`) |
| `OwnersOnPlans` | держатель аккаунта, у которого `AccountSubscription.PlanConfigId ∈ AudiencePlanIds`. Если в списке есть системный Free (`IsSystemFree`), то подходят и аккаунты без подписки / с `PlanConfigId = null` |
| `BillingAccount` | держатель аккаунта `TargetBillingAccountId` |
| `AllClients` | любой аутентифицированный неудалённый пользователь, кроме `SuperAdmin` (D2 п. 21.2) |

Управляющим компаний владельческие уведомления не показываются (US-20-03, допущение SPEC).
Вычисление при чтении дешевле снимка получателей. Цена: число адресатов в админке — **текущее**, а не
на момент публикации. Доказательная сила от этого не страдает: момент публикации и ознакомление каждого
адресата записаны.

Чистая функция `Services/Legal/NoticeAudience.Matches(notice, CallerFacts)`. `CallerFacts` — «держатель
каких аккаунтов, на каких тарифах, суперадмин ли» — загружаются одним запросом. Всего на
`GET /api/legal/notices`: активные уведомления (единицы и десятки строк) + факты вызывающего +
ознакомления вызывающего = **3 запроса к БД на один HTTP-запрос**. НФТ «не больше одного
дополнительного запроса при входе» касается числа HTTP-запросов фронта, а он ровно один.

### §404.3 Сроки и шаблоны (Т20-02 п. 3, п. 6)

Чистый класс `Services/Legal/PlatformNoticeRules.cs`:

- `PriceChange`: адресаты только владельческие; `EffectiveFrom ≥ сегодня(МСК) + 30`.
- `TermsChange`: `EffectiveFrom ≥ сегодня(МСК) + 15` для владельческих адресатов и `+ 10` для
  `AllClients`. Тип документа: `TermsOwner` → только владельческие адресаты, `TermsClient` → только
  `AllClients`, `Privacy` → любые. `attachment` обязателен.
- `Suspension`: только `BillingAccount`. `NewProcessor`/`Other`: любые адресаты, срок не проверяется.
- `PhotoRemoved`: **только системный** (через этот маршрут → 400).
- «Сегодня» — дата по `Europe/Moscow`, `EffectiveFrom` — `DateOnly` («календарные дни» D3).

Тексты — **новый файл-константа** `Services/Legal/PlatformNoticeTexts.cs` с шапкой «не переписывать
без legal-counsel» (конвенция цикла 18 п. 5). В нём: шаблоны `PriceChange` и `TermsChange` дословно по
Т20-02 п. 6; подпись кнопки «Я ознакомился» и подпись под ней; шаблоны заголовков и `PhotoRemoved` —
**от legal-counsel** (задача L3, их в заключении нет). Тело шаблонных видов сервер собирает из
параметров (`priceChange`, `termsChange`) и сохраняет **снимком** в `Body` вместе с `TemplateVersion`.
Даты — `dd.MM.yyyy`, суммы — `ru-RU` без копеек, если они нулевые.

Подсказка суперадмину на экране правки цены тарифа («новую цену для действующих абонентов применять не
раньше даты из опубликованного уведомления») — фронтовая константа (§402.8). Автоматической связки
«правка цены → уведомление» нет (вне объёма).

### §404.4 Будущая редакция до даты вступления (Т20-02 п. 5)

Решение: к уведомлению `TermsChange` **прикладывается снимок HTML новой редакции**
(`attachment.html`, ≤ 1 000 000 символов). Сервер считает SHA-256 и хранит оба значения. Процедура
оператора (DEPLOY, задача D7): собрать новую редакцию в `legal-drafts/`, выполнить
`LegalKit publish --source <артефакт> --values <боевые значения> --version <будущая> --effective-from
<дата> --out /tmp/next-edition` **во временный каталог** (живой каталог не трогается), взять оттуда
файл документа и приложить его в админке.

Отдача — `GET /api/legal/notices/{id}/attachment`, только адресату, `text/html` с заголовками
`Content-Security-Policy: sandbox; default-src 'none'; style-src 'unsafe-inline'` и
`X-Content-Type-Options: nosniff`. Фронт получает текст через `api` (с токеном) и вставляет его в
`<iframe sandbox="" srcdoc=…>`. Скрипты не исполняются, даже если учётная запись суперадмина
скомпрометирована.

Почему не «ссылка на черновик»: живой каталог на бою содержит **одну** редакцию каждого документа, и
будущий текст положить туда некуда, не сменив текущий (и не подняв 451). Отложенная публикация в LegalKit
— отдельная работа вне объёма.

### §404.5 Показ, приостановление, 451

- Фронт: `PlatformNoticeBanner` в `App.tsx` рядом с `LegalUpdateBanner`, для любого аутентифицированного
  пользователя. Один `GET /api/legal/notices?scope=pending` (react-query, `staleTime` 5 мин, инвалидация
  при логине и после «Я ознакомился»). Баннер **не блокирует** работу (не модалка). Показывает все
  непрочитанные, у каждого — настоящая `<button>` «Я ознакомился» и подпись под ней, `role="status"`.
- Раздел `/notices` («Уведомления сервиса») для всех, `scope=all`, плюс блок на странице «Ваша
  подписка» для держателя. После ознакомления уведомление остаётся видно до `VisibleUntilUtc`.
- **Приостановленный владелец (Т20-02 п. 4, D3 п. 15.2).** Блокировки входа владельца в продукте нет:
  `LockoutEnd` ставится только удалённым аккаунтам. Маршруты адресата не зависят от тарифа (без 402) и
  от активности компании и лежат под `/api/legal/`, которые `LegalConsentFilter` пропускает всегда.
  Тест `CY20-B-03h` закрепляет, что `GET /api/legal/notices` отвечает 200 пользователю с непринятой
  Material-редакцией. Фронтовый `ConsentGate` при 451 закрывает приложение целиком. Отдельный вход в
  `/notices` из-под гейта цикл не делает: запрос API не заблокирован, а 451 снимается принятием текста.

### §404.6 Хранение

`PlatformNoticeRule` (`Services/Retention/Rules/PlatformNoticeRule.cs`, имя `platform-notice`):
удаляет уведомления, у которых `VisibleUntilUtc < now − PlatformNoticeDays`; ознакомления удаляются
каскадом. `Retention:PlatformNoticeDays = 1095` — допущение SPEC «3 года, как у согласий». ⚠️ В D1
п. 13.2 строки про уведомления и ознакомления **нет**: legal-counsel добавляет её в задаче L3, иначе
срок не назван в Политике.

### §404.7 `PhotoRemoved` (Т20-07)

Способ убрать фото у суперадмина **уже есть**: `DELETE /api/companies/{id}/photos/{photoId}`
(`IsOwnerOrSuperAdmin`). Добавляется необязательный query-параметр `reason=DepictedPersonRequest`.
Его учитывают **только** для SuperAdmin, владельцу он ни на что не влияет. После коммита удаления
сервис `PlatformNoticePublisher.PublishPhotoRemovedAsync(company)` создаёт уведомление `PhotoRemoved`
с адресатом `BillingAccount = company.BillingAccountId`. Если аккаунта нет, берётся аккаунт владельца;
если нет и его — уведомление не создаётся, в лог пишется Warning без ПДн. Ошибка создания уведомления
удаление не откатывает: фото уже убрано, а это обязанность перед изображённым (3 рабочих дня, D3
п. 8.8).

---

## §405. US-20-04 — 24 часа для отмены (C17-6/C17-10, Т20-05)

`ClientRescheduleWindow` (единственная точка, `ARCHITECTURE_CYCLE17.md` §304.1/§304.5):

```csharp
public const int MaxEnforcedCancelHours = 24;
public static int EffectiveCancelHours(int storedMinHours) =>
    Math.Min(Normalize(storedMinHours), MaxEnforcedCancelHours);
```

- `BookingsController.Cancel` (ветка `ClientOwner`) и `GetClientBookings` (`clientCancelAllowed`)
  вызывают `CanClientCancel(now, start, EffectiveCancelHours(company.ClientRescheduleMinHours))`.
  Прямого вызова `Normalize` на пути отмены не остаётся. Юнит-тест на исходники для этого не нужен:
  это проверяет grep-пункт §417.
- Текст 409: «Отменить запись можно не позже чем за {**применённое** N} ч до визита…» (Т20-05 п. 2).
- `BookingDto` получает **в конец** `int? ClientCancelMinHours = null` — применённое N. Считается только
  на `GET /api/bookings/client`, в остальных местах `null` (конвенция §115). Фронт пишет «Отменить можно
  не позже чем за N ч» по этому полю, а не по `clientRescheduleMinHours`.
- Перенос не меняется: `IsWithinWindow` и `Normalize` (0–168). **Миграции нет.**
- Подпись поля в настройках компании заменяется дословно текстом Т20-05 п. 3 (фронтовая константа
  §402.8), связь с полем — `aria-describedby`.

---

## §406. US-20-05 — сроки хранения (CYCLE16 В1/В8, Т20-06)

### §406.1 `BookingEvents` — 3 года

- `RetentionPeriods.BookingEventDays`: значение по умолчанию **1095** (было 0), `appsettings.json` —
  1095, `.env.production.example` — `RETENTION__BOOKINGEVENTDAYS=1095`, `docker-compose.prod.yml` —
  `Retention__BookingEventDays=${RETENTION__BOOKINGEVENTDAYS:-1095}`.
- `DeploymentSafetyChecks.ValidateRetentionPeriods`: `BookingEventDays < 1` → отказ старта с текстом
  «установите 1095 (п. 13.2 Политики)». Строгое равенство не требуется: число сверяется с текстом
  тестом (§417, `CY20-U-12`) и видно в `GET /api/admin/retention/policy`.
- `RetentionPolicyDto` получает в конец `BookingEventDays`, `GuestDataGateEventDays`,
  `PlatformNoticeDays`. Сейчас `BookingEventDays` в DTO **нет**, хотя SPEC требует его показывать.

### §406.2 Журнал срабатываний гейта — 1 год, без IP

- Сущность `Core/Entities/GuestDataGateEvent.cs`, таблица `GuestDataGateEvents` (M1): `Id bigint
  identity`, `OccurredAtUtc timestamptz`, `UserId varchar(450)` (**без FK**: запись переживает удаление
  аккаунта — сработать гейт может и на самом удалении), `Operation int` (`GuestDataGateOperation`:
  `Export=0, DeleteAccount=1, Revoke=2, RevokePreview=3`, append-only), `Outcome int`
  (`GuestDataGateOutcome.Applied=0`), `TraceId varchar(64) null`. Код события
  `guest-data-gate.applied` — константа, в строке не хранится. Индексы: `(OccurredAtUtc)`,
  `(UserId, OccurredAtUtc)`.
- **В записи нет** IP, User-Agent, телефона в любом виде, счётчиков, идентификаторов компаний и
  сущностей (CYCLE16 §6.4). Это держит схема: колонок для них нет.
- Писатель — `Services/Subjects/GuestDataGateJournal.RecordAsync(userId, operation)`, единственный
  (приём `BookingEventLog`). Вызывается в четырёх точках `ProfileController`, где сейчас пишется
  `guest-data gate applied` (строки ~219, ~433, ~953, ~1054). Все четыре стоят **до** открытия
  транзакции, поэтому писатель делает собственный `SaveChangesAsync`, и у контекста в этот момент нет
  других несохранённых изменений (это проверяется `Debug.Assert(!db.ChangeTracker.HasChanges())`
  перед `Add`). Сбой записи журнала **не валит** операцию субъекта: `LogError` без ПДн, операция
  продолжается. Права субъекта важнее полноты служебного журнала. Строка лога Information остаётся
  как была: телефона в ней нет.
- Правило `GuestDataGateEventRule` (`guest-data-gate-event`), `Retention:GuestDataGateEventDays = 365`,
  отказ старта при `< 1`. Регистрируется поимённо в `Program.cs`.
- Доступ — только оператору: `GET /api/admin/guest-data-gate-events` (SuperAdmin, `PagedResult`,
  фильтры `userId`, `from`, `to`). Экрана в админке цикл не делает. В выгрузку субъекта журнал **не
  попадает**.
- **Строка `phone-change gate blocked: … phone={MaskedPhone}`** (цикл 14) относится **к обычному логу
  приложения**, а не к этому журналу. Это другой гейт (смена номера, цикл 14 §148.5), и живёт он по
  сроку технических журналов. В годовой журнал телефон не попадает ни в каком виде. Строку цикл не
  меняет.
- ⚠️ **Р4.** Если на бою `RETENTION_DRY_RUN=true` (значение по умолчанию), ни одно правило, включая
  новые, ничего не удаляет, и сроки п. 13.2 не исполняются. devops фиксирует фактическое значение
  (§413, П-6). Переключение — решение заказчика вне цикла.

### §406.3 П-5б — сроки заметок

Принимается правка текста под код (П-5б). Правила `ClientNoteRule`/`ClientHealthNoteRule` **не
меняются** (Т20-06 п. 4).

---

## §407. US-20-07 — только свои компании (LG6, Т20-09, П2)

### §407.1 Пути, которыми компания попадает в аккаунт или меняет владельца — вердикты

| Путь | Что сейчас | Вердикт |
|---|---|---|
| `POST /api/companies` (создание) | `billingAccountProvisioner.EnsureAccountAsync(userId)` — **аккаунт самого создателя**, владелец — создатель | ✅ правилу соответствует, не меняется |
| `POST /api/admin/companies/{id}/transfer` **со сменой владельца** | новый владелец — держатель или участник компании принимающего аккаунта (`ValidateNewOwnerAsync`) | ✅ соответствует (П2), не меняется |
| `POST …/transfer` **без смены владельца** | прежний владелец остаётся, даже если с принимающим аккаунтом не связан | ❌ **закрывается**: 409, если текущий владелец не держатель и не участник компании принимающего аккаунта (проверка до переноса, сама компания в выборку не попадает, потому что ещё числится в исходном аккаунте) |
| `POST …/transfer` (любой) | подтверждения перехода прав нет | ❌ **закрывается** (Т20-09 п. 1): обязательный `confirmRightsTransfer: true`, иначе 400; факт подтверждения пишется в обе строки `SubscriptionChangeLog` и в `CompanyOwnerChangeLog.Comment` |
| `PUT /api/admin/companies/{id}/owner` | **никакой проверки связи**: владельцем компании аккаунта A может стать любой пользователь | ❌ **закрывается тем же правилом**: новый владелец — держатель аккаунта компании или участник любой компании этого аккаунта (включая эту), иначе 409. Если у компании нет аккаунта (`BillingAccountId = null`), проверка не применяется |
| удаление держателя аккаунта (`POST /api/profile/delete-account`) | запрещено, только если пользователь **сам** владелец компании. Держатель, у которого компании ведут управляющие, удалиться может | ⚠️ правилу «владелец связан с аккаунтом» **не противоречит** (управляющие остаются участниками). Но остаётся аккаунт без живого абонента, то есть без оператора ПДн (D3 п. 6.1.5). **Не закрывается в цикле 20** — вопрос заказчику О-2 (§418). Закрыть можно одним `AnyAsync` и 409 |
| прочие пути записи `Company.BillingAccountId` | `grep` находит только `CompaniesController.Create` и `CompanyTransferService` (`ExecuteUpdateAsync`) | ✅ других нет |

### §407.2 Изменения

- `TransferFailureKind` — новый член `CurrentOwnerNotLinkedToTargetAccount`. Текст —
  `BillingTexts.TransferRejectedCurrentOwnerUnlinked(name)`.
- `CompanyTransferService.PreviewAsync`/`TransferAsync`: без `newOwnerUserId` вызывают
  `ValidateNewOwnerAsync(company.OwnerUserId, target)`. Превью в этом случае отдаёт `canTransfer:
  false`, `blockReason` с тем же текстом и новое поле `ownerChangeRequired: true` (в конец
  `CompanyTransferPreviewDto`).
- `CompanyTransferInput` получает в конец `bool ConfirmRightsTransfer = false`.
- `AdminController.UpdateCompanyOwner`: проверка через тот же `ValidateNewOwnerAsync(newOwnerId,
  company.BillingAccountId)` (один источник правила).
- **Существующие данные (Р7)** — SQL для devops/QA в §413 (П-7), только отчёт заказчику, без
  автоматических правок.

---

## §408. US-20-06 — подсказка о людях на фото (D2 цикла 10, Т20-07 п. 1)

Только фронт. `CompanyPhotosSection.tsx` над кнопкой выбора файла, **до** выбора, показывает раздел
«Текст» uiText `CompanyPhotoPeopleNotice` (`useLegalText`, `findSection`), связь с полем —
`aria-describedby`. Если раздел не найден, показывается весь текст (правило `legalSections.ts`).
Галочки нет, загрузку подсказка не блокирует. Для суперадмина при удалении фото — диалог «Причина:
по обращению изображённого человека (владельцу придёт уведомление) / другая» (§404.7).

---

## §409. US-20-08 — TD16-1 и `LegalPricingGateTests` (Т20-10 — обязательно)

- `TestHostSettings.PublishTermsOwner` сейчас снимает суффикс `-draft` с версии из скопированного
  манифеста. Тесты гейта витрины (`LegalPricingGateTests.WriteManifest`) при этом пишут свою версию
  `TermsOwner`, и посеянное опубликованное значение с ней расходится. **Исправление:** посев
  опубликованного `TermsOwner` в тестовом хосте берёт версию **из того манифеста, который реально
  лежит в `Legal:Root` этой фабрики** — одна вспомогательная функция
  `TestLegalManifest.PublishedTermsOwnerVersion(root)` для обоих мест, никаких зашитых строк. После
  подъёма версий в коммите А правок в `TestHostSettings` не требуется: это критерий приёмки.
- Семь `Task.Delay(2500)` в `LegalPricingGateTests.cs` заменяются на `_factory.ReloadLegalNow()` (как
  TD-02), комментарий-предупреждение в начале файла снимается.

---

## §410. US-20-09 — ручная регистрация обращений (Т20-13)

- `SubjectRequest` (+2 колонки, M1): `Channel int not null default 0` (`SubjectRequestChannel`
  append-only: `WebForm=0, Email=1, PostalMail=2`), `RegisteredByUserId varchar(450) null`.
- `POST /api/admin/subject-requests` (SuperAdmin): вид, канал (`Email`/`PostalMail`; `WebForm` → 400),
  дата поступления (не в будущем), телефон (необязателен; если указан — через `PhoneNormalizer`;
  пустой → `""`), контакт, текст. `Reference` — `SubjectRequestReference.Generate()` с тем же циклом
  повторов. `DueAtUtc` — `SubjectRequestDeadline.For(kind, receivedAtUtc, options)`, **от даты
  поступления**, а не от даты регистрации. Сигнал GlitchTip о новом обращении — тот же, что у
  публичной формы (фоновой отправкой, после коммита). Сигналы о сроках цикла 16 работают по `DueAtUtc`
  и подхватывают такие обращения без изменений.
- `GET /api/admin/subject-requests`: `SubjectRequestDto` получает в конец `Channel` и
  `RegisteredByName`.
- Публичная форма (`POST /api/subject-requests`) не меняется (`Channel = WebForm` по умолчанию).

---

## §411. Тексты гейта в манифест (Т20-08) — что меняется в коде после коммита А

- `LegalTextKey`: + `GuestDataGateNotice`, `GuestDataGateDeleteNotice`, `GuestDataGateRevokeNotice`,
  `HealthDataWrittenConsentForm`, `CompanyPhotoPeopleNotice`, все пять — в `All`. После этого
  `LegalDocumentProvider` требует их при каждой загрузке: fail-fast, DEPLOY §10.2c.
- `SubjectGateTexts.ManifestKey` → `LegalTextKey.GuestDataGateNotice`. Строчная буква
  (`"guestDataGateNotice"`) — известное расхождение с фронтом, который уже просит `GuestDataGateNotice`.
  `Fallback` остаётся как страховка.
- Выгрузка (`guestDataGate.explanation`): сервер отдаёт **плоский текст раздела «Текст»** (новая
  чистая функция `LegalSectionText.PlainSection(html, "Текст")` — C#-аналог `splitLegalSections`,
  теги снимаются, абзацы через `\n\n`). Сейчас ушёл бы весь HTML-файл вместе со служебной справкой.
- Фронт показывает врезки через `useLegalText` + `findSection(…, 'Текст')` (выгрузка, удаление),
  разделы «Своё согласие» / «Согласие, записанное салоном» (отзыв). При `phoneVerified === false` фронт
  **не вызывает** отзыв салонного согласия, а показывает второй раздел (указание юриста в файле 16).
  Сервер по-прежнему защищён своим 400.
- **Ссылка `/subject-request` в текстах 14–16 — на несуществующий маршрут.** В приложении маршрут
  называется `/data-request` (`App.tsx`, находка цикла 16). При вставке `href="/subject-request"`
  заменяется на `href="/data-request"`, а `/data-request` добавляется в `allowedTargets`
  `contracts/cycle11/legal-routes.json`. Иначе `legal links` краснит сборку (конвенция правовых текстов
  п. 3). Правка href согласуется с legal-counsel в задаче L2.

---

## §412. Блок А — тексты `legal-drafts/`: кто вносит и в каком порядке

**Кто.** По конвенции (§6 CURRENT_STATE, «Конвенции правовых текстов» п. 1) и прямому указанию
`LEGAL_REVIEW_CYCLE20.md` §1 («вставки вносит реализация по §1, затем `LegalKit build`») вставки
вносит **backend-developer**: у него есть `dotnet` для `LegalKit build`, и он же меняет `LegalTextKey`
в том же коммите. Вставка побайтная по цитате «Старый текст». Если цитата не находится, работа
останавливается и вопрос уходит legal-counsel, а не вставляется «примерно туда». Комментарии
`<!-- ЮРИСТУ: … -->` не удаляются. **legal-counsel** проверяет дифф и подтверждает его (L2), отдаёт
недостающие тексты (L3) и готовит документы блока В в `legal-internal/` (L4).

**Коммит А (один, до кода, читающего новые ключи):**

1. D1 (`01-privacy-policy.html`): П-1…П-15 (П-5а, П-5б, П-9а–г, П-10а–б, П-11а–в, П-14а–б, П-15а–в).
2. D2: К-1…К-4.
3. D3: В-1…В-11, В-12б, В-12в, В-13, В-14, **В-15**. **Не вносить:** В-12а, В-12г (отложено до
   C-13, П4).
4. D9 (`09-channel-offer.html`): П1-1, П1-2, П1-3, П1-5, П1-6, П1-7, П1-9. **Не вносить:** П1-4, П1-8.
5. D4: С-1…С-6. D6: Р-1. uiText 11: Н-1…Н-11.
6. Новые файлы 14, 15, 16, 17, 18 дословно (§1.8 и §3), `href="/subject-request"` →
   `href="/data-request"` (§411).
7. `legal.json`: пять записей `uiTexts` (§1.9), версии по таблице §1.9 (`<ДАТА>-draft`, `changeKind`
   **только** `Material`/`Editorial`), `isDraft: true` везде. `PublicAddressNotice` (13) и абзацы о
   геокодере **не трогать**.
8. `LegalTextKey` (+5, `All`), `RuntimeValueScanner.KnownNames` (+7), `LEGAL_RUNTIME_VALUE_NAMES`
   (+7), `contracts/legal/runtime-value-forms.json` (`knownNames`), `legal-routes.json`
   (`allowedTargets` + `/data-request`).
9. `ServiceBooking.LegalKit build` → `ServiceBooking.API/App_Data/legal` пересобран; `legal check`
   и `legal links` зелёные; `CommittedLegalArtifactTests` зелёный.

Коммит А не зависит ни от одной задачи B и ни от одной миграции. Всё, что читает новые ключи (B2, B11,
F2, F3, F8), мёржится после него. После вливания цикла 19 номера файлов 14–18, пунктов 9.10/9.11 и
таблица версий пересчитываются (Р1, §416).

---

## §413. Шаги devops (перед выкатом, при выкате, после)

**До мёржа в `develop` (в ветке):**

- **D1. CI.** В шаг «Lint API contracts» добавить `../contracts/cycle20/openapi.yaml`, в шаг
  «Generated API types must match the contracts» — `npm run types:api:cycle20` и
  `src/types/api-cycle20.generated.ts`. Обновить комментарий-список в `contracts/redocly.yaml`. ⚠️
  После правки проверить, что `ci.yml` парсится как YAML. Крупные скрипты — в `deploy/ci/`, а не
  инлайном (память проекта).
- **D2. Конфигурация.** `.env.production.example`: `RETENTION__BOOKINGEVENTDAYS=1095`,
  `RETENTION__GUESTDATAGATEEVENTDAYS=365`, `RETENTION__PLATFORMNOTICEDAYS=1095` с комментарием «число
  названо в п. 13.2 Политики — не менять без legal-counsel». `docker-compose.prod.yml`: три строки
  `Retention__…=${…:-<значение>}` рядом с `ScheduledTasks__data-retention__DryRun`. ⚠️ Соседние строки
  (`ADDRESSVERIFICATION__*`) удаляет цикл 19, поэтому при мёрже будет конфликт. Правило разрешения —
  §416.
- **D3. Архив спеки цикла 18:** `git show 7820bf9:SPEC.md > SPEC_CYCLE18_TRIAL_PLAN.md`, если файла
  ещё нет. Файл уже лежит в дереве — проверить совпадение и не перезаписывать.

**Перед выкатом кода на бой (чек-лист, каждый пункт с результатом в отчёте devops):**

- **П-1. Реальных пользователей нет** — письменное подтверждение заказчика в день выката. Без него
  миграция M2 (очистка сведений о здоровье) не выкатывается.
- **П-2. Бэкап** базы (§11 DEPLOY) непосредственно перед выкатом: M2 необратима.
- **П-3. C18-9 / О13 / О15** на живой базе: состояние `legal/legal.published.json` (есть ли
  опубликованный комплект) и распределение `ConsentRecord` по версиям
  (`SELECT "DocumentKey","DocumentVersion",count(*) FROM "ConsentRecords" GROUP BY 1,2`). Результат —
  в отчёт и заказчику (Г-7: публикация Material-редакций поднимет 451 у всех).
- **П-4. DEPLOY §10.2c: обновить `/opt/ezbook/app/legal/` ДО выката кода.** Положить из собранного
  артефакта `ServiceBooking.API/App_Data/legal/` файлы uiTexts **11, 14, 15, 16, 17, 18** и в живом
  `legal.json` добавить пять новых записей `uiTexts` и обновить запись `HealthDataConsent` (версия и
  файл) — `isDraft: true`. **Записи `documents` и их версии не трогать:** иначе при выкате поднимется
  451 ещё до публикации (Р2; одна стена — только на `publish`). Проверка — предпроверка
  `deploy/deploy-remote.sh` проходит без `missing uiTexts key(s)`.
- **П-5. `.env` на бою:** `RETENTION__BOOKINGEVENTDAYS=1095` (и два новых срока, если отличаются от
  значений по умолчанию, чего быть не должно). **Т20-11:** `SubjectRequests__ResponseWorkingDays` на
  бою не задан или ≤ 10; в `legal.values.json` на боевой машине (заполняет заказчик к `publish`, Г-4)
  `СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ` = «10 рабочих дней».
- **П-6. Р4:** записать фактическое значение `RETENTION_DRY_RUN` на бою. Если `true`, сообщить
  заказчику, что сроки п. 13.2 не исполняются, пока он не переключит режим по процедуре DEPLOY §11.4.
- **П-7. LG6, существующие данные (Р7):** выполнить и приложить результат (только отчёт, без правок):

  ```sql
  -- Компании, владелец которых не держатель их аккаунта и не участник ДРУГОЙ компании этого аккаунта.
  -- Возможны законные случаи (управляющий, назначенный абонентом, П2) — решает заказчик.
  SELECT c."Id", c."Name", c."BillingAccountId", c."OwnerUserId",
         EXISTS (SELECT 1 FROM "SubscriptionChangeLogs" l
                 WHERE l."CompanyId" = c."Id" AND l."ChangeKind" = 4
                   AND l."Comment" LIKE '%ответственный не менялся%') AS "CameByTransferWithoutOwnerChange"
  FROM "Companies" c
  JOIN "BillingAccounts" a ON a."Id" = c."BillingAccountId"
  WHERE c."OwnerUserId" <> a."OwnerUserId"
    AND NOT EXISTS (SELECT 1 FROM "CompanyMembers" m
                    JOIN "Companies" c2 ON c2."Id" = m."CompanyId"
                    WHERE m."UserId" = c."OwnerUserId"
                      AND c2."BillingAccountId" = c."BillingAccountId"
                      AND c2."Id" <> c."Id");
  ```
- **П-8. nginx access-логи (Т20-06 п. 3):** на хосте проверить ротацию логов nginx
  (`/etc/logrotate.d/nginx`: период × `rotate` ≤ 90 дней; если логи пишет контейнер — его
  драйвер/ротацию) и записать фактический срок в DEPLOY.md (новый подпункт §11). Если срок больше 90
  дней — привести к 90 (п. 13.2: «технические журналы, содержащие IP-адреса, — 90 дней»).
- **П-9. Сверка фактов §7/§9 п. 3 заключения юриста:** `PHONEVERIFY_PROVIDER`, `Notifications:Provider`
  и `StaffPush:Provider` = `logging` (П-10 D1 правдив, только пока GREEN-API не включён).

**При выкате и сразу после:**

- **П-10.** Миграции M1 и M2 применились (лог старта). `SELECT count(*) FROM "ClientHealthNotes"` =
  0. `GET /api/admin/retention/policy` → `bookingEventDays: 1095`, `guestDataGateEventDays: 365`,
  `platformNoticeDays: 1095`. `GET /api/legal/texts/GuestDataGateNotice` → 200.
- **П-11. Публикация комплекта (`LegalKit publish`) — отдельный шаг после критериев Г SPEC**, не в
  окне выката. Процедура — `LEGAL_REVIEW_CYCLE20.md` §9, шаги 12–17.

**D7. Документ оператора (DEPLOY.md, новый подпункт):** как приложить будущую редакцию к уведомлению
`TermsChange` (`publish --out` во временный каталог, §404.4) и как вручную зарегистрировать обращение
(US-20-09).

---

## §414. Структура файлов: что появляется и где

```
ServiceBooking.Core/
  Entities/GuestDataGateEvent.cs                      НОВЫЙ
  Entities/PlatformNotice.cs                          НОВЫЙ
  Entities/PlatformNoticeAcknowledgement.cs           НОВЫЙ
  Entities/ConsentRecord.cs                           + FormId, RevokedByUserId
  Entities/SubscriptionChangeLog.cs                   + ReasonCode, ReasonDetails
  Entities/BillingAccount.cs                          + ConsentOperatorFullName/Address/Inn
  Entities/SubjectRequest.cs                          + Channel, RegisteredByUserId
  Enums/ConsentSource.cs                              + PaperForm (в конец)
  Enums/LegalTextKey.cs                               + 5 ключей, All
  Enums/SubscriptionChangeReason.cs                   НОВЫЙ
  Enums/PlatformNoticeKind.cs, NoticeAudienceType.cs  НОВЫЕ
  Enums/GuestDataGateOperation.cs (+ Outcome)         НОВЫЙ
  Enums/SubjectRequestChannel.cs                      НОВЫЙ
ServiceBooking.Infrastructure/
  Data/AppDbContext.cs                                конфигурация + индексы
  Migrations/<ts>_Cycle20LegalClosure.cs              M1, аддитивная
  Migrations/<ts>_Cycle20PurgeHealthNotesWithoutWrittenConsent.cs   M2, данные
ServiceBooking.API/
  Controllers/ClientConsentsController.cs             отметка, бланк, 410, новые правила поля
  Controllers/ProfileController.cs                    журнал гейта, каскад отзыва, 400 на HealthData
  Controllers/BillingController.cs                    operator-details
  Controllers/AdminBillingController.cs               причина, история, reasons
  Controllers/PlatformNoticesController.cs            НОВЫЙ  api/legal/notices
  Controllers/AdminNoticesController.cs               НОВЫЙ  api/admin/notices
  Controllers/CompanyPhotosController.cs              ?reason=DepictedPersonRequest
  Controllers/BookingsController.cs                   EffectiveCancelHours, clientCancelMinHours
  Controllers/AdminController.cs                      retention DTO, owner-change правило, subject-requests POST, gate events
  Controllers/CompanyTransferController.cs            confirmRightsTransfer, ownerChangeRequired
  DTOs/Billing/AdminBillingDtos.cs                    − AssignSubscriptionInput, − AssignOptionInput
  DTOs/Legal/PlatformNoticeDtos.cs                    НОВЫЙ
  Services/Legal/WrittenHealthConsentRevoker.cs       НОВЫЙ (БД)
  Services/Legal/WrittenHealthConsentTexts.cs         НОВЫЙ (константы)
  Services/Legal/HealthConsentFormId.cs               НОВЫЙ (чистый)
  Services/Legal/LegalSectionText.cs                  НОВЫЙ (чистый)
  Services/Legal/PlatformNoticeRules.cs               НОВЫЙ (чистый: сроки, матрица видов)
  Services/Legal/NoticeAudience.cs                    НОВЫЙ (чистый)
  Services/Legal/PlatformNoticeTexts.cs               НОВЫЙ (константы, «не переписывать без legal-counsel»)
  Services/Legal/PlatformNoticePublisher.cs           НОВЫЙ (БД)
  Services/Billing/ManualPlanAssignmentPolicy.cs      НОВЫЙ (чистый)
  Services/Billing/SubscriptionChangeReasonTexts.cs   НОВЫЙ
  Services/Billing/ConsentOperatorDetailsValidator.cs НОВЫЙ (чистый)
  Services/Billing/CompanyTransferService.cs          правило текущего владельца
  Services/Billing/TrialActivationService.cs          ReasonCode=TrialReissue при override
  Services/Bookings/ClientRescheduleWindow.cs         MaxEnforcedCancelHours, EffectiveCancelHours
  Services/Subjects/GuestDataGateJournal.cs           НОВЫЙ (единственный писатель)
  Services/Subjects/SubjectGateTexts.cs               ключ-константа, раздел «Текст»
  Services/Retention/RetentionPeriods.cs              BookingEventDays=1095, +2 срока
  Services/Retention/RetentionPlan.cs                 + 2 отсечки
  Services/Retention/Rules/GuestDataGateEventRule.cs  НОВЫЙ
  Services/Retention/Rules/PlatformNoticeRule.cs      НОВЫЙ
  Services/DeploymentSafetyChecks.cs                  BookingEventDays/GuestDataGateEventDays ≥ 1
  Program.cs                                          регистрация 2 правил и сервисов
  appsettings.json                                    Retention: 1095 / 365 / 1095
ServiceBooking.LegalKit/RuntimeValueScanner.cs        + 7 имён
legal-drafts/                                         коммит А (§412)
contracts/cycle20/openapi.yaml                        НОВЫЙ (источник истины по форме)
contracts/legal/runtime-value-forms.json              + knownNames
contracts/cycle11/legal-routes.json                   + /data-request
frontend/src/
  legal/staffNotices.ts                               НОВЫЙ (константы юриста)
  api/platformNotices.ts, api/adminNotices.ts         НОВЫЕ
  api/clientConsents.ts, api/billing.ts, api/adminBilling.ts, api/admin.ts, api/profile.ts   правки
  utils/noticeError.ts, utils/healthConsentError.ts   НОВЫЕ мапперы
  utils/legalRuntimeValues.ts                         + 7 имён
  components/legal/PlatformNoticeBanner.tsx           НОВЫЙ
  components/legal/PlatformNoticeList.tsx             НОВЫЙ (+ iframe вложения)
  pages/NoticesPage.tsx                               НОВЫЙ  /notices
  pages/HealthConsentFormPrintPage.tsx                НОВЫЙ  /companies/:companyId/clients/:clientKey/health-consent-form
  pages/admin/NoticesAdminTab.tsx                     НОВЫЙ
  components/clientNotes/HealthNoteCard.tsx, ClientConsentModal.tsx, NotePhotoUploader.tsx  правки
  pages/owner/CompanyPhotosSection.tsx, CompanyManagePage.tsx     правки
  pages/ClientBookingsPage.tsx, ProfilePage.tsx, DeleteAccountPage.tsx   правки
  pages/admin/BillingAccountsAdminTab.tsx, PlansTab.tsx, SubjectRequestsTab.tsx   правки
  types/api-cycle20.generated.ts                      генерат (npm run types:api:cycle20)
```

---

## §415. Порядок работ и параллельность

Точка синхронизации ранняя и одна: **`contracts/cycle20/openapi.yaml` + `API_CONTRACT_CYCLE20.md`
написаны**. Фронт работает от мока (`npx @stoplight/prism mock contracts/cycle20/openapi.yaml --port
4020`).

### §415.1 Дорожка правовых текстов (legal-counsel + backend-developer)

| # | Задача | Исполнитель | US / Т20 | Зависит от | Блокирует |
|---|---|---|---|---|---|
| **L1** | **Коммит А** (§412): вставки §1, файлы 14–18, `legal.json`, `LegalTextKey`, `KnownNames`, `LEGAL_RUNTIME_VALUE_NAMES`, корпус, `legal-routes.json`, `LegalKit build` | backend-developer | блок А, US-20-06, Т20-04 п. 2, Т20-08, Т20-11, Т20-12 | — | B2, B11, F2, F3, F8 (мёрж), приёмку |
| **L2** | Проверка диффа L1 и письменное подтверждение: побайтность, ничего из отложенного (В-12а/г, П1-4/8), геокодер не тронут, `href` → `/data-request` | legal-counsel | блок А, Р8 | L1 | приёмку |
| **L3** | Недостающие тексты: шаблоны заголовков `PriceChange`/`TermsChange`, текст `PhotoRemoved` (тело + заголовок), строка п. 13.2 D1 о сроке уведомлений и ознакомлений (1095 дней от конца видимости или иной — тогда меняется конфигурация) | legal-counsel | Т20-02, Т20-07, US-20-03 | — | B4 (тексты), B5; строка 13.2 — вторым коммитом в `legal-drafts` + `build` |
| **L4** | Блок В (`legal-internal/`): бланк (= §3), акты № 1, 3, 4 (№ 2 отложен), тексты РКН, регламент (с **ежедневной** проверкой почты, §12.4 SPEC), пакет для юриста, чек-лист публикации | legal-counsel | блок В | — | Г-2 |

### §415.2 Backend

| # | Задача | US / Т20 | Зависит от | Параллельно с |
|---|---|---|---|---|
| **B1** | Модель: 3 новые сущности, колонки в 4 таблицах, 6 перечислений, индексы, **M1** (аддитивная) и **M2** (очистка). Генерация — только закреплённым `dotnet-ef` (DEPLOY §10.2c, C15-3) | US-20-01/02/03/05/09 | — | L1, F* |
| **B2** | Бумажное согласие: `HasWrittenHealthConsentAsync`, поле, бланк (`GET …/health-consent-form`, `HealthConsentFormId`), отметка и снятие, `WrittenHealthConsentRevoker` + 4 входа отзыва, 410, `POST /api/profile/consents` 400 на `HealthData`, `operator-details` + валидатор | US-20-01, Т20-03, Т20-04 | B1, **L1** | B3–B10 |
| **B3** | Причина назначения: перечисление, `ManualPlanAssignmentPolicy`, `AssignSubscription`, `TrialActivationService` (override), история, `GET …/subscription-change-reasons`, удаление дубликатов DTO | US-20-02, Т20-01 | B1 | B2, B4–B10 |
| **B4** | Уведомления: `PlatformNoticeRules`, `NoticeAudience`, `PlatformNoticeTexts`, `PlatformNoticePublisher`, два контроллера (8 маршрутов, контракт §434), вложение с CSP, `PlatformNoticeRule`, срок в `RetentionPeriods` | US-20-03, Т20-02 | B1, L3 (только тексты шаблонов — до L3 стоят тексты Т20-02 п. 6, а заголовки временно помечены `// L3`, CI-шаг запрета заглушек цикла 17 должен их ловить) | B2, B3, B5–B10 |
| **B5** | `PhotoRemoved` при удалении фото суперадмином (`?reason=DepictedPersonRequest`) | Т20-07 п. 2 | B4, L3 | B6–B10 |
| **B6** | Предел отмены: `EffectiveCancelHours`, `Cancel`, `GetClientBookings`, `clientCancelMinHours`, дополнение `Cycle17ClientCancelTests` случаями «окно > 24» | US-20-04, Т20-05 | — | всё |
| **B7** | Сроки: `BookingEventDays=1095` (три места + DTO + safety check), журнал гейта (сущность в B1, писатель, 4 точки, правило, admin GET) | US-20-05, Т20-06 | B1 | всё |
| **B8** | LG6: текущий владелец при переносе, `confirmRightsTransfer`, `ownerChangeRequired`, правило на `PUT …/owner` | US-20-07, Т20-09 | — | всё |
| **B9** | TD16-1: `TestLegalManifest.PublishedTermsOwnerVersion`, семь `ReloadLegalNow()` | US-20-08, Т20-10 | **L1** (версии уже подняты — это и есть проверка) | всё |
| **B10** | Ручная регистрация обращений | US-20-09, Т20-13 | B1 | всё |
| **B11** | `SubjectGateTexts` на `LegalTextKey`, `LegalSectionText`, `explanation` — плоский раздел «Текст» | Т20-08 п. 2 | **L1** | всё |
| **B12** | Документация: `API_DOCUMENTATION.md` (все изменения §431, в т. ч. ломающее для админского фронта), `CHANGELOG.md`, `DEPLOY.md` (D7 + подпункт про nginx от devops) | US-20-02 | B2–B11 | — |

### §415.3 Frontend (параллельно с B, от мока)

| # | Задача | US / Т20 | Зависит от |
|---|---|---|---|
| **F1** | `types:api:cycle20` (скрипт в `package.json` + генерат), API-модули, мапперы `noticeError.ts`/`healthConsentError.ts`, `legal/staffNotices.ts` | все | контракт |
| **F2** | Поле «здоровье»: `HealthNoteCard` по `writtenConsent`; `ClientConsentModal` → «Распечатать бланк / Бланк подписан, оригинал у нас / Отмена» (разделы «Напоминание сотруднику», «Подтверждение»); диалог отметки (галочка + `formId`); снятие отметки с причиной и предупреждением об удалении записи; строка «бланк не фотографируйте» у поля и в `NotePhotoUploader`; **страница печати** (A4, ч/б, `print:`, `BLANK_LINE`, подсказка про реквизиты) | US-20-01, Т20-03/04 | F1; мёрж после L1 |
| **F3** | Профиль: `HealthData` не предлагается к выдаче; живая выдача — только «Отозвать» + «Текст для клиента»; подпись ключа отметки в истории; отзыв салонного согласия при `phoneVerified === false` → раздел 2 файла 16 без вызова API; после своего отзыва → раздел 1; удаление аккаунта → `GuestDataGateDeleteNotice`; выгрузка → раздел «Текст» `GuestDataGateNotice`. Реквизиты оператора — форма в «Ваша подписка» | US-20-01, Т20-03, Т20-04, Т20-08 | F1; мёрж после L1 |
| **F4** | `PlatformNoticeBanner` в `App.tsx`, `/notices`, блок в «Ваша подписка», вложение в `<iframe sandbox>`; доступность (`<button>`, `role="status"`) | US-20-03, Т20-02 | F1 |
| **F5** | Админка «Уведомления»: форма по виду (матрица §434.5), предпросмотр, список со счётчиками адресатов и ознакомившихся, отзыв | US-20-03 | F1 |
| **F6** | Админка биллинга: «Основание» + «Описание ошибки» при скрытом тарифе ≠ текущего; колонка основания в истории; подсказка на цене в `PlansTab` | US-20-02, US-20-03 | F1 |
| **F7** | Отмена: `clientCancelMinHours` в `ClientBookingsPage` и в модалке отмены; подпись поля в `CompanyManagePage` (Т20-05 п. 3, `aria-describedby`) | US-20-04 | F1 |
| **F8** | Фото салона: `CompanyPhotoPeopleNotice` до выбора файла; диалог причины удаления для суперадмина | US-20-06, Т20-07 | F1; мёрж после L1 |
| **F9** | Перенос компании: обязательная галочка «Права на компанию переходят к принимающему абоненту», показ `ownerChangeRequired`; 409 смены владельца — текст сервера дословно | US-20-07 | F1 |
| **F10** | Обращения: форма ручной регистрации (канал, дата поступления, вид, контакт, текст, телефон необязательно), колонка «Канал» | US-20-09 | F1 |

### §415.4 DevOps

D1–D3 — в ветке, параллельно всему (§413). П-1…П-11 — при выкате, после приёмки цикла.

### §415.5 Сходятся

**S1** — сборка против живого бэкенда, `schemathesis run contracts/cycle20/openapi.yaml --checks all`,
три набора тестов (qa-engineer; colima и `DOCKER_HOST` — SPEC §11). **S2** — grep-сверка наличия
(§417 последний блок) **до** объявления готовности (урок C18-13).

```
L1 ──▶ L2
 │
 ├──▶ B2, B11, B9 ──┐
B1 ──▶ B2, B3, B4, B7, B10 ─┤
L3 ──▶ B4(тексты) ──▶ B5 ──┤
B6, B8 ─────────────────────┼──▶ S1 ──▶ S2 ──▶ мёрж ──▶ П-1…П-10 (выкат) ──▶ … ──▶ П-11 (publish, после Г)
F1 ──▶ F2…F10 (мёрж F2/F3/F8 после L1) ┘
D1, D2, D3 ──────────────────┘
```

---

## §416. Риски и решения

| # | Риск | Решение |
|---|---|---|
| **Р1** | Цикл 19 не влит. Правки пересекаются в `legal-drafts/` (D1, D2, 13), в `legal.json` (версии, возможно номера файлов 14–18), в артефакте, в `.env.production.example`/`docker-compose.prod.yml` (строки геокодера), в `DeploymentSafetyChecks.cs`/`Program.cs` | Абзацы и код геокодера не трогать. При мёрже: в `legal-drafts` принимать **обе** стороны (у цикла 19 — удаление про геокодер, у цикла 20 — вставки); версии — **бо́льшая** дата (или `-2-draft`); номера файлов — сдвинуть свои, если заняты; артефакт **только пересобрать** (`LegalKit build`), не мёржить руками; в конфигах — удаление строк геокодера + добавление строк retention. Публикация — только после Г-5 |
| **Р2** | Подъём версий документов вызовет 451 раньше публикации | на бою при выкате меняются только uiTexts (П-4), версии `documents` — только на `publish`. Одна стена |
| **Р3** | Салоны потеряют доступ к полю «здоровье» | данные тестовые (§5 LEGAL_DECISIONS), переходного режима нет. Текст «что изменилось» — в CHANGELOG |
| **Р4** | `DataRetentionTask` в сухом режиме | П-6, решение заказчика |
| **Р5** | Соблазн переиспользовать `LegalUpdateBanner`/`ConsentGate` | отдельные сущности, существующие механизмы не менялись |
| **Р6** | Причина обязательна → сломать отказ «триал этой ручкой» | отказ цикла 18 стоит **до** политики причины; тест `CY20-B-02d` |
| **Р7** | Чужие компании уже на бою | П-7, отчёт заказчику |
| **Р8** | Требования юриста сверх SPEC | всё из Т20-01…Т20-13 уже в SPEC §12. Новых историй архитектура не вводит. Расхождения — вопросы О-1/О-2 (§418) |
| **Р9** | Зелёный прогон ≠ наличие | grep-лист §417 |
| **Р10** | M2 необратима, а реальный пользователь может появиться до выката | П-1 и П-2 |
| **Р11** | XSS через HTML вложения | `<iframe sandbox="">` + CSP на ответе + доступ только адресату |
| **Р12** | Отметка ставится без бумаги (дисциплина салона) | риск абонента, заверение D3 12.1 «н». Продукт снижает его текстом у кнопки и явной галочкой «оригинал у нас» |
| **Р13** | Заголовки/`PhotoRemoved` без текста юриста уедут в `develop` | L3 до мёржа. Константы с пометкой `// L3` ловит CI-шаг запрета заглушек (цикл 17): devops добавляет маркер в его шаблон |
| **Р14** | Нумерация §380–§399 занята циклом 19 | цикл 20 использует §400–§449 |

---

## §417. Тесты, которые обязаны появиться (вход для qa-engineer)

**Юнит (без БД):**
`CY20-U-01` `ClientRescheduleWindow.EffectiveCancelHours`: 48 → 24, 24 → 24, 2 → 2, 0 → 0, мусор 999 → 2.
`CY20-U-02` `ManualPlanAssignmentPolicy`: скрытый ≠ текущего без причины → ошибка; скрытый = текущему →
без причины ОК; публичный → ОК; `TrialReissue` → ошибка; `OperatorErrorCorrection` без описания →
ошибка; 1001 символ → ошибка.
`CY20-U-03` `PlatformNoticeRules`: `PriceChange` +29/+30 дн; `TermsChange` владельцы +14/+15, клиенты
+9/+10; `PriceChange` для `AllClients` → ошибка; `PhotoRemoved` через админку → ошибка; `Suspension`
не на `BillingAccount` → ошибка; граница суток по МСК.
`CY20-U-04` `NoticeAudience.Matches`: все 4 типа, Free-подписка в `OwnersOnPlans`, суперадмин не
адресат `AllClients`, управляющий не адресат владельческих.
`CY20-U-05` `PlatformNoticeTexts`: шаблоны `PriceChange`/`TermsChange` совпадают с Т20-02 п. 6 побайтно
(прибитый SHA-256); «Я ознакомился» и подпись.
`CY20-U-06` `HealthConsentFormId`: формат, алфавит без I/L/O/U, `IsValid`.
`CY20-U-07` `ConsentOperatorDetailsValidator`: ИНН 10/12, «Иванов И. И.» → ошибка, пустые → ОК.
`CY20-U-08` `LegalSectionText.PlainSection`: раздел «Текст» из файла 14, без служебной справки.
`CY20-U-09` Корпус `runtime-value-forms.json`: `knownNames` = `RuntimeValueScanner.KnownNames`
(и симметричный Vitest = `LEGAL_RUNTIME_VALUE_NAMES`).
`CY20-U-10` `DeploymentSafetyChecks`: `BookingEventDays = 0` → отказ; `GuestDataGateEventDays = 0` → отказ.
`CY20-U-11` `LegalTextKey.All` = ключи `uiTexts` в `legal-drafts/legal.json`.
`CY20-U-12` «Число в тексте = число в коде»: `appsettings.json` `BookingEventDays` 1095 ↔ «3 года» в
строке 13.2 D1 про журнал изменений записи; `GuestDataGateEventDays` 365 ↔ «1 год»;
`MaxEnforcedCancelHours` 24 ↔ «24 (двадцати четырёх) часов» в D2 10.3.1 и D3 7.6; 30/15/10 в
`PlatformNoticeRules` ↔ D3 6.13.8/16.2, D2 21.2; `SubjectRequests:ResponseWorkingDays` 10 ↔ «10 рабочих
дней» (НФТ §7).

**Функциональные (`ServiceBooking.Tests`, новый класс на US, `IClassFixture<TestDatabaseFixture>`):**
`CY20-B-01` бумажное согласие: (a) без отметки `PUT` → 400 + `requiredTextKey`; `GET` → `value: null`;
(b) электронное `PdnConsent/HealthData` и салонная форма поле **не** открывают; (c) отметка → `PUT` 200,
`GET` отдаёт значение и `writtenConsent`; (d) снятие сотрудником → поле закрыто, заметка удалена;
(e) повторная отметка = новая строка, старая строка не изменилась, кроме однократного `RevokedAt*`;
(f) SuperAdmin → 403 на всех четырёх; (g) другая компания отметку не видит; (h) `POST …/health-consent`
→ 410; (i) отзыв клиентом из профиля с подтверждённым номером снимает отметку и удаляет заметку, с
неподтверждённым — 400, отметка на месте; (j) отзыв `PdnConsent/HealthData` снимает отметки во всех
компаниях (при подтверждённом номере); (k) `POST /api/profile/consents` с `HealthData` → 400;
(l) бланк: `formId` валиден, `operatorDetailsMissing` true/false, `Cache-Control: no-store`, SuperAdmin
403; (m) `DELETE health-note` без отметки → 200; (n) после M2 таблица пуста (тест миграции на шаблонной
базе с заранее вставленной строкой); (o) двойная отметка с тем же `formId` и версией — одна строка.
`CY20-B-02` причина: (a) скрытый ≠ текущему без причины → 400, в БД ничего; (b) с причиной → журнал с
кодом, описанием, автором; (c) продление того же скрытого без причины → 200; (d) **триал этой ручкой →
409 `TrialPlanNotAssignableHere` и с причиной `TrialReissue`, и с `OperatorErrorCorrection`** (Р6);
(e) regrant → журнал `TrialReissue` + описание; (f) история отдаёт `reasonTitle`.
`CY20-B-03` уведомления: (a) создание `PriceChange` через 10 дней → 400, через 30 → 201; (b) адресат
видит в `pending`, чужой — нет; (c) ознакомление → 200, повтор → та же запись (одна строка);
(d) после ознакомления в `all`, не в `pending`; (e) отзыв → нет в `pending`, редактирования нет;
(f) счётчики адресатов/ознакомившихся; (g) вложение: адресату 200 с CSP, чужому 404;
(h) **пользователь с непринятой Material-редакцией получает 200 на `GET /api/legal/notices`**;
(i) `AllClients` видит клиент, не видит суперадмин; (j) `PhotoRemoved` создаётся при удалении фото
суперадмином с `reason`, не создаётся без `reason` и не создаётся для владельца.
`CY20-B-04` отмена: окно 48 ч, визит через 30 ч — отмена 204, перенос 400; визит через 20 ч — отмена
409, в тексте «24»; окно 2 ч — как раньше; `clientCancelMinHours` = min(окно, 24).
`CY20-B-05` сроки: `GET /api/admin/retention/policy` отдаёт 1095/365/1095; каждое из четырёх
срабатываний гейта пишет одну строку журнала с правильной `Operation`; в строке нет телефона (проверка
всех колонок); правило удаляет запись старше 365 дней в live-режиме; `BookingEventRule` удаляет событие
старше 1095 дней; журнал не попадает в выгрузку.
`CY20-B-06` тексты гейта и новые uiTexts (Т20-08): `GET /api/legal/texts/{key}` → 200 для пяти новых
ключей; `GET /api/profile/export` при сработавшем гейте отдаёт в `guestDataGate.explanation` плоский
текст раздела «Текст» — без HTML-тегов и без служебной справки; `SubjectGateTexts.Fallback` при
загруженном манифесте не используется.
`CY20-B-07` LG6: перенос без смены владельца с несвязанным владельцем → 409 и превью с
`ownerChangeRequired`; со связанным → 204; без `confirmRightsTransfer` → 400; `PUT …/owner` на
несвязанного → 409, на сотрудника компании → 204; журнал содержит отметку о подтверждении;
(f) `POST /api/companies` кладёт компанию в аккаунт самого создателя (Т20-09 п. 2).
`CY20-B-08` `LegalPricingGateTests` без `Task.Delay`, стабильно 20 прогонов подряд.
`CY20-B-09` обращения: ручная регистрация → в списке, `dueAt` от даты поступления, канал; дата в будущем
→ 400; `WebForm` → 400.

**Фронт (Vitest):** баннер (кнопка, подпись, исчезает после ознакомления), iframe с `sandbox=""`, страница
печати (`BLANK_LINE` вместо пустых, нет полей для паспорта), `HealthNoteCard` по `writtenConsent`,
`ClientBookingsPage` по `clientCancelMinHours`, форма причины появляется только для скрытого ≠ текущего,
галочка переноса обязательна, подсказка о фото до выбора файла.

**grep-сверка наличия до объявления готовности (Р9, урок C18-13):**
`MaxEnforcedCancelHours` = 24; `BookingEventDays` 1095 в `appsettings.json`, `.env.production.example`,
`docker-compose.prod.yml`; пять ключей в `legal-drafts/legal.json`, `App_Data/legal/legal.json` и
`LegalTextKey`; `GuestDataGateEventRule` и `PlatformNoticeRule` зарегистрированы в `Program.cs`;
`class PlatformNoticeAcknowledgement`; `SubscriptionChangeReason`; `PaperForm`; `HasWrittenHealthConsentAsync`
и отсутствие `HasHealthConsentAsync`; `ls ServiceBooking.Infrastructure/Migrations | grep Cycle20` — 2
миграции (+2 `.Designer`); в `BookingsController.Cancel` и в расчёте `cancelAllowed` в
`GetClientBookings` нет `ClientRescheduleWindow.Normalize(` — только `EffectiveCancelHours(` (§405).

---

## §418. Вопросы заказчику, которые архитектура не решает сама (не блокируют цикл)

| # | Вопрос | Что сделано по умолчанию | Цена альтернативы |
|---|---|---|---|
| **О-1** | D3 6.13.15.4 (В-6) требует основания для **любого** изменения подписки без заявки, а П3 — только для смены на другой скрытый тариф. Назначение публичного тарифа без `requestId` и правка опций без заявки кодом не ограничены | как П3/SPEC | одна строка в `ManualPlanAssignmentPolicy.RequiresReason` + тесты |
| **О-2** | Держатель аккаунта, у которого компании ведут управляющие, может удалить свою учётную запись, и аккаунт остаётся без абонента (D3 6.1.5) | не закрывается | один `AnyAsync` + 409 в `DeleteAccount` + текст |
| **О-3** | Строка п. 13.2 D1 о сроке хранения уведомлений и ознакомлений отсутствует | задача L3, срок 1095 дней от конца видимости | меняется одной настройкой |

---

## §419. Совместимость и наблюдаемость

- **Ломающее для админского фронта:** обязательная причина на `PUT …/subscription` (US-20-02),
  обязательный `confirmRightsTransfer` на переносе, 409 на `PUT …/owner`. Внешних потребителей нет:
  маршруты админские.
- **Поведенческое для персонала:** поле «здоровье» открывается только отметкой. `POST …/health-consent`
  → 410.
- **В пользу клиента:** отмена не ранее 24 ч.
- Публичный API — только аддитивно: `clientCancelMinHours`, `writtenConsent`, новые маршруты.
- Логи: отметка, снятие, создание и отзыв уведомления, ручное обращение — Information без ПДн (id
  сущностей и пользователей, без телефонов, текстов и `formId`). Сбой журнала гейта — Error. Ни
  журнал гейта, ни логи не содержат IP.
