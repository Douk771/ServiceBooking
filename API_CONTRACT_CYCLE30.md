# API_CONTRACT — цикл 30 ServiceBooking: блок «Для покупателей» и скриншоты на главной goods

**Разделы §30.20–§30.27.** Архитектура — `ARCHITECTURE_CYCLE30.md` (§30.0–§30.15). Машиночитаемые схемы цикла:
- `contracts/cycle30/seed-state.schema.json` — файл засев → съёмка;
- `contracts/cycle30/screenshots-manifest.schema.json` — файл съёмка → компонент.

---

## §30.20. Итог

- **HTTP API не меняется.** Новых маршрутов, полей DTO, кодов ответа и текстов сервера нет. Эталон маршрутов
  `ServiceBooking.Tests/Tests/Cycle22RouteTable.golden.txt` (266 строк) не правится, `Cycle22RouteTableTests` должен
  остаться зелёным без изменений.
- **Новой OpenAPI нет.** Пустая `contracts/cycle30/openapi.yaml` дала бы QA ложное «проверено» (так же решено в цикле
  27, `ARCHITECTURE_CYCLE27.md` §550). `contracts/redocly.yaml` и CI-список линта не меняются.
- **Потребитель API в цикле один — скрипт засева.** Он вызывает только существующие маршруты (§30.21). Источники
  истины по форме — действующие `contracts/cycle{11,23,24,25,26}/openapi.yaml`.
- **Новые договорённости цикла — файловые.** Кто кому что передаёт: засев → `.state/seed.json` → съёмка →
  `screenshots.json` + 5 WebP → компонент. Для обоих JSON есть JSON Schema (§30.22, §30.23).
- Внутри страницы договорённости — id якорей и тексты (§30.24, §30.25).

## §30.21. Существующие маршруты, которые вызывает засев

Все маршруты есть на `b9c2a79`. Колонка «Схема» — где описана форма тела и ответа. «Нет» в «Авторизации» — без
заголовка `Authorization`. На любой ответ вне «Ожидаем» засев печатает метод, путь, статус и тело (`text/plain` или
JSON) и завершает работу с кодом 5 (§30.26). Сервер при этом не «уговаривается»: повторов с другими данными нет.

| # | Метод и путь | Авторизация | Тело запроса (ключевые поля) | Ожидаем | Схема |
|---|---|---|---|---|---|
| 0 | `GET /swagger/v1/swagger.json` | нет | — | 200 (только Development) | Swashbuckle, вне контрактов |
| 1 | `GET /api/health/ready` | нет | — | 200 | `HealthEndpointsExtensions` |
| 2 | `GET /api/legal/documents` | нет | — | 200, версии `Privacy`, `TermsClient`, `TermsOwner` | `contracts/cycle11/openapi.yaml` |
| 3 | `POST /api/auth/register` | нет | `firstName`, `lastName`, `phone`, `password` (≥ 8), `email: null`, `legal: { privacyAcknowledgedVersion, termsAcceptedVersion }`; `phoneVerification` не передаётся | 200 (`AuthResponseDto`, `token`); номер занят → засев выходит с кодом 2 | `DTOs/Auth/RegisterDto.cs`, `API_CONTRACT_CYCLE5.md` §40.1, `CYCLE14` §168 |
| 4 | `POST /api/auth/login` | нет | `phone`, `password` | 200 `AuthResponseDto` | `DTOs/Auth/LoginDto.cs` |
| 5 | `GET /api/cities` | нет | — | 200, город по имени | справочник городов |
| 6 | `POST /api/shops` | Bearer | `CreateShopInput`: `name`, `slug`, `cityId`, `address`, `phone`, `description`, `ownerTerms: { version }` | 201 `CreateShopResponse` | `contracts/cycle23/openapi.yaml` |
| 7 | `PUT /api/shops/{shopId}/settings` | Bearer | `ShopSettingsInput`: `customerMode: Anyone`, `acceptanceMode: Manual`, `allowCustomerCancel: true`, `trackStock: false` | 200 | `cycle23` |
| 8 | `PUT /api/shops/{shopId}/working-hours` | Bearer | `WorkingHoursInput`: 7 дней, `[{ start: "07:00", end: "23:00" }]` | 200 | `cycle24` |
| 9 | `PUT /api/shops/{shopId}/pickup-settings` | Bearer | `PickupSettingsInput`: `asapEnabled: true`, `scheduledEnabled: true`, `slotStepMinutes: 15`, `preorderDays: 1`, `minPrepMinutes: 60` | 200 | `cycle24` |
| 10 | `PUT /api/shops/{shopId}/acceptance` | Bearer | `AcceptanceInput`: `mode: Accepting` | 200 | `cycle24` |
| 11 | `GET` / `PUT /api/shops/{shopId}/notification-settings` | Bearer | `PUT` — только если мессенджер покупателю включён: та же форма с выключенным мессенджером | 200 | `cycle25` (описан целиком) |
| 12 | `POST /api/shops/{shopId}/categories` | Bearer | `CategoryInput`: `name` | 201/200 `CategoryDto` | `cycle23` |
| 13 | `POST /api/shops/{shopId}/products` | Bearer | `ProductInput`: `categoryId`, `name`, `unit` (`Piece`/`Weight`), `price`, `portionText` (только Piece), `weightStepGrams`/`minQuantityGrams` (только Weight), `isPublished: true` | 201/200 `ProductDto` | `cycle24` |
| 14 | `GET /api/storefront/{slug}` | нет | — | 200, товары с ценами | `cycle23`, `cycle26` |
| 15 | `GET /api/storefront/{slug}/pickup-slots` | нет | `date` — сегодня по магазину | 200, слоты с `startUtc` | `cycle24` |
| 16 | `POST /api/storefront/{slug}/orders` | **нет** (гость) | `CreateOrderInput`: `idempotencyKey`, `items[{ productId, quantity, expectedUnitPrice }]` (у Weight `quantity` — граммы), `customerName`, `customerPhone`, `comment`, `captchaToken: null`, `pickup: null` (ASAP) или `{ kind: Slot, date, slotStartUtc }`, `notifyByMessenger: false` | 201/200 `CreateOrderResponse` (`order`, `orderUrl` с токеном) | `cycle24` |
| 17 | `GET /api/shops/{shopId}/order-board` | Bearer | — | 200: `newOrders`, `accepted`, `ready`, `preorders`, `completedToday` | `cycle23`, `cycle24` |
| 18 | `POST /api/shops/{shopId}/orders/{orderId}/accept` | Bearer | `VersionInput`: `{ expectedVersion }` | 200 `StaffOrderDto` | `cycle23` |
| 19 | `POST /api/shops/{shopId}/orders/{orderId}/ready` | Bearer | `VersionInput` | 200 `StaffOrderDto` | `cycle23` |
| 20 | `GET /api/orders/public/{token}` | нет | — | 200 `PublicOrderDto`: `status: Accepted`, `pickup.kind: Slot` | `cycle23`, `cycle24` |

