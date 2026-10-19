# API_CONTRACT — цикл 44 ServiceBooking: страница «Все продукты EZBOOK» (about.ezbook.ru)

**Разделы §44.20–§44.29.** Решения — `ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md` §44.0–§44.17, требования —
`SPEC_CYCLE44_PRODUCTS_LANDING.md`. **Источник истины по форме HTTP** — `contracts/cycle44/openapi.yaml`, реестра приложений —
`contracts/cycle44/frontend-apps.json`. При расхождении с этим текстом по форме права схема, по смыслу — этот документ. Корневой
`API_CONTRACT.md` цикл не трогает (P-12).

---

## §44.20. API ServiceBooking: изменений нет

**В цикле 44 нет ни одного нового маршрута `/api/*`, ни одного изменённого маршрута, DTO, кода ответа или текста ошибки.**
`ServiceBooking.API`, `ServiceBooking.Core` и миграции не меняются. Контракты прошлых циклов (`contracts/cycle*/openapi.yaml`)
действуют без изменений. Шагов `types:api:cycle44` и `contracts:json` для цикла 44 нет: генерировать нечего.

Страница `about.ezbook.ru` **не делает HTTP-запросов к API ни на этапе сборки, ни в браузере**. Её vhost на `/api/` отвечает 404
(§44.21). Ссылка «Все продукты EZBOOK» в подвалах четырёх SPA тоже не делает запросов: адрес приходит из сборки (§44.26).

Существующие эндпоинты, которые рассматривались как источник адресов сайтов и **отвергнуты** (факты — `CURRENT_STATE.md`
§5.19, §9.14):

| Эндпоинт | Почему не подходит |
|---|---|
| `GET /api/companies/kinds-summary` | `[Authorize]`; адреса только тех продуктов, где у пользователя есть компании (P-4) |
| `GET /api/push/config` (`siteUrls`) | `[Authorize]` |
| `GET /api/demo/status` (`siteUrls`) | только `services` и `orders`; смысл — демо-стенды |
| `GET /api/pricing`, `GET /api/pricing/orders` | цены на странице не показываются (Q44-4) |

Полный список фактических эндпоинтов — `CURRENT_STATE.md` §4; цикл его не меняет.

---

## §44.21. HTTP-поверхность `about.ezbook.ru` (контракт nginx ↔ артефакт сборки)

Это единственный «внешний» интерфейс цикла. Его форма закреплена в `contracts/cycle44/openapi.yaml`. Заголовки безопасности
отдаёт только nginx: статический сервер CI их не ставит, поэтому в схеме они описаны, но не обязательны. Обязательность заголовков
проверяют `deploy/nginx/test-about-vhost.sh` (P1) и `deploy-remote.sh`.

| Запрос | Ответ | Тело | Заголовки (nginx) |
|---|---|---|---|
| `GET /` | 200 | `text/html; charset=utf-8` — готовая страница (§44.25) | CSP §44.21.1, `X-Frame-Options: DENY`, HSTS, `nosniff`, `Referrer-Policy`, `Cache-Control: no-cache`; **без `X-Robots-Tag`** |
| `GET /index.html` | 301 → `/` (nginx); 200 на статическом сервере CI | — | — |
| `GET /robots.txt` | 200 | `text/plain` (§44.25.3) | `no-cache` |
| `GET /sitemap.xml` | 200 | `application/xml` или `text/xml` (§44.25.4) | `no-cache` |
| `GET /favicon.svg`, `/favicon.ico`, `/apple-touch-icon.png` | 200 | изображение | — |
| `GET /assets/<name>-<hash>.css` | 200 | `text/css` | `Cache-Control: public, max-age=31536000, immutable` |
| `GET /<любой несуществующий путь>` | **404** | `text/html` — содержимое `404.html` (nginx `error_page`) | как у `/` |
| `GET /api/<что угодно>`, `GET /uploads/<что угодно>` | **404** | любое | — (API на этом хосте нет) |
| `GET https://ezbook.ru/__about/…`, `https://demo.visit.ezbook.ru/__about/…` | **404** | — | — |

### §44.21.1 CSP

```
Content-Security-Policy: default-src 'none'; style-src 'self'; img-src 'self' data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'
```

Следствие для фронта: в HTML нет `<script>`, инлайн-обработчиков, атрибутов `style=`, внешних шрифтов, картинок и стилей.

---

## §44.22. Модуль адресов — `frontend/src/utils/publicSites.ts`

