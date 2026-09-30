# ARCHITECTURE — цикл 30 ServiceBooking: блок «Для покупателей» и настоящие скриншоты на главной goods.ezbook.ru

**Разделы §30.0–§30.15.** Контракт — `API_CONTRACT_CYCLE30.md` (§30.20–§30.27). Машиночитаемые схемы цикла —
`contracts/cycle30/screenshots-manifest.schema.json` и `contracts/cycle30/seed-state.schema.json`.
Нумерация по схеме цикла 29 (`§29.N`): сквозные номера §5xx пересекались уже дважды (C27-1), а цикл 28 занял §570–§603.

**На входе:**
- корневой `SPEC.md` цикла 30. Решения заказчика по §0: Q-30-1 — вариант A целиком; Q-30-2 — блок после каталога и перед
  «Для бизнеса», плюс ссылка «Как сделать заказ» под `h1`; Q-30-3 — снимаем только локально или на изолированном стенде,
  данные выдуманные; Q-30-4 — отдельный телефонный кадр доски (всего 3 кадра); Q-30-5 — обязательны скрипт засева и
  инструкция пересъёмки, скрипт съёмки на усмотрение архитектора; Q-30-6 — push покупателю упоминаем, MAX/WhatsApp нет;
- `CURRENT_STATE.md` на `b9c2a79` (§5.7, §6.2, §7, §9.1);
- код `b9c2a79`: `CatalogHomePage.tsx`, `BusinessBlock.tsx` и его тесты, `OrderPage.tsx`, `OrdersScreenPage.tsx`,
  `OrderCard.tsx`, `OrderPushCard.tsx`, `vite.goods.config.ts`, `eslint.config.js`, `docker-compose.yml`,
  `RateLimitingExtensions.cs`, `AuthController`, `ShopsController`, `contracts/cycle23…26/openapi.yaml`;
- `ARCHITECTURE_CYCLE28.md` из ветки `cycle/028-showcase-data-demo-stand` (прочитан по рабочей копии `../ServiceBooking3`), R30-4.

Корневые `ARCHITECTURE.md`/`API_CONTRACT.md` — документы цикла 3. По конвенции проекта (CURRENT_STATE §6.5, циклы 27–29)
они **не перезаписываются**, документы цикла лежат рядом с суффиксом `_CYCLE30`.

---

## §30.0. Итог решений одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| A1 | Стек | Без изменений. React + TS + Tailwind (конфиг goods), vitest. **Одна новая dev-зависимость — `playwright-core`**: браузеры не скачивает, зависимостей не тянет. Рантайм-зависимостей ноль. Бэкенд, миграции, маршруты, OpenAPI не меняются | §30.1 |
| A2 | Общий компонент или копия (R30-5) | **Копия разметки**, как в цикле 27. `BusinessBlock` получает только `<ScreenshotFigure>`. Общим становится один новый компонент — `ScreenshotFigure`. Одинаковость стилей держит тест паритета классов T30-13, а не общий код: так T27/QA27 гарантированно не меняются | §30.4 |
| A3 | Засев демо-данных (R30-3) | Node-скрипт `frontend/scripts/screenshots/seed-goods-demo.mjs` ходит **через существующий HTTP API**. Стек отдельный: compose-проект `sb-shots` со своим томом и портами. Идемпотентность — пересозданием базы (`stack.sh reset`). Миграций, кода на старте API и изменений бэкенда нет | §30.7 |
| A4 | Защита боевой базы | Три замка до первой записи: хост из белого списка (`localhost`), жёсткий отказ для `*.ezbook.ru`, проба `GET /swagger/v1/swagger.json` = 200 (Swagger есть **только** в Development). Токены и пароль — только в `.state/seed.json`, он не в git | §30.7.1 |
| A5 | Съёмка | Скрипт `capture-goods-screenshots.mjs` на `playwright-core` + установленный Google Chrome. WebP кодирует сам Chrome через CDP `Page.captureScreenshot`, поэтому `sharp`/`cwebp` не нужны. Скрипт проверяет запретные строки в кадре, бюджет веса и размеры в пикселях, а затем пишет манифест | §30.8 |
| A6 | Формат и плотности | Только **WebP**, без запасного PNG/JPEG. Файлов 5: `order-page-1x/2x`, `board-desktop-1x/2x`, `board-phone-2x`. Телефонный кадр доски виден только на телефонах, а у них всех плотность ≥ 2, поэтому 1x ему не нужен | §30.5 |
| A7 | Телефон и десктоп для доски (Q-30-4) | Один `<picture>`: `<source media="(min-width: 768px)">` отдаёт десктопный кадр, `<img>` — телефонный. Грузится ровно один файл | §30.5 |
| A8 | alt и размеры | Номер заказа, время получения и размеры кадров пишет скрипт съёмки в `screenshots.json`. Компонент собирает alt и `width`/`height` из манифеста, так что alt всегда совпадает с картинкой | §30.5, §30.23 |
| A9 | Где лежат картинки | `frontend/goods/src/assets/screenshots/`: своя папка `assets/` у goods, импорт из исходников, хеш в имени от Vite. Общую `frontend/src/assets` не трогаем: картинки нужны только goods | §30.5 |
| A10 | Якоря | Сетка магазинов — `id="shop-list"`, заголовок нового блока — `h2#buyers-title`. Оба с `tabIndex={-1}` и `scroll-mt-24`, потому что шапка goods закреплена (`sticky`). Ссылки — обычные `<a href="#…">`, а не `Link`: путь и `?query` не меняются | §30.6 |
| A11 | Документы и схемы | Новой OpenAPI нет: пустая схема дала бы ложное «проверено» (так же решено в цикле 27, §550). Машиночитаемый контракт цикла — две JSON Schema для файлов, через которые договариваются засев → съёмка → компонент | §30.13 |

---

## §30.1. Стек и зависимости

**Что остаётся как было.** Фронт goods: React 18 + TS strict + Tailwind (`tailwind.goods.config.js`), `Icon` из
`@/components/ui/Icon`, `react-router-dom`, vitest 3 + Testing Library. Сборка — `vite.goods.config.ts`: импорт
`.webp` отдаёт URL с хешем, файлы > 4 КБ не инлайнятся. `.webp` и `.json` уже типизированы (`vite/client` в
`src/vite-env.d.ts`, `resolveJsonModule: true`), так что правки tsconfig не нужны.

**Новая dev-зависимость — `playwright-core` (^1.x, последняя на момент установки, фиксируется `package-lock.json`).**
- **Зачем.** Q-30-5: пересъёмка должна укладываться в 30 минут и давать тот же результат. Вручную не повторить точно
  ширину 390/1280, плотность 2x, обрезку по элементу, часовой пояс магазина и проверку «в кадре нет…». Через пару циклов
  ручные кадры молча разойдутся с продуктом (R30-1).
- **Почему `playwright-core`, а не `playwright`/`@playwright/test`/`puppeteer`.** У `playwright-core` нет
  postinstall-загрузки браузеров и нет транзитивных зависимостей. `npm ci` в CI ставит несколько мегабайт JS и **не
  скачивает браузеры**, что прямо требует SPEC §6. Браузер — установленный Google Chrome (`channel: 'chrome'`) или путь из
  `SHOTS_CHROME_PATH`. Запасной путь для машины без Chrome — `npx playwright-core install chromium`: руками, в кеш
  пользователя, не в репозиторий и не в CI. У `puppeteer-core` те же свойства, но нужные опции контекста (`timezoneId`,
  `locale`, `deviceScaleFactor`, `reducedMotion`) и локаторы у Playwright есть из коробки.
