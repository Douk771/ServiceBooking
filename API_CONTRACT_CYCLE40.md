# API_CONTRACT — цикл 40 ServiceBooking: упрощение каналов рассылок (WhatsApp / MAX)

**Разделы §40.20–§40.39.** Решения и механизмы — `ARCHITECTURE_CYCLE40.md` §40.0–§40.19. **Источник истины по форме** —
`contracts/cycle40/openapi.yaml` (пишется задачей BE-40-C первым коммитом цикла строго по §40.38 этого документа); при
расхождении текста со схемой по форме права схема, по смыслу, порядку проверок и текстам — этот документ. Рядом —
`contracts/cycle40/channel-vectors.json` (§40.39). Требования — `SPEC.md` цикла 40. Базовая ревизия — `develop` = `ece8038`.

Документ описывает **фактические существующие маршруты** каналов, настроек уведомлений, публичных страниц, админки и
биллинга (как они работают на `ece8038`) и то, что в них меняется, плюс три новых маршрута. Маршруты, которых цикл не
касается, не перечисляются. Корневой `API_CONTRACT.md` — документ цикла 3, не перезаписывается.

---

## §40.20. Конвенции (действуют без изменений)

- camelCase; enum — строками (имена членов C#); `DateTime` — ISO-8601 UTC (`…Z`); деньги опций — `number` (`decimal(10,2)`,
  как во всём биллинге).
- Осознанные 400/402/409/410/429/503 маршрутов каналов и настроек — **голая строка `text/plain`** по-русски (тексты —
  §40.33); фронт печатает её дословно. 401/403 — пустое тело; 404 — пустое тело, чужой канал **неотличим** от
  несуществующего. Исключения — уже существующие JSON-409 магазина (`CatalogConflictDto`) и «Домов» (`StaysConflictDto`).
- 451 — гейты правовых документов; владельческие действия — `[RequiresOwnerTerms]` (как сейчас).
- Новые поля DTO — **в конец** рекордов; ни одно существующее поле не удаляется и не переименовывается (§5.6 SPEC).
- Текст, который видит человек (состояние номера, причина, кнопка, подпись галочки, строки цен), собирает **сервер**.
- Транспорты: `NotificationTransport` = `WhatsApp` | `Max`; отображаемые имена — «WhatsApp» / «MAX».

---

## §40.21. Сводка изменений существующих маршрутов

| Маршрут | Что меняется | Раздел |
|---|---|---|
| `GET /api/notification-channels` | `ChannelDto` +6 полей в конце; `companies` = все компании аккаунта; `Replaced` по-прежнему в списке (старые вкладки), но с `displayStatus = null` | §40.22, §40.28 |
| `GET /api/notification-channels/offer` | `pricePerMonth` = цена опции WhatsApp; `allowedByPlan` всегда `true`; `transports[]` +`pricePerMonth`, `priceText`; `available` = опция продаётся ∧ сервис включён | §40.28.2 |
| `POST /api/notification-channels` | +`riskAccepted`; 402 по тарифу удалён; 200 при продлении заявки; новые 409 | §40.24 |
| `GET /api/notification-channels/{id}` | `ChannelDto` (новые поля) | §40.22 |
| `POST …/{id}/accept-risk` | без изменений | — |
| `POST …/{id}/connect` | 402 по тарифу удалён; из `Disconnected`; 202 идемпотентно при `Connecting`; новые 409 | §40.25 |
| `GET …/{id}/qr` | `Connecting` без экземпляра → 200 с `qrBase64: null` вместо 409 | §40.26 |
| `POST …/{id}/test-message` | +409 (сервис выключен, номер владельца = номер канала); пишет `lastTest` | §40.27 |
| `POST …/{id}/replace` | из любого привязанного состояния, а не только `Blocked` | §40.28.3 |
| `DELETE …/{id}` | судьба `Pending` (перепривязка) | §40.28.4 |
| `POST …/{id}/companies` | **410** без изменения данных | §40.28.5 |
| `DELETE …/{id}/companies/{companyId}` | **204** без изменения данных | §40.28.5 |
| `GET|PUT /api/companies/{companyId}/notification-settings` | 402 удалены; проверка приоритета удалена; +5 полей | §40.29.1 |
| `PUT /api/companies/{companyId}/notification-templates/{type}` | 402 «Канал не оплачен» удалён | §40.29.1 |
| `GET /api/companies/{companyId}/notifications/summary` | `channelPaidUntil` и `byCompany` — по аккаунту | §40.29.1 |
| `GET|PUT /api/shops/{shopId}/notification-settings` | `messengerAvailable` — по аккаунту; 400 приоритета удалён; +3 поля | §40.29.2 |
| `GET|PUT /api/stays/companies/{companyId}/notification-settings` | то же | §40.29.3 |
| `GET /api/companies/{slug}` | +`customerMessaging` | §40.30.1 |
| `GET /api/storefront/{slug}` | `customerNotifications.messengerOffered` по новому правилу; +2 поля | §40.30.2 |
| `GET /api/stays/public/companies/{slug}/houses/{houseSlug}` | +`messenger` | §40.30.3 |
| `POST /api/bookings` | +`notifyByMessenger` | §40.30.4 |
| `POST /api/storefront/{slug}/orders`, `POST /api/stays/public/houses/{houseId}/bookings` | правило «предлагается ли мессенджер» — новое; форма без изменений | §40.30.5 |
| `GET /api/admin/notification-channels` | +3 параметра; `AdminChannelDto` +8 полей; `companyCount` — по аккаунту | §40.31.1 |
| `GET /api/admin/notification-channels/summary` | +3 поля | §40.31.2 |
| `GET|PUT /api/admin/platform-settings` | +`customerMessagingEnabled`; `channelPricePerMonth` игнорируется на запись | §40.31.5 |
| `PUT /api/admin/billing-accounts/{id}/subscription` | для двух опций каналов — дата обязательна, правило тарифа не проверяется | §40.31.6 |
| `GET /api/admin/billing-accounts/{id}` | `numbersPaid` = число оплаченных транспортов; `channels[].assignedCompanies` = компании аккаунта | §40.31.6 |
| `GET /api/pricing` | +`messengerAddons` | §40.32 |
| `GET /api/billing/subscription` | +`messengerAddons`; опции каналов не в `availableOptions`; `plan.includes` без строк про мессенджеры; `coveredCompanies[].hasNumber` и `usage.numbersPaid/numbersText` — по новому правилу | §40.32 |
| `GET /api/admin/companies/{companyId}/transfer/preview` | `willDetachFromChannel`, `willCancelPendingNotifications` — новый смысл | §40.34 |
| `POST /api/admin/companies/{companyId}/transfer` | `Pending` компании перепривязываются к номеру нового аккаунта (форма без изменений) | §40.34 |
| `GET /api/profile/export` | записи (визиты): +3 поля | §40.35 |