```ts
export type SiteKey = 'zapis' | 'goods' | 'dom' | 'bani' | 'about'
export const SITE_KEYS: readonly SiteKey[]                       // ['zapis', 'goods', 'dom', 'bani', 'about']

/** Абсолютный origin: 'https://goods.ezbook.ru' — без пути, запроса, фрагмента и завершающего '/'. */
export type SiteOrigin = string
export type SiteUrls = Readonly<Record<SiteKey, SiteOrigin>>

export const PRODUCTION_SITE_URLS: SiteUrls
//  zapis: 'https://ezbook.ru'        (= PublicSites:ServicesBaseUrl)
//  goods: 'https://goods.ezbook.ru'  (= PublicSites:OrdersBaseUrl)
//  dom:   'https://dom.ezbook.ru'    (= PublicSites:StaysBaseUrl)
//  bani:  'https://bani.ezbook.ru'   (= PublicSites:BathsBaseUrl)
//  about: 'https://about.ezbook.ru'  (на бэкенде пары нет)

export const DEV_SITE_URLS: SiteUrls
//  zapis: 'http://localhost:5173', goods: '…:5174', dom: '…:5175', bani: '…:5176', about: '…:5177'

/** Переменная окружения ключа: 'zapis' → 'VITE_SITE_URL_ZAPIS', …, 'about' → 'VITE_SITE_URL_ABOUT'. */
export function siteUrlEnvName(key: SiteKey): string

export type SiteMode = 'production' | 'development'
export type SiteEnv = Readonly<Record<string, string | boolean | undefined>>   // совместим с import.meta.env и process.env

/**
 * Для каждого ключа: env[siteUrlEnvName(key)] (после trim), если непустое, иначе умолчание режима.
 * Бросает SiteUrlError (message на английском, с ключом и значением), если значение:
 *  - не парсится new URL(); схема не http/https;
 *  - содержит путь кроме '/', запрос, фрагмент, логин/пароль; оканчивается на '/';
 *  - в mode 'production' имеет схему http и хост не localhost/127.0.0.1.
 */
export function resolveSiteUrls(env: SiteEnv, mode: SiteMode): SiteUrls
export class SiteUrlError extends Error {}

/** Хост для показа текстом: 'https://goods.ezbook.ru' → 'goods.ezbook.ru'; с портом — с портом. */
export function displayHost(origin: SiteOrigin): string

/**
 * Адрес страницы «Все продукты EZBOOK» для подвалов SPA или null. Никогда не бросает.
 *  - env.DEV === true                         → resolveSiteUrls(env, 'development').about
 *  - иначе env.VITE_PRODUCTS_PAGE_LINK === 'on' → resolveSiteUrls(env, 'production').about
 *  - иначе, а также при SiteUrlError           → null
 * Возвращает origin + '/' (например 'https://about.ezbook.ru/').
 */
export function productsPageHref(env: SiteEnv): string | null
```

Переменные окружения (все необязательные, все публичные):

| Переменная | Где читается | Значение по умолчанию |
|---|---|---|
| `VITE_SITE_URL_ZAPIS` … `VITE_SITE_URL_ABOUT` | сборка `about` (`process.env`), подвалы SPA (`import.meta.env`) | по режиму, см. выше |
| `VITE_PRODUCTS_PAGE_LINK` | подвалы SPA | не задана → ссылки в релизе нет. Включает только строгое `on` |
| `SB_ABOUT_WEB_PORT` | только `vite.about.config.ts` (порт dev-сервера) | `5177` |

---

## §44.23. Конфигурация содержимого — `frontend/about/src/content/`

```ts
import type { IconName } from '@/components/ui/Icon'       // единственный тип из общего кода
import type { SiteKey } from '@/utils/publicSites'

export type ProductKey = Exclude<SiteKey, 'about'>
export type ProductStatus = 'live' | 'testing' | 'hidden'
export type RelativePath = `/${string}`                     // без схемы и хоста; '//' запрещено тестом

export interface ProductAction { label: string; path: RelativePath }
export interface ChooserRow { business: string }             // «Салон, барбершоп» — продукт берётся из карточки (P2)

interface ProductBase {
  key: ProductKey
  name: string                                               // 'EZBOOK Запись'
  icon: IconName
  forWhom: string                                            // одна строка: виды бизнеса
  features: readonly [string, string, string, ...string[]]   // 3..5 (≤5 — тест)
  forClients: string                                         // одна строка: что делает клиент
  clientAction: ProductAction                                // label: 'Найти мастера' и т. п.; path: '/'
  demo?: { label: string }                                   // 20.10.2026: ссылка на DEMO_SITE_URLS[key] (publicSites.ts)
  chooserRows?: readonly ChooserRow[]                        // P2
}
export interface LiveProduct    extends ProductBase { status: 'live';    businessAction: ProductAction; testingNote?: never }
export interface TestingProduct extends ProductBase { status: 'testing'; businessAction?: never;        testingNote: string }
export interface HiddenProduct  extends ProductBase { status: 'hidden';  businessAction?: ProductAction; testingNote?: string }
export type ProductCard = LiveProduct | TestingProduct | HiddenProduct

export interface PageCopy {
  meta: { title: string; description: string }              // <title>, <meta name="description">, og:title/og:description
  brand: 'EZBOOK'
  hero: { title: string; subtitle: string }                  // h1 и подзаголовок
  productsHeading: string                                    // h2 блока карточек
  testingBadge: 'Тестовый режим'
  account: { title: string; paragraphs: readonly [string, ...string[]] }   // P1, без обещаний единого входа
  chooser?: { title: string }                                // P2
  footer: { legalLabel: string; legalPath: RelativePath }   // 'Правовая информация', '/privacy' на сайте zapis
  notFound: { title: string; text: string; backLabel: string }
}

export const PRODUCTS: readonly ProductCard[]                // products.ts; порядок массива = порядок на странице
export const PAGE_COPY: PageCopy                             // page.ts
```

