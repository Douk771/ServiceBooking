# ARCHITECTURE — цикл 29 ServiceBooking: доделки цикла 26 «Единая карточка компании» (без юриста)

**Разделы §29.0–§29.18**, контракт — `API_CONTRACT_CYCLE29.md` §29.20–§29.29. Нумерация идёт с префиксом цикла, а не
сквозная (A29-5, §29.1). Со сквозными номерами (§543–§569 циклов 26–27) и с диапазоном, который возьмёт ветка
`cycle/028-*`, такие номера не пересекаются. В комментариях кода и в документах ссылаться только с именем файла:
`ARCHITECTURE_CYCLE29.md §29.4`.

**На входе:**
- `SPEC.md` цикла 29 (Q-29-1…Q-29-6 приняты автономно, спека написана по колонке «Решение»);
- `CURRENT_STATE.md` на `b9c2a79` (§5.5 цикл 26, §6 конвенции, §7 тесты и CI, §9.1 C26-1…C26-7);
- код ветки `cycle/029-cycle26-followups` (= `develop` `b9c2a79`), сверен по файлам, названным ниже.

Ветку подготовил devops-инженер, архитектор её не трогает. Архив спеки цикла 27
(`SPEC_CYCLE27_GOODS_BUSINESS_BLOCK.md`) уже лежит в рабочем дереве, его коммитит devops (SPEC, «Первое техническое
действие»).

| Файл | Что в нём | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE29.md` (этот) | решения, механизмы, структура, задачи, параллельность, риски | все |
| `API_CONTRACT_CYCLE29.md` (§29.20–§29.29) | контракт словами: порядок проверок, тексты, смысл полей, приёмка | backend, frontend, QA |
| `contracts/cycle29/openapi.yaml` | **источник истины по форме** (OpenAPI 3.0.3): prism, `openapi-typescript`, schemathesis, redocly, C#-сверка | backend, frontend, QA, CI |

Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` — документы цикла 3, по конвенции проекта (`CURRENT_STATE.md` §6.5,
§10.5) они не перезаписываются. Документы цикла лежат в корне с суффиксом цикла.

---

## §29.0. Что это за цикл для архитектуры

Цикл не продуктовый, это хвосты. Из шестнадцати пунктов спеки код приложения меняют восемь. Остальное — тесты, CI,
ручная проверка вёрстки и документы. Отсюда три следствия:

1. **Стек и структура не пересматриваются.** Новых пакетов, сервисов, переменных окружения, маршрутов и миграций нет.
   Всё, что добавляется, кладётся в существующие папки по существующим конвенциям (`CURRENT_STATE.md` §6).
2. **Каждое изменение привязано к одной точке кода.** Там, где SPEC оставляет выбор механизма (A29-1…A29-5, T-29-02,
   T-29-05), выбран вариант с минимальной площадью изменений и с тестом, который упадёт, если механизм сломается.
3. **Бэкенд и фронт почти не связаны.** Общая точка у них одна — поле `logoUrl` в каталоге goods, и она закрыта
   контрактом до начала работ. Остальные задачи идут параллельно с первого дня (§29.16).

---

## §29.1. Итог решений — ответы на SPEC §6 одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| — | Стек, зависимости, миграции | **Без изменений.** Ни NuGet, ни npm, ни миграций. Сверку контракта в C# делает самописный валидатор подмножества OpenAPI поверх `System.Text.Json`. Схему он читает из JSON, который собирает уже имеющийся `@redocly/cli bundle` | §29.2, §29.7 |
| A29-1 | Где ловить битую форму | **Обёртка над двумя фабриками провайдеров значений MVC** (`JQueryFormValueProviderFactory`, `JQueryQueryStringValueProviderFactory`). `ArgumentException` ловится только внутри вызова обёрнутой фабрики и превращается в `ValueProviderException` с русским текстом. Дальше штатный путь MVC: ошибка в `ModelState` → `ModelStateInvalidFilter` → `ModelValidationErrorFormatter` → 400 `text/plain`. Глобального перехвата `ArgumentException` нет | §29.4 |
| A29-1 | Какой код получает аноним | **401.** Аутентификация и авторизация — middleware, они срабатывают до MVC. Порядок: 401 → 429 → 451 → **400** → 404/403. 400 не зависит от `{id}`, поэтому не оракул | §29.4.3 |
| A29-2 | Нужен ли `contracts/cycle29/` | **Да**, дельта: каталог goods (+`logoUrl`) и две загрузки (400 на битую форму). Генерат `src/types/api-cycle29.generated.ts`. Сразу входит в CI (`redocly lint` и сверка генератов) вместе с `contracts/cycle26` (T-29-02) | §29.5, §29.7 |
| A29-3 | `type` кнопок в `SettingsTab` | У `Button` **нет** `type` по умолчанию (`frontend/src/components/ui/Button.tsx`), значит в `<form>` это `submit`. «Сохранить» города и пояса получает `type="button"`. Блок города обёрнут так, что Enter в его полях **не** запускает неявную отправку основной формы. Вложенных `<form>` нет | §29.9.3 |
| A29-4 | Правило первой буквы | Одна функция `companyInitial(name)` в `frontend/src/utils/companyInitial.ts`: первый символ класса «буква или цифра» (`/[\p{L}\p{N}]/u`) в верхнем регистре (`ru-RU`), не больше одного символа, иначе `''`. Один компонент `CompanyLogoMark` рисует логотип или заглушку, в том числе при ошибке загрузки картинки. Его используют `CompanyCard`, каталог goods и каталог ezbook | §29.8 |
| A29-5 | Нумерация § | **Префикс цикла**: §29.0–§29.18 здесь, §29.20–§29.29 в контракте. Ветку 28 проверить нельзя: она ещё не влита, и диапазон, который она займёт при влитии, неизвестен. Номер с префиксом цикла не пересекается ни с чем по построению | — |
| Q-29-6 | Текст 400 | Близкого текста в `*Texts.cs` нет (проверено поиском). Для формы — дословно SPEC: «Не удалось прочитать данные формы. Проверьте имена полей запроса.». Для строки запроса — «Не удалось прочитать параметры запроса. Проверьте имена параметров.» (О-1, §29.18) | §29.4 |
| T-29-02 | Способ сверки контракта | В проекте нет автоматической сверки в обычном прогоне: schemathesis описан в документах, но в CI не запускается (T8-5). Решение: функциональный xUnit-тест + валидатор §29.7 | §29.7 |
| T-29-05 | Флаг смены пояса | **DTO не меняется.** Кабинет goods уже сравнивает смещения на клиенте и блокирует выбор только при `!timeZoneChangeAllowed && смещения различаются` (сверено: `ShopProfileSection.tsx`, стр. 111–131). C26-3 — не дефект UI, а недокументированный риск. Закрывается тестами и формулировкой в контракте | §29.10 |
| Q-29-2 | Одна кнопка у салона | Как в SPEC: у основной формы одна кнопка. У адреса и у города с поясом свои кнопки, внутри группы «Адрес и карты» | §29.9 |
| US-29-01 | Семантика групп | `fieldset` + `legend`, как «Адрес и карты» в `ShopProfileSection` (цикл 26). Группа «Запись» — **одна колонка**: подписи длинные, а под окном переноса стоит многострочная подпись `CANCEL_WINDOW_FIELD_CAPTION` | §29.9 |

---

## §29.2. Стек, зависимости, миграции

**Без изменений** (`CURRENT_STATE.md` §1). .NET 8 / ASP.NET Core MVC / EF Core 8 / PostgreSQL 16; React 18 + Vite 5 +
Tailwind 3 + react-query 5 + react-hook-form 7; Vitest 3; openapi-typescript 7.13; @redocly/cli 2.54.

Чем закрыты места, где соблазнительно было бы взять пакет:

| Потребность | Не берём | Берём | Почему |
|---|---|---|---|
| Прочитать OpenAPI YAML из C#-теста | YamlDotNet, Microsoft.OpenApi.Readers | `redocly bundle --ext json` (уже в devDependencies) → закоммиченный `contracts/cycleNN/openapi.json` + `System.Text.Json` | SPEC §3 запрещает новые зависимости. Закоммиченный производный файл защищён от дрейфа тем же приёмом, что генераты типов: CI перегенерирует и делает `git diff --exit-code` |
| Проверить JSON по схеме | NJsonSchema, JsonSchema.Net | валидатор подмножества OpenAPI 3.0 (~200 строк) в `ServiceBooking.Tests/Infrastructure/` | Контракты проекта используют узкий набор ключевых слов (§29.7.2). На неизвестном ключевом слове валидатор падает, а не молчит |
| e2e на 360 px | Playwright | ручная проверка по кейсам `M29-` | Q-29-5 |

