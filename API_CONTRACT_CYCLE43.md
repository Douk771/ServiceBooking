# API_CONTRACT — цикл 43 ServiceBooking: опции мессенджеров — цена, отвязка от тарифов, подключение отдельно

**Разделы §43.20–§43.34.** Точка синхронизации backend, frontend и QA на время цикла. Решения — `ARCHITECTURE_CYCLE43.md`
(§43.0–§43.17). **Форма** (поля, типы, обязательность, перечисления) — `contracts/cycle43/openapi.yaml`; при расхождении по форме прав он.
Здесь — смысл, порядок проверок, коды и **точные тексты** ответов. Эталон правил — `contracts/cycle43/messenger-vectors.json`.
Замороженные контракты `contracts/cycle7`, `cycle19`, `cycle40`, `cycle42` не правятся. Изменения существующих маршрутов — только добавочные
(новые поля в конец, ослабление проверок, новые 409 у операций денег).

---

## §43.20. Общие соглашения (без изменений с цикла 40)

- camelCase; перечисления — строками (имена членов C#); `DateTime` — ISO-8601 UTC; деньги — `number` (рубли, до 2 знаков).
- Тела осознанных 400/409 новых и изменённых маршрутов — **голая строка `text/plain`** по-русски; фронт показывает её дословно.
- 401/403 — пустое тело; 404 — пустое тело (чужой и несуществующий неотличимы; неизвестный `{transport}` — тоже 404).
- `{transport}` в пути — `WhatsApp` или `Max` (без учёта регистра). Отображаемые имена — «WhatsApp» / «MAX».
- Даты в текстах — московское время, `dd.MM.yyyy`. Цена в тексте — `MessengerTexts.PriceText`: целое без копеек («490 ₽/мес»),
  иначе два знака через запятую («490,50 ₽/мес»), без разделителя тысяч. Сумма — то же без «/мес» («1470 ₽»).
- Все новые маршруты — только SuperAdmin.
- В текстах нет слов «VPN», «ВПН», «прокси», «обход».

## §43.21. Сводка маршрутов цикла

| Метод и путь | Новый / изменён | § |
|---|---|---|
| `GET /api/admin/messenger-options` | новый | §43.22 |
| `POST /api/admin/messenger-options/{transport}/price/preview` | новый | §43.23 |
| `PUT /api/admin/messenger-options/{transport}/price` | новый | §43.24 |
| `GET /api/admin/billing-accounts/{id}/messengers/{transport}/quote?months=N` | новый | §43.25 |
| `POST /api/admin/billing-accounts/{id}/messengers/{transport}/extend` | новый | §43.26 |
| `POST /api/admin/billing-accounts/{id}/messengers/{transport}/disable` | новый (P1) | §43.27 |
| `GET /api/admin/billing-accounts/{id}` | изменён (форма) | §43.28 |
| `PUT /api/admin/billing-accounts/{id}/subscription` | изменён (поведение; ответ = карточка §43.28) | §43.29 |
| `POST /api/admin/notification-channels/{id}/confirm-payment` | изменён (вход, новые 409) | §43.30 |
| `GET /api/billing/subscription?line=` | изменён (форма) | §43.31 |
| `GET /api/admin/billing-accounts`, `GET /api/profile` | изменён смысл `totalMonthlyPrice` (форма та же) | §43.32 |
| `GET|POST|PUT /api/admin/plans` | изменено поведение (форма та же) | §43.33 |
| `POST|PUT|DELETE /api/admin/options…`, `PUT /api/admin/platform-settings` | журнал и сброс кешей (форма та же) | §43.34 |

---

## §43.22. `GET /api/admin/messenger-options` — экран цены

**Ответ 200** `AdminMessengerOptionsDto`:
- `items[]` — ровно две строки (`WhatsApp`, затем `Max`), `AdminMessengerOptionDto`:
  `transport`, `label`, `optionId`, `open` (переключатель «открыто для владельцев»), `isActive`, `pricePerMonth` (`null` — не задана),
  `priceText` («490 ₽/мес» | «не задана»), `sale` (`sellable`, `reason`, `text`), `activePaidAccounts`, `history[]` — последние 50 записей
  журнала цены этой опции, новые сверху (`MessengerPriceChangeDto`: `changedAt`, `changedByName` (или `null`), `oldPricePerMonth`,
  `newPricePerMonth`, `oldPriceText`, `newPriceText`, `oldIsActive`, `newIsActive`, `comment`, `source`, `activePaidAccounts`).
- `planMatrixNote` — «Мессенджеры WhatsApp и MAX подключаются отдельно от тарифа. Цена — во вкладке уведомлений».
- `priceHint` — «Цена действует для всех аккаунтов и всех линеек. Мессенджер продаётся, когда он открыт для владельцев, опция активна,
  цена задана и оферта опубликована. Изменение цены не меняет уже оплаченные периоды».

Строки опции нет в каталоге (не должно случаться) → строка всё равно отдаётся: `optionId = null`, `isActive = false`, `pricePerMonth = null`,
`sale.reason = Inactive` (или `Closed`, если мессенджер закрыт). **401/403.**

### §43.22.1 `sale` — итог продажи (`ChannelOptionAvailability.Explain`, векторы `sale`)

Проверки по порядку, первая неуспешная — причина:

| `reason` | `text` |
|---|---|
| `null` (продаётся) | «Продаётся» |
| `Closed` | «Не продаётся: закрыт для владельцев» |
| `Inactive` | «Не продаётся: опция неактивна» |
| `NoPrice` | «Не продаётся: цена не задана» |
| `OfferNotPublished` | «Не продаётся: оферта не опубликована» |

`sellable == (reason == null)` и всегда равно `ChannelOptionAvailability.Sellable(...)` цикла 40.

## §43.23. `POST /api/admin/messenger-options/{transport}/price/preview` — последствия без записи

**Тело** `MessengerPriceInput`: `pricePerMonth` (`number|null`, поле обязательно: `null` — снять цену), `isActive` (`boolean|null`; `null` — не менять).

**Порядок:** 401/403 → `{transport}` неизвестен — 404 → валидация (§43.23.1, 400) → нет строки в каталоге — 409 «Опция MAX отсутствует в каталоге»
→ 200 `MessengerPricePreviewDto`.

### §43.23.1 Тексты 400 (общие для preview и PUT; векторы `priceValidation`)

| Условие | Текст |
|---|---|
| `pricePerMonth < 0` | «Цена не может быть отрицательной» |
| `pricePerMonth > 99 999 999,99` | «Цена — не больше 99 999 999,99 ₽» |
| больше двух знаков после запятой | «Цена — не больше двух знаков после запятой» |
| цена и активность совпадают с текущими | «Цена и активность не изменились — сохранять нечего» |
| (только PUT) `comment` пуст после обрезки пробелов | «Укажите причину изменения цены» |
| (только PUT) `comment` длиннее 500 | «Причина — не длиннее 500 символов» |

### §43.23.2 Ответ `MessengerPricePreviewDto`

`transport`, `currentPricePerMonth`, `newPricePerMonth`, `currentIsActive`, `newIsActive`, `sellableBefore`, `sellableAfter`,
`startsSelling`, `stopsSelling`, `isZeroPrice`, `activePaidAccounts`, `consequences[]` (тексты по порядку §43.23.3), `confirmTitle`
(«Изменить цену MAX?»), `confirmButtonText` («Сохранить»).

### §43.23.3 Тексты последствий (`MessengerPriceChangeRules.Consequences`, векторы `consequences`), порядок фиксирован; {M} — «WhatsApp»/«MAX»

1. `startsSelling` (не продавался → продаётся): «{M} начнёт продаваться: цена появится на странице тарифов и в мастере подключения».
   `stopsSelling` (продавался → нет): «Продажи {M} остановятся. Уже оплаченные периоды продолжат действовать».
2. Обе цены заданы и различаются: при N > 0 — «Оплата {M} действует у {N} {аккаунта|аккаунтов}: их оплаченные периоды не меняются.
   Новая цена — для следующих подтверждений оплаты» (форма слова: «аккаунта», если N % 10 = 1 и N % 100 ≠ 11, иначе «аккаунтов»);
   при N = 0 — «Аккаунтов с действующей оплатой {M} нет».
3. Новая цена 0: «Цена 0 ₽: {M} будет подключаться бесплатно».
4. Новая цена задана, но после изменения не продаётся по причине `Closed`/`Inactive`/`OfferNotPublished`: «{M} не продаётся: {закрыт для
   владельцев|опция неактивна|оферта не опубликована}. Цена начнёт действовать, когда это изменится».

Пустой список допустим (например, сняли цену у непродававшегося): экран показывает только `confirmTitle`.

## §43.24. `PUT /api/admin/messenger-options/{transport}/price` — сохранить

**Тело** `UpdateMessengerPriceInput`: `pricePerMonth` (`number|null`, обязательно), `isActive` (`boolean|null`), `comment` (обязательно, 1…500),
`expectedPricePerMonth` (`number|null` — цена, от которой считались последствия), `expectedIsActive` (`boolean`, обязательно).

**Порядок:** 401/403 → 404 (`{transport}`) → 400 (§43.23.1) → [транзакция, замок `subscription-option:{optionId}`] → 409 «Опция MAX отсутствует
в каталоге» → 409 при расхождении `expected*` с текущим: «Цену MAX уже изменил другой администратор. Обновите страницу и проверьте
последствия ещё раз» → запись (цена, активность, `UpdatedAtUtc`; остальные поля не трогаются) + строка журнала (`source =
MessengerPriceScreen`, `activePaidAccounts` — снимок) → коммит → сброс кешей → **200** `AdminMessengerOptionDto` (свежее состояние с журналом).

Гарантия: любой запрос, начатый после получения этого ответа, видит новую цену во всех местах (мастер, `/api/pricing`, «Подписка»,
суммы админки).

## §43.25. `GET /api/admin/billing-accounts/{id}/messengers/{transport}/quote?months=N` — расчёт

`months` — целое 1…24, обязательно.

**Порядок:** 401/403 → 404 (нет аккаунта или `{transport}`) → 400 «Срок — от 1 до 24 месяцев» → 409 «Опция MAX отсутствует в каталоге» →
409 причина «не продаётся» (§43.25.4) → 200 `MessengerQuoteDto`.

**Ответ** (векторы `quote`): `transport`, `months`, `pricePerMonth`, `amount` (`months × pricePerMonth`), `amountText` («3 мес. × 490 ₽ = 1470 ₽»),
`currentPaidUntil` (`ChannelOptionFunding.PaidUntil` или `null`), `newPaidUntil` (`max(now, currentPaidUntil ?? now) + months`),
`newPaidUntilText` («Будет оплачено до 20.01.2027»), `actionText` («Подключить на 3 мес.» | «Продлить на 3 мес.» — «Продлить», когда статус
строки `Paid` или `Ending`).

### §43.25.4 Тексты 409 «не продаётся» (общие для quote, extend, confirm-payment)

| Причина | Текст |
|---|---|
| `Closed` | «Опция MAX закрыта для подключения. Откройте её в блоке „Подключение мессенджеров“» (существующий текст цикла 40) |
| `Inactive` | «Опция MAX неактивна. Включите её во вкладке уведомлений, блок „Подключение мессенджеров“» |
| `NoPrice` | «У MAX не задана цена. Задайте её во вкладке уведомлений, блок „Подключение мессенджеров“» |
| `OfferNotPublished` | «Оферта для владельцев не опубликована — MAX не продаётся» |

## §43.26. `POST /api/admin/billing-accounts/{id}/messengers/{transport}/extend` — подключить или продлить

**Тело** `ExtendMessengerInput`: `months` (1…24), `comment` (`string|null`, ≤ 500), `expectedPricePerMonth` (`number`, обязательно — `pricePerMonth` из quote).

**Порядок:** 401/403 → 404 → 400 «Срок — от 1 до 24 месяцев» → 400 «Комментарий — не длиннее 500 символов» → 400 «Не передана ожидаемая цена — обновите
расчёт» (поле отсутствует или `null`) → [транзакция, замок `billing-account:{id}`] → 409 «Опция MAX отсутствует в каталоге» → 409 §43.25.4 →
409 «Цена MAX изменилась: теперь 590 ₽/мес. Проверьте сумму и повторите» → запись (`ARCHITECTURE_CYCLE43.md` §43.6.2) → **200** карточка аккаунта (§43.28).

Номер не нужен. Пробная строка выкупается (становится обычной). Отключение, назначенное на будущее, снимается. Повтор запроса — ещё одно
продление (как у «Подтвердить оплату»). Два одновременных запроса выполняются по очереди; оба продления применяются.

Журналы: строка `ChannelOptionChangeLogs` (источник `AdminBillingAccount`, цена, месяцы, сумма) и строка истории подписки
(`GET …/subscription-history`: `changeKind = "Options"`, `oldOptionsSummary` «MAX: оплачено до 20.10.2026» / «MAX: не подключён»,
`newOptionsSummary` «MAX: оплачено до 20.01.2027», `comment` «Продление MAX на 3 мес. (3 мес. × 490 ₽ = 1470 ₽). {комментарий}» или
«Подключение MAX на …»).

## §43.27. `POST /api/admin/billing-accounts/{id}/messengers/{transport}/disable` — отключить (P1)

**Тело** `DisableMessengerInput`: `when` (`Now` | `PeriodEnd`), `comment` (обязательно, 1…500).

**Порядок** (векторы `disable`): 401/403 → 404 → 400 «Укажите причину отключения» / «Причина — не длиннее 500 символов» → [транзакция, замок
`billing-account:{id}`] → 409 «Нечего отключать: MAX у аккаунта не подключён» (строки нет) → 409 «Нечего отключать: оплата MAX уже закончилась»
(не оплачено) → (`PeriodEnd`, пробная строка без своей даты) 409 «У пробного периода MAX нет даты окончания — отключите сейчас» →
(`PeriodEnd`, у оплаты нет даты — наследная строка при подписке без даты) 409 «У оплаты MAX нет даты окончания — отключите сейчас» →
(`PeriodEnd`, отключение уже назначено) 409 «Отключение MAX уже назначено на 20.11.2026» → запись: `Now` — `EndsAtUtc = now` (в том числе при
уже назначенном отключении); `PeriodEnd` — `EndsAtUtc = оплачено до` → журналы (источник `AdminOptionEnded`; история подписки
«Отключение MAX с 20.11.2026. {причина}») → **200** карточка аккаунта.

Денег не возвращаем. После `Now` транспорт ведёт себя как при истёкшей оплате (статус «Оплата закончилась {сегодня}»).

## §43.28. `GET /api/admin/billing-accounts/{id}` — карточка (изменение формы)

Форма cycle7 + последующих циклов **плюс в конец** (схема `AdminBillingAccountCycle43Part`):
- `options[]` — **без** строк WhatsApp и MAX. Остальные опции — как раньше, по правилам тарифа.
- `messengers[]` — ровно две строки (`WhatsApp`, `Max`), `AdminMessengerLineDto`:
  - `transport`, `label`, `optionId` (или `null`);
  - `status` (`NotConnected|Trial|Paid|Ending|Expired|PendingPayment`), `statusText` (§43.28.1);
  - `pricePerMonth` (текущая цена каталога или `null`), `priceText` («490 ₽/мес» | «Цена не задана»);
  - `monthlyPrice`, `countsInTotal`, `paidUntil`, `isTrial`, `endsAt`;
  - `number`: `state` (`None` | `AwaitingQr` | `Bound`), `text` («Номера нет» | «Номер ожидает привязки по QR» | «Номер +7 9** ***-**-12»
    или «Номер привязан»), `phoneMasked`, `channelId`;
  - `sale` (§43.22.1);
  - `canExtend` (= продаётся), `extendActionText` («Подключить» | «Продлить»), `extendDisabledText` (= `sale.text`, если нельзя, иначе `null`);
  - `canDisable` (строка есть, оплачена, не отключена сейчас), `disableNowConfirmText` («Деньги не возвращаются. Сообщения клиентам в MAX
    перестанут уходить с 20.10.2026» — сегодняшняя дата; векторы `disableConfirmText`), `disablePeriodEndConfirmText` (то же с датой
    «оплачено до» или `null`, если `PeriodEnd` недопустим по §43.27).
- `messengersMonthlyTotal` — `Σ monthlyPrice` строк с `countsInTotal`.
- `totalMonthlyPrice` = `plan.pricePerMonth + Σ options[].pricePerMonth + messengersMonthlyTotal`.

### §43.28.1 Тексты статуса (`MessengerLineRule`, векторы `line`; одинаковы в админке и у владельца)

| `status` | Текст |
|---|---|
| `Ending` | «Отключается {дата}» |
| `Trial` | «Пробный период до {дата}», пока окно рассылок не началось — «Пробный период» |
| `Paid` | «Оплачено до {дата}», без даты — «Оплачено» |
| `PendingPayment` | «Ожидает подтверждения оплаты» |
| `NotConnected` | «Не подключён» |
| `Expired` (триал) | «Пробный период закончился {дата}» / «Пробный период закончился» |
| `Expired` | «Оплата закончилась {дата}» / «Оплата закончилась» |

Суммы: `Trial` — 0 и входит в итог; `Paid`/`Ending` — цена каталога (`null` → 0) и входят; остальные — 0 и не входят.

## §43.29. `PUT /api/admin/billing-accounts/{id}/subscription` — форма подписки (изменение поведения)

Тело и ответ (карточка §43.28) — прежней формы. Строки `options[]` с опцией WhatsApp или MAX **игнорируются** во всех линейках (`line`
не задан, `Orders`, `Stays`, `Baths`): строки опций не создаются, не продлеваются, не завершаются, 400 «Для опций WhatsApp и MAX укажите дату
окончания оплаты» и 409 «Опция … закрыта для подключения» по ним больше не выдаются. Остальные проверки и ответы без изменений.
Отсутствие мессенджера в `options[]` его строку не завершает (как в цикле 40).

## §43.30. `POST /api/admin/notification-channels/{id}/confirm-payment` — «Подтвердить оплату» (изменение)

**Тело** (`ConfirmChannelPaymentCycle43Input`): `months` (1…12, без изменений), `comment` (≤ 500, без изменений), **новое необязательное**
`expectedPricePerMonth` (`number|null`; если передано — сверяется).

**Порядок:** 401/403 → 404 → 400 «Срок — от 1 до 12 месяцев» / «Комментарий — не длиннее 500 символов» (без изменений) → 409 `Replaced`
(без изменений) → 409 «У номера нет аккаунта — оплату подтвердить нельзя» → 409 «Опция MAX отсутствует в каталоге» → **новые** 409 §43.25.4
(`Closed` — прежний текст; `Inactive`, `NoPrice`, `OfferNotPublished` — новые) → **новый** 409 «Цена MAX изменилась: …» (если передан `expected…`)
→ продление по тому же правилу, что §43.26 → **200** `AdminChannelCardDto` (форма cycle40 без изменений).

Журналы: `ChannelOptionChangeLogs` (источник `AdminChannelCard`, цена, месяцы, сумма), `ChannelPaymentLogs` (как раньше + `Amount`),
**история подписки** (C40-7). В карточке номера `paymentEvents` — без изменения формы.

## §43.31. `GET /api/billing/subscription?line=Services|Orders|Stays|Baths` — «Подписка» владельца (изменение формы)

- `options[]` — **без** строк WhatsApp и MAX; `totalMonthlyPrice = plan.pricePerMonth + Σ options[].pricePerMonth` (инвариант cycle7).
- `availableOptions` — без изменений (мессенджеры исключены с цикла 40).
- `messengerAddons`, `messengerAddonsNote`, `usage.numbersPaid/numbersText` — без изменений (совместимость).
- **Новое в конце:** `messengers` (`OwnerMessengersBlockDto | null`):
  - `title` «Сообщения клиентам в мессенджеры», `note` «Общие для всех ваших компаний»;
  - `lines[]` (`OwnerMessengerLineDto`): `transport`, `label`, `status`, `statusText` (§43.28.1), `pricePerMonth`/`priceText` (только если продаётся
    или оплачено, иначе `null`), `monthlyPrice`, `paidUntil`, `isTrial`, `footnote` (у WhatsApp — «Доступ к WhatsApp в России ограничен: сообщения
    могут не доходить», у MAX — `null`), `action` (`Connect` | `Renew` | `null`), `actionText` («Подключить» | «Продлить» | `null`), `targetCompanyId`,
    `noCompanyText` («Сначала создайте компанию» — только при `action ≠ null` и `targetCompanyId = null`);
  - `monthlyTotal`, `monthlyTotalText` («Мессенджеры: 490 ₽/мес»; при 0 — «Мессенджеры: 0 ₽/мес»);
  - `conditionsUrl` «/offer-channel», `conditionsLabel` «Условия».
- `messengers = null`, если ни одной строки: строка показывается, когда мессенджер продаётся, у аккаунта есть строка опции или живой номер.
- Правило кнопки — `ARCHITECTURE_CYCLE43.md` §43.8.2 (векторы `ownerAction`). `targetCompanyId` — первая по дате создания компания аккаунта вида
  этой линейки. Ссылку строит фронт сайта: `notificationsHref(targetCompanyId, transport)` → раздел уведомлений компании с `?connect={transport}`.

## §43.32. Итоги в списке аккаунтов и профиле (смысл поля, форма та же)

`GET /api/admin/billing-accounts` → `items[].totalMonthlyPrice` и `GET /api/profile` → `plan.totalMonthlyPrice` = тариф «Записей» +
не-мессенджерные опции по правилам тарифа + мессенджеры по `MessengerLineRule` (каждый — один раз, независимо от тарифа и линейки).
Совпадает с `totalMonthlyPrice` карточки аккаунта.

## §43.33. Тарифы `/api/admin/plans` (поведение, форма та же)

- `POST`/`PUT`: элементы `options[]` с опцией WhatsApp или MAX игнорируются без ошибки. Существующие правила мессенджеров не меняются и не удаляются.
- `GET`: `items[].options[]` — без правил мессенджеров; `optionCoverage.configured/total/text` — по опциям, к которым правила тарифа применимы;
  при `total = 0` текст — «Опций, зависящих от тарифа, нет».

## §43.34. Каталог опций и настройки платформы (поведение, форма та же)

- `POST /api/admin/options`, `PUT /api/admin/options/{id}`, `DELETE /api/admin/options/{id}` — после коммита сбрасывают кеш цен и кеш
  каталога мессенджеров. Если у опции WhatsApp или MAX меняются `pricePerMonth` или `isActive`, пишется строка журнала цены
  (`source = OptionsCatalogApi`, `comment = null`). Форма ответов `GET /api/admin/options` (cycle19, `additionalProperties: false`) **не меняется**.
- `PUT /api/admin/platform-settings` — `channelPricePerMonth` принимается и игнорируется (как в цикле 40).
