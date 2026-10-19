# ARCHITECTURE — цикл 44 ServiceBooking: страница «Все продукты EZBOOK» (about.ezbook.ru)

**Дата: 2026-10-19. Ветка: `cycle/044-products-landing` (от `develop` на `e4670c3`).** Разделы §44.0–§44.17. Требования —
`SPEC_CYCLE44_PRODUCTS_LANDING.md` (+ «Решение заказчика 19.10.2026»), факты о коде — `CURRENT_STATE.md` §5.19, §9.14, §6.2.
Контракт — `API_CONTRACT_CYCLE44_PRODUCTS_LANDING.md` (§44.20–§44.29), машиночитаемо — `contracts/cycle44/openapi.yaml`
(HTTP-поверхность нового сайта и явное «изменений API нет») и `contracts/cycle44/frontend-apps.json` (реестр приложений для
guard-теста). Корневые `ARCHITECTURE.md`, `API_CONTRACT.md` и `SPEC.md` цикл **не трогает**: их переписывает невлитый цикл 43
(P-12, R44-8).

**Коротко.** Пятое приложение того же npm-пакета `frontend/` — `about`. В отличие от четырёх SPA, оно **не содержит ни строчки
клиентского JavaScript**. React-компоненты рендерятся в HTML **на этапе сборки** (`react-dom/server`, SSR-сборка Vite), и в релиз
попадают готовые `index.html`, `404.html`, `robots.txt`, `sitemap.xml` и один CSS-файл. Бэкенд, БД и API не меняются. Адреса
продуктов задаются при сборке из одного модуля (`src/utils/publicSites.ts`), а его согласованность с `PublicSites` в
`appsettings.json` проверяет тест. Сайт выкатывается тем же релизом, что и остальные (`dist/__about`), и обслуживается своим
vhost'ом. Пока заказчик не создаст DNS, сертификат и vhost, сайт молча отсутствует, а ссылки на него в подвалах выключены
переменной сборки.

---

## §44.0. Решения заказчика и ответы на вопросы архитектору одним экраном

Решения от 19.10.2026: Q44-1 (а) — адрес **`about.ezbook.ru`**; Q44-2 (а) — статусы в конфигурации: «Запись» и «Заказы» —
работает, «Дома» — тестовый режим без призыва подключаться, «Бани» — скрыт; Q44-3 (а) — страница открыта для индексации и
отдаётся готовым HTML без JS. Q44-4…Q44-11 — по рекомендациям §0.2 спеки: цен нет, названия «EZBOOK …», адресат — владелец
бизнеса, ссылка в подвалах — P1, кабинеты не трогаем, демо-ссылок и счётчиков нет, тексты — черновики команды на вычитку.

| # | Вопрос | Решение | Где |
|---|---|---|---|
| A44-1 | Источник адресов продуктов и адрес самой страницы | Один TS-модуль `frontend/src/utils/publicSites.ts`: боевые адреса пяти сайтов (4 продукта + `about`) закоммичены, dev-адреса — `localhost:5173…5177`, переопределение — `VITE_SITE_URL_<KEY>`. Тест сверяет боевые адреса с `PublicSites` в `ServiceBooking.API/appsettings.json`. **`PublicSitesOptions` на бэкенде не меняется** (P-3 не трогаем) | §44.3 |
| A44-2 | Готовый HTML | Статическая генерация на этапе сборки: SSR-сборка Vite только для `about` + `renderToStaticMarkup` + node-скрипт `scripts/prerender-about.mjs`. Клиентского JS у страницы **нет вообще**. Остальные приложения не затрагиваются | §44.4 |
| A44-3 | Шаблон главной | **Не расширяем.** Свои компоненты в `about/src/components/`, из общего кода — только `Icon` и `classes.ts` (белый список в `shared-sources.js`). `ServiceLanding`, `LandingAction`, guard T38-09 и главные четырёх продуктов не меняются, кроме однострочного хука прокрутки к якорю (§44.7, P1) | §44.5 |
| A44-4 | Список приложений в ~10 местах | Проходим **все** места по чек-листу §44.6. Сборку и деплой в одно место не сводим: nginx, `ci.yml`, bash-скрипты не умеют читать общий список, а цена ошибки — общий релиз (P-8). Вместо этого вводим **guard-тест реестра** `src/test/appRegistry.guard.test.ts` по `contracts/cycle44/frontend-apps.json`: он падает, если приложение пропущено хоть в одном месте. Тест сразу ловит существующий пропуск: `bani/src` нет в glob `testAreas.guard.test.ts` | §44.6 |
| A44-5 | Цели «Для бизнеса» | У всех четырёх главных секция «Для бизнеса» имеет `titleId: 'biz-title'`. Цель — `<сайт>/#biz-title`; в этой секции гостю видна кнопка «Подключить …» (`/register?returnTo=%2Fcabinet%2Fnew`). Нативная прокрутка к якорю в SPA ненадёжна, поэтому в `ServiceLanding` добавляется одноразовая прокрутка к `location.hash` после монтирования (P1). Без неё гость попадает на верх главной: ошибки и пустого экрана нет | §44.7 |
| A44-6 | Выкат | `deploy/nginx/about.ezbook.conf`, `location ^~ /__about/ { return 404; }` в `ezbook.conf` и `demo.visit.ezbook.conf`. Смоук в `deploy-remote.sh` пропускается, если vhost не установлен. Раздел `DEPLOY.md` §33 (номер — см. R44-A8) с ручными шагами заказчика и предупреждением про certbot. Релиз и откат общие, `rollback.sh` не меняется | §44.9 |
| A44-7 | Будущий переезд на корень | Адрес страницы задан в одном месте (`publicSites.ts`, ключ `about`). Компоненты используют только относительные якоря и адреса, полученные пропсами, а ассеты — пути от корня хоста. Переезд на `ezbook.ru` = правка одной строки + vhost + `301` с `about.ezbook.ru`. Это делается в цикле Р2/Р3, не сейчас | §44.10 |

---

## §44.1. Стек: новых зависимостей — ноль

| Что | Выбор | Почему |
|---|---|---|
| Разметка | React 18 + TSX, как во всех приложениях | Те же компоненты, `Icon`, токены и конвенции. Команда не учит новый шаблонизатор |
| Готовый HTML | `react-dom/server` → `renderToStaticMarkup` на этапе сборки | Уже в зависимостях (`react-dom`). Пакеты-генераторы (`vite-react-ssg`, `vite-plugin-ssr`/Vike, Astro, Next) ради одной страницы — лишняя зависимость с собственным циклом обновлений и риском для `npm audit` (TD-06). Ручной HTML без React потерял бы `Icon`, токены и проверку типов |
| Сборка | Vite 5 (уже есть): клиентская сборка — только для CSS, SSR-сборка (`build.ssr`) — для модуля рендера | Тот же инструмент и тот же PostCSS/Tailwind, что у остальных. SSR включается **только в `vite.about.config.ts`**: остальные конфиги не меняются (требование A44-2) |
| Стили | Tailwind 3 с пресетом `tailwind.config.js` (палитра `cream`/`ink`/`gold`) | Единая дизайн-система (§6.2) |
| Шрифты | **Внешних шрифтов нет** (NFR спеки): стеки пресета откатываются на `ui-serif`/`system-ui` | Google Fonts — это сторонний запрос и лишняя задержка LCP. Самохостинг Newsreader/Inter — P2 (§44.17) |
| Клиентский JS | **Нет** | Интерактивности нет: якоря — это обычные `<a href="#…">`, мобильного меню-«бургера» нет (якоря переносятся строками). Отсюда строгая CSP без `script-src`, отсутствие cookie/storage/SW и нулевой риск утечки скрытого продукта через JS-бандл |
| Хостинг | Тот же nginx на той же машине, статика из `current/__about` | ОЗУ машины на пределе (`DEPLOY.md`, 3,3 ГиБ): новый процесс недопустим, а статический vhost памяти не добавляет. Managed-хостинг/CDN для одной страницы не окупается. Когда витрина переедет на корень (Р3), статический результат без изменений можно отдать с любого CDN |

