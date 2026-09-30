# ARCHITECTURE — цикл 32 ServiceBooking: компактные настройки салона на общем блоке «Профиль компании»

**Разделы §32.0–§32.16**, контракт — `API_CONTRACT_CYCLE32.md` §32.20–§32.29. Нумерация с префиксом цикла, как у
циклов 29 и 31. В коде и документах ссылаться с именем файла: `ARCHITECTURE_CYCLE32.md §32.4`.

**На входе:**
- корневой `SPEC.md` цикла 32 (Q-32-1…Q-32-12 приняты автономно по колонке «Решение»);
- `CURRENT_STATE.md` §5.8 (guard общих исходников goods), §5.9 (фактическая раскладка настроек обоих кабинетов), §6;
- код ветки `cycle/032-salon-settings-compact-layout` (= `develop` `49f0d60`), сверен по файлам, названным ниже.

Ветку подготовил devops-инженер, архитектор её не трогает.

| Файл | Что в нём | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE32.md` (этот) | решения, интерфейсы компонентов, раскладка, тесты, задачи, риски | все |
| `API_CONTRACT_CYCLE32.md` (§32.20–§32.29) | как фронт зовёт существующие маршруты: состав тел, порядок, тексты ошибок, гарантии сервера | frontend, backend (тесты), QA |
| `contracts/cycle32/openapi.yaml` | **источник истины по форме** (OpenAPI 3.0.3): две непересекающиеся формы тела `PUT /api/companies/{id}` | frontend (`openapi-typescript`, prism), backend (`OpenApiContract`), QA (schemathesis), CI (redocly) |

Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` — документы цикла 3, по конвенции (`CURRENT_STATE.md` §6.5, §10.5) не
перезаписываются: документы цикла лежат в корне с суффиксом.

---

## §32.0. Что это за цикл для архитектуры

Цикл фронтовый: общий компонент, перекомпоновка вкладки, одна новая карточка. Сервер **не меняется** — ни кода, ни DTO,
ни маршрутов. SPEC R-1 разрешал назвать правку API, если без неё не обойтись. Разбор (§32.3) показал: обходимся.

Три места, где цена ошибки высокая, и что с ними делаем:
1. **Регрессия goods.** Профиль магазина переезжает на общий компонент. Защита: goods-обёртка `ShopProfileSection`
   остаётся по тому же пути, с тем же экспортом и пропсами. Поэтому `ShopProfileSection.test.tsx` (262 строки, 17 `it`)
   и `SettingsPage.test.tsx` проходят **без единой правки файла**. Разметка переносится как есть (§32.4.3).
2. **Уход с react-hook-form у салона.** Урок цикла 13 — фоновое перечитывание `['my-companies']` не должно стирать
   набранный текст. Защита: состояние карточек локальное, инициализируется один раз при монтировании с
   `key={company.id}`, как у goods. Тест V32-11 — преемник теста цикла 13.
3. **Правда в текстах про часовой пояс.** Тексты модалки и подсказки сверены с кодом (§32.6.1). Предположение спеки
   «правила применяются к новым записям» по коду **неверно**, строка-пояснение переписана (§32.8.2).

Бэкенд-разработчик в цикле пишет только **тесты**: они доказывают гарантии сервера, на которых стоит план — частичное
обновление, телефон байт в байт, тексты 400 (CY32-01…05, CY32-10…12). Продуктового серверного кода нет.

---

## §32.1. Итог решений одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| — | Стек, зависимости, миграции | **Без изменений.** Ни NuGet, ни npm, ни миграций | §32.2 |
| R-1 | Хватает ли `Company` для общего блока | **Да, правки DTO нет.** Есть `cityRegion`, `utcOffsetMinutes`, `timeZoneIsManual`. Пробел один: при ручном поясе неизвестен пояс самого города. Обходится текстом модалки без числа (§32.6), не правкой DTO | §32.3 |
| Q-32-1 / US-32-01 | Один блок на два кабинета | `src/components/company/CompanyProfileCard.tsx`: вёрстка и цепочка сохранения. Различия — пропсы: `texts`, `errorMessage`, `onChanged`, `timeZoneLock` (магазин), `manualTimeZone` и `legacyPhoneHint` (салон). Данные — нейтральный `CompanyProfileSnapshot`, который собирают тонкие адаптеры: goods `ShopProfileSection` (прежний путь и экспорт) и ezbook `SalonProfileSection` (новый) | §32.4, §32.5 |
| R-2 | Что зависит от пояса салона | ✔ по коду: срок переноса и отмены (сразу) и момент напоминаний (для записей, созданных или перенесённых после смены). **Не** зависят: время уже созданных записей и рабочие часы (хранятся как местное время), «сегодня» в слотах (считается по UTC). Уже поставленные в очередь напоминания не пересчитываются | §32.6.1 |
| Q-32-5 | Подтверждение смены пояса у салона | Чистая функция `planProfileZone` решает, что отправить и нужна ли модалка. Для магазина сводится ровно к текущему правилу цикла 29 (сравнение смещений) | §32.6 |
| R-3 | Два частичных PUT одного ресурса | ✔ сервер пишет только присланные поля, 400 — до сохранения, EF обновляет только изменённые колонки. Формы тел профиля и правил не пересекаются, это закреплено схемой (`oneOf`, `additionalProperties: false`). Доказательство — CY32-01/02 | §32.8, контракт §32.27.1 |
| R-4 | Маска телефона и старые номера | ✔ сервер не нормализует `Phone`. `PhoneInput` не зовёт `onChange` при монтировании, нетронутый номер уходит как был. Для салона с номером не в каноне — подсказка «сейчас сохранён номер «…»» | §32.7 |
| Q-32-4 / US-32-03 | Правила записи | `src/pages/owner/BookingRulesSection.tsx`: локальное состояние, своя кнопка «Сохранить правила», тело — только поля правил. Заблокированные тарифом флажки не отправляются (как сейчас) | §32.8 |
| Q-32-7…9 / US-32-04 | Раскладка вкладки | Колонка `max-w-[760px] flex flex-col gap-5`, порядок Q-32-7, все заголовки — `h2` в одном стиле `CARD_TITLE_CLASS`. Галерея и каталог получают необязательные пропсы уровня и класса заголовка, умолчания = как в goods сейчас | §32.9 |
| US-32-05 (P2) | Поля страницы на телефоне | `px-8` → `px-4 sm:px-8` у корня `CompanyManagePage` | §32.9.4 |
| R-5 | Смена ожиданий старых тестов | Явная таблица «старый тест → новый V32» (§32.12.2). Ни один тест не удаляется молча | §32.12 |
| R-6 | Нет браузерного e2e | Ручные `M32-01…09`, скриншоты goods «до» снимаются со `develop` до влития FE-2 | §32.12.4 |
| — | Машиночитаемый контракт | `contracts/cycle32/openapi.yaml`: существующие маршруты и две формы тела. Фронт строит тела с `satisfies` сгенерированных типов | §32.11 |

---

## §32.2. Стек, зависимости, миграции

**Без изменений** (`CURRENT_STATE.md` §1): .NET 8 / ASP.NET Core / EF Core 8 / PostgreSQL 16; React 18 + Vite 5 +
Tailwind 3.4 + react-query 5; Vitest 3; openapi-typescript 7.13; @redocly/cli 2.54.

| Потребность | Не берём | Берём | Почему |
|---|---|---|---|
| Форма профиля салона | react-hook-form (как сейчас в `SettingsTab`) | локальный `useState` + `baseline` в `useRef`, как в `ShopProfileSection` | Один компонент на два кабинета; у goods RHF нет. Прецедент цикла 26 уже доказал, что «инициализация один раз» закрывает урок цикла 13 без обходов `keepDirtyValues` |
| Смещение ручного IANA-пояса на клиенте | библиотеки часовых поясов (luxon, date-fns-tz) | `Intl.DateTimeFormat(..., { timeZone, timeZoneName: 'longOffset' })` в одной функции `utcOffsetMinutesOf` | Нужно только для решения «показать ли модалку». Сервер остаётся единственной проверкой пояса |
| Сверка тел запросов с контрактом | ajv в vitest | `openapi-typescript` + `satisfies` (`tsc`), prism mock | Инструменты уже в проекте и в CI |
| e2e вёрстки | Playwright | ручные `M32-` (Q-32-12) | Решение спеки |

Миграций нет. Сущности и таблицы не меняются.

---

## §32.3. Данные вкладки и разбор R-1

