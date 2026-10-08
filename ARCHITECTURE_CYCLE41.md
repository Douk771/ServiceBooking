# ARCHITECTURE — цикл 41 ServiceBooking: главная «Домов» (dom.ezbook.ru) по единому шаблону

**Разделы §41.0–§41.13.** В коде и документах ссылаться с именем файла: `ARCHITECTURE_CYCLE41.md §41.3.2`.

**На входе:**
- `SPEC_CYCLE41_STAYS_LANDING.md` (далее SPEC);
- `BRIEF_CYCLE41_STAYS_LANDING.md`, раздел «Ответы заказчика на Q41-1…Q41-4» (09.10.2026). **Он приоритетнее SPEC**;
- шаблон цикла 38 (`ARCHITECTURE_CYCLE38.md` §38.2–§38.6, `CURRENT_STATE.md` §5.13);
- код ветки `cycle/041-stays-landing` (`develop` `050c3d0` + документы `0df6d90`). Всё, что ниже названо «есть», сверено по файлам.

Ветку подготовил devops-инженер, архитектор её не трогает. Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` не меняются.

**Бэкенд и контракт.** API не меняется, поэтому `API_CONTRACT_CYCLE41.md` и `contracts/cycle41/` не создаются. Каталог по-прежнему
ходит в `GET /api/stays/catalog` (контракт цикла 37). Источник истины по параметрам — `contracts/cycle37/dom-routes.json`
(`catalogQueryParams`, `houseQueryParams`), файл не меняется. Объект запроса из `toApiQuery` для тех же фильтров остаётся тем же
байт в байт: это проверяет тест T41-04c.

---

## §41.0. Решения заказчика (сводка поправок к SPEC)

| Пункт SPEC | Решение заказчика | Что это значит для команды |
|---|---|---|
| Q41-1 | шапка — **вариант A** | В шаблоне появляется вариант шапки `layout: 'panel'`. Справа панель-слот, под абзацем три факта, за шапкой полоса во всю ширину с линейным рисунком гор (SVG шаблона). §41.3 |
| Q41-2 | «Для владельцев» — полная секция 4 шаблона, тексты нейтральные, без призывов | Тексты SPEC §4.4 дословно. Guard-тест проверяет запрещённые слова (§41.10, T41-03). Кнопка «Мой кабинет» (только для вошедшего) заменяется на «Войти в кабинет» → `/cabinet` для всех |
| Q41-3 | тарифов нет, в FAQ нет цен и ссылки на `/pricing` | В `stayLanding` нет `pricing`, поэтому секции 5 нет и нет ни одного запроса тарифов. Guard-тест — по образцу goods |
| Q41-4 | бренд «EZBOOK Дома» | Если продукт назван в тексте главной, то только «EZBOOK Дома». Название встречается ровно в одном месте: в вопросе 8 FAQ (§41.5.3). Форма «ezbook · Дома» в конфиге запрещена guard-тестом. Шапка и подвал сайта (`DomNavbar`, `DomFooter`: «ezbook · Дома») в этом цикле **не меняются**, см. §41.13 п. 1 |
| FAQ п. 7 | «Только Шерегеш» | Остаётся вопрос 7 «В каких местах есть дома?», запасной вопрос про туристический налог не используется. Ответ: «Только в Шерегеше.» (без «пока», чтобы не обещать другие города) |
| шапка | «напрямую владельцу» | Абзац шапки — нынешний текст дословно («…напрямую владельцу — без комиссии с гостя»). Третий факт для единства шапки тоже говорит «владельцу»: «Предоплата — напрямую владельцу» (в SPEC было «компании») |

Ответы на вопросы аналитика A41-1…A41-6 даны в §41.3.1 (A41-1), §41.3.3 (A41-2), §41.4.1 (A41-3), §41.4.3 (A41-4),
§41.10.1 (A41-5) и §41.10.4 (A41-6).

---

## §41.1. Стек и что добавляется

| Что | Решение | Почему |
|---|---|---|
| Фронт | React 18, TS 5.5, Tailwind 3.4, TanStack Query, React Router 6, Vitest 3.2 — без изменений | Задача — перекомпоновка существующей страницы на готовый шаблон |
| Шаблон | расширяется в `frontend/src/components/landing/` | Папка уже входит в `SCANNED_EZBOOK_DIRS`. dom импортирует её и сканирует Tailwind'ом без правки `frontend/shared-sources.js` |
| Цвета и токены | общий пресет `tailwind.config.js` (dom подключает его как `presets: [base]`) | R41-5 SPEC снят по коду: `cream-deep`, `line-strong`, `gold-dark`, `success-bg`, `shadow-soft` есть у всех трёх приложений |
| Картинки | только инлайн-SVG фона (несколько сотен байт) | Решение цикла 38: первый экран без фото |
| Бэкенд, БД, миграции, nginx, CI | **без изменений** | — |
| Новые npm-пакеты | **нет** | — |

---

## §41.2. Раскладка файлов

```
frontend/src/components/landing/            ← ШАБЛОН (общий для ezbook, goods, dom)
  types.ts                  ПРАВКА: LandingPanelHeroConfig, LandingPanelConfig, HeroFact(s), HeroBackdrop;
                            в LandingHeroConfig — `layout?: never` (§41.3.1). Существующие поля не меняются
  ServiceLanding.tsx        ПРАВКА: вторая ветка разметки для layout 'panel', проп heroAside (§41.3.2).
                            Ветка по умолчанию — байт в байт прежняя
  LandingPanelHero.tsx      НОВЫЙ: шапка с панелью: полоса фона, текст, факты, рамка панели
  LandingHeroBackdrop.tsx   НОВЫЙ: декоративные SVG фона по закрытому списку HeroBackdrop ('mountains')
  LandingHero.tsx           БЕЗ ИЗМЕНЕНИЙ (файл не трогать)
  classes.ts                ПРАВКА (только дописать): константа CONTAINER (§41.3.2)
  LandingPanelHero.test.tsx НОВЫЙ: T41-01, T41-02
  testConfig.ts             ПРАВКА (только дописать): makePanelConfig(over) для тестов шаблона