**Масштабирование.** Нагрузка ничтожна: статические файлы, `index.html` без кеша, `/assets/` immutable. API не вызывается, поэтому
падение API или БД страницу не задевает (NFR «Независимость»).

---

## §44.2. Модель данных

**БД, сущности EF, миграции — без изменений.** У страницы нет данных с сервера. Её «модель» — два модуля конфигурации,
которые читаются **только во время сборки** (точные типы — `API_CONTRACT_CYCLE44_PRODUCTS_LANDING.md` §44.22, §44.23).

```
SiteKey        = 'zapis' | 'goods' | 'dom' | 'bani' | 'about'      ← src/utils/publicSites.ts
SiteUrls       = Record<SiteKey, AbsoluteOrigin>                   ← resolveSiteUrls(env, mode)

ProductKey     = Exclude<SiteKey, 'about'>
ProductStatus  = 'live' | 'testing' | 'hidden'
ProductCard    = общие поля (key, name, icon, forWhom, features[3..5], forClients, clientAction, chooserRows?)
               + по статусу:
                 live    → businessAction обязателен
                 testing → testingNote обязателен, businessAction запрещён (never)
                 hidden  → поля карточки есть (переключение статуса — одна строка), в вывод не попадает
PRODUCTS       = readonly ProductCard[]   ← about/src/content/products.ts; порядок массива = порядок на странице
PAGE_COPY      = тексты шапки, первого экрана, блока «Один аккаунт», подвала, meta  ← about/src/content/page.ts
```

Связи: `ProductCard.key` → `SiteUrls[key]` (адрес сайта), `ProductCard.key` → якорь `#product-<key>`. Сопоставление ключей с
бэкендом (только для теста согласованности): `zapis` ↔ `PublicSites:ServicesBaseUrl`, `goods` ↔ `OrdersBaseUrl`, `dom` ↔
`StaysBaseUrl`, `bani` ↔ `BathsBaseUrl`. У `about` пары нет: бэкенд на эту страницу не ссылается.

Инварианты (проверяют tsc и тест CY44-01):
1. Ключи `PRODUCTS` уникальны, и каждый из четырёх продуктов присутствует ровно один раз (скрытый тоже).
2. `features.length` — от 3 до 5. Нижнюю границу проверяет tsc (кортеж), верхнюю — тест.
3. `testing` → нет `businessAction` (tsc: `never`) и есть непустой `testingNote`.
4. `clientAction.path` и `businessAction.path` — относительные пути, начинающиеся с `/`, без схемы и хоста (tsc: `` `/${string}` ``,
   тест: нет `//`).
5. Тексты без запрещённых слов (R44-7): «лучш», «№1», «самый», «бесплатн», «без комиссии», «₽», «от N руб». Тексты карточек
   `testing` без «подключ», «попробуй», «зарегистр».

---

## §44.3. Адреса сайтов (A44-1, US-44-06)

### §44.3.1 Один модуль — `frontend/src/utils/publicSites.ts`

Чистый модуль без API браузера и Node, поэтому его импортируют все: SSR-рендер `about`, подвалы четырёх SPA, тесты.
`src/utils/` уже входит в общий код вторичных сайтов (`SCANNED_EZBOOK_DIRS`), так что Tailwind- и ESLint-списки не меняются.

- `PRODUCTION_SITE_URLS` — боевые адреса пяти сайтов, **единственное место в `frontend/`, где написано `https://…ezbook.ru`**
  (если не считать `staffPushTexts.ts` — это существующий долг §5.19, его не трогаем). `about: 'https://about.ezbook.ru'`.
- `DEV_SITE_URLS` — `http://localhost:5173` (zapis), `5174` (goods), `5175` (dom), `5176` (bani), `5177` (about).
- `resolveSiteUrls(env, mode)` — для каждого ключа `env.VITE_SITE_URL_<KEY>`, если задан, иначе умолчание режима. Значение
  валидируется: абсолютный `http(s)://host[:port]` без пути, запроса, фрагмента и завершающего `/`. В `production` допускается
  только `https`, кроме `localhost`/`127.0.0.1`. Ошибка → **исключение** (сборка `about` падает сразу, а не публикует битую ссылку).
- `productsPageHref(env)` — для подвалов (§44.8): `null`, если ссылка выключена или адрес некорректен. Не бросает исключений,
  чтобы подвал никогда не ронял SPA.

### §44.3.2 Окружения

| Окружение | Как собирается | Какие адреса |
|---|---|---|
| Локально, `npm run dev:about` и `dev`/`dev:goods`/… | Vite dev (`mode = development`) | `DEV_SITE_URLS`. Если порт занят и Vite выбрал соседний (`strictPort: false`), разработчик задаёт `VITE_SITE_URL_*` в `.env` (`.env.dev.example`) |
| CI (`ci.yml`), стенд (`deploy-staging.yml`), бой (`deploy-production.yml`) | `npm run build:release` (`mode = production`) | `PRODUCTION_SITE_URLS`. **Стенд = бой**: это одна машина и одни хосты (`DEPLOY.md`, «Целевая машина»), поэтому «тестовое окружение» спеки получает те же адреса, что и бой. Для гипотетического стенда с другими хостами — `VITE_SITE_URL_*` в Variables репозитория вместе с `PublicSites__*` на машине (§44.9.5) |
| vitest | `mode = test` | Тесты передают `env` и `mode` явно, на `import.meta.env` не полагаются |

### §44.3.3 Согласованность с бэкендом — тест, а не новый эндпоинт

`src/utils/publicSites.consistency.test.ts` (CY44-07) читает `../../../ServiceBooking.API/appsettings.json` (прецедент импорта
файла вне `frontend/` — `src/test/testAreas.guard.test.ts`) и требует равенства четырёх пар из §44.2. Расходятся адреса — падает
frontend-джоб CI, кто бы ни правил: бэкенд-разработчик в `appsettings.json` или фронт в `publicSites.ts`.

**Почему не эндпоинт и не `PublicSitesOptions.AboutBaseUrl`.** Страница не ходит в API (NFR). Анонимный эндпоинт адресов
нужен был бы только SPA-подвалам, а им достаточно значения из сборки. Поле «сайт без вида компании» в `PublicSitesOptions` ломает
модель «адрес = `CompanyKind`» (P-3): пришлось бы менять `PublicSiteLinks.SiteBaseUrl` (`switch` с `UnreachableException`),
`DeploymentSafetyChecks.ValidatePublicSites` и стражей `CompanyKindExhaustiveTests`. Цена — правки в чувствительном бэкенде ради
значения, которое бэкенд не использует. **Ограничение:** тест сверяет закоммиченные умолчания, а не переопределения
`PublicSites__*` на машине. Это записано в `DEPLOY.md` §33 (§44.9.5).

---

## §44.4. Готовый HTML на этапе сборки (A44-2, US-44-07)

### §44.4.1 Конвейер `npm run build:about`

```
tsc -p tsconfig.about.json
  → vite build --config vite.about.config.ts                 # клиент: about/index.html + /src/styles.css → dist-about/ (HTML-шаблон + assets/*.css, JS нет)
  → vite build --config vite.about.config.ts --ssr           # SSR: about/src/prerender.tsx → dist-about-ssr/prerender.js (Node-модуль)
  → node scripts/prerender-about.mjs                         # import(dist-about-ssr/prerender.js).renderSite({ template, env: process.env, mode: 'production' })
                                                             # → пишет dist-about/index.html, 404.html, robots.txt, sitemap.xml; самопроверки §44.4.3
```