**Новые маршруты (3):** `GET /api/notification-channels/overview`, `GET /api/admin/notification-channels/{id}`,
`POST /api/admin/notification-channels/{id}/confirm-payment`.

---

## §40.22. Общие DTO

### §40.22.1 `ChannelDto` — существующие поля без изменений формы, смысл некоторых уточнён; новые — в конце

| Поле | Тип | Смысл после цикла |
|---|---|---|
| `id`, `transport`, `state` | | как сейчас |
| `stateText` | string | детальный текст состояния; называет **правильный** мессенджер; без «Назначенные салоны» |
| `phoneMasked` | string? | как сейчас |
| `paymentState` | `Paid|NotPaid|Suspended` | `Suspended` — приостановлен; `Paid` — транспорт оплачен и номер `Funded` |
| `paidUntil` | date-time? | `PaidUntil` транспорта (§40.3.1 архитектуры) |
| `requestedAt`, `connectedAt`, `riskAcceptedAt`, `idleSince`, `idleDeadline`, `replacedByChannelId` | | как сейчас |
| `companies` | `ChannelCompanyDto[]` | **все** компании аккаунта (`companyId`, `companyName`, `isActive`), активные первыми, затем по дате создания |
| `canConnect` | bool | `wizardStep = Qr` для этого канала (§40.6.3 архитектуры) |
| `canReplace` | bool | `state ∉ {NotConnected, Replaced}` |
| `fundingState` | `Funded|Unfunded|NotPaid` | по транспорту (§40.3.2) |
| `fundingText` | string | §40.33.4 |
| `inn`, `legalEntityForm` | | как сейчас |
| **`displayStatus`** | `Working|ActionRequired|Off` или null | три состояния; null только у `Replaced` |
| **`displayText`** | string? | одна строка пояснения (§40.33.1) |
| **`action`** | `Pay|BindNumber|Reconnect|ReplaceNumber|Unbind` или null | код единственной кнопки |
| **`lastTest`** | `ChannelTestDto?` | результат последнего проверочного (автоматического или ручного); null — ни одного не было |
| **`isTrial`** | bool | транспорт оплачен пробным периодом |
| **`paymentPending`** | bool | «Оплата на проверке» (не оплачен ∧ заявка новее последнего подтверждения) |

### §40.22.2 `ChannelTestDto`

`{ result: ChannelTestResult, atUtc: date-time, text: string }`; `result` ∈ `Pending`, `Sending`, `Sent`, `Failed`,
`SkippedSameNumber`, `SkippedNoOwnerPhone`, `SkippedPlatformDisabled`. Тексты — §40.33.3.

### §40.22.3 `CustomerMessagingOfferDto` (публичный)

`{ offered: bool, transports: NotificationTransport[], checkboxLabel: string | null }`. `offered = false` →
`transports = []`, `checkboxLabel = null`. **Никогда** не содержит номера, состояния, оплаты, причины.

### §40.22.4 `MessengerAddonDto`

`{ transport: NotificationTransport, label: "WhatsApp" | "MAX", pricePerMonth: number, text: "+ WhatsApp 490 ₽/мес" }`.

---

## §40.23. `GET /api/notification-channels/overview` (новый) — всё для блока «Номера»

`[Authorize]`. Доступ — как у `GET /api/notification-channels`: владелец хотя бы одной компании или владелец хотя бы одного
канала; иначе 403. Один запрос даёт всё для блока «Номера» во всех трёх сайтах. Политика частоты — общая.

`NumbersOverviewDto`:

| Поле | Тип | Смысл |
|---|---|---|
| `messagingEnabled` | bool | глобальный выключатель |
| `messagingDisabledText` | string? | «Рассылки временно отключены платформой» при `false` |
| `note` | string | «Номера общие для всех ваших компаний» |
| `companies` | `OverviewCompanyDto[]` | все компании аккаунта: `id`, `name`, `kind` (`Services|Orders|Stays`), `isActive`; порядок — по дате создания |
| `companiesText` | string | «Работает для всех ваших компаний: {N}» |
| `offer` | `{ version: string, url: "/offer-channel" }` | версия `TermsOwner` для галочки оферты |
| `risk` | `{ version: string, html: string, url: "/channel-risk" }` | текущий `ChannelRiskNotice` |
| `transports` | `TransportNumbersDto[2]` | всегда два: `WhatsApp`, затем `Max` |

`TransportNumbersDto`:

| Поле | Тип | Смысл |
|---|---|---|
| `transport`, `displayName` | | `WhatsApp`/«WhatsApp», `Max`/«MAX» |
| `pricePerMonth` | number? | цена опции; null — не продаётся |
| `priceText` | string? | «490 ₽/мес» |
| `sellable` | bool | опция продаётся (§40.7.1 архитектуры) |
| `unavailableText` | string? | «Подключение {М} временно недоступно» при `!sellable ∧ !paid`, иначе null |
| `paid`, `paidUntil`, `isTrial` | | оплата транспорта |
| `paymentPending` | bool | «Оплата на проверке» |
| `canRequestPayment` | bool | `!paid ∨ isTrial` |
| `wizardStep` | `Payment|PaymentPending|Qr|Done|Unavailable|None` | с какого шага открывать мастер |
| `displayStatus`, `displayText`, `action` | | как у `ChannelDto`; при отсутствии канала: `paymentPending` → `ActionRequired`/«Оплата на проверке»/null; иначе все три null (фронт показывает кнопку «Подключить {М}») |
| `channel` | `ChannelDto?` | первый живой канал транспорта |
| `extraChannels` | `ChannelDto[]` | лишние номера (`fundingState = Unfunded`) |
| `connectionNotice` | string? | у MAX — «Для авторизации по QR в MAX нужно отключить пароль входа в мессенджере.» |
| `qrInstruction` | string[] | шаги привязки (§40.33.5) |
| `prefill` | `{ legalEntityForm, inn }?` | из последнего канала аккаунта с ИНН |

Пример (prism-мок должен его отдавать): WhatsApp оплачен и работает, MAX не куплен.

