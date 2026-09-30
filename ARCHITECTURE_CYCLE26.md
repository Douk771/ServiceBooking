# ARCHITECTURE — цикл 26 ServiceBooking: «Единая карточка компании» (ezbook.ru + goods.ezbook.ru)

**Разделы §543–§556** (A8: нумерация продолжает §542 из `API_CONTRACT_CYCLE25.md`; поиск по `*.md` пересечений не
нашёл). Контракт цикла — §557–§569.

**На входе:**
- `SPEC.md` цикла 26 (Q-26-1…Q-26-6 решены по колонке «Рекомендую»);
- `CURRENT_STATE.md` на `0d41df4`, блок 🪪26-вход;
- код ветки `cycle/026-company-card-unified` (= `develop` `0d41df4`), сверен по файлам, перечисленным ниже.

Ветку подготовил devops-инженер. Архитектор ветку не трогает. Первое действие SPEC §12 (архив спеки цикла 25)
выполняет не архитектор, файл `SPEC_CYCLE25_GOODS_ORDERS_INSIGHTS_MAX.md` в рабочем дереве уже есть.

| Файл | Что в нём | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE26.md` (этот) | решения, изменения DTO, структура, задачи, риски | все |
| `API_CONTRACT_CYCLE26.md` (§557–§569) | контракт словами: порядок проверок, тексты, правило публичного адреса | backend, frontend, QA |
| `contracts/cycle26/openapi.yaml` | **источник истины по форме** (OpenAPI 3.0.3): prism, `openapi-typescript`, schemathesis, redocly | backend, frontend, QA, CI |

Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` — документы цикла 3. По конвенции проекта они не перезаписываются.

**Стек, структура репозитория и решения циклов 1–25 сохраняются.** Цикл — расширение: новых сервисов, пакетов,
переменных окружения, маршрутов и миграций нет.

---

## §543. Итог решений — ответы на SPEC §7 (A1–A8) одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| — | Нужны ли колонки или миграции | **Нет.** Поля карточки у `Company` общие для обоих типов. Индекс `CompanyPhotos (CompanyId, Position)` есть. У `Orders` есть индексы с ведущим `CompanyId` для проверки «есть ли заказы» | §544 |
| A1 | Где живёт общий компонент | `frontend/src/components/company/CompanyCard.tsx`. Принимает **явные пропсы** `CompanyCardData`, от DTO не зависит. Слот модуля — `children`. Каждая страница переводит свой DTO в `CompanyCardData` локальной функцией | §550 |
| A1 | Одна ссылка на звонок | Общая `telHref` в `frontend/src/utils/phone.ts` получает семантику `dialHref`: всегда `tel:+цифры`, а ведущая `8` у 11-значного номера превращается в `+7`. `goods/src/utils/dial.ts` удаляется, пять мест вызова переходят на `telHref` | §549.2 |
| A2 | Фото в `StorefrontDto` | Новые поля `photos` (≤ 10) и `email`. Фото читаются **одним** индексным запросом и только для активного магазина. Общий хелпер `CompanyPhotoQueries.OrderedAsync` с тем же порядком, что у `GET …/photos`. В `ShopManageDto.photos` поле **не добавляется**: кабинет читает `GET …/photos`, как салон | §547 |
| A3 | Кеши фронта | `CompanyPhotosSection` получает необязательный `onChanged`. Салон ничего не передаёт, поведение прежнее (`['company']`). goods передаёт сброс `['storefront']` | §551 |
| A4 | Тексты «салон» | Сервер: `CompanyPhotoTexts.LimitReached(kind)` и `ReorderMismatch(kind)`, салонные строки байт в байт прежние. Фронт: пропс `kind: 'salon' \| 'shop'`, заголовок и пустое состояние по нему. `uploadError.ts` распознаёт оба текста лимита | §546, §551 |
| A5 | Смена города магазина | Заказ хранит `PickupStartUtc` (UTC), а текст времени получения **рисуется по текущему поясу магазина** (`OrderDtoMapper.ToPickup` → `PickupSchedule.PickupText(zone)`). Смена пояса сдвинула бы время у всех существующих заказов. Решение без миграции: **смена на город с другим смещением UTC разрешена только магазину без заказов**, на город с тем же смещением — всегда. Кабинет знает об этом заранее (`timeZoneChangeAllowed`), сервер отвечает 409. **US-26-07 остаётся в цикле** | §548 |
| A6 | Сохранение профиля goods | **Одна кнопка «Сохранить»** на весь профиль. Внутри два последовательных запроса: `PUT /api/companies/{id}` (все поля, кроме адреса), затем `PUT …/address` (только если адрес изменён, после уведомления о публичности). Состояние формы локальное и из перечитанного `ShopManageDto` не пересобирается | §552 |
| A7 | Функция публичного адреса | Только на фронте: `frontend/src/utils/publicAddress.ts`. Серверной копии нет: сервер «город + адрес» нигде не собирает. `{Адрес}` в салонных шаблонах уведомлений — сырой адрес владельца, не карточка | §549.1 |
| A8 | Нумерация | §543–§556 здесь, §557–§569 в контракте, `contracts/cycle26/` | — |

---

## §544. Стек, зависимости, миграции

