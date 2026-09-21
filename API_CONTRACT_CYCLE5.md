# API_CONTRACT — цикл 5 ServiceBooking: правовые основания продукта

**Дата: 2026-09-21. Ветка `cycle/05-legal-compliance`. Архитектура — `ARCHITECTURE_CYCLE5.md`.**

---

## Как читать этот документ

Это **третье продолжение** контракта. Нумерация сквозная:

| Документ | Разделы | Статус |
|---|---|---|
| `API_CONTRACT.md` | 0–18 | цикл 3. Действует. §0.3 (451), §0.4 (allow-list), §1–4 (правовые эндпоинты), §5 (регистрация), §8–9 (права субъекта) — **изменяются этим циклом** |
| `API_CONTRACT_CYCLE4.md` | 19–37 | цикл 4. Действует. §29 (шаблоны), §22 (заявка на канал), §24.1 (connect), §30 (журнал доставки) — **изменяются этим циклом** |
| `API_CONTRACT_CYCLE5.md` (этот) | **38–53** | цикл 5 |

🔴 **Этот цикл содержит семь ломающих изменений существующих эндпоинтов.** Они собраны в §52.2
отдельной таблицей — прочитайте её первой, если вы фронтенд-разработчик.

**Правило, действующее без изменений с цикла 3:** фронт и бэкенд согласуются **только по этому
документу**. Если в нём чего-то нет — это не «на усмотрение», это вопрос к архитектору.

---

## 38. Общее для эндпоинтов цикла

### 38.1 Соглашения, действующие без изменений

Сериализация (camelCase, `JsonStringEnumConverter`, UTC-даты с `Z`), формат тел ошибок (plain text
для доменных 400/409, `ProblemDetails` для автоматической валидации), `PagedResult<T>`, политики
rate limiting — **всё как в циклах 3 и 4**. Новых конвенций цикл не вводит.

### 38.2 Коды, которыми пользуется этот цикл

| Код | Когда |
|---|---|
| 200 / 201 / 202 | успех; **202** — только у публичной формы обращения (§48.1) |
| 400 | отсутствует обязательное подтверждение/согласие; неверный ИНН; текст шаблона нарушает жёсткий запрет |
| 403 | нет прав на компанию; **суперадмин на поле противопоказаний** |
| 404 | неизвестный тип документа/ключ текста; чужой ресурс, существование которого не подтверждается |
| 409 | версия документа устарела (гонка); владелец пытается создать компанию, не приняв D3 — **нет**, здесь 451, см. ниже |
| **451** | 1) глобальный гейт по `Privacy`/`TermsClient` (как в цикле 3); 2) **новое:** owner-действие без акцепта действующего `TermsOwner` |
| 429 | лимиты, в т.ч. новая политика `subject-request` |
| 503 | манифест правовых документов не загружен (как в цикле 3) |

🔴 **Два разных 451 различаются телом.** Глобальный гейт возвращает `text/plain` (как было).
Owner-гейт возвращает **JSON** — фронту нужны тип и версия, чтобы открыть модалку акцепта:

```json
{ "reason": "OwnerTermsNotAccepted", "documentType": "TermsOwner", "version": "2026-09-21" }
```

Отличать по `Content-Type`. Это единственное место, где 451 несёт тело в JSON.

### 38.3 Перечисления цикла (значения — точные строки JSON)

```
LegalDocumentType  : "Privacy" | "TermsClient" | "TermsOwner" | "PdnConsent" | "ChannelRiskNotice"
LegalGate          : "Global" | "OwnerScope" | "None"
LegalChangeKind    : "Material" | "Editorial"                       ← без изменений
LegalTextKey       : "BookingNotice" | "TemplateAdWarning" | "UnsubscribePage"
                   | "PhotoConsent" | "HealthDataConsent" | "GuardianConfirmation"
ConsentPurpose     : "ProviderDelivery" | "WorkPhotos" | "HealthData" | "ChannelOffer"
ConsentAct         : "Acknowledged" | "Accepted" | "Consented" | "Confirmed"
ConsentSource      : "Registration" | "ReAcceptance" | "Profile" | "CompanyCreation"
                   | "ChannelRequest" | "ChannelLink" | "PhotoForm" | "HealthForm"
                   | "Booking" | "Migrated"
SubjectRequestKind : "Access" | "Rectification" | "Erasure" | "ConsentWithdrawal" | "Complaint"
SubjectRequestStatus: "Received" | "InProgress" | "Answered" | "Rejected"
DueState           : "OnTime" | "DueSoon" | "Overdue"
LegalEntityForm    : "Ip" | "Company" | "SelfEmployed"
```

🔴 **`"Terms"` больше не существует.** Он стал `"TermsClient"`. Это ломающее изменение №1.

### 38.4 Общая форма записи журнала согласий

Используется в §41, §49 и в админских ответах.

```json
{
  "id": "0f3c…",
  "documentKey": "PdnConsent",
  "documentVersion": "2026-09-21",
  "purpose": "WorkPhotos",
  "act": "Consented",
  "source": "Registration",
  "companyId": null,
  "grantedAt": "2026-09-21T10:14:03Z",
  "revokedAt": null,
  "revokeReason": null
}
```

