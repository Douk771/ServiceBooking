# ARCHITECTURE — цикл 46 ServiceBooking: главная «Записи» под владельца салона, компактный шаблон главных

**Дата: 2026-10-20. Ветка: `cycle/046-zapis-landing-rework` (от `develop` @ `9d87a2dc`, документы — `bcda4cc5`).** Разделы §46.0–§46.16.
Требования — `SPEC.md` (цикл 46) и `BRIEF_CYCLE46_ZAPIS_LANDING.md`. Контракт — `API_CONTRACT_CYCLE46_ZAPIS_LANDING.md` (§46.20–§46.26),
машиночитаемо — `contracts/cycle46/openapi.yaml` (дельта HTTP API = ноль) и **`contracts/cycle46/zapis-landing.json`** (эталон
порядка блоков и дословных текстов «Записи»). Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` цикл не трогает: по конвенции проекта
документы цикла лежат в корне под суффиксом (`CURRENT_STATE.md` §10.5). Факты о коде сверены по файлам на `bcda4cc5` (код = `9d87a2dc`).
`CURRENT_STATE.md` описывает `69bafa0` (цикл 44); цикл 45 (карусель, компактная раскладка) в нём ещё не отражён, его код прочитан
напрямую.

**Коротко.** Это только фронтенд. Бэкенд, БД, API и миграции не меняются. В шаблоне главных (`frontend/src/components/landing/`)
появляется второй порядок секций — `order: 'business-first'`. Его выбирает только «Запись». «Заказы», «Дома» и «Бани» конфиг не
правят и остаются в прежнем порядке `catalog-first`. Компактная раскладка K1–K8 делается в том же шаблоне и сразу касается всех
четырёх главных. Константы `classes.ts`, которые использует `about.ezbook.ru`, не меняются. Тексты «Записи» окончательные (П5,
подтверждено заказчиком повторно при запуске цикла: «тексты не черновики, это оригиналы»). Они переносятся дословно из
`zapis-landing.json`, и тест сверяет конфиг с этим файлом.

---

## §46.0. Решения одним экраном

Решения заказчика: Р1–Р4 (бриф, 20.10.2026), П1–П5 приняты как есть. **Тексты §4 спеки — окончательные, не черновики.**

| # | Вопрос (SPEC §5) | Решение | Где |
|---|---|---|---|
| A46-1 | Как задать порядок секций данными (§5 п. 1) | Поле `order?: 'catalog-first' \| 'business-first'` в конфиге. Это закрытый список раскладок, а не массив секций. Обе раскладки описаны в одном месте — `ServiceLanding.tsx`. Без `order` раскладка прежняя, поэтому три сервиса не правятся | §46.3 |
| A46-2 | «Как начать» у «Записи» отдельно, у остальных внутри бизнес-секции (§5 п. 1) | Данные те же (`business.stepsPanel`). Где выводить, решает раскладка: в `business-first` — отдельная секция `#biz-steps-title` с `h2` после клиентов, в `catalog-first` — внутри «Для бизнеса», как раньше | §46.5 |
| A46-3 | Типы конфига (§5 п. 2) | `LandingConfig` = объединение `CatalogFirstLandingConfig \| BusinessFirstLandingConfig`. В `business-first` `hero.primaryAction` (теперь любой `LandingAction`, в том числе `auth-route`) и `hero.clientLink` обязательны. У `clients` запрещены `actions` и `stepsPanel` (`never`), `media` тоже запрещено. Для остальных сервисов всё как раньше | §46.2, API §46.21 |
| A46-4 | Заглушка скриншота (§5 п. 3) | Один плейсхолдер на страницу — в первом пустом слоте **в порядке вывода**: для `business-first` это бизнес, потом клиенты. Для `catalog-first` правило §38.3.6 не меняется | §46.5.4 |
| A46-5 | Регрессия на четырёх главных (§5 п. 4) | Каждый тест на классы, который ломается, переписывается на новый инвариант по списку §46.11.2. Слепо подгонять ожидания нельзя. Сохранность содержимого проверяют два новых теста K7: «всё из конфига есть на странице» для четырёх главных и «инвентарь разметки = эталон цикла 41» для шаблона | §46.11 |
| A46-6 | Две кнопки в ряд на 375 px (§5 п. 5) | Новые классы кнопок шаблона: на телефоне `px-4`, 14 px, стрелка скрыта, `min-h-[44px]`; от `sm` — прежние размеры. Ряд остаётся `flex-wrap`: на 375 px пара помещается (расчёт §46.6.2), уже 360 px аккуратно переносится. Горизонтальной прокрутки нет. Равные колонки отвергнуты: «Подключить магазин» в половину ширины не помещается | §46.6.2 |
| A46-7 | Тесты с текстами (§5 п. 6) | `HomePage.test.tsx` переписывается под новые тексты и якоря. Тексты «Заказов» не меняются, поэтому T27/T30 по текстам не трогаются; правятся только проверки классов (§46.11.2) | §46.11 |
| A46-8 | Источник текстов | `contracts/cycle46/zapis-landing.json` — единственный эталон. Конфиг обязан ему соответствовать, тест сверяет посимвольно (CY46-08). Сверка M46-01 у оркестратора идёт по тому же файлу | §46.8 |
| A46-9 | Витрина `about.ezbook.ru` использует `classes.ts` | Её константы (`BTN_PRIMARY`, `BTN_SECONDARY`, `CHECK_ITEM`, `H2`, `H3`, `EYEBROW`, `LEAD`, `CONTAINER`, `FOCUS_RING`) **не меняются**. Шаблон главных получает свои новые константы. Значения закрепляет тест CY46-14 | §46.6.1 |

---

## §46.1. Стек: без изменений

Стек тот же: React 18, TypeScript, Tailwind 3.4, Vite 5, vitest. Новых зависимостей, картинок и шрифтов нет. Нужный вариант
`max-md:` есть в Tailwind ≥ 3.2, в проекте `^3.4.13`. Хостинг, сборка, CI и деплой не меняются: цикл выкатывается обычным релизом,
задач у devops-engineer нет, кроме фиксации документов. Масштабирования это не касается: страница статическая, запросы к API те же,
что и раньше (§46.20 контракта).

---

## §46.2. Модель данных

**БД, сущности EF, миграции — без изменений.** «Модель» цикла — это конфиг шаблона главной (TS-типы, проверяет tsc):