Миграций нет: `Company.LogoUrl` существует с цикла 13, индекс `CompanyPhotos (CompanyId, Position)` — с цикла 10.

---

## §29.3. Модель данных и DTO

Сущности и таблицы **не меняются**. Меняются два позиционных record'а, новое поле дописывается в конец с дефолтом
(`CURRENT_STATE.md` §6.1):

```csharp
// ServiceBooking.API/DTOs/Catalog/CatalogDtos.cs
public sealed record GoodsCatalogShopDto(
    string Slug, string Path, string Name, string? Address, string CityName, ShopOpenStateDto OpenState,
    CatalogAcceptance Acceptance, string AcceptanceText,
    // Cycle 29 (API_CONTRACT_CYCLE29.md §29.22).
    string? LogoUrl = null);

// ServiceBooking.API/Services/Shops/GoodsCatalogService.cs — запись кеша списка города
public sealed record CatalogEntry(
    Guid Id, string Slug, string Name, string? Address, string CityName, ShopOpenStateDto OpenState, CatalogAcceptance Acceptance,
    string? LogoUrl = null);
```

`CatalogEntry.LogoUrl` = `string.IsNullOrWhiteSpace(c.LogoUrl) ? null : c.LogoUrl`, так же как `Address` в той же
строке `BuildAsync`. Кандидаты уже грузятся целыми `Company` (`Include(c => c.City)`), поэтому колонка приходит тем же
SQL: ноль новых запросов, N+1 нет. Бюджет p95 (+20 мс) не расходуется. Комментарий класса `GoodsCatalogService`
(«No prices, photos, ratings…») дополнить: логотип — публичные данные карточки, показываются так же, как на витрине.

Фронт: `GoodsCatalogShopDto`/`GoodsCatalogPageDto` в `frontend/goods/src/types.ts` переводятся на генерат цикла 29
(`type K = C29['schemas']`). Прочие типы каталога (`CatalogCityDto`, `CatalogAcceptance`, `CatalogListingDto`)
остаются на цикле 25.

---

## §29.4. Неразбираемое имя поля — 400 (US-29-04, A29-1)

### §29.4.1 Почему сейчас 500 и где именно

MVC создаёт провайдеры значений **до** входа в действие: `ControllerBinderDelegateProvider` →
`CompositeValueProvider.TryCreateAsync` проходит все `MvcOptions.ValueProviderFactories`. Две из них по умолчанию —
`JQueryFormValueProviderFactory` (для тела с form content type) и `JQueryQueryStringValueProviderFactory` (для строки
запроса). Обе **сразу** прогоняют все ключи через `JQueryKeyValuePairNormalizer`, а он на ключе вида `file[` бросает
`ArgumentException`. `TryCreateAsync` штатно перехватывает только `ValueProviderException`: так ASP.NET Core сам
оборачивает `InvalidDataException`/`IOException` из `ReadFormAsync`. `ArgumentException` улетает в
`UseExceptionHandler`, получается 500 и событие в GlitchTip.

### §29.4.2 Механизм

1. **`ServiceBooking.API/Services/RequestTexts.cs`** (новый, по конвенции `*Texts`):
   ```csharp
   public static class RequestTexts
   {
       public const string MalformedFormFieldName = "Не удалось прочитать данные формы. Проверьте имена полей запроса.";
       public const string MalformedQueryParameterName = "Не удалось прочитать параметры запроса. Проверьте имена параметров.";
   }
   ```
2. **`ServiceBooking.API/Services/MalformedKeyGuardValueProviderFactory.cs`** (новый):
   ```csharp
   public enum MalformedKeySource { Form, Query }

   public sealed class MalformedKeyGuardValueProviderFactory(IValueProviderFactory inner, MalformedKeySource source)
       : IValueProviderFactory
   {
       public IValueProviderFactory Inner => inner;          // для юнит-теста Install
       public Task CreateValueProviderAsync(ValueProviderFactoryContext context);
       // try { await inner.CreateValueProviderAsync(context); }
       // catch (ArgumentException ex) { log Information; throw new ValueProviderException(text(source), ex); }

       /// Replaces the two JQuery factories IN PLACE (same index). Throws InvalidOperationException at startup if
       /// either is missing — a framework upgrade that renames them must fail loudly, not silently drop the guard.
       public static void Install(IList<IValueProviderFactory> factories);
   }
   ```
   - `catch` стоит **только** вокруг вызова обёрнутой фабрики. Внутри этого вызова `ArgumentException` бросает только
     разбор ключей: ошибки `ReadFormAsync` фреймворк уже превратил в `ValueProviderException`, и они проходят насквозь
     без изменений. Условие узкое по построению, текст стека не сравнивается. Требование SPEC «не превращать в 400
     любые `ArgumentException`» выполнено: `ArgumentException` из действия, сервиса или фильтра до этого кода не
     доходит.
   - Лог — `ILogger<MalformedKeyGuardValueProviderFactory>` из `context.ActionContext.HttpContext.RequestServices`,
     уровень **Information**, шаблон `"Rejected request with a malformed {Source} key on {Method} {Path}"`. Имена и
     значения полей в лог не пишутся. Сообщение `ArgumentException` содержит сам ключ и тоже не пишется.
3. **`Startup/ApiExtensions.cs` → `AddServiceBookingControllers`**: лямбда `AddControllers` становится блочной, в неё
   добавляется `MalformedKeyGuardValueProviderFactory.Install(options.ValueProviderFactories);`. Регистрация
   `LegalConsentFilter` и всё остальное не меняются.
4. **`Services/ModelValidationErrorFormatter.BuildResponse`**: первым шагом, до цикла по полям, проверить, есть ли в
   `ModelState` ошибка, у которой `ErrorMessage` дословно равен одной из двух констант `RequestTexts`. Если есть,
   вернуть 400 `text/plain; charset=utf-8` с этим текстом. `TryAddModelException` для `ValueProviderException`
   кладёт в `ModelState` именно `exception.Message` (класс помечен фреймворком как безопасный для клиента). Остальное
   поведение форматтера не меняется.

**Почему не фильтр исключений и не middleware.** Фильтр исключений MVC не видит исключений привязки модели в том
виде, который нужен. Middleware видит `ArgumentException` уже без контекста, и отличить «битый ключ» от любой другой
ошибки там можно только по стеку. Отказ от JQuery-фабрик целиком (`RemoveType<…>`) сломал бы допустимый синтаксис
`ids[]=…`: axios по умолчанию так сериализует массивы в `params`.

**Запасной путь, если проверка на .NET 8.0.11 покажет иное** (первый же юнит-тест BE-1 это выявит): обёртка вместо
`throw` кладёт маркер в `HttpContext.Items` и выбрасывает `ValueProviderException` с тем же текстом. Глобальный
`IAsyncActionFilter` с `Order = -3000` (раньше `ModelStateInvalidFilter`, у которого −2000) по маркеру возвращает тот
же `ContentResult`. Контракт при этом не меняется.

### §29.4.3 Порядок проверок и почему это не оракул

`Program.cs`: `UseAuthentication` → `UseAuthorization` → `UseRateLimiter` → `MapControllers`. Внутри MVC сначала идут
фильтры авторизации (`LegalConsentFilter` глобально, `[RequiresOwnerTerms]` — оба `IAsyncAuthorizationFilter`), потом
ресурсные (`[RequestSizeLimit]`), потом привязка модели (здесь 400), потом действие (здесь 404/403). Отсюда
401 → 429 → 451 → **400** → 404 → 403.

400 не читает БД и не зависит от `{id}`, поэтому одинаково отвечает на свою, чужую и несуществующую компанию, и
существования ресурса не раскрывает. Этот порядок уже действует на всех маршрутах проекта: автоматическая валидация
модели `[ApiController]` тоже отвечает до входа в действие. Все 52 контроллера помечены `[ApiController]`
(проверено поиском), значит `ModelStateInvalidFilter` стоит на каждом действии.