Вкладка читает только `GET /api/companies/my` (`['my-companies']`, `CompanyDto`). Новых запросов нет (NFR). Нужные
поля перечислены в контракте §32.21. У `Company` (`frontend/src/types/index.ts:43`) есть всё, что нужно блоку:

| Нужно блоку | У магазина (`ShopManageDto`) | У салона (`Company`) |
|---|---|---|
| город для `CityCombobox` | `cityId`, `cityName`, `cityRegion` | `cityId`, `cityName`, `cityRegion` ✔ |
| действующий пояс и его смещение | `timeZoneId`, `utcOffsetMinutes` (может быть `null`) | `timeZoneId`, `utcOffsetMinutes` (всегда число, `CompanyDtoAssembler.cs:89`) ✔ |
| ручной ли пояс | — (у магазина не бывает) | `timeZoneIsManual` ✔ |
| блокировка смены пояса | `timeZoneChangeAllowed`, `timeZoneChangeLockedText` | — (у салона блокировки нет, сервер её не делает) ✔ |

**Единственный пробел:** при ручном поясе DTO не содержит пояс самого города. `timeZoneId`/`utcOffsetMinutes`
описывают **действующий** пояс. Пробел мешает в одном сценарии: владелец выключает ручной пояс и не выбирает город
заново. Тогда клиент не знает, на какое смещение уйдёт салон.

Варианты:
- (А) добавить в `CompanyDto` поля `cityTimeZoneId`/`cityUtcOffsetMinutes`. Аддитивно и дёшево, но это правка
  сервера, контракта и генератов ради одного крайнего сценария;
- (Б) запросить город через `citiesApi.search`. Это новый запрос (нарушает NFR «вкладка не делает новых запросов»);
- **(В, выбрано)** в этом сценарии показать модалку «Сменить город?» всегда, с текстом «было UTC+6 → пояс города
  Барнаул» без числа (§32.6). Модалка честная: смещение действительно может измениться, а число клиент не знает.

Побочный факт: сейчас `CityTimeZoneFields` кладёт в объект города `timeZoneId` компании. У салона с ручным поясом
строка «Часовой пояс: Барнаул → UTC+6, Asia/Omsk» поэтому ложная: ручной пояс выдаётся за пояс города. Новая строка
пояса (§32.6.3) эту неточность убирает.

---

## §32.4. Общий блок — `frontend/src/components/company/CompanyProfileCard.tsx` (US-32-01)

### §32.4.1 Где лежит и что может импортировать

- `src/components/company/` сканируют **оба** конфига Tailwind. Guard `goods/src/sharedSources.guard.test.ts` пропускает
  файл без правок `goods-shared-sources.js`.
- Импорты только относительные (C26-2): `../../api/companies`, `../../api/companyAddress`,
  `../ui/{Button,Card,CityCombobox,Input,Modal,PhoneInput,InlineError}`, `./PublicAddressNotice`, `./profileZone`,
  `../../utils/{companyManageError,mapLinksFieldError,timezone,httpError}`, `../../types`.
- **Запрещено:** любой импорт из `frontend/goods/`, `ShopManageDto`, `getGoodsErrorMessage`, goods `StatePanels`.
  ESLint это уже ловит для `src/**`.
- Моки goods-тестов (`vi.mock('@/api/companies')`, `'@/components/ui/CityCombobox'` и т. д.) продолжают действовать:
  vitest сопоставляет модуль по разрешённому пути, а относительный импорт из `src/components/company/` разрешается в тот
  же файл. Это проверяется первым же прогоном `ShopProfileSection.test.tsx` в FE-2.

### §32.4.2 Интерфейс

```ts
import type { City } from '../../types'

/** Neutral data the card needs — built by a thin adapter per product (ShopManageDto / Company). */
export interface CompanyProfileSnapshot {
  id: string
  name: string
  description: string | null
  phone: string | null
  email: string | null
  logoUrl: string | null
  address: string | null
  yandexMapsUrl: string | null
  twoGisUrl: string | null
  /** CityCombobox value; the adapter builds `label` ("Город, Регион"). */
  city: City | null
  /** The company's EFFECTIVE zone now (manual if manual). offsetMinutes null = unknown. */
  zone: { id: string | null; offsetMinutes: number | null; isManual: boolean }
}

export interface CompanyProfileTexts {
  title: string          // h2
  logoAlt: string
  nameRequired: string
  phoneLabel: string
  emailLabel: string
  emailHint: string
  cityHint: string
  addressHint: string
  saveFailed: string     // fallback for errorMessage()
  timeZoneChange: (from: string, to: string) => string
}

/** Shop only (API_CONTRACT_CYCLE29.md §29.24): the flag forbids only a DIFFERENT UTC offset. */
export interface TimeZoneLock { allowed: boolean; lockedText: string | null; fallbackText: string }

export interface CompanyProfileCardProps {
  /** Header (logo, initial) reads it live; form fields read it ONCE at mount — mount with key={company.id}. */
  company: CompanyProfileSnapshot
  texts: CompanyProfileTexts
  /** Form-level error text: goods getGoodsErrorMessage, ezbook getCompanyManageErrorMessage. */
  errorMessage: (error: unknown, fallback: string) => string
  /** Cache invalidation after logo upload, full success, and address failure (the profile PUT did land). */
  onChanged: () => void
  /** Shop only. When set, a 409 of the profile PUT goes to the city field. */
  timeZoneLock?: TimeZoneLock
  /** Salon only: «Указать часовой пояс вручную» + IANA field (§32.6). */
  manualTimeZone?: boolean
  /** Salon only: hint when the stored phone is not canonical 7XXXXXXXXXX (§32.7). */
  legacyPhoneHint?: boolean
}
export function CompanyProfileCard(props: CompanyProfileCardProps): JSX.Element
```

Компонент не получает `useQueryClient`: инвалидации — забота адаптера (`onChanged`). Эффектов, зависящих от
`company`, нет: адаптеры создают снапшот заново на каждом рендере, а форма перечитывать его не должна.

### §32.4.3 Разметка — переносится из `ShopProfileSection.tsx` как есть

Строки 216–374 текущего `ShopProfileSection.tsx` переносятся в карточку без изменения классов, порядка и ролей. Тексты
заменяются на `texts.*`, `shop.*` — на `company.*`, `InlineError` — на общий `../ui/InlineError`. Добавления — только
под флагами салона, у goods их нет в DOM:

| Место | Магазин (без флагов) | Салон |
|---|---|---|
| шапка | как сейчас | то же, `alt={texts.logoAlt}` = «Логотип салона» |
| под `PhoneInput` | ничего | при `legacyPhoneHint` и нетронутом старом номере — `<p id=… className="text-xs text-muted">` (§32.7), связан через `aria-describedby` |
| под сеткой «Город \| Адрес» | ничего | при `manualTimeZone` — чекбокс «Указать часовой пояс вручную (IANA, например Asia/Barnaul)» (`w-4 h-4 accent-gold`, как сейчас) и при включённом — `<Input label="Часовой пояс (IANA)" placeholder="Asia/Barnaul" className="font-mono" error={errors.timeZone} />` на всю ширину. Чекбокс `disabled`, пока город не выбран (контракт §32.27.3) |
| строка «Часовой пояс: …» | `formatCityTimeZone(...)`, как сейчас | правила §32.6.3 |

Мелкие правки доступности (без визуальных изменений, в обоих кабинетах):
- подсказка под городом получает `id`, и `CityCombobox` связывает с ней поле. Для этого у `CityCombobox` новый
  необязательный проп `describedBy?: string`: он сливается с id ошибки в `aria-describedby`. Без пропа всё как сейчас;
- «Сохранено» остаётся `role="status"`.

У IANA-поля появляется подпись: сейчас это голый `<input>` без label, и это нарушает NFR «у каждого поля есть
подпись».

### §32.4.4 Состояние (урок цикла 13)

Как в `ShopProfileSection` сейчас: `useState(() => initialValues(company))`, `useState(() => company.city)`, `baseline`
в `useRef`. Для салона добавляются `manual` (`useState(company.zone.isManual)`) и `manualZoneId`
(`useState(company.zone.isManual ? company.zone.id ?? '' : '')`), а в `baseline` — `zone`. Кнопка «Сохранить» активна
всегда (Q-32-11). «Сохранено» держится 2,5 с, таймер чистится при размонтировании.

### §32.4.5 Цепочка сохранения (контракт §32.24)