```json
{ "messagingEnabled": true, "messagingDisabledText": null, "note": "Номера общие для всех ваших компаний",
  "companies": [{ "id": "…", "name": "Салон «Лён»", "kind": "Services", "isActive": true }],
  "companiesText": "Работает для всех ваших компаний: 1",
  "offer": { "version": "2026-10-01", "url": "/offer-channel" },
  "risk": { "version": "2026-10-01", "html": "<p>…</p>", "url": "/channel-risk" },
  "transports": [
    { "transport": "WhatsApp", "displayName": "WhatsApp", "pricePerMonth": 490, "priceText": "490 ₽/мес", "sellable": true,
      "unavailableText": null, "paid": true, "paidUntil": "2026-11-08T00:00:00Z", "isTrial": false, "paymentPending": false,
      "canRequestPayment": false, "wizardStep": "Done", "displayStatus": "Working",
      "displayText": "Сообщения уходят с номера +7 9** ***-**-12", "action": null, "channel": { "…": "ChannelDto" },
      "extraChannels": [], "connectionNotice": null, "qrInstruction": ["…"], "prefill": { "legalEntityForm": "Ip", "inn": "…" } },
    { "transport": "Max", "displayName": "MAX", "pricePerMonth": 490, "priceText": "490 ₽/мес", "sellable": true,
      "unavailableText": null, "paid": false, "paidUntil": null, "isTrial": false, "paymentPending": false,
      "canRequestPayment": true, "wizardStep": "Payment", "displayStatus": null, "displayText": null, "action": null,
      "channel": null, "extraChannels": [], "connectionNotice": "Для авторизации по QR в MAX нужно отключить пароль входа в мессенджере.",
      "qrInstruction": ["…"], "prefill": { "legalEntityForm": "Ip", "inn": "…" } } ] }
```

---

## §40.24. `POST /api/notification-channels` — шаг «Оплата» (заявка)

`[Authorize]`, `[RequiresOwnerTerms]`. Тело `CreateChannelRequestDto`:

| Поле | Правило |
|---|---|
| `legalEntityForm` | обязательно (как сейчас) |
| `inn` | `InnValidator` (как сейчас) |
| `offerAccepted.version` | обязательно, = текущая версия `TermsOwner` (как сейчас) |
| `transport` | `WhatsApp` (по умолчанию) или `Max` (как сейчас) |
| **`riskAccepted.version`** | **новое**, необязательно; мастер шлёт всегда. Присутствует → должна совпасть с текущим `ChannelRiskNotice` |

**Порядок проверок (первый отказ):**
1. Не владелец компании → 403.
2. Форма лица или версия оферты пусты → 400 «Укажите форму юридического лица и примите условия оферты.»; ИНН → 400
   «ИНН указан неверно, проверьте цифры».
3. Правовые документы недоступны → 503 «Правовые документы временно недоступны.»
4. Версия оферты устарела → 409 «Соглашение владельца было обновлено ещё раз — перечитайте и примите новую редакцию.»
5. `riskAccepted` прислан и версия устарела → 400 «Текст изменился, прочитайте заново».
6. Сервис выключен → 409 «Подключение временно недоступно».
7. Живой канал транспорта у аккаунта есть, транспорт оплачен и не пробный → 409 «У вас уже есть номер {М}».
8. Транспорт не оплачен и опция не продаётся → 409 «Подключение {М} временно недоступно».
9. Живой канал транспорта есть (не оплачен или пробный) → **продление заявки** на этой строке: `RequestedAtUtc = now`,
   форма и ИНН перезаписываются, риск (если прислан) пишется, согласие с офертой пишется в `ConsentLedger` (цель
   `ChannelOffer`) → **200** `ChannelDto`.
10. Иначе → новая строка (`NotConnected`, `RequestedAtUtc = now`), риск и согласие — в той же транзакции → **201** `ChannelDto`.

**Удалено:** 402 «Подключение канала недоступно на вашем тарифе»; 409 «У аккаунта уже есть канал транспорта … в живом
состоянии» (заменён пунктами 7 и 9).

---

## §40.25. `POST /api/notification-channels/{id}/connect` — начало шага «QR»

`[Authorize]`, `[RequiresOwnerTerms]`, тело пустое. Порядок:
1. Чужой/нет → 404.
2. Сервис выключен → 409 «Рассылки временно отключены платформой».
3. Приостановлен → 409 «Номер приостановлен администратором».
4. Транспорт не оплачен или номер не `Funded` (лишний) → **402** «Номер не оплачен».
5. Риск не принят → 409 «Сначала подтвердите условия подключения».
6. `state = Connecting` → **202** `ConnectResponseDto {state: "Connecting", refreshAfterSeconds: 3}` — новый экземпляр
   **не** создаётся (идемпотентно: двойной клик, две вкладки).
7. `state = Connected` → 409 «Номер уже подключён»; `Blocked` → 409 «Номер заблокирован — замените его»; `Disconnected` с
   причиной `ServerCountryMismatch` → 409 «Требуется вмешательство платформы для восстановления канала»; `Replaced` → 409
   «Номер заменён».
8. `InstanceCreationEnabled = false` → 409 «Создание каналов приостановлено платформой» (как сейчас).
9. Нет ключа шифрования → 503 «Сервис подключения временно недоступен, попробуйте позже» (как сейчас).
10. Из `Disconnected`: старый экземпляр снимается (база → провайдер, §40.7.3 архитектуры). Создание экземпляра; ошибка
    провайдера → 503 (как сейчас); несовпадение страны → 503 «Требуется вмешательство платформы для восстановления канала»
    (как сейчас).
11. **202** `ConnectResponseDto`.

Разрешённые исходные состояния для создания экземпляра: `NotConnected`, `NeedsReconnect`, `DisabledByOwner`, `Disconnected`.
**Удалено:** 402 «Подключение канала недоступно на вашем тарифе».

---

## §40.26. `GET /api/notification-channels/{id}/qr` — опрос

Ответ `QrResponseDto {state, qrBase64, refreshAfterSeconds, expiresInSeconds}` (форма без изменений).
- Чужой/нет → 404.
- `state ∉ {Connecting, Connected}` → 409 «Канал не в процессе подключения» (как сейчас).
- `Connected` → 200 `{state: "Connected", qrBase64: null, refreshAfterSeconds: 3, expiresInSeconds: 0}`.
- **`Connecting` без записанного экземпляра → 200 `{state: "Connecting", qrBase64: null, refreshAfterSeconds: 2,
  expiresInSeconds: 0}`** (было 409). Фронт продолжает опрос.
- Секрет недоступен → 409 «Канал не в процессе подключения» (как сейчас).
- Провайдер сообщил «авторизован» → переход в `Connected` (под замком канала; метка автопроверки, §40.8 архитектуры) →
  200 `Connected`. Иначе — 200 с QR, `expiresInSeconds: 20`.

---

## §40.27. `POST /api/notification-channels/{id}/test-message` — ручная проверка (P2-кнопка, маршрут существует)

Порядок: 404 → сервис выключен: 409 «Рассылки временно отключены платформой» → `state ≠ Connected`: 409 «Канал не
подключён» → чаще раза в 5 минут: 429 «Проверять канал можно не чаще одного раза в 5 минут» → нет телефона у аккаунта:
409 «У вашего аккаунта не указан номер телефона» → номер канала совпадает с телефоном аккаунта и
`AllowSameNumber = false`: 409 «Номер канала совпадает с телефоном вашего аккаунта — проверочное сообщение на него не
отправляем» → отправка → 200 `TestMessageResponseDto {delivered, message}` (форма как сейчас). Результат пишется в
`lastTest` и в журнал канала.