### §29.4.4 Что не меняется

- Корректные формы, в том числе с допустимыми скобками (`meta[x]=1`, `ids[]=…`).
- 413 и прочие отказы чтения тела: они идут через `ValueProviderException` фреймворка, обёртка их пропускает.
- Маршруты без параметров действия: они не создают провайдеров значений, форму не читают и отвечают как раньше
  (`API_CONTRACT_CYCLE29.md` §29.23.4).
- `UseExceptionHandler`, формат 500, сигналы GlitchTip.

---

## §29.5. Каталог goods: логотип (US-29-02, сервер)

- Поле — §29.3. Порядок, фильтры, кеш, тексты не меняются.
- Кеш 30 с: логотип попадает в `CatalogEntry` при сборке списка города, поэтому новый логотип виден не позже чем через
  `Orders:CatalogCacheSeconds`. Сбрасывать кеш из `UploadLogo` **не нужно**: SPEC это прямо разрешает («не позже, чем
  истечёт кеш»), а сброс добавил бы связь «компании → каталог goods» ради 30 секунд.
- Безопасность: значение берётся только из `Company.LogoUrl`. Туда пишет только `ImageUploadService` через
  `FileStorage` (публичная область). Сервер строку не собирает и чужих адресов не проксирует.
- Тесты: `CY29-10`, `CY29-11` (§29.14) на `CatalogTestFactory` (кеш выключен); сверка формы `CY29-33` (§29.7).

---

## §29.6. Галерея: один запрос (T-29-06, C26-4) и фото-квота (T-29-07, C26-5)

**T-29-06 (P2).** `CompanyDtoAssembler.GetPhotosOrderedAsync` удаляется. Единственный вызов
(`CompaniesController.cs:204`, `GetBySlug`) переходит на `CompanyPhotoQueries.OrderedAsync(db, c.Id,
HttpContext.RequestAborted)`. Порядок сортировки у двух методов сейчас совпадает байт в байт (Position, CreatedAtUtc,
Id). Разница одна: `AsNoTracking` у общего запроса. `GetBySlug` — GET без `SaveChanges`, поэтому на результат это не
влияет. Разработчик проверяет это чтением метода и пишет в отчёт. Критерий — зелёные CY26-05, CY26-20 и тесты
карточки салона без правки.

**T-29-07 (P1).** Код загрузки галереи (`CompanyPhotosController.Upload`) тарифной квоты фото не проверяет: в файле нет
`PhotoQuotaMb`, класс-комментарий это прямо фиксирует (решения П5/П7 цикла 10). Ожидаемый результат теста — «10 приняты,
11-е — 400 с текстом магазина». Тест `CY29-20`:
1. создать магазин обычным путём (`POST /api/shops`). Аккаунт получает системный бесплатный тариф линейки «Заказы»,
   проверить это чтением подписки в тесте;
2. загрузить 10 **разных** изображений (разные байты: иначе сработает дедупликация по хешу и вернётся 200);
3. 11-е → 400 «В галерее магазина может быть не больше 10 фотографий».

Лимит частоты `uploads` в окружении `Testing` поднят до 1000 (`appsettings.Testing.json`), 429 не помешает. **Если
тест покажет тарифный отказ, работа останавливается** (SPEC T-29-07): в отчёт идёт находка с текстом отказа, код не
правится.

---

## §29.7. Автоматическая сверка контрактов 26 и 29 (T-29-02, C26-1)

### §29.7.1 Поток

```
contracts/cycleNN/openapi.yaml ──(npm run contracts:json = redocly bundle --ext json)──► contracts/cycleNN/openapi.json (в git)
                                                                                                │
ServiceBooking.Tests/Infrastructure/OpenApiContract.cs  ◄── читает JSON (walk up до ServiceBooking.sln) ┘
        │
        └─ Cycle29ContractConformanceTests: реальный HTTP-ответ хоста → AssertResponse(method, pathTemplate, status, body)
```

- Скрипт в `frontend/package.json`:
  `"contracts:json": "redocly bundle ../contracts/cycle26/openapi.yaml --ext json --output ../contracts/cycle26/openapi.json && redocly bundle ../contracts/cycle29/openapi.yaml --ext json --output ../contracts/cycle29/openapi.json"`.
  Если у установленной версии `@redocly/cli` флаги называются иначе, devops подбирает эквивалент, не меняя смысла.
- Найти корень репозитория — приёмом, который уже есть: подъём от `AppContext.BaseDirectory` до папки с
  `ServiceBooking.sln` (`ServiceBooking.UnitTests/LegalKit/RuntimeValueFormsCorpusTests.cs`, `FindRepoFile`). Тест
  читает `contracts/cycle26/openapi.json` и `contracts/cycle29/openapi.json`.
- Почему JSON в git, а не генерация на лету: джоб `backend` в CI работает без Node. Дрейф JSON от YAML ловит джоб
  `frontend` (§29.7.4).

### §29.7.2 Валидатор (`ServiceBooking.Tests/Infrastructure/OpenApiContract.cs`)

```csharp
public sealed class OpenApiContract
{
    public static OpenApiContract Load(string cycle);   // "cycle26" | "cycle29"
    /// Resolves paths[pathTemplate][method].responses[status].content["application/json"].schema and validates body.
    /// Fails the test (FluentAssertions) with every violation listed as "$.photos[1].width: expected integer, got string".
    public void AssertResponse(string method, string pathTemplate, int status, JsonElement body);
    public IReadOnlyList<string> Validate(JsonElement schema, JsonElement value);   // for self-tests
}
```

Поддерживаемые ключевые слова (это весь набор, который используют `contracts/cycle26` и `cycle29`):
`$ref` (только `#/components/schemas/…`), `type` (`object`, `array`, `string`, `integer`, `number`, `boolean`),
`nullable`, `required`, `properties`, `additionalProperties` (`true`/`false`), `items`, `minItems`, `maxItems`,
`enum`, `allOf`, `format` (`uuid`, `date`, `date-time`; прочие — без проверки), `minimum`, `maximum`, `maxLength`,
`minLength`. Аннотации `description`, `example`, `default`, `title` игнорируются. **Любое другое ключевое слово —
исключение `NotSupportedException`** с его именем, чтобы молчаливое «прошло» не выдавалось за проверку.

Семантика:
- `integer` — JSON-число без дробной части; `number` — любое число;
- `null` допустим только при `nullable: true` на этом уровне, в том числе рядом с `allOf`;
- `required` — ключ обязан быть в объекте. Значение `null` при этом проверяется правилом `nullable`;
- **строгий режим по умолчанию**: свойство, которого нет в `properties`, — ошибка, если у схемы нет явного
  `additionalProperties: true`. Обоснование: контракт 26 объявляет `StorefrontDto` и `ShopManageDto` описанными
  «ЦЕЛИКОМ», а в контракте 29 у DTO каталога явно стоит `additionalProperties: false`. SPEC требует ловить лишние
  поля «если схема это запрещает», и для этих схем она запрещает;
- ответ с кодом, которого нет в операции, — ошибка (`status_code_conformance`).

Самопроверка — `ServiceBooking.Tests/Tests/OpenApiContractValidatorTests.cs`, без БД: по одному положительному и
отрицательному случаю на каждое ключевое слово, отказ на неизвестном ключевом слове, отказ на лишнем поле.

### §29.7.3 Сверяемые ответы

`ServiceBooking.Tests/Tests/Cycle29ContractConformanceTests.cs` (свой класс — своя БД; хост — `CatalogTestFactory`,
чтобы каталог читался без кеша). Одна подготовка на класс: магазин с email, логотипом, часами работы, опубликованным
товаром, видимый в каталоге, **≥ 2 фото**. Второй магазин — без логотипа.

| ID | Запрос | Контракт / операция |
|---|---|---|
| CY29-30 | `GET /api/storefront/{slug}` | cycle26 `get /api/storefront/{slug}` 200. Дополнительно проверяется `photos.Count >= 2` и `email != null`: без этого сверка пустой галереи ничего не доказывает |
| CY29-31 | `GET /api/companies/{id}/photos` | cycle26 `get /api/companies/{id}/photos` 200 |
| CY29-32 | `GET /api/shops/{shopId}` (владелец) | cycle26 `get /api/shops/{shopId}` 200 |
| CY29-33 | `GET /api/goods/catalog?cityId=…` | cycle29 `get /api/goods/catalog` 200; у одного элемента `logoUrl` строка, у другого `null` |

