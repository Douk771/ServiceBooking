# API_CONTRACT — цикл 33 ServiceBooking: «Устройства и уведомления» в профиле, одно включение на оба сайта

**Разделы §33.20–§33.29.** Решения и механизмы — `ARCHITECTURE_CYCLE33.md` §33.0–§33.17. **Источник истины по форме** —
`contracts/cycle33/openapi.yaml`: при расхождении этого текста со схемой по форме права схема, по смыслу и порядку
проверок — этот текст. Требования — корневой `SPEC.md` цикла 33. Базовая ревизия — `49f0d60`.

Корневой `API_CONTRACT.md` — документ цикла 3, по конвенции проекта (`CURRENT_STATE.md` §10.5) не перезаписывается.
Здесь описаны **фактические** маршруты `/api/push/*` (как они есть в `PushController` на `49f0d60`) и то, что цикл к
ним добавляет. Остальной API не меняется.

Конвенции без изменений: camelCase; enum — строкой; 400 от контроллера — голая строка `text/plain`; 400 автовалидации
`[ApiController]` — `application/problem+json`; 401 — пустое тело; 404 — пустое тело, не оракул; 451 — глобальный
правовой гейт, как везде; 500 — `ProblemDetails`.

---

## §33.20. Сводка изменений

| Маршрут | Что меняется | Ломает ли клиентов |
|---|---|---|
| `GET /api/push/config` | новый query `allSites` (bool, по умолчанию `false`); новые поля: `companies[].kind`, `siteUrls` | нет |
| `GET /api/push/subscriptions` | новый query `allSites`; новое поле `items[].site`; `isCurrent` учитывает сайт | нет |
| `POST /api/push/subscriptions` | в ответе новое поле `site`. Запрос, лимит на сайт, вытеснение — без изменений | нет |
| `DELETE /api/push/subscriptions/current` | без изменений. С цикла 33 его зовёт и выход на goods | нет |
| `DELETE /api/push/subscriptions/{id}` | без изменений (и раньше удалял устройство любого сайта вызывающего) | нет |
| Тело web-push сотрудникам (не HTTP-маршрут, контракт с service worker) | `url` абсолютный, если событие другого сайта; в теле записи — название салона | нет (§33.27) |
| Адрес регистрации service worker (контракт страницы с воркером) | `/sw.js?peer=<origin соседнего сайта>` | нет (§33.28) |

Новых маршрутов нет. Миграций нет. Эталон `Cycle22RouteTable.golden.txt` меняется в двух строках (новый параметр).
Push покупателям (`/api/orders/public/{token}/push-subscription[/remove]`) **не меняется**.

---

## §33.21. `GET /api/push/config`

**Авторизация:** `[Authorize]`. Лимита частоты нет.

**Query:**

| Параметр | Тип | По умолчанию | Смысл |
|---|---|---|---|
| `site` | `Services` \| `Orders` (без учёта регистра) | `Services` | сайт, с которого спрашивают. Эхо — в ответе `site` |
| `allSites` | bool | `false` | `true` — компании обоих видов. Новый фронт шлёт всегда |

**Порядок проверок:**

| # | Условие | Ответ |
|---|---|---|
| 1 | нет или недействителен токен | 401, пустое тело |
| 2 | `allSites` не разбирается как bool (`?allSites=abc`) | 400 `application/problem+json` (автовалидация) |
| 3 | глобальный правовой гейт | 451 |
| 4 | `site` не `Services`/`Orders` (числа и списки через запятую тоже отвергаются) | 400 `text/plain` «Неизвестный тип компании» |
| 5 | иначе | 200 |

**Ответ 200:**