> **Изменение 20.10.2026 (решение заказчика).** Все четыре продукта `live` (у `dom` и `bani` — `businessAction` `/#biz-title`, `testingNote` нет). В `ProductBase` добавлено необязательное `demo?: { label: string }`: при нём карточка выводит третью ссылку `<a href="{DEMO_SITE_URLS[key]}/" data-action="demo">` (у `zapis` — `https://demo.visit.ezbook.ru/`, у `goods` — `https://demo.zakaz.ezbook.ru/`; адреса в `frontend/src/utils/publicSites.ts`, одни на все окружения). Демо выводится при любом видимом статусе; демо-хост скрытого продукта входит в `hiddenHosts` самопроверки prerender. Запрет D35-3 (не давать ссылку на `demo.zakaz` до заключения юриста) снят тем же решением заказчика. Таблица ниже — исходные значения 19.10.2026.

Значения по умолчанию (решение заказчика 19.10.2026; тексты — черновики FE-44-3 на вычитку):

| Порядок | `key` | `name` | `status` | `icon` | `clientAction.path` | `businessAction` |
|---|---|---|---|---|---|---|
| 1 | `zapis` | EZBOOK Запись | `live` | `scissors` | `/` | `{ label: 'Для бизнеса', path: '/#biz-title' }` |
| 2 | `goods` | EZBOOK Заказы | `live` | `shopping-bag` | `/` | `{ label: 'Для бизнеса', path: '/#biz-title' }` |
| 3 | `dom` | EZBOOK Дома | `testing` | `home` | `/` | — (`testingNote` обязателен) |
| 4 | `bani` | EZBOOK Бани | `hidden` | `clock` | `/` | `{ label: 'Для бизнеса', path: '/#biz-title' }` — заготовка на будущее, сейчас не выводится |

---

## §44.24. Функция рендера — `frontend/about/src/prerender.tsx`

```ts
export interface RenderSiteInput {
  template: string             // содержимое dist-about/index.html (или about/index.html в dev), с маркерами
  env: SiteEnv                 // process.env в сборке; loadEnv(...) в dev
  mode: SiteMode               // 'production' в build:about; 'development' в dev:about
  year: number                 // для «© {year} EZBOOK»
  products?: readonly ProductCard[]   // по умолчанию PRODUCTS (параметр — для тестов)
  copy?: PageCopy                     // по умолчанию PAGE_COPY
}
export interface RenderReport {
  visibleProducts: readonly ProductKey[]   // в порядке вывода
  hiddenProducts: readonly ProductKey[]
  canonical: string                        // sites.about + '/'
}
export interface RenderSiteOutput {
  files: Readonly<{
    'index.html': string
    '404.html': string
    'robots.txt': string
    'sitemap.xml': string
  }>
  report: RenderReport                     // на диск не пишется; читает prerender-about.mjs для самопроверок
}
/** Чистая функция: без I/O, без Date.now(), без обращения к window/document. Бросает SiteUrlError из resolveSiteUrls
 *  и Error('about template: marker <!--about:head--> not found'), если в шаблоне нет любого из двух маркеров. */
export function renderSite(input: RenderSiteInput): RenderSiteOutput
```

Маркеры шаблона — ровно `<!--about:head-->` и `<!--about:body-->`, каждый ровно один раз. `index.html` и `404.html` получаются
из одного шаблона: CSS-ссылка с хешем у них общая.