Найденное расхождение исправляется по правилу `API_CONTRACT_CYCLE29.md` §29.26: неверный код правится в коде,
неполная копия схемы — в `contracts/cycle26/openapi.yaml`, затем `types:api:cycle26` и `contracts:json`. Если после
правки контракта меняется `api-cycle26.generated.ts`, фронт перепроверяет `tsc` для goods.

### §29.7.4 CI (`.github/workflows/ci.yml`, джоб `frontend`)

| Шаг | Изменение |
|---|---|
| `Lint API contracts (TD-07)` | добавить `../contracts/cycle26/openapi.yaml` и `../contracts/cycle29/openapi.yaml` |
| `Generated API types must match the contracts` | добавить `npm run types:api:cycle26`, `npm run types:api:cycle29` и оба файла в `git diff --exit-code` |
| **новый** `Contract JSON bundles must match YAML (T-29-02)` | `npm run contracts:json && git diff --exit-code -- ../contracts/cycle26/openapi.json ../contracts/cycle29/openapi.json`; при расхождении — `::error::` с подсказкой команды |
| `contracts/redocly.yaml` | дописать циклы 26 и 29 в комментарий-перечень линтуемых спек |

Сама сверка ответов (CY29-30…33) идёт в шаге `Functional tests` джоба `backend`, отдельного шага не нужно.

---

## §29.8. Логотип и заглушка: одно правило (A29-4, US-29-02, US-29-03)

### §29.8.1 `frontend/src/utils/companyInitial.ts`

```ts
/** ARCHITECTURE_CYCLE29.md §29.8 — the one placeholder letter for a company without a (loadable) logo. */
export function companyInitial(name: string | null | undefined): string
```

Правило: первый символ `name`, подходящий под `/[\p{L}\p{N}]/u` (буква любого алфавита или цифра), переведённый в
верхний регистр через `toLocaleUpperCase('ru-RU')`, от результата берётся первый символ. Нет такого символа → `''`.

| name | было в `CompanyCard` (`name.trim()[0]?.toUpperCase()`) | стало |
|---|---|---|
| `Барбершоп` | `Б` | `Б` |
| `  салон` | `С` | `С` |
| `«Ромашка»` | `«` | `Р` |
| `"Лаванда"` | `"` | `Л` |
| `— Кофе` | `—` | `К` |
| `ёлка` | `Ё` | `Ё` |
| `1-я пекарня` | `1` | `1` |
| `🍕 Пицца` | половина суррогатной пары | `П` |
| `ß-bar` | `SS` | `S` |
| `''`, `'   '`, `null` | `''` | `''` |

Меняются только краевые случаи из таблицы, их SPEC разрешает. Тест `companyInitial.test.ts` проверяет таблицу целиком.

### §29.8.2 `frontend/src/components/company/CompanyLogoMark.tsx`

```ts
export interface CompanyLogoMarkProps {
  name: string
  logoUrl?: string | null
  /** 'card' — 64×64 шапка CompanyCard; 'catalog' — 56×56 карточка каталога (обоих сайтов). */
  size: 'card' | 'catalog'
  /** Добавочные классы обёртки, напр. наезд на галерею `-mt-[52px]` в CompanyCard. */
  className?: string
}
export function CompanyLogoMark(props: CompanyLogoMarkProps): JSX.Element
```

| | `card` | `catalog` |
|---|---|---|
| Бокс | `w-16 h-16 rounded-[18px] border-4 border-white shrink-0` (как сейчас в `CompanyCard`) | `w-14 h-14 rounded-2xl shrink-0` (как сейчас иконка в каталогах) |
| `<img>` | `object-cover`, `width={64} height={64}`, `alt=""`, без lazy (над сгибом) | `object-cover`, `width={56} height={56}`, `alt=""`, `loading="lazy"`, `decoding="async"` |
| Заглушка | `div aria-hidden="true"`, `bg-cream-deep text-gold-dark font-serif text-2xl flex items-center justify-center`, текст `companyInitial(name)` | то же, `text-xl` |
| `data-testid` | `company-logo-img` / `company-logo-initial` | то же |

Ошибка загрузки: `const [failedSrc, setFailedSrc] = useState<string | null>(null)`. Картинка рисуется, если
`logoUrl && logoUrl !== failedSrc`; `onError={() => setFailedSrc(logoUrl)}`. Новый `logoUrl` (замена логотипа)
снова пробует загрузку без эффектов и `key`.

`alt=""` везде. В каталогах картинка лежит внутри ссылки, у которой название уже есть в `h3` (ezbook) или в
`aria-label` и `h3` (goods). В `CompanyCard` название — `h1` рядом.

### §29.8.3 Места вызова

| Где | Было | Стало |
|---|---|---|
| `frontend/src/components/company/CompanyCard.tsx` (стр. 39–52) | свой `img`/`div` | `<CompanyLogoMark size="card" name={company.name} logoUrl={company.logoUrl} className={overlap} />`. `overlap` — та же строка ` -mt-[52px]` при галерее |
| `frontend/goods/src/pages/CatalogHomePage.tsx`, `ShopRow` (стр. 155–157) | иконка `store` | `<CompanyLogoMark size="catalog" name={shop.name} logoUrl={shop.logoUrl} />`, импорт `@/components/company/CompanyLogoMark` (алиас в goods разрешён, ESLint запрещает только страницы ezbook) |
| `frontend/src/pages/HomePage.tsx`, локальный `CompanyCard` (стр. 44–50), **P2 US-29-03** | `img alt={company.name}` / иконка `store` | `<CompanyLogoMark size="catalog" …/>`, импорт **относительный** `../components/company/CompanyLogoMark` (C26-2: в `vite.config.ts` ezbook нет алиаса `@`) |

В `ShopRow` блок с названием: `<div className="min-w-0">` → `<div className="min-w-0 flex-1">`. Тогда `truncate` у
`h3` обрезает длинное название независимо от ширины контента, а логотип (`shrink-0`) не сжимается. В `HomePage` —
то же самое. Остальная разметка обеих карточек не меняется.

---

## §29.9. Кабинет салона: группы полей (US-29-01)

### §29.9.1 Принцип (R29-1)

Меняется **только разметка**: обёртки, порядок JSX, классы сетки. Не трогаются `useForm` (`values`,
`resetOptions: { keepDirtyValues: true }`), `isDirty` по `dirtyFields`, `handleSubmit`, `updateMut`, `logoMut`,
`mapLinksError`, все `register`, тексты, `aria-*`, тарифные `disabled`. Если в ходе работы понадобится менять логику
формы, US-29-01 сужается до групп внутри нынешних карточек (SPEC R29-1), и это записывается в отчёт.

### §29.9.2 Раскладка `SettingsTab` (`frontend/src/pages/owner/CompanyManagePage.tsx`)

```
<h2>Настройки компании</h2>                         ← без изменений
<Card className="p-6">
  [Шапка]  логотип 64×64 + «Загрузить/Заменить логотип» + «JPEG, PNG или WEBP, до 5 МБ» + ошибка логотипа
           ← блок стр. 786–821 без изменений, ВНЕ <form>, как сейчас
  <form onSubmit={…как сейчас…} className="flex flex-col gap-6">
    <fieldset G>  <legend L>Основное</legend>        Название; Описание
    <fieldset G>  <legend L>Контакты</legend>        <div className="grid sm:grid-cols-2 gap-3"> Телефон | Email </div>
    <fieldset G>  <legend L>Адрес и карты</legend>
                    <CompanyAddressField …/>                         ← как сейчас (своя кнопка type="button")
                    <CityTimeZoneFields company companyId />         ← бывшая CityTimeZoneCard, §29.9.3
                    <div className="grid sm:grid-cols-2 gap-3">
                      [Яндекс Карты: Input + подсказка + ошибка] | [2ГИС: Input + ошибка]
                    </div>
    <fieldset G>  <legend L>Запись</legend>           одна колонка:
                    горизонт + подсказка; окно переноса + CANCEL_WINDOW_FIELD_CAPTION + ошибка;
                    три галочки с тарифными подсказками
    «Сохранено» / settingsError / <Button type="submit">Сохранить изменения</Button>   ← как сейчас
  </form>
</Card>
<CompanyPhotosSection/>  {company && <WidgetCard/>}  <PhotoUsageCard/>     ← порядок прежний; CityTimeZoneCard отсюда уходит
```