```json
{
  "enabled": true,
  "publicKey": "BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkvMeAtA3LFgDzkrxZJjSgSnfckjBJuBkr3qBUYIHBQFLXYp5Nksh8U",
  "maxSubscriptionsPerUser": 10,
  "companies": [
    { "companyId": "0b9c…", "companyName": "Салон на Ленина", "staffPushEnabled": true,  "kind": "Services" },
    { "companyId": "7e21…", "companyName": "Шаурма на Ленина", "staffPushEnabled": false, "kind": "Orders" }
  ],
  "site": "Services",
  "siteUrls": { "services": "https://ezbook.ru", "orders": "https://goods.ezbook.ru" }
}
```

| Поле | Смысл |
|---|---|
| `enabled` | `Notifications:StaffPush:Provider = web-push`. `false` — нормальное состояние («не включено на платформе») |
| `publicKey` | VAPID public key; `null`, когда `enabled = false` |
| `maxSubscriptionsPerUser` | потолок устройств **на один сайт** (пользователь × сайт), не на весь список |
| `companies` | компании, где вызывающий `Master` или `CompanyOwner`. `allSites=false` — только вида `site` (как до цикла); `allSites=true` — обоих видов, порядок: `Services`, затем `Orders`, внутри — по имени (ordinal). Пустой массив ⇔ у пользователя нет роли сотрудника/владельца — **раздел профиля не показывается** (Q-33-5) |
| `companies[].staffPushEnabled` | настройка компании «push сотрудникам» (`CompanyNotificationSettings.StaffPushEnabled`, по умолчанию `true`) |
| `companies[].kind` | **новое.** Вид компании. Есть в обоих режимах |
| `site` | эхо запрошенного сайта |
| `siteUrls` | **новое.** `PublicSites:ServicesBaseUrl` / `OrdersBaseUrl` (без завершающего `/`). Есть в обоих режимах. Фронт берёт из него `peer` для воркера (§33.28) |

## §33.22. `GET /api/push/subscriptions`

**Авторизация:** `[Authorize]`. Лимита частоты нет.

**Query:**

| Параметр | Тип | По умолчанию | Смысл |
|---|---|---|---|
| `currentEndpoint` | string | — | endpoint подписки этого браузера на **этом** сайте. Без него у всех `isCurrent = false` |
| `site` | `Services` \| `Orders` | `Services` | сайт, с которого спрашивают |
| `allSites` | bool | `false` | `true` — устройства обоих сайтов |

**Порядок проверок:** 401 → 400 problem+json (`allSites`) → 451 → 400 «Неизвестный тип компании» (`site`) → 200.

**Ответ 200:**

```json
{
  "items": [
    { "id": "4c1e…", "deviceLabel": "Chrome на Android", "createdAtUtc": "2026-10-01T08:12:00Z",
      "lastSuccessAtUtc": "2026-10-01T09:40:05Z", "isCurrent": true,  "site": "Services" },
    { "id": "9a0f…", "deviceLabel": "Safari на iOS",     "createdAtUtc": "2026-09-28T17:03:00Z",
      "lastSuccessAtUtc": null,                   "isCurrent": false, "site": "Orders" }
  ]
}
```

| Поле | Смысл |
|---|---|
| `items` | `allSites=false` — только устройства сайта `site` (как до цикла); `allSites=true` — все устройства вызывающего. Порядок — `createdAtUtc` по убыванию |
| `isCurrent` | `true` ⇔ `endpoint == currentEndpoint` **и** `site` строки == `site` запроса |
| `site` | **новое.** Где включено устройство: `Services` — «через ezbook.ru», `Orders` — «через goods.ezbook.ru» |
| `lastSuccessAtUtc` | последняя принятая сервисом push доставка; `null` — ещё ни одной |

Ключи подписки и endpoint в ответе не отдаются никогда.

## §33.23. `POST /api/push/subscriptions`

**Авторизация:** `[Authorize]`. Политика частоты `push-subscribe` (20 в час на пользователя) — без изменений.

**Тело запроса** (без изменений):

