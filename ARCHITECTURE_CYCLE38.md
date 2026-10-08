# ARCHITECTURE — цикл 38 ServiceBooking: единый шаблон главной, FAQ, публичные тарифы «Заказов»

**Разделы §38.0–§38.17.** Контракт — `API_CONTRACT_CYCLE38.md` §38.20–§38.27, машиночитаемая схема — `contracts/cycle38/openapi.yaml`.
В коде и документах ссылаться с именем файла: `ARCHITECTURE_CYCLE38.md §38.4.2`.

**На входе:**
- `SPEC_CYCLE38_UNIFIED_LANDING.md` (далее SPEC);
- `BRIEF_CYCLE38_UNIFIED_LANDING.md`: **«Ответы заказчика» (п. 1–6) и «Уточнения» (п. 7–8) приоритетнее SPEC**;
- код ветки `cycle/038-unified-landing-template` (= `develop` `55d01ee` + документы `8ec6764`). Всё, что ниже названо «есть», сверено по файлам.

Ветку подготовил devops-инженер, архитектор её не трогает. Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` не меняются.
Документы цикла лежат в корне с суффиксом `_CYCLE38`.

| Файл | Что в нём | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE38.md` (этот) | решения, API шаблона, раскладка файлов, миграция главных, тарифы «Заказов», тесты, задачи | все |
| `API_CONTRACT_CYCLE38.md` | новый `GET /api/pricing/orders`; что не меняется (`GET /api/pricing`); команда `ops tariffs apply` | backend, frontend, QA |
| `contracts/cycle38/openapi.yaml` | **источник истины по форме** нового маршрута | backend, frontend (prism, генерат типов), QA (schemathesis), CI |

---

## §38.0. Что меняют решения заказчика (сводка поправок к SPEC)

| Пункт SPEC | Решение заказчика | Что это значит для команды |
|---|---|---|
| Q38-1, §6.2 | «Лавка» 690 / «Магазин» 1 490 / «Сеть магазинов» от 2 990 утверждены | Значения §6.2 SPEC сеются как есть (§38.10.1). |
| Q38-2 | бесплатный тариф в сетке, название «Бесплатный» | Сидер выравнивает бесплатный тариф «Заказов» (§38.10.2 п. 5). |
| Q38-3, B-5, B-8 (часть про флаг), критерий «404 пока выключено» | **Переключателя нет.** Сетка публична сразу. Юридический гейт — процесс релиза | Нет ключа `pricing.orders.public-enabled`, нет правки экрана настроек админки, нет проверки `TermsOwner` в коде. Единственное условие показа — есть хотя бы один активный публичный тариф линейки Orders (§38.9.2). На бою сетка появляется, когда оператор запускает `ops tariffs apply` после ответа юриста (§38.15). |
| Q38-4 + уточнение 7 | каталог под первым экраном; фото салона **не** в первом экране, а в необязательном «медиа-блоке» после каталога | Первый экран шаблона без картинки (§38.3.3). Слот `media` (§38.3.3). |
| Q38-5 | без скриншотов у «Записи», но слот есть, без скриншота — заглушка «Здесь будут скриншоты», один раз | §38.3.6. |
| Q38-6 | push на бою включены: «напоминание накануне визита» и push-формулировки **оставить** | Критерий SPEC «на главной нет фразы «напоминание накануне визита»» **отменён**. Шаги «Записи» берутся из текущего `#how` дословно. В FAQ «Заказов» п. 4 можно сказать про уведомление на странице заказа (§38.6.2). |
| уточнение 8 | названия и адреса на карточках каталога не обрезаются | Общая карточка каталога без `truncate` (§38.5). Необязательная задача P2 SPEC становится обязательной: одно место правки для двух сервисов. |

---

## §38.1. Стек и что добавляется

| Что | Решение | Почему |
|---|---|---|
| Фронт | React 18 + TS 5.5 + Tailwind 3.4 + TanStack Query + Vitest 3.2 — без изменений | шаблон — набор React-компонентов, не генератор и не пакет (BRIEF «словарь») |
| Где лежит шаблон | `frontend/src/components/landing/**` | `src/components/**` уже в `GOODS_SCANNED_EZBOOK_DIRS`: goods импортирует его, Tailwind goods его сканирует, `goods-shared-sources.js` не меняется |
| Бэкенд | ASP.NET Core, EF Core, PostgreSQL — без изменений | новый маршрут — тонкое чтение из памяти, как `/api/pricing` |
| Миграции БД | **нет** | все нужные поля у `SubscriptionPlanConfig` есть с цикла 24. Тарифы сеются командой, а не миграцией (иначе попадут в базу каждого функционального теста, как объяснено в `TariffCatalogSeeder`) |
| Новые пакеты npm/NuGet | **нет** | — |
| Контракт | OpenAPI 3.0.3 `contracts/cycle38/openapi.yaml` + `openapi.json` (для `OpenApiContract.Load("cycle38")`) + генерат `src/types/api-cycle38.generated.ts` | как в циклах 31–35 |

---

## §38.2. Раскладка файлов

```
frontend/src/components/landing/            ← ШАБЛОН (общий, goods импортирует через @/components/landing/...)
  types.ts                  типы конфига (§38.3) — только типы, без JSX
  classes.ts                общие строки классов секций (одно место для отступов/заголовков)
  ServiceLanding.tsx        корень: порядок секций, <main>, решает, где показать заглушку скриншота
  LandingHero.tsx           секция 1
  LandingCatalogFrame.tsx   секция 2 (рамка вокруг каталога сервиса)
  LandingMedia.tsx          слот «медиа-блок» после каталога (необязательный)
  LandingClientsSection.tsx секция 3 — разметка бывшего goods BuyersBlock
  LandingBusinessSection.tsx секция 4 — разметка бывшего goods BusinessBlock
  LandingStepsPanel.tsx     панель «3 шага» (общая для 3 и 4)
  LandingScreenshot.tsx     бывший goods ScreenshotFigure + LandingScreenshotPlaceholder
  LandingActionLink.tsx     кнопка/ссылка по LandingAction (anchor | route | auth-route)
  LandingPricingSection.tsx секция 5 — бывший PricingTeaser, данные через PricingLine
  FaqSection.tsx            секция 6 — доступный FAQ
  CatalogCard.tsx           общая карточка каталога (салон / магазин), без обрезки названия
  *.test.tsx, landing.guard.test.ts

frontend/src/components/pricing/            ← общие тарифы (goods уже может импортировать)
  pricingLine.ts            ТИПЫ PricingLine/PricingGridView/PricingPlanView + хук usePricingGrid (новый)
  zapisPricingLine.ts       линейка «Записи»: GET /api/pricing → PricingGridView (новый)
  PricingPageBody.tsx       тело страницы /pricing для любой линейки (перенос из src/pages/PricingPage.tsx)
  PlanCard.tsx              ПРАВКА: подписи лимитов приходят строками plan.limitLines
  PricingNavLink.tsx        ПРАВКА: проп line
  OptionRow.tsx             без изменений
  PricingTeaser.tsx         УДАЛЯЕТСЯ (заменён LandingPricingSection)

frontend/src/pages/
  HomePage.tsx              ПРАВКА: только <ServiceLanding config={zapisLanding} catalog={<ZapisCompanyCatalog/>} />
  home/zapisLanding.ts      конфиг главной «Записи» (тексты, якоря, медиа salonHero) — новый
  home/zapisFaq.ts          8 вопросов «Записи» — новый
  home/ZapisCompanyCatalog.tsx  каталог салонов (вынесен из HomePage как есть) — новый
  PricingPage.tsx           ПРАВКА: <PricingPageBody line={zapisPricingLine} … />

frontend/goods/src/
  pages/CatalogHomePage.tsx ПРАВКА: только <ServiceLanding config={goodsLanding} catalog={<ShopCatalog/>} />
  pages/PricingPage.tsx     новый: <PricingPageBody line={ordersPricingLine} … />
  landing/goodsLanding.ts   конфиг главной «Заказов» (тексты BuyersBlock/BusinessBlock дословно) — новый
  landing/goodsFaq.ts       8 вопросов «Заказов» — новый
  components/ShopCatalog.tsx форма поиска + «Поделиться» + section#shop-list (вынесено из CatalogHomePage как есть) — новый
  api/ordersPricing.ts      GET /api/pricing/orders (null на 404) — новый
  pricing/ordersPricingLine.ts  линейка «Заказов» → PricingGridView — новый
  components/BuyersBlock.tsx, BusinessBlock.tsx, ScreenshotFigure.tsx  УДАЛЯЮТСЯ
  components/GoodsNavbar.tsx ПРАВКА: <PricingNavLink line={ordersPricingLine} …/>
  GoodsApp.tsx              ПРАВКА: <Route path="/pricing">, '/pricing' в CONSENT_GATE_BYPASS_PATHS

ServiceBooking.API/
  Services/Showcase/Tariffs/OrdersTariffCatalog.cs     чистое описание сетки «Заказов» — новый
  Services/Showcase/Tariffs/TariffCatalogSeeder.cs     ПРАВКА: раздел «Заказы» в plan/apply
  Services/Billing/OrdersPricingCatalogBuilder.cs      чистое построение OrdersPublicPricingDto — новый
  Services/Billing/PricingCatalogCache.cs              ПРАВКА: GetOrdersAsync, свой ключ кеша, Invalidate чистит оба
  Controllers/PricingController.cs                     ПРАВКА: GET api/pricing/orders
  DTOs/Billing/PricingDtos.cs                          ПРАВКА: OrdersPublicPricingDto, OrdersPublicPlanDto (дописываются, старые не трогаются)

contracts/cycle38/openapi.yaml (+ openapi.json)        новый
contracts/cycle23/goods-routes.json                     ПРАВКА: "/pricing" в spaRoutes
contracts/cycle36/test-areas.json                       ПРАВКА: новые префиксы (§38.12.4)
```