- `G` = `className="min-w-0 flex flex-col gap-4 border-t border-line pt-5"`, `L` = `className="text-[13px]
  font-semibold text-ink pr-2"`. Это классы `ShopProfileSection` плюс `min-w-0`: у `fieldset` в браузерах
  `min-inline-size: min-content`, Tailwind preflight его не сбрасывает, и без `min-w-0` длинная строка на 360 px
  раздвинет страницу.
- Иерархия: `h2` «Настройки компании» → группы как `legend` (не заголовки) → внутри «Адрес и карты» подзаголовок
  **`h3`** «Город и часовой пояс» (бывший `h2` карточки, текст тот же). Уровни не пропускаются.
- Пары через «|»: одна колонка уже 640 px, две от `sm`. Так исправляется и нынешний `grid grid-cols-2` телефона и
  email, который на 360 px всегда в две колонки (вероятный дефект US-29-05).
- Порядок табуляции совпадает с порядком DOM, а он — с визуальным: логотип → название → описание → телефон → email →
  адрес (+ его кнопка) → город → галочка IANA (→ поле) → «Сохранить» города → Яндекс → 2ГИС → горизонт → окно →
  три галочки → «Сохранить изменения».

### §29.9.3 Город и пояс внутри основной формы (A29-3)

`CityTimeZoneCard` переименовывается в `CityTimeZoneFields`. Состояние (`city`, `manualZone`, `zoneId`, `error`),
`mut` и тело запроса `{ cityId, timeZoneId }` не меняются. Разметка:

```tsx
<div role="group" aria-labelledby={titleId} className="flex flex-col gap-3" onKeyDown={swallowImplicitSubmit}>
  <h3 id={titleId} className="text-sm font-semibold text-ink">Город и часовой пояс</h3>
  <p className="text-sm text-muted">От часового пояса зависит момент отправки напоминаний… (дословно)</p>
  <CityCombobox …/> {строка пояса} {галочка IANA} {поле IANA} {error} {Сохранено}
  <Button type="button" className="self-start" …>Сохранить</Button>
</div>
```

- **`type="button"`** у «Сохранить»: иначе внутри `<form>` кнопка по умолчанию стала бы `submit` и отправила бы
  основную форму.
- **`swallowImplicitSubmit`**: `if (e.key === 'Enter' && e.target instanceof HTMLInputElement) e.preventDefault()`.
  Нажатие Enter в текстовом поле внутри `<form>` вызывает неявную отправку формы. Сейчас блок города стоит вне формы,
  и Enter в нём ничего не делает. `CityCombobox` при **закрытом** списке на Enter только открывает список и
  `preventDefault` не вызывает (`CityCombobox.tsx`, стр. 65–68). Без обёртки Enter в поле города или IANA отправил бы
  основную форму. Обработчик стоит на обёртке (всплытие), собственная обработка Enter в `CityCombobox` при открытом
  списке срабатывает раньше и не меняется.
- Enter в поле адреса (`CompanyAddressField`) уже сейчас стоит внутри формы. Это прежнее поведение, его не трогаем.
- Кнопка основной формы шлёт только `register`-поля, `cityId`/`timeZoneId` в них нет. Этот инвариант проверяет тест.

### §29.9.4 Тесты (`CompanyManagePage.test.tsx`, новый `describe`)

Моки: `companiesApi.getMy/update/uploadLogo`, `companyAddressApi` (модуль `src/api/companyAddress.ts`), `citiesApi`.

| ID | Проверка |
|---|---|
| V29-01 | четыре группы по доступному имени: `getByRole('group', { name: 'Основное' })` и т. д.; каждое поле `within` своей группы (Название и Описание — «Основное»; Телефон и Email — «Контакты»; Адрес, «Город и часовой пояс», Яндекс и 2ГИС — «Адрес и карты»; горизонт, окно и три галочки — «Запись») |
| V29-02 | «Сохранить изменения» → ровно один вызов `companiesApi.update` без ключей `cityId`/`timeZoneId`/`address`; `companyAddressApi` не вызывается |
| V29-03 | «Сохранить» города → один вызов `update` с телом ровно `{ cityId, timeZoneId }`; основная форма не отправлена (второго вызова нет) |
| V29-04 | Enter в поле IANA и в поле города при закрытом списке → `update` не вызывается |
| V29-05 | ввод в «Название», затем сохранение адреса (инвалидирует `['my-companies']`) → введённое значение на месте, «Сохранить изменения» активна (если такого теста ещё нет; нынешний `describe` цикла 13 проверяет refetch — не дублировать, а дополнить сценарием с адресом) |
| V29-06 | ошибка 400 ссылки карт — у своего поля (`aria-describedby`) внутри группы «Адрес и карты» |

Существующие тесты `SettingsTab` проходят без правки проверок. Если тест завязан на старую разметку, правка
описывается в отчёте как вынужденная (SPEC).

---

## §29.10. Смена города магазина (T-29-05, C26-3)

Разбор кода (`frontend/goods/src/components/profile/ShopProfileSection.tsx`):
- `offsetChanged = cityChanged && city.utcOffsetMinutes !== shop.utcOffsetMinutes` (стр. 112);
- блок `!shop.timeZoneChangeAllowed` срабатывает **только внутри** `if (offsetChanged)` (стр. 123–127);
- при `shop.utcOffsetMinutes == null` любое смещение города «другое». Это совпадает с сервером («пояс не распознан →
  смещения разные», `API_CONTRACT_CYCLE26.md` §563 п. 6).

Интерфейс уже не строже сервера. C26-3 описывает риск («если UI опирается только на флаг»), который в коде не
реализовался. Решение: **DTO не менять** (новое поле в `ShopManageDto` дублировало бы правило), смысл флага
зафиксирован в `API_CONTRACT_CYCLE29.md` §29.24, `ShopTimeZoneChangePolicy` остаётся единственным источником.
Доработка кода — только комментарий у `offsetChanged` со ссылкой на §29.24, чтобы следующий редактор не заменил
сравнение смещений проверкой флага.

Тесты (`ShopProfileSection.test.tsx`, отдельный `describe('T-29-05 …')`; часть уже покрыта тестом «with
timeZoneChangeAllowed=false…», новые тесты его не дублируют, а разводят случаи):

| ID | Сценарий | Ожидание |
|---|---|---|
| V29-10 | `timeZoneChangeAllowed=false`, город с тем же смещением | диалога нет, `update` вызван с `{ cityId }`, после 200 — «Сохранено», ошибки у города нет |
| V29-11 | `false`, город с другим смещением | `timeZoneChangeLockedText` **дословно** в `role="alert"` у города, `update` не вызван |
| V29-12 | `false`, `utcOffsetMinutes: null` у магазина | как V29-11 |
| V29-13 | `true`, другой пояс, подтверждение → сервер 409 с текстом | текст ответа у поля города (регресс CY26-42 на стороне UI) |

Функционального `CY29-` нет: DTO не менялся (SPEC: «если меняется DTO — функциональный»). Серверная часть покрыта
CY26-40…45.

---

## §29.11. Встраиваемая форма: фото и карты (US-29-07, P2, режется первой)

`frontend/src/pages/EmbedPage.tsx`, только ветка салона (`kind !== 'Orders'`):
- под компактной шапкой, до списка услуг: `{company.photos?.length ? <div className="mb-4"><CompanyPhotoGallery
  photos={company.photos} companyName={company.name} /></div> : null}`;
- в шапке под адресом: `<CompanyMapLinks yandexUrl={company.yandexMapsUrl} twoGisUrl={company.twoGisUrl} />`.
  Компонент сам ничего не рисует без ссылок и уже ставит `target="_blank"` + `rel="noopener noreferrer"`. Внутри
  iframe ссылка откроется в новой вкладке верхнего окна;
- импорты относительные (`../components/company/...`);
- в шапке текстовому блоку добавить `min-w-0 flex-1`, у `h1` — `break-words`: иначе длинное название на 360 px
  выходит за контейнер (вероятный дефект US-29-05).