1. `begin()`: пустое `name.trim()` → `errors.name = texts.nameRequired`, стоп.
2. `plan = planProfileZone(baseline.zone+cityId, {city, manual, manualZoneId}, !!manualTimeZone, utcOffsetMinutesOf)`.
3. `plan.confirm && timeZoneLock && !timeZoneLock.allowed` → `errors.city = lockedText ?? fallbackText`, стоп.
   `plan.confirm` → модалка «Сменить город?» с `texts.timeZoneChange(from, to)`. «Отмена» → стоп, «Сменить город» →
   шаг 4.
4. Адрес изменён (`trim` отличается от baseline) → `PublicAddressNotice`, «Отмена» → стоп.
5. `run()`: тело по контракту §32.22, собранное с `satisfies C32['schemas']['CompanyProfileUpdateInput']`
   (`cityId`/`timeZoneId` берутся из `plan`) → `companiesApi.update`. Ошибка → раскладка §32.4.6, стоп.
6. `baseline` ← отправленные значения (город и пояс — по `plan`).
7. Адрес изменён → `companyAddressApi.saveAddress(id, values.address)`. Ошибка → `errors.address = ADDRESS_FAILED`,
   `formError = 'Остальные изменения сохранены'`, `onChanged()`, стоп.
8. `saved = true` на 2,5 с, `onChanged()`.

### §32.4.6 Раскладка ошибок основного PUT

`status` и `raw` (строковое тело, обрезанное) берутся новым `src/utils/httpError.ts`: `httpStatusOf(err)`,
`plainErrorBody(err)`. Для магазина это ровно то, что сейчас дают `httpStatus` и `getGoodsErrorMessage` на 400.

| # | Условие | Куда | Текст |
|---|---|---|---|
| 1 | `status === 409 && timeZoneLock` | город | `errorMessage(err, texts.saveFailed)` |
| 2 | `status === 400 && raw.includes('Город не найден')` | город | `raw` |
| 3 | `status === 400 && manualTimeZone && raw === 'Неизвестный часовой пояс'` | IANA-поле | `raw` |
| 4 | `status === 400`, `mapLinksFieldError(raw)` — `yandexMapsUrl`/`twoGisUrl` (+ правило §567 цикла 26: общий «Ссылка…» идёт к 2ГИС, если Яндекс не отправляли) | поле ссылки | `raw` |
| 5 | иначе | под формой, `InlineError` | `errorMessage(err, texts.saveFailed)` |

Почему маршрутизация по `raw`, а не по тексту маппера: ezbook-маппер `getCompanyManageErrorMessage` на любой 400
отвечает общей фразой, и тогда «Город не найден» ушёл бы под форму. У goods на 400 с телом `raw` и текст маппера
совпадают, поэтому для магазина поведение не меняется. Строки 2–3 закреплены функциональным тестом CY32-05.

### §32.4.7 Общий `InlineError` — `frontend/src/components/ui/InlineError.tsx` (новый)

Разметка — дословно из goods `StatePanels.InlineError`:
`<div role="alert" className="bg-danger-bg text-danger text-sm px-4 py-2.5 rounded-xl">`. goods `StatePanels.tsx`
вместо своей функции делает `export { InlineError } from '@/components/ui/InlineError'`: копия одна, импорты goods не
меняются. Цена — одна строка в goods, разметка та же.

---

## §32.5. Адаптеры

### §32.5.1 goods — `frontend/goods/src/components/profile/ShopProfileSection.tsx` (переписывается в обёртку)

Экспорт, путь и проп `{ shop: ShopManageDto }` те же. Внутри:
- `cityOf(shop)` — текущая функция без изменений → `snapshot.city`;
- `zone = { id: shop.timeZoneId, offsetMinutes: shop.utcOffsetMinutes ?? null, isManual: false }`;
- `timeZoneLock = { allowed: shop.timeZoneChangeAllowed, lockedText: shop.timeZoneChangeLockedText ?? null,
  fallbackText: 'У магазина уже есть заказы — часовой пояс сменить нельзя.' }`;
- `errorMessage = getGoodsErrorMessage`;
- `onChanged` = текущий `refetchShop` (`['shop', id]`, `['my-shops']`, `['storefront']`);
- `texts` — текущие строки магазина (таблица §32.5.3).

`SettingsPage.tsx` не меняется (`<ShopProfileSection key={shop.id} shop={shop} />`). Ожидания `ShopProfileSection.test.tsx`
и `SettingsPage.test.tsx` не меняются — это критерий приёмки FE-2.

### §32.5.2 ezbook — `frontend/src/pages/owner/SalonProfileSection.tsx` (новый)

```ts
export function SalonProfileSection({ company }: { company: Company }): JSX.Element
// mounted as <SalonProfileSection key={company.id} company={company} />
```
- `city`: при `cityId != null && cityName` — `{ id, name, region: cityRegion ?? '', timeZoneId: company.timeZoneId ?? '',
  utcOffsetMinutes: company.utcOffsetMinutes ?? 0, label }`. `timeZoneId`/`utcOffsetMinutes` этого объекта карточка
  использует только для строки пояса при неручном поясе (§32.6.3);
- `zone = { id: company.timeZoneId ?? null, offsetMinutes: company.utcOffsetMinutes ?? null, isManual: !!company.timeZoneIsManual }`;
- `errorMessage = getCompanyManageErrorMessage`; `manualTimeZone`, `legacyPhoneHint` — `true`; `timeZoneLock` не передаётся;
- `onChanged`: `invalidateQueries(['my-companies'])` (вкладки, `h1`, виджет) и `invalidateQueries(['company'])` — по
  префиксу, публичная страница салона `['company', slug]`, как делает `CompanyPhotosSection`.

### §32.5.3 Тексты (Q-32-10, R-2)

| Ключ | Магазин (как сейчас) | Салон |
|---|---|---|
| `title` | Профиль магазина | Профиль салона |
| `logoAlt` | Логотип магазина | Логотип салона |
| `nameRequired` | Введите название магазина | Введите название салона |
| `phoneLabel` | Телефон для покупателей | Телефон для клиентов |
| `emailLabel` | Email для покупателей | Email для клиентов |
| `emailHint` | Виден на странице магазина | Виден на странице салона |
| `cityHint` | Часы работы и время получения заказов считаются по часовому поясу города | Напоминания клиентам и срок переноса и отмены записи считаются по часовому поясу салона |
| `addressHint` | Адрес виден покупателям на странице магазина | Адрес виден клиентам на странице салона |
| `saveFailed` | Не удалось сохранить профиль. | Не удалось сохранить профиль салона. |
| `timeZoneChange(from, to)` | `Часовой пояс магазина изменится: ${from} → ${to}. Часы работы и время получения заказов будут считаться по новому поясу.` | `Часовой пояс салона изменится: ${from} → ${to}. Срок переноса и отмены сразу начнёт считаться по новому поясу, напоминания — для записей, созданных или перенесённых после смены. Время уже созданных записей и рабочие часы не изменятся.` |

Метки полей ссылок — «Яндекс Карты» и «2ГИС» (было у салона «Ссылка на Яндекс Картах» / «Ссылка на 2ГИС»). Подсказка
салона «Вставьте ссылку на карточку компании — она сохранится как есть, без изменений» уходит вместе с прежней
раскладкой, у goods её нет (Q-32-1: вид берётся у goods).

---

## §32.6. Часовой пояс: что отправить и когда спросить (Q-32-3, Q-32-5, R-2)

### §32.6.1 Что зависит от пояса салона (✔ по коду)

| Что | Зависит? | Где в коде |
|---|---|---|
| срок, до которого клиент может перенести или отменить запись | **да, сразу** (считается в момент действия) | `BookingsController.cs:126, 310–311, 430` — `ComputeVisitStartUtc(Date, StartTime, company.TimeZoneId)` |
| момент напоминаний клиенту и push персоналу | **да** — для записей, созданных или перенесённых после смены | `NotificationScheduler.cs:53, 97` (`DueAtUtc` фиксируется при постановке в очередь), `StaffPushScheduler.cs:50, 113` |
| уже поставленные в очередь напоминания | **нет**: `DueAtUtc` не пересчитывается. Диспетчер обновляет только `VisitStartUtc` | `NotificationDispatchTask.cs:291–301` |
| дата и время уже созданных записей, рабочие часы | **нет** — хранятся как местные `Date`/`StartTime` | модель `Booking`, `WorkingHours` |
| «сегодня» для слотов и горизонта записи | **нет** — считается по UTC (давнее поведение, цикл его не трогает) | `BookingAvailabilityController.cs:180`, `BookingCreationService.cs:147` |

Отсюда тексты §32.5.3: подсказка обещает только то, что правда (напоминания и срок переноса/отмены), модалка прямо
говорит, что время записей и часы не сдвигаются. Это сохраняет смысл прежнего пояснения («напоминания … по местному
времени салона») и не обещает пересчёта уже запланированных напоминаний.

