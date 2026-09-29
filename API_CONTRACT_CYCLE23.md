# API_CONTRACT — цикл 23 ServiceBooking: «Заказы», цикл 1 «Ядро» (goods.ezbook.ru)

**Разделы §406–§425.** Точка синхронизации backend-, frontend-, devops-инженера и QA на время цикла. Решения —
`ARCHITECTURE_CYCLE23.md` (§386–§405), требования — `SPEC.md` цикла 23.

**Источник истины по ФОРМЕ** — `contracts/cycle23/openapi.yaml`. Этот файл фиксирует то, чего схема не выражает:
порядок проверок, тексты, какие коды когда, изменения поведения существующих маршрутов. Расхождение по форме —
прав YAML; по смыслу — этот файл.

```bash
# фронт — мок до появления бэкенда
npx @stoplight/prism mock contracts/cycle23/openapi.yaml --port 4023
# фронт — типы (генерат в git, руками не правится)
cd frontend && npm run types:api:cycle23          # -> src/types/api-cycle23.generated.ts
# QA — автосверка живого бэкенда со схемой
schemathesis run contracts/cycle23/openapi.yaml --base-url http://localhost:5000 --checks all
# CI — линт схемы
npx @redocly/cli lint --config ../contracts/redocly.yaml ../contracts/cycle23/openapi.yaml
```

Ещё два машиночитаемых артефакта: `contracts/cycle23/goods-routes.json` (маршруты goods, политика адреса магазина),
`contracts/cycle23/order-money-vectors.json` (эталон денежной арифметики — читают юнит-тесты бэкенда и фронта).

**Базовая ревизия** — `develop` = `169534a`. Существующий API — `API_DOCUMENTATION.md` и контракты циклов 3–22.

---

## §406. Конвенции

### §406.1 Без изменений