| Потребность | Чем закрываем | Почему не новое |
|---|---|---|
| Галерея магазина | существующие `CompanyPhotosController`, `ImageUploadService`, `FileStorage` (публичная область `uploads/companies`), `CompanyPhotoOrdering`, advisory-lock `company-photos:{id}` | салонный конвейер уже типонезависимый, мешала только одна проверка `Kind` |
| Карусель | `CompanyPhotoGallery` (уже общий, `components/company/`) | — |
| Проверка ссылок карт | `MapLinkValidation` через `PUT /api/companies/{id}` | уже работает для магазина (🪪26-вход В) |
| Выбор города и подпись пояса | `CityCombobox`, `formatCityTimeZone`, `GET /api/cities` | goods уже импортирует `CityCombobox` на `CreateShopPage` |
| Смещение пояса | `TimeZoneOffset.TryGetUtcOffsetMinutes` | — |

**Новых npm- и NuGet-зависимостей нет. Миграций нет.** Проверено по коду:
- `Company` уже содержит `Email`, `LogoUrl`, `Address`, `CityId`, `YandexMapsUrl`, `TwoGisUrl`, `Photos`;
- `CompanyPhoto` связан FK `Cascade` на `Companies` и имеет индексы `(CompanyId, Position)` и уникальный
  `(CompanyId, ContentHash)`;
- `Orders` имеет индексы `IX_Orders_Report (CompanyId, PickupDate)` и `IX_Orders_CompanyId_CustomerPhone`, так что
  `AnyAsync(o => o.CompanyId == id)` идёт по индексу.

**Масштаб и продажа как сервиса.** Стоимость хостинга не меняется. Витрина делает на один индексный запрос больше (≤ 10
строк), число HTTP-запросов страницы не растёт. Все файлы лежат в той же публичной области, что и салонные, поэтому
будущий переезд в объектное хранилище затронет их одинаково.

---

## §545. Модель данных и DTO

**Сущности и таблицы не меняются.** Меняются только DTO (`ServiceBooking.API/DTOs/Shops/ShopDtos.cs`). Поля
**добавляются в конец** позиционных record, чтобы не сдвигать существующие вызовы:

```csharp
public record StorefrontDto(/* …все поля цикла 24 без изменений… */,
    string? Email, List<CompanyPhotoDto> Photos);            // цикл 26

public record ShopManageDto(/* …все поля цикла 24 без изменений… */,
    string? CityRegion, int? UtcOffsetMinutes, bool TimeZoneChangeAllowed, string? TimeZoneChangeLockedText); // цикл 26
```

`CompanyPhotoDto` (`DTOs/Companies/CompanyPhotoDto.cs`) переиспользуется как есть.

**Инварианты (проверяются тестами):**
1. `StorefrontDto.photos` упорядочены так же, как ответ `GET /api/companies/{id}/photos`, и у неактивного магазина
   всегда `[]`.
2. `StorefrontDto.email` равен `null` для пустой или пробельной строки.
3. `ShopManageDto.timeZoneChangeAllowed == !Orders.Any(CompanyId)`, а `timeZoneChangeLockedText` задан тогда и только
   тогда, когда `timeZoneChangeAllowed = false`.
4. После любого успешного `PUT /api/companies/{id}` у магазина `TimeZoneId` равен поясу его города (если город задан),
   а `TimeZoneIsManual = false`, если город или пояс трогали в этом запросе.
5. При 400/409 у `PUT /api/companies/{id}` ни одно поле тела не сохраняется: `SaveChanges` один, в конце.

---

## §546. Галерея магазина — сервер (T-26-01, US-26-01)

`ServiceBooking.API/Controllers/CompanyPhotosController.cs`:
1. Убрать три вызова `CompanyKindGuard.RejectShop(company.Kind)` в `Upload`, `Delete`, `Reorder`. Порядок
   «404 → права → остальное» не меняется.
2. Лимит: `return BadRequest(CompanyPhotoTexts.LimitReached(company.Kind))` вместо литерала.
3. Перестановка: в `catch (InvalidPhotoReorderException)` отвечать `BadRequest(CompanyPhotoTexts.ReorderMismatch(company.Kind))`.
   Сообщение исключения в `CompanyPhotoOrdering` остаётся прежним (его читают юнит-тесты).
4. `GetPhotos` читает через `CompanyPhotoQueries.OrderedAsync` (§547), поведение прежнее.

Новый чистый класс `ServiceBooking.API/Services/Companies/CompanyPhotoTexts.cs`:

```csharp
public static class CompanyPhotoTexts
{
    public static string LimitReached(CompanyKind kind);    // Services → «В галерее салона может быть не больше 10 фотографий»
                                                            // Orders   → «В галерее магазина может быть не больше 10 фотографий»
    public static string ReorderMismatch(CompanyKind kind); // «Список должен содержать все фотографии салона|магазина ровно по одному разу»
}
```

`CompanyKindGuard`: обновить комментарий-список мест вызова §389.2 (фото-маршруты убраны). Остальные салонные маршруты,
включая `photo-usage`, для магазина по-прежнему отвечают 409.

**Удаление магазина и аккаунта (SPEC T-26-01, сверено по коду).** Физического удаления компании в коде нет: нет ни
`Companies.Remove`, ни маршрута удаления. `AccountDeletionService` отказывает (`OwnsCompany`), пока за пользователем
числится компания, любого типа. Блокировка — это `IsActive = false`. Поэтому фото магазина живут и чистятся ровно
так же, как фото салона: удаляются по одному через `DELETE`. Если физическое удаление появится, FK `Cascade` удалит
строки, а файлы нужно будет чистить явно, одинаково для обоих типов (записано в риски §555.6).