### §32.6.2 `frontend/src/components/company/profileZone.ts` (новый, чистая функция)

```ts
export interface ZoneBaseline { cityId: number | null; zoneId: string | null; offsetMinutes: number | null; isManual: boolean }
export interface ZoneDraft { city: City | null; manual: boolean; manualZoneId: string }
export interface ZonePlan {
  cityId?: number                           // present ⇔ city changed
  timeZoneId?: string | null                // present ⇔ salon changed the manual zone state (never for shops)
  confirm: { from: string; to: string } | null
}
export function planProfileZone(
  base: ZoneBaseline, draft: ZoneDraft, manualAllowed: boolean,
  offsetOf: (zoneId: string) => number | null,
): ZonePlan
```

Правила:
1. `cityChanged = draft.city != null && draft.city.id !== base.cityId` → `cityId = draft.city.id`.
2. Только при `manualAllowed`: `z = draft.manual ? draft.manualZoneId.trim() : ''`.
   - `z !== '' && (!base.isManual || z !== base.zoneId)` → `timeZoneId = z`;
   - `z === '' && base.isManual` → `timeZoneId = null` (пустое ручное поле = выключить, как сейчас);
   - иначе ключа нет.
3. Новое смещение `o`:
   - `timeZoneId` — строка → `o = offsetOf(z)`. `null` (браузер не знает пояс) → модалки нет, пояс проверит сервер (400 у поля);
   - `timeZoneId === null` → при `cityChanged` `o = draft.city.utcOffsetMinutes`, иначе смещение **неизвестно** —
     модалка всегда, `to = 'пояс города ' + (draft.city?.label ?? '')` (§32.3, вариант В);
   - ключа нет, `cityChanged && !base.isManual` → `o = draft.city.utcOffsetMinutes`;
   - иначе пояс не меняется, модалки нет.
4. `confirm`, если `o` известно и `o !== base.offsetMinutes` (для `null` в базе это всегда «другое» — как V29-12).
   `from = base.offsetMinutes != null ? formatUtcOffset(base.offsetMinutes) : (base.zoneId ?? '—')`,
   `to = formatUtcOffset(o)`.

**Для магазина** (`manualAllowed = false`, `isManual = false`) остаются только п. 1 и ветка «ключа нет»:
`confirm ⇔ cityChanged && city.utcOffsetMinutes !== shop.utcOffsetMinutes`. Это дословно текущая строка 114
`ShopProfileSection.tsx`. Тексты `from`/`to` тоже совпадают со строкой 358.

### §32.6.3 Строка «Часовой пояс: …» под городом

| Состояние | Текст |
|---|---|
| ручной пояс включён, поле не пустое | `Часовой пояс: ${z} — указан вручную` |
| город выбран заново в этой сессии | `Часовой пояс: ${formatCityTimeZone(city.label, city.utcOffsetMinutes, city.timeZoneId)}` |
| город не меняли, `base.isManual = false` (всегда у магазина) | то же от начального `city` — как сейчас |
| ручной пояс был и выключен, город не меняли | `Часовой пояс: как у города ${city.label}` |
| города нет | строки нет |

### §32.6.4 `utcOffsetMinutesOf` — в `frontend/src/utils/timezone.ts`

```ts
/** Client-side offset of an arbitrary IANA zone — used ONLY to decide whether to show the confirmation. */
export function utcOffsetMinutesOf(timeZoneId: string, at: Date = new Date()): number | null
```
Реализация: `new Intl.DateTimeFormat('en-US', { timeZone, timeZoneName: 'longOffset' }).formatToParts(at)` → часть
`timeZoneName` вида `GMT+07:00` / `GMT-03:30` / `GMT` → минуты. `RangeError` (неизвестный пояс) или неожиданный
формат → `null`. Комментарий в шапке файла («`utcOffsetMinutes` comes from the server, never computed here») уточнить:
сервер остаётся источником для показа, клиент считает смещение только ручного пояса и только для решения о модалке.

---

## §32.7. Телефон (Q-32-6, R-4)

- Поле — `PhoneInput` (маска `+7 (900) 000-00-00`, `restrictToRussia` по умолчанию), как у магазина.
- Нетронутое значение уходит как пришло (контракт §32.27.2). Отдельного «не отправлять, если не менялся» не делаем:
  goods шлёт телефон всегда, и тест магазина это фиксирует (`phone: '79001234567'` в теле без правок).
- **Подсказка для старых номеров (только салон, `legacyPhoneHint`):** показывается, если сохранённый номер не пуст,
  не совпадает с `/^7\d{10}$/`, а текущее значение поля равно сохранённому. Текст:
  `Сейчас сохранён номер «${raw}». Он не изменится, пока вы не исправите поле; новый номер вводится в формате +7.`
  Зачем: маска показывает такой номер искажённым (без добавочного, иностранный — пустым), и владелец иначе решит,
  что номер уже испорчен. Связь с полем — через `aria-describedby`.
- После правки в БД попадает `7XXXXXXXXXX`. Публичные экраны форматируют его сами. Это принятый риск Q-32-6,
  сервер не меняем.

---

## §32.8. Карточка «Правила записи» — `frontend/src/pages/owner/BookingRulesSection.tsx` (US-32-03)

Не общий компонент: поля у записи и заказов разные (SPEC §3). Общий у них только стиль карточки (`CARD_TITLE_CLASS`,
строка-пояснение, «Сохранено»).

### §32.8.1 Интерфейс и состояние

```ts
export function BookingRulesSection({ company }: { company: Company }): JSX.Element
// mounted as <BookingRulesSection key={company.id} company={company} />
```
Локальный `useState`, инициализация один раз:
- `horizon = company.bookingHorizonDays ? String(company.bookingHorizonDays) : ''`;
- `windowHours = company.clientRescheduleMinHours != null ? String(company.clientRescheduleMinHours) : ''`;
- `allowSelfBooking = company.allowSelfBooking`, `requirePrepayment = company.requirePrepayment ?? false`.

Правки профиля правила не сбрасывают, и наоборот: карточки не делят состояние, а перечитывание `['my-companies']` не
пересоздаёт ни одну из них (один `key`).

### §32.8.2 Вёрстка

```
<Card className="p-6">
  h2.CARD_TITLE_CLASS.mb-1   «Правила записи»
  p.text-sm.text-ink-soft.mb-5   RULES_NOTE
  flex flex-col gap-4:
    Input number 0–365  «На сколько дней вперёд клиент может записаться»  placeholder 90
      p.text-xs.text-muted  «Пусто или 0 — 90 дней по умолчанию»        (aria-describedby)
    Input number 0–168  «За сколько часов клиент может перенести или отменить запись»  placeholder 2
      p#…caption.text-xs.text-muted  {CANCEL_WINDOW_FIELD_CAPTION}      (дословно, aria-describedby)
    label+checkbox «Разрешить клиентам записываться самостоятельно»   disabled = !planAllowsOnlineBooking
      p.text-xs.text-warning  «Онлайн-запись не входит в текущий тариф — повысьте тариф, чтобы включить»
    label+checkbox «Требовать предоплату при онлайн-записи»           disabled = !planAllowsOnlinePayment
      p.text-xs.text-warning  «Онлайн-оплата не входит в текущий тариф — повысьте тариф, чтобы включить»
  InlineError (если ошибка без поля)
  flex items-center gap-3: Button «Сохранить правила» (всегда активна) + SavedNote «Сохранено» role=status, 2,5 с
```
Метки, диапазоны, плейсхолдеры, классы чекбоксов (`has-[:disabled]:…`) и тексты предупреждений переносятся из
`SettingsTab` дословно. Ошибки полей — через проп `error` у `Input`.

**Строка-пояснение (R-3 SPEC, ✔ по коду):** предположение спеки «правила применяются к новым записям» неверно. Горизонт
и самозапись проверяются и при переносе уже созданной записи (`BookingsController.cs:303, 315`). Окно переноса и отмены —
в момент действия клиента (`:310–312`, `:429–431`). Только предоплата решается при создании записи
(`BookingCreationService.cs:159`). Поэтому:

`RULES_NOTE = 'Изменения действуют сразу — и для новых записей, и для переноса или отмены уже созданных. Предоплата
запрашивается только в новых записях.'`

### §32.8.3 Сохранение