- **Почему не `sharp`/`cwebp`.** Chrome сам кодирует WebP (`Page.captureScreenshot`, `format: 'webp'`, `quality`) через
  CDP-сессию Playwright. Нативных бинарников в `node_modules` не прибавится.
- **Аудит.** `npm audit --omit=dev --audit-level=high` dev-зависимости не проверяет, прод-граф не меняется. Лицензия
  Apache-2.0.
- **CI не меняется.** Шага съёмки в CI нет (SPEC §6). Скрипты только линтуются правилом `scripts/**/*.mjs`, которое уже
  есть в `eslint.config.js`.

**Для скриптов нужен Node ≥ 20** (глобальный `fetch`, `crypto.randomUUID`). В CI стоит 20, на машине разработчика 24.

**Масштаб и продажа как сервиса.** Хостинг не дорожает: плюс ≤ 320 КБ статики на загрузку главной, отдаёт тот же
nginx, CDN нет. Демо-стенд на боевой машине не заводится (Q-30-3).

## §30.2. Модель данных

**База данных, сущности и DTO не меняются.** Данные цикла — это:
1. **Тексты блока** — константы `as const` в `BuyersBlock.tsx` (§30.3). Для компонента и тестов источник истины —
   таблица §30.3.
2. **Манифест скриншотов** — `frontend/goods/src/assets/screenshots/screenshots.json`. Его пишет скрипт съёмки,
   читает компонент. Форма — §30.23 и `contracts/cycle30/screenshots-manifest.schema.json`.
3. **Состояние засева** — `frontend/scripts/screenshots/.state/seed.json`. Его пишет засев, читает съёмка, в git он не
   попадает. Форма — §30.22 и `contracts/cycle30/seed-state.schema.json`.
4. **Демо-набор** — константы в `frontend/scripts/screenshots/demo-data.mjs` (§30.7.3). Существует только в локальной
   базе стека `sb-shots`.

## §30.3. Тексты блока «Для покупателей» (вариант A, дословно)

Правила набора — как в цикле 27 (§545), тест сравнивает строки дословно:
- тире `—` (U+2014) с обычными пробелами вокруг;
- **без неразрывных пробелов** (U+00A0) и `&nbsp;`;
- «ё» — как в таблице (заберёте, всё);
- «QR-коду» — через обычный дефис;
- у заголовков шагов **нет точки в конце** (A5 цикла 27: точка в SPEC — артефакт жирного шрифта в markdown).

| Ключ | Текст |
|---|---|
| eyebrow | `Для покупателей` |
| h2 `#buyers-title` | `Соберите заказ с телефона и заберите, когда он готов` |
| абзац | `Выберите магазин или кафе, добавьте товары в корзину и укажите, когда удобно забрать. Звонить не нужно: магазин увидит заказ сразу, а вы — когда он будет готов.` |
| основная кнопка | `Выбрать магазин` → `href="#shop-list"` |
| вторая кнопка | `Мои заказы` → `/orders` (для анонима и для вошедшего одинаково; анонима перенаправит `RequireAuth`) |
| h3 `#buyers-steps-title` | `Как сделать заказ` |
| steps[0] | icon `store` · title `Выберите магазин` · text `Найдите магазин или кафе в каталоге своего города или откройте его по ссылке либо QR-коду.` |
| steps[1] | icon `shopping-bag` · title `Соберите корзину` · text `Добавьте товары — поштучно или на вес. Сумма за весовой товар уточнится при выдаче.` |
| steps[2] | icon `clock` · title `Выберите время и оформите` · text `Укажите, когда заберёте заказ — как можно скорее или к удобному часу. Всё на одном экране.` |
| h3 `#buyers-track-title` | `Как следить за заказом` |
| track[0] | `После оформления откроется страница заказа: номер, статус и время получения.` |
| track[1] | `Страница обновляется сама — вы увидите, когда магазин примет заказ и когда он будет готов к выдаче.` |
| track[2] | `Включите уведомление на странице заказа, если браузер это разрешает, — тогда о смене статуса сообщим без обновления страницы.` |
| track[3] | `Сохраните ссылку на заказ. Если вы вошли в аккаунт, все заказы будут в разделе «Мои заказы».` |
| track[4] | `Если магазин изменит состав заказа, на странице будет видно, что было и что стало.` |
| строка про оплату | `Оплата — при получении в магазине.` |
| подпись под кадром заказа | `Так выглядит страница заказа: статус меняется сам` |
| подпись под кадром доски (в `BusinessBlock`) | `Экран заказов магазина: новые, принятые и готовые заказы` |
| alt доски (фиксированный) | `Экран заказов магазина: колонки „Новые“, „Принятые“ и „Готовы к выдаче“ с карточками заказов — номер, время получения, покупатель, состав и сумма` |
| alt кадра заказа (шаблон, §30.5) | `Страница заказа на телефоне: заказ № {orderNumber} принят магазином, получение сегодня к {pickupClock}, отмечены шаги „Заказ оформлен“ и „Магазин принял заказ“` |
| ссылка под `h1` | `Как сделать заказ` → `href="#buyers-title"` |

Кавычки: «ёлочки» — в track[3], как в SPEC; „лапки“ — в alt, как в шаблонах SPEC US-30-04/05. Длина: h2 — 52 символа
(≤ 60), самый длинный пункт — track[2], 126 символов (≤ 140).

**Запрещённые слова** (тест T30-11 и ревью): `столов`, `бесплатн`, `комисси`, `WhatsApp`, `MAX`, а также всё из SPEC §3
«Не писать». Корень «уведомл» встречается **только** в track[2]. «Без регистрации» не пишем совсем.

## §30.4. Компоненты и разметка

### §30.4.1 `frontend/goods/src/components/BuyersBlock.tsx` 🆕

`export function BuyersBlock()`, без пропсов. Именованный экспорт; константы `steps`/`track` не экспортируются
(`react-refresh/only-export-components`). `useAuthStore` не нужен: адреса кнопок от входа не зависят.

Дерево. Классы обязательны; помеченное «= biz» копируется **побайтно** из `BusinessBlock.tsx`, это проверяет T30-13:

```
section  aria-labelledby="buyers-title"  className="mt-20 md:mt-28 border-t border-line pt-12"          = biz
  p (eyebrow)  className= biz eyebrow                                                                   = biz
  div  className="max-w-[720px]"
    h2#buyers-title  tabIndex={-1}  className="<biz h2> scroll-mt-24 focus:outline-none"                 ⊇ biz
    p   className="mt-5 text-[16px] leading-relaxed text-ink-soft max-w-[480px]"                        = biz
    div className="mt-7 flex flex-wrap items-center gap-3"                                              = biz
      a  href="#shop-list"  className=<biz primary>  «Выбрать магазин» <Icon name="arrow-right" size={16} aria-hidden />
      Link to="/orders"     className=<biz secondary> «Мои заказы»
  div (панель шагов)  className=<biz panel>                                                             = biz
    h3#buyers-steps-title  className=<biz h3>                                                           = biz
    ol  aria-labelledby="buyers-steps-title"  className="list-none grid md:grid-cols-3 gap-10"           = biz
      li key={title}  → div круг 46×46 + Icon(20, strokeWidth 1.6, text-gold-dark, aria-hidden) + h4 + p  = biz
  div  className="mt-12 md:mt-16 grid md:grid-cols-[1.05fr_0.95fr] gap-10 md:gap-16 items-start"
    div
      h3#buyers-track-title  className="font-serif text-[26px] sm:text-[28px] leading-[1.2] font-medium text-ink mb-6"
      ul  aria-labelledby="buyers-track-title"  className="space-y-4"
        li  className="flex items-start gap-3 text-[15px] leading-[1.6] text-ink"                        = biz benefit li
          Icon name="check" size={18} className="text-gold-dark shrink-0 mt-[3px]" aria-hidden          = biz
          span {text}
      p  className="mt-6 text-[13px] text-muted"  «Оплата — при получении в магазине.»
    ScreenshotFigure  (кадр страницы заказа, §30.5)  className="mx-auto w-full max-w-[320px] md:max-w-[340px]"
```

