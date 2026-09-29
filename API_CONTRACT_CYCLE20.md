# API_CONTRACT — цикл 20 ServiceBooking: закрытие правовых вопросов

**Разделы §430–§445.** Читается вместе с машиночитаемой схемой **`contracts/cycle20/openapi.yaml`**.
**При расхождении прав YAML:** он источник истины по **форме**, этот файл — по **смыслу и правилам**.
Решения — `ARCHITECTURE_CYCLE20.md` (§400–§419), требования — `SPEC_CYCLE20_LEGAL_CLOSURE.md` (§12 имеет
приоритет над §1–11) и `LEGAL_REVIEW_CYCLE20.md` §2 (Т20-01…Т20-13).

Контракт описывает **фактические существующие маршруты** в той форме, в какой они есть на `develop`
(`e3774c1`), и то, что цикл к ним добавляет. Новые маршруты помечены **НОВЫЙ**. Остальной API — в
`API_DOCUMENTATION.md` и контрактах циклов 3–18.

```bash
# фронт — мок до появления бэкенда
npx @stoplight/prism mock contracts/cycle20/openapi.yaml --port 4020
# фронт — типы (генерат в git, руками не правится)
cd frontend && npm run types:api:cycle20
#   package.json: "types:api:cycle20": "openapi-typescript ../contracts/cycle20/openapi.yaml -o src/types/api-cycle20.generated.ts"
# QA — автосверка живого бэкенда со схемой
schemathesis run contracts/cycle20/openapi.yaml --base-url http://localhost:5000 --checks all
# CI — линт схемы и сверка генерата (devops, задача D1)
npx @redocly/cli lint --config ../contracts/redocly.yaml ../contracts/cycle20/openapi.yaml
npm run types:api:cycle20 && git diff --exit-code -- src/types/api-cycle20.generated.ts
```

---

## §430. Конвенции, действующие без изменений