Скрипт `frontend/scripts/prerender-about.mjs` (без аргументов): читает `dist-about/index.html`, импортирует
`dist-about-ssr/prerender.js`, вызывает `renderSite({ template, env: process.env, mode: 'production', year: new Date().getFullYear() })`,
пишет четыре файла в `dist-about/`, выполняет самопроверки (`ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md` §44.4.3). Код выхода —
0 или 1 с сообщением `prerender-about: <причина>` в stderr.

---

## §44.25. Разметочный контракт результата (на него опираются смоук, тесты и будущие проверки)

### §44.25.1 `index.html`

```html
<!doctype html>
<html lang="ru">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>{copy.meta.title}</title>
  <meta name="description" content="{copy.meta.description}" />
  <link rel="canonical" href="{sites.about}/" />
  <meta property="og:type" content="website" />
  <meta property="og:title" content="{copy.meta.title}" />
  <meta property="og:description" content="{copy.meta.description}" />
  <meta property="og:url" content="{sites.about}/" />
  <meta property="og:locale" content="ru_RU" />
  <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
  <link rel="alternate icon" href="/favicon.ico" />
  <link rel="apple-touch-icon" href="/apple-touch-icon.png" />
  <link rel="stylesheet" href="/assets/index-<hash>.css" />
</head>
<body>
  <a href="#main" class="sr-only …">Перейти к содержимому</a>
  <header> … EZBOOK … <nav aria-label="Продукты EZBOOK"> <a href="#product-zapis">EZBOOK Запись</a> … </nav></header>
  <main id="main">
    <section aria-labelledby="hero-title"><h1 id="hero-title">{copy.hero.title}</h1> …</section>
    <section aria-labelledby="products-title"><h2 id="products-title">…</h2>
      <article id="product-zapis" data-product="zapis" data-status="live" aria-labelledby="product-zapis-title">
        <h3 id="product-zapis-title">EZBOOK Запись</h3>
        … <p data-role="host">ezbook.ru</p>
        <a href="https://ezbook.ru/" data-action="client">Найти мастера<span class="sr-only"> — EZBOOK Запись</span></a>
        <a href="https://ezbook.ru/#biz-title" data-action="business">Для бизнеса<span class="sr-only"> — EZBOOK Запись</span></a>
      </article>
      <article id="product-dom" data-product="dom" data-status="testing" …>
        … <p data-role="testing-badge">Тестовый режим</p> <p>{testingNote}</p>
        <a href="https://dom.ezbook.ru/" data-action="client">…</a>        <!-- data-action="business" отсутствует -->
      </article>
    </section>
    <section aria-labelledby="account-title"> … </section>          <!-- P1 -->
    <section aria-labelledby="chooser-title"> … </section>          <!-- P2 -->
  </main>
  <footer> … <a href="https://ezbook.ru/privacy">Правовая информация</a> … © {year} EZBOOK </footer>
</body>
</html>
```

Обязательные свойства (проверяют CY44-02, смоук и самопроверки):
- нет `<script`, `style=`, `noindex`, `<div id="root">`, `/api/`;
- ровно один `<h1`; у каждой карточки `h3`; `nav` содержит якоря только видимых продуктов;
- `data-product` есть только у видимых продуктов, `data-status` ∈ {`live`, `testing`};
- `data-action="business"` есть **только** у `data-status="live"`;
- `data-action` ∈ {`client`, `business`, `demo`}; `demo` — только у продукта с `demo` и адресом в `DEMO_SITE_URLS`, при любом видимом статусе
  (демо — витрина на примерных данных, не призыв подключаться); у каждой ссылки действия нет `target` и есть `<span class="sr-only"> — {name}</span>`;
- `data-role="testing-badge"` с текстом «Тестовый режим» — у каждого `testing`;
- все `href` вне `#…` — абсолютные, и их origin ∈ значения `SiteUrls` ∪ значения `DEMO_SITE_URLS`;
- атрибуты `id`, `data-*`, `aria-*` из примера — стабильные имена. Классы Tailwind и порядок атрибутов контрактом не являются.

### §44.25.2 `404.html`

Тот же `<head>`, но `<title>{copy.notFound.title} — EZBOOK</title>` и `<meta name="robots" content="noindex">`. Это допустимо:
страница 404 не индексируется. `canonical` нет. Тело — `h1` с `copy.notFound.title`, текст и ссылка `<a href="/">`.

### §44.25.3 `robots.txt`

```
User-agent: *
Allow: /

Sitemap: {sites.about}/sitemap.xml
```