```
LandingConfig = CatalogFirstLandingConfig            ← goods, (dom, bani через LandingPanelConfig) — order не пишется
              | BusinessFirstLandingConfig           ← только zapis — order: 'business-first'

Общее:          catalog, business (с stepsPanel), pricing?, faq
CatalogFirst:   hero: LandingHeroConfig, media?, clients: LandingClientsConfig        (actions и stepsPanel обязательны — как было)
BusinessFirst:  hero: LandingHeroConfig & { primaryAction; clientLink } (обязательны), media?: never,
                clients: LandingClientsShowcaseConfig (actions?: never, stepsPanel?: never)
LandingHeroConfig.primaryAction?: LandingAction  (было AnchorAction | RouteAction)
LandingHeroConfig.clientLink?: { lead: string; action: AnchorAction }
```

Связи и инварианты, которые проверяют тесты, а не tsc:
1. `hero.clientLink.action.href === '#' + catalog.id` (CY46-01) — ссылка из первого экрана ведёт на каталог.
2. `hero.primaryAction` глубоко равен `business.actions[0]` у «Записи» (CY46-04) — две кнопки «Подключить салон» ведут себя одинаково.
3. `hero.howTo.href === '#' + clients.titleId` — у всех четырёх, как и раньше.
4. Конфиг «Записи» = `zapis-landing.json` (CY46-08).

Точные типы — API_CONTRACT §46.21.

---

## §46.3. Порядок секций данными (A46-1)

### §46.3.1 Почему закрытый список, а не массив секций

Массив вида `sections: ['hero', 'business', …]` позволил бы собрать из конфига любой порядок, дубли и пропуски. Ловить их
пришлось бы рантайм-проверкой, и это противоречит NFR «переставить вёрстку из конфига нельзя, конфиг задаёт только данные и порядок».
Нужны ровно две истории: «для клиента» (`catalog-first`) и «для владельца» (`business-first`). Закрытый тип даёт обе, а tsc проверяет,
что у каждой раскладки есть свои обязательные данные (A46-3). Третья раскладка — это новое значение типа и новая ветка в
`ServiceLanding`. Это осознанная правка шаблона, а не строчка конфига.

### §46.3.2 `ServiceLanding.tsx` — единственное место порядка

`LandingSections` ветвится по `config.order ?? 'catalog-first'`:

```
catalog-first (как сейчас, без изменений порядка):
  LandingCatalogFrame(placement='top') → LandingMedia? → LandingClientsSection(steps внутри)
  → LandingBusinessSection(steps внутри) → LandingPricingSection? → FaqSection

business-first (новое, только «Запись»):
  LandingBusinessSection(steps='none') → LandingClientsSection(showcase) → LandingStepsSection(business.stepsPanel)
  → LandingPricingSection? → FaqSection → LandingCatalogFrame(placement='end')
```

Герой (`LandingHero` или `LandingPanelHero`) остаётся вне `LandingSections`, как сейчас. Ветку с шапкой-панелью допускают
только `catalog-first`-конфиги: `LandingPanelConfig` построен на `CatalogFirstLandingConfig`. Хук `useInitialHashScroll()` вызывается
как прежде: в `business-first` `#biz-title` стоит сразу под первым экраном и рендерится синхронно, поэтому переход
`visit.ezbook.ru/#biz-title` с `about.ezbook.ru` работает надёжнее, чем раньше (CY46-03).

Внутренние пропсы компонентов (не поля конфига, guard T38-09 их не касается):
- `LandingBusinessSection` — `steps: 'inside' | 'none'`;
- `LandingCatalogFrame` — `placement: 'top' | 'end'`: `top` — прежний `mt-8 scroll-mt-24`, `end` — `${SECTION} scroll-mt-24`;
- `LandingStepsPanel` — `headingLevel: 2 | 3`: 3 — `h3` + шаги `h4` (как сейчас), 2 — `h2` + шаги `h3`;
- `LandingClientsSection` принимает `LandingClientsConfig | LandingClientsShowcaseConfig`. Ряд кнопок и панель шагов выводятся,
  только если они есть в конфиге.

Новый компонент `LandingStepsSection.tsx`: `<section aria-labelledby={panel.titleId} className={STEPS_SECTION}>` и внутри
`<LandingStepsPanel panel headingLevel={2} />`. У панели в этом варианте нет собственного верхнего отступа: отступ даёт секция.

---

## §46.4. Первый экран «Записи» (US-46-01)

`LandingHero` (обычная шапка, не панель):
- основная кнопка рендерится через **`LandingActionLink`** (`primary`), а не через локальный `HeroLink`. Так `auth-route` работает
  в первом экране так же, как в «Для салона»: гость → `/register?returnTo=%2Fcabinet%2Fnew`, вошедший → `/cabinet/new` (CY46-04).
  `HeroLink` удаляется. У «Заказов» `primaryAction` нет — для них ничего не меняется;
- ряд «кнопка + ссылка»: `mt-6 flex flex-wrap items-center gap-x-5 gap-y-1`. Ссылка `howTo` получает `min-h-[44px]`, а было
  `36px` (NFR: цель нажатия не меньше 44 px). На 375 px ссылка «Как записываются клиенты» может уйти на вторую строку. Это допустимо:
  это текстовая ссылка, а не вторая кнопка (K2, K6);
- `clientLink` (новое): `<p className="mt-3 text-[14px] text-ink-soft">{lead} <a href className="inline-flex items-center min-h-[44px]
  font-semibold text-ink underline underline-offset-4 hover:no-underline ${FOCUS_RING}">{label}</a></p>`. Это строка, а не кнопка.
  40 символов на 14 px — около 300 px, то есть одна строка на 375 px.

Бюджет первого экрана на 375×812 (оценка; проверяет оркестратор): шапка сайта ~64, `pt-10` 40, надзаголовок ~36, `h1` 4 строки
~160, абзац ~6 строк ~160, кнопка ~68. До низа кнопки ≈ 530 px < 812, то есть заголовок, абзац и «Подключить салон» видны без
прокрутки.

---

## §46.5. Секции «Записи» в раскладке `business-first`

### §46.5.1 «Для салона» (`#biz-title`) — `LandingBusinessSection steps='none'`
Разметка прежняя, без панели шагов. 7 пунктов возможностей и скриншот `bookingsShot`. Ограничение высоты скриншота на телефоне —
§46.6.4.

### §46.5.2 «Так к вам записываются клиенты» (`#clients-title`) — `LandingClientsSection` с `LandingClientsShowcaseConfig`
Надзаголовок, `h2` (`tabIndex=-1`, `scroll-mt-24` — сюда ведёт ссылка из первого экрана), абзац. Ряда кнопок и панели нет. Дальше
карусель `bookingFlowCarousel` без изменений (цикл 45: автолистание, пауза, свайп, «уменьшить движение») и плотный список «Что ещё
может клиент» с примечанием в строку под списком.

