# ARCHITECTURE — цикл 48 ServiceBooking: витрина для эквайера (этап A брифа)

**Дата: 2026-10-26. Ветка `cycle/048-card-payments-storefront` (от `develop` @ `5bcc4a19`, документы — `e194d6f`).** Разделы §48.0–§48.18.
Вводные: `SPEC.md` (цикл 48, включая §0 «Решения заказчика» и §11 «Дополнение», US-48-01…08), `LEGAL_REVIEW_CYCLE48.md` (тексты и
ТЛ48-01…11 — часть приёмки), `BRIEF_CARD_PAYMENTS.md` §0–§3, §5, `CURRENT_STATE.md` (`5bcc4a19`). Контракт —
`API_CONTRACT_CYCLE48_STOREFRONT.md` (§48.20–§48.31). Машиночитаемо:
- `contracts/cycle48/openapi.yaml` — HTTP-дельта (только значения текстовых полей) и схема источника реквизитов;
- `contracts/cycle48/storefront-pages.json` — тексты `/contacts`, `/payment-info`, подвала, подписи под ценами, notice, «Сети», FAQ — дословно из правового заключения; фронт рендерит прямо из него;
- `contracts/cycle48/forbidden-phrases.json` — один список запрещённых фраз для C# и TS;
- `contracts/legal/operator-public.json` — **единый источник** публичных реквизитов (4 значения заполняет DO-48-1).

Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` не трогаются: по конвенции проекта (CURRENT_STATE §10.5) документы цикла лежат в корне
под суффиксом. Факты о коде сверены по файлам на `e194d6f` (код = `5bcc4a19`).

**Коротко.** Почти весь цикл — фронтенд и данные. Схема БД, миграции и форма HTTP API не меняются. Бэкенд меняет значения трёх
текстовых полей витрин цен, тексты трёх тарифов (через сидер, без миграции), шлёт «сигнал оператору» о новой заявке и
сверяет реквизиты при публикации документов. На четырёх сайтах появляются общий подвал с реквизитами, страницы `/contacts` и
`/payment-info` без запросов к API, `<noscript>` и пререндер двух новых страниц. Оплаты картой нет (этап B вне скоупа).

---

## §48.0. Решения одним экраном

| # | Вопрос | Решение | Где |
|---|---|---|---|
| A48-1 | Единый источник реквизитов и сверка с `legal.values.json` | Файл `contracts/legal/operator-public.json`: 8 ключей, общих с `legal.values.json` (имена те же), и 5 ключей только для сайта (ТЛ48-06). Его читают фронт (импорт JSON при сборке), API (встроенный ресурс) и LegalKit. Сверка «целиком, только общие ключи» делается в двух местах: **(а)** precheck в `deploy-remote.sh` до любых изменений на машине (файл релиза против `legal.values.json` хоста), **(б)** `legal publish` отказывает (код 5), если значения расходятся со встроенным в образ файлом. Ни выкат, ни публикация с расхождением не проходят | §48.3 |
| A48-2 | Где живёт текст `/payment-info` | **Вариант 1** (как рекомендует L48-4): статично во фронте, из `storefront-pages.json`. Не документ LegalKit, без версии и акцепта, без API | §48.5 |
| A48-3 | Одна реализация на четыре приложения | Данные и форматирование — `src/utils/operatorRequisites.ts`. Разметка подвала — `src/components/sites/OperatorRequisites.tsx`. Обе папки вторичным приложениям уже разрешены (`SCANNED_EZBOOK_DIRS`). Страницы — `src/pages/ContactsPage.tsx`, `PaymentInfoPage.tsx`, добавляются в `SHARED_EZBOOK_PAGES`. Сайт-специфика — один проп `site` | §48.4 |
| A48-4 | Маршруты против адресов компаний | `contacts` уже зарезервирован во всех трёх картах адресов. `payment-info` добавляется в `reservedSlugs` goods/dom/bani, оба пути — в `spaRoutes`. В `legal-routes.json` появляются два «самоотображённых» алиаса, как `/data-request`: существующие тесты четырёх приложений сами потребуют `<Route>` и запись в `CONSENT_GATE_BYPASS_PATHS`. `/payment-terms` → `/payment-info`. У салонов адрес `/company/:slug`, конфликта нет. Боевые адреса проверяет SQL-стоп перед выкатом | §48.7 |
| A48-5 | Пререндер и `/pricing` | Одна SSR-сборка (`vite.storefront.config.ts`), скрипт `prerender-storefront.mjs` пишет в `dist*` четырёх приложений `contacts/index.html` и `payment-info/index.html` (шаблон — собственный `index.html` приложения) и подставляет `<noscript>`. nginx не меняется: `try_files $uri $uri/ /index.html` уже отдаёт `<path>/index.html` с теми же заголовками `no-cache`. Клиент — прежний `createRoot().render` (без гидратации: React заменяет готовую разметку). `/pricing` **не пререндерится**: цены в БД, снимок устареет. Модератор без JS видит `<noscript>` со ссылками | §48.10 |
| A48-6 | Тексты «Сети» без миграции | По образцу `AlignFreeTariff`: поле меняется, только пока оно байт в байт равно прежнему сиду. Сидер обрабатывает «Сеть», «Сеть магазинов» и (ТЛ48-07) «Пробный период». `ops tariffs plan` предупреждает, если в тексте осталась запрещённая фраза. Порядок на бою: `plan` → `apply` → M48-07 | §48.9 |
| A48-7 | Источник налоговой оговорки на `/pricing` | `НДС_ОГОВОРКА` из единого источника. Фронт выводит её статично под сеткой вместе с формулой и ссылками (ТЛ48-02). Значение совпадает с опубликованным, это гарантирует сверка A48-1. `pricing.legal-notice` и эндпоинт его записи **не нужны** (долг C28-2 закрывается как ненужный). Поле `legalNotice` в API не трогаем, фронт выводит его как раньше, на бою оно `null` (отчёт DO-48-3) | §48.8 |
| A48-8 | Подвал `about.ezbook.ru` | Делается. `about` импортирует те же два модуля: в `ABOUT_SHARED_SOURCES` добавляются `src/utils/operatorRequisites.ts` и `src/components/sites/OperatorRequisites.tsx`. Ссылки абсолютные, на сайт «Записи». Страница остаётся без клиентского JS, CSP не меняется | §48.6.4 |
| ТЛ48-03 | `taxNote` строк мессенджеров | Константа `PricingCatalogBuilder.MessengerTaxNote` (= `storefront-pages.json` `pricing.messengerTaxNote`) передаётся в `MessengerAddonsProvider.BuildAsync` из `PricingCatalogCache` **и** `OwnerSubscriptionService`. `notice` туда больше не попадает | §48.8.3 |
| ТЛ48-07 | Пробный период и п. 6.13.17 | Сообщения в пробном периоде **возможны**: `TrialOptionGrantRule` выдаёт открытые опции каналов, окно рассылок — `PlatformSettings.TrialMailingWindowDays`, по умолчанию 7. Поэтому у «Пробного периода» появляется 4-й пункт «Отправка сообщений клиентам — не дольше 7 дней из 14», выравнивается сидером. DEPLOY проверяет, что на бою окно = 7 | §48.9 |
| ТЛ48-08 (US-48-08) | Оператор узнаёт о заявке в тот же день | Существующий «сигнал оператору» `IGlitchTipSignalService` (им уже пользуются обращения субъектов ПДн): после коммита заявки на тариф (`POST /api/billing/subscription/request`) и заявки на оплату номера (`POST /api/notification-channels`, `paymentRequest: true`). Отправка в фоне, ответ не меняется, сбой не мешает заявке. Без новых сервисов и миграций | §48.11 |
| R48-3 | `support@` до проверки ящика | Механический гейт: `deploy-remote.sh` отказывает в выкате, пока в `.env` хоста нет `SUPPORT_MAILBOX_VERIFIED=true`. Заказчик ставит строку после M48-05 | §48.18 |

---

## §48.1. Стек: без изменений

Бэкенд: .NET 8, ASP.NET Core MVC, EF Core 8 + PostgreSQL 16. Фронтенд: React 18, TypeScript, Vite 5, Tailwind 3.4, vitest.
**Новых зависимостей npm и NuGet нет**:
- пререндер собирается уже имеющимися `react-dom/server` (`renderToStaticMarkup`) и `react-router-dom/server` (`StaticRouter`, есть в 6.30), как у `about`;
- сверка в deploy-precheck — `python3`, который уже нужен `check_legal_manifest`.

Хостинг, nginx-vhost'ы, CSP, порядок выката не меняются.

Почему не новый статический сайт или CMS для «Контактов» и «Оплаты»: страницы должны быть на каждом из четырёх доменов
(правило Р7), с теми же шапкой и подвалом, и не зависеть от API. Тот же SPA-маршрут плюс готовый HTML со сборки решает все
три требования без новой инфраструктуры. Для эквайера и будущих платёжных провайдеров это также самый дешёвый в эксплуатации
вариант: тексты — файл в git, проверка — автотесты.

---

## §48.2. Модель данных

**БД, сущности EF и миграции — без изменений** (ломающие миграции запрещены, добавочные не нужны). Меняются только значения
строк `SubscriptionPlanConfigs.Description/Highlights` у трёх тарифов и только через сидер по правилу «поле равно прежнему сиду»
(§48.9).

«Модель» цикла — три файла данных в git:

```
contracts/legal/operator-public.json      ← единый источник реквизитов (форма — openapi.yaml#/components/schemas/OperatorPublicRequisites)
  values{13 ключей}                          8 общих с legal.values.json  +  5 только для сайта
  sharedWithLegalValues[8]                   по ним — сверка целиком (ТЛ48-06)
  derived.ТЕЛЕФОН_TEL                        не хранится: '+' + цифры ТЕЛЕФОН_ОПЕРАТОРА

contracts/cycle48/storefront-pages.json   ← тексты (шаблоны с {{КЛЮЧ}}) и структура блоков
  pages.contacts / pages.paymentInfo         title, h1, blocks[h2|p|ul|ol]
  footer.identity / footer.contacts          строка подвала §6.3
  pricing.notice / termsUnderGrid / messengerTaxNote
  tariffs.zapisNetwork / ordersChain / zapisTrial   (новые и legacy-значения для сидера)
  faq.zapisPriceAnswer

contracts/cycle48/forbidden-phrases.json  ← регулярки запрещённых фраз, scope tariffTexts | publicCopy
```

Инварианты, которые проверяют тесты (не типы):
1. В шаблонах только ключи из `storefront-pages.json` `placeholders`. После подстановки нет `{{`/`}}` (CY48-06, CY48-B2).
2. Ни одно значение `operator-public.json` не пустое и не содержит `ЗАПОЛНИТЬ` (CY48-B8). `ГОРОД_ОПЕРАТОРА` без «г. » входит в `ЮРИДИЧЕСКИЙ_АДРЕС`.
3. `ТЕЛЕФОН_TEL` соответствует `^\+7\d{10}$`.
4. Литералы значений (ИНН, цифры телефона, адреса почты, фамилия) не встречаются ни в одном исходнике `src/`, `goods/src`, `dom/src`, `bani/src`, `about/src`, кроме тестов (CY48-02). Значение меняется в одном месте.

---

## §48.3. Единый источник реквизитов (A48-1, US-48-01, ТЛ48-06)

### §48.3.1 Почему файл в репозитории, а не переменные сборки или API
- **Не API.** `/contacts` и `/payment-info` обязаны показываться при недоступном API (US-48-02), а `<noscript>` и пререндер строятся на сборке.
- **Не GitHub Variables.** Тогда значение жило бы в третьем месте, не проверялось бы тестами, а CI-сборка зависела бы от ручной настройки. Решение Q48-1 разрешает хранить публичные реквизиты в git.
- **Не копия `legal.values.json`.** У сайта 5 своих ключей, которых нет в схеме `legal.values` (`additionalProperties: false`, менять её в этом цикле нельзя — L48-3). Кроме того, ключи `ОТВЕТСТВЕННЫЙ_ЗА_ОБРАБОТКУ` и `ПОЧТА_ОТВЕТСТВЕННОГО` сайту не нужны.

### §48.3.2 Кто читает

| Потребитель | Как | Что берёт |
|---|---|---|
| Фронт (4 SPA + about, SSR-сборка) | `import data from '../../../contracts/legal/operator-public.json'` в `src/utils/operatorRequisites.ts` (такой импорт уже есть: `src/utils/slots/slug.ts`) | всё |
| API | `<EmbeddedResource Include="..\contracts\legal\operator-public.json" LogicalName="operator-public.json" />` (как `goods-routes.json`); класс `Services/Legal/OperatorPublicRequisites.cs` | `СРОК_СЧЁТА`, `СРОК_АКТИВАЦИИ` для notice, плюс Fill для тестов |
| LegalKit (`publish`, `status`) | тот же класс через ссылку на сборку API | 8 общих ключей |
| `deploy-remote.sh` | файл в чекауте релиза на машине | 8 общих ключей |

### §48.3.3 Сверка с `legal.values.json`
Правило одно для обеих точек. Для каждого ключа из `sharedWithLegalValues` значение в `legal.values.json` → `values[key]`
должно быть **строго равно** (ordinal, без trim и нормализации) значению в `operator-public.json`. Ключи сравниваются только общие
и только целиком. Пример: если `НАИМЕНОВАНИЕ_ОПЕРАТОРА` на бою содержит статус, это расхождение, и его нужно устранить, а не
обойти (LEGAL §6.3, последний пункт).

1. **Precheck выката** (`deploy-remote.sh`, функция `check_operator_requisites`, DO-48-3): выполняется рядом с `check_legal_manifest`, до переключения симлинка. Расхождение, маркер `ЗАПОЛНИТЬ` или битый JSON → `exit 1` («Nothing has been changed on this host yet — no rollback needed»). Если на хосте нет `legal.values.json` (первичная установка), шаг выводит WARNING и пропускается.
2. **`legal publish`** (BE-48-5): после `PlaceholderValues.Load` значения сверяются со встроенным `operator-public.json`. При расхождении — отказ с кодом 5 и списком ключей. `legal status --values <path>` печатает тот же список в отдельном блоке; на код возврата `status` это не влияет.

**Порядок правки реквизита** (DEPLOY §35.3):
1. Поправить `legal.values.json` на машине и `operator-public.json` в репозитории.
2. Выкатить релиз. Precheck сравнит новый файл с новым `legal.values.json`.
3. Выполнить `legal publish` из нового образа. Сверка пройдёт.

Окно между шагами 2 и 3 — минуты. В это время сайт уже показывает новое значение, а опубликованные документы ещё старое.
Это принятый риск, его ловит M48-08. Обратный порядок (сначала publish) невозможен: publish откажет, потому что старый образ
содержит старые значения сайта.

### §48.3.4 Заполнение значений (DO-48-1)
Значения ФИО, ИНН, телефона и `ezbook.privacy@yandex.ru` известны из LEGAL §6.3 и SPEC. `ЮРИДИЧЕСКИЙ_АДРЕС`, `ПОЧТОВЫЙ_АДРЕС`,
`СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ` и `НДС_ОГОВОРКА` в репозитории сейчас стоят маркером `ЗАПОЛНИТЬ (DO-48-1)…`. Их берут дословно из
опубликованных текстов: `GET https://ezbook.ru/api/legal/documents/TermsOwner` и `…/Privacy`, поле `contentHtml`. Это публичные
данные, доступа к машине не нужно. Окончательно совпадение подтверждает precheck на выкате. Пока маркер стоит, красными будут
CY48-B8 и guard CY48-02. Так и задумано: с незаполненным источником в `develop` не попасть.

---

## §48.4. Одна реализация на пять фронтов (A48-3)

```
src/utils/operatorRequisites.ts          чистый модуль (без React, без браузерных API — его же импортирует SSR и about)
  OPERATOR: Readonly<Record<OperatorKey, string>>     значения из JSON
  telHref(): string                                   'tel:+79132258915' (из ТЕЛЕФОН_ОПЕРАТОРА)
  fillOperatorTemplate(template): string              {{KEY}} → значение; неизвестный ключ → throw OperatorTemplateError
  StorefrontSite = 'zapis' | 'goods' | 'dom' | 'bani'
  resolveStorefrontHref(site, href, siteUrls): string '/pricing' на dom|bani → `${siteUrls.zapis}/pricing`; остальное как есть
src/utils/storefrontMarkup.ts            чистый разбор мини-разметки storefront-pages.json → дерево узлов (API §48.22.2)
src/components/storefront/StorefrontBlocks.tsx    рендер блоков (h2/p/ul/ol) и инлайнов (strong/em/link/text); ссылки — linkMode
src/components/sites/OperatorRequisites.tsx       <OperatorRequisitesLine linkMode /> — две строки подвала (§48.6)
                                                  <OperatorNoscriptContent site />  — содержимое <noscript> (§48.10.1)
src/pages/ContactsPage.tsx, src/pages/PaymentInfoPage.tsx   страницы, проп site
src/components/pricing/PricingTermsNote.tsx       подпись под сеткой /pricing (ТЛ48-02)
```

Почему так:
- `src/utils/` и `src/components/` уже входят в `SCANNED_EZBOOK_DIRS`: вторичные приложения могут их импортировать, а их Tailwind сканирует эти папки. ESLint-границы менять не нужно.
- Страницы лежат в `src/pages/`. Вторичным приложениям чужие страницы запрещены регэкспом `appAllowedPagesRegex()`, поэтому обе добавляются в `SHARED_EZBOOK_PAGES` (`frontend/shared-sources.js`). Полноту списка проверяют существующие guard-тесты `*/sharedSources.guard.test.ts`.
- Сайт-специфика — один проп `site: StorefrontSite`. Он решает три вещи: показывать ли пункт «Тарифы и цены» в «Документах», куда ведёт инлайн-ссылка `/pricing` и что передать в `document.title`. Заголовок по LEGAL одинаков на всех сайтах: «… — EZBOOK».
- `linkMode` у ссылок: `'router'` (`<Link>`, SPA и SSR внутри `StaticRouter`), `'plain'` (`<a href>`, `<noscript>`), `{ origin }` (абсолютные `<a>`, about). Компонент импортирует `Link` всегда, а в about рендерится только SSR, так что лишнего клиентского JS нет.

---

## §48.5. Тексты страниц: формат и рендер (A48-2, ТЛ48-04, ТЛ48-05)

### §48.5.1 Почему JSON-эталон, а не JSX
Требований три: тексты дословно от юриста, реквизиты не набраны руками в тексте, проверку делает автотест. Если вписать текст в
JSX, тест сверял бы JSX с Markdown, и у текста было бы два места правки. В `storefront-pages.json` строки записаны той же
мини-разметкой, что и в `LEGAL_REVIEW_CYCLE48.md` (`**`, `*`, `[текст](href)`, `{{КЛЮЧ}}`). Поэтому:
- фронт рендерит прямо из файла (кода с текстом нет);
- CY48-05 после нормализации проверяет, что **каждая** строка файла входит в правовое заключение (нормализация — API §48.22.4);
- бэкенд сверяет свои константы с теми же строками (CY48-B2, CY48-B4).

### §48.5.2 Правила рендера (детали — API §48.22)
- Сначала разбирается разметка, затем `{{КЛЮЧ}}` заменяются в текстовых узлах и `href`. Значение (например, адрес с `*`) не может сломать разметку.
- `dangerouslySetInnerHTML` не используется. HTML в строках не поддерживается.
- Ссылки `/…` — `Link` (`linkMode: 'router'`). `https://…` — `<a rel="noopener noreferrer">` без `target`. `tel:`/`mailto:` — `<a>`.
- Элемент списка `{ "text", "sites" }` рендерится только на перечисленных сайтах. `{ "paras": [...] }` — несколько `<p>` внутри `<li>`.
- Ни один узел не делает запросов: страницы не используют `useQuery`/`axios` (CY48-03/04 проверяют это моком, который падает на любом запросе).

### §48.5.3 Отклонения от буквы текста (осознанные, слова не меняются)
1. Маркер `[*]` из §1.4 не выводится. Абзац оставлен решением ведущего (SPEC §11).
2. Пометка «(на ezbook и goods)» у пункта «Тарифы и цены» на `/contacts` — указание для разработчика, на сайт не выводится. Пункт показывается только на `zapis` и `goods` (SPEC US-48-02 «где есть»).
3. Инлайн-ссылка «Тарифы и цены» в разделе 3 `/payment-info` на dom и bani ведёт на `https://ezbook.ru/pricing` через `publicSites.ts`: своей `/pricing` у них нет, а слова в предложении менять нельзя.
4. E-mail в тексте `/payment-info` оформлены ссылками `mailto:`. Текст не меняется, нормализация CY48-05 убирает разметку ссылок.

---

## §48.6. Подвал с реквизитами (US-48-01, US-48-05)

### §48.6.1 Структура (четыре подвала одинаково)
```
<footer class="border-t border-line mt-16 print:hidden">
  <div class="max-w-[1180px] mx-auto px-4 sm:px-8 py-6 flex flex-col items-center gap-3 text-xs text-ink-soft">
    <OperatorRequisitesLine linkMode="router" />          ← НОВОЕ: footer.identity (p) + footer.contacts (p)
    <div class="flex flex-wrap items-center justify-center gap-x-6 gap-y-2">
      <span>© {год} {бренд}</span> … прежние <Link> … <ProductsPageLink />   ← БЕЗ ИЗМЕНЕНИЙ (порядок, тексты, href)
    </div>
  </div>
</footer>
```
- `footer.identity`: `<p class="text-center [overflow-wrap:anywhere]">`. ИНН — `<span class="whitespace-nowrap">`, как и номер телефона в `footer.contacts`. На 375 px строка переносится только по словам, ИНН и телефон не рвутся.
- `footer.contacts`: ссылки `inline-flex min-h-[24px] items-center` (цель нажатия ≥ 24 px). Разделитель ` · ` — текст.
- Прежние ссылки каждого подвала остаются. У ezbook их 7 + `ProductsPageLink`, у goods/dom/bani — 5. Ссылки «Оплата и возврат» и «Контакты и реквизиты» стоят в строке реквизитов, а не в списке правовых ссылок: так одна реализация на всех сайтах.

### §48.6.2 Контраст (NFR WCAG 2.1 AA)
Сейчас подвал набран `text-muted` (`#A69A88`) на `cream` (`#FAF6F0`): **≈ 2,6 : 1, AA не проходит**. Цвет текста подвала
меняется на `text-ink-soft` (`#6B5F52`), это ≈ 5,8 : 1. Ссылки: `text-ink-soft hover:text-ink underline-offset-2`. Шрифт
`text-xs` остаётся, для AA размер не нужен. Подвал меняется на всех четырёх сайтах сразу, поэтому сверка вида — M48-02 (R48-6).

### §48.6.3 Где подвал не виден
`LegalGuard` при неподтверждённой редакции у вошедшего пользователя заменяет страницу и подвал гейтом. Так было и раньше.
Модератор без входа гейта не видит. `/contacts` и `/payment-info` сами входят в bypass (§48.7).

### §48.6.4 about.ezbook.ru (A48-8)
В `about/src/components/SiteFooter.tsx` над текущей строкой выводится `<OperatorRequisitesLine linkMode={{ origin: sites.zapis }} />`.
Ссылки получаются абсолютные: `https://ezbook.ru/contacts`, `/payment-info`. `ABOUT_SHARED_SOURCES` дополняется двумя файлами
(§48.0, A48-8). Самопроверки `prerender-about.mjs` (нет `<script`, нет `style=`, нет `/api/`) остаются зелёными: компонент не
даёт ни того, ни другого. Классы новых строк попадают в CSS about через whitelist `aboutTailwindContent()`. Ограничение:
about на машину пока не выкачен, поэтому M48 проверяет его только локально.

---

## §48.7. Страницы `/contacts`, `/payment-info` и маршруты (US-48-02, US-48-03, A48-4)

### §48.7.1 Компоненты
```tsx
export function ContactsPage({ site }: { site: StorefrontSite })      // pages.contacts
export function PaymentInfoPage({ site }: { site: StorefrontSite })   // pages.paymentInfo
// оба: useEffect(document.title = page.title); <main className="max-w-[760px] mx-auto px-4 sm:px-8 pt-11 pb-24">
//      <h1 className="font-serif text-[30px] md:text-[36px] font-medium text-ink mb-6">{page.h1}</h1>
//      <StorefrontBlocks blocks={page.blocks} site={site} linkMode="router" />
```
Один `h1`, landmark `<main>`, `h2` у разделов, `ol` для шагов. Типографика — как у `LegalDocumentPage` (760 px, serif-заголовок),
чтобы страницы читались как часть правового раздела.

### §48.7.2 Маршруты в четырёх приложениях
В `App.tsx`, `GoodsApp.tsx`, `DomApp.tsx`, `BaniApp.tsx` — сразу после `<Route path="/payment-terms" …>`, до `/:slug`:
```tsx
<Route path="/contacts" element={<ContactsPage site="goods" />} />
<Route path="/payment-info" element={<PaymentInfoPage site="goods" />} />
<Route path="/payment-terms" element={<Navigate to="/payment-info" replace />} />   // было /terms-owner#payment-terms
```
`site`: `zapis` в `App.tsx`, `goods`, `dom`, `bani` — в своих. В `CONSENT_GATE_BYPASS_PATHS` всех четырёх добавляются
`'/contacts'` и `'/payment-info'`.

### §48.7.3 Карты маршрутов и резерв адресов (точные правки — API §48.27)
- `contracts/cycle11/legal-routes.json`: алиасы `"/contacts": "/contacts"` и `"/payment-info": "/payment-info"` (самоотображённые, по прецеденту `/data-request`), `"/payment-terms": "/payment-info"`, `allowedTargets` дополняется двумя путями. `anchorsPending["/terms-owner"] = ["payment-terms"]` **остаётся**: это напоминание этапу B вернуть алиас на якорь Приложения № 2. `LegalRoutes.cs` правится синхронно, его держит `LegalRoutesTests`.
  - Плюс: существующие тесты `legalRoutes.test.ts`, `goodsRoutes.test.ts`, `domRoutes.test.ts`, `baniApp.routes.test.ts` сами требуют `<Route>` и запись в bypass на всех четырёх сайтах. Новый список вести не нужно.
  - Плюс: правовые тексты цикла 49 смогут ссылаться на `/contacts` и `/payment-info`, и `legal links` не сочтёт ссылки битыми.
- `goods-routes.json`, `cycle39/dom-routes.json`, `cycle42/bani-routes.json`: в `spaRoutes` добавляются `/contacts` и `/payment-info`, в `reservedSlugs` — `payment-info` (`contacts` уже есть во всех трёх). Существующий тест «первый сегмент каждого маршрута зарезервирован» (фронт и `SlugPolicy*Tests`) это закрепляет. `baniRoutes.test` проверяет, что резерв bani ⊇ резерва dom.
- **Салоны** (`ezbook.ru/company/:slug`) не конфликтуют: сервер их адреса не проверяет, и это не меняется.

### §48.7.4 Уже существующие адреса
Новое резервное слово действует только на **новые** адреса. Если на бою уже есть магазин, «Дом» или «Баня» с адресом
`payment-info` или `contacts`, его страница станет недоступна. Поэтому `deploy/checks/cycle48-slugs-live.sql` (DO-48-3) — стоп
перед выкатом: если у компании `Kind <> 0` (не салон) адрес `contacts` или `payment-info` (без учёта регистра), выкат
останавливается до переименования адреса суперадмином. Только чтение.

---

## §48.8. `/pricing` (US-48-04, ТЛ48-01…03)

### §48.8.1 Текст над ценами (`notice`) — бэкенд
```csharp
// PricingCatalogBuilder
public const string NoticeTemplate = "<storefront-pages.json pricing.notice.zapis, байт в байт>";
public static string DefaultNotice => OperatorPublicRequisites.Fill(NoticeTemplate);   // было const
public const string MessengerTaxNote = "Без НДС: оператор применяет налог на профессиональный доход";
public static PublicPricingDto Build(..., string? legalNotice, string? notice = null, LegalSnapshot? legalSnapshot = null)  // notice ?? DefaultNotice
// OrdersPricingCatalogBuilder — то же: NoticeTemplate (= pricing.notice.derivedOrders), DefaultNotice => Fill(...), notice ?? DefaultNotice
```
`DefaultNotice` становится вычисляемым свойством, а не `const`: сроки — значения источника, а не текст в коде. Существующие
вызовы `PricingCatalogBuilder.DefaultNotice` и `OrdersPricingCatalogBuilder.DefaultNotice` в выражениях компилируются без
изменений. Параметр по умолчанию `notice = DefaultNotice` заменяется на `string? notice = null`, все вызовы — с именованными
аргументами (✔ grep: `PricingCatalogCache`, юнит-тесты).

### §48.8.2 Подпись под сеткой — фронт (ТЛ48-02, A48-7)
`PricingTermsNote` рендерит `pricing.termsUnderGrid` через `StorefrontBlocks`: формулу, ссылки на `/terms-owner`,
`/offer-channel`, `/payment-info`, `/contacts` и `{{НДС_ОГОВОРКА}}` дословно. Место — в `PricingPageBody` сразу под сеткой
тарифов, до блока опций. Выводится всегда, когда есть сетка, на обоих `/pricing`. Классы `max-w-[720px] text-sm text-ink-soft
space-y-3 mb-16`. Оговорка не сворачивается и не прячется за кликом (п. 6.13.6).

`data.notice` выводится как раньше, абзацем (LEGAL §2.4 разрешает так). `data.legalNotice` выводится как раньше, если не
`null`. На бою оба `legal-notice` пусты (отчёт DO-48-3). Если когда-то там появится текст, он покажется над сеткой и не
задвоит оговорку: оговорка живёт только в подписи.

### §48.8.3 Налоговая пометка мессенджеров (ТЛ48-03)
`PricingCatalogCache.BuildAsync`: `messengerAddons.BuildAsync(PricingCatalogBuilder.MessengerTaxNote, ct)`, было `dto.Notice`.
`OwnerSubscriptionService` (строка 155): так же, было `PricingCatalogBuilder.DefaultNotice`. Фронт (`MessengerAddonLines`,
`MessengersBlock`) не меняется: он печатает `Условия · {taxNote}`.

### §48.8.4 FAQ «Записи» (Q48-6)
`contracts/cycle46/zapis-landing.json` → версия **48.0.0**. Поле `faq[0].answer` — шаблон `storefront-pages.json`
`faq.zapisPriceAnswer` с `{{…}}`. `zapisFaq.ts`: `answer: fillOperatorTemplate(<тот же шаблон из JSON>)`. Тест CY46-08 (deep
equal) сравнивает конфиг с JSON после той же подстановки. FAQ «Заказов» не меняется.

### §48.8.5 Сколько видно на бою
Витрина «Записи» показывается только при `pricing.public-enabled = true` (R48-4). Цикл флаг не меняет. Отчёт DO-48-3 выводит его
значение, M48-06 проверяет страницу.

---

## §48.9. Тексты тарифов без миграции (A48-6, ТЛ48-01, ТЛ48-07, ТЛ48-10)

### §48.9.1 Каталоги
- `ZapisTariffCatalog.Network`: `Description` и 4-й пункт `Highlights` берутся из `tariffs.zapisNetwork`. Цена 3 900, лимиты `null`, квота фото и пункты 1–3 не меняются.
- `ZapisTariffCatalog.Trial`: в `Highlights` добавляется 4-й пункт из `tariffs.zapisTrial`.
- `OrdersTariffCatalog.Chain`: `Description` и 3-й пункт из `tariffs.ordersChain`. Цена 2 990, `MaxProductsPerShop = 1000`, `MaxOrdersPerMonth = null` не меняются.
- Новые константы прежнего сида: `LegacyNetworkDescription`, `LegacyNetworkHighlights` (строка через `'\n'`), `LegacyTrialHighlights`, `LegacyChainDescription`, `LegacyChainHighlights`. Значения — `legacy*` из JSON, байт в байт как в сидах циклов 28 и 38.

### §48.9.2 Сидер
`TariffCatalogSeeder` получает один чистый метод на оба каталога:
```csharp
public static List<string> AlignPaidTariffTexts(SubscriptionPlanConfig existing, string legacyDescription, string newDescription,
                                                string legacyHighlights, string newHighlights, bool apply)
// Description: меняется, только если existing.Description == legacyDescription (ordinal).
// Highlights:  меняется, только если existing.Highlights == legacyHighlights (вся строка, ordinal).
// Возвращает описания изменений; при apply=false ничего не мутирует.
```
Он вызывается в ветке «уже есть» для `Network`, `Trial` (только Highlights) и `Chain`. Найденный тариф ищется как раньше:
сначала по Id, потом по имени. Строка отчёта: `«Сеть»: тексты (решение 25.10.2026): {изменено|будет изменено} — описание
обновлено; преимущества обновлены`. Цены, лимиты, `IsPublic` и `IsActive` по-прежнему не трогаются.

После выравнивания (и в `plan`, и в `apply`) для каждого тарифа сеток «Записи» и «Заказов» его `Description` и каждый пункт
`Highlights` проверяются по `forbidden-phrases.json` (scope `tariffTexts`). Если есть совпадение, печатается строка
`внимание: «Сеть»: в {описании|преимуществах} осталось «…» (поле правили в админке) — поправьте в админке, текст —
contracts/cycle48/storefront-pages.json`. Это R48-4 и M48-07.

Чистая база (функциональные тесты, демо): `ToEntity` берёт новые тексты, и `ops tariffs apply` создаёт тарифы сразу правильными.

### §48.9.3 На бою
Порядок (DEPLOY §35.4): `ops tariffs plan` → прочитать → `ops tariffs apply` → не позже чем через 60 с (кеш) проверить
`/pricing` (M48-06, M48-07). Перед этим DEPLOY проверяет: окно рассылок пробного периода = 7 (иначе править пункт в админке),
а у абонентов «Сети»/«Сети магазинов» счета выставлены по опубликованной цене (ТЛ48-10, список — отчёт DO-48-3; проверяет
заказчик в админке).

---

## §48.10. Видимость без JavaScript (US-48-06, A48-5)

### §48.10.1 `<noscript>` — обязательно
- В `index.html`, `goods/index.html`, `dom/index.html`, `bani/index.html` сразу после `<body>` ставится маркер `<!--storefront:noscript-->`.
- В dev маркер остаётся комментарием. В релизе `scripts/prerender-storefront.mjs` заменяет его на `<noscript>` с разметкой `renderToStaticMarkup(<OperatorNoscriptContent site linkMode="plain" />)`.
- Состав без единого нового слова:
  - `<p>`: `footer.identity`;
  - `<p>`: `footer.contacts` (телефон, e-mail, «Контакты и реквизиты», «Оплата и возврат»);
  - `<ul>`: блок `documents` страницы `/contacts` с учётом `sites` (Соглашение, Приложение № 1, Тарифы и цены, где есть, Оплата и возврат, Политика, Пользовательское соглашение).
- Без `style=` и `<script>`, разметка — только теги и `href`. Под CSP `default-src 'self'` это допустимо.
- `noindex` у dom и bani не трогаем: он в `<head>` шаблона.

### §48.10.2 Пререндер `/contacts` и `/payment-info` — желательно (P1)
- **Сборка.** `vite.storefront.config.ts`: только `--ssr`, вход `src/storefront/prerender.tsx`, выход `dist-storefront-ssr/prerender.js`, алиас `@` → `src`. Модуль экспортирует:
  ```ts
  renderNoscript(site: StorefrontSite): string                       // внутренность <noscript>…</noscript>
  renderPage(site: StorefrontSite, page: 'contacts' | 'paymentInfo'): { title: string; html: string }
                                                                      // <StaticRouter location={path}> + <ContactsPage|PaymentInfoPage site> + <OperatorRequisitesLine linkMode="router">
  ```
- **Скрипт** `scripts/prerender-storefront.mjs`. Для каждой пары (`dist`, zapis), (`dist-goods`, goods), (`dist-dom`, dom), (`dist-bani`, bani):
  1. В `index.html` подставляет `<noscript>` вместо маркера.
  2. Из того же шаблона (уже с `<noscript>`) пишет `contacts/index.html` и `payment-info/index.html`: `<title>` заменяется на `title`, внутрь `<div id="root">` кладётся `html`.
  3. Самопроверки, при любой ошибке `exit 1` и `prerender-storefront: <причина>`:
     - маркер `<!--storefront:` нигде не остался;
     - в `<noscript>` и в `html` нет `style=` и `<script`;
     - в каждой странице ровно один `<h1`;
     - в каждом файле есть значение ИНН из источника;
     - нет `/api/`;
     - у dom и bani сохранён `noindex`.
- **Порядок в `build:release`**: `build` → `build:goods` → `build:dom` → `build:bani` → `build:about` → **`build:storefront`** (`vite build --config vite.storefront.config.ts --ssr && node scripts/prerender-storefront.mjs`) → `merge-site-dist.mjs goods dom bani about`.
- **nginx — без изменений.** Запрос `/contacts`: `try_files $uri $uri/ /index.html` находит каталог `contacts/`, и модуль `index` отдаёт `contacts/index.html` в том же `location /`, с теми же `Cache-Control: no-cache`, CSP и HSTS. Отдаётся ли он без внешнего 301 на `/contacts/`, проверяет DO-48-2 на настоящем vhost (тест по образцу `test-about-vhost.sh`). Если nginx делает 301, допустимо и это: страница и редирект остаются на своём домене. Тогда отметить в DEPLOY. Правка vhost в этом цикле не требуется.
- **Клиент.** `main.tsx` каждого приложения вызывает `createRoot(#root).render(...)`, и React **заменяет** готовую разметку. Гидратации нет, поэтому расхождение шапки и состояния входа не ломает рендер. Мерцание минимальное: разметка содержимого совпадает.
- **Кеш.** Файлы со сборки неизменяемы в пределах релиза. При выкате меняется симлинк, а `no-cache` заставляет браузер переспросить.
- **Сервис-воркеры** goods, dom и bani не перехватывают `fetch` (правило R11). Пререндер им не мешает.

### §48.10.3 Почему `/pricing` не пререндерится
Цены и тексты тарифов — в БД, их правят в админке. Снимок со сборки устарел бы до следующего выката, а показывать
модератору устаревшие цены хуже, чем не показывать их без JS. Без JS страница `/pricing` отдаёт `<noscript>` со ссылками на
условия, а цены видны с JS. SPEC считает это приемлемым (US-48-06, п. 3). Статичную часть (подпись под сеткой) отдельно не
пререндерим: без цен она теряет смысл.

---

## §48.11. Сигнал оператору о новой заявке (US-48-08, ТЛ48-08)

Почему GlitchTip-сигнал, а не push персоналу:
- push адресован сотрудникам компаний и требует подписки устройства, у суперадмина её может не быть;
- `IGlitchTipSignalService` уже доставляет оператору обращения субъектов ПДн и сигналы пробного периода, то есть канал «оператор узнаёт сегодня» на бою уже проверен;
- новых сервисов и секретов нет (тот же `Sentry:Dsn`).

```csharp
// Services/Signals/BillingRequestSignal.cs — чисто, без EF/HTTP
public static string ForSubscriptionRequest(CompanyKind line, string? planName, int optionLines, Guid billingAccountId, DateOnly invoiceDueBy)
// "Новая заявка на подписку: линейка=Services, тариф=«Салон», опций=2, аккаунт=1a2b3c4d, счёт выставить до 27.10.2026"
public static string ForMessengerPaymentRequest(NotificationTransport transport, Guid billingAccountId, DateOnly invoiceDueBy)
// "Новая заявка на опцию: мессенджер=WhatsApp, аккаунт=1a2b3c4d, счёт выставить до 27.10.2026"
public static DateOnly InvoiceDueBy(DateTime utcNow)   // WorkingDays.Add(дата в Asia/Barnaul, 1) — «следующий рабочий день»
```
- **Когда.** `BillingController.SubmitRequest` — после `transaction.CommitAsync()`. `NotificationChannelsController` (POST) — после коммита и только при `dto.PaymentRequest == true`. Повторная заявка (перезапись) тоже даёт сигнал: оператору важна свежая.
- **Как.** Fire-and-forget, `CancellationToken.None`, `try/catch` с `LogWarning`, по образцу `SubjectRequestsController.SendSignalInBackgroundAsync`. Ответ эндпоинта байт в байт прежний, задержки нет.
- **Состав.** Только линейка, имя тарифа из каталога, число строк опций, первые 8 hex Id аккаунта и дата. Телефона, имени, комментария и ИНН нет: правило из `IGlitchTipSignalService` (legal §6 цикла 16).
- **Уникальность.** Каждое сообщение уникально (аккаунт и дата), и GlitchTip заводит отдельную issue, а алерт «new issue» срабатывает на каждую заявку. Работает ли алерт на бою, проверяет M48-11. Это условие выката ТЛ48-08.
- **Demo.** На эндпоинте заявки стоит `[DemoForbidden]`, поэтому на демо сигналов нет.

---

## §48.12. Структура проекта — что меняется

```
contracts/legal/operator-public.json                         НОВОЕ   единый источник (этот документ; 4 значения — DO-48-1)
contracts/cycle48/openapi.yaml, storefront-pages.json, forbidden-phrases.json   НОВОЕ (этот документ)
contracts/cycle48/openapi.json                                НОВОЕ   генерат (npm run contracts:json) — BE-48-2
contracts/cycle11/legal-routes.json                           ИЗМ.    алиасы /contacts, /payment-info; /payment-terms → /payment-info — BE-48-1
contracts/cycle23/goods-routes.json, cycle39/dom-routes.json, cycle42/bani-routes.json   ИЗМ.  spaRoutes + reservedSlugs — BE-48-1
contracts/cycle46/zapis-landing.json                          ИЗМ.    48.0.0, faq[0].answer — FE-48-3
ServiceBooking.API/
  ServiceBooking.API.csproj                                   ИЗМ.    EmbeddedResource operator-public.json
  Services/Legal/OperatorPublicRequisites.cs                  НОВОЕ   загрузка ресурса, Get, Fill, TelHref
  Services/Legal/LegalRoutes.cs                               ИЗМ.    Aliases
  Services/Billing/PricingCatalogBuilder.cs, OrdersPricingCatalogBuilder.cs   ИЗМ.  NoticeTemplate, DefaultNotice =>, MessengerTaxNote
  Services/Billing/PricingCatalogCache.cs, OwnerSubscriptionService.cs        ИЗМ.  taxNote = MessengerTaxNote
  Services/Showcase/Tariffs/ZapisTariffCatalog.cs, OrdersTariffCatalog.cs, TariffCatalogSeeder.cs   ИЗМ.  §48.9
  Services/Signals/BillingRequestSignal.cs                    НОВОЕ
  Controllers/BillingController.cs, NotificationChannelsController.cs         ИЗМ.  сигнал после коммита
ServiceBooking.LegalKit/RequisitesDrift.cs                    НОВОЕ   чистая сверка; PublishCommand.cs, StatusCommand.cs — ИЗМ.
ServiceBooking.Tests/Infrastructure/OpenApiContract.cs        ИЗМ.    + AssertSchema(componentName, JsonElement)
ServiceBooking.UnitTests/Cycle48/*.cs, ServiceBooking.Tests/Tests/Cycle48*.cs, UnitTests/LegalKit/RequisitesDriftTests.cs   НОВОЕ
frontend/
  shared-sources.js                                           ИЗМ.    SHARED_EZBOOK_PAGES + 2; ABOUT_SHARED_SOURCES + 2
  index.html, goods/index.html, dom/index.html, bani/index.html   ИЗМ.  маркер <!--storefront:noscript-->
  public/robots.txt                                           ИЗМ.    Allow: /contacts, /payment-info, /pricing
  src/utils/operatorRequisites.ts, src/utils/storefrontMarkup.ts         НОВОЕ
  src/components/storefront/StorefrontBlocks.tsx              НОВОЕ
  src/components/sites/OperatorRequisites.tsx                 НОВОЕ
  src/components/pricing/PricingTermsNote.tsx                 НОВОЕ;  PricingPageBody.tsx — ИЗМ.
  src/pages/ContactsPage.tsx, src/pages/PaymentInfoPage.tsx   НОВОЕ
  src/pages/home/zapisFaq.ts                                  ИЗМ.
  src/components/layout/Footer.tsx, goods/…/GoodsFooter.tsx, dom/…/DomFooter.tsx, bani/…/BaniFooter.tsx   ИЗМ.
  src/App.tsx, goods/src/GoodsApp.tsx, dom/src/DomApp.tsx, bani/src/BaniApp.tsx   ИЗМ.  маршруты, bypass
  about/src/components/SiteFooter.tsx (+ AboutPage.tsx — проп)  ИЗМ.
  src/storefront/prerender.tsx, vite.storefront.config.ts, scripts/prerender-storefront.mjs   НОВОЕ
  tsconfig.storefront.json                                    НОВОЕ   (если SSR-вход не покрывается tsconfig.json; см. FE-48-4)
  package.json                                                ИЗМ.    build:storefront, build:release
  scripts/contracts-to-json.mjs                               ИЗМ.    + 'cycle48' — BE-48-2
  contracts/cycle36/test-areas.json                           ИЗМ.    только если новый тест вне существующих префиксов (guard подскажет)
deploy/
  deploy-remote.sh                                            ИЗМ.    check_operator_requisites, cycle48 slugs, SUPPORT_MAILBOX_VERIFIED, смоуки noscript/страниц
  checks/cycle48-storefront-report.sql, checks/cycle48-slugs-live.sql   НОВОЕ
  ci/smoke-frontend.sh                                        ИЗМ.    noscript (4 профиля), пререндер
  ci/test-requisites-precheck.sh                              НОВОЕ   фикстуры сверки
  nginx/test-storefront-pages.sh                              НОВОЕ   /contacts, /contacts/ на настоящем vhost
.github/workflows/ci.yml                                      ИЗМ.    redocly cycle48, contracts-json diff cycle48, новые смоуки
DEPLOY.md §35, ACQUIRER_CHECKLIST.md                          ИЗМ./НОВОЕ
docs/owner.md («Тариф»), CHANGELOG.md, CURRENT_STATE.md, TEST_CATALOG.md   ИЗМ.  (DOC-48-1, QA-48-1)
```
Не меняются: `legal-drafts/**`, `App_Data/legal/**`, `legal.values.example.json` и схема `legal-values.schema.json` (L48-3),
`Migrations/**`, nginx-vhost'ы.

---

## §48.13. Тесты

### §48.13.1 Автотесты разработчиков (CY48-*)

| ID | Где | Что проверяет | Задача |
|---|---|---|---|
| CY48-01 | `src/components/layout/Footer.test.tsx`, `goods/…/GoodsFooter.test.tsx`, `dom/…/DomFooter.test.tsx`, `bani/…/BaniFooter.test.tsx` | Строка реквизитов = `fillOperatorTemplate(footer.identity)`. `tel:` = `telHref()` и `^tel:\+7\d{10}$`. `mailto:` = `ПОЧТА_ПОДДЕРЖКИ`. «Контакты и реквизиты» → `/contacts`, «Оплата и возврат» → `/payment-info` (свой домен). Список прежних ссылок (текст + href) = список на `5bcc4a19` (литералом в тесте). Классы ИНН и телефона содержат `whitespace-nowrap`, ссылки — `min-h-[24px]`, текст — не `text-muted` | FE-48-1 |
| CY48-02 | `src/test/operatorSingleSource.guard.test.ts` | Ни один исходник (`import.meta.glob` raw: `src`, `goods/src`, `dom/src`, `bani/src`, `about/src`, кроме `*.test.*`, `operatorRequisites.ts`) не содержит ИНН, 10 цифр телефона подряд или с пробелами, `support@ezbook.ru`, `ezbook.privacy@`, фамилию (город не проверяется: «Барнаул» законно есть в коде как город по умолчанию). В `operator-public.json` нет `ЗАПОЛНИТЬ` и пустых значений | FE-48-0 |
| CY48-03 | `src/pages/ContactsPage.test.tsx` (×4 сайта через `it.each`) | Один `h1`, `document.title`, все 13 значений есть в DOM, пункт «Тарифы и цены» только на zapis/goods. Мок `axios`-адаптера и `fetch` падает на любом вызове, а страница всё равно рендерится целиком. При `consentRequired = true` в `LegalGuard` страница видна (bypass) | FE-48-2 |
| CY48-04 | `src/pages/PaymentInfoPage.test.tsx` (×4) | Как CY48-03. Ссылка «Тарифы и цены» на dom/bani — абсолютная `https://ezbook.ru/pricing`. В DOM нет `{{`. `/payment-terms` → `/payment-info` (MemoryRouter + маршруты приложения) | FE-48-2 |
| CY48-05 | `src/components/storefront/storefrontPages.contract.test.ts` | Каждая строка `storefront-pages.json` (кроме `legacy*`, `derived*`, `_*`, у `tariffs.*.highlights` — только последний пункт) после нормализации (API §48.22.4) входит в нормализованный `LEGAL_REVIEW_CYCLE48.md` (`?raw`). `pricing.notice.derivedOrders == zapis.replace('заявку на тариф и опции', 'заявку на тариф')` | FE-48-0 |
| CY48-06 | `src/utils/operatorRequisites.test.ts`, `src/utils/storefrontMarkup.test.ts` | `fillOperatorTemplate`: все ключи `placeholders`, неизвестный ключ → throw. `telHref`. Разбор разметки: bold/italic/link/placeholder, значение со `*` или `[` не ломает дерево, HTML не интерпретируется. `resolveStorefrontHref` по таблице сайтов | FE-48-0 |
| CY48-07 | `src/test/forbiddenPhrases.guard.test.ts` | Регулярки `forbidden-phrases.json` (scope `publicCopy`) не находят совпадений: в восьми конфигах главных и FAQ (после подстановки), в `storefront-pages.json`, в `zapis-landing.json` | FE-48-3 |
| CY48-08 | `zapisFaq.test.ts`, `zapisLanding.contract.test.tsx` (существующие) | `zapis-landing.json` версии 48.0.0. `zapisFaq[0].answer == fill(faq[0].answer)`, и `faq[0].answer == storefront-pages.json faq.zapisPriceAnswer` | FE-48-3 |
| CY48-09 | существующие `legalRoutes.test.ts`, `goodsRoutes.test.ts`, `domRoutes.test.ts`, `baniApp.routes.test.ts`, `baniRoutes.test.ts`, `*/slug.test.ts` + новый `it` в каждом | Маршруты `/contacts` и `/payment-info` есть, они в bypass и объявлены до `/:slug`. `payment-info` в резерве, валидатор адреса фронта отвечает «зарезервирован» | FE-48-2 |
| CY48-10 | `src/pages/PricingPage.test.tsx`, `goods/src/pages/PricingPage.test.tsx` | Под сеткой есть `PricingTermsNote`: 4 ссылки с правильными `href` и текст `НДС_ОГОВОРКА`. `notice` из ответа выведен. Фикстуры без запрещённых фраз | FE-48-3 |
| CY48-11 | `src/storefront/prerender.test.tsx` (node-env) | `renderNoscript(site)`: identity, contacts, documents (с учётом sites), нет `style=` и `<script`. `renderPage`: один `<h1`, title, ИНН, нет `{{` и `/api/` | FE-48-4 |
| CY48-12 | `about/src/render.test.tsx` (+1 `it`), `about/src/sharedSources.guard.test.ts` (существующий) | В подвале about есть строка реквизитов, ссылки `https://ezbook.ru/contacts` и `/payment-info`. Whitelist соблюдён | FE-48-1 |
| CY48-B1 | `UnitTests/Cycle48/OperatorPublicRequisitesTests.cs` | Встроенный ресурс загружается. `Fill` подставляет и бросает на неизвестном ключе. `TelHref` | BE-48-0 |
| CY48-B2 | `UnitTests/Cycle48/PricingNoticeTests.cs` | `NoticeTemplate` обоих билдеров == `storefront-pages.json` (`zapis`/`derivedOrders`). `DefaultNotice` не содержит `{{`, содержит `СРОК_СЧЁТА` и `СРОК_АКТИВАЦИИ` источника. Совпадает с pattern из `openapi.yaml` | BE-48-2 |
| CY48-B3 | `UnitTests/Cycle48/MessengerTaxNoteTests.cs` + функциональный в CY48-B5 | `MessengerTaxNote == pricing.messengerTaxNote`. `taxNote` в `GET /api/pricing` и `GET /api/billing/subscription` — константа, а не notice | BE-48-2 |
| CY48-B4 | `UnitTests/Cycle48/TariffTextsAlignmentTests.cs` | Каталоги == `tariffs.*`, `Legacy*` == `legacy*`. `AlignPaidTariffTexts`: поле = legacy → меняется; правлено → не трогается; `apply=false` → не мутирует. Цена и лимиты не меняются. Предупреждение `plan` о запрещённой фразе. Функционально: `ops tariffs apply` на чистой базе создаёт «Сеть», «Сеть магазинов», «Пробный период» с новыми текстами | BE-48-3 |
| CY48-B5 | `Tests/Tests/Cycle48StorefrontContractTests.cs` | После `TariffCatalogSeeder.ApplyAsync` и `pricing.public-enabled = true`: ответы `GET /api/pricing`, `GET /api/pricing/orders`, `GET /api/billing/subscription` проходят `OpenApiContract.Load("cycle48").AssertResponse(...)`. Тексты тарифов проходят `forbidden-phrases.json` (`tariffTexts`) | BE-48-2 (+BE-48-3 для данных) |
| CY48-B6 | `UnitTests/Cycle48/BillingRequestSignalTests.cs`, `Tests/Tests/Cycle48OperatorSignalTests.cs` | Состав сообщения (нет телефона, имени, комментария). `InvoiceDueBy`: пятница → понедельник. Функционально: `IGlitchTipSignalService` заменён записывающим фейком через `ConfigureTestServices` — одна заявка = один сигнал. Фейк бросает → ответ 200 и тело как без фейка. `paymentRequest: false` → сигнала нет | BE-48-4 |
| CY48-B7 | `UnitTests/LegalKit/RequisitesDriftTests.cs`, `…/PublishCommandTests` (+кейсы) | `RequisitesDrift.Compare`: равенство, расхождение одного ключа, отсутствие ключа, различие на пробел. `publish` с расхождением → код 5, ни одного записанного файла, в выводе ключ и оба значения | BE-48-5 |
| CY48-B8 | `Tests/Tests/Cycle48OperatorRequisitesSchemaTests.cs` | `operator-public.json` проходит `OpenApiContract.Load("cycle48").AssertSchema("OperatorPublicRequisites", …)`. Город входит в адрес. `ТЕЛЕФОН_TEL` по шаблону | BE-48-0 |
| CY48-B9 | существующие `LegalRoutesTests`, `SlugPolicyTests`, `StaysSlugPolicyTests`, `BathsSlugPolicyTests` (+ `[InlineData("payment-info")]`) | Синхронность `LegalRoutes` и JSON. `payment-info` → `Reserved` для магазина, «Дома» и «Бани» | BE-48-1 |
| CY48-B10 | `UnitTests/Cycle48/ForbiddenPhrasesTests.cs` | Регулярки JSON компилируются в .NET, разрешённые строки («Цена не зависит от числа филиалов», «Цена не зависит от числа гостей», «— от 690 ₽/мес» вне tariffTexts) не ловятся, старые ловятся. `Description`/`Highlights` тарифов `ZapisTariffCatalog.Grid` и `OrdersTariffCatalog.Grid` чисты (константы `Legacy*` — прежние тексты, не проверяются) | BE-48-3 |
| CY48-D1 | `deploy/ci/test-requisites-precheck.sh` (в CI) | Python-сверка на фикстурах: равны → 0; расхождение → 1 и ключ в выводе; нет `legal.values.json` → 0 + WARNING; `ЗАПОЛНИТЬ` → 1 | DO-48-3 |
| CY48-D2 | `deploy/ci/smoke-frontend.sh` (профили ezbook/goods/dom/bani) | В `index.html` есть `<noscript>` с ИНН и `href="/contacts"`. Если есть `contacts/index.html` и `payment-info/index.html`, GET `/contacts` = 200, тело содержит «Контакты и реквизиты» и ИНН | DO-48-2 |
| CY48-D3 | `deploy/nginx/test-storefront-pages.sh` (в CI) | Настоящий `ezbook.conf` (и `goods.ezbook.conf`) над собранным dist: `/contacts` и `/contacts/` → 200 (или ровно один 301 на свой домен — тогда записать), `Cache-Control: no-cache`, CSP присутствует, `/payment-info` — то же | DO-48-2 |
| CY48-D4 | `deploy/checks/cycle48-*.sql` | Отчёт выполняется на пустой схеме без ошибок (CI-шаг с postgres-сервисом, если есть; иначе — прогон QA на тестовой базе) | DO-48-3 |

### §48.13.2 Существующие тесты, которые меняются осознанно
- `Footer.test.tsx` ×4: прежние ожидания ссылок остаются, добавляются CY48-01.
- `PricingPage.test.tsx` (ezbook): фикстура «Сеть» с «после заявки» → новый текст.
- `legalRoutes`-тесты ×4: правок не требуют, падают, пока не добавлены маршруты. Так и задумано.
- `OrdersPricingCatalogBuilderTests` (строка 102): сравнивает с `DefaultNotice`, правок не требует.
- `PricingCatalogBuilder*Tests`: если где-то сравнивают с литералом прежнего `DefaultNotice`, заменить на свойство. Подгонять под литерал нельзя.
- `zapisLanding.contract.test.tsx`/`zapisFaq.test.ts`: сравнивают с JSON, после FE-48-3 зелёные.

### §48.13.3 Ручные проверки
M48-01…M48-10 — SPEC §9. Добавляются:
- **M48-11** (ТЛ48-08): на бою создать заявку на тариф тестовым владельцем, оператору пришло уведомление GlitchTip (почта или приложение), затем отменить заявку.
- **M48-12** (ТЛ48-09): есть письменная правовая оценка по каждому мессенджеру, строка которого видна на `/pricing`.
- **M48-13** (ТЛ48-10): в админке у абонентов «Сети» и «Сети магазинов» нет счетов не по опубликованной цене (список — отчёт DO-48-3).
- **M48-14**: `about.ezbook.ru` локально (`npm run build:about`, открыть `dist-about/index.html`): строка реквизитов, абсолютные ссылки.

---

## §48.14. Задачи и параллельность

**Контракт готов**: `contracts/legal/operator-public.json`, `contracts/cycle48/*` и этот документ. После коммита документов (DO-48-0)
все три трека стартуют одновременно. Владение файлами разведено, общих файлов у треков нет, кроме оговорённых.

### §48.14.1 Backend (backend-developer)

| Задача | Приоритет | Содержание | Владеет файлами | Зависит от |
|---|---|---|---|---|
| **BE-48-0** | P0, первой | `OperatorPublicRequisites` (EmbeddedResource, `Get`, `Fill`, `TelHref`). `OpenApiContract.AssertSchema`. CY48-B1, CY48-B8 (B8 красный до DO-48-1) | `.csproj`, `Services/Legal/OperatorPublicRequisites.cs`, `Tests/Infrastructure/OpenApiContract.cs`, тесты | — |
| **BE-48-1** | P0, первой (маленькая) | Правки `legal-routes.json`, `LegalRoutes.cs`, `goods-routes.json`, `cycle39/dom-routes.json`, `cycle42/bani-routes.json` по API §48.27. CY48-B9. Фронтовые route-тесты после этого красные, пока FE-48-2 не добавит маршруты. Это ожидаемо | эти 5 файлов, тесты слагов | — |
| **BE-48-2** | P0 | Notice ×2, `MessengerTaxNote`, `PricingCatalogCache`, `OwnerSubscriptionService`. `'cycle48'` в `scripts/contracts-to-json.mjs` и генерат `contracts/cycle48/openapi.json`. CY48-B2, B3, B5 | `Services/Billing/{PricingCatalogBuilder,OrdersPricingCatalogBuilder,PricingCatalogCache,OwnerSubscriptionService}.cs`, `frontend/scripts/contracts-to-json.mjs`, `contracts/cycle48/openapi.json`, тесты | BE-48-0 |
| **BE-48-3** | P0 | Тексты тарифов, `AlignPaidTariffTexts`, предупреждения `plan`. CY48-B4, B10 | `Services/Showcase/Tariffs/*`, тесты | — (CY48-B5 зелёный после BE-48-2 + BE-48-3) |
| **BE-48-4** | P0 (условие выката) | `BillingRequestSignal`, сигнал в двух контроллерах. CY48-B6 | `Services/Signals/BillingRequestSignal.cs`, `BillingController.cs`, `NotificationChannelsController.cs`, тесты | — |
| **BE-48-5** | P1 | `RequisitesDrift` в LegalKit, отказ `publish`, блок `status --values`. CY48-B7 | `ServiceBooking.LegalKit/*`, тесты LegalKit | BE-48-0 |

Один исполнитель: BE-48-1 → BE-48-0 → BE-48-2 → BE-48-3 → BE-48-4 → BE-48-5.

### §48.14.2 Frontend (frontend-developer)

| Задача | Приоритет | Содержание | Владеет файлами | Зависит от |
|---|---|---|---|---|
| **FE-48-0** | P0, первой | `operatorRequisites.ts`, `storefrontMarkup.ts`, `StorefrontBlocks.tsx`, TS-типы `storefront-pages.json` (API §48.22.1). CY48-02, 05, 06 | эти файлы и их тесты | — |
| **FE-48-1** | P0 | `OperatorRequisites.tsx`, четыре подвала, контраст, about (`SiteFooter`, `AboutPage`, `ABOUT_SHARED_SOURCES`). CY48-01, 12 | `src/components/sites/OperatorRequisites.tsx`, 4 файла `*Footer.tsx` + тесты, `about/src/components/{SiteFooter,AboutPage}.tsx`, `shared-sources.js` (только `ABOUT_SHARED_SOURCES`) | FE-48-0 |
| **FE-48-2** | P0 | `ContactsPage`, `PaymentInfoPage`, маршруты и bypass в 4 `*App.tsx`, `SHARED_EZBOOK_PAGES`, `robots.txt`. CY48-03, 04, 09 | `src/pages/{Contacts,PaymentInfo}Page.tsx` + тесты, 4 `*App.tsx`, `shared-sources.js` (только `SHARED_EZBOOK_PAGES`), `public/robots.txt`, route-тесты | FE-48-0, **BE-48-1** (JSON-карты) |
| **FE-48-3** | P0 | `PricingTermsNote` + `PricingPageBody`, `zapisFaq.ts`, `zapis-landing.json` 48.0.0, фикстуры. CY48-07, 08, 10 | `src/components/pricing/{PricingTermsNote,PricingPageBody}.tsx`, `src/pages/PricingPage.tsx`, `goods/src/pages/PricingPage.tsx` (проп `site`), `src/pages/home/zapisFaq.ts`, `contracts/cycle46/zapis-landing.json`, `src/pages/PricingPage.test.tsx`, `goods/src/pages/PricingPage.test.tsx`, `src/test/forbiddenPhrases.guard.test.ts` | FE-48-0. С BE-48-2 — только по контракту (notice приходит из API) |
| **FE-48-4** | P0 (`<noscript>`), P1 (пререндер страниц) | `src/storefront/prerender.tsx`, `vite.storefront.config.ts`, `scripts/prerender-storefront.mjs`, маркер в 4 `index.html`, `package.json` (`build:storefront`, `build:release`), при необходимости `tsconfig.storefront.json` и шаг tsc в CI (сообщить DO). CY48-11 | эти файлы | FE-48-1, FE-48-2 (компоненты) |

`shared-sources.js` правят двое (FE-48-1 — `ABOUT_SHARED_SOURCES`, FE-48-2 — `SHARED_EZBOOK_PAGES`). Это разные константы
в одном файле. Конфликт слияния тривиален, но если исполнитель один — правка одним коммитом.

Один исполнитель: FE-48-0 → FE-48-1 → FE-48-2 → FE-48-3 → FE-48-4.

### §48.14.3 DevOps (devops-engineer, отдельный агент) — DO-трек

| Задача | Приоритет | Содержание | Владеет файлами | Зависит от |
|---|---|---|---|---|
| **DO-48-0** | P0, первой | Фиксация документов цикла: этот файл, `API_CONTRACT_CYCLE48_STOREFRONT.md`, `contracts/cycle48/{openapi.yaml,storefront-pages.json,forbidden-phrases.json}`, `contracts/legal/operator-public.json` | — | — |
| **DO-48-1** | P0, сразу | Заполнить 4 значения `operator-public.json` дословно из опубликованных `/terms-owner` и `/privacy` (`GET https://ezbook.ru/api/legal/documents/{TermsOwner,Privacy}` → `contentHtml`, п. 1.1.2, 6.13.6, 6.13.7 и раздел обращений Политики). Если сети нет — запросить у человека значения из боевого `legal.values.json` | `contracts/legal/operator-public.json` (только `values`) | DO-48-0 |
| **DO-48-2** | P0 | CI: `redocly lint` + `../contracts/cycle48/openapi.yaml`; шаг «Contract JSON must match» + `cycle48`; `smoke-frontend.sh` — проверки CY48-D2 в профилях ezbook/goods/dom/bani; `deploy/nginx/test-storefront-pages.sh` (CY48-D3) и шаг в CI; tsc-шаг для `tsconfig.storefront.json`, если FE-48-4 его заведёт | `.github/workflows/ci.yml`, `deploy/ci/smoke-frontend.sh`, `deploy/nginx/test-storefront-pages.sh`, `contracts/redocly.yaml` (комментарий-список) | пишется сразу; зелёный после FE-48-4, BE-48-2 |
| **DO-48-3** | P0 | `deploy-remote.sh`: (а) `check_operator_requisites` (python, §48.3.3) рядом с `check_legal_manifest`; (б) `cycle48-storefront-report.sql` (отчёт) + `cycle48-slugs-live.sql` (стоп), по образцу cycle19, до любых изменений; (в) гейт `SUPPORT_MAILBOX_VERIFIED=true` в `.env`; (г) после readiness — смоуки: `curl https://<host>/` → `<noscript>` с ИНН на 4 сайтах (dom/bani — если vhost установлен), `/contacts` → 200 + «Контакты и реквизиты»; ошибка → `rollback_hint`. `deploy/ci/test-requisites-precheck.sh` (CY48-D1) + шаг в CI | `deploy/deploy-remote.sh`, `deploy/checks/cycle48-*.sql`, `deploy/ci/test-requisites-precheck.sh` | — |
| **DO-48-4** | P0 | `DEPLOY.md` §35 (API §48.30) и `ACQUIRER_CHECKLIST.md` (US-48-07, §48.18.2) | `DEPLOY.md`, `ACQUIRER_CHECKLIST.md` | — |

Состав отчёта `cycle48-storefront-report.sql` (только чтение, 6 блоков):
1. Компании `Kind <> 0` с адресом `contacts`/`payment-info`.
2. Строки «Сеть», «Сеть магазинов», «Пробный период»: `Description`, `Highlights`, признак «= legacy / = новый / правлено».
3. `pricing.public-enabled`.
4. `pricing.legal-notice`, `pricing.orders.legal-notice`.
5. Окно рассылок пробного периода.
6. Аккаунты с подпиской на «Сеть» и «Сеть магазинов» (Id, линейка, `PaidUntil`) — для ТЛ48-10.

Стоп-сигнал — только блок 1, он вынесен в `cycle48-slugs-live.sql`. Имена таблиц и колонок DO сверяет по
`AppDbContext`/снапшоту.

### §48.14.4 QA и документация

| Задача | Содержание | Зависит от |
|---|---|---|
| **QA-48-1** | Полный прогон: `dotnet test` (оба набора), `npm run test:run`, `npm run lint`, все `tsc`, `npm run build:release`, `redocly lint` cycle48, `smoke-frontend.sh` ×4, `test-storefront-pages.sh`. По желанию — `schemathesis` по `contracts/cycle48/openapi.yaml`. Раздел «Цикл 48» в `TEST_CATALOG.md` (CY48-*, CY48-QA-*). Сверка `/payment-info` и `/contacts` в браузере с LEGAL §1.4/§6.1 | все BE/FE/DO |
| **DOC-48-1** | `docs/owner.md` §«Тариф»: абзац «Оплатить или сменить тариф в интерфейсе нельзя…» заменить путём подключения (заявка → счёт → оплата → активация) со ссылкой на `/payment-info`. `CHANGELOG.md` (цикл 48), `CURRENT_STATE.md` (§5.x цикла 48, §9 — долг §48.16) | после QA-48-1 |
| **M48-01…14** | Оркестратор и заказчик на бою по `ACQUIRER_CHECKLIST.md` | после выката |

```
DO-48-0 ─┬─> DO-48-1 ─────────────────────────────────────────────┐
         ├─> BE-48-1 ──┬─> FE-48-2 ─┐                              │
         ├─> BE-48-0 ──┼─> BE-48-2 ─┼─> (CY48-B5) ─┐               │
         │             └─> BE-48-5  │              │               │
         ├─> BE-48-3, BE-48-4 ──────┤              ├─> QA-48-1 ─> DOC-48-1 ─> человек: develop ─> выкат ─> M48 ─> заявка эквайеру
         ├─> FE-48-0 ─┬─> FE-48-1 ──┼─> FE-48-4 ───┤
         │            └─> FE-48-3 ──┘              │
         └─> DO-48-2, DO-48-3, DO-48-4 ────────────┘
```

---

## §48.15. Безопасность, доступность, производительность

- **ПДн и секреты.** Публикуются только сведения об операторе в объёме п. 6.13.7. Решение Q48-1 сознательное, значения и так опубликованы. Данных пользователей нет. Новые страницы не ставят cookie и не ходят в API. Секретов в файлах нет: `legal.values.json` и `.env` по-прежнему вне git. Сигнал оператору не содержит ПДн (§48.11).
- **XSS.** Тексты из JSON рендерятся узлами React, без `dangerouslySetInnerHTML`. Значения подставляются в текстовые узлы и `href` после разбора разметки. Допустимые схемы `href`: `/`, `https://`, `tel:`, `mailto:` (иное → ошибка рендера, тест CY48-06). Пререндер и `<noscript>` — `renderToStaticMarkup`, экранирование React.
- **CSP.** Без inline-скриптов и `style=`, это проверяют самопроверки скрипта. Заголовки vhost'ов не меняются.
- **Доступность.** Один `h1`, `<main>`, `h2`-разделы без пропуска уровней. Контраст подвала ≥ 4,5 : 1 (§48.6.2). Цели нажатия в подвале ≥ 24 px. Понятные тексты ссылок `tel:`/`mailto:`: видимый номер и адрес. Внешняя ссылка ФНС — без `target`.
- **Производительность.** Подвал и страницы без сетевых запросов. JSON ≈ 15 КБ попадает в основной бандл каждого приложения. Это приемлемо, но можно вынести страницы в `React.lazy` — на усмотрение FE, если бандл заметно вырастет (ориентир — +10 КБ gzip). Пререндер — 8 HTML-файлов на релиз. Кеш витрины цен (60 с) не меняется. `DefaultNotice` вычисляется один раз на построение кеша.

---

## §48.16. Риски

| # | Риск | Решение |
|---|---|---|
| R48-1 | Вторая заявка с недочётом | `ACQUIRER_CHECKLIST.md`. Заявка только после M48-01…14 |
| R48-2 | Реквизиты на сайте ≠ опубликованным | Один источник + две сверки (precheck выката, publish) + M48-08 |
| R48-3 | Неработающий `support@` на бою | Гейт `SUPPORT_MAILBOX_VERIFIED=true` в `deploy-remote.sh` + M48-05 |
| R48-4 | На бою текст «Сети» правили в админке / витрина выключена | Предупреждение `ops tariffs plan`, отчёт DO-48-3, M48-06/07 |
| R48-6 | Подвал меняет вид четырёх сайтов | Прежние ссылки и порядок не меняются, новые две строки — сверху. M48-02 на 375/1280 |
| R48-9 | DO-48-1 перенесёт значение неточно (пробел, кавычки) | Precheck выката сравнивает побайтно с боевым `legal.values.json` и остановит выкат до изменений |
| R48-10 | `try_files $uri/` отдаёт 301 вместо 200 | CY48-D3 на настоящем vhost. 301 на свой домен допустим. Без пререндера страницы всё равно работают как SPA |
| R48-11 | Пререндер и SPA разойдутся по разметке | Один и тот же компонент. Клиент не гидратирует, а заменяет. CY48-11 + M48-04 |
| R48-12 | `contracts:json` и генерат cycle48 расходятся | CI-шаг diff (DO-48-2) |
| R48-13 | Сигнал GlitchTip не доходит до человека (алерт не настроен) | M48-11 — условие выката (ТЛ48-08). Резерв: экран заявок `/admin` (как сейчас) |
| R48-14 | Окно рассылок пробного периода на бою ≠ 7 | Отчёт DO-48-3, блок 5. Тогда поправить 4-й пункт «Пробного периода» в админке (DEPLOY §35.4) |
| R48-15 | Цена «Сети» в `Description` литералом («3 900 ₽») разойдётся с ценой, если её поменяют в админке | Цену в цикле не меняем. При будущей смене цены текст правится в админке вместе с ценой (заметка в DEPLOY §35, долг в CURRENT_STATE §9) |
| R48-16 | Бандл вырастет из-за JSON текстов | §48.15, при необходимости `React.lazy` |

Долг на цикл 49 (записать в CURRENT_STATE §9): п. 6.2.2 (статус и ИНН в заявке на тариф) не исполняется (LEGAL §1.4,
примечание). Сроки и второй адрес перенести в Соглашение (LEGAL §1.3, §3). Алиас `/payment-terms` вернуть на Приложение № 2
(`anchorsPending`). Покрытие Соглашением «Заказов» и поддоменов (LEGAL §8 п. 3).

---

## §48.17. Отклонения от SPEC и уточнения

1. **US-48-01 «одна реализация».** Это модуль и компонент в `src/` (A48-3). Подвалы ×4 остаются отдельными файлами: у каждого свой бренд и свой набор правовых ссылок.
2. **US-48-02/L48-3.** `ezbook.privacy@yandex.ru` подписан как адрес официальных обращений (SPEC §11, ТЛ48-05). Текст — LEGAL §6.1.
3. **US-48-03.** Текст — LEGAL §1.4 с абзацем [*]. Уточнения отображения — §48.5.3.
4. **US-48-04.** Подпись под ценами появляется и на goods (п. 6.13.4 требует её на любой странице цен). `/offer-channel` на goods относительная: маршрут-редирект есть во всех приложениях (✔ `GoodsApp.tsx:160`). Абсолютная ссылка, о которой спрашивал LEGAL §2.5, не нужна.
5. **US-48-06.** `/pricing` не пререндерится (§48.10.3).
6. **US-48-07.** Чек-лист — отдельный файл `ACQUIRER_CHECKLIST.md` в корне (§48.18.2). Анкета — ссылкой на LEGAL §5, без копии.
7. **US-48-08.** Канал — GlitchTip-сигнал (§48.11). Дополнительно сигнал срабатывает на заявку на оплату номера мессенджера: это тоже заявка с обещанным сроком счёта.
8. **ТЛ48-07.** 4-й пункт «Пробного периода» добавляется: проверка по коду показала, что в пробном периоде сообщения возможны.
9. **R48-3.** Гейт выката по `support@` — механический, а не только строка в DEPLOY.

---

## §48.18. Выкат и документация

### §48.18.1 DEPLOY.md §35 (DO-48-4) — состав
- §35.1 Что выкатывается. Без миграций.
- §35.2 Блокеры выката:
  - M48-05 и `SUPPORT_MAILBOX_VERIFIED=true`;
  - ТЛ48-08 / M48-11;
  - ТЛ48-09 / M48-12;
  - вычитка `/payment-info` и `/contacts` живым юристом (рекомендовано).
- §35.3 Порядок правки реквизита (§48.3.3).
- §35.4 Шаги после выката:
  - `ops tariffs plan` → `apply`;
  - `pricing.public-enabled`;
  - окно пробного периода;
  - ТЛ48-10.
- §35.5 Откат: обычный `rollback.sh`. Тексты тарифов при откате остаются новыми — это безопасно, старый код их показывает.

### §48.18.2 `ACQUIRER_CHECKLIST.md` (US-48-07)
- Шапка: «Проект Продамусу (или другому провайдеру) отправляется только после выката цикла 48 и прохождения всех пунктов ниже» (бриф §0, §2).
- M48-01…M48-14 — галочки с командой или адресом проверки.
- Анкета и ответ модератору — ссылкой на `LEGAL_REVIEW_CYCLE48.md` §5.1–§5.2: «копировать оттуда, не переписывать».
- Строка про §8 п. 4 заключения: вычитка комплекта живым юристом, шаг 12 чек-листа публикации.
