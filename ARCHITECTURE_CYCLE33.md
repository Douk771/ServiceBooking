# ARCHITECTURE — цикл 33 ServiceBooking: «Устройства и уведомления» в профиле, одно включение на оба сайта

**Разделы §33.0–§33.17**, контракт — `API_CONTRACT_CYCLE33.md` §33.20–§33.29, машиночитаемая схема —
`contracts/cycle33/openapi.yaml`. Нумерация с префиксом цикла, как у циклов 29–31. В коде и документах ссылаться с
именем файла: `ARCHITECTURE_CYCLE33.md §33.6`.

**На входе:**
- корневой `SPEC.md` цикла 33 (Q-33-1…Q-33-8 приняты автономно, открытые вопросы О-33-1…О-33-6 закрываются здесь);
- `CURRENT_STATE.md` на `364cc2b` (§1 стек, §4 API, §6 конвенции, §7.3 CI, §10.5 документы циклов);
- код ветки `cycle/033-unified-notifications-profile` (= `develop` `49f0d60`), сверен по файлам, названным ниже.

Ветку подготовил devops-инженер, архитектор её не трогает. Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` — документы
цикла 3, по конвенции (`CURRENT_STATE.md` §10.5) не перезаписываются: документы цикла лежат в корне с суффиксом.

| Файл | Что в нём | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE33.md` (этот) | решения, механизмы, структура, задачи, параллельность, риски | все |
| `API_CONTRACT_CYCLE33.md` (§33.20–§33.29) | контракт словами: параметры, порядок проверок, смысл полей, тело push, адрес воркера | backend, frontend, QA |
| `contracts/cycle33/openapi.yaml` | **источник истины по форме** (OpenAPI 3.0.3): prism, `openapi-typescript`, schemathesis, redocly, C#-валидатор `OpenApiContract.cs` | backend, frontend, QA, CI |

---

## §33.0. Что это за цикл для архитектуры

Цикл про **маршрутизацию уже существующих push сотрудникам**, а не про новые уведомления. Поводы, тексты заказов,
очередь `StaffPushNotification`, диспетчер `staff-push-dispatch`, проверки прав на момент отправки — всё остаётся.
Меняются четыре вещи:

1. **Выбор устройств-получателей** при постановке в очередь перестаёт фильтровать по сайту подписки (§33.4).
2. **Адрес по нажатию** в теле push становится абсолютным, когда событие «чужого» сайта уходит на подписку другого
   сайта, а оба service worker'а получают закрытый список из двух разрешённых сайтов (§33.5).
3. **Два маршрута чтения** `/api/push/config` и `/api/push/subscriptions` получают режим «оба сайта» по явному
   параметру (§33.3). Остальной API push не меняется.
4. **Фронт:** один общий блок «Устройства и уведомления» в профиле обоих сайтов вместо страницы goods и карточки ezbook
   (§33.7–§33.10).

Следствия:
- **Стек не пересматривается.** Ни NuGet, ни npm, ни миграций, ни новых переменных окружения (§33.2).
- **Бэкенд и фронт связаны в трёх точках**, все три закрыты контрактом до начала работ: параметр `allSites` и новые
  поля DTO, тело push (`url`), адрес регистрации воркера `/sw.js?peer=`. Фронт работает против prism-мока, бэкенд — по
  схеме (§33.16).

---

## §33.1. Итог решений одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| — | Стек, зависимости, миграции | **Без изменений.** Колонка `PushSubscriptions.Site` остаётся и используется для признака сайта и лимита | §33.2 |
| О-33-1 | Где снимается фильтр по сайту | В **двух местах постановки в очередь**: `StaffPushScheduler` (2 запроса: запись создана, запись перенесена) и `OrderStaffPushQueue.QueueAsync`. Диспетчер не меняется: он и так проверяет членство, флаг компании и владельца подписки на момент отправки. Push покупателям (`OrderPushSubscription`, `customer-order-push-dispatch`) не трогается | §33.4 |
| О-33-2 | Дубли в одном браузере (US-33-07) | **Вариант «честно предупредить».** Сервер не может узнать, что две подписки из одного браузера. Блок показывает подсказку, если в списке есть устройство **другого** сайта с той же подписью, что у текущего браузера («Похоже, …»). В списке видны обе записи, лишнюю можно удалить. Сервер дубли не режет: ложное совпадение подписи (два одинаковых телефона) молча лишило бы человека уведомлений | §33.9.4 |
| О-33-3 | Адрес по нажатию | Сервер пишет **относительный** `url` для подписки «своего» сайта (как сейчас) и **абсолютный** `{PublicSites:<сайт события>}{путь}` для подписки другого сайта. Домены — из `PublicSiteLinks` (конфигурация). Воркер принимает: свой origin, origin «соседа» из параметра регистрации `/sw.js?peer=<origin>`. Всё остальное — страница по умолчанию своего сайта. Соседа странице отдаёт `GET /api/push/config` (новое поле `siteUrls`) | §33.5 |
| О-33-4 | Лимит устройств | **Остаётся на пару (пользователь, сайт)**, код вытеснения не меняется. Новое устройство вытесняет только самое старое устройство **своего** сайта. В общем списке может быть до `2 × maxSubscriptionsPerUser` строк | §33.4.3 |
| О-33-5 | Выход на goods | **Дефект подтверждён:** выход на goods (`GoodsNavbar`, `GoodsProfilePage`) сейчас не удаляет серверную строку подписки. Чинится в цикле (P0, безопасность): оба места зовут `unsubscribeCurrentDeviceOnLogout({ keepBrowserSubscription: true })` до очистки токена | §33.10.3 |
| О-33-6 | Две формулировки «недоступно» | Один набор текстов `staffPushTexts.ts` с параметром «название приложения на экране „Домой“» (`Запись` / `Заказы` — это `short_name` манифестов (у ezbook меняется с EZBOOK на «Запись», см. §33.8), оно же имя в настройках iOS). Один блок шагов для iPhone. Тексты для покупателя (`goodsPushMessage(…, 'customer')`) не меняются | §33.8 |
| Q-33-5 | Кто видит раздел | Тот, у кого `GET /api/push/config?allSites=true` вернул непустой `companies` (роль Master/CompanyOwner хотя бы в одной компании любого вида). Запрос конфигурации больше не зависит от поддержки service worker, иначе на iPhone в Safari раздел не появился бы вовсе | §33.7.2 |
| US-33-01 | `/cabinet/devices` | Маршрут остаётся, элемент — `<Navigate to="/profile#devices" replace />`. `goods-routes.json` не меняется. Блок сам прокручивает к себе и ставит фокус на заголовок, если `location.hash === '#devices'` | §33.10.1 |
| US-33-03 | Текст уведомления о записи | В тело push о записи (новая и перенесённая) добавляется короткое название салона, как у заказов. Сериализация — без `\uXXXX`-экранирования кириллицы, с гарантией ≤ 1000 символов (размер колонки) | §33.6 |
| T-33-03 | Контракт | `contracts/cycle33/openapi.yaml` + `openapi.json`, шаги CI, скрипты `package.json`, `contracts-to-json.mjs`, `OpenApiContractValidatorTests`, пункт в `API_DOCUMENTATION.md` | §33.12 |

---

## §33.2. Стек, зависимости, миграции

**Без изменений** (`CURRENT_STATE.md` §1): .NET 8 / ASP.NET Core MVC / EF Core 8 / PostgreSQL 16 / Lib.Net.Http.WebPush;
React 18 + Vite 5 + Tailwind 3.4 + react-query 5 + react-router 6; Vitest 3; openapi-typescript 7.13; @redocly/cli.