### §46.5.3 «Как начать принимать записи» (`#biz-steps-title`) — `LandingStepsSection`
Отдельная секция с панелью: `h2` + 3 шага `h3` (K1). Тексты шагов — §46.8.

### §46.5.4 Заглушка скриншота (A46-4)
```ts
const slots = order === 'business-first' ? ['business', 'clients'] : ['clients', 'business']   // порядок вывода
const firstEmpty = slots.find((s) => !config[s].screenshot)                                      // один плейсхолдер на страницу
```
У «Записи» оба слота заполнены, и плейсхолдера нет. У «Домов» и «Бань» скриншотов нет: плейсхолдер стоит в секции клиентов, как
раньше (T38-02 без изменений). Новый кейс CY46-12 проверяет, что в `business-first` с двумя пустыми слотами плейсхолдер один и он
в секции `#biz-title`.

### §46.5.5 Каталог — последним (`LandingCatalogFrame placement='end'`)
Секция `#companies` получает ритм обычной секции (`SECTION`). Содержимое — §46.7.

---

## §46.6. Компактная раскладка K1–K8 (Р4) — для всех четырёх главных

### §46.6.1 Где меняется и что нельзя трогать
Меняются только файлы шаблона `src/components/landing/*`. Новые константы лежат в `classes.ts` (контракт §46.25). Константы,
которые импортирует `about/src`, остаются **байт в байт** (A46-9, тест CY46-14). Новые классы попадут и в CSS витрины: её Tailwind
сканирует `classes.ts`, это несколько сотен байт. Разметку витрины это не меняет.

### §46.6.2 Таблица правил

| Правило | Компонент | Было | Стало |
|---|---|---|---|
| **K1** шаги | `LandingStepsPanel` | `li`: кружок 46 px (`mb-5`) → `h4` → `p`; панель `px-6 py-10 sm:px-10 md:px-12 md:py-14`, заголовок `mb-8 md:mb-10`, `ol gap-10`, отступ сверху `mt-12 md:mt-16` | `li`: `<div class="flex items-center gap-3">` + кружок `STEP_ICON` (36 px, иконка 18) + заголовок шага `text-[19px] md:text-[21px] leading-snug` → `p mt-2 text-[15px] leading-[1.55]`. Панель `rounded-[28px] bg-cream-deep px-5 py-6 sm:px-8 sm:py-8 md:px-12 md:py-12`, заголовок `mb-5 md:mb-8`, `ol list-none grid gap-5 md:grid-cols-3 md:gap-8`, отступ внутри секции `mt-8 md:mt-12` (в `LandingStepsSection` — 0) |
| **K2** кнопки | `LandingActionLink`, ряды в `LandingClientsSection`, `LandingBusinessSection` | `BTN_PRIMARY`/`BTN_SECONDARY` (`px-7 py-3.5`, 15 px), ряд `mt-7 flex flex-wrap gap-3` | `BTN_ACTION_PRIMARY`/`BTN_ACTION_SECONDARY` (до `sm`: `px-4 py-2.5`, 14 px, `whitespace-nowrap`, `min-h-[44px]`; от `sm` — как было), ряд `ACTIONS_ROW`. Стрелка `hidden sm:block` |
| **K3** короткие списки | `LandingClientsSection` (список «Что ещё…», «Что важно знать», «Как следить за заказом») | вариант с каруселью: карточка с рамкой на каждый пункт, `sm:grid-cols-2 lg:grid-cols-4`; без карусели — `space-y-4`; примечание `mt-4`/`mt-6` | `ul` — `DENSE_LIST`, `li` — `CHECK_ITEM` без рамки и фона. Вариант с каруселью: `sm:grid-cols-2`. Вариант со скриншотом сбоку: `sm:grid-cols-2 md:grid-cols-1`, потому что от `md` список уже стоит колонкой рядом со скриншотом. Заголовок списка `mb-4`, примечание `<p class="mt-3 text-[13px] text-muted">` сразу под списком |
| **K4** преимущества | `LandingBusinessSection` | `ul space-y-4 md:pt-2`, верхняя сетка `gap-10 md:gap-16` | `ul ${DENSE_LIST} sm:grid-cols-2 md:grid-cols-1 md:pt-2`, верхняя сетка `gap-6 md:gap-16`. Две колонки на 640–767 px; от `md` раскладка «текст \| список» уже двухколоночная |
| **K5** ритм | `classes.SECTION`, `LandingPanelHero`, медиа-блоки секций | `mt-20 md:mt-28 … pt-12` (128 / 160 px); карусель и списки `mt-12 md:mt-16`; низ шапки-панели `pb-20 md:pb-28` | `SECTION = 'mt-10 md:mt-20 border-t border-line pt-6 md:pt-12'` (64 / 128 px); карусель, список, скриншот, панель — `mt-8 md:mt-12`; надзаголовок секции `mb-3`; низ шапки-панели `pb-12 md:pb-24`; FAQ — список `mt-5 md:mt-8`, вопрос `py-4 md:py-5`; тарифы — шапка `mb-6 md:mb-10`, сетка `gap-4 md:gap-6` |
| **K6** общее | все | — | Нет строки, где стоит только иконка (K1), одна кнопка при наличии второй (K2) или одинокое примечание (K3). Каталог «Записи» — §46.7 |
| **K7** ничего не теряется | все | — | Тексты, ссылки, кнопки и скриншоты остаются в DOM, меняются только классы и одна обёртка в шаге. Проверка — CY46-09, CY46-10 |
| **K8** без горизонтальной прокрутки | все | поле поиска «Записи» `w-[280px]`, сетка каталога `minmax(320px, 1fr)` | `w-full sm:w-[280px]`, `minmax(min(320px, 100%), 1fr)`; кнопки `whitespace-nowrap` внутри `flex-wrap` |

Карусель (`LandingScreenshotCarousel.tsx`) **не меняется**, меняется только внешний отступ, который передаёт секция. Её кнопки
36 px — существующий долг (R46-8), спека требует оставить карусель как есть.

**Расчёт K2 для худшей пары** («Подключить магазин» + «Войти в кабинет», Inter 14 px semibold, средняя ширина кириллической буквы
≈ 0,56 em ≈ 7,8 px, пробел ≈ 4 px): 137 + 32 = 169 px и 110 + 32 + 2 = 144 px, с промежутком 8 px — **321 px ≤ 343 px** (ширина
контейнера на 375 px). Запас 22 px. Если при M46-02 пара всё же переносится на 375 px, вторая ступень — `px-3.5` на телефоне.
Шрифт и подписи не трогаем.