1. `parseBookingHorizonInput(horizon)`: ошибка → у поля горизонта, запроса нет.
2. Тело по контракту §32.23 с `satisfies C32['schemas']['BookingRulesUpdateInput']`:
   `bookingHorizonDays` всегда; `clientRescheduleMinHours` — если поле не пустое (`parseInt`); флажки — если не
   заблокированы тарифом.
3. `companiesApi.update(company.id, body)`. Успех → «Сохранено», `invalidateQueries(['my-companies'])` и `['company']`
   (горизонт и самозапись видны на публичной странице и в виджете).
4. Ошибка: `400` + `mapLinksFieldError(raw) === 'clientRescheduleMinHours'` → у поля окна; `400` + `raw` начинается с
   «Горизонт записи» → у поля горизонта; иначе `InlineError` с
   `getCompanyManageErrorMessage(err, 'Не удалось сохранить правила записи.')`.

---

## §32.9. Раскладка вкладки «Настройки» (US-32-04, US-32-05)

### §32.9.1 `SettingsTab` — новая форма

```tsx
export function SettingsTab({ companyId }: { companyId: string }) {
  const { data: companies, isLoading } = useQuery({ queryKey: ['my-companies'], queryFn: companiesApi.getMy })
  const company = companies?.find((c) => c.id === companyId)
  return (
    <div className="max-w-[760px] flex flex-col gap-5">
      {company ? <SalonProfileSection key={company.id} company={company} /> : isLoading ? <ProfileSkeleton /> : null}
      <CompanyPhotosSection companyId={companyId} headingAs="h2" headingClassName={CARD_TITLE_CLASS} />
      {company && <BookingRulesSection key={company.id} company={company} />}
      <SalonCatalogListingSection companyId={companyId} />
      {company && <WidgetCard company={company} />}
      <PhotoUsageCard companyId={companyId} />
    </div>
  )
}
```
- Порядок — Q-32-7. Отступ между карточками задаёт только `gap-5` контейнера. `mt-[18px]` у `WidgetCard`,
  `PhotoUsageCard` и её скелетона удаляются. `PhotoUsageCard` при ошибке возвращает `null`: flex-gap для
  отсутствующего элемента места не оставляет.
- Заголовка «Настройки компании» нет (Q-32-8). Уровни на странице: `h1` (имя компании) → `h2` у всех шести карточек.
- Колонка `max-w-[760px]` **без** `mx-auto`: вкладки и `h1` стоят слева в контейнере `max-w-4xl`, колонка
  выравнивается по ним. Центровка сдвинула бы левый край карточек относительно вкладок на ~68 px.
- `ProfileSkeleton` — `<div className="h-72 bg-cream-deep rounded-2xl animate-pulse" />`. Если компании нет в
  `['my-companies']` (не владелец), карточек профиля и правил нет. Раньше в этом случае рисовалась пустая форма, и её
  сохранение перезаписало бы поля пустыми строками.
- `SettingsTab` остаётся экспортом `CompanyManagePage.tsx` (его импортирует тест).

### §32.9.2 Стиль заголовков — `frontend/src/components/company/cardTitle.ts` (новый)

```ts
/** ARCHITECTURE_CYCLE32.md §32.9.2 — the one card-title style (goods RulesSection/SellerSection/ShopProfileSection). */
export const CARD_TITLE_CLASS = 'text-[15px] font-semibold text-ink'
```
- `CompanyProfileCard`, `BookingRulesSection`, `WidgetCard`, `PhotoUsageCard` — `h2` с этим классом (+ отступ снизу,
  как у goods `mb-1`/`mb-1.5`). Прочая разметка `WidgetCard`/`PhotoUsageCard` не меняется.
- `CompanyPhotosSection` — новые необязательные пропсы `headingAs?: 'h2' | 'h3'` (умолчание `'h3'`) и
  `headingClassName?: string` (умолчание `'text-lg font-semibold text-ink'`, как сейчас). goods их не передаёт, поэтому
  его галерея не меняется.
- `CatalogListingCard` — новый необязательный проп `headingClassName?: string` (умолчание `'font-serif text-xl text-ink'`,
  как сейчас). `SalonCatalogListingSection` передаёт `headingAs="h2"` и `headingClassName={CARD_TITLE_CLASS}`. goods
  `CatalogListingSection` не меняется.

### §32.9.3 360 px

- Все сетки карточки профиля — `grid sm:grid-cols-2`: до 640 px одна колонка (как у goods).
- Модалки — существующий `Modal` (уже проверен на 360 в циклах 26 и 29).
- Ширина фиксированных элементов: код виджета в `textarea` переносится, ссылка предпросмотра — `flex-1` + кнопка.
  Проверка — M32-02.

### §32.9.4 US-32-05 (P2) — боковые поля страницы

`CompanyManagePage`: `max-w-4xl mx-auto px-8 pt-11 pb-24` → `max-w-4xl mx-auto px-4 sm:px-8 pt-11 pb-24`. Затрагивает
все вкладки, только отступ. Проверка M32-08 по всем пяти вкладкам на 360 px. Если какая-то вкладка ломается, история
откладывается целиком (SPEC).

---

## §32.10. Что удаляется

| Что | Где | Почему безопасно |
|---|---|---|
| `useForm`-форма, fieldset «Основное», «Контакты», «Адрес и карты», «Запись», кнопка «Сохранить изменения», `h2` «Настройки компании» | `SettingsTab` | заменено карточками профиля и правил |
| `CityTimeZoneFields`, `swallowImplicitSubmit` | `CompanyManagePage.tsx:1004–1105` | город и пояс — внутри общего блока |
| `CompanyAddressField.tsx` + `CompanyAddressField.test.tsx` | `src/components/company/` | ✔ поиск: другой пользователь — только `SettingsTab`. Адрес пишет общий блок |
| строка `vi.mock('@/components/company/CompanyAddressField', …)` | `goods/src/pages/cabinet/SettingsPage.test.tsx:18` | мок модуля, который goods-страница никогда не импортировала. После удаления файла строка ссылается на несуществующий модуль. Ожидания теста не меняются |
| функция `InlineError` в goods `StatePanels.tsx` | — | заменяется реэкспортом общего (§32.4.7) |
| неиспользуемые импорты `CompanyManagePage.tsx` (`useForm`, `Controller` — если больше нигде, `CityCombobox`, `CompanyAddressField`, `mapLinksFieldError`, `parseBookingHorizonInput`, `formatCityTimeZone`, `CANCEL_WINDOW_FIELD_CAPTION`, `City`) | — | ловят ESLint и `tsc` |

---

## §32.11. Контракт, типы и CI

| Что | Где | Кто |
|---|---|---|
| `contracts/cycle32/openapi.yaml` | готов (этот цикл) | архитектор |
| `"types:api:cycle32": "openapi-typescript ../contracts/cycle32/openapi.yaml -o src/types/api-cycle32.generated.ts"` | `frontend/package.json` + генерат | frontend (FE-0) |
| `UpdateCompanyPayload` += `clientRescheduleMinHours?: number` | `src/api/companies.ts` (тип фронта; сервер поле принимает с цикла 15) | frontend (FE-0) |
| `cycle32` в список `cycles` | `frontend/scripts/contracts-to-json.mjs` + сгенерированный `contracts/cycle32/openapi.json` | backend (BE-2) |
| `ci.yml`: lint (+ `../contracts/cycle32/openapi.yaml`), шаг генератов (+ `npm run types:api:cycle32` и файл в `git diff`), шаг JSON (+ `../contracts/cycle32/openapi.json`) | `.github/workflows/ci.yml` строки ~176, ~213–219 | backend (BE-2) |
| перечень в комментарии | `contracts/redocly.yaml` | backend (BE-2) |

Тела PUT во фронте строятся так:
```ts
import type { components as C32 } from '../../types/api-cycle32.generated'
const body = { name, description, phone, email, ...zoneKeys, ...linkKeys } satisfies C32['schemas']['CompanyProfileUpdateInput']
```
`additionalProperties: false` даёт тип без индексной сигнатуры. Лишний ключ в литерале — ошибка `tsc`.

---

## §32.12. Тесты

### §32.12.1 Vitest — новые и изменённые файлы

