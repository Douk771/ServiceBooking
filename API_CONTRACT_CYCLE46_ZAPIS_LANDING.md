# API_CONTRACT — цикл 46 ServiceBooking: главная «Записи» под владельца салона

**Разделы §46.20–§46.26.** Решения — `ARCHITECTURE_CYCLE46_ZAPIS_LANDING.md` (§46.0–§46.16), требования — `SPEC.md` (цикл 46,
`bcda4cc5`). Машиночитаемо: `contracts/cycle46/openapi.yaml` (дельта HTTP API = ноль) и **`contracts/cycle46/zapis-landing.json`**
(эталон порядка блоков и дословных текстов «Записи» — источник истины для кода и для сверки M46-01). Если этот текст и JSON
расходятся по строке текста, прав JSON; по смыслу — этот документ. Корневые `API_CONTRACT.md` и `ARCHITECTURE.md` цикл не трогает
(конвенция `CURRENT_STATE.md` §10.5: документы цикла — в корне под суффиксом).

---

## §46.20. HTTP API ServiceBooking: изменений нет

**В цикле 46 нет ни одного нового или изменённого маршрута `/api/*`, DTO, кода ответа или текста ошибки.** `ServiceBooking.API`,
`ServiceBooking.Core` и миграции не меняются. Генерировать типы (`types:api:*`) и `openapi.json` не нужно.

Главная «Записи» по-прежнему читает два анонимных маршрута. Оба остаются без изменений (факты — `CURRENT_STATE.md` §4):

| Маршрут | Кто вызывает | Поведение на главной |
|---|---|---|
| `GET /api/companies/public?search&page&pageSize` | `ZapisCompanyCatalog` (`companiesApi.getPublic`, `pageSize = 20`) | Каталог «Салоны на EZBOOK» в конце страницы. Ошибка → строка ошибки (§46.24) |
| `GET /api/pricing` | `LandingPricingSection` через `zapisPricingLine` | Блок «Тарифы». 404, ошибка, пустой список → блока нет. Строка перед ценой — «Онлайн-запись для салона — от {цена}» |

Порядок запросов меняется только визуально: каталог теперь стоит внизу, но монтируется вместе со страницей, как и раньше. Поэтому
`GET /api/companies/public` уходит при открытии `/`, как и до цикла. Ленивую загрузку каталога в этом цикле не вводим (§46.14 R46-9
архитектуры).

---

## §46.21. Контракт конфига шаблона главной — `frontend/src/components/landing/types.ts`

Это главный контракт цикла между «шаблоном» (`src/components/landing/*`) и «конфигами сервисов» (`zapisLanding.ts`,
`goodsLanding.ts`, `stayLanding.ts`, `baniLanding.ts`). Его проверяет tsc. Изменения помечены `// 46:`. Остальное — без изменений.

```ts
// 46: порядок секций — данные конфига. Закрытый список, а не массив: две известные раскладки, произвольную перестановку из конфига
//     сделать нельзя (NFR «перекрасить или переставить вёрстку из конфига нельзя»).
export type LandingOrder = 'catalog-first' | 'business-first'

// 46: вторичная строка первого экрана для клиента: «{lead} <a href={action.href}>{action.label}</a>».
export interface LandingHeroClientLink {
  lead: string            // 'Ищете, куда записаться?'
  action: AnchorAction    // { kind: 'anchor', href: '#companies', label: 'Салоны на EZBOOK' }; href === `#${catalog.id}` — тест CY46-01
}

export interface LandingHeroConfig {
  layout?: never
  eyebrow: string
  title: string | { lead: string; accent: string }
  intro: string
  primaryAction?: LandingAction            // 46: было AnchorAction | RouteAction — теперь допускает и 'auth-route'
  howTo: AnchorAction
  clientLink?: LandingHeroClientLink       // 46: новое
}