Правило зависимостей: `src/components/landing/**` и `src/components/pricing/**` **не импортируют** ничего из `src/pages/**` и из
`goods/**`. Всё сервисное (ассеты, API «Заказов», конфиги) приходит пропсами. Это держит guard-тест `sharedSources.guard.test.ts`
зелёным без правки `goods-shared-sources.js`.

---

## §38.3. API шаблона

### §38.3.0. Принцип

Страница сервиса = **один объект конфига** + **свой компонент каталога** + вызов `ServiceLanding`:

```tsx
// frontend/src/components/landing/ServiceLanding.tsx
export function ServiceLanding(props: { config: LandingConfig; catalog: ReactNode }): JSX.Element
```

- Порядок секций, контейнер, отступы, типографика, классы — **только в шаблоне**. В типах конфига нет полей `className`, `style`,
  `as` — перекрасить секцию из конфига нельзя по построению.
- Конфиги сервисов — файлы `.ts` (не `.tsx`), без JSX. Это проверяет `landing.guard.test.ts` (§38.12.1).
- Один объект, а не пропсы по секциям: порядок и состав задаются одним местом, а «Дома» и «Бани» добавят файл конфига, не трогая
  разметку (SPEC §8 «Расширяемость»).

### §38.3.1. Типы (`frontend/src/components/landing/types.ts`)

Ниже — обязательная форма. Имена полей менять можно только вместе с этим документом.

```ts
import type { IconName } from '../ui/Icon'            // ПРАВКА Icon.tsx: `export type IconName`
import type { PricingLine } from '../pricing/pricingLine'

export type AnchorHref = `#${string}`
export interface AnchorAction    { kind: 'anchor'; href: AnchorHref; label: string }          // <a href="#…">
export interface RouteAction     { kind: 'route'; to: string; label: string }                 // <Link to>
/** Адрес зависит от входа: useAuthStore(s => s.isAuthenticated()). */
export interface AuthRouteAction { kind: 'auth-route'; guestTo: string; authedTo: string; label: string }
export type LandingAction = AnchorAction | RouteAction | AuthRouteAction

export interface LandingStep { icon: IconName; title: string; text: string }
/** Ровно три шага: 2 или 4 — ошибка tsc. */
export type ThreeSteps = readonly [LandingStep, LandingStep, LandingStep]
export interface LandingStepsPanel { title: string; titleId: string; steps: ThreeSteps }

export interface LandingShotSource { media: string; srcSet: string; width: number; height: number }
export interface LandingScreenshot {
  src: string; srcSet?: string; width: number; height: number
  sources?: readonly LandingShotSource[]; alt: string; caption: string
}

export interface LandingHeroConfig {
  eyebrow: string
  /** Строка — одна строка заголовка; объект — «строка + выделенная часть» (выделение с новой строки). */
  title: string | { lead: string; accent: string }
  intro: string
  primaryAction?: AnchorAction | RouteAction
  /** Якорь на заголовок секции 3 («Как сделать заказ» / «Как записаться»). */
  howTo: AnchorAction
}

export interface LandingCatalogFrameConfig { id: string; ariaLabel: string }

/** Уточнение 7: необязательный медиа-блок после каталога. */
export interface LandingMediaConfig { src: string; alt: string; width: number; height: number; caption?: string }

export interface LandingClientsConfig {
  eyebrow: string
  title: string
  titleId: string                       // h2, tabIndex=-1, цель якоря howTo
  text: string
  actions: readonly [LandingAction] | readonly [LandingAction, LandingAction]   // первая — основная (тёмная, со стрелкой)
  stepsPanel: LandingStepsPanel
  list: { title: string; titleId: string; items: readonly [string, ...string[]]; note?: string }
  screenshot?: LandingScreenshot
}

export interface LandingBusinessConfig {
  eyebrow: string
  title: string
  titleId: string
  text: string
  actions: readonly [LandingAction, LandingAction]
  benefits: readonly [string, ...string[]]
  screenshot?: LandingScreenshot
  stepsPanel: LandingStepsPanel
}

export interface LandingPricingConfig {
  line: PricingLine
  /** Фраза перед «— от {цена}»: «Онлайн-запись и аналитика» / «Приём заказов с самовывозом». */
  lead: string
}

export interface FaqItem {
  question: string
  /** Простой текст. Массив — абзацы. HTML не интерпретируется никогда. */
  answer: string | readonly [string, ...string[]]
  /** Необязательная ссылка после ответа (внутренний маршрут). */
  link?: { to: string; label: string }
}
/** Не меньше 6 — проверяет tsc; не больше 8 — проверяет тест конфига (§38.12.1). */
export type FaqItems = readonly [FaqItem, FaqItem, FaqItem, FaqItem, FaqItem, FaqItem, ...FaqItem[]]

export interface LandingConfig {
  hero: LandingHeroConfig
  catalog: LandingCatalogFrameConfig
  media?: LandingMediaConfig
  clients: LandingClientsConfig
  business: LandingBusinessConfig
  pricing?: LandingPricingConfig
  faq: { items: FaqItems }
}
```

Публичные экспорты шаблона (кроме типов): `ServiceLanding`, `LandingClientsSection`, `LandingBusinessSection`, `FaqSection`,
`LandingPricingSection`, `CatalogCard`. Секции экспортируются, чтобы тесты сервисов могли рендерить их по отдельности.
Сигнатуры секций:

```ts
LandingClientsSection(props: { config: LandingClientsConfig; placeholder: boolean })
LandingBusinessSection(props: { config: LandingBusinessConfig; placeholder: boolean })
FaqSection(props: { items: FaqItems })
LandingPricingSection(props: { config: LandingPricingConfig })
```
`placeholder` учитывается только когда у секции нет `config.screenshot` (§38.3.6).

### §38.3.2. Порядок и рамки секций (фиксирует `ServiceLanding`)

```
<main class="max-w-[1180px] mx-auto px-4 sm:px-8 pt-10 md:pt-16 pb-10">   ← как сейчас у CatalogHomePage
  1  LandingHero                       (h1)
  2  LandingCatalogFrame               <section id={catalog.id} aria-label={catalog.ariaLabel} class="mt-8 scroll-mt-24">{catalog}</section>
  2a LandingMedia                      если config.media
  3  LandingClientsSection             <section aria-labelledby={clients.titleId}> (h2 id=titleId tabIndex=-1)
  4  LandingBusinessSection            <section aria-labelledby={business.titleId}> (h2)
  5  LandingPricingSection             если config.pricing и сетка получена и в ней есть тарифы; иначе null
  6  FaqSection                        <section id="faq" aria-labelledby="faq-title"> (h2 «Частые вопросы»)
</main>
```

- Футер в шаблон не входит: `Footer` (ezbook, `App.tsx`) и `GoodsFooter` (`GoodsApp.tsx`) остаются в раскладке приложения.
  Свой `<footer>` из `HomePage.tsx` удаляется — на `/` ezbook становится один футер.
- `<main>` на ezbook раньше не было (страница была `<div>`); в `App.tsx` другого `<main>` нет, дубля не будет.
- Секции 3–6 используют одну и ту же обёртку `SECTION = 'mt-20 md:mt-28 border-t border-line pt-12'` и одни константы
  заголовков из `classes.ts`. Разметка секций 3 и 4 переносится из `BuyersBlock`/`BusinessBlock` **дословно** (классы, порядок
  элементов, `aria-*`, `aria-hidden` на иконках), меняются только источники текста.

### §38.3.3. Первый экран и медиа-блок

- `LandingHero` — разметка заголовка `CatalogHomePage` сейчас: обёртка `max-w-[720px]`, подзаголовок
  (`text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark`), `h1` (`font-serif text-[36px] sm:text-[48px] …`),
  абзац `mt-4`. Картинки справа нет (уточнение 7).
- `title` объектом: `{lead}<br/><em class="text-gold-dark italic">{accent}</em>` внутри того же `h1`.
- Без `primaryAction` якорь `howTo` рисуется как сейчас у goods (`<p class="mt-2"><a …>`), разметка goods не меняется.
  С `primaryAction`: `<div class="mt-6 flex flex-wrap items-center gap-x-6 gap-y-3">` → основная кнопка (стиль основной кнопки
  секции 3) и тот же якорь `howTo`.
- `LandingMedia`: `<figure class="mt-16 md:mt-20">` + `<img loading="lazy" decoding="async" width height alt
  class="block w-full h-auto aspect-[4/3] md:aspect-[21/9] object-cover rounded-[28px] border border-line">` + необязательный
  `figcaption`. Без заголовка (это иллюстрация, не секция). `width`/`height` обязательны — без них сдвиг вёрстки.

### §38.3.4. Каталог

Шаблон рисует только рамку (`id`, `aria-label`, `mt-8`, `scroll-mt-24`). Всё внутри — компонент сервиса, со своим состоянием
и своими классами (это разрешено SPEC §4.1: «Фильтры и карточки шаблон **не** рисует»). Карточки — общий `CatalogCard` (§38.5).

| Сервис | `catalog.id` | `catalog.ariaLabel` | Что внутри |
|---|---|---|---|
| «Запись» | `companies` | `Компании и салоны` | `ZapisCompanyCatalog`: заголовок с городом (`h2`), поиск с задержкой 300 мс, `CityCombobox`, «Все города», сетка `CatalogCard`, пагинация, пустые состояния, ошибка. Перенос из `HomePage.tsx` как есть, без собственного контейнера/полей (`max-w`, `px-8`, `pt-[88px]` уходят — их даёт `main`). |
| «Заказы» | `catalog` | `Каталог магазинов` | `ShopCatalog`: `form role="search"`, «Поделиться каталогом города», `section#shop-list` (`tabIndex=-1`, `aria-label="Магазины"`, `aria-live="polite"`, `scroll-mt-24`), `ul[data-testid=catalog-list]`, `li` → `CatalogCard` с `data-testid="catalog-shop"`. Состояние (URL-фильтры, `homeCity`, запрос) переезжает из `CatalogHomePage` без изменений. |