Данные уже приходят в `GET /api/companies/{slug}` (`photos`, ссылки карт), запросов +0 (`API_CONTRACT_CYCLE29.md`
§29.25). Высота сниппета 700 (`WidgetCard`) не меняется: лишний контент прокручивается внутри iframe. Логика
`BookingModal`, кнопок услуг и подвала не трогается.

Тесты (`EmbedPage.test.tsx`): V29-20 — при `photos` из двух элементов и обеих ссылках есть галерея (регион или
изображение по `alt` из `CompanyPhotoGallery`) и две ссылки с `target="_blank"`; V29-21 — при `photos: []` и без ссылок
нет ни галереи, ни ссылок, ни пустой обёртки (`container` не содержит `.mb-4`-обёртки галереи); V29-22 — кнопка
«Записаться» по-прежнему открывает `BookingModal`.

---

## §29.12. Узкий экран 360 px (US-29-05)

**Процесс.** Проверяет QA в devtools на 360×740 по таблице экранов SPEC, после влития FE-1…FE-3 (и FE-6/FE-7, если они
делаются). Данные для проверки — отдельный набор через API стенда разработчика: длинное название (≥ 40 символов;
одним словом и с пробелами), длинный адрес, телефон, email, обе ссылки карт, с логотипом и без, с галереей и без.
Каждый дефект QA записывает кейсом `M29-` с вердиктом «дефект» и передаёт фронту (FE-8). Исправленный дефект
перепроверяется, в отчёт идёт «экран — что было — что стало».

**Подозрения по коду** — проверить первыми, это не готовый список дефектов:

| # | Где | Что | Исправление, если подтвердится |
|---|---|---|---|
| S1 | `SettingsTab`: телефон/email | `grid grid-cols-2` на любой ширине | уходит с US-29-01 (`sm:grid-cols-2`) |
| S2 | любые новые `fieldset` | `min-inline-size: min-content` | `min-w-0` (уже в §29.9.2) |
| S3 | `EmbedPage`, шапка | текстовый блок без `min-w-0`, `h1` без переноса | §29.11 |
| S4 | `WidgetCard` (настройки салона) | блок кода сниппета — длинная строка без переноса | `overflow-x-auto` на самом блоке кода или `break-all`, чтобы прокручивался блок, а не страница |
| S5 | каталоги, сетка `minmax(320px, 1fr)` при `px-4` | запас 8 px, любой внутренний `min-width` его съест | проверить `scrollWidth`; при переполнении — `minmax(min(320px,100%),1fr)` |
| S6 | `CompanyCard`, `h1 text-[30px]` одним словом | `break-words` есть, проверить на 40 символов подряд | при переполнении — `[overflow-wrap:anywhere]` |
| S7 | `CityCombobox`, выпадающий список | ширина списка и длинные подписи «Город, регион» | `max-w-full`, перенос строки в пункте |

Правка, найденная на 360 px, не должна ломать 1280 px (контрольная точка SPEC).

---

## §29.13. ESLint по goods (T-29-01)

Цель: `cd frontend && npx eslint goods --max-warnings 0` — чисто. Правила глобально не отключаются. Исправления по
существу:
- `@typescript-eslint/no-explicit-any` → реальный тип или `unknown` с сужением;
- `no-unused-vars` → удалить, а у намеренно неиспользуемых аргументов — префикс `_` (это уже разрешено конфигом);
- `react-refresh/only-export-components` → вынести константы и хелперы из файла компонента в соседний `*.ts`. Если
  вынос ломает структуру без пользы — `// eslint-disable-next-line react-refresh/only-export-components -- <почему>`.

Проверка: `npm run lint` — 0 ошибок, число предупреждений вне `goods` не выросло (QA сравнивает с базовой линией на
`b9c2a79`); `tsc -p tsconfig.goods.json`, `test:run`, `build:goods` — зелёные. Предлагаемое усиление на будущее —
флаг `--max-warnings 0` для `goods` в CI. В этом цикле **не** включается: SPEC этого не требует, а шаг `Lint` общий.

---

## §29.14. Тесты и QA

Базовую линию QA снимает на `b9c2a79` до начала изменений (юнит / функциональные / vitest / число предупреждений
ESLint вне `goods`). Финальные числа не ниже базовой линии (T-29-08).

### §29.14.1 Функциональные (`ServiceBooking.Tests`, атрибут `[TestCase("CY29-xx")]`)

| ID | Файл | US / T | Проверка |
|---|---|---|---|
| CY29-01 | `Cycle29MalformedFormTests` | US-29-04 | владелец, `POST …/photos`, multipart: валидный `file` + поле `file[` → 400, тело дословно §29.23.1, `Content-Type` text/plain; в БД 0 фото; в `Storage:PublicRoot/companies` файлов не прибавилось |
| CY29-02 | 〃 | US-29-04 | владелец, `POST …/logo` с полем `file[` → 400; `Company.LogoUrl` прежний, файлов не прибавилось |
| CY29-03 | 〃 | US-29-04 | маршрут без файлов: `PUT /api/companies/{id}` телом multipart с полем `name[` → 400; компания не изменилась |
| CY29-04 | 〃 | US-29-04 | аноним, `POST …/photos` с битой формой → **401** |
| CY29-05 | 〃 | US-29-04 | владелец чужой компании и случайный `Guid` → оба 400 с одинаковым телом (не оракул) |
| CY29-06 | 〃 | US-29-04 (О-1) | `GET /api/goods/catalog?a[=1` → 400, текст для строки запроса |
| CY29-07 | 〃 | US-29-04 | регресс: валидный `file` + допустимое поле `meta[x]=1` → 201 (скобки как таковые не запрещены) |
| CY29-10 | `Cycle29CatalogLogoTests` (`CatalogTestFactory`) | US-29-02 | после `POST …/logo` в каталоге `logoUrl` == `StorefrontDto.logoUrl` == `ShopManageDto.logoUrl` |
| CY29-11 | 〃 | US-29-02 | магазин без логотипа → ключ `logoUrl` есть, значение `null` |
| CY29-20 | `Cycle29ShopGalleryQuotaTests` | T-29-07 | бесплатный тариф «Заказы»: 10 разных фото → 201 ×10; 11-е → 400 «В галерее магазина может быть не больше 10 фотографий» |
| CY29-30…33 | `Cycle29ContractConformanceTests` | T-29-02 | §29.7.3 |

Юнит (`ServiceBooking.UnitTests`):
- `MalformedKeyGuardValueProviderFactoryTests`: на реальных `JQueryFormValueProviderFactory` и
  `JQueryQueryStringValueProviderFactory` с `DefaultHttpContext` ключ `file[` → `ValueProviderException` с текстом;
  допустимые `a[b]`, `ids[]` → провайдер добавлен; исключение фреймворка, отличное от `ArgumentException`, проходит
  без изменений; `Install` заменяет обе фабрики на тех же индексах и бросает исключение, если какой-то из них нет.
  **Этот тест первым подтверждает, какие ключи бросают исключение** на 8.0.11. CY-тесты используют только
  подтверждённые ключи;
- `ModelValidationErrorFormatterTests`: ошибка `ModelState` с текстом `RequestTexts.*` → 400 с этим текстом; прочие
  ошибки → прежний текст.

Сверка валидатора — `OpenApiContractValidatorTests` (§29.7.2).

### §29.14.2 Vitest

| ID | Файл | Проверка |
|---|---|---|
| V29-30 | `src/utils/companyInitial.test.ts` | таблица §29.8.1 |
| V29-31 | `src/components/company/CompanyLogoMark.test.tsx` | картинка при `logoUrl` (`alt=""`, `width`/`height`, `loading="lazy"` только у `catalog`); буква без `logoUrl`; буква после `fireEvent.error(img)`; при смене `logoUrl` снова картинка |
| V29-32 | `goods/src/pages/CatalogHomePage.test.tsx` (новый) | в `ShopRow` картинка при `logoUrl`, буква без него, буква после ошибки картинки; `aria-label` ссылки прежний |
| V29-33 | `src/pages/HomePage.test.tsx` (P2) | буква вместо иконки, `alt=""` у логотипа |
| V29-01…06 | `CompanyManagePage.test.tsx` | §29.9.4 |
| V29-10…13 | `ShopProfileSection.test.tsx` | §29.10 |
| V29-20…22 | `EmbedPage.test.tsx` (P2) | §29.11 |
| — | `CompanyCard` (существующие тесты) | зелёные без правки |