/** Без изменений: секция клиентов в раскладке 'catalog-first' («Заказы», «Дома», «Бани»). */
export interface LandingClientsConfig {
  eyebrow: string
  title: string
  titleId: string
  text: string
  actions: readonly [LandingAction] | readonly [LandingAction, LandingAction]
  stepsPanel: LandingStepsPanel
  list: { title: string; titleId: string; items: readonly [string, ...string[]]; note?: string }
  screenshot?: LandingShot
}

// 46: секция клиентов в раскладке 'business-first' — рассказ владельцу «как к вам записываются клиенты».
//     Кнопок и панели шагов нет (П2, П3); задать их нельзя — tsc.
export interface LandingClientsShowcaseConfig {
  eyebrow: string
  title: string
  titleId: string
  text: string
  actions?: never
  stepsPanel?: never
  list: { title: string; titleId: string; items: readonly [string, ...string[]]; note?: string }
  screenshot?: LandingShot
}

/** Без изменений по форме. В 'business-first' stepsPanel выводится отдельной секцией после клиентов (§46.23). */
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

// 46: общая часть двух раскладок
interface LandingConfigCommon {
  catalog: LandingCatalogFrameConfig
  business: LandingBusinessConfig
  pricing?: LandingPricingConfig
  faq: { items: FaqItems }
}

// 46: прежний LandingConfig под новым именем; order можно не писать — goods/dom/bani не правятся.
export interface CatalogFirstLandingConfig extends LandingConfigCommon {
  order?: 'catalog-first'
  hero: LandingHeroConfig
  media?: LandingMediaConfig
  clients: LandingClientsConfig
}

// 46: раскладка «Записи»: герой → бизнес → клиенты → шаги → тарифы → FAQ → каталог.
//     Кнопка в первом экране и строка-ссылка на каталог обязательны: каталог внизу, к нему ведёт один тап (US-46-05).
export interface BusinessFirstLandingConfig extends LandingConfigCommon {
  order: 'business-first'
  hero: LandingHeroConfig & { primaryAction: LandingAction; clientLink: LandingHeroClientLink }
  media?: never
  clients: LandingClientsShowcaseConfig
}

export type LandingConfig = CatalogFirstLandingConfig | BusinessFirstLandingConfig   // 46: было interface