`#shop-list` остаётся внутренним `section` каталога «Заказов»: на него ведут кнопка «Выбрать магазин» и тесты, `aria-live`
висит только над списком, а не над формой.

### §38.3.5. Секции 3 и 4

- Кнопки: `LandingActionLink` рисует `anchor` → `<a href>`, `route` → `<Link to>`, `auth-route` → `<Link to={authed ? authedTo : guestTo}>`.
  Первая кнопка в массиве — основная (`bg-ink … !text-cream`, стрелка `arrow-right` с `aria-hidden`), вторая — вторичная
  (`bg-white … border border-line`). Классы — из нынешних блоков.
- Шаги: `LandingStepsPanel` — `div.rounded-[28px].bg-cream-deep` → `h3#{titleId}` → `ol[aria-labelledby={titleId}].list-none.grid.md:grid-cols-3.gap-10`
  → `li` (круг 46 px с `Icon size=20 strokeWidth=1.6 aria-hidden`, `h4`, `p`). Один компонент на обе секции — паритет классов
  T30-13 теперь гарантирован построением.
- Секция 3, нижний блок: `grid md:grid-cols-[1.05fr_0.95fr]` → слева `h3#{list.titleId}` + `ul[aria-labelledby]` с галочками +
  `note` (`mt-6 text-[13px] text-muted`); справа скриншот или заглушка (§38.3.6).
- Секция 4: верхняя сетка (текст + кнопки | `ul` преимуществ), затем скриншот (если есть), затем панель шагов — как `BusinessBlock`.
- Внутри секций 3 и 4 никаких дополнительных `ul`, `a`, `img`, `h3` сверх перечисленного: тесты T30-07, QA27-06, QA27-08, QA30-08
  считают их по первому вхождению и по количеству.

### §38.3.6. Слот скриншота и заглушка (Q38-5)

- Константа `SCREENSHOT_PLACEHOLDER_TEXT = 'Здесь будут скриншоты'` живёт **только** в `LandingScreenshot.tsx`.
- «Один раз» реализуется в `ServiceLanding`: заглушка рисуется в **первом** слоте без скриншота в порядке «секция 3 → секция 4»
  (`placeholder={true}` получает только эта секция); остальные пустые слоты не рисуют ничего. У «Записи» оба слота пустые →
  одна заглушка в секции 3 (на месте скриншота страницы заказа у goods). У «Заказов» оба скриншота есть → заглушки нет.
- Разметка заглушки: `<div data-testid="landing-screenshot-placeholder" class="mx-auto w-full max-w-[320px] md:max-w-[340px]
  aspect-[390/600] rounded-[20px] border border-dashed border-line-strong bg-cream-deep flex items-center justify-center
  text-center px-6"><p class="text-[15px] text-muted">Здесь будут скриншоты</p></div>`. Не `img`, не `figure`.
- Если заказчик имел в виду «заглушка в каждом пустом слоте», правка — одна строка в `ServiceLanding`; тест T38-02 это зафиксирует.

### §38.3.7. Тарифы на главной (`LandingPricingSection`)

Логика нынешнего `PricingTeaser`, данные через `PricingLine` (§38.7):
- пока грузится, при 404 (`null`), при ошибке и при пустом списке тарифов — `null`;
- `<section aria-labelledby="pricing-title" class={SECTION}>`, `h2#pricing-title` «Тарифы», строка
  `{lead} — от {formatMonthlyPrice(самый дешёвый платный)}` (платный = `!isFree && pricePerMonth > 0`; нет платного — строки нет),
  ссылка «Все тарифы» → `/pricing`;
- до 3 карточек по `sortOrder`: `h3` название, цена, затем `plan.limitLines`, затем до 3 `highlights`. Карточка — та же, что в
  `PricingTeaser` сейчас (`bg-white border border-line rounded-[20px] p-6`).

У «Записи» на карточке тизера появляются строки лимитов «до N компаний / сотрудников» — сознательное следствие одного шаблона.

---

## §38.4. Миграция главных

### §38.4.1. «Заказы» (`CatalogHomePage.tsx`)

1. Вынести форму, «Поделиться» и `section#shop-list` вместе со всем состоянием в `goods/src/components/ShopCatalog.tsx`
   **без изменения поведения**: URL-параметры `search/openNow/page`, `/city/:cityId`, `readStoredCity/writeStoredCity`,
   `placeholderData`, `retry: false`, тексты состояний.
2. `goods/src/landing/goodsLanding.ts` — тексты `BuyersBlock`/`BusinessBlock` **дословно** (тесты T27/T30 их сверяют):

| Поле | Значение |
|---|---|
| `hero` | eyebrow «Заказы с самовывозом», title «Где заказать в вашем городе», intro «Выберите магазин, соберите заказ и заберите его в удобное время.», `howTo: {kind:'anchor', href:'#buyers-title', label:'Как сделать заказ'}`, без `primaryAction` |
| `catalog` | `{ id: 'catalog', ariaLabel: 'Каталог магазинов' }` |
| `media` | нет |
| `clients` | eyebrow «Для покупателей», `titleId: 'buyers-title'`, actions `[{anchor '#shop-list' «Выбрать магазин»}, {route '/orders' «Мои заказы»}]`, stepsPanel `{title:'Как сделать заказ', titleId:'buyers-steps-title', steps: …}`, list `{title:'Как следить за заказом', titleId:'buyers-track-title', items: 5 строк track, note:'Оплата — при получении в магазине.'}`, `screenshot: orderPageShot` |
| `business` | eyebrow «Для бизнеса», `titleId: 'biz-title'`, actions `[{auth-route guest '/register?returnTo=%2Fcabinet%2Fnew', authed '/cabinet/new', «Подключить магазин»}, {route '/cabinet' «Войти в кабинет»}]`, benefits 4 строки, `screenshot: boardShot`, stepsPanel `{title:'Как начать принимать заказы', titleId:'biz-steps-title', …}` |
| `pricing` | `{ line: ordersPricingLine, lead: 'Приём заказов с самовывозом' }` |
| `faq` | `goodsFaq` (§38.6.2) |

3. `CatalogHomePage` = `<ServiceLanding config={goodsLanding} catalog={<ShopCatalog />} />`.
4. Удалить `BuyersBlock.tsx`, `BusinessBlock.tsx`, `ScreenshotFigure.tsx`. Объекты из `shots.ts` совместимы с
   `LandingScreenshot` (`shots.ts` не меняется).

Сохраняются: `#shop-list` (`tabIndex=-1`, `aria-label="Магазины"`), `data-testid="catalog-list"`, `catalog-shop`, `aria-label` ссылки
магазина `"{name}, {openState.text}, {acceptanceText}"`, `#buyers-title` (`tabIndex=-1`), `buyers-steps-title`, `buyers-track-title`,
`biz-title`, `biz-steps-title`, `main` как предок `#shop-list`, порядок «якорь → `role=search`».

### §38.4.2. «Запись» (`HomePage.tsx`)

1. Вынести каталог в `src/pages/home/ZapisCompanyCatalog.tsx` **без изменения поведения**: `HOME_CITY_KEY = 'home-city'`,
   `loadStoredCity` с очисткой битой записи, задержка 300 мс, сброс страницы на 1 при смене фильтра, `PAGE_SIZE = 20`,
   queryKey `['companies-public', …]`, пустые состояния, ошибка. Внешний контейнер секции убрать (рамку даёт шаблон).
2. `src/pages/home/zapisLanding.ts`:

