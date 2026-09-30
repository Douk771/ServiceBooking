# API_CONTRACT — цикл 28 ServiceBooking: тарифы «Записи», витрина, демо-стенд

**Разделы §590–§603.** Решения — `ARCHITECTURE_CYCLE28.md` §570–§583, требования — корневой `SPEC.md` цикла 28.
**Источник истины по форме — `contracts/cycle28/openapi.yaml`.** Если форма здесь и в схеме расходится, права схема.
Здесь записаны смысл, порядок проверок, тексты и команды оператора.

```
frontend  npx @stoplight/prism mock contracts/cycle28/openapi.yaml --port 4028
          npm run types:api:cycle28   (openapi-typescript -> src/types/api-cycle28.generated.ts)
QA        schemathesis run contracts/cycle28/openapi.yaml --base-url http://localhost:5000 --checks all
CI        npx @redocly/cli lint --config ../contracts/redocly.yaml ../contracts/cycle28/openapi.yaml
```

---

## §590. Конвенции и сводка изменений

Конвенции проекта без изменений: camelCase; enum — строкой; `DateTime` — ISO-8601 UTC; `DateOnly` — `YYYY-MM-DD`;
400/402/409/429 маршрутов `/api/companies/*`, `/api/admin/*`, `/api/profile/*` — голая строка по-русски в
`text/plain`; 401/403 — пустое тело (одно исключение — §599); 404 — пустое тело, не оракул. Поля DTO **только
дописываются в конец** record.

| Маршрут | Что меняется | Раздел |
|---|---|---|
| `GET /api/companies`, `GET /api/companies/public`, `GET /api/companies/{slug}`, `GET /api/companies/my`, `GET /api/companies/member` | `CompanyDto` + `isShowcase`, `showcaseBookingOpen` | §591 |
| `POST /api/bookings` | новый 409 JSON `ShowcaseBookingClosed` | §592 |
| `GET /api/bookings/client`, `GET /api/bookings/master`, `GET /api/bookings/{id}`, ответ `POST /api/bookings` | `BookingDto` + `companyIsShowcase` | §593 |
| `GET /api/admin/users`, `/companies`, `/billing-accounts` | параметр `showcase`, поле `isShowcase` | §594 |
| `GET /api/admin/stats` | счётчики без витрины + три поля витрины | §594 |
| `POST /api/Companies/{id}/members`, `POST /api/companies`, `PUT /api/admin/companies/{id}/owner`, `POST /api/admin/companies/{companyId}/transfer`, `PUT /api/admin/billing-accounts/{accountId}/subscription` | новые 409 (смешивание, префикс `primer-`, служебный тариф) | §595 |
| `POST /api/auth/login` | витринная учётка → 401 (форма прежняя) | §596 |
| `POST /api/companies/{id}/mail` | новый 409 для витрины и демо | §596 |
| `GET /api/demo/status` 🆕 | статус демо; на бою 404 | §597 |
| `POST /api/demo/login` 🆕 | вход под ролью; на бою 404 | §598 |
| `POST /api/profile/change-password`, `change-phone`, `delete-account`, `POST /api/billing/subscription/request`, `POST /api/billing/trial`, `POST /api/admin/companies/{companyId}/transfer`, `PUT /api/admin/companies/{id}/owner` | в демо для демо-ролей — 403 с телом | §599 |
| все `/api/*` на демо | 503 во время сброса, `X-Robots-Tag` | §600a |
| `GET /api/pricing` | **форма без изменений**, меняются только данные | §601 |

---

## §591. `CompanyDto` — два новых поля

| Поле | Тип | Смысл |
|---|---|---|
| `isShowcase` | `boolean` | компания создана генератором витрины. У настоящих компаний всегда `false` |
| `showcaseBookingOpen` | `boolean` | витринная компания принимает онлайн-запись. У настоящих всегда `false`. При `isShowcase = true, showcaseBookingOpen = false` слоты и календарь работают, а подтверждение отказывает (§592) |