// 46: шапка-панель («Дома», «Бани») бывает только в раскладке 'catalog-first'.
export type LandingPanelConfig = Omit<CatalogFirstLandingConfig, 'hero'> & { hero: LandingPanelHeroConfig }
```

Неизменные типы: `AnchorAction`, `RouteAction`, `AuthRouteAction`, `LandingAction`, `LandingStep`, `ThreeSteps`, `LandingStepsPanel`,
`LandingScreenshot*`, `LandingShot`, `LandingCatalogFrameConfig`, `LandingMediaConfig`, `LandingPricingConfig`, `FaqItem`, `FaqItems`,
`HeroBackdrop`, `HeroFact(s)`, `LandingPanelHeroConfig`. Пропсы `ServiceLanding` (`ServiceLandingProps`) по форме не меняются:
`{ config: LandingConfig; catalog; heroAside?: undefined } | { config: LandingPanelConfig; catalog; heroAside: ReactElement }`.

**Инварианты, которые проверяет tsc (CY46-11, `@ts-expect-error` в `ServiceLanding.test.tsx`):**
1. `order: 'business-first'` без `hero.primaryAction` или без `hero.clientLink` — ошибка.
2. `order: 'business-first'` с `clients.actions` или `clients.stepsPanel` — ошибка.
3. `order: 'business-first'` с `media` — ошибка.
4. `LandingPanelConfig` с `order: 'business-first'` — ошибка.
5. Конфиг без `order` (goods) — валидный `CatalogFirstLandingConfig`.
6. Guard T38-09 по-прежнему: в `types.ts` нет полей `className`, `style`, `as`.

---

## §46.22. Конфиг главной «Записи» — `frontend/src/pages/home/zapisLanding.ts`, `zapisFaq.ts`

Значения — **ровно** `contracts/cycle46/zapis-landing.json` (секции `hero`, `business`, `clients`, `steps`, `pricing`, `faq`, `catalog`).
Форма:

```ts
export const zapisLanding: BusinessFirstLandingConfig = {   // тип можно писать как LandingConfig — сужение даст order
  order: 'business-first',
  hero:     { eyebrow, title: { lead, accent }, intro, primaryAction /* auth-route */, howTo /* #clients-title */, clientLink /* #companies */ },
  catalog:  { id: 'companies', ariaLabel: 'Салоны на EZBOOK' },
  business: { eyebrow, title, titleId: 'biz-title', text, actions /* [auth-route, route /cabinet] */, benefits /* 7 */,
              screenshot: bookingsShot, stepsPanel: { title, titleId: 'biz-steps-title', steps /* 3, иконки store/users/external-link */ } },
  clients:  { eyebrow, title, titleId: 'clients-title', text, list: { title, titleId: 'clients-list-title', items /* 5 */, note },
              screenshot: bookingFlowCarousel },
  pricing:  { line: zapisPricingLine, lead: 'Онлайн-запись для салона' },
  faq:      { items: zapisFaq },   // 8 вопросов в порядке JSON
}
```

Обязательные совпадения с прежним кодом (на них опираются тесты других циклов, их не трогаем):
- `business.titleId === 'biz-title'` и подстрока `business:` в исходнике до `titleId: 'biz-title'` — CY44-08
  (`about/src/targets.guard.test.ts`);
- `business.actions[0]` — `auth-route` `/register?returnTo=%2Fcabinet%2Fnew` → `/cabinet/new` — `CreateCompanyRoute.test.tsx`;
- `clients.screenshot === bookingFlowCarousel`, `business.screenshot === bookingsShot` — `shots.test.ts`;
- `hero.primaryAction` **глубоко равен** `business.actions[0]` — CY46-04 (одинаковое поведение двух кнопок «Подключить салон»).

Комментарий-шапку `zapisLanding.ts` и `zapisFaq.ts` обновить: «Тексты — SPEC цикла 46 §4, **утверждены заказчиком 20.10.2026 как
окончательные**; эталон — `contracts/cycle46/zapis-landing.json`. Правка текста = правка JSON и решение заказчика». Слова «черновики
на вычитку» из шапок убрать.

---

## §46.23. Разметочный контракт главной «Записи» (`/` на `visit.ezbook.ru`)

На эти `id`, `aria-*` и порядок опираются тесты CY46-01…03, `useInitialHashScroll` и переход `about.ezbook.ru → /#biz-title`.
Классы Tailwind контрактом не являются (кроме перечисленных в §46.25).

