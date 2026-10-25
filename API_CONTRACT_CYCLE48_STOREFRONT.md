# API_CONTRACT — цикл 48 ServiceBooking: витрина для эквайера

**Дата: 2026-10-26. Ветка `cycle/048-card-payments-storefront`.** Разделы §48.20–§48.31. Решения — `ARCHITECTURE_CYCLE48_STOREFRONT.md`
(§48.0–§48.18). Машиночитаемо (источник истины по форме):
- `contracts/cycle48/openapi.yaml` — HTTP-дельта и схема `OperatorPublicRequisites`;
- `contracts/cycle48/storefront-pages.json` — тексты;
- `contracts/cycle48/forbidden-phrases.json` — запрещённые фразы;
- `contracts/legal/operator-public.json` — значения.

При расхождении по форме прав машиночитаемый файл, по смыслу — этот документ.

---

## §48.20. Что меняется и что нет

| Поверхность | Изменение |
|---|---|
| Новые HTTP-маршруты `/api/*` | **нет** |
| Форма DTO (поля, типы, nullability, коды ответов) | **нет** |
| `GET /api/pricing` | значения `notice`, `messengerAddonsNote.taxNote`, `plans[].description/highlights` («Сеть», «Пробный период») |
| `GET /api/pricing/orders` | значения `notice`, `plans[].description/highlights` («Сеть магазинов») |
| `GET /api/billing/subscription` | значение `messengerAddonsNote.taxNote` |
| `POST /api/billing/subscription/request`, `POST /api/notification-channels` | побочный эффект: «сигнал оператору» после фиксации (§48.25). Ответы прежние |
| CLI `ops tariffs plan\|apply` | новые строки отчёта (§48.26) |
| CLI `legal publish`, `legal status` | сверка реквизитов (§48.29) |
| SPA-маршруты 4 сайтов | `/contacts`, `/payment-info`; `/payment-terms` → `/payment-info` (§48.27) |
| Файлы релиза | `<noscript>` в `index.html` ×4; `contacts/index.html`, `payment-info/index.html` ×4 (§48.28) |
| `deploy-remote.sh` | три новых precheck и смоуки (§48.30) |
| БД, миграции, `legal-drafts/`, `App_Data/legal/`, `legal.values.example.json`, схема `legal-values` | **без изменений** |

---

## §48.21. `contracts/legal/operator-public.json`

Форма — `openapi.yaml#/components/schemas/OperatorPublicRequisites` (`additionalProperties: false`).

| Ключ `values` | Общий с `legal.values.json` | Где на сайте | Ограничение |
|---|---|---|---|
| `НАИМЕНОВАНИЕ_ОПЕРАТОРА` | да | подвал, `/contacts`, `/payment-info`, `<noscript>` | только ФИО, три слова, без статуса |
| `ИНН_ОПЕРАТОРА` | да | там же | `^[0-9]{12}$` |
| `ЮРИДИЧЕСКИЙ_АДРЕС` | да | `/contacts` | непустое, без `ЗАПОЛНИТЬ` |
| `ПОЧТОВЫЙ_АДРЕС` | да | `/contacts`, `/payment-info` | то же |
| `ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ` | да | `/contacts`, `/payment-info` | e-mail |
| `ТЕЛЕФОН_ОПЕРАТОРА` | да | подвал, `/contacts`, `<noscript>` | `^\+7[ 0-9()-]{10,16}$`, видимый текст |
| `СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ` | да | `/contacts` | непустое |
| `НДС_ОГОВОРКА` | да | `/pricing` (подпись), `/payment-info` | непустое, выводится **без пересказа** |
| `ПОЧТА_ПОДДЕРЖКИ` | нет | подвал, `/contacts`, `/payment-info`, `<noscript>` | `^[^@\s]+@ezbook\.ru$` |
| `ГОРОД_ОПЕРАТОРА` | нет | подвал, `<noscript>` | `^г\. …`; город входит в `ЮРИДИЧЕСКИЙ_АДРЕС` (тест) |
| `СРОК_СЧЁТА` | нет | `/payment-info`, notice, FAQ | непустое |
| `СРОК_АКТИВАЦИИ` | нет | то же | непустое |
| `РЕЖИМ_РАБОТЫ` | нет | `/contacts` | непустое |