**SuperAdmin.** `DELETE ?reason=DepictedPersonRequest` работает и для магазина. Уведомление
`PlatformNoticeTexts.BuildPhotoRemoved` говорит «компании» и правок не требует. Отдельного входа в админке к галерее
магазина нет: SuperAdmin открывает кабинет goods `/cabinet/:shopId/settings`, где `myRole = SuperAdmin` и `isOwner = true`.

---

## §547. Витрина — сервер (US-26-02, Q-26-6)

Новый хелпер `ServiceBooking.API/Services/Companies/CompanyPhotoQueries.cs`:

```csharp
public static class CompanyPhotoQueries
{
    /// Порядок — Position, CreatedAtUtc, Id (как GetPhotos). AsNoTracking. Индекс (CompanyId, Position).
    public static Task<List<CompanyPhotoDto>> OrderedAsync(AppDbContext db, Guid companyId, CancellationToken ct);
}
```

`StorefrontController.Get`:
- `var photos = shop.IsActive ? await CompanyPhotoQueries.OrderedAsync(db, shop.Id, ct) : [];` — до `Build`;
- в `Build` передать `Email: string.IsNullOrWhiteSpace(shop.Email) ? null : shop.Email`, `Photos: photos`.

Серверного кеша у витрины нет, поэтому правки видны сразу. Бюджет p95 (+50 мс) с запасом: один индексный запрос на ≤ 10
строк. `CompanyDtoAssembler` салона не трогаем, у него своя пакетная сборка фото.

---

## §548. Смена города магазина (US-26-07, A5, R26-1)

### §548.1 Что зависит от пояса (разбор кода)

| Данные | Как хранится | Что будет при смене пояса |
|---|---|---|
| `Order.PickupStartUtc/EndUtc` | UTC-момент | **текст «К 12:30» пересчитается в новом поясе** (`PickupSchedule.PickupText(kind, date, startUtc, ctx.Zone, …)`) — покупатель и персонал увидят другое время |
| `Order.PickupDate`, `BusinessDate` | местная дата | не изменятся, но разойдутся с временем, нарисованным в новом поясе |
| Отчёты, лист сборки, «сегодня» | по `PickupDate` и рабочему дню пояса | интервалы слотов уже оформленных заказов сдвинутся |
| Часы работы, особые дни, меню на дату | местное время и даты | **следуют за новым поясом** — это и есть нужное поведение |
| Очереди push и MAX | ключ — событие заказа | без заказов пусты |

Вывод: если у магазина есть заказы, смена пояса нарушает критерий SPEC («созданные заказы сохраняют дату и время»).
Без заказов менять нечего. Сохранять пояс в каждом заказе — это миграция и правка всех мест, где рисуется время, а SPEC
велит в таком случае резать историю. Поэтому историю не режем, а сужаем:

### §548.2 Правило

Чистый класс `ServiceBooking.API/Services/Shops/ShopTimeZoneChangePolicy.cs`:

```csharp
public static class ShopTimeZoneChangePolicy
{
    /// Allowed, если newZoneId == currentZoneId, ИЛИ смещения обоих поясов на nowUtc равны (оба распознаны), ИЛИ !hasOrders.
    public static bool IsAllowed(string currentZoneId, string newZoneId, bool hasOrders, DateTime nowUtc);
    /// Текст §566 с FormatOffset(текущего смещения); null-смещение → «UTC».
    public static string LockedText(int? currentOffsetMinutes);
    /// 420 → "UTC+7", 330 → "UTC+5:30", -180 → "UTC-3", 0 → "UTC+0".
    public static string FormatOffset(int minutes);
}
```

`CompaniesController.Update` — ветка **только для `company.Kind == CompanyKind.Orders`**, вставляется на месте
вызова `CompanyTimeZoneResolver.ForUpdate` (салон идёт прежним путём):
1. Если `dto.TimeZoneId.IsSpecified` со значением, и значение ≠ `city.TimeZoneId`, то
   `BadRequest("Часовой пояс магазина задаётся городом")`.
2. `newZone = (cityChanged || dto.TimeZoneId.IsSpecified) ? city.TimeZoneId : company.TimeZoneId`, при этом
   `TimeZoneIsManual = false`, если зону трогали.
3. Если `newZone != company.TimeZoneId`: `hasOrders = await db.Orders.AnyAsync(o => o.CompanyId == id)`, и если
   `!IsAllowed(...)`, то `Conflict(LockedText(currentOffset))` **до** `SaveChangesAsync`.
4. Иначе всё сохраняется, как сейчас.

`ShopManageMapper.BuildAsync`: вместо чтения одного `Name` читать `{ Name, Region }` города. `UtcOffsetMinutes` —
`TimeZoneOffset.TryGetUtcOffsetMinutes(shop.TimeZoneId, now)`. `hasOrders` — тот же `AnyAsync`, а
`TimeZoneChangeAllowed = !hasOrders`, `LockedText` при `false`. Это +1 индексный запрос на ответ `ShopManageDto`.

### §548.3 Пограничные случаи

- **Гонка.** Первый заказ может появиться между чтением кабинета и сохранением, поэтому сервер проверяет заново и
  отвечает 409. Оставшееся окно — между `AnyAsync` и `SaveChanges` того же запроса, миллисекунды. Первый заказ
  магазина, оформленный в ту же миллисекунду, когда владелец меняет город, мы принимаем как допустимый риск (§555.6).
  Advisory-lock на оформление заказа ради этого не вводится.
- **Каталог goods.** Список города кешируется на `Orders:CatalogCacheSeconds` = 30 с, поэтому магазин появится в новом
  городе и пропадёт из старого за ≤ 30 с, а не «сразу». Витрина обновляется сразу (§556 п. 4).