frontend/dom/src/
  pages/CatalogPage.tsx     ПЕРЕПИСЫВАЕТСЯ: только <ServiceLanding config={stayLanding} heroAside={<StaySearchPanel/>}
                            catalog={<StayCatalog/>} /> (без <main>, <section>, <h1>, <h2>)
  pages/CatalogPage.test.tsx  НОВЫЙ: T41-04
  landing/stayLanding.ts    НОВЫЙ: конфиг главной «Домов» (LandingPanelConfig), только данные
  landing/stayFaq.ts        НОВЫЙ: 8 вопросов (FaqItems)
  landing/configs.guard.test.ts  НОВЫЙ: T41-03
  components/catalog/StaySearchPanel.tsx  НОВЫЙ: форма «Подбор дома» (перенос из CatalogPage как есть)
  components/catalog/StayCatalog.tsx      НОВЫЙ: h2 + счётчик + список + состояния + пагинация (перенос как есть)
  hooks/useCatalogFilters.ts              НОВЫЙ: чтение и запись фильтров в URL (общий для панели и каталога)
  components/HouseCard.tsx  ПРАВКА: перенос длинных слов, без line-clamp (§41.6)
  components/HouseCard.test.tsx  НОВЫЙ: T41-05
  test/fixtures.ts          ПРАВКА (только дописать): catalogItemFixture(over), catalogPageFixture(items, over)
  utils/catalogQuery.ts     БЕЗ ИЗМЕНЕНИЙ
  DomApp.tsx, DomNavbar.tsx, DomFooter.tsx, CompanyPage.tsx, HousePage.tsx, BookingPage.tsx  БЕЗ ИЗМЕНЕНИЙ

frontend/shared-sources.js                  БЕЗ ИЗМЕНЕНИЙ
frontend/src/pages/home/*, frontend/goods/src/landing/*   БЕЗ ИЗМЕНЕНИЙ (конфиги и тесты «Записи» и «Заказов»)
contracts/cycle36/test-areas.json           ПРАВКА: + "src/components/landing/" в область stays (§41.10.4)
.github/workflows/ci.yml                    НЕ ТРОГАЕМ (зона конфликтов с циклом 39)
```

Правила зависимостей (как в цикле 38, проверяются существующими guard-тестами):
- `src/components/landing/**` не импортирует ничего из `src/pages/**`, `goods/**` и `dom/**`;
- dom-страницы не импортируют чужие `pages`. dom берёт шаблон через `@/components/landing/...`;
- конфиг (`stayLanding.ts`, `stayFaq.ts`) — `.ts` без JSX и без `className`.

---

## §41.3. Расширение шаблона: шапка с боковой панелью

### §41.3.1. Типы (`types.ts`) — ответ на A41-1

Добавляется (обязательная форма; имена менять только вместе с этим документом):

```ts
/** Закрытый список фоновых рисунков шапки с панелью. Новый мотив = новая строка тут + SVG в LandingHeroBackdrop.tsx. */
export type HeroBackdrop = 'none' | 'mountains'

/** Короткий факт под абзацем шапки; иконка декоративная (aria-hidden). */
export interface HeroFact { icon: IconName; text: string }
/** От 0 до 3 фактов: 4 — ошибка tsc. */
export type HeroFacts =
  | readonly []
  | readonly [HeroFact]
  | readonly [HeroFact, HeroFact]
  | readonly [HeroFact, HeroFact, HeroFact]

/** Шапка с боковой панелью (цикл 41). Основной кнопки нет: её роль играет панель (слот heroAside). */
export interface LandingPanelHeroConfig {
  layout: 'panel'
  eyebrow: string
  title: string | { lead: string; accent: string }
  intro: string
  /** Якорь на заголовок секции 3. */
  howTo: AnchorAction
  facts: HeroFacts
  backdrop: HeroBackdrop
}

/** Конфиг главной с шапкой-панелью: всё как в LandingConfig, кроме hero. */
export type LandingPanelConfig = Omit<LandingConfig, 'hero'> & { hero: LandingPanelHeroConfig }
```

В существующий `LandingHeroConfig` дописывается **одно** поле:

```ts
export interface LandingHeroConfig {
  /** Обычная шапка. Поле есть только для того, чтобы конфиг с layout: 'panel' нельзя было передать как обычный. */
  layout?: never
  // eyebrow, title, intro, primaryAction?, howTo — без изменений
}
```

Почему так:
- **Отдельный тип конфига, а не `hero: LandingHeroConfig | LandingPanelHeroConfig` внутри `LandingConfig`.** При объединении внутри
  `LandingConfig` конфиги `zapisLanding: LandingConfig` и `goodsLanding: LandingConfig` пришлось бы сужать в каждой точке вызова,
  иначе tsc потребует `heroAside` и у них. А их файлы трогать нельзя (US-41-07).
- **`layout?: never`** делает `LandingPanelConfig` несовместимым с `LandingConfig`. Поэтому передать конфиг с панелью без слота
  нельзя, tsc выдаст ошибку (см. ниже). Конфиги «Записи» и «Заказов» поля `layout` не задают и компилируются без правки.
- Полей `className`/`style`/`as` нет. Оформление выбирается только закрытыми списками (`layout`, `backdrop`, иконки `IconName`).
  Проверка в `landing.guard.test.ts` (`/\b(className|style|as)\??:/` по `types.ts`) остаётся зелёной без правки. Имена `layout`,
  `backdrop`, `facts` под неё не попадают.

**Ответ на A41-1: что будет, если вариант с панелью включён, а слот не передан.**
1. **Ошибка типов.** Пропсы `ServiceLanding` — объединение (§41.3.2). Для `LandingPanelConfig` проп `heroAside: ReactElement`
   обязателен. Значения `undefined` и `null` тип не принимает. Обычному конфигу `heroAside` передать нельзя (`heroAside?: undefined`).
2. **Во время выполнения** (обход типов через `as any`) страница не падает. Шапка рисуется без колонки панели: текст на всю ширину,
   рамка панели не выводится. Проверяет T41-01g.

### §41.3.2. `ServiceLanding`: две ветки разметки

```ts
export type ServiceLandingProps =
  | { config: LandingConfig; catalog: ReactNode; heroAside?: undefined }
  | { config: LandingPanelConfig; catalog: ReactNode; heroAside: ReactElement }