| Поле | Значение |
|---|---|
| `hero` | eyebrow «Онлайн-запись за пару минут», title `{lead:'Красота и уход,', accent:'подобранные под вас'}`, intro — текущий абзац дословно, `primaryAction: {anchor '#companies' «Найти специалиста»}`, `howTo: {anchor '#clients-title' «Как записаться»}` |
| `catalog` | `{ id: 'companies', ariaLabel: 'Компании и салоны' }` — `/#companies` продолжает работать |
| `media` | `{ src: salonHero, alt: 'Интерьер салона', width, height }` — `width/height` = реальные размеры `src/assets/salon-hero.jpg` (разработчик снимает их с файла) |
| `clients` | eyebrow «Для клиентов», title «Запишитесь к мастеру онлайн, без звонков», `titleId:'clients-title'`, text — черновик (одно предложение без новых обещаний, напр. «Выберите салон и мастера, посмотрите свободное время и запишитесь — без звонков и ожидания.»), actions `[{anchor '#companies' «Найти специалиста»}, {route '/my-visits' «Мои визиты»}]`, stepsPanel `{title:'Как записаться', titleId:'clients-steps-title', steps: три шага из нынешнего howItWorks ДОСЛОВНО, иконки user/calendar/check-circle}` (Q38-6: «напоминание накануне визита» остаётся), list `{title:'Что ещё можно', titleId:'clients-list-title', items: SPEC §4.2 (до 5 услуг за визит; запись другого человека с подтверждением полномочий; перенос и отмена в «Моих визитах» по правилам салона; отзыв после визита), note:'Оплата — в салоне.'}`, без `screenshot` |
| `business` | eyebrow «Для бизнеса», title «Салон принимает записи онлайн, а вы видите расписание мастеров в одном месте», `titleId:'biz-title'`, text — черновик без новых обещаний, actions `[{auth-route guest '/register', authed '/cabinet', «Подключить салон»}, {route '/cabinet' «Войти в кабинет»}]`, benefits — SPEC §4.2 (5 пунктов), stepsPanel `{title:'Как начать принимать записи', titleId:'biz-steps-title', steps: «Заведите компанию и услуги» (store) → «Добавьте мастеров и расписание» (users) → «Поделитесь страницей записи» (external-link)}`, без `screenshot` |
| `pricing` | `{ line: zapisPricingLine, lead: 'Онлайн-запись и аналитика' }` |
| `faq` | `zapisFaq` (§38.6.2) |

   **Сверить при реализации (T-38-08):** `/cabinet` закрыт `ProtectedRoute roles={['Master','CompanyOwner','SuperAdmin']}`. Если
   вошедший клиент без этих ролей попадает на нём в тупик, `authedTo` ставится на тот адрес, откуда клиент сейчас реально
   заводит компанию (проверить `RegisterPage`/`ProfilePage`), и решение записывается комментарием в `zapisLanding.ts`.
3. `HomePage` = `<ServiceLanding config={zapisLanding} catalog={<ZapisCompanyCatalog />} />`. Импорт `salonHero` переезжает в
   `zapisLanding.ts`. Встроенный `<footer>` удаляется.

### §38.4.3. Демо-режимы

Шаблон не знает о демо. `DemoBanner`, `DemoProductProvider`, `DemoMaintenanceGate` стоят в раскладке приложений и не меняются.
Бейдж витрины `ShowcaseBadge` и скрытие плашки «Онлайн-запись» у закрытой витрины переезжают в `ZapisCompanyCatalog` вместе с
картой компании (через слоты `CatalogCard`, §38.5). Существующие демо-тесты фронта должны остаться зелёными без правки.

---

## §38.5. Общая карточка каталога без обрезки (уточнение 8)

`frontend/src/components/landing/CatalogCard.tsx`:

```ts
export interface CatalogCardProps {
  to: string
  name: string
  logoUrl?: string | null
  ariaLabel?: string                 // goods: "{name}, {openState}, {acceptance}"
  testId?: string                    // goods: 'catalog-shop'
  subtitle?: string                  // goods: openState.text
  badge?: ReactNode                  // ezbook: <ShowcaseBadge className="mt-1.5" /> у витрины
  description?: string | null        // ezbook: описание компании (line-clamp-2 остаётся: это не название и не адрес)
  place?: string | null              // адрес (у goods — publicAddress(...))
  pill?: { text: string; tone: 'success' | 'info' | 'muted'; icon?: 'check' }
}
```

- Корень — `Link` с классами нынешних карточек + `h-full` (карточки в ряду сетки одной высоты, ряд растёт по самой высокой).
- Название: `h3.font-serif.text-[19px].font-medium.text-ink.break-words.[overflow-wrap:anywhere]` — **без** `truncate`. Длинное
  слово без пробелов переносится по символам.
- Адрес: `span.min-w-0.break-words.[overflow-wrap:anywhere]` без `truncate`; нижняя строка `flex items-start justify-between gap-3`,
  плашка `shrink-0`, иконка `map-pin` с `mt-0.5`.
- Цвета плашки: `success → bg-success-bg text-success`, `info → bg-info-bg text-info`, `muted → bg-cream-deep text-muted`
  (нынешняя `ACCEPT_STYLE` goods переезжает в отображение `acceptance → tone` в `ShopCatalog`).
- `CompanyLogoMark size="catalog"` — как сейчас (тесты V29-32/V29-33 его сверяют).
- ezbook оборачивает карточку в элемент сетки, goods — в `li` внутри `ul[data-testid=catalog-list]`.
- Сетка у обоих — `grid gap-6` + `repeat(auto-fill, minmax(320px, 1fr))`: на 360 px (поле 16 px × 2) остаётся 328 px ≥ 320.

---

## §38.6. FAQ

### §38.6.1. Компонент (`FaqSection.tsx`) — паттерн WAI-ARIA «disclosure»

```
<section id="faq" aria-labelledby="faq-title" class={SECTION}>
  <h2 id="faq-title" class={H2}>Частые вопросы</h2>
  <ul class="mt-8 divide-y divide-line border-y border-line" role="list">
    <li>
      <h3 class="m-0">
        <button type="button" id={qId} aria-expanded={open} aria-controls={aId}
                class="w-full flex items-start justify-between gap-4 py-5 text-left font-serif text-[19px] text-ink
                       focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-gold-dark">
          <span class="break-words">{question}</span>
          <Icon name="chevron-down" aria-hidden class={open ? 'rotate-180' : ''} />
        </button>
      </h3>
      <div id={aId} hidden={!open} class="pb-5 text-[15px] leading-[1.6] text-ink-soft max-w-[720px]">
        {абзацы — <p> на каждый элемент answer}
        {link && <Link to={link.to} class="…underline">{link.label}</Link>}
      </div>
    </li>
  </ul>
</section>
```

- Почему кнопка, а не `details/summary`: заголовок `h3` внутри `summary` по-разному читается экранными читалками, а в jsdom
  клавиатурная активация `summary` не моделируется — тест на Enter/пробел через `user-event` стал бы ненадёжным.
  `button` + `aria-expanded` даёт Enter/пробел нативно и проверяется однозначно.
- Состояние: `useState<ReadonlySet<number>>` — открыто может быть несколько пунктов; по умолчанию всё свёрнуто.
- Id: `useId()` + индекс; `aria-controls` всегда указывает на существующий элемент (панель в DOM, скрыта `hidden`).
- Ответ выводится **только** как текстовые узлы React. `dangerouslySetInnerHTML` в файле запрещён (guard-тест §38.12.1).
- `role="region"` у панелей не ставится: при 6–8 пунктах это засоряет навигацию по областям (рекомендация APG).

### §38.6.2. Данные

Лежат рядом с конфигом сервиса: `src/pages/home/zapisFaq.ts`, `goods/src/landing/goodsFaq.ts`. Тексты — черновики SPEC §5 с
поправками заказчика:

| Сервис | Пункт | Поправка к SPEC §5 |
|---|---|---|
| «Запись» | 7 «Сколько стоит EZBOOK для салона?» | «…— в разделе «Тарифы».» + `link: { to: '/pricing', label: 'Тарифы и цены' }` |
| «Запись» | остальные | как в SPEC §5.1. Пометки «сверить» разработчик проверяет по коду; если UI-путь не подтвердился — правит текст, а не продукт |
| «Заказы» | 4 «Как узнать, что заказ готов?» | Q38-6 разрешает: после первого предложения можно добавить «На странице заказа можно включить уведомление, если браузер это разрешает.» (формулировка `BuyersBlock` track[2]) |
| «Заказы» | 7 «Сколько стоит подключить магазин?» | переключателя нет → основной вариант текста («…— в разделе «Тарифы».») + `link: { to: '/pricing', label: 'Тарифы и цены' }`. Запасной вариант SPEC про «Подписку» не нужен |
| «Заказы» | остальные | как в SPEC §5.2 |

Ни один ответ не называет цен (они меняются в админке, FAQ — в коде), сроков и онлайн-оплаты.

---

## §38.7. Тарифы на фронте: одна абстракция «линейка»

### §38.7.1. `src/components/pricing/pricingLine.ts`

```ts
import type { PricingOptionDto } from '../../types/pricing'

export interface PricingPlanView {
  id: string; name: string; description: string | null; pricePerMonth: number
  highlights: readonly string[]; sortOrder: number; isFree: boolean; isTrial: boolean
  /** Готовые строки лимитов для карточки: «до 3 магазинов», «Заказы без ограничений»… — формирует линейка. */
  limitLines: readonly string[]
}
export interface PricingGridView {
  plans: readonly PricingPlanView[]
  options: readonly PricingOptionDto[]        // у «Заказов» всегда []
  notice: string
  legalNotice: string | null
}
export interface PricingLine {
  /** ['public-pricing', 'services'] | ['public-pricing', 'orders'] — одна линейка = один ключ кеша. */
  queryKey: readonly ['public-pricing', string]
  /** null = 404 (сетки нет) — ожидаемое состояние, не ошибка. */
  fetchGrid: () => Promise<PricingGridView | null>
}
export function usePricingGrid(line: PricingLine) {
  return useQuery({ queryKey: line.queryKey, queryFn: line.fetchGrid, retry: false, staleTime: 20_000 })
}
```