- **Магазин с ручным поясом** (создан через API с `timeZoneId`): при смене города пояс сбрасывается на пояс города по
  тому же правилу. UI goods ручного пояса не показывает.
- **Салоны не затронуты.** Правило про заказы для них бессмысленно, у салонов своя логика напоминаний цикла 4.

---

## §549. Общие функции фронта

### §549.1 `publicAddress` (US-26-05)

`frontend/src/utils/publicAddress.ts`, `export function publicAddress(cityName?: string | null, address?: string | null): string`.
Алгоритм и эталонная таблица — `API_CONTRACT_CYCLE26.md` §565. Юнит-тест `publicAddress.test.ts` покрывает таблицу
целиком (`it.each`). Поиск префиксов делается регулярным выражением `/^(город\s+|г\.\s*|г\s+)/` над нормализованной
строкой, а граница после города проверяется по `/^[\s,]|$/`. Места вызова перечислены в §550 и §556 п. 6.

### §549.2 Ссылка на звонок

`frontend/src/utils/phone.ts` → `telHref(raw)`:
- нет цифр → `''`;
- ведущий `+` → `tel:+{digits}`;
- 11 цифр, первая `8`, без `+` → `tel:+7{digits[1..]}`;
- иначе → `tel:+{digits}` (канонический номер хранится без плюса, так же его показывает `formatPhone`).

`frontend/goods/src/utils/dial.ts` и `dial.test.ts` удаляются, их случаи переносятся в `phone.test.ts`. Пять мест
(`StorefrontPage`, `OrderPage`, `CustomerPage`, `OrderCard`, `SpecialDays`) импортируют `telHref` из `@/utils/phone`.
Случай `telHref('79990000000')` в `phone.test.ts` **намеренно** меняется с `'tel:79990000000'` на `'tel:+79990000000'`:
старое поведение набирало номер как местный на части телефонов (так объяснено в `dial.ts`). Это исправление, а не
регресс.

---

## §550. Единая публичная карточка (US-26-04, A1)

`frontend/src/components/company/CompanyCard.tsx`:

```ts
export interface CompanyCardData {
  name: string
  description?: string | null
  logoUrl?: string | null
  phone?: string | null
  email?: string | null
  address?: string | null
  cityName?: string | null
  yandexMapsUrl?: string | null
  twoGisUrl?: string | null
  photos: CompanyPhoto[]            // CompanyPhoto из frontend/src/types — структурно равен CompanyPhotoDto cycle26
}
export function CompanyCard(props: { company: CompanyCardData; children?: ReactNode }): JSX.Element
export function CompanyCardSkeleton(): JSX.Element
```

**Вёрстка** (образец — нынешняя шапка `CompanyPage.tsx`, стр. 141–219):
1. Корень: `bg-white border border-line rounded-3xl p-2 overflow-hidden`. Отступ снизу задаёт страница.
2. `CompanyPhotoGallery` рендерится **только при `photos.length > 0`**. Сама галерея не меняется: её заглушка на
   220 px при пустом массиве остаётся для её собственного теста, но карточка её больше не вызывает.
3. Ряд `px-6 pb-6 pt-7 flex items-start gap-5 relative z-10` — **всегда** (защита §204 цикла 13 и тест `relative z-10`).
   Логотип или заглушка 64×64, `rounded-[18px] border-4 border-white shrink-0`. Класс `-mt-[52px]` добавляется
   **только при галерее**. Заглушка — первая буква названия (`name.trim()[0]?.toUpperCase()`), `bg-cream-deep
   text-gold-dark font-serif text-2xl`. Логотип `alt=""` (декоративный: название рядом в `h1`), у заглушки `aria-hidden`.
4. `h1` `font-serif text-[30px] font-medium text-ink break-words`, затем описание.
5. Контакты `flex flex-col items-start gap-2 text-[13.5px] text-gold-dark`, строго в порядке:
   телефон (`telHref` → `<a>` `min-h-[44px]`, иначе `<span>`, как §305.4 цикла 17), адрес (`publicAddress`, иконка
   `map-pin`, `break-words`), email (`<a href="mailto:…">`, иконка `mail`), `CompanyMapLinks` (отдельной строкой, не
   внутри адреса, §305.5). Пустое поле строки не оставляет.
6. `children` — слот модуля под контактами, в обёртке `mt-3.5 flex flex-col items-start gap-2`. Если детей нет, слот
   не рисуется.
7. Ширина 360 px: у текстовой колонки `min-w-0 flex-1`, у всех строк `break-words`, горизонтальной прокрутки нет.

`CompanyCardSkeleton`: тот же корень, внутри плашка 64×64 и три полосы `animate-pulse`. Используют обе страницы.

**ezbook `CompanyPage.tsx`:** шапка (стр. 140–220) заменяется на
`<CompanyCard company={toCardData(company)}>{!company.allowSelfBooking && <бейдж «Запись только через мастера»/>}</CompanyCard>`,
а скелетон `h-[280px]` — на `CompanyCardSkeleton`. `toCardData` — локальная функция страницы: `photos: company.photos ?? []`.

**goods `StorefrontPage.tsx`:** `<header>` (стр. 134–161) и блок `open-state` (стр. 163–180) заменяются на
`<CompanyCard company={toCardData(shop)}>` со слотом, в котором лежит **тот же** блок состояния: `data-testid="open-state"`,
текст `openState.text` и раскрывающиеся «Часы работы». Плашка «не принимает заказы», `PickupPicker`, каталог и
«Продавец» остаются на местах. Скелетон — `CompanyCardSkeleton` + `LoadingList`. Ветка «Магазин недоступен» карточку
не рисует.