- **Порядок на телефоне:** надзаголовок → h2 → абзац → кнопки → шаги → «Как следить» → список → оплата → кадр.
  Кадр стоит под списком, как требует US-30-04.
- **Иерархия заголовков:** h2 → h3 «Как сделать заказ» → h4 ×3 → h3 «Как следить за заказом». Уровни не пропускаются.
- `outline-none` на ссылки **не ставить**: видимый фокус остаётся. `focus:outline-none` допустим только на цели якоря
  (h2 и секция каталога). Это не интерактивные элементы, фокус на них — точка старта для следующего Tab.
- JSDoc над компонентом: ссылка на `ARCHITECTURE_CYCLE30.md §30.4` и на `BusinessBlock.tsx` как образец разметки.

### §30.4.2 `frontend/goods/src/components/ScreenshotFigure.tsx` 🆕

Единственная общая часть двух блоков. Именованный экспорт, без состояния:

```ts
type ShotSource = { media: string; srcSet: string; width: number; height: number }
type Props = {
  src: string; srcSet?: string; width: number; height: number   // <img>: кадр по умолчанию (телефонный)
  sources?: ShotSource[]                                          // <source> для других экранов (доска на md+)
  alt: string; caption: string; className?: string; imgClassName?: string
}
```

Разметка:

```
figure className={className}
  picture
    {sources.map(s => <source key={s.media} type="image/webp" media={s.media} srcSet={s.srcSet} width={s.width} height={s.height} />)}
    img src srcSet width height alt loading="lazy" decoding="async"
        className="block w-full h-auto rounded-[20px] border border-line bg-white shadow-soft {imgClassName}"
  figcaption className="mt-3 text-[13px] text-muted text-center"  {caption}
```

- `width`/`height` — CSS-размер 1x-кадра из манифеста. Браузер заранее знает пропорцию, вёрстка не прыгает (R30-7).
  Отображаемый размер задаёт CSS (`w-full h-auto` + `max-w-*` снаружи).
- Рамка `border-line` + `rounded-[20px]` + `shadow-soft` одинаково смотрится на `bg-cream` и на `bg-cream-deep`.
- `sizes` не нужен: `srcSet` с дескрипторами `1x`/`2x`.

### §30.4.3 `BusinessBlock.tsx` ✏️ — только вставка кадра

Между верхней сеткой (`div.grid md:grid-cols-[1.05fr_0.95fr]`) и панелью шагов вставить:

```
<ScreenshotFigure className="mt-12 md:mt-16" {...boardShot} imgClassName="max-w-[320px] mx-auto md:max-w-none" />
```

Больше ничего не меняется: тексты, классы, порядок, JSDoc (дописать ссылку на §30.4.3). Почему T27/QA27 не заденет:
- в `figure` нет `a`, `ul`, `h*` и `svg`;
- `ol.parentElement` — всё та же панель;
- обе ссылки стоят до `ol`;
- в подписи нет `01`/`02`/`03`;
- `alt` не входит в `textContent`.

### §30.4.4 `CatalogHomePage.tsx` ✏️

1. В шапке, сразу после `<p>` «Выберите магазин, соберите заказ…», внутри того же `div.max-w-[720px]`:
   `<p className="mt-2"><a href="#buyers-title" className="inline-flex items-center min-h-[36px] text-sm text-ink-soft underline underline-offset-2 hover:no-underline">Как сделать заказ</a></p>`.
   Стиль подчёркнутой ссылки взят с «Поделиться каталогом города»: `underline underline-offset-2 hover:no-underline min-h-[36px]`.
   Размер `text-sm` вместо `text-xs` — ссылка ведёт к основному содержимому, мелкий шрифт тут неуместен.
2. `<section aria-label="Магазины" …>` получает `id="shop-list" tabIndex={-1}` и классы
   `scroll-mt-24 focus:outline-none`. `aria-live` и остальное не меняются.
3. `<BuyersBlock />` ставится перед `<BusinessBlock />`.

Больше в странице ничего не трогаем: поиск, город, запросы, `ShopRow`.

## §30.5. Скриншоты: файлы, плотности, вес, подключение

| Кадр | Экран | Вьюпорт, CSS px | Файлы | Где показывается | Бюджет (2x) |
|---|---|---|---|---|---|
| страница заказа | `/o/{token}`, статус «Принят» | 390 × до 900 | `order-page-1x.webp`, `order-page-2x.webp` | «Для покупателей», все ширины | ≤ 120 КБ |
| доска, десктоп | `/cabinet/{shopId}/orders` | 1280 × до 820 | `board-desktop-1x.webp`, `board-desktop-2x.webp` | «Для бизнеса», ≥ 768 px | ≤ 200 КБ |
| доска, телефон | то же, вкладка «Новые» | 390 × до 900 | `board-phone-2x.webp` | «Для бизнеса», < 768 px | ≤ 120 КБ |

- **Одна загрузка главной** тянет два файла: кадр заказа и один из кадров доски. Худший случай — 2x-десктоп:
  ≤ 120 + 200 = 320 КБ при бюджете ≤ 450 КБ. У 1x-файла вес не больше, чем у его 2x.
- **Без запасного PNG/JPEG.** WebP поддерживают Safari ≥ 14 / iOS ≥ 14 (2020), Chrome, Firefox, Edge. Запасной формат
  удвоил бы вес в репозитории ради браузеров, в которых goods и так не тестируется. В худшем случае такой браузер
  покажет alt.
- **Имена без `@`** (`-1x`, а не `@1x`): в импорте `@` путается с алиасом. Формат совпадает с расширением — это
  проверяет тест по сигнатуре RIFF/WEBP (урок `salon-hero.jpg`, CURRENT_STATE §9.1).
- **Ленивая загрузка.** У каждого `img`: `loading="lazy"`, `decoding="async"`, `width`/`height`. Оба блока ниже первого
  экрана. `<picture>` с `media` гарантирует, что на неподходящем экране кадр не скачается.

**Модуль `frontend/goods/src/assets/screenshots/shots.ts` 🆕** — единственное место, которое знает про файлы:

```ts
import manifest from './screenshots.json'
import orderPage1x from './order-page-1x.webp'   // … и остальные 4 файла статическими импортами
export const MD_MEDIA = '(min-width: 768px)'      // = Tailwind md; менять вместе с конфигом
export const orderPageAlt = (m: { orderNumber: number; pickupClock: string }) =>
  `Страница заказа на телефоне: заказ № ${m.orderNumber} принят магазином, получение сегодня к ${m.pickupClock}, отмечены шаги „Заказ оформлен“ и „Магазин принял заказ“`
export const orderPageShot = { src: orderPage1x, srcSet: `${orderPage1x} 1x, ${orderPage2x} 2x`,
  width: manifest.orderPage.cssWidth, height: manifest.orderPage.cssHeight,
  alt: orderPageAlt(manifest.orderPage), caption: 'Так выглядит страница заказа: статус меняется сам' }
export const boardShot = { src: boardPhone2x, width: manifest.boardPhone.cssWidth, height: manifest.boardPhone.cssHeight,
  sources: [{ media: MD_MEDIA, srcSet: `${boardDesktop1x} 1x, ${boardDesktop2x} 2x`,
              width: manifest.boardDesktop.cssWidth, height: manifest.boardDesktop.cssHeight }],
  alt: BOARD_ALT, caption: 'Экран заказов магазина: новые, принятые и готовые заказы' }
```