| Файл | Кейсы |
|---|---|
| `goods/src/components/profile/ShopProfileSection.test.tsx` | **без правок файла**, 17 `it` зелёные (US-32-01) |
| `goods/src/pages/cabinet/SettingsPage.test.tsx` | без правок ожиданий; удалена только строка мока `CompanyAddressField` (§32.10) |
| `goods/src/sharedSources.guard.test.ts` | без правок, зелёный |
| `src/components/company/profileZone.test.ts` (новый) | магазин: тот же город → пусто; другой город того же смещения → `cityId`, без `confirm`; другое смещение → `confirm {UTC+7 → UTC+3}`; `offsetMinutes: null` → `confirm` (V29-12). Салон: ручной включён (было выкл.) → `timeZoneId: 'Asia/Omsk'` и `confirm` при другом смещении; тот же ручной пояс → ключа нет; ручной выключен (был вкл.), город тот же → `timeZoneId: null`, `confirm.to = 'пояс города Барнаул, Алтайский край'`; выкл. + смена города → `null` + `cityId`, смещение нового города; город сменён при сохранённом ручном поясе → только `cityId`, без `confirm`; пустое ручное поле при `isManual` → `null`; `offsetOf → null` → без `confirm`; при `manualAllowed = false` ключа `timeZoneId` нет никогда |
| `src/utils/timezone.test.ts` (дополнить) | `utcOffsetMinutesOf('Asia/Barnaul') = 420`, `'Europe/Moscow' = 180`, `'Asia/Kolkata' = 330`, `'UTC' = 0`, `'Mars/Olympus' = null` |
| `src/components/company/CompanyProfileCard.test.tsx` (новый, общие механизмы) | раскладка ошибок §32.4.6 строки 1–5 на обоих наборах флагов (409 без `timeZoneLock` → под формой; «Неизвестный часовой пояс» без `manualTimeZone` → под формой); `describedBy` у города; «Сохранено» `role="status"` исчезает через 2,5 с (fake timers) |
| `src/pages/owner/SalonProfileSection.test.tsx` (новый) | V32-01…V32-13 (§32.12.3) |
| `src/pages/owner/BookingRulesSection.test.tsx` (новый) | V32-14…V32-18 |
| `src/pages/owner/CompanyManagePage.test.tsx` | `SettingsTab`: V32-19…V32-22 + перенос старых (§32.12.2). Блок `MembersTab` не трогается |
| `src/components/company/CompanyPhotosSection.test.tsx`, `CatalogListingCard.test.tsx` | + по одному `it`: `headingAs="h2"` и `headingClassName` применяются; без пропов — прежние `h3`/классы |
| `src/components/ui/CityCombobox.test.tsx` (если есть; иначе в `CompanyProfileCard.test.tsx`) | `describedBy` сливается с id ошибки |

### §32.12.2 Старые тесты `CompanyManagePage.test.tsx` → новые (R-5, осознанная смена ожиданий)

| Было | Что проверял | Стало |
|---|---|---|
| цикл 13: «keeps typed-but-unsubmitted text…» | набранное не теряется при перечитывании `['my-companies']`; кнопка не гаснет | **V32-11**: тот же сценарий на `Название *` и кнопке «Сохранить». Кнопка активна всегда (Q-32-11) |
| §305.3: подпись Т20-05 | подпись `CANCEL_WINDOW_FIELD_CAPTION` | **V32-15** в `BookingRulesSection.test.tsx`: `getByText(CANCEL_WINDOW_FIELD_CAPTION)` (равенство константе, дословно) и `toHaveAccessibleDescription` содержит её |
| §305.3: метка про перенос и отмену | метка поля | V32-15 |
| §305.3: очищенное окно не уходит | `clientRescheduleMinHours` нет в теле | **V32-16** на кнопке «Сохранить правила» |
| V29-01 | четыре группы, блок города внутри «Адрес и карты» | **V32-01** + **V32-19**: групп «Основное»/«Контакты»/«Запись» и `h3` «Город и часовой пояс» нет; есть группа «Адрес и карты» в профиле |
| V29-02 | основное сохранение без `cityId/timeZoneId/address` | **V32-02**: точное тело `toEqual` |
| C31 (чекбокс каталога) | чекбокса нет, блок каталога есть | **V32-20**, смысл тот же |
| C31 R-4 | `showInPublicListing` не уходит | **V32-20**: ни в теле профиля, ни в теле правил |
| V29-03 | город сохраняется своим `{cityId, timeZoneId}` | **V32-04/05/08**: город и пояс — в общем PUT по `planProfileZone` |
| V29-04 | Enter в блоке города не сабмитит основную форму | **V32-21**: Enter в IANA-поле запускает сохранение **профиля** (одна форма) и не трогает правила |
| V29-05 | — | в коде такого теста нет (пропуск нумерации цикла 29), заменять нечего |
| V29-06 | 400 про Яндекс — у поля | **V32-10**, смысл тот же, метка «Яндекс Карты» |

В `TEST_CATALOG.md` (раздел «Цикл 32») эта таблица переносится как есть. Так QA видит смену ожиданий из плана, а не
по диффу.

### §32.12.3 Перечень V32

| ID | Проверяет |
|---|---|
| V32-01 | один `h2` «Профиль салона»; метки `Название *`, `Описание`, `Телефон для клиентов`, `Email для клиентов`, `Город`, `Адрес`, `Яндекс Карты`, `2ГИС`; группа «Адрес и карты»; тексты «Виден на странице салона», «Адрес виден клиентам на странице салона»; `img` с `alt` «Логотип салона»; одна кнопка «Сохранить» в карточке |
| V32-02 | без правок → `update('co1', { name, description, phone, email })` **ровно** (`toEqual`); `saveAddress` не вызван |
| V32-03 | правка названия и телефона → одно тело с этими значениями, телефон `7XXXXXXXXXX` |
| V32-04 | город с другим смещением → диалог «Сменить город?» с «Часовой пояс салона изменится: UTC+7 → UTC+3.»; «Отмена» — вызовов нет; «Сменить город» → `cityId: 3`, ключа `timeZoneId` нет |
| V32-05 | город с тем же смещением → диалога нет, `cityId` в теле |
| V32-06 | изменён адрес → `PublicAddressNotice` до запроса; после подтверждения `update` вызван **раньше** `saveAddress` (`invocationCallOrder`) |
| V32-07 | сбой адреса → у поля «Не удалось сохранить адрес. Попробуйте ещё раз.», под формой «Остальные изменения сохранены» |
| V32-08 | ручной пояс: включить + `Asia/Omsk` → диалог (UTC+7 → UTC+6) → `timeZoneId: 'Asia/Omsk'`; салон с `timeZoneIsManual` — выключить → диалог «… → пояс города Барнаул, Алтайский край» → `timeZoneId: null`; ничего не трогали → ключей нет |
| V32-09 | пустое название → «Введите название салона» у поля, `update` не вызван |
| V32-10 | 400 «Город не найден» — у города; 400 «Неизвестный часовой пояс» — у IANA-поля; 400 про Яндекс — у «Яндекс Карты»; 400 другой → «Проверьте введённые данные — сервер их не принял.» под формой |
| V32-11 | набранное не теряется при `qc.setQueryData(['my-companies'], …)`, кнопка активна |
| V32-12 | сохранённый телефон «8 (3852) 12-34-56 доб. 5»: подсказка видна и связана с полем; без правки телефона в теле тот же `phone` байт в байт; после правки подсказки нет |
| V32-13 | успех → `role="status"` «Сохранено»; инвалидированы `['my-companies']` и `['company']` |
| V32-14 | «Правила записи»: горизонт 30 → тело **ровно** `{ bookingHorizonDays: 30, clientRescheduleMinHours: 2, allowSelfBooking: true, requirePrepayment: false }` (нет ни одного ключа профиля) |
| V32-15 | `CANCEL_WINDOW_FIELD_CAPTION` дословно, связан с полем; метка «За сколько часов клиент может перенести или отменить запись» |
| V32-16 | очищенное окно → ключа нет; очищенный горизонт → `0`; горизонт 400 → ошибка у поля без запроса |
| V32-17 | тариф без онлайн-записи и оплаты → оба флажка `disabled`, оба предупреждения; в теле нет `allowSelfBooking` и `requirePrepayment` |
| V32-18 | правка названия (не сохранена) → «Сохранить правила» → название в поле осталось; и наоборот: правка горизонта → «Сохранить» профиля → горизонт остался; 400 «Окно переноса…» → у поля окна |
| V32-19 | порядок `h2` на вкладке: «Профиль салона», «Фотографии салона», «Правила записи», «Каталог ezbook.ru», «Виджет для сайта», «Хранилище фото клиентов»; `h3` на вкладке нет; «Настройки компании» нет; у контейнера классы `max-w-[760px]` и `gap-5` |
| V32-20 | нет чекбокса «Показывать компанию в общем списке»; есть `switch` «Показывать салон в каталоге ezbook.ru»; ни в одном теле `update` нет `showInPublicListing` |
| V32-21 | Enter в IANA-поле → сохранение профиля (вызов с телом профиля), правила не отправлены |
| V32-22 | `PhotoUsageCard` при ошибке не рендерится: последний ребёнок контейнера — виджет |