- `about/index.html` — шаблон: `<html lang="ru">`, `charset`, `viewport`, иконки, `<link rel="stylesheet" href="/src/styles.css">`
  и два маркера: `<!--about:head-->` в `<head>` и `<!--about:body-->` в `<body>`. **Тега `<script>` в шаблоне нет.** Vite
  обрабатывает CSS по ссылке из HTML как вход и выдаёт `/assets/index-<hash>.css`. HTML-комментарии при сборке сохраняются
  (тот же приём использует официальный пример Vite SSR с `<!--app-html-->`).
- `vite.about.config.ts` ветвится по `isSsrBuild` (Vite ≥ 5.1, у нас ^5.4): для SSR — `build.ssr: 'src/prerender.tsx'`,
  `outDir: '../dist-about-ssr'`. Зависимости (`react`, `react-dom`) остаются внешними и берутся из `node_modules`.
- `about/src/prerender.tsx` экспортирует `renderSite(input) → Record<имя файла, содержимое>` (контракт — API_CONTRACT §44.24).
  Эта функция **чистая**: она собирает `<head>` (title, description, canonical, og:*) и `<body>` через `renderToStaticMarkup`
  (React экранирует атрибуты и текст, ручного экранирования нет) и подставляет их в маркеры шаблона. Она же формирует
  `robots.txt`, `sitemap.xml` и `404.html`. Её целиком покрывают vitest-тесты в jsdom.
- `scripts/prerender-about.mjs` — тонкий: чтение шаблона, `import()` SSR-модуля, вызов, запись файлов, самопроверки. Год в
  подвале передаётся параметром `year` (из `new Date()` в скрипте), поэтому тесты детерминированы.

### §44.4.2 Разработка — `npm run dev:about`

Vite dev-сервер на `SB_ABOUT_WEB_PORT` (5177) **без прокси `/api`**. Встроенный в `vite.about.config.ts` плагин (`apply: 'serve'`)
в `transformIndexHtml` вызывает `server.ssrLoadModule('/src/prerender.tsx')` и ту же `renderSite(...)` с `mode: 'development'`.
Middleware отдаёт `/robots.txt` и `/sitemap.xml` из того же результата. На изменение файлов `about/src/**` или
`src/utils/publicSites.ts` плагин шлёт `full-reload`. Код рендера в dev и в сборке один и тот же, поэтому расхождение
«в dev работает, в релизе нет» исключено конструктивно. Dev-клиент Vite (`/@vite/client`) в dev есть — это нормально, в релиз он
не попадает. `@vitejs/plugin-react` в этом конфиге **не подключается**: клиентского React нет.

### §44.4.3 Самопроверки сборки (скрипт падает с кодом 1)

1. В любом файле результата остался маркер `<!--about:`.
2. В `index.html` или `404.html` есть `<script` или атрибут `style=` (CSP §44.11 их бы заблокировала).
3. В `index.html` нет ровно одного `<h1`, нет `<link rel="canonical"`, `<meta name="description"` или `<title>`.
4. В `index.html` есть `noindex`.
5. В `index.html` встречается `data-product="<key>"` продукта со статусом `hidden` или его адрес сайта.
6. Число `data-product=` в `index.html` не равно числу не скрытых продуктов.
7. В `index.html` есть `/api/`.

Пункты 5–6 сверяются с `renderSite(...).report` — служебным полем результата, которое на диск не пишется.

### §44.4.4 Запасной путь (если Vite всё же добавит JS-чанк)

Если на конкретной версии Vite клиентская сборка HTML без скрипта всё же выдаст пустой JS-чанк и `<script type="module">`,
`prerender-about.mjs` **не вырезает его молча**: самопроверка 2 роняет сборку. Тогда разработчик переходит на запасной путь:
CSS собирается Tailwind CLI (`tailwindcss -c tailwind.about.config.js -i about/src/styles.css --minify`, бинарь уже есть в
`tailwindcss`), хеш имени файла считает скрипт, а клиентская Vite-сборка из конвейера убирается. Контракт `renderSite` от этого не
меняется. О переходе сообщить в отчёте.

### §44.4.5 Почему не затрагиваются остальные приложения

SSR включается только флагом `--ssr` в `build:about`. `vite.config.ts`, `vite.goods|dom|bani.config.ts` и их `index.html` не
меняются. Общий модуль `publicSites.ts` — чистый TS без серверных API, и в браузерных сборках он остаётся обычным модулем.

---

## §44.5. Компоненты страницы (A44-3)

**Решение: свои компоненты, шаблон главной не расширяется.** Расширение `ServiceLanding` ради страницы без каталога, FAQ и
внутренних маршрутов потребовало бы сделать секции необязательными и добавить в `LandingAction` вид `external`. Это задело бы
типы, guard T38-09, эталон T41-02 и главные четырёх продуктов (P-5). При этом страница использует `react-router` (`Link`), а здесь
роутера нет вовсе.

Из общего кода берётся только белый список (новая константа `ABOUT_SHARED_SOURCES` в `frontend/shared-sources.js`):
`src/components/ui/Icon.tsx`, `src/components/landing/classes.ts` (`H2`, `H3`, `EYEBROW`, `LEAD`, `BTN_PRIMARY`, `BTN_SECONDARY`,
`CHECK_ITEM`, `FOCUS_RING`, `CONTAINER`), `src/utils/publicSites.ts`. По этому же списку строятся Tailwind `content` для `about` и
ESLint-запрет на остальной `src/` (§44.6, п. 4). Сверку списка с фактическими импортами выполняет guard
`about/src/sharedSources.guard.test.ts`.

Компоненты (`about/src/components/`) получают на вход только пропсы (`products`, `sites`, `copy`, `year`). Хуков, состояния,
эффектов, обращений к `window`/`document` в них нет, и это проверяет guard (CY44-03).

| Компонент | Что рисует |
|---|---|
| `AboutPage` | `<header>` + `<main>` + `<footer>`. Единственное место порядка блоков §4 спеки |
| `SiteHeader` | Словесный знак «EZBOOK» (текст, без картинки) и `<nav aria-label="Продукты EZBOOK">` с якорями `#product-<key>` на видимые продукты. Без «Войти» |
| `HeroSection` | Один `h1`, подзаголовок, список продуктов якорями |
| `ProductCard` | `<article id="product-<key>" data-product="<key>" data-status="live|testing" aria-labelledby="product-<key>-title">`, `h3`, иконка `aria-hidden`, «для кого», 3–5 пунктов, «для клиентов», адрес текстом (`new URL(url).host`), действия. У `testing` — плашка «Тестовый режим» **текстом** и `testingNote` |
| `AccountSection` (P1) | Блок «Один аккаунт на всех сайтах»: `h2` и текст из `PAGE_COPY` |
| `ChooserSection` (P2) | «Какой продукт выбрать»: `<dl>` или `<ul>` строк `chooserRows` видимых продуктов со ссылкой-якорем на карточку |
| `SiteFooter` | «EZBOOK», ссылки на видимые продукты, «Правовая информация» → `<zapis>/privacy`, `© {year}` |
| `ExternalLink` | Обычный `<a href>` с абсолютным адресом, без `target`. Классы из `classes.ts` + `FOCUS_RING`, `min-h-[44px]`. Контекст для скринридера даёт `<span className="sr-only"> — {product.name}</span>` |

Раскладка: карточки идут в одну колонку до `lg`, с `lg:` (≥ 1024 px) — `lg:grid-cols-2`. Текст без `truncate`. Якоря в
шапке — `flex-wrap` с высотой касания не меньше 44 px. Иконки по умолчанию: Запись — `scissors`, Заказы — `shopping-bag`,
Дома — `home`, Бани — `clock` (новую иконку не рисуем; выбор фиксируется в конфиге).

Семантика (NFR «Доступность»): один `h1`; `h2` — «Продукты», «Один аккаунт…», «Какой продукт выбрать»; `h3` — у каждой
карточки; `lang="ru"`; ссылка «Перейти к содержимому» (`sr-only focus:not-sr-only`) на `#main`.

---

## §44.6. Регистрация пятого приложения и guard реестра (A44-4, R44-5)