`BOARD_ALT` — фиксированная строка из §30.3. Для телефонного кадра она почти точна: вместо «колонок» там вкладки со
счётчиками. Взят шаблон заказчика из SPEC US-30-05 без правок: текст согласован, а важное дублирует текст блока.

## §30.6. Якоря и фокус (US-30-03)

- Ссылки `href="#shop-list"` и `href="#buyers-title"` — обычные `<a>`. React Router их не перехватывает, браузер делает
  переход по фрагменту: путь и `?city/search/openNow/page` сохраняются (`/city/5?openNow=1#shop-list`). `BrowserRouter`
  получает `popstate` и перерисовывает страницу с тем же `useSearchParams`, фильтры не сбрасываются. `<base>` в
  `goods/index.html` нет (проверено), поэтому `#…` не уводит на `/`.
- Цели якорей — `tabIndex={-1}`. По HTML-спецификации переход по фрагменту фокусирует фокусируемую цель, и следующий
  Tab идёт внутрь секции. `scroll-mt-24` (96 px) не даёт закреплённой шапке (`GoodsNavbar`: `sticky top-0`) закрыть
  цель.
- Плавную прокрутку и JS-обработчики не добавляем. Меньше кода — меньше расхождений между браузерами.
- id проверены против зарезервированных слагов `contracts/cycle23/goods-routes.json`: `shop-list`, `buyers-*` там нет
  (R30-6). `shops`/`catalog` заняты, поэтому их не берём.

## §30.7. Засев демо-данных (US-30-07 SPEC §7, R30-3, R30-4)

### §30.7.1 Изоляция и защита боевой базы (Q-30-3)

1. **Отдельный стек.** `frontend/scripts/screenshots/stack.sh up|down|reset` запускает корневой `docker-compose.yml`
   с `-p sb-shots` и портами `SB_DB_PORT=55432`, `SB_API_PORT=55000`. У стека свой том `sb-shots_postgres_data`,
   обычный dev-стек и его база не затрагиваются. `reset` = `down -v` + `up -d --build postgres api`. Перед вызовом
   скрипт выставляет переменные colima, если они не заданы (память проекта: без `DOCKER_HOST` Docker не найдётся).
2. **Замки в `seed-goods-demo.mjs`, до первого запроса на запись:**
   - `SHOTS_API_URL` (по умолчанию `http://localhost:55000`). Хост должен быть `localhost`/`127.0.0.1`/`::1` или явно
     совпадать с `SHOTS_ALLOW_HOST` (изолированный стенд);
   - хост, оканчивающийся на `ezbook.ru`, — **отказ всегда**, без обхода, код выхода 3, сетевых запросов нет;
   - `GET {api}/swagger/v1/swagger.json` должен вернуть 200. Swagger включается только в `Development`
     (`ApiExtensions.cs:159`), бой всегда `Production`. Иначе код 3 «Это не стенд разработки».
3. **Что не делаем:** миграции с данными, код в `StartupSeedingExtensions`, флаги в БД, SQL в обход API. Всё это ушло бы
   в образ и в бой.
4. **Секреты.** Пароль владельца случайный (`crypto.randomBytes`), живёт только в `.state/seed.json`. Каталог `.state/`
   закрыт `frontend/scripts/screenshots/.gitignore`. `PublicToken` заказа хранится там же, в манифест и в git не попадает.

### §30.7.2 Порядок вызовов (все маршруты существуют, формы — `API_CONTRACT_CYCLE30.md §30.21`)

1. Замки §30.7.1. Затем ожидание `GET /api/health/ready` = 200, не дольше 120 с.
2. `GET /api/legal/documents` — версии `Privacy`, `TermsClient`, `TermsOwner`.
3. `POST /api/auth/register` — владелец: `Елена Демидова`, `+79000000000`, случайный пароль, `legal` с версиями, без
   `phoneVerification`. Если номер занят — код выхода 2: «База уже засеяна — выполните `stack.sh reset`». Токен берётся
   из ответа или из `POST /api/auth/login`.
4. `GET /api/cities` — город по имени `SHOTS_CITY` (по умолчанию `Москва`).
5. `POST /api/shops` — магазин §30.7.3, `ownerTerms.version` = версия `TermsOwner`.
6. `PUT /api/shops/{id}/settings` — `customerMode: Anyone`, `acceptanceMode: Manual`, `allowCustomerCancel: true`,
   `trackStock: false`.
7. `PUT /api/shops/{id}/working-hours` — все 7 дней `07:00–23:00`.
8. `PUT /api/shops/{id}/pickup-settings` — `asapEnabled: true`, `scheduledEnabled: true`, `slotStepMinutes: 15`,
   `preorderDays: 1`, `minPrepMinutes: 60`. Час на приготовление даёт окно съёмки без «Просрочен».
9. `PUT /api/shops/{id}/acceptance` — `mode: Accepting`.
10. `GET /api/shops/{id}/notification-settings`. Если мессенджер покупателю предлагается — выключить через `PUT` той же
    формой. Push покупателю не трогаем: он зависит от настроек стенда, а его карточка в кадр не попадает (§30.8).
11. `POST …/categories` ×3, `POST …/products` ×8 (§30.7.3).
12. **Проверка времени.** Местное время магазина (пояс города) должно быть в `[07:00, 20:30]`, иначе код 4: «Слоты на
    +2 ч не поместятся в рабочие часы; запустите днём или задайте `SHOTS_CITY` с другим поясом».
13. `GET /api/storefront/{slug}` (цены для `expectedUnitPrice`) и `GET /api/storefront/{slug}/pickup-slots` на сегодня.
14. Шесть заказов `POST /api/storefront/{slug}/orders` **без** заголовка авторизации (гость). `captchaToken: null`: в
    dev-стеке секрет капчи пуст, проверка пропускается. `notifyByMessenger: false`, свой `idempotencyKey`. Порядок
    создания задаёт номера (таблица §30.7.3).
15. Владелец: `GET /api/shops/{id}/order-board` (id и `version` по номеру), затем `POST …/orders/{id}/accept` для
    B, C, D, A и `POST …/orders/{id}/ready` для A, B. Тело — `{ expectedVersion }` (`VersionInput`).
16. **Самопроверка:** на доске New = 2, Accepted = 2, Ready = 2, `preorders` пуст. `GET /api/orders/public/{token}`
    заказа C: `status = Accepted`, `pickup.kind = Slot`, дата — сегодня. `pickupClock` = `HH:mm` из `pickup.startUtc`
    в поясе магазина. Любое расхождение — код 5 и текст, что именно не так.
17. Записать `.state/seed.json` (§30.22). Напечатать сводку и команду съёмки.

**Если сервер отказал правилом**: 451 нужен документ, 402 тариф, 409 «не принимает», 429 лимит и т. п. Скрипт
печатает статус и тело ответа и останавливается. **Бэкенд не правится.** Дальше — конфигурация стека: переменные
окружения в `stack.sh`, например `RateLimits__order-create__AnonymousPermitLimit`. Если конфигурацией не решается,
вопрос выносится наверх до продолжения (R30-3). Ожидаемый запас: анонимный лимит заказов — 20 в час с IP, скрипт
создаёт 6, а `reset` пересоздаёт контейнер API и тем самым обнуляет лимиты в памяти.