`source: "Migrated"` встречается только на dev- и тестовых базах: боевая база зачищается перед
релизом (§44.8), поэтому в бою каждая строка журнала ссылается на версию и хеш текста с первого
дня. **IP и User-Agent наружу не отдаются никогда** — ни пользователю, ни суперадмину через API;
они существуют для доказывания и читаются из БД при необходимости.

---

## 39. Правовые документы — существующие эндпоинты · **BREAKING** · T5-B2

### 39.1 `GET /api/legal/documents` — публично

**Было** (цикл 3, §1): массив метаданных **двух** документов.
**Стало:** объект с двумя массивами.

```json
{
  "documents": [
    { "type": "Privacy",     "title": "…", "version": "2026-09-21-draft", "effectiveFrom": "2026-09-21",
      "isDraft": true, "changeKind": "Material", "gate": "Global",     "url": "/privacy" },
    { "type": "TermsClient", "…": "…", "gate": "Global",     "url": "/terms" },
    { "type": "TermsOwner",  "…": "…", "gate": "OwnerScope", "url": "/terms-owner" },
    { "type": "PdnConsent",  "…": "…", "gate": "None",       "url": "/pdn-consent",
      "purposes": [
        { "key": "ProviderDelivery", "title": "Передача привлекаемым лицам для доставки уведомлений" },
        { "key": "WorkPhotos",       "title": "Фотофиксация выполненной работы" },
        { "key": "HealthData",       "title": "Обработка сведений о состоянии здоровья" }
      ] },
    { "type": "ChannelRiskNotice", "…": "…", "gate": "None", "url": "/channel-risk" }
  ],
  "uiTexts": [
    { "key": "BookingNotice", "version": "2026-09-21-draft", "isDraft": true },
    { "key": "TemplateAdWarning", "…": "…" }, { "key": "UnsubscribePage", "…": "…" },
    { "key": "PhotoConsent", "…": "…" }, { "key": "HealthDataConsent", "…": "…" },
    { "key": "GuardianConfirmation", "…": "…" }
  ]
}
```

- HTML по-прежнему **не отдаётся** — подвал и форма регистрации не тянут тексты.
- 🔴 **`purposes` берёт фронт отсюда, а не из зашитого массива.** Если юрист изменит набор целей,
  форма регистрации и раздел «Мои согласия» подстроятся без релиза фронта. Заголовки целей — из
  манифеста, то есть их пишет юрист, а не разработчик.
- `url` — маршрут SPA, по которому лежит читаемый текст. Подвал строит ссылки по нему.

### 39.2 `GET /api/legal/documents/{type}` — публично

Без изменений по форме: метаданные + `contentHtml`, `Cache-Control: public, max-age=300`.
Изменение: `{type}` принимает пять значений. Неизвестный тип → **404** (не 500).

### 39.3 `GET /api/legal/texts/{key}` — публично · **НОВЫЙ**

Тексты интерфейса. Та же форма, что у документа, но без `changeKind` и `gate`.

```json
{ "key": "BookingNotice", "version": "2026-09-21-draft", "isDraft": true, "contentHtml": "<p>…</p>" }
```

Неизвестный ключ → 404. Кешируется так же, 5 минут.
**Используется:** формой записи (D5), редактором шаблона (D7), страницей отписки (D8), формами
согласий на фото и здоровье (D10/D11), чекбоксом «записываю другого человека» (D12).

### 39.4 `GET /api/legal/consent-status` — авторизованные · **BREAKING**

**Было:** `{ requiresAcceptance, showBanner, documents[] }` по двум документам.
**Стало:** то же, но `documents[]` содержит только документы с `gate ≠ "None"`, и добавлен
`ownerActionBlocked`.

```json
{
  "requiresAcceptance": false,          // блокирует ВСЁ (gate: Global, changeKind: Material)
  "ownerActionBlocked": true,           // блокирует owner-действия (gate: OwnerScope)
  "showBanner": false,                  // есть Editorial-расхождение, ничего не блокирует
  "documents": [
    { "type": "Privacy",     "currentVersion": "2026-09-21", "acceptedVersion": "2026-09-21",
      "changeKind": "Material", "gate": "Global" },
    { "type": "TermsClient", "currentVersion": "2026-09-21", "acceptedVersion": "2026-09-21", "…": "…" },
    { "type": "TermsOwner",  "currentVersion": "2026-09-21", "acceptedVersion": "2026-09-15",
      "changeKind": "Material", "gate": "OwnerScope" }
  ]
}
```

По-прежнему считается **из claim'ов токена и снимка в памяти, без запроса к БД**.
`ownerActionBlocked` приходит `false` у пользователя без роли `CompanyOwner`.

### 39.5 `POST /api/legal/accept` — авторизованные · **BREAKING**

**Было:** `{ "privacyVersion": "…", "termsVersion": "…" }` — фиксированные два поля.
**Стало:** список, чтобы добавление шестого документа не ломало контракт.

```json
{ "accept": [ { "type": "Privacy", "version": "2026-09-21" },
              { "type": "TermsClient", "version": "2026-09-21" } ] }
```

| Код | Когда |
|---|---|
| 200 | принято. Тело: `{ "token": "…", "acceptedAt": "…" }` — **новый токен обязателен**, как и раньше |
| 400 | пустой список; тип с `gate: "None"` (согласие принимается не здесь, а в §41) |
| 409 | версия в теле не совпадает с действующей — оператор заменил текст ещё раз |
| 503 | манифест не загружен |