### §44.6.1 Чек-лист мест (образец — `bani`, §5.19). Каждый пункт закреплён за задачей §44.13

| # | Место | Что сделать для `about` | Задача |
|---|---|---|---|
| 1 | `frontend/vite.about.config.ts` | `root: 'about'`, `outDir: '../dist-about'`, алиасы `@about` → `about/src`, `@` → `src`, PostCSS с `tailwind.about.config.js`, порт `SB_ABOUT_WEB_PORT` (5177), **без прокси `/api`/`/uploads`**, ветка `isSsrBuild`, dev-плагин §44.4.2 | FE-44-2 |
| 2 | `frontend/tsconfig.about.json` | extends `tsconfig.json`, `types: ["vite/client"]`, paths `@/*`, `@about/*`, `include: ["about/src", "src/components/ui/Icon.tsx", "src/components/landing/classes.ts", "src/utils/publicSites.ts", "src/vite-env.d.ts"]`. Тесты `about/src/**/*.test.ts(x)` входят, `appsettings.json` — через `resolveJsonModule` | FE-44-2 |
| 3 | `frontend/tailwind.about.config.js` (+ `.d.ts` по образцу bani) | `presets: [base]`, `content: aboutTailwindContent()` из `shared-sources.js` (свои исходники + `ABOUT_SHARED_SOURCES`, **не** весь `src/components`: так CSS остаётся маленьким) | FE-44-2 |
| 4 | `frontend/shared-sources.js` | `ABOUT_SHARED_SOURCES`, `aboutTailwindContent()`. В `SECONDARY_APPS` `about` **не добавляется**: это не «вторичный сайт с общими страницами», и `appTailwindContent('about')` должен по-прежнему бросать исключение | FE-44-2 |
| 5 | `frontend/eslint.config.js` | `ignores`: `dist-about/**`, `dist-about-ssr/**`. Блок `files: ['about/src/**/*.{ts,tsx}']`: запрет `@/App`, `@goods`, `@dom`, `@bani`, `react-router-dom`, `axios`, `@tanstack/react-query`, `zustand`, любых `src/api`, `src/pages`, `src/store`, и любого `src/**` вне `ABOUT_SHARED_SOURCES`. **Обратные запреты** `(^@about/)|(/about/)|(^about/)` — в блоки `goods/src`, `dom/src`, `bani/src`, `src` | FE-44-2 |
| 6 | `frontend/vitest.config.ts` | алиас `@about`, `include` + `about/src/**/*.test.{ts,tsx}` | FE-44-2 |
| 7 | `frontend/package.json` | `dev:about`, `build:about` (§44.4.1). `build:release` дописывает `&& npm run build:about` и аргумент `about` в `merge-site-dist.mjs` | FE-44-2 |
| 8 | `.gitignore` | `frontend/dist-about/`, `frontend/dist-about-ssr/` | FE-44-2 |
| 9 | `frontend/scripts/test-area.mjs`, `contracts/cycle36/test-areas.json`, `src/test/testAreas.guard.test.ts` | обход `about/src`; префикс `about/src/` в области `platform`; в glob guard'а добавить `../../about/src/**` **и `../../bani/src/**`** (сейчас bani туда не входит — существующий пропуск) | FE-44-2 |
| 10 | `.github/workflows/ci.yml` | шаг `npx tsc --noEmit -p tsconfig.about.json`; `../contracts/cycle44/openapi.yaml` в `redocly lint`; смоук `DIST_DIR: frontend/dist/__about`, `SMOKE_PROFILE: about`, `PORT: "4177"`; `VITE_PRODUCTS_PAGE_LINK: ${{ vars.VITE_PRODUCTS_PAGE_LINK }}` в env шага Build. В список `sw.js` **не** добавлять: у `about` нет воркера | DO-44-2 |
| 11 | `deploy/ci/smoke-frontend.sh` | профиль `about` (§44.9.3) | DO-44-2 |
| 12 | `.github/workflows/deploy-staging.yml`, `deploy-production.yml` | `VITE_PRODUCTS_PAGE_LINK` в env шага сборки. `build:release` уже включает `about` | DO-44-2 |
| 13 | `deploy/deploy-remote.sh` | `ABOUT_HOST`, `ABOUT_SMOKE`, `ABOUT_VHOST`, проверка «только если vhost установлен» (§44.9.4) | DO-44-3 |
| 14 | `deploy/nginx/about.ezbook.conf` (новый), `ezbook.conf`, `demo.visit.ezbook.conf` | vhost (§44.9.1); `location ^~ /__about/ { return 404; }` в двух корневых vhost'ах | DO-44-1 |
| 15 | `.env.dev.example` | `SB_ABOUT_WEB_PORT=5177`, закомментированные `VITE_SITE_URL_*` и `VITE_PRODUCTS_PAGE_LINK` с пояснением | DO-44-4 |
| 16 | `DEPLOY.md` | раздел §33 (§44.9.5) | DO-44-4 |

### §44.6.2 Guard реестра — `frontend/src/test/appRegistry.guard.test.ts` (CY44-06)

Источник — `contracts/cycle44/frontend-apps.json`: имя, каталог, подкаталог релиза, vhost, dev-порт, профиль смоука, флаги
`serviceWorker` и `apiProxy`. Тест читает файлы из чек-листа как текст (`?raw`-импорт, как JSON в `testAreas.guard`; если Vite не
пустит за пределы `frontend/`, допускается `node:fs` по конвенции §6.2 — файл в `tsconfig.node.json`, исключён из браузерных
tsconfig) и для **каждого** приложения с непустым `distSubdir` проверяет пункты 5–14 §44.6.1 (для `serviceWorker: true` — ещё
`sw.js` в `ci.yml` и в блоке `globals.serviceworker` ESLint). Новое шестое приложение, которое забыли вписать хоть в одно место,
краснит CI. **Сводить сами места в один список не будем** (цена — переписывание nginx/bash/YAML на генерацию; риск — общий релиз
P-8). Guard даёт ту же гарантию при нулевом риске для работающих сайтов.

---

## §44.7. Цели переходов (A44-5, US-44-02, US-44-03)

Проверено по коду на `e4670c3`:

| Продукт | «Открыть сайт» (клиент) | «Для бизнеса» (только `live`) | Что видит гость в цели |
|---|---|---|---|
| Запись | `<zapis>/` | `<zapis>/#biz-title` | секция 4 главной (`zapisLanding.ts:56`), кнопки «Подключить салон» → `/register?returnTo=%2Fcabinet%2Fnew` и «Войти в кабинет» |
| Заказы | `<goods>/` | `<goods>/#biz-title` | `goodsLanding.ts:53`, «Подключить магазин» → то же |
| Дома (`testing`) | `<dom>/` | — (запрещено типом) | — |
| Бани (`hidden`) | — | — | — (`baniLanding.ts:62` тоже `biz-title`: при смене статуса на `live` цель уже есть) |

Действие клиента ведёт на **корень** сайта, а не на якорь каталога: каталог стоит сразу под первым экраном у всех четырёх, а
якорь в SPA без доработки ненадёжен. Пути живут в `PRODUCTS` (`clientAction.path`, `businessAction.path`), адрес хоста — в
`publicSites.ts`.

**Прокрутка к якорю (FE-44-5, P1).** Браузер пытается прокрутить к `#biz-title` до того, как React отрисует главную, и якоря не
находит. В `ServiceLanding` добавляется хук `useInitialHashScroll()` (`src/components/landing/useInitialHashScroll.ts`): один раз
после монтирования, если `location.hash` непустой и элемент с таким `id` есть, вызывается `scrollIntoView({ block: 'start' })`.
Секция 4 рендерится синхронно из конфига, поэтому элемент к этому моменту уже существует. Каталог над ней догружается позже;
сдвиг компенсирует scroll anchoring браузера, а в браузерах без него якорь может сместиться. Это известное ограничение: ошибки
нет, гость в секции или рядом. Хук не импортирует страницы, не использует `dangerouslySetInnerHTML` и не меняет разметку —
T38-09 и T41-02 остаются зелёными. Глобального scroll-to-top в приложениях нет (✔ grep `scrollTo(`), перебивать хук нечему.