### §30.7.3 Демо-набор (`demo-data.mjs`)

Всё выдуманное. Имена покупателей — имя и инициал, телефоны — `+7 900 000-00-xx`.

- **Магазин:** `Пекарня на Садовой`, слаг `pekarnya-na-sadovoy`, адрес `ул. Садовая, 12`, телефон `+79000000099`,
  описание одной строкой.
- **Категории и товары:**

| Категория | Товар | Единица | Цена | Прочее |
|---|---|---|---|---|
| Хлеб и выпечка | Хлеб ржаной | Piece | 85 | — |
| | Багет | Piece | 95 | — |
| | Круассан | Piece | 120 | — |
| Кулинария | Сырники | Piece | 180 | `portionText` «2 шт» |
| | Пирог с капустой | Weight | 890 (за кг) | шаг 50 г, минимум 200 г |
| | Салат оливье | Weight | 760 (за кг) | шаг 50 г, минимум 200 г |
| Напитки | Морс клюквенный | Piece | 150 | «0,5 л» |
| | Капучино | Piece | 190 | «300 мл» |

- **Заказы** (порядок создания = номер дня):

| Порядок | Покупатель | Телефон | Получение | Состав | Комментарий | Итоговый статус |
|---|---|---|---|---|---|---|
| A | Мария С. | +79000000001 | как можно скорее | Багет ×1, Круассан ×2 | — | Ready |
| B | Олег П. | +79000000002 | как можно скорее | Пирог с капустой 450 г, Морс ×1 | — | Ready |
| C | Ирина Д. | +79000000003 | слот ≥ сейчас + 90 мин | Хлеб ржаной ×1, Сырники ×2 | — | **Accepted — кадр страницы заказа** |
| D | Сергей К. | +79000000004 | как можно скорее | Салат оливье 300 г, Капучино ×2 | `Капучино без сахара` | Accepted |
| E | Анна Л. | +79000000005 | слот ≥ сейчас + 120 мин | Круассан ×4, Пирог с капустой 600 г | `Упакуйте, пожалуйста, отдельно` | New |
| F | Дмитрий В. | +79000000006 | как можно скорее | Багет ×2 | — | New |

Покрытие US-30-05:
- в каждой колонке по 2 карточки, всего 6;
- весовые (сумма «≈») — B, D, E;
- комментарии — D, E;
- позднее время — C, E.

Сроки всех заказов — не раньше «сейчас + 60 мин» (время приготовления), поэтому «Просрочен» не появится, если снимать в
течение 55 минут после засева.

### §30.7.4 Совместимость с циклом 28 (R30-4)

Цикл 28 (`ARCHITECTURE_CYCLE28.md` §570–§583, ещё не влит) делает витрину и демо **только для «Записи»**: команды
`ops` внутри образа API, пометка `IsShowcase` в БД, отдельный compose-проект демо. goods он не трогает. Наш засев — это
dev-инструмент вне образа, без флагов в БД, через публичный HTTP. Двух несовместимых механизмов в одной базе не
появится. Когда у goods будет свой демо-стенд, генератор `ops` получит профиль goods. Съёмке тогда достаточно, чтобы он
выдал `.state/seed.json` той же формы (§30.22): от способа засева она не зависит. Это записывается в §30.15 как задел,
в этом цикле не делается.

## §30.8. Съёмка (`capture-goods-screenshots.mjs`)

1. Прочитать `.state/seed.json` и проверить `schemaVersion = 1`. Если `now > earliestDueUtc − 5 мин` — код 2:
   «Данные устарели, пересейте».
2. Поднять Vite goods программно (`createServer({ configFile: 'vite.goods.config.ts', server: { port: 55174, strictPort: true } })`,
   `VITE_API_TARGET = state.apiUrl`) либо взять уже запущенный из `SHOTS_WEB_URL`.
3. Браузер: `chromium.launch({ channel: 'chrome' })` или `executablePath: SHOTS_CHROME_PATH`, headless, аргументы
   `--autoplay-policy=no-user-gesture-required --hide-scrollbars`.
   Контекст: `locale: 'ru-RU'`, `timezoneId: state.shop.timeZoneId` (`OrderPage` форматирует дату в поясе браузера),
   `colorScheme: 'light'`, `reducedMotion: 'reduce'`, `serviceWorkers: 'block'`, разрешений не выдавать.
4. Перед каждым кадром: ждать `document.fonts.ready` (шрифты Google, нужен интернет) и `networkidle`.
   Проверить, что на странице нет `[role="dialog"]`.
5. **Кадр заказа** (DPR 1 и 2, вьюпорт 390×844): аноним открывает `/o/{token}`. Номер `[data-testid="order-number"]`
   должен совпасть с `state.orderPage.number`, а `[data-testid="order-pickup"]` — содержать `pickupClock`.
   Обрезка: от `y = 0` (шапка goods) до низа первой `main > section` (карточка с номером, статусом, временем и шкалой)
   + 20 px, не больше 900. Карточка push, блок «Ссылка на этот заказ» и состав в кадр **не входят**: это решение по
   US-30-04, так в кадре нет адреса стенда и состояния разрешений браузера.
6. **Доска, десктоп** (DPR 1 и 2, вьюпорт 1280×900). Вход владельцем через форму `/login` с телефоном и паролем из
   состояния. Если форма потребует шаг, которого нет у скрипта, — запасной путь: `POST /api/auth/login` и запись в
   `localStorage['auth-store']` в форме `persist` из `src/store/authStore.ts`. Затем `/cabinet/{shopId}/orders`.
   Если пилюля показывает «Звук выключен», нажать её и дождаться «Звук включён». Проверить, что в каждой колонке
   (`section[aria-label="Новые"|"Принятые"|"Готовы к выдаче"]`) есть карточка. Обрезка: от `y = 0` до
   `min(низ сетки колонок + 24, 820)`. «Предзаказы» и «Завершённые сегодня» отрезаются.
7. **Доска, телефон** (DPR 2, 390×844): то же, активна вкладка «Новые» (по умолчанию), `role="tablist"` виден.
   Обрезка: от `y = 0` до `min(низ section[aria-label="Новые"] + 16, 900)`.
8. **Запретные строки в кадре** — в тексте шапки и снимаемой области: `Просрочен`, `Нет связи`, `Звук выключен`,
   `Звук заблокирован`, `Сообщение покупателю`, `MAX/WhatsApp`, `localhost`, `127.0.0.1`, `Ссылка на этот заказ`,
   `Отключите автоблокировку`, `Загрузка…`, `Не удалось`. Если хоть одна найдена — код 4 с именем кадра и строкой.
9. **Кодирование:** CDP `Page.captureScreenshot({ format: 'webp', quality, clip: { …, scale: 1 }, captureBeyondViewport: true })`.
   Начальное качество — 82 для 2x и 85 для 1x. Если файл больше бюджета §30.5, качество снижается шагом 5 до 60.
   Если и тогда не укладывается — код 5: уменьшить высоту обрезки, **бюджет не поднимать**. Размер в пикселях по
   заголовку WebP (VP8/VP8L/VP8X) должен быть ровно `cssWidth×DPR` × `cssHeight×DPR`.