- Ключ `['public-pricing']` сейчас общий у `PricingTeaser`, `PricingPage`, `PricingNavLink` с «сырым» DTO. Все три переходят на
  `usePricingGrid(line)`, поэтому под ключом линейки всегда лежит `PricingGridView` — разных форм данных под одним ключом нет.
  Других пользователей ключа нет (проверено grep по `frontend/`). На главной ezbook по-прежнему **один** запрос `/api/pricing`
  (шапка и тизер делят ключ), на goods — один `/api/pricing/orders`.
- `zapisPricingLine` (`src/components/pricing/zapisPricingLine.ts`): `fetchGrid = () => pricingApi.getPublicPricing().then(toView)`;
  `limitLines = [formatIncludedLimitLine(includedCompanies, 'компании', 'компании', 'компаний'),
  formatIncludedLimitLine(includedEmployees, 'сотрудники', 'сотрудника', 'сотрудников')]` — ровно нынешний вывод `PlanCard`;
  `isTrial = dto.isTrial ?? false`.
- `ordersPricingLine` (`goods/src/pricing/ordersPricingLine.ts`): `fetchGrid = () => ordersPricingApi.get().then(toView)`;
  `limitLines` в порядке:
  1. `formatIncludedLimitLine(includedShops, 'магазины', 'магазина', 'магазинов')`
  2. `formatIncludedLimitLine(includedMembers, 'участники', 'участника', 'участников')`
  3. `` `${formatIncludedLimit(includedProductsPerShop, 'товара', 'товаров')} в магазине` `` — поле никогда не `null` (§38.9.3), «без ограничений» у товаров не бывает
  4. `includedOrdersPerMonth === null ? 'Заказы без ограничений' : `${formatIncludedLimit(n, 'заказа', 'заказов')} в месяц``

  `options: []`, `isTrial: false`.
- `formatIncludedLimit` печатает число через `count.toLocaleString('ru-RU')` («до 1 500 заказов в месяц»). Для «Записи» (числа < 1000)
  вывод не меняется; тест `pricingFormat.test.ts` дополняется случаем 1500.

### §38.7.2. Компоненты

| Компонент | Изменение |
|---|---|
| `PlanCard` | проп `plan: PricingPlanView`; вместо двух зашитых строк — `plan.limitLines.map(...)` той же разметкой `li`. Слова «компании/сотрудники» из файла уходят |
| `PricingPageBody` (новый, `src/components/pricing/`) | тело нынешней `PricingPage` целиком (скелетон, ошибка с «Попробовать снова», «Тарифы пока не опубликованы.» на 404, заголовок, `notice`, `legalNotice`, сетка `PlanCard`, опции, CTA). Пропсы: `{ line: PricingLine; documentTitle: string; cta: AuthRouteAction }`. Колонки сетки на `xl`: литеральная карта `{4: 'xl:grid-cols-4', 5: 'xl:grid-cols-5'}` по числу тарифов (у «Заказов» 4) |
| `src/pages/PricingPage.tsx` | `<PricingPageBody line={zapisPricingLine} documentTitle="Тарифы и цены — EZBOOK" cta={{kind:'auth-route', guestTo:'/register', authedTo:'/register', label:'Зарегистрировать компанию'}} />` — для «Записи» ничего не меняется |
| `goods/src/pages/PricingPage.tsx` | `<PricingPageBody line={ordersPricingLine} documentTitle="Тарифы и цены — EZBOOK Заказы" cta={{kind:'auth-route', guestTo:'/register?returnTo=%2Fcabinet%2Fnew', authedTo:'/cabinet/new', label:'Подключить магазин'}} />` |
| `PricingNavLink` | проп `line: PricingLine`; `Navbar` передаёт `zapisPricingLine`, `GoodsNavbar` — `ordersPricingLine` (на десктопе первым пунктом перед веткой входа, в мобильном меню первым пунктом; так же, как в `Navbar`). Ссылки нет, пока сетка не получена (404/ошибка/загрузка) |
| `PricingTeaser` | удаляется; его роль у `LandingPricingSection` |

### §38.7.3. Маршрут `/pricing` на goods

- `GoodsApp.tsx`: `<Route path="/pricing" element={<PricingPage />} />` **до** `/:slug`; `'/pricing'` в `CONSENT_GATE_BYPASS_PATHS`
  (как у ezbook: владелец с непринятой редакцией должен видеть цены).
- `contracts/cycle23/goods-routes.json`: `"/pricing"` в `spaRoutes`. Слово `pricing` уже в `reservedSlugs`, `SlugPolicy` не меняется.
  `goodsRoutes.test.ts` сверяет обе стороны.
- nginx goods уже отдаёт `index.html` на любой одиночный сегмент (так работает `/:slug`) — правка не нужна.

---

## §38.8. Модель данных

Схема БД **не меняется**. Используются существующие поля `SubscriptionPlanConfig` (цикл 24, §448.1):

| Поле | Смысл для линейки Orders | Публичное поле DTO |
|---|---|---|
| `Line` | `CompanyKind.Orders` | — (фильтр) |
| `MaxCompanies` | магазинов на аккаунт | `includedShops` |
| `MaxEmployees` | участников вместе с владельцем | `includedMembers` |
| `MaxProductsPerShop` | товаров в магазине (`null` — тариф не ограничивает, действует потолок `Orders:MaxProductsPerShop` = 1000) | `includedProductsPerShop` = `min(значение ?? потолок, потолок)` |
| `MaxOrdersPerMonth` | заказов в календарный месяц на аккаунт | `includedOrdersPerMonth` |
| `IsActive`, `IsPublic`, `SortOrder`, `IsSystemFree`, `Name`, `Description`, `Highlights`, `PricePerMonth` | как у «Записи» | `isFree` = `IsSystemFree` |

`PlanOptionRules` получают строки для новых тарифов (§38.10.2). `PlatformSettings` получает только **необязательный** текстовый
ключ `pricing.orders.legal-notice` (как `pricing.legal-notice` у «Записи»: без экрана админки, ставится в БД, когда юрист даст текст).
Это текст, а не переключатель.

---

## §38.9. Публичная сетка «Заказов» на бэкенде

### §38.9.1. Форма эндпоинта: отдельный путь `GET /api/pricing/orders`

Альтернатива `GET /api/pricing?line=orders` отвергнута: тот же маршрут с другой формой тела (у «Заказов» нет опций и `isTrial`,
есть два новых лимита) — это `oneOf` в контракте и ветвление в одном действии. Отдельный путь даёт свою схему, свою строку в
`Cycle22RouteTable.golden.txt` и гарантирует, что `GET /api/pricing` без параметра не меняется ни на байт (его действие, DTO и
кеш не трогаются). Под `/api/pricing/*` других маршрутов нет, конфликта нет.

### §38.9.2. Когда 200, когда 404

- **200** — в линейке Orders есть хотя бы один тариф с `IsActive && IsPublic`, не являющийся служебным (`ShowcaseCatalog.IsServicePlan(id)` = false).
- **404 с пустым телом** — таких тарифов нет. Это и есть «сетки нет»: на бою до `ops tariffs apply` бесплатный тариф «Заказов»
  имеет `IsPublic = false` (сид цикла 24), значит 404, и фронт ничего не рисует.
- Переключателя `pricing.public-enabled` и проверки `TermsOwner` у этого маршрута **нет** (Q38-3). Включение/выключение сетки
  «Записи» на «Заказы» не влияет, и наоборот (тест).

### §38.9.3. Построение (`OrdersPricingCatalogBuilder`, чистая статика)

```csharp
public static OrdersPublicPricingDto Build(string version, IEnumerable<SubscriptionPlanConfig> plans, int productsCeiling,
    string? legalNotice, string notice = OrdersPricingCatalogBuilder.DefaultNotice)
```
- Фильтр: `Line == Orders && IsActive && IsPublic && !ShowcaseCatalog.IsServicePlan(Id)`. Явное исключение служебного «Демо» —
  страховка на случай, если его по ошибке сделают публичным в админке (SPEC: «не показывать нигде»).
- Сортировка: `SortOrder`, затем `PricePerMonth` (как у «Записи»).
- `Highlights`: та же разбивка по `\n`, `Trim`, не больше `PricingCatalogBuilder.PublicMaxHighlights` (5). Хелпер разбивки
  выносится в `internal static` и используется обоими билдерами (поведение «Записи» не меняется).
- `IncludedProductsPerShop = Math.Min(plan.MaxProductsPerShop ?? ceiling, ceiling)` — то же правило, что в
  `OwnerSubscriptionService` (строка с `ordersOptions.Value.MaxProductsPerShop`). Поле никогда не `null`.
- `DefaultNotice = "Подключение тарифа выполняет администратор платформы — оставьте заявку в кабинете, и мы свяжемся с вами."`
- Опций нет вовсе (B-7): в DTO нет поля `options`.
- Число подписчиков, правила опций, `IsActive`, `AllowNotificationChannel` наружу не выходят.