---

## §40.28. Остальные маршруты владельца

### §40.28.1 `GET /api/notification-channels`, `GET /api/notification-channels/{id}`
Форма списка — `ChannelListDto {channels: ChannelDto[]}` (как сейчас), элементы — §40.22.1. Порядок — по `createdAt` ↓.

### §40.28.2 `GET /api/notification-channels/offer` (для старых вкладок; новый фронт читает overview)
`ChannelOfferDto`: `pricePerMonth` = цена опции `notifications.whatsapp` (null — не продаётся); `allowedByPlan` = **всегда
`true`**; `riskText`, `riskVersion` — как сейчас; `transports[]` (`TransportOfferDto`): `transport`, `displayName`,
`available` (= опция транспорта продаётся ∧ сервис включён), `connectionNotice`, **`pricePerMonth`**, **`priceText`** (в конец).

### §40.28.3 `POST /api/notification-channels/{id}/replace`
404 → `state = NotConnected` → 409 «Заменить можно только привязанный номер» → `state = Replaced` → 409 «Номер уже
заменён» → **201** `ReplaceChannelResponseDto {newChannelId, paidUntil, companiesMoved}` (форма как сейчас;
`companiesMoved` = число компаний аккаунта). Старый экземпляр гасится у провайдера, `Pending` переходят на новую строку.
**Удалено:** 409 «Канал не заблокирован».

### §40.28.4 `DELETE /api/notification-channels/{id}` — «Отвязать»
404 → 204. Номер → `DisabledByOwner`, экземпляр удаляется у провайдера (как сейчас). `Pending` этого номера:
перепривязываются на второй маршрутизируемый транспорт аккаунта (если флаг `RebindPendingToOtherTransport` включён и
дубля нет), иначе `Cancelled`/`BookingOrAssignmentCancelled` (§40.9 архитектуры).

### §40.28.5 Наследные маршруты назначений
- `POST /api/notification-channels/{id}/companies` — чужой/нет канал → 404; иначе **410** «Назначать компании больше не
  нужно: номер работает для всех ваших компаний». Тело не читается и не валидируется. `[RequiresOwnerTerms]` остаётся.
- `DELETE /api/notification-channels/{id}/companies/{companyId}` — чужой/нет канал → 404; иначе **204**. Данные не меняются,
  очередь не трогается.

### §40.28.6 `POST /api/notification-channels/{id}/accept-risk` — без изменений (старые вкладки).

---

## §40.29. Настройки уведомлений компаний

### §40.29.1 Салон — `GET|PUT /api/companies/{companyId}/notification-settings`

Права и `CompanyKindGuard.RejectNonSalonAsync` — как сейчас.

`NotificationSettingsDto` — существующие поля:

| Поле | После цикла |
|---|---|
| `enabledTypes`, `reminderLeadMinutes`, `minLeadMinutes`, `deliveryMode`, `priorityTransport` | как сейчас |
| `planAllowsChannel` | **всегда `true`** |
| `channel` (`SettingsChannelDto`: `assigned`, `channelId`, `state`, `stateText`, `paymentState`, `paidUntil`) | канал **приоритетного транспорта аккаунта**; `assigned` = такой канал есть |
| `effectiveEnabled` | = `messagingActive` |
| `blockedReason` | null или текст §40.33.6 |
| `connectedTransports` | работающие транспорты аккаунта |
| `priorityChannelHealthy` | приоритетный транспорт работает |

Новые (в конец): `messagingActive: bool`, `inactiveText: string?` («Подключите WhatsApp или MAX выше»),
`deliveryChoiceVisible: bool`, `priorityWarning: string?`, `workingTransports: NotificationTransport[]`.

`PUT` (`UpdateNotificationSettingsDto` — без изменений формы): проверки сроков (400, как сейчас) → сохранение. **Удалено:**
402 «Недоступно на вашем тарифе», 402 «Канал не оплачен», 400 «Приоритетный канал должен быть среди оплаченных
транспортов компании». `deliveryMode`/`priorityTransport` не прислан → не меняется (как сейчас); фронт присылает их только
при `deliveryChoiceVisible = true`.

`PUT /api/companies/{companyId}/notification-templates/{type}` — **удалено** 402 «Канал не оплачен»; остальное как сейчас.

`GET /api/companies/{companyId}/notifications/summary` — форма без изменений; `channelPaidUntil` = самый поздний
`paidUntil` оплаченных транспортов аккаунта; `byCompany` — разбивка по всем компаниям аккаунта, если их больше одной
(иначе null).

### §40.29.2 Магазин — `GET|PUT /api/shops/{shopId}/notification-settings`

`ShopNotificationSettingsDto` — существующие поля: `messengerAvailable` = у аккаунта есть оплаченный транспорт;
`messengerUnavailableText` — §40.33.6; `channels` (`ShopChannelStatusDto[]`) = номера аккаунта (первые живые обоих
транспортов). Новые (в конец): `messagingActive: bool`, `deliveryChoiceVisible: bool`, `priorityWarning: string?`.

`PUT`: `customerMessengerEnabled: true` без оплаченного транспорта → 409 `CatalogConflictDto {code: "MessengerUnavailable",
message: "Сначала подключите и оплатите номер WhatsApp или MAX"}` (код как сейчас, текст новый). **Удалено:** 400
«Приоритетный канал должен быть среди оплаченных каналов магазина».

### §40.29.3 «Дома» — `GET|PUT /api/stays/companies/{companyId}/notification-settings`

`StaysNotificationSettingsDto`: `messengerAvailable` — как у магазина. Новые (в конец): `messagingActive`,
`deliveryChoiceVisible`, `priorityWarning`. `PUT`: `guestMessengerEnabled: true` без оплаченного транспорта → 409
`StaysConflictDto {code: "MessengerUnavailable", message: "Подключите и оплатите номер WhatsApp или MAX, чтобы отправлять
сообщения гостям"}`. **Удалено:** 400 «Приоритетный канал должен быть среди оплаченных каналов компании».

---

## §40.30. Публичные страницы и создание записи / заказа / брони

### §40.30.1 `GET /api/companies/{slug}` (страница салона и виджет `/embed/:slug`)

`CompanyDto` — в конец поле **`customerMessaging: CustomerMessagingOfferDto?`**: заполняется **только** этим маршрутом
(в `GET /api/companies`, `/my`, `/member` и прочих — `null`). Для компаний вида `Orders`/`Stays` — `{offered: false, …}`
(их страницы переадресуют). Правило — §40.10.1 архитектуры; кеш ≤ 30 с.

### §40.30.2 `GET /api/storefront/{slug}` (витрина goods)

`customerNotifications` (`StorefrontCustomerNotificationsDto`): `webPushOffered` — как сейчас; `messengerOffered` — по
новому правилу (флаг магазина ∧ рассылки работают); в конец **`messengerTransports: NotificationTransport[]`**,
**`messengerLabel: string?`**.