### §29.14.3 Ручные `M29-` (раздел «Цикл 29» в `TEST_CATALOG.md`, формат `M27-`)

M29-01 ezbook `/company/:slug` (3 состояния); M29-02 goods `/:slug` (3 состояния + «Часы работы»); M29-03 goods `/` и
`/city/:id` (с логотипом, без, битый URL логотипа); M29-04 ezbook `/` (с логотипом и без); M29-05 goods
`/cabinet/:shopId/settings` (пустой и заполненный профиль, ошибка у поля карты); M29-06 ezbook настройки салона (то же
плюс пары в одну колонку на 360 и в две на 1280); M29-07 `/embed/:slug` (если US-29-07); M29-08 контрольная точка
1280 px по всем экранам; перепроверка M27-02. Критерии каждого кейса — пять пунктов US-29-05 (scrollWidth, логотип,
переносы, зона нажатия ≥ 44 px, колонки). Визуальные истории цикла 26, которые закрыты только осмотром (T-29-03),
получают свои `M29-` с пометкой «вместо vitest».

### §29.14.4 T-29-03, T-29-04, T-29-08

- **T-29-03** (QA): для каждого из 24 ID `CY26-*` найти тест (`grep -rn "CY26-" ServiceBooking.Tests`). ID без теста →
  тест в этом цикле: пишет QA; если нужен продуктовый код — задача BE/FE. Атрибут `CY26-*` без строки в каталоге →
  строка в каталоге. Визуальные US-26-04/05/06/08 сопоставить с vitest-файлами. Итоговая строка в разделе цикла 26:
  «сверено в цикле 29: N ID, M тестов, дописано K».
- **T-29-04** (codebase-analyst, по отчёту QA): §7.1 `CURRENT_STATE.md` — фактические числа с хешем и датой
  финального коммита; закрытие C26-1, C26-3 (как «не дефект, закреплено V29-10…13»), C26-4, C26-5, C26-7.
- **T-29-08** (QA): `dotnet build -warnaserror`, юнит, функциональные, `npm run lint`, оба `tsc`, `test:run`,
  `build:release`; `Cycle22RouteTable.golden.txt` без diff.

---

## §29.15. Структура проекта — что добавляется и меняется

```
contracts/
  cycle26/openapi.json                         + производный (redocly bundle), в git
  cycle29/openapi.yaml                         + контракт цикла (архитектор, уже в ветке)
  cycle29/openapi.json                         + производный, в git
  redocly.yaml                                 ~ комментарий-перечень (26, 29)
.github/workflows/ci.yml                       ~ §29.7.4
ServiceBooking.API/
  DTOs/Catalog/CatalogDtos.cs                  ~ GoodsCatalogShopDto + LogoUrl
  Services/Shops/GoodsCatalogService.cs        ~ CatalogEntry + LogoUrl, BuildAsync, комментарий класса
  Services/RequestTexts.cs                     + тексты §29.4.2
  Services/MalformedKeyGuardValueProviderFactory.cs  + обёртка и Install
  Services/ModelValidationErrorFormatter.cs    ~ проброс текстов RequestTexts
  Startup/ApiExtensions.cs                     ~ Install в AddControllers
  Services/Companies/CompanyDtoAssembler.cs    ~ (P2) удалить GetPhotosOrderedAsync
  Controllers/CompaniesController.cs           ~ (P2) GetBySlug → CompanyPhotoQueries.OrderedAsync
ServiceBooking.UnitTests/
  MalformedKeyGuardValueProviderFactoryTests.cs  +
  ModelValidationErrorFormatterTests.cs          + (или дополнить, если есть)
ServiceBooking.Tests/
  Infrastructure/OpenApiContract.cs            + валидатор
  Tests/OpenApiContractValidatorTests.cs       +
  Tests/Cycle29MalformedFormTests.cs           +
  Tests/Cycle29CatalogLogoTests.cs             +
  Tests/Cycle29ShopGalleryQuotaTests.cs        +
  Tests/Cycle29ContractConformanceTests.cs     +
frontend/
  package.json                                 ~ types:api:cycle29, contracts:json
  src/types/api-cycle29.generated.ts           + генерат (руками не править)
  src/utils/companyInitial.ts (+ .test.ts)     +
  src/components/company/CompanyLogoMark.tsx (+ .test.tsx)  +
  src/components/company/CompanyCard.tsx       ~ CompanyLogoMark
  src/pages/HomePage.tsx (+ test)              ~ (P2) CompanyLogoMark
  src/pages/EmbedPage.tsx (+ test)             ~ (P2) галерея, ссылки карт
  src/pages/owner/CompanyManagePage.tsx (+ test)  ~ SettingsTab группы, CityTimeZoneFields
  goods/src/types.ts                           ~ каталог из генерата 29
  goods/src/pages/CatalogHomePage.tsx          ~ ShopRow → CompanyLogoMark
  goods/src/pages/CatalogHomePage.test.tsx     +
  goods/src/components/profile/ShopProfileSection.tsx (+ test)  ~ комментарий; тесты T-29-05
  goods/**                                     ~ правки ESLint (T-29-01)
API_DOCUMENTATION.md                           ~ logoUrl в каталоге goods, 400 на битую форму (§6 кодов ответа)
TEST_CATALOG.md                                ~ «Цикл 29» (CY29-, M29-), сверка «Цикла 26»
CURRENT_STATE.md, CHANGELOG.md, README.md      ~ по ролям SPEC §9
```

---

## §29.16. Разбивка работ и параллельность

### §29.16.1 Задачи

**DevOps** (первым, ~1 ч; разблокирует BE-3 и FE-0):
- **DO-1 (P0)** — закоммитить архив `SPEC_CYCLE27_GOODS_BUSINESS_BLOCK.md` вместе со спекой цикла и документами
  архитектора. Добавить в `frontend/package.json` скрипты `types:api:cycle29` (по образцу `types:api:cycle26`) и
  `contracts:json` (§29.7.1). Выполнить оба, закоммитить `api-cycle29.generated.ts` и два `openapi.json`. Внести в
  `ci.yml` изменения §29.7.4 и обновить комментарий `redocly.yaml`. Прогнать `redocly lint` для 26 и 29 локально;
  если lint цикла 26 найдёт предсуществующие ошибки, исправить их в YAML (форму не менять) и перегенерировать.

**Backend:**
| Задача | Приоритет | Зависит от | Что |
|---|---|---|---|
| BE-1 | P0 | — | §29.4 целиком + юнит-тесты + CY29-01…07. **Начать с юнит-теста на реальной фабрике** (подтверждение механизма и ключей) |
| BE-2 | P0 | — | §29.3, §29.5 + CY29-10, CY29-11 |
| BE-3 | P0 | DO-1 (или локально `npm run contracts:json`) | §29.7.2–§29.7.3: валидатор, самопроверка, CY29-30…33; исправление найденных расхождений по §29.26 |
| BE-4 | P1 | — | CY29-20 (T-29-07). При тарифном отказе — стоп и находка в отчёт |
| BE-5 | P2 | — | T-29-06 (§29.6) |
| BE-6 | P0 | BE-1, BE-2 | `API_DOCUMENTATION.md`: `logoUrl` в разделе каталога goods (подраздел «Цикл 29 (unreleased)»), 400 на битую форму в §6 «Справочник кодов ответа» |

**Frontend:**
| Задача | Приоритет | Зависит от | Что |
|---|---|---|---|
| FE-0 | P0 | DO-1 (или сам запускает `types:api:cycle29`) | `goods/src/types.ts`: каталог на генерат 29 |
| FE-1 | P0 | — | `companyInitial`, `CompanyLogoMark`, перевод `CompanyCard`; V29-30, V29-31 |
| FE-2 | P0 | FE-0, FE-1 | `ShopRow` (§29.8.3); V29-32 |
| FE-3 | P0 | — | `SettingsTab` (§29.9); V29-01…06 |
| FE-4 | P0 | — | T-29-01 ESLint goods (§29.13). Делать **последним из P0 фронта** или после влития FE-2, чтобы не править одни и те же goods-файлы дважды |
| FE-5 | P1 | — | T-29-05: комментарий + V29-10…13 |
| FE-6 | P2 | FE-1 | US-29-03 `HomePage`; V29-33 |
| FE-7 | P2 | — | US-29-07 `EmbedPage`; V29-20…22 |
| FE-8 | P0 | QA-2 | исправления дефектов 360 px |