```json
{ "endpoint": "https://fcm.googleapis.com/fcm/send/…", "keys": { "p256dh": "…", "auth": "…" },
  "deviceLabel": "Chrome на Android", "site": "Orders" }
```

`site` не передан — `Services`. Новый фронт передаёт его на обоих сайтах явно.

**Порядок проверок** (без изменений): 401 → 451 → 409 `text/plain` «Уведомления на устройство пока не включены на
платформе.» → 400 «Некорректный адрес подписки (endpoint).» / «Некорректный ключ подписки (p256dh).» / «Некорректный
ключ подписки (auth).» / «Слишком длинное название устройства.» / «Неизвестный тип компании» → 201 (создана) или 200
(тот же endpoint — обновлена или переназначена вызывающему). 429 `text/plain` «Слишком много подписок устройств.
Повторите позже.»

**Ответ 201/200:** `PushSubscriptionDto` — как в §33.22, `isCurrent: true`, **новое поле `site`** = сайт сохранённой
строки.

**Лимит:** на пару (пользователь, `site`). Одиннадцатое устройство сайта молча вытесняет самое старое устройство **того же
сайта**. Устройства другого сайта не вытесняются никогда.

## §33.24. `DELETE /api/push/subscriptions/current`

Без изменений. Тело `{ "endpoint": "…" }`. 204 всегда (идемпотентно, даже если строки нет или она чужая); 400
«Некорректный адрес подписки (endpoint).» при пустом или длиннее 500 символов; 401.

Удаляет **только** строку `PushSubscriptions` вызывающего с этим endpoint. Подписки покупателей на статус заказа
(`OrderPushSubscriptions`) с тем же endpoint не трогает.

С цикла 33 вызывается при выходе на **обоих** сайтах, до очистки токена. На goods фронт после вызова **не** отменяет
подписку браузера (`keepBrowserSubscription`).

## §33.25. `DELETE /api/push/subscriptions/{id}`

Без изменений. 204 — удалено устройство вызывающего **любого сайта**; 404 (пустое тело) — нет такой строки или она
чужая; 401. Именно этим маршрутом профиль одного сайта удаляет устройство другого сайта (US-33-05).

## §33.26. Кто получает push сотрудникам (поведение, не форма)

| Событие | Кому ставится строка в очередь (до цикла) | С цикла 33 |
|---|---|---|
| Новая запись (кроме записи, созданной самим мастером) | подписки мастера с `site = Services` | **все** подписки мастера |
| Запись перенесена клиентом | подписки мастера с `site = Services` | **все** подписки мастера |
| Новый заказ, отмена покупателем | подписки сотрудников и владельца магазина (кроме самого покупателя) с `site = Orders` | **все** их подписки |
| Лимит заказов 80 % / 100 % | подписки владельца аккаунта с `site = Orders` | **все** его подписки |

На момент отправки диспетчер `staff-push-dispatch` проверяет (без изменений): получатель всё ещё Master/CompanyOwner
компании; у компании включён push сотрудникам (кроме лимита заказов); подписка всё ещё принадлежит получателю. Иначе
строка `Skipped` с причиной `MasterNoLongerInCompany` / `StaffPushDisabledByCompany` / `PushSubscriptionReassigned`.

## §33.27. Тело web-push сотрудникам (контракт сервер → service worker)

Форма прежняя: JSON-объект `{ title, body, tag, url }` (схема `StaffPushPayload` в `openapi.yaml`, в пути не входит).
Кириллица пишется как есть (без `\uXXXX`), весь JSON ≤ 1000 символов: при превышении укорачивается `body` с «…».

**`url`:**

| Сайт подписки == сайт события | `url` |
|---|---|
| да | относительный путь, как до цикла |
| нет | `{siteUrls[сайт события]}{тот же путь}` — абсолютный адрес из `PublicSites` |