### §40.30.3 `GET /api/stays/public/companies/{slug}/houses/{houseSlug}` (страница дома dom)

`PublicHouseDto` — в конец **`messenger: CustomerMessagingOfferDto`** (флаг `GuestMessengerEnabled` ∧ рассылки работают).

### §40.30.4 `POST /api/bookings` (запись ezbook)

`CreateBookingDto` — в конец **`notifyByMessenger: boolean | null`** (по умолчанию null). Проверок и новых ответов нет.
- Запись сотрудником за клиента (ручная запись) → сохраняется `null` при любом значении поля.
- Иначе сохраняется присланное. `true` → в записи сохраняются версия текста `BookingMessengerConsent` (если есть в
  манифесте) и момент.
- `false` → сообщения в мессенджер по этой записи не ставятся (`Skipped`/`ClientDeclinedMessenger`), включая перенос и
  отмену. `null` → поведение до цикла.
- Ответ (`BookingDto`) **не** сообщает, отписан ли номер и уйдёт ли сообщение.

### §40.30.5 `POST /api/storefront/{slug}/orders`, `POST /api/stays/public/houses/{houseId}/bookings`

Форма без изменений. `notifyByMessenger: true` учитывается, только если для компании сейчас `offered = true` (перепроверка
без кеша), иначе молча сохраняется `false` — как сейчас, но по новому правилу.

---

## §40.31. Админка

Все маршруты — `[Authorize(Roles = "SuperAdmin")]`.

### §40.31.1 `GET /api/admin/notification-channels` — таблица

Query (существующие): `state`, `paymentState`, `transport`, `page`, `pageSize`. **Новые:** `displayStatus`
(`Working|ActionRequired|Off`), `payment` (`Paid|NotPaid|Requested|Suspended|Trial`), `includeReplaced` (bool, по умолчанию
`false`). Неизвестное значение enum → 400 (поведение model binding, как у существующих). Порядок — `createdAt` ↓, `id`.

`PagedResult<AdminChannelDto>`; `AdminChannelDto` — существующие поля (`id`, `transport`, `state`, `paymentState`,
`ownerName`, `ownerPhoneMasked`, `paidUntil`, `companyCount` (**= компании аккаунта**), `idleSince`, `requestedAt`, `inn`,
`legalEntityForm`) + в конец: `displayStatus`, `displayText`, `stateText`, `phoneMasked`, `paymentText` (§40.33.7),
`isSuspended`, `createdAt`, `availableActions` (`Suspend|Resume|ConfirmPayment`[]).

### §40.31.2 `GET /api/admin/notification-channels/summary`

Существующие 8 полей — без изменений смысла (кроме `pendingRequests`: заявка новее подтверждения оплаты; `expiringIn7Days`:
`paidUntil` транспорта в [сейчас, +7 дней]). В конец: `working`, `actionRequired`, `off` (без `Replaced`).

### §40.31.3 `GET /api/admin/notification-channels/{id}` (новый) — карточка

404 — нет канала. `AdminChannelCardDto`:

| Поле | Тип / смысл |
|---|---|
| `channel` | `AdminChannelDto` |
| `billingAccountId`, `ownerUserId` | |
| `state`, `lastStateReason`, `lastStateReasonText` | детальное состояние |
| `legalEntityForm`, `inn` | |
| `requestedAt`, `riskAcceptedAt`, `riskAcceptedVersion`, `instanceCreatedAt`, `connectedAt`, `lastStateCheckAt`, `idleSince`, `idleDeadline` | даты |
| `lastTest` | `ChannelTestDto?` |
| `payment` | `{ paid, paidUntil, isTrial, requested, lastPaymentAt }` |
| `replacedByChannelId`, `replacesChannelId` | цепочка замен |
| `companies` | `[{ id, name, kind, isActive }]` — компании аккаунта |
| `stateEvents` | последние 200 `ChannelStateEvent`, новые первыми: `{ occurredAtUtc, fromState, toState, reason, reasonText, detail }` |
| `paymentEvents` | слияние журналов, новые первыми: `{ occurredAtUtc, kind: "Suspended"|"Resumed"|"PaymentConfirmed"|"OptionChanged", source: ChannelOptionChangeSource?, changedByName: string?, oldPaidUntil, newPaidUntil, comment }` |
| `availableActions` | `Suspend|Resume|ConfirmPayment` |

`ConfirmPayment` доступно, если канал не `Replaced` и опция транспорта есть в каталоге.

### §40.31.4 `POST /api/admin/notification-channels/{id}/confirm-payment` (новый)

Тело `{ months: int, comment: string? }`. Порядок: 404 → `months` не 1…12 → 400 «Срок — от 1 до 12 месяцев» → `comment` > 500
→ 400 «Комментарий — не длиннее 500 символов» → канал `Replaced` → 409 «Номер заменён — подтвердите оплату в карточке
нового номера» → опции транспорта нет в каталоге → 409 «Опция {М} отсутствует в каталоге» → **200** `AdminChannelCardDto`
(обновлённая). Эффект и журналы — §40.13.2 архитектуры (срок продлевается от `max(сейчас, текущий срок)`). Повтор запроса
продлевает ещё раз (это действие, а не установка значения; фронт блокирует кнопку на время запроса).

`POST …/{id}/suspend`, `POST …/{id}/resume` — без изменений формы; приостановка теперь реально останавливает отправку.

### §40.31.5 `GET|PUT /api/admin/platform-settings`

`AdminPlatformSettingsDto` — существующие поля без изменений формы; в конец **`customerMessagingEnabled: boolean | null`**.
GET — всегда `true|false`. PUT — `null`/не прислано → не менять; значение → запись через `PlatformSettingsWriter` (журнал).
`channelPricePerMonth` на PUT **принимается и игнорируется** (ни записи, ни строки журнала).

### §40.31.6 Биллинг-аккаунты

- `PUT /api/admin/billing-accounts/{id}/subscription` (все линейки): строка опции `notifications.whatsapp` или
  `notifications.max` без `paidUntil` → 400 «Для опций WhatsApp и MAX укажите дату окончания оплаты» (проверяется вместе с
  остальной валидацией строк опций, до записи). Для этих двух опций 409 «Опция недоступна на выбранном тарифе» **не
  выдаётся**. Остальное — как сейчас. Изменение срока/окончания этих опций пишется в журнал карточки канала.
- `GET /api/admin/billing-accounts/{id}`: форма без изменений; `numbersPaid` = число оплаченных транспортов (0…2);
  `channels[].assignedCompanies` = число компаний аккаунта.

---

## §40.32. Цены мессенджеров