10. **Атомарная запись.** Все файлы пишутся во временный каталог. Только если все 5 прошли пп. 8–9, они переносятся в
    `frontend/goods/src/assets/screenshots/`, и туда же пишется `screenshots.json` (§30.23): `capturedAt`,
    `sourceCommit` (`git rev-parse --short HEAD`, при грязном дереве с суффиксом `-dirty`), `browser`
    (`browser.version()`), размеры, байты, `orderNumber`, `pickupClock`, `statusText`. Итог печатается таблицей вместе с
    alt кадра заказа.

npm-скрипты в `frontend/package.json`: `"shots:seed": "node scripts/screenshots/seed-goods-demo.mjs"`,
`"shots:capture": "node scripts/screenshots/capture-goods-screenshots.mjs"`.

## §30.9. Инструкция пересъёмки — `frontend/scripts/screenshots/README.md` 🆕

Цель — ≤ 30 минут. Разделы:
1. **Когда переснимать:** после правок «снятых» файлов:
   - `goods/src/pages/OrderPage.tsx`, `components/OrderTimeline.tsx`, `components/OrderStatusBadge.tsx`,
     `components/GoodsNavbar.tsx`;
   - `pages/cabinet/OrdersScreenPage.tsx`, `pages/cabinet/ShopLayout.tsx`, `components/orders/OrderCard.tsx`;
   - палитры в `tailwind.config.js`.

   `sourceCommit` в манифесте показывает, с какого кода сняты кадры (R30-1).
2. **Что нужно:** Docker (colima: `DOCKER_HOST`, `TESTCONTAINERS_…` не нужен), Node ≥ 20, Google Chrome, интернет
   (шрифты). Снимать днём по времени магазина.
3. **Шаги:** `stack.sh reset` → `npm run shots:seed` → `npm run shots:capture` → просмотреть 5 файлов глазами по чек-листу
   «В кадре нет» (US-30-04/05) → `npm run test:run -- goods/src/assets goods/src/components` → коммит 5 файлов и
   `screenshots.json` → `stack.sh down`.
4. **Коды выхода** обоих скриптов и что делать при каждом.
5. **Ручной запасной путь** (без Playwright). Chrome DevTools → Device Toolbar 390/1280, DPR 2 → «Capture node
   screenshot» → конвертация в WebP (Squoosh, `cwebp -q 80`) → ручная правка `screenshots.json` по схеме →
   `npx --yes ajv-cli@5 validate …` (§30.26).
6. **Никогда:** не указывать `SHOTS_API_URL` на ezbook.ru/goods.ezbook.ru, не коммитить `.state/`.

## §30.10. Тесты

Рядом с кодом, `globals: false`, ID в названии `it(...)`. Тексты в тестах дублируются литералами из §30.3, а не
импортируются из компонента: именно так таблица работает как контракт.

**`goods/src/components/BuyersBlock.test.tsx` 🆕** (рендер `<MemoryRouter><BuyersBlock/></MemoryRouter>`, аноним/вошедший
через `useAuthStore.setState`, как в T27):

| ID | Проверка |
|---|---|
| T30-01 | Надзаголовок `Для покупателей` |
| T30-02 | `h2`: `id="buyers-title"`, текст дословно, `tabIndex = -1` |
| T30-03 | `getByRole('region', { name: <h2> })` с `aria-labelledby="buyers-title"` |
| T30-04 | Абзац дословно |
| T30-05 | Порядок заголовков в DOM: h2, h3 «Как сделать заказ», h4 ×3 (заголовки шагов по порядку), h3 «Как следить за заказом» |
| T30-06 | `ol` из 3 `li`, тексты шагов дословно, каждый ≤ 140 символов, иконки шагов есть |
| T30-07 | `ul` из 5 `li`, пункты дословно, каждый ≤ 140, в каждом `svg[aria-hidden="true"]` |
| T30-08 | Строка про оплату дословно |
| T30-09 | «Выбрать магазин» → `href="#shop-list"`, у неё `bg-ink` и `svg`. «Мои заказы» → `href="/orders"` для анонима и вошедшего. Обёртка кнопок `flex-wrap`, обе ссылки стоят до `ol` |
| T30-10 | Все `svg` в секции — `aria-hidden="true"` |
| T30-11 | В `textContent` нет `/столов/i`, `/бесплатн/i`, `/комисси/i`, `/whatsapp/i`, `/\bMAX\b/`. Корень `/уведомл/i` есть ровно в одном элементе — `track[2]` |
| T30-12 | `img` кадра заказа: `alt` = `orderPageAlt(manifest.orderPage)` (не пустой, содержит номер), `width`/`height` = манифест, `loading="lazy"`, `decoding="async"`, `srcset` содержит `1x` и `2x`. `figcaption` дословно |
| T30-13 | Паритет с `BusinessBlock` (рендер обоих): классы надзаголовка, `section`, абзаца, обёртки кнопок, основной и второй кнопки, панели, `h3` шагов, `ol`, круга, `h4`, `p` шага, `li` и `svg` списка с галочками. Классы `BuyersBlock` ⊇ классов `BusinessBlock` (сравнение наборов токенов) |

**`goods/src/components/BusinessBlock.screenshot.test.tsx` 🆕** (T27/QA27 не трогаются):

| ID | Проверка |
|---|---|
| T30-14 | Одна `figure`. `img`: alt = `BOARD_ALT`, `width`/`height` = `boardPhone`, lazy + async. `picture > source[media="(min-width: 768px)"]`: `srcset` с `1x` и `2x`, `width`/`height` = `boardDesktop`. `figcaption` дословно. `figure` стоит после `ul` преимуществ и до `ol` |

**`goods/src/assets/screenshots/shots.test.ts` 🆕** (читает файлы через `node:fs` и `import.meta.url`):

| ID | Проверка |
|---|---|
| T30-15 | Форма манифеста по §30.23: `schemaVersion = 1`, `capturedAt` и `sourceCommit` непустые. `orderPage.cssWidth = 390`, `boardDesktop.cssWidth ∈ [1280, 1440]`, `boardPhone.cssWidth = 390`, высоты — целые > 0 и ≤ 900. `orderNumber` — целое > 0, `pickupClock` ~ `^\d{2}:\d{2}$`. Все 5 файлов существуют |
| T30-16 | Каждый файл начинается с `RIFF????WEBP`. Бюджеты: `order-page-2x` ≤ 120 КБ, `board-desktop-2x` ≤ 200 КБ, `board-phone-2x` ≤ 120 КБ, каждый 1x ≤ своего 2x. `order-page-2x` + `board-desktop-2x` ≤ 450 КБ |
| T30-17 | Размер в пикселях из заголовка WebP (VP8 — байты 26–29, VP8L — 21–24, VP8X — 24–29) = `cssWidth×k` × `cssHeight×k`, где k = 1 или 2 |

**`goods/src/pages/CatalogHomePage.test.tsx` 🆕** (`vi.mock('../api/goodsCatalog')`, `QueryClientProvider` с
`retry: false`, `MemoryRouter initialEntries={['/city/5?openNow=1']}`):

| ID | Проверка |
|---|---|
| T30-18 | Порядок в `main`: `section[aria-label="Магазины"]#shop-list` (`tabIndex = -1`) → регион «Для покупателей» → регион «Для бизнеса». Ссылка «Как сделать заказ» → `href="#buyers-title"` и стоит до `form[role="search"]` |
| T30-19 | Каталог отвечает ошибкой (мок отклоняет) — оба блока всё равно на странице |

Итог: +19 кейсов vitest. Критерий — «новых красных нет». 8 известных красных `CartPanel` при локальном `.env` с
site-key остаются как есть (CURRENT_STATE §7.2).