```html
<main class="max-w-[1180px] mx-auto px-4 sm:px-8 pt-10 md:pt-16 pb-10">
  <div class="max-w-[720px]">                                                   <!-- 1. первый экран -->
    <p>Онлайн-запись для салонов и мастеров</p>
    <h1>Онлайн-запись в ваш салон —<br><em>без звонков и переписок</em></h1>
    <p>{intro}</p>
    <div>                                                                         <!-- кнопка и ссылка в одном ряду, flex-wrap -->
      <a href="/register?returnTo=%2Fcabinet%2Fnew">Подключить салон<svg aria-hidden="true"/></a>   <!-- вошедший: /cabinet/new -->
      <a href="#clients-title">Как записываются клиенты</a>
    </div>
    <p>Ищете, куда записаться? <a href="#companies">Салоны на EZBOOK</a></p>      <!-- одна строка текста на 375 px -->
  </div>
  <section aria-labelledby="biz-title">                                            <!-- 2. «Для салона» -->
    <p>Для салона</p><h2 id="biz-title">…</h2><p>…</p>
    <div><a>Подключить салон</a><a href="/cabinet">Войти в кабинет</a></div>
    <ul>7 × <li><svg aria-hidden="true"/><span>…</span></li></ul>
    <figure><picture>…</picture><figcaption>Записи мастера на день: клиент, услуга и время</figcaption></figure>
    <!-- панели шагов внутри НЕТ -->
  </section>
  <section aria-labelledby="clients-title">                                        <!-- 3. «Так к вам записываются клиенты» -->
    <p>Ваши клиенты</p><h2 id="clients-title" tabindex="-1">…</h2><p>…</p>
    <!-- кнопок и панели «Как записаться» НЕТ -->
    <div role="region" aria-roledescription="карусель" aria-label="Как проходит запись">…</div>
    <h3 id="clients-list-title">Что ещё может клиент</h3>
    <ul aria-labelledby="clients-list-title">5 × <li><svg aria-hidden="true"/><span>…</span></li></ul>
    <p>Оплата — в салоне, EZBOOK деньги клиентов не принимает.</p>
  </section>
  <section aria-labelledby="biz-steps-title">                                      <!-- 4. «Как начать принимать записи» -->
    <div>                                                                          <!-- панель cream-deep -->
      <h2 id="biz-steps-title">Как начать принимать записи</h2>
      <ol aria-labelledby="biz-steps-title">
        3 × <li><div><div><svg aria-hidden="true"/></div><h3>…</h3></div><p>…</p></li>   <!-- иконка и заголовок шага — один ряд -->
      </ol>
    </div>
  </section>
  <section aria-labelledby="pricing-title">…</section>                             <!-- 5. только если тарифы опубликованы -->
  <section id="faq" aria-labelledby="faq-title"><h2 id="faq-title">Частые вопросы</h2>…</section>   <!-- 6. 8 × h3 > button -->
  <section id="companies" aria-label="Салоны на EZBOOK">                           <!-- 7. каталог -->
    <h2>Салоны на EZBOOK</h2><p>Выберите салон и запишитесь онлайн</p><input placeholder="Поиск по названию или адресу">
    …карточки | загрузка | ошибка | пусто…
  </section>
</main>
```

Свойства (проверяются автотестами):
- ровно один `h1`; порядок `h1` → `#biz-title` → `#clients-title` → `#biz-steps-title` → (`#pricing-title`) → `#faq-title` →
  `#companies` (CY46-01); `#biz-title`, `#clients-title`, `#biz-steps-title`, `#faq-title` — элементы `H2`;
- уровни заголовков без пропусков: в секции 4 — `h2` → `h3` (а в панели шагов «Заказов», «Домов», «Бань» по-прежнему `h3` → `h4`);
- в секции `[aria-labelledby="clients-title"]` нет ссылок (кнопок) и нет элемента `#clients-steps-title`;
- в секции `[aria-labelledby="biz-title"]` нет `ol` (шаги вынесены);
- все `svg` внутри `main`, кроме карусели, — `aria-hidden="true"`;
- `#companies` — последний дочерний `section` в `main`.

**У «Заказов», «Домов» и «Бань» разметка по составу и порядку не меняется.** Меняются только классы раскладки (§46.25) и внутренняя
обёртка шага: `li > div(иконка + h4) + p` вместо `li > div(иконка) + h4 + p`.

---

## §46.24. Каталог «Записи» — `frontend/src/pages/home/ZapisCompanyCatalog.tsx`

Поведение и запросы прежние: поиск с задержкой 300 мс, сброс на страницу 1, `PAGE_SIZE = 20`, `Pagination`, плашки «Онлайн-запись»
и «Демо». Меняются тексты (JSON `catalog`) и раскладка:

| Состояние | Разметка |
|---|---|
| Шапка | `h2` «Салоны на EZBOOK» + `p` «Выберите салон и запишитесь онлайн»; поле поиска **на всю ширину контейнера** до `sm`, от `sm` — `280px` (было: всегда `280px`) |
| Загрузка | ровно **2** заглушки-карточки (было 6) |
| Ошибка | `<div role="alert">` в одну строку: иконка `alert-circle` 20 px `aria-hidden` + текст «Не удалось загрузить список компаний. Попробуйте обновить страницу.» |
| Пусто | одна строка: иконка `store` 20 px `aria-hidden` + «Салонов пока нет» |
| Список | без изменений; сетка `repeat(auto-fill, minmax(min(320px, 100%), 1fr))` (защита от горизонтальной прокрутки уже 320 px) |