### §44.25.4 `sitemap.xml`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url><loc>{sites.about}/</loc></url>
</urlset>
```

`lastmod` не пишется: он менялся бы на каждой сборке без изменения содержимого.

---

## §44.26. Ссылка в подвалах — `frontend/src/components/sites/ProductsPageLink.tsx`

```ts
/** Рендерит <a href={productsPageHref(import.meta.env)}>Все продукты EZBOOK</a> или null. Пропсов нет. */
export function ProductsPageLink(): JSX.Element | null
```

- Разметка: `<a href="https://about.ezbook.ru/" data-testid="products-page-link" className="hover:text-ink-soft transition-colors">Все продукты EZBOOK</a>`.
  Это обычная ссылка, не `<Link>` из react-router: адрес на другом origin. Открывается в той же вкладке.
- Место — последний элемент ряда ссылок в `Footer` (ezbook), `GoodsFooter`, `DomFooter`, `BaniFooter`. Шапки не меняются.

---

## §44.27. Артефакт, скрипты, порты

| Что | Значение |
|---|---|
| npm-скрипты | `dev:about` = `vite --config vite.about.config.ts`; `build:about` — конвейер §44.4.1 архитектуры; `build:release` = прежняя цепочка `&& npm run build:about && node scripts/merge-site-dist.mjs goods dom bani about` |
| Промежуточные каталоги | `frontend/dist-about/` (результат), `frontend/dist-about-ssr/` (SSR-модуль, в релиз **не** попадает) — оба в `.gitignore` |
| Релиз | `frontend/dist/__about/` = `index.html`, `404.html`, `robots.txt`, `sitemap.xml`, `favicon.svg`, `favicon.ico`, `apple-touch-icon.png`, `assets/index-<hash>.css`. **Нет** `*.js`, `sw.js`, `manifest.webmanifest` |
| На машине | `/var/www/ezbook/current/__about/` |
| Dev-порт | `SB_ABOUT_WEB_PORT`, по умолчанию 5177; прокси `/api` нет |
| Порт смоука в CI | `4177` |

---

## §44.28. Реестр приложений — `contracts/cycle44/frontend-apps.json`

Машиночитаемый список пяти фронтенд-приложений, который читает guard CY44-06. Сборку он **не** управляет: это эталон для
проверки, что каждое приложение вписано во все места (`ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md` §44.6). Поля записи:

| Поле | Тип | Смысл |
|---|---|---|
| `name` | string `^[a-z]+$` | имя приложения (`ezbook`, `goods`, `dom`, `bani`, `about`) |
| `siteKey` | `SiteKey` | ключ в `publicSites.ts` |
| `sourceDir` | string | `src` для ezbook, иначе `<name>/src` |
| `distSubdir` | string | `""` для ezbook (корень релиза), иначе `__<name>` |
| `vhost` | string | файл в `deploy/nginx/` |
| `host` | string | боевой хост |
| `devPort`, `devPortEnv` | number, string | порт и переменная dev-сервера |
| `smokeProfile` | string | значение `SMOKE_PROFILE` (`ezbook` — профиль по умолчанию) |
| `serviceWorker` | boolean | есть `<app>/public/sw.js` → обязателен в списке R11 `ci.yml` и в ESLint-globals |
| `apiProxy` | boolean | vhost проксирует `/api/` на `127.0.0.1:5000` |
| `publicSitesKey` | string \| null | ключ `PublicSites:*BaseUrl` бэкенда; `null` у `about` |
| `deploySmoke` | `"always"` \| `"if-vhost-installed"` | поведение `deploy-remote.sh` |

Добавление шестого приложения начинается со строки в этом файле. После этого guard перечислит все места, куда его нужно вписать.

---

## §44.29. Как проверить, что стороны сошлись (машинно, без ручных тестов)

```bash
# 1. Схема валидна (CI, frontend-джоб)
cd frontend && npx @redocly/cli lint --config ../contracts/redocly.yaml ../contracts/cycle44/openapi.yaml

# 2. Артефакт соответствует схеме (QA, локально или в CI после build:release)
python3 -m http.server 4177 --directory frontend/dist/__about &
schemathesis run contracts/cycle44/openapi.yaml --base-url http://127.0.0.1:4177 --checks all
#    + тот же прогон против https://about.ezbook.ru после выката (заголовки nginx там присутствуют)

# 3. Разметочный контракт §44.25 и HTTP-поверхность артефакта
DIST_DIR=frontend/dist/__about SMOKE_PROFILE=about PORT=4177 bash deploy/ci/smoke-frontend.sh

# 4. Внутренние контракты §44.22–§44.24, §44.26, реестр §44.28
cd frontend && npx vitest run about/src src/utils/publicSites src/components/sites src/test/appRegistry.guard.test.ts
```