| Потребность | Не берём | Берём | Почему |
|---|---|---|---|
| Сообщить воркеру домен соседнего сайта | переменную сборки `VITE_*`; IndexedDB + `postMessage`; запрос API из воркера | параметр адреса регистрации `/sw.js?peer=<origin>` | Воркер читает `self.location` синхронно, без сети и хранилища. Значение — из конфигурации сервера (`PublicSites`), а не из кода. nginx отдаёт `/sw.js` через `location = /sw.js`, аргументы не мешают (§33.5.3) |
| Узнать, что два сайта в одном браузере | fingerprint браузера | эвристика по подписи устройства + честная формулировка «Похоже» | Хранилища двух origin разделены браузером, общего идентификатора нет и не должно быть |
| Тесты воркера | Playwright, workbox-тестинг | vitest: исходник воркера через `?raw`, выполнение в песочнице с поддельным `self` | Нет новых зависимостей; e2e в проекте нет (Р-33-2) |

**Миграций нет.** `PushSubscription.Site` уже есть (цикл 24), индекс `(UserId, Site)` остаётся — он нужен лимиту.
Новых конфигурационных ключей нет: домены — существующие `PublicSites:ServicesBaseUrl` / `OrdersBaseUrl`.

---

## §33.3. Модель данных и DTO

Сущности не меняются. Меняются только три DTO в `ServiceBooking.API/DTOs/Notifications/PushDtos.cs`, **новые поля — в
конец record'ов** (NFR «Совместимость»):

| Record | Новое поле | Значение |
|---|---|---|
| `PushConfigCompanyDto` | `CompanyKind Kind` | вид компании. Нужен фронту для текстов по ролям и для показа `StaffMaxCard` только сотрудникам магазинов |
| `PushConfigDto` | `PushSiteUrlsDto? SiteUrls = null` (в ответе **всегда** заполнен) | `{ services, orders }` — `PublicSiteLinks.SiteBaseUrl(kind)`. Из него фронт берёт `peer` для воркера |
| `PushSubscriptionDto` | `CompanyKind Site = CompanyKind.Services` | на каком сайте включено устройство («через ezbook.ru» / «через goods.ezbook.ru») |

Новый record `PushSiteUrlsDto(string Services, string Orders)` — там же.

У `PushConfigDto` последний существующий параметр имеет значение по умолчанию (`Site = Services`), поэтому новый тоже
объявляется с `= null`. Контроллер заполняет его всегда; контракт объявляет поле обязательным и не-null.

**Контроллер** `PushController` (§33.21–§33.23 контракта):
- `GetConfig(site, allSites)`: при `allSites=true` в `companies` — компании **обоих** видов, где вызывающий Master
  или CompanyOwner (тот же `CompanyMembership.IsStaffRole`), порядок: сначала салоны, потом магазины, внутри — по
  `Name` (ordinal). Без `allSites` — как сейчас: только компании вида `site`. `kind` и `siteUrls` есть в обоих режимах.
- `ListSubscriptions(currentEndpoint, site, allSites)`: при `allSites=true` — все подписки пользователя, по
  `CreatedAtUtc` убыв. Без него — как сейчас (фильтр по `site`). `isCurrent = endpoint совпал && row.Site == site`:
  «это устройство» — только подписка текущего сайта в текущем браузере (US-33-05).
- `PushSubscriptionWriter.ListAsync` получает параметр `bool allSites` (или отдельный метод `ListAllSitesAsync`,
  выбор за разработчиком). `UpsertAsync`, `DeleteByIdAsync`, `DeleteByEndpointAsync`, `EvictOverflowAsync` не
  меняются: удаление по id и по endpoint уже не смотрит на сайт.

**Почему `allSites` — явный параметр, а не новое поведение по умолчанию.** Старые вкладки (открытая страница
`/cabinet/devices` старой сборки, `MyDevicesCard` старой сборки) продолжают видеть ровно своё. CY24-65 («конфиг и
устройства разделены по сайту») остаётся зелёным без правок. Новый фронт всегда шлёт `allSites=true`.

Эталон маршрутов `Cycle22RouteTable.golden.txt` содержит параметры действий: **две строки** (`GET push/config`,
`GET push/subscriptions`) меняются из-за нового query-параметра. Новых маршрутов нет, счёт 268 не меняется.

---

## §33.4. Одно включение — оба вида уведомлений (US-33-03, О-33-1, О-33-4)

### §33.4.1 Где сейчас стоит фильтр

Сверено по коду:

| Место | Сейчас | После |
|---|---|---|
| `StaffPushScheduler.OnBookingCreatedAsync` | `s.UserId == booking.MasterId && s.Site == CompanyKind.Services` | `s.UserId == booking.MasterId` |
| `StaffPushScheduler.OnBookingRescheduledAsync` | то же | то же, без фильтра по сайту |
| `OrderStaffPushQueue.QueueAsync` (новые заказы, отмена покупателем, предупреждение о лимите владельцу) | `s.Site == CompanyKind.Orders && userIds.Contains(s.UserId)` | `userIds.Contains(s.UserId)` |
| `StaffPushDispatchTask.ProcessRowAsync` | фильтра по сайту нет | **без изменений** |
| `CustomerOrderPushQueue` / `customer-order-push-dispatch` | отдельная таблица `OrderPushSubscription` | **не трогается** (Q-33-8) |

Уточнение к SPEC §7 О-33-1: push сотрудникам о заказах идёт через **тот же** `staff-push-dispatch` (строки
`StaffPushNotification` с `OrderId`), а не через `customer-order-push-dispatch` — тот только для покупателей.

### §33.4.2 Почему права не расширяются

Строка очереди по-прежнему одна на пару (событие, подписка) и несёт `UserId` получателя. Диспетчер на момент отправки
проверяет (без изменений):
1. `CompanyMembership.IsStaffAsync(row.CompanyId, row.UserId)` — человек всё ещё сотрудник этой компании;
2. `CompanyNotificationSettings.StaffPushEnabled` компании (кроме `OwnerOrderLimitWarning`, как сейчас);
3. `subscription.UserId == row.UserId` — подписку не переназначили другому (общий компьютер).

Кто и о чём получает — определяется членством, а не сайтом подписки. Сотрудник только салона получает только записи,
сотрудник только магазина — только заказы, ровно как до цикла. Уже включённые устройства (Q-33-3) начинают получать
второй вид без повторного включения: миграция данных не нужна, фильтр просто снят.

`OwnerOrderLimitWarning` тоже уходит на все устройства владельца: это уведомление «по праву» владельца аккаунта магазина.

### §33.4.3 Лимит устройств (О-33-4)

Лимит `MaxSubscriptionsPerUser` (10) и вытеснение самого старого — **на пару (пользователь, сайт), без изменений**
(`PushSubscriptionWriter.EvictOverflowAsync`). Следствия:
- включение на goods никогда не вытеснит устройство ezbook и наоборот;
- в общем списке до 20 строк. Для реального человека это недостижимо, а правило «ничего чужого молча не удаляется»
  важнее компактности;
- `maxSubscriptionsPerUser` в конфиге — по-прежнему «на сайт», в контракте так и описано.

---

## §33.5. Адрес по нажатию и закрытый список сайтов (US-33-04, О-33-3)

### §33.5.1 Сервер: `url` в теле push

Новый чистый помощник `ServiceBooking.API/Services/Notifications/StaffPushLinks.cs`:

```csharp
// Relative when the device subscribed on the event's own site, absolute {PublicSites:<eventSite>}{path} otherwise.
public static string For(CompanyKind eventSite, CompanyKind subscriptionSite, string relativePath, PublicSiteLinks links)
```

| Событие | Сайт события | Относительный путь (как сейчас) |
|---|---|---|
| Новая запись, запись перенесена | `Services` | `/my-bookings?booking={bookingId}` |
| Новый заказ, отмена покупателем | `Orders` | `/cabinet/{shopId}/orders?order={orderId}` |
| Предупреждение о лимите заказов | `Orders` | `/cabinet/subscription` |