**Тесты (R26-3).** `CompanyPage.test.tsx` остаётся зелёным без правок, кроме одного намеренного изменения: «shows a map
link next to the address» ждёт `'Барнаул, Ленина, 5'`. Новый `CompanyCard.test.tsx` покрывает:
- порядок контактов;
- отсутствие пустых строк;
- `-mt-[52px]` только при фото;
- заглушку буквой;
- слот;
- `mailto:`;
- отсутствие `<a href="">`.

`StorefrontPage.test.tsx` дополняется карусель при `photos`, email, «Барнаул, …» и тем, что `open-state` находится
внутри карточки.

---

## §551. Галерея в кабинете goods (US-26-01, A3, A4)

`frontend/src/pages/owner/CompanyPhotosSection.tsx`:

```ts
export function CompanyPhotosSection(props: {
  companyId: string
  kind?: 'salon' | 'shop'        // по умолчанию 'salon' — ezbook не меняется
  onChanged?: () => void         // вызывается в invalidate() ПОСЛЕ существующих сбросов
})
```

- Для `'shop'`: заголовок «Фотографии магазина», пустое состояние «В галерее магазина пока нет фотографий». Всё
  остальное как у салона: счётчик, «Обложка», «Сделать обложкой», стрелки, удаление, диалог SuperAdmin, drag-and-drop,
  `CompanyPhotoPeopleNotice` до выбора файла, ошибки под блоком.
- `frontend/src/utils/uploadError.ts`: условие лимита становится `/В галерее (салона|магазина) может быть не больше 10
  фотографий/`, текст сервера возвращается как есть.
- goods `SettingsPage`: сразу под «Профилем магазина» —
  `<CompanyPhotosSection companyId={shop.id} kind="shop" onChanged={() => qc.invalidateQueries({ queryKey: ['storefront'] })} />`.
- Сотрудник блок не видит: `settings` входит в `OWNER_ONLY_SEGMENTS`, а сервер отвечает ему 403.

---

## §552. Профиль магазина в кабинете (US-26-03, US-26-06, US-26-07; A6)

Новый компонент `frontend/goods/src/components/profile/ShopProfileSection.tsx` заменяет `ProfileSection` в
`SettingsPage.tsx`. Монтируется как `<ShopProfileSection key={shop.id} shop={shop} />`.

### §552.1 Раскладка (карточка «Профиль магазина»)

1. **Шапка:** логотип 64×64 (заглушка — буква, как в `CompanyCard`), заголовок «Профиль магазина», кнопка
   «Загрузить/Заменить логотип» и подпись «JPEG, PNG или WEBP, до 5 МБ» в одну строку (`flex-wrap` на узком экране).
   Логотип загружается сразу, как сейчас, и в «Сохранить» не входит.
2. **Основное:** «Название *», «Описание».
3. **Контакты:** `grid sm:grid-cols-2 gap-4`: «Телефон для покупателей» (`PhoneInput`) и «Email для покупателей» с
   подсказкой «Виден на странице магазина».
4. **Адрес и карты** (`fieldset` с `legend`): `grid sm:grid-cols-2`, в первой строке «Город» (`CityCombobox`) и
   «Адрес» (обычный `Input`, **не** `CompanyAddressField`), во второй — «Яндекс Карты» и «2ГИС» с плейсхолдерами
   салона. Под полем города — подсказка о поясе и «Часовой пояс: …». Одна подсказка на группу: «Адрес виден
   покупателям на странице магазина».
5. Кнопка «Сохранить», рядом `SavedNote` («Сохранено», `role="status"`). Ошибка без поля — `InlineError` под формой.

Все поля имеют `label`. Ошибки связаны с полем через `aria-describedby`, а у поля с ошибкой стоит `aria-invalid`.
DOM-порядок совпадает с визуальным, поэтому совпадает и порядок табуляции.

### §552.2 Состояние и сохранение (без потери ввода — урок цикла 13)

- Состояние — `useState` по полям, начальные значения берутся из `shop` **один раз** (компонент с `key={shop.id}`).
  Перечитанный `ShopManageDto` форму **не пересобирает**. `baseline` (ref) — значения на последнем успешном сохранении.
  «Изменено» считается как `value !== baseline`.
- Порядок при нажатии «Сохранить»:
  1. Пустое название → ошибка у поля, запроса нет.
  2. Город изменён и `city.utcOffsetMinutes !== shop.utcOffsetMinutes`:
     - при `!shop.timeZoneChangeAllowed` → `timeZoneChangeLockedText` у поля города, запроса нет;
     - иначе модальное подтверждение §566, «Отмена» прерывает сохранение.
  3. Адрес изменён → `PublicAddressNotice` (как `CompanyAddressField`), «Отмена» прерывает сохранение.
  4. `companiesApi.update(id, { name, description, phone, email, cityId? , yandexMapsUrl?, twoGisUrl? })`. Ссылки
     карт уходят только изменённые (`""` — очистка), `cityId` — только при смене, `timeZoneId` не уходит никогда.
  5. Если адрес изменён → `companyAddressApi.saveAddress(id, address)`.
  6. Успех → `baseline` = отправленные значения, сброс `['shop', id]`, `['my-shops']`, `['storefront']`,
     «Сохранено» на 2,5 с.