export function ServiceLanding(props: ServiceLandingProps): JSX.Element
```

Ветка выбирается по `props.config.hero.layout === 'panel'`. Сужение — через функцию-предикат
`isPanelProps(p): p is Extract<ServiceLandingProps, { config: LandingPanelConfig }>`, потому что TS 5.5 не сужает внешнее
объединение по вложенному полю.

**Ветка по умолчанию («Запись», «Заказы») — без единого изменения в разметке.** Строка класса `main` остаётся литералом
`'max-w-[1180px] mx-auto px-4 sm:px-8 pt-10 md:pt-16 pb-10'`, состав и порядок детей те же. Код этой ветки переносится
как есть. Если его выносят во вспомогательную функцию, вывод `container.innerHTML` для `makeConfig()` не должен измениться
(проверяет T41-02).

**Ветка `panel`:**

```
<main class="pb-10">                                              ← MAIN_PANEL, без max-w и px: полоса фона во всю ширину
  <LandingPanelHero config={hero} aside={heroAside} />            ← §41.3.3
  <div class={CONTAINER}>                                         ← CONTAINER = 'max-w-[1180px] mx-auto px-4 sm:px-8'
    LandingCatalogFrame            (как в ветке по умолчанию, mt-8 scroll-mt-24)
    LandingMedia                   если config.media
    LandingClientsSection          placeholder — по тому же правилу «один раз» (§38.3.6)
    LandingBusinessSection
    LandingPricingSection          если config.pricing
    FaqSection
  </div>
</main>
```

- Порядок секций, правило заглушки скриншота и наличие тарифов вычисляются **одним** кодом для обеих веток. Отличается только
  обёртка и шапка. Код секций 2–6 не дублируется: это общий фрагмент, который вставляется в обе обёртки.
- Константа `CONTAINER` дописывается в `classes.ts`. В ветке по умолчанию **не используется**: там остаётся литерал, чтобы строка
  класса `main` гарантированно не изменилась.
- Вертикальные отступы панельной ветки: `pt-10 md:pt-16` переезжают внутрь полосы шапки (§41.3.3), `pb-10` остаётся у `main`.

### §41.3.3. `LandingPanelHero` — разметка (ответ на A41-2)

```
<div class="relative overflow-hidden border-b border-line bg-gradient-to-b from-cream-deep to-cream">   ← полоса во всю ширину main
  {backdrop !== 'none' && <LandingHeroBackdrop kind={backdrop} />}                                       ← absolute, снизу, aria-hidden
  <div class="relative {CONTAINER} pt-10 md:pt-16 pb-20 md:pb-28 grid gap-8 lg:grid-cols-[1.05fr_1fr] lg:items-center">
    <div>                                                                       ← колонка текста
      <p class="inline-flex items-center rounded-full border border-line bg-white px-3 py-1 mb-4
                text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark">{eyebrow}</p>
      <h1 class="font-serif text-[36px] sm:text-[48px] leading-[1.08] font-medium text-ink">       ← классы = LandingHero (T41-01f)
        {lead}<br/><em class="text-gold-dark italic">{accent}</em>                              ← или строка
      </h1>
      <p class="mt-4 max-w-[520px] text-[16px] leading-relaxed text-ink-soft">{intro}</p>
      <p class="mt-2"><a href={howTo.href} class="inline-flex items-center min-h-[44px] text-[15px] font-semibold text-ink
                underline underline-offset-4 hover:no-underline {FOCUS_RING}">{howTo.label}</a></p>
      {facts.length > 0 &&
        <ul role="list" class="mt-4 flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:gap-x-6">
          <li class="flex items-center gap-2 text-[14px] text-ink">
            <Icon name={icon} size={16} strokeWidth={1.6} class="shrink-0 text-gold-dark" aria-hidden />{text}
          </li> …
        </ul>}
    </div>
    {aside &&
      <div class="rounded-3xl border border-line bg-white p-5 shadow-soft sm:p-6">{aside}</div>}       ← рамка панели — шаблон
  </div>
</div>
```

**Ответ на A41-2: как сделать полосу во всю ширину.** Для варианта `panel` шапка **выходит из контейнера на уровне
`ServiceLanding`**: у `main` нет `max-w` и `px`, контейнер рисуется внутри полосы и вокруг секций 2–6. Отвергнутые способы:
- `w-screen` / `100vw` + `left-1/2 -translate-x-1/2` или отрицательные поля. `100vw` включает ширину полосы прокрутки, поэтому
  на Windows и в браузерах с постоянной полосой появляется горизонтальная прокрутка (R41-6). Убрать её можно только
  `overflow-x: clip` на предке, а это меняет `main` у «Записи» и «Заказов»;
- фон на `body` или в `DomApp`. Тогда шапка шаблона зависела бы от раскладки приложения, и «Баням» пришлось бы повторять это у себя.

Полоса — обычный блок шириной `main` (= ширина окна без полосы прокрутки), поэтому горизонтальной прокрутки нет.
`overflow-hidden` стоит только на полосе и обрезает SVG.

**Рамка панели** (белая карточка) — в шаблоне. Слот кладёт внутрь своё содержимое (у dom — `<form aria-label="Подбор дома">`)
без собственной карточки. Шаблон не добавляет вокруг слота ни заголовка, ни ориентира, ни `aria-*`: доступное имя даёт сам слот.

**Мобильная раскладка (< 1024 px):** одна колонка в порядке DOM: eyebrow → `h1` → абзац → якорь → факты → панель. На ≥ 1024 px
панель стоит справа (`lg:grid-cols-[1.05fr_1fr]`). Порядок Tab совпадает с порядком DOM: якорь «Как забронировать» → поля панели →
каталог (US-41-08). Факты не фокусируются.

**Фон «горы» (`LandingHeroBackdrop.tsx`):**
- `Record<Exclude<HeroBackdrop, 'none'>, () => JSX.Element>`. Сейчас в нём один ключ `mountains`;
- `<svg aria-hidden="true" focusable="false" viewBox="0 0 1200 120" preserveAspectRatio="none"
  class="pointer-events-none absolute inset-x-0 bottom-0 h-16 w-full md:h-24 text-line-strong">`;
- два контура хребта: `<path fill="none" stroke="currentColor" stroke-width="1.5" vector-effect="non-scaling-stroke" d="…"/>`.
  Дальний контур с `opacity="0.6"`. Без `<title>`, без заливки, без текста. Пример контура (разработчик может перерисовать
  в тех же рамках): `M0 96 L140 60 L230 84 L360 30 L470 78 L590 44 L700 88 L820 36 L930 70 L1040 50 L1200 92`;
- **рисунок не пересекается с текстом.** Нижнее поле полосы (`pb-20` = 80 px при высоте рисунка 64 px, `md:pb-28` = 112 px при
  96 px) оставлено под горы. Панель — непрозрачная белая карточка, поэтому линии за ней не видны. Тонкая линия `#D8C9AE` под текстом
  `ink-soft` дала бы контраст 3.8:1, это ниже AA. Поэтому рисунок только в нижнем поле;