**Защита якоря от переименования (CY44-08).** Тест `about/src/targets.guard.test.ts` читает как текст четыре конфига главных
(`src/pages/home/zapisLanding.ts`, `goods/src/landing/goodsLanding.ts`, `dom/src/landing/stayLanding.ts`,
`bani/src/landing/baniLanding.ts` — через `import.meta.glob(..., { query: '?raw' })`, ESLint-границы при этом не нарушаются) и
требует в каждом `titleId: 'biz-title'` у секции `business`, а у всех `businessAction.path` в `PRODUCTS` — значение `'/#biz-title'`.

---

## §44.8. Ссылка «Все продукты EZBOOK» в подвалах четырёх сайтов (US-44-08, P1, R44-4)

- Общий компонент `src/components/sites/ProductsPageLink.tsx` (каталог общего кода вторичных сайтов, область тестов `platform`).
  Он рендерит `<a href={href} className="hover:text-ink-soft transition-colors">Все продукты EZBOOK</a>` или `null`.
- `href = productsPageHref(import.meta.env)` (§44.3.1). Правило:
  - `import.meta.env.DEV === true` → ссылка есть всегда (в dev — `http://localhost:5177`);
  - иначе ссылка есть **только при `VITE_PRODUCTS_PAGE_LINK === 'on'`**;
  - адрес — `resolveSiteUrls(env, mode).about`; если он некорректен — `null`.
- Вставка — **одна строка** в конец списка ссылок `Footer.tsx`, `GoodsFooter.tsx`, `DomFooter.tsx`, `BaniFooter.tsx`. Остальное
  содержимое подвалов, шапки и навигация не меняются.
- **Почему флаг, а не «ссылка всегда».** Сайт попадает в каждый релиз, но открывается только после ручных шагов заказчика
  (DNS, сертификат, vhost). Без флага первая же выкатка дала бы на всех четырёх сайтах ссылку в никуда (R44-4). Заказчик
  включает ссылки сам, без коммита: Variable репозитория `VITE_PRODUCTS_PAGE_LINK=on` + обычный деплой (`DEPLOY.md` §33, шаг 6).
- `src/vite-env.d.ts`: в `ImportMetaEnv` дописать `VITE_PRODUCTS_PAGE_LINK?`, `VITE_SITE_URL_ZAPIS?`, `…_GOODS?`, `…_DOM?`,
  `…_BANI?`, `…_ABOUT?`.

---

## §44.9. DevOps: vhost, CI, деплой (A44-6)

### §44.9.1 `deploy/nginx/about.ezbook.conf` (DO-44-1)

- `server_name about.ezbook.ru; root /var/www/ezbook/current/__about; index index.html;`,
  `access_log /var/log/nginx/about.access.log;` (обычный формат: в путях этого хоста нет токенов).
- **Никакого проксирования.** `location /api/ { return 404; }` и `location /uploads/ { return 404; }` явно: хост не зависит от
  API и не открывает его второй дверью.
- `location / { try_files $uri $uri/ =404; }` — **без SPA-fallback**: неизвестный адрес даёт честный 404, а не «мягкий 404» для
  поисковика. `error_page 404 /404.html; location = /404.html { internal; }`. `location = /index.html { return 301 /; }` — одна
  каноническая форма.