- Ошибки:
  - шаг 4, 400: `mapLinksFieldError` с поправкой §567 (если общий текст «Ссылка…» пришёл, а Яндекс в запросе не
    было, ошибка идёт к 2ГИС) → у поля ссылки; «Город не найден» → у города; остальное → под формой;
  - шаг 4, 409: текст у поля города;
  - шаг 4, 451 и прочее: `getGoodsErrorMessage`;
  - шаг 5, ошибка: у поля адреса «Не удалось сохранить адрес. Попробуйте ещё раз.», при этом `baseline` полей шага 4
    уже обновлён. Под формой — «Остальные изменения сохранены».
- `UpdateCompanyPayload` (`frontend/src/api/companies.ts`) получает `yandexMapsUrl?: string` и `twoGisUrl?: string`.
  Салон уже шлёт их, но тип их не содержал — это чистое расширение.

### §552.3 Тесты (`SettingsPage.test.tsx` / `ShopProfileSection.test.tsx`)

- Группы и подписи на месте.
- Нетронутые ссылки не отправляются, очищенная уходит как `""`.
- Ошибка 2ГИС показывается у 2ГИС.
- Ввод в адресе не теряется, когда сохранение завершилось, а `['shop']` перечитан.
- Уведомление об адресе появляется до записи.
- Смена города:
  - тот же пояс — без подтверждения;
  - другой пояс — подтверждение;
  - `timeZoneChangeAllowed=false` — текст и 0 запросов;
  - 409 — текст у города.
- Email: подпись «Email для покупателей».
- Блок фото `kind="shop"`.

### §552.4 Кабинет салона (US-26-08, P2, режется первым)

`CompanyManagePage.tsx` → `SettingsTab`: раскладка по группам §552.1 внутри существующей формы react-hook-form.
- Шапка с логотипом.
- Основное.
- Контакты: телефон | email.
- Адрес и карты: `CompanyAddressField` | ссылки.
- Отдельная группа «Запись»: горизонт, окно переноса, флаги.

`CityTimeZoneCard` и `CompanyPhotosSection` переставляются сразу под карточку настроек. **Код полей, валидация, тексты,
сохранение и тесты не меняются**: меняются только обёртки `fieldset`/`grid` и порядок JSX. Критерий готовности —
зелёные без правок тесты `CompanyManagePage*`, `CompanyPhotosSection.test.tsx` и `CompanyAddressField.test.tsx`.

---

## §553. Тесты и QA

Базовая линия прогона — не ниже **2382 / 1085 / 1077** (юнит, функциональные, vitest; `CURRENT_STATE.md` 📊25 З).

Бэкенд:
- `ServiceBooking.UnitTests/CompanyPhotoTextsTests.cs`: салонные строки байт в байт, строки магазина.
- `ServiceBooking.UnitTests/ShopTimeZoneChangePolicyTests.cs`:
  - тот же пояс;
  - другой ID с тем же смещением;
  - другое смещение без заказов и с заказами;
  - нераспознанный пояс;
  - `FormatOffset` для 420, 330, −180 и 0.
- `ServiceBooking.Tests/Tests/Cycle26CompanyCardTests.cs`, база — `Cycle24TestBase` (фабрика магазина и заказа уже
  есть). Кейсы `CY26-01…` покрывают перечень `API_CONTRACT_CYCLE26.md` §568 п. 3, в том числе «409 → ссылки карт из
  того же тела не сохранены» и «`photo-usage` для магазина по-прежнему 409».

Фронт:
- `publicAddress.test.ts`;
- `phone.test.ts` (объединённые случаи);
- `CompanyCard.test.tsx`;
- `CompanyPage.test.tsx` (одно изменение ожидания);
- `StorefrontPage.test.tsx`;
- `ShopProfileSection.test.tsx`;
- `CompanyPhotosSection.test.tsx` (+ `kind="shop"`, + `onChanged`);
- `uploadError` — текст магазина.

QA:
- `TEST_CATALOG.md`, раздел «Цикл 26», кейсы `CY26-*`;
- schemathesis по `contracts/cycle26/openapi.yaml`;
- ручной проход 360/768/1440 px обеих карточек и профиля;
- клавиатура и читалка экрана: карусель, подтверждение города, ошибки полей.

Регресс:
- карточка салона: галерея, z-index логотипа, телефон, карты;
- витрина: выбор времени, корзина;
- `Cycle23ShopsCatalogTests` CY23-04.

---

## §554. Структура проекта — что добавляется и меняется