- полоса с фоном «горы» не добавляет сетевых запросов и не влияет на LCP.

### §41.3.4. Контраст (US-41-08, AA 4.5:1) — посчитано по токенам пресета

| Текст | Цвет | Фон (худший случай) | Контраст | Итог |
|---|---|---|---|---|
| eyebrow, 13 px | `gold-dark` #8F6C46 | **белая плашка** #FFFFFF | 4.8:1 | AA. Поэтому eyebrow на плашке, а не прямо на полосе |
| eyebrow без плашки (не делать) | `gold-dark` | `cream-deep` #F1E9DC | 4.0:1 | ниже AA |
| `h1` | `ink` #2B2420 | `cream-deep` | > 12:1 | AA |
| выделение в `h1` (36–48 px — крупный текст) | `gold-dark` | `cream-deep` | 4.0:1 | AA для крупного (≥ 3:1) |
| абзац, якорь | `ink-soft` #6B5F52 / `ink` | `cream-deep` | 5.2:1 / > 12:1 | AA |
| факты | `ink` | `cream-deep`…`cream` | > 12:1 | AA |

Попутная находка, в цикле **не исправляется**: у «Записи» и «Заказов» eyebrow `gold-dark` на `cream` даёт 4.4:1, это чуть ниже
AA. Разметку их шапок трогать нельзя (US-41-07). Пункт записывается в `CURRENT_STATE.md` §9.9 (§41.11).

---

## §41.4. Перенос `CatalogPage` на шаблон

### §41.4.1. Где живёт состояние (ответ на A41-3)

`dom/src/hooks/useCatalogFilters.ts`:

```ts
export function useCatalogFilters(): {
  filters: CatalogFilters                       // useMemo(() => parseCatalogFilters(sp), [sp])
  setFilters: (next: CatalogFilters) => void    // setSp(toSearchParams(next)) — новая запись истории, как сейчас
  resetFilters: () => void                      // setSp(new URLSearchParams()) — адрес снова «/»
}
```

- **URL — единственное общее состояние.** Панель и каталог не передают друг другу ни пропсов, ни контекста, ни стора: оба читают
  URL через хук. Значит, «назад/вперёд» и ссылка с фильтрами обновляют обе части одним механизмом.
- **Эффект пишет в URL только в панели**: это задержанная запись «Гостей» и «Цены», перенос строк 57–64 нынешнего файла как есть.
  Каталог пишет в URL только по прямому действию пользователя: смена страницы в `Pagination` и «Сбросить фильтры» в пустом
  состоянии. Так не будет ни петли эффектов, ни двойной записи истории (R41-2).
- `catalogQuery.ts` не меняется (`parseCatalogFilters`, `toSearchParams`, `toApiQuery`, `hasActiveFilters`, `houseLink`).

### §41.4.2. `StaySearchPanel` (слот `heroAside`)

Перенос строк 36–82 и 109–180 нынешнего `CatalogPage.tsx` **без изменения поведения и текстов**:
- корень — `<form aria-label="Подбор дома" onSubmit={preventDefault + onDates(checkIn, checkOut)}>`. Классов карточки
  (`rounded-3xl border bg-white p-5 shadow-soft`) у формы **больше нет**, их даёт рамка шаблона. Остальные классы формы и полей
  переносятся как есть (внутри слота классы сервиса допустимы, как у каталога в цикле 38);
- 4 поля с `label` («Заезд», «Выезд», «Гостей», «Цена за ночь до, ₽»), сетка `grid grid-cols-2 gap-3`, `min-h-[44px]`;
- `min` заезда — `deviceToday()` (функция переезжает в файл панели), `min` выезда — `addDays(checkIn, 1)`;
- правило очистки выезда при заезде ≥ выезда, тексты подсказок, `p[role="status"]`;
- синхронизация полей с URL (эффект по `filters.checkIn/checkOut/guests/maxPrice`);
- `useDebouncedValue` (400 мс) для «Гостей» и «Цены», сброс страницы на 1;
- «Сбросить фильтры» (`Button variant="ghost" size="sm"`) только при `hasActiveFilters`: `resetFilters()` и сброс подсказки.

Известная мелочь сохраняется как есть: «Сбросить фильтры» в пустом состоянии каталога не гасит подсказку о датах в панели. Так было
и раньше. Исправлять это — менять поведение, это не задача цикла.

### §41.4.3. `StayCatalog` (слот `catalog`) — ответ на A41-4

Перенос строк 84–88 и 184–233 как есть. Изменения только в обёртке:

```
<>                                                         ← корень без section и без контейнера (рамку и поля даёт шаблон)
  <h2 id="houses-title" class="font-serif text-[32px] font-medium text-ink mb-4">Дома в Шерегеше</h2>   ← как h2 города у «Записи»
  <div aria-live="polite">                                 ← только список и состояния, формы тут нет
    загрузка (три скелета) | ошибка | пусто (два текста) | счётчик + ul.grid + Pagination
  </div>
</>
```

- **A41-4: `h2` «Дома в Шерегеше» есть.** Тогда структура заголовков ровная: `h1` → `h2` (каталог) → `h3` (карточки) → `h2`
  (секции 3, 4, 6). Сейчас `h3` карточек идут сразу после `h1`. Заголовок стоит вне `aria-live`, чтобы читалка не озвучивала его
  при каждой смене фильтра.
- queryKey `['stays-catalog', toApiQuery(filters)]`, `placeholderData: (prev) => prev`, тексты состояний, счётчик, пагинация по 12,
  `HouseCard item filters` — без изменений. Сетка `grid gap-5 sm:grid-cols-2 lg:grid-cols-3` остаётся.
- `useAuthStore` в каталоге больше не нужен: кнопки владельцев ушли в конфиг (`auth-route`).

### §41.4.4. `CatalogPage.tsx` после переноса

```tsx
export function CatalogPage() {
  return <ServiceLanding config={stayLanding} heroAside={<StaySearchPanel />} catalog={<StayCatalog />} />
}
```

В файле нет `<main`, `<section`, `<h1`, `<h2` (guard T41-03). `DomApp` не меняется: маршрут `/` → `CatalogPage`, один `DomFooter`,
своего `main` у раскладки нет. Значит, на `/` один `main` (из шаблона) и один `footer`.

### §41.4.5. Что сохраняется (контрольный список для разработчика и QA)