## §30.11. Структура проекта — что добавляется и меняется

```
frontend/
├── package.json, package-lock.json            ✏️ + devDependency playwright-core, + scripts shots:seed / shots:capture   (FE-30-3)
├── scripts/screenshots/                       🆕
│   ├── README.md                              инструкция пересъёмки §30.9                                               (FE-30-3, дополняет FE-30-4)
│   ├── .gitignore                             `.state/`                                                                  (BE-30-1)
│   ├── stack.sh                               up | down | reset стека sb-shots                                           (BE-30-1)
│   ├── seed-goods-demo.mjs                    засев §30.7                                                                (BE-30-1)
│   ├── demo-data.mjs                          демо-набор §30.7.3                                                         (BE-30-1)
│   ├── capture-goods-screenshots.mjs          съёмка §30.8                                                               (FE-30-3)
│   └── .state/seed.json                       не в git
└── goods/src/
    ├── assets/screenshots/                    🆕
    │   ├── order-page-1x.webp, order-page-2x.webp, board-desktop-1x.webp, board-desktop-2x.webp, board-phone-2x.webp  (FE-30-4)
    │   ├── screenshots.json                   пишет скрипт                                                               (FE-30-4)
    │   ├── shots.ts                           §30.5                                                                      (FE-30-1)
    │   └── shots.test.ts                      T30-15…17                                                                  (FE-30-2)
    ├── components/
    │   ├── BuyersBlock.tsx 🆕, ScreenshotFigure.tsx 🆕, BusinessBlock.tsx ✏️                                            (FE-30-1)
    │   └── BuyersBlock.test.tsx 🆕, BusinessBlock.screenshot.test.tsx 🆕                                                (FE-30-2)
    └── pages/CatalogHomePage.tsx ✏️, CatalogHomePage.test.tsx 🆕                                                        (FE-30-1 / FE-30-2)
contracts/cycle30/screenshots-manifest.schema.json, seed-state.schema.json   🆕 (architect, этот коммит)
TEST_CATALOG.md                                ✏️ раздел «Цикл 30»                                                        (QA-30-1)
SPEC_CYCLE27_GOODS_BUSINESS_BLOCK.md           🆕 архив спеки 27                                                          (DO-30-1)
ARCHITECTURE_CYCLE30.md, API_CONTRACT_CYCLE30.md                                                                          (architect)
```

**Не меняются:**
- бэкенд целиком, миграции, `contracts/cycle2*`, `contracts/redocly.yaml`, `ci.yml`, `docker-compose*.yml`;
- `GoodsNavbar`, `OrderPage`, `OrdersScreenPage`, `OrderCard`;
- `frontend/src/**`, ezbook, `tailwind*.config.js`, tsconfig, `vite*.config.ts`, `eslint.config.js`;
- `BusinessBlock.test.tsx`, `BusinessBlock.qa.test.tsx`;
- корневые `ARCHITECTURE.md`/`API_CONTRACT.md`, `README.md`, `CHANGELOG.md` (SPEC §10 — «Вызов 2»).

## §30.12. Разбивка работ и параллельность

Бэкенд-кода в цикле нет. `backend-developer` пишет засев, потому что лучше всех знает правила API. Пишет его на JS
(Node), в дерево `frontend/`.

| ID | Роль | Задача | Готово, когда | Зависит от |
|---|---|---|---|---|
| DO-30-1 | devops | `git show b9c2a79:SPEC.md > SPEC_CYCLE27_GOODS_BUSINESS_BLOCK.md` в корне, коммит в ветку цикла | файл есть и побайтно совпадает с `b9c2a79:SPEC.md` | — |
| BE-30-1 | backend | `stack.sh`, `seed-goods-demo.mjs`, `demo-data.mjs`, `.gitignore` по §30.7 и §30.22. Код выхода и текст на каждую ошибку, `--help` | на чистом `stack.sh reset` скрипт проходит, самопроверка п. 16 зелёная. Повторный запуск без `reset` даёт код 2. `SHOTS_API_URL=https://goods.ezbook.ru` даёт код 3 без единого сетевого запроса (логом видно). `npm run lint` зелёный. Бэкенд не тронут (`git diff --stat` вне `frontend/scripts`) | — |
| FE-30-1 | frontend | `BuyersBlock.tsx`, `ScreenshotFigure.tsx`, `shots.ts`, вставка в `BusinessBlock.tsx`, правки `CatalogHomePage.tsx` по §30.3–§30.6 | `tsc -p tsconfig.goods.json` и `npm run lint` зелёные. Визуально 360/768/1280 без горизонтальной прокрутки — после FE-30-4 | — (vitest и `vite build` зелёные только после появления файлов FE-30-4) |
| FE-30-2 | frontend | Тесты T30-01…19 по §30.10 | все 19 зелёные на FE-30-1 + FE-30-4 | таблица §30.3 и схема §30.23, а не код |
| FE-30-3 | frontend | `playwright-core` в devDependencies, npm-скрипты, `capture-goods-screenshots.mjs` по §30.8, `README.md` по §30.9 | на данных BE-30-1 даёт 5 файлов и манифест, который проходит `ajv` (§30.26). Запретная строка в кадре (проверка — временно подставить ее в список) даёт код 4. `npm run lint`, `npm audit --omit=dev --audit-level=high` зелёные | форма `.state/seed.json` §30.22 (не код BE-30-1) |
| FE-30-4 | frontend | Прогон `reset → seed → capture` по README с секундомером. Просмотр кадров глазами по спискам «В кадре нет». Коммит 5 WebP и `screenshots.json` | T30-12…17 зелёные, `build:release` зелёный, время прогона записано в README (≤ 30 мин) | BE-30-1, FE-30-3 |
| QA-30-1 | qa | Раздел «Цикл 30» в `TEST_CATALOG.md`: T30-01…19, ручные M30 (§30.14), независимые `BuyersBlock.qa.test.tsx` (QA30-…) по SPEC | раздел есть, QA-тесты зелёные | для прогона — FE-30-1, FE-30-4 |
| QA-30-2 | qa | Финальный прогон §30.14, проверки §30.26, ручные M30 | §30.14 выполнен | всё выше |

**Параллельно с первой минуты:** DO-30-1, BE-30-1, FE-30-1, FE-30-2, FE-30-3, QA-30-1 (написание кейсов).
FE-30-2 пишется по таблице §30.3 и схеме манифеста, FE-30-3 — по схеме состояния засева. Коды друг друга им не нужны.

**Последовательно:** BE-30-1 + FE-30-3 → FE-30-4 (критический путь: без файлов не соберутся vitest и `vite build`
главной) → QA-30-2.

**Рабочие копии.** FE-30-1/2/3/4 — один исполнитель или по очереди в одном worktree: у них общие `package.json`,
`BusinessBlock.tsx`, `README.md`. BE-30-1 — свой worktree: трогает только свои новые файлы в `frontend/scripts/screenshots/`.
Оркестратору: DO-30-1 сверить по `git log` до мержа, трек devops воркфлоу умеет пропускать (память проекта).

**Заглушки картинок не коммитить.** Нельзя, чтобы в `develop` уехали не настоящие кадры. Если среда агента не
поднимает Docker/Chrome, FE-30-4 выполняет человек по README (§30.15, R-A).

## §30.13. Контракт и машиночитаемые схемы

- **HTTP API не меняется.** Ни маршрутов, ни DTO, ни кодов. `Cycle22RouteTable.golden.txt` не правится.
  **Новой `contracts/cycle30/openapi.yaml` нет.** Формы маршрутов, которые вызывает засев, уже описаны в
  `contracts/cycle11`, `cycle23`, `cycle24`, `cycle25` и `cycle26/openapi.yaml`. Источники истины — они (§30.21).