```
ServiceBooking.API/
├── Controllers/
│   ├── CompanyPhotosController.cs     − RejectShop ×3; тексты через CompanyPhotoTexts; GetPhotos через CompanyPhotoQueries
│   ├── StorefrontController.cs        + photos, email
│   └── CompaniesController.cs         Update: ветка Kind = Orders (§548.2)
├── DTOs/Shops/ShopDtos.cs             StorefrontDto +Email +Photos; ShopManageDto +4 поля
└── Services/
    ├── Companies/CompanyPhotoTexts.cs      🆕 (чистый)
    ├── Companies/CompanyPhotoQueries.cs    🆕
    ├── Companies/CompanyKindGuard.cs       только комментарий §389.2
    ├── Shops/ShopTimeZoneChangePolicy.cs   🆕 (чистый)
    └── Shops/ShopManageMapper.cs           регион, смещение, hasOrders
ServiceBooking.UnitTests/   CompanyPhotoTextsTests.cs 🆕, ShopTimeZoneChangePolicyTests.cs 🆕
ServiceBooking.Tests/Tests/ Cycle26CompanyCardTests.cs 🆕

frontend/src/
├── components/company/CompanyCard.tsx (+ .test.tsx)     🆕 CompanyCard, CompanyCardSkeleton
├── utils/publicAddress.ts (+ .test.ts)                  🆕
├── utils/phone.ts (+ .test.ts)                          telHref — единая семантика
├── utils/uploadError.ts                                 текст лимита магазина
├── api/companies.ts                                     UpdateCompanyPayload + yandexMapsUrl/twoGisUrl
├── pages/CompanyPage.tsx (+ .test.tsx)                  шапка → CompanyCard
├── pages/owner/CompanyPhotosSection.tsx (+ .test.tsx)   kind, onChanged
├── pages/owner/CompanyManagePage.tsx                    P2: раскладка SettingsTab
└── types/api-cycle26.generated.ts                       🆕 генерат
frontend/goods/src/
├── components/profile/ShopProfileSection.tsx (+ .test.tsx)  🆕
├── pages/cabinet/SettingsPage.tsx                       ProfileSection → ShopProfileSection; + CompanyPhotosSection
├── pages/StorefrontPage.tsx (+ .test.tsx)               header/open-state → CompanyCard + слот
├── pages/CatalogHomePage.tsx, pages/OrderPage.tsx       publicAddress
├── pages/cabinet/CustomerPage.tsx, components/orders/OrderCard.tsx, components/hours/SpecialDays.tsx   dialHref → telHref
├── utils/dial.ts, utils/dial.test.ts                    ❌ удаляются
└── types.ts                                             StorefrontDto, ShopManageDto, CompanyPhotoDto ← cycle26
frontend/package.json        "types:api:cycle26"
contracts/cycle26/openapi.yaml 🆕 ; contracts/redocly.yaml (комментарий-список) ; .github/workflows/ci.yml
```

`contracts/cycle23/goods-routes.json` не меняется: новых SPA-маршрутов нет.

---

## §555. Разбивка работ

Контракт готов **до** кода. Фронт стартует в первый день на `npx @stoplight/prism mock contracts/cycle26/openapi.yaml --port 4026`.

### §555.1 Backend

| # | Задача | Зависит от | Параллельно с |
|---|---|---|---|
| **BE-1** Галерея | `CompanyPhotoTexts` + юнит-тесты; снять `RejectShop` ×3; тексты по типу; комментарий `CompanyKindGuard`; функциональные тесты галереи магазина и регресс CY23-04 | — | BE-2, BE-3 |
| **BE-2** Витрина | `CompanyPhotoQueries` (+ перевод `GetPhotos` на него), `StorefrontDto` + `Email`/`Photos`, тесты витрины | — (правит `CompanyPhotosController.GetPhotos`, а BE-1 — три других метода того же файла: **BE-1 мерджится первым**, конфликт механический) | BE-1, BE-3 |
| **BE-3** Город магазина | `ShopTimeZoneChangePolicy` + юнит-тесты; ветка в `CompaniesController.Update`; `ShopManageDto` +4 поля в `ShopManageMapper`; тесты смены города и регресс салона (`CompaniesTests` цикла 4) | — (`ShopDtos.cs` правят BE-2 и BE-3, разные record — конфликт механический) | BE-1, BE-2 |
| **BE-4** Документы | `API_DOCUMENTATION.md`: фото в витрине, снятие 409, новые поля `ShopManageDto`, 400/409 `PUT /api/companies/{id}` для магазина; сверка формы ответов с `contracts/cycle26` | BE-1…BE-3 | — |

Правила для всех: сборка с `-warnaserror`; перед заявлением о готовности — `grep` классов §554 («зелёный прогон ≠
функционал»).

### §555.2 Frontend

| # | Задача | Зависит от | Параллельно с |
|---|---|---|---|
| **FE-0** | `types:api:cycle26` + генерат; `goods/src/types.ts` переводит `StorefrontDto`, `ShopManageDto`, `CompanyPhotoDto` на cycle26 | — | всё |
| **FE-1** | `publicAddress` + тест; единый `telHref`, удаление `dial.ts`, пять импортов, перенос тестов | — | всё |
| **FE-2** | `CompanyCard` + `CompanyCardSkeleton` + тест; `CompanyPage` на карточке; правка одного ожидания в `CompanyPage.test.tsx` | FE-1 | FE-4, FE-5 |
| **FE-3** | `StorefrontPage` на карточке со слотом состояния и часов; тесты витрины; `CatalogHomePage` и `OrderPage` через `publicAddress` | FE-0, FE-1, FE-2 | FE-4, FE-5 |
| **FE-4** | `CompanyPhotosSection` (`kind`, `onChanged`) + тесты; `uploadError` | — | всё |
| **FE-5** | `ShopProfileSection`: раскладка, ссылки карт, email, единое сохранение, адрес с уведомлением; блок фото в `SettingsPage` | FE-0, FE-4 (блок фото) | FE-2, FE-3 |
| **FE-6** (P1) | город в профиле: `CityCombobox`, подсказка пояса, подтверждение, запрет по `timeZoneChangeAllowed`, 409 | FE-5 | FE-2, FE-3 |
| **FE-7** (P2) | раскладка `SettingsTab` салона | — (правит только `CompanyManagePage.tsx`) | всё |

Backend и frontend не блокируют друг друга: весь фронт работает на prism-моке. Интеграционная проверка идёт после
BE-1…BE-3 и FE-3/FE-5/FE-6.

### §555.3 DevOps