| Что | Где после переноса |
|---|---|
| `form[aria-label="Подбор дома"]`, `label` у каждого поля, `p[role="status"]` с подсказкой | `StaySearchPanel` |
| URL-параметры `checkIn`, `checkOut`, `guests`, `maxPrice`, `page`; значения по умолчанию в адрес не пишутся; битые отбрасываются | `useCatalogFilters` + `catalogQuery.ts` |
| Одна запись истории на итог ввода «Гостей»/«Цены» после паузы | `StaySearchPanel` |
| `aria-live="polite"` только над списком и состояниями | `StayCatalog` |
| Тексты: «Не удалось загрузить каталог.», «Пока нет домов в каталоге», «Как только владельцы опубликуют дома, они появятся здесь.», «По этим условиям домов нет», «Попробуйте другие даты, меньше гостей или более высокую цену — или сбросьте фильтры.», «Все дома: N», «Свободно на ваши даты: N», «Сбросить фильтры» | `StayCatalog`, `StaySearchPanel` |
| Ссылка карточки с датами и `adults` | `HouseCard` → `houseLink` (без изменений) |
| «Подключить дома»: гость → `/register?returnTo=%2Fcabinet%2Fnew`, вошедший → `/cabinet/new` | `stayLanding.business.actions[0]` (`auth-route`) |
| Якорь на секцию гостей | `#guests-title` (`h2`, `tabIndex=-1` — даёт шаблон) |
| Новые id | `houses`, `houses-title`, `guests-title`, `guests-steps-title`, `guests-list-title`, `biz-title`, `biz-steps-title`, `faq`, `faq-title` |

---

## §41.5. Конфиг `dom/src/landing/stayLanding.ts`

`export const stayLanding: LandingPanelConfig`. Тексты — SPEC §4 **дословно** с поправками §41.0. Комментарий в шапке файла:
«черновики на вычитку заказчиком и юристом вместе с комплектом Stay* (SPEC §0)».

### §41.5.1. Поля

| Поле | Значение |
|---|---|
| `hero` | `layout: 'panel'`, eyebrow «Шерегеш · посуточно», `title: { lead: 'Дом на склоне,', accent: 'а не номер в отеле' }` (неразрывный пробел — как сейчас), intro «Свободные даты видны сразу. Бронь держится за вами, пока вы оплачиваете её напрямую владельцу — без комиссии с гостя.», `howTo: { kind: 'anchor', href: '#guests-title', label: 'Как забронировать' }`, `facts`: `map-pin` «Только дома в Шерегеше», `calendar` «Свободные даты видны сразу», `credit-card` «Предоплата — напрямую владельцу»; `backdrop: 'mountains'` |
| `catalog` | `{ id: 'houses', ariaLabel: 'Дома в Шерегеше' }` |
| `media` | нет |
| `clients` | SPEC §4.3 дословно: eyebrow «Для гостей», title «Забронируйте дом на свои даты — без звонков и переписки», `titleId: 'guests-title'`, text — SPEC; actions `[{anchor '#houses' «Выбрать дом»}, {route '/bookings' «Мои брони»}]`; stepsPanel «Как забронировать», `titleId: 'guests-steps-title'`, шаги `calendar` / `credit-card` / `check-circle`; list «Что важно знать», `titleId: 'guests-list-title'`, 5 пунктов, note — SPEC; **без `screenshot`** → заглушка «Здесь будут скриншоты» один раз, в секции 3 |
| `business` | SPEC §4.4 дословно: eyebrow «Для владельцев», title «Свободные даты, брони и оплаты по всем домам — в одном месте», `titleId: 'biz-title'`, text — нынешний текст плашки дословно; actions `[{auth-route guestTo '/register?returnTo=%2Fcabinet%2Fnew', authedTo '/cabinet/new', «Подключить дома»}, {route '/cabinet' «Войти в кабинет»}]`; benefits — 6 строк SPEC; stepsPanel «Как начать», `titleId: 'biz-steps-title'`, шаги `store` / `home` / `qr-code`; без `screenshot` |
| `pricing` | **нет** (Q41-3) |
| `faq` | `{ items: stayFaq }` |

`/bookings` и `/cabinet` закрыты `RequireAuth`: гость уходит на `/login?returnTo=…` и возвращается обратно. Это уже есть в
`DomApp`, проверено по коду.

### §41.5.2. Порядок секций на странице

`h1` (шапка) → `form[aria-label="Подбор дома"]` (панель) → `section#houses` → `h2#guests-title` → `h2#biz-title` → `section#faq`.
Медиа-блока и `#pricing-title` нет.

### §41.5.3. FAQ — `dom/src/landing/stayFaq.ts`

`export const stayFaq: FaqItems` — 8 пунктов, тексты SPEC §5, п. 1–8, **дословно**, с поправками:

| № | Поправка |
|---|---|
| 7 | вопрос «В каких местах есть дома?», ответ **«Только в Шерегеше.»** (решение «Только Шерегеш»). Запасной вопрос о туристическом налоге не используется |
| 8 | вопрос **«Как сдавать свои дома через EZBOOK Дома?»** (Q41-4). Ответ — SPEC без изменений |
| 1 | пометка SPEC «сверить»: попадает ли бронь вошедшего в `/bookings`. Разработчик сверяет по `MyBookingsPage` и `BookingPanel`. Если не подтвердилось, правится текст, а не продукт, и это записывается в отчёт задачи |
| все | ни одного `link`. Ответы — строки или массивы строк, без HTML |

---

## §41.6. `HouseCard`: без обрезки названия и адреса

Правка — только классы, разметка и порядок элементов не меняются:

| Элемент | Было | Стало |
|---|---|---|
| название компании `p` | `text-xs font-medium uppercase tracking-wide text-gold-dark` | + `break-words [overflow-wrap:anywhere]` |
| название дома `h3` | `font-serif text-xl leading-snug text-ink` | + `break-words [overflow-wrap:anywhere]` |
| адрес `span` | `line-clamp-2` | `min-w-0 break-words [overflow-wrap:anywhere]` (без `line-clamp`) |

Остальное в карточке не меняется (US-41-03). Правка видна и на странице компании `/:slug`: это ожидаемо (R41-7). `CatalogCard`
шаблона не используется, потому что у карточки дома обложка, цена за даты и признак «Занято». Общая у них только колонка текста,
а не карточка.

---

## §41.7. Модель данных

Не меняется. Схема БД, DTO (`StayCatalogItemDto`) и `CatalogQuery` те же.

---

## §41.8. Производительность и сеть

- На `/` по-прежнему **один** запрос `GET /api/stays/catalog` и ни одного запроса тарифов: в конфиге нет `pricing`, в `DomNavbar`
  нет `PricingNavLink`.