* `camelCase` в JSON; `enum` — строками (имена членов C#); `DateTime` — ISO-8601 UTC; `DateOnly` —
  `yyyy-MM-dd`.
* Тела осознанных ошибок **400/402/409/410/429** — **голая строка** `text/plain` по-русски; **401/403** —
  пустое тело; **404** — пустое тело (`SuppressMapClientErrors = true`), кроме существующего «Тариф не
  найден.» у `PUT …/subscription`. `ProblemDetails` не используется.
* **Существующие исключения из «4xx — строка», которые цикл сохраняет:** `RequiredConsentDto`
  (`400` JSON `{ message, requiredTextKey }` у `PUT …/health-note`, цикл 5) и `TrialRefusalDto`
  (`409` JSON `{ code, message }` у `PUT …/subscription` для тарифа-триала, цикл 18). Новых исключений
  цикл не вводит.
* **451** — «требуется принять новую редакцию документов» (`LegalConsentFilter`). Новые маршруты по
  умолчанию закрыты гейтом. Маршруты `/api/legal/notices*` лежат под префиксом `/api/legal/`, который
  уже в allow-list, — **это намеренно** (§434.1, Т20-02 п. 4). В allow-list больше ничего не
  добавляется.
* `[RequiresOwnerTerms]` (цикл 12, `OwnerScope`) — **451** у владельческих действий до принятия
  `TermsOwner`. Цикл ставит его на одно новое действие — отметку о бумажном согласии (§432.5).
* **503** со строкой «Правовые документы временно недоступны.» — если снимок правовых документов не
  загружен (существующее поведение всех маршрутов, читающих тексты).
* Текст, который видит человек, собирает **сервер**; фронт печатает его дословно и ветвится по
  машиночитаемому полю. Тексты юриста — только из манифеста или из файлов-констант «не переписывать
  без legal-counsel» (`PlatformNoticeTexts.cs`, `WrittenHealthConsentTexts.cs`,
  `frontend/src/legal/staffNotices.ts`).
* Все новые поля существующих DTO — **в конец** позиционных record, `null` у вычисляемого поля =
  «сервер не считал это для данного вызова».

---

## §431. Сводка: что цикл добавляет и меняет

| Маршрут | Что | US / Т20 |
|---|---|---|
| `GET /api/companies/{companyId}/clients/{clientKey}/health-note` | поле открывается **только** отметкой; `+writtenConsent` | US-20-01, Т20-03 |
| `PUT …/health-note` | `400 RequiredConsentDto` теперь с `requiredTextKey: "HealthDataWrittenConsentForm"` | US-20-01 |
| `DELETE …/health-note` | не меняется: работает **и без отметки** | US-20-01 |
| `GET …/health-consent-form` | **НОВЫЙ.** Значения для печати бланка + `formId` | US-20-01, Т20-04 |
| `POST …/health-written-consent` | **НОВЫЙ.** Отметка «письменное согласие получено» | US-20-01, Т20-03 |
| `POST …/health-written-consent/revoke` | **НОВЫЙ.** Снятие отметки сотрудником + удаление заметки | US-20-01, Т20-03 п. 4 |
| `POST …/health-consent` | **410 Gone** (салонная электронная форма выведена) | US-20-01 |
| `GET\|PUT /api/billing/operator-details` | **НОВЫЙ.** Реквизиты оператора для бланка (держатель аккаунта) | Т20-04 п. 3 |
| `POST /api/profile/consents` | `400`, если в `purposes` есть `HealthData` | Т20-03 п. 3 |
| `POST /api/profile/consents/revoke` | `documentKey` принимает `HealthDataWrittenConsentForm`; отзыв `HealthDataConsent`/`HealthData` снимает и отметки | US-20-01 |
| `PUT /api/admin/billing-accounts/{accountId}/subscription` | `+reasonCode`, `+reasonDetails`; **ломающее для админского фронта**: без причины при скрытом тарифе ≠ текущего — `400` | US-20-02, Т20-01 |
| `GET /api/admin/billing-accounts/{accountId}/subscription-history` | `+reasonCode`, `+reasonTitle`, `+reasonDetails` | US-20-02 |
| `GET /api/admin/subscription-change-reasons` | **НОВЫЙ.** Закрытый список оснований | US-20-02 |
| `GET /api/legal/notices` | **НОВЫЙ.** Уведомления вызывающему (`scope=pending\|all`) | US-20-03, Т20-02 |
| `POST /api/legal/notices/{id}/acknowledge` | **НОВЫЙ.** «Я ознакомился» | US-20-03 |
| `GET /api/legal/notices/{id}/attachment` | **НОВЫЙ.** Снимок будущей редакции (`text/html`, sandbox) | Т20-02 п. 5 |
| `GET\|POST /api/admin/notices` | **НОВЫЙ.** Список со счётчиками / публикация | US-20-03 |
| `POST /api/admin/notices/preview` | **НОВЫЙ.** Предпросмотр без записи | US-20-03 |
| `POST /api/admin/notices/{id}/revoke` | **НОВЫЙ.** Отзыв | US-20-03 |
| `GET /api/admin/notices/{id}/attachment` | **НОВЫЙ.** Вложение для суперадмина | US-20-03 |
| `DELETE /api/companies/{id}/photos/{photoId}` | `+?reason=DepictedPersonRequest` (только SuperAdmin) → уведомление `PhotoRemoved` | Т20-07 п. 2 |
| `GET /api/bookings/client` | `+clientCancelMinHours`; `clientCancelAllowed` считается с пределом 24 ч | US-20-04, Т20-05 |
| `PATCH /api/bookings/{id}/cancel` | `409` — по пределу 24 ч, текст называет **применённое** число | US-20-04 |
| `GET /api/admin/retention/policy` | `+bookingEventDays`, `+guestDataGateEventDays`, `+platformNoticeDays` | US-20-05 |
| `GET /api/admin/guest-data-gate-events` | **НОВЫЙ.** Журнал срабатываний гейта (1 год, без IP) | US-20-05, Т20-06 |
| `GET /api/profile/export` | `guestDataGate.explanation` — плоский текст раздела «Текст» из манифеста | Т20-08 |
| `GET /api/legal/texts/{key}` | пять новых ключей | Т20-08 |
| `GET /api/admin/companies/{companyId}/transfer/preview` | `+ownerChangeRequired`; несвязанный текущий владелец → `canTransfer: false` | US-20-07 |
| `POST /api/admin/companies/{companyId}/transfer` | `+confirmRightsTransfer` (обязателен); `409` при несвязанном владельце | US-20-07, Т20-09 |
| `PUT /api/admin/companies/{id}/owner` | `409`, если новый владелец не связан с аккаунтом компании | US-20-07 |
| `POST /api/admin/subject-requests` | **НОВЫЙ.** Ручная регистрация обращения | US-20-09, Т20-13 |
| `GET /api/admin/subject-requests` | `+channel`, `+registeredByName` | US-20-09 |

Итого **14 новых маршрутов** (5 — US-20-01, 1 — US-20-02, 8 — US-20-03; плюс 2 служебных админских:
журнал гейта и ручное обращение — всего 16 операций, считая `GET`/`PUT` operator-details и
`GET`/`POST` admin/notices раздельно). `API_DOCUMENTATION.md` дополняется всеми (задача B12).

---

## §432. US-20-01 — бумажное согласие на сведения о здоровье

Общее для всех маршрутов раздела: `[Authorize]`; доступ — сотрудник компании
(`CompanyMembership.IsStaffAsync`); **SuperAdmin → 403** всегда (правило §48.2 цикла 5 не
ослабляется); `{clientKey}` — `userId` зарегистрированного клиента или `phone:<номер>` гостя; клиент,
не связанный с компанией записью, → **404** (существование чужого клиента не подтверждается).

### §432.1 `GET …/health-note`

```json
{
  "value": "Аллергия на латекс",
  "updatedAt": "2026-09-29T10:15:00Z",
  "updatedBy": "Анна Смирнова",
  "consentRequired": false,
  "writtenConsent": {
    "granted": true,
    "recordId": "0f5e…",
    "confirmedAt": "2026-09-29T10:10:00Z",
    "confirmedByName": "Анна Смирнова",
    "formId": "HD-7K3M9QTX",
    "formVersion": "2026-09-28-draft",
    "currentFormVersion": "2026-09-28-draft"
  }
}
```

| Состояние | `value` / `updatedAt` / `updatedBy` | `consentRequired` | `writtenConsent.granted` |
|---|---|---|---|
| отметки нет | **всегда `null`**, даже если строка в БД есть (защита от гонки) | `true` | `false` |
| отметка есть, заметки нет | `null` | `false` | `true` |
| отметка есть, заметка есть | значение | `false` | `true` |
| отметка есть, расшифровка не удалась | `"(данные недоступны, обратитесь к платформе)"` (как сейчас) | `false` | `true` |

`writtenConsent` приходит **всегда** (объект, не `null`). При `granted: false` все поля, кроме
`currentFormVersion`, — `null`. `currentFormVersion` — текущая версия uiText
`HealthDataWrittenConsentForm`; `formVersion ≠ currentFormVersion` фронт показывает как «бланк
подписан по прежней редакции», но поле **не закрывает** (отметка действует).

🔴 Электронное согласие — салонная форма `HealthDataConsent` и цель `PdnConsent/HealthData` —
`consentRequired` больше **не снимает**.

Коды: **200**, **401**, **403**, **404**, **451**.

### §432.2 `PUT …/health-note`

Тело прежнее: `{ "value": "…" }`. `[RequiresOwnerTerms]` — как было.

| Код | Тело | Когда |
|---|---|---|
| **200** | пустое | записано |
| **400** | строка | значение пустое или длиннее 2000 |
| **400** | `RequiredConsentDto` JSON | отметки нет: `{ "message": "Для заполнения этого поля нужно письменное согласие клиента: распечатайте бланк, получите подпись и отметьте получение.", "requiredTextKey": "HealthDataWrittenConsentForm" }` |
| **401 / 403 / 404 / 451** | пустое | как у §432.1; 451 — в т. ч. непринятый `TermsOwner` |

Фронт различает два 400 по `Content-Type` (маппер `utils/healthConsentError.ts`), а не по тексту.

### §432.3 `DELETE …/health-note`

Не меняется: **200** идемпотентно, **и при отсутствии отметки** (удаление уменьшает обработку
спецкатегории — `ARCHITECTURE_CYCLE20.md` §402.3).

### §432.4 `GET …/health-consent-form` — НОВЫЙ

Значения для печатной страницы бланка. Сам текст бланка фронт берёт существующим
`GET /api/legal/texts/HealthDataWrittenConsentForm` и подставляет значения через
`applyLegalRuntimeValues` (механизм циклов 15/17).

```json
{
  "textKey": "HealthDataWrittenConsentForm",
  "textVersion": "2026-09-28-draft",
  "formId": "HD-7K3M9QTX",
  "formPrintedDate": "2026-09-29",
  "runtimeValues": {
    "clientFullName": "Мария Иванова",
    "companyName": "Студия «Лотос»",
    "companyAddress": "г. Барнаул, ул. Ленина, 1",
    "operatorFullName": null,
    "operatorAddress": null,
    "operatorInn": null,
    "formId": "HD-7K3M9QTX",
    "formPrintedDate": "29.09.2026"
  },
  "operatorDetailsMissing": true
}
```

* `formId` — **новый на каждый запрос** (`HD-` + 8 символов Crockford base32, без I/L/O/U), на
  сервере **не хранится** до отметки. Фронт запоминает последний напечатанный `formId` в состоянии
  страницы и подставляет его в диалог отметки.
* `runtimeValues` — ровно семь новых имён плюс существующее `companyName`; ключи совпадают с
  `data-legal-value` файла 17. `null` = «в системе нет»: фронт подставляет `BLANK_LINE`
  (`'_'.repeat(32)`) — строка печатается линией для заполнения от руки, а не пропадает.
* `clientFullName`: зарегистрированный — `FirstName LastName`; гость — `GuestName` последней записи в
  этой компании.
* `operatorDetailsMissing` — `true`, если на биллинг-аккаунте компании не заполнены ФИО **или** адрес
  оператора (ИНН необязателен). Фронт показывает подсказку «заполните реквизиты в разделе „Ваша
  подписка“» (владельцу — со ссылкой, управляющему — текстом).
* 🔴 **Полей паспорта нет** ни в ответе, ни в каком-либо запросе цикла.
* Заголовок ответа **`Cache-Control: no-store`**. Тело в лог не пишется.

Коды: **200**, **401**, **403**, **404**, **451**, **503**.

### §432.5 `POST …/health-written-consent` — НОВЫЙ

`[RequiresOwnerTerms]` (отметка — заверение абонента по D3 п. 12.1 «н»).

```json
{ "textVersion": "2026-09-28-draft", "formId": "HD-7K3M9QTX", "confirmed": true }
```

| Поле | Правило |
|---|---|
| `textVersion` | обязателен; должен совпасть с текущей версией uiText, иначе **409** «Текст бланка обновлён — распечатайте бланк заново и отметьте получение по новой редакции.» |
| `formId` | `null` — салон использовал собственный бланк; иначе формат `^HD-[0-9A-HJKMNP-TV-Z]{8}$`, иначе **400**. Существование не проверяется (хранить нечего) |
| `confirmed` | обязан быть `true` («подписанный оригинал у нас»), иначе **400** |

**200** — `WrittenHealthConsentStateDto` (та же форма, что `writtenConsent` в §432.1).
**Идемпотентность:** живая отметка с тем же `formId` и той же версией уже есть → **200** с текущим
состоянием, новая строка не пишется. Иначе пишется новая строка журнала `ConsentRecord`
(`Source = PaperForm`, без IP и User-Agent).

Коды: **200**, **400**, **401**, **403**, **404**, **409**, **451**, **503**.

### §432.6 `POST …/health-written-consent/revoke` — НОВЫЙ

```json
{ "reason": "SubjectWithdrew" }
```

`reason` — `SubjectWithdrew` (клиент отозвал письменно) | `MarkedByMistake` (отметка ошибочна).
Неизвестное значение — **400**.

```json
{ "revoked": 1, "healthNotesDeleted": 1, "writtenConsent": { "granted": false, "recordId": null, "confirmedAt": null, "confirmedByName": null, "formId": null, "formVersion": null, "currentFormVersion": "2026-09-28-draft" } }
```

Снимает **все** живые отметки клиента в этой компании и **в той же транзакции** удаляет его заметку о
здоровье в этой компании. Идемпотентно: нечего снимать → **200** с `revoked: 0`. Фронт до вызова
предупреждает: «запись о здоровье будет удалена без возможности восстановления».
`[RequiresOwnerTerms]` **нет** (снятие уменьшает обработку).

Коды: **200**, **400**, **401**, **403**, **404**, **451**.

### §432.7 `POST …/health-consent` — выведен, **410 Gone**

Тело — строка: «Согласие на обработку сведений о здоровье теперь оформляется на бумажном бланке.
Распечатайте бланк в карточке клиента и отметьте получение подписанного экземпляра.» Проверки прав
до 410 не выполняются (маршрут выведен целиком, как у цикла 7). `GET|POST …/photo-consent` не
меняются.

### §432.8 `GET|PUT /api/billing/operator-details` — НОВЫЙ

Реквизиты оператора ПДн для бланка (Т20-04 п. 3, D1 П-13). Доступ — **держатель** биллинг-аккаунта
(`BillingAccount.OwnerUserId == caller`); у кого аккаунта нет — **404**.

```json
{ "fullName": "Иванова Мария Сергеевна", "address": "г. Барнаул, ул. Ленина, 1", "inn": "222500000000", "missing": false }
```

`PUT` — тело `{ fullName, address, inn }`, все три nullable; пустая строка и строка из пробелов =
`null`. Ответ **200** — та же форма.

| Поле | Проверка (400 со строкой) |
|---|---|
| `fullName` | ≤ 300; **инициалы не принимаются** (ч. 4 ст. 9 152-ФЗ): «Иванов И. И.» → 400 «Укажите фамилию, имя и отчество полностью» |
| `address` | ≤ 500 |
| `inn` | 10 или 12 цифр |

`missing` = `fullName == null || address == null` (то же правило, что `operatorDetailsMissing` в
§432.4). Заполнять **не обязательно**: бланк печатается и без реквизитов.

Коды: **200**, **400** (только `PUT`), **401**, **404**, **451**.

### §432.9 Профиль клиента

**`POST /api/profile/consents`** (allow-list 451, форма прежняя): `HealthData` в `purposes` → **400**
«Согласие на обработку сведений о здоровье даётся в салоне на бумажном бланке.» Остальные цели — как
было. Цель в манифесте остаётся — ради отзыва ранее данных.

**`POST /api/profile/consents/revoke`** (allow-list 451, форма тела и ответа прежняя):

| `documentKey` | `companyId` | Что снимается | Гейт цикла 16 |
|---|---|---|---|
| `HealthDataWrittenConsentForm` — **новое значение** | обязателен | все отметки в этой компании + заметка там же | да: без подтверждённого номера — прежний **400**, ничего не снимается |
| `HealthDataConsent` | обязателен | салонная электронная запись **и** отметки в этой компании + заметка | да, как выше |
| `PdnConsent` с `purpose: "HealthData"` или без `purpose` | — | выдача в профиле **и** отметки во **всех** компаниях + заметки | да: при неподтверждённом номере отметки по телефону не трогаются |
| `PhotoConsent` | обязателен | без изменений | как было |

`effects.healthNotesDeleted` учитывает удалённые заметки. Иные `documentKey` — прежний **400**.
Текст ответа не раскрывает, существовали ли данные (правило «ответ не оракул», §272 цикла 16).

**`GET /api/profile/consents`** — форма не меняется; в `history` появляются записи с
`documentKey: "HealthDataWrittenConsentForm"` и `source: "PaperForm"`. Подпись для людей — во
фронтовом словаре ключей.

---

## §433. US-20-02 — основание ручного назначения тарифа

### §433.1 `PUT /api/admin/billing-accounts/{accountId}/subscription`

Тело — существующее `Billing_AssignSubscriptionInput` плюс два поля **в конец**:

```json
{
  "planId": "5b1c…", "isActive": true, "paidUntil": "2026-12-31",
  "options": [], "amount": null, "comment": null, "requestId": null, "confirmLimitOverflow": false,
  "reasonCode": "OperatorErrorCorrection",
  "reasonDetails": "Оплата от 12.09 прошла, но тариф не был применён из-за сбоя импорта"
}
```

Порядок проверок (часть контракта):
1. существующие (дата, `paidUntil`, заявка, тариф не найден → 404 строка);
2. **тариф-триал → 409 `TrialRefusalDto { code: "TrialPlanNotAssignableHere" }`** — первым, при любой
   причине (Р6);
3. `ManualPlanAssignmentPolicy`: причина обязательна, когда целевой тариф `isPublic: false` **и**
   отличается от текущего (П3). Правила:

| Условие | Ответ |
|---|---|
| причина обязательна, `reasonCode` нет | **400** «Для назначения скрытого тарифа укажите основание из списка.» |
| `reasonCode: "TrialReissue"` | **400** «Перевыдача пробного периода делается только через «Выдать повторно» в блоке «Пробный период».» |
| `reasonCode: "OperatorErrorCorrection"`, `reasonDetails` пуст | **400** «Опишите исправляемую ошибку.» |
| `reasonDetails` длиннее 1000 | **400** |
| неизвестный `reasonCode` | **400** (биндинг модели) |

4. остальные существующие проверки (опции, лимиты — 409 строкой).

Причина, присланная там, где она не обязательна (публичный тариф, продление того же скрытого),
**принимается и пишется** в журнал. Ответ **200** — карточка аккаунта (форма прежняя).

🔴 **Ломающее изменение для админского фронта** (§442): старый клиент без `reasonCode` получит 400 при
назначении скрытого тарифа, отличного от текущего.

### §433.2 `GET …/subscription-history`

Элемент `items[]` получает три поля:

```json
{ "reasonCode": "OperatorErrorCorrection", "reasonTitle": "Исправление ошибки оператора", "reasonDetails": "Оплата от 12.09 …" }
```

У записей без причины все три — `null`. Повторная выдача триала (`TrialGranted` с
`grantSource: SuperAdminOverride`) — `reasonCode: "TrialReissue"`, `reasonDetails` = причина из
`regrant`. Обычная выдача и автопереход «триал → Free» причины не имеют.

### §433.3 `GET /api/admin/subscription-change-reasons` — НОВЫЙ

`[Authorize(Roles = "SuperAdmin")]`. Источник подписей для выпадающего списка — фронт своих не держит.

```json
{
  "items": [
    { "code": "OperatorErrorCorrection", "title": "Исправление ошибки оператора", "detailsRequired": true, "assignableManually": true },
    { "code": "TrialReissue", "title": "Перевыдача пробного периода", "detailsRequired": true, "assignableManually": false }
  ]
}
```

В выпадающем списке экрана назначения — только `assignableManually: true`. `TrialReissue` в списке
есть, чтобы история показывалась теми же подписями.

### §433.4 `POST …/trial/regrant` — форма не меняется

Тело `{ reason }` обязательно, как в цикле 18. Меняется только запись в журнале (§433.2).

---

## §434. US-20-03 — уведомления в кабинете с фиксацией ознакомления

### §434.1 `GET /api/legal/notices?scope=pending|all` — НОВЫЙ

`[Authorize]`. **В allow-list 451 по префиксу** `/api/legal/` — читается и до принятия новой редакции,
и в период приостановления (Т20-02 п. 4, D3 п. 15.2). Не зависит от тарифа (402 нет) и от активности
компании. `scope` по умолчанию `pending`; иное значение → **400**.

```json
{
  "acknowledgeButtonText": "Я ознакомился",
  "acknowledgeCaption": "Это подтверждает только то, что вы прочитали сообщение, а не согласие с ним.",
  "items": [
    {
      "id": "c2a7…",
      "kind": "PriceChange",
      "title": "Изменение цены тарифа «Бизнес»",
      "body": "С 01.11.2026 меняется цена «Бизнес»: было 990 ₽, станет 1 190 ₽ за месяц. …",
      "linkUrl": null,
      "effectiveFrom": "2026-11-01",
      "publishedAt": "2026-09-29T09:00:00Z",
      "visibleUntil": "2027-09-29T09:00:00Z",
      "attachment": null,
      "acknowledged": false,
      "acknowledgedAt": null,
      "revokedAt": null
    }
  ]
}
```

* `pending` — видимые (`visibleUntil > now`), не отозванные, **без** ознакомления вызывающего. Это
  источник баннера: один запрос при входе.
* `all` — все видимые вызывающему, включая прочитанные и отозванные (у отозванных `revokedAt` не
  `null`, баннер их не показывает). Источник раздела `/notices` и блока в «Ваша подписка».
* `attachment` — `null` или `{ "title": "Соглашение с компанией, редакция от 01.11.2026", "sha256": "…" }`;
  сам HTML — §434.3.
* Адресат вычисляется при чтении (`ARCHITECTURE_CYCLE20.md` §404.2): владельческие виды —
  держателю биллинг-аккаунта (не управляющему), `AllClients` — любому, кроме SuperAdmin.
* Сортировка: `publishedAt` по убыванию. Пусто — `items: []`, не 404.

Коды: **200**, **400**, **401**.

### §434.2 `POST /api/legal/notices/{id}/acknowledge` — НОВЫЙ

`[Authorize]`, тела нет. Записывает ознакомление (`userId`, биллинг-аккаунт — для владельческих видов,
время UTC). **Идемпотентно:** повтор → **200**, новой записи нет, `acknowledgedAt` не двигается.

| Код | Тело | Когда |
|---|---|---|
| **200** | `PlatformNoticeDto` с `acknowledged: true` | записано или уже было |
| **404** | пустое | уведомления нет, срок видимости вышел или вызывающий не адресат (не различаются) |
| **409** | строка | уведомление отозвано |
| **401** | пустое | нет токена |

### §434.3 `GET /api/legal/notices/{id}/attachment` — НОВЫЙ

`[Authorize]`, только адресату. **200** `text/html; charset=utf-8` с заголовками
`Content-Security-Policy: sandbox; default-src 'none'; style-src 'unsafe-inline'`,
`X-Content-Type-Options: nosniff`, `Cache-Control: no-store`. Фронт получает текст через `api` (с
токеном) и показывает **только** в `<iframe sandbox="" srcdoc=…>` — ни `dangerouslySetInnerHTML`, ни
`sandbox="allow-scripts"`. **404** — нет вложения, не адресат, срок вышел. **401**.

### §434.4 `GET /api/admin/notices` — НОВЫЙ

`[Authorize(Roles = "SuperAdmin")]`. Параметры `kind`, `page`, `pageSize`. Ответ — `PagedResult`
`{ items, page, pageSize, total, hasNext }`, элемент — `AdminPlatformNoticeDto`: все поля
`PlatformNoticeDto` (кроме `acknowledged*`) плюс `audienceType`, `audiencePlanIds`,
`targetBillingAccountId`, `templateVersion`, `createdByName` («Система» для `PhotoRemoved`),
`revokedByName`, `revokeReason`, **`audienceCount`** (текущее число адресатов) и
**`acknowledgedCount`**.

### §434.5 `POST /api/admin/notices` — НОВЫЙ, публикация; матрица видов

`[Authorize(Roles = "SuperAdmin")]`. Опубликованное **не редактируется** (маршрута `PUT` нет).

```json
{
  "kind": "TermsChange",
  "audience": { "type": "AllOwners", "planIds": null, "billingAccountId": null },
  "effectiveFrom": "2026-10-20",
  "title": null,
  "body": null,
  "linkUrl": "/legal/terms-owner",
  "priceChange": null,
  "termsChange": { "documentType": "TermsOwner", "changesSummary": "уточнён порядок уведомлений в кабинете" },
  "attachment": { "title": "Соглашение с компанией, редакция от 20.10.2026", "html": "<!doctype html>…" }
}
```

**Матрица видов** (правило `PlatformNoticeRules`; «сегодня» — дата по `Europe/Moscow`):

| `kind` | `title` / `body` | Параметры | `effectiveFrom` | Адресаты | `attachment` |
|---|---|---|---|---|---|
| `PriceChange` | **собирает сервер** по шаблону Т20-02 п. 6; присланные → 400 | `priceChange { planId, oldPricePerMonth, newPricePerMonth }` обязателен | обязательна, **≥ сегодня + 30** | `AllOwners`, `OwnersOnPlans`, `BillingAccount` | необязательно |
| `TermsChange` | **собирает сервер**; присланные → 400 | `termsChange { documentType, changesSummary ≤ 1000 }` обязателен | обязательна, **≥ +15** для владельческих адресатов, **≥ +10** для `AllClients` | `TermsOwner` → только владельческие; `TermsClient` → только `AllClients`; `Privacy` → любые | **обязательно** |
| `Suspension` | суперадмин: `title ≤ 200`, `body ≤ 4000` обязательны | — | необязательна | **только** `BillingAccount` | нет |
| `NewProcessor`, `Other` | суперадмин, обязательны | — | необязательна, срок не проверяется | любые | необязательно |
| `PhotoRemoved` | — | — | — | — | **400**: создаётся только системой (§434.8) |

Общие правила (все — **400** со строкой):
* параметры чужого вида (`priceChange` у `TermsChange` и т. п.) — отказ, а не молчаливое игнорирование;
* `linkUrl` — только относительный путь, начинающийся с `/` и не с `//`, ≤ 500;
* `audience.planIds` непуст для `OwnersOnPlans` (и только для него); `audience.billingAccountId` — для
  `BillingAccount` (и только для него); несуществующий тариф/аккаунт → 400;
* `attachment.html` ≤ 1 000 000 символов, `attachment.title` ≤ 200; сервер считает SHA-256 сам;
* ⚠️ шаблон тела `TermsChange` в заключении юриста есть **только для `TermsOwner`**. Для `TermsClient` и
  `Privacy` тело — шаблон от legal-counsel (задача L3 расширяется им). Пока шаблона нет, создание с
  этими `documentType` → **400** «Шаблон уведомления для этого документа ещё не утверждён» (fail-closed,
  а не самодельный текст).

**201** — `AdminPlatformNoticeDto`, заголовок `Location: /api/admin/notices`. `visibleUntil` =
`max(publishedAt + 365 дн, (effectiveFrom ?? publishedAt) + 30 дн)`.

### §434.6 `POST /api/admin/notices/preview` — НОВЫЙ

Тело — то же, что у §434.5. Ничего не пишет. **200**:

```json
{ "title": "…", "body": "…", "templateVersion": "2026-09-29", "effectiveFrom": "2026-10-20", "visibleUntil": "2027-09-29T09:00:00Z", "audienceCount": 42, "attachmentSha256": "…" }
```

Ошибки валидации — те же **400**, что при публикации (один валидатор).

### §434.7 `POST /api/admin/notices/{id}/revoke` — НОВЫЙ

Тело `{ "reason": "Ошибка в дате вступления" }` (обязательна, ≤ 500). **200** —
`AdminPlatformNoticeDto` с `revokedAt`/`revokedByName`/`revokeReason`. **409** — уже отозвано.
**404** — нет. Ознакомления не удаляются.

`GET /api/admin/notices/{id}/attachment` — то же, что §434.3, для SuperAdmin; **404**, если вложения нет.

### §434.8 `DELETE /api/companies/{id}/photos/{photoId}?reason=DepictedPersonRequest`

Существующий маршрут (`IsOwnerOrSuperAdmin`, ответ **204**). Новый необязательный параметр `reason`
(единственное значение `DepictedPersonRequest`, иное → **400**). Учитывается **только** у SuperAdmin:
после коммита удаления сервер публикует уведомление `PhotoRemoved` адресату `BillingAccount` компании.
Ошибка публикации удаление **не откатывает** (Warning в лог без ПДн). У владельца параметр ни на что не
влияет. Остальные коды — прежние (401/403/404/429).

---

## §435. US-20-04 — предел 24 ч для отмены

### `GET /api/bookings/client`

Элемент `BookingDto` получает **в конец** `clientCancelMinHours: int | null` — **применённое** число
часов (`min(окно компании, 24)`). Заполнено только здесь, в остальных ответах `null`.
`clientCancelAllowed` считается с тем же числом. `clientRescheduleMinHours` (окно переноса, 0–168) не
меняется и для текста про отмену **не используется**.

```json
{ "clientRescheduleMinHours": 48, "clientRescheduleAllowed": false, "clientCancelAllowed": true, "clientCancelMinHours": 24 }
```

### `PATCH /api/bookings/{id}/cancel`

Форма прежняя (тело — JSON-строка причины ≤ 300). Для клиента-владельца записи **409** — «Отменить
запись можно не позже чем за {N} ч до визита. Чтобы отменить, свяжитесь с салоном.», где **N —
применённое** число (≤ 24). Персонал окном не ограничен, как было. Коды: **204**, **400**, **401**,
**403**, **404**, **409**.

Подпись поля окна в настройках компании — дословно Т20-05 п. 3 (фронтовая константа), маршруты
настройки не меняются, миграции нет.

---

## §436. US-20-05 — сроки хранения и журнал гейта

### §436.1 `GET /api/admin/retention/policy`

`RetentionPolicyDto` получает в конец: `bookingEventDays` (1095), `guestDataGateEventDays` (365),
`platformNoticeDays` (1095). Остальная форма прежняя.

### §436.2 `GET /api/admin/guest-data-gate-events` — НОВЫЙ

`[Authorize(Roles = "SuperAdmin")]`. Параметры: `userId`, `from`, `to` (date-time, `from > to` → **400**),
`page`, `pageSize`. `PagedResult`, сортировка по `occurredAt` убыв.

```json
{ "id": 1024, "occurredAt": "2026-09-29T08:00:00Z", "eventCode": "guest-data-gate.applied", "userId": "a1b2…", "operation": "Export", "outcome": "Applied", "traceId": "00-4bf9…" }
```

`operation`: `Export` | `DeleteAccount` | `Revoke` | `RevokePreview`. `outcome`: `Applied`.
🔴 **Ни IP, ни User-Agent, ни телефона в любом виде, ни счётчиков, ни id компаний** — полей нет
(`LEGAL_REVIEW_CYCLE16.md` §6.4). Журнал **не** попадает в выгрузку субъекта. Экрана в админке нет.

### §436.3 `GET /api/profile/export` → `guestDataGate.explanation`

Форма прежняя. `explanation` теперь — **плоский текст** раздела «Текст» uiText `GuestDataGateNotice`
(абзацы через `\n\n`, без HTML и без служебной справки юриста); `Fallback` — только если манифест без
ключа.

---

## §437. US-20-07 — только свои компании

«Связан с аккаунтом» = держатель биллинг-аккаунта **или** участник (`CompanyMembers`) любой компании
этого аккаунта (П2).

### §437.1 `GET /api/admin/companies/{companyId}/transfer/preview`

`CompanyTransferPreviewDto` получает в конец `ownerChangeRequired: boolean`. Без `newOwnerUserId`,
если текущий владелец не связан с принимающим аккаунтом: `canTransfer: false`,
`blockReason: "Ответственный за компанию {имя} не связан с принимающим аккаунтом. Укажите нового
ответственного из этого аккаунта."`, `ownerChangeRequired: true`. Во всех остальных случаях
`ownerChangeRequired: false`.

### §437.2 `POST /api/admin/companies/{companyId}/transfer`

Тело `CompanyTransferInput` получает в конец `confirmRightsTransfer: boolean` (по умолчанию `false`).

| Код | Тело | Когда |
|---|---|---|
| **204** | пустое | перенесено; факт подтверждения записан в обе строки `SubscriptionChangeLog` и в `CompanyOwnerChangeLog.comment` |
| **400** | строка | `confirmRightsTransfer` не `true`: «Подтвердите, что права на компанию переходят к принимающему абоненту.»; новый владелец не найден/удалён (как было) |
| **402** | строка | лимит компаний (как было) |
| **409** | строка | **новое:** текущий владелец не связан с принимающим аккаунтом (перенос без смены владельца); прочие существующие поводы |
| **404** | пустое | компании или аккаунта нет |

Проверка `confirmRightsTransfer` — **первой**, до расчётов.

### §437.3 `PUT /api/admin/companies/{id}/owner`

Тело прежнее `{ newOwnerUserId }`. Новое: **409** со строкой «Новый ответственный не связан с
биллинг-аккаунтом компании: он должен быть держателем аккаунта или сотрудником одной из его
компаний.», если у компании есть аккаунт и новый владелец с ним не связан. Сотрудник **этой** компании
связан. Остальное — как было (**204**, **400** «User not found»/«User account has been deleted»,
**404**).

---

## §438. US-20-09 — ручная регистрация обращений

### `POST /api/admin/subject-requests` — НОВЫЙ

`[Authorize(Roles = "SuperAdmin")]`.

```json
{
  "kind": "ConsentWithdrawal",
  "channel": "PostalMail",
  "receivedAt": "2026-09-25T00:00:00Z",
  "phone": "+7 913 000-00-00",
  "contactValue": "г. Барнаул, а/я 12",
  "message": "Прошу отозвать согласие на обработку сведений о здоровье, данное в студии «Лотос»."
}
```

| Поле | Правило (400 со строкой) |
|---|---|
| `kind` | `Access` \| `Rectification` \| `Erasure` \| `ConsentWithdrawal` \| `Complaint` |
| `channel` | `Email` \| `PostalMail`; `WebForm` → 400 (веб-форма — только публичный маршрут) |
| `receivedAt` | обязательна, не в будущем |
| `phone` | необязателен; если указан — через `PhoneNormalizer`, иначе 400 |
| `contactValue` | обязателен, ≤ 200 |
| `message` | обязателен, ≤ 4000 |

**201** — `SubjectRequestDto` (с `reference`, `dueAt` — **от `receivedAt`**, по виду обращения).
Сигнал GlitchTip — тот же, что у публичной формы. Капчи и rate-limit нет (маршрут админский).

### `GET /api/admin/subject-requests`

Элемент получает в конец `channel` (`WebForm` | `Email` | `PostalMail`) и `registeredByName`
(`null` у веб-формы). Для обращения без телефона `phoneMasked` — пустая строка.

Публичный `POST /api/subject-requests` **не меняется** (ответ байт в байт прежний, §50.1 цикла 5).

---

## §439. Тексты гейта и новые uiTexts (Т20-08)

`GET /api/legal/texts/{key}` (публичный, форма `LegalUiTextDto { key, version, isDraft, contentHtml }`)
начинает отдавать пять ключей: `GuestDataGateNotice`, `GuestDataGateDeleteNotice`,
`GuestDataGateRevokeNotice`, `HealthDataWrittenConsentForm`, `CompanyPhotoPeopleNotice`. До коммита А
они отвечают **404** — поэтому фронт F2/F3/F8 мёржится после коммита А (§415.3 архитектуры).

| Ключ | Где показывается | Раздел |
|---|---|---|
| `GuestDataGateNotice` | экран выгрузки данных | «Текст» |
| `GuestDataGateDeleteNotice` | экран удаления учётной записи, рядом с `trialRegistryNotice` | «Текст» |
| `GuestDataGateRevokeNotice` | отзыв согласия, записанного салоном | «Своё согласие» / «Согласие, записанное салоном» (при `phoneVerified === false` — второй раздел **без** вызова API) |
| `HealthDataWrittenConsentForm` | страница печати бланка | «Бланк» (+ §432.4) |
| `CompanyPhotoPeopleNotice` | над выбором файла в `CompanyPhotosSection`, до выбора | «Текст» |

---

## §440. Таблица «код ответа → причина» для нового и изменённого

| Маршрут | 2xx | 400 | 404 | 409 | 410 | 451 |
|---|---|---|---|---|---|---|
| `GET …/health-note` | 200 `HealthNoteDto` | — | клиент не связан | — | — | да |
| `PUT …/health-note` | 200 | пусто/2000; `RequiredConsentDto` | клиент | — | — | да (+OwnerScope) |
| `GET …/health-consent-form` | 200 | — | клиент | — | — | да |
| `POST …/health-written-consent` | 200 | `confirmed`, `formId` | клиент | версия бланка | — | да (+OwnerScope) |
| `POST …/health-written-consent/revoke` | 200 | `reason` | клиент | — | — | да |
| `POST …/health-consent` | — | — | — | — | **всегда** | — |
| `GET\|PUT /billing/operator-details` | 200 | валидация (`PUT`) | нет аккаунта | — | — | да |
| `PUT /admin/…/subscription` | 200 | основание; прежние | аккаунт; «Тариф не найден.» | `TrialPlanNotAssignableHere` (JSON); лимиты | — | — |
| `GET /admin/subscription-change-reasons` | 200 | — | — | — | — | — |
| `GET /legal/notices` | 200 | `scope` | — | — | — | **нет** (allow-list) |
| `POST /legal/notices/{id}/acknowledge` | 200 | — | нет/не адресат/истекло | отозвано | — | **нет** |
| `GET /legal/notices/{id}/attachment` | 200 html | — | нет/не адресат | — | — | **нет** |
| `POST /admin/notices` | 201 | матрица §434.5 | — | — | — | — |
| `POST /admin/notices/preview` | 200 | матрица §434.5 | — | — | — | — |
| `POST /admin/notices/{id}/revoke` | 200 | пустая причина | нет | уже отозвано | — | — |
| `DELETE /companies/{id}/photos/{photoId}` | 204 | неизвестный `reason` | фото/компания | — | — | да |
| `PATCH /bookings/{id}/cancel` | 204 | причина > 300 | запись | окно (≤ 24 ч) | — | да |
| `GET /admin/guest-data-gate-events` | 200 | `from > to` | — | — | — | — |
| `POST /admin/companies/{id}/transfer` | 204 | нет подтверждения | компания/аккаунт | владелец не связан | — | — |
| `PUT /admin/companies/{id}/owner` | 204 | нет/удалён | компания | не связан | — | — |
| `POST /admin/subject-requests` | 201 | валидация | — | — | — | — |

401 — у всех авторизованных маршрутов; 403 — не SuperAdmin на админских, SuperAdmin и не-сотрудник на
§432.1–§432.6.

---

## §441. Что обязан проверить фронт (и чего не должен делать)

**Обязан:**
1. Открывать поле «здоровье» по `consentRequired`/`writtenConsent.granted`, а не по наличию
   электронного согласия; при `consentRequired: true` — кнопки «Распечатать бланк» и «Бланк подписан,
   оригинал у нас».
2. Страница печати: текст uiText `HealthDataWrittenConsentForm` + `runtimeValues`, `null` →
   `BLANK_LINE`; A4, ч/б (`print:`); **ни одного поля ввода паспорта**; подсказка про реквизиты при
   `operatorDetailsMissing`.
3. Диалог отметки: галочка `confirmed`, `formId` последнего напечатанного бланка (можно очистить —
   «собственный бланк салона»), `textVersion` из §432.4. На **409** — перечитать бланк и попросить
   распечатать заново.
4. Снятие отметки: выбор причины и предупреждение «запись о здоровье будет удалена».
5. Строка «Бланк согласия не фотографируйте и не загружайте в сервис» — у поля и в `NotePhotoUploader`
   (константа `staffNotices.ts`, дословно).
6. Профиль: цель `HealthData` не предлагать к выдаче; живую — только «Отозвать» + «Текст для клиента».
7. Баннер уведомлений: один `GET /api/legal/notices?scope=pending`, настоящая `<button>` с
   `acknowledgeButtonText`, подпись `acknowledgeCaption` под ней, `role="status"`; не модалка; после
   «Я ознакомился» — инвалидировать запрос. Тексты — из ответа, не свои.
8. Вложение — только `<iframe sandbox="" srcdoc>`.
9. Админка уведомлений: форма по матрице §434.5, предпросмотр перед публикацией (`/preview`), счётчики.
10. Назначение тарифа: поле «Основание» появляется и обязательно, когда выбран тариф `isPublic: false`,
    отличный от текущего; варианты — из §433.3 (`assignableManually: true`); «Описание ошибки»
    обязательно для `OperatorErrorCorrection`.
11. Отмена записи: текст «Отменить можно не позже чем за N ч» — по `clientCancelMinHours`.
12. Перенос компании: обязательная галочка → `confirmRightsTransfer: true`; при
    `ownerChangeRequired: true` — требовать выбора нового ответственного; тексты 409 — дословно.
13. Ручное обращение: `channel` только `Email`/`PostalMail`; колонка «Канал» в списке.
14. Удаление фото суперадмином — диалог причины; «по обращению изображённого» → `?reason=DepictedPersonRequest`.

**Не должен:**
* считать, открыто ли поле «здоровье», по электронным согласиям;
* хранить `formId`, реквизиты или текст бланка где-либо, кроме памяти страницы печати;
* закрывать уведомление без вызова `acknowledge` или помечать прочитанным в `localStorage`;
* вставлять HTML вложения в DOM приложения;
* держать свои подписи оснований тарифа, шаблоны уведомлений или «24» константой для текста отмены.

---

## §442. Совместимость

| Изменение | Для кого ломающее | Почему допустимо |
|---|---|---|
| `reasonCode` обязателен при скрытом тарифе ≠ текущего | админский фронт | маршрут админский, внешних потребителей нет (US-20-02) |
| `confirmRightsTransfer` обязателен | админский фронт | то же; Т20-09 п. 1 |
| `409` на `PUT …/owner` | админский фронт | то же; LG6 |
| `POST …/health-consent` → 410 | фронт персонала | салонная электронная форма выводится решением LG1; фронт F2 переходит на §432.5 |
| поле «здоровье» закрыто без отметки | персонал салонов | решение LG1 + П1; данные тестовые |
| `400` на `HealthData` в `POST /profile/consents` | фронт профиля | Т20-03 п. 3; фронт F3 цель не предлагает |

Публичный API — только аддитивно: `writtenConsent`, `clientCancelMinHours`, `ownerChangeRequired`,
`channel`, новые маршруты. Поведение отмены меняется **в пользу клиента**.

---

## §443. Чего в контракте намеренно нет

* **Ни одного поля паспортных данных** и ни одного маршрута, сохраняющего сгенерированный бланк.
* **Ни одного суперадминского маршрута над отметкой** о бумажном согласии (§402.4 архитектуры).
* **`PUT /api/admin/notices/{id}`** — опубликованное не редактируется.
* **Маршрута доставки уведомлений** по почте/SMS/мессенджеру (C18-8(б): только кабинет).
* **Связки «правка цены тарифа → уведомление»** (вне объёма SPEC).
* **IP, User-Agent, телефона и счётчиков** в журнале гейта.
* **Формы заявки на канал для гражданина без статуса** (C-13, П4 — отложено), callback, геокодера.

---

## §444. Приёмка контракта

* [ ] `contracts/cycle20/openapi.yaml` проходит `redocly lint` с конфигурацией проекта и **включён в
      CI-шаг линта** (D1); `contracts/redocly.yaml` — комментарий-список обновлён.
* [ ] `npm run types:api:cycle20` в `package.json` и в CI-шаге сверки генератов;
      `src/types/api-cycle20.generated.ts` закоммичен.
* [ ] `schemathesis run contracts/cycle20/openapi.yaml --checks all` зелёный против живого бэкенда.
* [ ] Тексты шаблонов уведомлений, подписи кнопки — из `PlatformNoticeTexts.cs`; причины — из
      `SubscriptionChangeReasonTexts.cs`; строки 410/400 бумажного согласия — из
      `WrittenHealthConsentTexts.cs`; в контроллерах и компонентах русских правовых строк нет.
* [ ] `GET /api/legal/notices` отвечает 200 пользователю с непринятой Material-редакцией (`CY20-B-03h`).
* [ ] `API_DOCUMENTATION.md` дополнен всеми строками §431 (B12).

---

## §445. Карта «текст → маршрут и поле»

| Текст | Источник | Маршрут | Поле |
|---|---|---|---|
| Бланк письменного согласия | uiText `HealthDataWrittenConsentForm` | `GET /api/legal/texts/{key}` + §432.4 | `contentHtml` + `runtimeValues` |
| «Нужно письменное согласие…» | `WrittenHealthConsentTexts` | `PUT …/health-note` | `RequiredConsentDto.message` |
| Замена салонной формы | `WrittenHealthConsentTexts` | `POST …/health-consent` | тело 410 |
| «Бланк не фотографируйте…» | `staffNotices.ts` | — (фронт) | — |
| Шаблон `PriceChange`, `TermsChange` (TermsOwner) | `PlatformNoticeTexts` (Т20-02 п. 6) | `GET /api/legal/notices` | `body` |
| Заголовки шаблонных видов, `PhotoRemoved`, `TermsChange` для TermsClient/Privacy | `PlatformNoticeTexts` (**L3**) | там же | `title`, `body` |
| «Я ознакомился» + подпись | `PlatformNoticeTexts` (Т20-02 п. 6) | там же | `acknowledgeButtonText`, `acknowledgeCaption` |
| Основания тарифа | `SubscriptionChangeReasonTexts` | §433.2, §433.3 | `reasonTitle`, `title` |
| Отказ в отмене | `BookingsController` (как в цикле 17) | `PATCH …/cancel` | тело 409 |
| Подпись поля окна отмены | `staffNotices.ts` (Т20-05 п. 3) | — (фронт) | — |
| Врезки гейта | uiTexts 14–16 | `GET /api/legal/texts/{key}`, `GET /api/profile/export` | `contentHtml`, `guestDataGate.explanation` |
| Подсказка о людях на фото | uiText `CompanyPhotoPeopleNotice` | `GET /api/legal/texts/{key}` | `contentHtml` |
| Отказы LG6 | `BillingTexts` | §437 | тела 400/409 |