`data-testid` не добавляются: тесты ищут по тексту и роли.

---

## §46.25. Общие классы шаблона — `frontend/src/components/landing/classes.ts`

Константы, которые импортирует `about.ezbook.ru` (`about/src/components/*`), **не меняются ни на символ**: `BTN_PRIMARY`,
`BTN_SECONDARY`, `CHECK_ITEM`, `H2`, `H3`, `EYEBROW`, `LEAD`, `CONTAINER`, `FOCUS_RING`. Их значения закрепляет тест CY46-14.
Страница «Все продукты» из-за цикла 46 не меняется.

Меняется и добавляется (используются только шаблоном главных):

```ts
// изменено (K5): 64 px пустоты на телефоне вместо 128, 128 px на десктопе вместо 160
export const SECTION = 'mt-10 md:mt-20 border-t border-line pt-6 md:pt-12'
// новое: отдельная секция с панелью шагов («Запись», блок 4) — без линии, разделитель — сама панель
export const STEPS_SECTION = 'mt-10 md:mt-20'
// новое (K2): ряд действий — пара кнопок в одну строку на 375 px; flex-wrap остаётся страховкой для экранов уже 360 px
export const ACTIONS_ROW = 'mt-6 flex flex-wrap items-center gap-2 sm:gap-3'
export const BTN_ACTION_PRIMARY =
  'inline-flex items-center justify-center gap-2 min-h-[44px] whitespace-nowrap rounded-full bg-ink hover:bg-ink/90 !text-cream text-[14px] sm:text-[15px] font-semibold px-4 sm:px-7 py-2.5 sm:py-3.5 transition-colors'
export const BTN_ACTION_SECONDARY =
  'inline-flex items-center justify-center min-h-[44px] whitespace-nowrap rounded-full bg-white hover:bg-cream-deep !text-ink border border-line text-[14px] sm:text-[15px] font-semibold px-4 sm:px-7 py-2.5 sm:py-3.5 transition-colors'
// новое (K3, K4): плотный список с галочкой; колонки задаёт компонент
export const DENSE_LIST = 'grid gap-x-8 gap-y-2.5'
// новое (K1): кружок иконки шага в одном ряду с заголовком
export const STEP_ICON = 'w-9 h-9 shrink-0 rounded-full bg-cream border border-line-strong flex items-center justify-center'
```

Стрелка у основной кнопки остаётся в DOM (на неё опираются QA27-07, T30-09), но видна только от `sm`:
`<Icon name="arrow-right" size={16} className="hidden sm:block" aria-hidden />`.

---

## §46.26. Как проверить, что стороны сошлись (машинно)

```bash
# 1. Дельта HTTP API = ноль (схема валидна, paths пуст)
cd frontend && npx @redocly/cli lint --config ../contracts/redocly.yaml ../contracts/cycle46/openapi.yaml

# 2. Типы конфига (§46.21): четыре конфига компилируются, запрещённые формы — нет (CY46-11)
cd frontend && npx tsc --noEmit && npx tsc --noEmit -p tsconfig.goods.json \
  && npx tsc --noEmit -p tsconfig.dom.json && npx tsc --noEmit -p tsconfig.bani.json \
  && npx tsc --noEmit -p tsconfig.about.json        # about — только чтобы убедиться, что classes.ts не сломал витрину

# 3. Тексты и порядок «Записи» = contracts/cycle46/zapis-landing.json; разметка §46.23; каталог §46.24; классы §46.25
cd frontend && npx vitest run src/pages/home src/pages/HomePage.test.tsx src/components/landing

# 4. Остальные три главные не изменились по составу и порядку (CY46-02, CY46-09)
cd frontend && npx vitest run goods/src/landing goods/src/pages/CatalogHomePage.test.tsx dom/src/pages/CatalogPage.test.tsx bani/src \
  about/src/targets.guard.test.ts
```