### §32.12.4 Функциональные тесты сервера — `ServiceBooking.Tests/Tests/Cycle32CompanySettingsTests.cs` (новый)

Продуктовый код не меняется, тесты фиксируют гарантии, на которых стоит план. `[TestCase("CY32-xx")]`, своя фикстура.

| ID | Проверяет |
|---|---|
| CY32-01 | салон с непустыми правилами (горизонт 30, окно 12, самозапись выкл., предоплата вкл., показ в каталоге вкл., ручной пояс `Asia/Omsk`, ссылки, адрес). PUT тела профиля (`name, description, phone, email`) → 200; `GET /my`: правила, флаги, `showInPublicListing`, пояс, `timeZoneIsManual`, ссылки, адрес не изменились (R-3) |
| CY32-02 | тот же салон. PUT тела правил → 200; имя, описание, телефон, e-mail, город, пояс, ссылки, адрес не изменились (R-3) |
| CY32-03 | телефон `"8 (3852) 12-34-56 доб. 5"` записан → повторный PUT профиля с тем же значением → `GET` отдаёт строку байт в байт; PUT `"79001234567"` → хранится как есть (R-4) |
| CY32-04 | таблица контракта §32.27.3 целиком — 6 строк, плюс салон без города: `timeZoneId: "Asia/Omsk"` → 200, пояс не изменился |
| CY32-05 | тексты 400 дословно и `text/plain`: «Город не найден», «Неизвестный часовой пояс», «Окно переноса — от 0 до 168 часов», «Горизонт записи — от 1 до 365 дней». После каждого 400 имя компании (присланное в том же теле) не изменилось — атомарность |

`Cycle32ContractConformanceTests.cs` (валидатор `OpenApiContract.Load("cycle32")`):

| ID | Запрос | Операция контракта |
|---|---|---|
| CY32-10 | `GET /api/companies/my` | 200 `CompanySettingsList` |
| CY32-11 | `PUT /api/companies/{id}` — тело профиля и тело правил | 200 `CompanySettingsDto` |
| CY32-12 | `PUT /api/companies/{id}/address`, `POST …/logo` | 200 `CompanyAddressUpdateResultDto`, `CompanySettingsDto` |

Плюс `OpenApiContractValidatorTests.Bundled_contracts_load_and_unknown_path_is_reported`: `[InlineData("cycle32")]`.
`Cycle22RouteTable.golden.txt` — **без изменений**.

### §32.12.5 Ручные `M32-` (T-32-02, раздел «Цикл 32» в `TEST_CATALOG.md`, формат `M31-`)

Размеры 360 × 740 и 1280 × 800. Колонки: `Кейс | Шаги | Ожидаемый результат | Критерий`.
- M32-01 — вкладка «Настройки» салона на 1280 целиком: порядок, одинаковые отступы, один стиль заголовков, ширина ≤ 760.
- M32-02 — то же на 360: нет горизонтальной прокрутки, сетки в одну колонку, обе модалки влезают.
- M32-03 — профиль салона и профиль магазина рядом (1280 и 360): одинаковые шапка, сетка, отступы.
- M32-04 — смена города (другое смещение) и адреса одним нажатием: «Сменить город?» → уведомление о публичном адресе →
  «Сохранено»; публичная страница салона и `h1` обновились.
- M32-05 — ручной пояс: включить `Asia/Omsk` (модалка), выключить (модалка «пояс города»), ввести `Asia/Nowhere` (ошибка у поля).
- M32-06 — «Правила записи» на тарифе без онлайн-записи и оплаты: флажки недоступны, тексты; сохранение правил не
  сбрасывает несохранённое название.
- M32-07 — goods «Настройки»: скриншоты до (со `develop`) и после — без визуальных различий.
- M32-08 (P2) — `/owner/company/:id` на 360, все пять вкладок: боковые поля 16 px, ничего не сломалось.
- M32-09 — салон с телефоном «8 (3852) 12-34-56 доб. 5»: подсказка видна; сохранение с правкой только названия номер не
  меняет (публичная страница показывает прежний).

---

## §32.13. Структура проекта — что добавляется и меняется

```
contracts/cycle32/openapi.yaml                               + (готов)
contracts/cycle32/openapi.json                               + генерат contracts:json
contracts/redocly.yaml                                       ~ комментарий
.github/workflows/ci.yml                                     ~ lint, генераты, JSON
ServiceBooking.Tests/
  Tests/Cycle32CompanySettingsTests.cs                       +
  Tests/Cycle32ContractConformanceTests.cs                   +
  Tests/OpenApiContractValidatorTests.cs                     ~ InlineData("cycle32")
frontend/
  package.json                                               ~ types:api:cycle32
  scripts/contracts-to-json.mjs                              ~ + 'cycle32'
  src/types/api-cycle32.generated.ts                         + генерат
  src/api/companies.ts                                       ~ UpdateCompanyPayload.clientRescheduleMinHours
  src/utils/httpError.ts                                     + httpStatusOf, plainErrorBody
  src/utils/timezone.ts (+ test)                             ~ utcOffsetMinutesOf
  src/components/ui/InlineError.tsx                          +
  src/components/ui/CityCombobox.tsx                         ~ проп describedBy
  src/components/company/cardTitle.ts                        + CARD_TITLE_CLASS
  src/components/company/profileZone.ts (+ test)             + planProfileZone
  src/components/company/CompanyProfileCard.tsx (+ test)     + общий блок
  src/components/company/CompanyPhotosSection.tsx (+ test)   ~ headingAs, headingClassName
  src/components/company/CatalogListingCard.tsx (+ test)     ~ headingClassName
  src/components/company/CompanyAddressField.tsx (+ test)    − удалить
  src/pages/owner/SalonProfileSection.tsx (+ test)           +
  src/pages/owner/BookingRulesSection.tsx (+ test)           +
  src/pages/owner/SalonCatalogListingSection.tsx             ~ headingAs h2, класс
  src/pages/owner/CompanyManagePage.tsx (+ test)             ~ SettingsTab, Widget/PhotoUsage, px (P2), − CityTimeZoneFields
  goods/src/components/profile/ShopProfileSection.tsx        ~ обёртка над CompanyProfileCard (путь, экспорт, пропсы те же)
  goods/src/components/StatePanels.tsx                       ~ реэкспорт InlineError
  goods/src/pages/cabinet/SettingsPage.test.tsx              ~ − строка мока CompanyAddressField
Документы: TEST_CATALOG.md (раздел «Цикл 32»), CHANGELOG.md («Не выпущено — цикл 32»),
           CURRENT_STATE.md — обновляет codebase-analyst после влития. docs/owner.md — technical-writer при релизе (SPEC §3).
```

`goods-shared-sources.js`, `tailwind.goods.config.js`, `eslint.config.js` **не меняются**: все новые общие файлы лежат
в `src/components/**` и `src/utils/**`, которые уже сканируются.

---

## §32.14. Разбивка работ и параллельность

### §32.14.1 Задачи

**Backend** (только тесты и обвязка контракта)

| ID | Задача | SPEC | Зависит от |
|---|---|---|---|
| BE-1 | `Cycle32CompanySettingsTests` CY32-01…05 | R-3, R-4, US-32-02 | — |
| BE-2 | `contracts-to-json.mjs` + `openapi.json`, CI (lint, генераты, JSON), `redocly.yaml`; `Cycle32ContractConformanceTests` CY32-10…12, `InlineData("cycle32")` | T-32-01 | контракт (готов); шаг генератов в CI краснеет, пока нет FE-0 — вливать после FE-0 или в той же ветке |

**Frontend**

| ID | Задача | SPEC | Зависит от |
|---|---|---|---|
| FE-0 | `types:api:cycle32` + генерат; `UpdateCompanyPayload.clientRescheduleMinHours` | — | контракт (готов) |
| FE-1 | основа: `httpError.ts`, `utcOffsetMinutesOf` (+test), `profileZone.ts` (+test), `cardTitle.ts`, `ui/InlineError.tsx` + реэкспорт в goods `StatePanels`, `CityCombobox.describedBy` | US-32-01, R-2 | — |
| FE-2 | `CompanyProfileCard` (перенос разметки §32.4.3, цепочка §32.4.5, ошибки §32.4.6) + goods `ShopProfileSection` → обёртка + `CompanyProfileCard.test.tsx`. **Гейт:** goods-тесты без правок и guard зелёные, скриншот goods «до» снят | US-32-01 | FE-0, FE-1 |
| FE-3 | `SalonProfileSection` + V32-01…13 | US-32-02 | FE-2 |
| FE-4 | `BookingRulesSection` + V32-14…18 | US-32-03 | FE-0, FE-1 (`cardTitle`, `InlineError`, `httpError`) |
| FE-5 | `SettingsTab` (§32.9.1), пропсы заголовков галереи и каталога, `WidgetCard`/`PhotoUsageCard`, удаление §32.10, перенос тестов §32.12.2, V32-19…22 | US-32-04, T-32-01 | FE-3, FE-4 |
| FE-6 (P2) | `px-4 sm:px-8` у `CompanyManagePage` | US-32-05 | FE-5 (тот же файл) |