**QA:**
| Задача | Когда | Что |
|---|---|---|
| QA-0 | сразу | базовая линия на `b9c2a79` (все числа §29.14 + предупреждения ESLint вне `goods`) |
| QA-1 | сразу, параллельно всем | T-29-03 (сверка `CY26-*`) |
| QA-2 | после FE-1…FE-3 (+FE-6/7) | US-29-05, кейсы `M29-`, дефекты → FE-8 → перепроверка |
| QA-3 | в конце | T-29-08, раздел «Цикл 29» в `TEST_CATALOG.md`, отчёт с числами для T-29-04 |

**После QA-3:** codebase-analyst — T-29-04; product-analyst — CHANGELOG (включая C26-7 и уточнение про логотипы
ezbook) и README («Вызов 2»).

### §29.16.2 Что идёт параллельно, а что последовательно

```
день 1:  DO-1 ──┐
                ├─► BE-3 ───────────────┐
         BE-1 ──┼───────────────────────┤
         BE-2 ──┤  BE-4   BE-5          ├─► BE-6
                │                       │
                ├─► FE-0 ─► FE-2 ─┐     │
         FE-1 ──┴───────► FE-6    ├─► QA-2 ─► FE-8 ─► QA-2(перепроверка) ─┐
         FE-3 ────────────────────┘                                       ├─► QA-3 ─► T-29-04, CHANGELOG/README
         FE-5   FE-7(P2)   FE-4 (после FE-2)                              │
         QA-0 ─► QA-1 ────────────────────────────────────────────────────┘
```

- **Полностью параллельно:** BE-1, BE-2, BE-4, BE-5 и все FE, кроме FE-2, FE-6, FE-8. Бэкенд и фронт друг друга
  не ждут: форма `logoUrl` зафиксирована в YAML, фронт проверяет её на prism-моке или на vitest-фикстурах.
- **Последовательно:** DO-1 → FE-0 → FE-2; DO-1 → BE-3; FE-1 → FE-2/FE-6; FE-1…FE-3 → QA-2 → FE-8; всё → QA-3.
- **Точки синхронизации:** S0 — DO-1 влит (скрипты и генерат в ветке); S1 — BE-2 и FE-2 влиты, QA проверяет живой
  каталог с логотипом (CY29-10 + осмотр); S2 — список дефектов 360 px передан фронту; S3 — финальный прогон.
- **Конфликты файлов:** `package.json` правит только DO-1. Если FE-0 начинает раньше, он добавляет **только**
  `types:api:cycle29`, а DO-1 потом делает rebase. `CompanyManagePage.tsx` правят FE-3 и, по S4 из §29.12, FE-8 —
  строго последовательно. goods-файлы правят FE-2, FE-4, FE-5 — FE-4 последним.

### §29.16.3 Порядок урезания (SPEC §1)

US-29-07 (FE-7) → US-29-03 (FE-6) → T-29-06 (BE-5) → T-29-07 (BE-4) → T-29-05 (FE-5). P0 не режутся. Если FE-7 не
делается, строка `/embed/:slug` в US-29-05 и кейс M29-07 пропускаются.

---

## §29.17. Риски

| # | Риск | Вероятность / влияние | Решение |
|---|---|---|---|
| R1 | Механизм §29.4 на ASP.NET Core 8.0.11 ведёт себя иначе, чем по чтению исходников (например, `ValueProviderException` не доходит до `ModelState`) | низкая / среднее | первый юнит-тест BE-1 на реальной фабрике и CY29-01 это покажут; запасной путь — §29.4.2 |
| R2 | Будущее обновление ASP.NET Core переименует или уберёт JQuery-фабрики, и защита тихо исчезнет | низкая / среднее | `Install` падает на старте, если фабрики не найдены (fail-fast, конвенция проекта) |
| R3 | 400 на битую форму раньше 403/404 сочтут оракулом | — | не зависит от `{id}` (§29.4.3), закреплено CY29-05 |
| R4 | Сверка T-29-02 найдёт много расхождений контракта 26 с кодом (R29-3) | средняя / среднее | правка по §29.26; если расхождений больше пяти или они требуют менять поведение фронта, BE-3 фиксирует список, чинит только то, что ломает форму для фронта, остальное — находкой в отчёт и пунктом в CURRENT_STATE |
| R5 | Валидатор сам с ошибкой, и «зелёная» сверка ничего не доказывает | низкая / высокое | самопроверка на каждое ключевое слово; падение на неизвестном ключевом слове; CY29-30 требует ≥ 2 фото и email |
| R6 | `openapi.json` разойдётся с YAML | средняя / низкое | CI-шаг `git diff --exit-code` (§29.7.4) |
| R7 | Перекомпоновка `SettingsTab` сломает ресинк формы (урок цикла 13) | средняя / высокое | меняется только разметка (§29.9.1), V29-05, существующие тесты без правки проверок; иначе — сужение по R29-1 |
| R8 | Enter в поле блока города внутри `<form>` отправит основную форму | высокая без меры / среднее | `type="button"` + `swallowImplicitSubmit` (§29.9.3), V29-04 |
| R9 | Ленивые логотипы в каталоге дают сдвиг вёрстки | низкая | фиксированные `width`/`height` + бокс фиксированного размера |
| R10 | 360 px без автоматического регресса (R29-2) | принят | кейсы `M29-`; e2e — отдельное решение заказчика |
| R11 | Встраиваемая форма показывает фото салона на чужих сайтах (правовая память SPEC §8, L21) | — | в этом цикле юрист не зовётся; пункт уходит в CHANGELOG «Что стоит прочитать до выката» (product-analyst) |
| R12 | Параллельная ветка 28 при влитии тронет `CURRENT_STATE.md`/`TEST_CATALOG.md` | средняя / низкое | сводит codebase-analyst по смыслу (`CURRENT_STATE.md` §0); § цикла 29 с префиксом не конфликтуют |

Секреты, авторизация, масштабирование: цикл их не затрагивает. Новых переменных окружения и секретов нет, права
маршрутов не меняются. Нагрузку каталог не добавляет: поле читается тем же запросом, логотипы грузятся лениво из
nginx-статики `/uploads`.

---

## §29.18. Отклонения от буквы SPEC и решения сверх неё (читать обязательно)

- **О-1. 400 и для строки запроса.** SPEC говорит о теле формы. `JQueryQueryStringValueProviderFactory` падает на
  тех же ключах тем же `ArgumentException` (`?a[=1` → 500 на любом маршруте с параметрами, включая анонимный каталог
  goods). Защита ставится той же обёрткой, у неё свой текст (§29.4.2). Цена — одна строка в `Install` и один тест
  (CY29-06). Без этого «общее правило» US-29-04 оставило бы соседний 500.
- **О-2. Строгий режим валидатора.** SPEC: «лишних полей быть не должно, если схема это запрещает». В контракте 26 у
  `StorefrontDto`/`ShopManageDto` нет `additionalProperties: false`, но в шапке они объявлены описанными «ЦЕЛИКОМ».
  Валидатор считает лишнее поле ошибкой везде, где нет явного `additionalProperties: true` (§29.7.2). Если это даст
  расхождения в унаследованных из цикла 24 вложенных схемах, они исправляются в `contracts/cycle26/openapi.yaml` по
  §29.26, а не отключением режима.
- **О-3. T-29-05 без изменения DTO и без функционального теста.** Кабинет уже сравнивает смещения (§29.10). Меняются
  только тесты и комментарий. SPEC допускает этот вариант («сравнивать смещения на клиенте»).
- **О-4. `onError` → заглушка и в `CompanyCard`.** SPEC требует это для каталогов. Единый компонент даёт то же
  поведение и в шапке карточки. Это краевой случай, который SPEC разрешает менять (A29-4).
- **О-5. `min-w-0 flex-1` у блока названия в карточках каталогов.** Это профилактика US-29-02 («логотип не
  сжимается и не наезжает на название»), визуально на обычных данных ничего не меняется.
- **О-6. Нумерация § с префиксом цикла** вместо сквозной (A29-5).