Точные коды успеха (200 или 201) и имена полей засев берёт из указанных схем. Любой 2xx считается успехом.

**Поведение сервера, на которое опирается засев** (всё уже есть, ничего не меняется):
- в dev-стеке `SmartCaptcha:SecretKey` пуст — капча гостя пропускается;
- `PhoneVerification:Provider = stub` по умолчанию — телефон при регистрации не подтверждается;
- лимит `order-create` для анонима — 20 в час с IP, живёт в памяти процесса. `stack.sh reset` пересоздаёт контейнер и
  тем самым его сбрасывает;
- `OrderPhoneThrottle` — у каждого демо-заказа свой телефон;
- осознанные 4xx — строка `text/plain` по-русски или JSON у отдельных маршрутов цикла 25 (CURRENT_STATE §4). Засев
  печатает их как есть.

## §30.22. Файл состояния засева — `frontend/scripts/screenshots/.state/seed.json`

Схема — `contracts/cycle30/seed-state.schema.json`. Пишет BE-30-1, читает FE-30-3. **В git не попадает**
(`.gitignore`): внутри пароль владельца и токен заказа локальной базы.

```json
{
  "schemaVersion": 1,
  "seededAtUtc": "2026-10-01T09:05:12Z",
  "apiUrl": "http://localhost:55000",
  "shop": { "id": "3f0c…", "slug": "pekarnya-na-sadovoy", "name": "Пекарня на Садовой", "timeZoneId": "Europe/Moscow" },
  "owner": { "phone": "+79000000000", "password": "<случайный>" },
  "orderPage": { "token": "<PublicToken>", "number": 3, "status": "Accepted", "statusText": "Принят", "pickupClock": "11:45" },
  "board": { "newOrders": 2, "accepted": 2, "ready": 2, "earliestDueUtc": "2026-10-01T10:05:00Z" }
}
```

- `pickupClock` — `HH:mm` начала слота заказа C в поясе магазина.
- `earliestDueUtc` — самый ранний `pickup.dueUtc` среди активных заказов. Съёмка отказывается работать, если до него
  осталось меньше 5 минут.
- Всё время — ISO 8601 в UTC с `Z`.

## §30.23. Манифест скриншотов — `frontend/goods/src/assets/screenshots/screenshots.json`

Схема — `contracts/cycle30/screenshots-manifest.schema.json`. Пишет скрипт съёмки (FE-30-3/4), читают `shots.ts`
(FE-30-1) и тесты T30-12…17 (FE-30-2). Коммитится вместе с пятью WebP.