- `GET /api/pricing` — `PublicPricingDto` + в конец **`messengerAddons: MessengerAddonDto[]`** (0…2 элемента, WhatsApp
  затем MAX; элемента нет, если опция без цены, неактивна или юридически не продаётся). Публичный выключатель цен действует
  на весь ответ, как сейчас.
- `GET /api/billing/subscription?line=Services|Orders|Stays` — `OwnerSubscriptionDto` + в конец **`messengerAddons`** (тот же
  список); `availableOptions` — **без** двух опций каналов; `plan.includes` — без строк «Сообщения гостям/покупателям в MAX и
  WhatsApp»; `usage.numbersPaid` — число оплаченных транспортов; `usage.numbersText` — §40.33.8; `coveredCompanies[].hasNumber`
  — у аккаунта есть работающий номер.

---

## §40.33. Тексты (сервер собирает, фронт выводит дословно)

`{М}` — «WhatsApp» или «MAX». Дата `{дд.мм.гггг}` — по Москве (как прочие даты оплаты биллинга).

### §40.33.1 `displayText` — таблица §40.6.2 архитектуры (дословно оттуда)

### §40.33.2 `stateText` (детальный)

| Состояние | Текст |
|---|---|
| `NotConnected` | «Номер не привязан» |
| `Connecting` | «Номер привязывается» |
| `Connected` | «Номер работает, сообщения уходят с номера {маска}» / «Номер работает» |
| `Disconnected` (`ServerCountryMismatch`) | «Требуется вмешательство платформы для восстановления канала» |
| `Disconnected` | «Связь с {М} разорвана — возможно, устройство отключено в приложении. Подключите заново» |
| `Blocked` | «{М} заблокировал этот номер. Восстановить его нельзя — подключите другой номер, оплаченный период сохранится» |
| `DisabledByOwner` | «Номер отключён вами» |
| `NeedsReconnect` (`SecretUnavailable`) | «Требуется повторная привязка после технических работ на платформе. Оплаченный период сохранён — подключите номер заново, повторная оплата не потребуется» |
| `NeedsReconnect` | «Номер был отключён, потому что им {N} {дней} никто не пользовался. Оплаченный период сохранён — подключите номер заново, повторная оплата не потребуется» |
| `Replaced` | «Этот номер заменён — используйте новый» |

### §40.33.3 `ChannelTestDto.text`

| `result` | Текст |
|---|---|
| `Pending`, `Sending` | «Отправляем проверочное сообщение на ваш номер…» |
| `Sent` | «Проверочное сообщение отправлено на ваш номер {маска телефона аккаунта}» |
| `Failed` | «Не удалось отправить проверочное сообщение» (+ « — номер не был на связи» / « — не удалось подтвердить отправку») |
| `SkippedSameNumber` | «Проверочное сообщение не отправлено: номер совпадает с телефоном вашего аккаунта. Проверьте работу записью на другой номер» |
| `SkippedNoOwnerPhone` | «Проверочное сообщение не отправлено: у вашего аккаунта не указан номер телефона» |
| `SkippedPlatformDisabled` | «Проверочное сообщение не отправлено: рассылки временно отключены платформой» |

Текст самого проверочного сообщения: «Проверка номера {М} для уведомлений ezbook.ru: если вы видите это сообщение, номер
работает.»

### §40.33.4 `fundingText`
`NotPaid` — «Номер {М} не оплачен»; `Funded` — «Оплачено до {дд.мм.гггг}» / «Пробный период до {дд.мм.гггг}»;
`Unfunded` — «Лишний номер {М}: сообщения уходят с {маска рабочего}. Отвяжите этот номер».

### §40.33.5 `qrInstruction`
- WhatsApp: [«Откройте WhatsApp на телефоне с номером, который будет отправлять сообщения», «Настройки → Связанные
  устройства → Привязка устройства», «Наведите камеру на QR-код на этом экране»].
- MAX (**черновик, сверяется на M40-02** с документацией GREEN-API MAX): [«Отключите пароль входа в настройках MAX», «Откройте
  MAX на телефоне с номером, который будет отправлять сообщения», «Профиль → Устройства → Подключить устройство», «Наведите
  камеру на QR-код на этом экране»].

### §40.33.6 Настройки компаний
- `blockedReason` (салон): «Рассылки временно отключены платформой» / «Подключите WhatsApp или MAX» (нет оплаченного) /
  «Номер не работает — откройте блок „Номера“» (оплачен, но ничего не работает) / null.
- `inactiveText`: «Подключите WhatsApp или MAX выше».
- `priorityWarning`: «Приоритетный номер не работает: выберите другой или „во все“».
- `messengerUnavailableText` (магазин, «Дома»): «Подключите номер WhatsApp или MAX в блоке „Номера“» (нет оплаченного) /
  «Номер оплачен, но ещё не привязан — привяжите его в блоке „Номера“» / null.

### §40.33.7 `paymentText` (админ)
«оплачено до {дд.мм.гггг}» / «пробный до {дд.мм.гггг}» / «заявка от {дд.мм}» / «приостановлен» / «не оплачено».

### §40.33.8 `numbersText` (страница подписки)
«WhatsApp: {оплачено до дд.мм.гггг | пробный до дд.мм.гггг | не подключён}; MAX: {…}».

### §40.33.9 Причины журнала доставки (`NotificationTexts.StatusText`)
`ClientDeclinedMessenger` — «клиент не выбрал сообщения в мессенджер»; `PlatformMessagingDisabled` — «рассылки отключены
платформой»; `ChannelAccountMismatch` — «номер принадлежит другому аккаунту». `NoUsableChannel` — «номер не привязан или
отключён» (было про назначение); `NotOnPaidPlan` — «номер не оплачен».

### §40.33.10 Тексты событий журнала канала (`reasonText` админа)
`ReplacedByOwner` — «Заменён владельцем»; `RebindStarted` — «Переподключение: старый экземпляр снят»; `TestMessageSent` —
«Проверочное сообщение отправлено»; `TestMessageFailed` — «Проверочное сообщение не отправлено»; `TestMessageSkipped` —
«Проверочное сообщение пропущено». Остальные — существующие тексты.

### §40.33.11 Все 4xx новых и изменённых маршрутов — сводно

| Код | Текст | Где |
|---|---|---|
| 400 | «Текст изменился, прочитайте заново» | POST заявки (риск), accept-risk |
| 400 | «Срок — от 1 до 12 месяцев» / «Комментарий — не длиннее 500 символов» | confirm-payment |
| 400 | «Для опций WhatsApp и MAX укажите дату окончания оплаты» | billing-accounts |
| 402 | «Номер не оплачен» | connect |
| 409 | «Подключение временно недоступно» | POST заявки (сервис выключен) |
| 409 | «Рассылки временно отключены платформой» | connect, test-message |
| 409 | «У вас уже есть номер {М}» | POST заявки |
| 409 | «Подключение {М} временно недоступно» | POST заявки |
| 409 | «Номер приостановлен администратором» | connect |
| 409 | «Номер уже подключён» / «Номер заблокирован — замените его» / «Номер заменён» | connect |
| 409 | «Заменить можно только привязанный номер» / «Номер уже заменён» | replace |
| 409 | «Номер канала совпадает с телефоном вашего аккаунта — проверочное сообщение на него не отправляем» | test-message |
| 409 | «Номер заменён — подтвердите оплату в карточке нового номера» / «Опция {М} отсутствует в каталоге» | confirm-payment |
| 410 | «Назначать компании больше не нужно: номер работает для всех ваших компаний» | POST …/companies |