### §46.6.3 Высота «Записи» — оценка против цели US-46-06 (≤ 3600 px для блоков 2–4 на 375 px)

| Блок | Оценка | Из чего |
|---|---|---|
| 2 «Для салона» | ≈ 1420 | ритм 64 + шапка ~370 + кнопки ~70 + 7 пунктов ~400 + скриншот с ограничением (§46.6.4) ~520 |
| 3 «Клиенты» | ≈ 1300 | ритм 64 + шапка ~230 + карусель ~630 (без изменений) + список и примечание ~330 |
| 4 «Как начать» | ≈ 530 | ритм 40 + панель с отступами ~490 |
| **Итого** | **≈ 3250** | цель ≤ 3600 (было 4829) |

Без ограничения высоты скриншота в блоке 2 (`bookingsPhone` 390×1007 → на 320 px ширины ~826 px) оценка выходит ≈ 3590, то есть
на самой границе. Поэтому §46.6.4 обязателен. Если M46-02 покажет больше 3600, запасные рычаги в порядке применения: (1)
`max-md:!h-[min(52vh,460px)]` у скриншота блока 2; (2) `gap-y-2` в плотных списках; (3) `ol gap-4` в панели. Тексты и состав
блоков не трогаем.

### §46.6.4 Скриншот бизнес-секции на телефоне
`LandingBusinessSection` передаёт в `LandingScreenshotFigure`
`imgClassName="max-w-[320px] mx-auto md:max-w-none max-md:!w-auto max-md:!h-[min(60vh,540px)]"`. Высоту ограничиваем так же, как
у кадров карусели (цикл 45). Важно: базовый `h-auto` в `LandingScreenshotFigure` перебивается **только** с `!` (урок цикла 45, fix
`9d87a2dc`). От `md` идёт десктопный кадр через `<source media="(min-width: 768px)">` на всю ширину, как раньше. Это касается и
«Заказов» (`boardShot`), у них страница становится только ниже. Базовые классы `LandingScreenshotFigure` не меняются (QA30-08 ждёт
`h-auto`).

---

## §46.7. Каталог «Записи» — `ZapisCompanyCatalog.tsx` (US-46-05)

Запросы, поиск, пагинация, плашки и `CompanyCard` не меняются. Меняется следующее (контракт §46.24):
- заголовок «Салоны на EZBOOK», подзаголовок «Выберите салон и запишитесь онлайн» (без «Проверенные специалисты»);
- шапка: `flex flex-col gap-3 sm:flex-row sm:items-baseline sm:justify-between mb-6 md:mb-10`; обёртка поиска `relative w-full
  sm:w-auto`, поле `w-full sm:w-[280px]`. Иконка поиска остаётся внутри поля и получает `aria-hidden`;
- загрузка: 2 заглушки `h-32`, а не 6 по `h-40`. Высота заглушки не больше двух карточек;
- ошибка: `<div role="alert" className="flex items-center gap-3 py-4 text-muted">` + `Icon alert-circle size={20} aria-hidden` +
  `<p className="text-[15px]">` с прежним текстом;
- пусто: та же строка с иконкой `store` и текстом «Салонов пока нет»;
- сетка карточек: `repeat(auto-fill, minmax(min(320px, 100%), 1fr))`. Это защита от горизонтальной прокрутки на ширинах < 352 px.
  Инлайн-`style` уже есть в этом файле. Файл — страница сервиса, а не шаблон, поэтому guard T38-09 его не касается.

`catalog.ariaLabel` в конфиге — «Салоны на EZBOOK».

---

## §46.8. Тексты «Записи» (US-46-07) — окончательные, эталон в JSON

**Источник истины — `contracts/cycle46/zapis-landing.json`.** Строки в нём — это §4 спеки, перенесённые дословно. Тексты утверждены
заказчиком 20.10.2026 (П5) и повторно подтверждены при запуске цикла как оригиналы, не черновики. Разработчик **не перефразирует**
ни одной строки. Правка текста возможна только как правка JSON по решению заказчика. Комментарии «черновики на вычитку» в шапках
`zapisLanding.ts` и `zapisFaq.ts` заменяются на «утверждены заказчиком 20.10.2026, эталон — contracts/cycle46/zapis-landing.json».

При переносе из Markdown спеки в строки конфига применены ровно два типографских правила. Слова они не меняют:
- **T1. Вложенные кавычки.** В §4.6 ответы 3 и 4 в спеке сами стоят в «ёлочках», поэтому внутренние кавычки там — „лапки“
  («…проверка „я не робот“…»). На странице внешних кавычек нет, и внутренние становятся обычными «ёлочками», как в остальных
  ответах FAQ и в списке «Перенести… в «Моих визитах»…»: «проверка «я не робот»», «в разделе «Мои визиты»».
- **T2. Шаги «Как начать».** Запись спеки «**Заголовок** — текст» разделена на два поля шага (`title`, `text`). Текст — отдельный
  абзац под заголовком, поэтому начинается с заглавной буквы: «Название, адрес, услуги с ценами и длительностью.». Остальные
  два шага в спеке уже начинаются с глагола, их регистр меняется так же («Пригласите…», «Отправьте…»).

Если заказчик хочет буквальный вид спеки (с „лапками“ или строчной буквой), правка — одна строка в JSON и конфиге. Тесты сверяют
конфиг с JSON, а не с Markdown.

Правила фактов и слов (CY46-06, US-46-07, П4):
- в текстах главной «Записи» (конфиг, FAQ, каталог) нет `/проверенн/i`, `/лучш/i`, `/№ ?1/`, `/самый/i`. Слово «проверка» в FAQ
  («проверка «я не робот»») — это не оценка, поэтому шаблон — именно `проверенн`, а не `провер`;
- в первом экране (`hero.*`) нет `/бесплатн/i` и `/проверен/i`;
- ни одна строка-предложение (заголовок, абзац, пункт списка, вопрос, ответ, подпись кнопки) не повторяется дословно в двух блоках.
  Исключения: «Подключить салон» (первый экран и «Для салона», US-46-02) и «Салоны на EZBOOK» (ссылка первого экрана и заголовок
  каталога, на который она ведёт; оба текста заданы спекой §4.1 и §4.7). Это CY46-13.

Новых фактических утверждений по сравнению с прежней главной, FAQ и `CURRENT_STATE.md` §5.1 нет. Все пункты §4 сверены по спеке.
«Фото» в «Базе клиентов с заметками и фото» спека относит к существующей функции. Если QA не найдёт её в коде, это повод для
вопроса заказчику, а не для молчаливой правки текста (R46-6).

---

## §46.9. FAQ «Записи» (US-46-08)