- Картинок в первом экране нет, есть только инлайн-SVG. Обложки карточек — `loading="lazy"`, как сейчас.

---

## §41.9. Безопасность и право

- Нет `dangerouslySetInnerHTML` в шаблоне (проверяет существующий `landing.guard.test.ts`, он автоматически сканирует новые
  файлы шаблона) и в конфиге dom (T41-03).
- Реквизиты на главной не выводятся. `noindex` (`X-Robots-Tag` vhost) не трогается. В коде страницы нет `meta robots`,
  `canonical` и JSON-LD.
- Тексты «Для гостей», «Для владельцев» и FAQ — черновики. До снятия `noindex` и приглашения владельцев их вычитывают
  `legal-counsel` и живой юрист вместе с комплектом `Stay*` (SPEC §0, R41-4).

---

## §41.10. Тесты

### §41.10.1. Новые

| Id | Файл | Что проверяет |
|---|---|---|
| T41-01 | `src/components/landing/LandingPanelHero.test.tsx` (на `makePanelConfig`, без зависимостей от dom) | a) порядок `h1` → слот `heroAside` → `#cat` → `#c-title` → `#b-title` → `#faq`; один `main`, один `h1`; b) слот внутри рамки, рамка внутри полосы шапки, до `#cat`; c) у `main` нет класса `max-w-[1180px]`, у обёртки секций — есть; d) `backdrop: 'mountains'` → один `svg[aria-hidden="true"][focusable="false"]` без `title` и текста; `'none'` → svg нет; e) 3 факта → `ul` с 3 `li`, у иконок `aria-hidden`; 0 фактов → `ul` нет; f) `h1.className` в панельной шапке совпадает с `h1.className` обычной (паритет); у якоря `howTo` есть `href` и `min-h-[44px]`; g) панельный конфиг без `heroAside` (через `as never`) рисуется без рамки панели и не падает; h) порядок Tab (`user-event`): якорь → первое поле слота |
| T41-01t | там же, `// @ts-expect-error` (проверяет `tsc -p tsconfig.dom.json --noEmit` и корневой `tsc`) | панельный конфиг без `heroAside`; `heroAside={null}`; обычный конфиг с `heroAside`; 4 факта; `backdrop: 'photo'`; `primaryAction` в панельной шапке; `LandingPanelConfig` в переменную типа `LandingConfig` |
| T41-02 | там же | регрессия ветки по умолчанию: для `makeConfig()` у `main` ровно `'max-w-[1180px] mx-auto px-4 sm:px-8 pt-10 md:pt-16 pb-10'`, первый ребёнок `main` — `div.max-w-[720px]` с `h1`; нет `svg[aria-hidden]` фона и рамки панели. Плюс эталон `container.innerHTML` для `makeConfig()`: строку снимают **до** правки `ServiceLanding` и вписывают в тест литералом |
| T41-03 | `dom/src/landing/configs.guard.test.ts` (A41-5: **отдельный файл в dom**, а не правка goods-guard: тот считает `pages === 2` и не должен меняться) | `stayLanding.ts`/`stayFaq.ts` — `.ts`, без `className` и JSX (`/<[A-Za-z]+[\s/>]/`); `pages/CatalogPage.tsx` без `<main|<section|<h1|<h2`; `DomApp.tsx` содержит `<DomFooter />` ровно один раз и не содержит `<main`; FAQ 6–8 (сейчас 8); ни у одного пункта нет `link`; в JSON конфига и FAQ нет `/pricing`, «Тарифы», `₽`, `руб`; запрещённые слова — см. ниже; `stayLanding.pricing` и `.media` — `undefined`; `hero.layout === 'panel'`, `backdrop === 'mountains'`, 3 факта; `hero.intro` содержит «напрямую владельцу»; ответ FAQ 7 содержит «Шерегеш»; адреса двух кнопок владельцев; в тексте нет «ezbook ·» |
| T41-04 | `dom/src/pages/CatalogPage.test.tsx` (`createMemoryRouter` + `RouterProvider`, мок `../api/publicStays`, `useAuthStore` через `setState`) | a) структура: один `main`, один `h1`, нет `footer`; порядок §41.5.2; нет `#pricing-title`; `section#houses[aria-label="Дома в Шерегеше"]` содержит `h2` «Дома в Шерегеше»; элемент `[aria-live="polite"]` содержит список и не содержит форму; одна заглушка скриншота, внутри секции `#guests-title`; b) поля по `getByLabelText`; `min` заезда = сегодня (`vi.setSystemTime`), `min` выезда = заезд + 1; c) `/?checkIn=…&checkOut=…&guests=3&maxPrice=5000&page=2` заполняет форму; `publicStaysApi.catalog` вызван ровно с `{checkIn, checkOut, guests:3, maxPricePerNight:5000, page:2, pageSize:12}` и **один раз**; d) битые параметры (`checkOut < checkIn`, `guests=0`, `maxPrice=abc`) → запрос `{guests:1, page:1, pageSize:12}`, страница не падает; e) даты: заезд ≥ выезда очищает выезд; одна дата → «Укажите обе даты: заезд и выезд» в `role=status`; выезд ≤ заезда → «Дата выезда должна быть позже даты заезда»; обе корректны → обе в URL, `page` сброшен; f) ввод «12» в «Гостей» (fake timers, 400 мс) → ровно одна новая запись истории (по числу различных `location.key`, которые записал компонент-проба); `router.navigate(-1)` возвращает и форму, и запрос; g) «Сбросить фильтры» есть только при фильтрах, после нажатия `location.search === ''` и подсказка пустая; h) состояния: загрузка, ошибка с повтором, пусто без фильтров, пусто с фильтрами (оба текста + кнопка), счётчик «Все дома: N» / «Свободно на ваши даты: N», пагинация при `totalCount > 12`; i) ссылка карточки содержит даты и `adults`; j) кнопки: «Как забронировать» → `#guests-title`, «Выбрать дом» → `#houses`, «Мои брони» → `/bookings`, «Подключить дома» (гость / вошедший), «Войти в кабинет» → `/cabinet`; k) FAQ: 8 кнопок `aria-expanded="false"`, ссылок внутри `#faq` нет; l) название из 200 символов без пробелов и адрес из 300 символов выводятся целиком |
| T41-05 | `dom/src/components/HouseCard.test.tsx` | название дома, компании и адрес длиной 200/120/300 символов (`textContent` целиком); у `h3`, `p` компании и `span` адреса нет `truncate`/`line-clamp-*` и есть `break-words`; остальное без изменений: «Занято на выбранные даты», «до N гостей», «· без собак», номер в реестре, три варианта цены, `href` с датами |