### §38.9.4. Кеш (`PricingCatalogCache`)

- `private const string OrdersCacheKey = "pricing:public:orders";` (ключ «Записи» `pricing:public` не переименовывается).
- `public async Task<CachedOrdersPricing?> GetOrdersAsync(CancellationToken ct)` — 60 с в `IMemoryCache`; кешируется и
  «пустой» результат (`null` → 404), чтобы анонимный поток 404 тоже не бил в БД.
- ETag — тот же `ComputeETag` (SHA-256 JSON camelCase, 16 hex), `Version` = значение ETag, заголовок `W/"…"`.
- `PricingCatalogCache` получает в конструктор `IOptions<OrdersOptions>` (потолок товаров).
- `Invalidate()` удаляет **оба** ключа (и `PublicEnabledCacheKey`, как сейчас). Его уже вызывают все действия
  `AdminPlansController`/`AdminOptionsController` и смена переключателя «Записи» — значит правка тарифа «Заказов» в админке видна
  сразу (B-4). Новых вызовов в контроллерах не нужно.

### §38.9.5. Контроллер

`PricingController`: действие `[HttpGet("api/pricing/orders")] [AllowAnonymous] GetOrdersPublicPricing` — 404 при `null`,
`If-None-Match` → 304, иначе `ETag` + `Cache-Control: public, max-age=60` + 200. Без rate limit (ответ из памяти, как `/api/pricing`).
`LegalConsentFilter` анонимный запрос не трогает. Строка маршрута добавляется в `Cycle22RouteTable.golden.txt` (тест эталона
маршрутов покажет точный текст строки — его и вписать).

Админского «предпросмотра» для «Заказов» нет: сетка публична, предпросмотр не нужен (админ видит тарифы в своём экране тарифов).

---

## §38.10. Сетка «Заказов» в БД: каталог и сидер

### §38.10.1. `OrdersTariffCatalog` (чистое описание, по образцу `ZapisTariffCatalog`)

```csharp
public sealed record OrdersTariff(Guid Id, string Name, decimal PricePerMonth, int? MaxCompanies, int? MaxEmployees,
    int? MaxProductsPerShop, int? MaxOrdersPerMonth, int SortOrder, string Description, IReadOnlyList<string> Highlights);
```

| | Id (литерал, навсегда) | Name | ₽/мес | магазинов | участников | товаров | заказов/мес | SortOrder |
|---|---|---|---|---|---|---|---|---|
| `Lavka` | `5a1e0c38-0000-4000-8000-000000000020` | Лавка | 690 | 1 | 5 | 300 | 1500 | 20 |
| `Shop` | `5a1e0c38-0000-4000-8000-000000000030` | Магазин | 1490 | 3 | 15 | 1000 | 5000 | 30 |
| `Chain` | `5a1e0c38-0000-4000-8000-000000000040` | Сеть магазинов | 2990 | null | null | 1000 | null | 40 |

Описания и преимущества — **дословно** SPEC §6.2 (утверждены, Q38-1). `Grid = [Lavka, Shop, Chain]`.

Свойства создаваемой строки (`ToEntity`): `Line = Orders`, `AllowOrders = true`, `AllowPublicListing = true`,
`AllowNotificationChannel = true`, `AllowOnlineBooking/AllowAnalytics/AllowMailing/AllowOnlinePayment = false`,
`PhotoQuotaMb = 100`, `PhotoRetention` и `NotifyDaysBefore` — как у бесплатного тарифа «Заказов» в сиде цикла 24,
`IsActive = true`, `IsPublic = true`, `IsSystemFree = false`, `IsSystemTrial = false`.

Константы бесплатного тарифа (Q38-2):

```csharp
// Значения сида цикла 24 (миграция Cycle24OrdersTimeNotifyTariffs) — байт в байт.
public const string LegacyFreeName = "Заказы · Бесплатно";            // = OrdersFreePlan.Name
public const string LegacyFreeDescription = "Бесплатный уровень линейки «Заказы».";
// Highlights в сиде = NULL; IsPublic = false; SortOrder = -1.
public const string FreeName = "Бесплатный";
public const string FreeDescription = "Чтобы начать: один магазин, приём заказов по ссылке, QR-коду и из каталога.";
public const string FreeHighlights = "Экран заказов со звуком нового заказа\nВремя получения: как можно скорее или к часу\nПоказ в каталоге goods";
public const int FreeSortOrder = 10;
```

(«Показ в каталоге goods» честен: миграция цикла 25 выставила бесплатному тарифу «Заказов» `AllowPublicListing = true`.)

### §38.10.2. Сидер: расширение `ops tariffs apply`, а не новая команда

Почему так: одна команда оператора, один замок `ops:tariffs`, одна транзакция; сброс демо уже вызывает
`TariffCatalogSeeder.ApplyAsync` внутри своей транзакции — сетка «Заказов» на demo.zakaz появится **без нового кода в
`DemoResetService`** (B-8). Цена: отчёт команды становится длиннее; формат существующих строк «Записи» не меняется.

Правила раздела «Заказы» в `RunAsync` (после раздела «Записи», до `SaveChangesAsync`):
1. Поиск существующей строки: по Id (глобально) → по имени без учёта регистра **только среди `Line == Orders`** (R-7). Тариф
   «Записи» с тем же именем на поиск не влияет.
2. Найдена — не трогать; в отчёт: `«Заказы»: уже есть, не трогаю: {Name} — {расхождения}`. `DescribeDivergences` для Orders
   сравнивает цену, магазины, участников, товары, заказы/мес, `IsPublic`, `IsActive`.
3. Не найдена — создать (`apply`) / «будет создан» (`plan`).
4. Правила опций (вопрос SPEC §11 п. 5):
   - **созданным** тарифам: для каждой неретированной опции — `Unavailable`, кроме `notifications.whatsapp` → `Extra`
     (как у бесплатного тарифа «Заказов» в миграции цикла 24: платный тариф не может разрешать меньше бесплатного);
   - **найденным** тарифам: только недостающие строки, и только `Unavailable` (семантически ничего не меняет: «нет строки» =
     «недоступна»). `Extra` чужому тарифу не выдаётся никогда.
5. Выравнивание бесплатного тарифа «Заказов» (`IsSystemFree && Line == Orders`), каждое поле **только пока в нём значение сида
   цикла 24**: `Name == LegacyFreeName → FreeName`; `Description == LegacyFreeDescription → FreeDescription`;
   `SortOrder == -1 → 10`. Лимиты, `IsPublic` и `Highlights` не трогаются никогда (админ мог снять публикацию или очистить преимущества; новый публичный сид применяется только при создании тарифа).
   Чистая функция `AlignOrdersFreeTariff(plan, apply)` по образцу `AlignFreeTariff` (юнит-тест на оба случая).
6. Служебный «Демо» (`OrdersShowcasePlanId`) не ищется, не создаётся, не выравнивается (его заводит генератор демо).
7. Строка отчёта вместо нынешней про `pricing.public-enabled`: две строки — прежняя про «Записи» и
   `«Заказы»: переключателя нет — сетка видна на GET /api/pricing/orders, как только в ней есть публичный тариф`.

`TariffCatalogReport` дополняется полями **в конец** с умолчаниями: `int OrdersPlansCreated = 0, int OrdersRulesAdded = 0,
int OrdersFreeFieldsAligned = 0`. Смысл `PlansCreated/RulesAdded/FreeFieldsAligned` (только «Записи») не меняется — тесты
цикла 28 (`second.PlansCreated.Should().Be(0)` и т. п.) остаются верными.

### §38.10.3. Что увидит владелец

Новые тарифы с `IsActive = true` сразу появятся в `availablePlans` на `/cabinet/subscription` (goods) — это ожидаемо (SPEC R-9).
Биллинг не меняется: заявка `POST …/request` с `line=Orders`, назначает суперадмин.

---

## §38.11. Технические риски

| # | Риск | Решение |
|---|---|---|
| R38-1 | Перенос разметки ломает тесты T27/T30/QA27/QA30 и паритет T30-13 | Тексты и классы переносятся дословно; тесты перенаправляются на шаблон с конфигом goods (§38.12.2), паритет становится тестом шаблона. Список замен — в `TEST_CATALOG.md` |
| R38-2 | Правка шаблона ломает соседний сервис | Тесты шаблона + тесты обеих главных; перед мерджем `npm run test:area -- companies orders billing platform` и полный прогон |
| R38-3 | Два вида данных под одним ключом TanStack Query | Все потребители переходят на `usePricingGrid(line)` (§38.7.1); старый ключ `['public-pricing']` исчезает |
| R38-4 | Сетка «Заказов» на бою до заключения юриста | Кода-гейта нет по решению заказчика. Барьер — ручной `ops tariffs apply` после юриста; без него бесплатный тариф непубличен → 404 (§38.9.2). Записано в DEPLOY.md (T-38-03) |
| R38-5 | Кеш отдаёт чужую линейку | Разные ключи + тесты в обе стороны (Orders ∉ `/api/pricing`, Services/«Демо» ∉ `/api/pricing/orders`) |
| R38-6 | «Без ограничений» у товаров — неправда (потолок 1000) | `includedProductsPerShop` не бывает `null` (§38.9.3), фронт не умеет печатать «без ограничений» для товаров |
| R38-7 | Имя «Бесплатный» у двух линеек | Поиск сидера по имени ограничен линейкой; уникального индекса по `Name` нет (проверено `AppDbContext`) |
| R38-8 | Длинные названия ломают сетку карточек | `CatalogCard` без `truncate`, `overflow-wrap:anywhere`; ручной кейс 360 px |
| R38-9 | Тесты, рендерящие `CatalogHomePage`/`GoodsNavbar`/`HomePage`, начнут делать реальный запрос сетки | Мокать `goods/src/api/ordersPricing` и `src/api/pricing` (`HomePage.test` уже мокает `../api/pricing`) |
| R38-10 | Правка `ci.yml` ломает парсинг workflow | Только дописать имена файлов в существующие списки; без инлайн-скриптов; после правки проверить YAML парсером |
| R38-11 | Демо-тесты, считающие тарифы, увидят 3 новых строки Orders | Тесты цикла 28/35 сверяют «количество не изменилось после сброса» и конкретные Id — новых падений не ожидается; backend-разработчик прогоняет `Cycle28*`/`Cycle35*` |

