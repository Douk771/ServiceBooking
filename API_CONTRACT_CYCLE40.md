# API_CONTRACT — цикл 40 ServiceBooking: упрощение каналов рассылок (WhatsApp / MAX)

**Разделы §40.20–§40.39.** Решения и механизмы — `ARCHITECTURE_CYCLE40.md` §40.0–§40.19 (включая §40.0a — решения
заказчика от 2026-10-09 по `LEGAL_REVIEW_CYCLE40.md`). **Источник истины по форме** — `contracts/cycle40/openapi.yaml`
(пишется задачей BE-40-C первым коммитом цикла строго по §40.38); при расхождении по форме права схема, по смыслу, порядку
проверок и текстам — этот документ. Рядом — `contracts/cycle40/channel-vectors.json` (§40.39). Базовая ревизия — `ece8038`.

Документ описывает **фактические существующие маршруты** (как они работают на `ece8038`) и то, что в них меняется, плюс три
новых маршрута. Маршруты, которых цикл не касается, не перечисляются. Корневой `API_CONTRACT.md` не перезаписывается.

---

## §40.20. Конвенции (действуют без изменений)

- camelCase; enum — строками (имена членов C#); `DateTime` — ISO-8601 UTC (`…Z`); деньги опций — `number`.
- Осознанные 400/402/409/410/429/503 маршрутов каналов и настроек — **голая строка `text/plain`** по-русски (§40.33); фронт
  печатает её дословно. 401/403 — пустое тело; 404 — пустое тело, чужой канал неотличим от несуществующего. Исключения —
  существующие JSON-409 магазина (`CatalogConflictDto`) и «Домов» (`StaysConflictDto`).
- 451 — гейты правовых документов; владельческие действия — `[RequiresOwnerTerms]`.
- Новые поля DTO — **в конец** рекордов; существующие не удаляются и не переименовываются (§5.6 SPEC).
- Текст, который видит человек, собирает **сервер**. Ни один текст сервера не упоминает средства обхода блокировок (Т40-L-14).
- `NotificationTransport` = `WhatsApp` | `Max`; отображаемые имена — «WhatsApp» / «MAX».

---

## §40.21. Сводка изменений существующих маршрутов

| Маршрут | Что меняется | Раздел |
|---|---|---|
| `GET /api/notification-channels` | `ChannelDto` +6 полей; `companies` = все компании аккаунта | §40.22, §40.28 |
| `GET /api/notification-channels/offer` | закрытые/непродаваемые транспорты отсутствуют в `transports[]`; `pricePerMonth` = цена опции WhatsApp или null; `allowedByPlan` = `true`; `transports[]` +`pricePerMonth`, `priceText` | §40.28.2 |
| `POST /api/notification-channels` | +`riskAccepted`, +`paymentRequest`; 402 по тарифу удалён; закрытая опция → 409; 200 при продлении | §40.24 |
| `GET /api/notification-channels/{id}` | `ChannelDto` (новые поля) | §40.22 |
| `POST …/{id}/accept-risk` | без изменений | — |
| `POST …/{id}/connect` | 402 по тарифу удалён; условия текущей редакции → иначе 409; из `Disconnected`; 202 при `Connecting` | §40.25 |
| `GET …/{id}/qr` | `Connecting` без экземпляра → 200 с `qrBase64: null` | §40.26 |
| `POST …/{id}/test-message` | +409; пишет `lastTest` | §40.27 |
| `POST …/{id}/replace` | из любого привязанного состояния | §40.28.3 |
| `DELETE …/{id}` | `Pending` отменяются (по умолчанию) | §40.28.4 |
| `POST …/{id}/companies` / `DELETE …/{id}/companies/{companyId}` | 410 / 204 без изменения данных | §40.28.5 |
| `GET|PUT /api/companies/{companyId}/notification-settings` | 402 удалены; проверка приоритета удалена; +5 полей | §40.29.1 |
| `PUT /api/companies/{companyId}/notification-templates/{type}` | 402 удалён | §40.29.1 |
| `POST /api/companies/{companyId}/notification-templates/{type}/preview` | первой строкой «{Компания}:» | §40.29.1 |
| `GET /api/companies/{companyId}/notifications/summary` | по аккаунту | §40.29.1 |
| `GET|PUT /api/shops/{shopId}/notification-settings` | по аккаунту; 400 приоритета удалён; +3 поля | §40.29.2 |
| `GET|PUT /api/stays/companies/{companyId}/notification-settings` | то же | §40.29.3 |
| `GET /api/companies/{slug}` | +`customerMessaging` | §40.30.1 |
| `GET /api/storefront/{slug}` | `messengerOffered` по новому правилу; +2 поля | §40.30.2 |
| `GET /api/stays/public/companies/{slug}/houses/{houseSlug}` | +`messenger` | §40.30.3 |
| `POST /api/bookings` | +`notifyByMessenger` (клиент и сотрудник) | §40.30.4 |
| `BookingDto` (все ответы с записью) | +3 поля | §40.30.4 |
| `POST /api/storefront/{slug}/orders`, `POST /api/stays/public/houses/{houseId}/bookings` | новое «предлагается»; отметка вошедшего пишет согласие | §40.30.5 |
| `GET /api/notifications/preferences` | +`providerDeliveryConsent` | §40.30.6 |
| `GET /api/admin/notification-channels` | +3 параметра; +8 полей | §40.31.1 |
| `GET /api/admin/notification-channels/summary` | +3 поля | §40.31.2 |
| `POST /api/admin/notification-channels/{id}/suspend`, `/resume` | приостановка реально останавливает отправку | §40.31.4 |
| `GET|PUT /api/admin/platform-settings` | +`customerMessagingEnabled`, +`whatsAppOptionOpen`, +`maxOptionOpen`; `channelPricePerMonth` игнорируется на запись | §40.31.5 |
| `PUT /api/admin/billing-accounts/{id}/subscription` | две опции каналов: дата обязательна, правило тарифа не проверяется, закрытая → 409 | §40.31.6 |
| `GET /api/admin/billing-accounts/{id}` | `numbersPaid`, `channels[].assignedCompanies` — новый смысл | §40.31.6 |
| `GET /api/pricing` | +`messengerAddons`, +`messengerAddonsNote` | §40.32 |
| `GET /api/billing/subscription` | то же + опции каналов не в `availableOptions` и т. д. | §40.32 |
| `GET /api/admin/companies/{companyId}/transfer/preview`, `POST …/transfer` | новый смысл полей; перепривязка `Pending` | §40.34 |
| `GET /api/profile/export` | записи: +4 поля | §40.35 |

**Новые маршруты (3):** `GET /api/notification-channels/overview`, `GET /api/admin/notification-channels/{id}`,
`POST /api/admin/notification-channels/{id}/confirm-payment`.

---

## §40.22. Общие DTO

### §40.22.1 `ChannelDto` — существующие поля без изменений формы; новые — в конце

| Поле | Тип | Смысл после цикла |
|---|---|---|
| `id`, `transport`, `state`, `phoneMasked`, `requestedAt`, `connectedAt`, `riskAcceptedAt`, `idleSince`, `idleDeadline`, `replacedByChannelId`, `inn`, `legalEntityForm` | | как сейчас |
| `stateText` | string | детальный текст; правильный мессенджер (§40.33.2) |
| `paymentState` | `Paid|NotPaid|Suspended` | `Paid` — транспорт оплачен и номер `Funded` |
| `paidUntil` | date-time? | срок оплаты транспорта |
| `companies` | `ChannelCompanyDto[]` | **все** компании аккаунта |
| `canConnect` | bool | `wizardStep = Qr` |
| `canReplace` | bool | `state ∉ {NotConnected, Replaced}` |
| `fundingState`, `fundingText` | | по транспорту (§40.33.4) |
| **`displayStatus`** | `Working|ActionRequired|Off` или null | null только у `Replaced` |
| **`displayText`** | string? | §40.33.1 |
| **`action`** | `Pay|AcceptTerms|BindNumber|Reconnect|ReplaceNumber|Unbind` или null | единственная кнопка |
| **`lastTest`** | `ChannelTestDto?` | последнее проверочное |
| **`isTrial`** | bool | оплачен пробным периодом |
| **`paymentPending`** | bool | «Оплата на проверке» |

### §40.22.2 `ChannelTestDto`
`{ result: ChannelTestResult, atUtc, text }`; `result` ∈ `Pending`, `Sending`, `Sent`, `Failed`, `SkippedSameNumber`,
`SkippedNoOwnerPhone`, `SkippedPlatformDisabled`. Тексты — §40.33.3.

### §40.22.3 `CustomerMessagingOfferDto` (публичный)
`{ offered: bool, transports: NotificationTransport[], checkboxLabel: string | null }`. `offered = false` → `transports = []`,
`checkboxLabel = null`. Никогда не содержит номера, состояния, оплаты, причины.

### §40.22.4 `MessengerAddonDto`, `MessengerAddonsNoteDto`
`MessengerAddonDto { transport, label: "WhatsApp"|"MAX", pricePerMonth: number, text: "+ MAX 490 ₽/мес", footnote: string? }`;
`footnote` у WhatsApp — «Доступ к WhatsApp в России ограничен: сообщения могут не доходить», у MAX — null.
`MessengerAddonsNoteDto { conditionsUrl: "/offer-channel", conditionsLabel: "Условия", taxNote: string }`.

---

## §40.23. `GET /api/notification-channels/overview` (новый) — всё для блока «Номера»

`[Authorize]`. Доступ — как у `GET /api/notification-channels` (владелец компании или канала), иначе 403.

`NumbersOverviewDto`:

| Поле | Тип | Смысл |
|---|---|---|
| `messagingEnabled` | bool | глобальный выключатель |
| `messagingDisabledText` | string? | «Рассылки временно отключены платформой» при `false` |
| `note` | string | «Номера общие для всех ваших компаний» |
| `companies` | `OverviewCompanyDto[]` | `id`, `name`, `kind`, `isActive`; по дате создания |
| `companiesText` | string | «Работает для всех ваших компаний: {N}» |
| `offer` | `{ version, url: "/offer-channel" }` | версия `TermsOwner` |
| `risk` | `{ version, html, url: "/channel-risk" }` | текущий `ChannelRiskNotice` (полный текст для показа до отметки) |
| `statusNotice` | string | текст про статус (§40.33.12, Т40-L-13) |
| `transports` | `TransportNumbersDto[]` | **0…2 элемента**, порядок WhatsApp, MAX. Транспорт есть в списке, если опция **открыта**, либо у аккаунта уже есть его живой канал, оплата или триал |

`TransportNumbersDto`:

| Поле | Тип | Смысл |
|---|---|---|
| `transport`, `displayName` | | |
| **`open`** | bool | опция открыта для владельцев (Р40-Ю1) |
| `pricePerMonth`, `priceText` | number?, string? | цена; null — не продаётся |
| `sellable` | bool | открыта ∧ активна ∧ есть цена ∧ оферта опубликована |
| `unavailableText` | string? | «Подключение {М} сейчас недоступно» при `!sellable ∧ !paid` |
| `paid`, `paidUntil`, `isTrial` | | оплата транспорта |
| `paymentPending` | bool | «Оплата на проверке» |
| `canRequestPayment` | bool | `(!paid ∨ isTrial) ∧ sellable` |
| **`termsAccepted`** | bool | условия текущей редакции приняты (риск текущей версии, статус и ИНН заявлены, оферта текущей версии) |
| `wizardStep` | `Payment|PaymentPending|Terms|Qr|Done|Unavailable|None` | с какого шага открывать мастер |
| `displayStatus`, `displayText`, `action` | | как у `ChannelDto`; без канала: `paymentPending` → `ActionRequired`/«Оплата на проверке»/null; иначе null |
| `channel` | `ChannelDto?` | первый живой канал транспорта |
| `extraChannels` | `ChannelDto[]` | лишние номера |
| `connectionNotice` | string? | у MAX — про пароль входа |
| `qrInstruction` | string[] | §40.33.5 |
| `prefill` | `{ legalEntityForm, inn }?` | из последнего канала аккаунта с ИНН |

Пример (prism-мок): MAX открыт, оплачен пробным периодом, условия ещё не приняты; WhatsApp закрыт и у аккаунта его нет —
в списке один элемент.

```json
{ "messagingEnabled": true, "messagingDisabledText": null, "note": "Номера общие для всех ваших компаний",
  "companies": [{ "id": "…", "name": "Салон «Лён»", "kind": "Services", "isActive": true }],
  "companiesText": "Работает для всех ваших компаний: 1",
  "offer": { "version": "2026-10-20", "url": "/offer-channel" },
  "risk": { "version": "2026-10-20", "html": "<p>…</p>", "url": "/channel-risk" },
  "statusNotice": "Мессенджеры подключаются только тем, кто указал статус ИП, организации или самозанятого",
  "transports": [
    { "transport": "Max", "displayName": "MAX", "open": true, "pricePerMonth": 490, "priceText": "490 ₽/мес", "sellable": true,
      "unavailableText": null, "paid": true, "paidUntil": "2026-10-23T00:00:00Z", "isTrial": true, "paymentPending": false,
      "canRequestPayment": true, "termsAccepted": false, "wizardStep": "Terms", "displayStatus": null, "displayText": null,
      "action": null, "channel": null, "extraChannels": [],
      "connectionNotice": "Для авторизации по QR в MAX нужно отключить пароль входа в мессенджере.",
      "qrInstruction": ["…"], "prefill": null } ] }
```

---

## §40.24. `POST /api/notification-channels` — шаги «Оплата» и «Условия»

`[Authorize]`, `[RequiresOwnerTerms]`. Тело `CreateChannelRequestDto`:

| Поле | Правило |
|---|---|
| `legalEntityForm` | обязательно (как сейчас) |
| `inn` | `InnValidator` (как сейчас) |
| `offerAccepted.version` | обязательно, = текущая версия `TermsOwner` |
| `transport` | `WhatsApp` (по умолчанию) или `Max` |
| **`riskAccepted.version`** | новое, необязательно; мастер шлёт всегда; присутствует → = текущая версия `ChannelRiskNotice` |
| **`paymentRequest`** | новое, bool, по умолчанию `true`. `true` — шаг «Оплата» (заявка на оплату); `false` — шаг «Условия» (только статус, ИНН, оферта, риск — для уже оплаченного или пробного транспорта, Т40-L-03) |

**Порядок проверок (первый отказ):**
1. Не владелец компании → 403.
2. Форма лица или версия оферты пусты → 400 «Укажите форму юридического лица и примите условия оферты.»; ИНН → 400 «ИНН
   указан неверно, проверьте цифры».
3. Правовые документы недоступны → 503 «Правовые документы временно недоступны.»
4. Версия оферты устарела → 409 «Соглашение владельца было обновлено ещё раз — перечитайте и примите новую редакцию.»
5. `riskAccepted` прислан и устарел → 400 «Текст изменился, прочитайте заново».
6. Сервис выключен → 409 «Подключение временно недоступно».
7. **`paymentRequest = true`:**
   1. опция транспорта не продаётся (закрыта, неактивна, без цены, оферта не опубликована) → 409 «Подключение {М} сейчас
      недоступно»;
   2. живой канал есть, транспорт оплачен и не пробный → 409 «У вас уже есть номер {М}»;
   3. живой канал есть (не оплачен или пробный) → продление заявки: `RequestedAtUtc = now`, форма, ИНН, риск, согласие с
      офертой (`ConsentLedger`, цель `ChannelOffer`) → **200** `ChannelDto`;
   4. иначе → новая строка (`NotConnected`, `RequestedAtUtc = now`) → **201** `ChannelDto`.
8. **`paymentRequest = false`:**
   1. транспорт не оплачен (ни платёж, ни триал) → 409 «Сначала отправьте заявку на оплату {М}»;
   2. живой канал есть → форма, ИНН, риск, согласие с офертой обновляются, `RequestedAtUtc` не меняется → **200**;
   3. иначе → новая строка без `RequestedAtUtc` → **201**.
   Закрытость опции здесь не проверяется: это не продажа, транспорт уже оплачен.

**Удалено:** 402 «Подключение канала недоступно на вашем тарифе»; 409 «У аккаунта уже есть канал транспорта … в живом
состоянии».

---

## §40.25. `POST /api/notification-channels/{id}/connect` — начало шага «QR»

`[Authorize]`, `[RequiresOwnerTerms]`, тело пустое. Порядок:
1. Чужой/нет → 404.
2. Сервис выключен → 409 «Рассылки временно отключены платформой».
3. Приостановлен → 409 «Номер приостановлен администратором».
4. Транспорт не оплачен или номер не `Funded` → **402** «Номер не оплачен».
5. Условия не приняты в текущей редакции (риск не текущей версии, или нет статуса/ИНН, или оферта не текущей версии) → 409
   «Условия подключения обновились — примите их заново» (раньше — только при полном отсутствии принятия: «Сначала
   подтвердите условия подключения»; этот текст больше не выдаётся).
6. `state = Connecting` → **202** `{state: "Connecting", refreshAfterSeconds: 3}` без нового экземпляра.
7. `Connected` → 409 «Номер уже подключён»; `Blocked` → 409 «Номер заблокирован — замените его»; `Disconnected` с
   `ServerCountryMismatch` → 409 «Требуется вмешательство платформы для восстановления канала»; `Replaced` → 409 «Номер заменён».
8. `InstanceCreationEnabled = false` → 409 «Создание каналов приостановлено платформой».
9. Нет ключа шифрования → 503 «Сервис подключения временно недоступен, попробуйте позже».
10. Из `Disconnected` старый экземпляр снимается; создание экземпляра; ошибка провайдера → 503; страна не совпала с ожидаемой
    **для этого транспорта** → 503 «Требуется вмешательство платформы для восстановления канала». Сообщённая страна пишется в
    канал (`providerServerCountry` карточки админа).
11. **202** `ConnectResponseDto`.

Доступность опции (открыта/закрыта) здесь не проверяется — купленное работает.

---

## §40.26. `GET /api/notification-channels/{id}/qr` — опрос

Форма `QrResponseDto` без изменений. 404; `state ∉ {Connecting, Connected}` → 409 «Канал не в процессе подключения»;
`Connected` → 200 без QR; **`Connecting` без экземпляра → 200 `{state: "Connecting", qrBase64: null, refreshAfterSeconds: 2,
expiresInSeconds: 0}`**; секрет недоступен → 409; провайдер «авторизован» → переход в `Connected` (под замком канала, метка
автопроверки) → 200; иначе 200 с QR, `expiresInSeconds: 20`.

---

## §40.27. `POST /api/notification-channels/{id}/test-message`

404 → сервис выключен: 409 «Рассылки временно отключены платформой» → не `Connected`: 409 «Канал не подключён» → чаще раза
в 5 минут: 429 «Проверять канал можно не чаще одного раза в 5 минут» → нет телефона: 409 «У вашего аккаунта не указан номер
телефона» → номер совпадает с телефоном аккаунта и `AllowSameNumber = false`: 409 «Номер канала совпадает с телефоном вашего
аккаунта — проверочное сообщение на него не отправляем» → 200 `{delivered, message}`. Результат — в `lastTest` и журнал.

---

## §40.28. Остальные маршруты владельца

### §40.28.1 `GET /api/notification-channels`, `GET …/{id}`
`ChannelListDto {channels}` (как сейчас), элементы — §40.22.1; порядок `createdAt` ↓.

### §40.28.2 `GET /api/notification-channels/offer` (старые вкладки; новый фронт читает overview)
`pricePerMonth` = цена `notifications.whatsapp`, если WhatsApp продаётся, иначе null; `allowedByPlan` = `true`; `riskText`,
`riskVersion` — как сейчас; `transports[]` — **только продаваемые** транспорты (`transport`, `displayName`, `available` =
сервис включён, `connectionNotice`, в конец `pricePerMonth`, `priceText`).

### §40.28.3 `POST …/{id}/replace`
404 → `NotConnected` → 409 «Заменить можно только привязанный номер» → `Replaced` → 409 «Номер уже заменён» → **201**
`ReplaceChannelResponseDto {newChannelId, paidUntil, companiesMoved}` (`companiesMoved` = компании аккаунта). Удалено 409
«Канал не заблокирован».

### §40.28.4 `DELETE …/{id}` — «Отвязать»
404 → 204. `DisabledByOwner`, экземпляр удаляется. `Pending` номера **отменяются** (`BookingOrAssignmentCancelled`); перенос
на второй мессенджер — только при `Notifications:RebindPendingToOtherTransport = true` (по умолчанию `false`).

### §40.28.5 Наследные маршруты назначений
`POST …/{id}/companies` — 404 / **410** «Назначать компании больше не нужно: номер работает для всех ваших компаний» (тело не
читается). `DELETE …/{id}/companies/{companyId}` — 404 / **204**, данные не меняются.

---

## §40.29. Настройки уведомлений компаний

### §40.29.1 Салон — `GET|PUT /api/companies/{companyId}/notification-settings`

`NotificationSettingsDto`: `planAllowsChannel` = `true`; `channel` — канал приоритетного транспорта аккаунта;
`effectiveEnabled` = `messagingActive`; `blockedReason` — §40.33.6; `connectedTransports` — работающие транспорты;
`priorityChannelHealthy` — приоритетный работает. Новые (в конец): `messagingActive: bool`, `inactiveText: string?`,
`deliveryChoiceVisible: bool`, `priorityWarning: string?`, `workingTransports: NotificationTransport[]`.

`PUT`: проверки сроков (400, как сейчас) → сохранение. **Удалено:** 402 «Недоступно на вашем тарифе», 402 «Канал не
оплачен», 400 «Приоритетный канал должен быть среди оплаченных транспортов компании». `deliveryMode`/`priorityTransport` не
прислан → не меняется; фронт присылает их только при `deliveryChoiceVisible`.

Шаблоны: `PUT …/notification-templates/{type}` — удалён 402 «Канал не оплачен». `POST …/preview` — превью начинается строкой
`«Ваш салон»:` (как реальное сообщение, Т40-L-05). Сводка `…/notifications/summary` — `channelPaidUntil` и `byCompany` по
аккаунту.

### §40.29.2 Магазин — `GET|PUT /api/shops/{shopId}/notification-settings`

`messengerAvailable` = у аккаунта есть оплаченный транспорт; `messengerUnavailableText` — §40.33.6; `channels` = номера
аккаунта. Новые: `messagingActive`, `deliveryChoiceVisible`, `priorityWarning`. `PUT`: `customerMessengerEnabled: true` без
оплаченного транспорта → 409 `CatalogConflictDto {code: "MessengerUnavailable", message: "Сначала подключите и оплатите номер
WhatsApp или MAX"}`. Удалено 400 приоритета.

### §40.29.3 «Дома» — `GET|PUT /api/stays/companies/{companyId}/notification-settings`

`messengerAvailable` — как у магазина; новые: `messagingActive`, `deliveryChoiceVisible`, `priorityWarning`. `PUT`:
`guestMessengerEnabled: true` без оплаченного транспорта → 409 `StaysConflictDto {code: "MessengerUnavailable", message:
"Подключите и оплатите номер WhatsApp или MAX, чтобы отправлять сообщения гостям"}`. Удалено 400 приоритета.

---

## §40.30. Публичные страницы, создание записи / заказа / брони, профиль

### §40.30.1 `GET /api/companies/{slug}`
`CompanyDto` + в конец `customerMessaging: CustomerMessagingOfferDto?` — только этим маршрутом (в списках `null`); для
`Orders`/`Stays` — `{offered: false, …}`. `checkboxLabel` — «Получать уведомления о записи в {М}».

### §40.30.2 `GET /api/storefront/{slug}`
`customerNotifications.messengerOffered` — по новому правилу; в конец `messengerTransports`, `messengerLabel` («Получать
уведомления о заказе в {М}»).

### §40.30.3 `GET /api/stays/public/companies/{slug}/houses/{houseSlug}`
`PublicHouseDto` + в конец `messenger: CustomerMessagingOfferDto` («Получать уведомления о брони в {М}»).

### §40.30.4 `POST /api/bookings` (запись ezbook) и `BookingDto`

`CreateBookingDto` + в конец **`notifyByMessenger: boolean | null`** (по умолчанию null). Новых ответов и проверок нет. Смысл
зависит от того, кто создаёт запись:

| Кто | Значение | Что сохраняется |
|---|---|---|
| клиент (гость или вошедший) | `true` | `notifyByMessenger = true`, версия `BookingMessengerConsent` (или `fallback:<sha256>`), время; вошедший без действующего `PdnConsent/ProviderDelivery` — ещё и согласие в журнал (`ConsentSource.MessengerOptInBooking`) |
| клиент | `false` | `false` — сообщений по записи нет (`ClientDeclinedMessenger`), включая перенос и отмену |
| клиент | `null` / не прислано | `null` — сообщения только если номер принадлежит аккаунту с действующим `ProviderDelivery` (`NoProviderDeliveryConsent` иначе) |
| сотрудник (ручная запись) | `true` = отметка «Клиент согласился получать сообщения об этой записи…» | `true`, версия `StaffBookingMessengerConsentHint` (или `fallback:`), время, **кто отметил** |
| сотрудник | `false` / `null` | `null` — правило строки выше (только аккаунт с согласием) |

Отписка номера важнее любой отметки. Ответ создания **не** сообщает, отписан ли номер и уйдёт ли сообщение.

`BookingDto` (во всех ответах с записью) — в конец: `notifyByMessenger: boolean | null`, `messengerConsentAtUtc: date-time |
null`, `messengerConsentByStaff: boolean` (отметил сотрудник). Имя сотрудника не отдаётся.

### §40.30.5 Заказы и брони гостем

`POST /api/storefront/{slug}/orders`, `POST /api/stays/public/houses/{houseId}/bookings` — форма без изменений.
`notifyByMessenger: true` учитывается, только если сейчас `offered = true` (без кеша), иначе молча `false` (как сейчас).
Учтённая отметка вошедшего без действующего `ProviderDelivery` пишет согласие в журнал (`MessengerOptInOrder` /
`MessengerOptInStay`). Заказы и брони уже хранят версию и время отметки (`Orders`, `StayBookings` — без изменений формы).

### §40.30.6 `GET /api/notifications/preferences`

`NotificationPreferencesDto {enabled}` + в конец **`providerDeliveryConsent: bool`** — у пользователя действующее согласие
`PdnConsent/ProviderDelivery` (текущей редакции). Без телефона у аккаунта — `{enabled: true, providerDeliveryConsent: <по
журналу>}`. Галочка в форме у вошедшего стоит заранее только при `enabled ∧ providerDeliveryConsent` (Т40-L-09); при
`enabled = false` вместо галочки строка §40.33.13. `PUT` — без изменений.

---

## §40.31. Админка

Все маршруты — `[Authorize(Roles = "SuperAdmin")]`.

### §40.31.1 `GET /api/admin/notification-channels`
Существующие `state`, `paymentState`, `transport`, `page`, `pageSize` + новые `displayStatus` (`Working|ActionRequired|Off`),
`payment` (`Paid|NotPaid|Requested|Suspended|Trial`), `includeReplaced` (bool, `false`). `AdminChannelDto` — существующие поля
(`companyCount` = компании аккаунта) + в конец `displayStatus`, `displayText`, `stateText`, `phoneMasked`, `paymentText`,
`isSuspended`, `createdAt`, `availableActions` (`Suspend|Resume|ConfirmPayment`).

### §40.31.2 `GET /api/admin/notification-channels/summary`
Существующие 8 полей (`pendingRequests` — заявка новее подтверждения оплаты; `expiringIn7Days` — по сроку транспорта) + в
конец `working`, `actionRequired`, `off`.

### §40.31.3 `GET /api/admin/notification-channels/{id}` (новый)
404 — нет канала. `AdminChannelCardDto`: `channel` (`AdminChannelDto`), `billingAccountId`, `ownerUserId`, `state`,
`lastStateReason`, `lastStateReasonText`, `legalEntityForm`, `inn`, `requestedAt`, `riskAcceptedAt`, `riskAcceptedVersion`,
`termsAccepted`, `instanceCreatedAt`, `connectedAt`, `lastStateCheckAt`, `idleSince`, `idleDeadline`,
**`providerServerCountry: string?`**, `lastTest`, `payment {paid, paidUntil, isTrial, requested, lastPaymentAt}`,
**`optionOpen: bool`**, `replacedByChannelId`, `replacesChannelId`, `companies[]`, `stateEvents[]` (200 последних:
`occurredAtUtc, fromState, toState, reason, reasonText, detail`), `paymentEvents[]` (`occurredAtUtc, kind:
Suspended|Resumed|PaymentConfirmed|OptionChanged, source?, changedByName?, oldPaidUntil, newPaidUntil, comment`),
`availableActions`. `ConfirmPayment` доступно, если канал не `Replaced`, опция есть в каталоге и **открыта**.

### §40.31.4 `POST /api/admin/notification-channels/{id}/confirm-payment` (новый)
Тело `{ months: int, comment: string? }`. Порядок: 404 → `months` не 1…12 → 400 «Срок — от 1 до 12 месяцев» → `comment` > 500
→ 400 «Комментарий — не длиннее 500 символов» → `Replaced` → 409 «Номер заменён — подтвердите оплату в карточке нового
номера» → опции нет в каталоге → 409 «Опция {М} отсутствует в каталоге» → **опция закрыта** → 409 «Опция {М} закрыта для
подключения. Откройте её в блоке „Подключение мессенджеров“» → **200** `AdminChannelCardDto`. Срок продлевается от
`max(сейчас, текущий срок)`; повтор — ещё одно продление.

`suspend`/`resume` — форма без изменений; приостановленный номер больше не отправляет сообщения.

### §40.31.5 `GET|PUT /api/admin/platform-settings`
`AdminPlatformSettingsDto` — существующие поля + в конец:

| Поле | GET | PUT |
|---|---|---|
| `customerMessagingEnabled` | bool | `null` — не менять; значение — запись (журнал) |
| **`whatsAppOptionOpen`** | bool (нет ключа → умолчание конфигурации, `false`) | то же |
| **`maxOptionOpen`** | bool (нет ключа → умолчание конфигурации, `true`) | то же |

`channelPricePerMonth` на PUT принимается и игнорируется. Действие переключателей — мгновенно для новых запросов (кеш
инвалидируется после записи).

### §40.31.6 Биллинг-аккаунты
- `PUT …/billing-accounts/{id}/subscription` (все линейки), строки опций `notifications.whatsapp` / `notifications.max`:
  без `paidUntil` → 400 «Для опций WhatsApp и MAX укажите дату окончания оплаты»; **создание строки или продление срока
  закрытой опции** → 409 «Опция {М} закрыта для подключения. Откройте её в блоке „Подключение мессенджеров“» (неизменённая
  строка закрытой опции проходит); 409 «Опция недоступна на выбранном тарифе» для этих двух опций **не выдаётся**.
- `GET …/billing-accounts/{id}` — `numbersPaid` = число оплаченных транспортов; `channels[].assignedCompanies` = компании
  аккаунта.

---

## §40.32. Цены мессенджеров (Т40-L-11)

- `GET /api/pricing` — `PublicPricingDto` + в конец `messengerAddons: MessengerAddonDto[]` (0…2; только **продаваемые**:
  открыта, активна, с ценой, оферта опубликована) и `messengerAddonsNote: MessengerAddonsNoteDto | null` (null, если список
  пуст).
- `GET /api/billing/subscription?line=…` — `OwnerSubscriptionDto` + те же два поля; `availableOptions` — без двух опций каналов;
  `plan.includes` — без строк про мессенджеры; `usage.numbersPaid` — число оплаченных транспортов; `usage.numbersText` —
  §40.33.8; `coveredCompanies[].hasNumber` — есть работающий номер.

---

## §40.33. Тексты (сервер собирает, фронт выводит дословно)

`{М}` — «WhatsApp» или «MAX». Даты — по Москве.

### §40.33.1 `displayText` — таблица §40.6.2 архитектуры (дословно оттуда), включая «Примите условия подключения {М}».

### §40.33.2 `stateText`

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

Само сообщение: «Проверка номера {М} для уведомлений ezbook.ru: если вы видите это сообщение, номер работает.»

### §40.33.4 `fundingText`
`NotPaid` — «Номер {М} не оплачен»; `Funded` — «Оплачено до {дд.мм.гггг}» / «Пробный период до {дд.мм.гггг}»; `Unfunded` —
«Лишний номер {М}: сообщения уходят с {маска рабочего}. Отвяжите этот номер».

### §40.33.5 `qrInstruction`
- WhatsApp: [«Откройте WhatsApp на телефоне с номером, который будет отправлять сообщения», «Настройки → Связанные устройства
  → Привязка устройства», «Наведите камеру на QR-код на этом экране»].
- MAX (**черновик, сверяется на M40-02**): [«Отключите пароль входа в настройках MAX», «Откройте MAX на телефоне с номером,
  который будет отправлять сообщения», «Профиль → Устройства → Подключить устройство», «Наведите камеру на QR-код на этом
  экране»].

### §40.33.6 Настройки компаний
- `blockedReason` (салон): «Рассылки временно отключены платформой» / «Подключите WhatsApp или MAX» / «Номер не работает —
  откройте блок „Номера“» / null. Если у аккаунта нет ни одного открытого и ни одного оплаченного транспорта — «Подключение
  мессенджеров сейчас недоступно».
- `inactiveText`: «Подключите WhatsApp или MAX выше» (или «Подключите MAX выше», если открыт только MAX).
- `priorityWarning`: «Приоритетный номер не работает: выберите другой или „во все“».
- `messengerUnavailableText` (магазин, «Дома»): «Подключите номер в блоке „Номера“» / «Номер оплачен, но ещё не привязан —
  привяжите его в блоке „Номера“» / null.

### §40.33.7 `paymentText` (админ)
«оплачено до {дд.мм.гггг}» / «пробный до {дд.мм.гггг}» / «заявка от {дд.мм}» / «приостановлен» / «не оплачено».

### §40.33.8 `numbersText` (подписка)
По каждому показываемому транспорту: «{М}: {оплачено до дд.мм.гггг | пробный до дд.мм.гггг | не подключён}», через «; ».

### §40.33.9 Причины журнала доставки
`ClientDeclinedMessenger` — «клиент не выбрал сообщения в мессенджер»; `PlatformMessagingDisabled` — «рассылки отключены
платформой»; `ChannelAccountMismatch` — «номер принадлежит другому аккаунту»; `NoProviderDeliveryConsent` — «нет согласия
клиента на сообщения в мессенджер» (было про посредника); `NoUsableChannel` — «номер не привязан или отключён»; `NotOnPaidPlan` —
«номер не оплачен».

### §40.33.10 События журнала канала (админ)
`ReplacedByOwner` — «Заменён владельцем»; `RebindStarted` — «Переподключение: старый экземпляр снят»; `TestMessageSent` /
`Failed` / `Skipped` — «Проверочное сообщение отправлено / не отправлено / пропущено».

### §40.33.11 Все 4xx новых и изменённых маршрутов

| Код | Текст | Где |
|---|---|---|
| 400 | «Текст изменился, прочитайте заново» | POST заявки/условий, accept-risk |
| 400 | «Срок — от 1 до 12 месяцев» / «Комментарий — не длиннее 500 символов» | confirm-payment |
| 400 | «Для опций WhatsApp и MAX укажите дату окончания оплаты» | billing-accounts |
| 402 | «Номер не оплачен» | connect |
| 409 | «Подключение временно недоступно» | POST (сервис выключен) |
| 409 | «Подключение {М} сейчас недоступно» | POST `paymentRequest: true` (закрыта / не продаётся) |
| 409 | «Сначала отправьте заявку на оплату {М}» | POST `paymentRequest: false` |
| 409 | «У вас уже есть номер {М}» | POST `paymentRequest: true` |
| 409 | «Рассылки временно отключены платформой» | connect, test-message |
| 409 | «Номер приостановлен администратором» | connect |
| 409 | «Условия подключения обновились — примите их заново» | connect |
| 409 | «Номер уже подключён» / «Номер заблокирован — замените его» / «Номер заменён» | connect |
| 409 | «Заменить можно только привязанный номер» / «Номер уже заменён» | replace |
| 409 | «Номер канала совпадает с телефоном вашего аккаунта — проверочное сообщение на него не отправляем» | test-message |
| 409 | «Номер заменён — подтвердите оплату в карточке нового номера» / «Опция {М} отсутствует в каталоге» | confirm-payment |
| 409 | «Опция {М} закрыта для подключения. Откройте её в блоке „Подключение мессенджеров“» | confirm-payment, billing-accounts |
| 410 | «Назначать компании больше не нужно: номер работает для всех ваших компаний» | POST …/companies |

### §40.33.12 Тексты мастера (Т40-L-13, Т40-L-14)
- Под статусом: «Мессенджеры подключаются только тем, кто указал статус ИП, организации или самозанятого» (`statusNotice`
  overview; заменяет «Платные функции доступны только тем, кто ведёт предпринимательскую деятельность»).
- Заголовок шага `Terms`: «Условия подключения {М}»; пояснение на триале: «Пробный период уже включает {М}. Перед привязкой
  номера примите условия».
- Шаг `PaymentPending`: «Заявка отправлена. Администратор свяжется с вами для оплаты и подтвердит её — после этого привяжите
  номер здесь» (Q-40-7).
- Ни один текст мастера, подсказки и ошибки не упоминает средства обхода блокировок; страж-тест на сервере, линт строк на
  фронте (`MessengerTextsForbiddenWordsTests`, vitest-аналог).

### §40.33.13 Формы клиента и сотрудника
- Подпись галочки клиента — `checkboxLabel` сервера; правовая строка под ней — ключи `BookingMessengerConsent`,
  `OrderMessengerConsent`, `StayMessengerConsent` (запасные тексты — `LEGAL_REVIEW_CYCLE40.md` §5.3, §6.2, дословно).
- Вошедший с отпиской: «Уведомления в мессенджеры выключены в профиле» + ссылка «Изменить» → профиль ezbook.
- Отметка сотрудника: «Клиент согласился получать сообщения об этой записи в {М}»; подсказка — ключ
  `StaffBookingMessengerConsentHint` (запасной текст — обзор §7.3).

---

## §40.34. Передача компании (админ)

- `GET /api/admin/companies/{companyId}/transfer/preview` — форма без изменений; `willDetachFromChannel` = у исходного аккаунта
  есть живой канал; `willCancelPendingNotifications` = число `Pending`, для которых у целевого аккаунта нет маршрутизируемого
  номера того же транспорта.
- `POST /api/admin/companies/{companyId}/transfer` — форма без изменений; `Pending` компании перепривязываются к номеру того же
  транспорта нового аккаунта, иначе отменяются; наследная строка назначения удаляется, как сейчас.

---

## §40.35. Персональные данные

`GET /api/profile/export` — в конец элемента записи (визита): `notifyByMessenger`, `messengerConsentVersion`,
`messengerConsentAtUtc`, `messengerConsentByStaff`. Удаление аккаунта и retention — без изменений. Новые записи журнала
согласий (`ConsentSource.MessengerOptIn*`) видны в разделе «Согласия» профиля и в выгрузке существующим путём.

---

## §40.36. Частота запросов

Новых политик нет. `test-message` — «раз в 5 минут» (общее с автопроверкой).

---

## §40.37. Сводка новых маршрутов (для `Cycle22RouteTable.golden.txt`)

| Метод | Путь | Авторизация | Атрибуты |
|---|---|---|---|
| GET | `/api/notification-channels/overview` | `[Authorize]` | — |
| GET | `/api/admin/notification-channels/{id:guid}` | `SuperAdmin` | — |
| POST | `/api/admin/notification-channels/{id:guid}/confirm-payment` | `SuperAdmin` | — |

---

## §40.38. Состав `contracts/cycle40/openapi.yaml` (задача BE-40-C)

Формат — как `contracts/cycle37/openapi.yaml`: OpenAPI 3.0.3; шапка «источник истины по форме»; `info.title` «ServiceBooking
API — цикл 40 (каналы рассылок)», `info.version` «40.2.0», `license: Proprietary`; `servers` — `:5000` и `:4040` (prism);
`bearerAuth`. Описываются все новые маршруты и все существующие, у которых меняется форма (§40.21); большие существующие DTO
(`CompanyDto`, `PublicHouseDto`, `StorefrontDto`, `OwnerSubscriptionDto`, `PublicPricingDto`, `AdminPlatformSettingsDto`,
`CreateBookingDto`, `BookingDto`, `NotificationPreferencesDto`) — только новые поля, `additionalProperties: true`. Ошибки
`text/plain` — `schema: {type: string}` с `example` из §40.33.

**Теги:** `owner-numbers`, `company-settings`, `public`, `profile`, `admin-channels`, `billing`.

**operationId:**

| operationId | Маршрут |
|---|---|
| `getNotificationNumbersOverview` | GET `/api/notification-channels/overview` |
| `listNotificationChannels` | GET `/api/notification-channels` |
| `getNotificationChannelOffer` | GET `/api/notification-channels/offer` |
| `requestNotificationChannel` | POST `/api/notification-channels` (200, 201, 400, 403, 409, 503) |
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
| `createBooking` | POST `/api/bookings` (тело — новое поле; ответ — `BookingDtoCycle40Part`) |
| `getNotificationPreferences` | GET `/api/notifications/preferences` |
| `adminListNotificationChannels` | GET `/api/admin/notification-channels` |
| `adminGetNotificationChannelsSummary` | GET `/api/admin/notification-channels/summary` |
| `adminGetNotificationChannel` | GET `/api/admin/notification-channels/{id}` |
| `adminConfirmChannelPayment` | POST `/api/admin/notification-channels/{id}/confirm-payment` (200, 400, 404, 409) |
| `adminGetPlatformSettings`, `adminUpdatePlatformSettings` | GET/PUT `/api/admin/platform-settings` |
| `adminAssignSubscription` | PUT `/api/admin/billing-accounts/{id}/subscription` (новые 400 и 409) |
| `adminGetCompanyTransferPreview` | GET `/api/admin/companies/{companyId}/transfer/preview` |
| `getPublicPricing` | GET `/api/pricing` |
| `getOwnerSubscription` | GET `/api/billing/subscription` |

Литеральные пути против шаблонных (`…/overview` и `…/{id}`, `…/summary` и `…/{id}`) — проверить `redocly lint`
(`no-ambiguous-paths`).

**Схемы:** `NotificationTransport`, `ChannelState`, `ChannelStateReason` (+5), `ChannelPaymentStatus`, `ChannelFundingState`,
`ChannelDisplayStatus`, `ChannelAction` (с `AcceptTerms`), `ChannelWizardStep` (с `Terms`), `ChannelTestResult`,
`ChannelOptionChangeSource`, `ConsentSource` (+3, для выгрузки/«Согласий» — описанием), `LegalEntityForm`,
`NotificationDeliveryMode`, `NotificationType`, `NotificationReason` (+3); `ChannelCompanyDto`, `ChannelTestDto`, `ChannelDto`,
`ChannelListDto`, `TransportOfferDto`, `ChannelOfferDto`, `CreateChannelRequestDto` (`riskAccepted` nullable, `paymentRequest`
boolean default true), `ConnectResponseDto`, `QrResponseDto`, `TestMessageResponseDto`, `ReplaceChannelResponseDto`,
`OverviewCompanyDto`, `TransportNumbersDto` (`open`, `termsAccepted`), `NumbersOverviewDto` (`transports` `minItems: 0`,
`maxItems: 2`, `statusNotice`), `SettingsChannelDto`, `NotificationSettingsDto`, `UpdateNotificationSettingsDto`,
`ShopNotificationSettingsDto`, `ShopNotificationSettingsInput`, `StaysNotificationSettingsDto`, `StaysNotificationSettingsInput`,
`CustomerMessagingOfferDto`, `CompanyDtoCycle40Part`, `StorefrontCustomerNotificationsDto`, `PublicHouseDtoCycle40Part`,
`CreateBookingDtoCycle40Part` (`notifyByMessenger` nullable boolean), `BookingDtoCycle40Part` (`notifyByMessenger` nullable,
`messengerConsentAtUtc` nullable, `messengerConsentByStaff`), `NotificationPreferencesDtoCycle40Part`
(`providerDeliveryConsent`), `AdminChannelDto`, `AdminChannelSummaryDto`, `AdminChannelStateEventDto`,
`AdminChannelPaymentEventDto`, `AdminChannelCardDto` (`providerServerCountry`, `optionOpen`, `termsAccepted`),
`ConfirmChannelPaymentInput`, `AdminPlatformSettingsCycle40Part` (`customerMessagingEnabled`, `whatsAppOptionOpen`,
`maxOptionOpen` — nullable boolean), `MessengerAddonDto`, `MessengerAddonsNoteDto`, `PublicPricingCycle40Part`,
`OwnerSubscriptionCycle40Part`, `CompanyTransferPreviewCycle40Part`.

**Обязательные `example`:** `NumbersOverviewDto` — пример §40.23 (MAX на шаге `Terms`, WhatsApp скрыт) и второй (оба открыты,
WhatsApp работает, MAX `Payment`); `ChannelDto` — `Working`, `ActionRequired`/`Pay`, `ActionRequired`/`AcceptTerms`,
«Оплата на проверке», `Off`/приостановлен; `AdminChannelCardDto` — с событиями обоих журналов; `CustomerMessagingOfferDto` —
`offered: true` и `false`; `AdminPlatformSettingsCycle40Part` — WhatsApp закрыт, MAX открыт.

**CI и тесты (DO-40-02, BE-40-8):** `contracts/redocly.yaml` + шаг lint; `types:api:cycle40` + `git diff --exit-code`;
`contracts-to-json.mjs` (+`cycle40`) + сверка `openapi.json`; `[InlineData("cycle40")]` в `OpenApiContractValidatorTests`;
schemathesis (`schemathesis run contracts/cycle40/openapi.yaml --base-url http://localhost:5000 --checks all`).

---

## §40.39. Состав `contracts/cycle40/channel-vectors.json` (задача BE-40-C)

Один файл для C# (`ServiceBooking.UnitTests`) и vitest (`frontend/src/utils/channelRules.test.ts`); правка правила = правка
векторов в том же коммите. Корень: `{ "version": 2, "now": "2026-10-20T09:00:00Z", "availability": [...], "optionFunding":
[...], "routing": [...], "consent": [...], "display": [...], "wizardStep": [...], "offer": [...], "addons": [...] }`; кейс —
`{ "name", "input", "expect" }`.

| Раздел | `input` | `expect` | Минимальный набор кейсов |
|---|---|---|---|
| `availability` (§40.7.1) | `settingValue` (`"true"|"false"|null`), `configDefault`, `isActive`, `pricePerMonth`, `termsOwnerPublished`, `transport` | `open`, `sellable` | WhatsApp без ключа → закрыт; MAX без ключа → открыт; ключ `"true"` открывает WhatsApp; MAX открыт, но без цены → не продаётся; MAX открыт, оферта черновая → не продаётся (Т40-L-04); мусор в ключе → умолчание |
| `optionFunding` (§40.3.1) | `row` (`exists`, `endsAtUtc`, `paidUntilUtc`, `grantedByTrial`, `activatedAtUtc`), `trialEndsAtUtc`, `subscription` (`usable`, `paidUntil`), `optionOpen` | `paid`, `paidUntil`, `isTrial` | нет строки; `EndsAtUtc` прошёл; срок в будущем / вчера; триал без даты до/после конца; наследная без даты при годной/истёкшей подписке; «Старт» с оплаченной опцией → `paid` (О4); **закрытая, но оплаченная → `paid: true`** |
| `routing` (§40.5) | `mode`, `priority`, `candidates[]` (`transport`, `paid`, `funded`, `suspended`, `state`) | `targets[]` | только MAX при умолчании WhatsApp → `[Max]`; оба / `PriorityChannel` → приоритетный; оба / `AllChannels` → оба; WhatsApp `NotConnected` + MAX работает → `[Max]`; WhatsApp `DisabledByOwner` → `[Max]`; приостановлен → не цель; ни одного → `[]` |
| `consent` (§40.11.2) | `source` (`Guest|Customer|Staff`), `notifyByMessenger` (`true|false|null`), `recipientIsAccount`, `accountHasProviderDeliveryConsent`, `optedOut` | `decision` (`Allowed|Declined|NoConsent`), `skipReason`, `stored` (`notifyByMessenger`, `consentByStaff`), `writesConsentLedger` | гость `true` → Allowed, журнал не пишется; гость `false` → Declined; гость `null` → NoConsent; вошедший `true` без согласия → Allowed + журнал; вошедший `null` с согласием → Allowed; сотрудник `true` → Allowed, `consentByStaff: true`; сотрудник `false` → хранится `null`, номер без аккаунта → NoConsent; сотрудник `null`, номер аккаунта с согласием → Allowed; любая отметка при отписке → `RecipientOptedOut` |
| `display` (§40.6.2) | `ChannelDisplayFacts` | `displayStatus`, `displayText`, `action` | все 19 строк × оба транспорта |
| `wizardStep` (§40.6.3) | те же факты + `hasChannel` | `wizardStep`, `canRequestPayment` | каждая строка; **триал без условий → `Terms`**; оплачен, риск старой версии → `Terms`; закрыт и не оплачен → `Unavailable`; триал → `canRequestPayment: true` (если продаётся) |
| `offer` (§40.10) | `platformEnabled`, `kind`, `companyFlag`, `showcase`, `mode`, `priority`, транспорты (`routable`, `working`) | `offered`, `transports`, `checkboxLabel` | салон без типов → нет; оба работают + «во все» → оба; приоритетный сломан в `PriorityChannel` → нет; сервис выключен → нет; витрина → нет; подписи для записи/заказа/брони |
| `addons` (§40.14) | опции (`code`, `open`, `isActive`, `pricePerMonth`, `legallySellable`) | `messengerAddons[]`, `noteShown` | только MAX открыт → одна строка «+ MAX 490 ₽/мес», без сноски; оба открыты → две, сноска у WhatsApp; без цены → строки нет; дробная цена «+ MAX 490,50 ₽/мес»; ни слова «от»; пустой список → `noteShown: false` |