Оба поля заполняются для **любого** вызывающего, включая анонимного. Остальные поля прежние (полная форма в схеме).
У витринной компании: `phone = null`, `email = null`, `yandexMapsUrl = null`, `twoGisUrl = null`, `averageRating =
null`, `reviewCount = 0` (на бою), `slug` начинается с `primer-`, `bookingHorizonDays = 30`, `onlineBookingEnabled =
true` (тариф витрины разрешает — фронт показывает календарь).

**Правила для фронта:**
- бейдж «Пример» и строка `ShowcaseNotice` — при `isShowcase` в карточке каталога, в `CompanyCard` страницы компании
  и в `/embed/<slug>`. Больше нигде;
- `<meta name="robots" content="noindex, nofollow">` — на `/company/<slug>` и `/embed/<slug>` при `isShowcase`;
- строка `ShowcaseNotice` перед кнопкой подтверждения записи — при `isShowcase && showcaseBookingOpen`;
- при `isShowcase && !showcaseBookingOpen` UI записи не прячется. Отказ приходит с сервера (§592), фронт показывает
  его текст.

---

## §592. `POST /api/bookings` — запрет записи в закрытую витрину

Порядок проверок (новая выделена):
1. 404 — компании нет;
2. 409 `text/plain` — это магазин (цикл 23);
3. 403 пустое — `allowSelfBooking = false` и вызывающий не персонал;
4. **409 `application/json` `ShowcaseRefusalDto`** — `company.isShowcase && !company.showcaseBookingOpen`, и
   вызывающий не персонал этой компании и не SuperAdmin;
5. дальше без изменений: капча, обязательные поля гостя, формат телефона, 402 тарифа, услуги, горизонт, слот…

```json
{ "code": "ShowcaseBookingClosed",
  "message": "Это пример страницы салона: компания вымышленная, запись к ней не принимается." }
```

`message` — запасной текст сервера (`ShowcaseTexts.BookingClosed`). Фронт показывает текст uiText
`ShowcaseBookingClosed`, если `GET /api/legal/texts/ShowcaseBookingClosed` ответил 200, иначе `message`. Отличить этот
409 от «слот занят» (`text/plain`) можно по `Content-Type: application/json` и `code`.

Открытая витрина: ответ 201, как обычно, `BookingDto.companyIsShowcase = true`. Исходящих сообщений нет ни при
создании, ни при переносе, ни при отмене (§576 архитектуры). Запись удаляется правилом ретенции через 24 ч после
создания (настройка `Retention:ShowcaseVisitorBookingHours`).

---

## §593. `BookingDto` — одно новое поле

`companyIsShowcase: boolean` — запись в витринной компании. Заполняется на **всех** маршрутах, которые отдают
`BookingDto`: ответ `POST`, `/client`, `/master`, `/{id}`. Фронт показывает строку `ShowcaseNotice` в карточке записи
(«Мои визиты», экран успеха), если поле `true`. Остальная форма `BookingDto` не меняется. В схеме описаны только поля,
нужные этому циклу, с `additionalProperties: true`.

---

## §594. Админка

### §594.1 Фильтр `showcase`

`GET /api/admin/users`, `GET /api/admin/companies`, `GET /api/admin/billing-accounts` принимают
`showcase=all|only|exclude`:
- не передан или `all` — все строки (как раньше);
- `only` — только помеченные;
- `exclude` — только непомеченные;
- любое другое значение → 400 `text/plain` «showcase должен быть одним из: all, only, exclude.»

Фильтр складывается с остальными (`search`, `kind`, `status`, `trial`), пагинация прежняя.

### §594.2 Новые поля

- `AdminUserDto.isShowcase: boolean`.
- `AdminCompanyDto.isShowcase: boolean`, `AdminCompanyDto.showcaseBookingOpen: boolean`.
- Элемент `GET /api/admin/billing-accounts`: `isShowcase: boolean` (остальная форма — цикл 7/18, без изменений).

### §594.3 `GET /api/admin/stats`

`totalCompanies`, `totalUsers`, `totalBookings`, `completedBookings`, `totalRevenue` считаются **без** витрины
(компании и пользователи с `isShowcase`, записи с `ShowcaseKind ≠ None`). Новые поля: `showcaseCompanies`,
`showcaseUsers`, `showcaseBookings` (целые ≥ 0). `overdueSubjectRequests` прежний.