| # | Задача | Когда | Готово, если |
|---|---|---|---|
| **DO-1** | CI: redocly lint `../contracts/cycle26/openapi.yaml` (шаг `ci.yml` + комментарий-список в `contracts/redocly.yaml`); `npm run types:api:cycle26` и `src/types/api-cycle26.generated.ts` в `git diff --exit-code` | сразу | CI зелёный; порча `$ref` в cycle26 краснит CI |
| **DO-2** | `DEPLOY.md` §23: короткая запись «Цикл 26 — без миграций, переменных и правок nginx». Смоук стенда после выката: `GET https://$GOODS_HOST/api/storefront/<тестовый slug>` = 200 и тело содержит `"photos"` | до мерджа / после выката | запись есть; смоук вручную зелёный |

Nginx, `docker-compose*`, `.env*` в этом цикле не меняются: загрузки уже идут через `/api/`, файлы отдаются из
`/uploads/` тем же `location`, что и у салонов на обоих хостах. **Ручных шагов на сервере нет.**

### §555.4 Точки синхронизации

| Что | Где |
|---|---|
| форма DTO | `contracts/cycle26/openapi.yaml` (генерат — единственный источник типов goods) |
| тексты 400/409, правило адреса | `API_CONTRACT_CYCLE26.md` §560.2, §565, §566 |
| тексты подписей интерфейса | §566 (для фронта) |

### §555.5 Порядок урезания (R26-2) и выкат

- Сначала режется US-26-08 (FE-7), затем US-26-07 (FE-6; серверная часть BE-3 **остаётся**, потому что закрывает
  обход через API). P0 не режутся. Форма контракта от урезания не меняется.
- Выкат: обычный деплой, миграций нет. Фронт цикла 25 в окне выката работает: лишние поля он игнорирует. Порядок
  «API → фронт» не важен.

### §555.6 Риски

| # | Риск | Решение |
|---|---|---|
| R-1 (R26-1) | Смена пояса сдвигает время существующих заказов | запрет при наличии заказов (§548); проверка на сервере и заранее в UI; тест «409 → ничего не сохранено» |
| R-2 | Окно гонки «первый заказ ↔ смена города» | миллисекунды внутри одного запроса; последствия — сдвиг текста времени у одного заказа, исправляется обратной сменой города; принято |
| R-3 (R26-3) | Регресс карточки салона: z-index, телефон, карты | `relative z-10` всегда, `-mt-[52px]` при галерее; существующие тесты без правок, кроме одного ожидания; `CompanyCard.test.tsx` |
| R-4 | Видимые изменения у **всех** салонов: пропадает заглушка галереи 220 px у салонов без фото, в адресе появляется город | так требует SPEC (US-26-04 п. 2–3, US-26-05); записано в отклонениях §556 п. 3, чтобы заказчик увидел |
| R-5 (R26-4) | Дубль города у адресов, не попавших под правило | правило простое и покрыто таблицей; владелец правит адрес сам |
| R-6 | Потеря ввода в профиле при перечитывании | локальное состояние с `key={shop.id}`, без пересборки из DTO; тест |
| R-7 | Каталог goods видит смену города с задержкой ≤ 30 с | TTL кеша списка города; принято, §556 п. 4 |
| R-8 | Если появится физическое удаление компании, файлы галереи не удалятся (FK удалит только строки) | сейчас пути удаления нет ни у одного типа; при появлении — общая чистка для обоих типов |
| R-9 | Правовые L21/L22 (фото людей в магазине, публичный email) | до заключения юриста — утверждённые тексты; goods остаётся стендом |

---

## §556. Отклонения от буквы SPEC и решения сверх неё (читать обязательно)

1. **US-26-07 сужена, а не вырезана.** Магазин с заказами может сменить город только на город с тем же смещением
   UTC. Магазин без заказов меняет город свободно, с подтверждением. SPEC требует сохранить время уже созданных
   заказов, а иначе это невозможно без миграции. **Это видимое продуктовое ограничение — сообщить заказчику.**
2. **Новое серверное ограничение:** `timeZoneId` для магазина, отличный от пояса города, даёт 400. Смена города у
   магазина сбрасывает ручной пояс. Без этого проверку §548 можно обойти через API.
3. **У салонов без фото пропадает серая заглушка галереи 220 px**, логотип больше не наезжает. Это прямое требование
   US-26-04 (п. 2–3). SPEC §6 называет это изменением вида карточки салона, здесь оно названо явно.
4. **«Сразу отражается в каталоге goods»** на деле означает ≤ 30 с (кеш списка города цикла 25). Витрина обновляется
   сразу.
5. **Каталог goods:** город в строке адреса показывается, как и раньше, только без фильтра по городу (`showCity`). В
   этом случае строку собирает `publicAddress`, то есть с защитой от дубля.
6. **Сверх SPEC:** блок магазина на странице заказа goods (`OrderPage.tsx`) тоже собирает адрес через `publicAddress`.
   Там тот же дубль «Барнаул, Барнаул», а правка — одна строка.
7. **Ссылка на звонок:** единая `telHref` с семантикой `dialHref` (§549.2). Один случай в `phone.test.ts` меняет
   ожидание намеренно.
8. **Логотип в карточке декоративный** (`alt=""`): название стоит рядом в `h1`, и читалка не должна слышать его
   дважды. SPEC разрешает оба варианта.
9. **`ShopManageDto.photos` не добавляется** (A2): кабинет читает галерею тем же `GET`, что и салон.
10. **Сохранение профиля goods:** одна кнопка на весь профиль. Адрес перестаёт иметь свою кнопку в goods (у салона
    `CompanyAddressField` остаётся как есть), но по-прежнему пишется своим маршрутом и с уведомлением о публичности.