`zapisFaq.ts`: 8 вопросов в порядке JSON — сначала вопросы владельца (1–2), затем клиента (3–8). Тексты 1, 2, 5–8 прежние байт в
байт, 3 и 4 новые (§4.6, правило T1). Ссылка «Тарифы и цены» → `/pricing` — у вопроса 1. `FaqSection` не меняется, кроме отступов
K5. `zapisFaq.test.ts` (6–8 вопросов, ссылка на `/pricing`) остаётся и дополняется CY46-05.

---

## §46.10. Структура проекта — что меняется

```
frontend/src/components/landing/
  types.ts                       ИЗМЕНЕНИЕ  LandingOrder, LandingHeroClientLink, LandingClientsShowcaseConfig,
                                            Catalog/BusinessFirstLandingConfig, LandingConfig = union (API §46.21)
  classes.ts                     ИЗМЕНЕНИЕ  SECTION; новые STEPS_SECTION, ACTIONS_ROW, BTN_ACTION_*, DENSE_LIST, STEP_ICON (API §46.25)
  ServiceLanding.tsx             ИЗМЕНЕНИЕ  две раскладки, слоты плейсхолдера по порядку вывода
  LandingHero.tsx                ИЗМЕНЕНИЕ  LandingActionLink вместо HeroLink, clientLink, min-h-[44px]
  LandingActionLink.tsx          ИЗМЕНЕНИЕ  BTN_ACTION_*, стрелка hidden sm:block
  LandingClientsSection.tsx      ИЗМЕНЕНИЕ  showcase-вариант, ACTIONS_ROW, плотный список, отступы
  LandingBusinessSection.tsx     ИЗМЕНЕНИЕ  steps: 'inside'|'none', ACTIONS_ROW, плотные преимущества, высота скриншота на телефоне
  LandingStepsPanel.tsx          ИЗМЕНЕНИЕ  K1, headingLevel
  LandingStepsSection.tsx        НОВОЕ      отдельная секция «Как начать»
  LandingCatalogFrame.tsx        ИЗМЕНЕНИЕ  placement: 'top'|'end'
  LandingPanelHero.tsx           ИЗМЕНЕНИЕ  только pb (K5)
  FaqSection.tsx, LandingPricingSection.tsx   ИЗМЕНЕНИЕ  только отступы (K5)
  testConfig.ts                  ИЗМЕНЕНИЕ  makeConfig → CatalogFirstLandingConfig; новое makeBusinessFirstConfig()
  landingReference.ts            ИЗМЕНЕНИЕ  CYCLE41_DEFAULT_BRANCH_HTML (прежний эталон, переименован) + DEFAULT_BRANCH_HTML (новый снимок)
  ServiceLanding.test.tsx        ИЗМЕНЕНИЕ  + CY46-01t, CY46-11, CY46-12
  LandingPanelHero.test.tsx      ИЗМЕНЕНИЕ  T41-02 на новый снимок + CY46-10
  classes.test.ts                НОВОЕ      CY46-14
  LandingScreenshotCarousel.tsx, LandingScreenshot.tsx, CatalogCard.tsx, useInitialHashScroll.ts, LandingMedia.tsx   БЕЗ ИЗМЕНЕНИЙ
frontend/src/pages/home/
  zapisLanding.ts                ИЗМЕНЕНИЕ  order: 'business-first', тексты = JSON
  zapisFaq.ts                    ИЗМЕНЕНИЕ  порядок и тексты = JSON
  ZapisCompanyCatalog.tsx        ИЗМЕНЕНИЕ  §46.7
  zapisLanding.contract.test.tsx НОВОЕ      CY46-06, CY46-08, CY46-13
  zapisFaq.test.ts               ИЗМЕНЕНИЕ  + CY46-05
frontend/src/pages/HomePage.test.tsx         ИЗМЕНЕНИЕ  T38-10 под новые тексты; + CY46-01, CY46-03, CY46-04, состояния каталога
frontend/src/pages/home/zapisLanding.completeness.test.tsx        НОВОЕ  CY46-09 «Запись»
frontend/goods/src/landing/goodsLanding.completeness.test.tsx     НОВОЕ  CY46-09 «Заказы»
frontend/dom/src/landing/stayLanding.completeness.test.tsx        НОВОЕ  CY46-09 «Дома»
frontend/bani/src/landing/baniLanding.completeness.test.tsx       НОВОЕ  CY46-09 «Бани»
  (общий чистый помощник collectConfigStrings(config) — в src/components/landing/testConfig.ts)
frontend/goods/src/landing/goodsLanding.business.qa.test.tsx   ИЗМЕНЕНИЕ  QA27-04 (§46.11.2)
frontend/goods/src/landing/goodsLanding.qa.test.tsx            ИЗМЕНЕНИЕ  QA30-05 (§46.11.2)
frontend/goods/src/landing/goodsLanding.ts, frontend/dom/src/landing/stayLanding.ts, frontend/bani/src/landing/baniLanding.ts   БЕЗ ИЗМЕНЕНИЙ
frontend/about/**                                                 БЕЗ ИЗМЕНЕНИЙ
docs/client.md                   ИЗМЕНЕНИЕ  §1 «Найти компанию» (DOC-46-1)
contracts/cycle46/openapi.yaml, contracts/cycle46/zapis-landing.json   НОВОЕ (этот документ)
ServiceBooking.*/**, deploy/**, .github/**                        БЕЗ ИЗМЕНЕНИЙ
```

CY46-09 разнесён на четыре файла рядом с конфигами. ESLint запрещает `src/` импортировать `@goods`/`@dom`/`@bani` (✔
`eslint.config.js`, блоки `no-restricted-imports`), а приложениям разрешено импортировать общий `@/components/landing/*`. Общая
часть — чистая функция `collectConfigStrings(config): { text: string[]; alt: string[]; ariaLabel: string[] }` в `testConfig.ts`.
Этот файл исключён из glob guard T38-09 и в сборку не попадает. Рендер (`ServiceLanding` + `MemoryRouter` + `QueryClientProvider`,
мок тарифов) каждый файл делает сам. Новые файлы попадают в области `contracts/cycle36/test-areas.json` по существующим префиксам
каталогов. Guard `testAreas` проверить прогоном.

---

## §46.11. Тесты

### §46.11.1 Новые автотесты (CY46-*)