Каждый элемент `accept` даёт **отдельную строку журнала** (`Act`: `Acknowledged` для `Privacy`,
`Accepted` для `TermsClient`/`TermsOwner`; `Source: ReAcceptance`). Перезаписи нет.

---

## 40. `POST /api/auth/register` — **BREAKING № 2** · T5-B3

### 40.1 Дельта запроса

```diff
  { "firstName": "…", "lastName": "…", "phone": "…", "password": "…", "email": null,
-   "acceptedLegal": true
+   "legal": {
+     "privacyAcknowledgedVersion": "2026-09-21",
+     "termsAcceptedVersion": "2026-09-21"
+   }
  }
```

🔴 **Поле `acceptedLegal` удаляется полностью.** Запрос с ним и без `legal` → 400.

### 40.2 Что делает сервер

1. Сверяет обе версии с действующим снимком. Не совпало → **409** (пока человек заполнял форму,
   тексты заменили) с тем же текстом, что у `POST /api/legal/accept`.
2. В одной транзакции с созданием пользователя пишет **две** строки журнала:
   `Privacy / Acknowledged / Registration` и `TermsClient / Accepted / Registration`, обе с версией,
   хешем, `IpAddress` и `UserAgent`.
3. Возвращает токен с claim'ами `lcp` и `lct`.

🔴 **Про `PdnConsent` этот эндпоинт не знает ничего и знать не должен.** Требование ст. 9 «согласие
оформляется отдельно от иных документов» реализовано тем, что согласие **физически невозможно
подать этим запросом** (`ARCHITECTURE_CYCLE5.md` §46.2).

### 40.3 Коды

| Код | Когда |
|---|---|
| 201 | зарегистрирован |
| 400 | нет `legal`, нет одной из версий, невалидный телефон/пароль — как раньше |
| 409 | телефон занят (как раньше) **или** версия документа устарела (новое) — различаются текстом |
| 429 | политика `auth-register` (как раньше) |

### 40.4 Влияние на фронт: правки обязательны

Форма регистрации перестраивается целиком (T5-F1). Версии берутся из `GET /api/legal/documents`,
не зашиваются. После 201 — **второй вызов** §41.2, если человек отметил цели согласия; если не
отметил — второго вызова нет и регистрация уже состоялась.

---

## 41. Согласия пользователя · **НОВЫЕ** · T5-B3

Все три — `[Authorize]`, все три **в allow-list** `LegalConsentFilter` (доступны при 451).

### 41.1 `GET /api/profile/consents`

```json
{
  "document": { "type": "PdnConsent", "version": "2026-09-21", "isDraft": true,
                "purposes": [ { "key": "ProviderDelivery", "title": "…" }, "…" ] },
  "granted": [
    { "purpose": "ProviderDelivery", "version": "2026-09-21", "grantedAt": "…", "revokedAt": null },
    { "purpose": "WorkPhotos", "version": "2026-09-15", "grantedAt": "…", "revokedAt": "…" }
  ],
  "versionOutdated": true,
  "history": [ /* §38.4, весь журнал по этому пользователю, свежие сверху */ ]
}
```

`versionOutdated` — действующая версия D4 новее той, на которую давалось согласие. **Ничего не
блокирует**, фронт показывает мягкое приглашение перечитать.
`history` включает **все** ключи, а не только `PdnConsent`: человек вправе видеть, что и когда он
подписывал. Владельцу салона журнал согласий **не отдаётся ни в каком виде** (US-66 п. 5).

### 41.2 `POST /api/profile/consents`

```json
{ "documentKey": "PdnConsent", "version": "2026-09-21",
  "purposes": ["ProviderDelivery", "WorkPhotos"] }
```

| Код | Когда |
|---|---|
| 200 | записано. Тело — как §41.1 |
| 400 | неизвестный ключ или цель; ключ, которому здесь не место (`Privacy`, `TermsClient`) |
| 409 | версия устарела |

- **Пустой `purposes` — валидный запрос**: означает «ознакомился, согласия не даю», пишет одну
  строку с `Purpose: null` и `Act: "Consented"`, отмечающую факт показа формы. Ни на что не влияет.
- Повторный идентичный вызов в пределах 5 секунд не создаёт дубликата (идемпотентность,
  `ARCHITECTURE_CYCLE5.md` §45.4). Через сутки — создаёт новую строку, это законное повторное
  согласие.
- `documentKey` здесь принимает **только** `"PdnConsent"`. Согласия, даваемые в салоне
  (`PhotoConsent`, `HealthDataConsent`), подаются своими эндпоинтами (§44, §45) — у них другой
  субъектный контекст (клиент + компания).

### 41.3 `POST /api/profile/consents/revoke`

```json
{ "documentKey": "PdnConsent", "purpose": "WorkPhotos", "reason": "не хочу фото" }
```

`purpose: null` → отзывается согласие целиком со всеми целями.

```json
{ "revoked": 1,
  "effects": { "photosDeleted": 12, "healthNotesDeleted": 0,
               "profileFieldsCleared": [], "queuedNotificationsCancelled": 0 } }
```

| Код | Когда |
|---|---|
| 200 | отозвано **или** отзывать было нечего (`revoked: 0`) — идемпотентно |
| 400 | неизвестный ключ/цель |