---

## §38.12. Тесты

### §38.12.1. Новые (фронт)

| Id | Файл | Что проверяет |
|---|---|---|
| T38-01 | `src/components/landing/ServiceLanding.test.tsx` | Порядок: `h1` → `#{catalog.id}` → `#{clients.titleId}` → `#{business.titleId}` → `#pricing-title` → `#faq` (по `compareDocumentPosition`); один `h1`; `h2` у секций 3–6; `<footer>` в выводе нет; `media` рендерится между каталогом и секцией 3 и отсутствует без конфига |
| T38-02 | там же | Заглушка: без обоих скриншотов — ровно одна `landing-screenshot-placeholder` и она внутри секции 3; с обоими — ни одной; со скриншотом только в секции 3 — заглушка в секции 4 |
| T38-03 | там же | Паритет классов секций 3 и 4 (бывший T30-13, переписан на шаблон) |
| T38-04 | `LandingActionLink.test.tsx` | `anchor` → `a[href^="#"]`; `route` → `Link`; `auth-route` меняет адрес при входе |
| T38-05 | `ServiceLanding.test.tsx` (`// @ts-expect-error`) | 2 и 4 шага, отсутствие `title`, 5 FAQ-пунктов — ошибка `tsc` (проверяется шагом `npx tsc --noEmit`) |
| T38-06 | `FaqSection.test.tsx` | всё свёрнуто; Tab доходит до каждой кнопки; Enter и пробел раскрывают/сворачивают (`user-event`); `aria-expanded` и `hidden` согласованы; два пункта открыты одновременно; `answer: '<b>x</b>'` выводится буквально (`textContent`, нет элемента `b`); `link` рендерится `Link`; `h3 > button` |
| T38-07 | `CatalogCard.test.tsx` | название 200 символов без пробелов и 300 символов с пробелами выводится целиком; у `h3` и адреса нет `truncate`/`line-clamp`, есть `break-words`; `aria-label`/`data-testid` пробрасываются; тона плашки |
| T38-08 | `LandingPricingSection.test.tsx` | `null` при загрузке/404/ошибке/пустом списке; «{lead} — от 690 ₽/мес»; ≤ 3 карточек; `limitLines` на карточке; «Все тарифы» → `/pricing` |
| T38-09 | `landing.guard.test.ts` | конфиги `src/pages/home/*.ts`, `goods/src/landing/*.ts` — не `.tsx`, без `className`; `HomePage.tsx`/`CatalogHomePage.tsx` не содержат `<section`, `<h1`, `<h2`; в `src/components/landing/**` нет `dangerouslySetInnerHTML` и импортов из `src/pages`/`goods`; FAQ каждого сервиса 6–8 пунктов |
| T38-10 | `src/pages/HomePage.test.tsx` (дополнение) | `#companies` содержит каталог; якоря «Найти специалиста» → `#companies`, «Как записаться» → `#clients-title`; в странице нет `footer`; есть `img[alt="Интерьер салона"][loading=lazy]` после `#companies`; текст «напоминание накануне визита» **есть** (Q38-6); длинное название компании выводится целиком |
| T38-11 | `goods/src/pages/CatalogHomePage.test.tsx` (дополнение) | порядок секций с тарифами и FAQ; без медиа-блока; длинное название магазина выводится целиком |
| T38-12 | `src/components/pricing/PlanCard.test.tsx`, `pricingFormat.test.ts` | `limitLines` выводятся; слов «компании/сотрудники» в `PlanCard.tsx` нет; `formatIncludedLimit(1500, …)` = «до 1 500 …» |
| T38-13 | `goods/src/pricing/ordersPricingLine.test.ts` | маппинг DTO → `limitLines` (все 4 строки; `null` магазинов/участников/заказов → «… без ограничений»; товары всегда «до N товаров в магазине») |
| T38-14 | `goods/src/pages/PricingPage.test.tsx` | 4 тарифа по `sortOrder`, акцент на «Лавке», `document.title`, 404 → «Тарифы пока не опубликованы.», CTA по входу |
| T38-15 | `goods/src/components/GoodsNavbar.test.tsx` (дополнение) | «Тарифы» есть при 200 и нет при 404, десктоп и мобильное меню |
| T38-16 | `goods/src/goodsRoutes.test.ts` | без правки теста: зелёный с `/pricing` в JSON и `GoodsApp.tsx` |

### §38.12.2. Переписываемые (фронт) — внести в `TEST_CATALOG.md`

| Было | Стало |
|---|---|
| `goods/src/components/BuyersBlock.test.tsx` (T30-01…12) | `goods/src/landing/goodsLanding.clients.test.tsx`: те же id тестов, рендер `<LandingClientsSection config={goodsLanding.clients} placeholder={false} />`. Тексты и проверки дословно |
| T30-13 (паритет) | удалён из goods, заменён T38-03 (паритет держит общий компонент) |
| `BusinessBlock.test.tsx` (T27-01…10), `BusinessBlock.qa.test.tsx` (QA27-01…08), `BusinessBlock.screenshot.test.tsx` (T30-14) | `goods/src/landing/goodsLanding.business*.test.tsx`: рендер `<LandingBusinessSection config={goodsLanding.business} placeholder={false} />`, те же проверки |
| `BuyersBlock.qa.test.tsx` (QA30-02…10) | `goods/src/landing/goodsLanding.qa.test.tsx`; путь к `assets/screenshots` в QA30-10 поправить (`../assets/screenshots`) |
| `src/pages/PricingPage.test.tsx` | без изменения проверок; если тест импортирует `PricingTeaser`/старые пропсы `PlanCard` — перевести на новые |

### §38.12.3. Бэкенд

| Id | Тип | Что проверяет |
|---|---|---|
| T38-B01 | unit `OrdersTariffCatalogTests` | Id уникальны и не пересекаются с `ZapisTariffCatalog`/служебными; значения = SPEC §6.2; преимущества ≤ 120 символов и ≤ 5 |
| T38-B02 | unit `TariffCatalogSeederTests` | `AlignOrdersFreeTariff`: поле со значением сида меняется; изменённое админом — нет (на каждое поле) |
| T38-B03 | unit `OrdersPricingCatalogBuilderTests` | фильтр по линии/`IsActive`/`IsPublic`/«Демо»; сортировка; потолок товаров (`null`→1000, 1500→1000, 300→300); highlights ≤ 5 |
| T38-B04 | functional `Cycle38OrdersTariffsTests` | `apply` на чистой БД создаёт 3 тарифа Orders с Id/значениями; повтор ничего не меняет (`OrdersPlansCreated == 0`, число правил не растёт); изменённая в админке цена не перезаписывается и есть строка расхождения; тариф «Записи» «Магазин» на поиск не влияет; правило `notifications.whatsapp` = `Extra` у созданных (если опция есть); бесплатный выровнен |
| T38-B05 | functional `Cycle38OrdersPricingTests` | 404 без тела до сидирования; 200 после, ответ проходит `OpenApiContract.Load("cycle38").AssertResponse("get","/api/pricing/orders",200,…)`; «Демо», `IsPublic=false`, `IsActive=false`, тарифы Services отсутствуют; тарифы Orders отсутствуют в `/api/pricing`; `/api/pricing` при выключенном `pricing.public-enabled` → 404, а `/api/pricing/orders` → 200, и наоборот; `If-None-Match` → 304; правка цены через `PUT /api/admin/plans/{id}` видна сразу; заголовки `ETag` и `Cache-Control: public, max-age=60` |
| T38-B06 | functional (демо) | после `DemoResetService.ResetAsync` `/api/pricing/orders` → 200, «Демо» нет (можно в существующий класс демо-сценария цикла 35, на шаблонной базе) |
| — | эталон | `Cycle22RouteTable.golden.txt` + строка `GET api/pricing/orders` |
| — | регресс | существующие `PricingTests`, `Cycle28TariffsTests`, `LegalPricingGateTests`, `PricingCatalogBuilderTests` зелёные без правки |

Новые классы — `[Trait("Area", "billing")]`.

### §38.12.4. Области быстрого контура (`contracts/cycle36/test-areas.json`)

- `companies`: + `src/components/landing/`, `src/pages/home/`;
- `billing`: + `goods/src/pricing/`, `goods/src/pages/PricingPage`, `goods/src/api/ordersPricing` (они и так попадают в `orders`
  через `goods/src/` — дубль области допустим).