---

## §40.34. Передача компании (админ, `CompanyTransferController`)

- `GET /api/admin/companies/{companyId}/transfer/preview` — форма `CompanyTransferPreviewDto` без изменений;
  `willDetachFromChannel` = у **исходного** аккаунта есть живой канал (компания перестанет слать с его номеров);
  `willCancelPendingNotifications` = число `Pending` компании, для которых у **целевого** аккаунта нет маршрутизируемого
  номера того же транспорта (остальные перепривяжутся).
- `POST /api/admin/companies/{companyId}/transfer` — форма без изменений; на шаге 6 `Pending` компании перепривязываются к
  маршрутизируемому номеру того же транспорта нового аккаунта, иначе отменяются (§40.9 архитектуры); наследная строка
  назначения удаляется, как сейчас (иначе составной FK не даст сменить аккаунт).

---

## §40.35. Персональные данные

`GET /api/profile/export` — в конец элемента записи (визита) в выгрузке: `notifyByMessenger` (bool | null),
`messengerConsentVersion`, `messengerConsentAtUtc`. Удаление аккаунта — без изменений (новые поля записи не являются ПДн
сверх уже обезличиваемых). Retention — без изменений.

---

## §40.36. Частота запросов

Новых политик нет. `overview` и админские маршруты — общие лимиты. `confirm-payment` защищён ролью и замком аккаунта.
`test-message` — прежнее ограничение «раз в 5 минут» (общее с автопроверкой через `LastTestMessageAtUtc`).

---

## §40.37. Сводка новых маршрутов (для `Cycle22RouteTable.golden.txt`)

| Метод | Путь | Авторизация | Атрибуты |
|---|---|---|---|
| GET | `/api/notification-channels/overview` | `[Authorize]` | — |
| GET | `/api/admin/notification-channels/{id:guid}` | `SuperAdmin` | — |
| POST | `/api/admin/notification-channels/{id:guid}/confirm-payment` | `SuperAdmin` | — |

Существующие маршруты сохраняют пути, методы и атрибуты.

---

## §40.38. Состав `contracts/cycle40/openapi.yaml` (задача BE-40-C)

Формат — как `contracts/cycle37/openapi.yaml`: OpenAPI 3.0.3; шапка-комментарий «источник истины по форме», ссылки на
`ARCHITECTURE_CYCLE40.md`, этот документ и `channel-vectors.json`; `info.title` «ServiceBooking API — цикл 40 (каналы
рассылок)», `info.version` «40.1.0», `license: Proprietary`; `servers` — `http://localhost:5000` и `http://localhost:4040`
(prism); `securitySchemes.bearerAuth`. Описываются **все** новые маршруты и **все** существующие, у которых меняется форма
(§40.21); у больших существующих DTO (`CompanyDto`, `PublicHouseDto`, `StorefrontDto`, `OwnerSubscriptionDto`,
`PublicPricingDto`, `AdminPlatformSettingsDto`, `CreateBookingDto`) схема описывает только новые и значимые поля и стоит с
`additionalProperties: true`. Ошибки `text/plain` — `schema: {type: string}` с `example` из §40.33.

**Теги:** `owner-numbers` (номера владельца и мастер), `company-settings` (настройки трёх видов), `public` (страницы и
создание), `admin-channels`, `billing` (цены, подписка, биллинг-аккаунты, настройки платформы).

**operationId (уникальны):**

| operationId | Маршрут |
|---|---|
| `getNotificationNumbersOverview` | GET `/api/notification-channels/overview` |
| `listNotificationChannels` | GET `/api/notification-channels` |
| `getNotificationChannelOffer` | GET `/api/notification-channels/offer` |
| `requestNotificationChannel` | POST `/api/notification-channels` (ответы 200, 201, 400, 403, 409, 503) |
| `getNotificationChannel` | GET `/api/notification-channels/{id}` |
| `connectNotificationChannel` | POST `/api/notification-channels/{id}/connect` (202, 402, 404, 409, 503) |
| `getNotificationChannelQr` | GET `/api/notification-channels/{id}/qr` |
| `sendNotificationChannelTestMessage` | POST `/api/notification-channels/{id}/test-message` (200, 404, 409, 429) |
| `replaceNotificationChannel` | POST `/api/notification-channels/{id}/replace` (201, 404, 409) |
| `disconnectNotificationChannel` | DELETE `/api/notification-channels/{id}` (204, 404) |
| `assignCompanyToChannelLegacy` | POST `/api/notification-channels/{id}/companies` (404, 410) |
| `unassignCompanyFromChannelLegacy` | DELETE `/api/notification-channels/{id}/companies/{companyId}` (204, 404) |
| `getCompanyNotificationSettings`, `updateCompanyNotificationSettings` | GET/PUT `/api/companies/{companyId}/notification-settings` |
| `getShopNotificationSettings`, `updateShopNotificationSettings` | GET/PUT `/api/shops/{shopId}/notification-settings` |
| `getStaysNotificationSettings`, `updateStaysNotificationSettings` | GET/PUT `/api/stays/companies/{companyId}/notification-settings` |
| `getPublicCompanyBySlug` | GET `/api/companies/{slug}` |
| `getStorefront` | GET `/api/storefront/{slug}` |
| `getPublicHouse` | GET `/api/stays/public/companies/{slug}/houses/{houseSlug}` |
| `createBooking` | POST `/api/bookings` (только тело запроса: новое поле) |
| `adminListNotificationChannels` | GET `/api/admin/notification-channels` |
| `adminGetNotificationChannelsSummary` | GET `/api/admin/notification-channels/summary` |
| `adminGetNotificationChannel` | GET `/api/admin/notification-channels/{id}` |
| `adminConfirmChannelPayment` | POST `/api/admin/notification-channels/{id}/confirm-payment` |
| `adminGetPlatformSettings`, `adminUpdatePlatformSettings` | GET/PUT `/api/admin/platform-settings` |
| `adminAssignSubscription` | PUT `/api/admin/billing-accounts/{id}/subscription` (только новое 400) |
| `adminGetCompanyTransferPreview` | GET `/api/admin/companies/{companyId}/transfer/preview` (только смысл полей в описании) |
| `getPublicPricing` | GET `/api/pricing` |
| `getOwnerSubscription` | GET `/api/billing/subscription` |