**Запрещённые слова (T41-03).** В JS `\b` не работает с кириллицей, поэтому границы слова — через `\p{L}` с флагом `u`:
- во всём конфиге и FAQ: `/(?<!\p{L})задат(ок|ка|ку|ком)(?!\p{L})/iu`, `/невозвратн/iu`, `/депозит/iu`,
  `/(?<!\p{L})чек(а|и|ом|у|ов)?(?!\p{L})/iu`, `/(?<!\p{L})бан(я|и|ь|ей|ю)(?!\p{L})/iu`, `/(?<!\p{L})чан(ы|ов|ах)?(?!\p{L})/iu`;
- в `stayLanding.business` (Q41-2): `/бесплатн/iu`, `/пробн/iu`, `/присоединяйтесь/iu`, `/начните/iu`, `/попробуйте/iu`.
  Слово «попробуйте» проверяется только тут: в пустом состоянии каталога («Попробуйте другие даты…») оно законно, но тот текст
  лежит не в конфиге.

### §41.10.2. Регрессия (без правки тестов)

- Шаблон: `ServiceLanding.test.tsx` (T38-01…05), `FaqSection`, `LandingActionLink`, `LandingMedia`, `LandingPricingSection`,
  `CatalogCard`, `landing.guard.test.ts`.
- «Запись»: `src/pages/HomePage.test.tsx`, `zapisFaq.test.ts`. «Заказы»: `goods/src/pages/CatalogHomePage.test.tsx`,
  `goods/src/landing/*.test.tsx` (T27/T30/QA27/QA30 после переноса в цикле 38), `goods/src/landing/configs.guard.test.ts`.
- Guard-тесты приложений: `goods/src/sharedSources.guard.test.ts`, `dom/src/sharedSources.guard.test.ts`,
  `src/test/testAreas.guard.test.ts`, `dom/src/domRoutes.test.ts`.
- Все тесты dom (`npm run test:area -- stays`).
- Если какой-то из этих тестов приходится править, это сигнал, что разметка «Записи»/«Заказов» изменилась. Работа
  останавливается и разбирается, тест не подгоняется.

### §41.10.3. Команды (для разработчика перед сдачей и для QA)

```
cd frontend
npm run test:area -- stays companies orders
npx vitest run                      # полный прогон, не параллельно с другим полным прогоном (память: ложные падения)
npx tsc --noEmit && npx tsc -p tsconfig.dom.json --noEmit && npx tsc -p tsconfig.goods.json --noEmit   # @ts-expect-error T41-01t
npm run lint
npm run build && npm run build:goods && npm run build:dom
git diff develop -- src/pages/home goods/src/landing src/components/landing/LandingHero.tsx frontend/shared-sources.js   # пусто
```

### §41.10.4. Области быстрого контура (ответ на A41-6)

**Да:** `"src/components/landing/"` дописывается в `frontendPathPrefixes` области `stays` в `contracts/cycle36/test-areas.json`.
Тогда `npm run test:area -- stays` прогоняет тесты шаблона, и правка шаблона ловится в контуре dom. Сейчас этот префикс есть
только у `companies`. Дубль области допустим: прецедент — цикл 38, §38.12.4. Новые файлы `dom/src/**` уже попадают в `stays`
через префикс `dom/src/`. `ci.yml` не меняется.

---

## §41.11. Документы и нумерация

| Документ | Что дописать | Кто |
|---|---|---|
| `CURRENT_STATE.md` | **§5.14** «Главная „Домов“ на едином шаблоне, шапка с панелью — цикл 41»: `LandingPanelConfig`/`heroAside`, две ветки `ServiceLanding`, `LandingPanelHero`/`LandingHeroBackdrop`, `StaySearchPanel`/`StayCatalog`/`useCatalogFilters`, `stayLanding`/`stayFaq`, `HouseCard` без обрезки, тесты T41-01…05. **§9.9** «Долг и риски цикла 41»: контраст eyebrow 4.4:1 у «Записи»/«Заказов» (§41.3.4), бренд в шапке и подвале dom («ezbook · Дома» ≠ «EZBOOK Дома»), черновики FAQ до юриста, подсказка дат после сброса в пустом состоянии (§41.4.2), тексты об услугах после слияния цикла 39. Строка в §10.5 «Документы циклов» | FE (T-41-06) |
| `TEST_CATALOG.md` | раздел «Цикл 41»: T41-01…05 и ручные кейсы QA (T-41-07) | FE (автотесты), QA (ручные) |
| `CHANGELOG.md` (Unreleased) | главная «Домов» на шаблоне: шапка с панелью подбора, «Для гостей», «Для владельцев», FAQ; полные названия и адреса в карточке дома | FE |
| `DEPLOY.md` | раздел «Цикл 41»: «Ручных шагов нет, миграций нет, выкат обычным порядком. `noindex` не снимать (§28 п. 7)» | FE |

**Нумерация при слиянии с циклом 39.** §9.8 занят циклом 38. Цикл 39 в своей ветке заводит «§9.8», поэтому при его слиянии он
перенумеровывается. Правило: какой цикл вливается **вторым**, тот берёт следующий свободный номер (§5.15/§9.10) и правит ссылки
на себя. Остальные строки в `CHANGELOG.md`, `TEST_CATALOG.md` и `DEPLOY.md` сводятся вручную по строкам.

---

## §41.12. Задачи

Владельцы: **FE** = frontend-developer, **QA** = qa-engineer. Бэкенд и devops не участвуют.