---

## §595. Запрет смешивания и префикс `primer-` — новые 409 `text/plain`

| Маршрут | Когда | Текст |
|---|---|---|
| `POST /api/Companies/{id}/members` | витринная компания и невитринный пользователь, или наоборот | «Витринную компанию и настоящие учётные записи смешивать нельзя.» |
| `PUT /api/admin/companies/{id}/owner` | пометки компании и нового ответственного различаются | «Витринную компанию и настоящие учётные записи смешивать нельзя.» |
| `POST /api/admin/companies/{companyId}/transfer` | пометки компании и целевого аккаунта (или нового ответственного) различаются | «Витринную компанию нельзя перенести в настоящий аккаунт, а настоящую — в витринный.» |
| `PUT /api/admin/billing-accounts/{accountId}/subscription` | тариф «Витрина (служебный)» назначается невитринному аккаунту | «Служебный тариф витрины нельзя назначить настоящему аккаунту.» |
| `POST /api/companies` | слаг начинается с `primer-` | «Адрес, начинающийся с «primer-», зарезервирован. Выберите другой.» |

Проверка смешивания идёт **после** проверок прав и существования (404/403 прежние) и **до** любой записи в БД.
Существующие 409 этих маршрутов не меняются. Проверка слага идёт сразу после существующей проверки «Slug already
taken».

---

## §596. Вход и рассылка

- `POST /api/auth/login`: для витринной учётки вне демо-режима — тот же `401` «Invalid credentials», что и при неверном
  пароле. В демо-режиме — тоже 401: витринные учётки входят только через `POST /api/demo/login`.
- `POST /api/companies/{id}/mail`: после проверок прав и типа компании, до проверки тарифа:
  - витринная компания → 409 `text/plain` «По витринной компании рассылка недоступна.»;
  - любая компания в демо-режиме → 409 `text/plain` «В демо-версии рассылка не отправляется.»

---

## §597. `GET /api/demo/status` 🆕

Анонимный. **Вне демо-режима — 404 с пустым телом** (маршрут как будто не существует). В демо-режиме — 200
`DemoStatusDto`. Во время сброса отвечает 200 с `resetting: true` (503 на этот маршрут не распространяется).

```json
{
  "demoMode": true,
  "resetting": false,
  "resetLocalTime": "04:00",
  "timeZoneId": "Europe/Moscow",
  "lastResetAtUtc": "2026-10-01T01:00:12Z",
  "roles": [
    { "role": "owner",  "label": "Войти как владелец салона" },
    { "role": "master", "label": "Войти как мастер" },
    { "role": "client", "label": "Войти как клиент" }
  ]
}
```

`lastResetAtUtc` — `null`, пока сброса не было. Фронт кеширует ответ на время сессии (`staleTime: Infinity`) и
перезапрашивает раз в 15 с, только пока показан экран «Демо обновляется». 404 означает «не демо»: ни плашки, ни кнопок
ролей.

---

## §598. `POST /api/demo/login` 🆕

Анонимный, лимит `demo-login` 30/мин на IP (429 `text/plain`). **Вне демо-режима — 404 пустое тело.**

Тело: `{ "role": "owner" | "master" | "client" }`.

| Код | Когда | Тело |
|---|---|---|
| 200 | вход выполнен | `AuthResponseDto` (как у `POST /api/auth/login`): `token`, `userId`, `phone`, `email`, `firstName`, `lastName`, `roles`, `phoneVerified` |
| 400 | роль не из списка или тела нет | `text/plain` «Неизвестная демо-роль.» |
| 404 | не демо-режим | пусто |
| 409 | демо-данные ещё не созданы (первый запуск до `ops demo reset`) | `text/plain` «Демо-данные ещё не созданы. Зайдите чуть позже.» |
| 429 | лимит | `text/plain` |
| 503 | идёт сброс | §600a |

`roles`: владелец — `["CompanyOwner"]`, мастер — `["Master"]`, клиент — `["Client"]` (роли Identity из
`IdentityRoleSync`). Токен несёт claim `sb_demo = 1` и текущие версии Privacy/TermsClient (у владельца ещё и
TermsOwner), поэтому гейт 451 демо-роли не останавливает. После входа фронт кладёт токен в `auth-store`, как при
обычном входе, и ведёт владельца в `/cabinet`, мастера — в `/my-bookings`, клиента — в `/my-visits`.