| Что | Правило |
|---|---|
| JSON | camelCase; enum — строкой (имя члена C#); `DateTime` — ISO-8601 UTC; `DateOnly` — `"YYYY-MM-DD"`; `decimal` — число с 2 знаками |
| 400 / 402 / 429 | **голая строка** `text/plain` по-русски — фронт показывает `response.data` как есть |
| 401 / 403 | пустое тело. На goods 401 обрабатывает общий `api/client.ts` (выход + `/login`) |
| 404 | пустое тело; **не оракул**: «нет такого», «другого типа», «не ваше» неразличимы там, где это публичный маршрут |
| 451 | глобальный гейт (`text/plain`) → `ConsentGate`; владельческий (`application/json`) → `OwnerTermsGateModal`. Новые маршруты в allow-list **не** добавлены |
| 413 | пустое (лимит размера файла) |
| 500 | `ProblemDetails` + `traceId` |

### §406.2 Намеренное исключение: все 409 домена заказов — JSON

`{ "code": "<машиночитаемый>", "message": "<готовый русский текст>", … }` — три формы: `OrderRefusalDto`
(оформление), `OrderConflictDto` (действия над заказом), `CatalogConflictDto` (каталог и адрес магазина).
Прецеденты — `TrialRefusalDto` (цикл 18), `PricingPublicationBlockedDto` (цикл 11). Фронт ветвится **по `code`**,
показывает `message`. **409 существующих маршрутов** (в том числе новый 409 «это магазин», §421) — по-прежнему строка.

### §406.3 Количества и деньги

- `quantity` — **целое** в базовой единице: штуки (`Piece`) или **граммы** (`Weight`). Цена `Weight` — за 1 кг.
- Суммы строк и итог считает сервер (`order-money-vectors.json`); фронт может показать предварительный расчёт по тому
  же правилу до ответа `quote`, но всё, что сохранено, — числа сервера.
- `isApproximate`/`totalIsApproximate: true` — показывать «≈ 540 ₽» и пояснение «сумма уточнится при выдаче по
  фактическому весу». После выдачи — точная сумма `total` (= `FinalTotal`).

### §406.4 Роли в магазине

`Owner` (`CompanyOwner`), `Staff` (`Master`), `SuperAdmin`. Таблица прав — `ARCHITECTURE_CYCLE23.md` §392.2.
Сотрудник на владельческом маршруте — **403**. Любой, кто не участник магазина, — **403**; несуществующий id или id
салона — **404**. Порядок: токен → существование и тип → роль.

---

## §407. Сводка маршрутов

| Маршрут | Новый/изм. | Доступ | Rate limit | 451 |
|---|---|---|---|---|
| `GET /api/companies/my`, `GET /api/companies/member` | изм.: `?kind=` | вошедший | — | глоб. |
| `GET /api/companies/kinds-summary` | новый | вошедший | — | глоб. |
| `GET /api/companies/{slug}` | изм.: `+kind`, `+publicUrl` | все | — | глоб. (вошедший) |
| `GET /api/companies`, `GET /api/companies/public` | изм.: только салоны | все | — | — |
| `GET /api/admin/companies` | изм.: `?kind=`, `+kind`, `+publicUrl` | SuperAdmin | — | глоб. |
| `POST /api/companies/{id}/members` | изм.: для магазина только `Master` | владелец | — | владельч. |
| маршруты записи §421 | изм.: 409 для магазина | как были | как были | как были |
| `GET /api/profile/export` | изм.: `+orders` | вошедший | `data-export` | allow-list |
| `POST /api/profile/delete-account` | изм.: обезличивает заказы | вошедший | — | allow-list |
| `POST /api/shops` | новый | вошедший | — | владельч. |
| `GET /api/shops/my`, `GET /api/shops/slug-check` | новый | вошедший | — | глоб. |
| `GET /api/shops/{shopId}`, `GET …/qr` | новый | персонал | — | глоб. |
| `PUT /api/shops/{shopId}/settings`, `…/seller`, `…/slug` | новый | владелец | — | владельч. |
| `GET …/categories`, `GET …/products` | новый | персонал | — | глоб. |
| `POST/PUT/DELETE …/categories*`, `…/category-order`, `POST/PUT/DELETE …/products*`, `…/product-order`, `…/image` | новый | владелец | `uploads` у `…/image` | владельч. |
| `PUT …/products/{id}/sold-out`, `PUT …/products/{id}/stock` | новый | персонал | — | глоб. |
| `GET /api/storefront/{slug}`, `POST …/quote` | новый | все | `storefront` | глоб. (вошедший) |
| `POST /api/storefront/{slug}/orders` | новый | все | `order-create` | глоб. (вошедший) |
| `GET /api/orders/public/{token}`, `POST …/cancel` | новый | все | `order-public` | глоб. (вошедший) |
| `GET /api/orders/my` | новый | вошедший | — | глоб. |
| `GET /api/shops/{shopId}/order-board` | новый | персонал | `order-board` | глоб. |
| `GET …/orders/{orderId}`, `POST …/accept|reject|ready|issue-quote|issue|not-picked-up|cancel`, `PUT …/items` | новый | персонал | — | глоб. |

«глоб.» — действует глобальный гейт (для вошедшего с неподтверждённой существенной правкой — 451). «владельч.» —
`[RequiresOwnerTerms]`. «allow-list» — гейт не действует (существующее правило).

---

## §408. Изменения существующих маршрутов компаний

### §408.1 `CompanyDto` — два добавочных поля (в конце, с дефолтами — конвенция §115)

```json
{ "...": "все прежние поля без изменений",
  "kind": "Services",
  "publicUrl": "https://ezbook.ru/company/barber-na-lenina" }
```

У магазина: `"kind": "Orders"`, `"publicUrl": "https://goods.ezbook.ru/shaurma-na-lenina"`. Поля заполнены у
**всех** вызывающих, включая анонимных.

### §408.2 `GET /api/companies/my` и `GET /api/companies/member` — `?kind=`

- `kind` не передан → **`Services`**. Фронт ezbook параметр **не шлёт** — магазины из его кабинета исчезают сами
  (US-23-02). Фронт goods для своих нужд пользуется `GET /api/shops/my` и этот параметр не использует.
- `kind=Orders` → только магазины. Невалидное значение → 400 `«Неизвестный тип компании»`.

### §408.3 `GET /api/companies/kinds-summary` (новый)

```json
{ "services": { "count": 1, "siteUrl": "https://ezbook.ru" },
  "orders":   { "count": 2, "siteUrl": "https://goods.ezbook.ru" } }
```

Считаются активные компании, где вызывающий — участник **любой** роли (как `/member`). Для строки «Ваши магазины
управляются на goods.ezbook.ru» (ezbook) и «Ваши салоны — на ezbook.ru» (goods), и для ссылки на профиль ezbook.

### §408.4 `GET /api/companies/{slug}`

Отдаёт и магазин (`kind: "Orders"`, `publicUrl` на goods). Фронт ezbook на `CompanyPage` при `kind === "Orders"`
делает `location.replace(publicUrl)`; на `EmbedPage` — показывает «У этой компании нет онлайн-записи».
Заблокированный — 404, как и раньше.

### §408.5 `GET /api/companies` и `GET /api/companies/public`

Только `kind = Services` (фильтр в SQL). Форма ответа — прежняя (плюс §408.1).

### §408.6 `GET /api/admin/companies`

`?kind=Services|Orders` (не передан — все), в элементах `kind` и `publicUrl`. Ссылка «открыть публичную страницу»
на фронте — `publicUrl` (US-23-28). Блокировка/разблокировка — прежний `PUT /api/admin/companies/{id}`
(`isActive`) — работает и для магазина.

### §408.7 `POST /api/companies/{id}/members` для магазина

Роль — только `"Master"` (в интерфейсе goods «Сотрудник»). Иначе 400 `«В магазин можно добавить только сотрудника.»`.
Остальное (поиск/автосоздание по телефону, лимит мест 402, 409 «уже участник») — без изменений. `GET` и `DELETE`
участников — без изменений. `PUT …/provides-services|services|commission` у магазина → 409 (§421).

---

## §409. Магазин

### §409.1 `POST /api/shops` — создать магазин

Тело `CreateShopInput`. **Порядок проверок** (совпадает с `POST /api/companies` + политика адреса):

| # | Проверка | Ответ |
|---|---|---|
| 1 | `ownerTerms.version` пуст | 400 `«Для создания компании нужно принять соглашение с владельцем.»` |
| 2 | манифест недоступен | 503 `«Правовые документы временно недоступны.»` |
| 3 | версия не текущая | 409 **строка** `«Соглашение было обновлено ещё раз — перечитайте и примите новую редакцию.»` (существующий текст) |
| 4 | `name` пусто / > 200 | 400 `«Укажите название магазина»` |
| 5 | `slug` не по формату / длине | 409 `CatalogConflictDto{code:"SlugInvalid"}` `«Адрес — латиница, цифры и дефис, от 3 до 50 символов»` |
| 6 | `slug` в резерве | 409 `SlugReserved` `«Этот адрес занят сервисом — выберите другой»` |
| 7 | `slug` занят (без учёта регистра, салоны и магазины) | 409 `SlugTaken` `«Адрес уже занят — выберите другой»` |
| 8 | нет `cityId` | 400 `«Укажите город магазина»` |
| 9 | город не найден/неактивен | 400 `«Город не найден»` |
| 10 | неизвестный `timeZoneId` | 400 `«Неизвестный часовой пояс»` |
| 11 | лимит компаний аккаунта (салоны + магазины) | 402 строка `BillingTexts.CompanyLimitReached` |
| — | успех | 201 `{ shop: ShopManageDto, token }` |

⚠️ Коды 3 и 7: для `POST /api/companies` занятость slug — прежняя 409-строка `«Slug already taken»`; у магазина —
JSON (новый маршрут, новая конвенция §406.2). Фронт **обязан заменить токен** на новый из ответа (claim «lco»).
Адрес компании при создании — как в ezbook: фронт сначала показывает предупреждение о публичности
(`POST /api/companies/address/notice`), без согласия поле адреса не отправляет.

### §409.2 `GET /api/shops/my`

Магазины, где вызывающий — `CompanyOwner` или `Master`, **включая заблокированные** (`isActive: false` — кабинет
показывает «Магазин заблокирован администратором»). Сортировка по названию.

### §409.3 `GET /api/shops/slug-check?slug=&name=`

- `slug` передан → проверка по §409.1 п. 5–7: `{ slug, available, reason, reasonCode }`.
- Только `name` → предложение: транслитерация → нормализация → свободный вариант (`-2`, `-3`…):
  `{ slug: "shaurma-na-lenina", available: true, reason: null, reasonCode: null }`.
- Оба пусты → 400 `«Передайте адрес или название»`.

### §409.4 `GET /api/shops/{shopId}` → `ShopManageDto`

`myRole`, `settings`, `seller` (с `isComplete`, `requiredFields: []` в цикле 1), `acceptingOrders`,
`phoneVerificationAvailable` (= `GET /api/phone-verification/config` → `enabled`), `publicUrl`, `productCount`.

### §409.5 `PUT /api/shops/{shopId}/settings` (владелец)

Тело — все четыре поля (полная замена). Действуют **только на новые заказы** (снимки в заказе).
- `customerMode = VerifiedPhoneOnly` при выключенной подсистеме подтверждения → 409 `CatalogConflictDto{code:
  "PhoneVerificationUnavailable"}` `«Подтверждение телефона сейчас недоступно на платформе — режим включить нельзя»`.
- Фронт при выборе `VerifiedPhoneOnly` **до сохранения** показывает: «Подтвердить номер можно только через MAX —
  покупатели без MAX не смогут заказать» (US-23-10, R23-2).
- `trackStock: false` не трогает остатки товаров; `true` начинает с текущих цифр.

### §409.6 `PUT /api/shops/{shopId}/seller` (владелец) — [legal L2]

Все поля необязательны. `inn` — формальная проверка `InnValidator` по `legalForm` → 400 `«ИНН указан с ошибкой»`.
Пустая строка = очистить поле. Ответ — `ShopManageDto`.

### §409.7 `PUT /api/shops/{shopId}/slug` (владелец)

Проверки §409.1 п. 5–7 (свой же текущий адрес → 200 без изменений). Ответ — `ShopManageDto` с новым `publicUrl`.
Фронт до вызова: «Старая ссылка и напечатанные QR-коды перестанут работать».

### §409.8 `GET /api/shops/{shopId}/qr`

`image/png`, `Content-Disposition: attachment; filename="<slug>-qr.png"`. Фронт берёт blob через `api` (токен),
показывает предпросмотр `URL.createObjectURL`, скачивает тем же blob.

---

## §410. Каталог

### §410.1 Категории

- `GET …/categories` — все, включая скрытые, по `position`; `productCount` — неудалённые товары.
- `POST` — `{ name, isHidden? }`, встаёт последней. 400 `«Укажите название категории»` (пусто/ > 100);
  409 `CategoryLimitReached` `«В магазине уже 100 категорий»`.
- `PUT …/categories/{id}` — `{ name, isHidden }` полная замена.
- `PUT …/category-order` — `{ ids }` — **ровно** все категории магазина без повторов, иначе 400
  `«Список категорий устарел — обновите страницу»`.
- `DELETE` — пустая → 204; с товарами → 409 `CategoryNotEmpty` `«В категории есть товары — сначала перенесите или
  удалите их»`.

### §410.2 Товары — `ProductInput`

| Поле | Правило | Ошибка (400) |
|---|---|---|
| `name` | 1–200 после trim | `«Укажите название товара»` |
| `categoryId` | null или категория этого магазина | `«Категория не найдена»` |
| `price` | 0.01…1 000 000, не больше 2 знаков | `«Цена — от 0,01 до 1 000 000 ₽, не больше двух знаков после запятой»` |
| `unit` | `Piece`/`Weight`; при `PUT` не меняется | 409 `UnitChangeNotAllowed` `«Тип товара (штучный/весовой) менять нельзя — заведите новый товар»` |
| `portionText` | только Piece, ≤ 50 | `«Порция указывается только у штучного товара»` |
| `weightStepGrams` | только Weight, 10…5000; null → 100 | `«Шаг — от 10 до 5000 г»` |
| `minQuantityGrams` | только Weight, ≥ шаг, кратно шагу, ≤ 10 000; null → шаг | `«Минимальный вес — не меньше шага и кратен ему»` |
| `foodInfo.compositionAndAllergens` | ≤ 2000 | `«Состав — не длиннее 2000 символов»` |
| — | лимит 1000 товаров | 409 `ProductLimitReached` `«В магазине уже 1000 товаров»` |

`POST` → 201 `ProductDto` (последний в категории). `PUT` — полная замена (поля, не переданные в теле, считаются null
у nullable и дефолтом у шага/минимума). `DELETE` → 204, мягко: из каталога и витрины исчезает, заказы не меняются.
Изменение цены не меняет созданные заказы (снимок).

### §410.3 Порядок товаров — `PUT …/product-order`

`{ categoryId (null = «Другое»), productIds }` — ровно все неудалённые товары этой категории, иначе 400
`«Список товаров устарел — обновите страницу»`. Ответ — товары категории в новом порядке.

### §410.4 Фото товара

`POST …/image` multipart `file` — существующий конвейер (`ImageUploadService`: сигнатура байт, EXIF, сжатие, снятие
метаданных), политика `uploads`. Ошибки — существующие тексты загрузок (`utils/uploadError.ts`). `DELETE …/image` →
товар без фото.

### §410.5 «Закончилось» и остаток (владелец и сотрудник)

- `PUT …/sold-out` `{ isSoldOut }` → `ProductDto`. Действует сразу; заказы не меняются.
- `PUT …/stock` `{ onHand }` — целое ≥ 0 (штуки или граммы) либо `null` («не учитывать»). 400 `«Остаток — целое
  число от 0»`. Можно выставить `onHand < reserved` (`free` станет отрицательным — кабинет показывает предупреждение
  «Резерв больше остатка на N»).
- `ProductDto.stock` — `{ onHand, reserved, free }` всегда; `availableToCustomers` — как увидит покупатель.

---

## §411. `GET /api/storefront/{slug}` — витрина

- Нет такого адреса или это салон → **404** (пустое тело).
- Заблокированный/деактивированный → 200, `isAvailable: false`, `acceptingOrders: false`, `categories: []`,
  `notAcceptingReason: «Магазин недоступен»`. Фронт показывает экран «Магазин недоступен».
- Иначе: `categories` — видимые категории в порядке `position`, внутри — опубликованные неудалённые товары по
  `position`; **последним** блок `{ id: null, name: "Другое" }` (товары без категории), если непуст. Пустые
  категории не отдаются. Товары скрытых категорий не отдаются вовсе.
- `available: false` — товар «Закончилось» (отметка или нехватка остатка ниже минимума) или магазин не принимает
  заказы. Числа остатка на витрине **нет**.
- `seller` — null, если ни одно поле реквизитов не заполнено. `customerMode`, `allowCustomerCancel` — для подсказок
  на экране оформления.
- Для вошедшего с неподтверждённой существенной правкой документов — 451 (глобальный гейт, как везде).

---

## §412. `POST /api/storefront/{slug}/quote` — проверка корзины

Тело `{ items: [{ productId, quantity }] }`: > 50 строк → 400 `«В корзине не больше 50 позиций»`, повтор
`productId` → 400 `«Товар в корзине повторяется»`; пустой список → 200 с пустыми `lines` и `total: 0`.
Иначе всегда **200** (кроме 404/429): каждая строка — текущая цена, `lineTotal`, `isApproximate`, и `problem`, если
её нельзя заказать как есть (`NotFound`, `Unpublished`, `CategoryHidden`, `SoldOut`, `InsufficientStock` с
`availableQuantity`, `BelowMinimum`, `InvalidQuantity`). `PriceChanged` в `quote` не бывает — `quote` сам и есть
текущая цена. `total` — по строкам без проблем. Ничего не резервирует.

Фронт вызывает при открытии корзины и перед «Заказать»; строки с проблемами подсвечивает, «Заказать» блокирует, пока
`hasProblems` или `!acceptingOrders`.

---

## §413. `POST /api/storefront/{slug}/orders` — оформить заказ

### §413.1 Порядок проверок и ответы

| # | Проверка | Ответ |
|---|---|---|
| 1 | Модель: `customerName` 1–100 после trim; `comment` ≤ 500; без повторов `productId` | 400 строка (`«Укажите имя»`, `«Комментарий — не длиннее 500 символов»`, `«Товар в корзине повторяется»`) |
| 2 | Магазин по slug; салон/нет | 404 |
| 3 | `ShopOrderingGate` | 409 `ShopNotAcceptingOrders` + `notAcceptingReason` |
| 4 | Корзина пуста / больше 50 строк | 409 `EmptyCart` `«Корзина пуста»` / `TooManyLines` `«В заказе не больше 50 позиций»` |
| 5 | Повтор `idempotencyKey` в этом магазине | **200** + тот же `CreateOrderResponse` |
| 6 | Строгий режим, нет токена | 409 `LoginRequired` `«Этот магазин принимает заказы только от покупателей с подтверждённым телефоном. Войдите или зарегистрируйтесь»` |
| 7 | Строгий режим, подсистема подтверждения выключена, номер не подтверждён | 409 `PhoneVerificationUnavailable` `«Подтверждение телефона сейчас недоступно — заказать в этом магазине пока нельзя»` |
| 8 | Строгий режим, номер аккаунта не подтверждён (`VerifiedPhones`) | 409 `PhoneVerificationRequired` `«Подтвердите номер телефона через MAX, чтобы оформить заказ»` |
| 9 | Гость (`Anyone`, без токена): капча включена и нет токена капчи / не прошла | 400 `«Подтвердите, что вы не робот»` / `«Проверка капчи не пройдена»` |
| 10 | Гость: телефон пуст / не российский | 400 `«Укажите телефон»` / `«Введите номер телефона в формате +7 (900) 000-00-00»` |
| 11 | Лимит по телефону | 429 `«Слишком много заказов на этот номер — дождитесь выдачи текущих или позвоните в магазин»` |
| 12 | Позиции: количество, доступность, остаток, цены | 409 `PriceChanged` (все проблемы — только цены) или `ItemsUnavailable` (есть иные) + **все** `problems` |
| — | успех | **201** `{ order: PublicOrderDto, orderUrl }` |

Вошедший в режиме `Anyone`: капчи нет, телефон = номер аккаунта (поле тела игнорируется), `customerKind: Customer`.
Вошедший и подтверждённый — `CustomerPhoneVerified = true`. 429 от политики `order-create` (по IP/пользователю) —
строка `«Слишком много заказов подряд — попробуйте через несколько минут»`.

### §413.2 Цена изменилась

```json
{ "code": "PriceChanged",
  "message": "Цена изменилась — проверьте и подтвердите заказ ещё раз",
  "problems": [ { "productId": "…", "name": "Шаурма классическая", "reason": "PriceChanged",
                  "message": "Было 250 ₽, стало 270 ₽", "currentUnitPrice": 270.00, "availableQuantity": null } ] }
```

Фронт показывает новые цены, по подтверждению повторяет запрос **с тем же `idempotencyKey`** и новыми
`expectedUnitPrice`.

### §413.3 Нехватка

`ItemsUnavailable`, у позиции `InsufficientStock` → `message: «Осталось только 2 шт»` / `«Осталось только 0,8 кг»`,
`availableQuantity: 2` / `800`. Корзину можно поправить и отправить снова (тот же ключ).

### §413.4 После успеха (фронт)

Очистить корзину этого магазина, сбросить `idempotencyKey`, перейти на `/o/<token>` (из `orderUrl`), показать крупно
номер и ссылку с «Скопировать»; при `order.isGuest` — «Сохраните ссылку: другого способа вернуться к заказу нет».
Под кнопкой «Заказать» — строка правовых ссылок: текст `GET /api/legal/texts/OrderCheckoutNotice`, на 404 — нейтральная
строка со ссылками `/privacy` и `/terms` **[legal L4]**.

---

## §414. Заказ у покупателя

### §414.1 `GET /api/orders/public/{token}` → `PublicOrderDto`

- Неизвестный/неверный токен → 404 без тела (не отличим от «не было»).
- `timeline` — всегда четыре шага `New → Accepted → Ready → Issued` (`reached`, `reachedAtUtc`); при конечном
  «не выдан» (`Rejected`, `CancelledBy*`, `NotPickedUp`) — статус крупно, шкала до последнего достигнутого шага.
- `customerPhoneMasked` — `PhoneDisplayMask`; полного номера по ссылке нет нигде.
- `shopChanges` — правки магазина (из журнала, без имён сотрудников): «Магазин изменил заказ: было → стало» +
  `comment`. Пусто — правок не было.
- `reason` — причина отклонения/отмены магазином, если указана.
- `canCancel` = `AllowCustomerCancelSnapshot && status ∈ {New, Accepted}`.
- `total`/`totalIsApproximate` — предварительная сумма до выдачи, точная после.
- Фронт опрашивает каждые **10 с**, пока вкладка открыта; конечный статус — опрос останавливается.
- Персональных данных покупателя после обезличивания нет: `customerName: null`, `customerPhoneMasked: null`.

### §414.2 `POST /api/orders/public/{token}/cancel`

Без тела. Подтверждение — на фронте («Отменить заказ № 27?»).

| Ситуация | Ответ |
|---|---|
| Отмена разрешена, статус `New`/`Accepted` | 200 `PublicOrderDto` (`CancelledByCustomer`, `canCancel: false`); резерв вернулся |
| Статус `Ready` | 409 `AlreadyReady` `«Заказ уже собран — свяжитесь с магазином»` + `publicOrder` |
| Отмена не разрешена для заказа / конечный статус | 409 `CancelNotAllowed` `«Отменить этот заказ нельзя — свяжитесь с магазином»` + `publicOrder` |
| Нет токена | 404 |

Автор события: вошедший владелец заказа — `Customer`, иначе `Guest`.

### §414.3 `GET /api/orders/my` (P1)

Заказы, где `CustomerUserId` = вызывающий: активные (по `createdAtUtc` убыв.), затем конечные с `completedAtUtc` за
последние 30 дней. Гостевые заказы на тот же номер **не** подтягиваются.

---

## §415. `GET /api/shops/{shopId}/order-board` — экран заказов

- Параметры `sinceRevision`, `businessDate` — из прошлого ответа (первый запрос — без них).
- Совпали оба → `{ revision, changed: false, businessDate, serverTimeUtc }`, массивы `null`.
- Иначе → `changed: true` и четыре массива `StaffOrderCardDto`: `newOrders` (`New`), `accepted`, `ready` — от старых к
  новым по `createdAtUtc`; `completedToday` — конечные с `completedAtUtc` в текущих сутках магазина, по
  `completedAtUtc` убыв.
- `serverTimeUtc` — для «N мин назад» без расхождения часов планшета.
- Фронт: опрос каждые 5 с (таймер в Web Worker), немедленно при возвращении во вкладку; звук и выделение — на id,
  которых не было в прошлом ответе, в `newOrders` (или в `accepted` при автоприёме); `(N)` новых в заголовке вкладки;
  плашка «Нет связи», если успешного ответа нет > 30 с. Подробно — `ARCHITECTURE_CYCLE23.md` §397.

---

## §416. Карточка и переходы статусов

- `GET …/orders/{orderId}` → `StaffOrderDto` = карточка + `events` (журнал с автором и временем, US-23-26).
  Заказ другого магазина → 404.
- Все действия — `POST` с `expectedVersion`; ответ 200 — обновлённый `StaffOrderDto`.

| Маршрут | Тело | Из → В |
|---|---|---|
| `…/accept` | `{ expectedVersion }` | New → Accepted |
| `…/reject` | `{ expectedVersion, reason? }` (≤ 300, 400 `«Причина — не длиннее 300 символов»`) | New → Rejected |
| `…/ready` | `{ expectedVersion }` | Accepted → Ready |
| `…/not-picked-up` | `{ expectedVersion }` | Ready → NotPickedUp |
| `…/cancel` | `{ expectedVersion, reason? }` | Accepted, Ready → CancelledByShop |
| `…/issue` | §418 | Ready → Issued |

**409 `OrderConflictDto`:**

| Код | Когда | Тело |
|---|---|---|
| `VersionMismatch` | заказ изменён с момента загрузки (другим сотрудником/покупателем) | `«Заказ уже изменён — вот актуальное состояние»` + `order` |
| `InvalidTransition` | действие недопустимо в текущем статусе | `«Это действие недоступно для заказа в статусе „…“»` + `order` |

Фронт на 409 заменяет карточку на `order` из тела и **не повторяет** действие автоматически.
`availableActions` в карточке — единственный источник того, какие кнопки показывать.

---

## §417. `PUT …/orders/{orderId}/items` — правка магазином

Тело — **полный желаемый состав**: `[{ itemId, quantity }]` для оставшихся/изменённых строк и `[{ productId,
quantity }]` для новых (замена = убрать строку + добавить новую). `commentForCustomer?` ≤ 500.

| Проверка | Ответ |
|---|---|
| Статус не New/Accepted/Ready | 409 `InvalidTransition` + `order` |
| `expectedVersion` устарела | 409 `VersionMismatch` + `order` |
| `items` пуст | 409 `LastItemCannotBeRemoved` `«Пустой заказ не бывает — отклоните или отмените заказ»` |
| чужой `itemId`, у строки и `itemId`, и `productId`, или ни того ни другого | 400 `«Состав заказа указан с ошибкой»` |
| `productId` — не товар магазина или удалён | 409 `ProductUnavailable` `«Товар больше не продаётся»` |
| количество не по правилам единицы (по шагу **снимка** строки или товара) | 409 `InvalidQuantity` + `problems` |
| рост количества/новая строка больше свободного остатка | 409 `InsufficientStock` + `problems` (с `availableQuantity`) |
| успех | 200 `StaffOrderDto`, `isModified: true`, событие `Edited` (было → стало, итог до/после, комментарий) |

Новая позиция — по **текущей** цене каталога. Сообщений покупателю нет — он видит правку в `shopChanges`.

---

## §418. Выдача

1. `POST …/issue-quote` `{ actualQuantities: [{ itemId, quantity }] }` — по записи на каждую **весовую** позицию
   (граммы 1…100 000; кратность шагу не требуется). Ответ `{ lines, finalTotal }`; ничего не меняет. Фронт показывает
   «К оплате: 1 234,50 ₽» перед подтверждением; поля веса заполнены заказанным весом.
2. `POST …/issue` `{ expectedVersion, actualQuantities }` — те же количества. Ответ — `StaffOrderDto` со статусом
   `Issued`, `quantityActual`, `total` = итог к оплате (виден покупателю).

Ошибки: нет записи для весовой позиции / лишняя / запись для штучной → 400 `«Укажите фактический вес каждой весовой
позиции»`; статус не `Ready` → 409 `InvalidTransition`; версия → 409 `VersionMismatch`. Нехватка остатка при выдаче
**не** ошибка: списание до нуля, отметка в журнале (не видна покупателю).

---

## §419. Закрытые таблицы кодов 409 (JSON)

**`OrderRefusalCode`** (оформление): `ShopNotAcceptingOrders`, `LoginRequired`, `PhoneVerificationRequired`,
`PhoneVerificationUnavailable`, `EmptyCart`, `TooManyLines`, `PriceChanged`, `ItemsUnavailable`.

**`OrderConflictCode`** (действия над заказом): `VersionMismatch`, `InvalidTransition`, `CancelNotAllowed`,
`AlreadyReady`, `InsufficientStock`, `LastItemCannotBeRemoved`, `InvalidQuantity`, `ProductUnavailable`.

**`CatalogConflictCode`**: `SlugTaken`, `SlugReserved`, `SlugInvalid`, `CategoryNotEmpty`, `UnitChangeNotAllowed`,
`ProductLimitReached`, `CategoryLimitReached`, `PhoneVerificationUnavailable`.

**`OrderProblemReason`** (строки корзины/правки): `NotFound`, `Unpublished`, `SoldOut`, `CategoryHidden`,
`InsufficientStock`, `BelowMinimum`, `InvalidQuantity`, `PriceChanged`.

Перечисления — append-only: новые значения только в конец; фронт на неизвестный `code` показывает `message`.

---

## §420. Статусы — тексты и действия (собирает сервер, `OrderTexts`)

| `status` | `statusText` | `availableActions` |
|---|---|---|
| `New` | Новый | Accept, Reject, Edit |
| `Accepted` | Принят | MarkReady, Cancel, Edit |
| `Ready` | Готов к выдаче | Issue, NotPickedUp, Cancel, Edit |
| `Issued` | Выдан | — |
| `Rejected` | Отклонён | — |
| `CancelledByCustomer` | Отменён покупателем | — |
| `CancelledByShop` | Отменён магазином | — |
| `NotPickedUp` | Не забран | — |

Статус передаётся **текстом** (доступность, SPEC §6), цвет — дополнительно.

---

## §421. Маршруты записи, отказывающие магазину — 409 строкой

Текст: `«Это магазин: записи, услуги и расписание для него недоступны.»`. Проверка — **после** прав.

`POST /api/services`, `PUT/DELETE /api/services/{id}`, `POST /api/services/{id}/image`;
`PUT /api/companies/{id}/members/{memberId}/provides-services|services|commission`;
`GET /api/companies/{id}/masters`, `GET /api/companies/{id}/stats`, `GET /api/companies/{id}/photo-usage`;
`POST /api/companies/{id}/photos`, `DELETE …/photos/{photoId}`, `PUT …/photos/order` (`GET …/photos` у магазина → `[]`);
пишущие маршруты `WorkingHoursController`, `POST /api/schedule-template/apply` и остальные пишущие шаблона;
`POST /api/bookings`; `GET /api/bookings/occupied|slots|availability`;
`GET /api/reports/masters`; `…/mail` (`MailingController`); `GET /api/masters/clients`, `POST /api/masters/clients/notes`;
все маршруты `CompanyNotificationsController`, `CompanyPushSettingsController`, `ClientConsentsController`;
`POST /api/notification-channels/{id}/companies` (назначение магазина на канал).
`GET /api/companies/{companyId}/reviews` у магазина → пустая страница (публичный список — не ошибка).

Обратное (маршруты заказов с id/slug салона) — **404**.

---

## §422. Персональные данные

### §422.1 `GET /api/profile/export` — секция `orders`

```json
"orders": [ {
  "shopName": "Шаурма на Ленина", "shopAddress": "…", "seller": { "legalName": "ИП …", "inn": "…" },
  "number": 27, "businessDate": "2026-10-05", "createdAtUtc": "…", "status": "Issued", "statusText": "Выдан",
  "customerName": "Иван", "customerPhone": "79001234567", "comment": "без лука",
  "items": [ { "name": "…", "unit": "Weight", "unitPrice": 540.00, "quantityOrdered": 500, "quantityActual": 512, "lineTotal": 276.48 } ],
  "total": 276.48, "reason": null, "source": "Account" } ]
```

`source`: `Account` (заказ аккаунта) или `GuestSamePhone` — гостевые на тот же номер, **только при подтверждённом
номере** (`SubjectScope`, гейт цикла 16); без подтверждения — пояснение в существующем поле `guestDataGate`.
Магазины, где у субъекта есть заказы, добавляются в существующий перечень операторов выгрузки (`operators`).

### §422.2 `POST /api/profile/delete-account`

Форма запроса/ответа не меняется. Правило «владеете компанией → 409» распространяется на магазины. Заказы аккаунта
и (под гейтом) гостевые на подтверждённый номер обезличиваются (имя, телефон, комментарий), учёт магазина цел.
`GET /api/profile/delete-account/preview` — **без изменений** (там только `trialRegistryNotice`).

### §422.3 Сроки хранения — [legal L5]

`Retention:OrderPersonalDataDays = 0` → правило `order-personalization` ничего не делает («срок не задан» в сводке
`GET /api/admin/scheduled-tasks`). API не меняется.

---

## §423. Что обязан фронт и чего он не должен

**Обязан:** брать типы из генерата `api-cycle23`; ветвиться по `code` в 409; печатать `message`/`statusText`/тексты
сервера как есть; слать `expectedVersion`; держать `idempotencyKey` до успеха; показывать `≈` при `isApproximate`;
показывать `problems` построчно; в строгом режиме вести на `/login?returnTo=…` без потери корзины; при
`phoneVerified: false` вошедшего покупателя встраивать существующий диалог подтверждения (сценарий «подтвердить
свой номер», `POST /api/phone-verification/sessions` с текущим номером) и после `Verified` повторять оформление;
заменять токен после `POST /api/shops`.

**Не должен:** вычислять доступность товара, допустимые действия над заказом, статусы и их подписи; показывать число
остатка покупателю (кроме `availableQuantity` при отказе); строить абсолютные ссылки на магазин/заказ сам (есть
`publicUrl`/`orderUrl`); слать `kind` из ezbook.

---

## §424. Строки 400/429 цикла (сводно)

| Где | Текст |
|---|---|
| тип в `?kind=` | Неизвестный тип компании |
| участник магазина | В магазин можно добавить только сотрудника. |
| создание магазина | Укажите название магазина · Укажите город магазина · Город не найден · Неизвестный часовой пояс |
| slug-check | Передайте адрес или название |
| реквизиты | ИНН указан с ошибкой |
| категории | Укажите название категории · Список категорий устарел — обновите страницу |
| товары | см. §410.2 · Список товаров устарел — обновите страницу · Остаток — целое число от 0 |
| quote | В корзине не больше 50 позиций · Товар в корзине повторяется |
| оформление | Укажите имя · Комментарий — не длиннее 500 символов · Товар в корзине повторяется · Подтвердите, что вы не робот · Проверка капчи не пройдена · Укажите телефон · Введите номер телефона в формате +7 (900) 000-00-00 |
| действия персонала | Причина — не длиннее 300 символов · Состав заказа указан с ошибкой · Укажите фактический вес каждой весовой позиции |
| 429 `order-create` | Слишком много заказов подряд — попробуйте через несколько минут |
| 429 лимит по телефону | Слишком много заказов на этот номер — дождитесь выдачи текущих или позвоните в магазин |
| 429 `storefront`/`order-public`/`order-board` | Слишком много запросов — подождите минуту |

---

## §425. Совместимость и приёмка контракта

**Ломающих изменений для ezbook — нет** в смысле формы: все поля добавочные. Поведенческие изменения — ровно те, что
требует SPEC (US-23-01…03, US-23-28): магазины исчезают из публичных списков и кабинета ezbook (`?kind=` по умолчанию
`Services`), маршруты записи отказывают магазину (§421), `/company/<slug>` магазина переадресует на goods.
Перечисления `CompanyKind`, `OrderStatus`, `OrderEventKind`, `OrderActorKind`, `ProductUnit`, `ShopCustomerMode`,
`OrderAcceptanceMode` хранятся числом — append-only.

**Приёмка (QA):**
1. `redocly lint` чистый; генерат `api-cycle23` совпадает с закоммиченным.
2. `schemathesis` по `contracts/cycle23/openapi.yaml` на живом бэкенде — без несоответствий схеме.
3. Матрица §421 (каждый маршрут × id магазина → 409 строка) и обратная (маршруты заказов × салон → 404).
4. Параллельный тест последней единицы, инвариант резерва, идемпотентность двойного `POST`.
5. `VersionMismatch` при одновременных действиях двух сотрудников (второй получает актуальный заказ, действие не
   применено).
6. Векторы `order-money-vectors.json` зелёные в обоих наборах.
7. Существующие наборы — ни одного красного, числа не ниже базовой линии цикла 22 (1558 / 815 / 640).