| Событие | Сайт события | Путь | `tag` |
|---|---|---|---|
| Новая запись / перенос | `Services` | `/my-bookings?booking={bookingId}` | `b-{bookingId}` |
| Новый заказ / отмена покупателем | `Orders` | `/cabinet/{shopId}/orders?order={orderId}` | `o-{orderId}` |
| Лимит заказов | `Orders` | `/cabinet/subscription` | `limit-{yyyy-MM}` |

**Тексты** (изменение только у записей):

| Событие | `title` | `body` |
|---|---|---|
| Новая запись | «Новая запись» | «{услуги} · {дд.ММ.гггг} в {ЧЧ:мм} · {клиент} · {салон}» |
| Запись перенесена | «Запись перенесена» | «{услуги} · перенесено на {дд.ММ.гггг} в {ЧЧ:мм} · {клиент} · {салон}» |
| Новый заказ | «Новый заказ № {N}» | без изменений (цикл 24) |
| Отмена покупателем | «Покупатель отменил заказ № {N}» | без изменений |
| Лимит заказов | без изменений | без изменений |

`{салон}` — название до 60 символов (длиннее — 59 + «…»). Телефонов в теле нет.

Пример тела для ezbook-подписки о новом заказе:

```json
{"title":"Новый заказ № 27","body":"к 12:30 · 3 позиции · ≈ 540 ₽ · Шаурма на Ленина","tag":"o-7e21…","url":"https://goods.ezbook.ru/cabinet/7e21…/orders?order=5d3a…"}
```

## §33.28. Адрес регистрации service worker (контракт страница → воркер)

Оба сайта регистрируют воркер одной функцией `registerPushWorker` (`frontend/src/utils/pushWorker.ts`) с областью `/`:

```
/sw.js                                   — сосед неизвестен (покупатель; или первая регистрация без конфигурации)
/sw.js?peer=https%3A%2F%2Fgoods.ezbook.ru — ezbook: peer = origin(siteUrls.orders)
/sw.js?peer=https%3A%2F%2Fezbook.ru       — goods:  peer = origin(siteUrls.services)
```

Правила воркера (оба `sw.js` одинаково):

| Вход | Результат |
|---|---|
| `peer` отсутствует, не URL, не равен точно своему origin (есть путь, запрос, `/` в конце), равен своему origin, протокол не `https:` (кроме `http:` у воркера на `http:`) | соседа нет |
| `url` тела относительный или своего origin | открыть/сфокусировать **свою** вкладку, путь + запрос + фрагмент (как до цикла) |
| `url` абсолютный с origin == сосед | `clients.openWindow(url)`; свою вкладку **не** фокусировать |
| любой другой `url`, битый JSON | страница по умолчанию своего сайта: ezbook `/my-bookings`, goods `/cabinet` |

Страница, у которой воркер уже зарегистрирован, при загрузке конфигурации перерегистрирует его с актуальным `peer`,
если адрес отличается. Если регистрации нет — ничего не делает и разрешения не спрашивает. Путь покупателя
(`/o/:token`) при регистрации сохраняет уже записанный `peer`.

## §33.29. Как проверять контракт автоматически

```
frontend  npx @stoplight/prism mock contracts/cycle33/openapi.yaml --port 4033
          npm run types:api:cycle33        (-> src/types/api-cycle33.generated.ts)
backend   npm run contracts:json           (-> contracts/cycle33/openapi.json; читает OpenApiContract.cs, CY33-20…23)
QA        schemathesis run contracts/cycle33/openapi.yaml --base-url http://localhost:5000 \
            -H "Authorization: Bearer <токен сотрудника>" --checks all
CI        npx @redocly/cli lint --config ../contracts/redocly.yaml ../contracts/cycle33/openapi.yaml
```

Тело push и адрес воркера (§33.27, §33.28) проверяются не schemathesis, а тестами: CY33-01/02/12/13 (сервер) и
`serviceWorkerRouting.test.ts` (воркеры) — `ARCHITECTURE_CYCLE33.md` §33.13.