---

## §38.13. Задачи

Владельцы: **BE** = backend-developer, **FE** = frontend-developer, **DO** = devops-engineer, **QA** = qa-engineer.

| Id | Владелец | Задача | Зависит от | Файлы (основные) |
|---|---|---|---|---|
| T-38-01 | BE | `OrdersTariffCatalog` + раздел «Заказы» в `TariffCatalogSeeder` (plan/apply), правила опций, выравнивание бесплатного, поля отчёта. Тесты T38-B01, B02, B04, B06 | — | `Services/Showcase/Tariffs/*` |
| T-38-02 | BE | `OrdersPublicPricingDto`, `OrdersPricingCatalogBuilder`, `PricingCatalogCache.GetOrdersAsync` + ключ + `Invalidate`, действие `GET api/pricing/orders`, эталон маршрутов. Тесты T38-B03, B05 | контракт (готов); `openapi.json` из T-38-03 для B05 | `Services/Billing/*`, `Controllers/PricingController.cs`, `DTOs/Billing/PricingDtos.cs`, golden |
| T-38-03 | DO | CI и обвязка контракта: `types:api:cycle38` в `package.json`; `cycle38` в `scripts/contracts-to-json.mjs`; в `ci.yml` — линт `contracts/cycle38/openapi.yaml`, генерация и `git diff` генерата и `openapi.json` (дописать в существующие списки, без инлайн-скриптов, проверить YAML); комментарий в `contracts/redocly.yaml`; закоммитить `contracts/cycle38/openapi.json` и `src/types/api-cycle38.generated.ts`; `test-areas.json` (§38.12.4); раздел DEPLOY.md «Цикл 38» (§38.15) | — | `.github/workflows/ci.yml`, `frontend/package.json`, `frontend/scripts/contracts-to-json.mjs`, `contracts/*`, `DEPLOY.md` |
| T-38-07a | FE | `pricingLine.ts` (типы + `usePricingGrid`) — маленький первый коммит, от него зависят 04 и 07 | — | `src/components/pricing/pricingLine.ts` |
| T-38-04 | FE | Ядро шаблона: `types.ts`, `classes.ts`, `ServiceLanding`, `LandingHero`, `LandingCatalogFrame`, `LandingMedia`, `LandingStepsPanel`, `LandingClientsSection`, `LandingBusinessSection`, `LandingScreenshot` + заглушка, `LandingActionLink`; `export type IconName`. Тесты T38-01…05 | T-38-07a | `src/components/landing/*`, `src/components/ui/Icon.tsx` |
| T-38-05 | FE | `FaqSection`. Тест T38-06 | T-38-04 (`types.ts`) | `src/components/landing/FaqSection.tsx` |
| T-38-06 | FE | `CatalogCard` (уточнение 8). Тест T38-07 | — | `src/components/landing/CatalogCard.tsx` |
| T-38-07 | FE | `zapisPricingLine`, `PlanCard(limitLines)`, `PricingPageBody`, `PricingNavLink(line)`, `LandingPricingSection`, удаление `PricingTeaser`, `src/pages/PricingPage` и `Navbar` на новые пропсы, `formatIncludedLimit` с `toLocaleString`. Тесты T38-08, T38-12, правка `PricingPage.test` | T-38-07a | `src/components/pricing/*`, `src/components/landing/LandingPricingSection.tsx`, `src/pages/PricingPage.tsx`, `src/components/layout/Navbar.tsx`, `src/utils/pricingFormat.ts` |
| T-38-08 | FE | «Запись» на шаблон: `ZapisCompanyCatalog` (на `CatalogCard`), `zapisLanding.ts`, `zapisFaq.ts`, `HomePage.tsx`, удалить встроенный футер; сверить `authedTo` и пометки «сверить» FAQ. Тест T38-10 | 04, 05, 06, 07 | `src/pages/HomePage.tsx`, `src/pages/home/*` |
| T-38-09 | FE | «Заказы» на шаблон: `ShopCatalog` (на `CatalogCard`), `goodsLanding.ts` (пока без `pricing`), `goodsFaq.ts`, `CatalogHomePage.tsx`, удалить `BuyersBlock/BusinessBlock/ScreenshotFigure`, перенос тестов (§38.12.2), `TEST_CATALOG.md`. Тест T38-11 | 04, 05, 06, 07 | `goods/src/pages/CatalogHomePage.tsx`, `goods/src/components/ShopCatalog.tsx`, `goods/src/landing/*` |
| T-38-10 | FE | Тарифы goods: `api/ordersPricing.ts` (на генерате cycle38; `null` на 404), `ordersPricingLine.ts`, `goods/src/pages/PricingPage.tsx`, маршрут + `CONSENT_GATE_BYPASS_PATHS`, `goods-routes.json`, ссылка в `GoodsNavbar`, `pricing` в `goodsLanding`. Тесты T38-13…16 | 07, 09; генерат (03) или локальный `npm run types:api:cycle38`; бэкенд не нужен — работа по prism | `goods/src/api/ordersPricing.ts`, `goods/src/pricing/*`, `goods/src/pages/PricingPage.tsx`, `goods/src/GoodsApp.tsx`, `goods/src/components/GoodsNavbar.tsx`, `contracts/cycle23/goods-routes.json` |
| T-38-11 | QA | `schemathesis run contracts/cycle38/openapi.yaml` против dev-хоста после `ops tariffs apply`; ручные кейсы: 360 px на `/` обоих сайтов, длинное название (≥ 120 символов без пробелов) у салона и магазина, клавиатура и VoiceOver на FAQ, якоря и фокус, один футер на ezbook, demo.visit/demo.zakaz (баннер, бейджи, вход по ролям, сетка «Заказов» после сброса) | 01–10 | — |

### Параллельность

```
BE:  T-38-01 ─┐                      (01 и 02 независимы, можно параллельно; оба — по контракту)
     T-38-02 ─┴──────────────────────────────────────────────┐
DO:  T-38-03 (первым, ~час; генерат и openapi.json нужны FE в T-38-10 и BE в T38-B05)
FE:  07a → 04 → {05, 06, 07} → {08, 09} → 10 ─────────────────┤
QA:                                                           └─ T-38-11
```

- Бэкенд и фронт не блокируют друг друга: фронт «Заказов» (T-38-10) работает по `npx @stoplight/prism mock contracts/cycle38/openapi.yaml --port 4038`.
- 08 и 09 трогают разные файлы и могут идти параллельно (два разработчика) — общий у них только шаблон, который к этому моменту готов.
- Конфликтные файлы: `src/components/landing/types.ts` (04 создаёт, 05/07 только читают), `contracts/cycle23/goods-routes.json`
  (только 10), `goods/src/landing/goodsLanding.ts` (09 создаёт, 10 дописывает `pricing`), `TEST_CATALOG.md` (09; 08 дописывает
  в конце — при параллельной работе мерджить по строкам).

---

## §38.14. Безопасность

- `GET /api/pricing/orders` — анонимный, без ПДн, ответ из памяти, без rate limit (как `/api/pricing`, ARCHITECTURE_CYCLE7 §48).
  Наружу не выходят непубличные и служебные тарифы, правила опций, подписчики.
- FAQ: только текстовые узлы React, `dangerouslySetInnerHTML` запрещён guard-тестом.
- Ссылки FAQ — только внутренние (`Link to`), внешних URL в типе нет.

---

## §38.15. Выкат (для DEPLOY.md, T-38-03)

1. Выкатить код (миграций нет).
2. **Бой:** до ответа юриста ничего не делать — бесплатный тариф «Заказов» непубличен, `GET /api/pricing/orders` → 404, на goods
   нет ни блока, ни ссылки «Тарифы». После ответа юриста: `ops tariffs plan` → прочитать отчёт → `ops tariffs apply`. Сетка
   появится в течение 60 с. Текст под сеткой (если юрист его дал) — `pricing.orders.legal-notice` в `PlatformSettings`, затем
   любое сохранение тарифа в админке или подождать 60 с.
3. **demo.visit / demo.zakaz:** `ops demo reset --yes` — сетка «Заказов» засевается сама (тот же `ApplyAsync`).
4. Откат публикации: в админке снять `IsPublic` у тарифов «Заказов» (404 при пустом списке). Удалять тарифы не нужно.

---

## §38.16. Что сознательно не делаем

Страницы «Домов» и «Бань»; скриншоты «Записи» и доработку стенда съёмки; i18n и CMS для FAQ; JSON-LD `FAQPage`; онлайн-оплату;
пробный период «Заказов»; опции в публичной сетке «Заказов»; переделку шапок (кроме ссылки «Тарифы» в `GoodsNavbar`); правку
кабинетов и экрана настроек админки.

## §38.17. Открытые вопросы (не блокируют старт)

1. Заглушка скриншота «один раз» — понята как «один раз на страницу, в первом пустом слоте» (§38.3.6). Если имелось в виду
   «в каждом пустом слоте» — правка одной строки и теста T38-02.
2. Адрес «Подключить салон» для вошедшего клиента без роли владельца — сверка в T-38-08 (§38.4.2).
3. Тексты-черновики `clients.text` и `business.text` «Записи» — на вычитку заказчиком вместе с FAQ.