**QA / документы**

| ID | Задача | Зависит от |
|---|---|---|
| QA-1 | раздел «Цикл 32» в `TEST_CATALOG.md`: V32, CY32, таблица §32.12.2, M32-01…09 | формулировки — сразу |
| QA-2 | скриншоты goods «до» (M32-07) и салона «до» **со `develop` до влития FE-2**; прогон M32 после FE-5/FE-6 | FE-5, FE-6 |
| QA-3 | CHANGELOG «Не выпущено — цикл 32»: одна кнопка сохранения, карточка правил, маска телефона (Q-32-6), удалён `CompanyAddressField`, текст правил записи (О-32-3) | все |

### §32.14.2 Что параллельно, что последовательно

```
День 1 ──► BE-1 ─────────────────────┐          FE-0 ─┐
          BE-2 (вливать после FE-0) ─┘          FE-1 ─┼─► FE-2 ─► FE-3 ─┐
                                                      └─► FE-4 ─────────┼─► FE-5 ─► FE-6 (P2)
                                                                        └────────► QA-2, QA-3
```
- **Backend и frontend полностью параллельны.** Общая точка одна — контракт, он готов. Бэкенд не пишет продуктовый код,
  фронт не ждёт бэкенд: сервер уже ведёт себя так, как описано в контракте §32.27, а CY32 это закрепляют.
- Внутри фронта: FE-2 → FE-3 (салонный адаптер над общей карточкой). FE-4 идёт параллельно FE-2/FE-3. FE-5 собирает
  всё в `SettingsTab` и поэтому последний. FE-6 — тот же файл, после FE-5.
- Если фронт делает один исполнитель: FE-0 → FE-1 → FE-2 → FE-4 → FE-3 → FE-5 → FE-6. FE-4 раньше FE-3, чтобы к FE-5
  обе карточки были готовы и старые тесты переносились одним заходом.
- Интеграционная проверка — M32 после FE-5 против живого API.

### §32.14.3 Порядок урезания (SPEC §1)

US-32-05 (FE-6) → T-32-02 (M32, QA-2 — кроме M32-07: регрессию goods проверяем всегда). P0 (FE-0…FE-5, BE-1, BE-2) не
режутся. Подсказка для старых телефонов (§32.7) — часть US-32-02, не режется: без неё принятый риск Q-32-6 становится
незаметным для владельца.

---

## §32.15. Риски

| # | Риск | Вероятность / вред | Что делаем |
|---|---|---|---|
| R32-1 | Регрессия профиля магазина при переносе в общий компонент | средняя / высокий | Обёртка с тем же путём и экспортом; 17 `it` `ShopProfileSection.test.tsx` без правок файла — гейт FE-2; `planProfileZone` для магазина сводится к строке 114 (§32.6.2) — отдельные кейсы в `profileZone.test.ts`; M32-07 со скриншотами «до/после» |
| R32-2 | Моки goods-тестов по `@/…` не перехватят относительные импорты общего компонента | низкая / тесты goods упадут | vitest сопоставляет по разрешённому пути — это один файл. Если на практике не сработает, общий компонент импортирует эти модули так же, как их мокают тесты. Ожидания тестов не трогаем |
| R32-3 | Уход с RHF вернёт баг цикла 13 (набранное стирается при перечитывании) | низкая / высокий | Инициализация один раз + `key={company.id}`; V32-11, V32-18 |
| R32-4 | `Intl` `longOffset` не поддерживается браузером | низкая / без модалки при ручном поясе | `null` → модалки нет, сервер всё равно проверит пояс; целевые браузеры (Chrome ≥ 95, Safari ≥ 15.4) поддерживают |
| R32-5 | Смещение нового пояса неизвестно при выключении ручного пояса без выбора города | определённая, редкая / неточное число в модалке | Модалка всегда, «→ пояс города …» без числа (§32.3 В). Альтернатива А (поле DTO) названа и отложена |
| R32-6 | Старые номера салонов искажены маской и при правке теряют добавочный | средняя / недовольство владельца | Принятый риск Q-32-6; подсказка «сейчас сохранён номер…» (§32.7); CHANGELOG; M32-09 |
| R32-7 | Переписанные тесты сочтут молчаливой сменой ожиданий | средняя / спор на ревью | Таблица §32.12.2 в плане и в `TEST_CATALOG.md` |
| R32-8 | `max-w-[760px]` и `gap-5` не попадут в CSS ezbook | низкая | Классы уже используются в `src/**` или сканируются — проверка M32-01; guard цикла 31 защищает goods |
| R32-9 | Удаление `CompanyAddressField` сломает неучтённого потребителя | низкая | `tsc` и сборка упадут сразу; поиск на `49f0d60` — только `SettingsTab`, свой тест и мок goods |
| R32-10 | Строка правил «действуют сразу…» расходится с ожиданием заказчика («к новым записям») | низкая / вопрос заказчика | Текст — правда по коду (§32.8.2). Если заказчик хочет «только к новым», это изменение сервера и отдельный цикл |
| R32-11 | Уже запланированные напоминания не сдвигаются при смене пояса салона | существующее поведение / напоминание придёт по старому поясу | Модалка это не скрывает («напоминания — для записей, созданных или перенесённых после смены»). Пересчёт очереди — вне цикла, находка C32-1 для `CURRENT_STATE` |

Безопасность и секреты: новых секретов нет, права не меняются, все проверки остаются на сервере. Клиентская проверка
названия, горизонта и пояса — только удобство.

---

## §32.16. Отклонения от буквы SPEC и решения сверх неё (читать обязательно)

| # | Что | Почему |
|---|---|---|
| О-32-1 | Правки API нет. Пробел «пояс города при ручном поясе» закрыт текстом модалки без числа | R-1: правка DTO ради одного крайнего сценария дороже пользы (§32.3) |
| О-32-2 | Модалка при выключении ручного пояса без выбора города показывается всегда | Смещение клиенту неизвестно, спросить честнее, чем промолчать |
| О-32-3 | Строка-пояснение правил записи: «Изменения действуют сразу — и для новых записей, и для переноса или отмены уже созданных. Предоплата запрашивается только в новых записях.» вместо «применяются к новым записям» | SPEC R-3 просил сверить с сервером; по коду гипотеза спеки неверна (§32.8.2) |
| О-32-4 | Подсказка для телефона не в каноне (только салон) | Делает видимым принятый риск Q-32-6 и выполняет критерий «уже сохранённое значение уходит без изменений» явно для владельца |
| О-32-5 | Флажок ручного пояса недоступен, пока город не выбран | Сервер без города пояс проверяет, но не применяет — иначе «Сохранено» было бы неправдой |
| О-32-6 | У IANA-поля появилась подпись «Часовой пояс (IANA)» | NFR «у каждого поля есть подпись»; сейчас это голый `input` |
| О-32-7 | `aria-describedby` у города (проп `describedBy` у `CityCombobox`) — в обоих кабинетах | NFR доступности; у goods изменение невидимо, тесты goods не затронуты (город там замокан) |
| О-32-8 | `CompanyAddressField` удалён вместе с тестом; из goods-теста убрана строка мока несуществующего модуля | Потребителей не осталось; мёртвый код (конвенция цикла 22) |
| О-32-9 | Тела PUT карточек собираются с `satisfies` сгенерированных типов цикла 32 | Автоматическая проверка «фронт шлёт форму контракта» средствами, уже стоящими в CI |
| О-32-10 | Салон без записи в `['my-companies']` (не владелец) не видит карточек профиля и правил | Раньше рисовалась пустая форма, и её сохранение перезаписало бы поля пустыми строками |
| О-32-11 | Заголовки галереи и каталога в goods остаются как есть (`h3 text-lg` и `font-serif text-xl`) | SPEC §3: страница goods не меняется, кроме переезда профиля. Разнобой заголовков goods — кандидат в следующий цикл |