Тело для подписки своего сайта **байт в байт по смыслу прежнее** (относительный путь). Для подписки другого сайта —
`links.SiteBaseUrl(eventSite) + relativePath`, например `https://goods.ezbook.ru/cabinet/…/orders?order=…`.
`StaffPushScheduler` и `OrderStaffPushQueue` получают `PublicSiteLinks` через DI (он уже singleton,
`ApplicationServicesExtensions.cs:53`) и собирают два варианта тела один раз на событие, выбирая вариант по
`subscription.Site` для каждой строки.

Почему не всегда абсолютный: воркер старой сборки (до обновления) отбрасывает чужой origin и открывает страницу по
умолчанию, а свой абсолютный origin принимает. С относительным путём для «своих» подписок поведение без изменений даже
для старого воркера, а для «чужих» — безопасная деградация (страница по умолчанию своего сайта), а не 404.

### §33.5.2 Воркеры: закрытый список из двух origin

Оба файла (`frontend/public/sw.js`, `frontend/goods/public/sw.js`) получают **одинаковый** блок маршрутизации
(отличаются только страницей по умолчанию и заголовком по умолчанию, как сейчас):

1. `PEER_ORIGIN` вычисляется один раз при старте воркера из `new URL(self.location.href).searchParams.get('peer')`.
   Принимается только если: строка парсится как URL; **строка равна своему `origin`** (без пути, запроса, фрагмента,
   завершающего `/`); origin не равен своему; протокол `https:` (или `http:`, только когда сам воркер на `http:` —
   локальная разработка). Иначе `PEER_ORIGIN = null`.
2. `resolveTarget(url, fallbackPath)` → `{ kind: 'same', path }` | `{ kind: 'peer', href }`:
   - `new URL(url, self.location.origin)`; ошибка разбора → `same` + `fallbackPath`;
   - origin свой → `same` + `pathname + search + hash` (как нынешний `safeUrl`);
   - origin равен `PEER_ORIGIN` → `peer` + `href`;
   - иначе → `same` + `fallbackPath`.
3. `push`: в `notification.data.url` кладётся результат `resolveTarget` (путь или абсолютный href соседа).
4. `notificationclick`: `resolveTarget` вызывается **повторно** (данные могли прийти от прошлой версии воркера):
   - `same` → нынешняя логика: сфокусировать открытую вкладку своего сайта и `navigate(path)`, иначе `openWindow(path)`;
   - `peer` → **только** `clients.openWindow(href)`. Вкладки соседа воркер не видит (`clients.matchAll` отдаёт только
     свой origin), а фокусировать вкладку своего сайта запрещено US-33-04.

Запреты CI (`addEventListener('fetch'`, `caches`, `CacheStorage`) не затрагиваются: воркер не делает сетевых запросов
и не использует хранилища.

На iPhone приложение с экрана «Домой» откроет адрес соседа в Safari — допустимо по SPEC, проверяется в M33.

### §33.5.3 Страница: откуда воркер узнаёт соседа

Новый модуль `frontend/src/utils/pushWorker.ts` (единственное место, где регистрируется воркер):

| Функция | Что делает |
|---|---|
| `pushWorkerScriptUrl(peerOrigin: string \| null): string` | `'/sw.js'` или `` `/sw.js?peer=${encodeURIComponent(origin)}` ``; `origin` нормализуется через `new URL(x).origin`. Чистая, покрыта тестом |
| `registerPushWorker(peerOrigin?: string \| null)` | `peerOrigin` передан → регистрирует с ним. Не передан (путь покупателя, где соседа не знаем) → **сохраняет** `scriptURL` уже активного воркера, если он есть, иначе `'/sw.js'`. Так покупатель на `/o/:token` не стирает соседа, записанного сотрудником в том же браузере |
| `refreshPushWorkerPeer(peerOrigin: string)` | если регистрация **уже есть** и её `scriptURL` отличается от нужного — перерегистрирует. Если регистрации нет — ничего не делает (не устанавливает воркер молча и не спрашивает разрешение) |

Сосед: на ezbook — `config.siteUrls.orders`, на goods — `config.siteUrls.services`.

Смена `scriptURL` у той же области `/` — это обновление той же регистрации: подписка `PushManager` привязана к
регистрации и сохраняется. Это поведение спецификации Service Workers (алгоритм Register → Update); на реальных
устройствах его подтверждает M33-07.

Вызовы:
- `useWebPush.enableOnThisDevice` — `registerPushWorker(peer)` вместо `navigator.serviceWorker.register('/sw.js')`;
- `useWebPush` после загрузки конфигурации — `refreshPushWorkerPeer(peer)`: так устройства, включённые до цикла,
  получают соседа при первом открытии профиля (или главной кабинета goods, US-33-08);
- `goods/src/hooks/useOrderPush.ts` — `registerPushWorker()` без соседа вместо `register('/sw.js')`. Поведение
  уведомлений покупателю не меняется (Q-33-8), меняется только то, что регистрация не затирает `peer`.

Известная деградация: устройство, включённое до цикла, пока его владелец не открыл профиль или кабинет на этом
устройстве, по нажатию на уведомление «чужого» сайта откроет страницу по умолчанию **своего** сайта. Уведомление при
этом приходит. На бою push выключен (C24-2), реальных пользователей это не затрагивает.

Локальная разработка: в `appsettings.json` `PublicSites` указывают на боевые домены. Чтобы проверить переход между
сайтами локально, задать `PublicSites__ServicesBaseUrl=http://localhost:5173` и
`PublicSites__OrdersBaseUrl=http://localhost:5174` (в Development `http` разрешён, `ValidatePublicSites`).

---

## §33.6. Текст уведомления: что и в какой компании (US-33-03)

| Уведомление | Сейчас | После |
|---|---|---|
| Новый заказ | «Новый заказ № 27» / «к 12:30 · 3 позиции · ≈ 540 ₽ · Шаурма на Ленина» | без изменений |
| Отмена покупателем | «Покупатель отменил заказ № 27» / «к 12:30 · Шаурма на Ленина» | без изменений |
| Лимит заказов | «Лимит заказов исчерпан» / «5 из 5 в октябре…» | без изменений: лимит — на аккаунт, а не на магазин; «заказов» однозначно говорит о виде |
| Новая запись | «Новая запись» / «Стрижка · 02.10.2026 в 12:30 · Анна» | «Новая запись» / «Стрижка · 02.10.2026 в 12:30 · Анна · **Салон на Ленина**» |
| Запись перенесена | «Запись перенесена» / «Стрижка · перенесено на … · Анна» | то же + « · **{салон}**» |

Название салона — `OrderNotificationTexts.ShortName(company.Name)` (≤ 60 символов, как у магазина). Компания уже
загружается в `StaffPushScheduler` ради часового пояса.