Пути `/api/notification-channels/overview` и `/api/notification-channels/{id}` (а также
`/api/admin/notification-channels/summary` и `/api/admin/notification-channels/{id}`) — литерал против шаблона;
проверить `redocly lint` (`no-ambiguous-paths`) в BE-40-C.

**Схемы (`components/schemas`), обязательные поля — все перечисленные, если не помечено `nullable`:**
`NotificationTransport`, `ChannelState`, `ChannelStateReason` (+5), `ChannelPaymentStatus`, `ChannelFundingState`,
`ChannelDisplayStatus`, `ChannelAction`, `ChannelWizardStep`, `ChannelTestResult`, `ChannelOptionChangeSource`,
`LegalEntityForm`, `NotificationDeliveryMode`, `NotificationType`, `NotificationReason` (+3, для журнала);
`ChannelCompanyDto`, `ChannelTestDto`, `ChannelDto` (§40.22.1; новые поля nullable: `displayStatus`, `displayText`, `action`,
`lastTest`), `ChannelListDto`, `TransportOfferDto`, `ChannelOfferDto`, `CreateChannelRequestDto` (`riskAccepted` nullable),
`ConnectResponseDto`, `QrResponseDto` (`qrBase64` nullable), `TestMessageResponseDto`, `ReplaceChannelResponseDto`,
`OverviewCompanyDto`, `TransportNumbersDto`, `NumbersOverviewDto` (§40.23), `SettingsChannelDto`, `NotificationSettingsDto`,
`UpdateNotificationSettingsDto`, `ShopNotificationSettingsDto`, `ShopNotificationSettingsInput`, `StaysNotificationSettingsDto`,
`StaysNotificationSettingsInput`, `CustomerMessagingOfferDto`, `CompanyDtoCycle40Part` (`customerMessaging`, nullable),
`StorefrontCustomerNotificationsDto`, `PublicHouseDtoCycle40Part` (`messenger`), `CreateBookingDtoCycle40Part`
(`notifyByMessenger`, nullable boolean), `AdminChannelDto`, `AdminChannelSummaryDto`, `AdminChannelStateEventDto`,
`AdminChannelPaymentEventDto`, `AdminChannelCardDto`, `ConfirmChannelPaymentInput` (`months` integer 1…12, `comment`
string ≤ 500 nullable), `AdminPlatformSettingsCycle40Part` (`customerMessagingEnabled` nullable boolean),
`MessengerAddonDto`, `PublicPricingCycle40Part`, `OwnerSubscriptionCycle40Part`, `CompanyTransferPreviewCycle40Part`.

**Обязательные `example`** (prism отдаёт их как мок): `NumbersOverviewDto` — пример §40.23; `ChannelDto` — по одному на
`Working`, `ActionRequired`/`Pay`, `ActionRequired`/«Оплата на проверке», `Off`/приостановлен; `AdminChannelCardDto` — с двумя
событиями каждого журнала; `CustomerMessagingOfferDto` — `offered: true` с двумя транспортами и `offered: false`.

**Проверка в CI и тестах (DO-40-02, BE-40-8):** строка `contracts/cycle40/openapi.yaml` в `contracts/redocly.yaml` и в шаге
`redocly lint` `.github/workflows/ci.yml`; `"types:api:cycle40": "openapi-typescript ../contracts/cycle40/openapi.yaml -o
src/types/api-cycle40.generated.ts"` в `frontend/package.json` + `git diff --exit-code`; `'cycle40'` в
`frontend/scripts/contracts-to-json.mjs` + сверка `contracts/cycle40/openapi.json`; `[InlineData("cycle40")]` в
`ServiceBooking.Tests/Tests/OpenApiContractValidatorTests.cs` — ответы реального бэкенда сверяются со схемой (тем же
инструментом `OpenApiContract`, что для cycle37), QA прогоняет schemathesis
(`schemathesis run contracts/cycle40/openapi.yaml --base-url http://localhost:5000 --checks all`).

---

## §40.39. Состав `contracts/cycle40/channel-vectors.json` (задача BE-40-C)

Один файл для юнит-тестов C# (`ServiceBooking.UnitTests`) и vitest (`frontend/src/utils/channelRules.test.ts`); правка
правила = правка векторов в том же коммите. Корень: `{ "version": 1, "now": "2026-10-20T09:00:00Z", "optionFunding": [...],
"routing": [...], "display": [...], "wizardStep": [...], "offer": [...], "addons": [...] }`. Каждый кейс —
`{ "name": "...", "input": {...}, "expect": {...} }`.

| Раздел | `input` | `expect` | Минимальный набор кейсов |
|---|---|---|---|
| `optionFunding` (§40.3.1) | `row` (`exists`, `endsAtUtc`, `paidUntilUtc`, `grantedByTrial`, `activatedAtUtc`), `trialEndsAtUtc`, `subscription` (`usable`, `paidUntil`) | `paid`, `paidUntil`, `isTrial` | нет строки; закончилась по `EndsAtUtc`; срок в будущем; срок вчера; триал без даты до/после конца триала; наследная без даты при годной/истёкшей подписке; «Старт» и истёкшая подписка с оплаченной опцией → `paid: true` (О4) |
| `routing` (§40.5) | `mode`, `priority`, `candidates[]` (`transport`, `paid`, `funded`, `suspended`, `state`) | `targets[]` | только MAX при умолчании WhatsApp → `[Max]`; оба, `PriorityChannel` → приоритетный; оба, `AllChannels` → оба; WhatsApp оплачен, но `NotConnected` + MAX работает → `[Max]`; WhatsApp `DisabledByOwner` → `[Max]`; приостановлен → не цель; ни одного → `[]` |
| `display` (§40.6.2) | `ChannelDisplayFacts` | `displayStatus`, `displayText`, `action` | все 18 строк таблицы × оба транспорта (тексты с правильным мессенджером) |
| `wizardStep` (§40.6.3) | те же факты + `hasChannel` | `wizardStep`, `canRequestPayment` | каждая строка таблицы; триал → `canRequestPayment: true` |
| `offer` (§40.10.1) | `platformEnabled`, `kind`, `companyFlag`, `showcase`, `mode`, `priority`, транспорты (`routable`, `working`) | `offered`, `transports`, `checkboxLabel` | салон без типов → нет; оба работают + «во все» → оба, «… в WhatsApp и MAX»; приоритетный сломан в `PriorityChannel` → нет (отклонение №3 архитектуры); сервис выключен → нет; витрина → нет |
| `addons` (§40.14) | опции (`code`, `isActive`, `pricePerMonth`, `legallySellable`) | `messengerAddons[]` | обе с ценой → «+ WhatsApp 490 ₽/мес», «+ MAX 490 ₽/мес»; без цены → строки нет; дробная цена «+ MAX 490,50 ₽/мес»; ни слова «от» |