Производный ключ `ТЕЛЕФОН_TEL` = `'+' + (все цифры ТЕЛЕФОН_ОПЕРАТОРА)` и должен соответствовать `^\+7\d{10}$`. Значение не
хранится. Обе реализации (TS `telHref`, C# `OperatorPublicRequisites.TelHref`) выводят его одинаково.

**Сверка (ТЛ48-06).** Для каждого `k ∈ sharedWithLegalValues`: `legalValues.values[k] === site.values[k]` — ordinal, без trim.
Отсутствие ключа в `legal.values.json` тоже считается расхождением.

---

## §48.22. `contracts/cycle48/storefront-pages.json` — формат и рендер

### §48.22.1 TS-типы (frontend, `src/utils/storefrontMarkup.ts`)
```ts
export type StorefrontSite = 'zapis' | 'goods' | 'dom' | 'bani'
export type StorefrontItem = string | { text: string; sites?: StorefrontSite[] } | { paras: string[]; sites?: StorefrontSite[] }
export type StorefrontBlock =
  | { type: 'h2' | 'p'; text: string; id?: string }
  | { type: 'ul' | 'ol'; items: StorefrontItem[]; id?: string }
export interface StorefrontPage { path: '/contacts' | '/payment-info'; title: string; h1: string; blocks: StorefrontBlock[] }
export interface StorefrontPagesFile {
  contract: 'storefront-pages'; version: string
  placeholders: string[]; sites: StorefrontSite[]
  pages: { contacts: StorefrontPage; paymentInfo: StorefrontPage }
  footer: { identity: string; contacts: string }
  pricing: { notice: { zapis: string; derivedOrders: string }; termsUnderGrid: { blocks: StorefrontBlock[] }; messengerTaxNote: string }
  tariffs: Record<'zapisNetwork' | 'ordersChain' | 'zapisTrial', { description?: string; highlights: string[]; legacyDescription?: string; legacyHighlights: string[] }>
  faq: { zapisPriceAnswer: string }
}
```
JSON импортируется с приведением `as StorefrontPagesFile`. Ключи `_*` игнорируются. Тест CY48-06 проверяет, что каждый
`type` и каждый `sites` допустимы.

### §48.22.2 Мини-разметка (грамматика)
```
inline   := (text | bold | italic | link)*
bold     := '**' (text | link | italic)+ '**'
italic   := '*' text+ '*'                       (одиночная '*', не часть '**')
link     := '[' (text | bold)* ']' '(' href ')'
text     := любые символы, кроме начала конструкций; {{КЛЮЧ}} — обычный текст на этапе разбора
href     := '/' путь | 'https://' … | 'tel:' … | 'mailto:' …     (иное → StorefrontMarkupError)
```
`parseInline(src): InlineNode[]`, где `InlineNode = { kind: 'text'; value } | { kind: 'strong'; children } | { kind: 'em'; children } | { kind: 'link'; href; children }`.
Подстановка `{{КЛЮЧ}}` выполняется **после** разбора: в `value` текстовых узлов и в `href`. Функция —
`fillOperatorTemplate`, она бросает исключение на ключ вне `placeholders`. Незакрытая конструкция — ошибка разбора
(исключение). Молча выводить сырой текст нельзя: тест обязан упасть.

### §48.22.3 Рендер
```tsx
type LinkMode = 'router' | 'plain' | { origin: string }
<StorefrontBlocks blocks={StorefrontBlock[]} site={StorefrontSite} linkMode={LinkMode} />
<StorefrontInline source={string} site={StorefrontSite} linkMode={LinkMode} />   // одна строка (подвал, notice-подобные места)
```

| Узел | Разметка |
|---|---|
| `h2` | `<h2 className="font-serif text-[22px] font-medium text-ink mt-10 mb-3">` |
| `p` | `<p className="text-[15px] leading-[1.65] text-ink-soft mb-3">` |
| `ul` | `<ul className="list-disc pl-5 space-y-2 text-[15px] leading-[1.65] text-ink-soft mb-3">` |
| `ol` | `<ol className="list-decimal …">` |
| элемент `{paras}` | `<li>` с `<p>` внутри (у второго и далее `mt-2`) |
| `strong` | `<strong className="font-semibold text-ink">` |
| `em` | `<em>` |

Ссылки:
- `/…`: при `'router'` — `<Link to={resolveStorefrontHref(site, href, siteUrls)}>`; если результат абсолютный, то `<a href>`. При `'plain'` — `<a href>`. При `{origin}` — `<a href={origin + href}>`.
- `https://…` — `<a href rel="noopener noreferrer">`.
- `tel:`/`mailto:` — `<a href>`.

Класс ссылок: `text-ink underline underline-offset-2 hover:no-underline`. `siteUrls` = `resolveSiteUrls(import.meta.env, mode)`
из `publicSites.ts` (тот же вызов, что в `ProductsPageLink`).

`resolveStorefrontHref(site, href, siteUrls)`: если `href === '/pricing'` и `site ∈ {dom, bani}`, вернуть
`${siteUrls.zapis}/pricing`. Иначе вернуть `href` без изменений.

Элемент с `sites` выводится, только если `site ∈ sites`.

### §48.22.4 Нормализация для сверки с правовым заключением (CY48-05)
Функция одна, ей обрабатываются обе стороны (JSON-строка и весь `LEGAL_REVIEW_CYCLE48.md`):
1. Убрать маркеры цитаты в начале строк: `/^\s*>\s?/gm` → `''`.
2. Ссылки → текст: `/\[([^\]]*)\]\([^)]*\)/g` → `'$1'`.
3. Убрать маркер `[*] `: `/\[\*\]\s/g` → `''`.
4. Убрать обратные кавычки: `` /`/g `` → `''`.
5. Схлопнуть пробелы: `/\s+/g` → `' '`, затем trim.

Проверка: `normLegal.includes(normJsonString)`. Охват — все строки файла, кроме ключей, начинающихся с `_`, `legacy`, `derived`,
и кроме `sites`, `type`, `id`, `path`, `contract`, `version`, `placeholders`, `markup`, `source`. У `tariffs.*.highlights`
проверяется только последний пункт: пункты 1–3 не менялись и в заключении упомянуты не все.

### §48.22.5 Что где выводится

| Ключ JSON | Где | Кто рендерит |
|---|---|---|
| `pages.contacts`, `pages.paymentInfo` | `/contacts`, `/payment-info` на 4 сайтах, пререндер | `ContactsPage` / `PaymentInfoPage` |
| `footer.identity`, `footer.contacts` | подвал 4 сайтов и about, `<noscript>` | `OperatorRequisitesLine` |
| `pages.contacts.blocks[id=documents]` | `<noscript>` | `OperatorNoscriptContent` |
| `pricing.termsUnderGrid` | под сеткой `/pricing` ezbook и goods | `PricingTermsNote` |
| `pricing.notice.*`, `pricing.messengerTaxNote` | API (бэкенд-константы) | — (сверка CY48-B2/B3) |
| `tariffs.*` | БД через сидер | — (сверка CY48-B4) |
| `faq.zapisPriceAnswer` | `contracts/cycle46/zapis-landing.json` `faq[0].answer`, `zapisFaq.ts` | `FaqSection` (без изменений) |

---

## §48.23. Фронтенд-модули: сигнатуры

```ts
// src/utils/operatorRequisites.ts (чистый; импортируется приложениями, about и SSR)
export type OperatorKey = 'НАИМЕНОВАНИЕ_ОПЕРАТОРА' | 'ИНН_ОПЕРАТОРА' | /* … все 13 … */ 'РЕЖИМ_РАБОТЫ'
export const OPERATOR: Readonly<Record<OperatorKey, string>>
export function telHref(): string                                   // 'tel:+79132258915'
export class OperatorTemplateError extends Error {}
export function fillOperatorTemplate(template: string): string      // {{KEY}} и {{ТЕЛЕФОН_TEL}}; иначе throw
export function resolveStorefrontHref(site: StorefrontSite, href: string, siteUrls: SiteUrls): string

// src/components/sites/OperatorRequisites.tsx
export function OperatorRequisitesLine(props: { linkMode: LinkMode; site?: StorefrontSite }): JSX.Element
//   <div className="flex flex-col items-center gap-1 text-center" data-testid="operator-requisites">
//     <p>{identity}</p><p>{contacts}</p></div>
//   ИНН и телефон обёрнуты в <span className="whitespace-nowrap">; ссылки inline-flex min-h-[24px] items-center
export function OperatorNoscriptContent(props: { site: StorefrontSite }): JSX.Element   // §48.10.1, linkMode 'plain'

// src/pages/ContactsPage.tsx, src/pages/PaymentInfoPage.tsx
export function ContactsPage(props: { site: StorefrontSite }): JSX.Element
export function PaymentInfoPage(props: { site: StorefrontSite }): JSX.Element

// src/components/pricing/PricingTermsNote.tsx
export function PricingTermsNote(props: { site: 'zapis' | 'goods' }): JSX.Element
//   PricingPageBody получает проп site (ezbook PricingPage → 'zapis', goods PricingPage → 'goods')

// src/storefront/prerender.tsx (вход SSR-сборки, в приложения не импортируется)
export function renderNoscript(site: StorefrontSite): string
export function renderPage(site: StorefrontSite, page: 'contacts' | 'paymentInfo'): { title: string; html: string }
```

`frontend/shared-sources.js`:
```js
export const SHARED_EZBOOK_PAGES = [ …прежние…, 'ContactsPage', 'PaymentInfoPage' ]
export const ABOUT_SHARED_SOURCES = [ …прежние…, 'src/utils/operatorRequisites.ts', 'src/components/sites/OperatorRequisites.tsx' ]
```
Если `OperatorRequisites.tsx` транзитивно тянет `storefrontMarkup.ts`/`StorefrontBlocks.tsx`, их тоже нужно добавить в
`ABOUT_SHARED_SOURCES`: guard `about.guard` проверяет только `@/…`-импорты самого about. Для Tailwind важны все файлы с
классами. Окончательный список определяет FE-48-1: guard подскажет.

---

## §48.24. HTTP: значения текстовых полей

### §48.24.1 `GET /api/pricing` (анонимно, кеш 60 с, ETag)
- `notice` = `fillOperator(storefront-pages.json pricing.notice.zapis)`. Pattern — `PricingNoticeZapis`.
- `messengerAddonsNote.taxNote` (если `messengerAddonsNote != null`) = `"Без НДС: оператор применяет налог на профессиональный доход"`.
- `plans[]` после `ops tariffs apply` на чистой базе:
  - «Сеть»: `description` = `tariffs.zapisNetwork.description`, `highlights` = `tariffs.zapisNetwork.highlights`;
  - «Пробный период»: `highlights` = `tariffs.zapisTrial.highlights` (4 пункта).
- `plans[].description`, `plans[].highlights[]` для **любого** тарифа соответствуют `TariffSafeText` (якорь `tariffSafe`).
- `legalNotice`, `version`, `currency`, `options`, `messengerAddons` и коды 200/304/404 — без изменений.

### §48.24.2 `GET /api/pricing/orders`
- `notice` = `fillOperator(pricing.notice.derivedOrders)`. Pattern — `PricingNoticeOrders`.
- «Сеть магазинов»: `description` = `tariffs.ordersChain.description`, `highlights` = `tariffs.ordersChain.highlights` (3 пункта).
- Остальное без изменений.

### §48.24.3 `GET /api/billing/subscription?line=…`
- `messengerAddonsNote.taxNote` = та же константа, для любой линейки. Остальное без изменений.

### §48.24.4 Бэкенд-константы и сигнатуры
```csharp
// Services/Legal/OperatorPublicRequisites.cs
public static class OperatorPublicRequisites
{
    public const string ResourceName = "operator-public.json";
    public static IReadOnlyDictionary<string, string> Values { get; }               // 13 ключей
    public static IReadOnlyList<string> SharedWithLegalValues { get; }               // 8 ключей
    public static string Get(string key);                                            // KeyNotFoundException
    public static string TelHref();                                                  // "tel:+79132258915"
    public static string Fill(string template);                                      // {{KEY}}, {{ТЕЛЕФОН_TEL}}; неизвестный → InvalidOperationException
}
// PricingCatalogBuilder
public const string NoticeTemplate = "…";   // == storefront-pages.json pricing.notice.zapis
public static string DefaultNotice => OperatorPublicRequisites.Fill(NoticeTemplate);
public const string MessengerTaxNote = "Без НДС: оператор применяет налог на профессиональный доход";
public static PublicPricingDto Build(string version, IEnumerable<SubscriptionPlanConfig> plans, IEnumerable<SubscriptionOption> options,
    string? legalNotice, string? notice = null, LegalSnapshot? legalSnapshot = null);
// OrdersPricingCatalogBuilder
public const string NoticeTemplate = "…";   // == pricing.notice.derivedOrders
public static string DefaultNotice => OperatorPublicRequisites.Fill(NoticeTemplate);
public static OrdersPublicPricingDto Build(string version, IEnumerable<SubscriptionPlanConfig> plans, int productsCeiling,
    string? legalNotice, string? notice = null);
// PricingCatalogCache.BuildAsync:   messengerAddons.BuildAsync(PricingCatalogBuilder.MessengerTaxNote, ct)
// OwnerSubscriptionService (стр. 155): messengerAddons.BuildAsync(PricingCatalogBuilder.MessengerTaxNote)
```

---

## §48.25. «Сигнал оператору» о новой заявке (US-48-08)

| Триггер | Условие | Сообщение (`IGlitchTipSignalService.SendAsync(message, CancellationToken.None)`) |
|---|---|---|
| `POST /api/billing/subscription/request` | ответ 200 (после `transaction.CommitAsync()`) | `Новая заявка на подписку: линейка={Services\|Orders\|Stays\|Baths}, тариф=«{имя тарифа}»\|тариф=без смены, опций={N}, аккаунт={первые 8 hex Id}, счёт выставить до {дд.мм.гггг}` |
| `POST /api/notification-channels` | `dto.paymentRequest == true` и ответ 200/201 (после коммита) | `Новая заявка на опцию: мессенджер={WhatsApp\|MAX}, аккаунт={первые 8 hex Id}, счёт выставить до {дд.мм.гггг}` |

- `счёт выставить до` = `WorkingDays.Add(today(Asia/Barnaul), 1)`: «не позднее следующего рабочего дня после дня подачи». Праздники не учитываются: срок получается раньше, то есть строже.
- Запрещено в сообщении: телефон, ФИО, e-mail, комментарий заявки, ИНН, название компании.
- Отправка не ждёт ответа (fire-and-forget). Любое исключение логируется (`LogWarning`) и не влияет на ответ. Ответы обоих эндпоинтов — байт в байт как до цикла.
- На демо-стенде эндпоинт заявки закрыт `[DemoForbidden]`, сигнала нет. При пустом `Sentry:Dsn` сервис ничего не делает, как и сейчас.

---

## §48.26. `ops tariffs plan|apply` — новые строки отчёта

Новые строки добавляются к существующим, прежние не меняются:
```
«Сеть»: тексты (решение 25.10.2026): будет изменено — описание обновлено; преимущества обновлены      (plan)
«Сеть»: тексты (решение 25.10.2026): изменено — описание обновлено; преимущества обновлены            (apply)
«Сеть»: тексты уже новые или изменены в админке — не трогаю
«Пробный период»: пункт о сроке сообщений (п. 6.13.17): будет изменено|изменено — преимущества обновлены
«Заказы»: «Сеть магазинов»: тексты (решение 26.10.2026): будет изменено|изменено — описание обновлено; преимущества обновлены
внимание: «<имя>»: в описании осталось «<совпадение>» (поле правили в админке) — поправьте в админке, текст — contracts/cycle48/storefront-pages.json
внимание: «<имя>»: в преимуществах осталось «<совпадение>» (поле правили в админке) — поправьте в админке, текст — contracts/cycle48/storefront-pages.json
```
`TariffCatalogReport` не меняет форму. Выровненные поля платных тарифов **не** прибавляются к `FreeFieldsAligned` /
`OrdersFreeFieldsAligned`: эти счётчики описывают бесплатный тариф. Новые строки — только в `Lines`. Коды выхода `ops` прежние.

---

## §48.27. Маршруты SPA и резерв адресов — точные правки

### §48.27.1 `contracts/cycle11/legal-routes.json`
```json
"aliases": {
  "/offer-channel": "/terms-owner#offer-channel",
  "/payment-terms": "/payment-info",
  "/data-request": "/data-request",
  "/contacts": "/contacts",
  "/payment-info": "/payment-info"
},
"anchorsPending": { "/terms-owner": ["payment-terms"] },          // без изменений
"allowedTargets": ["/privacy", "/terms", "/terms-owner", "/pdn-consent", "/channel-risk", "/offer-channel", "/payment-terms", "/data-request", "/contacts", "/payment-info"]
```
В `_aliasesComment` дописать: «Цикл 48: `/payment-terms` до этапа B ведёт на страницу-пересказ `/payment-info` (не документ). При
подключении Приложения № 2 вернуть `"/terms-owner#payment-terms"` и перенести якорь из `anchorsPending` в `anchors`. `/contacts`,
`/payment-info` — обычные маршруты фронта, самоотображены по образцу `/data-request`.» `ServiceBooking.API/Services/Legal/LegalRoutes.cs`
`Aliases` — те же 5 записей.

### §48.27.2 `goods-routes.json`, `cycle39/dom-routes.json`, `cycle42/bani-routes.json`
- `spaRoutes`: добавить `"/contacts"`, `"/payment-info"` (после `"/payment-terms"`).
- `reservedSlugs`: добавить `"payment-info"` в алфавитном порядке, после `"payment-terms"`. `"contacts"` уже есть.
- В `_reservedComment` каждого файла дописать «цикл 48: payment-info».

### §48.27.3 Приложения
Во всех четырёх `*App.tsx`:
- `CONSENT_GATE_BYPASS_PATHS` += `'/contacts'`, `'/payment-info'`;
- маршруты — ARCHITECTURE §48.7.2;
- `/payment-terms` → `<Navigate to="/payment-info" replace />`.

В goods, dom и bani оба новых маршрута объявляются до `/:slug`, порядок `path="…"` в исходнике проверяют route-тесты.

### §48.27.4 Поведение
| Запрос | Ответ |
|---|---|
| `https://{ezbook\|goods\|dom\|bani}/contacts` | страница «Контакты и реквизиты» своего сайта; без входа; при неподтверждённой редакции — без гейта |
| `…/payment-info` | страница «Оплата и возврат» |
| `…/payment-terms` | клиентский редирект на `/payment-info` (replace) |
| создать магазин/«Дом»/«Баню» с адресом `payment-info` | прежний отказ «адрес зарезервирован» (форма и текст ошибки — как у остальных резервных слов, цикл 23/37/42) |

---

## §48.28. Статические файлы релиза

### §48.28.1 Маркер и `<noscript>`
В исходных `index.html` четырёх приложений сразу после `<body>` стоит маркер `<!--storefront:noscript-->`. В релизном `index.html`
(и в пререндеренных файлах) он заменяется на:
```html
<noscript><div class="mx-auto max-w-[760px] px-4 py-6 text-sm text-ink-soft" data-operator-requisites="noscript">
  <p>{НАИМЕНОВАНИЕ_ОПЕРАТОРА}, самозанятый (плательщик НПД), ИНН <span class="whitespace-nowrap">{ИНН_ОПЕРАТОРА}</span>, {ГОРОД_ОПЕРАТОРА}</p>
  <p><a href="tel:{ТЕЛЕФОН_TEL}"><span class="whitespace-nowrap">{ТЕЛЕФОН_ОПЕРАТОРА}</span></a> · <a href="mailto:{ПОЧТА_ПОДДЕРЖКИ}">{ПОЧТА_ПОДДЕРЖКИ}</a> · <a href="/contacts">Контакты и реквизиты</a> · <a href="/payment-info">Оплата и возврат</a></p>
  <ul><li><a href="/terms-owner">Соглашение с компанией</a> — публичная оферта для владельцев бизнеса</li> … </ul>
</div></noscript>
```
Пример выше иллюстративный: разметку даёт `renderNoscript(site)`, значения — из источника. На dom и bani в списке нет пункта
«Тарифы и цены». Классы стоят в атрибуте `class`, а не `style`. Если CSS не загрузился, текст остаётся читаемым.

### §48.28.2 Пререндер
| Файл в `dist*/` | Шаблон | `<title>` | `<div id="root">…</div>` |
|---|---|---|---|
| `contacts/index.html` | `index.html` своего приложения (уже с `<noscript>`) | `Контакты и реквизиты — EZBOOK` | `renderPage(site,'contacts').html` |
| `payment-info/index.html` | то же | `Оплата и возврат — EZBOOK` | `renderPage(site,'paymentInfo').html` |

Префикс `<div id="root">` в файле сохраняется буквально: на него опирается смоук `deploy-remote.sh`. Скрипты, CSS-ссылки и `<head>`
шаблона не трогаются, кроме `<title>`.

### §48.28.3 Самопроверки `scripts/prerender-storefront.mjs`
Код выхода 0, или 1 с `prerender-storefront: <причина>` в stderr. Проверки ARCHITECTURE §48.10.2 п. 3. Выход по
файлам: `prerender-storefront: wrote dist/{index.html,contacts/index.html,payment-info/index.html}, dist-goods/…, dist-dom/…, dist-bani/…`.

### §48.28.4 nginx
Ожидаемое поведение (проверяет CY48-D3):
- `GET /contacts` и `GET /contacts/` → 200 с телом `contacts/index.html`;
- `Cache-Control: no-cache`, CSP и HSTS — как у `location /`.

Допустимая альтернатива: ровно один 301 с `/contacts` на `/contacts/` того же хоста. Vhost'ы не меняются.

---

## §48.29. LegalKit: сверка реквизитов

### §48.29.1 `RequisitesDrift` (чистый)
```csharp
internal static class RequisitesDrift
{
    public sealed record Difference(string Key, string? LegalValue, string SiteValue);
    public static IReadOnlyList<Difference> Compare(IReadOnlyDictionary<string, string> legalValues,
                                                    IReadOnlyDictionary<string, string> siteValues,
                                                    IReadOnlyList<string> sharedKeys);
}
```

### §48.29.2 `legal publish`
После загрузки `--values`, до чтения источника, `Compare(values.Values, OperatorPublicRequisites.Values,
OperatorPublicRequisites.SharedWithLegalValues)`. Каждое расхождение — строка в общем списке отказа, код выхода **5** (как у
прочих отказов). На диск ничего не пишется. Текст строки:
```
Реквизит {KEY} на сайте (contracts/legal/operator-public.json) отличается от legal.values.json: на сайте «{site}», в файле значений «{legal|— нет —}». Публиковать документы с другими реквизитами, чем на сайте, нельзя (ТЛ48-06): выровняйте оба места и выкатите релиз, затем повторите publish (DEPLOY.md §35.3).
```
Флага обхода нет.

### §48.29.3 `legal status`
Новый необязательный аргумент `--values <path>`. Если он передан, печатается блок:
```
Реквизиты сайта (contracts/legal/operator-public.json) против legal.values.json:
  ИНН_ОПЕРАТОРА: совпадает
  ЮРИДИЧЕСКИЙ_АДРЕС: РАСХОДИТСЯ — на сайте «…», в файле «…»
```
Код выхода `status` от этого блока не зависит.

---

## §48.30. Выкат: precheck, гейты, смоуки, DEPLOY §35

### §48.30.1 `deploy-remote.sh` — до любых изменений на хосте (после `check_legal_manifest`, до `check_retired_limit_options`)
| Шаг | Вход | Отказ (`exit 1`, «Nothing has been changed on this host yet — no rollback needed») |
|---|---|---|
| `check_operator_requisites` (python3) | `contracts/legal/operator-public.json` (чекаут релиза), `legal.values.json` (хост) | битый JSON; значение содержит `ЗАПОЛНИТЬ`; расхождение по ключу из `sharedWithLegalValues` (печатает ключ и оба значения). Нет `legal.values.json` → WARNING, шаг пропущен. Нет `python3` → WARNING, как у `check_legal_manifest` |
| `check_support_mailbox` | `.env` хоста: `SUPPORT_MAILBOX_VERIFIED` (через `env_file_value`) | значение ≠ `true`: «support@ezbook.ru ещё не проверен (M48-05). После проверки допишите SUPPORT_MAILBOX_VERIFIED=true в .env — DEPLOY.md §35.2» |
| `check_cycle48_slugs` | `deploy/checks/cycle48-storefront-report.sql` (отчёт в лог), `deploy/checks/cycle48-slugs-live.sql` (строки = стоп) | psql-ошибка (fail-closed); есть строки — «адрес компании совпадает с новым маршрутом /contacts или /payment-info — переименуйте адрес в админке, DEPLOY.md §35.2». Postgres не запущен → SKIPPED (первый выкат) |

### §48.30.2 После readiness (смоуки, ошибка → `rollback_hint`, `exit 1`)
- ezbook: `curl -sf https://127.0.0.1/` с `--resolve` на хост ezbook. Тело содержит `<noscript` и значение ИНН. `GET /contacts` → 200 и `Контакты и реквизиты`.
- goods: то же внутри существующего блока goods-смоука.
- dom и bani: то же внутри своих блоков, только если vhost установлен.
- ИНН берётся из `contracts/legal/operator-public.json` чекаута (python3 или `grep -o`), а не литералом в скрипте.

### §48.30.3 SQL
- `cycle48-storefront-report.sql`: 6 блоков ARCHITECTURE §48.14.3, только `SELECT`, `ON_ERROR_STOP=1`.
- `cycle48-slugs-live.sql`: `SELECT "Id", "Kind", "Slug" FROM "Companies" WHERE "Kind" <> 0 AND lower("Slug") IN ('contacts', 'payment-info')`. Имена колонок DO сверяет по снапшоту миграций. Пустой результат — норма.

### §48.30.4 DEPLOY.md §35 (структура — ARCHITECTURE §48.18.1). Обязательные шаги заказчика по порядку
1. Ящик `support@ezbook.ru` (VK WorkSpace, MX/SPF/DKIM в reg.ru), проверка M48-05, затем `SUPPORT_MAILBOX_VERIFIED=true` в `/opt/ezbook/app/.env`.
2. GlitchTip: проект API, алерт на новые issue. Проверка M48-11 сразу после выката.
3. Выкат: штатный `deploy`. Прочитать отчёт cycle48 в логе.
4. `docker compose -f docker-compose.prod.yml exec -T api dotnet ServiceBooking.API.dll ops tariffs plan`, прочитать раздел «Сеть» и «внимание:», затем `… ops tariffs apply`.
5. Админка:
   - `pricing.public-enabled` включён;
   - окно рассылок пробного периода = 7 (иначе поправить 4-й пункт «Пробного периода»);
   - абоненты «Сети» по опубликованной цене (ТЛ48-10).
6. `ACQUIRER_CHECKLIST.md` целиком.

---

## §48.31. Как проверить контракт (QA)

```bash
# форма контракта
cd frontend && npx @redocly/cli lint --config ../contracts/redocly.yaml ../contracts/cycle48/openapi.yaml
npm run contracts:json && git diff --exit-code -- ../contracts/cycle48/openapi.json

# бэкенд сошёлся с контрактом (ответы валидируются по contracts/cycle48/openapi.json, источник реквизитов — по схеме)
dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~Cycle48"
dotnet test ServiceBooking.UnitTests --filter "FullyQualifiedName~Cycle48|FullyQualifiedName~LegalRoutes|FullyQualifiedName~SlugPolicy|FullyQualifiedName~RequisitesDrift"

# фронт сошёлся с контрактом (тексты = storefront-pages.json = LEGAL_REVIEW, маршруты = *-routes.json, единый источник, запреты)
cd frontend && npx vitest run src/components/storefront src/utils/operatorRequisites src/utils/storefrontMarkup src/pages src/components/layout \
  src/components/pricing src/test goods/src dom/src bani/src about/src src/storefront

# статика релиза
cd frontend && npm run build:release
DIST_DIR=frontend/dist bash deploy/ci/smoke-frontend.sh                                  # + SMOKE_PROFILE=goods|dom|bani
bash deploy/nginx/test-storefront-pages.sh
bash deploy/ci/test-requisites-precheck.sh

# по желанию: генеративная проверка живого API по схеме (после ops tariffs apply и pricing.public-enabled=true)
schemathesis run contracts/cycle48/openapi.yaml --base-url http://localhost:5000 --checks all
```
Без браузера не проверить: вёрстку на 375/1280 (M48-02), реальную доставку почты (M48-05) и сигнала (M48-11).