| Кейс | Файл | Что проверяет |
|---|---|---|
| CY46-01 | `HomePage.test.tsx` (+ шаблонный CY46-01t в `ServiceLanding.test.tsx` на `makeBusinessFirstConfig`) | порядок `h1` → `#biz-title` → `#clients-title` → `#biz-steps-title` → `#pricing-title` (при моке тарифов) → `#faq-title` → `#companies`; все четыре `id` — `H2`; `#companies` — последняя секция; `clientLink` ведёт на `#companies`; «Как записываются клиенты» → `#clients-title`; в секции клиентов нет ссылок и `#clients-steps-title`; в бизнес-секции нет `ol` |
| CY46-02 | существующие: `dom/src/pages/CatalogPage.test.tsx`, `goods/src/pages/CatalogHomePage.test.tsx`, тесты порядка bani, `ServiceLanding.test.tsx` T38-01, `LandingPanelHero.test.tsx` T41-01 | порядок трёх сервисов прежний. **Ожидания порядка не правятся** |
| CY46-03 | `about/src/targets.guard.test.ts` (существующий) + `HomePage.test.tsx` | `titleId: 'biz-title'` у всех четырёх; на `/` «Записи» с `location.hash = '#biz-title'` вызывается `scrollIntoView` у `#biz-title` (мок, как в `useInitialHashScroll.test.tsx`) |
| CY46-04 | `HomePage.test.tsx`, `CreateCompanyRoute.test.tsx` | «Подключить салон» в первом экране: гость → `/register?returnTo=%2Fcabinet%2Fnew`, вошедший → `/cabinet/new`; `zapisLanding.hero.primaryAction` глубоко равен `business.actions[0]` |
| CY46-05 | `zapisFaq.test.ts` | 8 вопросов; первые два — `JSON.faq[0..1].question`; ссылка `/pricing` у первого |
| CY46-06 | `zapisLanding.contract.test.tsx` | запрещённые шаблоны JSON `rules` по всем строкам конфига, FAQ и исходнику `ZapisCompanyCatalog.tsx` (`?raw`); `forbiddenInHero` по `hero` |
| CY46-07 | `landing.guard.test.ts` (существующий) | T38-09 зелёный: в `types.ts` нет `className\|style\|as`, шаблон не импортирует `pages`/goods, нет `dangerouslySetInnerHTML`. Новый `LandingStepsSection.tsx` попадает в glob автоматически |
| CY46-08 | `zapisLanding.contract.test.tsx` | `zapisLanding` и `zapisFaq` **глубоко равны** соответствующим секциям JSON (кроме ссылок на объекты скриншотов: для них сверяются `caption`/`label`). Отрендеренная `HomePage` содержит каждую строку JSON (ответы FAQ — в скрытых узлах, `textContent` их видит). `ZapisCompanyCatalog` в состояниях «загрузка», «ошибка», «пусто», «список» показывает тексты `catalog` |
| CY46-09 | `*Landing.completeness.test.tsx` ×4 (§46.10) | для четырёх конфигов каждая строка конфига есть в DOM (`textContent`), кроме служебных ключей (`titleId`, `id`, `href`, `to`, `guestTo`, `authedTo`, `icon`, `src`, `srcSet`, `media`, `kind`, `layout`, `backdrop`, `order`, `width`, `height`) и атрибутов: `alt` — у `img`, `ariaLabel` — у секции каталога, `label` карусели — у `region`. Тарифы мокаются. Это K7: компактная раскладка ничего не потеряла |
| CY46-10 | `LandingPanelHero.test.tsx` | инвентарь разметки `ServiceLanding(makeConfig())` равен инвентарю **прежнего** эталона `CYCLE41_DEFAULT_BRANCH_HTML` (разбирается `DOMParser`): последовательность текстовых узлов, теги и `id` заголовков, `href` ссылок, `aria-*`, число `svg`. Классы и обёртки `div` не сравниваются |
| CY46-11 | `ServiceLanding.test.tsx` (`@ts-expect-error`) | инварианты tsc из API §46.21 пп. 1–5 |
| CY46-12 | `ServiceLanding.test.tsx` | `business-first` без скриншотов → ровно один плейсхолдер, в секции `#biz-title`; со скриншотом только у бизнеса → плейсхолдер в секции клиентов |
| CY46-13 | `zapisLanding.contract.test.tsx` | нет повторов строк между блоками, кроме `rules.allowedRepeats` |
| CY46-14 | `src/components/landing/classes.test.ts` | значения `BTN_PRIMARY`, `BTN_SECONDARY`, `CHECK_ITEM`, `H2`, `H3`, `EYEBROW`, `LEAD`, `CONTAINER`, `FOCUS_RING` равны литералам на `9d87a2dc`: витрина `about` не задета |
| CY46-K | `ServiceLanding.test.tsx` | инварианты раскладки K1–K4 на классах: иконка шага и заголовок шага — общий родитель с `flex` и `items-center`; у `ol` нет `gap-10`; у пунктов коротких списков нет `border`; ряд кнопок — `flex-wrap`, у обеих кнопок `min-h-[44px]` и `whitespace-nowrap`; `SECTION` не содержит `mt-20` без префикса |

### §46.11.2 Существующие тесты, которые меняются осознанно (A46-5)

| Тест | Почему ломается | Новое ожидание (а не «подгонка») |
|---|---|---|
| `LandingPanelHero.test.tsx` T41-02 «innerHTML равен эталону цикла 41» | эталон — полный снимок классов | эталон переименован в `CYCLE41_DEFAULT_BRANCH_HTML`. Новый `DEFAULT_BRANCH_HTML` снимается после правок одним коммитом с этой таблицей в описании. Диф эталона на ревью должен состоять **только** из классов §46.6.2 и обёртки шага. Сохранность содержимого доказывает CY46-10. Проверка класса `main` в T41-02 не меняется (класс `main` не меняется) |
| `goodsLanding.business.qa.test.tsx` QA27-04 | `ol gap-10`; кружок искали как `li.querySelector('div')` с `w-[46px] h-[46px]`; `h4 text-[21px]` | `ol` содержит `list-none`, `md:grid-cols-3` и **не** содержит `gap-10`. Кружок — `svg.parentElement` с `rounded-full`, `bg-cream`, `border-line-strong`, `w-9`, `h-9`. `circle.parentElement === h4.parentElement`, у родителя `flex` и `items-center` (K1). `h4` — `font-serif`, `md:text-[21px]`. Проверки `svg width=20` → **18** и `stroke-width 1.6` (не меняется) |
| `goodsLanding.qa.test.tsx` QA30-05 | то же: `gap-10`, `text-[21px]`, `svg width 20` | то же, что QA27-04. Панель `bg-cream-deep` и `rounded-[28px]` — без изменений |
| `HomePage.test.tsx` T38-10 | «Найти специалиста», «Как записаться», «Что ещё можно — сетка карточек» | ссылки «Салоны на EZBOOK» → `#companies` и «Как записываются клиенты» → `#clients-title`; «напоминание накануне визита» остаётся (теперь в списке); 8 вопросов FAQ; карусель — тот же `region` «Как проходит запись»; список `ul[aria-labelledby="clients-list-title"]` — `grid`, 5 пунктов, у `li` нет `border` |
| `ServiceLanding.test.tsx` T38-01…05 | не ломаются (`makeConfig` = `catalog-first`) | без изменений; только тип `makeConfig` |