⚠️ **`queuedNotificationsCancelled` при текущем умолчании платформы всегда `0`.** Режим проверки
согласия на цель `ProviderDelivery` — **`Off`** по решению заказчика
(`ARCHITECTURE_CYCLE5.md` §52.3): отзыв этой цели фиксируется в журнале, но на доставку уведомлений
не влияет. 🔴 Поэтому фронт **не обещает** пользователю прекращения уведомлений при отзыве этой
цели — для отказа от уведомлений есть отписка, и это отдельный механизм (US-68 п. 6). Если
платформа переключится на `AccountsOnly`/`Strict`, счётчик начнёт быть ненулевым сам, **без правок
контракта и без правок фронта**.

🔴 **Что отзыв НЕ делает** (фронт обязан показать это **до** подтверждения, US-68 п. 3): не удаляет
историю визитов, отзывы и расчёты салона; не снимает и не включает отписку от уведомлений; не
удаляет аккаунт; не удаляет строки журнала.

**Предпросмотр последствий** — `GET /api/profile/consents/revoke-preview?purpose=WorkPhotos`,
возвращает тот же объект `effects` с числами, **ничего не меняя**. Нужен, чтобы экран говорил
«будет удалено 12 фотографий», а не «фотографии будут удалены».

---

## 42. Соглашение владельца и owner-гейт · **BREAKING № 3** · T5-B4

### 42.1 `POST /api/companies` — дельта запроса

```diff
  { "name": "…", "slug": "…", "cityId": 42, "…": "…",
+   "ownerTerms": { "version": "2026-09-21" }
  }
```

| Код | Когда |
|---|---|
| 201 | компания создана; пишется строка `TermsOwner / Accepted / CompanyCreation`; **выдаётся новый токен** в теле (`{ "company": {…}, "token": "…" }`) — в него входит claim `lco` |
| 400 | нет `ownerTerms` |
| 409 | версия устарела |

🔴 **Новый токен в ответе на создание компании — обязательный элемент, а не удобство.** Без него
claim `lco` появится только после следующего логина, и владелец получит 451 на собственную компанию
через секунду после её создания.

### 42.2 Owner-гейт: какие действия возвращают 451

Действия, требующие действующего акцепта `TermsOwner` (атрибут `[RequiresOwnerTerms]`):

```
POST   /api/companies                         PUT    /api/companies/{id}
POST   /api/companies/{id}/members            DELETE /api/companies/{id}/members/{memberId}
PUT    /api/companies/{id}/notification-settings
PUT    /api/companies/{id}/notification-templates/{type}
POST   /api/notification-channels             POST   /api/notification-channels/{id}/connect
POST   /api/notification-channels/{id}/companies
POST   /api/masters/clients/notes             PUT    /api/companies/{companyId}/clients/{key}/health-note
POST   /api/client-notes/{noteId}/photos
```

Тело 451 — JSON из §38.2. Фронт открывает модалку с текстом D3 и вызывает §42.3.

🔴 **Чтение не блокируется никогда.** Владелец, не принявший новую редакцию, видит своё расписание,
свои записи и свою клиентскую базу; он не может только **изменять** и **подключать платное**. Это
сознательная граница: блокировать салону доступ к его собственным данным из-за редакции соглашения
было бы ровно тем, за что мы критикуем текущую реализацию.

### 42.3 `POST /api/legal/accept` для `TermsOwner`

Тот же эндпоинт §39.5, элемент `{ "type": "TermsOwner", "version": "…" }`. Пишет строку
`TermsOwner / Accepted / ReAcceptance`, выдаёт новый токен с обновлённым `lco`.

---

## 43. Публичные страницы документов · **НОВЫЕ МАРШРУТЫ SPA** · T5-F2

Серверных эндпоинтов не добавляют, но входят в контракт, потому что на них ссылаются тексты.

| Маршрут | Источник | Авторизация |
|---|---|---|
| `/terms-owner` | `GET /api/legal/documents/TermsOwner` | нет |
| `/pdn-consent` | `GET /api/legal/documents/PdnConsent` | нет |
| `/channel-risk` | `GET /api/legal/documents/ChannelRiskNotice` | нет |
| `/offer-channel` | **редирект на `/terms-owner#offer-channel`** | нет |

Плашка «черновая редакция» рисуется **по полю `isDraft`**, а не по разметке внутри текста
(US-79 п. 2). Все четыре добавляются в `robots.txt` и в подвал обычными `<a href>`.

---

## 44. Согласие на фотофиксацию · **НОВЫЕ + BREAKING № 4** · T5-B7

### 44.1 `GET /api/companies/{companyId}/clients/{clientKey}/photo-consent`

`clientKey` — `userId` или `phone:79991234567` (канонический). Персонал компании.

```json
{ "granted": true, "grantedAt": "…", "version": "2026-09-15",
  "confirmedBy": "Иванова М.", "textVersionOutdated": false, "source": "PhotoForm" }
```

`granted: false` + `textVersionOutdated: true` эквивалентны для фронта: нужна форма.

### 44.2 `POST /api/companies/{companyId}/clients/{clientKey}/photo-consent`

```json
{ "textVersion": "2026-09-21", "confirmed": true }
```

Подтверждение **сотрудником** (минимум US-76 п. 2): пишется строка
`PhotoConsent / Consented / PhotoForm` с `CompanyId`, `SubjectPhone` (или `UserId`),
`RecordedByUserId` = сотрудник, версией и хешем текста D10.

| Код | Когда |
|---|---|
| 200 | записано |
| 400 | `confirmed: false` |
| 403 | не персонал компании |
| 409 | версия текста устарела |