**Размер тела.** Колонка `StaffPushNotifications.Payload` — 1000 символов. Сейчас тело записи сериализуется с
экранированием кириллицы `\uXXXX` (×6 к длине). С названием салона и абсолютным адресом длинный список услуг может не
влезть. Поэтому:
- новый помощник `Services/Notifications/StaffPushPayloadJson.cs` — `Serialize(title, body, tag, url)`: JSON
  `{title, body, tag, url}` с `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (как `OrderStaffPushQueue.PayloadOptions`)
  и гарантией длины ≤ 1000: если не влезает, `body` укорачивается с «…»;
- им пользуются `StaffPushScheduler` (оба метода) и `OrderStaffPushQueue`. `OrderStaffPushQueue.PayloadOptions`
  остаётся (им пользуется `CustomerOrderPushQueue`).

Телефона в теле по-прежнему нет (юнит-тест `BuildPayload_NeverContainsAPhoneLikePattern` остаётся).

---

## §33.7. Общий блок «Устройства и уведомления» (T-33-01, Q-33-6)

### §33.7.1 Где лежит и как подключается

`frontend/src/components/push/DevicesAndNotificationsSection.tsx` — один компонент для обоих сайтов:
- лежит в `src/components/**` — эту папку сканирует сборка стилей goods (`GOODS_SCANNED_EZBOOK_DIRS`), guard-тест
  `sharedSources.guard.test.ts` её уже знает, правка `goods-shared-sources.js` **не нужна**;
- импорты внутри `src/**` только относительные (C26-2).

```ts
interface DevicesAndNotificationsSectionProps {
  site: PushSite                      // 'Services' на ezbook, 'Orders' на goods
  appName: 'Запись' | 'Заказы'        // short_name манифеста сайта — для текстов iPhone
  keepBrowserSubscription?: boolean   // goods: true (как сейчас на /cabinet/devices)
  ordersExtra?: ReactNode             // goods: <StaffMaxCard />; рисуется только если среди компаний есть магазин
  className?: string                  // внешние отступы под соседние карточки профиля
}
```

Хост-страница просто вставляет компонент. Решение «показывать ли» принимает сам блок.

### §33.7.2 Состояния

| Состояние | Что видно |
|---|---|
| Конфигурация грузится | **ничего** (роль ещё неизвестна; иначе у клиента мелькнул бы чужой раздел) |
| `companies` пуст (Q-33-5) | **ничего**, профиль как сейчас |
| Иначе | `<section aria-labelledby="devices">` в `Card`: заголовок `h2#devices` «Устройства и уведомления» (`tabIndex={-1}`), пояснение, область переключателя, подсказки, список устройств; ниже — `ordersExtra`, если есть магазин |
| Область переключателя: список устройств грузится | скелетон (`h-14`), как `LoadingList` сейчас |
| Область переключателя: `reason ≠ null` | плашка с `data-testid="push-unavailable"` и `data-reason={reason}`, текст причины, для `ios-safari-not-installed` — шаги (§33.8.2) |
| Область переключателя: доступно | `role="switch"`, `aria-checked`, `aria-label` = текст подписи, цель касания ≥ 44 px |
| Ошибка действия | `<p role="alert">` с текстом ошибки дословно |
| Список пуст | `data-testid="no-devices"`: «Пока ни одного устройства. Включите уведомления выше — устройство появится здесь.» |
| Строка устройства | подпись; « · это устройство» при `isCurrent`; вторая строка: «через ezbook.ru» / «через goods.ezbook.ru» · «Последняя доставка: …» или «Подписано: …»; кнопка удаления `aria-label="Отключить уведомления на устройстве «{подпись}»"`, `min-h-[44px] min-w-[44px]` |

Список устройств показывается и тогда, когда `reason ≠ null` (например, iPhone в Safari): удалить забытое устройство
можно с любого браузера. Он запрашивается, если `config.enabled && companies.length > 0`, независимо от поддержки
service worker в этом браузере.

Ссылки «К списку магазинов» в блоке нет (US-33-01). Крупного заголовка страницы нет — только `h2` блока.

### §33.7.3 Переход по `#devices`

Блок после появления заголовка: если `useLocation().hash === '#devices'` — `heading.scrollIntoView({ block: 'start' })`
и `heading.focus({ preventScroll: true })`. Один раз на монтирование. Так работают и перенаправление
`/cabinet/devices`, и ссылки из подсказок (US-33-06), и ссылка с `/my-bookings`.

---

## §33.8. Тексты и причины (О-33-6, US-33-03)

### §33.8.1 Модуль `frontend/src/utils/staffPushTexts.ts` (новый, чистый, покрыт тестом)

Роли считаются по `companies[].kind`: `hasServices`, `hasOrders`. `noun` = «записях» | «заказах» | «записях и заказах».

| Функция / константа | Текст |
|---|---|
| `staffPushIntro(kinds, site)` | «Push о новых {noun} приходит на ваши устройства, где вы включили уведомления. Выключить push сотрудникам может владелец — в настройках уведомлений компании.» На ezbook (`site === 'Services'`) дописывается: «Это уведомления вам как сотруднику, а не сообщения клиентам.» (US-33-02) |
| `staffPushSwitchLabel(kinds)` | «Уведомлять меня о новых {noun} на этом устройстве» (он же `aria-label`) |
| `ONE_DEVICE_ENOUGH_TEXT` (только при обоих видах) | «Достаточно включить на одном устройстве один раз — придут уведомления и о записях, и о заказах.» |
| `deviceSiteLabel(site)` | `Services` → «через ezbook.ru», `Orders` → «через goods.ezbook.ru» |
| `staffPushUnavailableMessage(reason, appName)` | таблица ниже |
| `likelySameBrowserOnOtherSite(devices, site, label)` | §33.9.4 |

| Причина | Текст (один на оба сайта; `{app}` = `Запись` / `Заказы`) |
|---|---|
| `ios-safari-not-installed` | «На айфоне уведомления приходят только приложению «{app}», добавленному на экран «Домой». Это займёт минуту:» |
| `ios-version-too-old` | «На этой версии iOS уведомления от сайтов не работают даже с экрана «Домой». Нужна iOS 16.4 или новее — обновить: Настройки → Основные → Обновление ПО.» |
| `unsupported-browser` | «Ваш браузер не умеет присылать уведомления. Включите их на другом устройстве или в другом браузере.» |
| `insecure-context` | «Уведомления работают только по защищённому соединению (HTTPS). На этом адресе они недоступны.» |
| `permission-denied` | «Вы запретили уведомления в браузере. Чтобы вернуть: значок замка в адресной строке → Уведомления → Разрешить.» |
| `ios-permission-denied` | «Вы запретили уведомления для «{app}». Чтобы вернуть: Настройки айфона → Уведомления → «{app}» → Допуск уведомлений.» |
| `platform-disabled` | «Уведомления на устройство пока не включены на платформе.» |
| `company-disabled` | «Уведомления сотрудникам выключены во всех ваших компаниях. Включает их владелец в настройках уведомлений салона или магазина.» |

Причина по-прежнему выбирается `getPushUnavailableReason` (порядок причин не меняется). Вход `companyStaffPushEnabled`
= `companies.some(c => c.staffPushEnabled)` по компаниям **обоих** видов (при `allSites=true`). Пример из SPEC:
сотрудник только магазина на ezbook.ru видит рабочий переключатель.

`PUSH_UNAVAILABLE_MESSAGES` удаляется из `pushAvailability.ts` (переезжает в `staffPushTexts.ts`). Ветка `'staff'` в
`goods/src/utils/goodsPush.ts` удаляется как мёртвая; `goodsPushMessage(reason, 'customer')` и `OrderPushCard` не
меняются.

### §33.8.2 Шаги для iPhone

`frontend/src/components/push/PushUnavailableNotice.tsx` переделывается: пропсы `{ reason, appName, showOneSiteHint }`,
текст из `staffPushUnavailableMessage`, шаги `IosInstallSteps` (с существующим `ShareGlyph`):
1. «Нажмите кнопку «Поделиться» [значок] внизу или вверху экрана. Если её не видно — сначала нажмите «⋯».»
2. «Выберите «На экран «Домой»» (если пункта нет в списке — пролистайте вниз), затем «Добавить».»
3. «Откройте «{app}» с новой иконки на экране «Домой» и **войдите заново** — приложение не видит вход, выполненный в
   браузере.»
4. «Откройте «Профиль» → «Устройства и уведомления» и включите переключатель — айфон спросит разрешение.»

При `showOneSiteHint` (оба вида) после шагов: «Достаточно сделать это для одного из сайтов — ezbook.ru или
goods.ezbook.ru.» `goods/src/components/push/GoodsIosSteps.tsx` удаляется.

---

## §33.9. Хук `useWebPush` и клиент API

### §33.9.1 `frontend/src/api/push.ts`

```ts
getConfig: (site: PushSite, opts?: { allSites?: boolean }) => …   // ?site=…&allSites=true
listSubscriptions: (currentEndpoint: string | undefined, site: PushSite, opts?: { allSites?: boolean }) => …
```
`allSites` уходит в запрос только при `true`. Остальные методы без изменений.

### §33.9.2 `frontend/src/hooks/useWebPush.ts`

- Опции: `{ site: PushSite; keepBrowserSubscription?: boolean }` — `site` становится обязательным (после цикла у хука
  один потребитель — общий блок, плюс P2-подсказка кабинета). ezbook явно шлёт `site: 'Services'` и в `POST`.
- Конфигурация: `pushApi.getConfig(site, { allSites: true })`, ключ `['push-config', site, 'all']`, **без**
  `enabled: SERVICE_WORKER_SUPPORTED`.
- Устройства: `pushApi.listSubscriptions(currentEndpoint, site, { allSites: true })`, ключ
  `['push-devices', site, 'all', currentEndpoint]`, `enabled: config.enabled && companies.length > 0`.
- Новые поля результата: `companies`, `hasServices`, `hasOrders`, `isStaff: boolean | undefined` (`undefined`, пока
  конфигурация грузится), `siteUrls`.
- `enableOnThisDevice`: `registerPushWorker(peer)` (§33.5.3). Остальное как сейчас.
- После загрузки конфигурации: `refreshPushWorkerPeer(peer)` (молча, ошибки глотаются).
- `disableDevice(id)` для устройства другого сайта — только `DELETE /api/push/subscriptions/{id}`; подписку браузера не
  трогает (у чужого сайта `isCurrent` всегда `false`). Это уже так работает.
- `unsubscribeCurrentDeviceOnLogout(opts?: { keepBrowserSubscription?: boolean })`: при `true` удаляет серверную
  строку по endpoint и **не** зовёт `subscription.unsubscribe()` (подписку браузера на goods делит покупатель).

### §33.9.3 Типы

`types/index.ts`: `PushConfig`, `PushSubscriptionDevice` переводятся с `api-cycle9.generated.ts` на
`api-cycle33.generated.ts` (`PushConfigDtoCycle33`, `PushSubscriptionDtoCycle33`), новые алиасы `PushConfigCompany`,
`PushSiteUrls`. `PushSite` = `CompanyKind` из генерата.

### §33.9.4 Подсказка о дублях (US-33-07, О-33-2)

`likelySameBrowserOnOtherSite(devices, site, currentLabel)` → `PushSubscriptionDevice | null`: первое устройство с
`device.site !== site` и `device.deviceLabel === currentLabel` (`describeDevice()`).

| Условие | Подсказка под переключателем (обычный текст, не `alert`) |
|---|---|
| совпадение есть, на этом сайте **не** включено | «Похоже, на этом устройстве уведомления уже включены через {goods.ezbook.ru\|ezbook.ru}. Включать ещё раз не нужно — туда приходят все ваши уведомления.» |
| совпадение есть, на этом сайте включено | «Похоже, на этом устройстве уведомления включены и через {…}. Одно событие может прийти дважды — лишнюю запись удалите в списке ниже.» |

«Похоже» — честная оговорка: одинаковая подпись бывает у двух разных телефонов. Переключатель при этом не
блокируется.

---

## §33.10. Изменения по страницам

### §33.10.1 goods

| Файл | Изменение | История |
|---|---|---|
| `GoodsApp.tsx` | `<Route path="/cabinet/devices" element={<Navigate to="/profile#devices" replace />} />` вместо `DevicesPage` (внутри `RequireAuth`, как сейчас) | US-33-01 |
| `pages/GoodsProfilePage.tsx` | после карточки «Телефон»: `<DevicesAndNotificationsSection site="Orders" appName="Заказы" keepBrowserSubscription ordersExtra={<StaffMaxCard />} className="mb-4" />`; «Выйти» — §33.10.3 | US-33-01, О-33-5 |
| `components/GoodsNavbar.tsx` | выход — §33.10.3 | О-33-5 |
| `pages/cabinet/CabinetHomePage.tsx` | кнопка «Устройства и уведомления» удаляется. `nav` рисуется только если есть кнопка «Подписка» (иначе пустой `nav` у сотрудника) | US-33-01 |
| `pages/cabinet/CabinetHomePage.tsx` (P2) | строка «Уведомления о новых заказах на этом устройстве не включены — включить в профиле» со ссылкой `/profile#devices`, если `isStaff && !isSubscribedOnThisDevice` после загрузки списка | US-33-08 |
| `pages/cabinet/ShopNotificationsPage.tsx` | подсказки: «Сотрудник включает уведомления на своём устройстве в профиле, раздел «Устройства и уведомления».» и «…тем сотрудникам, кто подключил его в профиле, раздел «Устройства и уведомления».» Ссылки — `Link` на `/profile#devices` | US-33-06 |
| `hooks/useOrderPush.ts` | `registerPushWorker()` вместо `register('/sw.js')` | §33.5.3 |
| `utils/goodsPush.ts` | удалить ветку `'staff'`, тесты поправить | О-33-6 |
| удаляются | `pages/cabinet/DevicesPage.tsx`, `DevicesPage.test.tsx`, `components/push/GoodsIosSteps.tsx` | — |
| не меняются | `contracts/cycle23/goods-routes.json` (маршрут остаётся и занимает адрес), `goodsRoutes.test.ts` | NFR |

### §33.10.2 ezbook

| Файл | Изменение | История |
|---|---|---|
| `pages/ProfilePage.tsx` | сразу после `<NotificationPreferencesCard />`: `<DevicesAndNotificationsSection site="Services" appName="Запись" className="mb-[18px]" />`. Карточка «Уведомления о визитах» не меняется | US-33-02 |
| `pages/MyBookingsPage.tsx` | вместо `<MyDevicesCard />` — одна строка: «Уведомления на устройства — в профиле» (`Link` на `/profile#devices`, `text-sm`, цель ≥ 44 px) | US-33-02, Q-33-4 |
| `components/push/StaffPushSettingsCard.tsx` | в пояснение дописать: «Мастер включает их у себя в профиле, раздел «Устройства и уведомления».» (текст, без ссылки: карточку видит владелец, а не мастер) | US-33-06 |
| `components/push/PushUnavailableNotice.tsx` | §33.8.2 | О-33-6 |
| удаляется | `components/push/MyDevicesCard.tsx` (других потребителей нет, сверено поиском) | US-33-02 |

### §33.10.3 Выход на goods (О-33-5)

Сейчас `GoodsNavbar` и `GoodsProfilePage` зовут `logout()` сразу, серверная строка подписки goods остаётся. После цикла
эта строка получала бы и записи салона с именами клиентов. Исправление по образцу ezbook `Navbar.tsx`:

```
await unsubscribeCurrentDeviceOnLogout({ keepBrowserSubscription: true })  // до очистки токена, ошибки глотаются
logout(); qc.clear(); navigate('/')
```

`DELETE /api/push/subscriptions/current` удаляет только строку `PushSubscriptions` этого пользователя с этим endpoint.
Подписка покупателя на статус заказа (`OrderPushSubscription`, тот же endpoint) не затрагивается: другая таблица.
Перехватчик 401 не меняется (он осознанно не зовёт удаление, §105.5 цикла 9).

---

## §33.11. Безопасность и приватность

| Требование SPEC §6 | Как выполняется |
|---|---|
| Права не расширяются | §33.4.2: получатель — тот же пользователь, проверка членства и флага компании на момент отправки без изменений |
| Выход на сайте X гасит оба вида через подписку X | ezbook — уже (`Navbar`); goods — §33.10.3. Переназначение endpoint новому владельцу (`UpsertAsync`) и проверка `subscription.UserId == row.UserId` в диспетчере работают для любых строк |
| Нажатие открывает только два домена | §33.5.2: закрытый список {свой origin, `PEER_ORIGIN`}. `PEER_ORIGIN` принимается только как точный origin; иначе страница по умолчанию своего сайта |
| Нет телефонов в теле | без изменений; в тело записи добавлено только название салона |
| Ключи подписок не отдаются | новые поля DTO — `kind`, `site`, `siteUrls`; ключей и endpoint в ответах нет, как и раньше |
| Чужая подписка — 404 | без изменений (`DeleteByIdAsync` фильтрует по `UserId`) |

Новых секретов нет. `siteUrls` — публичные адреса сайтов, они и так отдаются в `kinds-summary`.

---

## §33.12. Контракты и CI (T-33-03)

| Что | Изменение |
|---|---|
| `contracts/cycle33/openapi.yaml` | новый (дельта: `/api/push/config`, `/api/push/subscriptions` GET/POST, `/api/push/subscriptions/current`, `/api/push/subscriptions/{id}`; компонент `StaffPushPayload`) |
| `contracts/cycle33/openapi.json` | генерируется `npm run contracts:json`, коммитится |
| `frontend/scripts/contracts-to-json.mjs` | `cycles = ['cycle31', 'cycle33']` |
| `frontend/package.json` | `"types:api:cycle33": "openapi-typescript ../contracts/cycle33/openapi.yaml -o src/types/api-cycle33.generated.ts"` |
| `.github/workflows/ci.yml` | redocly lint: `+ ../contracts/cycle33/openapi.yaml`; генераты: `npm run types:api:cycle33` и файл в `git diff --exit-code`; шаг JSON переименовать в «Contract JSON must match yaml (31, 33)» и добавить `../contracts/cycle33/openapi.json` в diff |
| `contracts/redocly.yaml` | строка комментария про cycle33 |
| `ServiceBooking.Tests/Tests/OpenApiContractValidatorTests.cs` | `[InlineData("cycle33")]` |
| `API_DOCUMENTATION.md` | пункт «Цикл 33 (unreleased)» у раздела push: `allSites`, новые поля, `url` для другого сайта, лимит на сайт |

Схема пишется только ключевыми словами из набора `OpenApiContract.cs` (без `oneOf`, `pattern`, `const`,
`uniqueItems`, без соседей у `$ref`).

---

## §33.13. Тесты и QA (T-33-02, T-33-04)

### §33.13.1 Функциональные `ServiceBooking.Tests` (новый файл `Cycle33UnifiedPushTests.cs`, `[TestCase("CY33-xx")]`)

| ID | Что проверяет |
|---|---|
| CY33-01 | Запись в салоне A: мастер с подписками ezbook и goods получает 2 строки; у ezbook `url` относительный, у goods — `{ServicesBaseUrl}/my-bookings?booking=…` |
| CY33-02 | Заказ в магазине B: сотрудник с подписками обоих сайтов — 2 строки; у ezbook `url` = `{OrdersBaseUrl}/cabinet/{shopId}/orders?order=…` |
| CY33-03 | Сотрудник только магазина с подпиской ezbook получает заказы; мастер только салона с подпиской goods получает записи; ни у кого нет строк чужого вида |
| CY33-04 | Магазин B выключил push сотрудникам: строк о заказах нет ни на одном устройстве; записи салона A идут. Выключение после постановки в очередь → `Skipped/StaffPushDisabledByCompany` при проходе диспетчера |
| CY33-05 | Сотрудника убрали из магазина после постановки → строка на ezbook-подписке `Skipped/MasterNoLongerInCompany` |
| CY33-06 | Endpoint ezbook переназначен другому пользователю (общий компьютер) → строка о заказе `Skipped/PushSubscriptionReassigned` |
| CY33-07 | `GET /api/push/subscriptions?allSites=true` — оба сайта, поле `site`, порядок по `createdAtUtc` убыв.; `isCurrent` только при совпадении endpoint **и** сайта; без `allSites` — прежнее поведение |
| CY33-08 | `GET /api/push/config?allSites=true` — компании обоих видов с `kind`, порядок салоны → магазины; `siteUrls` из конфигурации; без `allSites` — только вид `site`; `allSites=abc` → 400 |
| CY33-09 | Удаление подписки goods из-под ezbook по id → 204, строки нет, новые события на неё не ставятся; чужой id → 404; `OrderPushSubscription` с тем же endpoint цела |
| CY33-10 | Лимит на сайт: 10 ezbook + 11-я goods — ничего не вытеснено; 11-я ezbook вытесняет самую старую ezbook, goods не трогается |
| CY33-11 | `DELETE /api/push/subscriptions/current` для goods-endpoint удаляет только строку сотрудника, подписка покупателя остаётся |
| CY33-12 | Тело записи: название салона, нет телефона, нет `\u`, длина ≤ 1000 при названии салона 200 символов и 20 услугах |
| CY33-13 | Предупреждение о лимите заказов уходит и на ezbook-устройство владельца, `url` абсолютный `…/cabinet/subscription` |
| CY33-20…23 | Контракт: ответы `GET config` (оба режима), `GET subscriptions` (оба режима), `POST subscriptions` сверяются с `OpenApiContract.Load("cycle33")` |

**Меняются ожидания прежних тестов (осознанный разворот поведения):**
- CY24-66 `NewOrder_QueuesStaffPushOnlyToGoodsDevices…` — теперь 3 строки (устройство ezbook владельца тоже),
  `OnlyContain(… Site == Orders …)` снимается, у строки ezbook `url` абсолютный; имя теста привести к смыслу;
- `ServiceBooking.UnitTests/StaffPushSchedulerTests.cs` — сигнатура `BuildPayload` получает название салона и `url`;
  `BuildPayload_UrlIsRelative_NoOriginOrScheme` остаётся для своего сайта, добавляется вариант для другого сайта;
- `Cycle22RouteTable.golden.txt` — 2 строки (параметр `allSites`).

**Юнит-тесты:** `StaffPushLinksTests` (4 сочетания сайтов), `StaffPushPayloadJsonTests` (кириллица без экранирования,
усечение `body` до ≤ 1000, валидный JSON).

### §33.13.2 Vitest

| Файл | Что |
|---|---|
| `src/utils/staffPushTexts.test.ts` | все причины × оба `appName`; `noun` по ролям; `likelySameBrowserOnOtherSite` |
| `src/utils/pushWorker.test.ts` | `pushWorkerScriptUrl` (нормализация origin, кодирование); `registerPushWorker()` без соседа сохраняет `scriptURL` активного воркера; `refreshPushWorkerPeer` не регистрирует, если регистрации нет |
| `src/utils/serviceWorkerRouting.test.ts` | оба воркера (`import … from '../../public/sw.js?raw'`, `'../../goods/public/sw.js?raw'`), выполнение в песочнице с поддельным `self` (`location`, `registration.showNotification`, `clients.matchAll/openWindow`, `addEventListener`). Таблица: относительный путь; свой абсолютный; сосед при корректном `peer`; сосед без `peer`; чужой домен; `javascript:`; `peer` с путём; `peer` на `http:` при `https:`-воркере; битый JSON. Для соседа — `openWindow(href)` и **ни одного** `focus()`/`navigate()` |
| `src/components/push/DevicesAndNotificationsSection.test.tsx` | нет раздела при пустом `companies` и во время загрузки; все состояния §33.7.2; подпись сайта у устройства; `ordersExtra` только при магазине; фраза «Достаточно включить…» только при обоих видах; подсказка о дублях; фокус на заголовке при `#devices` |
| `goods/src/pages/GoodsProfilePage.test.tsx` | раздел есть у сотрудника магазина, нет у покупателя; «Выйти» ждёт `unsubscribeCurrentDeviceOnLogout({ keepBrowserSubscription: true })` до `logout()` и выходит даже при его ошибке |
| `goods/src/components/GoodsNavbar.test.tsx` | то же для навбара |
| `goods/src/GoodsApp.redirect.test.tsx` (или в существующем тесте маршрутов) | `/cabinet/devices` → `/profile#devices` с заменой в истории |
| `goods/src/pages/cabinet/CabinetHomePage.test.tsx` | нет кнопки «Устройства и уведомления»; «Подписка» есть у владельца; P2-строка по условию |
| `src/pages/ProfilePage.test.tsx`, `src/pages/MyBookingsPage.test.tsx` | раздел по Q-33-5; на `/my-bookings` ссылка есть, `MyDevicesCard` нет |
| `src/components/push/PushUnavailableNotice.test.tsx` | переписать под новые пропсы |

CI-проверка воркеров на `fetch`/`caches`/`CacheStorage` должна оставаться зелёной.

### §33.13.3 Ручные `M33-` (T-33-04, гейт выката, не мержа)

Стенд с `WEBPUSH_STAFFPUSH_PROVIDER=web-push` и настоящими VAPID, реальные Android (Chrome) и iPhone (экран «Домой»):

| Кейс | Суть |
|---|---|
| M33-01 | Включить на ezbook, получить запись и заказ, нажать на оба: запись → `/my-bookings` ezbook, заказ → кабинет goods (новая вкладка/Safari на iPhone) |
| M33-02 | То же с включением на goods |
| M33-03 | Удалить устройство goods из профиля ezbook — уведомления на него не приходят; подписка на статус заказа в том же браузере продолжает работать |
| M33-04 | Включить на обоих сайтах в одном браузере — подсказка о дублях видна, после удаления лишней записи дублей нет |
| M33-05 | Выход на goods — push обоих видов на этот браузер прекращаются |
| M33-06 | Вид раздела на 360 и 1280 px на обоих сайтах; `/cabinet/devices` открывает профиль с разделом на экране |
| M33-07 | Устройство, включённое **до** выката: приходят оба вида; после открытия профиля нажатие на «чужое» уведомление ведёт на соседний сайт (перерегистрация воркера сохранила подписку) |

Раздел «Цикл 33» в `TEST_CATALOG.md` — в конце файла, формат как у цикла 31 (`ID | US | Что проверяет | Тест`, ручные
`Кейс | Шаги | Ожидаемый результат | Критерий | Вердикт`).

---

## §33.14. Документация

- `docs/master.md` §«Уведомления о новых записях на телефон или компьютер» — место настройки теперь профиль, одно
  включение на оба сайта.
- `CHANGELOG.md` — раздел «Не выпущено — цикл 33…» сверху: перенос в профиль, одно включение, выход на goods теперь
  отключает push (исправление), название салона в push о записи.
- `API_DOCUMENTATION.md` — §33.12.

---

## §33.15. Структура проекта — что добавляется и меняется

```
ServiceBooking.API/
  DTOs/Notifications/PushDtos.cs                         ~ Kind, SiteUrls, Site; + PushSiteUrlsDto
  Controllers/PushController.cs                          ~ allSites, siteUrls, isCurrent по сайту
  Services/Notifications/PushSubscriptionWriter.cs       ~ ListAsync(allSites)
  Services/Notifications/StaffPushScheduler.cs           ~ без фильтра сайта, url по сайту подписки, название салона
  Services/Notifications/StaffPushLinks.cs               + относительный/абсолютный адрес
  Services/Notifications/StaffPushPayloadJson.cs         + сериализация тела ≤ 1000
  Services/Orders/Notifications/OrderStaffPushQueue.cs   ~ без фильтра сайта, url по сайту подписки
ServiceBooking.Tests/Tests/Cycle33UnifiedPushTests.cs    + CY33-01…13, 20…23
ServiceBooking.Tests/Tests/Cycle24NotificationsTests.cs  ~ CY24-66
ServiceBooking.Tests/Tests/Cycle22RouteTable.golden.txt  ~ 2 строки
ServiceBooking.UnitTests/StaffPushSchedulerTests.cs      ~ ; + StaffPushLinksTests.cs, StaffPushPayloadJsonTests.cs
contracts/cycle33/openapi.yaml, openapi.json             +
frontend/
  public/sw.js, goods/public/sw.js                       ~ PEER_ORIGIN, resolveTarget
  src/api/push.ts                                        ~ allSites
  src/hooks/useWebPush.ts                                ~ §33.9.2
  src/utils/pushWorker.ts (+test)                        +
  src/utils/staffPushTexts.ts (+test)                    +
  src/utils/serviceWorkerRouting.test.ts                 +
  src/utils/pushAvailability.ts                          ~ без PUSH_UNAVAILABLE_MESSAGES
  src/components/push/DevicesAndNotificationsSection.tsx (+test)  +
  src/components/push/PushUnavailableNotice.tsx (+test)  ~
  src/components/push/StaffPushSettingsCard.tsx          ~ текст
  src/components/push/MyDevicesCard.tsx                  − удалить
  src/pages/ProfilePage.tsx, MyBookingsPage.tsx          ~
  src/types/index.ts, api-cycle33.generated.ts           ~ / +
  goods/src/GoodsApp.tsx                                 ~ Navigate
  goods/src/pages/GoodsProfilePage.tsx                   ~
  goods/src/components/GoodsNavbar.tsx                   ~ выход
  goods/src/pages/cabinet/CabinetHomePage.tsx            ~
  goods/src/pages/cabinet/ShopNotificationsPage.tsx      ~ подсказки
  goods/src/hooks/useOrderPush.ts                        ~ registerPushWorker()
  goods/src/utils/goodsPush.ts (+test)                   ~ без ветки staff
  goods/src/pages/cabinet/DevicesPage.tsx (+test)        − удалить
  goods/src/components/push/GoodsIosSteps.tsx            − удалить
  scripts/contracts-to-json.mjs, package.json            ~
.github/workflows/ci.yml, contracts/redocly.yaml         ~
```

Цена рефакторинга существующего: удаляются три компонента (`DevicesPage`, `MyDevicesCard`, `GoodsIosSteps`), их
поведение целиком переходит в общий блок. Риск — потерять состояние, которое было на старой странице; закрывается
таблицей §33.7.2 и тестами `DevicesAndNotificationsSection.test.tsx`, которые переносят проверки `DevicesPage.test.tsx`.

---

## §33.16. Разбивка работ и параллельность

### §33.16.1 Задачи

**Backend**

| ID | Задача | SPEC | Зависит от |
|---|---|---|---|
| BE-0 | `npm run contracts:json` для cycle33, `openapi.json`, `contracts-to-json.mjs`, CI-шаги, `package.json` скрипт генерата, `redocly.yaml`, `OpenApiContractValidatorTests` | T-33-03 | контракт (готов) |
| BE-1 | DTO (§33.3), `PushController` (`allSites`, `siteUrls`, `kind`, `site`, `isCurrent` по сайту), `PushSubscriptionWriter.ListAsync`, golden (2 строки) | US-33-05, Q-33-5 | — |
| BE-2 | `StaffPushLinks`, `StaffPushPayloadJson` + юнит-тесты | US-33-04, US-33-03 | — |
| BE-3 | снять фильтр сайта в `StaffPushScheduler` (2 места) и `OrderStaffPushQueue`; `url` по сайту подписки; название салона в теле записи; правка `StaffPushSchedulerTests`, CY24-66 | US-33-03, US-33-04 | BE-2 |
| BE-4 | функциональные CY33-01…13, контрактные CY33-20…23 | T-33-02 | BE-0, BE-1, BE-3 |
| BE-5 | `API_DOCUMENTATION.md` | T-33-03 | BE-1, BE-3 |

**Frontend**

| ID | Задача | SPEC | Зависит от |
|---|---|---|---|
| FE-0 | `types:api:cycle33`, генерат, `types/index.ts`, `api/push.ts` | — | контракт (готов) |
| FE-0b | `frontend/public/manifest.webmanifest`: `short_name` `EZBOOK` → `Запись` (имя иконки на «Домой»; на уже установленных iPhone остаётся старое до переустановки; проверить тесты/guard на строку `EZBOOK`) | решение заказчика | — |
| FE-1 | `staffPushTexts.ts` + тест; переделка `PushUnavailableNotice` (шаги iPhone, `appName`); `pushAvailability.ts` | О-33-6 | — |
| FE-2 | `pushWorker.ts` + тест; блок маршрутизации в **обоих** `sw.js`; `serviceWorkerRouting.test.ts`; `useOrderPush` на `registerPushWorker()` | US-33-04 | — (контракт `peer` — §33.28) |
| FE-3 | `useWebPush` (§33.9.2), `unsubscribeCurrentDeviceOnLogout(opts)` | US-33-03, US-33-05 | FE-0, FE-2 |
| FE-4 | `DevicesAndNotificationsSection` + тест (переносит проверки `DevicesPage.test.tsx`) | US-33-01…03, 05, 07 | FE-1, FE-3. **Не ждёт BE**: prism-мок `contracts/cycle33` |
| FE-5 | goods: профиль, `Navigate`, кабинет без кнопки, выход в `GoodsNavbar`/`GoodsProfilePage`, удаление `DevicesPage`/`GoodsIosSteps`, `goodsPush.ts` | US-33-01, О-33-5 | FE-4 |
| FE-6 | ezbook: профиль, строка на `/my-bookings`, удаление `MyDevicesCard` | US-33-02 | FE-4 |
| FE-7 | подсказки `ShopNotificationsPage`, `StaffPushSettingsCard` | US-33-06 (P1) | FE-5 |
| FE-8 | строка на `/cabinet` | US-33-08 (P2) | FE-3, FE-5 |

**QA / документы**

| ID | Задача | Зависит от |
|---|---|---|
| QA-1 | раздел «Цикл 33» в `TEST_CATALOG.md`: CY33-, vitest одной строкой, M33-01…07 (T-33-04) | формулировки — сразу |
| QA-2 | интеграционная сверка фронта с живым API (`schemathesis` по `contracts/cycle33/openapi.yaml`, CY33-20…23) | BE-1, FE-4 |
| QA-3 | прогон M33 на стенде с push (гейт выката) | всё |
| QA-4 | `CHANGELOG.md`, `docs/master.md` | всё |

### §33.16.2 Что идёт параллельно, а что последовательно

```
BE-0 ───────────────────────────────┐
BE-1 ───────────────────────────────┼─► BE-4 → BE-5
BE-2 → BE-3 ────────────────────────┘
FE-0 ─┐
FE-1 ─┼─► FE-3 → FE-4 ─┬─► FE-5 → FE-7, FE-8
FE-2 ─┘                └─► FE-6
                                    └──────────► QA-2 (BE-1 + FE-4), QA-3 (всё)
```

- **Backend и frontend полностью параллельны.** Точки стыка — три, все в контракте: параметр `allSites` и поля DTO
  (FE-0/FE-3/FE-4 работают против `prism mock contracts/cycle33/openapi.yaml`), тело push `url` (§33.27 — фронту нужен
  только формат, воркер тестируется на таблице без сервера), адрес регистрации воркера `/sw.js?peer=` (§33.28 — целиком
  внутри фронта, значение приходит из `siteUrls`).
- Внутри бэкенда последовательно только BE-2 → BE-3 (помощники раньше их использования). BE-1 и BE-3 трогают разные
  файлы.
- Внутри фронта FE-1, FE-2 независимы; FE-3 собирает их; FE-5 и FE-6 можно делать двумя исполнителями после FE-4.
- **Мерж:** BE-3 меняет поведение доставки. Фронт без BE-1 работает против старого сервера деградированно (сервер
  игнорирует `allSites`, отдаёт один сайт, нет `kind`/`site`/`siteUrls`), поэтому влитие фронта — после BE-1.

### §33.16.3 Порядок урезания (SPEC §1)

US-33-08 (FE-8) → US-33-07 (подсказка о дублях в FE-4 — отдельным коммитом; фраза «Достаточно включить…» остаётся, она
дешёвая) → US-33-06 (FE-7). P0 (BE-0…BE-4, FE-0…FE-6) не режутся. T-33-04 (M33) — гейт выката, не мержа.

---

## §33.17. Риски и отклонения от буквы SPEC

### §33.17.1 Риски

| # | Риск | Вероятность / вред | Что делаем |
|---|---|---|---|
| R33-1 | Перерегистрация воркера с новым `scriptURL` на каком-то браузере теряет подписку | низкая / устройство перестаёт получать push до повторного включения | Поведение по спецификации; `refreshPushWorkerPeer` трогает только существующие регистрации и только при отличии URL; M33-07 на Android и iPhone. Если подтвердится потеря — откат `refreshPushWorkerPeer` (одна строка), соседа получают только новые включения |
| R33-2 | `openWindow` на адрес соседа запрещён или открывается не там (iOS PWA → Safari) | средняя на iOS / неудобство | Допустимо по SPEC; M33-01/02. Уведомление всё равно приходит |
| R33-3 | Устройства до выката без `peer` открывают страницу по умолчанию своего сайта | высокая до первого открытия профиля / неудобство | §33.5.3, безопасная деградация; на бою push выключен |
| R33-4 | Дубли при включении на обоих сайтах | низкая / раздражение | Подсказка §33.9.4, M33-04 |
| R33-5 | Тело push о записи не влезает в 1000 символов | низкая после §33.6 / 500 на создании записи | `StaffPushPayloadJson` гарантирует длину, CY33-12 |
| R33-6 | Push на реальных устройствах ни разу не проверялся (C24-2, Р-33-1) | высокая для приёмки | M33 — гейт выката, ответственный и стенд назначаются до выката (SPEC «проверить с заказчиком», п. 4) |
| R33-7 | Заказчик ожидал выбор видов на устройстве (Q-33-2) | средняя / переделка | Вне цикла по SPEC. Модель это не блокирует: выбор видов позже ляжет полем на `PushSubscription` и фильтром в тех же двух местах §33.4.1 |
| R33-8 | `www.ezbook.ru` — отдельный origin (nginx отдаёт тот же сайт) | низкая | Подписка, сделанная на `www`, получает записи относительным путём (свой origin), заказы — абсолютным адресом `goods.ezbook.ru`. Переход на соседа с `goods` ведёт на `ezbook.ru` из конфигурации; вход на `www` и без `www` — разные хранилища, возможно окно входа |

### §33.17.2 Отклонения от буквы SPEC и решения сверх неё (читать обязательно)

| # | Что | Почему |
|---|---|---|
| О-33-A | Режим «оба сайта» включается параметром `allSites=true`, а не меняет ответы по умолчанию | Старые вкладки и CY24-65 не ломаются (NFR «Совместимость») |
| О-33-B | Лимит устройств остаётся на сайт; в общем списке может быть до 20 строк | О-33-4: ничего чужого молча не вытесняется |
| О-33-C | Дубли не режутся сервером, выбран вариант «предупредить», формулировка с «Похоже» | О-33-2: надёжного признака «тот же браузер» нет; ложная склейка хуже дубля |
| О-33-D | Нажатие на уведомление соседнего сайта всегда открывает новое окно | Воркер не видит вкладки другого origin; фокусировать свою вкладку запрещено US-33-04 |
| О-33-E | `url` абсолютный только для подписки другого сайта | Безопасная деградация старых воркеров (§33.5.1) |
| О-33-F | Домены воркеру передаются параметром регистрации из `siteUrls` конфигурации | SPEC О-33-3: «из конфигурации, а не зашиваются в код» |
| О-33-G | Выход на goods теперь удаляет серверную строку подписки сотрудника | О-33-5: без этого цикл расширил бы утечку имён клиентов на общем компьютере |
| О-33-H | В push о записи добавлено название салона; тело записи сериализуется без `\u`-экранирования | US-33-03 «в какой компании»; экранирование ×6 съедало бы лимит колонки |
| О-33-I | Предупреждение о лимите заказов тоже приходит на устройства ezbook, текст не меняется | Q-33-2 «все виды по праву»; лимит — на аккаунт, «заказов» однозначно |
| О-33-J | В подсказку `StaffPushSettingsCard` дописано, где мастер включает уведомления, хотя отсылки к старому месту там не было | US-33-06 допускает «если там есть отсылка»; фраза дешёвая и помогает владельцу объяснить мастеру |
| О-33-K | `goods-routes.json` не меняется, `/cabinet/devices` остаётся маршрутом-перенаправлением | NFR: адрес остаётся занятым; `goodsRoutes.test.ts` без правок |