**Не меняются и обязаны остаться зелёными без правки ожиданий:** `goodsLanding.clients.test.tsx` (T30-01…12: `flex-wrap` у ряда
есть, `svg` у основной кнопки в DOM, порядок заголовков `H2/H3/H4×3/H3`), `goodsLanding.business.qa.test.tsx` QA27-01…03, 05…08,
`goodsLanding.business.screenshot.test.tsx`, `goodsLanding.qa.test.tsx` QA30-02, 06…10, `LandingScreenshotCarousel.test.tsx`,
`useInitialHashScroll.test.tsx`, `FaqSection.test.tsx`, `LandingPricingSection.test.tsx`, `LandingActionLink.test.tsx`
(проверить: если он сверяет литерал `BTN_PRIMARY`, ожидание меняется на `BTN_ACTION_PRIMARY` с той же логикой «основная/вторичная»),
`shots.test.ts`, `about/src/**` (все), тесты порядка `dom`/`bani`.

### §46.11.3 Ручные проверки (оркестратор; агенты в браузере не проверяют)

- **M46-01** — тексты на живой странице = `zapis-landing.json` (сверка, а не вычитка).
- **M46-02** — 375×812 и 1280 для «Записи», «Заказов», «Домов», «Бань»: K1–K8, замер высот против брифа (блоки 2–4 «Записи» ≤ 3600,
  страница «Записи» без каталога −25 %, у трёх остальных высота не выросла, панели шагов ниже), пара кнопок в один ряд на 375,
  нет строк с одинокой иконкой, нет горизонтальной прокрутки. Отдельно: скриншот «Для салона» на 375 ≈ 487 px по высоте (§46.6.4).
- **M46-03** — каталог «Записи» на 375 в трёх состояниях (загрузка, ошибка, пусто). Чтобы получить ошибку, достаточно остановить
  API, пустой список — поиск по несуществующему названию.
- **M46-04** (добавлено) — `about.ezbook.ru` на 375 и 1280 выглядит как до цикла (A46-9).

---

## §46.12. Задачи и параллельность

Бэкенда нет: **у backend-developer задач нет.** У devops-engineer — только фиксация документов. Работа фронтенда делится по
владению файлами, чтобы два исполнителя не правили одни и те же файлы.

| Задача | Приоритет | Содержание | Владеет файлами | Зависит от |
|---|---|---|---|---|
| **FE-46-0** | P0, первой | `types.ts` по API §46.21 и `testConfig.ts` (`makeConfig` → `CatalogFirstLandingConfig`, `makeBusinessFirstConfig`). Один небольшой коммит, после него tsc зелёный на прежней разметке. Конфиг «Записи» пока остаётся `catalog-first` | `types.ts`, `testConfig.ts` | — |
| **FE-46-1** | P0 | Шаблон, раскладка `business-first`: `ServiceLanding` (две ветки, слоты плейсхолдера), `LandingHero` (`LandingActionLink`, `clientLink`), `LandingStepsSection`, пропсы `steps`/`placement`/`headingLevel`, showcase-вариант `LandingClientsSection`. Тесты CY46-01t, CY46-11, CY46-12 | `src/components/landing/*` кроме `types.ts`, `testConfig.ts` | FE-46-0 |
| **FE-46-2** | P0 | Компактная раскладка K1–K8 в тех же файлах шаблона: `classes.ts`, кнопки, шаги, списки, ритм, скриншот бизнес-секции. Пересъёмка `DEFAULT_BRANCH_HTML`, CY46-10, CY46-14, CY46-K, правки QA27-04 и QA30-05 | `src/components/landing/*`, `landingReference.ts`, два goods-теста | FE-46-0. **Тот же исполнитель, что FE-46-1, после него** (общие файлы) |
| **FE-46-3** | P0 | Содержимое «Записи»: `zapisLanding.ts` (`order: 'business-first'`, тексты = JSON), `zapisFaq.ts`, `ZapisCompanyCatalog.tsx` (§46.7), шапки-комментарии (§46.8). Тесты: `zapisLanding.contract.test.tsx` (CY46-06, 08, 13), `zapisFaq.test.ts` (CY46-05), `HomePage.test.tsx` (T38-10, CY46-01, 03, 04, каталог) | `src/pages/home/*`, `src/pages/HomePage.test.tsx`, `CreateCompanyRoute.test.tsx` | FE-46-0 (типы). **Параллельно с FE-46-1/2**: конфиг и тесты пишутся по контракту §46.21–§46.24; тесты порядка в `HomePage.test.tsx` зеленеют после FE-46-1 |
| **FE-46-4** | P1 | `collectConfigStrings` в `testConfig.ts` (согласовать с владельцем FE-46-0/1) + четыре `*Landing.completeness.test.tsx` (CY46-09); прогон CY46-02 | четыре новых файла | пишется сразу; зелёный после FE-46-2 и FE-46-3 |
| **DOC-46-1** | P1 | `docs/client.md` §1: «Откройте главную страницу. В первом экране нажмите «Салоны на EZBOOK» — список салонов внизу страницы, над ним поле поиска…». `CHANGELOG.md` — раздел цикла 46, `CURRENT_STATE.md` — запись о циклах 45 и 46 (закрывающие документы конвейера) | `docs/`, корневые журналы | после FE-46-3 |
| **QA-46-1** | P0 | Прогон §46.26 контракта, полный `npm run test:run`, `npm run lint`, все `tsc`. Кейсы CY46-* в `TEST_CATALOG.md` (раздел «Цикл 46») | — | после FE-46-1…4 |
| **M46-01…04** | P0 | Оркестратор, в браузере | — | после QA-46-1 |

```
FE-46-0 ──┬──> FE-46-1 ──> FE-46-2 ──┐
          └──> FE-46-3 ──────────────┼──> FE-46-4 (зелёный) ──> QA-46-1 ──> M46-01…04 ──> DOC-46-1, мердж
                                     ┘
```

При одном исполнителе порядок такой: FE-46-0 → FE-46-1 → FE-46-3 → FE-46-2 → FE-46-4. Так тексты и порядок «Записи» проверяются
до правки классов, а снимок T41-02 пересъёмывается один раз.