### 44.3 `POST /api/client-notes/{noteId}/photos` — **BREAKING**

**Новый предусловный код:**

| Код | Когда |
|---|---|
| **400** | нет действующего согласия на фотофиксацию для пары «клиент + компания». Тело: `"Перед загрузкой фото нужно подтвердить согласие клиента на фотофиксацию."` |

Остальное — **без изменений**: лимит 5 МБ, `uploads`-политика, ≤5 фото на заметку, идемпотентность
по SHA-256, квота, 403 суперадмину на чтение.

🔴 **Отсутствие согласия блокирует только загрузку фото.** Заметка, запись, уведомления и всё
остальное обслуживание работают (US-76 п. 6). Проверяется тестом.

### 44.4 Что делает отзыв

Отзыв цели `WorkPhotos` (§41.3) или салонного `PhotoConsent` удаляет фото **необратимо**; факт
удаления логируется без содержимого. Повторная загрузка после нового согласия — обычный путь.

---

## 45. Противопоказания и особенности здоровья · **НОВЫЕ** · T5-B6

🔴 **Три эндпоинта, и больше поле не приходит нигде.** Его нет ни в `ClientNoteDto`, ни в
`MasterClientDto`, ни в одной админской выборке — ни пустым, ни `null`.

### 45.1 `GET /api/companies/{companyId}/clients/{clientKey}/health-note`

| Код | Когда |
|---|---|
| 200 | `{ "value": "аллергия на аммиак", "updatedAt": "…", "updatedBy": "Иванова М." }` |
| 200 | `{ "value": null, "consentRequired": true }` — поле пусто и согласия нет |
| **403** | 🔴 **вызывающий — SuperAdmin** (та же конвенция, что у фото заметок) |
| 403 | не `Master`/`CompanyOwner` этой компании |
| 404 | клиент не относится к этой компании |

### 45.2 `PUT /api/companies/{companyId}/clients/{clientKey}/health-note`

```json
{ "value": "беременность — не красим" }
```

| Код | Когда |
|---|---|
| 200 | сохранено (значение шифруется на сервере) |
| **400** | нет действующего согласия. Тело: `"Для заполнения этого поля нужно согласие клиента на обработку сведений о состоянии здоровья."` + `{"requiredTextKey": "HealthDataConsent"}` — 🔴 единственный 400 цикла с JSON-телом, потому что фронту нужен ключ текста |
| 400 | длина > 2000 |
| 403 | SuperAdmin или не персонал компании |

### 45.3 `DELETE /api/companies/{companyId}/clients/{clientKey}/health-note`

200 всегда (идемпотентно). Персонал компании.

### 45.4 `POST /api/companies/{companyId}/clients/{clientKey}/health-consent`

Форма согласия (D11), по устройству — копия §44.2: `{ "textVersion": "…", "confirmed": true }` →
строка `HealthDataConsent / Consented / HealthForm`.

### 45.5 Поведение в остальных механизмах

| Механизм | Поведение |
|---|---|
| `GET /api/profile/export` | **входит** в выгрузку, расшифрованным, секцией `healthNotes` (§49) |
| `POST /api/profile/delete-account` | удаляется вместе с заметками |
| отзыв согласия (§41.3) | 🔴 **очищается немедленно**, не дожидаясь ретенции |
| ретенция | срок как у `ClientNote` (3 года), но отзыв — раньше |
| поиск клиентов | **не участвует**: поле непоисковое по построению |

---

## 46. Запись: уведомление по ст. 18 и подтверждение полномочий · **BREAKING № 5** · T5-B16

### 46.1 `POST /api/bookings` — дельта запроса

```diff
  { "companyId": "…", "serviceId": "…", "masterId": "…", "date": "…", "startTime": "…",
    "guestName": null, "guestPhone": null, "guestEmail": null,
+   "bookedForOther": false,
+   "guardianConfirmation": null      // { "textVersion": "2026-09-21", "confirmed": true }
  }
```

| Код | Когда |
|---|---|
| 201 | как раньше |
| **400** | `bookedForOther: true`, а `guardianConfirmation` отсутствует или `confirmed: false` |
| 409 | версия текста D12 устарела |

`bookedForOther: false` (умолчание) — подтверждения не требуется, поведение **ровно как сегодня**.
Требование действует в клиентской форме, **гостевой** и в виджете `/embed` (US-78 п. 1).

### 46.2 Дельта ответа

```diff
  { "id": "…", "…": "…",
    "consentPrivacyVersion": "2026-09-21", "consentTermsVersion": "2026-09-21",
    "consentAcceptedAt": "…",
+   "bookingNoticeVersion": "2026-09-21",
+   "bookedForOther": false, "guardianConfirmedAt": null
  }
```

Все версии заполняет **сервер** из снимка; из тела запроса они не принимаются никогда (правило
цикла 3 в силе). `consentTermsVersion` теперь содержит версию `TermsClient` — имя поля сохранено
осознанно, чтобы не ломать экспорт и отчёты.

### 46.3 Строка под кнопкой записи

🔴 **Переписывается как уведомление по ст. 18, а не как согласие.** Текст — `GET /api/legal/texts/BookingNotice`
(D5): кто оператор, цели, правовое основание, перечень данных, кому передаются (салон и
**ООО «ГРИН-АПИ», ИНН 5047259512** поимённо), что доставка идёт через иностранный мессенджер и **не
гарантируется**. Отметки не требует — это информирование.