```json
{
  "schemaVersion": 1,
  "capturedAt": "2026-10-01T09:12:44Z",
  "sourceCommit": "a1b2c3d",
  "browser": "Chrome/141.0.0.0",
  "orderPage": {
    "cssWidth": 390, "cssHeight": 612,
    "files": { "1x": "order-page-1x.webp", "2x": "order-page-2x.webp" },
    "bytes": { "1x": 38211, "2x": 97540 },
    "orderNumber": 3, "pickupClock": "11:45", "statusText": "Принят"
  },
  "boardDesktop": {
    "cssWidth": 1280, "cssHeight": 820,
    "files": { "1x": "board-desktop-1x.webp", "2x": "board-desktop-2x.webp" },
    "bytes": { "1x": 88012, "2x": 191230 },
    "orderCount": 6
  },
  "boardPhone": {
    "cssWidth": 390, "cssHeight": 780,
    "files": { "2x": "board-phone-2x.webp" },
    "bytes": { "2x": 101877 }
  }
}
```

Правила:
- имена файлов фиксированы: компонент импортирует их статически, `files` нужен для сверки;
- `cssWidth`/`cssHeight` — CSS-пиксели кадра. Пиксели файла = CSS × плотность (T30-17);
- `bytes` — фактический размер. Бюджеты — в схеме (`maximum`) и в T30-16;
- в манифесте нет токенов, телефонов и адресов стенда.

## §30.24. Договорённости внутри страницы (id и якоря)

| Элемент | id / атрибуты | Кто ссылается |
|---|---|---|
| сетка магазинов `section[aria-label="Магазины"]` | `id="shop-list"`, `tabIndex={-1}` | кнопка «Выбрать магазин» (`href="#shop-list"`) |
| h2 «Для покупателей» | `id="buyers-title"`, `tabIndex={-1}` | `section[aria-labelledby]`, ссылка под `h1` (`href="#buyers-title"`) |
| h3 «Как сделать заказ» | `id="buyers-steps-title"` | `ol[aria-labelledby]` |
| h3 «Как следить за заказом» | `id="buyers-track-title"` | `ul[aria-labelledby]` |
| «Для бизнеса» | `biz-title`, `biz-steps-title` — **без изменений** | — |

Ни один id не совпадает с зарезервированными слагами `contracts/cycle23/goods-routes.json`.

## §30.25. Тексты цикла

Все тексты — фронтовые константы, сервер их не присылает. Полный список дословно — `ARCHITECTURE_CYCLE30.md` §30.3.
Серверные тексты (`OrderTexts`, `ShopTexts`) не меняются.

## §30.26. Автоматические проверки для QA

| Что | Команда | Ожидаем |
|---|---|---|
| Манифест соответствует схеме | `npx --yes ajv-cli@5 validate -s contracts/cycle30/screenshots-manifest.schema.json -d frontend/goods/src/assets/screenshots/screenshots.json` | `valid` |
| Состояние засева соответствует схеме (после `shots:seed`) | `npx --yes ajv-cli@5 validate -s contracts/cycle30/seed-state.schema.json -d frontend/scripts/screenshots/.state/seed.json` | `valid` |
| Компонент и файлы сошлись с манифестом | `cd frontend && npx vitest run goods/src/assets goods/src/components goods/src/pages/CatalogHomePage.test.tsx` | T30-01…19 зелёные |
| API не изменился | `dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~Cycle22RouteTableTests"`; `git diff --stat b9c2a79 -- ServiceBooking.* contracts/cycle2*` | зелёный; пусто |
| Засев не пишет в чужое | `SHOTS_API_URL=https://goods.ezbook.ru npm run shots:seed` (из `frontend/`) | код 3, в выводе нет ни одного HTTP-запроса |
| Секреты не в git | `git ls-files frontend/scripts/screenshots/.state` | пусто |

**Коды выхода скриптов:**

| Код | `shots:seed` | `shots:capture` |
|---|---|---|
| 0 | засеяно, `.state/seed.json` записан | 5 файлов и манифест записаны |
| 1 | непредвиденная ошибка (стек, сеть) | непредвиденная ошибка (браузер, Vite) |
| 2 | база уже засеяна → `stack.sh reset` | нет `.state/seed.json` или данные устарели → пересеять |
| 3 | адрес не локальный или не Development — отказ до записи | — |
| 4 | время магазина вне `[07:00, 20:30]` | в кадре запретная строка или модалка |
| 5 | сервер ответил не так, как ожидалось (§30.21), или самопроверка не прошла | бюджет веса или размер в пикселях не сошлись |

## §30.27. Что фронт и скрипты обязаны делать и чего делать не должны

- Главная goods **не делает новых запросов**: блоки статичные, картинки — статика сборки (US-30-01).
- Скрипты не вызывают ничего, кроме маршрутов §30.21. Никаких прямых SQL, `docker exec psql` и правок БД.
- Засев не шлёт токены и пароль никуда, кроме своего API. В stdout пароль не печатается: только путь к файлу состояния.
- Съёмка не выдаёт браузеру разрешение на уведомления. Карточку push и блок ссылки заказа в кадр не берёт.
- Бюджеты веса и имена файлов меняются только вместе со схемой §30.23, тестами T30-15…17 и этим документом.