---

## §46.13. Безопасность, доступность, производительность

- **Безопасность.** Новых входов, запросов, хранилищ и секретов нет. HTML не интерпретируется: в шаблоне нет
  `dangerouslySetInnerHTML` (T38-09). Ссылки в конфиге — якоря и внутренние маршруты. Юрист не нужен (SPEC §5 п. 7):
  оценочные формулировки закрыты CY46-06.
- **Доступность.** Один `h1`. Уровни заголовков без пропусков: в отдельной секции шагов `h2 → h3`, в панели внутри секции
  `h3 → h4`. У всех секций `aria-labelledby`, у каталога — `aria-label`. Иконки `aria-hidden`. Цели нажатия: кнопки и ссылки
  первого экрана — `min-h-[44px]`. Ошибка каталога — `role="alert"`. Карусель и FAQ не меняются. Видимый фокус: `FOCUS_RING` у
  новой ссылки `clientLink`. У кнопок фокус-стиль прежний (браузерный), как у `BTN_PRIMARY` сейчас.
- **Производительность.** Новых картинок, шрифтов и JS-зависимостей нет. Каталог внизу страницы грузится как раньше, при
  монтировании. Lazy-загрузка по видимости — кандидат на будущее (R46-9). Скриншоты по-прежнему `loading="lazy"`.

---

## §46.14. Риски

| # | Риск | Решение |
|---|---|---|
| R46-1 | Пара кнопок не помещается в ряд на 375 px | Расчёт §46.6.2 (запас 22 px); вторая ступень — `px-3.5`; `flex-wrap` не даёт горизонтальной прокрутки ни при какой ширине |
| R46-2 | Высота блоков 2–4 > 3600 px | Обязательное ограничение высоты скриншота (§46.6.4) и рычаги §46.6.3. Агенты высоту не измеряют (jsdom), измеряет оркестратор (M46-02) |
| R46-3 | Перебитый класс высоты скриншота (урок цикла 45: `h-auto` бьёт `h-[…]`) | Только `!`-классы; M46-02 отдельно меряет скриншот «Для салона» |
| R46-4 | Регрессия витрины `about.ezbook.ru` через `classes.ts` | Константы витрины не меняются, их закрепляет CY46-14; M46-04 |
| R46-5 | Подгонка тестов вместо осознанной правки | Закрытый список правок §46.11.2 с новыми инвариантами; содержимое страхуют CY46-09 и CY46-10. На ревью диф `landingReference.ts` сверяется с таблицей §46.6.2 |
| R46-6 | Текст обещает несуществующую функцию («… с заметками и фото», «Отзывы клиентов после визита», «Перенос и отмена… по вашим правилам») | Тексты утверждены заказчиком, их не правим. QA при прогоне сверяет три утверждения с `CURRENT_STATE.md` §5.1 и кодом. Если что-то не подтвердится — сообщить оркестратору для решения заказчика, а не переписывать |
| R46-7 | Типографские правила T1/T2 сочтут отступлением от «дословно» | Слова не меняются. Правила записаны явно (§46.8), эталон — JSON; откат — одна строка |
| R46-8 | Кнопки карусели 36 px < 44 px (NFR) | Существующий долг цикла 45. Спека требует оставить карусель без изменений. Записать в долг цикла (CURRENT_STATE §9) |
| R46-9 | Каталог внизу всё равно грузится сразу (лишний запрос для владельца) | Поведение как раньше, нагрузка та же. Ленивая загрузка (IntersectionObserver) — кандидат на следующий цикл, вместе с кнопкой «Посмотреть демо» |
| R46-10 | Плейсхолдер «Здесь будут скриншоты» у «Домов»/«Бань» занимает ~490 px на телефоне | Это существующее поведение, K7 запрещает его убирать. Высота страниц не растёт. Кандидат на отдельное решение заказчика |
| R46-11 | ESLint-границы запрещают `src/` импортировать конфиги goods/dom/bani | Учтено: CY46-09 — четыре файла рядом с конфигами и чистый помощник в `testConfig.ts` (§46.10) |
| R46-12 | Типовое объединение `LandingConfig` ломает места, где конфиг собирают spread'ом (`makeConfig(over)`) | `makeConfig` типизируется как `CatalogFirstLandingConfig`, для второй раскладки — отдельная фабрика. Других потребителей типа нет (✔ grep) |

---

## §46.15. Отклонения от SPEC и уточнения

1. **T1/T2** (§46.8): внутренние кавычки «ёлочками» и заглавная буква текста шага. Слова §4 не меняются.
2. **Повторы (US-46-02):** кроме «Подключить салон», допускается «Салоны на EZBOOK» — ссылка первого экрана и заголовок каталога,
   на который она ведёт. Обе строки заданы спекой (§4.1, §4.7).
3. **K4 «от планшета две колонки»:** на 640–767 px список преимуществ идёт в две колонки. От 768 px двухколоночна сама раскладка
   «текст | список». Две колонки внутри правой половины на 768 px сделали бы строки по 2–3 слова. То же для короткого списка рядом
   со скриншотом (K3).
4. **Ссылка «Как записываются клиенты» в первом экране** на 375 px может стоять второй строкой под кнопкой. K2 касается пары
   кнопок, а это текстовая ссылка.
5. **Скриншот «Для салона» на телефоне** ограничен по высоте (§46.6.4). Кадр не обрезается, а уменьшается целиком. Ради цели по
   высоте это касается и «Заказов».
6. **Шаги «Как начать» у «Записи»** — отдельная секция с `h2` (в спеке это «блок 4» с якорем `#biz-steps-title`). Визуально это
   та же панель.

---

## §46.16. Документация и закрытие цикла

- `docs/client.md` §1 — DOC-46-1. Другие пользовательские документы главную «Записи» не описывают (✔ grep «Компании и салоны»).
- `CHANGELOG.md`, `CURRENT_STATE.md` (§5.5 строки циклов 45 и 46, §9 — долг R46-8…R46-10), `TEST_CATALOG.md` (раздел «Цикл 46») —
  закрывающие документы. По опыту прошлых циклов конвейер может их пропустить, проверять руками.
- `contracts/cycle46/openapi.yaml` в список `redocly lint` в `ci.yml` **не добавляется**: операций нет, проверять нечего. Его
  линтует QA локально (§46.26 контракта). Правку `ci.yml` ради пустой дельты не делаем: так нет риска сломать CI-YAML.
- Выкат — обычный релиз фронтенда (`deploy-staging`). Миграций и ручных шагов нет, раздел в `DEPLOY.md` не нужен.