🔴 **Гостевая запись не требует ни одного согласия** (основание договорное, US-67 п. 6). Тест на это
обязателен.

---

## 47. Шаблоны сообщений · **BREAKING № 6** · T5-B12

### 47.1 `GET /api/companies/{id}/notification-templates` — дельта ответа

```diff
  { "templates": [ … ],
+   "adMarkers": ["скидк", "акци", "промо", "дарим", "подар", "спецпредлож", "%", "бесплатн",
+                 "приводи", "успей", "только до"],
+   "warningTextKey": "TemplateAdWarning",
+   "warningVersion": "2026-09-21"
  }
```

🔴 **Словарь маркеров приезжает с сервера.** Фронт его не хранит и не дополняет — иначе клиентская
подсветка разойдётся с серверной проверкой при первой же правке словаря суперадмином.

### 47.2 `PUT /api/companies/{id}/notification-templates/{type}` — **BREAKING**

```diff
- { "body": "Здравствуйте, {ИмяКлиента}! …" }
+ { "body": "Здравствуйте, {ИмяКлиента}! …",
+   "acknowledgement": {
+     "warningVersion": "2026-09-21",
+     "accepted": true,
+     "confirmedDespiteMarkers": false
+   } }
```

| Код | Когда |
|---|---|
| 200 | сохранено |
| **400** | нет `acknowledgement` или `accepted: false`. Текст: «Подтвердите, что текст сервисный и вы принимаете ответственность за его содержание.» |
| **400** | сработали маркеры, а `confirmedDespiteMarkers` не `true`. 🔴 Тело — JSON: `{ "markersHit": ["скидк","%"], "message": "Такой текст с высокой вероятностью является рекламой…" }` |
| **400** | 🔴 **жёсткий запрет**: чужой телефон (не `{ТелефонСалона}`) или упоминание стороннего мессенджера/соцсети. Это **не обходится** подтверждением |
| 409 | `warningVersion` устарела |

**Подтверждение не кешируется и не наследуется** (US-69 п. 4): серверного состояния «уже
подтверждал» не существует, каждое сохранение несёт своё поле. Второе сохранение того же текста
требует нового подтверждения.

Записывается в `NotificationTemplateHistory`: `AcknowledgedByUserId`, `AcknowledgedAtUtc`,
`WarningVersion`, `AdMarkersHit`, `NewBody`.

### 47.3 Предупреждение у поля

Обязательное, **несворачиваемое, не в тултипе и не в справке** (US-69 п. 1). Текст —
`GET /api/legal/texts/TemplateAdWarning`. Проверяется вручную при приёмке.

---

## 48. Обращения субъектов данных · **НОВЫЕ** · T5-B10

### 48.1 `POST /api/subject-requests` — **анонимно**

```json
{ "kind": "Erasure", "phone": "+7 999 123-45-67",
  "contactValue": "me@example.com", "message": "Прошу удалить мои данные",
  "captchaToken": "…" }
```

**Ответ — всегда одинаковый, независимо от того, известен ли номер:**

```json
{ "reference": "SR-7K3Q2M", "responseDueByWorkingDays": 10 }
```

Код — **202 Accepted**.

| Код | Когда |
|---|---|
| 202 | принято |
| 400 | не заполнено обязательное; телефон не приводится к каноническому виду; `message` > 4000 |
| 400 | капча не пройдена |
| **429** | политика `subject-request`: 3/час и 10/сутки на IP |

🔴 **В ответе нет ничего о субъекте** — ни имени, ни числа записей, ни «такой номер найден/не
найден». Существование номера в системе не подтверждается и не опровергается: это тоже утечка.
Проверяется тестом, сравнивающим ответы для существующего и несуществующего номера побайтово.

🔴 **Данные автоматически не выдаются** (ПЛ4). Обращение создаёт заявку; отвечает человек.

### 48.2 `GET /api/admin/subject-requests` — SuperAdmin

`?status=&kind=&dueState=&page=&pageSize=` → `PagedResult<SubjectRequestDto>`:

```json
{ "id": "…", "reference": "SR-7K3Q2M", "kind": "Erasure", "status": "Received",
  "phoneMasked": "+7 999 ***-**-67", "contactValue": "me@example.com",
  "message": "…", "receivedAt": "…", "dueAt": "…", "dueState": "DueSoon",
  "answeredAt": null, "handlerName": null, "resolution": null }
```

Сортировка по умолчанию — по `dueAt` возрастающе: горящее сверху. `dueState` считает **сервер**
(`DueSoon` — менее двух суток до срока). Счётчик `overdue` дополнительно попадает в существующую
админскую сводку, чтобы просрочка была видна без захода в раздел.

### 48.3 `POST /api/admin/subject-requests/{id}/status` — SuperAdmin

```json
{ "status": "Answered", "resolution": "Данные удалены, ответ отправлен на e-mail" }
```

200; `AnsweredAtUtc` и `HandlerUserId` проставляет сервер. Переход в `Answered`/`Rejected` требует
непустого `resolution` (400 иначе) — иначе журнал перестаёт быть доказательством.

### 48.4 Маршрут SPA

`/data-request` — публичная страница формы. Ссылка на неё обязана быть в политике (D1) и в подвале.

---