| Id | Владелец | Задача | Зависит от | Файлы |
|---|---|---|---|---|
| T-41-01 | FE | Расширение шаблона. Первым делом снять эталон `innerHTML` для T41-02 на нетронутом коде. Затем `types.ts` (§41.3.1), `classes.ts` (`CONTAINER`), `ServiceLanding` (две ветки, §41.3.2), `LandingPanelHero`, `LandingHeroBackdrop` (§41.3.3), `makePanelConfig` в `testConfig.ts`. Тесты T41-01, T41-01t, T41-02. Прогон регрессии шаблона, «Записи» и «Заказов» (§41.10.2) | — | `src/components/landing/*` |
| T-41-02 | FE | `HouseCard` (§41.6), `catalogItemFixture`/`catalogPageFixture` в `test/fixtures.ts`, тест T41-05. Глазами проверить `/:slug` с длинным названием | — | `dom/src/components/HouseCard.tsx`, `dom/src/test/fixtures.ts` |
| T-41-03 | FE | `useCatalogFilters`, `StaySearchPanel`, `StayCatalog`: перенос из `CatalogPage` без изменения поведения (§41.4.1–§41.4.3). Пока T-41-05 не сделан, `CatalogPage` можно временно собрать из двух новых компонентов в старой обёртке, чтобы ветка компилировалась | — | `dom/src/hooks/useCatalogFilters.ts`, `dom/src/components/catalog/*` |
| T-41-04 | FE | `stayLanding.ts`, `stayFaq.ts` (§41.5), сверка FAQ п. 1 по коду, guard T41-03 | T-41-01 (типы) | `dom/src/landing/*` |
| T-41-05 | FE | `CatalogPage` на `ServiceLanding` (§41.4.4), тест T41-04, префикс в `test-areas.json` (§41.10.4). Все команды §41.10.3 | 01, 02, 03, 04 | `dom/src/pages/CatalogPage.tsx`, `CatalogPage.test.tsx`, `contracts/cycle36/test-areas.json` |
| T-41-06 | FE | Документы §41.11: `CURRENT_STATE.md` §5.14/§9.9/§10.5, `CHANGELOG.md`, автотесты в `TEST_CATALOG.md`, `DEPLOY.md` | 05 | корневые `.md` |
| T-41-07 | QA | Полный прогон §41.10.3 (по одному, не параллельно). Ручные кейсы: 360 / 768 / 1024 / 1280 px — порядок «заголовок → панель → каталог», панель справа с 1024 px, **нет горизонтальной прокрутки** (в том числе Windows/Chrome с постоянной полосой прокрутки); поля ≥ 44 px; горы не заходят под текст; контраст eyebrow, абзаца и выделения по §41.3.4 (замер инструментом); клавиатура: Tab «якорь → 4 поля → каталог», FAQ Enter/пробел; VoiceOver: один `main`, один `h1`, структура `h2`, имя формы «Подбор дома», `aria-live` озвучивает только список; ссылка с фильтрами, «назад/вперёд», битые параметры; длинное название и адрес на `/` и `/:slug`; в сети на `/` один `GET /api/stays/catalog` и ни одного `/api/pricing*`; в ответе vhost есть `X-Robots-Tag: noindex`, в DOM нет `meta robots`, `canonical` и `application/ld+json`; «Запись» и «Заказы»: `/` выглядят как до цикла (сравнение со скриншотами `develop`). Ручные кейсы — в `TEST_CATALOG.md` | 01–06 | — |

### Параллельность

```
FE:  T-41-01 ──┐
     T-41-02 ──┼── T-41-04 (после 01) ── T-41-05 ── T-41-06
     T-41-03 ──┘
QA:                                                  └── T-41-07
```

- 01, 02 и 03 не пересекаются по файлам: шаблон, карточка, новые компоненты dom. Их можно делать параллельно (два разработчика)
  или в любом порядке.
- 04 нужен только `types.ts` из 01. Конфиг можно начать сразу, когда типы закоммичены, не дожидаясь `ServiceLanding`.
- Конфликтные файлы: `dom/src/test/fixtures.ts` (02 дописывает, 05 только читает), `src/components/landing/testConfig.ts` (только 01),
  `contracts/cycle36/test-areas.json` (только 05).

---

## §41.13. Технические риски и открытые пункты

| # | Риск | Решение |
|---|---|---|
| R41-1 | Правка `ServiceLanding`/`types.ts` меняет разметку «Записи»/«Заказов» | Ветка по умолчанию — литерал прежней разметки; `LandingHero.tsx` не трогается; T41-02 с эталоном `innerHTML`; тесты §41.10.2 без правки; `git diff` §41.10.3 |
| R41-2 | Двойная запись истории или петля эффектов (панель и каталог на одном URL) | URL пишет из эффекта только панель; каталог пишет только по клику (§41.4.1); T41-04f |
| R41-3 | Полный блок «Для владельцев» читается как приглашение | Тексты SPEC §4.4 без призывов; guard запрещённых слов (T41-03) |
| R41-4 | FAQ разойдётся с правовыми текстами `Stay*` после юриста | Без процентов и сроков; вычитка вместе с `Stay*` до снятия `noindex` (§41.9) |
| R41-5 | Нет токенов Tailwind в dom | Снят: общий пресет, `src/components/**` сканируется dom (`sharedSources.guard`) |
| R41-6 | Полоса во всю ширину даёт горизонтальную прокрутку | Без `100vw`: шапка вне контейнера на уровне `ServiceLanding` (§41.3.3); ручной кейс Windows и 360 px |
| R41-7 | Высота карточек на `/:slug` меняется | Ожидаемо (US-41-03); ручной кейс |
| R41-8 | Тарифы «по аналогии» с «Заказами» | Нет `pricing` в конфиге; guard на `/pricing`, «Тарифы», `₽` (T41-03) |
| R41-9 | eyebrow `gold-dark` на светлом фоне ниже AA | В панельной шапке — на белой плашке (4.8:1). У «Записи»/«Заказов» — долг в §9.9, не трогаем |
| R41-10 | `layout?: never` в `LandingHeroConfig` ломает чужой код | Поле необязательное, его никто не задаёт; `tsc` трёх приложений в §41.10.3 |
| R41-11 | Слияние с циклом 39 (`CompanyPage` с услугами, нумерация `CURRENT_STATE`) | `CompanyPage` не трогаем; правило нумерации §41.11; после слияния — ручной кейс `/:slug` с услугами и длинными названиями |
| R41-12 | Поля `type=date` 2×2 на 360 px в iOS Safari шире колонки | Сетка и классы полей те же, что сейчас на бою; ручной кейс iOS в T-41-07. Если поле не помещается — `min-w-0` у `label` (правка внутри слота dom, шаблон не меняется) |

**Открытые пункты (не блокируют старт):**
1. Шапка и подвал dom пишут бренд «ezbook · Дома», а решение Q41-4 — «EZBOOK Дома». В этом цикле меняются только тексты главной.
   Привести `DomNavbar` (в том числе `aria-label`) и `DomFooter` к одному написанию — отдельная правка по команде заказчика.
2. После слияния цикла 39 (услуги-слоты) отдельной правкой конфига добавить услуги в «Для гостей» и FAQ. Это вопрос к заказчику.
3. Все тексты главной — черновики на вычитку заказчиком (SPEC §4, §5).