- **Машиночитаемый контракт цикла** — две JSON Schema (draft-07, без `format`, проверяются готовым `ajv-cli`):
  - `contracts/cycle30/seed-state.schema.json` — засев (BE-30-1) ↔ съёмка (FE-30-3);
  - `contracts/cycle30/screenshots-manifest.schema.json` — съёмка (FE-30-3/4) ↔ компонент и тесты (FE-30-1/2).
- **Контракт компонента и теста** — таблица текстов §30.3 и id якорей §30.24.

## §30.14. Критерии готовности цикла и ручные кейсы

**Автоматически (QA-30-2):**
- `cd frontend && npm ci && npm run lint && npx tsc --noEmit && npx tsc --noEmit -p tsconfig.goods.json && npm run test:run && npm run build:release`;
  новых красных нет, T30-01…19 зелёные;
- `npm audit --omit=dev --audit-level=high` зелёный;
- `npx --yes ajv-cli@5 validate -s contracts/cycle30/screenshots-manifest.schema.json -d frontend/goods/src/assets/screenshots/screenshots.json` — valid;
- `git diff --stat b9c2a79 -- ServiceBooking.* contracts/cycle2* .github` — пусто;
- `git ls-files frontend/scripts/screenshots/.state` — пусто;
- `grep -rn "столов\|бесплатн\|комисси" frontend/goods/src/components/BuyersBlock.tsx` — пусто;
- бэкенд-наборы не затронуты, прогон — по усмотрению QA.

**Ручные M30 (формат M27: `Кейс | Шаги | Ожидаемый результат | Критерий`):**
- **M30-01.** Раскладка на 360, 768 и 1280 px: нет горизонтальной прокрутки. Порядок: каталог → «Для покупателей» →
  «Для бизнеса». На 360 кадр доски телефонный, от 768 — десктопный.
- **M30-02.** Чёткость на экране 2x (Retina) — текст на кадрах не мылится.
- **M30-03.** Нет сдвига вёрстки при загрузке картинок: DevTools → Performance → Layout Shift около нуля от `img`.
- **M30-04.** Ленивая загрузка: вкладка «Сеть», картинки запрашиваются только при прокрутке. На 360 `board-desktop-*` не
  запрашивается никогда, на 1280 — `board-phone-*`.
- **M30-05.** Вес: картинки на одну загрузку ≤ 450 КБ, в ответах `Content-Type: image/webp` (`vite preview`, при выкате
  — на стенде).
- **M30-06.** VoiceOver/TalkBack: alt читается, иконки молчат, заголовки идут по иерархии.
- **M30-07.** Кадры сверены со списками «В кадре нет» из US-30-04/05. Номер и время в alt совпадают с картинкой.
- **M30-08.** Якоря: на `/city/{id}?openNow=1&search=…` «Как сделать заказ» и «Выбрать магазин» прокручивают к цели, не
  прячут её под шапкой, фильтры не сбрасываются. После Enter с клавиатуры следующий Tab идёт внутрь цели.
- **M30-09.** «Мои заказы»: аноним попадает на `/login?returnTo=%2Forders`, после входа — на `/orders`.
- **M30-10.** **Push на реальном телефоне** (Q-30-6, C24-2). На стенде с `WEBPUSH_STAFFPUSH_PROVIDER=web-push` проверить
  Android Chrome и iPhone с goods на экране «Домой»: включить уведомление на странице заказа, персонал меняет статус,
  уведомление приходит. **Гейт выката текста track[2]**, не гейт мержа.
- **M30-11.** Пересъёмка по README с секундомером ≤ 30 мин (можно зачесть прогон FE-30-4).
- **M30-12.** Защита: `SHOTS_API_URL=https://goods.ezbook.ru npm run shots:seed` → код 3, сетевых запросов нет.

## §30.15. Риски и решения

| # | Риск | Вероятность / влияние | Решение |
|---|---|---|---|
| R-A | Среда агента не поднимает Docker/colima или Chrome, нет интернета для шрифтов | средняя / высокое (без файлов цикл не закрыть) | FE-30-4 делает человек по README. Заглушек нет, красные T30-15…17 — намеренный гейт |
| R-B | Засев упирается в правило сервера: 451 по документам, требование подтверждённого телефона, тариф, лимит | низкая / среднее | Сначала конфигурация стека через env в `stack.sh`. Бэкенд не правим. Если не решается — стоп и вопрос наверх (R30-3) |
| R-C | WebP 2x не влезает в бюджет | средняя / низкое | Снижение качества до 60, затем меньше высота обрезки (доска — до 1 карточки на колонку). Бюджет молча не поднимаем |
| R-D | Скриншоты устаревают (R30-1) | высокая со временем / среднее | Список «снятых» файлов в README, `sourceCommit` в манифесте, пересъёмка ≤ 30 мин. Автоматической проверки свежести нет (решение SPEC) |
| R-E | Демо-данные попадают в боевую базу | низкая / высокое | Три замка §30.7.1, отдельный compose-проект, никаких миграций и кода на старте, `.state/` вне git, M30-12 |
| R-F | **Текст track[2] обещает push, а на бою провайдер `logging`** (CURRENT_STATE §1: `WEBPUSH_STAFFPUSH_PROVIDER` не задан 🖥; C24-2 — на устройствах не проверялось) | средняя / высокое (обещание без функции) | До выката главной devops проверяет на машине `WEBPUSH_STAFFPUSH_PROVIDER=web-push` и VAPID (C21-2: реальный `sub` для Apple). QA проходит M30-10. Если push на бою не включают — track[2] снимается до выката, решает заказчик |
| R-G | Старые браузеры без WebP или без `width`/`height` у `<source>` | низкая / низкое | Покажется alt или будет небольшой сдвиг вёрстки. Принято |
| R-H | Номера § пересекаются с другими циклами | — | Схема `§30.N`, как у цикла 29 |
| R-I | Агенты в одном чекауте затирают правки друг друга | средняя / среднее | Распределение по worktree в §30.12 |
| R-J | Кадр доски на 768 px после ужатия до ~700 px мелковат (R30-2) | средняя / низкое | Снимаем при 1280, обрезаем по высоте. На 768–1024 это иллюстрация, подробности — в тексте блока |
| R-K | Съёмка ловит случайное состояние: подсветку нового заказа, модалку уведомлений платформы | низкая / низкое | `reducedMotion`, проверка на `[role="dialog"]`, запретные строки, осмотр глазами в FE-30-4 |
| R-L | Цикл 28 и будущий goods-демо дадут второй механизм засева | низкая / низкое | Съёмка зависит только от `.state/seed.json`. Будущий профиль goods в `ops` выдаёт тот же файл (§30.7.4) |
| — | Авторизация, секреты, масштабирование продукта | — | Не затрагиваются: статичный блок, чтение стора авторизации без изменений. Секреты засева локальные и живут вне git |

**Отклонения от буквы SPEC (осознанные):**
1. Ссылка под `h1` — `text-sm text-ink-soft`, а не `text-xs text-muted` как у «Поделиться»: подчёркивание и отступы те
   же, крупнее ради заметности (§30.4.4).
2. alt доски — шаблон SPEC дословно, хотя на телефонном кадре вкладки, а не колонки (§30.5).
3. Проверка изображения `BusinessBlock` вынесена в отдельный файл теста, чтобы T27/QA27 не трогать ни одной строкой.