## 49. `GET /api/profile/export` — расширение · T5-B11

**Формат не меняется:** синхронный JSON, `Content-Disposition: attachment`, лимит `data-export`
(3/сутки), в allow-list при 451. Добавляются четыре секции.

```diff
  { "profile": {…}, "bookings": […], "guestBookings": […], "reviews": […],
    "noteMetadata": […], "photoMetadata": […],
-   "explanation": "Тексты заметок и фотографии признаны результатом работы салона…",
+   "explanation": "Оператором заметок, фотографий и сведений, внесённых сотрудниками компании,
+                   является сама компания. Запрос об их предоставлении, уточнении или удалении
+                   направляйте ей; контакты — в разделе operators.",
+   "operators": [
+     { "companyId": "…", "name": "Салон N", "address": "…", "phone": "…", "email": "…",
+       "whatIsStored": ["bookings", "notes", "photos", "healthNotes"] }
+   ],
+   "consents": [ /* §38.4 */ ],
+   "notifications": [ { "sentAt": "…", "type": "Reminder", "status": "Delivered",
+                        "companyName": "…", "bodyAvailable": false } ],
+   "optOut": { "optedOut": false, "optedOutAt": null },
+   "healthNotes": [ { "companyName": "Салон N", "value": "аллергия на аммиак", "updatedAt": "…" } ]
  }
```

🔴 **Формулировка «признаны результатом работы салона» удаляется из кода и из выгрузки.** Она не
является основанием отказа (ч. 8 ст. 14 исчерпывающа). Греп по репозиторию на неё — часть приёмки.

`bodyAvailable: false` — текст затёрт по сроку хранения (§51). `operators` содержит **только
публичные реквизиты компании** — те же, что на её публичной странице.

**Что режется и в каком порядке** (§3.4 спеки): `consents` и `notifications`/`optOut` — первыми;
`operators` и `healthNotes` — **не режутся**.

---

## 50. Заявка на канал: ИНН и статус · **BREAKING № 7** · T5-B4

### 50.1 `POST /api/notification-channels` — дельта запроса

```diff
  { "…": "…",
+   "legalEntityForm": "Ip",          // "Ip" | "Company" | "SelfEmployed"
+   "inn": "770708389300",
+   "offerAccepted": { "version": "2026-09-21" }
  }
```

| Код | Когда |
|---|---|
| 201 | заявка создана; пишется строка `TermsOwner / Accepted / ChannelRequest` с `Purpose: "ChannelOffer"` |
| **400** | нет ИНН; длина не 10 и не 12; **контрольная сумма не сходится** — текст «ИНН указан неверно, проверьте цифры» |
| **400** | нет `legalEntityForm` или `offerAccepted` |
| 409 | версия соглашения владельца устарела |
| 451 | owner-гейт (§42.2) |

🔴 **Проверка ИНН формальная** (ПЛ5): длина и контрольная сумма, без обращения к ЕГРЮЛ/ЕГРИП.
Это ожидаемое состояние, а не недоделка (§13 п. 7 спеки).

🔴 **Бесплатный путь ИНН не требует нигде**: регистрация, создание компании, работа на бесплатном
тарифе не меняются ни на шаг (US-82 п. 1).

### 50.2 Видимость ИНН

ИНН отдаётся **только владельцу** (в карточке канала) и **суперадмину** (в админском списке).
Ни в одном публичном или клиентском ответе его нет.

### 50.3 `POST /api/notification-channels/{id}/connect` — новый код

| Код | Когда |
|---|---|
| **409** | `Notifications:GreenApi:InstanceCreationEnabled: false` — создание каналов приостановлено платформой (ПЛ1 не подтверждён). Текст человеческий, состояние канала не меняется |

Расхождение фактической страны сервера с ожидаемой гасит канал: состояние `Disconnected`,
`lastStateReason: "ServerCountryMismatch"`, текст для владельца — «требуется вмешательство
платформы». Владелец ничего не может с этим сделать сам, и интерфейс не должен предлагать ему
кнопку «повторить».

---

## 51. Журнал доставки: затёртые строки · T5-B9

`GET /api/companies/{id}/notifications` — дельта элемента:

```diff
  { "id": "…", "type": "Reminder", "status": "Delivered", "…": "…",
-   "body": "Здравствуйте, Мария! …", "recipientPhone": "+7 999 ***-**-67",
+   "body": null, "recipientPhone": null, "recipientName": null,
+   "contentRedacted": true
  }
```

🔴 **Фронт определяет «текст удалён по сроку хранения» по `contentRedacted`, а не по пустой строке.**
Метаданные (`status`, `reason`, даты, компания) сохраняются — журнал доставки и его сводка
(`/notifications/summary`) продолжают работать в полном объёме, включая измеритель доставки US-32.

Вебхуки статусов, приходящие после затирания, обрабатываются как обычно и обновляют статус.

---

## 52. Сводка изменений

### 52.1 Новые эндпоинты