---

## §599. Запрещённые действия демо-ролей

В демо-режиме, если в токене есть `sb_demo = 1`, эти маршруты **до** выполнения действия отвечают 403 с телом
`text/plain` «В демо-версии это действие недоступно.» и заголовком `X-Demo-Restricted: 1`:

- `POST /api/profile/change-password`
- `POST /api/profile/change-phone`
- `POST /api/profile/delete-account`
- `POST /api/billing/subscription/request`
- `POST /api/billing/trial`
- `POST /api/admin/companies/{companyId}/transfer`
- `PUT /api/admin/companies/{id}/owner`

Это единственное место, где 403 имеет тело. Фронт показывает тело как ошибку формы, только если есть заголовок
`X-Demo-Restricted`. Во всех прочих случаях 403 прежний, пустой. Вне демо-режима и для учёток без `sb_demo` поведение
маршрутов не меняется.

---

## §600. Тексты интерфейса (заглушки [L28-*]) — ключи вне `LegalTextKey.All`

Фронт читает `GET /api/legal/texts/{key}`. На 404 показывает запасной текст ниже, дословно. Добавлять ключи в живой
`legal.json` при выкате **не нужно**: выкат не остановится (DEPLOY §10.2c).

| Ключ | Где | Запасной текст фронта |
|---|---|---|
| `ShowcaseNotice` [L28-1] | под карточкой витринной компании, в `/embed`, перед подтверждением записи, в карточке записи; скрытым текстом у бейджа | «Это пример страницы салона: компания вымышленная и показана, чтобы продемонстрировать возможности сервиса. Запись здесь не означает настоящего визита, салон не свяжется с вами. Данные такой записи удаляются автоматически в течение суток.» |
| `ShowcaseBookingClosed` [L28-1] | ошибка подтверждения записи в закрытую витрину | «Это пример страницы салона: компания вымышленная, запись к ней не принимается.» |
| `DemoBanner` [L28-3] | плашка на всех страницах демо | «Демо-версия. Данные удаляются каждую ночь. Не вводите настоящие имена и телефоны.» |

Подпись бейджа — «Пример». Это не правовой текст, а константа фронта.

## §600a. Демо: 503 во время сброса и заголовки

- Пока идёт сброс, **любой** `/api/*`, кроме `/api/health/*` и `GET /api/demo/status`, отвечает 503 `text/plain`
  «Демо обновляется, зайдите через минуту» с заголовками `Retry-After: 60` и `X-Demo-Resetting: 1`. Фронт показывает
  полноэкранное сообщение `DemoMaintenanceScreen` с этим текстом (не ошибку) и опрашивает `GET /api/demo/status` раз в
  15 с. Когда `resetting: false`, перезагружает страницу.
- Учётки демо-ролей пересоздаются с **теми же Id** (UUIDv5), поэтому выданный до сброса токен остаётся рабочим. Всё,
  что посетитель сделал под ролью, после сброса исчезает. Если посетитель зарегистрировался сам, его учётки после сброса
  нет: запросы с его токеном дают 401, и фронт делает обычный выход.
- В демо-режиме API добавляет ко всем ответам `X-Robots-Tag: noindex, nofollow`. nginx демо отдаёт `robots.txt` с
  `Disallow: /` и тот же заголовок на статике.

---

## §601. `/pricing` — данные, а не форма

`GET /api/pricing` и `PublicPricingDto` не меняются (цикл 7/18). После `ops tariffs apply` и включения публикации в
ответе: системный бесплатный тариф (текущее имя из БД), «Пробный период» (`isTrial: true`), «Студия» 790, «Салон»
1890, «Сеть» 3900. Порядок `sortOrder`: бесплатный −1, пробный 10, студия 20, салон 30, сеть 40. `options` — пустой
массив: у опции рассылок нет цены. Тариф «Витрина (служебный)» не попадает никогда (`isPublic = false`).
`includedCompanies`/`includedEmployees`: студия 1/5, салон 3/15, сеть `null`/`null` (фронт пишет «без ограничения»),
пробный 3/15.