- Кеш: `/assets/` — `public, max-age=31536000, immutable`; `index.html`, `404.html`, `robots.txt`, `sitemap.xml` — `no-cache`.
- Заголовки — на каждом `location` с `add_header` и на уровне `server` (`add_header` не наследуется, как в соседних vhost'ах):
  - `Content-Security-Policy: default-src 'none'; style-src 'self'; img-src 'self' data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`.
    `script-src` нет совсем: страница без JS, и любая попытка добавить скрипт молча не заработает;
  - `X-Frame-Options: DENY`, `Strict-Transport-Security: max-age=15552000`, `X-Content-Type-Options: nosniff`,
    `Referrer-Policy: strict-origin-when-cross-origin`;
  - **`X-Robots-Tag` нет** (Q44-3 (а)). Это главное отличие от `dom`/`bani`.
- `gzip on;` с `text/css`, `application/xml`, `text/plain` (`text/html` — по умолчанию).
- Шапка файла — как у `bani.ezbook.conf`: ручная установка, `certbot --nginx -d about.ezbook.ru`, **повторное копирование стирает
  443-блок certbot**.

`ezbook.conf` и `demo.visit.ezbook.conf`: `location ^~ /__about/ { return 404; }` рядом с `/__bani/` (иначе
`ezbook.ru/__about/` отдаст страницу с чужого origin, P-7). `goods|dom|bani.ezbook.conf` не меняются: их корень — свой
подкаталог, и `__about` в него не входит.

### §44.9.2 CI (DO-44-2) — `.github/workflows/ci.yml`

Пункты 10–12 §44.6.1. `npm run test:run` подхватывает тесты `about` через `vitest.config.ts`, `npm run lint` — через
`eslint.config.js`. `npm run build:release` собирает `about` и проверяет его самопроверками §44.4.3. Шаги
`types:api:cycle44` и `contracts:json` **не добавляются**: у цикла нет маршрутов API, генерировать нечего (API_CONTRACT §44.20).

P1 (DO-44-5): `deploy/nginx/test-about-vhost.sh` по образцу `test-bani-masking.sh` — поднять `nginx-light` с vhost'ом на
`dist/__about` (HTTP, без TLS) и проверить curl'ом: `/` → 200 с CSP без `script-src`, без `X-Robots-Tag`; `/index.html` → 301;
`/nope` → 404 с телом `404.html`; `/api/health/ready` → 404; `/assets/*.css` → `immutable`. Запускается в CI после масок
dom/bani.

### §44.9.3 Смоук собранного сайта — `deploy/ci/smoke-frontend.sh`, `SMOKE_PROFILE=about` (CY44-04)

Общая часть (favicon.ico/svg) уже есть. Ветка `about` стоит **до** ветки `goods|dom|bani` и завершается `exit 0`:
1. `GET /index.html` → 200; есть `lang="ru"`, ровно один `<h1`, `<title>`, `<meta name="description"`,
   `<link rel="canonical" href="https://`;
2. **нет** `<script`, `noindex`, `<div id="root">`, `/api/`;
3. не меньше двух `data-product="` и у каждой карточки есть `href="http` (`python3`-проверка, как у манифеста);
4. `GET /robots.txt` → 200, есть `Allow: /` и `Sitemap: https://`, нет `Disallow: /` без пути;
5. `GET /sitemap.xml` → 200, парсится как XML, ровно один `<loc>` и он равен canonical из п. 1;
6. `GET /404.html` → 200 (на статическом сервере CI) и есть ссылка на `/`.

Манифест, `apple-touch-icon` и `sw.js` для `about` **не требуются**: это не PWA.

### §44.9.4 Деплой — `deploy/deploy-remote.sh` (DO-44-3)

По образцу `bani` (строки 43–47, 360–388): `ABOUT_HOST="${ABOUT_HOST:-about.ezbook.ru}"`, `ABOUT_SMOKE="${ABOUT_SMOKE:-1}"`,
`ABOUT_VHOST="${ABOUT_VHOST:-/etc/nginx/sites-enabled/about.ezbook.conf}"`.
- `ABOUT_SMOKE=0` → «skipped»; vhost не установлен → `WARNING` и пропуск (деплой не зависит от DNS `about`).
- Иначе: обязателен `current/__about/index.html` (нет → ERROR: релиз собран без `about`); `curl -sf --resolve
  "$ABOUT_HOST:443:127.0.0.1" "https://$ABOUT_HOST/"` → 200, содержит `<h1` и не содержит `<div id="root">`; `curl -sI` того же
  адреса — без `x-robots-tag`. Проверки `/api/health/ready` **нет**: API на этом хосте нет.
- `rollback.sh` не меняется: `about` переключается и откатывается симлинком `current` вместе со всеми (P-8).

### §44.9.5 `DEPLOY.md` — раздел «§33. about.ezbook.ru — страница «Все продукты EZBOOK» (цикл 44)» (DO-44-4)

Порядок первого выката (ручные шаги — только заказчик, агенты DNS, vhost и certbot не трогают):
1. DNS: `A about.ezbook.ru` → та же машина; дождаться `dig +short about.ezbook.ru`.
2. Деплой обычным путём: сборка ляжет в `current/__about`, смоук `about` пропустится с WARNING. Миграций у цикла нет.
3. vhost: `sudo cp deploy/nginx/about.ezbook.conf /etc/nginx/sites-available/`, `ln -s` в `sites-enabled`, `nginx -t`, reload,
   **сразу** `sudo certbot --nginx -d about.ezbook.ru`. Предупреждение: повторное копирование стирает 443-блок certbot.
4. `ezbook.conf` и `demo.visit.ezbook.conf` на машине: дописать строку `location ^~ /__about/ { return 404; }` **вручную**, не
   затирая блоки certbot; `nginx -t`, reload.
5. Проверка: `curl -s https://about.ezbook.ru/ | grep -c '<h1'` (1), `curl -sI https://about.ezbook.ru/ | grep -i x-robots-tag`
   (пусто), `curl -s -o /dev/null -w '%{http_code}' https://about.ezbook.ru/api/health/ready` (404),
   `curl -s -o /dev/null -w '%{http_code}' https://ezbook.ru/__about/` (404). Повторный деплой должен вывести `about OK`.
6. **Только после п. 5:** Variable репозитория `VITE_PRODUCTS_PAGE_LINK=on` (Settings → Variables), затем обычный деплой. В
   подвалах четырёх сайтов появится «Все продукты EZBOOK». Выключение — удалить Variable и задеплоить.
7. Необязательно: добавить сайт в Яндекс Вебмастер и отправить `sitemap.xml`.

Отдельно записать: «Если на машине переопределён `PublicSites__*`, тот же адрес нужно задать в `VITE_SITE_URL_*` (Variables), иначе
ссылки страницы и подвалов разойдутся с адресами в уведомлениях» (ограничение §44.3.3). И: «Статусы продуктов на странице
меняются правкой `frontend/about/src/content/products.ts` и деплоем. Бани показывать (`hidden` → `testing`) только после выката
bani и открытия `https://bani.ezbook.ru/`».

**Откат.** `deploy/rollback.sh`: страница откатывается вместе со всеми. Чтобы убрать страницу совсем — удалить симлинк vhost'а,
выполнить reload и снять Variable `VITE_PRODUCTS_PAGE_LINK`.

---

## §44.10. Будущий переезд на `ezbook.ru` (A44-7, R44-2) — заметка, не работа этого цикла

- Сейчас адрес страницы — **одна строка** `PRODUCTION_SITE_URLS.about`. Из неё строятся canonical, `og:url`, `sitemap.xml`,
  `Sitemap:` в `robots.txt` и ссылка в подвалах. Компоненты собственный адрес не знают: внутренние ссылки — только `#якоря`, ассеты —
  пути от корня хоста (`/assets/…`), а это работает на любом хосте, где сборка лежит в корне.
- При Р2/Р3 (Запись → `visit.ezbook.ru`, витрина → `ezbook.ru`): `about` → `https://ezbook.ru`, `zapis` → `https://visit.ezbook.ru`
  (одновременно с `PublicSites:ServicesBaseUrl` — тест согласованности не даст забыть). Корневой vhost начинает отдавать
  `current/__about`, а `about.ezbook.conf` сводится к `return 301 https://ezbook.ru$request_uri;`. Старые ссылки
  `about.ezbook.ru/...` выживают через 301, поисковик переносит вес по canonical и 301.
- Если витрину придётся отдавать **с подпути** (например, `ezbook.ru/products`), понадобится `base` в `vite.about.config.ts`.
  Сейчас это не нужно, и закладываться на это не будем.

---

## §44.11. Безопасность, приватность, производительность, доступность

- **ПДн и правовое.** Форм, входа, cookie, `localStorage`, service worker, счётчиков нет, и технически их нет за отсутствием JS. CSP
  `default-src 'none'` запрещает и сторонние загрузки. Поэтому `legal-counsel` не нужен (§0-bis спеки). Если любое из этого
  появится — сначала юрист (§0-bis).
- **Секреты.** Нет: сборке нужны только публичные адреса. `VITE_PRODUCTS_PAGE_LINK` — не секрет (Variable, а не Secret).
- **Утечка скрытого продукта.** Скрытая карточка не рендерится, а JS-бандла, куда мог бы попасть её текст, нет. Самопроверка §44.4.3
  п. 5 и тест CY44-02 контролируют это дважды.
- **Чужой origin.** `location ^~ /__about/` → 404 в двух корневых vhost'ах; guard реестра §44.6.2.
- **Кадрирование.** `X-Frame-Options DENY` + `frame-ancestors 'none'`.
- **Производительность.** Ожидаемый вес — HTML ≈ 15–25 КБ, CSS ≈ 8–15 КБ до gzip. Шрифтов, картинок и JS нет, поэтому LCP
  определяет `h1` в первом же ответе. Цель NFR (≤ 2,5 с на 4G) достигается с большим запасом. Ручная проверка — M44-* в QA.
- **Доступность.** §44.5: семантика заголовков, `nav`, skip-link, `FOCUS_RING` на всех ссылках, цели не меньше 44×44, «Тестовый
  режим» текстом, иконки `aria-hidden`, контраст по палитре (`ink` на `cream`, плашка — `warning`/`warning-bg`).
- **Бренд.** «EZBOOK» везде (P-9 не повторять). Guard CY44-03 запрещает в `about/src` написания `ezbook ·` и `Ezbook`.

---

## §44.12. Структура проекта — что добавляется и что меняется

```
frontend/
  about/                                   НОВОЕ — пятое приложение (статическое)
    index.html                             шаблон: маркеры <!--about:head-->, <!--about:body-->, CSS-ссылка, без <script>
    public/                                favicon.svg, favicon.ico, apple-touch-icon.png (копии из frontend/public/)
    src/
      styles.css                           @tailwind base/components/utilities
      prerender.tsx                        renderSite(input) → файлы + report (единственная точка входа SSR)
      content/
        types.ts                           ProductCard (дискриминированное объединение по status), PageCopy
        products.ts                        PRODUCTS — статусы, порядок, тексты карточек, пути действий
        page.ts                            PAGE_COPY — h1, подзаголовок, «Один аккаунт», подвал, meta
      components/
        AboutPage.tsx  SiteHeader.tsx  HeroSection.tsx  ProductCard.tsx
        AccountSection.tsx  ChooserSection.tsx  SiteFooter.tsx  ExternalLink.tsx  Head.tsx  NotFoundPage.tsx
      products.config.test.ts              CY44-01
      render.test.tsx                      CY44-02 (+ head, robots, sitemap, 404)
      about.guard.test.ts                  CY44-03 (URL, /api, dangerouslySetInnerHTML, style, хуки, storage, бренд, запрещённые слова)
      targets.guard.test.ts                CY44-08
      sharedSources.guard.test.ts          импорты about ⊆ ABOUT_SHARED_SOURCES
  src/
    utils/publicSites.ts                   НОВОЕ — адреса пяти сайтов (§44.3)
    utils/publicSites.test.ts              resolveSiteUrls / productsPageHref
    utils/publicSites.consistency.test.ts  CY44-07 — сверка с appsettings.json
    components/sites/ProductsPageLink.tsx  НОВОЕ — ссылка в подвалах (+ .test.tsx, CY44-05)
    components/landing/useInitialHashScroll.ts   НОВОЕ (P1) (+ .test.ts)
    components/landing/ServiceLanding.tsx  ИЗМЕНЕНИЕ (P1): вызов хука, одна строка
    components/layout/Footer.tsx           ИЗМЕНЕНИЕ: <ProductsPageLink />
    test/appRegistry.guard.test.ts         НОВОЕ — CY44-06
    test/testAreas.guard.test.ts           ИЗМЕНЕНИЕ: glob + about/src, bani/src
    vite-env.d.ts                          ИЗМЕНЕНИЕ: типы VITE_PRODUCTS_PAGE_LINK, VITE_SITE_URL_*
  goods/src/components/GoodsFooter.tsx     ИЗМЕНЕНИЕ: <ProductsPageLink />
  dom/src/components/DomFooter.tsx         ИЗМЕНЕНИЕ: <ProductsPageLink />
  bani/src/components/BaniFooter.tsx       ИЗМЕНЕНИЕ: <ProductsPageLink />
  vite.about.config.ts  tsconfig.about.json  tailwind.about.config.js(+.d.ts)   НОВОЕ
  scripts/prerender-about.mjs              НОВОЕ
  shared-sources.js  eslint.config.js  vitest.config.ts  package.json  scripts/test-area.mjs   ИЗМЕНЕНИЕ (§44.6.1)
contracts/cycle44/openapi.yaml             НОВОЕ — HTTP-поверхность about.ezbook.ru; изменений API нет
contracts/cycle44/frontend-apps.json       НОВОЕ — реестр приложений для CY44-06
contracts/cycle36/test-areas.json          ИЗМЕНЕНИЕ: about/src/ в platform
deploy/nginx/about.ezbook.conf             НОВОЕ
deploy/nginx/test-about-vhost.sh           НОВОЕ (P1)
deploy/nginx/ezbook.conf  demo.visit.ezbook.conf   ИЗМЕНЕНИЕ: /__about/ → 404
deploy/ci/smoke-frontend.sh  deploy/deploy-remote.sh   ИЗМЕНЕНИЕ
.github/workflows/ci.yml  deploy-staging.yml  deploy-production.yml   ИЗМЕНЕНИЕ
.gitignore  .env.dev.example  DEPLOY.md    ИЗМЕНЕНИЕ
ServiceBooking.API/**, ServiceBooking.Core/**, ServiceBooking.Tests/**   БЕЗ ИЗМЕНЕНИЙ
```

---

## §44.13. Разбивка работ и параллельность

Бэкенда в цикле нет: **у backend-developer задач нет** (C#, миграции, контракты API не меняются). Работа делится между
frontend-developer (можно на двух исполнителей) и devops-engineer. Между ними действует внутренний контракт из API_CONTRACT
§44.22–§44.27: сигнатуры `publicSites.ts`, типы `PRODUCTS`, `renderSite`, разметочные якоря (`data-product`, `id`), раскладка
`dist/__about`. Он зафиксирован сейчас, поэтому все задачи первой волны стартуют одновременно.

### §44.13.1 Frontend

| Задача | Приоритет | Содержание | Зависит от |
|---|---|---|---|
| FE-44-1 | P0 | `src/utils/publicSites.ts` + `publicSites.test.ts` + `publicSites.consistency.test.ts` (CY44-07), типы в `vite-env.d.ts` | — |
| FE-44-2 | P0 | Каркас и конвейер: `about/index.html`, `public/`, `styles.css`, `vite.about.config.ts` (с dev-плагином и `isSsrBuild`), `tsconfig.about.json`, `tailwind.about.config.js`, `shared-sources.js`, `eslint.config.js`, `vitest.config.ts`, `package.json`, `.gitignore`, `scripts/prerender-about.mjs` с самопроверками, пункты 1–9 §44.6.1, `sharedSources.guard.test.ts`. На время разработки — заглушка `renderSite`, удовлетворяющая контракту | контракт §44.24 |
| FE-44-3 | P0 | Содержимое и компоненты: `content/types.ts`, `products.ts` (статусы по решению заказчика), `page.ts` (черновики текстов по `zapisLanding.ts`, `goodsLanding.ts`, `stayLanding.ts`, `baniLanding.ts` и README «О проекте», Q44-11), компоненты §44.5, `prerender.tsx`; тесты CY44-01, CY44-02, CY44-03 | контракт §44.22–§44.24 (FE-44-1 можно мокать) |
| FE-44-4 | P1 | `ProductsPageLink` + вставка в четыре подвала + тесты CY44-05 | FE-44-1 (сигнатура) |
| FE-44-5 | P1 | `useInitialHashScroll` + вызов в `ServiceLanding` + тест; `targets.guard.test.ts` (CY44-08) | — |
| FE-44-6 | P0 | `src/test/appRegistry.guard.test.ts` (CY44-06) по `contracts/cycle44/frontend-apps.json` | пишется сразу; зелёным становится после FE-44-2, DO-44-1…3 |
| FE-44-7 | P2 | `ChooserSection` («Какой продукт выбрать», `chooserRows`) | FE-44-3 |

### §44.13.2 DevOps

| Задача | Приоритет | Содержание | Зависит от |
|---|---|---|---|
| DO-44-1 | P0 | `deploy/nginx/about.ezbook.conf`; `/__about/` в `ezbook.conf`, `demo.visit.ezbook.conf` | — |
| DO-44-2 | P0 | `ci.yml` (tsc about, redocly cycle44, смоук about, Variable в Build), `deploy-staging.yml`, `deploy-production.yml`, профиль `about` в `smoke-frontend.sh` | раскладка `dist/__about` и разметочный контракт §44.25 (уже зафиксированы) |
| DO-44-3 | P0 | Смоук `about` в `deploy-remote.sh` | — |
| DO-44-4 | P0 | `DEPLOY.md` §33, `.env.dev.example` | — |
| DO-44-5 | P1 | `deploy/nginx/test-about-vhost.sh` + шаг в CI | DO-44-1 |

### §44.13.3 QA

CY44-01…08 (§44.14) — проверить, что написаны и зелёные. Плюс машинная сверка формы: `redocly lint` и `schemathesis` по
`contracts/cycle44/openapi.yaml` против статического сервера на `frontend/dist/__about` (команда — в шапке схемы). Ручные
M44-01…M44-05: 360 px (iOS Safari, Android Chrome) — без горизонтальной прокрутки, цели ≥ 44 px; клавиатура и видимый фокус;
`curl` без JS; переходы со всех карточек в dev и (после выката) на бою; Lighthouse mobile — LCP ≤ 2,5 с, Accessibility без
ошибок. Регресс: тесты главных и подвалов четырёх сайтов, `build:release`, смоуки `ezbook|goods|dom|bani`.

### §44.13.4 Порядок и точки синхронизации

```
Волна 1 (параллельно):  FE-44-1 | FE-44-2 (заглушка renderSite) | FE-44-3 | FE-44-5 | DO-44-1 | DO-44-3 | DO-44-4 | FE-44-6 (красный)
S1: FE-44-2 + FE-44-3 сведены → `npm run build:release` даёт настоящий dist/__about, самопроверки зелёные
Волна 2:                DO-44-2 (смоук about проверяется на реальном артефакте) | FE-44-4 (после FE-44-1) | DO-44-5
S2: FE-44-6 зелёный (все места §44.6.1 на месте) → полный прогон CI → QA
Волна 3 (по остатку):   FE-44-7
```

### §44.13.5 Если не укладываемся — что режется (сверху вниз)

FE-44-7 (P2) → DO-44-5 → FE-44-5 (гость попадёт на верх главной, а не в секцию) → `404.html` и `sitemap.xml` (останутся `robots.txt`
без `Sitemap:` и 404 nginx по умолчанию) → FE-44-4 (US-44-08 P1). **Не режутся:** FE-44-1…3, FE-44-6, DO-44-1…4 —
без них не выполнены P0-истории, а R44-5 остаётся открытым.

---

## §44.14. Тесты (соответствие кейсам спеки)

| Кейс | Файл | Что проверяет |
|---|---|---|
| CY44-01 | `about/src/products.config.test.ts` | инварианты §44.2: уникальность, все четыре ключа, 3–5 пунктов, непустые поля, `testing` без `businessAction` и с `testingNote`, относительные пути, запрещённые слова |
| CY44-02 | `about/src/render.test.tsx` | `renderSite` на фикстурах статусов (все `live`; дефолт заказчика; всё `hidden`, кроме одного): скрытого нет нигде (карточки, `nav`, подвал, chooser, `data-product`, адрес); у `testing` есть «Тестовый режим», нет «Для бизнеса» и слов «подключ/попробуй/зарегистр»; ровно один `h1`; canonical, description, `lang`; ссылки абсолютные и из `sites`; `robots.txt`/`sitemap.xml`/`404.html` |
| CY44-03 | `about/src/about.guard.test.ts` | нет `https?://…ezbook.ru`, `/api`, `dangerouslySetInnerHTML`, `style=`/`style:`, `useState`/`useEffect`, `react-router`, `localStorage`/`document.cookie`/`serviceWorker`, `ezbook ·`/`Ezbook` в исходниках `about/src` (без тестов) |
| CY44-04 | `deploy/ci/smoke-frontend.sh` профиль `about` + самопроверки `prerender-about.mjs` | готовый HTML без JS в собранном артефакте (§44.9.3) |
| CY44-05 | `src/components/sites/ProductsPageLink.test.tsx` | dev → ссылка на `localhost:5177`; prod без флага → нет ссылки; prod с флагом → `https://about.ezbook.ru`; кривой `VITE_SITE_URL_ABOUT` → нет ссылки. Плюс по одному рендер-тесту на каждый из четырёх подвалов (ссылка есть при флаге, правовые ссылки на месте) |
| CY44-06 | `src/test/appRegistry.guard.test.ts` | §44.6.2 |
| CY44-07 | `src/utils/publicSites.consistency.test.ts` | §44.3.3 |
| CY44-08 | `about/src/targets.guard.test.ts` | §44.7 |

Все новые vitest-файлы лежат в областях `platform` (`about/src/`, `src/components/sites/`, `src/test/`) и `companies`/`stays`
(`src/components/landing/`) — `testAreas.guard` это проверяет. `src/utils/publicSites*` попадает в область по префиксу
`src/utils/`, если он уже есть в какой-либо области. Если нет — добавить `"src/utils/publicSites"` в `platform` (FE-44-1).

---

## §44.15. Риски и решения

| # | Риск | Решение |
|---|---|---|
| R44-A1 | Клиентская Vite-сборка HTML без скрипта на текущей версии всё же добавит JS-чанк | Самопроверка §44.4.3 п. 2 роняет сборку; запасной путь §44.4.4 (Tailwind CLI) без изменения контракта |
| R44-A2 | Расхождение dev и релиза | Один `renderSite` и в dev-плагине, и в скрипте; смоук проверяет именно релизный артефакт |
| R44-A3 | Ссылки в подвалах до готовности DNS/сертификата (R44-4) | Флаг `VITE_PRODUCTS_PAGE_LINK`, включение — шаг 6 `DEPLOY.md` §33 после проверки шага 5 |
| R44-A4 | Пропуск места регистрации (R44-5, P-7) | Чек-лист §44.6.1 + guard CY44-06 по реестру |
| R44-A5 | Скрытый продукт утечёт в HTML (R44-1) | Нет JS-бандла; CY44-02; самопроверка п. 5–6 |
| R44-A6 | Адреса страницы разойдутся с `PublicSites` | CY44-07 по закоммиченным умолчаниям; переопределения на машине — правило в `DEPLOY.md` §33 |
| R44-A7 | Строгая CSP сломает страницу (инлайн-стили, внешний ресурс) | Самопроверка п. 2, guard CY44-03 (`style`), DO-44-5 проверяет заголовок; страница не использует ни скриптов, ни внешних ресурсов |
| R44-A8 | Конфликты слияния с циклом 43: `ci.yml`, `package.json`, `eslint.config.js`, `contracts/redocly.yaml`/список lint, `contracts/cycle36/test-areas.json`, `DEPLOY.md` (номер §33 может занять цикл 43) | Правки только дописыванием строк. Номер раздела `DEPLOY.md` — следующий свободный на момент мерджа; ссылки на него в этом документе обновить при закрытии цикла |
| R44-A9 | Якорь `#biz-title` переименуют в одном из продуктов | CY44-08 |
| R44-A10 | Ручное копирование vhost сотрёт 443-блок certbot (P-8) | Предупреждение в шапке vhost'а и в `DEPLOY.md` §33, шаги 3–4 |
| R44-A11 | Тексты устаревают (R44-6), рекламные формулировки (R44-7) | Запрещённые слова в CY44-01/03; пункт «сверить `about/src/content/*`» — в закрывающем шаге документации каждого цикла (сообщить technical-writer при закрытии цикла 44) |
| R44-A12 | Товарный знак (R44-3) | Решение заказчика; имя бренда — в `PAGE_COPY` и `PRODUCTS`, разметка его не содержит |
| R44-A13 | ОЗУ машины | Статика nginx, новых процессов нет |

---

## §44.16. Отклонения от буквы SPEC (читать обязательно)

1. **«Тестовое окружение» = бой.** По `DEPLOY.md` стенд и бой — одна машина с одними хостами, поэтому адреса в сборке одни.
   Переменные `VITE_SITE_URL_*` оставлены для иного стенда (US-44-06 выполняется: в dev — локальные порты, в релизе — один
   источник, сверенный с `PublicSites`).
2. **Сверка с `PublicSites` — по закоммиченным умолчаниям `appsettings.json`**, а не по фактической конфигурации машины (§44.3.3).
3. **«Открыть сайт / Найти …» ведёт на корень продукта**, а не на якорь каталога (§44.7): каталог стоит сразу под первым экраном,
   а дополнительный якорь — лишняя точка отказа.
4. **«Для бизнеса» → `#biz-title`.** Точная прокрутка в секцию — P1 (FE-44-5). Без неё критерий спеки «гость не получает ошибку и
   не попадает на пустой экран» выполнен, но гость окажется на верху главной.
5. **Шрифты.** Без внешних шрифтов (NFR) заголовки рисуются системным serif, а не Newsreader. Визуально страница отличается от
   четырёх сайтов, палитра и сетка те же. Самохостинг шрифтов — P2 (§44.17).
6. **Ссылка в подвалах выключена до шага заказчика** (R44-4). В dev она видна всегда, в релизе — при `VITE_PRODUCTS_PAGE_LINK=on`.
   QA проверяет её тестом CY44-05 и сборкой с флагом.
7. **Правовая ссылка в подвале страницы** — одна, «Правовая информация» → `https://ezbook.ru/privacy` (через `sites.zapis`).
8. **Добавлены `404.html` и `sitemap.xml`**, которых нет в спеке. Это следствие Q44-3 (а): честный 404 вместо SPA-fallback и
   канонический адрес для поисковика. Режутся первыми после P2 (§44.13.5).

---

## §44.17. Открытые вопросы заказчику (кодирование не блокируют; по умолчанию — как в скобках)

- Точная формулировка `h1`, подзаголовка, `<title>`, `description` и строки «Тестовый режим» (черновики команды, Q44-11).
- Самохостинг шрифтов Newsreader/Inter для визуального единства с продуктами (нет — P2, +≈100 КБ).
- Нужна ли правовая ссылка в подвале страницы без сбора данных (да, одна ссылка на политику `ezbook.ru`).
- Переименование самих приложений по Р1 (`ezbook · Заказы` → `EZBOOK Заказы`) — отдельный цикл (P-9 остаётся долгом).