| Метод | Путь | Доступ | Задача |
|---|---|---|---|
| GET | `/api/legal/texts/{key}` | публично | T5-B2 |
| GET | `/api/profile/consents` | авторизованные (allow-list) | T5-B3 |
| POST | `/api/profile/consents` | авторизованные (allow-list) | T5-B3 |
| POST | `/api/profile/consents/revoke` | авторизованные (allow-list) | T5-B3/B5 |
| GET | `/api/profile/consents/revoke-preview` | авторизованные (allow-list) | T5-B5 |
| GET | `/api/companies/{id}/clients/{key}/photo-consent` | персонал компании | T5-B7 |
| POST | `/api/companies/{id}/clients/{key}/photo-consent` | персонал компании | T5-B7 |
| GET | `/api/companies/{id}/clients/{key}/health-note` | `Master`/`CompanyOwner`, **403 SuperAdmin** | T5-B6 |
| PUT | `/api/companies/{id}/clients/{key}/health-note` | то же | T5-B6 |
| DELETE | `/api/companies/{id}/clients/{key}/health-note` | то же | T5-B6 |
| POST | `/api/companies/{id}/clients/{key}/health-consent` | персонал компании | T5-B6 |
| POST | `/api/subject-requests` | **анонимно** | T5-B10 |
| GET | `/api/admin/subject-requests` | SuperAdmin | T5-B10 |
| POST | `/api/admin/subject-requests/{id}/status` | SuperAdmin | T5-B10 |
| GET | `/api/admin/retention/policy` | SuperAdmin | T5-B8 |

### 52.2 🔴 Ломающие изменения — читать фронтенду первым делом

| № | Эндпоинт | Что ломается | Что делать фронту |
|---|---|---|---|
| 1 | везде | `LegalDocumentType.Terms` → `TermsClient` | заменить строковый литерал; маршрут `/terms` **сохраняется** |
| 2 | `POST /api/auth/register` | `acceptedLegal` удалён, добавлен `legal` | перестроить форму регистрации целиком (T5-F1) |
| 3 | `POST /api/companies` | добавлен `ownerTerms`; в ответе **новый токен** | добавить акцепт D3 в мастер создания компании и сохранить токен |
| 4 | `POST /api/client-notes/{id}/photos` | 400 без согласия | шаг согласия перед первой загрузкой |
| 5 | `POST /api/bookings` | 400 при `bookedForOther` без подтверждения | чекбокс + текст D12 в трёх формах |
| 6 | `PUT …/notification-templates/{type}` | 400 без `acknowledgement` | модалка подтверждения при каждом сохранении |
| 7 | `POST /api/notification-channels` | 400 без ИНН и оферты | поля ИНН/форма лица + акцепт оферты |
| — | `GET /api/legal/documents` | форма ответа: объект вместо массива | переписать чтение |
| — | `POST /api/legal/accept` | тело: список вместо двух полей | переписать `ConsentGate` |
| — | `GET /api/legal/consent-status` | `ownerActionBlocked` | обработать owner-модалку |
| — | `GET …/notifications` | `contentRedacted` | показывать «текст удалён по сроку хранения» |
| — | тарифы | `PhotoRetention.Forever` исчез | убрать вариант из селекта |

### 52.3 Что в контракте намеренно не меняется

- Аутентификация, роли, `SecurityStamp`, формат токена (кроме **одного нового claim'а** `lco`).
- Слоты, расписание, услуги, отзывы, отчёты, города, часовые пояса — **ни одного изменения**.
- Очередь, диспетчер, вебхук статусов, отписка `/u/{token}`, привязка по QR — кроме §50.3.
- `GET /api/profile/export`: **формат файла** (только состав секций).
- `POST /api/profile/delete-account`: поведение целиком — кроме того, что журнал согласий больше
  не удаляется (наружу это не видно).
- Пагинация, rate limiting (кроме новой политики), health-эндпоинты, формат ошибок.

---

## 53. Чек-лист согласования BE↔FE перед мёржем цикла

- [ ] `GET /api/legal/documents` отдаёт пять документов и шесть текстов; фронт строит форму
      регистрации **по `purposes` из ответа**, а не по зашитому массиву.
- [ ] Регистрация выполняется **двумя** вызовами; отказ от второго не мешает ни регистрации,
      ни последующей записи на визит (проверено вручную, не только тестом).
- [ ] 451 различается по `Content-Type`: глобальный — `text/plain`, owner — `application/json`.
- [ ] Из `ConsentGate` достижимы **выгрузка данных и отзыв согласия**.
- [ ] `POST /api/companies` возвращает новый токен, и фронт его сохраняет.
- [ ] Предпросмотр отзыва показывает **числа** («будет удалено 12 фотографий»), а не общие слова.
- [ ] Поле противопоказаний не приходит ни в одном ответе, кроме трёх своих эндпоинтов и экспорта
      (проверено грепом по сетевым ответам, а не по коду).
- [ ] Суперадмин получает 403 на health-note — и интерфейс не показывает ему пустой блок,
      а не показывает блок вовсе.
- [ ] `adMarkers` берутся с сервера; подсветка на фронте и проверка на сервере дают одинаковый
      результат на одном и том же тексте.
- [ ] Ответ `POST /api/subject-requests` одинаков для существующего и несуществующего номера.
- [ ] Затёртые строки журнала доставки определяются по `contentRedacted`.
- [ ] Греп фронта, README и лендинга: «сервис рассылок», «платформа информирования»,
      «мессенджер-маркетинг» — **0 вхождений**.
- [ ] Греп кода и выгрузки: «результат работы салона» — **0 вхождений**.
- [ ] Экран регистрации проходим с клавиатуры, ссылки на документы читаются скринридером,
      каждый блок имеет связанную подпись (НФТ §11.4 — неподтверждаемое согласие равно
      отсутствующему).