Фронт (FE-4): сетка карточек рассчитана на 5 тарифов, новых полей не читает.

---

## §602. Команды оператора (не HTTP, но контракт для devops и QA)

Запуск на машине:

```
docker compose -f docker-compose.prod.yml --env-file .env exec -T api dotnet ServiceBooking.API.dll ops <команда>
# демо:
docker compose -f docker-compose.demo.yml --env-file .env.demo exec -T api-demo dotnet ServiceBooking.API.dll ops <команда>
```

| Команда | Изменяет | Коды выхода |
|---|---|---|
| `ops tariffs plan` | нет | 0 |
| `ops tariffs apply` | да (только создаёт) | 0; 1 ошибка |
| `ops showcase plan [create\|recreate\|delete]` | нет | 0 |
| `ops showcase create [--yes]` | да, только с `--yes` | 0; 2 витрина уже есть / нет города справочника / нет тарифа витрины |
| `ops showcase recreate [--yes]` | да, только с `--yes` | 0; 2 |
| `ops showcase delete [--yes]` | да, только с `--yes` | 0 (в том числе если удалять нечего) |
| `ops demo reset [--yes]` | да, только с `--yes` | 0; 2 не демо-режим или нет метки `instance.kind = demo` |

Общие коды: 3 — есть непримененные миграции; 4 — занят замок `ops:showcase` / `ops:tariffs` (идёт другой запуск);
64 — неизвестная команда (печатается справка). Без `--yes` изменяющая команда печатает план и выходит с 0, ничего не
меняя.

**Формат отчёта** (stdout, построчно, чтобы грепалось в DEPLOY и тестах):

```
ops showcase recreate — профиль prod, сегодня 2026-10-01 (по поясу каждой компании)
будет удалено: companies=9 users=158 billingAccounts=7 services=78 bookings=9412 bookingEvents=19870 photos=38 files=61
будет создано: companies=9 users=158 billingAccounts=7 services=78 bookings=9398 bookingEvents=19811 photos=38 files=61
режим: только показать (добавьте --yes, чтобы выполнить)
```

После выполнения: `выполнено: …` с фактическими числами и временем `за 00:41`. Для `tariffs`:
`создан: Студия (id …)` / `уже есть, не трогаю: Салон — в админке цена 1990, в сетке 1890` / `правила опций:
добавлено N`. Телефоны и имена в вывод не попадают.

---

## §603. Смоуки и проверочные кейсы (для QA и devops)

**Бой после прохода A** (DEPLOY §24):
1. `GET /api/companies/public?pageSize=100` — 200, ≥ 9 элементов с `isShowcase: true`, у каждого `slug` на `primer-`.
2. `curl -I https://ezbook.ru/company/<primer-слаг>` — есть `X-Robots-Tag: noindex, nofollow`. На
   `https://ezbook.ru/` заголовка нет.
3. `GET /api/pricing` — 200 после включения, 5 тарифов, `options: []`.
4. `POST /api/auth/login` телефоном витринного владельца и любым паролем — 401.
5. `ops showcase plan` — «будет удалено» равно фактическому числу из отчёта создания.

**Демо** (`deploy/ci/demo-smoke.sh <base-url>`):
1. `GET /` — 200.
2. `GET /robots.txt` содержит `Disallow: /`; заголовок `X-Robots-Tag` на `/` и на `/api/demo/status`.
3. `GET /api/demo/status` — 200, `demoMode: true`.
4. `POST /api/demo/login {"role":"owner"}` — 200 и токен. `GET /api/companies/my` с токеном — 200, непустой массив.
5. `POST /api/profile/change-password` с этим токеном — 403 и `X-Demo-Restricted: 1`.

**Бой, отрицательные** (функциональные тесты): `GET /api/demo/status` и `POST /api/demo/login` — 404 на боевой
конфигурации; `ops demo reset --yes` — код 2.

Кейсы `CY28-01…30` заводит QA в `TEST_CATALOG.md` («Цикл 28») по разбивке `ARCHITECTURE_CYCLE28.md` §582.2.
