# CURRENT_STATE — фактическое состояние кодовой базы ServiceBooking

**Актуально по состоянию на коммит: `242c7d9`, дата: 2026-09-23.**

Документ описывает **что есть в репозитории сейчас**, без предложений по развитию.

📸 **Точка отсчёта этой редакции. Ветка — `develop`, HEAD — `242c7d9`** (мёрж-коммит
`Merge cycle/10-master-booking-history-photo into develop`, CI зелёный), рабочее дерево чистое.
Редакция записывает **цикл 10 «свобода ручной записи, история изменений записи, фото салона»**
поверх редакции цикла 7.

📸 **Диапазон изменений этой редакции (цикл 10): `bd3be3f..242c7d9`, 25 коммитов, +14 352 / −1 144
строк, 73 файла.** Полный список — `git log --oneline bd3be3f..242c7d9`. Документ обновлялся
**точечно по диапазону**: разделы, которых цикл 10 не касался, остались в редакции цикла 7 (и глубже
— циклов 6, 8 и 5). **Отсчитывайте следующий diff от `242c7d9`.**
⚠️ Между `0929b48` (шапка прошлой редакции) и `bd3be3f` (базовая точка цикла 10) лежат ещё три
коммита: две правки скриптов и текстов деплоя (`96b68df`, `bd3be3f`) и запись цикла 7 в
`CHANGELOG.md` (`5e9c723`). Продуктового кода они не трогают, поэтому в этой редакции не описаны.

📸 **Главное в цикле 10 — три блока:**
- **Блок A: один экран записи вместо двух.** `ManualBookingModal` **удалён**, его способности
  поглощены `BookingModal`; выбор даты везде — помесячный `BookingCalendar`. Персонал получил
  свободу записи на любую будущую дату и любое свободное время: `GET /api/bookings/availability`
  научился `extendedHours`, дни без графика и выходные для персонала **нажимаются**, сохраняя подпись.
- **Блок B: журнал изменений записи.** Новая append-only таблица `BookingEvent`, новый эндпоинт
  `GET /api/bookings/{id}/history`, панель истории в списке записей персонала. Клиенту журнал не
  показывается ни в каком виде — это внутренняя информация персонала (решение заказчика П8).
- **Блок C: фотогалерея салона.** Новая таблица `CompanyPhoto`, четыре эндпоинта фотографий,
  управление в настройках компании и показ на публичной карточке вместо серой заглушки.

**Две новые миграции, обе чисто добавочные** — ни одной миграции данных, ни одного `NOT NULL` на
существующей таблице (§3).

📸 **Правило цикла 10, которое легче всего нарушить в следующем цикле — запишите его себе.**
Намерение «записываю клиента» против «записываюсь сам» задаётся **точкой входа** (и переключателем
в форме), а **не** `staffMode`. `staffMode` управляет **только** свободой выбора даты и времени.
Вывод намерения из `staffMode` уже один раз привёл к блокеру внутри цикла: запись сотрудника
**самому себе** становилась ничьей — `ClientId = null`, без предоплаты и без согласий. В коде это
закреплено явным состоянием `bookForClient` (`frontend/src/components/booking/BookingModal.tsx:103`,
починка — коммит `4f8ed63`), см. §6.

📸 **Числа прогонов на `242c7d9`** (автор этого документа работает только на чтение и тесты не
запускает; числа получены от того, кто выполнял прогон, и сходятся со статическим подсчётом
атрибутов):

| Команда | Результат | Было в редакции цикла 7 |
|---|---|---|
| `dotnet test ServiceBooking.Tests` | 📸 **603** (нужен Docker, §7.2) | 549 |
| `dotnet test ServiceBooking.UnitTests` | 📸 **942** | 908 |
| `npm run test:run` (в `frontend/`) | 📸 **369**, 57 файлов | 341 / 53 |

Статический пересчёт на `242c7d9` сходится: 626 `[Fact` + 316 `[InlineData]` = **942** (юнит),
586 + 16 = 602 (функциональные — расхождение на единицу с приёмочными **603** всё то же, `[Fact`
внутри комментария в `LegalConsentVersionChangeTests.cs`), **369** вызовов `it(...)` в **57** файлах
(фронт).

💳 Точка отсчёта этой редакции. Ветка — **`develop`**, HEAD — `0929b48`, рабочее дерево чистое.
Редакция записывает **цикл 7 «биллинг-аккаунты и модель тарифов»** поверх редакции цикла 6.

💳 **Диапазон изменений этой редакции (цикл 7): `aac6231..0929b48`, 141 коммит, +47 951 / −1 208
строк, 168 файлов.** Полный список — `git log --oneline aac6231..0929b48`. Документ обновлялся
**точечно по диапазону**: разделы, которых цикл 7 не касался, остались в редакции цикла 6 (и глубже
— циклов 8 и 5).

⚠️ **Про точку отсчёта — важно для следующего, кто будет считать diff.** Задача на эту редакцию
пришла с указанием отсчитывать от `7a36543` и «отразить заодно циклы 5, 6 и 8». Это **неверно**:
`7a36543` — точка отсчёта редакции **цикла 5**, и циклы 5, 8 и 6 в этом документе уже записаны
(прошлыми редакциями, последняя из них — `a676899`, она и проставила в шапке `aac6231`). Diff от
`7a36543` (293 коммита, 492 файла) заставил бы переписать заново уже описанное. Фактически
неописанным оставался **только** диапазон `aac6231..HEAD`, и он целиком состоит из цикла 7 — чужих
циклов в нём нет. Проверка, по которой это установлено: `git log --oneline aac6231..HEAD -- CURRENT_STATE.md`
(один коммит, цикл 6) и `grep -ci "цикл 7\|BillingAccount" CURRENT_STATE.md` (было 0 при 37/51/41
упоминаниях циклов 5/6/8). **Отсчитывайте следующий diff от `0929b48`.**

💳 **Главное отличие от редакции цикла 6: единица тарификации переехала с человека на биллинг-аккаунт.**
У компании стало две независимые оси: `BillingAccountId` — кто платит, `OwnerUserId` — кто управляет.
Смена ответственного за компанию больше не трогает деньги и не отвязывает номер уведомлений; продажа
филиала — отдельная админская операция переноса компании между аккаунтами. Появились каталог опций,
матрица «тариф × опция», подписка аккаунта, экран владельца «Ваша подписка», публичная витрина цен за
выключенным рубильником, экран биллинг-аккаунтов в админке и очередь заявок. **11 новых миграций**
(включая необратимую `AddCoTenancyConstraints`), 4 новых контроллера, 5 новых сущностей.
⚠️ **Код в `develop`, но на стенде его нет** — деплой упал на миграциях и откачен, см. §8 и §9 B1.

🗓 Точка отсчёта прошлой редакции (цикл 6). Ветка — **`cycle/06-booking-fixes`** (цикл 6, готов к вливанию в
`develop`), HEAD — `aac6231`, рабочее дерево чистое (кроме самого этого файла в момент его правки).
⚠️ **Важно про порядок циклов.** Цикл 6 делался **параллельно** с циклами 5 и 8, и они влились в
`develop` раньше. При мёрже `develop` в ветку цикла 6 этот файл взят из `develop` **целиком** —
он свежее; правка цикла 6, которая в нём была (одно правило про изоляцию тестовой базы), циклом 8
решена системно и лучше. Поэтому ниже разделы циклов 5 и 8 оставлены как есть, а цикл 6 **дописан
поверх их редакции**. Хронология в документе не линейна: «прошлая редакция» в тексте ниже означает
цикл 8, хотя номер цикла меньше.
Модель веток прежняя: `master` ← `release-candidate` ← `develop` ← `cycle/NN-<слаг>`
(см. §0.0 DEPLOY.md). `develop` — интеграционный ствол и одновременно **та самая ветка, которая
развёрнута на боевой машине**: `master` отстаёт, тегов нет, релиз формально не объявлен.

🗓 **Диапазон изменений этой редакции (цикл 6): `14a7fb6..aac6231`, 76 коммитов, +19 364 / −692
строк, 122 файла.** Из них **67 — сам цикл 6** «доработка сценария записи на услуги» (истории
US-60…US-67, ветка `cycle/06-booking-fixes`), остальные девять — хвосты чужой работы, доехавшие
той же веткой: пять коммитов `T9 *` (закрытие неблокирующих находок ревью цикла 8 — см. §9 T8-2),
две правки документов состояния, `.gitignore` и сам мёрж `develop`. Полный список —
`git log --oneline 14a7fb6..aac6231`.
Документ обновлялся **точечно по диапазону**: разделы, которых цикл 6 не касался, остались в
редакции цикла 8 (и, глубже, цикла 5).

🗓 **Главное отличие от редакции цикла 8: цикл 6 — противоположность ему по характеру.** Цикл 8
почти не трогал продуктовый код; цикл 6 трогает **только** его. Изменились: вход и регистрация
(различимые причины отказа), телефон (жёсткий российский формат при создании новых данных),
членство в компании (признак «оказывает услуги»), подписки (дата окончания стала обязательной,
появилась диагностика), выбор мастера (шаг пропускается, если специалист один), **календарь
доступности месяца одним запросом**, **рабочее окно по умолчанию вместо «сутки напролёт»** и
**несколько услуг за один визит**. Четыре новые миграции, одна новая сущность, три новых эндпоинта,
шесть изменённых. Тестовой инфраструктуры цикл не касался — она осталась ровно такой, какой её
сделал цикл 8.

🔬 **Диапазон прошлой редакции (цикл 8): `7b382d8..6562a86`, 56 коммитов, +9 910 / −1 369
строк, 75 файлов.** Это **весь цикл 8 «изоляция тестовой и локальной среды»** (ветка
`cycle/08-test-env-isolation`, смёржена в `develop`, CI зелёный), обе его фазы — изоляция прогонов
друг от друга и параллелизм внутри прогона. Полный список — `git log --oneline 7b382d8..HEAD`.
Документ обновлялся **точечно по диапазону**, а не пересканированием: разделы, которых цикл 8 не
касался, остались в редакции на `7b382d8` и описывают состояние цикла 5.

🔬 **Главное отличие от прошлой редакции: продуктового кода цикл 8 почти не трогал.** Весь диф по
`ServiceBooking.API` / `Core` / `Infrastructure` / `frontend/src` — это **+27 / −2 строки в одном
файле `ServiceBooking.API/Program.cs`** (каталог логов стал конфигурируемым; Serilog в окружении
`Testing` больше не захватывает статический `Log.Logger`). HTTP-контракт, модель данных, миграции и
экраны — **без единого изменения**; правки контракта, сделанные в середине цикла, были сознательно
откачены коммитом `36d56f4` («Undo the API changes that cycle 8 promised not to make»), и сверка
идёт против зафиксированного инварианта `contracts/cycle8/servicebooking-invariant.openapi.yaml`.
Всё остальное — **тестовая и локальная инфраструктура**: новый проект `ServiceBooking.TestKit`,
своя одноразовая база на каждый тест-класс, параллельный прогон, параметризованный dev-стек.
Поэтому §3 (модель данных), §4 (что реализовано) и большая часть §5 в этой редакции **не менялись**.

Ниже — описание прошлой редакции (цикл 5, диапазон `7a36543..071fc11`, 18 коммитов,
+37 272 / −2 632 строк, 252 файла), оставленное как есть, потому что предметно ничего из него
циклом 8 не отменено.

**Главное отличие от прошлой редакции: переделан контур согласий, построенный в цикле 3.** Это не
новая функция рядом, а **переписанное работающее** — через этот контур проходит каждый пользователь
при каждом входе. `UserConsent` (одна перезаписываемая строка на пару «пользователь + документ»)
заменён на **журнал событий `ConsentRecord`**; правовых документов стало пять вместо двух, плюс шесть
невersионируемых текстов интерфейса; блокировка 451 перестала быть одной на всех и получила область
действия (`LegalGate`).

Сюда же входят: отдельная зашифрованная таблица противопоказаний клиента (`ClientHealthNote`),
обращения субъектов данных (`SubjectRequest`), **тринадцать правил уничтожения по срокам хранения** и
четвёртая фоновая задача `DataRetentionTask` (по умолчанию — **сухой прогон**), акцепт владельцем
текста об ответственности за рекламу при сохранении шаблона, ИНН и форма лица при заявке на канал,
подтверждение полномочий при записи за другого человека.

⚠️ **Функция уведомлений в WhatsApp (цикл 4) по-прежнему не выпущена** — тарифный флаг
`AllowNotificationChannel` выключен у всех планов, цена опции не задана, провайдер по умолчанию
`logging`. Цикл 5 снял часть правовых блокеров, но **не все** (§9, блок P0-цикл-5). Это **ожидаемое
состояние, а не дефект**.

⚠️ **Правовые тексты цикла 5 — каркас, а не готовые документы.** Двенадцать документов юриста лежат
в `legal-drafts/` с **пятнадцатью видами плейсхолдеров** (`{{ИНН_ОПЕРАТОРА}}`, `{{НОМЕР_УВЕДОМЛЕНИЯ_РКН}}`
и т.п.); всё, что читает приложение, помечено `isDraft: true`. До публикации нужна вычитка
практикующим юристом.

Все утверждения ниже получены чтением исходников, конфигов и git-истории. Где чего-то не нашлось —
так и написано.

🗓 **Числа прогонов на `aac6231`** (автор этого документа работает только на чтение и тесты не
запускает; числа получены от того, кто выполнял прогон на слитом дереве цикла 6, и сходятся со
статическим подсчётом атрибутов):

| Команда | Результат | Было в редакции цикла 8 |
|---|---|---|
| `dotnet build ServiceBooking.sln -warnaserror` | **0 warnings, 0 errors** | 0 / 0 |
| `dotnet test ServiceBooking.UnitTests` | 🗓 **777 / 777** | 691 / 691 |
| `dotnet test ServiceBooking.Tests` | 🗓 **488 / 488** (по правилам цикла 8, нужен Docker) | 465 / 465 |
| `npm run test:run` (в `frontend/`) | 🗓 **283 / 283**, 43 файла | 181 / 181, 32 файла |
| `npx tsc --noEmit` (в `frontend/`) | чисто | чисто |
| `npm run build` (в `frontend/`) | успешно | успешно |
| 🗓 `npx @redocly/cli lint openapi-cycle6.yaml` | валидно, **одно предупреждение про `localhost`** в `servers` | контракта не было |

Статический подсчёт на `aac6231` сходится: вызовов `it(...)` в `frontend/src` — **283** в
**43** файлах; файлов в `ServiceBooking.UnitTests` — 60 (было 53), в `ServiceBooking.Tests/Tests/` —
30 (было 29). В `ServiceBooking.Tests` атрибуты идут парой `[Fact, TestCase("ID")]`, голого `[Fact]`
там не встретить; одно вхождение `[Fact` там — внутри комментария, в счёт не идёт.
Числа промежуточных редакций (691 → 711 после закрытия находок T9 → **777** после цикла 6) в
тексте ниже местами встречаются как есть — это история, а не расхождение.

🗓 **Прирост цикла 6 распределён по всем трём наборам:** юнит +66 (чистые правила цикла:
`LoginOutcome`, `BookingHorizon`, `BookingServiceSelection`, `SubscriptionDiagnostics`,
`ModelValidationErrorFormatter`, доработки `SlotCalculator`/`PhoneNormalizer`) плюс +20 от закрытия
находок T9; функциональный +23 (в том числе новый файл `MultiServiceBookingTests.cs` и переписанные
кейсы входа и слотов); фронтенд +102 в 11 новых файлах — впервые покрыты **экраны записи**
(`BookingCalendar`, `ManualBookingModal`, `RescheduleModal`, `PhoneInput`, `LoginPage`, `AdminPage`,
`CompanyManagePage`) и пять новых утилит.

🔬 **Весь прирост цикла 8 (+100 юнит-тестов) — покрытие самой тестовой инфраструктуры**
(`ServiceBooking.TestKit`: имена баз, ключ прогона, метки, арифметика бюджета соединений,
классификация «живой/мёртвый» ресурс, разбор `--max-age`). Функциональный набор и фронт не
изменились ни на один тест — цикл менял то, **как** тесты запускаются, а не что они проверяют.
🔬 **Прогон функционального набора больше не пересоздаёт общую базу `servicebooking_test`** — её
вообще нет, каждый прогон и каждый тест-класс заводят свою одноразовую (§7).

⚖️ **Пропорция роста другая, чем в цикле 4.** Фронтенд вырос почти вдвое (+81) — впервые прирост
фронта обогнал функциональный набор (+16). Причина в том, что цикл 5 менял в основном **формы и
экраны согласий**, а не серверные алгоритмы.

---

## 1. Стек и версии

📸 **Цикл 10 стек не менял.** Диф по всем `*.csproj`, `ServiceBooking.sln` и
`frontend/package-lock.json` в диапазоне `bd3be3f..242c7d9` — **пустой**. В `frontend/package.json`
изменение ровно одно: новый скрипт `types:api:cycle10`
(`openapi-typescript ../contracts/cycle10/openapi.yaml -o src/types/api-cycle10.generated.ts`) рядом
с прежним `types:api` цикла 7. Ни одной новой зависимости, ни одного нового проекта.
⚠️ **`npm audit` на фронтенде показывает четыре уязвимости**: `axios` (high), `form-data` (high),
`react-router` (moderate, два разных совета). Они существовали **до** цикла 10, чинятся патч-версиями,
цикл их не трогал — см. §9 D5.

🗓 **Цикл 6 стек не менял вообще.** Диф по всем `*.csproj`, `ServiceBooking.sln`,
`frontend/package.json` и `frontend/package-lock.json` в диапазоне `14a7fb6..aac6231` — **пустой**:
ни одной новой зависимости, ни одного обновления версии, ни одного нового проекта. Это осознанное
решение цикла, записанное в `ARCHITECTURE_CYCLE6.md` §41 («Стек: ничего не добавляем, и вот
почему»): маска телефона, календарь месяца и мультивыбор услуг сделаны на том, что уже есть, без
`react-imask`, `react-day-picker` и им подобных.

### Бэкенд

| Что | Значение | Откуда |
|---|---|---|
| Язык / рантайм | C#, `net8.0`, `Nullable=enable`, `ImplicitUsings=enable` | все `*.csproj` |
| SDK на машине | .NET SDK 8.0.203 | `dotnet --version` |
| Фреймворк | ASP.NET Core Web API (контроллеры, minimal hosting в `Program.cs`) | `ServiceBooking.API/Program.cs` |
| ORM | EF Core 8.0.11 + `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11 | `ServiceBooking.Infrastructure/ServiceBooking.Infrastructure.csproj` |
| СУБД | PostgreSQL (в docker-compose — `postgres:16-alpine`) 🔬 версия пина сверяется скриптом `deploy/ci/check-image-pins.sh` между `TestKit/TestInfrastructure.cs`, обоими compose-файлами и `ci.yml` | `docker-compose.yml`, `docker-compose.prod.yml` |
| Аутентификация | ASP.NET Core Identity (`IdentityDbContext<AppUser>`) + JWT Bearer 8.0.11 | `Program.cs`, `Services/TokenService.cs` |
| Обработка изображений | **SkiaSharp 2.88.8** + `SkiaSharp.NativeAssets.Linux.NoDependencies` (цикл 2) — декод, ориентация по EXIF, ресайз, ре-энкод | `ServiceBooking.API.csproj`, `Services/ImageProcessor.cs` |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` (встроенный в ASP.NET Core 8), 🗓 **восемь** именованных политик: `uploads`, `auth-login`, `auth-register`, `booking-create`, `data-export`, `notifications-webhook` (600/мин на IP), ⚖️ `subject-request` (3/час на IP), 🗓 `availability` (60/мин на IP); глобального лимитера нет | `Program.cs`, секция `RateLimits` |
| 🆕 **Шифрование секретов** | **AES-GCM напрямую** (`System.Security.Cryptography`), мастер-ключ из конфигурации (`Notifications:EncryptionKey`, 32 байта base64). ASP.NET Core Data Protection **отвергнут осознанно** — его key ring в контейнере эфемерен и молча перестал бы читать ранее зашифрованные токены после передеплоя. Формат шифротекста `v1.<keyId>.<base64(nonce12‖tag16‖ct)>`, AAD — id канала. ⚖️ Цикл 5 добавил **строковую перегрузку связанных данных** (`Encrypt/Decrypt(…, string associatedData)`) и обёртку `HealthNoteProtector` над ней — **второй криптографии в проекте намеренно не заводили**: один ключ, одна процедура ротации. Обратная совместимость подтверждена ревью побайтово — старые токены каналов читаются | `Services/Notifications/SecretProtector.cs`, `ChannelKeyFingerprint.cs`, ⚖️ `Services/Legal/HealthNoteProtector.cs` |
| 🆕 **HTTP-клиент наружу** | `IHttpClientFactory`, именованный клиент `green-api` с keep-alive и **IPv4-first `ConnectCallback`**; логирование этого клиента заглушено на уровне категорий (токен в URL) | `Program.cs`, `Services/Notifications/GreenApi/GreenApiHandlerFactory.cs`, `PreferIPv4.cs` |
| 🆕 Кеш в памяти | `IMemoryCache` (`AddMemoryCache`) — кеш QR-ответа и 60-секундный кеш `PlatformSettings` | `Program.cs` |
| **Логирование** ⭐ | **Serilog.AspNetCore 8.0.3** (`CompactJsonFormatter` в stdout и `logs/app-.json`) + маскирование телефонов | `Program.cs`, `Services/LogMasking.cs`, `PhoneMaskingEnricher.cs` |
| **Трекер ошибок** ⭐ | **Sentry.Serilog 4.13.0** — синк включается только при непустом `Sentry:Dsn`; целевой приёмник — self-hosted **GlitchTip** (Sentry-совместимый) | `ServiceBooking.API.csproj`, `docker-compose.glitchtip.yml` |
| **Health-checks** ⭐ | встроенные `Microsoft.Extensions.Diagnostics.HealthChecks`, два анонимных эндпоинта со своим двухполевым ответом | `Program.cs`, `Services/Health/` |
| Фоновые задачи | Свой `BackgroundService` + `IScheduledTask` (цикл 2). Hangfire/Quartz **нет**. ⚖️ Задач стало **четыре** (добавилась `data-retention`, период — сутки), сам раннер не менялся ни в цикле 4, ни в 5 | `Services/Scheduling/` |
| Документация API | Swashbuckle.AspNetCore 6.5.0, Swagger **только в Development** (цикл 1) | `Program.cs` |
| Правовые документы | ⭐ **файлы на диске** (`App_Data/legal/legal.json` + HTML), снимок в памяти с перечитыванием по mtime; не БД и не внешний сервис. ⚖️ Манифест цикла 5 состоит из **двух списков**: `documents[]` — пять версионируемых документов (`Privacy`, `TermsClient`, `TermsOwner`, `PdnConsent`, `ChannelRiskNotice`) с `gate`/`changeKind`/`purposes`, и `uiTexts[]` — **шесть текстов интерфейса**, которые **не являются версионируемыми документами и не участвуют в гейте 451** | `Services/Legal/LegalDocumentProvider.cs`, `App_Data/legal/legal.json` |
| Стиль кода | ⭐ `.editorconfig` в корне — **описывает** уже сложившийся стиль; `dotnet format` в CI **не подключён** | `.editorconfig` |
| 🔬 **Тестовая инфраструктура** | **Testcontainers.PostgreSql 3.10.0** + **Npgsql 8.0.5** в отдельном проекте `ServiceBooking.TestKit` (`net8.0`, `OutputType=Exe` — одновременно библиотека для обоих тестовых проектов и CLI `status`/`sweep`/`doctor`). Функциональный прогон **сам поднимает Postgres в Docker** на динамическом порту; заранее установленная PostgreSQL больше не нужна и не используется | `ServiceBooking.TestKit/*`, `docs/testing-isolation.md` |
| Менеджер пакетов | NuGet, версии зафиксированы в `.csproj` (без `Directory.Packages.props`, без lock-файлов) | — |

### Фронтенд

| Что | Значение | Откуда |
|---|---|---|
| Сборщик | Vite 5.4.x. 🔬 **Порты и цель прокси читаются из контракта `SB_*`** (цикл 8): dev-порт `SB_WEB_PORT` (дефолт 5173, `strictPort: false` — занят, возьмёт следующий), прокси `/api` и `/uploads` → `VITE_API_TARGET ?? http://localhost:${SB_API_PORT}` (дефолт 5000). Приоритет: process env → `.env` в корне репозитория → дефолт из контракта; `loadEnv` ограничен префиксами `VITE_`/`SB_`, чтобы не втянуть чужие секреты из общего `.env`; пустое значение трактуется как «не задано», нечисловой/вне диапазона порт — как дефолт | `frontend/vite.config.ts`, `.env.dev.example` |
| Тест-раннер | **Vitest 3.2 + jsdom 25 + @testing-library/react 16 + @testing-library/jest-dom + user-event** (появился в цикле 2, US-23); конфиг **отдельный от vite.config.ts** | `frontend/vitest.config.ts`, `frontend/src/test/setup.ts` |
| Библиотека UI | React 18.3 + React DOM 18.3, TypeScript 5.5 (`strict`, `noUnusedLocals`, `noUnusedParameters`) | `frontend/package.json`, `frontend/tsconfig.json` |
| Роутинг | `react-router-dom` 6.26 | `frontend/src/App.tsx` |
| Server state | `@tanstack/react-query` 5.56 (`retry: 1`, `staleTime: 30_000`) | `frontend/src/App.tsx` |
| Client state | `zustand` 4.5 + `persist` (ключ localStorage `auth-store`) | `frontend/src/store/authStore.ts` |
| HTTP-клиент | `axios` 1.7, единственный инстанс с `baseURL: '/api'` | `frontend/src/api/client.ts` |
| Формы | `react-hook-form` 7.53 | `CabinetPage.tsx`, `OwnerPage.tsx`, `CompanyManagePage.tsx` |
| Даты | `date-fns` 3.6 + локаль `ru` | все страницы с датами |
| Стили | Tailwind CSS 3.4 + PostCSS + Autoprefixer, кастомная палитра | `frontend/tailwind.config.js`, `frontend/src/index.css` |
| Линтер / форматтер | ⭐ **ESLint 9** (flat-config, `typescript-eslint` 8, плагины react/react-hooks/react-refresh, `eslint-config-prettier`) + **Prettier 3**; `npm run lint` **входит в CI** | `frontend/eslint.config.js`, `package.json` |
| Менеджер пакетов | npm, есть `package-lock.json`; ⭐ на проде фронт **больше не собирается** — CI выкладывает артефакт `frontend-dist-<sha>` | `.github/workflows/ci.yml`, `deploy/deploy.sh` |

Отдельной библиотеки валидации форм (zod/yup) нет — валидация делается правилами `react-hook-form`
и `[Required]`/`[EmailAddress]`-атрибутами DTO на бэкенде.

💳 **Единственная новая зависимость цикла 7 во всём репозитории** (бэкенд не получил ни одного нового
пакета): `openapi-typescript` ^7.13.0 в `devDependencies` фронтенда, плюс скрипт
`npm run types:api` → `openapi-typescript ../contracts/cycle7/openapi.yaml -o src/types/api-cycle7.generated.ts`.
Это **первая в проекте генерация типов из контракта**: `frontend/src/types/api-cycle7.generated.ts`
— сгенерированный файл, руками его не правят, перекодогенерируют. Через него заведены перечисления
`SubscriptionStatus` и `OptionAvailability` (коммит `82d557b`). ⚠️ Скрипт **не входит ни в CI, ни в
`npm run build`** — расхождение сгенерированного файла с `contracts/cycle7/openapi.yaml` ничем не
проверяется, синхронность держится только дисциплиной.

### Внешние сервисы

- 🆕 **GREEN-API (WhatsApp)** — третья реальная интеграция, **появилась в цикле 4 и в закоммиченной
  конфигурации выключена**. `Notifications:Provider` по умолчанию `"logging"` — транспорт-заглушка,
  которая не делает ни одного сетевого вызова (`Services/Notifications/LoggingNotificationTransport.cs`,
  `NoopChannelProvisioning`). Реальный адаптер включается значением `"green-api"`; **нераспознанное
  значение роняет старт**, чтобы прод не мог тихо «отправлять» в никуда.
  Код адаптера — `Services/Notifications/GreenApi/` (`GreenApiTransport`, `GreenApiProvisioning`,
  `GreenApiUrls`, `GreenApiResultClassifier`, `GreenApiWebhookParser`, `GreenApiStateInstanceParser`,
  `GreenApiHandlerFactory`), базовый адрес `https://api.green-api.com`.
  Модель: **один экземпляр провайдера = один номер = одна оплата**, номер принадлежит салону,
  платформа платит провайдеру партнёрским токеном. **Партнёрского аккаунта GREEN-API пока нет** (§9).
- **Yandex SmartCaptcha** — вторая реальная внешняя интеграция.
  Сервер: `ServiceBooking.API/Services/CaptchaService.cs`, `POST https://smartcaptcha.cloud.yandex.ru/validate`.
  Клиент: `frontend/src/components/booking/SmartCaptcha.tsx`, скрипт `https://smartcaptcha.yandexcloud.net/captcha.js`,
  ключ из `VITE_SMARTCAPTCHA_SITEKEY` (единственная используемая `import.meta.env`-переменная во всём фронтенде).
- **GlitchTip (self-hosted, Sentry-совместимый)** — ещё одна реальная интеграция, ⭐ **поднята и
  работает**: `https://errors.ezbook.ru` (nginx + TLS от certbot + basic-auth перед собственной
  формой логина GlitchTip), стек `docker-compose.glitchtip.yml` с явным `name: glitchtip`.
  `Sentry:Dsn` в **закоммиченном** конфиге по-прежнему пуст — боевое значение живёт только в `.env`
  на машине. Цепочка «ошибка в приложении → issue в трекере → письмо» проверена end-to-end
  2026-09-17 (`DEPLOY.md` §16). Тем же `SENTRY_DSN` пользуются `deploy/backup/backup.sh` и
  `deploy/monitor/health-alert.sh` — один DSN даёт три канала алертов.
- **Платёжного шлюза нет.** Ни SDK, ни HTTP-вызовов — `PaymentStatus` меняется только вручную
  через `PATCH /api/bookings/{id}/mark-paid`.
- **Почтового провайдера нет.** SMTP/SendGrid/любой другой клиент в коде отсутствует (см. §5).
- **Почтового провайдера по-прежнему нет.** Письмо владельцу о разрыве канала было в спеке цикла 4
  и **вырезано решением заказчика** (SPEC §0, редакция 6) — единственный канал оповещения владельца
  это плашка в кабинете. SMTP-клиента в коде нет.
- ~~Интеграции с мессенджерами нет~~ — закрыто циклом 4 (см. GREEN-API выше). MAX, исследованный в
  цикле 2, так и не реализован; выбран WhatsApp.
- **Хранилище файлов — локальный диск, но теперь ДВА класса хранения** (цикл 2, `Services/FileStorage.cs`):
  - *публичный* — по умолчанию `wwwroot/uploads/{companies,avatars,services}` (переопределяется
    `Storage:PublicRoot`), метод возвращает URL вида `/uploads/<область>/<guid>.<ext>`;
  - *приватный* — `App_Data/private-uploads/<companyId>/<guid>.jpg` (фото к заметкам о клиентах),
    **никогда не раздаётся статикой**, метод возвращает непрозрачный storage-key, не URL. Отдаётся
    только через `GET /api/client-notes/photos/{id}` с проверкой членства в компании.
  Корни настраиваются `Storage:PublicRoot` / `Storage:PrivateRoot`; в Production `Program.cs` **падает
  на старте**, если приватный корень резолвится внутри `wwwroot`, ⭐ если приватный корень резолвится
  внутри фактического `Storage:PublicRoot` (или совпадает с ним — раздача теперь следует за этой
  настройкой, а не жёстко за `wwwroot`, см. ниже), ⭐ и если сам `Storage:PublicRoot` резолвится в
  content root приложения или выше него (иначе `/uploads/...` отдавал бы `appsettings.Production.json`,
  DLL-ки и `App_Data/legal/...` кому угодно). Облачного стораджа нет.
  ⭐ Цикл sanitation: `Program.cs` больше не вызывает голый `app.UseStaticFiles()` (который резолвит
  `IWebHostEnvironment.WebRootFileProvider`, т.е. буквально `wwwroot`, ОДИН раз при старте хоста —
  на чистом клоне без закоммиченного `wwwroot` это давало пустой провайдер навсегда, даже после того
  как первая загрузка создавала каталог). Теперь раздача идёт через явный `PhysicalFileProvider`,
  построенный поверх того же `FileStorage.PublicRootFullPath`, что использует запись — `Storage:PublicRoot`
  стал единственным источником правды для чтения и записи. Каталог публичного корня создаётся
  (`Directory.CreateDirectory`) непосредственно перед конфигурацией раздачи, так что провайдер никогда
  не строится поверх ещё не существующего каталога. Раздаётся ровно `PublicRootFullPath` под
  `RequestPath = "/uploads"` — не весь `wwwroot` целиком.

### Как собирается и запускается

🔬 **Цикл 8 параметризовал dev-стек через необязательный `.env` в корне репозитория**
(`.env.dev.example` закоммичен, сам `.env` — в `.gitignore`): `SB_DB_PORT` (5432), `SB_DB_NAME`
(`servicebooking`), `SB_API_PORT` (5000), `SB_WEB_PORT` (5173), `SB_GLITCHTIP_PORT` (8000). **Свежий
клон без `.env` ведёт себя ровно как раньше** — все переменные имеют дефолты прямо в
`docker-compose.yml`. Верхнеуровневого `name:` в `docker-compose.yml` **сознательно нет**: имя
проекта compose выводит из каталога рабочей копии, и именно это разводит две копии репозитория по
разным контейнерам, сети и тому (`docker compose down -v` в одной не трогает данные другой).
Переопределить можно штатной `COMPOSE_PROJECT_NAME`.

```bash
# БД (dev)
docker compose up -d postgres          # docker-compose.yml, порт SB_DB_PORT (по умолчанию 5432) наружу

# Бэкенд
dotnet run --project ServiceBooking.API        # слушает http://localhost:5000 (профиль из launchSettings — 5291)
# Миграции применяются автоматически на старте: Program.cs -> db.Database.MigrateAsync()
# Там же сидятся роли (Client/Master/CompanyOwner/SuperAdmin) и SuperAdmin из конфига

# Фронтенд
cd frontend && npm install && npm run dev      # http://localhost:5173, /api проксируется на :5000

# Всё сразу в докере (api + postgres, без фронта)
docker compose up --build
```

Прод-сборка фронта: `npm run build` = `tsc && vite build` → `frontend/dist`.
Тесты фронта: `npm test` (watch) / `npm run test:run` (однократно).
Линт и формат: `npm run lint` (ESLint, есть в CI) / `npm run format` (Prettier).
⭐ На боевой сервер `dist` больше не собирается на месте — берётся артефактом из CI (§8).

**Расхождение конфигов устранено (цикл 2).** `ServiceBooking.API/Properties/launchSettings.json`
приведён в порядок: профили `http`/`https` слушают `http://localhost:5000` (https дополнительно 7016),
`launchUrl: "weatherforecast"` удалён, `launchBrowser: false`. Порт совпадает с тем, куда проксирует
Vite и куда мапится docker-compose.

**Конфигурация приложения** (`ServiceBooking.API/appsettings.json`). Секции цикла 2: `Storage`
(`PrivateRoot`, `PublicRoot`, `MinFreeDiskMb: 1024`), `Uploads` (`MaxFileBytes: 5242880`,
`PerUserPerMinute: 10`), `ScheduledTasks` (`Enabled`, `TickSeconds: 60`, и по подсекции на задачу —
сейчас одна, `photo-retention-cleanup`). ⭐ Цикл 3 добавил три: `ForwardedHeaders:TrustedNetworks`
(пустой список в git — в Production **пустой список роняет старт**), `RateLimits` (по подсекции на
политику: `auth-login` 10/мин, `auth-register` 5/час, `booking-create` 120/час и 10/час анонимам,
`data-export` 3/сутки) и `Sentry` (`Dsn`, `Release` — оба пустые). Секция `Legal` (`Root`,
`ReloadSeconds: 30`) в закоммиченном файле **отсутствует** — работают дефолты из `LegalOptions`
(`<ContentRoot>/App_Data/legal`, 30 с). Настроек Serilog в конфиге нет — логгер сконфигурирован кодом.
Отдельно появился **закоммиченный** `appsettings.Testing.json` (исключение из правила
`**/appsettings.*.json` в `.gitignore`): выключает планировщик и поднимает лимит загрузок до 1000/мин,
чтобы функциональные тесты вели себя одинаково у всех и в CI. Секретов не содержит.
🆕 Цикл 4 добавил туда же явное выключение двух новых задач
(`ScheduledTasks:notification-dispatch:Enabled=false`, `channel-health:Enabled=false`) — поверх общего
`ScheduledTasks:Enabled=false`, чтобы специализированная фабрика могла включать ровно одну из них.

🆕 **Секция `Notifications` (цикл 4)** — самая большая новая секция `appsettings.json`:
`Provider` (`logging` по умолчанию), `EncryptionKey` (пусто в git), `KeyRotationAck`,
`KeyFingerprintPath` (`App_Data/state/.notifications-key-fingerprint`), `PartnerToken`,
`WebhookToken`, `UnsubscribeKey`, подсекция `GreenApi` (`ApiUrl`, `TimeoutSeconds: 15`,
`ConnectPreference: IPv4First`, `ConnectTimeoutSeconds: 5`, `PerAddressConnectTimeoutSeconds: 2`),
подсекция `Dispatch` (`BatchSize: 200`, `BudgetSeconds: 50`, `MaxParallelChannels: 8`,
`PauseMinMs: 5000`, `PauseMaxMs: 15000`, `InFlightGraceMinutes: 5`, `MaxAttempts: 5`),
⚖️ цикл 5 добавил в неё `ProviderDeliveryConsent` (`AccountsOnly` по умолчанию — режим спорного
правового гейта, см. §4.18) и в подсекцию `GreenApi` — `ServerCountry` (пусто) и
`InstanceCreationEnabled: false`,
а также `ReminderJitterMinutes: 15`, `UnauthorizedInstanceTimeoutMinutes: 15`,
`TestMessageCooldownMinutes: 5`, `ConsecutiveFailureThreshold: 5`, `AllowedRecipients: []`
(белый список получателей для обкатки). Все секреты этой секции в git **пустые** — боевые значения
живут только в `.env` на машине (§8).

⚖️ **Две новые секции цикла 5 в `appsettings.json`:**
- **`Retention`** — тринадцать числовых сроков в сутках (`NotificationBodyDays: 30`,
  `NotificationMetadataDays: 365`, `TemplateHistoryDays: 1095`, `InactiveAccountDays: 1095`,
  `BookingPersonalizationDays: 1095`, `ClientNoteDays: 1095`, `ClientNotePhotoDays: 365`,
  `ClientHealthNoteDays: 1095`, `ConsentRecordDays: 1095`, `ChannelStateEventDays: 365`,
  `PaymentLogDays: 1825`, `MailLogDays: 365`, `AppLogDays: 90`) плюс `AppLogDirectory` (пусто =
  проверка выключена). Сроки **намеренно вынесены из кода в конфиг**, но два из них
  (`TemplateHistoryDays` ≥ 365 и `ConsentRecordDays` ≥ 1095) дополнительно проверяются fail-fast'ом
  на старте — это юридические минимумы, а не дефолты по вкусу оператора.
- **`SubjectRequests:ResponseWorkingDays: 10`** — срок ответа на обращение субъекта; **вычисляется и
  сохраняется в момент приёма обращения**, а не пересчитывается из текущего конфига.
- Подсекция `ScheduledTasks:data-retention` (`Enabled: true`, `MaxRunMinutes: 10`, **`DryRun: true`**,
  `BatchSize: 500`). ⚠️ **Сухой прогон — значение по умолчанию**: чтобы задача действительно удаляла,
  оператор обязан явно выставить `false` (на машине — через `RETENTION_DRY_RUN`, §8).
- В `appsettings.Testing.json` добавлены выключение `data-retention` и поднятый лимит политики
  `subject-request`.

🗓 **Новая секция цикла 6 в `appsettings.json` — ровно одна, и она маленькая:**

- **`Booking:DefaultWorkWindow`** (`Start: "09:00"`, `End: "21:00"`) — рабочее окно «по умолчанию»
  для ручной записи персонала на дату, на которую расписание не задано (US-66). До цикла 6 такой
  день раскрывался на **целые сутки 00:00–24:00**; теперь это окно, а сутки целиком доступны только
  по явному запросу (`extendedHours=true`, см. §4.5). Значение разбирается
  `DeploymentSafetyChecks.ParseDefaultWorkWindow` и **роняет старт**, если оно отсутствует, не
  парсится или перевёрнуто, — вместо тихого отката к суткам напролёт.
- Туда же, в `RateLimits`, добавлена восьмая политика — **`availability`** (60/мин на IP): новый
  эндпоинт `GET /api/bookings/availability` анонимный, гостевой сценарий записи без него не работает.

---

## 2. Структура репозитория

```
ServiceBooking.sln                  🔬 6 проектов (+ папка Solution Items) — добавлен ServiceBooking.TestKit
├── ServiceBooking.API/             ← точка входа, вся бизнес-логика веб-слоя
│   ├── Program.cs                  🆕 784 строки: Serilog, fail-fast прод-конфига (через DeploymentSafetyChecks),
│   │                               DI, Identity, JWT (+ перечитывание ролей, SecurityStamp и claim'ы согласия),
│   │                               CORS, Swagger (только Dev), exception handler, ForwardedHeaders,
│   │                               рейт-лимиты (🗓 +политика `availability`), глобальный LegalConsentFilter,
│   │                               health-эндпоинты, регистрация фоновых задач, миграции, сид,
│   │                               🗓 InvalidModelStateResponseFactory (форма тела 400 автоматической
│   │                               валидации, ГЛОБАЛЬНО для всех контроллеров — см. §6)
│   ├── Controllers/                ⚖️ 20 контроллеров (21 класс — в Reviews их два); цикл 4 добавил
│   │                               CitiesController, NotificationChannelsController, NotificationsController,
│   │                               CompanyNotificationsController; ⚖️ цикл 5 — ClientConsentsController,
│   │                               SubjectRequestsController
│   ├── DTOs/                       Auth / Bookings / ⭐ Common (PagedResult, 🆕 Optional<T>) / 🆕 Cities /
│   │                               ClientNotes / Companies / 🆕 Notifications (Channel/Log/Settings/Template/Unsubscribe) /
│   │                               Services / WorkingHours
│   ├── Services/                   🗓 +AvailabilityService (статус дней месяца), +BookingHorizon (горизонт записи),
│   │                               +BookingServiceSelection (состав визита), +MasterCapability (умеет ли мастер),
│   │                               +LoginOutcome (исходы входа), +SubscriptionDiagnostics (состояние тарифа),
│   │                               +ModelValidationErrorFormatter (тело 400 автовалидации);
│   │                               SlotService+SlotCalculator, SubscriptionResolver, CaptchaService, TokenService,
│   │   │                           AdvisoryLock, BookingFilters, CompanyMembership, PhoneNormalizer, FileStorage,
│   │   │                           ImageSignature, ImageProcessor, ImageUploadService, PhotoQuota,
│   │   │                           ⭐ DeploymentSafetyChecks, ⭐ IdentityRoleSync, ⭐ LogMasking, ⭐ PhoneMaskingEnricher,
│   │   │                           🆕 NotificationScheduler (постановка в очередь), 🆕 NotificationTexts,
│   │   │                           🆕 NotificationRiskText, 🆕 ChannelPresentation, 🆕 CompanyTimeZone,
│   │   │                           🆕 CitySearch, 🆕 PhoneDisplayMask, 🆕 UnsubscribeTokens,
│   │   │                           🆕 PlatformSettingsWriter, 🆕 ProviderWebhookParsing
│   │   ├── Legal/                  ⭐ LegalDocumentProvider, LegalConsentFilter, LegalOptions, LegalSnapshot;
│   │   │                           ⚖️ +ConsentLedger, ConsentSubject, HealthNoteProtector,
│   │   │                           RequiresOwnerTermsAttribute
│   │   ├── Retention/              ⚖️ IRetentionRule, RetentionPeriods, RetentionPlan, RetentionRuleRunner,
│   │   │                           Rules/ — 📸 ЧЕТЫРНАДЦАТЬ правил уничтожения (см. §4.18)
│   │   ├── Health/                 ⭐ DatabaseReadyHealthCheck
│   │   ├── Notifications/          🆕 SecretProtector, ChannelKeyFingerprint, NotificationOptions, NotificationGate,
│   │   │   │                       NotificationTiming, NotificationTemplateRenderer/Validator, DefaultTemplates,
│   │   │   │                       ChannelStateMapper/Transition, ChannelIdleCalculator, ChannelPaymentState,
│   │   │   │                       PlatformSettings, PauseGenerator, PreferIPv4, ProviderCallback,
│   │   │   │                       INotificationTransport/IChannelProvisioning/INotificationClock/IDispatchDelay,
│   │   │   │                       LoggingNotificationTransport
│   │   │   └── GreenApi/           🆕 адаптер провайдера: Transport, Provisioning, Urls, ResultClassifier,
│   │   │                           WebhookParser, StateInstanceParser, HandlerFactory
│   │   └── Scheduling/             IScheduledTask, ScheduledTaskRunner, ScheduledTaskOptions,
│   │                               ScheduledTaskSchedule, Tasks/{PhotoRetentionCleanupTask,
│   │                               🆕 NotificationDispatchTask, 🆕 ChannelHealthTask,
│   │                               ⚖️ DataRetentionTask}
│   ├── App_Data/legal/             ⭐ В GIT: манифест legal.json + HTML (ЧЕРНОВИКИ);
│   │                               ⚖️ файлов стало 11: privacy, terms, terms-owner, pdn-consent,
│   │                               channel-risk-notice + шесть текстов интерфейса;
│   │                               на проде перекрывается bind-mount'ом ./legal с хоста
│   ├── App_Data/private-uploads/   приватный класс хранения (в .gitignore)
│   ├── Dockerfile                  multi-stage, aspnet:8.0, EXPOSE 8080 (комментарий «не менять на -alpine»)
│   └── appsettings*.json           appsettings.json и appsettings.Testing.json в git; Development/Production — в .gitignore
├── ServiceBooking.Core/            только сущности и перечисления, зависимость одна — Identity.EFCore
│   ├── Entities/                   🗓 33 класса (+BookingService — строка визита, см. §3); ⚖️ было 32
│   │                               (−UserConsent, +ConsentRecord, +ClientHealthNote,
│   │                               +SubjectRequest; 🆕 12 сущностей цикла 4, см. §3)
│   └── Enums/                      BookingStatus, ⭐ LegalDocumentType (⚖️ пять членов), PaymentStatus,
│                                   PhotoRetention (⚖️ без Forever), UserRole, 🆕 +7 цикла 4;
│                                   ⚖️ +7 цикла 5: LegalGate, ConsentPurpose, ConsentAct, ConsentSource,
│                                   SubjectRequestKind/Status, LegalEntityForm, ProviderDeliveryConsentMode;
│                                   ⚖️ LegalTextKey — СТРОКИ, а не перечисление
├── ServiceBooking.Infrastructure/  AppDbContext + 🗓 40 миграций EF Core (было 36; цикл 8 их не трогал,
│                                   цикл 6 добавил четыре — см. §3)
├── 🔬 ServiceBooking.TestKit/      НОВЫЙ ПРОЕКТ (цикл 8): среда тестового прогона. Библиотека для обоих
│                                   тестовых проектов И CLI (status/sweep/doctor, --json).
│                                   TestRunKey (ключ прогона, 8 hex, статика в процессе),
│                                   TestDatabaseNaming (имена sbtest_<ключ>_<слот> + защита от сноса
│                                   не-одноразовой базы), TestServerLease (Postgres через Testcontainers
│                                   на динамическом порту либо внешний сервер), TestDatabaseLease
│                                   (шаблон прогона + клон базы на класс, единственное место в репозитории,
│                                   где выполняется DROP DATABASE), Sweeper (уборка после убитых прогонов),
│                                   EnvStatus (status/doctor), ResourceLabels, StableHash, TestKitJson,
│                                   TestInfrastructure (единственный источник пинов/лимитов),
│                                   TestSafetyException,
│                                   ⚠️ TestSlot и TestData ПЕРЕЕХАЛИ СЮДА из ServiceBooking.Tests/Infrastructure/
│                                   (коммит `7d99394`, закрытие находки T9 L5 — искать их по старому пути больше не нужно)
├── ServiceBooking.UnitTests/       xUnit, БЕЗ БД и без HTTP — чистая логика; 🗓 60 файлов, 777 запусков
│                                   (🔬 +9 файлов цикла 8 — покрытие TestKit; 🗓 +7 файлов цикла 6, см. §7.1)
├── ServiceBooking.Tests/           xUnit, функциональные тесты через WebApplicationFactory; 🗓 30 файлов, 488 запусков
│   ├── Infrastructure/             ApiTestBase, CustomWebApplicationFactory, TestDatabaseFixture, JsonHelpers,
│   │                               TestCaseAttribute, TestImages, ⭐ LegalDocumentsTestFactory, ⭐ RateLimitTestFactory,
│   │                               🆕 NotificationTestBase, NotificationTestFactory, NotificationDispatchTestFactory,
│   │                               🔬 UploadsStaticFilesTestFactory, TestRunEnvironment (одна среда на процесс),
│   │                               TestHostSettings (единственное место настройки всех тестовых хостов),
│   │                               TestPhones, TestParallelism, RandomTestCaseOrderer (случайный порядок с семенем);
│   │                               ⚠️ TestSlot и TestData отсюда УЕХАЛИ в ServiceBooking.TestKit/ (`7d99394`)
│   ├── 🔬 xunit.runner.json        maxParallelThreads=4, parallelizeTestCollections=true
│   ├── 🔬 AssemblyInfo.cs          TestCaseOrderer + ОДНА ЗАКОММЕНТИРОВАННАЯ строка отката параллелизма
│   └── Tests/                      🗓 30 файлов по доменам (+MultiServiceBookingTests.cs — визит из нескольких услуг)
├── frontend/                       React SPA
│   ├── src/api/                    ⚖️ 23 модуля — тонкая обёртка над axios, по одному на домен
│   │                               (⭐ +legal.ts; 🆕 +cities.ts, +notifications.ts, +notificationChannels.ts,
│   │                               +platformSettings.ts; ⚖️ +consents.ts, +clientConsents.ts,
│   │                               +subjectRequests.ts)
│   ├── src/pages/                  страницы; вложенные owner/ и admin/ — вкладки;
│   │                               ⭐ +LegalDocumentPage.tsx, +DeleteAccountPage.tsx;
│   │                               🆕 +UnsubscribePage.tsx, owner/{NotificationsSection, NotificationSettingsTab,
│   │                               NotificationTemplatesTab, NotificationLogTab}.tsx, admin/NotificationsAdminTab.tsx;
│   │                               ⚖️ +ConsentsPage.tsx, +SubjectRequestPage.tsx,
│   │                               admin/SubjectRequestsTab.tsx, owner/TemplateAcknowledgementModal.tsx
│   ├── src/components/             booking/ (🗓 +BookingCalendar.tsx — месячный календарь доступности),
│   │                               clientNotes/ (⚖️ +HealthNoteCard, +PhotoConsentBadge,
│   │                               +ClientConsentModal), ⭐ legal/ (ConsentGate, LegalUpdateBanner,
│   │                               ⚖️ +OwnerTermsGateModal), layout/, review/, schedule/,
│   │                               ui/ (⭐ +Pagination, 🆕 +CityCombobox, 🗓 +PhoneInput — маска +7),
│   │                               🆕 notifications/ (QrModal, AssignCompanyDialog, RiskAcceptanceModal,
│   │                               ChannelBreachBanner, ⚖️ +ChannelRequestModal)
│   ├── src/hooks/                  useOverlayDismiss, useAuthedImage, ⭐ useExportData, ⭐ useDebouncedValue,
│   │                               ⚖️ +useLegalText, +usePhotoUploadWithConsent
│   ├── src/store/                  ⚖️ ДВА zustand-стора: authStore.ts и ownerGateStore.ts
│   ├── src/test/setup.ts           setup Vitest (jest-dom + cleanup Testing Library)
│   ├── src/types/index.ts          общие TS-типы (ручная копия серверных DTO)
│   ├── src/utils/                  мапперы ошибок HTTP → русский текст (⭐ +authError, +legalError,
│   │                               🆕 +notificationError, 🗓 +providesServicesError) + phone.ts (🗓 маска и
│   │                               looksRussian), 🆕 timezone.ts, 🆕 channelBanner.ts,
│   │                               🗓 +bookingHorizon.ts, +bookingServices.ts
│   ├── eslint.config.js            ⭐ ESLint 9 flat-config + Prettier
│   └── design_handoff_site_redesign/  HTML-макеты редизайна, не участвуют в сборке
├── .editorconfig                   ⭐ описывает уже сложившийся C#-стиль; в CI НЕ проверяется
├── .github/workflows/
│   ├── ci.yml                      CI: три job'а (backend, frontend, docker-build со смоук-прогоном образа)
│   ├── deploy-staging.yml          🚀 деплой стенда по кнопке (ветка develop), environment `staging`
│   └── deploy-production.yml       🚀 деплой прода по кнопке (только тег на master + ввод слова `deploy`),
│                                   environment `production` с required reviewer
├── 🔬 contracts/cycle8/           servicebooking-invariant.openapi.yaml (OpenAPI 3.1 — ИНВАРИАНТ HTTP-поверхности,
│                                   снятый с `7b382d8`; первый машиночитаемый контракт API в репозитории)
│                                   и testkit-status.schema.json (JSON Schema вывода CLI TestKit)
├── 🔬 .env.dev.example             образец `.env` РАБОЧЕЙ КОПИИ (SB_* порты dev-стека); сам `.env` в .gitignore
├── deploy/
│   ├── 🔬 ci/check-image-pins.sh   bash-проверка: мажорная версия postgres одинакова в TestKit,
│   │                               обоих compose-файлах и ci.yml
│   ├── deploy.sh / deploy-remote.sh
│   ├── ssh-deploy-wrapper.sh       🚀 форс-команда в authorized_keys пользователя ezbookdeploy:
│   │                               закрытый allowlist upload-release/deploy/rollback/health
│   ├── nginx/ezbook.conf           основной vhost (ezbook.ru)
│   ├── nginx/errors.ezbook.conf    🚀 vhost трекера ошибок (errors.ezbook.ru, basic-auth)
│   ├── ci/smoke.sh                 смоук живого контейнера (health, регистрация, загрузка аватара)
│   ├── backup/                     backup.sh + systemd .service/.timer (локальный бэкап, включая .env)
│   ├── monitor/                    health-alert.sh + systemd .service/.timer
│   └── rollback.sh                 откат одной командой
├── docker-compose.yml              dev: postgres + api
├── docker-compose.prod.yml         prod: postgres + api на 127.0.0.1:5000, два volume + ⭐ bind-mount ./legal
│                                   + 🆕 ЗАПИСЫВАЕМЫЙ bind-mount ./state (отпечаток ключа шифрования)
├── docker-compose.glitchtip.yml    ⭐ self-hosted GlitchTip (Sentry-совместимый трекер), отдельный стек
├── README.md                       продуктовое описание + «чего пока нет» + ⭐ запуск/секреты/CI/деплой
├── CHANGELOG.md                    changelog по датам циклов, самая свежая запись сверху
├── docs/                           пользовательская документация по ролям (⭐ +personal-data.md,
│                                   ⚖️ +incident-runbook.md — утечка ПДн, что делать;
│                                   🔬 +testing-isolation.md — запуск тестов и локальная среда,
│                                   документ ДЛЯ КОМАНДЫ, не для пользователя)
├── legal-drafts/                   ⚖️ ДВЕНАДЦАТЬ HTML-документов юриста + README.md +
│                                   legal.json + legal.manifest.proposed.json; ИСХОДНИКИ
│                                   с плейсхолдерами {{…}}, приложение их НЕ читает
├── LEGAL_REVIEW.md                 ⚖️ ~205 КБ, юридическое заключение по продукту, редакция 4
├── SPEC.md                         🔬 ~71 КБ, спека ЦИКЛА 8 (изоляция тестовой и локальной среды)
├── 🗓 SPEC_CYCLE6_BOOKING_FIXES.md ~88 КБ, спека ЦИКЛА 6 (SPEC.md занят циклом 8), истории US-60…US-67
├── 🗓 ARCHITECTURE_CYCLE6.md (~138 КБ, §41–§54) / API_CONTRACT_CYCLE6.md (~59 КБ, §38–§46)
├── 🗓 openapi-cycle6.yaml          ~64 КБ, OpenAPI 3.0.3 — машиночитаемый контракт цикла 6 (12 путей)
├── 🔬 SPEC_CYCLE5_LEGAL.md         ~163 КБ, сохранённая спека цикла 5 (SPEC.md занят циклом 8)
├── 🔬 ARCHITECTURE_CYCLE8.md (~88 КБ, §61–§81) / ARCHITECTURE_CYCLE8_PHASE2.md (~108 КБ, §89–§99)
├── 🔬 API_CONTRACT_CYCLE8.md (~27 КБ, §82–§88) / API_CONTRACT_CYCLE8_PHASE2.md (~17 КБ)
├── ARCHITECTURE.md / API_CONTRACT.md                документы ЦИКЛА 3 (не перезаписаны!)
├── ARCHITECTURE_CYCLE4.md / API_CONTRACT_CYCLE4.md  🆕 документы цикла 4 (разделы 21–40 и 19–37)
├── ARCHITECTURE_CYCLE5.md / API_CONTRACT_CYCLE5.md  ⚖️ документы цикла 5 (разделы 41–60 и 38–53)
├── SPEC_CYCLE3_PRODUCTION.md       🆕 сохранённая спека цикла 3
├── SPEC_CYCLE4_NOTIFICATIONS.md    ⚖️ сохранённая спека цикла 4 (SPEC.md занят циклом 5)
├── SPEC_DEFERRED_NOTIFICATIONS.md / SPEC_APPENDIX_CHANNELS.md  ⭐ спека ОТЛОЖЕННОГО цикла уведомлений
├── API_DOCUMENTATION.md            ~247 КБ, подробный справочник эндпоинтов (рус.)
├── TEST_CATALOG.md                 ~270 КБ, человекочитаемый каталог всех тест-кейсов (рус.)
├── DEPLOY.md                       🚀 ~111 КБ, ПЕРЕПИСАН ЦЕЛИКОМ под фактическую машину
│                                   (Ubuntu 24.04 desktop, не VPS): 16 разделов + «Почему так сделано»
│                                   + §16 чек-лист первого запуска с датами и результатами
├── DEPLOY-windows.md               VK Cloud Windows (IIS+ARR) — контур ВЫВЕДЕН ИЗ СКОУПА, файл не удалён
└── .env.production.example, .deploy.env.example, appsettings.Production.json.example
```

⭐ — появилось в цикле 3. 🚀 — появилось/изменилось при первом реальном развёртывании
(диапазон `6369266..0e61369`). 🆕 — появилось в **цикле 4** (диапазон `0e61369..7a36543`).
⚖️ — появилось или **переделано** в **цикле 5** (диапазон `7a36543..071fc11`).
🔬 — появилось или переделано в **цикле 8** (диапазон `7b382d8..6562a86`).
🗓 — появилось или переделано в **цикле 6** (диапазон `14a7fb6..aac6231`; цикл 6 делался
параллельно циклам 5 и 8 и влился в `develop` после них, поэтому его значок в тексте встречается
позже «восьмёрки», хотя номер меньше).
Значки прежних циклов намеренно оставлены как были: так видно, что именно в каком цикле возникло.

📸 **Что цикл 10 добавил и удалил в структуре** (`ARCHITECTURE_CYCLE10.md` §110):

- `ServiceBooking.API/Controllers/CompanyPhotosController.cs` — **новый** контроллер (205 строк),
  все четыре маршрута `/api/companies/{id}/photos*`. Фотографии салона намеренно не стали ещё одним
  куском и так самого большого `CompaniesController`.
- `ServiceBooking.API/Services/Bookings/` — **новая папка**: `BookingEventLog.cs` (единственный
  писатель журнала — никакой контроллер не добавляет строку в `BookingEvents` напрямую),
  `BookingActorResolver.cs` (кто совершил действие: клиент, гость, сотрудник, суперадмин),
  `BookingEventTexts.cs` (человеческие формулировки событий).
- `ServiceBooking.API/Services/CompanyPhotoOrdering.cs` — чистая (без БД) логика порядка и
  уплотнения позиций фото; `Services/Retention/Rules/BookingEventRule.cs` — новое правило
  уничтожения (§4.18, §9 D1).
- `ServiceBooking.Core/Entities/BookingEvent.cs`, `CompanyPhoto.cs`; перечисления
  `Core/Enums/BookingEventKind.cs`, `BookingActorKind.cs`.
- Фронтенд: `api/companyPhotos.ts`, `components/booking/BookingHistoryPanel.tsx`,
  `components/company/CompanyPhotoGallery.tsx`, `pages/owner/CompanyPhotosSection.tsx`,
  `types/api-cycle10.generated.ts` (сгенерирован из `contracts/cycle10/openapi.yaml`).
- ⚠️ **Удалён** `frontend/src/components/booking/ManualBookingModal.tsx` (623 строки) вместе со
  своим тестом (`ManualBookingModal.test.tsx`, 179 строк). Его функции поглощены `BookingModal.tsx`
  — см. §4.5, §5.0-ter и §6.

### Точка входа и слои

- Единственная точка входа приложения — `ServiceBooking.API/Program.cs` (🆕 вырос до **784 строк**).
  **Второй процесс** в том же
  хосте — `ScheduledTaskRunner` (`BackgroundService`), тикает раз в `ScheduledTasks:TickSeconds` (60 с);
  ⚖️ задач в нём теперь **четыре**, и раннер не менялся **ни в цикле 4, ни в цикле 5** — расширение
  через `IScheduledTask` продолжает работать как задумано.
  ⭐ Третий фоновый «житель» — `LegalDocumentProvider`: держит снимок правовых документов в памяти и
  перечитывает манифест с диска по mtime (не чаще раза в `Legal:ReloadSeconds`).
- **Бизнес-логика по-прежнему живёт в контроллерах.** Сервисного слоя как такового нет, но `Services/`
  заметно вырос: к слотам/тарифам/капче/токенам добавились чистые (без БД) помощники — `SlotCalculator`,
  `PhoneNormalizer`, `PhotoQuota`, `ScheduledTaskSchedule`, `BookingFilters`, `ImageSignature`,
  `ImageProcessor` — и статические `AdvisoryLock`, `CompanyMembership`.
  Правила «кто что может» **всё ещё** реализованы приватными методами внутри каждого контроллера
  (`CanManageCompany`, `CanManage`, `CanManageBookingAsync`), но их «членская» половина теперь
  делегируется в единственное место — `CompanyMembership.IsStaffAsync` / `IsOwnerAsync`.
- `Core` — анемичные POCO-сущности без поведения. `Infrastructure` — только `AppDbContext` и миграции.
  Репозиториев нет, контроллеры работают с `AppDbContext` напрямую.
- Выделился слой «чистая логика без инфраструктуры» — именно он покрыт `ServiceBooking.UnitTests`:
  `SlotCalculator`, `SubscriptionResolver.Resolve` (статический), `PhoneNormalizer`, `PhotoQuota`,
  `ScheduledTaskSchedule`, `BookingFilters`, `ImageSignature`, `ImageProcessor`, `FileStorage`, `TokenService`,
  ⭐ `DeploymentSafetyChecks`, `Pagination.Normalize`, `LogMasking`, `LegalDocumentProvider`,
  `LegalConsentFilter`. Цикл 3 **сознательно вытаскивал логику в этот слой ради тестируемости** —
  именно поэтому fail-fast переехал из `Program.cs` в отдельный класс.
  🆕 **Цикл 4 продолжил эту линию агрессивнее всех предыдущих:** почти вся его логика — чистые
  классы, покрытые юнит-тестами (`SecretProtector`, `ChannelKeyFingerprint`, `NotificationGate`,
  `NotificationTiming`, `NotificationTemplateRenderer/Validator`, `ChannelStateMapper`,
  `ChannelIdleCalculator`, `ChannelPaymentState`, `ChannelPresentation`, `CompanyTimeZoneResolver`,
  `CitySearch`, `PhoneDisplayMask`, `UnsubscribeTokens`, `PauseGenerator`, `PreferIPv4`,
  `GreenApiUrls/ResultClassifier/WebhookParser/StateInstanceParser`, `NotificationTexts`,
  `Optional<T>`). Именно поэтому юнит-набор вырос с 215 до 488, а функциональный — только на 42.
- ⚖️ **Цикл 5 добавил к этому слою первый «единственный читатель и писатель» таблицы** —
  `Services/Legal/ConsentLedger.cs`. Это не репозиторий в общем смысле (их в проекте по-прежнему нет):
  журнал согласий — **единственная** таблица, доступ к которой не размазан по контроллерам, и сделано
  это ровно затем, чтобы ответ на вопрос «что сейчас считается согласованным» существовал в одном месте.
  Сам `ConsentLedger` **намеренно не стоит на горячем пути**: Privacy/TermsClient/TermsOwner проверяются
  по claim'ам JWT против снимка в памяти, без запросов в БД. Чистых классов цикл 5 добавил меньше, чем
  цикл 4, но они есть и покрыты юнит-тестами: `InnValidator`, `WorkingDays`, `TemplateAdHeuristics`,
  `ClientKey`, `RetentionPlan`, `SubjectRequestReference`, `HealthNoteProtector`.
- 🗓 **Цикл 6 следовал той же линии:** шесть новых чистых классов (`LoginOutcome`/`LoginOutcomeMapper`,
  `BookingHorizon`, `BookingServiceSelection`, `SubscriptionDiagnostics`,
  `ModelValidationErrorFormatter` и почти чистый `MasterCapability` — у него один запрос в БД,
  поэтому он проверяется функциональными тестами, а не юнитом). `SlotCalculator` цикл переработал
  (`ScheduleFallback` вместо `bool`), а `AvailabilityService` намеренно оставил тонкой обёрткой над
  тремя запросами, отдав всю арифметику дня тому же `SlotCalculator.Calculate`.
- 💳 **Цикл 7 — первый, кто завёл в `Services/` предметный подкаталог с собственным «движком».**
  Весь биллинг лежит в **`ServiceBooking.API/Services/Billing/`** (14 файлов, новый каталог):
  - **чистая логика, покрытая юнитами** — `BillingCalculator` (итог = цена тарифа + Σ опция × количество,
    и статус подписки), `CompanyTransferCalculator` (можно ли перенести компанию и почему нет),
    `PricingCatalogBuilder` (сборка публичной витрины), `CapabilityKeys` / `OptionCapabilityCatalog`
    (ключи возможностей — `companies`/`employees`, **не** коды опций, см. коммит `d60e29b`),
    `BillingTexts` (тексты отказов);
  - **с БД** — `AccountUsageReader` (сколько сотрудников/компаний занято по аккаунту),
    `OwnerSubscriptionService`, `SubscriptionAssignmentValidator`, `CompanyTransferService`
    (атомарный перенос компании между аккаунтами), `CompanyOwnerWriter` (смена ответственного —
    **без** денег и без отвязки номера), `BillingAccountProvisioner`, `ChannelFunding`;
  - **кэш** — `PricingCatalogCache` (держит витрину и флаг публикации; в нём же константа
    `PublicEnabledSettingKey = "pricing.public-enabled"`).
  Контроллеры при этом остались «толстыми» по-прежнему: `AdminBillingController` — 12 маршрутов и
  ~800 строк, часть логики (в том числе вычисление статуса подписки в SQL) живёт прямо в нём,
  а не в `BillingCalculator` — см. §9 B4.
- 📸 **Новое вне кода в цикле 10:** `contracts/cycle10/openapi.yaml` (четвёртый машиночитаемый
  контракт, §10.3) — каталог `contracts/` теперь содержит `cycle8/`, `cycle7/` и `cycle10/`.
- 💳 **Новые каталоги вне кода:** `contracts/cycle7/openapi.yaml` (третий машиночитаемый контракт,
  §10.3) и **`deploy/checks/`** — два SQL-скрипта сверки для миграции биллинга
  (`billing-precheck.sql` — перед выкатом, `billing-migration-check.sql` — после), см. §8.
- 🆕 **Файлы цикла 4 поделены между двумя бэкенд-разработчиками:** `Services/Notifications/**` —
  один, `Services/Notification*.cs` в корне `Services/` (`NotificationScheduler`, `NotificationTexts`,
  `NotificationRiskText`) — другой. Это объясняет, почему презентационный код лежит не рядом с
  кодами причин, которые он переводит (объяснено комментарием в самих файлах).

### Мёртвый проект `ServiceBooking/` — удалён

Blazor Server-шаблон из первого коммита удалён целиком в цикле 2 (US-22): каталог `ServiceBooking/`
и запись о проекте в `ServiceBooking.sln` больше не существуют. Именно его предупреждение сборки
мешало включить `-warnaserror` в CI — теперь флаг включён.

---

## 3. Модель данных

Источник: `ServiceBooking.Core/Entities/*`, конфигурация связей — `ServiceBooking.Infrastructure/Data/AppDbContext.cs`.
Плюс стандартные таблицы ASP.NET Identity (`AspNetUsers` и т.д.) через `IdentityDbContext<AppUser>`.

### Сущности

| Сущность | Ключ | Ключевые поля | Связи |
|---|---|---|---|
| `AppUser : IdentityUser` | string | `FirstName`, `LastName`, `AvatarUrl`, `CreatedAt`, ⭐ **`DeletedAtUtc?`** (надгробие удалённого аккаунта, цикл 3) | 1—N: CompanyMemberships, ClientBookings, MasterBookings, MasterServices, WorkingHours |
| `Company` | Guid | `Name`, `Slug` (**уникальный индекс**), `Description`, `LogoUrl`, `Address`, `Phone`, `Email`, `AllowSelfBooking`, `RequirePrepayment`, `ShowInPublicListing`, `IsActive`, `OwnerUserId`, 🆕 **`CityId?`** (FK `Restrict`), 🆕 **`TimeZoneId`** (IANA, дефолт `Europe/Moscow`), 🆕 **`TimeZoneIsManual`**, 🗓 **`BookingHorizonDays`** (дефолт 90, допустимо 1..365; насколько вперёд клиент может записаться сам — персонала не касается) | N—1 Owner (`Restrict`), 🆕 N—1 City (`Restrict`), 1—N Members / Services / Bookings |
| `CompanyMember` | Guid | `CompanyId`, `UserId`, `Role: UserRole`, `Bio`, **`CommissionPercent`** (переехал сюда с `AppUser` в цикле 1), `JoinedAt`, 🗓 **`ProvidesServices`** (дефолт `true` — виден ли участник в публичном списке «выбрать мастера»; имеет смысл для ролей `Master`/`CompanyOwner`) | «многие-ко-многим» User↔Company с ролью |
| `Service` | Guid | `CompanyId`, `Name`, `DurationMinutes`, `Price decimal(10,2)`, `ImageUrl`, `IsActive` | 1—N MasterServices, Bookings |
| `MasterService` | Guid | `MasterId`, `ServiceId` | связка «мастер умеет услугу» |
| `WorkingHours` | Guid | `MasterId`, `CompanyId`, **`Date: DateOnly`**, `StartTime`, `EndTime`, `IsWorking`; **уникальный индекс `(MasterId, CompanyId, Date)`** (цикл 1) | 1—N `ScheduleBreak` |
| `ScheduleBreak` | Guid | `WorkingHoursId`, `StartTime`, `EndTime` | перерывы внутри дня |
| `WeeklyScheduleTemplate` | Guid | `MasterId`, `CompanyId`, `DayOfWeek` (ISO 1..7), `IsWorking`, `StartTime`, `EndTime`; индекс `(MasterId, CompanyId)` | шаблон, «раскатываемый» в `WorkingHours` |
| `Booking` | Guid | `CompanyId`, `ServiceId`, `MasterId`, `ClientId?`, `GuestName/Phone/Email`, `Date`, `StartTime`, `EndTime`, **`Price` (снимок цены)**, **`CommissionPercent` (снимок комиссии, цикл 1)**, `Status`, `PaymentStatus`, `Notes`, `CancellationReason`, ⭐ **`ConsentPrivacyVersion?` / `ConsentTermsVersion?` / `ConsentAcceptedAtUtc?`** (снимок согласия, заполняет сервер, в т.ч. для гостя; ⚖️ `ConsentTermsVersion` **сохранил имя цикла 3**, но держит теперь версию `TermsClient`), ⭐ **`ClientDeleted`**, ⚖️ **`BookedForOther`**, **`GuardianConfirmedAtUtc?`**, **`GuardianConfirmationVersion?`** (запись за другого человека — только для самозаписи клиента/гостя/виджета, не для ручной записи персоналом), ⚖️ **`BookingNoticeVersion?`** (версия уведомления по ст. 18, сервер пишет **на каждую** запись, включая записи персонала); 🗓 навигация **`BookingServices`** (состав визита) | Master `Restrict`, Client `SetNull`, 🗓 1—N `BookingService` (`Cascade`) |
| 🗓 **`BookingService`** | Guid | `BookingId`, `ServiceId`, `Position` (0-based, он же порядок показа), `NameSnapshot` (≤200, снимок имени на момент записи), `DurationMinutes`, `Price decimal(10,2)`; **уникальный индекс `(BookingId, Position)`** | US-67: одна строка на каждую услугу визита. `Booking.ServiceId`/`Booking.Price` сохранили смысл «первая услуга» / **«итог по визиту»**, а эта таблица — разбивка под итогом. После backfill'а миграции **никогда не пуста**: у записи, созданной до цикла 6, ровно одна строка. Booking `Cascade`, Service `Restrict` |
| `Review` | Guid | `BookingId` (**уникальный индекс** — 1 отзыв на запись), `CompanyId`, `MasterId`, `ClientId?`, `ReviewerName`, `Rating 1..5`, `Comment` | Booking `Cascade` |
| `ClientNote` | Guid | `CompanyId`, `MasterId` (автор), `ClientId?` / `GuestPhone?`, `Note`, **`BookingId?`** (визит, к которому написана заметка; `SetNull`), `CreatedAt`; индексы `(CompanyId, ClientId)` и `(CompanyId, GuestPhone)` | заметки общие для компании; удалять может **автор или владелец компании** (решение Q16); 1—N `ClientNotePhoto` |
| `ClientNotePhoto` | Guid | `ClientNoteId`, `CompanyId` (денормализованная копия), `StoragePath`, `ThumbnailPath`, `ContentType`, `SizeBytes` (полный размер + миниатюра), `Width`, `Height`, `ContentHash` (SHA-256 **обработанных** байт), `UploadedByUserId?` (`SetNull`), `CreatedAt`; индексы `ClientNoteId`, `(CompanyId, CreatedAt)`, **уникальный `(ClientNoteId, ContentHash)`** | фото к заметке; каскад от заметки; ≤5 на заметку |
| ~~`UserConsent`~~ ⭐ | — | — | ⚖️ **УДАЛЕНА в цикле 5** вместе с таблицей (миграция `ConsentJournal`). Заменена журналом `ConsentRecord` — см. ниже |
| `ScheduledTaskState` | string `Name` (PK) | `LastStartedAtUtc?`, `LastFinishedAtUtc?`, `LastSucceeded`, `LastDurationMs`, `LastSummary?`, `LastError?` | состояние периодической задачи, переживающее рестарт |
| `AccountSubscription` | Guid | `OwnerUserId` (**уникальный индекс**), `PlanConfigId?`, `PaidUntil?`, `IsActive` | подписка на **аккаунт владельца**, а не на компанию |
| `SubscriptionPlanConfig` | Guid | `Name`, `PricePerMonth`, `MaxEmployees?`, `MaxCompanies?`, `AllowOnlineBooking`, `AllowMailing`, `AllowAnalytics`, `AllowPublicListing`, `AllowOnlinePayment`, **`PhotoQuotaMb?`** (null = без ограничения, дефолт 100), **`PhotoRetention`**, `IsActive`, `NotifyDaysBefore` | справочник тарифов |
| `SubscriptionChangeLog` | Guid | `OwnerUserId` (индекс), `ChangedByUserId`, старые/новые план, `PaidUntil`, `IsActive`, `Comment` | аудит изменений подписки |
| `MailLog` | Guid | `CompanyId`, `Subject`, `Message`, `SentById`, `RecipientCount`, `SentAt` | журнал «рассылок» |
| `SubscriptionPlanConfig` | — | 🆕 **`AllowNotificationChannel`** (по умолчанию `false` **у всех планов, включая новые** — решение заказчика Q1) | см. выше |

#### 🆕 Сущности цикла 4 (12 новых)

| Сущность | Ключ | Ключевые поля | Смысл |
|---|---|---|---|
| **`City`** | int | `Name`, `Region`, `TimeZoneId` (IANA), `IsActive`, `SearchName` (нормализованное: строчные, ё→е, без дефисов/пробелов) | справочник городов, 🆕 **301 строка** (91 из `AddNotificationChannels`/`SeedCities` + 210 из `ExpandCityDirectory`, цикл 9, US-113, ARCHITECTURE_CYCLE9.md §103.3 — `IX_Cities_Name_Region` теперь уникальный). Поиск — обычный `LIKE` по `SearchName`, без full-text и trigram: на таком объёме seq scan дешевле, и это **явно закомментированное решение**, а не недосмотр |
| **`NotificationChannel`** | Guid | `OwnerUserId` (владелец **аккаунта**, тот же ключ, что у `AccountSubscription`), `Transport`, `State`, `PhoneNumber?` (канонический), `ProviderInstanceId?` (**уникальный среди непустых**), `ProviderSecretCiphertext?` + `ProviderSecretKeyId?`, `OrphanedInstanceId?`, `RequestedAtUtc?`, `ContactEmail?` (**зарезервировано, в цикле 4 не используется — письма вырезаны**), `PaidFromUtc?`/`PaidUntilUtc?`, `IsSuspendedByAdmin`, `IdleSinceUtc?`/`IdleWarningSentAtUtc?`, `InstanceCreatedAtUtc?`, `ConnectedAtUtc?`, `LastStateCheckAtUtc?`, `LastStateReason?`, `ConsecutiveSendFailures`, `LastTestMessageAtUtc?`, `DisruptionNotifiedAtUtc?`, `RiskAcceptedAtUtc?`/`RiskAcceptedVersion?`, `ReplacedByChannelId?` | **один экземпляр провайдера = один номер = одна оплата**. Канал принадлежит аккаунту владельца, а **не** компании |
| **`ChannelCompanyAssignment`** | Guid | `ChannelId`, `CompanyId` (**уникальный индекс** — компания не может быть на двух каналах), `AssignedAtUtc`, `AssignedByUserId` | назначение компаний на канал; правило держится **индексом**, а не проверкой в коде |
| **`ChannelStateEvent`** | Guid | `ChannelId`, `FromState`, `ToState`, `Reason`, `Detail?`, `OccurredAtUtc` | история переходов состояния. `Detail` — техническая заметка, **никогда не секрет провайдера** |
| **`ChannelPaymentLog`** | Guid | `ChannelId`, `ChangedByUserId`, `OldPaidUntil?`/`NewPaidUntil?`, `Amount?`, `Comment?`, `ChangedAtUtc` | журнал оплат канала суперадмином, по форме — копия `SubscriptionChangeLog` |
| **`CompanyNotificationSettings`** | **`CompanyId` (PK)** | `EnabledTypeMask` (битовая маска по `NotificationType`, дефолт — все биты), `ReminderLeadMinutes` (60..4320, дефолт 1440), `MinLeadMinutes` (0..720, дефолт 120), `UpdatedAt`, `UpdatedByUserId?` | настройки принадлежат **компании, а не каналу**. Отсутствие строки = дефолты, поэтому backfill существующим компаниям не нужен |
| **`NotificationTemplate`** | Guid | `CompanyId`, `Type`, `Body` (≤1000), `UpdatedAt`, `UpdatedByUserId?` | переопределение платформенного текста. Нет строки или пустой `Body` = платформенный дефолт |
| **`NotificationTemplateHistory`** | Guid | `CompanyId`, `Type`, `PreviousBody`, `ChangedByUserId`, `ChangedAtUtc` | снимок **предыдущего** текста при каждой правке |
| **`OutboundNotification`** | Guid | `CompanyId`, `ChannelId?`, `BookingId?`, `Type`, `RecipientPhone`/`RecipientName?`/`RecipientUserId?`, `Body` (**снимок отрендеренного текста на момент постановки**), `DueAtUtc`, `VisitStartUtc` (денормализовано), `Status`, `Reason?`, `ReasonDetail?`, `AttemptCount`, `LastAttemptAtUtc?` (**он же маркер «в полёте»**), `NextAttemptAtUtc?`, `ProviderMessageId?`, `SentAtUtc?`/`DeliveredAtUtc?`/`ReadAtUtc?`, `Generation` (поколение переносов), `IdempotencyKey` | **одна строка — и очередь, и вечный журнал**: строки никогда не удаляются, меняется только статус |
| **`NotificationOptOut`** | Guid | `Phone` (канонический, **уникальный**), `UserId?` (информационно), `OptedOutAtUtc`, `Source` | одна таблица на всю платформу **по номеру**, а не флаг на `AppUser` + таблица для гостей. Следствие **намеренное**: отписка принадлежит номеру, смена телефона её с собой не уносит |
| **`PlatformSetting`** | **`Key` (PK, ≤100)** | `Value` (≤200), `UpdatedAt`, `UpdatedByUserId?` | параметры платформы, правимые суперадмином без пересборки и с журналом. Ключи цикла 4: `notifications.channel.price-per-month`, `notifications.channel.idle-days`. **Отсутствие ключа цены = опция не предлагается**, а не «цена 0» |
| **`PlatformSettingChangeLog`** | Guid | `Key`, `OldValue?`, `NewValue?`, `ChangedByUserId`, `ChangedAtUtc`, `Comment?` | журнал правок параметров платформы |

Изменения цикла 5 в сущностях цикла 4:

| Сущность | Что добавилось | Зачем |
|---|---|---|
| `NotificationChannel` | ⚖️ `LegalEntityForm?`, `Inn?` | заявка на канал требует ИНН и форму лица; **против ЕГРЮЛ/ЕГРИП не сверяется**, только формальная контрольная сумма (`InnValidator`). Наружу видно только владельцу и суперадмину |
| `NotificationTemplateHistory` | ⚖️ `NewBody`, `AcknowledgedByUserId?`, `AcknowledgedAtUtc?`, `WarningVersion?`, `AdMarkersHit?` | акцепт владельцем ответственности за рекламные маркеры при сохранении шаблона. `NewBody` дублирует `PreviousBody` следующей строки **намеренно** — это журнал, а не нормализованная модель |
| `OutboundNotification` | ⚖️ `ContentRedactedAtUtc?` | отметка, что текст сообщения и данные получателя **затёрты на месте** правилом уничтожения; метаданные (статус, причина, даты) остаются вечным журналом |

#### ⚖️ Сущности цикла 5 (3 новые, одна старая удалена)

| Сущность | Ключ | Ключевые поля | Смысл |
|---|---|---|---|
| **`ConsentRecord`** | Guid | `UserId?` / `SubjectPhone?` + `CompanyId?` (субъект с аккаунтом **или** гость салона), `DocumentKey` (**строка**, ≤64), `DocumentVersion`, `DocumentHash` (SHA-256 **показанного текста**), `Purpose?: ConsentPurpose`, `Act: ConsentAct`, `Source: ConsentSource`, `GrantedAtUtc`, `IpAddress?`/`UserAgent?` (**наружу не отдаются никогда**), `RecordedByUserId?`, `RevokedAtUtc?`, `RevokeReason?` | **журнал событий вместо перезаписываемой строки.** Уникального индекса **нет вообще** — «что сейчас верно» это запрос (`ConsentLedger`), а не строка. Отзыв — проставление `RevokedAtUtc`, **не удаление**. `DocumentHash` — растяжка: если оператор подменит файл, не подняв версию, старые строки будут ссылаться на хеш, которому на диске ничего не соответствует. Индексы — два **частичных** (`CurrentByUser`, `CurrentBySubject`) + `Retention` по `GrantedAtUtc` |
| **`ClientHealthNote`** | Guid | `CompanyId`, `ClientId?` / `GuestPhone?`, **`Ciphertext`**, `KeyId`, `UpdatedAt`, `UpdatedByUserId?`; два **частичных уникальных** индекса | противопоказания и особенности здоровья клиента — **отдельная зашифрованная таблица**, намеренно **не колонка на `ClientNote`**: от `ClientNote` к этому типу нет навигационного свойства нигде в модели, поэтому «заодно» утечь в чью-то будущую проекцию физически нечему. Шифрование — `HealthNoteProtector` поверх `SecretProtector`, AAD привязан к «компания + субъект». **Суперадмину отказывается в доступе явно (`Forbid`), а не отдаётся пустое значение** |
| **`SubjectRequest`** | Guid | `Reference` (человекочитаемый номер), `Kind: SubjectRequestKind`, `SubjectPhone`, `ContactValue`, `Message`, `Status`, `ReceivedAtUtc`, **`DueAtUtc`**, `AnsweredAtUtc?`, `HandlerUserId?`, `Resolution?` | обращение субъекта данных (доступ / исправление / удаление / отзыв согласия / жалоба). Форма **анонимная**; ответ на `POST` одинаков независимо от того, знает ли система этот телефон. `DueAtUtc` **вычисляется и сохраняется в момент приёма** из `SubjectRequests:ResponseWorkingDays` — позднейшая правка конфига не переписывает уже данное человеку обещание. Отвечает **человек** (суперадмин), автоматики нет |

Перечисления: `BookingStatus { Pending, Confirmed, Cancelled, Completed, NoShow }`,
`PaymentStatus { NotRequired, Pending, Paid }`, `UserRole { Client, Master, CompanyOwner, SuperAdmin }`,
⚖️ `PhotoRetention { SixMonths = 0, TwelveMonths = 1 }` — **`Forever = 2` удалён** (миграция
`RemovePhotoRetentionForever` **переписывает** существующие строки в `TwelveMonths`; это удаление
значения, а не перенумерация — `0`/`1` сохранили смысл),
⚖️ **`LegalDocumentType { Privacy = 0, TermsClient = 1, TermsOwner = 2, PdnConsent = 3,
ChannelRiskNotice = 4 }`** — пять членов вместо двух; `Terms` **переименован** в `TermsClient` с
сохранением числового значения `1` (всё уже сохранённое продолжает означать тот же документ, меняется
только строка в JSON — это ломающее изменение контракта, §5.3).
Сериализуются как строки (`JsonStringEnumConverter` в `Program.cs`).

⚖️ **Семь перечислений цикла 5 плюс один намеренно-не-перечисление:**

| Перечисление | Члены | Важное |
|---|---|---|
| `LegalGate` | `Global`, `OwnerScope`, `None` | **где именно блокирует существенная правка документа**. `Global` — 451 на всех защищённых вызовах (Privacy, TermsClient); `OwnerScope` — только на перечисленных действиях владельца (TermsOwner); `None` — не блокирует вовсе (PdnConsent, ChannelRiskNotice). Отсутствующее/нераспознанное значение в манифесте по умолчанию **`Global`** — опечатка оператора обязана блокировать больше, а не меньше |
| `ConsentAct` | `Acknowledged`, `Accepted`, `Consented`, `Confirmed` | правовая природа события — **своя колонка**, а не вывод из `DocumentKey` по switch'у, который забудут обновить |
| `ConsentPurpose` | `ProviderDelivery`, `WorkPhotos`, `HealthData`, `ChannelOffer` | цели внутри `PdnConsent`. `null` означает «документ целиком» |
| `ConsentSource` | 10 членов (`Registration`, `ReAcceptance`, `Profile`, `CompanyCreation`, `ChannelRequest`, `ChannelLink`, `PhotoForm`, `HealthForm`, `Booking`, `Migrated`) | `Migrated` пишет **только миграция**, приложение — никогда |
| `SubjectRequestKind` / `SubjectRequestStatus` | `Access, Rectification, Erasure, ConsentWithdrawal, Complaint` / `Received, InProgress, Answered, Rejected` | обращения субъектов |
| `LegalEntityForm` | `Ip`, `Company`, `SelfEmployed` | **чисто описательное** — определяет ожидаемую длину ИНН (10 или 12), само по себе ничего не гейтит |
| `ProviderDeliveryConsentMode` | `Strict`, `AccountsOnly` (по умолчанию), `Off` | **единственный спорный вопрос права вынесен в конфигурацию, а не в ветку кода.** Влияет **только** на то, проверяет ли `NotificationGate` согласие при постановке в очередь; предъявляется и записывается это согласие **при любом значении флага**. Нераспознанное значение **роняет старт** |
| **`LegalTextKey`** | `BookingNotice`, `TemplateAdWarning`, `UnsubscribePage`, `PhotoConsent`, `HealthDataConsent`, `GuardianConfirmation` | ⚠️ **это статический класс строковых констант, а НЕ перечисление** — намеренно: значения едут в БД (`ConsentRecord.DocumentKey`) и в манифест как строки, и добавление седьмого текста должно быть **правкой манифеста**, а не миграцией с перекомпиляцией каждого switch'а. Ни один из шести не участвует в гейте 451 и ни один не сравнивается с claim'ом JWT |

🆕 **Семь перечислений цикла 4:**

| Перечисление | Члены | Важное |
|---|---|---|
| `ChannelState` | `NotConnected, Connecting, Connected, Disconnected, Blocked, DisabledByOwner, NeedsReconnect, Replaced` | первые семь — ровно то, что видит владелец по SPEC; **восьмое (`Replaced`) — сознательное отступление от буквы спеки**, задокументировано в `ARCHITECTURE_CYCLE4.md` §38.1 |
| `ChannelStateReason` | 11 членов (`Authorized`, `ProviderReportsUnauthorized`, `ProviderReportsBlocked`, `ConsecutiveSendFailuresExceeded`, `DisconnectedByOwner`, `SuspendedByAdmin`, `UnauthorizedInstanceTimedOut`, `IdleInstanceDeleted`, `ReplacedAfterBan`, `SecretUnavailable`) | русский текст для владельца собирается **на сервере** из этого кода, в одном месте |
| `ChannelPaymentStatus` | `NotPaid, Paid, Suspended` | **никогда не хранится** — вычисляется `ChannelPaymentState.Of` |
| `NotificationStatus` | `Pending = 0, Sent, Delivered, Failed, Expired, Skipped, Cancelled` | ⚠️ **`Pending` обязан остаться 0**: частичный индекс диспетчера объявлен сырым SQL-фильтром `"Status" = 0`. Перестановка членов **молча** ломает индекс — он остаётся, но перестаёт совпадать с запросом, и тот сваливается в full scan. Добавлять только в конец |
| `NotificationType` | `BookingConfirmed, Reminder, BookingCancelled, BookingRescheduled, StaffBookingCreated, StaffBookingCancelled` | используется как **позиция бита** в `EnabledTypeMask` → значения тоже append-only. Два последних члена **никем не ставятся в очередь** — US-34 не реализована (§5.1) |
| `NotificationTransport` | `WhatsApp` | один член; заведено enum'ом заранее, чтобы второй транспорт был новым членом + адаптером, а не миграцией |
| `NotificationReason` | 11 членов (`Delivered`, `RecipientHasNoWhatsApp`, `RejectedByProvider`, `RetriesExhausted`, `VisitAlreadyStarted`, `RecipientOptedOut`, `NotOnPaidPlan`, `NoUsableChannel`, `TypeDisabledByCompany`, `BelowMinimumLeadTime`, `BookingOrAssignmentCancelled`) | русский текст журнала доставки собирает сервер (`NotificationTexts`) |

### Как это связано смыслово

- Аккаунт = телефон **в канонической форме** (цикл 2, US-26). `UserName == PhoneNumber ==` только цифры,
  без `+`, скобок, пробелов и дефисов; 11-значный номер с `8` превращается в `7…`, 10-значный с `9`
  дополняется до `7XXXXXXXXXX`, всё остальное сохраняет цифры как есть (10..15 цифр — E.164).
  Единственная реализация — `Services/PhoneNormalizer.cs` (чистый статический класс). Применяется во
  всех точках входа: register, login, change-phone, AddMember (поиск и автосоздание), гостевая запись,
  поиск в админке, сид SuperAdmin. Уникальность даётся Identity-индексом. Email опционален.
- Тариф привязан к **владельцу** (`Company.OwnerUserId`), одна подписка покрывает все его компании-филиалы.
  Разрешение тарифа — `SubscriptionResolver`; при отсутствии/неактивности/просрочке падаем в
  `EffectivePlan.Free` = `{OnlineBooking: false, Mailing: false, Analytics: false, PublicListing: true,
  OnlinePayment: false, MaxEmployees: 1, MaxCompanies: 1}`.
- Расписание **датовое**, не по дням недели (миграция `DateBasedSchedule`). `WeeklyScheduleTemplate`
  — только заготовка, которую `POST /api/schedule-template/apply` разворачивает в строки `WorkingHours`.
- `Booking.Price` и `Booking.CommissionPercent` — снимки `Service.Price` и `CompanyMember.CommissionPercent`
  на момент создания; отчёты читают их, а не текущие значения (иначе уход мастера из компании
  обнулял бы историческую комиссию).
- **Фото привязаны к заметке, а не к записи.** Заметка опционально ссылается на визит (`BookingId?`);
  фото наследует компанию заметки денормализованным полем — чтобы квота считалась одним
  индексированным агрегатом, а раздача проверялась одной строкой без join'а.
- **Квота и срок хранения фото — свойства тарифа** (`PhotoQuotaMb`, `PhotoRetention`), разрешаются тем
  же `SubscriptionResolver`; на Free-базлайне — 100 МБ и 6 месяцев.
- ⚖️ **Согласие хранится в трёх видах и с разным сроком жизни** (переделано в цикле 5).
  «Что произошло» — **журнал `ConsentRecord`**: строка на каждое событие, с версией документа и хешем
  показанного текста; отзыв ставит `RevokedAtUtc`, строка не удаляется. «Что сейчас верно» — **запрос**
  через `ConsentLedger`, а не хранимое поле. «Быстрая проверка на каждом запросе» — claim'ы в JWT
  против снимка документов в памяти, **без похода в БД** (`LegalConsentFilter`, `RequiresOwnerTerms`).
  «Исторический факт» — снимок версий **на самой записи** (`Booking`), который переживает и смену
  редакции документа, и удаление аккаунта, и относится в том числе к гостю, у которого аккаунта нет.
  Одна таблица обслуживает и субъектов с аккаунтом, и гостей салона (`SubjectPhone` + `CompanyId`) —
  цена этого решения два частичных индекса вместо одного уникального, выгода в том, что читатель,
  выгрузка и «что помнить при следующей правке» существуют по одному разу, а не по два.
- ⭐ **Удалённый аккаунт — надгробие, а не отсутствие строки** (`AppUser.DeletedAtUtc`, §4.15).
  Записи такого клиента остаются в истории компании анонимизированными, с флагом
  `Booking.ClientDeleted`.
- 🆕 **Канал принадлежит аккаунту владельца, компания — только назначается на него.** Это та же
  ось, что у подписки (`AccountSubscription.OwnerUserId`): один номер обслуживает все филиалы
  владельца. Компания при этом может быть назначена **максимум на один канал** (уникальный индекс).
- 🆕 **Часовой пояс компании выводится из города, но может быть перебит вручную.**
  `TimeZoneIsManual` существует ровно для того, чтобы последующее сохранение города **не сбрасывало**
  ручную правку обратно на зону города. Логика — чистый `CompanyTimeZoneResolver`
  (`ForNewCompany` / `ForUpdate`).
- 🆕 **`OutboundNotification` — очередь и журнал в одной таблице.** Идемпотентность даёт
  `IdempotencyKey` с участием `Generation` (поколение переносов), так что перенос записи может
  поставить новое напоминание, не столкнувшись со старым. `LastAttemptAtUtc` пишется **до** исходящего
  HTTP-вызова и работает маркером «в полёте» на `InFlightGraceMinutes`.
- 🆕 **Отписка живёт по номеру телефона, а не по человеку** — см. комментарий на `NotificationOptOut`.

### 💳 Биллинг-аккаунты и каталог тарифов — пять новых сущностей (цикл 7)

**Смысл модели в одной фразе:** платит не человек, а **биллинг-аккаунт**; у компании теперь две
независимые оси — `Company.BillingAccountId` (кто платит) и `Company.OwnerUserId` (кто управляет).
Смена ответственного за компанию не трогает деньги и не отвязывает номер уведомлений. Перенос
компании между аккаунтами (продажа филиала) — **отдельная** операция, только через администратора.

| Сущность | Файл | Ключевые поля |
|---|---|---|
| **`BillingAccount`** | `Core/Entities/BillingAccount.cs` | `Id`, `OwnerUserId` + `Owner`, `Name?`, **`GrandfatheredEmployeeBonus`** (бонус сотрудников, начисленный при миграции), `CreatedAtUtc`/`UpdatedAtUtc`; **заявка владельца лежит полями прямо здесь** — `RequestedPlanId`+`RequestedPlan`, `RequestedOptionsJson`, `RequestedAtUtc`, `RequestedByUserId`, `RequestedComment`, `LastRejectionReason`, `LastRejectedAtUtc` (см. §9 B2) |
| **`SubscriptionOption`** | `Core/Entities/SubscriptionOption.cs` | `Code`, `Name`, `Description?`, **`Kind`** (`OptionKind`: переключатель или количество), `CapabilityKey?`, `PricePerMonth?`, `UnitName?`, `MaxQuantity?`, `UnitPriceText?`, `IsPublic`, `IsActive`, `SortOrder` |
| **`AccountSubscriptionOption`** | `Core/Entities/AccountSubscriptionOption.cs` | связка `BillingAccountId` × `OptionId`, `Quantity` (по умолчанию 1), `PaidUntilUtc?`, `EndsAtUtc?`, `ActivatedAtUtc?`/`ActivatedByUserId?`, плюс «запрошенное» — `RequestedQuantity?`, `RequestedAtUtc?`, `RequestedByUserId?` |
| **`PlanOptionRule`** | `Core/Entities/PlanOptionRule.cs` | матрица «тариф × опция»: `PlanConfigId` × `OptionId`, **`Availability`** (`OptionAvailability`), `IncludedQuantity?`. ⚠️ Отсутствие строки трактуется как `Unavailable`, а не как «можно докупить» (коммит `889b63d`) |
| **`CompanyOwnerChangeLog`** | `Core/Entities/CompanyOwnerChangeLog.cs` | `CompanyId`, `OldOwnerUserId` → `NewOwnerUserId`, `ChangedByUserId`, `ChangedAtUtc`, `Comment?`, **`WithTransfer`** (была ли смена ответственного одновременно переносом между аккаунтами) |

Новые перечисления: `Core/Enums/OptionKind.cs`, `OptionAvailability.cs`, `SubscriptionChangeKind.cs`.

**`BillingAccountId` добавлен в четыре существующие сущности** — `Company`, `AccountSubscription`,
`SubscriptionChangeLog`, `NotificationChannel`. ⚠️ **Во всех четырёх тип объявлен как `Guid?`**
(`Core/Entities/Company.cs:56`, `AccountSubscription.cs:18`, `SubscriptionChangeLog.cs:22`,
`NotificationChannel.cs:22`), хотя обязательность держит БД (`NOT NULL` из `AddCoTenancyConstraints`).
Ветки обработки `null` в коде **недостижимы** — см. §9 B5.

`SubscriptionChangeLog` расширен колонками переноса (`04b2df7`) и `SubscriptionChangeKind`.

**Каналы уведомлений оплачиваются за номер.** `NotificationChannel.BillingAccountId` + оплаченное
количество опции на аккаунте: финансирование получают **N заведённых раньше** каналов, остальные
молчат, но **назначений не теряют** (`ChannelFunding`, `NotificationGate.Evaluate` теперь принимает
`channelIsFunded` явным аргументом — `676d1ec`).

**Лимит сотрудников стал суммарным по аккаунту**, а не по компании: `AccountUsageReader` считает
занятое по всем компаниям аккаунта, лимит = лимит тарифа + опции + `GrandfatheredEmployeeBonus`.
⚠️ `CompanyManagePage` раньше показывал `maxEmployees` аккаунта как лимит компании — исправлено
(`ac5c6c2`).

### 📸 Журнал изменений записи и фотогалерея салона — две новые сущности (цикл 10)

| Сущность | Файл | Ключевые поля |
|---|---|---|
| **`BookingEvent`** | `Core/Entities/BookingEvent.cs` | `Id`, `BookingId`, **`CompanyId`** (денормализованная копия `Booking.CompanyId` — проверка доступа к истории и правило уничтожения читают её **без join'а** к `Bookings`, тот же приём, что у `ClientNotePhoto.CompanyId`), `Kind: BookingEventKind`, `OccurredAtUtc`, `ActorKind: BookingActorKind`, `ActorUserId?`, **`ActorNameSnapshot?`** и `ActorRoleSnapshot?` (снимок имени и роли автора — переживает удаление аккаунта и увольнение, как `Booking.Price`), `PreviousDate?`/`PreviousStartTime?`/`NewDate?`/`NewStartTime?` (только для `Rescheduled`), `CancellationReason?` (только для `Cancelled` и только если причину указали) |
| **`CompanyPhoto`** | `Core/Entities/CompanyPhoto.cs` | `Id`, `CompanyId`, `Url`, `ThumbnailUrl`, `ContentType`, `SizeBytes`, `Width`, `Height`, **`ContentHash`** (SHA-256 **обработанных** байт, уникальность в пределах компании), **`Position`** (0-based, `0` = обложка), `UploadedByUserId?`, `CreatedAtUtc` |

Новые перечисления: `Core/Enums/BookingEventKind.cs` (`Created`, `Rescheduled`, `Cancelled`,
`Completed`, `NoShow`, `PaymentMarked` — значения **никогда не перенумеровываются**, журнал
append-only) и `Core/Enums/BookingActorKind.cs` (`Client`, `Guest`, `Staff`, `SuperAdmin`, `System`;
⚠️ `System` в цикле 10 **не пишет никто** — значение заведено «на вырост»).

Навигации у существующих сущностей: `Booking.Events` и `Company.Photos`. Новых колонок у старых
таблиц цикл **не добавил**.

⚠️ **Три правила этой части модели, которые легко нарушить:**
1. **`BookingEvents` — append-only, и писатель ровно один:**
   `ServiceBooking.API/Services/Bookings/BookingEventLog.cs`. Ни один контроллер не добавляет строку
   напрямую; точек вызова шесть (создание, перенос, отмена, «выполнено», «не пришёл», отметка
   оплаты), и запись события идёт **в той же транзакции**, что и само изменение записи
   (`ARCHITECTURE_CYCLE10.md` §105).
2. **Backfill'а истории у старых записей нет** (решение заказчика П3): у записи, созданной до
   внедрения журнала, история просто пуста, и это отличается от «истории не было» — см.
   `precedesJournal` в `API_CONTRACT_CYCLE10.md` §122.2.
3. **Уникальность `(CompanyId, Position)` у фотографий держит НЕ база, а сервер.** Уникального
   индекса намеренно нет: недеферрируемый уникальный индекс Postgres не пережил бы построчные
   `UPDATE` от EF при перестановке. Порядок применяется `CompanyPhotoOrdering` под advisory-lock'ом
   `company-photos:{companyId}`. Чтение всюду детерминировано: `Position` → `CreatedAtUtc` → `Id`;
   есть защита от дублей `Position == 0` при поиске обложки (`b09ad38`).

📸 **Фото салона сознательно живут вне квоты и вне ретенции клиентских фото** (решения П5/П6): они
**не** расходуют `PhotoQuotaMb`, **не** попадают под `ClientNotePhotoDays` и
`photo-retention-cleanup`, лежат в **публичном** классе хранения (`/uploads/...`, как логотип) и
отдаются анонимно. Ограничение — **10 фото на компанию** (`CompanyPhotoOrdering.MaxPhotosPerCompany`)
и размер файла, а не мегабайты тарифа.

⚠️ **Файлы удалённых фотографий.** Каскад от `Company` удаляет **строки**, но не файлы на диске.
Сегодня это недостижимо (жёсткого удаления компании в продукте нет — `DeleteAccount` отказывается
удалять аккаунт, владеющий компанией, деактивация лишь ставит `IsActive = false`), но тот, кто
заведёт эндпоинт «удалить компанию», обязан удалить и файлы — предупреждение записано комментарием
прямо в `CompanyPhoto.cs`.

### Миграции (📸 53, все в `ServiceBooking.Infrastructure/Migrations/`)

Первые 13 — как раньше: `InitialCreate` → `DateBasedSchedule` → `AddSubscriptionAndCommission` →
`AddReviewsTemplatesNotes` → `AddPromoGiftMailPlans` → `AddPrepaymentSupport` →
`LinkSubscriptionsToPlanConfigs` → `RemovePlanKey` → `AccountLevelSubscriptions` →
`AddBookingPriceSnapshot` → `RemovePromoCodesAndGiftCertificates` → `AddPublicListingAndOnlinePayment` →
`AddCompanyIdToClientNote`.

Цикл 1 добавил пять: `DeduplicateWorkingHours` → `AddWorkingHoursUniqueIndex` →
`AddCompanyMemberCommission` → `DeduplicateCompanyMembers` → `AddCompanyMemberUniqueIndex`
(парами: сначала чистка дублей, затем уникальный индекс).

Цикл 2 добавил ещё пять: `AddClientNoteBookingId` → `AddClientNotePhotos` → `AddPlanPhotoLimits` →
`AddScheduledTaskState` → **`NormalizePhoneNumbers`**.

⭐ Цикл 3 добавил четыре: **`ResyncIdentityRoles`** → **`AddLegalConsent`** →
**`AddUserDeletionTombstone`** → **`AddMaxLengthToConsentVersions`**.
`ResyncIdentityRoles` — **миграция данных**: разово пересчитывает Identity-роли по строкам
`CompanyMember` тем же правилом, что `IdentityRoleSync`, и чинит историю «роль осталась после
удаления из компании». Её **`Down` — намеренный no-op**: откатывать пересчёт ролей бессмысленно.
`AddMaxLengthToConsentVersions` — поздняя правка по ревью: ограничение длины строк версий
(до неё колонки были `text` без потолка).

`NormalizePhoneNumbers` — единственная **разрушительная** миграция проекта и сознательно последняя в
цикле. Она приводит уже лежащие в БД номера к канонической форме (ручная транслитерация
`PhoneNormalizer.Normalize` в SQL, строго `[^0-9]`, не `\D`), а при коллизии (два аккаунта сводятся к
одному номеру) — оставляет аккаунт с наибольшим числом связанных строк (записи как клиент + как мастер
+ членства), при равенстве — самый ранний по `CreatedAt`; заметки/отзывы/журналы рассылок проигравшего
**переназначаются** победителю, остальное каскадно удаляется вместе с аккаунтом. Если проигравший
владеет компанией или имеет записи как мастер — миграция **останавливается с описательной ошибкой**,
а не молча меняет данные. Проект не в продакшене, боевых данных нет.

🆕 **Цикл 4 добавил две:**

1. **`20260918051815_AddNotificationChannels`** — вся схема цикла одним файлом, **собранным блоками**:
   таблица `Cities` + сид **91 города** (Калининград → Камчатка, с IANA-зонами) → колонки
   `CityId`/`TimeZoneId`/`TimeZoneIsManual` на `Companies` → **backfill существующих компаний на
   Барнаул / `Asia/Barnaul`** (UTC+7, **отдельная зона, не новосибирская**) → одиннадцать таблиц
   уведомлений и каналов → `AllowNotificationChannel` на планах → индексы, включая **частичный
   `IX_OutboundNotifications_Dispatch` с сырым фильтром `"Status" = 0`**.
2. **`20260921082526_AddChannelLastStateReason`** — поздняя правка по ревью: колонка
   `NotificationChannels.LastStateReason` (кеш последней причины перехода, чтобы список каналов не
   делал второй запрос на строку).

⚠️ **Правило, действующее с момента мёржа цикла 4:** первую из этих двух миграций **не редактировать**.
Она уже применена, и всё новое оформляется **новыми** миграциями — как это и сделала вторая.

⚖️ **Цикл 5 добавил восемь**, все от 2026-09-21, в порядке применения:

1. **`ConsentJournal`** — ⚠️ **самая ответственная миграция цикла и необратимая по данным.** Создаёт
   `ConsentRecords` с двумя частичными индексами, **переносит `INSERT…SELECT`-ом каждую строку
   `UserConsents`** в журнал (`Act = Accepted`, `Source = Migrated`, хеш/IP/User-Agent пустые — этой
   информации на `UserConsent` никогда не было) и **дропает таблицу `UserConsents`**. `Down` таблицу
   пересоздаёт, но **пустой**: откат структуры возможен, откат данных — нет. На проде это `INSERT`
   на ноль строк (боевых данных нет), путь существует ради dev/test-баз.
2. **`RemovePhotoRetentionForever`** — переписывает данные: `PhotoRetention.Forever` → `TwelveMonths`.
3. **`AddClientHealthNotes`** — таблица противопоказаний + два частичных уникальных индекса.
4. **`AddBookingGuardianFields`** — `BookedForOther`, `GuardianConfirmed*`, `BookingNoticeVersion`.
5. **`AddChannelLegalEntityFields`** — `LegalEntityForm`, `Inn` на канале.
6. **`AddTemplateAcknowledgement`** — поля акцепта и `AdMarkersHit` на истории шаблонов.
7. **`AddRedactionAndWidenSettings`** — `ContentRedactedAtUtc` и расширение полей настроек.
8. **`AddSubjectRequests`** — таблица обращений субъектов.

🗓 **Цикл 6 добавил четыре**, в порядке применения:

1. **`20260921191620_BackfillSubscriptionPaidUntil`** — **миграция данных**: подпискам без даты
   окончания ставит «оплачено на год вперёд» (`PaidUntil IS NULL` → `now() + interval '1 year'`),
   потому что с этого цикла дата окончания обязательна и состояние «дата не задана» должно
   перестать существовать. **`Down` — намеренный no-op**: какие строки были `NULL` до прогона,
   обратно уже не восстановить.
2. **`20260921191736_AddMemberProvidesServices`** — колонка `CompanyMembers.ProvidesServices`
   с дефолтом `true`, так что видимость существующих участников не меняется.
3. **`20260921192244_AddCompanyBookingHorizonDays`** — колонка `Companies.BookingHorizonDays`
   с дефолтом 90.
4. **`20260922053047_AddBookingServices`** — таблица `BookingServices` (уникальный индекс
   `(BookingId, Position)` + индекс по `ServiceId`, FK на `Bookings` `Cascade` и на `Services`
   `Restrict`) **плюс backfill одним `INSERT…SELECT`**: каждой существующей записи заводится ровно
   одна строка с `Position = 0`, именем услуги (`COALESCE(s."Name", 'Услуга')` — услуга могла быть
   удалена), длительностью, посчитанной из `EndTime - StartTime`, и ценой записи. Благодаря этому
   «у визита всегда есть хотя бы одна строка состава» — инвариант, а не пожелание.
   `Down` дропает таблицу целиком.

💳 **Цикл 7 добавил одиннадцать** — самый большой миграционный блок за всю историю проекта,
в порядке применения:

1. **`20260921193859_AddPricingCatalog`** — каталог тарифов/витрины (в коммите помечен как «цикл 5»
   из-за коллизии имён, см. §9 B10 — это цикл 7).
2. **`20260922052509_AddBillingAccounts`** — таблицы `BillingAccounts` и `CompanyOwnerChangeLogs`.
3. **`20260922062335_BackfillBillingAccounts`** ⚠️ — **миграция данных**: заводит аккаунт каждому
   владельцу и проставляет `Company.BillingAccountId`. Источников владельцев **три**, и все три
   объединяются (`e928600`, находка ревью B2). Здесь же начисляется
   **`GrandfatheredEmployeeBonus`** — «грандфатеринг»: у кого сотрудников больше нового лимита,
   бонусом получает разницу, чтобы миграция никого не отключила.
4. **`20260922070734_AddChannelBillingAccountAndSubscriptionOptions`** — `NotificationChannel.BillingAccountId`
   и таблица `AccountSubscriptionOptions`.
5. **`20260922070754_BackfillChannelBillingAccounts`** ⚠️ — **миграция данных**: привязывает
   существующие каналы к аккаунтам и заводит оплаченное количество под уже работающие номера.
6. **`20260922073745_AddSubscriptionChangeLogTransferColumns`** — колонки переноса в журнале.
7. **`20260922075732_AddPlanOptionRulesAndSubscriptionRequestColumns`** — матрица `PlanOptionRules`
   и поля заявки на `BillingAccounts`.
8. **`20260922111125_AddCoTenancyConstraints`** ⚠️⚠️ — **необратимая и самая опасная**: делает
   `BillingAccountId` `NOT NULL`, добавляет альтернативные и составные ключи и составные FK, чтобы
   компания, её подписка, её каналы и её опции физически не могли разъехаться по разным аккаунтам
   («co-tenancy» — арендатор один на всю связку). Именно на этой миграции упал деплой на стенд
   (§8, §9 B1).
9. **`20260922121140_SeedBillingCatalog`** ⚠️ — **миграция данных**: системный бесплатный тариф,
   каталог опций, правила матрицы и backfill платных каналов.
10. **`20260922123507_AddBillingAccountLastRejectionColumns`** — `LastRejectionReason`/`LastRejectedAtUtc`
    (добавлена по итогам ревью).
11. **`20260922154148_FixSeedBillingCatalogCapabilityKeys`** — правит ключи возможностей, засеянные
    девятой миграцией: они должны быть `companies`/`employees`, а не коды опций (`d60e29b`).

⚠️ Пункты 10 и 11 — **починки по итогам ревью уже поверх своих же миграций**. Это значит, что на
любой базе, куда 9-я миграция успела приехать до 11-й, состояние каталога между ними было
некорректным; накатывать надо весь блок целиком.

📸 **Цикл 10 добавил две — и обе чисто добавочные, без миграции данных:**

1. **`20260923055550_AddBookingEventJournal`** — таблица `BookingEvents` (FK на `Bookings` каскадом,
   на `AspNetUsers` — `SetNull`; индексы `(BookingId, OccurredAtUtc)`, `(CompanyId, OccurredAtUtc)`
   и `ActorUserId`).
2. **`20260923060446_AddCompanyPhotos`** — таблица `CompanyPhotos` (FK на `Companies` каскадом,
   уникальный индекс `(CompanyId, ContentHash)`, индекс `(CompanyId, Position)` — **не** уникальный,
   см. выше).

Ни одна из двух не переписывает существующие данные, не делает колонку `NOT NULL` и не трогает
старые таблицы, поэтому обе обратимы `Down`'ом. Список разрушительных ниже цикл 10 **не пополнил**.

Итого **разрушительных по данным миграций в проекте теперь восемь**: `NormalizePhoneNumbers`
(цикл 2), `ConsentJournal` и `RemovePhotoRetentionForever` (обе — цикл 5), 🗓
`BackfillSubscriptionPaidUntil` (цикл 6 — переписывает `NULL` на дату без возможности отката) и 💳
четыре из цикла 7 (`BackfillBillingAccounts`, `BackfillChannelBillingAccounts`,
**`AddCoTenancyConstraints` — необратима структурно**, `SeedBillingCatalog`).
Проект не в продакшене, боевых данных нет.

История по-прежнему видна прямо в названиях: промокоды и подарочные сертификаты были добавлены и затем
**удалены целиком**, а подписка переехала с компании на аккаунт владельца. Остатков этих фич в коде нет.

---

## 4. Что реализовано

Ниже — по функциональным блокам. Все эндпоинты выписаны из атрибутов контроллеров, а не из документации.

🗓 **Где искать цикл 6:** §4.1 (вход, регистрация, телефон — US-60/US-61), §4.2 (признак «оказывает
услуги» — US-62; горизонт записи в настройках компании — US-65), §4.5 (слоты, календарь месяца,
рабочее окно, несколько услуг за визит, перенос записи — US-64…US-67), §4.6 и §4.10 (подписки и
диагностика тарифа — US-63), §4.9 (как визит из нескольких услуг попадает в отчёты), §4.11 (почему
виджет остался на одной услуге).

📸 **Где искать цикл 10:** §4.20 — три блока цикла одним разделом (свобода ручной записи, журнал
изменений записи, фотогалерея салона). Затронуты также §4.2 (`includeHidden` у списка мастеров,
фотографии в `CompanyDto`), §4.5 (`extendedHours`/`staffMode`/`scheduleState` у `/availability`,
новый `GET /api/bookings/{id}/history`, **удаление `ManualBookingModal`**), §4.12 и §4.18
(тексты ошибок загрузки стали русскими; новое правило уничтожения `booking-event`).

💳 **Где искать цикл 7:** §4.19 — весь биллинг одним блоком (модель, эндпоинты владельца и
администратора, перенос компаний, витрина цен, экраны). Затронуты также §4.6 (старый контур
подписок — часть его маршрутов отозвана в `410`), §4.17 (WhatsApp: опция заведена, но цены нет и она
непубличная — §9 B9) и §4.2 (лимит сотрудников стал суммарным по аккаунту).

### 4.1 Аутентификация и профиль — работает

`ServiceBooking.API/Controllers/AuthController.cs`, `ProfileController.cs`, `Services/TokenService.cs`

| Метод | Путь | Доступ |
|---|---|---|
| POST | `/api/auth/register` | анонимно; телефон нормализуется, невалидный → 400; выдаёт роль `Client`; ⚖️ **требует объект `legal` с двумя версиями** (`privacyAcknowledgedVersion`, `termsAcceptedVersion`) вместо прежнего `acceptedLegal: true` — **ломающее изменение цикла 5**, версии сверяются со снимком на сервере; согласие на обработку ПДн (`PdnConsent`) через этот вызов принять нельзя **by design**; лимит `auth-register` 5/час на IP |
| POST | `/api/auth/login` | анонимно; поиск по канонической форме телефона, lockout после 5 попыток на 15 мин; невалидный номер даёт тот же 401, а не 400; ⭐ лимит `auth-login` 10/мин на IP. 🗓 **Причины отказа различаются с цикла 6** (`Services/LoginOutcome.cs`): **423** — аккаунт временно заблокирован Identity, **403** — вход запрещён (`IsNotAllowed`), **401** — и «аккаунт не найден», и «неверный пароль». Одинаковый 401 у двух последних — **намеренная защита от перебора номеров**, а не недоделка |
| GET / PUT | `/api/profile` | авторизованные |
| POST | `/api/profile/change-password` | авторизованные |
| POST | `/api/profile/change-phone` | авторизованные; требует текущий пароль, через `SetUserNameAsync` |
| POST | `/api/profile/avatar` | авторизованные; свой аватар (id из токена, route-параметра нет), ≤5 МБ, rate limit `uploads`, профиль обработки `Avatar` (512 px, квадратный кроп) |
| **GET** | **`/api/profile/export`** ⭐ | авторизованные; выгрузка своих данных, см. §4.15 |
| **POST** | **`/api/profile/delete-account`** ⭐ | авторизованные; удаление аккаунта, см. §4.15 |

JWT: HS256, срок **7 дней**, claims `sub/phone/given_name/family_name/jti/role` + **`sstamp`**
(хеш `SecurityStamp`, цикл 1) + ⭐ **claim'ы принятых версий правовых документов** (цикл 3) — именно
по ним `LegalConsentFilter` решает, отдавать ли 451, не заглядывая в БД.
Важные детали в `Program.cs` (`JwtBearerEvents.OnTokenValidated`), на каждом запросе:
роли **перечитываются из БД** и подменяют claim'ы токена (отзыв роли действует немедленно), и
**сверяется хеш `SecurityStamp`** — смена пароля или телефона инвалидирует все ранее выданные токены.

`GET /api/profile` дополнительно отдаёт `ProfilePlanDto` для `CompanyOwner` — показывает реальную
строку подписки (в т.ч. просроченную), а не нормализованный Free.

Фронт: `LoginPage.tsx`, `RegisterPage.tsx`, `ProfilePage.tsx`, `store/authStore.ts` (persist в localStorage),
`api/client.ts` — интерцептор на 401 делает `logout()` + редирект на `/login`.

🗓 **Что цикл 6 изменил здесь (US-60, US-61):**

- **Тело ошибки регистрации от Identity — это МАССИВ `{code, description}`** (`application/json`),
  а не строка, и это намеренно оставлено как есть. Разбирает его теперь фронт:
  `frontend/src/utils/authError.ts` переводит коды (`PasswordTooShort`, `PasswordRequiresDigit`,
  `DuplicateUserName` → «Этот телефон уже зарегистрирован» и т.д.) в русский текст. До цикла 6
  любое тело-не-строка молча превращалось в «Проверьте введённые данные» — именно это и прятало
  настоящую причину отказа.
- **Форма регистрации показывает требования к паролю до отправки**, а не после отказа сервера.
- ⚠️ **Парольная политика Identity НЕ менялась — это прямое решение заказчика.** Один агент в
  середине цикла «согласовал» её с документацией, ослабив; коммит `1615e8f` **откачен** коммитом
  `95a2b14`, а документация приведена к фактической политике (`d54caf8`). Не ослаблять повторно.
- **Телефон: при СОЗДАНИИ новых данных принимается только российский формат.**
  `PhoneNormalizer.IsRussian` / `TryNormalizeRussian` (длина 11, первая цифра `7`) вызывается ровно
  в четырёх точках: `AuthController.Register`, `ProfileController.ChangePhone`,
  `CompaniesController.AddMember`, гостевая запись в `BookingsController.Create`. **Вход по-прежнему
  зовёт `Normalize`** — аккаунт с иностранным номером, заведённый раньше, продолжает работать.
  Известное принятое следствие: код `+7` общий с Казахстаном, казахстанский номер проверку проходит.
- **Фронт: `components/ui/PhoneInput.tsx`**, маска `+7 (800) 800-80-01`. У формы входа режим
  `restrictToRussia={false}`: маска держится, **пока введённое похоже на российский номер**
  (`utils/phone.ts` → `looksRussian`), и отпускает, как только появился чужой код страны — дальше
  поле работает как обычный текст, а цифры из него извлекает сервер.

### 4.2 Компании — работает

`Controllers/CompaniesController.cs` (635 строк — самый большой контроллер)

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/companies` | публично; фильтр `ShowInPublicListing && plan.AllowPublicListing` |
| GET | `/api/companies/my` | владелец — свои компании |
| GET | `/api/companies/member` | все компании, где я участник любой роли |
| GET | `/api/companies/{slug}` | публично |
| GET | `/api/companies/{id}/masters?serviceId=` 📸 `&includeHidden` | публично; **фильтр по ролям `Master`/`CompanyOwner`** (цикл 1); 🗓 **+ фильтр по `ProvidesServices == true`**; 🗓 кривой `serviceId` теперь даёт 400, а не игнорируется. 📸 **`includeHidden=true` — просьба, а не право**: сервер выполняет её, только если вызывающий сам персонал этой компании (или SuperAdmin), и тогда в список попадают участники с `ProvidesServices == false` (в DTO приезжает `providesServices`). Всем остальным флаг не даёт ничего |
| 📸 GET | `/api/companies/{id}/photos` · POST `/photos` · DELETE `/photos/{photoId}` · PUT `/photos/order` | `CompanyPhotosController` — см. §4.20 |
| GET | `/api/companies/{id}/members` | владелец/SuperAdmin |
| POST | `/api/companies` | авторизованные; **лимит `MaxCompanies`** под advisory lock, 402 при превышении. 🆕 **ЛОМАЮЩЕЕ: `cityId` обязателен** — без него 400. Опционально `timeZoneId` (перебивает зону города и ставит `TimeZoneIsManual`) |
| PUT | `/api/companies/{id}` | владелец; 🆕 принимает `cityId` и `timeZoneId`, разрешает их через `CompanyTimeZoneResolver.ForUpdate` (ручная зона переживает смену города); 🗓 принимает `bookingHorizonDays`: поле не прислали или `null` → не трогаем, `0` → явный сброс в дефолт 90, вне `[1, 365]` → **400** (`BookingHorizon.TryNormalize`) |
| POST | `/api/companies/{id}/logo` | владелец; ≤5 МБ, rate limit `uploads`, **тип определяется по сигнатуре файла**, ре-энкод профилем `CompanyLogo` (512 px), старый файл удаляется **после** коммита нового URL |
| GET | `/api/companies/{id}/photo-usage` | персонал компании **или SuperAdmin**; занятый объём, число фото, квота, % и срок хранения — единственное место, где SuperAdmin получает цифры по клиентским фото (содержимое ему недоступно) |
| POST | `/api/companies/{id}/members` | владелец; **лимит `MaxEmployees`** под advisory lock, 402; телефон нормализуется; неизвестное имя роли → 400 |
| PUT | `/api/companies/{id}/members/{memberId}/services` | владелец |
| PUT | `/api/companies/{id}/members/{memberId}/commission` | владелец; clamp 0..100; пишет в `CompanyMember.CommissionPercent` |
| 🗓 PUT | `/api/companies/{id}/members/{memberId}/provides-services` | владелец; тело `{ providesServices, confirm }`. Включение — всегда без вопросов; **выключение у участника с будущими записями → 409**, и повторить нужно с `confirm: true`. Уже созданные записи при этом не отменяются — участник просто перестаёт предлагаться новым клиентам |
| DELETE | `/api/companies/{id}/members/{memberId}` | владелец |
| GET | `/api/companies/{id}/stats?from&to` | владелец; выручка, новые клиенты, топ услуг, по мастерам, по дням |

Автосоздание аккаунта мастера по телефону при `AddMember`: пароль выводится детерминированно —
`"Sb" + последние 6 цифр **канонического** телефона`, добитый нулями до 8 символов. После US-26 для
номера, введённого как «8 999…», результат отличается от того, что был до цикла 2.

Фронт: `pages/owner/CompanyManagePage.tsx` (вкладки «Услуги / Расписание / Сотрудники / Настройки»
+ блок кода для встраивания виджета + индикатор занятого под фото места),
`pages/CabinetPage.tsx` (вкладка «Мои компании» с созданием), `pages/CompanyPage.tsx` (публичная витрина).

### 4.3 Услуги — работает

`Controllers/ServicesController.cs`: `GET /api/services?companyId=` (публично),
`POST`, `PUT /{id}`, `DELETE /{id}` (удаление — мягкое, `IsActive = false`),
**`POST /api/services/{id}/image`** ⭐ (загрузка картинки услуги, профиль `ServiceImage` 1200 px,
rate limit `uploads`).

Права ужесточены в цикле 2: `CanManageCompany` здесь теперь — **только `CompanyOwner` (+SuperAdmin)**,
мастер больше не управляет каталогом услуг. `CreateServiceDto` **не принимает `ImageUrl`** — единственный
способ задать картинку — специальный эндпоинт загрузки (иначе клиент мог указать произвольный путь
`/uploads/...` и удалить чужой файл при замене). Есть валидация: `Name` 1..200,
`DurationMinutes` 1..1440, `Price` 0..1 000 000.

### 4.4 Расписание мастеров — работает

`Controllers/WorkingHoursController.cs`: `GET /api/workinghours?masterId&companyId&from&to`
(**теперь с проверкой `CanManage`** — цикл 1 закрыл утечку чужого расписания),
`PUT /api/workinghours` (upsert дня + полная перезапись перерывов), `DELETE /api/workinghours/{id}`.

`Controllers/ScheduleTemplateController.cs`: `GET`/`PUT /api/schedule-template`,
`POST /api/schedule-template/apply?masterId&companyId&from&to` — раскатка недельного шаблона по датам
(ISO: Пн=1 … Вс=7), существующие дни перезаписываются.
Цикл 1 добавил здесь: транзакцию + advisory lock на `PUT` (delete-then-insert стал атомарным) и на
`apply` (find-or-create больше не даёт дублей против нового уникального индекса), а также валидацию
диапазона — `to >= from` и не более **366** дней за один вызов (366, а не 365, чтобы високосный год
раскатывался одним запросом).

Фронт: `pages/owner/ScheduleTab.tsx` (календарь по месяцам, перерывы, сортировка мастеров с владельцем
первым), `components/schedule/WeeklyTemplateModal.tsx`.

### 4.5 Слоты и бронирование — работает, это ядро продукта

`Controllers/BookingsController.cs` + `Services/SlotService.cs`

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/bookings/occupied?masterId&date` | **`[Authorize]`** (цикл 1): SuperAdmin, сам мастер или персонал компании, где этот мастер тоже состоит. Занятость намеренно **не** скоупится по компании — один человек занят у всех работодателей |
| GET | `/api/bookings/slots?companyId&masterId&serviceId&date&manual` 🗓 `&extendedHours&serviceIds&excludeBookingId` | публично; **`companyId` обязателен** (цикл 1), проверяется тройка «услуга принадлежит компании» + «мастер работает в компании»; `manual=true` учитывается **только для персонала этой компании**. 🗓 Три новых параметра — см. разбор ниже |
| 🗓 GET | `/api/bookings/availability?companyId&masterId&serviceId\|serviceIds&from&to&manual` 📸 `&extendedHours` | публично (лимит `availability` 60/мин на IP); статус **каждого дня диапазона одним запросом** — для месячного календаря. 📸 Цикл 10 добавил `extendedHours` и два поля в ответе — `staffMode` и `days[].scheduleState`, см. ниже |
| 📸 GET | `/api/bookings/{id}/history` | **новый.** `[Authorize]`; **персонал компании этой записи** (`Master`/`CompanyOwner` в ней) **или SuperAdmin**. ⚠️ Право **шире**, чем право менять запись: историю видит любой сотрудник компании, не только назначенный мастер и владелец. Клиенту — 403, независимо от того, его это запись или нет |
| POST | `/api/bookings` | публично (гость) и авторизованно |
| GET | `/api/bookings/{id}` | владелец записи или персонал |
| GET | `/api/bookings/client?status=` | мои записи как клиента, с фильтром статуса |
| GET | `/api/bookings/master?date&to` | `Master,CompanyOwner` |
| PATCH | `/api/bookings/{id}/complete` \| `/mark-paid` \| `/noshow` | `Master,CompanyOwner,SuperAdmin` |
| PATCH | `/api/bookings/{id}/reschedule` | персонал (advisory lock + проверка конфликта) |
| PATCH | `/api/bookings/{id}/cancel` | клиент записи или персонал; принимает причину отмены, которую видит вторая сторона |

`GET /api/bookings/my` **удалён** в цикле 2 как мёртвый дубль `/api/bookings/client`.

Логика слотов вынесена в чистый `Services/SlotCalculator.cs` (без БД и EF), `SlotService` остался
тонкой обёрткой над запросами. Шаг сетки **по-прежнему жёстко 30 минут** (`SlotCalculator.StepMinutes`
— цикл 6 это не менял, см. §9.22), слот занят, если пересекается с бронью (статус ≠ Cancelled) или
перерывом. Проверка при самой записи (`IsSlotAllowed`) построена **поверх той же `Calculate`**, а не
отдельным предикатом — чтобы выдача слотов и их валидация не разошлись.

🗓 **US-66: `bool allowWithoutSchedule` заменён перечислением `ScheduleFallback`** — три значения
вместо двух состояний, чтобы забытый вызов не мог молча раскрыть сутки напролёт:

| Значение | Когда | Что делает |
|---|---|---|
| `None` | публичный/гостевой путь — без изменений | нет строки `WorkingHours` на дату → пустой список |
| `DefaultWindow` | `manual=true` от персонала этой компании | нет расписания на дату → окно **09:00–21:00** из `Booking:DefaultWorkWindow` |
| `WholeDay` | `manual=true` **и** `extendedHours=true` от персонала | **сутки 00:00–24:00, при этом расписание И перерывы игнорируются всегда** — в том числе на дату, где строка `WorkingHours` есть |

⚠️ Про `WholeDay` важно не ошибиться при чтении: это **не «фоллбек на случай отсутствия
расписания»**, а явный запрос персонала «покажи любое время». Поэтому окно расписания и перерывы
игнорируются и тогда, когда расписание на дату задано — иначе тумблер «показать другие часы» был бы
пустышкой на любом рабочем дне. Сервер такие записи персонала принимает, поэтому прятать это время
из сетки было бы враньём в другую сторону.

🗓 **US-65: `GET /api/bookings/availability`** (`Services/AvailabilityService.cs`) отдаёт
`{ from, to, totalDurationMinutes, stepMinutes, horizonDays, horizonLastDate, days[] }`, где у дня
одно из трёх серверных состояний — `Available` (плюс `lastFreeSlotStart`), `FullyBooked`, `DayOff`.
Четвёртое состояние («прошедший день») сервер **не присылает намеренно** — браузер знает свою
локальную дату лучше. Статус дня считает **та же `SlotCalculator.Calculate`**, что отвечает и на
`/slots`, поэтому календарь и сетка слотов разойтись не могут.

🗓 **US-65/Q5: горизонт записи стал настройкой компании** — `Companies.BookingHorizonDays`
(дефолт 90, `Services/BookingHorizon.cs` — чистая логика, ровно три точки вызова: сохранение
настройки, `/availability`, клиентский путь `POST /api/bookings`). **Персонала горизонт не
касается**: ручная запись и перенос ограничения не знают.

🗓 **Перенос записи: `excludeBookingId` у `GET /api/bookings/slots`.** Сетка для переноса не должна
блокироваться собственным интервалом переносимой записи, и должна продолжать работать, если услугу
с тех пор деактивировали, убрали из умений мастера или мастер уволился из компании. Поэтому на этом
пути длительность и состав берутся **из самой записи** (`BookingServices`/`Service`), а не
перерешиваются по текущему каталогу. Порядок проверок **намеренный и важен**: сначала
`CanManageBookingAsync` (нет прав → 403), и только потом сверка `companyId`/`masterId` с записью
(не совпало → 400). Обратный порядок превратил бы эндпоинт в оракул «принадлежит ли запись X
компании Y и мастеру Z» для любого, кто знает публичный id.

Гейты в `POST /api/bookings` (цикл 1 их существенно перебрал):
1. **компания должна существовать — для любого вызывающего**, а не только для гостя (раньше
   аутентифицированный запрос с несуществующим `CompanyId` падал 500 из-за нарушения FK);
2. «ручная запись персонала» = аутентифицированный вызывающий, **реально работающий в этой компании**,
   передавший `GuestName`. Все остальные — гостевой путь, даже если у них есть аккаунт (раньше одного
   `guestName` хватало, чтобы обойти четыре гейта);
3. `AllowSelfBooking` проверяется **для всех непереонала**, включая залогиненного клиента (раньше жил
   внутри гостевой ветки);
4. гостевой путь → капча (если `IsEnforced`), имя+телефон, телефон нормализуется;
5. тариф: `!AllowOnlineBooking && !isStaffManualBooking` → **402**;
6. предоплата: `PaymentStatus = Pending` только если `plan.AllowOnlinePayment && company.RequirePrepayment` и это не ручная запись;
7. проверка конфликта слота внутри транзакции с `pg_advisory_xact_lock` (`Services/AdvisoryLock.cs`) → 409;
8. снимаются `Price` и `CommissionPercent`;
9. 🗓 **горизонт записи** — для клиентского/гостевого пути дата дальше `BookingHorizonDays` → 400
   (персонал этой проверке не подлежит);
10. 🗓 **состав визита** — `serviceIds` (1..5, без повторов; если прислан и `serviceId`, он обязан
    совпадать с первым элементом) проверяется `Services/BookingServiceSelection.cs`, умение мастера
    по **каждой** услуге — `Services/MasterCapability.cs` (услуга, у которой вообще нет привязок
    к мастерам, по-прежнему считается доступной всем — прежний фоллбек сохранён).

🗓 **US-67: несколько услуг за один визит.** `serviceIds` принимают три эндпоинта —
`GET /api/bookings/slots`, `GET /api/bookings/availability`, `POST /api/bookings`;
`BookingDto` получил `services[]` и `totalDurationMinutes`. Инварианты, на которые можно опираться:
`Σ services[].price == price`, `Σ services[].durationMinutes == totalDurationMinutes`,
`services[0].serviceId == serviceId`, и `services[]` **никогда не пуст** (у записей, созданных до
цикла 6, там ровно одна строка — backfill миграции). Текст уведомления перечисляет услуги визита
через запятую (`NotificationScheduler`).

Фронт: 📸 **`components/booking/BookingModal.tsx` — единственная модалка записи в продукте**
(гость/клиент **и** персонал; выбор компании → услуг → мастера → даты → слота, капча). Отдельного
`ManualBookingModal.tsx` больше нет, он удалён в цикле 10 — подробности в §4.20 и §6.
`RescheduleModal.tsx`,
`pages/MyBookingsPage.tsx` (персонал; под записью раскрывается панель с историей клиента и заметками),
`pages/ClientBookingsPage.tsx` (клиент; маршрут `/my-visits` теперь доступен **всем**
аутентифицированным ролям, включая мастера и владельца).

🗓 **Что цикл 6 изменил на фронте в этом блоке:**
- **`components/booking/BookingCalendar.tsx`** — месячный календарь вместо списка ближайших дат.
  Четыре состояния дня: свободен / **«Занято»** (сервер сказал `FullyBooked` либо сегодня уже
  прошёл последний слот) / выходной / вне горизонта и прошедшие (некликабельны). Формулировку
  «Занято» выбрал заказчик, она вынесена в одну константу `DAY_FULL_LABEL`.
- **US-64: шаг выбора мастера пропускается**, если в компании ровно один активный специалист,
  оказывающий услуги.
- `ManualBookingModal.tsx` (📸 **удалён в цикле 10**, описано как история) — мультивыбор услуг и
  тумблер «показать другие часы» (`extendedHours`); обе способности переехали в `BookingModal`;
  `RescheduleModal.tsx` — читает слоты с сервера и передаёт `excludeBookingId`, а пустую сетку
  объясняет причиной, а не «попробуйте снова»; визит из нескольких услуг рисуется **одной строкой**
  в списках записей.

### 4.6 Тарифы и подписки — работает административно

`Services/SubscriptionResolver.cs` (батчевое разрешение планов без N+1), `AdminController` (CRUD планов и
назначение подписок), `DTOs/Companies/CompanyDto.cs` (флаги для UI).

`SubscriptionResolver.Resolve` — чистая статическая функция (`sub`, `nowUtc` → `EffectivePlan`),
покрыта юнит-тестами. `EffectivePlan` расширен полями `PhotoQuotaMb` и `PhotoRetention`.
**Деактивированный тариф (`SubscriptionPlanConfig.IsActive == false`) теперь роняет владельца во Free**
(цикл 1) — раньше «удалённый» план продолжал раздавать возможности уже подписанным.

`CompanyDto` отдаёт три уровня флагов, и это осознанно (см. комментарии в файле):
- собственные тумблеры владельца: `AllowSelfBooking`, `RequirePrepayment`, `ShowInPublicListing`;
- вычисленные «реально работает»: `OnlineBookingEnabled`, `PrepaymentEnabled`, `PublicListingEnabled`;
- «сырые» возможности тарифа: `PlanAllowsOnlineBooking`, `PlanAllowsOnlinePayment`, `PlanAllowsPublicListing`, `MaxEmployees`.

🗓 **Что цикл 6 изменил в подписках (US-63) — три вещи, и все три ломающие для того, кто звал
`PUT /api/admin/owners/{ownerUserId}/subscription` по-старому:**
1. **Дата окончания стала обязательной**: план задан, а `paidUntil` нет → **400** «Укажите дату
   окончания подписки», причём проверка идёт **до записи** — отказ не трогает ни подписку, ни
   журнал изменений. Старые строки без даты закрыты миграцией `BackfillSubscriptionPaidUntil`.
2. **Дата без времени суток трактуется как «включительно до конца этого дня»**
   (`23:59:59.999`), как её читает человек, а не как полночь в его начале. Явно присланное время
   суток сохраняется как есть.
3. **`isActive` отсутствует или `null` теперь означает `true`**, а не молчаливое `false`: раньше
   «просто продлить» роняло владельца во Free.

🗓 Плюс новый диагностический эндпоинт **`GET /api/admin/owners/{ownerUserId}/subscription`**
(`Services/SubscriptionDiagnostics.cs`) — отвечает на вопрос «тариф назначен, почему не работает»
одним запросом вместо перебора шести независимых точек отказа: общий статус
(`NoSubscription` / `Active` / `Expired` / `Deactivated` / `PlanRetired`) с русским текстом и, **по
каждой компании владельца**, точная причина, по которой онлайн-запись выключена
(`PlanDisallowsOnlineBooking` — упёрлись в тариф, 402 при записи; `SelfBookingDisabledByOwner` —
владелец сам выключил тумблер, 403). Для клиента симптом один и тот же, а чинится по-разному.
В админке это отдельная панель фактического состояния.

⭐ Цикл 3 добавил в `CompanyDto` поля `AverageRating` (`double?`) и `ReviewCount` — **агрегат считается
в БД**, а не подтягиванием отзывов на клиент; витрина компании показывает рейтинг без отдельного запроса.

Последняя группа добавлена коммитами `c062dd5` и `bc34db3` — чтобы UI гасил тумблер/кнопку заранее,
а не ловил 402 после заполнения формы. Фронт: `CompanyManagePage.tsx` (SettingsTab, MembersTab),
`CabinetPage.tsx` (скрывает вкладки «Отчёты»/«Рассылка» по `allowAnalytics`/`allowMailing`).

### 4.7 Отзывы — работает

`Controllers/ReviewsController.cs`: `POST /api/reviews` (только по завершённой записи, рейтинг 1..5,
один отзыв на бронь), `GET /api/reviews/can-review` (список ID записей, ждущих отзыва),
`GET /api/companies/{companyId}/reviews` (публично, класс `CompanyReviewsController` в том же файле;
⭐ **с цикла 3 отдаёт `PagedResult<ReviewDto>`, а не массив** — ломающее изменение).
Цикл 1 закрыл дыру: проверка авторства стала строгой (`booking.ClientId != userId` → 403) — раньше
условие `ClientId != null && …` полностью пропускало **гостевые** записи, и любой, кто узнал
`bookingId` (а он возвращается гостю при создании), мог оставить отзыв чужому бизнесу от своего имени.
Как следствие отзыв по гостевой записи теперь невозможен вовсе, и ветка с `GuestName` в
`ReviewerName` удалена как мёртвая.
Фронт: `components/review/ReviewModal.tsx`, отображение на `CompanyPage.tsx`.

### 4.8 База клиентов мастера и заметки — работает, переработано в цикле 2

`Controllers/MastersController.cs`: `GET /api/masters/clients?companyId&search&page&pageSize`
(⭐ цикл 3: **серверный поиск** — эвристика «похоже на телефон» та же, что в `AdminController.GetUsers`,
и **`PagedResult<MasterClientDto>` вместо массива**, ломающее изменение; фильтрация и нарезка страницы
делаются **в памяти**, см. §5.3.5), `POST /api/masters/clients/notes`,
`DELETE /api/masters/clients/notes/{id}`.
Доступ — только персонал компании (`CompanyMembership.IsStaffAsync`), участник с ролью `Client` больше
не проходит. Группировка отдельно по зарегистрированным клиентам и по `GuestPhone`.

Что изменилось:
- **правило «контакты скрыты через 24 часа после визита» удалено** (цикл 2): оно было
  полуреализовано (не пряталось обратно, не было способа раскрыть раньше) и ничего не защищало;
- **заметка стала объектом**: `ClientNoteDto { id, note, createdAt, authorId, authorName, bookingId?,
  bookingDate?, bookingServiceName?, canDelete, photos[] }` — раньше на фронт уезжал `string[]`;
- удалять заметку может **автор или владелец компании** (решение Q16);
- выборка заметок ограничена **50 на клиента** и делается оконной функцией `ROW_NUMBER()` в SQL
  (`FromSqlInterpolated`), а не подтягиванием всей истории компании в память;
- заметка опционально ссылается на визит (`bookingId`), и тогда в карточке видно, к какой услуге и
  дате она относится.

Фронт: `pages/MasterClientsPage.tsx`, вкладка «Клиенты» в `CabinetPage`, раскрывающаяся панель под
записью в `MyBookingsPage.tsx`, компоненты `components/clientNotes/{NoteCard, NotePhotoUploader,
PhotoGallery, PhotoViewerModal}.tsx`.

### 4.9 Отчёты и статистика — работает

- `GET /api/reports/masters?companyId&from&to` (`ReportsController`) — выручка, комиссия мастера,
  доля компании; гейт `plan.AllowAnalytics` → 402. Цикл 1 исправил основу расчёта: фильтр идёт
  **по дате визита** (`Booking.Date`), а комиссия берётся из **снимка на записи**
  (`Booking.CommissionPercent`), а не из текущей строки `CompanyMembers` — уход мастера или смена
  ставки больше не переписывают закрытый период. Фронт: `ReportsTab` внутри `CabinetPage.tsx`.
- `GET /api/companies/{id}/stats` (`CompaniesController`) — сводка по компании. Фронт: `pages/owner/DashboardTab.tsx`.
- `GET /api/admin/stats` — платформенная сводка.

🗓 **Как цикл 6 разложил визит из нескольких услуг по отчётам — сознательное упрощение, которое
надо знать перед тем, как читать цифры:** «топ услуг» (`popularServices`) считает **вхождения
услуг** — визит из трёх услуг даёт три засчитанные строки; а **выручка считается по визиту ровно
один раз** (из `Booking.Price`). То есть **раскладки выручки по услугам в продукте нет**: на вопрос
«сколько принесло окрашивание против стрижки» отчёты сейчас не отвечают (§9, долг цикла 6).

### 4.10 Админка — работает

`Controllers/AdminController.cs`, все методы `[Authorize(Roles = "SuperAdmin")]`:
`GET /api/admin/stats`, `GET /api/admin/users?search&page&pageSize` (поиск по телефону в любом
формате — строка запроса нормализуется; ⭐ цикл 3: `PagedResult<AdminUserDto>` **и починенный N+1** —
роли больше не догружаются по пользователю в цикле), `PUT /api/admin/users/{id}/roles`,
`GET /api/admin/companies?search&page&pageSize` (⭐ тоже `PagedResult<T>`), `PUT /api/admin/companies/{id}` (тело — `Name`, `IsActive`,
`AllowSelfBooking`; блокировка/разблокировка компании доступна из UI, US-04),
`PUT /api/admin/companies/{id}/owner`,
`PUT /api/admin/owners/{ownerUserId}/subscription` (🗓 дата окончания обязательна, см. §4.6),
🗓 **`GET /api/admin/owners/{ownerUserId}/subscription`** (диагностика тарифа, §4.6),
`GET /api/admin/owners/{ownerUserId}/subscription-history`,
`GET /api/admin/bookings` (лимит `Take(500)`), `GET|POST|PUT|DELETE /api/admin/plans[/{id}]`
(delete — мягкий, `IsActive = false`; **вернуть тариф в продажу теперь можно через `PUT`**, US-05),
`GET /api/admin/scheduled-tasks` (цикл 2, см. §4.13).
Из `AdminUserDto` убран `CommissionPercent` (комиссия стала per-company).
Фронт: `pages/AdminPage.tsx` (вкладки stats/companies/users/bookings) + `pages/admin/PlansTab.tsx`
(в редакторе тарифа появились квота на фото и срок хранения).

### 4.11 Виджет-встраивание — работает целиком

Маршрут `/embed/:slug` (`frontend/src/App.tsx`) рендерит `pages/EmbedPage.tsx` без навбара — список
услуг компании + `BookingModal`. В цикле 2 (US-03) в настройках компании
(`pages/owner/CompanyManagePage.tsx`) появился блок с готовым `<iframe …>`-сниппетом, ссылкой, кнопкой
«Скопировать» и предпросмотром; имя компании экранируется для атрибута `title`.

🗓 **Виджет сознательно остался на одной услуге за визит.** Мультивыбор услуг (US-67) в
`/embed/:slug` **не выведен** — это решение цикла 6, а не пропущенный экран: виджет живёт в чужом
iframe с неизвестной высотой, и множественный выбор там требует отдельного разговора о вёрстке.
Горизонт записи компании виджет при этом уважает — он приезжает в `CompanyDto`.

### 4.12 Фото к заметкам о клиентах — функция цикла 2

`Controllers/ClientNotePhotosController.cs` (195 строк), `Services/{ImageUploadService, ImageProcessor,
ImageSignature, FileStorage}.cs`.

| Метод | Путь | Доступ |
|---|---|---|
| POST | `/api/client-notes/{noteId}/photos` | персонал компании заметки; `[EnableRateLimiting("uploads")]`, `[RequestSizeLimit(5 МБ)]` |
| GET | `/api/client-notes/photos/{id}` | персонал **этой** компании; чужому — 404 (существование чужого фото не подтверждается); **SuperAdmin получает 403** — единственное место в продукте, где ему отказано |
| GET | `/api/client-notes/photos/{id}/thumb` | то же, миниатюра |
| DELETE | `/api/client-notes/photos/{id}` | автор заметки, загрузивший это фото, или владелец компании |

Конвейер загрузки (общий для всех **четырёх** точек загрузки изображений — фото заметки, аватар,
картинка услуги, логотип): наличие файла → лимит 5 МБ → **определение типа по сигнатуре байт**
(Content-Type и имя файла не используются нигде) → проверка свободного места на диске
(`Storage:MinFreeDiskMb`) → защита от «бомбы» (`MaxPixels = 50 млн`) → декод/поворот по EXIF/ресайз/
ре-энкод SkiaSharp по профилю (`ClientNotePhoto` 1600 px, `ClientNotePhotoThumb` 320 px, `Avatar` 512 px
с квадратным кропом, `ServiceImage` 1200 px, `CompanyLogo` 512 px) — метаданные, включая геолокацию,
не переносятся. Далее — специфика вызывающего: права, квота, запись.

Специфика фото заметки: ≤5 фото на заметку; идемпотентность по SHA-256 **обработанных** байт
(уникальный индекс `(ClientNoteId, ContentHash)` — повторный клик не тратит квоту дважды); квота
компании считается суммой `SizeBytes` под `pg_advisory_xact_lock("company-photo-quota:{companyId}")`,
файлы пишутся **внутри** удерживаемого лока; при превышении — 400 с текстом «сколько из скольки занято».
Порядок операций на удаление — «сначала строка БД, потом файл»; на замену публичного файла — «сначала
новый файл и коммит, потом удаление старого»: худший исход — осиротевший файл, который подметёт
фоновая уборка.

Отдача приватных фото: `ClientNotePhotoDto.Url` — это путь к **защищённому API**, а не значение для
`<img src>` (браузер не приложит `Authorization`). Фронт грузит их как blob через тот же axios
(`hooks/useAuthedImage.ts`, `components/ui/AuthedImage.tsx`), с `IntersectionObserver` для ленивой
загрузки и `staleTime: Infinity` в react-query.

⚖️ **Цикл 5 поставил перед загрузкой гейт согласия.** `POST` фото к заметке теперь **отвечает 400,
если у клиента нет действующего согласия на фотофиксацию**, и это действие дополнительно помечено
`[RequiresOwnerTerms]`. На фронте согласие собирается там, **где стоит мастер** — в момент загрузки
(`hooks/usePhotoUploadWithConsent.tsx`, `components/clientNotes/{PhotoConsentBadge, ClientConsentModal}`),
а не отдельным административным экраном постфактум. Срок хранения фото при этом перестал иметь
вариант «вечно» (§4.13).

### 4.13 Периодические фоновые задачи — первый фоновый процесс в продукте (цикл 2)

`Services/Scheduling/`: `IScheduledTask` (контракт: `Name`, `DefaultPeriod`, `ExecuteAsync` →
`ScheduledTaskOutcome{Scanned, Affected, BytesFreed, Summary}`), `ScheduledTaskRunner`
(единственный `BackgroundService`), `ScheduledTaskOptions` (читает `ScheduledTasks:{Name}:{Enabled,
PeriodMinutes, MaxRunMinutes}`), `ScheduledTaskSchedule` (чистые `IsDue`/`IsOverdue`).

Как устроено: раннер **не знает ни одной задачи по имени** — перечисляет всё, что зарегистрировано как
`IScheduledTask`. Добавление второй задачи = новый класс + одна строка регистрации в `Program.cs`.
«Пора ли» читается из БД (`ScheduledTaskState`), а не из памяти — рестарт не приводит к раннему
повторному запуску. Параллельный запуск на двух инстансах исключён неблокирующим
`pg_try_advisory_xact_lock("scheduled-task:{Name}")`; лок держится отдельным соединением, чтобы
промежуточные коммиты задачи его не отпускали. Есть бюджет времени (`MaxRunMinutes`, дефолт 10):
исчерпание — **штатный** исход, уже закоммиченное сохраняется, остальное доедет следующим запуском.
Упавшая задача помечается неуспешной и повторяется **по обычному расписанию**, без backoff.

⚖️ **Задач теперь четыре** (раннер при этом не менялся ни строкой ни в цикле 4, ни в цикле 5):

1. `photo-retention-cleanup` (период — сутки): удаляет фото, пережившие срок хранения своего тарифа,
   батчами по 200; затем подметает осиротевшие файлы на диске старше 24 часов. ⚖️ Исключения
   «хранить вечно» у неё больше нет — `PhotoRetention.Forever` удалён, потолок тарифа 12 месяцев.
2. 🆕 **`notification-dispatch`** (период — **1 минута**, `MaxRunMinutes: 2`) — отправщик очереди,
   `Services/Scheduling/Tasks/NotificationDispatchTask.cs` (~464 строки, «главный архитектурный вопрос
   цикла»). Один проход: выборка батча по частичному индексу → классификация по гейтам/таймингам
   **одним `SaveChanges`** → отправка, сгруппированная по каналам, с ограниченным параллелизмом
   **между** каналами и строго последовательно **внутри** канала, с паузой 5–15 с между отправками.
   Собственный бюджет = `min(MaxRunTime − 10 с, Dispatch:BudgetSeconds)` — задача останавливает себя
   раньше, чем сработает жёсткая отмена раннера. Backoff повторов — `[1, 5, 15, 60, 180]` минут,
   потолок `MaxAttempts: 5`. **Пауза принципиально не может держать транзакцию** — это и было главным
   ограничением дизайна.
3. 🆕 **`channel-health`** (период — **15 минут**, `MaxRunMinutes: 10`) —
   `Services/Scheduling/Tasks/ChannelHealthTask.cs` (~405 строк), четыре работы в одном проходе, каждая
   одним батч-запросом: опрос состояния каналов (`Connecting`/`Connected`/`Disconnected`), детекция
   простоя и удаление простаивающего экземпляра, таймаут неавторизованного экземпляра, повтор удаления
   «осиротевших» экземпляров у провайдера (`OrphanedInstanceId`).
4. ⚖️ **`data-retention`** (период — **сутки**, `MaxRunMinutes: 10`, `BatchSize: 500`,
   **`DryRun: true` по умолчанию**) — `Services/Scheduling/Tasks/DataRetentionTask.cs`. Своей логики
   не содержит: перебирает зарегистрированные `IRetentionRule` и выполняет каждое по разу, подробности
   и полный список из тринадцати правил — §4.18.

`GET /api/admin/scheduled-tasks` (SuperAdmin) отдаёт по каждой задаче: `enabled`, `periodMinutes`,
`lastStartedAt`, `lastFinishedAt`, `lastDurationMs`, `lastSucceeded`, `lastSummary`, `lastError`,
`isOverdue` (не финишировала дольше двух своих периодов). Зависимости резолвятся `[FromServices]` в
самом действии, а не в primary-конструкторе контроллера, — чтобы остальные админские вызовы за это не платили.
В окружении `Testing` планировщик **выключен** (`appsettings.Testing.json`), функциональные тесты
дёргают задачу напрямую. 🆕 Исключение — специализированная фабрика `NotificationDispatchTestFactory`,
которая поднимает **свой** хост с реально тикающим раннером (§7.2).

### 4.14 Правовой контур: документы, согласие, 451 ⚖️ — построен в цикле 3, **переписан в цикле 5**

`Controllers/LegalController.cs`, `Controllers/ClientConsentsController.cs`,
`Services/Legal/{LegalDocumentProvider, LegalConsentFilter, LegalOptions, LegalSnapshot,
ConsentLedger, ConsentSubject, HealthNoteProtector, RequiresOwnerTermsAttribute}.cs`,
`Core/Enums/{LegalDocumentType, LegalGate, LegalTextKey, ConsentAct, ConsentPurpose, ConsentSource}.cs`,
сущность **`ConsentRecord`** (вместо `UserConsent`).

⚠️ **Это не дополнение цикла 3, а замена его механизма.** Через контур проходит каждый пользователь
при каждом входе, поэтому расхождения здесь видны сразу и всем.

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/legal/documents` | публично; ⚖️ метаданные **пяти документов И шести текстов интерфейса** двумя списками (`documents[]`, `uiTexts[]`), без HTML. Для `PdnConsent` здесь же едет список `purposes` — **форма согласия строится из ответа, а не из захардкоженного массива на фронте**, поэтому смена набора целей юристом не требует релиза фронта |
| GET | `/api/legal/documents/{type}` | публично; метаданные **и** HTML одного документа; неизвестный тип → **404, а не 500**; `Cache-Control: public, max-age=300` |
| GET | ⚖️ `/api/legal/texts/{key}` | публично, **новое**; один текст интерфейса. Версионируется как документ, но **claim'а под него нет, ветки 451 нет, в `consent-status` он не появляется** |
| GET | `/api/legal/consent-status` | авторизованные; ⚖️ отвечает **только про документы с `gate != None`** — PdnConsent и ChannelRiskNotice ничего не блокируют и в ответе «что меня блокирует» им не место. Считается **из claim'ов токена и снимка в памяти, без запроса в БД** |
| POST | `/api/legal/accept` | авторизованные; ⚖️ тело — **список** принимаемых документов (любое подмножество `{Privacy, TermsClient, TermsOwner}`), а не две фиксированные строки: шестой блокирующий документ станет добавлением, а не ломающим изменением. Версии **сверяются с текущим снимком** (409, если оператор успел заменить текст ещё раз) |
| GET/POST | ⚖️ `/api/profile/consents` | авторизованные, **новое**; просмотр и выдача согласия `PdnConsent` по целям. Через этот вызов принимается **только** `PdnConsent` — всё остальное 400 |
| POST | ⚖️ `/api/profile/consents/revoke` | авторизованные, **новое**; отзыв. Ставит `RevokedAtUtc`, строку не удаляет |
| GET | ⚖️ `/api/profile/consents/revoke-preview` | авторизованные, **новое**; что именно перестанет работать, **до** того как человек нажмёт отзыв |

⚖️ **Салонный контур согласий** — `ClientConsentsController`,
`[Route("api/companies/{companyId:guid}/clients/{clientKey}")]`, весь класс под `[Authorize]`.
`{clientKey}` — это либо `userId` зарегистрированного клиента, либо `phone:79991234567` для гостя;
формат разбирается **в одном месте** (`Services/ClientKey.cs`).

| Метод | Путь | Что делает |
|---|---|---|
| GET/POST | `…/photo-consent` | согласие клиента на фотофиксацию работы |
| GET | `…/health-note` | чтение противопоказаний; **суперадмину — `Forbid`** |
| PUT | `…/health-note` | запись; `[RequiresOwnerTerms]`; **суперадмину — `Forbid`** |
| DELETE | `…/health-note` | удаление; **суперадмину — `Forbid`** |
| POST | `…/health-consent` | согласие на обработку сведений о здоровье |

Как это устроено:

- **Документы — файлы, а не строки в БД.** `App_Data/legal/` содержит манифест `legal.json` и по
  HTML-файлу на документ. ⚖️ Манифест цикла 5 состоит из **двух списков**: `documents[]` (`type`,
  `version`, `effectiveFrom`, `isDraft`, `changeKind`, **`gate`**, `title`, `file`, для PdnConsent —
  `purposes[]`) и `uiTexts[]` (`key`, `version`, `isDraft`, `file`). `LegalDocumentProvider` держит
  снимок в памяти и перечитывает по mtime не чаще раза в `Legal:ReloadSeconds` (30 с). На проде каталог
  **bind-mount'ится с хоста** — замена текста это **эксплуатационная операция, а не релиз**.
- ⚖️ **Разделение прошло не по линии «документ / не документ», а по линии «блокирует вход / не
  блокирует».** Шесть текстов интерфейса (уведомление при записи, предупреждение о рекламе в шаблоне,
  текст страницы отписки, форма согласия на фото, форма согласия на данные о здоровье, подтверждение
  полномочий) версионируются и хешируются наравне с документами, но **не являются версионируемыми
  документами в смысле гейта**: у них нет члена `LegalDocumentType`, нет claim'а и нет ветки 451.
  Побочная выгода записана прямо в коде: **добавление седьмого текста — правка манифеста, а не миграция**.
- ⚖️ **`changeKind` решает, блокировать ли, `gate` — кого и где.** `Material` + `Global` — 451 на всех
  защищённых вызовах (Privacy, TermsClient); `Material` + `OwnerScope` — 451 **только на
  перечисленных действиях владельца** (TermsOwner); `gate: None` — не блокирует нигде.
  `Editorial` — только баннер. Отсутствующее/нераспознанное значение `gate` в манифесте трактуется как
  **`Global`**: опечатка оператора обязана **закрывать**, а не открывать.
- ⚖️ **Почему владельца нельзя было блокировать глобально.** Владелец салона почти всегда ещё и клиент
  собственной платформы; сплошной 451 по `TermsOwner` отрезал бы его и от своей истории записей, и от
  профиля. Поэтому `OwnerScope` реализован **отдельным атрибутом действия** `[RequiresOwnerTerms]`, а
  не глобальным фильтром. Он висит на **двенадцати действиях** (по коду: 4 в `CompaniesController`,
  3 в `NotificationChannelsController`, 2 в `CompanyNotificationsController`, по одному в
  `MastersController`, `ClientNotePhotosController`, `ClientConsentsController`).
  ⚠️ **Отсутствующий claim `lco` трактуется как «ещё не применимо», а не как расхождение** — у того,
  кто создаёт первую компанию, этого claim'а нет вовсе; **первый** акцепт гейтится телом запроса
  `POST /api/companies`, а атрибут ловит только того, кто принял однажды и с тех пор устарел.
- **Гейт для глобальных документов — по-прежнему глобальный MVC-фильтр** `LegalConsentFilter` с
  allow-list'ом (правовые эндпоинты, принятие согласия, выгрузка данных, страница отписки). Анонимные
  запросы фильтр не трогает.
- ⚖️ **`ConsentLedger` — единственный читатель и единственный писатель журнала.** Он **намеренно не
  стоит на горячем пути**: на каждом запросе работают claim'ы против снимка в памяти, а в БД ходят
  только четыре «холодных» места — экраны управления согласиями, постановка уведомления в очередь,
  загрузка фото, чтение/запись противопоказаний.
- **Снимок согласия на записи** — как и раньше, заполняет сервер; ⚖️ добавился `BookingNoticeVersion`
  (пишется **на каждую** запись) и поля подтверждения полномочий при записи за другого человека.
- ⚖️ **Тексты по-прежнему черновые** (`isDraft: true` у всех одиннадцати файлов), fail-fast на это
  намеренно нет. Но ⚖️ **загрузчик манифеста теперь не примет документ с `isDraft: false`, пока в нём
  остался хоть один незаполненный плейсхолдер `{{…}}`** — недописанный текст в прод не уедет.

Фронт: страницы `/privacy`, `/terms`, ⚖️ `/terms-owner`, `/pdn-consent`, `/channel-risk` (все —
`pages/LegalDocumentPage.tsx`) и редирект `/offer-channel` → `/terms-owner#offer-channel`;
⚖️ `/profile/consents` (`ConsentsPage.tsx`) — управление согласиями и их история;
`components/legal/ConsentGate.tsx` (блокирующий экран на глобальном `Material`; из него **достижима
выгрузка данных**), ⚖️ `components/legal/OwnerTermsGateModal.tsx` + `store/ownerGateStore.ts`
(владельческий гейт — модальное окно, а не полноэкранная блокировка),
`components/legal/LegalUpdateBanner.tsx`, ⚖️ `hooks/useLegalText.ts` (тексты интерфейса),
`utils/legalSections.ts`, `api/legal.ts`, `api/consents.ts`, `api/clientConsents.ts`,
`utils/legalError.ts`. ⚖️ `frontend/public/robots.txt` явно разрешает индексацию пяти правовых страниц.

### 4.15 Права субъекта данных: выгрузка и удаление аккаунта ⭐ — новое в цикле 3

`Controllers/ProfileController.cs`.

- **`GET /api/profile/export`** (лимит `data-export` — 3 раза в сутки) отдаёт JSON с
  `Content-Disposition: attachment`: профиль, история согласий, членства в компаниях, **записи**
  (и как клиента, и **гостевые по каноническому телефону** — визиты, сделанные до регистрации),
  отзывы, **метаданные** заметок и фото о себе (компания, дата, размер/количество).
  В файл **намеренно не входят** тексты заметок сотрудников и содержимое фотографий — они признаны
  результатом работы салона; в самом JSON лежит поле с объяснением этого пользователю.
  ⚖️ **Цикл 5 расширил выгрузку**: история согласий берётся из журнала (`ConsentLedger.HistoryAsync`)
  и включает **отозванные и перекрытые** строки — выгрузка это правовой артефакт, а не снимок
  текущего состояния; появились перечень операторов-салонов с указанием, что у кого хранится,
  отправленные уведомления с полем **`bodyAvailable`** (`false`, если текст уже затёрт правилом
  уничтожения — честнее, чем молча отдать пустоту), статус отписки и **расшифрованные
  противопоказания о себе** (расшифровка явным вызовом `HealthNoteProtector`, не через прозрачный
  конвертер). Две из этих правок — находки ревью: салонные согласия (фото/здоровье) сначала не
  попадали в историю, а заметка о здоровье гостя молча выпадала из выгрузки.
- **`POST /api/profile/delete-account`** (POST, а не DELETE — нужно тело с текущим паролем).
  Два гейта: текущий пароль и **«за вами числится компания» → 409** (владелец обязан сначала передать
  компанию). Дальше, одной транзакцией: согласия удаляются; заметки и фото **о** пользователе
  удаляются (файлы — после коммита); записи **анонимизируются, а не удаляются** (`ClientId = null`,
  гостевые поля и заметки очищаются, ставится `ClientDeleted = true`) — выручка и комиссия салона
  должны уцелеть; отзывы деперсонализируются (`ReviewerName = "Удалённый пользователь"`); членства
  снимаются, роли пересчитываются `IdentityRoleSync` под теми же advisory-локами; сам аккаунт
  становится **надгробием** — поля затираются, `PasswordHash` очищается, ставится вечный lockout,
  **телефон освобождается** для повторной регистрации, проставляется `AppUser.DeletedAtUtc`.
- **Почему надгробие, а не `DELETE` строки:** четыре FK на `Restrict` (`Booking.Master`,
  `Review.Master`, `Company.Owner`, `MailLog.SentBy`) физическое удаление просто не пропустят, а
  пятый — `ClientNote.Master` на `Cascade` — молча снёс бы заметки этого человека **о других
  клиентах**, то есть данные компании. Причина зафиксирована в `ARCHITECTURE.md` §19.2 и
  комментарием на самом поле `DeletedAtUtc`.

Фронт: `pages/DeleteAccountPage.tsx` (маршрут `/profile/delete`), `hooks/useExportData.ts`
(скачивание blob'ом через тот же axios + `a[download]`), кнопки в `ProfilePage.tsx` и в `ConsentGate`.
Пользовательское описание — `docs/personal-data.md`.

### 4.16 Эксплуатационная обвязка: health, логи, лимиты, роли ⭐ — новое в цикле 3

- **Health-эндпоинты** (`Services/Health/DatabaseReadyHealthCheck.cs`): `GET /api/health/live` —
  анонимный, **не касается БД** вовсе (`Predicate = _ => false`), и `GET /api/health/ready` —
  анонимный, проверяет соединение и применённые миграции. У обоих **свой ResponseWriter на два поля**
  (`status`, `failed`): стандартный ответ фреймворка вложил бы текст исключения и слил бы кусок
  строки подключения на публичный эндпоинт. Rate limiting к ним намеренно **не применён** — мониторинг
  не должен уметь залочить сам себя. `docker-compose.prod.yml` смотрит на `live`, скрипты деплоя
  ждут `ready`.
- **Логирование:** Serilog заменяет хост-логгер целиком, `CompactJsonFormatter` в stdout и
  `logs/app-.json`; телефоны маскируются (`Services/LogMasking.cs`, `PhoneMaskingEnricher`), в том
  числе в строке запроса (`?search=<телефон>`); синк в GlitchTip по протоколу Sentry
  (`Sentry.Serilog`) включается только при непустом `Sentry:Dsn`.
- **Rate limiting** (§6): `auth-login`, `auth-register`, `booking-create` (разные лимиты для
  авторизованных и анонимов), `data-export` — плюс существовавший `uploads`. IP берётся после
  `UseForwardedHeaders` со списком доверенных сетей, отсутствие которого в Production роняет старт.
- **`Services/IdentityRoleSync.cs`** — единственный пересчёт ролей `Master`/`CompanyOwner` из строк
  `CompanyMember`; вызывается из пяти операций (§6). Закрывает дыру цикла 2: `RemoveMember` не снимал
  Identity-роль, и человек, удалённый из единственной компании, продолжал проходить
  `[Authorize(Roles = …)]`. Историю почистила миграция данных `ResyncIdentityRoles`.
- **`Services/DeploymentSafetyChecks.cs`** — fail-fast прод-конфига, вынесенный из `Program.cs` в
  чистые статические методы **ради тестируемости** (⚖️ теперь **84 юнит-теста**), см. §8.

### 4.17 🆕 Уведомления клиенту в WhatsApp — функция цикла 4, код есть, наружу не выпущена

Самый крупный блок цикла. **Код реализован, протестирован и смёржен, но выключен тарифно** — см.
предупреждение в преамбуле и §9.

**Контроллеры и эндпоинты** (выписаны из атрибутов):

`Controllers/NotificationChannelsController.cs` (~652 строки, `[Route("api/notification-channels")]`,
весь класс под `[Authorize]`):

| Метод | Путь | Что делает |
|---|---|---|
| GET | `/api/notification-channels` | список каналов владельца (`ChannelListDto`) |
| GET | `/api/notification-channels/offer` | что вообще предлагается: цена из `PlatformSetting`, тариф, текст риска и его версия. **Если цена не задана или тариф не разрешает — опция не предлагается** |
| POST | `/api/notification-channels` | заявка на канал (`RequestedAtUtc`), оплату проставляет суперадмин |
| GET | `/api/notification-channels/{id}` | карточка канала |
| POST | `/api/notification-channels/{id}/accept-risk` | принятие текста о рисках; версия сверяется с `NotificationRiskText.CurrentVersion`, устаревшая → 400 |
| POST | `/api/notification-channels/{id}/connect` | создание экземпляра у провайдера, переход в `Connecting` |
| GET | `/api/notification-channels/{id}/qr` | QR для привязки номера (ответ кешируется в `IMemoryCache`); при успешной авторизации фиксирует `Authorized` и заполняет номер из `wid` |
| POST | `/api/notification-channels/{id}/test-message` | тестовое сообщение, кулдаун `TestMessageCooldownMinutes` |
| DELETE | `/api/notification-channels/{id}` | отключение канала владельцем |
| POST | `/api/notification-channels/{id}/replace` | замена номера после бана в том же оплаченном периоде (старый канал → `Replaced`) |
| POST | `/api/notification-channels/{id}/companies` | назначить компанию на канал |
| DELETE | `/api/notification-channels/{id}/companies/{companyId}` | снять компанию с канала |

`Controllers/CompanyNotificationsController.cs` (`[Route("api/companies/{companyId:guid}")]`, `[Authorize]`):
`GET|PUT /notification-settings`, `GET /notification-templates`, `PUT /notification-templates/{type}`,
`POST /notification-templates/{type}/preview`, `GET /notifications` (журнал доставки,
`PagedResult<NotificationLogItemDto>`), `GET /notifications/summary?days=`.

`Controllers/NotificationsController.cs` (`[Route("api/notifications")]`, класс **без** `[Authorize]`):
`GET|PUT /preferences` (`[Authorize]` — переключатель у клиента в профиле),
`GET|POST /unsubscribe/{token}` (**анонимно**, токен подписан `UnsubscribeKey`),
`POST /provider-webhook/{token}` (**анонимно**, политика лимита `notifications-webhook`, парсер
резолвится через `[FromServices]`).

`Controllers/CitiesController.cs`: `GET /api/cities?search=&take=` — **публично**, справочник городов.

`Controllers/AdminController.cs` (всё под `SuperAdmin`): 🆕 `GET /api/admin/notification-channels`
(`PagedResult<AdminChannelDto>`), `GET /api/admin/notification-channels/summary`,
`POST /api/admin/notification-channels/{id}/payment` (проставить оплаченный период + сумму + комментарий),
`POST .../{id}/suspend`, `POST .../{id}/resume`, `GET|PUT /api/admin/platform-settings`
(цена канала и число дней простоя).

**Шифрование чужих секретов — впервые в проекте** (`Services/Notifications/SecretProtector.cs`,
`ChannelKeyFingerprint.cs`): AES-GCM напрямую, мастер-ключ из `.env`
(`NOTIFICATIONS_ENCRYPTION_KEY`, 32 байта base64), формат `v1.<keyId>.<base64(nonce12‖tag16‖ct)>`,
AAD — id канала (шифротекст, перенесённый в чужую строку, не расшифруется), **новый случайный nonce
на каждый вызов**. Отпечаток ключа пишется в файл рядом с `.env`
(`App_Data/state/.notifications-key-fingerprint`, записываемый bind-mount `./state`), и при
расхождении **приложение не стартует**. Невозможность расшифровать секрет — отдельная причина
`ChannelStateReason.SecretUnavailable` (это инцидент платформы, а не действие владельца), экземпляр
при этом выводится из эксплуатации, и Connect открывается заново.

**Постановка в очередь** — `Services/NotificationScheduler.cs`, вызывается **напрямую из
`BookingsController`** на создание / отмену / перенос записи. Решение «слать или нет» — чистый
`NotificationGate`; когда слать — чистый `NotificationTiming` (с джиттером `ReminderJitterMinutes`).

**Три рубежа против попадания токена в лог:** заглушено логирование HTTP-клиента `green-api` на
уровне категорий; `GreenApiUrls.SafeLabel` для того, что адаптер логирует сам; маскирование
токена в access-логе nginx (§8).

Фронт: раздел «Уведомления» в кабинете владельца (`pages/owner/NotificationsSection.tsx` +
вкладки `NotificationSettingsTab` / `NotificationTemplatesTab` / `NotificationLogTab`), привязка по
QR (`components/notifications/QrModal.tsx`), назначение компаний (`AssignCompanyDialog`), принятие
рисков (`RiskAcceptanceModal`), плашка о разрыве (`ChannelBreachBanner`), админский экран
(`pages/admin/NotificationsAdminTab.tsx`), выбор города (`components/ui/CityCombobox.tsx`),
публичная страница отписки `/u/:token` (`pages/UnsubscribePage.tsx` — **добавлена в
`CONSENT_GATE_BYPASS_PATHS`**, иначе заблокированный согласием пользователь не смог бы отписаться),
переключатель уведомлений в `ProfilePage.tsx`.

⚖️ **Что цикл 5 изменил в этой функции** (кода уведомлений он касался вынужденно, потому что именно
там лежали правовые дыры):
- **Заявка на канал требует ИНН и форму лица** и принятия оферты — `POST /api/notification-channels`
  без них теперь 400 (ломающее изменение). ИНН проверяется **только формальной контрольной суммой**
  (`Services/InnValidator.cs`), против ЕГРЮЛ/ЕГРИП не сверяется — и это записано как осознанное.
- **`GreenApi:InstanceCreationEnabled: false` по умолчанию** и **обязательный `ServerCountry`,
  если создание экземпляров включено**: «пусть провайдер решит сам, в какой стране сервер» перестало
  быть допустимым значением по умолчанию — fail-fast на старте.
- **Новая ветка гейта постановки в очередь** — `ProviderDeliveryConsentMode` (§4.18).
- **Сохранение шаблона требует акцепта** текста об ответственности за рекламу (§4.18).

### 4.18 ⚖️ Сроки хранения, обращения субъектов, спецкатегории — новое в цикле 5

**Уничтожение по срокам хранения.** `Services/Retention/` — `IRetentionRule`, `RetentionPeriods`
(конфигурация), `RetentionPlan`, `RetentionRuleRunner` и **тринадцать правил** в `Rules/`:
`NotificationBodyRedactionRule`, `NotificationMetadataDeletionRule`, `TemplateHistoryRule`,
`ConsentRecordRule`, `InactiveAccountRule`, `BookingPersonalizationRule`, `ClientNoteRule`,
`ClientNotePhotoRule`, `ClientHealthNoteRule`, `ChannelStateEventRule`, `PaymentLogRule`,
`MailLogRule`, `AppLogAgeRule`, 📸 **`BookingEventRule`** (четырнадцатое, цикл 10 — журнал изменений
записи). ⚠️ **У четырнадцатого срок не задан:** `Retention:BookingEventDays: 0`, и правило читает
ноль как «срок не настроен» — оно **ничего не удаляет** и честно пишет это в сводку, вместо того
чтобы молча ничего не делать или удалять по выдуманному рубежу. Ждёт ответа юриста (в журнале ФИО
сотрудников), см. §9 D1. Старт приложения оно при этом **не роняет** — в отличие от fail-fast'ов на
`TemplateHistoryDays`/`ConsentRecordDays`, которые охраняют **известные** юридические минимумы. Их исполняет четвёртая фоновая задача
`Services/Scheduling/Tasks/DataRetentionTask.cs` (период — сутки).

Как это устроено:

- **Задача не содержит правил вовсе** — она перебирает то, что зарегистрировано в `Program.cs`, и
  выполняет каждое по разу. Регистрация **поимённая, не по рефлексии**: список в `Program.cs` и есть
  список того, что работает.
- ⚠️ **Сухой прогон — значение по умолчанию.** `DryRun: true` в `appsettings.json`; чтобы задача
  действительно что-то удалила, оператор обязан явно выставить `false`. Обратного (по умолчанию
  удаляем) в проекте нет нигде.
- **Время — из той же абстракции `INotificationClock`**, что у диспетчера уведомлений; каждое правило
  получает «сейчас» **данными**, ни одно не читает `DateTime.UtcNow` само. Именно это позволяет
  функциональному тесту сдвинуть фейковые часы и увидеть результат сразу.
- **Два вида действия**: часть правил **затирает поля на месте** (текст уведомления и данные
  получателя, анонимизация записи и неактивного аккаунта — тем же механизмом, что уже использовало
  удаление аккаунта), часть **удаляет строки**. `AppLogAgeRule` **ничего не удаляет вообще** — только
  считает старые файлы и сообщает; ротация логов это дело эксплуатации, а правило здесь работает
  растяжкой.
- **Два срока проверяются fail-fast'ом на старте** (`TemplateHistoryDays` ≥ 365,
  `ConsentRecordDays` ≥ 1095): «не меньше трёх лет» не должно зависеть от того, кто последним правил
  `appsettings.Production.json`.
- Журнал прогона — **одна строка на правило, только числа и даты**: ни телефонов, ни текста
  сообщений, ни идентификаторов субъектов.

🔴 **Правила уничтожения для `NotificationOptOut` не существует.** Это надо читать буквально: оно не
выключено, не помечено «бессрочно» и не стоит за флагом — **файла и регистрации нет**. Включить
настройкой нельзя, только новым файлом правила и новой строкой регистрации. Смысл ровно один: удаление
строки отписки означало бы возобновление рассылки тому, кто от неё отказался. Факт зафиксирован
комментариями и в `Program.cs`, и в самой задаче, и отдельным тестом (§7).

**Обращения субъектов данных.** `Controllers/SubjectRequestsController.cs` —
`POST /api/subject-requests`, **анонимно**, политика лимита `subject-request` (3/час на IP).
Ответ **одинаков независимо от того, знает ли система указанный телефон**. Срок ответа считается
в рабочих днях (`Services/WorkingDays.cs`) из `SubjectRequests:ResponseWorkingDays` и **сохраняется
на строке** — позднейшая правка конфига не переписывает обещание, данное конкретному человеку.
Номер обращения генерирует `Services/SubjectRequestReference.cs`.
Разбор — суперадмином: `GET /api/admin/subject-requests`,
`POST /api/admin/subject-requests/{id}/status`. Отдельно — `GET /api/admin/retention/policy`
(что и с какими сроками уничтожается). Фронт: публичная страница `/data-request`
(`pages/SubjectRequestPage.tsx`) и вкладка `pages/admin/SubjectRequestsTab.tsx`.

**Спецкатегории (сведения о здоровье).** Отдельная таблица `ClientHealthNote`, шифрование
`HealthNoteProtector` поверх `SecretProtector` (**тот же мастер-ключ `Notifications:EncryptionKey` —
вторую криптографию намеренно не заводили: один ключ, одна процедура ротации**), AAD привязан к
«компания + субъект». Расшифровка — **только явными вызовами**, EF-конвертер отвергнут осознанно:
прозрачный конвертер расшифровывал бы поле в любой будущей проекции, которую напишут не думая об этом.
Любая неудача расшифровки (не тот ключ, порча, чужой AAD) даёт `null`, а не 500. **Суперадмину
чтение/запись/удаление запрещены явным `Forbid`** — не пустым значением. Фронт:
`components/clientNotes/HealthNoteCard.tsx`, `ClientConsentModal.tsx`, `PhotoConsentBadge.tsx`,
`hooks/usePhotoUploadWithConsent.tsx`.

**Реклама в шаблонах уведомлений.** `Services/Notifications/TemplateAdHeuristics.cs` — словарь
маркеров, которые указывают на рекламный характер текста; при сохранении шаблона владелец **обязан
подтвердить ответственность**, и подтверждение пишется в историю шаблона вместе с тем, **что именно
сервер обнаружил** (`AdMarkersHit`) — доказательством служит не только факт нажатия кнопки. Фронт:
`pages/owner/TemplateAcknowledgementModal.tsx`, `utils/templateMarkers.ts`.

**Спорный вопрос права вынесен в конфигурацию.** `ProviderDeliveryConsentMode`
(`Notifications:ProviderDeliveryConsent`, по умолчанию `AccountsOnly`) управляет **только тем,
проверяет ли `NotificationGate` согласие на передачу данных привлекаемому лицу при постановке
сообщения в очередь**. Само согласие предъявляется и записывается **при любом значении флага**.
`AccountsOnly` — гость никогда не блокируется (его никто не спрашивал, основание договорное),
владелец аккаунта без действующего согласия блокируется; `Strict` блокирует и гостя;
`Off` не проверяет никогда. Нераспознанное значение **роняет старт**.

### 4.19 💳 Биллинг-аккаунты, тарифы и опции — функция цикла 7, в `develop` есть, на стенде нет

⚠️ **Читать вместе с §8 и §9 B1:** код влит в `develop`, но **на стенде его нет** — деплой упал на
миграциях и откачен на предыдущий релиз. Всё описанное ниже проверено тестами и локально, но
**вживую не работало ни разу**.

Код: `Services/Billing/**` (14 файлов, см. §2), контроллеры `BillingController`,
`AdminBillingController`, `CompanyTransferController`, `PricingController`, DTO — `DTOs/Billing/**`.

**Что считается.** Итог = цена тарифа + Σ (цена опции × количество). Тариф даёт цену, возможности и
базовые лимиты; опция — переключатель (`OptionKind`) или количество. Матрица `PlanOptionRule`
задаёт для каждой пары «тариф × опция» доступность (`OptionAvailability`) и включённое количество;
**строки нет — значит `Unavailable`**. Опции **меняют лимиты** сотрудников и компаний (`447226f`),
лимит сотрудников — **суммарный по аккаунту** плюс `GrandfatheredEmployeeBonus`. Отказ по лимиту
(`SeatLimitReached`) разложен на «тариф / докуплено / бонус» (`1a30205`).

#### Эндпоинты владельца — `BillingController` (`api/billing`, `[Authorize]`)

| Метод | Путь | Что делает |
|---|---|---|
| GET | `/api/billing/subscription` | текущая подписка аккаунта: тариф, опции, итог, статус, лимиты и занятое |
| POST | `/api/billing/subscription/request` | подать/переподать заявку на смену тарифа и набора опций (US-65, US-70) |
| DELETE | `/api/billing/subscription/request` | отозвать свою заявку |

Владелец **сам ничего не оплачивает и не активирует** — заявка уходит администратору. Повторная
подача по уже принадлежащей опции разрешена (`f3760c2`).

#### Эндпоинты администратора — `AdminBillingController` (`api/admin`, `[Authorize(Roles = "SuperAdmin")]`)

| Метод | Путь | Что делает |
|---|---|---|
| GET/POST | `/api/admin/options` | каталог опций: список и создание |
| PUT/DELETE | `/api/admin/options/{id}` | правка и удаление опции |
| GET | `/api/admin/option-capabilities` | справочник ключей возможностей (`companies`, `employees`) |
| GET | `/api/admin/billing-accounts` | список аккаунтов: тариф, статус, итог **с учётом опций**, реальное число каналов (`734b9e8`) |
| GET | `/api/admin/billing-accounts/{accountId}` | карточка аккаунта |
| PUT | `/api/admin/billing-accounts/{accountId}/subscription` | назначить тариф и опции, задать `PaidUntil` |
| GET | `/api/admin/billing-accounts/{accountId}/subscription-history` | история изменений подписки |
| GET | `/api/admin/subscription-requests` | очередь заявок владельцев |
| POST | `/api/admin/subscription-requests/{id}/reject` | отклонить заявку с причиной |

⚠️ **Очередь заявок отвечает пустым списком на любой статус, кроме ожидающих** — истории одобренных
и отклонённых не существует, см. §9 B2.

#### Перенос компании — `CompanyTransferController` (`api/admin/companies`, `SuperAdmin`)

| Метод | Путь | Что делает |
|---|---|---|
| GET | `/{companyId}/transfer/preview` | предпросмотр: можно ли перенести и что изменится |
| POST | `/{companyId}/transfer` | атомарный перенос компании в другой биллинг-аккаунт (US-77) |
| GET | `/{companyId}/owner-history` | история смен ответственного (`CompanyOwnerChangeLogs`) |

Перенос **атомарен и отказывает при нехватке лимита** у принимающего аккаунта
(`CompanyTransferCalculator`, `CompanyTransferService`). Отвязка каналов сбрасывается в БД **до**
обновления `BillingAccountId` (`5073f34`) — иначе составные FK из `AddCoTenancyConstraints` ловят
промежуточное состояние. Берутся два advisory-lock'а, **аккаунт раньше компании** (`6f11eda`).

**Смена ответственного за компанию (`CompanyOwnerWriter`) денег не трогает** и **не отвязывает
номер уведомлений** — поведение цикла 4 (§47.4) намеренно отменено (`0a6fab6`), два теста цикла 4
переписаны под разворот (`0c56cb2`).

#### Публичная витрина цен — `PricingController`

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/pricing` | **анонимно**, но за рубильником |
| GET | `/api/admin/pricing/preview` | `SuperAdmin`, предпросмотр в обход кэша |

⚠️ **Рубильник `pricing.public-enabled` (платформенная настройка в БД, не `appsettings`) по
умолчанию выключен**, и `GET /api/pricing` отвечает **404 с пустым телом**, пока суперадмин его не
включит. Это **требование `SPEC_CYCLE7_PRICING.md` §7 — вычитка юристом**, а не недоделка (§9 B8).
Ключ — `PricingCatalogCache.PublicEnabledSettingKey`. Эндпоинт внесён в allow-list
`LegalConsentFilter` (`3945256`), иначе анонимная витрина требовала бы согласия.

#### Отозванные эндпоинты

`PUT /api/admin/owners/{ownerUserId}/subscription` и одноимённый legacy-маршрут отвечают
**`410 Gone`** с телом `text/plain`, указывающим замену (`AdminController.LegacyEndpointGone`,
строки 295–300, 952–956, 1046–1051). Тело запроса не принимается и не применяется. Причина:
подписка больше не адресуется по владельцу-человеку.

#### Экраны фронтенда

| Экран | Файл | Маршрут |
|---|---|---|
| «Ваша подписка» (владелец) | `frontend/src/pages/BillingPage.tsx` | `/billing` |
| Публичная витрина цен | `frontend/src/pages/PricingPage.tsx` | `/pricing` (в списке анонимно доступных, `App.tsx:56`) |
| Биллинг-аккаунты (админка) | `frontend/src/pages/admin/BillingAccountsAdminTab.tsx` + `billingAccountsHelpers.ts` | вкладка `/admin` |
| Тизер и ссылка в навигации | `components/pricing/PricingTeaser.tsx`, `PricingNavLink.tsx` | главная / шапка |

Плюс `components/pricing/PlanCard.tsx`, `OptionRow.tsx`; API-клиенты `api/billing.ts`,
`api/adminBilling.ts`, `api/pricing.ts`; утилиты `utils/pricingFormat.ts` (русские падежи и суммы),
`utils/billingError.ts`, `utils/adminBillingError.ts`; типы `types/pricing.ts` и сгенерированный
`types/api-cycle7.generated.ts` (§1).

Редактор тарифа в админке умеет задавать врезки (`highlights`), матрицу опций и флаг системного
бесплатного тарифа (`1c8229f`, `c7ae43a`); правка тарифа больше не затирает эти поля (`9429f0c`,
`5abc75a`).

---

### 4.20 📸 Свобода ручной записи, журнал изменений записи и фото салона — функция цикла 10

Три блока одного цикла. Документы: `SPEC_CYCLE10_MASTER_BOOKING_HISTORY_PHOTO.md` (ред. 3),
`ARCHITECTURE_CYCLE10.md` §100–§117, `API_CONTRACT_CYCLE10.md` §120–§133,
`contracts/cycle10/openapi.yaml`.

**Блок A. Один экран записи вместо двух — работает.**

- `ManualBookingModal` **удалён**; `BookingModal` — единственный компонент записи, открывается из
  **трёх** точек: публичная карточка компании (`CompanyPage.tsx:252`), виджет `/embed/:slug`
  (`EmbedPage.tsx:125`, по-прежнему одна услуга за визит), «Мои записи → Записать клиента»
  (`MyBookingsPage.tsx:480`). Выбор даты везде — помесячный `BookingCalendar`; плоского списка
  «сегодня + 14 дней» в продукте больше нет.
- **Режим персонала включает сервер, а не фронт.** `GET /api/bookings/availability` возвращает
  `staffMode: true/false` — это **единственный** признак, по которому интерфейс рисует режим
  персонала. Ни роль в токене, ни переданный `manual`, ни точка открытия модалки таким признаком не
  являются; `BookingCalendar` сообщает флаг наверх через `onStaffModeChange`, до ответа сервера
  модалка ведёт себя как обычный клиентский путь.
- **`extendedHours` у `/availability`** (был только у `/slots`) — закрывает дефект, из-за которого
  месячный календарь персонала показывал ложное «занято», если день свободен только вне окна
  09:00–21:00. Таблица «просьба → что применит сервер» у обоих эндпоинтов теперь **одна и та же**
  (`ScheduleFallback.None` / `DefaultWindow` / `WholeDay`, §4.5), поэтому календарь и сетка времени
  разойтись не могут.
- **`days[].scheduleState`** — `Working` / `DayOff` / `NoSchedule`, и **только при `staffMode: true`**
  (анонимному посетителю различать «выходной» и «график не заполнен» незачем — решение по
  приватности расписания). Для персонала статус графика — **информация, а не запрет**: день сохраняет
  подпись «выходной»/«нет графика», но **нажимается**; подпись попадает и в `aria-label` кнопки дня.
- **Горизонт записи** (`BookingHorizonDays`) персоналу по-прежнему не применяется — теперь и в
  календаре: он листается вперёд помесячно, верхняя граница интерфейса — 365 дней.
- **Регрессия, которую слияние не допустило** (это и есть критерий приёмки блока): для гостя,
  клиента и сотрудника **чужой** компании объединённая модалка ведёт себя ровно как прежний
  `BookingModal` — `DayOff` не нажимается, горизонт применяется, капча и согласия гостя на месте.

**Блок B. Журнал изменений записи — работает, только для персонала.**

- `GET /api/bookings/{id}/history` → `{ bookingId, precedesJournal, events[] }`; у события —
  `kind` (`Created`/`Rescheduled`/`Cancelled`/`Completed`/`NoShow`/`PaymentMarked`), `occurredAt`,
  готовый человеческий `title`, `actor` (`kind` + снимок имени + роль в компании + `label`),
  `reschedule` (откуда/куда) и `cancellationReason`.
- **`precedesJournal`** отличает «у записи ещё ничего не происходило» от «запись создана до
  внедрения журнала, истории и не будет» — backfill'а истории сознательно нет.
- `BookingDto.historyEventCount` — число событий, заполняется **только персоналу этой компании и
  SuperAdmin**, у клиента поле `null`. В списке записей персонала считается одним группирующим
  запросом на страницу, без N+1.
- Фронт: `components/booking/BookingHistoryPanel.tsx`, раскрывается под записью в
  `pages/MyBookingsPage.tsx`. Даты переноса форматируются по-человечески, а не сырым ISO (`66bb65d`).
- ⚠️ **Клиенту история не показывается ни в каком виде** — это решение заказчика (П8), а не
  недоделка. Состав карточки записи в «Моих визитах» не менялся.

**Блок C. Фотогалерея салона — работает.**

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/companies/{id}/photos` | **анонимно**; `CompanyPhotoDto[]` в порядке показа, 404 если компании нет |
| POST | `/api/companies/{id}/photos` | владелец компании или SuperAdmin; `multipart/form-data`, поле **`file`**, ≤5 МБ, политика `uploads`. **201** + фото в конец списка; повторная загрузка того же файла даёт **200** и уже существующее фото (дедуп по хешу), лимит не расходуется |
| DELETE | `/api/companies/{id}/photos/{photoId}` | владелец или SuperAdmin; после удаления позиции уплотняются, обложкой становится следующее фото. Файл с диска удаляется **после** коммита |
| PUT | `/api/companies/{id}/photos/order` | владелец или SuperAdmin; тело — **полная перестановка** id'шников (все ровно по разу), первый = обложка. Частичный список, чужой или повторённый id → 400 |

- **До 10 фото на компанию**, первое (`position = 0`) — обложка. Конвейер тот же, что у логотипа и
  фото заметок: тип по сигнатуре байт, ре-энкод (профили `CompanyPhoto` 1600 px и `CompanyPhotoThumb`
  480 px), метаданные и геолокация не сохраняются. Класс хранения — **публичный**.
- `CompanyDto` получил `coverPhotoUrl`/`coverThumbnailUrl` (на списке компаний и на публичной
  карточке) и `photos[]` — **только** у `GET /api/companies/{slug}`, чтобы публичной странице не
  требовался дополнительный запрос. `photos: null` означает «этот эндпоинт список не отдаёт», `[]` —
  «фотографий нет».
- Фронт: `pages/owner/CompanyPhotosSection.tsx` (управление в настройках компании — drop-zone,
  удаление, стрелки порядка), `components/company/CompanyPhotoGallery.tsx` (публичный показ с
  лайтбоксом и ленивой загрузкой полноразмерного изображения) на `pages/CompanyPage.tsx` вместо
  прежней серой заглушки «Фото: компания».
- ⚠️ **Согласия на публикацию изображений людей продукт не собирает** — решение заказчика (П7):
  экрана и чекбокса в потоке загрузки нет, `ConsentRecord` на фото салона не пишется. Открытый
  вопрос к юристу — §9 D2.

---

## 5. Что реализовано частично, заглушки и несогласованности

Явных маркеров `TODO`/`FIXME`/`HACK` в коде **нет ни одного** (⚖️ перепроверено grep'ом заново на
`071fc11` по всем четырём проектам и `frontend/src`). Всё ниже выявлено чтением кода.

### 5.0 ⚖️ Ломающие изменения цикла 5 — то, что сломается у любого, кто звал API по-старому

Цикл 5 менял работающее, поэтому список коротких «до → после» важнее обычного:

| Что | Было | Стало |
|---|---|---|
| **Регистрация** | `POST /api/auth/register` с `"acceptedLegal": true` | объект `"legal": { "privacyAcknowledgedVersion", "termsAcceptedVersion" }` — версии, которые клиент **утверждает, что видел**; сервер сверяет их со своим снимком. `PdnConsent` через этот вызов принять **нельзя by design** — это отдельный, неблокирующий вызов |
| **Тип документа** | `"Terms"` | `"TermsClient"` (числовое значение `1` сохранено — ломается только строка в JSON и маршруты, где тип передаётся строкой) |
| **Создание компании** | `POST /api/companies` без правовых полей | требует акцепта соглашения владельца в теле запроса; дальнейшие действия владельца гейтятся `[RequiresOwnerTerms]` |
| **Заявка на канал** | `POST /api/notification-channels` без реквизитов | требует ИНН, форму лица и принятие оферты |
| **Загрузка фото к заметке** | загружалась всегда | **400 без согласия клиента** на фотофиксацию |
| **Сохранение шаблона** | сохранялось всегда | требует акцепта текста об ответственности за рекламу |
| **`PhotoRetention.Forever`** | был | **удалён**; миграция переписывает существующие строки в `TwelveMonths` |
| **Таблица `UserConsents`** | была | **дропнута** миграцией; вместо неё журнал `ConsentRecords` |

Именно последствия первой строки этой таблицы не довели до `deploy/ci/smoke.sh` внутри цикла — см.
урок в §9.

### 5.0-bis 💳 Ломающие изменения цикла 7 — что сломается у звавших API по-старому

1. **`PUT /api/admin/owners/{ownerUserId}/subscription` → `410 Gone`.** Не «не найдено» и не
   «запрещено» — отозван. Замена: `PUT /api/admin/billing-accounts/{accountId}/subscription`.
   Тело запроса при обращении к старому маршруту не принимается и не применяется (§4.19).
2. **Подписка адресуется аккаунтом, а не владельцем.** Всё, что раньше читало подписку по
   `OwnerUserId`, переведено на `BillingAccountId` (`fee4999`, `a65aae2`, `de37630`). Внешний код,
   строивший запросы вокруг владельца-человека, ломается по смыслу, а не по синтаксису.
3. **`maxEmployees` больше не лимит компании, а лимит аккаунта** — суммарный по всем её компаниям.
   Клиент, показывавший его как «сколько можно в этот салон», покажет неверное (`ac5c6c2`).
4. **Контракт тарифа в админке изменил форму** (`AdminPlanDto`/`AdminPlanInput`): появились массив
   `highlights` и матрица опций, флаг `isSystemFree` отделён (`c7ae43a`, `9429f0c`).
   `POST /api/admin/plans` теперь отвечает **`201 Created`**, а не `200` (`93e48c1`).

### 5.0-ter 📸 Что цикл 10 сломал бы тому, кто звал API или читал экраны по-старому

Цикл сознательно добавочный (`API_CONTRACT_CYCLE10.md` §120.1): все новые параметры запроса
необязательны, все новые поля ответов — с безопасными значениями по умолчанию. Ломающих изменений
формы ответов **нет**. Но два изменения заметны снаружи:

1. **Тексты ошибок загрузки изображений стали русскими — во всём общем конвейере.** Затронуты **все
   четыре** эндпоинта загрузки (аватар, логотип компании, картинка услуги, фото заметок) плюс новые
   фото салона: `File is required` → `Нужно выбрать файл для загрузки.`, `Image is too large — the
   limit is 5 MB` → `Слишком большой файл — максимум 5 МБ.`, `Unsupported image type — use JPEG, PNG
   or WEBP` → `Можно загрузить JPEG, PNG или WEBP.`, `File is not a valid image` → `Файл повреждён
   или это не изображение.`, `Image dimensions are too large…` → `Слишком большое изображение —
   попробуйте файл меньшего размера.`, `Server storage is full…` → `На сервере закончилось место.
   Попробуйте позже.` Любой внешний потребитель, разбиравший **английские** подстроки, сломается.
   Фронтовые мапперы (`utils/uploadError.ts`, `utils/companyManageError.ts`) переведены на новые
   подстроки в том же цикле (`279654f`, `18e21d1`), `API_DOCUMENTATION.md` обновлён.
   ⚠️ Сохранено намеренно: сообщение про размеры изображения делит с байтовым общий префикс
   «Слишком больш…», чтобы фронтовый разбор по подстроке продолжал попадать в нужную ветку.
2. **`ManualBookingModal` удалён из фронтенда.** Любая ссылка на этот файл в чужой ветке или в
   документе устарела; см. §4.20 и §6. Внутреннее изменение, HTTP-контракта не касается.

### 5.1 Настоящие заглушки

1. **Рассылка не отправляет писем.** `Controllers/MailingController.cs` — `POST /api/companies/{id}/mail`
   собирает список email'ов клиентов, пишет `MailLog` и возвращает
   `{ recipientCount, message = "Рассылка поставлена в очередь" }`; `GET` того же маршрута отдаёт
   журнал. **Очереди нет, отправки нет.** Отдельно: аккаунты идентифицируются телефоном, email
   опционален, поэтому список получателей на практике может быть почти пустым. Фронт
   (`pages/owner/MailingTab.tsx`) показывает это как рабочую функцию — **решением Q9 текст оставлен
   как есть**, это сознательный выбор, а не упущение.

2. **Предоплата не проводится.** `RequirePrepayment` / `PaymentStatus.Pending` — организационный флаг.
   Платёж подтверждается вручную (`PATCH /api/bookings/{id}/mark-paid`). Интеграции нет.

3. 🆕 **Уведомления клиенту реализованы, но выключены; уведомлений персоналу нет вовсе.**
   - Клиентские уведомления в WhatsApp (§4.17) **написаны и покрыты тестами**, но
     `AllowNotificationChannel = false` у всех тарифов и цена опции не задана — после выката опция
     **никому не предлагается**. Провайдер по умолчанию `logging` — сетевых вызовов нет.
     💳 **Цикл 7 это состояние не изменил, а перенёс в новую модель:** опция WhatsApp-уведомлений
     **заведена в каталоге** `SubscriptionOption` (миграция `SeedBillingCatalog`), но
     `PricePerMonth` не задана и `IsPublic = false`, `Notifications:Provider` остаётся `logging`.
     То есть US-74 **по-прежнему невыпущена**, и «опция никому не предлагается» — **ожидаемое
     состояние, а не дефект** (§9 B9). Оплата каналов теперь идёт за номер через оплаченное
     количество опции на аккаунте (§3), но включать нечего.
   - **US-34 «уведомления персоналу» не реализована** — отложена осознанно, была первой в порядке
     урезания цикла. Следы в коде есть и они **мёртвые**: члены `NotificationType.StaffBookingCreated`
     / `StaffBookingCancelled` существуют, для них есть тексты (`NotificationTexts`) и дефолтные
     шаблоны (`DefaultTemplates`), но **ни одна строка кода их в очередь не ставит** (проверено
     grep'ом). Эндпоинт `PUT /api/companies/{id}/members/{memberId}/notifications` из
     `API_CONTRACT_CYCLE4.md` §35 **не существует**.
   - Email/SMS по-прежнему нет. Письмо владельцу о разрыве канала **вырезано решением заказчика**,
     единственный канал оповещения владельца — плашка в кабинете.
   - `SubscriptionPlanConfig.NotifyDaysBefore` сохраняется, редактируется в
     `pages/admin/PlansTab.tsx` и подписан «Уведомление за N дн. до деактивации» — но **никем не читается**
     в бизнес-логике (единственное использование в бэкенде — присваивание в `AdminController.UpdatePlan`).

4. **Самостоятельной покупки тарифа нет.** Подписку может выставить только SuperAdmin через
   `PUT /api/admin/owners/{ownerUserId}/subscription`. Экрана «оплатить тариф» на фронте нет —
   `ProfilePage.tsx` только показывает текущий план.

5. **Клиент не видит своих фото.** Фотофиксация работает только внутрь салона: у клиента нет ни
   эндпоинта, ни экрана. Осознанное решение Q5, а не пробел.

6. ⚖️ **ЗАКРЫТО циклом 5: согласие клиента на фотосъёмку появилось.** Прежняя формулировка («ни
   чекбокса, ни дисклеймера, ни хранения факта») больше не верна: есть форма согласия
   (`POST …/photo-consent`), текст интерфейса `PhotoConsent` в манифесте, запись в журнал согласий,
   бейдж в интерфейсе мастера и **отказ загрузить фото без согласия (400)**. Решение Q6 цикла 2
   снято. ⚠️ Остаётся сверить README и `docs/faq.md`, которые всё ещё пишут, что согласия нет — эти
   два файла правит product-analyst параллельно, здесь они не описываются.

7. ⚖️ **Правовые документы — по-прежнему черновик, и черновиков стало одиннадцать.** Все записи
   `legal.json` помечены `"isDraft": true`; Privacy и TermsClient остались на `2026-09-08-draft`,
   три новых документа и шесть текстов интерфейса — на `2026-09-21-draft`. Fail-fast на `isDraft`
   в Production **намеренно отсутствует** (решение заказчика). Что **изменилось**: загрузчик манифеста
   теперь **не примет `isDraft: false`, пока в тексте остался хоть один плейсхолдер `{{…}}`**.
   Исходники юриста лежат отдельно — `legal-drafts/`, двенадцать HTML плюс манифест, **пятнадцать
   видов незаполненных плейсхолдеров** (`{{ИНН_ОПЕРАТОРА}}`, `{{ОГРН_ОПЕРАТОРА}}`,
   `{{НОМЕР_УВЕДОМЛЕНИЯ_РКН}}`, `{{ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ}}` и т.д.). Их не может заполнить команда:
   четыре значения из ЕГРЮЛ, два появятся после уведомления РКН, семь — решения заказчика.
   `legal-drafts/legal.manifest.proposed.json` — **предлагаемый** манифест, **не подключён**.

8. ⭐ **`Booking.ClientDeleted` не доведён до интерфейса.** Бэкенд проставляет флаг, он приезжает в
   DTO и объявлен в TS-типах — и там же заканчивается (см. §5.2).

9. ⚖️ **Текст о рисках подключения WhatsApp существует теперь ДВАЖДЫ, и это несогласованность.**
   Цикл 4 оставил `Services/NotificationRiskText.cs` — константу `CurrentVersion = "2026-09-18-draft"`
   с «рыбой»; именно её всё ещё сверяет `NotificationChannelsController` (`offer` и `accept-risk`).
   Цикл 5 завёл **полноценный документ** `ChannelRiskNotice` в манифесте (`channel-risk-notice.html`,
   версия `2026-09-21-draft`, `gate: None`) и публичную страницу `/channel-risk`. То есть в продукте
   сейчас два независимых носителя одного и того же текста с **разными версиями**, и принятие риска
   каналом сверяется со старым. Механика приёма при этом настоящая в обоих случаях.

10. 🆕 **`NotificationChannel.ContactEmail` — зарезервированное, сознательно неиспользуемое поле.**
    Сбор email при оплате канала и письмо о разрыве вырезаны решением заказчика; колонка оставлена в
    схеме, чтобы возврат функции не требовал миграции. Ни в одном DTO не отдаётся.

**Закрыто в цикле 2** (эти пункты из прошлой редакции больше не актуальны): загрузка аватара
(`POST /api/profile/avatar`) и картинки услуги (`POST /api/services/{id}/image`) реализованы —
`AppUser.AvatarUrl` и `Service.ImageUrl` теперь заполняются, а не висят пустыми.

### 5.2 Мёртвый код

Почти весь мёртвый код прошлой редакции удалён в цикле 2 (US-22):

| Что было | Что стало |
|---|---|
| `ServiceBooking/` (весь Blazor-проект, включая `Services/TimeSlot.cs` и свой `Dockerfile`) | **удалён**, из `.sln` тоже |
| `frontend/src/pages/DashboardPage.tsx` (192 стр.) | **удалён**; редирект `/dashboard` → `/cabinet` в `App.tsx` оставлен |
| `frontend/src/pages/owner/OwnerPage.tsx` (187 стр.) | **удалён**; редирект `/owner` → `/cabinet` оставлен |
| `GET /api/bookings/my` | **удалён** как дубль `/api/bookings/client` |
| `AppUser.CommissionPercent`, `ProfileDto.CommissionPercent`, `AdminUserDto.CommissionPercent` | **удалены** (комиссия живёт на `CompanyMember`) |
| расхождение `BookingDto` и TS-типов по `price`/`companySlug` | **закрыто**: поля добавлены в DTO, ветки UI ожили (решение Q12) |

Что осталось:

| Файл | Состояние |
|---|---|
| `ServiceBooking.API/appsettings.Production.json.example` + `.env.production.example` | оба описывают один и тот же прод — два разных способа конфигурации (файл vs env), актуален второй (`docker-compose.prod.yml`) |
| `frontend/design_handoff_site_redesign/` | 10 HTML-макетов редизайна, в сборку не идут |
| `components/auth/` | пустой каталог (не убран и в цикле 3) |
| ⭐ `Booking.ClientDeleted` во фронтенде | **фактически мёртвый флаг**: единственное упоминание во всём `frontend/src` — объявление поля в `types/index.ts:103` (проверено grep'ом). Ни одна страница его не показывает |

Комментарий в `tailwind.config.js`: legacy-шкала `primary`/`accent` намеренно оставлена перекрашенной
в новую палитру, чтобы не мигрировать вручную «ещё не перестилизованные» компоненты — то есть часть
разметки формально ещё на старых токенах.

### 5.3 Несогласованности бэкенда и фронтенда

Из десяти пунктов позапрошлой редакции восемь закрыл цикл 1/2; цикл 3 добавил два новых. Что есть сейчас:

1. **`BookingStatus.Pending` фактически недостижим.** Это дефолт сущности, но
   `BookingsController.Create` всегда ставит `Confirmed`. Ни один эндпоинт не выставляет `Pending`.
   Статус присутствует в enum, в TS-типах и в фильтрах UI. **Решение Q10: оставлено намеренно** —
   сценарий «запись ждёт подтверждения салона» проектируется отдельной историей позже.

2. **Пароль автосозданного мастера никуда не отправляется.** Генерируется детерминированно из
   канонического телефона и остаётся только в БД в виде хэша; передача пароля мастеру — ручная
   договорённость владельца. Схема слабая, но осознанная и задокументированная.

3. **`MailingController.CanManageCompany` — единственная копия проверки прав, не перешедшая на
   `CompanyMembership`.** Инлайн-`AnyAsync` по `CompanyMembers` с ролью `CompanyOwner`; логика та же,
   но код продублирован.

4. **Типы фронта по-прежнему копируются вручную** (`frontend/src/types/index.ts`), генерации из
   OpenAPI нет. Именно так и возник разрыв `price`/`companySlug`, который цикл 2 закрыл, и так же
   «повис» `clientDeleted` (п. выше). 🆕 Цикл 4 добавил в этот файл **+177 строк** ручных копий
   (каналы, состояния, шаблоны, журнал, города). ⚖️ Цикл 5 добавил ещё **+190** (пять типов
   документов, тексты интерфейса, цели согласий, обращения субъектов, заметка о здоровье, ИНН) —
   поверхность ручного копирования продолжает расти линейно с каждым циклом.
   🗓 Цикл 6 добавил ещё +20 строк (состав визита, статусы дня календаря, горизонт, диагностика
   тарифа) — и это **первый цикл, у которого есть машиночитаемая схема того же контракта**
   (`openapi-cycle6.yaml`, §10.3). Генерации типов из неё всё равно нет: схема используется для
   проверки (`npx openapi-typescript … && npx tsc --noEmit`), а `src/types/index.ts` по-прежнему
   пишется руками.

5. ⭐ **Пагинация `GET /api/masters/clients` — единственная из четырёх, которая считается в памяти.**
   Контроллер материализует весь список клиентов компании, применяет `search` и режет `Skip/Take`
   там же. Признано приемлемым ревьюером и **прокомментировано в коде**; три остальные выборки
   (`admin/users`, `admin/companies`, публичные отзывы) пагинируются в SQL.

6. ⭐ **`docs/schedule.md` — единственный файл пользовательской документации, не тронутый циклом 3**
   (последняя правка — 1 сентября). Расписание цикл 3 не менял, так что расхождения не видно, но
   стилистически он отстал от остальных семи файлов.

Закрыто (для истории, чтобы не искать заново): фильтр ролей в `GET /api/companies/{id}/masters`;
права мастера на CRUD услуг; `SubscriptionResolver` теперь проверяет `PlanConfig.IsActive`;
`GET /api/workinghours` проверяет принадлежность; 500 на несуществующем `CompanyId` в
`POST /api/bookings`; дубль `using` в `BookingsController`; `launchSettings.json`;
расхождение `BookingDto` и TS-типов.

### 5.4 Документация, которая может быть устаревшей

🗓 **Что отстало от кода после цикла 6** (перепроверено на `aac6231`):
- **`API_DOCUMENTATION.md`** — не знает ни одного эндпоинта и параметра цикла 6 (§10.3).
- **`CHANGELOG.md`** — записи про цикл 6 нет вовсе (её вносит product-analyst).
- **`README.md`** и **все ролевые гайды в `docs/`** — цикл их не трогал: описание сценария записи
  там соответствует состоянию до календаря, мультивыбора услуг и маски телефона.
- **`TEST_CATALOG.md`**, наоборот, обновлён вместе с кодом (§10.4).

- 🆕 **Расхождение README/CHANGELOG с фактом развёртывания, бывшее «самым крупным» в прошлой
  редакции, ЗАКРЫТО** коммитом `d4137dd` (в начале этого диапазона): README теперь пишет, где сервис
  работает и почему это стенд, а в CHANGELOG появился верхний раздел **«Не выпущено»** — то, что
  принято командой, но ещё не выкачено. Содержимое цикла 4 в оба файла вносится **параллельно
  product-analyst'ом**, этим документом не описывается.
- 🆕 **`SPEC.md` в корне — это теперь спека ЦИКЛА 9** (MAX, Web Push мастеру, города на главной, три
  починки). Спека цикла 8 архивирована как **`SPEC_CYCLE8_TEST_ISOLATION.md`** (первым же действием
  цикла 9, тем же приёмом, каким цикл 8 в своё время восстановил спеку цикла 5) — ссылки вида
  «`SPEC.md`» в документах цикла 8 (`ARCHITECTURE_CYCLE8.md`, `ARCHITECTURE_CYCLE8_PHASE2.md`) теперь
  означают эту архивную копию, а не корневой файл. Спека цикла 5 восстановлена рядом как
  **`SPEC_CYCLE5_LEGAL.md`** (первым же действием цикла 8, потому что на неё
  ссылаются `CURRENT_STATE.md` и `LEGAL_REVIEW.md`). Документы цикла 8 — `ARCHITECTURE_CYCLE8.md`
  (§61–§81, фаза 1), `ARCHITECTURE_CYCLE8_PHASE2.md` (§89–§99, фаза 2; ⚠️ внутри файла **§98.5
  физически стоит ПОСЛЕ §99** — порядок разделов в оглавлении и в тексте расходится),
  `API_CONTRACT_CYCLE8.md` (§82–§88) и `API_CONTRACT_CYCLE8_PHASE2.md`. В корне теперь **четыре
  поколения** документов цикла одновременно (3, 4, 5, 8); нумерации разделов 21–40 / 41–60 / 61–99
  сквозные и не пересекаются, но ссылка «§26» без указания файла по-прежнему неоднозначна.
- 🔬 **`TEST_CATALOG.md` обновлён под цикл 8 точечно** (+20/−17 строк): преамбула и команда запуска
  больше не обещают заранее поднятую PostgreSQL и базу `servicebooking_test`. Остальной текст
  каталога — описания кейсов — цикл 8 не трогал, и это корректно: набор кейсов не менялся.
- 🔬 **`ARCHITECTURE_CYCLE8_PHASE2.md` §99.5 (приёмочные грепы) содержит греп, который больше не
  проходит буквально.** Грep № P1 ожидает, что `grep -rn "DisableTestParallelization"
  ServiceBooking.Tests/AssemblyInfo.cs` вернёт пусто; там теперь лежит **закомментированная** строка
  отката параллелизма (сознательное решение §98.1). Проверять надо не «пусто», а «нет
  раскомментированной строки».
- ⚖️ **`ARCHITECTURE.md` и `API_CONTRACT.md` в корне — это документы ЦИКЛА 3.** Цикл 4 положил рядом
  `ARCHITECTURE_CYCLE4.md` (разделы **21–40**) и `API_CONTRACT_CYCLE4.md` (**19–37**), цикл 5 — тем
  же приёмом `ARCHITECTURE_CYCLE5.md` (**41–60**) и `API_CONTRACT_CYCLE5.md` (**38–53**). Нумерация
  сквозная и не пересекается, но **ссылка вида «§26» без указания файла неоднозначна**: смотреть надо
  на номер (≥41 / ≥38 — цикл 5, ≥21 / ≥19 — цикл 4). `SPEC.md` **перезаписан** циклом 5, спека
  цикла 4 сохранена как `SPEC_CYCLE4_NOTIFICATIONS.md`, цикла 3 — `SPEC_CYCLE3_PRODUCTION.md`.
  ⚠️ В корне теперь **три поколения** документов цикла одновременно.
- ⚖️ **Комментарий на `OutboundNotification.ContentRedactedAtUtc` устарел внутри собственного цикла.**
  Он утверждает, что «ничего эту колонку пока не ставит, она нужна только для `bodyAvailable`
  в выгрузке» — на момент его написания это было правдой, но правила уничтожения дописали позже в том
  же цикле, и `NotificationBodyRedactionRule` колонку заполняет.
- 🆕 **`API_CONTRACT_CYCLE4.md` §35 описывает эндпоинт, которого нет** —
  `PUT /api/companies/{id}/members/{memberId}/notifications` (US-34). В самом контракте он помечен
  «режется первым», и он действительно был срезан; раздел из документа не убран.
- `API_DOCUMENTATION.md` (🆕 ~247 КБ) **обновлён в цикле 3** коммитом `90a69b1`: в нём есть `/api/legal/*`,
  `/api/profile/export`, `/api/profile/delete-account`, `/api/health/*`, `acceptedLegal` в регистрации
  и новый §3.11 про конверт `PagedResult<T>`. Отдельно в нём есть §7 «Известные ограничения» — раздел,
  который стоит перечитывать вместе с §9 этого документа.
- ⚠️ ⭐ **`TEST_CATALOG.md` содержит устаревшее замечание о самом себе.** Его последний раздел
  («Документация, не обновлённая вместе с кодом») утверждает, что `API_DOCUMENTATION.md` в цикле 3 не
  тронут ни одной строкой. На момент написания это было правдой — документ обновили **позже**, тем
  самым `90a69b1`, а замечание не убрали.
- 🆕 **`DEPLOY.md` дополнен в цикле 4** (+192 строки): раздел «Инвентарь секретов» (на который раньше
  ссылался `backup.sh` вникуда) появился, `.env` в бэкапе описан, рядом — процедура ротации ключа
  шифрования и создание каталога `state/` до первого `docker compose up`. Прежняя претензия
  «§11.1 перечисляет шесть шагов из семи» в этой редакции **снята**.
- 🚀 Преамбула `ARCHITECTURE.md` (цикл 3) называет baseline **цикла 3** — исторические числа на
  момент начала того цикла, а не текущие (`488`/`449`/`100`, §7). Преамбула
  `ARCHITECTURE_CYCLE4.md` аналогично называет baseline цикла 4 (`215`/`407`/`78`).
- ⚖️ Соглашения об архиве (`docs/history/`) в репозитории **по-прежнему нет**, каталога такого нет,
  в README оно не описано. Циклы 4 и 5 решают проблему **суффиксом в имени файла**
  (`*_CYCLE4.md`, `*_CYCLE5.md`, `SPEC_CYCLE3_PRODUCTION.md`, `SPEC_CYCLE4_NOTIFICATIONS.md`), а не
  переносом в архив. Поэтому документы трёх последних циклов **одновременно лежат в корне**, и эта
  редакция `CURRENT_STATE.md` их, как и прошлая, **не архивировала** — переносить некуда.
  Более ранние редакции живут только в git-истории
  (SPEC цикла 2 — `git show 0492092:SPEC.md`, цикла 1 — `e6b746c`, ещё более ранняя — `7c86ca2`).
- ⭐ `SPEC_DEFERRED_NOTIFICATIONS.md` и `SPEC_APPENDIX_CHANNELS.md` — сохранённая спека **отложенной**
  темы уведомлений по телефону (MAX/SMS). 🆕 Цикл 4 закрыл эту тему **другим каналом** (WhatsApp), но
  файлы остались: на них ссылается код (`ProfileController.ChangePhone` — пункт Д-1 о подтверждении
  номера, он **не реализован и сейчас**), и исследование каналов доставки из приложения сохраняет
  ценность. Читать их как «план работ» уже нельзя.

---

## 6. Конвенции проекта

Соблюдаются последовательно; ниже — то, чему стоит следовать, а не изобретать рядом своё.

### Бэкенд (C#)

- **Primary constructors** для всех контроллеров и сервисов:
  `public class BookingsController(AppDbContext db, SlotService slotService, ...) : ControllerBase`.
  Приватных полей `_db` нет нигде.
- **File-scoped namespaces**, `ImplicitUsings`, `Nullable` включены во всех проектах.
- **DTO — только `record`** с позиционными параметрами. Никаких классов-DTO, никакого AutoMapper:
  маппинг руками, обычно приватным статическим методом `MapToDto` в контроллере
  (`CompaniesController.MapToDto`, `BookingsController.MapToDto`) — намеренно «единственный источник
  истины», о чём есть комментарий в коде.
- **Именование:** сущности — `ServiceBooking.Core/Entities/<Name>.cs`, DTO — `DTOs/<Домен>/<Name>Dto.cs`,
  контроллеры — `<Домен>Controller`. Маршруты: `[Route("api/[controller]")]` там, где имя совпадает
  (`auth`, `bookings`, `companies`, `services`, `workinghours`), и явная строка там, где нет
  (`api/admin`, `api/masters`, `api/profile`, `api/reports`, `api/reviews`, `api/schedule-template`,
  `api/companies/{id:guid}/mail`).
- **Мелкие DTO живут в конце файла контроллера** (`AdminController.cs`, `ProfileController.cs`,
  `ReportsController.cs`, `MastersController.cs`, `ScheduleTemplateController.cs`), крупные вынесены в `DTOs/`.
- **Обработка ошибок — двухрежимная.** Осознанные 4xx контроллеры возвращают напрямую и **plain text**:
  `NotFound()`, `Forbid()`, `BadRequest("...")`, `Conflict(...)`, `StatusCode(402, "...")`,
  `NoContent()` — фронтовые мапперы (`src/utils/*Error.ts`) читают `response.data` как строку, поэтому
  ValidationProblemDetails там, где нужен свой текст, намеренно не используется.
  🗓 **Цикл 6 распространил эту конвенцию на автоматическую валидацию модели.** Раньше 400 от
  `[ApiController]` (пустой/невалидный query- или body-параметр, пойманный до входа в метод) уезжал
  как `application/problem+json` с машинными именами полей в `errors` — фронтовые мапперы такое
  тело не понимали и показывали «Проверьте введённые данные», пряча настоящую причину (это и был
  блокер US-60). Теперь `ApiBehaviorOptions.InvalidModelStateResponseFactory` в `Program.cs`
  приводит их к тому же виду: **голая строка `text/plain`, по-русски, с названием поля**
  (`Services/ModelValidationErrorFormatter.cs`, словарь понятных имён полей; незнакомое имя поля
  даёт общую фразу, а не сырой C#-идентификатор). ⚠️ Это **глобальная** правка: она действует на
  **все** контроллеры, включая те, которых цикл 6 не касался. Какие запросы получают 400, а какие
  нет, она не меняет — только форму тела.
  ⚠️ Единственное намеренное исключение из «4xx — строка»: **400 от Identity при регистрации** —
  это **массив `{code, description}`** (`application/json`), и трогать его не стали; разбирает его
  фронт (`utils/authError.ts`, §4.1).
  **Необработанные исключения** (цикл 1) вне Development ловит `app.UseExceptionHandler` и отдаёт
  `application/problem+json` с `traceId`; в Development работает developer exception page.
  **402 Payment Required — проектная конвенция для «упёрлись в тариф»** (лимит компаний, лимит
  сотрудников, online booking, mailing, analytics). **429** — загрузки и четыре политики цикла 3
  (см. ниже). **451 Unavailable For Legal Reasons** — «требуется принять новую редакцию документов»
  (цикл 3, `LegalConsentFilter`); фронт обрабатывает его отдельно, разлогинивать пользователя нельзя.
- **Авторизация — двухуровневая:** атрибут `[Authorize]`/`[Authorize(Roles=...)]` + приватный
  асинхронный предикат внутри контроллера (`CanManageCompany` / `CanManage` / `CanManageBookingAsync`).
  Приватные предикаты остались конвенцией, но их «членская» половина **обязана** идти через
  `Services/CompanyMembership.cs` (`IsStaffAsync` / `IsOwnerAsync`) — единственное SQL-определение
  «этот человек действительно работает в этой компании». Своих `AnyAsync` по `CompanyMembers` в новом
  коде быть не должно (единственное оставшееся исключение — `MailingController`).
- **Конкурентность:** любое «посчитал → записал» оборачивается в транзакцию +
  `AdvisoryLock.AcquireAsync(db, key)` (`pg_advisory_xact_lock`). Ключи: `booking-slot:{masterId}:{date}`,
  `owner-companies:{userId}`, `company-members:{companyId}`, `schedule-template:{masterId}:{companyId}`,
  `working-hours:{masterId}:{companyId}`, `company-photo-quota:{companyId}`,
  `scheduled-task:{name}`. Для фоновых задач есть неблокирующий `TryAcquireAsync`
  (`pg_try_advisory_xact_lock`): «кто-то уже делает» означает «пропустить запуск», а не «встать в очередь».
- **Загрузка файлов — только через `ImageUploadService`.** Четыре точки загрузки в продукте, одна
  реализация конвейера. Новая точка загрузки = новый `ImageProfile` + вызов `ReadAndProcessAsync`,
  а не свой код чтения `IFormFile`. Тип файла определяется **исключительно по сигнатуре байт**
  (`ImageSignature`), `Content-Type` и имя файла не используются нигде.
- **Два класса хранения — два разных типа возвращаемого значения** (`FileStorage`): публичный отдаёт
  URL, приватный — непрозрачный ключ, из которого нет пути обратно к URL. Флага `isPublic` намеренно
  нет: ошибка «положил приватное в публичное» должна быть ошибкой компиляции, а не находкой ревью.
- **Порядок операций с файлами:** на замену — «новый файл → коммит строки → удаление старого»;
  на удаление — «строка БД → файл». Худший исход в обоих случаях — осиротевший файл, который подметёт
  фоновая уборка; строка, указывающая в никуда, недопустима.
- **Фоновая работа — только через `IScheduledTask`.** Новая периодическая задача = класс + одна строка
  `AddScoped<IScheduledTask, …>` в `Program.cs`. Свои `BackgroundService`/таймеры заводить не нужно.
- ⭐ **Пагинация — только через `DTOs/Common/PagedResult.cs`.** Новый список наружу отдаётся конвертом
  `PagedResult<T>(Items, Page, PageSize, Total, HasNext)`, параметры нормализуются
  `Pagination.Normalize(page, pageSize)` (`pageSize` по умолчанию 20, потолок 100 — **клампится, а не
  400**; `page` клампится и снизу, и **сверху**, чтобы `(page-1)*pageSize` не переполнил `int`).
  Свои `?page=`-раскладки в контроллерах писать нельзя: четыре существующие выборки специально сведены
  в одну точку.
- ⭐ **Identity-роли пересчитываются только `Services/IdentityRoleSync.cs`.** Любое изменение
  `CompanyMember` завершается вызовом `IdentityRoleSync.SyncAsync(db, userManager, userId)` **внутри
  той же транзакции и строго после `SaveChangesAsync`** (порядок объяснён в XML-комментарии класса).
  Функция владеет **только** ролями `Master`/`CompanyOwner`; `Client` и `SuperAdmin` не трогает
  никогда. Сейчас вызывается из пяти операций (`Create`, `AddMember`, `RemoveMember`,
  `UpdateCompanyOwner` — там дважды, для нового и старого владельца, — и `DeleteAccount`), итого шесть
  вызовов.
- ⭐ **Правовой гейт — глобальный фильтр с allow-list, а не атрибут на действии.**
  `LegalConsentFilter` зарегистрирован как MVC-фильтр на **все** действия; он молчит для анонимных
  запросов и для явного allow-list (сами правовые эндпоинты, принятие согласия, выгрузка данных).
  Новый эндпоинт по умолчанию **закрыт** гейтом — это намеренное направление ошибки.
- ⭐ **Логирование — Serilog, и телефоны в него не попадают.** Хост-логгер заменён целиком
  (`builder.Host.UseSerilog`), формат — `CompactJsonFormatter` в stdout и в `logs/app-.json`.
  Маскирование — `Services/LogMasking.cs` + `PhoneMaskingEnricher`; `UseSerilogRequestLogging`
  отдельно позаботился о том, чтобы `?search=<телефон>` не утёк в строку запроса. Синк в GlitchTip —
  `Sentry.Serilog`, включается **только** при непустом `Sentry:Dsn`. Осознанные 4xx (400/402/403/404/
  409/429/451) логируются на уровне Information, а не Warning/Error.
- ⭐ **Проверки прод-конфигурации — в `Services/DeploymentSafetyChecks.cs`, чистыми статическими
  методами.** Новую обязательную настройку добавляют туда (и покрывают юнит-тестом), а не отдельным
  `if` в `Program.cs`.
- **Телефон — только через `PhoneNormalizer`.** Любая новая точка входа, принимающая номер, обязана
  нормализовать его до записи и до поиска.
- 🆕 **Секреты чужих аккаунтов — только через `SecretProtector`.** Никакого хранения токена
  провайдера в открытом виде, никакого Data Protection, никакого своего AES. Нарушение расшифровки
  бросает `ChannelSecretUnavailableException`, и вызывающий обязан перевести это в «каналу нужно
  переподключение», а **не** дать `CryptographicException` или его текст дойти до лога/ответа.
- 🆕 **Русский текст статусов и причин собирает сервер, в одном месте.** `ChannelStateReason` и
  `NotificationReason` — машиночитаемые коды; формулировки живут в `Services/NotificationTexts.cs` и
  `Services/ChannelPresentation.cs`. Фронт их **не сочиняет** и не собирает из кусочков.
- 🆕 **Параметры платформы, правимые суперадмином, — таблица `PlatformSetting`, а не `appsettings`
  и не поле на тарифе.** Чтение — `Services/Notifications/PlatformSettings` (кеш 60 с), запись —
  `Services/PlatformSettingsWriter` (пишет журнал). **Отсутствие ключа ≠ значение по умолчанию:**
  отсутствие цены означает «опция не предлагается», а не «бесплатно».
- 🆕 **«Поле не прислали» и «прислали null» различаются через `DTOs/Common/Optional<T>`** (+ свой
  `OptionalJsonConverterFactory`, зарегистрирован в `Program.cs`). Для частичных обновлений
  (например, снять ручной часовой пояс) писать отдельные флаги `xxxSpecified` не нужно.
- 🆕 **Абстракции над временем, задержкой и случайностью обязательны для всего, что «ждёт»:**
  `INotificationClock`, `IDispatchDelay`, `IPauseGenerator`. Прода это не меняет, но делает
  отправщик тестируемым мгновенно (§7.2). `DateTime.UtcNow` и `Task.Delay` прямо в такой логике —
  регресс.
- 🆕 **Внешний провайдер — за интерфейсом с заглушкой по умолчанию.** `INotificationTransport` /
  `IChannelProvisioning`, дефолт — `logging`/no-op, реальный адаптер включается строкой конфигурации,
  **нераспознанное значение роняет старт** (тихий откат на заглушку в проде недопустим).
- 🆕 **Миграции: первую миграцию смёрженного цикла не редактируют.** Всё новое — новой миграцией
  (образец — `AddChannelLastStateReason`, добавленная поверх по итогам ревью).
- 🆕 **Значения `enum`, участвующие в SQL-фильтрах индексов или в битовых масках, — append-only.**
  Касается `NotificationStatus` (частичный индекс диспетчера с сырым фильтром `"Status" = 0`) и
  `NotificationType` (позиции битов в `EnabledTypeMask`). Перестановка ломает **молча**.
- ⭐ **Rate limiting — именованные политики в `Program.cs` + `[EnableRateLimiting("…")]` на действии.**
  Глобального лимитера **нет** (`app.UseRateLimiter()` — no-op без атрибута), поэтому «не навесил
  атрибут» = «лимита нет»; на health-эндпоинтах это сделано намеренно. Пять политик:
  `uploads` (10/мин на пользователя, четыре точки загрузки), `auth-login` (10/мин на IP),
  `auth-register` (5/час на IP), `booking-create` (120/час авторизованным, 10/час анонимным),
  `data-export` (3/сутки). Числа читаются из секции `RateLimits` конфигурации; IP берётся **после**
  `UseForwardedHeaders` с явным списком `ForwardedHeaders:TrustedNetworks`. Тело ответа 429 пишет
  `OnRejected` — иначе фронтовым мапперам `*Error.ts` нечего было бы показать.
- **Комментарии — развёрнутые, объясняющие «почему», на английском.** Это заметная черта кодовой базы:
  почти каждое неочевидное решение прокомментировано абзацем (`Program.cs` про `OnTokenValidated`,
  🗓 `SlotCalculator` про `ScheduleFallback` (бывший `allowWithoutSchedule`), `EffectivePlan.Free`
  про Free-базлайн). XML-документация включена (`GenerateDocumentationFile`) и подхватывается
  Swagger'ом.
- **Локализация:** сообщения об ошибках API — **в основном английские** (`"Time slot is no longer
  available"`, `"Company limit reached for the current tariff plan."`), но есть русские вкрапления
  в `ProfileController` (`"Неверный текущий пароль"`) и `MailingController`. Единой конвенции нет.
  🗓 Цикл 6 перевёл на русский **шесть** сообщений сервера (отказы при записи — 402/403, состав
  визита, горизонт, дата подписки) и все тексты новых эндпоинтов. Общего правила это по-прежнему не
  задаёт: **новые сообщения пишутся по-русски, старые английские никто массово не переводил.**
- **Миграции:** EF Core Code First, применяются автоматически при старте
  (`await db.Database.MigrateAsync()` в `Program.cs`). Имена — `PascalCase` описанием изменения.
  Скриптов отката/сидов данных (кроме ролей и SuperAdmin) нет.

🗓 **Конвенции, которые подтвердил и ужесточил цикл 6** (им следовать, а не заводить рядом своё):

- **Чистая логика живёт вне контроллеров, в `Services/*.cs`, без EF и без HTTP** — и тем самым
  покрывается юнит-тестами. Цикл 6 добавил по этому образцу шесть файлов: `LoginOutcome`,
  `BookingHorizon`, `BookingServiceSelection`, `MasterCapability`, `SubscriptionDiagnostics`,
  `ModelValidationErrorFormatter`. Всё, что требует запроса в БД, остаётся у вызывающего.
- **Правило, выраженное перечислением, а не `bool`.** Замена `allowWithoutSchedule` на
  `ScheduleFallback` — прямое следствие находки ревью: булев флаг у третьего вызова означает уже не
  то, что задумывалось. Новое состояние с тремя и более исходами оформляется enum'ом.
- **Временное ограничение — отдельный предикат, а не правка постоянной валидации.**
  `PhoneNormalizer.IsRussian` намеренно **не** вложен в `IsValid` (техническая граница E.164, на
  которую опираются миграция, поиск по телефону и номер самого GREEN-API): снять «только Россия»
  потом — это правка одной функции, а не ревизия всех вызовов.
- **Снимок вместо ссылки в исторических данных.** `BookingService.NameSnapshot`/`DurationMinutes`/
  `Price` копируются на момент записи — переименование или подорожание услуги не переписывает уже
  состоявшийся визит. Тем же приёмом раньше сделаны `Booking.Price` и `Booking.CommissionPercent`.
- **Порядок проверок в эндпоинте — часть контракта, а не деталь.** Сначала права, потом сверка
  принадлежности (см. `excludeBookingId`, §4.5): обратный порядок делает из кода ответа оракул.

⚖️ **Конвенции, которые добавил цикл 5** (им следовать, а не заводить рядом своё):

- **Одно правило уничтожения — один файл в `Services/Retention/Rules/`, зарегистрированный поимённо
  в `Program.cs`.** Никакой автоподхватки рефлексией: список регистраций **и есть** список того, что
  работает, и его можно прочитать глазами. Побочное следствие используется намеренно — отсутствие
  правила видно так же явно, как его наличие (случай `NotificationOptOut`, §4.18).
- **Расширяемое — строкой, закрытое — перечислением.** Ключи текстов интерфейса (`LegalTextKey`) —
  строковые константы, потому что добавление седьмого текста должно быть правкой манифеста; типы
  документов (`LegalDocumentType`) — перечисление, потому что каждый член означает ещё и claim, и
  политику гейта, и ветку сравнения версий.
- **Нераспознанное значение конфигурации роняет старт**, а отсутствующее значение трактуется в
  **более строгую** сторону (`gate` по умолчанию `Global`, `DryRun` по умолчанию `true`,
  `InstanceCreationEnabled` по умолчанию `false`). Fail-closed — общее правило цикла.
- **Опасные операции по умолчанию выключены и включаются явным действием оператора**, а не наоборот.
- **Шифруемое поле расшифровывается только явным вызовом**, никогда — прозрачным конвертером EF.
- **Доступ запрещать явным `Forbid`, а не отдавать пустое значение** — чтобы «ничего не нашлось» и
  «вам не положено» не выглядели одинаково.
- **Разбор составного идентификатора живёт в одном месте** (`Services/ClientKey.cs` для `{clientKey}`).
- **Сроки и пороги — конфигурация; юридические минимумы — fail-fast поверх конфигурации.**

### Фронтенд (TypeScript/React)

- ⚖️ **Второй zustand-стор появился и это осознанно:** `ownerGateStore` держит состояние
  владельческого гейта отдельно от `authStore`, потому что это состояние сессии UI, а не аутентификации.
- ⚖️ **Правовые тексты тянутся с сервера хуком `useLegalText`, а не хардкодятся в компоненте.**
  Список целей согласия форма строит **из ответа `/api/legal/documents`**, а не из своего массива, —
  смена набора целей юристом не требует релиза фронта.
- ⚖️ Мапперов ошибок стало больше по тому же образцу (`subjectRequestError.ts`) — новый домен ошибок
  оформляется отдельным маппером, а не `catch` с текстом внутри компонента.

- **Функциональные компоненты, именованный экспорт** (`export function CabinetPage()`); дефолтный экспорт
  только у `App.tsx`.
- **Слой API — `src/api/<домен>.ts`,** объект-неймспейс с методами, всегда `.then(r => r.data)`:
  ```ts
  export const companiesApi = { getAll: () => api.get<Company[]>('/companies').then(r => r.data), ... }
  ```
  Компоненты **никогда не дёргают axios напрямую** — только через эти модули.
- **Данные с сервера — исключительно react-query** (`useQuery` / `useMutation` + `queryClient.invalidateQueries`).
  Ключи — массивы вида `['my-companies']`, `['company', slug]`, `['services', companyId]`.
- **Клиентское состояние — только `authStore`** (zustand + persist). Другого глобального стора нет,
  локальное состояние — `useState`.
- **Формы:** `react-hook-form` там, где полей много (создание компании, услуги), иначе — управляемые `useState`.
- **Ошибки HTTP → текст пользователю** через `src/utils/*Error.ts` — 🆕 сейчас их двенадцать
  (`authError`, `bookingError`, `cancelError`, `companyError`, `companyAdminError`, `companyManageError`,
  `legalError`, `memberError`, `planError`, `scheduleError`, `uploadError`, 🆕 `notificationError`,
  🗓 `providesServicesError`)
  — `switch` по `status` с явной обработкой
  402/403/409/429. Это устоявшийся паттерн: новый пользовательский сценарий с гейтом должен получить
  свой маппер, а не строить текст на месте.
  🗓 Цикл 6 добавил к этому правилу оговорку: маппер обязан пережить тело, которое **не строка** —
  `authError.ts` умеет разобрать массив ошибок Identity и, как страховка, вытащить текст из
  `ProblemDetails`/`ValidationProblemDetails`. Молча схлопывать чужую форму тела в общую фразу
  нельзя: именно так и потерялась настоящая причина отказа в US-60.
- 🗓 **Ввод телефона — только через `components/ui/PhoneInput.tsx`**, а не собственный `<Input>` с
  ручной маской. У компонента два режима: `restrictToRussia` по умолчанию `true` (формы, которые
  **создают** номер — регистрация, смена телефона, добавление сотрудника, гостевая запись) и
  `false` для формы входа, где номер может быть иностранным. Отдельного клиентского нормализатора
  для входа больше нет — дублирующий удалён (`d536650`).
- **Приватные изображения грузятся как blob,** а не через `<img src>`: `hooks/useAuthedImage.ts` +
  `components/ui/AuthedImage.tsx`, `IntersectionObserver` для ленивой загрузки, `staleTime: Infinity`
  в react-query. Публичные картинки (логотип, аватар, картинка услуги) — обычный `<img src>`.
- ⭐ **Пагинированные списки** рисует общий `components/ui/Pagination.tsx`, поиск по серверу
  дебаунсится общим `hooks/useDebouncedValue.ts` (см. `MasterClientsPage.tsx` как образец связки
  «дебаунс → серверный `search` → `PagedResult`»). Скачивание файлов из защищённого API —
  `hooks/useExportData.ts` (blob + `a[download]`), по тому же принципу, что и приватные картинки.
- ⭐ **Линтер и форматтер есть:** ESLint 9 (flat-конфиг `frontend/eslint.config.js`,
  `typescript-eslint`, `eslint-plugin-react`/`react-hooks`/`react-refresh`, `eslint-config-prettier`)
  и Prettier 3. Команды — `npm run lint` и `npm run format`; `npm run lint` **входит в CI**.
  На бэкенде аналог — `.editorconfig` в корне (описывает уже сложившийся стиль; в CI не проверяется,
  §9.25).
- **Тесты фронта** лежат рядом с кодом (`src/utils/phone.test.ts`,
  `src/components/clientNotes/PhotoGallery.test.tsx`), а не в отдельном каталоге.
  `globals: false` — `describe`/`it`/`expect` импортируются явно, в тон конвенции именованных экспортов.
  `vitest.config.ts` намеренно отделён от `vite.config.ts`.
- **Стили — только Tailwind-утилиты в JSX,** с произвольными значениями (`text-[13.5px]`, `rounded-[18px]`)
  под макет. Токены палитры (`cream`, `ink`, `gold`, `line`, `muted`, `success/danger/warning/info`) — в
  `tailwind.config.js`. CSS-модулей нет, `index.css` содержит только base-слой и `line-clamp-2`.
- **UI-примитивы** в `src/components/ui/`: `Button`, `Input`, `Card`, `Modal`, `Badge`, `Icon`
  (свой SVG-набор из ~40 иконок, эмодзи не используются). Оверлеи закрываются через общий хук
  `src/hooks/useOverlayDismiss.ts`.
- **Вкладочные страницы** оформлены как локальные компоненты в одном файле (`AdminPage.tsx`,
  `CabinetPage.tsx`) либо вынесены в подпапку (`pages/owner/`, `pages/admin/`).
- **Язык интерфейса — русский**, форматирование чисел `toLocaleString('ru-RU')`, дат — `date-fns` с локалью `ru`.
- **Типы дублируются вручную** в `src/types/index.ts` — генерации из OpenAPI нет
  (отсюда риск расхождений, см. §5.3.4). 💳 **Частичное исключение с цикла 7:** типы биллинга
  берутся из сгенерированного `src/types/api-cycle7.generated.ts` (`npm run types:api`), руками он
  не правится. Правило для новых экранов биллинга — **брать перечисления оттуда**, а не описывать
  строковые литералы заново; для всего остального ручной `types/index.ts` остаётся нормой.

### 💳 Конвенции биллинга (цикл 7) — чему следовать, если трогаете эту область

- **Предметный подкаталог в `Services/`.** Новый код биллинга кладётся в `Services/Billing/`, а не
  в корень `Services/`. Внутри — разделение на чистые калькуляторы (`*Calculator`, покрываются
  юнитами) и сервисы с БД (`*Service`, `*Reader`, `*Writer`, `*Provisioner`), см. §2.
- **Тексты отказов — в одном месте.** `BillingTexts` (бэкенд) и `utils/billingError.ts` /
  `utils/adminBillingError.ts` (фронт). Сервер **составляет текст сам**, клиент его печатает, а не
  собирает из кусков — это было отдельно исправлено в `46e2bbc` (`fundingText`).
- **Ключи возможностей — из `CapabilityKeys`/`OptionCapabilityCatalog`**, это `companies` и
  `employees`. ⚠️ Коды опций ключами возможностей **не являются** — на этом уже ошиблись один раз
  (`d60e29b`, потребовалась 11-я миграция).
- **Advisory-lock'и берутся в фиксированном порядке — аккаунт, затем компания** (`6f11eda`), ключи
  унифицированы (`f3760c2`). Нарушение порядка даёт взаимную блокировку.
- **Контракт первичен.** Для биллинга есть машиночитаемый `contracts/cycle7/openapi.yaml`; новые
  поля и коды ответов сначала туда, потом в код и в сгенерированные типы.
- **Отзыв маршрута оформляется как `410 Gone` с указанием замены**
  (`AdminController.LegacyEndpointGone`), а не удалением маршрута и не `404`.

### 📸 Конвенции цикла 10 — чему следовать, если трогаете запись, журнал или фото

1. **Модалка записи в продукте одна.** Не заводите вторую рядом: `ManualBookingModal` удалён именно
   потому, что два экрана записи разошлись по механике выбора даты. Новая точка входа = ещё один
   вызов `BookingModal` с нужными пропсами (`company`/`service`/`allowMultipleServices`), а не копия
   компонента.
2. **`staffMode` берётся только из ответа сервера** (`availability.staffMode`), никогда — из
   `authStore`, роли в токене, пропсов или того, откуда модалку открыли. То же правило у
   `manual`/`extendedHours`/`includeHidden` на сервере: это **просьба, а не разрешение**, членство
   проверяет сервер сам.
3. ⚠️ **Намерение «записываю клиента» ≠ `staffMode`.** Намерение задаётся точкой входа и явным
   состоянием `bookForClient` (переключатель в форме), `staffMode` управляет **только** свободой
   выбора даты и времени. Вывод намерения из `staffMode` уже стоил цикла блокера: запись сотрудника
   самому себе уходила как гостевая — `ClientId = null`, без предоплаты и без согласий. Починка —
   `4f8ed63`; комментарий, объясняющий правило, живёт прямо в `BookingModal.tsx` рядом с
   `useState(!company)`.
4. **Журнал пишется ровно одним классом** — `Services/Bookings/BookingEventLog`. Появилось новое
   действие над записью — добавьте вызов туда же и в той же транзакции; `db.BookingEvents.Add(...)`
   из контроллера в ревью не проходит. Значения `BookingEventKind`/`BookingActorKind` **не
   перенумеровываются** — таблица append-only.
5. **Чистая логика — отдельным классом без БД**, как `CompanyPhotoOrdering` (порядок и уплотнение
   позиций) и `BookingEventTexts` (формулировки). Это же то, что покрывается юнит-тестами;
   контроллер остаётся на транзакции, advisory-lock'е и правах.
6. **Типы фронта для нового контракта генерируются, а не пишутся руками**:
   `npm run types:api:cycle10` из `contracts/cycle10/openapi.yaml` →
   `src/types/api-cycle10.generated.ts`. Прикладные типы в `types/index.ts` остаются ручными, как и
   раньше; сгенерированный файл — сверка формы, а не замена им.
7. **Новый API-модуль фронта** — тонкая обёртка над общим `api` из `api/client.ts`, возвращающая
   `r.data` (см. `api/companyPhotos.ts`); заголовки `multipart/form-data` — только там, где реально
   грузится файл. Инвалидация кеша — через `queryClient` ключами существующих запросов.
8. **Тексты ошибок, которые увидит пользователь, теперь пишутся по-русски** — включая общий конвейер
   загрузки изображений (§5.0-ter). Если добавляете новое сообщение об ошибке загрузки, проверьте
   мапперы `frontend/src/utils/uploadError.ts` и `companyManageError.ts`: они разбирают **подстроки**
   и молча деградируют до общего текста, если подстрока не совпала.

### 🔬 Тесты (конвенции цикла 8 — им нужно следовать, а не заводить своё рядом)

- **Единица изоляции — тест-класс.** Новый функциональный тест-класс объявляет
  `IClassFixture<TestDatabaseFixture>` и получает **свою** базу, склонированную из шаблона прогона,
  плюс свой `CustomWebApplicationFactory`. Коллекций `"Api"`/`"NotificationDispatch"` больше нет —
  не восстанавливать их «чтобы было как раньше».
- **Уникальные значения берутся у фикстуры, а не изобретаются на месте:** `fixture.Data.Phone()`,
  `.Email()`, `.Slug()`, `.Name()`, `.Dir()` (`Infrastructure/TestData.cs`). Уникальность здесь —
  по построению (слот класса зашит в значение), а не по договорённости; `Guid.NewGuid()` в теле
  теста считается регрессом, кроме случая, когда сам идентификатор — предмет проверки.
- **Ни один тестовый хост не настраивается вручную.** Всё, что раньше было ~60 строками
  `builder.UseSetting(...)` в каждой фабрике, живёт в `Infrastructure/TestHostSettings.Apply`
  (строка подключения, JWT, суперадмин на фабрику, временные корни `public`/`private`/`state`/
  `logs`/`legal` под `$TMPDIR/sb-test/<ключ прогона>/<слот класса>/`). Новая фабрика = новый
  `factoryTag` + запись в закрытый словарь `TestHostSettings.SuperAdmins`, иначе старт падает с
  объяснением.
- **`EnsureDeletedAsync` запрещён** — удаление базы только через `TestDatabaseLease`, только для
  имён `sbtest_<8 hex>_<слот>` и только через проверку `TestDatabaseNaming`. Это не стиль, а
  защита: `ServiceBooking.TestKit/TestDatabaseLease.cs` — единственное место в репозитории, где
  вообще выполняется `DROP DATABASE`.
- **Общего статического состояния между тестовыми классами быть не должно** — оно разобрано
  поимённо в `ARCHITECTURE_CYCLE8_PHASE2.md` §95.1; новое заводить нельзя, иначе параллелизм
  снова станет небезопасным.
- **Пинованные значения инфраструктуры (образ Postgres, `max_connections`, размер пула, TTL
  уборки) объявляются один раз** в `ServiceBooking.TestKit/TestInfrastructure.cs`; повтор числа
  литералом в другом файле ловится `deploy/ci/check-image-pins.sh` или ревью.
- **Порядок тестов внутри класса случайный** (`RandomTestCaseOrderer`) — писать тест, который
  зависит от соседа по классу, бессмысленно: он покраснеет на следующем семени.

### Git

- Сообщения коммитов — на английском, одна строка-заголовок в повелительном наклонении +
  развёрнутое тело с объяснением «почему», трейлер `Co-Authored-By: Claude …` (в цикле 3 —
  `Claude Opus 5`, раньше `Claude Sonnet 5`).
- Работа идёт в ветке **`develop`** — интеграционном стволе модели `master` ← `release-candidate`
  ← `develop` ← `cycle/NN-<слаг>` (§0.0 DEPLOY.md). `master` — предыдущее состояние, циклы в него
  **не вливались**: релиза не было, на боевую машину ничего не выкатывалось. Ветки завершённых
  циклов размечены задним числом (`cycle/01-cleanup`, `cycle/02-photos-scheduler`,
  `cycle/03-production-readiness`), `sanitation-cycle` сохранён и дублирует третью из них.
  CI триггерится на push в `master`, `release-candidate`, `develop`, любую ветку по маске
  `cycle/**` и на любой pull request.
  🆕 **Цикл 4 — первый, прошедший модель веток целиком по назначению:** работа шла в
  `cycle/04-notifications-whatsapp`, влилась в `develop` мерж-коммитом `2a11d07` **с сохранением
  истории ветки** (не squash), и в теле мержа записан результат приёмки: числа прогонов, два
  пройденных ревью и **явный список того, почему релиз не готов**. Это удобная точка отсчёта —
  `git log --oneline 0e61369..2a11d07` показывает весь цикл, а два коммита после мержа
  (`aae3541`, `7a36543`) — только починку CI.
- 🆕 Цикл 4 — **15 коммитов** в ветке цикла (`560526c..85ad781`) плюс мерж и две починки CI.
  Заголовки — в том же повествовательном стиле, что и раньше; тело объясняет «почему», трейлер
  `Co-Authored-By: Claude Opus 5` (у мерж-коммита — `Claude Sonnet 5`).
- ⚖️ Цикл 5 — **15 коммитов** в ветке `cycle/05-legal-compliance`, мерж-коммит `14c9331`
  (история ветки сохранена, не squash), плюс два коммита уже на `develop`: починка смоук-теста под
  новый контракт регистрации (`aecddb1`) и документы (`071fc11`). Модель веток соблюдена так же,
  как в цикле 4. Обратите внимание на порядок: **правка смоука пришла после мержа** — в самой ветке
  цикла расхождение не заметили (§9, L6).
- 🔬 Цикл 8 — **56 коммитов** в диапазоне `7b382d8..6562a86` (ветка `cycle/08-test-env-isolation`,
  смёржена в `develop`), две фазы в одной ветке: фаза 1 — изоляция прогонов друг от друга, фаза 2 —
  параллелизм внутри прогона (потребована заказчиком по ходу цикла). Заголовки несут идентификаторы
  задач архитектуры (`T8-B2`, `T8-P7`, `T8-P11a`) либо номера находок ревью (`N1/N2/N20`,
  `B1/B2`). Отдельно стоит заметить коммит `36d56f4` «Undo the API changes that cycle 8 promised not
  to make» — цикл поймал сам себя на нарушении собственного запрета трогать HTTP-контракт и откатил
  правки.
  ⚠️ **Процессная находка цикла, не связанная с кодом:** дважды за цикл два агента работали в
  **одном git-чекауте одновременно** и затирали друг другу незакоммиченные правки (следы — коммит
  `500362d` «Salvage the sweeper work the network outage interrupted»). Рабочий вывод, записанный
  здесь для следующих циклов: **отдельный `git worktree` на роль**. Сам цикл 8 это уже использовал
  на приёмке — второй прогон гнали из `git worktree add --detach`.
- Цикл 3 — **26 коммитов** (`f3adc6e..7a551eb`), в отличие от цикла 2, уехавшего одним коммитом
  `0492092`. Заголовки мелких коммитов несут идентификатор задачи из ARCHITECTURE (`T-B1`, `T-F5`, …)
  и историю из SPEC (`US-42`), ломающие изменения помечены прямо в заголовке (`BREAKING #1`,
  `BREAKING #2`). Рабочее дерево чистое.

---

## 7. Тесты

### Что есть — три набора

| Набор | Проект/каталог | Что нужно на машине | Команда | Объём |
|---|---|---|---|---|
| Юнит-тесты бэкенда | `ServiceBooking.UnitTests` | ничего | `dotnet test ServiceBooking.UnitTests` | 📸 **942** запуска (было 908) |
| **Функциональные (API) тесты** | `ServiceBooking.Tests` | 🔬 **запущенный Docker** (не PostgreSQL!) | `dotnet test ServiceBooking.Tests` | 📸 **603** запуска (было 549) |
| Тесты фронтенда | `frontend/src/**/*.test.ts(x)` | Node 20 | `npm run test:run` (в `frontend/`) | 📸 **369** тестов, 57 файлов (было 341 / 53) |

📸 **Для QA: базовый функциональный прогон — `dotnet test ServiceBooking.Tests`** (xUnit +
`WebApplicationFactory` + реальный Postgres в контейнере, поднимается самим прогоном; нужен
запущенный Docker). Цикл 10 механику прогона **не менял** — ни команды, ни предусловий, ни правил
изоляции; изменились только числа выше.

📸 **Что добавил цикл 10 (+34 юнит, +54 функциональных, +28 фронтовых):**
- функциональные — три новых файла: `Tests/ManualBookingFreedomTests.cs` (18 кейсов, префикс
  `BK-`), `Tests/BookingHistoryTests.cs` (16, префикс `BKH-`), `Tests/CompanyPhotosTests.cs`
  (20, префикс `CPH-`); плюс правки `ClientNotePhotosTests`/`CompaniesTests` под русские тексты
  ошибок загрузки;
- юнит — `BookingEventTextsTests.cs` (5) и `CompanyPhotoOrderingTests.cs` (14, включая перестановки,
  уплотнение после удаления и отказы на неполной перестановке);
- фронтенд — `BookingHistoryPanel.test.tsx`, `CompanyPhotoGallery.test.tsx`,
  `CompanyPhotosSection.test.tsx`, `BookingModal.captcha.test.tsx`, `utils/companyManageError.test.ts`,
  существенно переписанные `BookingModal.test.tsx` и `BookingCalendar.test.tsx`;
  **удалён** `ManualBookingModal.test.tsx` вместе с компонентом.

💳 **Набор, команда и предусловия цикл 7 тоже не менял** — механика прогона осталась такой, какой
её сделал цикл 8. Цикл 7 добавил **+131 юнит, +61 функциональный, +58 фронтовых** теста.
Статический пересчёт на `0929b48` даёт 607 `[Fact` + 301 `[InlineData]` = 908 (юнит),
532 + 16 = 548 (функциональные; расхождение на единицу с приёмочными 549 — тот самый `[Fact` внутри
комментария в `LegalConsentVersionChangeTests.cs`, описанный ниже), 341 `it(...)` в 53 файлах (фронт).

🗓 **Набор, команда и предусловия цикл 6 не менял** — всё, что описано ниже про Docker, одноразовые
базы и параллелизм, осталось ровно таким, каким его сделал цикл 8. Цикл 6 добавил тесты, а не
механику прогона.

🔬 **Главное изменение цикла 8 для всех, кто прогоняет тесты: заранее поднятая PostgreSQL больше не
нужна и не используется — нужен Docker.** Прогон сам поднимает контейнер `postgres:16-alpine` на
свободном порту, заводит в нём свои одноразовые базы и убирает их за собой. Дефолтной строки
подключения в коде **нет вовсе**: нет Docker и нет явно заданного сервера — прогон отказывается
стартовать с объяснением, **ничего не удаляя**. Полное человеческое руководство —
🔬 **`docs/testing-isolation.md`** (~33 КБ), оно подробнее этого раздела и рассчитано на того, кто
встретил красный прогон.

Количества посчитаны статически по атрибутам `[Fact]`/`[Theory]`+`[InlineData]` и вызовам `it(...)`.
В `ServiceBooking.Tests` атрибуты идут парой `[Fact, TestCase("ID")]` — голого `[Fact]` там не
встретить, искать надо `[Fact` (⚠️ один такой хит — внутри комментария в
`LegalConsentVersionChangeTests.cs`, отсюда 450 вхождений при 449 реальных тестах).

⚖️ **Пропорция роста в цикле 5 обратная циклу 4:** фронтенд +81 (впервые обогнал функциональный
набор), юнит +103, функциональный всего +16. Причина не в небрежности, а в предмете: цикл 5 менял
формы, экраны и тексты, а не серверные алгоритмы, и значительная часть его серверной работы —
**правка уже существовавших** функциональных тестов под новый контракт, а не написание новых.

### 7.1 Юнит-тесты бэкенда — `ServiceBooking.UnitTests`

**Ни БД, ни HTTP, ни моков** — только чистые функции.

- **Фреймворк:** xUnit 2.5.3 + FluentAssertions 6.12.1, `Microsoft.NET.Test.Sdk` 17.8.0,
  `coverlet.collector` 6.0.0. Ссылается напрямую на `ServiceBooking.API`.
- `ServiceBooking.API.csproj` содержит `<InternalsVisibleTo Include="ServiceBooking.UnitTests" />`.
- Атрибут `[TestCase(...)]` здесь **не используется** — стабильные ID есть только у функционального набора.

🆕 **Цикл 4 добавил 22 файла** (перечислены ниже отдельным блоком) — юнит-набор стал основным
носителем правил этого цикла.

| Файл | Что покрывает | `[Fact]` + `[InlineData]` |
|---|---|---|
| **`DeploymentSafetyChecksTests.cs`** ⭐🆕⚖️ | fail-fast прод-конфига: Jwt-ключ, пароль SuperAdmin, приватный корень внутри `wwwroot`, доверенные сети, 🆕 секреты уведомлений, отпечаток ключа, наличие tzdata, ⚖️ режим согласия на передачу, страна сервера провайдера, минимальные сроки хранения, 🗓 разбор `Booking:DefaultWorkWindow` | 🗓 **88** (было 84) |
| **`LegalDocumentProviderTests.cs`** ⭐ | чтение и перечитывание `legal.json`, версии, `isDraft`, `changeKind` | 9 + 11 = 20 |
| **`PaginationTests.cs`** ⭐ | `Pagination.Normalize`: кламп снизу и **сверху** (переполнение `(page-1)*pageSize`), `HasNext` | 7 + 14 = 21 |
| **`LogMaskingTests.cs`** ⭐ | маскирование телефонов в логах | 5 + 11 = 16 |
| **`LegalConsentFilterTests.cs`** ⭐ | решение фильтра «требуется ли новое согласие» | 4 |
| `BookingFiltersTests.cs` | разбор `?status=`, предикат `Upcoming` | 11 + 6 = 17 |
| `SlotCalculatorTests.cs` | сетка слотов, перерывы, 🗓 три режима `ScheduleFallback` (включая «`WholeDay` игнорирует и расписание, и перерывы»), граница суток | 🗓 выросли в цикле 6 |
| `ImageProcessorTests.cs` | ресайз, кроп, EXIF, формат вывода | 15 |
| `PhoneNormalizerTests.cs` | каноническая форма, валидность, 🗓 `IsRussian`/`TryNormalizeRussian` | 🗓 выросли в цикле 6 |
| `ScheduledTaskScheduleTests.cs` | `IsDue` / `IsOverdue` | 10 |
| `SubscriptionResolverRulesTests.cs` | правило разрешения тарифа, включая `PlanConfig.IsActive` | 10 |
| `PhotoQuotaTests.cs` | окно хранения (⚖️ без `Forever`) | ⚖️ 8 |
| `ImageSignatureTests.cs` | определение JPEG/PNG/WEBP по байтам | 7 |
| `TokenServiceTests.cs` | claims, хеш `SecurityStamp` | 5 |
| `FileStorageTests.cs` | containment-проверка путей, ключи vs URL | 8 |

🗓 **Файлы цикла 6** (семь новых; плюс выросли `SlotCalculatorTests` и `PhoneNormalizerTests`):

| Файл | Что покрывает | `[Fact]` + `[InlineData]` |
|---|---|---|
| `BookingServiceSelectionTests.cs` | состав визита: 1..5 услуг, повторы, совпадение `serviceId` с первым элементом, суммы длительности и цены | 15 |
| `BookingHorizonTests.cs` | горизонт записи: нормализация `null`/`0`/отрицательного, границы `[1, 365]`, последняя доступная дата | 12 |
| `LoginOutcomeTests.cs` | отображение `SignInResult` → исход → код ответа (401/403/423), включая неразличимость двух 401 | 9 |
| `SubscriptionDiagnosticsTests.cs` | статус подписки и причина, по которой онлайн-запись выключена | 9 |
| `ModelValidationErrorFormatterTests.cs` | текст 400 автовалидации: известное поле, неизвестное поле, тело без имени поля | 7 |
| `TestSlotTests.cs` 🔬 | слоты тест-классов, переполнение за `c999` (закрытие находки T9 L5) | 9 |
| `TestDataTests.cs` 🔬 | уникальные значения на класс (переехали в TestKit) | 5 |

🆕 **Файлы цикла 4** (22 новых, суммарно ~273 запуска). ⚖️ Числа в этой таблице — **на момент цикла 4**;
шесть из них цикл 5 увеличил, актуальные значения — в блоке «Существенно выросли» ниже:

| Файл | Что покрывает | Запусков |
|---|---|---|
| `ChannelKeyFingerprintTests.cs` | отпечаток мастер-ключа, расхождение, подтверждение ротации | 18 |
| `NotificationGateTests.cs` | решение «слать или нет»: тариф, канал, тип, отписка, минимальный запас | 18 |
| `SecretProtectorTests.cs` | AES-GCM, AAD на id канала, порча шифротекста, чужой ключ | 15 |
| `GreenApiResultClassifierTests.cs` | постоянная ошибка vs временная, «нет WhatsApp» | 15 |
| `CompanyTimeZoneResolverTests.cs` | вывод зоны из города, ручное переопределение, смена города | 15 |
| `NotificationTemplateTests.cs` | рендер и валидация шаблонов, плейсхолдеры | 23 |
| `ChannelPresentationTests.cs` | русские формулировки состояния канала | 23 |
| `NotificationTimingTests.cs` | когда ставить напоминание, джиттер, границы | 19 |
| `NotificationTextsTests.cs` | русские формулировки журнала доставки | 12 |
| `GreenApiUrlsTests.cs` | сборка URL и `SafeLabel` (токен не должен попасть в лог) | 11 |
| `GreenApiWebhookParserTests.cs` | разбор вебхука статусов | 11 |
| `ChannelStateMapperTests.cs` | состояние провайдера → `ChannelState` | 10 |
| `CitySearchTests.cs` | нормализация поисковой строки (ё→е, дефисы) | 9 |
| `ChannelIdleCalculatorTests.cs` | расчёт простоя канала | 8 |
| `GreenApiStateInstanceParserTests.cs` | разбор ответа о состоянии экземпляра | 8 |
| `UnsubscribeTokensTests.cs` | подпись и разбор токена отписки | 8 |
| `ChannelPaymentStateTests.cs` | вычисление `ChannelPaymentStatus` | 5 |
| `PreferIPv4Tests.cs` | упорядочивание адресов, IPv4 первым | 5 |
| `ScheduledTaskOptionsTests.cs` | чтение подсекций новых задач | 5 |
| `PauseGeneratorTests.cs` | пауза 5–15 с между отправками | 4 |
| `PhoneDisplayMaskTests.cs` | маска номера для показа владельцу | 4 |
| `OptionalTests.cs` | «не прислали» vs «прислали null» | 3 |

⚖️ **Файлы цикла 5** (7 новых, плюс заметный рост существующих):

| Файл | Что покрывает | Запусков |
|---|---|---|
| `InnValidatorTests.cs` | контрольная сумма ИНН на 10 и 12 знаков | 12 |
| `TemplateAdHeuristicsTests.cs` | словарь рекламных маркеров в шаблоне | 7 |
| `WorkingDaysTests.cs` | срок ответа на обращение в рабочих днях | 7 |
| `ClientKeyTests.cs` | разбор `{clientKey}` — `userId` или `phone:…` | 6 |
| `HealthNoteProtectorTests.cs` | шифрование заметки о здоровье, чужой AAD, порча | 5 |
| `RetentionPlanTests.cs` | план уничтожения: какие правила на какие сроки | 5 |
| `AppLogAgeRuleTests.cs` | правило-растяжка по возрасту файлов логов | 4 |

Существенно выросли: `NotificationTemplateTests` (30, +31 строка кода на акцепт),
`NotificationGateTests` (29 — новая ветка `ProviderDeliveryConsentMode`),
`LegalDocumentProviderTests` (28 — пять документов, шесть текстов, `gate`, плейсхолдеры),
`ChannelPresentationTests` (25), `SecretProtectorTests` (18 — **строковая перегрузка AAD и
побайтовая проверка, что старые токены каналов читаются**), `LegalConsentFilterTests` (7 — области
блокировки), `PhotoQuotaTests` (8 — без `Forever`).

🔬 **Файлы цикла 8** (9 новых, суммарно **100** запусков — весь прирост набора). Это первое в
проекте покрытие **самой тестовой инфраструктуры**: раньше `ServiceBooking.Tests/Infrastructure/`
не был покрыт ничем, и его ошибки проявлялись как необъяснимо красный CI (см. §9, урок цикла 4,
пункт E). Тестируются `internal`-функции `ServiceBooking.TestKit` через
`<InternalsVisibleTo Include="ServiceBooking.UnitTests" />` в его `.csproj`:

| Файл | Что покрывает | Запусков |
|---|---|---|
| `TestDatabaseNamingTests.cs` | имена `sbtest_<ключ>_<слот>`, якорь регэкспа, отказ удалять чужую/не-одноразовую базу | 25 |
| `EnvStatusParallelConnectionBudgetTests.cs` | арифметика бюджета соединений и разбор `max_connections` (добавлено QA по итогам ревью — backend проверял её только руками) | 17 |
| `SweeperParseAgeTests.cs` | разбор `--max-age` (`30m`/`2h`/`1d`, отказ вместо «подмету всё») | 13 |
| `SweeperClassifyDatabaseRowTests.cs` | классификация базы «мёртвая / живая / неопределённая» | 10 |
| `EnvStatusDoctorPortsAndProjectNameTests.cs` | имя compose-проекта из каталога и «мой порт vs порт соседа» | 9 |
| `TestRunKeyTests.cs` | ключ прогона: формат 8 hex, переопределение переменной окружения | 9 |
| `ResourceLabelsTests.cs` | метки `com.servicebooking.test*` на контейнерах/базах | 7 |
| `EnvStatusClassifyContainerLivenessTests.cs` | «живой ли контейнер» | 5 |
| `StableHashTests.cs` | FNV-1a — стабильный между процессами хеш (семя случайного порядка тестов) | 5 |

| **Итого** | | 🗓 **777 запусков** (691 на конец цикла 8 → 711 после закрытия находок T9 → 777 после цикла 6) |

### 7.2 Функциональные (API) тесты — `ServiceBooking.Tests`

**Это тот набор, который QA прогоняет как базовый.**

- **Фреймворк:** xUnit 2.5.3 + FluentAssertions 6.12.1 + `Microsoft.AspNetCore.Mvc.Testing` 8.0.11,
  `Microsoft.NET.Test.Sdk` 17.8.0, `coverlet.collector` 6.0.0 (покрытие настроено, но нигде не собирается).
- **Характер:** поднимается **реальный HTTP-конвейер** приложения через
  `WebApplicationFactory<Program>` (`Infrastructure/CustomWebApplicationFactory.cs`, окружение `Testing`)
  и **реальная PostgreSQL-база**. Моков нет вообще.
- 🔬 **Изоляция — переделана целиком в цикле 8, это главное, что нужно знать про этот набор:**
  - **Свой Postgres на прогон.** `TestRunEnvironment` (один на процесс) поднимает контейнер
    `postgres:16-alpine` через Testcontainers на **динамическом** порту (`mode=container`) либо
    использует внешний сервер из `SERVICEBOOKING_TEST_CONNECTION` (`mode=external` — так работает
    CI). Контейнер живёт весь прогон; уборка привязана к `AppDomain.ProcessExit`, страховка —
    сторож Testcontainers (Ryuk) и `TestKit sweep`.
  - **Своя база на тест-класс.** `TestDatabaseFixture` стал `IClassFixture` (был `ICollectionFixture`):
    каждый класс берёт свой слот (`TestSlot.NextForClass()` → `c01`, `c02`, …), клонирует базу
    `sbtest_<ключ прогона>_<слот>` **из шаблона прогона** (`CREATE DATABASE … TEMPLATE`, ~0,2–0,4 с
    вместо полного прогона миграций) и дропает её, когда класс закончил. Одновременно живых баз —
    примерно «степень параллелизма + 1».
  - **Коллекции `"Api"` и `"NotificationDispatch"` распущены**, общих для набора баз
    `api`/`legal`/`dispatch` фазы 1 больше нет (имена оставлены зарезервированными).
  - **Файловые корни — под `$TMPDIR/sb-test/<ключ прогона>/<слот класса>/`** (`public`, `private`,
    `state`, `logs`, `legal`), а не в каталоге репозитория. Привязка к слоту **класса**, а не к типу
    фабрики: иначе 27 классов бывшей коллекции `"Api"` дрались бы за один временный каталог при
    копировании `App_Data/legal`.
  - **`EnsureDeletedAsync` удалён.** Снос базы делает только `TestDatabaseLease` и только через
    проверку имени `TestDatabaseNaming` — попытка удалить не-одноразовую базу даёт отказ
    `TestSafetyException`, ничего не удалив.
- 🔬 **Параллелизм включён: 4 потока локально, 2 в CI.** `ServiceBooking.Tests/xunit.runner.json`
  (`parallelizeTestCollections: true`, `maxParallelThreads: 4`); в CI — `-- xUnit.MaxParallelThreads=2`
  плюс `SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS=2` (второе значение видит только проверка бюджета
  соединений внутри процесса, реальный параллелизм задаёт первое — их надо держать одинаковыми).
  Единица параллелизма — класс; **внутри** класса тесты по-прежнему последовательны.
  **Откат — одна закомментированная строка** `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
  в `ServiceBooking.Tests/AssemblyInfo.cs`; больше ничего править не нужно.
- 🔬 **Порядок тестов внутри класса случайный** (`Infrastructure/RandomTestCaseOrderer.cs`, хеш
  FNV-1a из `TestKit.StableHash`) — чтобы вскрывать зависимости «тест B проходит только после теста
  A». Семя печатается один раз за прогон и повторяется переменной
  `SERVICEBOOKING_TEST_ORDER_SEED=<семя>`. Порядок **между** классами семенем не управляется.
- 🔬 **Fail-fast по бюджету соединений.** До первого теста считается
  `P × 2 хоста × PoolMaxSize(8) + 4` и сверяется с `max_connections` сервера (в контейнере поднят до
  300); не сходится — прогон падает сразу с тремя вариантами действий, а не «connection limit
  exceeded» на трёхсотом тесте. Тот же расчёт заранее показывает
  `TestKit doctor` (проверка `parallel-connection-budget`).
- 🔬 **Замеренный эффект (приёмка, `ARCHITECTURE_CYCLE8_PHASE2.md` §98.4):** локальный прогон
  **178 с → медиана 46,2 с** (~3,6× к последовательному); 13/13 зелёных прогонов (10 локально +
  3 в CI), ни одного `[Skip]`, ни одного теста, возвращённого в последовательный режим.
- **Дополнительные фабрики** (каждая поднимает **свой** хост поверх той же базы):
  - ⭐ `Infrastructure/RateLimitTestFactory.cs` — хост с жёсткими лимитами. Основная фабрика в
    `Testing` поднимает все `RateLimits:*` до 10000/мин именно для того, чтобы остальные сотни тестов
    никогда не упирались в лимит; тесты `SEC-` про 429 нуждаются в обратном.
  - ⭐ `Infrastructure/LegalDocumentsTestFactory.cs` — хост, смотрящий на **одноразовую копию**
    `App_Data/legal`, чтобы `LEG-`-тесты переписывали `legal.json`/HTML прямо на диске (существенная
    vs редакционная правка, «подменили файл — без пересборки»), не мешая остальным. `ReloadSeconds: 1`,
    чтобы не спать 30 секунд на каждую смену версии. 🆕 **Получила собственного суперадмина**
    (коммит `7a36543`) — см. §9 про урок общих ресурсов. ⚖️ **Переписана в цикле 5** под манифест из
    пяти документов и шести текстов.
  - ⚖️ `Infrastructure/ApiTestBase.cs` **вырос на +101 строку**: общие хелперы регистрации теперь
    строят новый объект `legal` с версиями, прочитанными у живого хоста. Это и есть причина, по
    которой функциональный набор «починился» почти механически — и одновременно причина, по которой
    он **не мог** поймать расхождение со смоук-скриптом (§9, L6).
  - 🆕 `Infrastructure/NotificationTestFactory.cs` — хост на тест для тестов каналов.
  - 🆕 `Infrastructure/NotificationTestBase.cs` — общий базовый класс (🔬 коллекции `"Api"` больше
    нет; идентичность суперадмина база берёт у фабрики, а не из константы).
  - 🆕 **`Infrastructure/NotificationDispatchTestFactory.cs` — первая в проекте тестовая
    инфраструктура с РЕАЛЬНО тикающим `ScheduledTaskRunner`.** До цикла 4 раннер в тестах был выключен
    целиком, и его собственное поведение (advisory lock, бюджет, частичный проход) проверить было
    нечем. Фабрика включает его обратно на своём хосте и подменяет три абстракции на
    записывающие/фейковые: `IDispatchDelay` → `RecordingDelay`, `INotificationClock` → `FakeClock`,
    `INotificationTransport` → `RecordingTransport` (регистрируются **после** `Program.cs` — побеждает
    последняя). Экземпляр **на каждый тест**, не общий. Реальный сетевой вызов через этот хост
    невозможен и без подмены — `Notifications:Provider=logging` уже это гарантирует; подмена нужна
    для наблюдаемости и мгновенности, а не для безопасности.
    🔬 Отдельной коллекции `"NotificationDispatch"` с `DisableParallelization = true` **больше нет**
    — цикл 8 распустил её: эти тесты изолированы собственной базой класса, как и все остальные.
- 🔬 **Строка подключения:** дефолта больше нет. `SERVICEBOOKING_TEST_CONNECTION` **переосмыслена
  как строка к СЕРВЕРУ**, а не к базе — компонент `Database` из неё всё равно переписывается на
  `postgres`, имена баз прогон выбирает сам. Если переменная не задана и Docker недоступен — прогон
  отказывается стартовать. Остальные переменные (все необязательные):
  `SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS`, `SERVICEBOOKING_TEST_ORDER_SEED`,
  `SERVICEBOOKING_TEST_RUN_KEY` (ровно 8 hex), `SERVICEBOOKING_TEST_NO_TEMPLATE` (аварийный
  выключатель клонирования), `SERVICEBOOKING_TEST_SEEDED_TEMPLATE`. `TESTCONTAINERS_RYUK_DISABLED`
  **запрещена** — `doctor` отдельно проверяет, что её не выставили.
- 🔬 **Общая настройка всех тестовых хостов — `Infrastructure/TestHostSettings.Apply`** (раньше ~60
  строк `UseSetting` копипастой в каждой фабрике). Там же — закрытый словарь суперадминов **на
  фабрику** (`api`, `ratelimit`, `ntf`, `uploads`, `dispatch`, `legal`): это прямое следствие урока
  цикла 4 (общий суперадмин решал за всех, с какой версией документов он согласен).
- 🔬 **Уникальные значения — `Infrastructure/TestData.cs`** (`fixture.Data.Phone()/Email()/Slug()/
  Name()/Dir()`): один механизм на класс вместо пяти приёмов вразнобой, слот класса зашит в каждое
  значение — по номеру телефона в логе видно, чей он.
- 🔬 **Что прогон печатает первыми строками:** `run=` (ключ прогона), `mode=container|external`,
  `server=`/`user=`, `parallel=`, семя порядка и пары «слот ↔ база». Машиночитаемая копия —
  `TestResults/sb-test-run.json` (пишется лучшим усилием, отсутствие файла прогон не роняет).
- **Окружение `Testing`** читает закоммиченный `appsettings.Testing.json`: планировщик фоновых задач
  **выключен**, лимиты загрузок и rate limiting подняты.
- **Хелперы:** `Infrastructure/ApiTestBase.cs`, `JsonHelpers.cs`, `TestImages.cs`.
- **Маркировка:** каждый тест помечен `[Fact, TestCase("PREFIX-NNN")]` — стабильный ID для
  перекрёстных ссылок из `TEST_CATALOG.md`. Поиск теста по ID: `grep -rn "BK-003" ServiceBooking.Tests/`.

| Файл | Префикс | `[Fact]` + `[InlineData]` |
|---|---|---|
| `Tests/CompaniesTests.cs` | `CO-` | 77 + 3 = 80 |
| `Tests/BookingsFlowSmokeTests.cs` | `BK-` | 54 |
| `Tests/AdminTests.cs` | `ADM-` | 42 |
| `Tests/ServicesTests.cs` | `SVC-` | 19 |
| `Tests/MastersTests.cs` | `MC-` | 19 |
| `Tests/ClientNotePhotosTests.cs` | `MC-` (продолжает нумерацию) | ⚖️ 21 (загрузка требует согласия) |
| `Tests/ProfileTests.cs` | `PROF-` | 18 |
| `Tests/WorkingHoursTests.cs` | `WH-` | 17 |
| `Tests/ScheduleTemplateTests.cs` | `ST-` | 15 |
| **`Tests/LegalConsentTests.cs`** ⭐⚖️ | `LEG-` (⚖️ **переписан целиком** под пять типов документов и раздельную блокировку) | ⚖️ 17 |
| **`Tests/LegalPriorityTests.cs`** ⚖️ | `LGL-` — **новый файл, инварианты правового контура** (см. ниже) | ⚖️ 9 |
| `Tests/AuthTests.cs` | `AUTH-` | 12 + 2 = 14 |
| `Tests/ReportsTests.cs` | `RPT-` | 13 |
| `Tests/ReviewsTests.cs` | `RV-` | 11 + 6 = 17 |
| **`Tests/DataRightsTests.cs`** ⭐ | `LEG-` (выгрузка и удаление аккаунта) | 10 |
| `Tests/MailingTests.cs` | `MAIL-` | 9 |
| `Tests/SchedulerTests.cs` | `SCH-` | 9 |
| **`Tests/PaginationTests.cs`** ⭐ | `PAG-` | 9 + 5 = 14 |
| **`Tests/LegalConsentVersionChangeTests.cs`** ⭐ | `LEG-` (смена редакции на живом хосте) | 7 |
| **`Tests/RateLimitingTests.cs`** ⭐ | `SEC-` | 6 |
| **`Tests/HealthTests.cs`** ⭐ | `OPS-` | 4 |
| **`Tests/IdentityRoleSyncTests.cs`** ⭐ | `SEC-` | 4 |
| `Tests/UploadsStaticFilesTests.cs` | — | 2 |
| 🆕 **`Tests/NotificationChannelsTests.cs`** | `NTF-C001…C018` (свой `NotificationTestFactory` на тест) | ⚖️ 20 (заявка требует ИНН и оферты) |
| 🆕 **`Tests/NotificationWebhookUnsubscribeTests.cs`** | `NTF-W*`, `NTF-U*`, `NTF-L*` | 9 |
| 🆕 **`Tests/NotificationCitiesTimeZoneTests.cs`** | `NTF-G001…G006` (🔬 бывш. коллекция `"Api"`) | 6 |
| 🆕 **`Tests/NotificationQueueingTests.cs`** | `NTF-Q001…Q004` (🔬 бывш. коллекция `"Api"`) | ⚖️ 5 (ветка `AccountsOnly`) |
| 🆕 **`Tests/NotificationDispatchExtraTests.cs`** | `NTF-D03…D05` (коллекция `"NotificationDispatch"`) | 3 |
| 🆕 **`Tests/NotificationDispatchTests.cs`** | `NTF-D01…D02` (коллекция `"NotificationDispatch"`) | 2 |
| 🗓 **`Tests/MultiServiceBookingTests.cs`** | `BK-060…BK-067` — визит из нескольких услуг (US-67) | 8 |
| **Итого** | | 🗓 **488 запусков** (было 465) |

🔬 **Все указания на коллекции в таблице выше — историческая справка.** Цикл 8 распустил обе
коллекции: каждый из 30 файлов теперь сам себе единица изоляции со своей базой и своим слотом.

🗓 **Что добавил цикл 6 (+23 запуска).** Один новый файл — `MultiServiceBookingTests.cs`
(`BK-060…BK-067`); остальное разошлось по существующим файлам: `AuthTests` (новые `AUTH-014`,
`AUTH-015` — 423 при блокировке, 400 на слабый пароль; переписан `AUTH-007` под настоящую семантику
lockout), `BookingsFlowSmokeTests`/`CompaniesTests`/`AdminTests` (`BK-068…BK-073`, `ADM-045` —
сетка переноса с `excludeBookingId` и проверка порядка «права → совпадение пары», окно по умолчанию
и `extendedHours`, признак «оказывает услуги», обязательная дата подписки). Попутно сделаны
детерминированными `NTF-Q003`/`Q004`, зависевшие от времени на часах машины.

⚖️ **`LegalPriorityTests.cs` — единственный новый функциональный файл цикла и самый важный для
понимания цикла.** Он проверяет не эндпоинты, а **инварианты**, которые иначе некому защитить:

- `LGL-073-DRY` — **сухой прогон не пишет в базу ничего**, и листание при этом завершается (то есть
  режим «только посмотреть» не зациклится на первом же батче);
- `LGL-073-OPTOUT` — **отписка переживает полный прогон уничтожения**: после того как отработали все
  тринадцать правил, строка `NotificationOptOut` на месте. Это прямая проверка того, что правила для
  неё не появилось «заодно»;
- `LGL-073-DELETE-HEALTH` / `-GUEST` — удаление заметки о здоровье и клиента, и гостя;
- `LGL-068-REVOKE-EFFECTS` / `-IDEMPOTENT` — что именно прекращается при отзыве согласия и что
  повторный отзыв ничего не ломает;
- `LGL-074-IDENTICAL` — ответ на обращение субъекта **одинаков** для известного и неизвестного
  системе телефона; `LGL-074-RATE` — лимит на форму обращений;
- `LGL-077-SUPERADMIN` — суперадмину отказано в доступе к противопоказаниям.

Человекочитаемое описание каждого кейса — в `TEST_CATALOG.md` (⚖️ ~276 КБ, русский), §10.4.
**Оговорка про `LEG-036`** (гонка одновременного принятия согласия): сторож **вероятностный** —
красноту подтверждали на 20 итерациях, в репозиторий закоммичен одиночный прогон (см. §9.15).

### Как запускать (для QA — базовый прогон)

🔬 **Фреймворк и команда базового прогона — xUnit 2.5.3 + `Microsoft.AspNetCore.Mvc.Testing`
(`WebApplicationFactory`) + реальный Postgres в Docker; команда — `dotnet test ServiceBooking.Tests`.**
🗓 Цикл 6 здесь ничего не изменил: та же команда, то же предусловие (Docker), те же правила изоляции.
💳 Цикл 7 — тоже. 📸 Цикл 10 — тоже.

💳 **Нюанс окружения, который стоил времени и который надо знать заранее (macOS + colima).** Если
Docker на машине поднят через **colima**, прогону нужны две переменные окружения:

```bash
export DOCKER_HOST=unix://$HOME/.colima/default/docker.sock
export TESTCONTAINERS_RYUK_DISABLED=true
```

⚠️ **Здесь противоречие в самой документации проекта, и его надо разрешать в пользу факта:**
`docs/testing-isolation.md` (строки 184 и 373) объявляет `TESTCONTAINERS_RYUK_DISABLED`
**запрещённой** — «это единственная уборка при аварийном завершении». На colima без неё прогон не
стартует, поэтому в цикле 7 её ставили. **Цена ровно та, о которой предупреждает
`testing-isolation.md`:** после прогонов на машине **остаются мёртвые контейнеры Postgres**, и
убирать их надо руками — `dotnet run --project ServiceBooking.TestKit -- sweep --apply`.
Про сам путь к сокету colima в `docs/testing-isolation.md` есть раздел «macOS + colima» (строка 394);
про вынужденный Ryuk — нет.
Отдельного e2e/браузерного набора (Playwright, Cypress и т.п.) в проекте **нет** — ни пакета в
`frontend/package.json`, ни каталога с такими тестами; «функциональные» здесь означает уровень API.
Единственная проверка против живого собранного образа — bash-скрипт `deploy/ci/smoke.sh` (запускает
CI-джоб `docker-build`, не тест-раннер).

```bash
# 🔬 Предусловие изменилось: нужен ЗАПУЩЕННЫЙ DOCKER, а не PostgreSQL.
# Ничего настраивать не надо — ни переменных окружения, ни строки подключения.
# Прогон сам поднимает Postgres на свободном порту, заводит свои базы и убирает их за собой.
# Проверить готовность среды, ничего не меняя и не удаляя:
dotnet run --project ServiceBooking.TestKit -- doctor     # 0 — ок, 2 — есть непройденные проверки

cd /Users/ikolomeets/RiderProjects/ServiceBooking3   # 💳 путь уточнён: каталог называется ServiceBooking3
dotnet build ServiceBooking.sln -warnaserror   # так же, как в CI
dotnet test ServiceBooking.UnitTests     # быстрый, без БД и без Docker — прогонять первым
dotnet test ServiceBooking.Tests         # основной функциональный набор, нужен Docker

cd frontend && npm ci && npm run lint && npx tsc --noEmit && npm run test:run
```

Полезное при разборе красного прогона (подробности — `docs/testing-isolation.md`):

```bash
SERVICEBOOKING_TEST_ORDER_SEED=<семя из лога> dotnet test ServiceBooking.Tests   # повторить порядок
dotnet test ServiceBooking.Tests -- xUnit.MaxParallelThreads=1                   # гонка или поломка?
dotnet run --project ServiceBooking.TestKit -- status            # что живо на машине
dotnet run --project ServiceBooking.TestKit -- sweep             # сухой прогон уборки, ничего не трогает
dotnet run --project ServiceBooking.TestKit -- sweep --apply     # удалить только мёртвое
```

💳 Числа приёмки **цикла 7** (на момент мерджа в `develop`): `dotnet build ServiceBooking.sln
-warnaserror` — **чисто**; `ServiceBooking.UnitTests` — **908**; `ServiceBooking.Tests` — **549**;
`npm run test:run` — **341**.
⚠️ **Чего в этих числах нет:** приёмочного теста миграции из `ARCHITECTURE_CYCLE7.md` §54.5 — он
**не написан** (§9 B3). Корректность самой опасной миграции подтверждена **не тестом**, а
экспериментами на данных и SQL-скриптами `deploy/checks/`. Для QA это значит: зелёный прогон
**ничего не говорит** о том, переживут ли миграцию реальные данные стенда.

🗓 Числа приёмки **цикла 6** (на слитом дереве ветки `cycle/06-booking-fixes`, коммит `aac6231`;
выполнял не автор этого документа): `dotnet build ServiceBooking.sln -warnaserror` — **0/0**;
`ServiceBooking.UnitTests` — **777/777**; `ServiceBooking.Tests` — **488/488** (по правилам цикла 8);
`npm run test:run` — **283/283** в 43 файлах; `npx tsc --noEmit` — чисто;
`npx @redocly/cli lint openapi-cycle6.yaml` — валидно, одно предупреждение про `localhost` в `servers`.

Числа приёмки цикла 8 (на ветке цикла, финальный коммит `691cf96`): `ServiceBooking.UnitTests` —
**691/691**; `ServiceBooking.Tests` — **465/465**, 13 зелёных прогонов подряд (10 локально + 3 в CI),
медиана 46,2 с; `npm run test:run` — **181/181**; `tsc --noEmit` — чисто; `npm run build` — успешно.

🆕 **Важно для QA, прогоняющего базовый набор:** сетевых вызовов наружу функциональный набор не
делает — `Notifications:Provider` в `Testing` остаётся `logging`, транспорт-заглушка. Ни одного
сообщения в WhatsApp при прогоне не уходит.

⚖️ **Второе важное для QA, появившееся в цикле 5:** задача уничтожения `data-retention` в окружении
`Testing` **выключена** (`appsettings.Testing.json`), как и три остальные фоновые задачи, а в
закоммиченной конфигурации она в любом случае работает **в сухом прогоне**. То есть базовый прогон
ничего не удаляет по срокам хранения; тесты, которым это нужно, дёргают задачу напрямую и двигают
фейковые часы.

Ожидаемый шум в выводе функционального набора, не являющийся сбоем:
`RequestSizeLimitFilter ... does not support IHttpRequestBodySizeFeature` (у `TestServer` нет этой
фичи — тесты на лимит размера это учитывают) и намеренный `DbUpdateException`/FK-нарушение из теста
на обработку ошибок.

Запуск подмножества:
```bash
dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~LegalConsentTests"
```

### 7.3 Тесты фронтенда — Vitest

- **Раннер:** Vitest 3.2, окружение `jsdom` 25, `@testing-library/react` 16 + `jest-dom` + `user-event`.
- **Конфиг:** `frontend/vitest.config.ts` (намеренно отдельный от `vite.config.ts`),
  `globals: false` (явные импорты `describe`/`it`/`expect`), setup — `src/test/setup.ts`.
- 📸 **Что добавил цикл 10 (+28, стало 369 тестов в 57 файлах):** `BookingHistoryPanel.test.tsx`,
  `components/company/CompanyPhotoGallery.test.tsx`, `pages/owner/CompanyPhotosSection.test.tsx`,
  `utils/companyManageError.test.ts` — новые; `BookingModal.test.tsx` переписан под объединённую
  модалку (сценарии персонала переехали туда из удалённого `ManualBookingModal.test.tsx`), капча
  вынесена в отдельный `BookingModal.captcha.test.tsx`, `BookingCalendar.test.tsx` дополнен режимом
  персонала (`staffMode`/`scheduleState`), `utils/uploadError.test.ts` — под русские тексты.
- 🗓 **Что добавил цикл 6 (+102, стало 283 теста в 43 файлах):** впервые покрыты **экраны самой
  записи**, которые до этого были в списке непокрытых годами. Новые файлы:
  `components/booking/BookingCalendar.test.tsx`, `ManualBookingModal.test.tsx`,
  `RescheduleModal.test.tsx`, `components/ui/PhoneInput.test.tsx`, `pages/LoginPage.test.tsx`,
  `pages/AdminPage.test.tsx`, `pages/owner/CompanyManagePage.test.tsx` и пять утилит
  (`bookingError`, `bookingHorizon`, `bookingServices`, `providesServicesError` — новые,
  `authError`/`phone` — существенно дополнены). Дополнен и `BookingModal.test.tsx`.
- ⚖️ **Что добавил цикл 5 (+81, 32 файла тестов):** впервые заметная доля — **экраны и формы, а не
  утилиты**: `pages/RegisterPage` (новый контракт согласий в форме регистрации),
  `pages/ConsentsPage`, `pages/SubjectRequestPage`, `components/booking/BookingModal`,
  `components/clientNotes/{HealthNoteCard, NotePhotoUploader, PhotoConsentBadge}`,
  `components/legal/OwnerTermsGateModal`, `components/notifications/ChannelRequestModal`,
  `pages/owner/{NotificationTemplatesTab, TemplateAcknowledgementModal}`,
  `hooks/usePhotoUploadWithConsent`, плюс утилиты `inn`, `legalSections`, `subjectRequestError`,
  `templateMarkers`. Претензия прошлой редакции «весь прирост — утилиты» к этому циклу **не относится**.
- **Что покрыто было к циклу 4 (100):** `utils/uploadError` (14), `utils/phone` (10), 🆕 `utils/notificationError` (9),
  🆕 `utils/channelBanner` (8), `utils/authError` (7), `utils/cancelError` (6), `utils/legalError` (6),
  `pages/MasterClientsPage` (6), 🆕 `utils/timezone` (5), `pages/LegalDocumentPage` (5),
  `components/clientNotes/PhotoGallery` (5), `components/ui/Pagination` (4),
  `components/legal/ConsentGate` (4), `components/legal/LegalUpdateBanner` (4),
  `hooks/useDebouncedValue` (4), `pages/CompanyPage` (3).
- 🆕 Весь прирост цикла 4 (+22) был утилитами; экраны уведомлений (`NotificationsSection` и три его
  вкладки, `QrModal`, `AssignCompanyDialog`, `RiskAcceptanceModal`, `NotificationsAdminTab`,
  `UnsubscribePage`, `CityCombobox`) так и **остались не покрыты** — цикл 5 их не трогал.
- ⚖️ Не покрыт и новый `pages/admin/SubjectRequestsTab.tsx` (админский журнал обращений).

### Чего в тестах НЕТ

- 📸 **Контракт цикла 10 (`contracts/cycle10/openapi.yaml`) в CI не проверяется** — ровно как
  контракты циклов 6, 7 и инвариант цикла 8. В `package.json` есть только скрипт генерации типов
  (`npm run types:api:cycle10`), шага в `ci.yml` нет: сверка формы ответов с контрактом остаётся
  ручной операцией (`.github/workflows/ci.yml` цикл 10 не трогал вообще).
- **Нет e2e-тестов через браузер.** Ни Playwright, ни Cypress. «Функциональные» здесь = API-уровень.
  Ближайшее к e2e — `deploy/ci/smoke.sh`: bash + curl против **живого контейнера** (health, регистрация,
  загрузка аватара), запускается CI-джобом `docker-build`, а не тест-раннером. 🔬 Цикл 8 этого не
  изменил; скрипт лишь перестал хардкодить порт (`BASE_URL=http://localhost:${SB_API_PORT:-5000}`).
- 🗓 **Контракт цикла 6 (`openapi-cycle6.yaml`) в CI тоже не проверяется.** Он валиден
  (`npx @redocly/cli lint` — одно предупреждение про `localhost` в `servers`), в
  `ARCHITECTURE_CYCLE6.md` §53 расписаны четыре готовых способа сверки (redocly lint, schemathesis
  против живого API, `openapi-diff` против Swagger приложения, `openapi-typescript` + `tsc`), но
  **шага в `ci.yml` нет**, пакеты в `package.json` не добавлены. То есть положение ровно такое же,
  как с инвариантом цикла 8 ниже, — второй машиночитаемый контракт и вторая ручная проверка.
- 📸 **Контракт цикла 10 (`contracts/cycle10/openapi.yaml`) — четвёртый в этом же положении**
  (см. врезку выше): типы из него генерируются, но ни генерация, ни lint в `ci.yml` не вызываются.
- 💳 **Контракт цикла 7 (`contracts/cycle7/openapi.yaml`) в CI не проверяется тоже** — третий
  машиночитаемый контракт и третья ручная сверка. Отличие от двух предыдущих: из него **реально
  генерируются типы фронтенда** (`npm run types:api`), но сам этот скрипт в CI не вызывается, так
  что расхождение контракта и `api-cycle7.generated.ts` никем не ловится (§1).
- 💳 **Нет приёмочного теста миграции биллинга** (`ARCHITECTURE_CYCLE7.md` §54.5). Причина
  техническая и её стоит знать, прежде чем браться: тестовая обвязка цикла 8 **всегда поднимает
  полностью смигрированную базу**, а этому тесту нужно состояние «до» — то есть нужна точка
  расширения в `TestDatabaseFixture`, которой нет. Самая опасная миграция проекта
  (`AddCoTenancyConstraints`) автотестами **не покрыта вообще**; вместо теста — SQL-скрипты
  `deploy/checks/` и ручные эксперименты на данных. См. §9 B3.
- 🔬 **Проверка HTTP-поверхности по OpenAPI-инварианту в CI НЕ автоматизирована.** Файл
  `contracts/cycle8/servicebooking-invariant.openapi.yaml` существует и описывает семь операций,
  которые цикл 8 обязан был не сломать, но запускается он **вручную**
  (`npx @redocly/cli lint …`, `pipx run schemathesis run … --base-url …`) — шага в `ci.yml` нет,
  пакеты в `package.json` не добавлены. Артефакты schemathesis добавлены в `.gitignore`
  (`.schemathesis/`), то есть инструментом пользовались локально.
- 🔬 **Сам `ServiceBooking.TestKit` покрыт юнит-тестами частично.** Покрыты чистые функции (имена,
  ключ, метки, бюджет, классификация, разбор `--max-age`); **не покрыты** пути, требующие Docker и
  живого Postgres: `TestServerLease`, клонирование/дроп в `TestDatabaseLease`, реальный
  `sweep --apply`. Они проверялись руками на приёмке.
- **Покрытие фронтенда остаётся точечным** — см. §9.18.
- 🚀 **Ручной чек-лист живых проверок появился** — `DEPLOY.md` §16, семь пунктов, все закрыты с
  датами и результатами (§10.4). Автотестами эти сценарии по-прежнему не покрыты.
- **Нет шага `dotnet format`** (`.editorconfig` есть, ESLint в CI есть) — см. §9.25.
- Не покрыты автотестами: капча с реальным ключом (в `Testing` `SmartCaptcha:SecretKey` пуст →
  валидация пропускается), миграции `NormalizePhoneNumbers`/`ResyncIdentityRoles` на боевом объёме
  данных, сами скрипты `deploy/backup/*`, `deploy/rollback.sh`, `deploy/monitor/*` (bash, тестов
  нет). 🚀 Последние три, в отличие от прошлой редакции, **прогонялись вручную на живой машине**
  2026-09-17 — это не покрытие тестом, но и не «никогда не запускалось».
- 🆕 **Реальный GREEN-API не проверялся ничем.** Адаптер покрыт юнит-тестами на разбор ответов и
  сборку URL, но живого вызова к провайдеру не делал ни один тест и ни один человек — партнёрского
  аккаунта нет. Привязка по QR, реальная доставка сообщения, реальный вебхук статусов и поведение при
  бане номера существуют только в виде кода и тестов против заглушек.
- 🆕 `TEST_CATALOG.md` содержит **явный раздел «Не покрыто функциональными тестами этого прогона»** —
  это зафиксированный, а не скрытый пробел.
- ⚖️ **Прицельно не покрыто циклом 5** (зафиксировано, не блокер): акцепт владельцем при сохранении
  шаблона; эвристика рекламных маркеров на уровне API; поля подтверждения полномочий при записи за
  другого человека; админский журнал обращений субъектов; полнота истории согласий в выгрузке.
- ⚖️ **Смоук-тест против собранного образа поймал то, чего не увидели 465 функциональных тестов.**
  Ломающее изменение контракта регистрации не довели до `deploy/ci/smoke.sh`, потому что
  функциональные тесты **строят запрос из текущего кода**, а скрипт носил свою захардкоженную копию
  тела запроса. Разошлись — и никакой тест этого увидеть не мог. Чинили уже на `develop`, отдельным
  коммитом (`aecddb1`); заодно скрипт перестал хардкодить версии документов и **читает их из
  `/api/legal/documents` того самого образа**, что делает проверку строго сильнее прежней. Класс
  расхождений, который ловится только прогоном против настоящего образа, — см. §9.

---

## 8. CI и деплой

📸 **Цикл 10 не изменил здесь ничего.** В диапазоне `bd3be3f..242c7d9` нет ни одного изменения в
`.github/workflows/**`, `deploy/**`, `docker-compose*.yml` и `Dockerfile`; новых проверок fail-fast
прод-конфига цикл тоже не добавил (`Retention:BookingEventDays: 0` старт намеренно **не** роняет,
§4.18).

⚠️ 📸 **Что важно знать при следующем выкате (риск R6 из `ARCHITECTURE_CYCLE10.md` §116).** В
`develop` накопилось **13 невыкаченных миграций**: одиннадцать из цикла 7 (включая необратимую
`AddCoTenancyConstraints` и четыре разрушительных по данным) и две добавочные из цикла 10. Миграции
применяются **автоматически на старте приложения**, поэтому на релизе они поедут **все разом**;
накатывать их надо **целиком и в порядке**, по процедуре цикла 7 (предпроверка
`deploy/checks/billing-precheck.sql` → миграции → `billing-migration-check.sql`, шаг **10.2a**
`DEPLOY.md`). Две миграции цикла 10 сами по себе безопасны и обратимы — опасен по-прежнему блок
цикла 7, на котором деплой уже падал (ниже и §9 B1).

🗓 **Цикл 6 не изменил здесь ничего.** В диапазоне `14a7fb6..aac6231` нет ни одного изменения в
`.github/workflows/**`, `deploy/**`, `docker-compose*.yml` и `Dockerfile`. Единственное, что стоит
держать в голове при выкатке: цикл добавил **четыре миграции**, одна из которых переписывает данные
(`BackfillSubscriptionPaidUntil`, §3), а миграции применяются **автоматически на старте**
приложения. И вторая: fail-fast прод-конфига получил новую проверку —
`DeploymentSafetyChecks.ParseDefaultWorkWindow` роняет старт при отсутствующем, неразбираемом или
перевёрнутом `Booking:DefaultWorkWindow` (в закоммиченном `appsettings.json` значение есть).

### CI — есть (`.github/workflows/ci.yml`)

Появился в цикле 1, расширен в 2 и 3. Триггеры: push в `master`, `release-candidate`, `develop`
и любую ветку по маске `cycle/**`, плюс **любой**
pull request. `concurrency` с `cancel-in-progress`, у каждого job'а `timeout-minutes: 15`.

**Три независимых job'а:**

| Job | Что делает |
|---|---|
| `backend` | сервис-контейнер `postgres:16` c health-check; 🔬 `SERVICEBOOKING_TEST_CONNECTION` указывает на **сервер** (`Database=postgres`), базы прогон заводит свои — Docker-in-Docker не нужен; 🔬 шаг «Derive test run key» складывает `GITHUB_RUN_ID` с хешем `GITHUB_JOB` в 8 hex (`SERVICEBOOKING_TEST_RUN_KEY`), чтобы два джоба одной сборки не столкнулись на одинаковых именах баз; 🔬 `SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS=2`; кеш `~/.nuget/packages`; `dotnet restore` → **`dotnet build … -c Release -warnaserror`** → `dotnet test ServiceBooking.UnitTests` (быстрый, без БД, идёт первым) → 🔬 `dotnet test ServiceBooking.Tests --no-build -c Release -- xUnit.MaxParallelThreads=2` → 🔬 `bash deploy/ci/check-image-pins.sh` |
| `frontend` | Node 20 c npm-кешем; `npm ci` → ⭐ **`npm run lint`** (ESLint) → `npx tsc --noEmit` → `npm run test:run` → `npm run build` (с `VITE_SMARTCAPTCHA_SITEKEY` из **переменной репозитория**, не секрета — site-ключ публичен) → ⭐ **выгрузка артефакта `frontend-dist-<sha>`** (только для `master`/`release-candidate`/`develop` — веток, с которых деплоят; retention 30 дней) |
| `docker-build` | ⭐ теперь **не только собирает, но и запускает**: `docker build` → поднимает `postgres:16-alpine` в отдельной docker-сети → запускает образ с `ASPNETCORE_ENVIRONMENT=Production` и полным набором переменных из `DEPLOY.md` → `deploy/ci/smoke.sh` → `docker logs` при любом исходе |

Про `docker-build` важны две вещи, обе записаны комментариями прямо в workflow:
- **Запуск в `Production` — намеренный.** Это одновременно проверка, что fail-fast
  (`DeploymentSafetyChecks`) удовлетворяется **ровно тем** набором переменных, который описан в
  `DEPLOY.md` и `.env.production.example`: добавили новую обязательную переменную и забыли про
  документацию — job краснеет. 🆕 Цикл 4 добавил туда шесть обязательных `NOTIFICATIONS_*`
  переменных — этот механизм их и держит.
- **`deploy/ci/smoke.sh` гоняет реальную загрузку изображения** по HTTP в живой контейнер, то есть
  проверяет, что `SkiaSharp.NativeAssets.Linux.NoDependencies` действительно грузится на glibc-базе
  и что файл потом реально отдаётся. Ни `dotnet build`, ни `dotnet run` этого поймать не могут.
  ⚖️ **Цикл 5 научил скрипт новому контракту регистрации** (`legal: { privacyAcknowledgedVersion,
  termsAcceptedVersion }` вместо `acceptedLegal: true`) и заодно сделал его строже: версии больше не
  захардкожены, а **читаются из `GET /api/legal/documents` этого же образа**, с явной проверкой, что
  `Privacy` и `TermsClient` в манифесте вообще присутствуют. До этой правки смоук краснел — и это
  единственная проверка во всём проекте, которая поймала расхождение (§7, §9).
  Скрипт запускается и руками: `BASE_URL=http://localhost:${SB_API_PORT:-5000} deploy/ci/smoke.sh`.
  В самом `ServiceBooking.API/Dockerfile` вверху стоит предупреждение: **не менять тег на `-alpine`**.

🔬 **Цикл 8 добавил в `backend` пятый шаг — `deploy/ci/check-image-pins.sh`:** быстрый bash,
краснеющий, если мажорная версия образа Postgres разошлась между
`ServiceBooking.TestKit/TestInfrastructure.cs`, `docker-compose.yml`, `docker-compose.prod.yml` и
самим `ci.yml`. Тестов у самого скрипта нет.

Чего в CI нет: `dotnet format --verify-no-changes` (см. §9), сбора покрытия, 🔬 проверки
OpenAPI-инварианта (`@redocly/cli lint` / `schemathesis` из `contracts/cycle8/` — только вручную),
**авто**деплоя —
`ci.yml` только проверяет и складывает артефакт фронта. Деплой — отдельные workflow, запускаемые
человеком кнопкой (ниже).

### 🚀 Деплой по кнопке из GitHub Actions — есть и выполнялся

Два отдельных workflow (`workflow_dispatch`, вручную):

| Workflow | Куда | Гварды |
|---|---|---|
| `.github/workflows/deploy-staging.yml` | стенд, ветка `develop` | `environment: staging`, без обязательных ревьюеров — стенд должен катиться быстро |
| `.github/workflows/deploy-production.yml` | прод, **только тег на `master`** | `environment: production` с **Required reviewers**; отдельный job `guard`: ref должен быть тегом, тег должен лежать на `master`, в поле подтверждения должно быть введено ровно `deploy`. Все три отказа проверены живьём 2026-09-17: `guard: failure`, `deploy: skipped`, до SSH исполнение не доходит |
| 💳 `.github/workflows/rollback-staging.yml` | стенд, **аварийный откат** | `workflow_dispatch`, `environment: staging`, `concurrency: deploy-staging` (общая с деплоем, `cancel-in-progress: false`), `timeout-minutes: 10`. Новых секретов не заводит — те же `DEPLOY_SSH_KEY`/`DEPLOY_USER`/`DEPLOY_HOST` |

💳 **Почему `rollback-staging.yml` появился именно в цикле 7 (коммит `0929b48`, последний в
диапазоне).** Комментарий в самом workflow объясняет прямо: `deploy-remote.sh` при провале проверки
готовности **только печатает подсказку про откат — и никогда его не выполняет**. Деплой, оставивший
API посреди миграции или в крешлупе, остаётся в этом состоянии, пока откат не запустит человек.
Workflow дёргает ту самую команду `rollback`, которая уже была в allowlist'е
`deploy/ssh-deploy-wrapper.sh`. Это **прямое следствие упавшего деплоя цикла 7** (см. ниже и §9 B1).

Облачный раннер GitHub подключается по SSH на 22-й порт под отдельным системным пользователем
**`ezbookdeploy`** (`DEPLOY.md` §8). Три независимых рубежа, ни один не полагается на остальные:
отдельный непривилегированный пользователь (не root, не личный, пароль заблокирован);
**`command=` в `authorized_keys`**, указывающая на `/usr/local/sbin/ezbook-deploy-wrapper.sh`
(копия `deploy/ssh-deploy-wrapper.sh`, владелец root, сам пользователь её не отредактирует) с
**закрытым** allowlist'ом `upload-release`/`deploy`/`rollback`/`health` и `*) refuse` в конце;
точечный `sudo` ровно на `nginx -t` и `systemctl reload nginx`. Секреты приложения (`.env`) в
GitHub **не заводятся** — в секретах только `DEPLOY_SSH_KEY`, `DEPLOY_USER`, `DEPLOY_HOST`
(+ переменные `DEPLOY_HOST_KEY`, `VITE_SMARTCAPTCHA_SITEKEY`).

🚀 **Форс-команда приводит рабочее дерево к коммиту принудительно** (коммит `88f1ed2`):
`git checkout --force` + `git clean -fd` (без `-x`, поэтому `.env` и `legal/` не трогаются никогда),
с печатью `git status --short` отбрасываемого **до** переключения. Причина: прежний `git checkout`
падал от любой ручной правки или постороннего файла в `/opt/ezbook/app`, причём уже **после**
заливки фронтенда, оставляя машину в промежуточном состоянии. Следствие, зафиксированное в
`DEPLOY.md`: **любая правка прямо в `/opt/ezbook/app` не переживёт следующий деплой** (§9).

### 💳 Состояние стенда после цикла 7 — деплой упал и откачен

⚠️ **Это самое важное в разделе для того, кто придёт следующим.** Код цикла 7 влит в `develop`, но
**на стенде его нет**: деплой **упал на миграциях**, и стенд **откачен на предыдущий релиз**.
Расхождение `develop` ↔ стенд сейчас — весь цикл 7.

Что известно про причину и что было (и не было) сделано:

- Упало на миграционном блоке цикла 7 — 11 миграций, среди них необратимая
  `AddCoTenancyConstraints` (`NOT NULL` + альтернативные и составные ключи, §3).
- **Предпроверка `deploy/checks/billing-precheck.sql` на данных стенда не прогонялась.** Скрипт
  существует и специально сделан запускаемым **до** появления колонок цикла 5 (`57bf611`, находка
  ревью B7), но его никто не выполнил против стенда перед выкатом.
- **Шаг `10.2a` в `DEPLOY.md`** (строка 742, «Проверки перед выкатом миграции биллинг-аккаунтов»)
  описан как **ручная процедура и в автоматику деплоя не встроен**. То есть ничто в пайплайне не
  заставляет прогнать предпроверку — её можно молча пропустить, что и произошло.
- **Требуется ручной разбор на сервере.** Автоматического пути «накатить ещё раз и всё получится» нет.
- Второй скрипт, `deploy/checks/billing-migration-check.sql`, предназначен для сверки **после**
  миграции; он тоже не отработал, потому что до него не дошло.

Порядок, в котором это задумывалось (`ARCHITECTURE_CYCLE7.md` §54.1/§54.5): `billing-precheck.sql`
на данных стенда → миграции → `billing-migration-check.sql`. Фактически выполнен только средний шаг.

### Деплой — выполнен вживую; целевая машина

**Проект развёрнут и работает: `https://ezbook.ru`, TLS от Let's Encrypt (certbot --nginx).**
💳 ⚠️ **С поправкой выше: на стенде работает предыдущий релиз, без цикла 7.**
Runbook: `DEPLOY.md` (~111 КБ) — 🚀 **переписан целиком** под фактическую машину; прежняя редакция
описывала AlmaLinux-VPS на reg.ru, которого в итоге не было. Windows/IIS-контур
(`DEPLOY-windows.md`) **выведен из скоупа** решением заказчика, но из репозитория не удалён (§9.13).

🚀 **Целевая машина — не VPS, а десктоп в квартире** (`DEPLOY.md`, «Целевая машина»):

| Параметр | Значение |
|---|---|
| ОС | Ubuntu 24.04 **desktop** (GNOME/GDM), не серверная установка |
| ОЗУ | **3,3 ГиБ**, свободно ~1 ГБ, **492 МиБ уже в подкачке** |
| Диск | один раздел `/dev/sda4`, 210 ГБ, ~180 ГБ свободно, **шифрования (LUKS) нет** |
| Сеть | локальный `192.168.0.93` за домашним роутером **D-Link DIR-615** со статическим белым адресом, проброс 80/443 |
| Соседи на машине | **чужое `fleetservice.service`** (системный `python3`, слушает `0.0.0.0:8443`) — трогать нельзя; GDM/gnome-remote-desktop (3389/3390), CUPS (631) |
| Файрвол | `ufw` включён; разрешены 22, 80, **443** (открыт 2026-09-16), 8443, 3389, 3390 |
| Каталоги | код — `/opt/ezbook/app` (клон `develop`), релизы фронта — `/var/www/ezbook/releases/<ts>` + симлинк `current`, бэкапы — `/var/backups/servicebooking` (root, 0700) |

Роутер **не разворачивает трафик на себя (нет hairpin NAT)** — машина не видит себя по публичному
имени. Это ломало отправку событий в трекер молча (Sentry-клиент не падает, он теряет события).
Обойдено двумя способами: `extra_hosts: errors.ezbook.ru:host-gateway` в `docker-compose.prod.yml`
(для контейнера) и строка в `/etc/hosts` (для скриптов на хосте).

**Что нашлось и было исправлено при развёртывании** — девять дефектов, каждый отдельным коммитом:

| Что было сломано | Симптом | Коммит |
|---|---|---|
| раздача `/uploads` шла из `wwwroot`, а запись — в `Storage:PublicRoot` | на чистом клоне каталога нет → всё отдавалось 404 навсегда | `3ec5dc8` (до диапазона) |
| `.dockerignore` вообще не был в репозитории (его прятал `.gitignore`) | в образ попадали локальные загрузки, исключение секретов не работало | `909dcc3` (до диапазона) |
| `try_files … /index.html` в `location /embed/` | внутренний редирект пересопоставлялся с `location /` → `/embed/` получал `X-Frame-Options: DENY` и `frame-ancestors 'none'`; **виджет не встраивался вообще, на 100% запросов**. Починено именованным `location @embed_fallback` | `017567b` |
| в стеке GlitchTip не было сервиса миграций | стек «поднимается успешно», логин отдаёт 500 `relation "users_user" does not exist` | `ec1fe75` |
| basic-auth стоял на всём поддомене, включая пути приёма событий | приложение получало 401 на каждое событие, панель выглядела как «ошибок нет» | `71c449a` |
| оба стека делили compose-проект `app` | контейнеры `postgres` схлопнулись, GlitchTip остался без своей базы; `down` одного стека снёс бы контейнеры другого | `125a72b` |
| машина не видела себя по публичному имени | события, алерты бэкапа и монитора терялись молча | `f1be54e` |
| деплой падал от любого постороннего файла в рабочем дереве | обрыв посреди прогона, уже после заливки фронта | `88f1ed2` |

Первые два дефекта исправлены **до** коммита из шапки прошлой редакции и в §8/§9 уже были описаны;
здесь они перечислены рядом, потому что относятся к одной находке — «на чистой машине не работает
то, что на машине разработчика работало».

- `docker-compose.prod.yml`: `postgres` (порт наружу не публикуется) + `api` на `127.0.0.1:5000`,
  два named volume — `api_uploads` → `/app/wwwroot/uploads` и `api_private_uploads` →
  `/app/private-uploads` (**персональные данные, бэкапить отдельно**), плюс ⭐ **bind-mount
  `./legal:/app/App_Data/legal:ro`** поверх черновика, запечённого в образ: правовые тексты меняются
  на хосте **без пересборки и без релиза** (`DEPLOY.md` §2.1). Health-check контейнера смотрит на
  `/api/health/live`, готовность (`ready`) проверяет скрипт деплоя.
  🆕 **Цикл 4 добавил сюда шесть `Notifications__*` переменных** (`EncryptionKey`, `KeyRotationAck`,
  `Provider`, `PartnerToken`, `WebhookToken`, `UnsubscribeKey`) и **записываемый** bind-mount
  `./state:/app/App_Data/state` (в отличие от `./legal`, который `:ro`). Именно туда приложение
  пишет отпечаток мастер-ключа и сверяется с ним на каждом старте. Named volume здесь **не годится
  намеренно**: `deploy-remote.sh` пересоздаёт контейнер на каждом деплое. Каталог `./state`
  (0700) должен существовать **до** первого `docker compose up`; `/state/` добавлен в `.gitignore`
  тем же правилом, что `/legal/` и `.env`.
  ⚖️ **Цикл 5 добавил сюда одну переменную — `RETENTION_DRY_RUN`** (`ScheduledTasks__data-retention__DryRun`,
  по умолчанию `true`). Форма та же фейл-сейфная, что у `NOTIFICATIONS_PROVIDER`: пусто/не задано =
  запечённое в образ безопасное умолчание, то есть **сухой прогон**. Чтобы задача начала удалять,
  оператор обязан выставить `false` осознанно и по процедуре из `DEPLOY.md` §11.4 (сначала сухой
  прогон, разбор журнала, потом боевой).
- `deploy/nginx/ezbook.conf`: статика из `/var/www/ezbook/current` (симлинк на релиз), прокси `/api/`
  и `/uploads/` на `127.0.0.1:5000`, `client_max_body_size 6M`, TLS через `certbot --nginx`.
  `/swagger/` не проксируется. Заголовки: на уровне `server` — `Strict-Transport-Security`,
  `X-Content-Type-Options`, `Referrer-Policy`; в `location /` они **повторены намеренно** (nginx не
  наследует `add_header` между уровнями — об этом есть комментарий прямо в конфиге) плюс
  `X-Frame-Options: DENY` и полный `Content-Security-Policy` с исключениями под SmartCaptcha и
  `img-src … blob:` под приватные фото и выгрузку. `location /embed/` вынесен **отдельно и намеренно
  без анти-фрейминга** — виджет для того и существует, чтобы его встраивали; 🚀 его fallback теперь
  уходит в **именованный** `location @embed_fallback` (`rewrite ^ /index.html break`), а не в
  `try_files … /index.html`, потому что последний — внутренний редирект, который nginx
  пересопоставляет с `location /` и оттуда притаскивает framing-заголовки (коммит `017567b`).
  Обе стороны проверены живьём 2026-09-17: на `/` framing-заголовки строгие, на `/embed/test` их
  нет, HSTS/`nosniff`/`Referrer-Policy` есть везде.
  🆕 **Цикл 4 добавил маскирование токенов в access-логе nginx.** Два маршрута несут секрет прямо в
  пути: `/api/notifications/provider-webhook/{token}` (токен вебхука) и
  `/api/notifications/unsubscribe/{token}` (из него восстанавливается телефон клиента, то есть это
  ПДн, а не только креденшл). nginx пишет путь в лог **до** того, как запрос дойдёт до приложения,
  поэтому маскирование на стороне .NET его не покрывает. Сделано `map` + отдельный `log_format`
  (`notifications_masked`) и два отдельных `location` с тем же `proxy_pass` — **не** `access_log off`:
  выключение стёрло бы коды ответа и тайминги вебхука, а это единственный сигнал, когда сбоит сам
  GREEN-API.
- ⭐ **Деплой больше не собирает фронт на боевом сервере.** `deploy/deploy.sh` (на машине
  разработчика, требует `gh auth login`) скачивает артефакт `frontend-dist-<sha>`, который CI собрал
  **для этого же коммита**, кладёт его на машину новым каталогом `/var/www/ezbook/releases/<ts>/` и
  вызывает `deploy/deploy-remote.sh`. Тот тегирует текущий образ API как `previous`, запоминает
  текущий релиз, **атомарно переключает симлинк `current`**, пересобирает и перезапускает контейнер,
  **ждёт `/api/health/ready`** (а не «контейнер стартовал»), и если готовность не наступила —
  печатает следующей строкой готовую команду отката.
- ⭐ **Откат одной командой без аргументов:** `bash deploy/rollback.sh` — возвращает фронт на
  предыдущий релиз (или на указанный timestamp) и образ API на тег `previous`. Прямо в шапке скрипта
  записано, чего он **не** делает: **не откатывает миграцию БД** (они применяются на старте и
  необратимы) — на этот случай в `DEPLOY.md` §8 есть отдельный раздел.
- ⭐ **Бэкап:** `deploy/backup/backup.sh` + `servicebooking-backup.{service,timer}` — ежесуточный
  `pg_dump` и снимок обоих файловых хранилищ, **с хоста, а не из контейнера** (должен работать, когда
  приложение лежит), в `/var/backups/servicebooking` — **вне** docker-томов, чтобы
  `docker compose down -v` не унёс копии вместе с оригиналом. Хранение: 7 суточных + 4 недельных
  (воскресные). Перед стартом проверяет свободное место (1,5× от прошлого набора).
  🆕 **В набор добавлен файл отпечатка ключа шифрования** (`notifications-key-fingerprint-<ts>.txt`)
  — он обязан ехать **в том же наборе, что и `.env`**: если восстановленный `.env` попадёт на машину
  без файла отпечатка, приложение просто запишет новый на первом старте, и защита «этот `.env` не от
  этой базы» **молча перестанет работать**. 🆕 `DEPLOY.md` §11.2 теперь **описывает восстановление
  `.env` и отпечатка** (шаг 3b) — прежняя претензия к раннбуку снята, но копия всё так же локальная
  (§9).
  ⚖️ **Цикл 5 переписал шаг 3b в развилку.** Оператору теперь сначала предлагается решить, **на какой
  машине он находится** (та же или новая), и только потом выполнять ровно один из двух вариантов.
  Причина названа в раннбуке прямо: тем же ключом `NOTIFICATIONS_ENCRYPTION_KEY` с цикла 5 зашифрованы
  **не только токены WhatsApp-каналов, но и противопоказания клиентов всех салонов** — пропуск шага
  там, где он обязателен, это не мелкая неаккуратность процедуры, а потеря медицинских данных.
  ⚖️ Появился **§11.2b — обязательный шаг после восстановления: повторно применить удаления**,
  выполненные по запросам субъектов уже после снятия восстанавливаемой копии.
  ⚖️ Процедура **ротации ключа теперь начинается с `ClientHealthNotes`**, а не с каналов.
  ⚖️ Появился **§11.4** — уничтожение по срокам хранения, с обязательным сухим прогоном первым.
  ⚠️ **В копию входит и `.env`** (шаг 3b скрипта, файл `env-<ts>.txt`, права 0600) — без него дамп
  базы и тома бесполезны. Но: `DEPLOY.md` §11.1 в списке «что делает каждый прогон» этот шаг **не
  называет** (перечислено 6 шагов из 7), процедура восстановления §11.2 `.env` **не восстанавливает**,
  а комментарий в самом скрипте ссылается на «DEPLOY.md, инвентарь секретов» — раздела с таким
  названием в `DEPLOY.md` **нет** (§9.2 и §9.3).
  **Копия локальная, внешней нет** — см. §9.2 и §9.3. Восстановление прогонялось на этой машине 2026-09-17
  (заметка с фото → бэкап → удаление → восстановление → фото открывается); попутно исправлены две
  ошибки самой инструкции: имена томов (`app_api_*`, а не `ezbook_api_*` — при неверном имени docker
  молча создаёт пустой том и восстановление «успешно» ничего не восстанавливает) и требование
  выполнять шаги из-под `sudo -i`.
- ⭐ **Мониторинг:** `deploy/monitor/health-alert.{sh,service,timer}` — systemd-таймер на том же
  хосте дёргает `/api/health/ready`, после трёх подряд неудач шлёт письмо. Таймер поднят
  (`DEPLOY.md` §12.9). Ограничение прежнее и записано в раннбуке: **если вся машина недоступна,
  алерта не будет** — монитор крутится на проверяемой машине.
  `docker-compose.glitchtip.yml` — self-hosted **GlitchTip** (говорит по протоколу Sentry, поэтому
  синк `Sentry.Serilog` в API менять не нужно): четыре контейнера, отдельный стек со своими
  postgres/redis, наружу только через nginx. Выбран вместо self-hosted Sentry осознанно (~20
  контейнеров и 16 ГБ RAM против ~1 ГБ) — обоснование в шапке файла. 🚀 **Поднят и работает** на
  `errors.ezbook.ru`; появился одноразовый сервис `migrate` (схему БД не создаёт больше никто —
  ни `web`, ни `worker`), `web` стартует только после его успешного завершения, и стеку задано
  явное `name: glitchtip`. Отдельно в чек-листе зафиксировано: письмо из GlitchTip приходит на
  адрес **учётной записи в панели**, а не на ящик-отправитель; без правила оповещения в проекте
  события копятся, а письма не уходят.

**Fail-fast прод-конфигурации** живёт теперь в `Services/DeploymentSafetyChecks.cs` (вынесен из
`Program.cs` ради тестируемости — чистые статические методы, ⚖️ **84 юнит-теста**). В окружении Production
приложение **не стартует**, если `Jwt:Key` пуст/короче 32 символов/равен плейсхолдеру; если
`SuperAdmin:Password` пуст или равен `Admin12345`/`CHANGE_ME`; если `Storage:PrivateRoot` резолвится
внутри `wwwroot`; ⭐ если `Storage:PrivateRoot` резолвится внутри фактического `Storage:PublicRoot`
(или совпадает с ним); ⭐ если сам `Storage:PublicRoot` резолвится в content root приложения или выше
него (см. §5); ⭐ если не настроен `ForwardedHeaders:TrustedNetworks` (иначе rate limiting по IP
считал бы всех за один адрес docker-бриджа). Предупреждение без падения — `SuperAdmin:Phone` по
умолчанию. **На `isDraft` в `legal.json` fail-fast намеренно нет** (§9.7).

🆕 **Три новые проверки цикла 4**, все выполняются **до `Build()`** (чистые проверки конфигурации и
файловой системы, без DI):
- `ValidateNotificationSecrets` — при `Notifications:Provider=green-api` в Production обязаны быть
  заданы `EncryptionKey`, `PartnerToken`, `WebhookToken`, `UnsubscribeKey`; **нераспознанный
  `Provider` роняет старт всегда**. Часть правил работает даже в Development.
- `ValidateChannelKeyFingerprint` — сверяет отпечаток текущего мастер-ключа с файлом в
  `Notifications:KeyFingerprintPath`; **расхождение = отказ старта**, осознанная ротация
  подтверждается переменной `NOTIFICATIONS_KEY_ROTATION_ACK`. Единственное некритичное предупреждение
  этой проверки пишется **минимальным bootstrap-логгером Serilog** (консоль, JSON), потому что
  полный конвейер логирования на этот момент ещё не построен — раньше тут был `Console.WriteLine`,
  который не доходил ни до файла, ни до GlitchTip.
- `ValidateTimeZoneDatabase` — резолвит `Asia/Barnaul` (намеренно не более снисходительный
  `Europe/Moscow`); отсутствие tzdata в образе станет пойманным отказом деплоя, а не загадкой в
  рантайме. Параллельно в `Dockerfile` **явно доустановлен `tzdata`** — не потому, что базового
  образа не хватает сегодня, а чтобы не зависеть от того, что Microsoft его не вырежет.

⚖️ **Три новые проверки цикла 5**, все — **безусловные, без послабления для Development**
(это проверки согласованности конфигурации, а не секретов, и ошибиться в них на ноутбуке так же
неправильно, как в проде):
- `ValidateProviderDeliveryConsentMode` — `Notifications:ProviderDeliveryConsent` обязан быть одним из
  `Strict`/`AccountsOnly`/`Off`; **нераспознанное значение роняет старт**, потому что это правовой
  гейт, а не косметическая настройка.
- `ValidateGreenApiServerCountry` — если создание экземпляров у провайдера включено
  (`GreenApi:InstanceCreationEnabled`), **страна сервера обязана быть задана**: «пусть решит
  провайдер» не является допустимым умолчанием для требования локализации данных.
- `ValidateRetentionPeriods` — `Retention:TemplateHistoryDays` ≥ **365** и
  `Retention:ConsentRecordDays` ≥ **1095**. Это юридические минимумы: «не меньше трёх лет» не должно
  зависеть от того, кто последним правил `appsettings.Production.json`.

⭐ **До коммита `909dcc3` файла `.dockerignore` в репозитории вообще не было** — он сам был
перечислен строкой в `.gitignore` и потому никогда не коммитился; любой чистый клон собирал образ
БЕЗ единого исключения, включая исключение секретов (`appsettings.Development/Production.json`).
Это важнее конкретного списка паттернов ниже, но для полноты: теперь `.dockerignore` закоммичен и
исключает `appsettings.Development.json`/`appsettings.Production.json`, оба каталога загрузок
(`ServiceBooking.API/wwwroot/uploads/**`, `ServiceBooking.API/App_Data/private-uploads/**`), рантайм-логи
Serilog (`ServiceBooking.API/logs/**`), артефакты тестовых прогонов (`**/TestResults`) и `frontend/`
(фронтенд собирается отдельной джобой CI, в образ API не входит).

**Секреты:** `.gitignore` исключает `**/appsettings.*.json` (кроме базового и `Testing`), `.env`,
`.env.production`, `.deploy.env` и ⭐ `/legal/` (каталог оператора на машине; в git лежит только
черновик `ServiceBooking.API/App_Data/legal/`). В git закоммичен только
`ServiceBooking.API/appsettings.json` с плейсхолдерами. **Но локально на машине разработчика лежат
незакоммиченные `appsettings.Development.json` и `appsettings.Production.json` с настоящими
секретами** (боевой пароль Postgres, JWT-ключ, серверный ключ SmartCaptcha) — их нельзя случайно
`git add -f`. 🚀 Появился второй носитель боевых секретов — **`.env` в `/opt/ezbook/app` на самой
машине** (`POSTGRES_PASSWORD`, `JWT_KEY`, `SUPERADMIN_PASSWORD`, `SMARTCAPTCHA_SECRET_KEY`, `SENTRY_DSN`,
`GLITCHTIP_*`, 🆕 `NOTIFICATIONS_ENCRYPTION_KEY`, `NOTIFICATIONS_KEY_ROTATION_ACK`,
`NOTIFICATIONS_PROVIDER`, `NOTIFICATIONS_PARTNER_TOKEN`, `NOTIFICATIONS_WEBHOOK_TOKEN`,
`NOTIFICATIONS_UNSUBSCRIBE_KEY`). В GitHub он не попадает никогда; в локальный бэкап попадает; вне машины
существует только в менеджере паролей оператора (§9.3).
🆕 ⚠️ **`NOTIFICATIONS_ENCRYPTION_KEY` — не рядовой секрет.** Остальные значения можно
перевыпустить (`openssl rand`), заплатив разлогиниванием пользователей или перевыпуском ключа во
внешнем кабинете. Этот — **нет**: он расшифровывает токены WhatsApp-экземпляров **чужих салонов**,
его потеря необратимо уносит все подключённые каналы. Поэтому в `DEPLOY.md` он вынесен **отдельной
строкой** инвентаря секретов, а не в общий список ротируемых.
⚖️ **С цикла 5 цена его потери выросла ещё раз:** тем же ключом зашифрованы **противопоказания
клиентов всех салонов** (`ClientHealthNotes`). Ротация ключа в раннбуке теперь **начинается именно с
этой таблицы**, а не с каналов. В `.env.production.example` добавлена ещё одна, не секретная
переменная — `RETENTION_DRY_RUN` (по умолчанию `true`).

---

## 9. Технический долг и риски (по убыванию приоритета)

⚠️ **Нумерация сместилась относительно прошлой редакции.** Блок P0 переписан целиком (было 4
пункта, стало 9), поэтому все последующие пункты сдвинулись **на +5**: бывший §9.5 стал §9.10,
бывший §9.25 — §9.30. Ссылки внутри этого документа обновлены; ссылки из чужих документов на §9.N
надо перечитывать с учётом сдвига.
🚀 — пункт появился, переформулирован или закрыт по итогам первого реального развёртывания
(`6369266..0e61369`).

🆕 **Нумерация 1–30 ниже СОХРАНЕНА от прошлой редакции** — цикл 4 добавил свой блок с буквенными
номерами (P0-A…P0-E), чтобы ссылки вида «§9.17» из других документов остались валидными.
⚖️ **Цикл 5 поступил так же** — его блок идёт ниже с номерами L1…L6, нумерация 1–30 и P0-A…P0-E
не тронута.
🔬 **Цикл 8 поступил так же** — его блок идёт с номерами T8-1…T8-8. Нумерация 1–30,
P0-A…P0-E и L1…L6 не тронута; ⚠️ обратите внимание, что буквы `L` и `M` внутри блока цикла 8 — это
**номера находок ревью фазы 2**, а не пункты L1…L6 цикла 5, это разные пространства имён.
🗓 **Цикл 6 — тоже**: его блок идёт с номерами C1…C5, остальная нумерация не тронута.
💳 **Цикл 7 — тоже**: его блок идёт **первым**, с номерами **B1…B11**, остальная нумерация не
тронута. ⚠️ Осторожно: буквой `B` в этом же документе помечены **находки ревью цикла 7** (B1…B13,
они же в текстах коммитов вида `fix(billing): … (B5, B6)`) — это **другое** пространство имён.
Пункты долга ниже — `§9 B1`…`§9 B11`; находки ревью — просто `B5` без `§9`.

---

📸 **Цикл 10 — тоже**: его блок идёт **первым**, с номерами **D1…D6**, остальная нумерация не
тронута.

---

**📸 D — долг, ограничения и сознательные решения цикла 10 (ручная запись, журнал, фото салона)**

**D1. Срок хранения журнала изменений не задан — правило зарегистрировано и ничего не удаляет.**
`Retention:BookingEventDays: 0` в `appsettings.json`; `BookingEventRule` читает ноль как «срок не
настроен» и честно пишет это в сводку прогона. Ждёт решения юриста: в журнале лежат **ФИО
сотрудников** (снимок имени и роли автора действия) — это ПДн работника, и «пусть лежит вечно» здесь
не ответ. Когда срок появится, вся починка — **одно число в конфигурации**: ни миграции, ни правки
кода. Пока число не задано, `BookingEvents` растёт без ограничения срока.

**D2. Согласие на публикацию фото с изображениями людей не собирается — решение заказчика, а не
недоделка.** Экрана и чекбокса в потоке загрузки нет, `ConsentRecord` на фото салона не пишется
(П7). Обязанность соблюдать ст. 152.1 ГК при этом остаётся на владельце компании, который загружает
фото. **Открытый вопрос к юристу:** покрыт ли этот случай действующим пользовательским соглашением
(«владелец подтверждает правомерность загружаемого контента»), и если нет — нужна ли туда отдельная
строка. Это правка документа, а не кода.

**D3. Три операции с фотографиями делят один бюджет политики `uploads`.** Загрузка, удаление и
перестановка порядка все помечены `[EnableRateLimiting("uploads")]` — бюджет **общий, 10 запросов в
минуту**. Практическое следствие: владелец, только что загрузивший десять фото, выбирает бюджет
целиком и **на первом же клике по стрелке порядка получает 429 с текстом про загрузки**, который
про перестановку ничего не говорит. Поведение соответствует контракту (429 у удаления и
перестановки описан, `89d30ab`), поэтому записано как долг, а не как баг.

**D4. Косметика в недостижимой сегодня ветке `DayOff && staffMode` в `BookingCalendar`.** В одном
сочетании условий ячейка дня может выглядеть кликабельной, не будучи ею. Сегодня эта комбинация
недостижима (сервер для персонала возвращает такие дни как `Available` + `scheduleState: DayOff`),
но ветка в коде есть — и станет видимой, если сервер когда-нибудь начнёт отдавать персоналу
`status: DayOff`.

**D5. Четыре уязвимости в npm-зависимостях фронтенда** — `axios` (high), `form-data` (high),
`react-router` (moderate ×2). **Существовали до цикла 10**, цикл их не трогал и не усугубил;
чинятся патч-версиями, ломающих обновлений не требуют.

**D6. Невыкаченных миграций в `develop` стало 13** (11 из цикла 7 + 2 из цикла 10) — риск R6 из
`ARCHITECTURE_CYCLE10.md` §116. Применять на релизе целиком и по порядку; подробности и процедура —
§8 и §9 B1. Сами миграции цикла 10 добавочные и обратимые.

---

**💳 B — долг, ограничения и сознательные решения цикла 7 (биллинг-аккаунты и тарифы)**

**B1. Деплой на стенд упал на миграциях; стенд откачен, разбор — ручной.** ⚠️ Самый срочный пункт
всего раздела. Код в `develop`, на стенде — предыдущий релиз. Предпроверка
`deploy/checks/billing-precheck.sql` **на данных стенда не прогонялась**; шаг **10.2a** `DEPLOY.md`
(строка 742) описан как **ручная процедура и не встроен в автоматику деплоя**, поэтому пропустить
его ничего не мешает. Требуется ручной разбор на сервере. Подробности — §8. Смягчающее: в этом же
цикле появился `rollback-staging.yml`, дающий аварийный откат кнопкой (раньше
`deploy-remote.sh` откат только **печатал**, но не выполнял).

**B2. Заявки на опции — поля на биллинг-аккаунте, а не отдельная сущность.** `RequestedPlanId`,
`RequestedOptionsJson`, `RequestedAtUtc`, `RequestedByUserId`, `RequestedComment`,
`LastRejectionReason`, `LastRejectedAtUtc` лежат прямо на `BillingAccount` (§3). Следствия, каждое
из которых всплывёт:
- **Истории отклонённых и одобренных заявок не существует** — хранится только «последняя причина
  отказа», и та перезаписывается.
- **Запрос очереди `GET /api/admin/subscription-requests` по любому статусу, кроме ожидающих,
  всегда возвращает пустой список.** Это не баг в фильтре — данных просто нет.
- Заявка у аккаунта может быть **только одна**; повторная подача затирает предыдущую.
Выделение заявки в отдельную сущность — отдельная работа с миграцией.

**B3. Приёмочный тест миграции (`ARCHITECTURE_CYCLE7.md` §54.5) не написан.** Причина не в спешке,
а в устройстве обвязки: тестовый фикстур цикла 8 **всегда поднимает полностью смигрированную базу**,
а тесту нужно состояние «до миграции». Нужной точки расширения в `TestDatabaseFixture` нет.
Вместо теста миграция проверена **экспериментами на данных и сверочными скриптами**
`deploy/checks/`. ⚠️ Самая опасная миграция проекта (`AddCoTenancyConstraints`, необратимая)
автотестами **не покрыта**. См. также §7 «Чего в тестах НЕТ» и B1 — пункты связаны.

**B4. Логика статуса подписки существует в двух экземплярах — на C# и продублированная внутри
SQL-запроса.** `BillingCalculator` считает статус в C#; `AdminBillingController` (строка ~212)
**повторяет ту же таблицу истинности прямо в SQL**, потому что **EF Core не транслирует вызов
общего метода** в выражение запроса. Комментарий в коде это фиксирует честно.
Эквивалентность двух реализаций держится **одним тестом**, сверяющим статус из списка аккаунтов со
статусом из карточки аккаунта. ⚠️ Любая правка правил статуса должна менять **оба** места; тест
поймает расхождение, но только если правило вообще проявляется в обоих представлениях.

**B5. Тип поля ссылки на биллинг-аккаунт в коде допускает пустое значение, обязательность держит
только БД.** `Guid? BillingAccountId` в `Company` (стр. 56), `AccountSubscription` (18),
`SubscriptionChangeLog` (22), `NotificationChannel` (22) — при `NOT NULL` в схеме после
`AddCoTenancyConstraints`. Следствие: **ветки обработки `null` в коде недостижимы** — мёртвый код,
который выглядит живым, и компилятор требует его писать. Приведение типа к `Guid` — отдельная
правка модели, в цикле не делалась.

**B6. Пять тестов вокруг системного бесплатного тарифа зависят от общего нетранзакционного
состояния базы и прибираются за собой вручную.** Это **нарушение конвенции изоляции цикла 8** (§6):
остальной набор полагается на одноразовую базу и откат, а эти пять — на ручную уборку.
⚠️ Практический риск: при падении в середине такой тест **оставляет базу грязной** для соседей по
классу, и симптом проявится не там, где причина.

**B7. Маршрут переключения системного бесплатного тарифа не гарантирует инвариант «ровно один».**
После снятия флага система может жить **без системного бесплатного тарифа сколь угодно долго** —
промежуточного состояния никто не запрещает и не чинит автоматически. Инвариант держится
дисциплиной администратора, а не кодом.

**B8. Публикация цен наружу заблокирована — и это не техническая недоделка.** Рубильник
`pricing.public-enabled` выключен по умолчанию, `GET /api/pricing` отвечает 404 с пустым телом,
пока суперадмин его не включит. Причина — **вычитка юристом, требование `SPEC_CYCLE7_PRICING.md`
§7**. Витрина, тизер на главной и ссылка в навигации написаны, протестированы и готовы; включать
их **нельзя до юридической вычитки**. Не «чинить».

**B9. WhatsApp-уведомления остаются невыпущенными (US-74).** Опция **создана в каталоге**
(`SeedBillingCatalog`), но **цена не задана** и опция **непубличная** (`IsPublic = false`);
`Notifications:Provider` остаётся `logging`. «Опция никому не предлагается» — **ожидаемое
состояние**, а не дефект. Состояние тянется с цикла 4 (§4.17, §5.1 п. 3) и циклом 7 не изменено,
а лишь перенесено в новую модель.

**B10. Коллизия имён документов, которую пришлось разруливать по ходу цикла.** Документы этого
цикла изначально назвали по нумерации **пятого** цикла — и они столкнулись с уже существующими
документами правового цикла 5. Переименованы в **седьмой слот**, при этом **переписано 173 ссылки**.
Следы коллизии в истории остались: коммиты `9bb8ec7`, `02c6404`, `8774466`, `de2ecbf` и другие
говорят «cycle-5 / цикл 5», хотя это цикл 7 — **читать их по диапазону `aac6231..0929b48`, а не по
тексту сообщения**. Урок на будущее, который стоит знать: **сквозная нумерация разделов в проекте
не уникальна между циклами** (§43, §54 и т. п. существуют в нескольких `ARCHITECTURE_CYCLE*.md`
одновременно), и **ссылки опираются на имя файла**. Ссылка вида «§54.5» без имени файла
неоднозначна; писать надо «`ARCHITECTURE_CYCLE7.md` §54.5».

**B11. Окружение тестов на машине разработки: colima требует запрещённой документацией
переменной.** Нужны `DOCKER_HOST=unix://$HOME/.colima/default/docker.sock` и
`TESTCONTAINERS_RYUK_DISABLED=true`, при том что `docs/testing-isolation.md` (строки 184, 373)
объявляет вторую **запрещённой**. Цена ровно та, о которой там предупреждают: **после прогонов
иногда остаются мёртвые контейнеры Postgres**, убираются вручную через
`dotnet run --project ServiceBooking.TestKit -- sweep --apply`. Документация и фактическая практика
разошлись — расхождение не устранено ни в ту, ни в другую сторону. Подробности — §7.

---

**🗓 C — долг и сознательные упрощения цикла 6 (сценарий записи)**

**C1. Раскладки выручки по услугам нет.** Визит из нескольких услуг вносит в выручку **один**
вклад (`Booking.Price`), а «топ услуг» считает **вхождения** услуг, не деньги. Поэтому вопрос
«сколько принесло окрашивание против стрижки» отчётами сейчас не закрывается. Это **осознанное
упрощение цикла 6**, а не дефект: поделить цену визита между услугами честно нельзя без правила
про скидки на комплекс, а такого правила у заказчика ещё нет. Если раскладка понадобится — это
отдельная работа (данные для неё уже лежат: `BookingServices.Price` хранит цену каждой услуги на
момент записи).

**C2. `/embed/:slug` сознательно остался на одной услуге за визит.** Мультивыбор (US-67) в виджет
не выведен: он живёт в чужом iframe неизвестной высоты. Расхождение поведения основного сайта и
виджета — **принятое**, а не забытое; см. §4.11.

**C3. Шаг сетки слотов по-прежнему 30 минут — цикл 6 этого не трогал.** Пункт §9.22 остаётся
открытым, и после US-67 он стал заметнее: визит из нескольких услуг легко даёт нестандартную
суммарную длительность (45, 75, 105 минут), которая ложится в получасовую сетку с неожиданным для
пользователя результатом. Цикл добавил расчёт суммарной длительности, но не сделал сетку от неё
зависимой.

**C4. Часовых поясов в ядре расписания по-прежнему нет — цикл 6 этого не трогал.** Пункт §9.12
без изменений: `Booking.Date/StartTime/EndTime` — `DateOnly`/`TimeOnly` без зоны, новый
`GET /api/bookings/availability` и `Companies.BookingHorizonDays` считают горизонт **от
`DateOnly.FromDateTime(DateTime.UtcNow)`**, то есть от UTC-даты, а не от даты в зоне компании. Для
российских зон (UTC+2…+12) это значит, что на границе суток «сегодня» у сервера и у пользователя
может отличаться на день.

**C5. 🧠 Урок цикла про архивирование документов — конвенция §10.5 требует уточнения.**
Спека цикла 6 была заархивирована в `SPEC_CYCLE6_BOOKING_FIXES.md` (потому что `SPEC.md` занят
циклом 8), **но 43 ссылки вида «`SPEC.md` §0.1» в коде и документах цикла остались нетронутыми**.
`SPEC.md` — это теперь спека цикла 8, и у неё **есть свой §0.1 и свой Q7**. То есть читатель,
идущий по ссылке из комментария в `SlotCalculator.cs`, попадал в связный, аккуратный и совершенно
чужой текст про make-цель. **Это хуже битой ссылки**: битая видна сразу, а эта молча вводит в
заблуждение. Исправлено коммитом `aac6231` (ссылки цикла 6 перенаправлены на его спеку; ссылки
других циклов не тронуты — их `SPEC.md` означал своё). **Вывод для конвенции: переименование спеки
обязано включать обновление ссылок на неё в том же коммите**, и это должно быть записано в §10.5
явно, а не подразумеваться.

---

**🔬 P0-цикл-8 — что оставил после себя цикл изоляции тестовой среды**

Цикл 8 **закрыл** главный пункт долга прошлой редакции (§9.24d и урок P0-цикл-4 E — «прогон молча
уничтожает и пересоздаёт разделяемый ресурс»): общей базы `servicebooking_test` больше нет,
`EnsureDeletedAsync` удалён, два прогона на одной машине проверены на приёмке и не мешают друг
другу. Ниже — то, что он оставил.

**T8-1. 🔴 Фиксированные `Task.Delay(1200)` в тестах правовых документов — самая хрупкая точка
набора.** `ARCHITECTURE_CYCLE8_PHASE2.md` §98.5 (находка ревью L8, подтверждённая прогоном):
тесты `LegalConsentVersionChangeTests` синхронизируются **фиксированными снами**, неявно
привязанными к `Legal:ReloadSeconds=1`. Работает, пока машина не занята. Из **одиннадцати** прогонов
приёмки одно падение (1 тест из 465) случилось именно в прогоне, шедшем **одновременно со вторым
прогоном** на той же машине; повтор с тем же семенем в тишине — зелёный, то есть причина не в
порядке тестов, а в компрессии времени. Следствие: **порог времени US-101 замерен на незанятой
машине и на занятой не гарантируется**; прогон тестов хочет машину в своё распоряжение — изоляция
даёт независимость данных, но не независимость от нехватки CPU. Правильное лечение — заменить сны
на **ожидание условия** («дождись версии X» с опросом), это около десятка мест и работа отдельного
цикла, к изоляции отношения не имеющая.

**T8-2. Неблокирующие находки ревью фазы 2 — ЗАКРЫТЫ** коммитом `ee9f61b` (ветка
`cycle/09-test-env-followups`, влита в `develop` 2026-09-22, CI зелёный). Таблица ниже описывала их
состояние на `6562a86` и оставлена как история находок; после каждой — что с ней сделали.

Итог по закрытию:
- **M2** — перед `DROP DATABASE` пул сбрасывается, точечно по строке подключения класса. `ClearAllPools`
  из буквального текста §91.5 п. 3 применять было нельзя: он снёс бы пулы соседних классов, идущих
  параллельно. Документ поправлен под то, что верно.
- **M3** — имя тест-класса пишется вторым `COMMENT ON DATABASE`, когда базовый класс впервые узнаёт
  свой тип, и выводится в `sweep --json`. Проверено на реальной осиротевшей базе: `"testClass": "AdminTests"`.
- **M4** — формула сведена в одно место (`EnvStatus`), `TestRunEnvironment` зовёт её. Обоснование
  переписано: `PoolMaxSize=8` — потолок **на класс**, потому что пул Npgsql ключуется строкой
  подключения; множитель «2 хоста» оставлен как запас и назван запасом.
- **M7** — проверено делом, `kill -9` посреди прогона: в container-режиме Ryuk убирает за ~15 с, в
  external-режиме база и шаблон не убираются **никогда** (на SIGKILL `ProcessExit` не вызывается).
  Срок уборки подметальщиком снижен 2 ч → 30 мин. Остаточный разрыв: `sweep --apply` нигде не
  вызывается по расписанию — на эфемерных раннерах GitHub это не нужно (всё умирает с машиной), но
  для self-hosted или общего сервера Postgres уборку придётся завести.
- **L1, L2, L3, L5, L6** — закрыты: аварийный режим больше не сериализует миграции; семафоры
  окружения и дропа разведены; бюджет проверяется один раз на процесс, `int.Parse` → `TryParse`;
  переполнение слота за `c999` падает с внятным сообщением; приёмочный греп P1 заякорён на
  `^\[assembly` и больше не спотыкается о закомментированную строку отката.
- Юнит-покрытие 691 → **711**. В том числе закреплено утверждение, на котором держится защита от
  сноса чужой базы: регэксп принимает классовые слоты `c01…c999` и отвергает `c1000`. До этого оно
  жило только в комментарии.

Историческая таблица находок (состояние на `6562a86`):

| № | Что | Где | Почему это важно / чем компенсировано |
|---|---|---|---|
| **M2** | Перед `DROP DATABASE` пул Npgsql **не сбрасывается**: контракт `ARCHITECTURE_CYCLE8_PHASE2.md` §91.5 п. 3 предписывает «диспознуть хост → `NpgsqlConnection.ClearAllPools()` → `DROP … WITH (FORCE)`», в коде `ClearAllPools` нет нигде — только `WITH (FORCE)` | `ServiceBooking.TestKit/TestDatabaseLease.cs` (`DropUncheckedAsync`), `Tests/Infrastructure/TestDatabaseFixture.cs` | На практике `WITH (FORCE)` сам отстреливает чужие сессии, поэтому не падает. Но контракт **не соблюдён буквально**, и если `FORCE` когда-нибудь уберут, дроп начнёт падать на живых соединениях пула |
| **M3** | Связка «слот ↔ тест-класс» описана и в контракте, и в JSON-схеме (`testClass`), но `TestClass` **везде передаётся `null`** (три места: `EnvStatus.cs:512`, `Sweeper.cs:265`, `Sweeper.cs:449`) | `ServiceBooking.TestKit/` | По повисшей базе `sbtest_<ключ>_c07` **невозможно узнать, чей это класс** — ровно в тот момент, когда это нужно. Причина техническая: xUnit v2 не передаёт фикстуре тип её класса; пара логируется тестом, но в метку не попадает |
| **M4** | Бюджет соединений считается как `P × 2 хоста × 8` (`RequiredConnections`, 68 при P=4), хотя **оба хоста класса ходят по одной строке подключения и делят один пул Npgsql** | `TestKit/EnvStatus.cs`, `Tests/Infrastructure/TestRunEnvironment.cs` (формула **продублирована** в двух местах) | Ошибка в безопасную сторону — оценка завышена (замер на приёмке дал пик 58–59 при расчётных 68). Но **обоснование числа 8 не соответствует модели**, поэтому и объяснение в сообщении об ошибке вводит в заблуждение |
| **M7** | Контейнер убирается только по `AppDomain.ProcessExit`; при `kill -9` в режиме `external` базы живут до `--max-age` (2 ч) | `Tests/Infrastructure/TestRunEnvironment.cs` | Компенсировано Ryuk'ом (контейнеры) и `TestKit sweep` (базы), но это **ручной шаг**, а не автоматическая уборка |
| **L1** | Аварийный режим `SERVICEBOOKING_TEST_NO_TEMPLATE=1` прогоняет миграции каждого класса **внутри общего семафора клонирования** (`CloneGate`), то есть сериализует миграции всех классов | `TestKit/TestDatabaseLease.cs` (`CreateClassDatabaseAsync`) | Аварийный режим станет ещё и очень медленным ровно тогда, когда им придётся воспользоваться |
| **L5** | Слот в JSON-схеме ограничен `^(template\|api\|legal\|dispatch\|c[0-9]{2,3})$`, а `TestSlot.NextForClass()` формата `c{N:D2}` рано или поздно выдаст `c1000` | `contracts/cycle8/testkit-status.schema.json` vs `Tests/Infrastructure/TestSlot.cs` | Практически недостижимо (29 тест-классов), но `--json` перестанет соответствовать собственной схеме без предупреждения. `TestDatabaseNaming` (защита от сноса) при этом шире и не сломается |
| **L6** | Приёмочный греп № P1 (`ARCHITECTURE_CYCLE8_PHASE2.md` §99.5) ожидает **пусто** от `grep -rn "DisableTestParallelization" ServiceBooking.Tests/AssemblyInfo.cs`, а там теперь лежит **закомментированная** строка отката | `ServiceBooking.Tests/AssemblyInfo.cs` | Греп как приёмочная проверка сломан: он краснеет на сознательно оставленном механизме отката. Чинить надо греп, а не код |

**T8-3. Ограничение среды: macOS + colima.** Testcontainers ищет сокет Docker в
`/var/run/docker.sock`, colima его там не создаёт, и подъём падает на Ryuk
(`invalid mount config for type bind …`). Лечится **двумя переменными, без отключения Ryuk**:
`DOCKER_HOST=unix://$HOME/.colima/default/docker.sock` и
`TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`. На Docker Desktop и в CI они не нужны
и ничего не меняют. ⚠️ Rider, запущенный из Dock/Finder, переменные из `~/.zshrc` **не наследует** —
их надо задавать в конфигурации запуска IDE. Записано в `docs/testing-isolation.md`.

**T8-4. Защита от разрушительных действий работает по ИМЕНИ базы, а не по адресу сервера.** Если
подсунуть в `SERVICEBOOKING_TEST_CONNECTION` боевой хост, прогон создаст там свои `sbtest_*` и
удалит только их — боевая база уцелеет, но мусор и нагрузка на боевом сервере появятся. Именно
поэтому прогон печатает `server=`/`user=` первой строкой.

**T8-5. OpenAPI-инвариант есть, но в CI не проверяется.** `contracts/cycle8/servicebooking-invariant.openapi.yaml`
— первый машиночитаемый контракт API в репозитории, но запускается только вручную
(`@redocly/cli`, `schemathesis`); шага в `ci.yml` нет, пакетов в `package.json` нет. Схема покрывает
**семь** операций, а не весь API, и правило приоритета в ней — «расхождение схемы и кода означает,
что сломан код» — верно только для цикла 8 и требует пересмотра, как только API начнут менять.

**T8-6. Покрытие самого TestKit неполное.** Чистые функции покрыты (100 новых юнит-тестов), но пути,
требующие Docker и живого Postgres (`TestServerLease`, клонирование и дроп в `TestDatabaseLease`,
реальный `sweep --apply`), проверялись **только руками на приёмке**. Ошибка в них проявится как
загадочно красный прогон — тот самый класс дефектов, ради которого цикл затевался.

**T8-7. Правка боевого кода за цикл — две строки смысла, обе в `Program.cs`, обе стоит знать.**
(1) каталог логов стал конфигурируемым (`Logs:Directory`, дефолт `"logs"` — поведение
Development/Production дословно прежнее); (2) в окружении **`Testing`** Serilog больше не
захватывает процессный `Log.Logger` (`preserveStaticLogger: true`), а `UseSerilogRequestLogging`
получил **явный** `opts.Logger` из DI этого хоста. Вне `Testing` поведение не меняется, потому что
там хост в процессе всегда один. ⚠️ Это значит, что **окружение `Testing` теперь ведёт себя иначе,
чем прод, в одном конкретном аспекте** — логирование; если когда-нибудь тест начнёт проверять
поведение статического логгера, он будет проверять не то, что работает на проде.

**T8-8. 🧠 Процессный урок цикла.** Дважды за цикл **два агента работали в одном git-чекауте
одновременно** и затирали друг другу незакоммиченные правки (след — коммит `500362d` «Salvage the
sweeper work the network outage interrupted»). Вывод, применимый к любому следующему циклу:
**отдельный `git worktree` на роль**. Сам цикл 8 это уже использовал на приёмке — второй
одновременный прогон гнали из `git worktree add --detach`.

---

**⚖️ P0-цикл-5 — что цикл 5 закрыл, что переформулировал и что осталось**

**L1. Функция уведомлений по-прежнему НЕ выпущена, и правовая часть закрыта не полностью.** Цикл 5
снял главный блокер прошлой редакции — «правовая оценка по 41-ФЗ не проведена»: заключение сделано
(`LEGAL_REVIEW.md`), и 41-ФЗ к платформе **не применяется**. Но выпуск блокируют пять вещей, ни одна
из которых не решается кодом:
  1. **уведомление в Роскомнадзор** об обработке персональных данных не подано;
  2. **договоры поручения с салонами** не заключены;
  3. **партнёрского аккаунта GREEN-API нет** — реального экземпляра не создавалось ни разу
     (перешло из цикла 4 без изменений);
  4. **вопрос практикующему юристу** — вправе ли платформа привлекать обработчика в выбранной схеме —
     не задан и не закрыт;
  5. **пятнадцать видов плейсхолдеров** в двенадцати документах юриста не заполнены (§5.1 п. 7):
     часть требует данных ЕГРЮЛ, часть появится только после уведомления РКН, часть — решения заказчика.

**L2. P0-3 (бэкап и `.env`) закрыт НЕ ПОЛНОСТЬЮ, а переформулирован — и на его месте новая дыра.**
Что закрыто: бэкапы работают и **проверены**; проблема была не в них, а в **закомментированном шаге
раннбука**, который теперь стал развилкой (§8). Что **открылось**: 🔴 **восстановление базы из копии
воскрешает данные, которые были удалены по запросу субъекта** — а журнал обращений и журнал отзывов
согласий лежат **в том же самом дампе**, то есть восстанавливаются вместе с воскрешёнными данными и
не могут служить независимым доказательством того, что удаление было. Временная мера — ручной
обязательный шаг `DEPLOY.md` §11.2b «повторно применить удаления после восстановления». Настоящее
решение — **журнал удалений, живущий вне базы**, — в этом цикле не делалось и переходит в следующий.

**L3. Правовые тексты — каркас, а не готовые документы.** Двенадцать документов написаны и увязаны с
механикой (версии, хеши, гейты, акцепты — всё настоящее и покрыто тестами), но **до публикации нужна
вычитка практикующим юристом**. Всё, что читает приложение, помечено `isDraft: true`. Загрузчик
манифеста не примет `isDraft: false`, пока остался хоть один плейсхолдер — но от **неверного по сути**
текста без плейсхолдеров это не защищает.

**L4. Прицельно не покрыто тестами** (зафиксировано командой, не скрыто): акцепт владельца при
сохранении шаблона; эвристика рекламных маркеров на уровне API; поля подтверждения полномочий при
записи за другого человека; админский журнал обращений субъектов; полнота истории согласий в выгрузке.

**L5. Долг цикла 4 в этом цикле не брался вообще.** Не сдвинулись: **уведомления персоналу**
(US-34, P0-цикл-4 пункт B) и **справочник городов** — 91 запись, **админского способа добавить город
по-прежнему нет**, единственный путь это новая миграция.

**L6. 🧠 Урок цикла, который стоит записать.** **Смоук-тест против собранного образа поймал то, чего
не увидели 465 функциональных тестов.** Ломающее изменение контракта регистрации не довели до
`deploy/ci/smoke.sh`: функциональные тесты **строят запрос из текущего кода** и потому не могут
разойтись с ним по определению, а скрипт носил **свою захардкоженную копию** тела запроса. Ни один
тест этого класса расхождений увидеть не мог — его ловит только проверка против **настоящего образа**
с настоящим HTTP. Чинили уже на `develop` (`aecddb1`), заодно убрав из скрипта хардкод версий
документов. Вывод, применимый к любому следующему циклу: у каждого внешнего потребителя API
(скрипты, curl-рецепты в документации, примеры) есть своя копия контракта, и ломающее изменение
обязано пройти по всем копиям, а не только по коду и тестам.

---

**🆕 P0-цикл-4 — почему функция уведомлений написана, но не выпущена**

**A. Функция выключена тарифно и наружу не предлагается.** `AllowNotificationChannel = false` у всех
планов, включая новые (решение заказчика Q1); цена опции (`notifications.channel.price-per-month`)
не задана, а её отсутствие означает «опция не предлагается», а не «бесплатно». Суперадмин включает
флаг вручную при выпуске. **Для QA: сразу после выката «опция никому не предлагается» — ожидаемое
состояние, а не дефект.** Что блокирует выпуск:
  1. **правовая оценка по 41-ФЗ** не проведена;
  2. **партнёрского аккаунта GREEN-API нет** — реального экземпляра не создавалось ни разу;
  3. **P0-3 (восстановление `.env` из бэкапа)** — см. ниже, цена вопроса выросла качественно;
  4. **справочник городов неполон (91 запись) и нет админского способа добавить город** —
     единственный путь сейчас новая миграция.

**B. US-34 «уведомления персоналу» не реализована** — отложена осознанно, была **первой в порядке
урезания**. В коде остались члены перечисления, тексты и дефолтные шаблоны для
`StaffBookingCreated`/`StaffBookingCancelled`, но никто их не ставит в очередь (§5.1); эндпоинт из
`API_CONTRACT_CYCLE4.md` §35 не существует.

**C. Правовые тексты — заглушки.** Текст о рисках подключения WhatsApp — «рыба» с версией
`2026-09-18-draft` (`Services/NotificationRiskText.cs`), ждёт вычитки юристом. Механика принятия
версии при этом настоящая и работает. Отдельно: политика и оферта в `App_Data/legal/` остаются
черновиком `2026-09-08-draft` (§9.7) и **уведомления в WhatsApp никак не покрывают** — про отправку
сообщений клиенту в них ничего не сказано.

**D. Задача T4-D3 не выполнена: сетевой контур на боевой машине после выката не проверялся.**
Спайк показал, что `api.green-api.com` с машины достижим, но **глобального IPv6 на ней нет**, и без
keep-alive и упорядочивания адресов каждое новое соединение стоит ~5 секунд. Лечение **в коде есть**
(`GreenApiHandlerFactory` — keep-alive, `PreferIPv4` — IPv4-first `ConnectCallback`, настройки
`ConnectPreference`/`ConnectTimeoutSeconds`/`PerAddressConnectTimeoutSeconds`), покрыто юнит-тестами,
но **живой проверки после выката не было**.

**E. 🧠 Урок про тестовую инфраструктуру — записан, чтобы не повторять.** Три падения CI подряд в
конце цикла (коммиты `aae3541`, `7a36543`) вызваны **молча общими ресурсами между тестами**, и все
три давали зелёное локально и красное в CI:
  1. **база, не пересоздававшаяся под новую коллекцию** — `TestDatabaseFixture` чистит её один раз,
     а новая коллекция `"NotificationDispatch"` поднимает свой хост поверх той же базы;
  2. **счётчик отправок, считавший чужие строки** — `RecordingTransport` регистрируется один раз на
     хост, а соседние тесты оставляли в общей базе pending-строки, и проход уносил чужую за бюджет;
     исправлено фильтрацией по своему номеру телефона (приём, который соседний тест уже применял);
  3. **аккаунт суперадмина, «застолбленный» фабрикой правовых тестов** — она смотрела на одноразовый
     манифест со случайной версией, но **использовала тот же телефон суперадмина**, что и все
     остальные фабрики; чья коллекция стартовала первой на чистой базе, та и решала, с какой версией
     документов согласен общий суперадмин — после чего **весь остальной набор** получал 451. Какой
     именно тест за это платил, зависело только от порядка.
  Попутно: две проверки сравнивали `DateTime` на точное равенство со значением, прошедшим через
  Postgres (микросекунды против тиков) — на машине разработчика совпадало, в CI нет.

---

**P0 — эксплуатационные ограничения работающей системы**

🚀 **Блок «выглядит готовым, но вживую не проверялось» из прошлой редакции закрыт фактом
развёртывания и удалён.** Что именно перестало быть риском: живой деплой (выполнен, включая деплой
по кнопке и оба отказа прод-гварда), откат и обратная выкатка, восстановление из бэкапа, живой
GlitchTip с цепочкой до письма, CSP на всех трёх чувствительных сценариях (выгрузка через `blob:`,
SmartCaptcha, приватное фото), сосуществование с `fleetservice.service` на 8443, отсутствие
framing-заголовков на `/embed/`. Чек-лист `DEPLOY.md` §16 — семь пунктов из семи закрыты, дата и
результат проставлены у каждого. То, что осталось, — ниже, и это уже не «не проверено», а
«проверено и является ограничением».

1. 🚀 **Запас по памяти исчерпан: 3,3 ГиБ ОЗУ, свободно ~1 ГБ, 492 МиБ уже в подкачке.** На машине
   одновременно живут: стек приложения (`postgres` + `api`), стек GlitchTip (свои `postgres`,
   `redis`, `web`, `worker`), nginx, GNOME/GDM desktop-сессия и **чужой `fleetservice.service`**.
   Любое добавление сервиса (второй экземпляр, кеш, очередь, ещё один контейнер) требует расчёта
   памяти заранее, а не «попробуем и посмотрим»: OOM-killer на этой конфигурации выберет жертву
   сам, и ею может оказаться чужой сервис или Postgres. GlitchTip и выбирали-то вместо self-hosted
   Sentry именно по этому критерию (~1 ГБ против ~16 ГБ) — запас был съеден уже тогда.
2. 🚀 **Бэкап только локальный; внешней копии нет. При потере машины теряется всё.**
   `deploy/backup/backup.sh` кладёт дампы в `/var/backups/servicebooking` **на той же машине**, где
   лежат оригиналы. Защищает от порчи данных и ошибки оператора, **не защищает** от утраты машины
   (пожар, кража, смерть диска — диск ещё и **не зашифрован**, LUKS нет). Решение заказчика
   (SPEC R13), в `backup.sh` есть готовая заглушка `upload_offsite()` под будущую доработку. Тем же
   свойством страдает мониторинг: `health-alert.sh` крутится на проверяемой машине.
3. 🚀🆕⚖️ **Конфигурация (`.env`) при потере машины невосстановима — и теперь от неё зависят учётные
   данные ЧУЖИХ аккаунтов WhatsApp, а с цикла 5 ещё и медицинские сведения о клиентах салонов.**
   ⚖️ См. L2 выше: бэкапы проверены, дыра была в раннбуке и закрыта развилкой; но копия **по-прежнему
   лежит на той же машине**, а сверху добавилась новая проблема — восстановление воскрешает удалённое
   по запросу субъекта. 🆕 **Цена вопроса выросла качественно, и это прямо названо
   блокирующим выпуск функции уведомлений (P0-цикл-4, пункт A).** Что изменилось к лучшему: раздел
   «Инвентарь секретов» в `DEPLOY.md` **появился** (раньше `backup.sh` ссылался вникуда), §11.2
   теперь **описывает восстановление `.env` и файла отпечатка ключа** (шаг 3b, с предупреждением
   восстанавливать их **одним набором**), а сам отпечаток попал в бэкап. Что **не** изменилось:
   **копия по-прежнему лежит на той же машине**, внешней нет. В `.env` лежат `Jwt:Key` (его потеря =
   разлогинивание всех), пароль Postgres, пароль и секрет GlitchTip, серверный ключ SmartCaptcha и
   🆕 **`NOTIFICATIONS_ENCRYPTION_KEY`, потеря которого необратимо уносит WhatsApp-каналы всех
   салонов**. **Часть значений выдаётся внешними кабинетами и заново берётся только оттуда.**
   Единственный реальный носитель вне машины — менеджер паролей оператора, и то, что он заполнен,
   документом не подтверждается.
4. 🚀 **RDP торчит в интернет: порты 3389/3390 открыты в `ufw` и проброшены роутером.** Это
   пред-существующая настройка машины (заказчик пользуется удалённым рабочим столом), риск принят
   **осознанно**, но до этой редакции нигде как принятый риск зафиксирован не был — `DEPLOY.md`
   упоминает эти порты только в списке занятых и в объяснении, что их «настраивал кто-то осмысленно».
   Машина с персональными данными клиентов салонов (телефоны, фото к заметкам) доступна снаружи не
   только по 22/80/443.
5. 🚀 **Репозиторий публичный, и в нём лежат раннбуки с описанием инфраструктуры.** `DEPLOY.md`
   (~111 КБ) называет домены, внутренний адрес машины, модель роутера, занятые порты, имена
   пользователей (`ezbookdeploy`), пути (`/opt/ezbook/app`, `/var/backups/servicebooking`), схему
   доступа и точные команды. Секретов там нет — но карта есть. **Решение сделать репозиторий
   публичным заказчиком не подтверждалось; вопрос открыт** и никем не закрыт.
6. 🚀 **Имя compose-проекта приложения выводится из имени каталога.** В
   `docker-compose.glitchtip.yml` имя задано явно (`name: glitchtip`) — именно после того, как оба
   стека получили проект `app` и схлопнулись по именам сервисов. В `docker-compose.prod.yml` имя
   **намеренно не задано**: оно и так `app` (по каталогу `/opt/ezbook/app`), и существующие тома
   называются `app_postgres_data`, `app_api_uploads`, `app_api_private_uploads`. Последствие:
   **переименование или перенос каталога приведёт к созданию новых пустых томов** — docker не
   ошибётся, он молча создаст пустое, и приложение поднимется с чистой базой. Ровно эта ловушка уже
   сработала на процедуре восстановления (искали `ezbook_api_*`, а тома — `app_api_*`).
7. 🚀⚖️ **Правовые тексты — ЧЕРНОВАЯ редакция, юрист их не вычитывал, и система уже работает.**
   `legal.json`: `"isDraft": true` у **всех одиннадцати** записей. Fail-fast на черновик в Production
   **намеренно отсутствует** (решение заказчика). Сайт открыт, значит **каждая регистрация фиксирует
   согласие именно с черновиком**. ⚖️ Что **улучшилось** по сравнению с прошлой редакцией: такие
   записи теперь не теряются при повторном принятии — журнал `ConsentRecord` хранит **каждое** событие
   с версией и хешем текста, так что после вычитки будет видно, кто с какой редакцией соглашался.
   Что **не** изменилось: видимость черновика обеспечена только плашкой в UI и текстом самого документа.
8. 🚀 **Релиза не было: `master` отстаёт, тегов нет, «прод» формально не существует.** Развёрнута
   ветка `develop`, деплой выполнялся workflow `deploy-staging.yml`, то есть всё описанное выше —
   **стенд**, хотя и на публичном домене с настоящим TLS. `deploy-production.yml` нацелен на **ту же
   физическую машину** (второй машины нет): если кто-то создаст тег на `master` раньше решения о
   релизе, прод-воркфлоу технически выкатится на железо стенда. Единственные защиты — организационная
   (тег не создаётся) и required reviewer в environment `production`. Прямо записано в `DEPLOY.md` §9.
9. 🚀 **Локальные правки в `/opt/ezbook/app` не переживают деплой.** Форс-команда делает
   `git checkout --force` + `git clean -fd` и приводит дерево ровно к выкатываемому коммиту.
   Это **осознанное решение** (альтернатива — обрыв деплоя посреди прогона, уже после заливки
   фронта), отбрасываемое печатается в лог прогона до переключения, а `.env` и `legal/` не
   затрагиваются (они в `.gitignore`, а `clean` идёт без `-x`). Но аварийная правка «на месте»
   исчезнет без предупреждения при следующем нажатии кнопки.

**P1 — влияет на безопасность или корректность данных**

10. ⭐ **Смена номера телефона не подтверждается ничем, кроме текущего пароля.**
   `POST /api/profile/change-phone` сразу присваивает новый номер. Риск конкретный: гостевые визиты
   и заметки о клиенте ищутся по номеру, поэтому, указав чужой номер, можно прочитать чужую
   гостевую историю. Отложено **до появления SMS-канала**; решение описано в
   `SPEC_DEFERRED_NOTIFICATIONS.md` (пункт Д-1) и комментарием в `ProfileController.ChangePhone`
   (строка 151).
11. **Слабые дефолты в закоммиченном `appsettings.json` никуда не делись**
   (`Jwt:Key = "CHANGE_ME_…"`, `SuperAdmin:Password = "Admin12345"`, `SuperAdmin:Phone = "+70000000000"`).
   В цикле 3 fail-fast вынесен из `Program.cs` в чистый `Services/DeploymentSafetyChecks.cs`
   (`ValidateSecrets`, `ValidateTrustedNetworksConfigured`) и **покрыт юнит-тестами** (28 запусков),
   а CI-джоб `docker-build` стартует образ именно в `Production` — то есть проверка теперь сама под
   тестом. Остаточный риск **прежний**: в **не**-Production окружениях (стенд с
   `ASPNETCORE_ENVIRONMENT=Staging`) проверка не срабатывает вовсе.
12. 🆕 **Часовые пояса введены ЧАСТИЧНО — только там, где без них не работали уведомления.**
   Прежняя формулировка «полное отсутствие работы с часовыми поясами» больше не верна: у компании
   появились `CityId`, `TimeZoneId` и `TimeZoneIsManual`, есть справочник городов и чистый
   `CompanyTimeZoneResolver`, а расчёт «когда слать напоминание» (`NotificationTiming`) считает в
   зоне компании. **Но сама модель записи не изменилась:** `Booking.Date/StartTime/EndTime` — всё те
   же `DateOnly`/`TimeOnly` без TZ, слоты и расписание считаются без зоны, все контейнеры живут в UTC
   (`DEPLOY.md` §13, `README.md`). То есть зона сейчас — **свойство уведомлений, а не свойство
   расписания**, и источник ошибок «на границе суток» в ядре бронирования остаётся.
   Побочное следствие: **справочник городов неполон (91 запись) и пополняется только миграцией** —
   админского способа добавить город нет.
   🗓 Цикл 6 этого не менял и добавил к пункту ещё одну поверхность: горизонт записи и месячный
   календарь считают «сегодня» **от UTC-даты сервера**, а не от даты в зоне компании (C4 выше).
13. **Security-заголовки: Linux-контур закрыт, Windows-контур — нет.** `deploy/nginx/ezbook.conf`
   теперь отдаёт `Strict-Transport-Security`, `X-Content-Type-Options`, `Referrer-Policy`,
   `X-Frame-Options: DENY` и полноценный `Content-Security-Policy` (с явными исключениями под
   SmartCaptcha и `blob:` для приватных фото), причём `location /embed/` намеренно оставлен без
   анти-фрейминга. В **Windows/IIS-контуре** (`frontend/public/web.config`, `DEPLOY-windows.md`)
   заголовков нет вообще — этот контур **выведен из скоупа цикла 3 решением заказчика**, но
   `DEPLOY-windows.md` из репозитория не удалён, и по нему всё ещё можно развернуть систему без защиты.
14. ⚖️ **ЗАКРЫТО циклом 5: юридический риск фотофиксации снят на уровне механики.** Появились форма
   согласия на съёмку, текст `PhotoConsent` в манифесте, запись факта в журнал согласий и **отказ
   загрузить фото без согласия (400)**; отдельно сделано согласие на сведения о здоровье, а сами
   сведения вынесены в зашифрованную таблицу, закрытую в том числе от суперадмина. Решение Q6 цикла 2
   снято. **Остаточный риск** — тот же, что у всего правового контура: тексты форм черновые и не
   вычитаны юристом (L3), а README и `docs/faq.md` всё ещё утверждают, что согласия нет.

**P2 — код без тестов, на который многое завязано / хрупкие места**

15. ⭐ **Сторож гонки согласия — вероятностный.** Тест `LEG-036` (`LegalConsentTests.cs`) ловит гонку
    «одновременное принятие согласия», и его красноту подтверждали **на 20 итерациях**, а в репозиторий
    закоммичен **одиночный прогон**. То есть зелёный LEG-036 в CI не доказывает отсутствия регрессии —
    он лишь не поймал её в этот раз.
16. ⭐ **`Booking.ClientDeleted` фактически мёртв в UI.** Бэкенд проставляет флаг при удалении
    аккаунта, он доезжает до фронта и объявлен в `frontend/src/types/index.ts` (строка 103) — и это
    **единственное** его упоминание во всём фронтенде (проверено grep'ом). Ни одна страница его не
    отображает: персонал не видит, что клиент удалился.
17. ⭐ **Пагинация `GET /api/masters/clients` работает в памяти.** Контроллер материализует весь
    список клиентов компании, фильтрует по `search` и режет `Skip/Take` там же, а не в SQL.
    Признано приемлемым ревьюером (список ограничен одной компанией) и **задокументировано
    комментарием в коде** — но с ростом базы клиентов это первый кандидат на деградацию.
    Остальные три выборки (`admin/users`, `admin/companies`, публичные отзывы) пагинируются в БД.
18. **Фронтенд покрыт точечно, но заметно лучше.** 🗓 **283 теста Vitest в 43 файлах** (было 181 в
    32 после цикла 5 и 100 после цикла 4). 🗓 **Прирост цикла 6 (+102) закрыл главный пробел,
    который этот пункт называл годами: экраны записи.** `BookingModal`, `ManualBookingModal`,
    `RescheduleModal`, новый `BookingCalendar`, `PhoneInput`, `LoginPage`, `AdminPage`,
    `CompanyManagePage` теперь покрыты. 📸 **На `242c7d9` это 369 тестов в 57 файлах**, а
    `ManualBookingModal` вместе со своим тестом **удалён** — его сценарии переехали в
    `BookingModal.test.tsx`/`BookingModal.captcha.test.tsx` (§7.3). **Остаются непокрытыми**: весь раздел уведомлений цикла 4,
    админский журнал обращений субъектов, календарь `ScheduleTab`, `DeleteAccountPage`,
    `useExportData`, `useAuthedImage`.
    ⚠️ Ниже — список непокрытого в редакции цикла 5, **устаревший в части экранов записи**;
    оставлен, чтобы было видно, что именно и когда закрыли. ⚖️ Прирост цикла 5
    (+81) — впервые в основном экраны и формы, а не утилиты (§7.3). Покрыты мапперы ошибок,
    `formatPhone`, `timezone`, `channelBanner`, `PhotoGallery`, `Pagination`, `useDebouncedValue`,
    правовой контур (`ConsentGate`, `LegalUpdateBanner`, `LegalDocumentPage`) и по одному тесту на
    `CompanyPage` и `MasterClientsPage`. **Не покрыты**: `BookingModal`, `ManualBookingModal`,
    `RescheduleModal`, календарь `ScheduleTab`, вкладочные страницы кабинета и админки,
    `DeleteAccountPage`, `useExportData`, `useAuthedImage`, 🆕 **весь раздел уведомлений**
    (`NotificationsSection` и три вкладки, `QrModal`, `AssignCompanyDialog`, `RiskAcceptanceModal`,
    `ChannelBreachBanner`, `NotificationsAdminTab`, `UnsubscribePage`, `CityCombobox`).
19. **`CompaniesController` — 635 строк** (было 571) и 16 эндпоинтов, включая логику подписок,
    загрузку файлов, квоту фото и целиком сборку статистики (`GetStats`, ~70 строк агрегаций
    **в памяти** после `ToListAsync()`). Контроллер продолжает расти.
20. **Логика прав по-прежнему размазана по приватным копиям** `CanManageCompany`/`CanManage`, хотя их
    «членская» половина унифицирована через `CompanyMembership`. `MailingController` **до сих пор**
    не переведён на общий хелпер — единственное оставшееся исключение.
21. **Перечитывание ролей и сверка `SecurityStamp` на каждом запросе** (`Program.cs`,
    `OnTokenValidated`) — дополнительный запрос к БД на каждый аутентифицированный вызов без кеша.
    Цикл 3 добавил туда же чтение состояния согласия (claim'ы `consent_*` сверяются с актуальной
    версией документа), то есть путь на каждом запросе стал длиннее, а не короче.
22. **Шаг сетки слотов по-прежнему захардкожен 30 минутами** (`SlotCalculator.StepMinutes`).
    🗓 Цикл 6 этого не менял, хотя работал ровно в этом файле; после US-67 пункт стал острее —
    см. C3 выше.
23. **Миграция `NormalizePhoneNumbers` необратима и не проверялась на реальных данных.** Сейчас это
    безопасно (боевых данных нет), но повторно применить её к живой базе будет нельзя.
    Рядом появилась вторая миграция с данными — `ResyncIdentityRoles`, у неё **`Down` — no-op**
    (осознанно: откат пересчёта ролей бессмыслен).
    ⚖️ **Цикл 5 добавил в этот список ещё две необратимые по данным:** `ConsentJournal` (переносит
    строки в журнал и **дропает `UserConsents`**; `Down` пересоздаёт таблицу **пустой** — структура
    откатывается, данные нет) и `RemovePhotoRetentionForever` (переписывает `Forever` → `TwelveMonths`).
    Итого разрушительных миграций в проекте **три**.
24. **Ограничитель `PermitLimit` читается из конфигурации на каждый запрос** через
    `ctx.RequestServices.GetRequiredService<IConfiguration>()` — приём из цикла 2 сохранён и
    распространён на четыре новые политики.

🆕 **P2, добавленное циклом 4:**

24a. **Отправщик — самый большой новый код, никогда не работавший против настоящего провайдера.**
    `NotificationDispatchTask` (~464 строки) и `ChannelHealthTask` (~405) покрыты тестами против
    заглушек и фейковых часов; живого GREEN-API не видел ни один прогон. Всё, что касается реальных
    таймаутов, реальных кодов ошибок и реального поведения WhatsApp при бане, **проверено только по
    документации провайдера**.

24b. **Два перечисления стали хрупкими по своим числовым значениям.** `NotificationStatus.Pending`
    обязан остаться `0` (частичный индекс диспетчера объявлен сырым SQL `"Status" = 0`), а значения
    `NotificationType` — позиции битов в `EnabledTypeMask`. Перестановка члена не ломает сборку и не
    роняет тест на ровном месте: индекс останется, запрос молча уедет в full scan, а маска молча
    сменит смысл. Оба риска **зафиксированы XML-комментариями прямо на перечислениях** — но держатся
    только на том, что их прочитают.

24c. **Мастер-ключ шифрования — единственная точка отказа для всей функции.** Его потеря или подмена
    не деградирует систему частично: все сохранённые токены становятся нечитаемы, каналы уходят в
    `NeedsReconnect` с причиной `SecretUnavailable`, и каждому салону придётся заново проходить
    привязку по QR. Защит две (отпечаток + отказ старта, обязательное подтверждение ротации), обе
    **обнаруживают** проблему, но ни одна её не **восстанавливает**.

24d. ✅ **ЗАКРЫТО циклом 8.** ~~Тесты уведомлений делят ту же базу `servicebooking_test`, что и
    остальные 400+; отдельная коллекция `"NotificationDispatch"` отключает параллелизм у себя, но
    база одна на весь прогон~~. 🔬 Базы `servicebooking_test` больше нет: **у каждого тест-класса
    своя одноразовая база**, обе коллекции (`"Api"` и `"NotificationDispatch"`) распущены,
    `EnsureDeletedAsync` удалён (§7.2). Класс дефектов, давший три падения CI в конце цикла 4
    (P0-цикл-4, пункт E), устранён конструктивно, а не дисциплиной тестов. Остаточная хрупкость
    этого же набора теперь другая — фиксированные `Task.Delay` в правовых тестах, см. T8-1.

24e. **`AdminController` и `CompaniesController` продолжили расти:** админский контроллер получил
    +211 строк (каналы, оплаты, параметры платформы), компании — +125 (города и зоны). Сервисного
    слоя по-прежнему нет. ⚖️ Цикл 5 добавил админскому ещё +142 (обращения субъектов, политика
    уничтожения), `ProfileController` вырос на **+486** и стал вторым по объёму после компаний.

⚖️ **P2, добавленное циклом 5:**

24f. 🔴 **Тринадцать правил уничтожения ни разу не работали в боевом режиме.** Они покрыты
    юнит-тестами и функциональным тестом на сухой прогон, но **по умолчанию ничего не удаляют**, и на
    живой машине боевой прогон не выполнялся. Это самый большой новый код цикла, и цена его ошибки
    асимметрична: не отработало — накопились данные; отработало неверно — данные уничтожены
    безвозвратно. Защиты две — сухой прогон по умолчанию и процедура «сначала посмотреть журнал»
    (`DEPLOY.md` §11.4), обе **обнаруживают**, но не **восстанавливают**.

24g. **Список действий под `[RequiresOwnerTerms]` ведётся руками.** Двенадцать действий в шести
    контроллерах; ничто не проверяет, что новое действие владельца не забыли пометить. Пропуск
    атрибута не ломает ни сборку, ни тест — он просто тихо оставляет дыру в гейте.

24h. **Текст о рисках канала существует в двух местах с разными версиями** (§5.1 п. 9):
    константа `NotificationRiskText` цикла 4, с которой сверяется контроллер, и документ
    `ChannelRiskNotice` цикла 5 в манифесте, с которого читает публичная страница.

24i. **Мастер-ключ шифрования стал единой точкой отказа не только для каналов, но и для медицинских
    сведений** (расширение 24c). Решение переиспользовать ключ осознанное — «вторая криптография это
    вторая процедура ротации и второй способ потерять данные», — но радиус поражения вырос.

24j. **`ConsentLedger` — новая центральная зависимость правового контура.** Единственный читатель и
    писатель журнала; он намеренно вне горячего пути, но любая ошибка в нём теперь одинаково
    затрагивает регистрацию, профиль, фото, здоровье и постановку уведомлений в очередь.

**P3 — эксплуатация, гигиена, недоделки**

25. ⭐ **Шага `dotnet format` в CI нет.** `.editorconfig` появился (US-50) и **описывает уже
    существующий стиль**, а не задаёт новый, но `dotnet format --verify-no-changes` в CI не добавлен:
    сухой прогон даёт несколько сотен предсуществующих расхождений по переносам (в основном в
    `ServiceBooking.Tests`). Массовое переформатирование отложено отдельным коммитом — причина
    записана комментарием в шапке самого `.editorconfig`. ESLint в CI, наоборот, **добавлен**
    (`npm run lint` отдельным шагом).
26. **Устаревшие зависимости фронта с известными уязвимостями (`axios`, `form-data`, `react-router`)
    не закрыты.** Версии в `frontend/package.json` те же, что и до цикла 3 (`axios ^1.7.7`,
    `react-router-dom ^6.26.2`); `npm audit fix` закрывает часть без мажора, `react-router` требует
    мажорного апгрейда.
27. **Фичи, выглядящие готовыми в UI, но не работающие по сути** (🚀 теперь это видят живые
    пользователи на `ezbook.ru`, а не только разработчики)**:** «Рассылка» (писем нет, текст
    «Рассылка поставлена в очередь» **осознанно оставлен вводящим в заблуждение**, решение Q9),
    предоплата (платежей нет), `NotifyDaysBefore` в редакторе тарифов (уведомлений нет), обещание
    «напоминание накануне визита» на `HomePage.tsx` (Q8). README и `docs/faq.md` про это пишут
    честно — интерфейс нет.
    🆕 **Уведомления в этот список не попадают, и это важное отличие:** они не «выглядят рабочими, но
    не работают», а **не показываются вовсе**, пока суперадмин не включит тарифный флаг и не задаст
    цену. Обещание «напоминание накануне визита» на `HomePage.tsx` теперь имеет за собой код — но до
    выпуска опции оно по-прежнему невыполнимо.
28. **Локальные загруженные файлы не воспроизводимы на чистом клоне.** `wwwroot/uploads/**` и
    `App_Data/private-uploads/**` — в `.gitignore`; на машине разработчика в приватном каталоге лежат
    ~36 папок компаний с реальными JPEG. На свежем клоне ссылки из дампа БД будут битыми.
29. **Валидация DTO неполна** (`Slug`, `Bio`, `Comment`, `SendMailDto.Message`), нет запрета удалять
    последнего владельца, нет проверки статуса в `MarkPaid` — явно отложено как некритичное.
30. **`frontend/design_handoff_site_redesign/`** (10 HTML-макетов) лежит внутри `frontend/`, в сборку
    не идёт; **пустой каталог `frontend/src/components/auth/`** всё ещё на месте; два `.example`-файла
    прод-конфига (`ServiceBooking.API/appsettings.Production.json.example` и `.env.production.example`)
    описывают один и тот же прод двумя способами, актуален второй.

🚀 **Закрыто первым реальным развёртыванием** (не переоткрывать): «живого деплоя не было ни разу»;
«GlitchTip вживую не поднимался»; «цепочка ошибка → трекер → письмо не проверялась»; «CSP на
скачивании выгрузки не проверялся живьём» (проверены все три чувствительных сценария, не только
выгрузка); «откат не выполнялся на реальном systemd/nginx» (проверена и обратимость — выкатка
вперёд после отката); «восстановление из бэкапа не прогонялось на этой машине»; «неизвестно,
переживёт ли `fleetservice.service` установку docker/nginx» (переживает, отвечает как раньше);
«деплой по кнопке не проверен сквозь весь путь» (включая оба отказа прод-гварда и проверку
форс-команды снаружи по отдельным ключам). Побочно закрыты две дыры доступа, найденные при этом:
`ezbookdeploy` состоял в группе `sudo` (то есть имел `ALL : ALL`, и точечные `NOPASSWD` были
бессмысленны), и в его `authorized_keys` по ошибке попал личный ключ **без** префикса `command=`.

🆕 **Закрыто циклом 4** (не переоткрывать без причины): «интеграции с мессенджерами нет»;
«полное отсутствие часовых поясов» — закрыто **частично и только для уведомлений** (см. §9.12,
формулировка изменена, а не снята); «`DEPLOY.md` §11.1 не называет копирование `.env`, а §11.2 его
не восстанавливает» и «`backup.sh` ссылается на несуществующий раздел инвентаря секретов» — раздел
появился, процедура восстановления `.env` и отпечатка описана; «README и CHANGELOG отстали от факта
развёртывания» — закрыто коммитом `d4137dd`; «раннер `ScheduledTaskRunner` в тестах выключен
целиком, его собственное поведение проверить нечем» — закрыто `NotificationDispatchTestFactory`.

⚖️ **Закрыто циклом 5** (не переоткрывать без причины): «правовая оценка по 41-ФЗ не проведена» —
проведена, закон к платформе не применяется (`LEGAL_REVIEW.md`); «согласия клиента на фотосъёмку нет»
— есть, вместе с отказом загрузить фото без него; «спецкатегории (здоровье) обрабатываются без
отдельного основания и без защиты» — отдельная зашифрованная таблица, отдельное согласие, доступ
закрыт даже суперадмину; «у субъекта нет способа обратиться к оператору» — анонимная форма обращений
со сроком ответа, вычисляемым в рабочих днях и фиксируемым на строке; «сроков хранения нет ни у чего»
— тринадцать правил и конфигурируемые сроки с fail-fast на юридические минимумы;
«согласие перезаписывается и историю восстановить нельзя» — журнал вместо строки;
«`DEPLOY.md` не описывает, что делать при утечке» — `docs/incident-runbook.md`.
⚠️ **Частично**: «бэкапы не проверены» — проверены, но восстановление воскрешает удалённое (L2).

🗓 **Закрыто циклом 6** (не переоткрывать без причины): «вход отвечает одинаковым 401 на любую
причину, и пользователь не понимает, что делать» — 423/403/401 различаются, а два 401 совпадают
намеренно; «тело ошибки регистрации не показывается человеку» — разбирается и переводится;
«клиент не видит занятость месяца и щёлкает дни по одному» — `GET /api/bookings/availability`
плюс месячный календарь; «ручная запись на дату без расписания раскрывает сутки напролёт молча» —
окно по умолчанию 09:00–21:00, сутки только по явному запросу; «подписку можно сохранить без даты
окончания, и потом никто не понимает, почему тариф не работает» — дата обязательна плюс
диагностический эндпоинт; «за визит можно записать только одну услугу» — до пяти;
«сетка переноса блокируется собственной записью и ломается о деактивированную услугу или
уволившегося мастера» — `excludeBookingId`; «400 от автоматической валидации приходит в форме,
которую фронт не понимает» — приведено к конвенции проекта **глобально**;
«экраны записи не покрыты тестами фронтенда» — покрыты (§7.3).

**Закрыто циклом 3** (не переоткрывать без причины):
отсутствие политики конфиденциальности, пользовательского соглашения и записи согласия;
отсутствие прав субъекта данных (выгрузка и удаление аккаунта);
rate limiting только на загрузках — теперь закрыты вход, регистрация, гостевая запись и выгрузка;
доверие `X-Forwarded-For` без списка доверенных сетей;
отсутствие структурированного логирования и трекера ошибок (Serilog + маскирование телефонов +
Sentry-совместимый синк) и отсутствие health-эндпоинтов;
односторонняя синхронизация Identity-ролей (`RemoveMember` не снимал роль) — закрыто
`IdentityRoleSync` + миграцией данных;
отсутствие пагинации на четырёх выборках и N+1 по ролям в `admin/users`;
отсутствие бэкапа, отката и security-заголовков в Linux-контуре как таковых;
«образ ни разу не запускался» — CI-джоб `docker-build` теперь **запускает** контейнер и гоняет
`deploy/ci/smoke.sh`, включая реальную загрузку изображения (то есть загрузку SkiaSharp внутри образа);
сборка фронта на боевом сервере — перенесена в CI-артефакт;
отсутствие `.editorconfig` и линтера на фронте.

**Закрыто циклами 1 и 2** (для истории): права мастера на CRUD услуг;
`GET /api/workinghours` без проверки принадлежности; публичный `GET /api/bookings/occupied`;
обход гейтов через `guestName` и `manual=true`; Swagger в Production; отсутствие глобальной обработки
исключений; `SubscriptionResolver` и `PlanConfig.IsActive`; комиссия, привязанная к аккаунту, а не к
компании; основа расчёта отчётов; загрузка логотипа по `Content-Type`; отсутствие CI; мёртвый
Blazor-проект и мёртвые страницы фронта; расхождение `BookingDto` и TS-типов; недостижимый `/embed`;
`launchSettings.json`; отсутствие тестов фронтенда; отсутствие юнит-тестов; двоение аккаунтов по
формату телефона.

---

## 10. Что уже существует в документации и тест-кейсах

Раздел нужен, чтобы следующие агенты **дополняли существующее, а не заводили параллельные версии**.
Всё перечисленное лежит в репозитории.

📸 **Состояние на `242c7d9`.** Цикл 10 добавил в документацию **четыре документа цикла** —
`SPEC_CYCLE10_MASTER_BOOKING_HISTORY_PHOTO.md` (~55 КБ, редакция 3), `ARCHITECTURE_CYCLE10.md`
(~87 КБ, §100–§117), `API_CONTRACT_CYCLE10.md` (~34 КБ, §120–§133), **`contracts/cycle10/openapi.yaml`**
(807 строк, **четвёртый** машиночитаемый контракт, §10.3) — плюс раздел цикла в `TEST_CATALOG.md`
(+117 строк, §10.4) и точечную правку `API_DOCUMENTATION.md` (русские тексты ошибок загрузки,
§5.0-ter).

⚠️ **Чего цикл 10 в документации НЕ сделал — это пробелы, а не оформление:**
- **`API_DOCUMENTATION.md` про новые эндпоинты цикла 10 не знает.** Проверено: единственная правка
  этого файла в диапазоне — тексты ошибок загрузки. `GET /api/bookings/{id}/history` и четыре
  маршрута фотографий компании описаны **только** в `API_CONTRACT_CYCLE10.md` и
  `contracts/cycle10/openapi.yaml`; в справочнике эндпоинтов для внешних потребителей их нет.
- **`README.md`, `CHANGELOG.md` и `docs/**` цикл 10 не трогал вообще**
  (`git log bd3be3f..242c7d9 -- README.md CHANGELOG.md docs/` — пусто; `grep -ci "цикл 10"` по обоим
  файлам → 0). Про один экран записи, историю изменений и фотогалерею там не сказано ничего.
  ⚠️ Продуктовое описание цикла 10 вносится product-analyst'ом **параллельно с этой редакцией** —
  актуальное состояние `CHANGELOG.md` и `README.md` смотреть прямо в них, а не здесь.
- **Отдельного документа с описаниями тест-кейсов цикл 10 не завёл** — его сценарии легли в общий
  `TEST_CATALOG.md`, см. §10.4.

💳 **Состояние на `0929b48`.** Цикл 7 добавил в документацию **четыре документа цикла** —
`SPEC_CYCLE7_PRICING.md` (~70 КБ), `ARCHITECTURE_CYCLE7.md` (~149 КБ), `API_CONTRACT_CYCLE7.md`
(~73 КБ), **`contracts/cycle7/openapi.yaml`** (~1 900 строк, третий машиночитаемый контракт, §10.3)
— плюс правки `TEST_CATALOG.md` (+1 402 строки, §10.4), `DEPLOY.md` (+32 строки — шаг **10.2a**,
предпроверки миграции биллинга) и `API_DOCUMENTATION.md` (−28/+, раздел про
`PUT /api/admin/owners/{id}/subscription` заменён отметкой «отозван, отвечает 410»).

⚠️ **Чего цикл 7 НЕ сделал в документации — знать обязательно, это пробел, а не оформление:**
- **`CHANGELOG.md` записи про цикл 7 нет вообще.** Проверено: `grep -ci "биллинг-аккаунт" CHANGELOG.md`
  → 0. Верхние разделы «Не выпущено» — про циклы 6, 8 и 5; про биллинг, тарифы, опции, витрину цен
  и перенос компаний в changelog **не сказано ничего**.
- **`README.md` цикл 7 не трогал.** Единственный коммит, правивший README в этом диапазоне, —
  `ea762b3`, и он **про цикл 6** (product-analyst). Врезки «Что умеет» / «Чего пока нет» про
  биллинг-аккаунты, опции и витрину цен ничего не знают.
- **`docs/**` цикл 7 не трогал вообще** (`git log aac6231..HEAD -- docs/` — пусто). Ролевые гайды
  `owner.md`, `admin.md`, `accounts.md`, `faq.md` описывают старую модель, где платит **человек**,
  а не биллинг-аккаунт. Для `docs/owner.md` и `docs/admin.md` это прямое фактическое устаревание:
  экранов «Ваша подписка» и «Биллинг-аккаунты» там нет.
- **Отдельного документа с описаниями тест-кейсов цикл 7 не завёл** — его сценарии легли в общий
  `TEST_CATALOG.md`, см. §10.4.

🗓 **Отметка прошлой редакции про цикл 6 частично устарела в лучшую сторону:** сказанное ниже «в
`CHANGELOG.md` записи про цикл 6 нет и в `README.md` ничего не сказано» **закрыто** коммитом
`ea762b3` — и changelog, и README теперь цикл 6 описывают (верхний раздел changelog — «Не выпущено
— запись на услуги: вход починен, сценарий записи доведён»). А вот про `docs/**` замечание
**остаётся в силе**: ролевые гайды цикл 6 так и не тронул.

🗓 **Состояние на `aac6231`.** Цикл 6 добавил в документацию: `SPEC_CYCLE6_BOOKING_FIXES.md`
(заархивированная спека цикла — `SPEC.md` занят циклом 8), `ARCHITECTURE_CYCLE6.md`,
`API_CONTRACT_CYCLE6.md`, **`openapi-cycle6.yaml`** (второй машиночитаемый контракт в репозитории,
§10.3) и раздел цикла в `TEST_CATALOG.md` (§10.4).
⚠️ **Чего цикл 6 НЕ сделал в документации, и это надо знать:** в `CHANGELOG.md` **записи про цикл 6
нет** (его вносит product-analyst — на `aac6231` в файле только разделы циклов 4/5/8), в `README.md`
ничего про календарь, несколько услуг за визит и российский формат телефона не сказано, и в
`docs/**` (ролевые гайды `client.md`, `owner.md`, `master.md`, `faq.md`) тоже — **ни один файл
пользовательской документации цикл 6 не тронул**. То есть описание сценария записи в гайдах
описывает состояние до цикла 6.

🔬 **Состояние на `6562a86`** (цикл 8). `README.md` и `CHANGELOG.md` правятся product-analyst'ом
**параллельно с этой редакцией** — конкретные формулировки и размеры смотреть прямо в них.
Цикл 8 добавил в документацию: `docs/testing-isolation.md` (новый, для команды),
`docs/README.md` (новый раздел «Не для пользователей: документы команды проекта»),
`contracts/cycle8/` (**первые машиночитаемые контракты в репозитории**), четыре документа цикла
(`ARCHITECTURE_CYCLE8*.md`, `API_CONTRACT_CYCLE8*.md`), `SPEC_CYCLE5_LEGAL.md` (восстановленная
спека цикла 5), `.env.dev.example`, плюс точечные правки `TEST_CATALOG.md` и `DEPLOY.md`.

🆕 **Расхождение продуктовой документации с фактом развёртывания, о котором предупреждала прошлая
редакция, ЗАКРЫТО** коммитом `d4137dd`. ⚖️ Продуктовое описание цикла 5 вносится в `README.md` и
`CHANGELOG.md` **параллельно, product-analyst'ом**, и этим документом не фиксируется — актуальное
состояние этих двух файлов смотреть прямо в них. ⚠️ На момент этой редакции README и `docs/faq.md`
ещё содержат утверждение «согласия на фотосъёмку нет», которое циклом 5 перестало быть верным (§5.1 п. 6).

### 10.1 Краткая продуктовая документация

| Что | Путь | Формат | Структура |
|---|---|---|---|
| Обзор продукта | `README.md` (⚖️ ~30 КБ; правится product-analyst'ом прямо сейчас, размер и формулировки могут отличаться) | Markdown, русский | `## О проекте` (внутри жирными врезками «Для кого», «Роли», **«Что умеет»**, **«Чего пока нет»** — честный список отсутствующего: платежи, письма, уведомления, самостоятельная оплата тарифа, клиентский просмотр фото, согласие на съёмку) → **`## Запуск`** (`### Локально, всё в Docker`, `### Локально, без Docker для API`, `### Переменные окружения и секреты`, `### CI`, `### Деплой`). В цикле 3 README вырос эксплуатационной половиной: врезки «Бэкапы» (и прямо — что копия локальная) и «Часовые пояса» (UTC везде) |
| Changelog | `CHANGELOG.md` (⚖️ ~88 КБ; правится product-analyst'ом прямо сейчас) | Markdown, русский, по мотивам Keep a Changelog | **По датам завершения цикла, самая свежая запись сверху**; номеров версий в проекте нет. 🆕 **Самый верхний раздел — `## Не выпущено`**: туда кладётся принятое командой, но не выкаченное на работающий адрес; дату раздел получает в момент фактического выката. Сейчас там — уведомления в WhatsApp, с оговоркой «функцию нельзя включить сейчас, и после выката она никому не предлагается — это ожидаемое состояние, а не дефект». Ниже — датированные записи (`2026-09-17 — сервис впервые развёрнут`, `2026-09-15`, `2026-09-07`, …) |

GitHub Releases / wiki в проекте не используются. 🆕 Оба файла приведены в соответствие с
реальностью коммитом `d4137dd` и **дополняются product-analyst'ом прямо сейчас** — считать их
устаревшими больше нельзя, но и опираться на конкретные формулировки из этой редакции не стоит.

🆕 Пользовательская документация в `docs/**` циклом 4 не менялась — раздела про уведомления в
WhatsApp там нет до сих пор. ⚖️ **Цикл 5 добавил в `docs/` ровно один файл и ни одного не правил:**
`docs/incident-runbook.md` (~13 КБ) — «Утечка персональных данных: что делать прямо сейчас».
Markdown, русский, структура по фазам: «Это вообще инцидент?» → «Первые 15 минут — остановить
дальнейшую утечку» → «Зафиксировать масштаб» → **«Сроки — это самое важное в этом документе»** →
«Уведомить пользователей» → «Журнал инцидентов» → «После острой фазы». Это **эксплуатационный
документ, а не пользовательский** — лежит в `docs/` рядом с ролевыми гайдами, но адресован команде.
⚠️ Ролевые гайды (`docs/client.md`, `owner.md`, `master.md`, `faq.md`, `personal-data.md`) про
согласия цикла 5 **ничего не знают** — их цикл не трогал.

Отдельно, не продуктовая, но постоянно нужная документация: **`DEPLOY.md`** (⚖️ ~153 КБ, markdown,
русский) — эксплуатационный раннбук машины. Структура: «Целевая машина (факты)» → «Чек-лист всей
установки» → §0…§15 пошаговые разделы (каждый шаг с блоком «Должно получиться») → **§16 чек-лист
первого запуска с датами и вердиктами** → **«Почему так сделано»** (объяснительная часть, вынесена
в конец: почему docker не из snap, почему бэкап локальный, почему `ezbookdeploy` не root, почему
CSP устроена так, какую ветку катим и почему AlmaLinux-вариант не сохранён параллельно).
🆕 Цикл 4 дописал сюда (+192 строки): раздел **«Принятые риски и где лежат секреты»** с подразделом
**«Инвентарь секретов»** (§55), отдельную строку про `NOTIFICATIONS_ENCRYPTION_KEY` и процедуру его
ротации, создание каталога `state/` до первого запуска и шаг 3b восстановления `.env` + отпечатка
ключа в §11.2.
⚖️ Цикл 5 дописал ещё +252 строки (файл вырос до ~153 КБ): шаг 3b **превращён в развилку** «та же
машина / новая машина» с объяснением, что ключом зашифрованы и медицинские данные; новый
**§11.2b «обязательный шаг после восстановления: повторно применить удаления»**; новый
**§11.4 «уничтожение по срокам хранения — сначала сухой прогон»** с переключателем `RETENTION_DRY_RUN`;
процедура ротации ключа переставлена так, что **начинается с `ClientHealthNotes`**.
🔬 Цикл 8 дописал в `DEPLOY.md` ровно одну врезку (+6 строк) в §3: **«не путайте с `.env` рабочей
копии разработчика»** — на боевой машине `docker-compose.prod.yml` не читает ни одной переменной
`SB_*`, образец боевого файла по-прежнему `.env.production.example`.

### 10.2 Развёрнутая пользовательская документация

Каталог **`docs/`**, Markdown, русский, **разбита по ролям**, точка входа — `docs/README.md`
с таблицей «кто вы → с чего начать» и разделом «Что нового» со ссылкой на `CHANGELOG.md`.

💳 ⚠️ **Цикл 7 не тронул здесь ни одного файла** (`git log aac6231..0929b48 -- docs/` — пусто).
Это значит, что `docs/owner.md`, `docs/admin.md`, `docs/accounts.md` и `docs/faq.md` описывают
**старую модель, где платит человек, а не биллинг-аккаунт**: экранов «Ваша подписка» (`/billing`),
«Биллинг-аккаунты» в админке, очереди заявок, переноса компаний и витрины цен (`/pricing`) в
гайдах нет вовсе. Накопленное отставание `docs/**` теперь составляет **три цикла подряд** (5, 6, 7).
Текущий полный список файлов каталога: `README.md`, `accounts.md`, `admin.md`, `client.md`,
`faq.md`, `incident-runbook.md`, `master.md`, `owner.md`, `personal-data.md`, `schedule.md`,
`testing-isolation.md`.

| Файл | О чём |
|---|---|
| `docs/README.md` | оглавление, роли в двух словах, ссылка на CHANGELOG |
| `docs/client.md` | запись на услугу — для клиента |
| `docs/master.md` | кабинет мастера (включая заметки и фото) |
| `docs/owner.md` | кабинет владельца компании |
| `docs/admin.md` | администрирование платформы |
| `docs/accounts.md` | общая: аккаунт, телефон как логин, смена пароля/номера |
| **`docs/personal-data.md`** ⭐ (~16 КБ, цикл 3) | общая: правовые документы и **плашка «Черновая редакция»**, согласие при регистрации, что происходит при редакционной и существенной правке, «Скачать свои данные», «Удалить аккаунт» (что удаляется, что остаётся, освобождение телефона, почему владелец компании так удалиться не может, что делать, если аккаунта нет) |
| `docs/schedule.md` | общая: расписание, перерывы, расчёт свободного времени (единственный файл `docs/`, не тронутый циклом 3) |
| `docs/faq.md` | частые вопросы **и честный список ограничений** |
| ⚖️ `docs/incident-runbook.md` (~13 КБ) | **утечка ПДн: что делать прямо сейчас** — по фазам, с акцентом на сроки уведомления РКН и субъектов. Документ для команды, не для пользователя |
| 🔬 **`docs/testing-isolation.md`** (~33 КБ) | **«Запуск тестов и локальная среда»** — тоже документ **для команды, не для пользователя**. Структура: «Коротко» → **«Что изменилось (если вы работали с репозиторием до цикла 8)»** (таблица было/стало) → «Что нужно на машине» → «Как прогнать тесты» (консоль и Rider) → «Что печатает прогон» → **«Если прогон красный или не стартует»** (по симптомам: отказ стартовать, отказ удалять базу, бюджет соединений, «то зелёный, то красный», мусор после прерванного прогона) → «Параллелизм» → «Случайный порядок тестов» → «Инструмент диагностики `ServiceBooking.TestKit`» (`status`/`sweep`/`doctor`) → «Две рабочие копии на одной машине» → **таблица переменных окружения тестов** → «Как это устроено в CI» → **«Известные ограничения среды»** (macOS + colima) → «Куда смотреть дальше». Это **первый документ проекта, адресованный тому, кто встретил красный прогон**, и он подробнее §7 этого файла |

🔬 `docs/README.md` получил в цикле 8 новый последний раздел — **«Не для пользователей: документы
команды проекта»** со ссылками на `testing-isolation.md` и `incident-runbook.md`: до этого оба
эксплуатационных документа лежали в пользовательском каталоге без пометки.

Отдельного сайта документации и справочного раздела внутри приложения **нет**. ⚖️ Внутри приложения
пользователю доступны правовые документы — страницы `/privacy`, `/terms`, `/terms-owner`,
`/pdn-consent`, `/channel-risk` (все пять разрешены к индексации в `frontend/public/robots.txt`), плюс
экран управления своими согласиями `/profile/consents` и публичная форма обращения `/data-request`.

### 10.3 Документация API для внешних потребителей

| Что | Путь | Формат | Структура |
|---|---|---|---|
| Справочник эндпоинтов | `API_DOCUMENTATION.md` (⚖️ ~259 КБ) | Markdown, русский | §1 Обзор → §2 Аутентификация → §3 Ключевые бизнес-концепции (**§3.11 «Конверт `PagedResult<T>`»**) → **§4 Справочник эндпоинтов** (основной объём) → §5 Сквозные сценарии (curl-рецепты) → §6 Справочник кодов ответа → **§7 Известные ограничения**. 🆕 **Обновлён в цикле 4**: добавлены `§4.3a GET /api/cities` и большой **§4.15 «Notifications (WhatsApp)»** с подразделами «Каналы владельца», «Настройки и шаблоны компании», «Журнал доставки и отметка в записи», «Отписка и вебхук», «Админка». Оба новых раздела **явно помечены «недоступно в текущем релизе»** |
| Контракт цикла 3 | `API_CONTRACT.md` (~51 КБ) | Markdown, русский | разделы **до 19**: контракт цикла 3, коды ошибок (включая 451) |
| 🆕 Контракт цикла 4 | **`API_CONTRACT_CYCLE4.md`** (~40 КБ) | Markdown, русский | **разделы 19–37, продолжение предыдущего файла, нумерация не пересекается**. §19 общее для всех эндпоинтов цикла → §20–27 каналы → §28–30 настройки/шаблоны/журнал → §31 город и часовой пояс → §32–33 отписка и вебхук → §34 админка → §35 (эндпоинт US-34, **срезан, в коде его нет**) → §36 сводка новых и изменённых эндпоинтов → §37 чек-лист согласования BE↔FE |
| ⚖️ Контракт цикла 5 | **`API_CONTRACT_CYCLE5.md`** (~52 КБ) | Markdown, русский | **разделы 38–53**, та же схема продолжения. §38 общее → §39 правовые документы (**BREAKING**) → §40 регистрация (**BREAKING № 2**) → §41 согласия пользователя → §42 соглашение владельца и owner-гейт (**BREAKING № 3**) → §43 публичные страницы документов → §44 согласие на фотофиксацию (**BREAKING № 4**) → §45 противопоказания → §46 запись: ст. 18 и подтверждение полномочий (**BREAKING № 5**) → §47 шаблоны (**BREAKING № 6**) → §48 обращения субъектов → §49 расширение выгрузки → §50 заявка на канал: ИНН (**BREAKING № 7**) → §51 затёртые строки журнала → §52 сводка изменений → §53 чек-лист BE↔FE. **Семь ломающих изменений пронумерованы в самом контракте** — удобная точка входа для любого, кто звал API по-старому |

⚖️ `API_DOCUMENTATION.md` **обновлён в цикле 5** (+212 строк): переработан §2.6 «Правовые документы и
согласия», добавлены `GET /api/legal/texts/{key}`, `GET|POST /api/profile/consents` и отдельный
подраздел про режим `T-24` (проверка согласия на передачу привлекаемому лицу).

🔬 **Контракт цикла 8** — `API_CONTRACT_CYCLE8.md` (~27 КБ, §82–§88) и
`API_CONTRACT_CYCLE8_PHASE2.md` (~17 КБ). Это **контракт не HTTP-эндпоинтов, а среды**: §83 прямо
фиксирует, что цикл не добавляет и не меняет ни одного эндпоинта; §85 — таблица переменных
окружения (`SB_*` для dev-стека и `SERVICEBOOKING_TEST_*` для прогона), §86 — CLI `TestKit` и схема
его JSON-вывода.

🗓 **Контракт цикла 6** — `API_CONTRACT_CYCLE6.md` (~59 КБ, **разделы 38–46**, та же схема
продолжения нумерации): §38 общее для всех эндпоинтов цикла (в т.ч. **§38.1 — форма тела 4xx**) →
§39 вход и различимые причины отказа → §40 признак «оказывает услуги» и список специалистов →
§41 время: слоты, доступность дней, горизонт → §42 тариф → §43 несколько услуг за визит →
§44 телефон → §45 TS-типы, которые фронтенд обязан обновить → §46 матрица «кто чего ждёт».

🔬 **Утверждение позапрошлой редакции «OpenAPI/Swagger-файла в репозитории нет» БОЛЬШЕ НЕ ВЕРНО.**
Цикл 8 положил первые машиночитаемые контракты, 🗓 цикл 6 — ещё один, **первый, который описывает
продуктовые эндпоинты, а не инвариант или среду**:

| Что | Путь | Формат | Структура |
|---|---|---|---|
| 🗓 Контракт цикла 6 | `openapi-cycle6.yaml` (~64 КБ) | **OpenAPI 3.0.3 (YAML)**, редакция 2 от 2026-09-22 | **12 путей** — только те, которых цикл касается: `/api/auth/login`, `/api/auth/register`, `/api/companies/{id}`, `/api/companies/{id}/masters`, `/api/companies/{id}/members`, `/api/companies/{id}/members/{memberId}/provides-services`, `/api/bookings/slots`, `/api/bookings/availability`, `/api/bookings`, `/api/bookings/{id}`, `/api/bookings/{id}/reschedule`, `/api/admin/owners/{ownerUserId}/subscription`. **Это НЕ полное описание API** — сказано в шапке файла; остальные 18 контроллеров цикл не трогает, они в `API_DOCUMENTATION.md` и контрактах циклов 3–5. В шапке же зафиксирована **конвенция тел ошибок** (голая строка `text/plain`; исключение — массив Identity на регистрации) и список готовых команд проверки. Смысловые правила, которые схемой не выражаются, живут в `API_CONTRACT_CYCLE6.md` — файлы читаются вместе. ⚠️ **Шага в CI нет**, проверка ручная |
| 🔬 Инвариант HTTP-поверхности | `contracts/cycle8/servicebooking-invariant.openapi.yaml` (~24 КБ) | **OpenAPI 3.1 (YAML)** | `info` (version `8.0.0-invariant`) → `servers` → `paths` для **семи** операций, отобранных по признаку «цикл 8 технически способен это сломать»: `/api/health/live`, `/api/health/ready`, `/api/legal/documents`, `/api/auth/register`, `/api/auth/login`, `/api/profile/avatar`, `/uploads/{path}`. Снят с `7b382d8`. **Это НЕ полное описание API** — записано прямо в шапке файла; полное по-прежнему `API_DOCUMENTATION.md` + контракты циклов + живой Swagger. Версии правовых документов в схему **сознательно не зашиты** (чтобы не воспроизвести дефект §9 L6). Проверяется вручную: `npx @redocly/cli lint …`, `pipx run schemathesis run … --base-url …`; ⚠️ **шага в CI нет** (§9 T8-5) |
| 🔬 Схема вывода CLI TestKit | `contracts/cycle8/testkit-status.schema.json` (~17 КБ) | **JSON Schema** | описание `--json` для `status`/`sweep`/`doctor`: `schemaVersion`, ресурсы (`kind` container/database/directory, `runKey`, `slot`, `testClass`, `workdir`, возраст, живость), проверки доктора, блок `parallelism`. ⚠️ Поле `testClass` схема допускает, но код **всегда пишет `null`** (§9 T8-2, M3), а `pattern` слота ограничен `c[0-9]{2,3}` (§9 T8-2, L5) |

💳 **Контракт цикла 7 — `API_CONTRACT_CYCLE7.md`** (~73 КБ, Markdown, русский, редакция 2.1)
**плюс `contracts/cycle7/openapi.yaml`** (~1 905 строк, OpenAPI YAML) — **третий машиночитаемый
контракт** и первый, из которого **реально генерируются типы фронтенда**:

| Что | Путь | Формат | Структура |
|---|---|---|---|
| 📸 Контракт цикла 10 (смысловой) | `API_CONTRACT_CYCLE10.md` (~34 КБ) | Markdown, русский | **§120–§133**, нумерация продолжает общую схему. §120.1 — правило «всё новое добавочное», §121 `availability` (включая таблицу «`manual`/`extendedHours` — просьба, а не разрешение» и таблицу «как это читать интерфейсу»), §122 история и правило неразглашения, §123 `historyEventCount`, §124 `masters`/`includeHidden`, §125–§128 фото салона, §129 `CompanyDto`, §130 **чего цикл НЕ меняет**, §131 разбор ответов для фронта, §132 автосверка для QA, §133 сводная таблица |
| 📸 Контракт цикла 10 (машиночитаемый) | `contracts/cycle10/openapi.yaml` (807 строк) | **OpenAPI (YAML)**, **четвёртый** машиночитаемый контракт | Десять путей: `/bookings/availability`, `/bookings/{id}/history`, `/bookings/{id}`, `/bookings/master`, `/companies/{id}/masters`, `/companies/{id}/photos`, `/companies/{id}/photos/{photoId}`, `/companies/{id}/photos/order`, `/companies/{slug}`, `/companies`. **Это НЕ полное описание API.** Из него генерируются типы — `npm run types:api:cycle10` → `src/types/api-cycle10.generated.ts`. ⚠️ **Шага в CI нет**, сверка ручная |
| 💳 Контракт цикла 7 (смысловой) | `API_CONTRACT_CYCLE7.md` (~73 КБ) | Markdown, русский, редакция 2.1 | Продолжение той же схемы нумерации. Описывает биллинг-аккаунты, каталог опций, матрицу «тариф × опция», заявки владельца, перенос компаний, витрину цен и **отзыв `PUT /api/admin/owners/{id}/subscription` в `410 Gone`**. Смысловые правила, не выражаемые схемой, живут здесь — файлы читаются вместе с YAML |
| 💳 Контракт цикла 7 (машиночитаемый) | `contracts/cycle7/openapi.yaml` (~1 905 строк) | **OpenAPI (YAML)** | Пути биллинга: `/api/billing/subscription(/request)`, `/api/admin/options(/{id})`, `/api/admin/option-capabilities`, `/api/admin/billing-accounts(/{id}/subscription\|/subscription-history)`, `/api/admin/subscription-requests(/{id}/reject)`, `/api/admin/companies/{id}/transfer(/preview)`, `/api/admin/companies/{id}/owner-history`, `/api/pricing`, `/api/admin/pricing/preview`, плюс отозванный маршрут с ответом `410` и телом `text/plain`. **Это НЕ полное описание API.** ⚠️ **Шага в CI нет** |

Postman-коллекции по-прежнему нет. 💳 **Утверждение «генерации TS-типов из схемы нет» с цикла 7
БОЛЬШЕ НЕ ВЕРНО, но только для биллинга:** `npm run types:api` генерирует
`frontend/src/types/api-cycle7.generated.ts` из `contracts/cycle7/openapi.yaml`, и фронтенд берёт
оттуда перечисления `SubscriptionStatus` и `OptionAvailability`. Для всего остального API типы
по-прежнему дублируются руками в `src/types/index.ts`. ⚠️ Скрипт **в CI не вызывается** — расхождение
контракта и сгенерированного файла ничем не проверяется (§1, §7).
Цикл 8 генерации не заводил (осознанно не трогал HTTP-контракт), 🗓 а цикл 6 использует
`openapi-typescript` только как **проверку** (сгенерировать во временный файл и прогнать `tsc`).

💳 ⚠️ **`API_DOCUMENTATION.md` циклом 7 обновлён ровно в одном месте** (−28 строк): раздел про
`PUT /api/admin/owners/{ownerUserId}/subscription` заменён отметкой «отозван в цикле 7, отвечает
`410 Gone`, замена — `PUT /api/admin/billing-accounts/{accountId}/subscription`». **Разделов про
сами эндпоинты биллинга в справочнике нет** — отметка про замену это прямо признаёт («отдельного
раздела в этом документе для него пока нет»). Источник истины по биллингу —
`contracts/cycle7/openapi.yaml` + `API_CONTRACT_CYCLE7.md`.

⚠️ 📸 **`API_DOCUMENTATION.md` циклом 10 обновлён только в текстах ошибок загрузки** (§5.0-ter).
Справочник **не знает** ни про `GET /api/bookings/{id}/history`, ни про четыре маршрута фотографий
компании, ни про `extendedHours`/`staffMode`/`scheduleState`, ни про `includeHidden`. Источник истины
по эндпоинтам цикла 10 — `contracts/cycle10/openapi.yaml` + `API_CONTRACT_CYCLE10.md`.

⚠️ 🗓 **`API_DOCUMENTATION.md` циклом 6 не обновлён.** Справочник (~259 КБ) не знает ни про
`GET /api/bookings/availability`, ни про `serviceIds`, ни про `provides-services`, ни про
диагностику тарифа, ни про новые коды ответа входа. На время, пока это так, **источник истины по
эндпоинтам цикла 6 — `openapi-cycle6.yaml` + `API_CONTRACT_CYCLE6.md`**, а не справочник.

### 10.4 Описания тест-кейсов

**`TEST_CATALOG.md`** (💳 ~459 КБ, было ~304 КБ), Markdown, русский — человекочитаемое описание
**каждого** автоматизированного кейса, отдельно от самого кода тестов. **Это единственное место в
проекте, где тест-кейсы описаны текстом**; отдельного `TESTPLAN.md` или каталога `docs/testing/`
с кейсами нет (`docs/testing-isolation.md` — про механику прогона, а не про сценарии).

📸 **Цикл 10 добавил в каталог +117 строк одним разделом-«прогоном»** —
`## Cycle 10 (US-120…US-126, master booking freedom / история изменений записи / фото салона) —
приёмка QA`, со своими подразделами «Числа», «Баг, найденный этим прогоном (адресовано
backend-developer, не блокер) — ИСПРАВЛЕНО» и «Вердикт по критериям приёмки». То есть это опять
частично **журнал прогона**, а не только каталог кейсов (та же оговорка, что у цикла 7 ниже).
Новые префиксы кейсов: **`BK-`** (свобода ручной записи, `ManualBookingFreedomTests.cs`),
**`BKH-`** (история, `BookingHistoryTests.cs`), **`CPH-`** (фото салона, `CompanyPhotosTests.cs`).

💳 **Цикл 7 добавил в каталог +1 402 строки** — больше, чем любой предыдущий цикл, и структурно
иначе: **не только по месту и не одним разделом, а несколькими разделами-«прогонами»**, идущими
хронологически. Это важно знать, прежде чем туда дописывать:
- `## Pricing (US-71, US-72, цикл 7 — понятная модель тарифов и опций)` — доменный раздел витрины;
- `## Починка красного набора после 5dcaf3f` — разбор падений, классы причин, итог по числам;
- `## Приёмка QA цикла 7 после завершения всех 6 этапов` — новые наборы **`BillingTests.cs` (префикс
  `BLL-`)** и **`CompanyTransferTests.cs` (префикс `TRF-`)**, контрактная проверка `schemathesis`
  против `contracts/cycle7/openapi.yaml`, сверка `GET /api/billing/subscription` с уже написанным
  фронтом, найденные блокеры US-77/US-67;
- `## Финальный проход перед мерджем в develop` — в т.ч. три теста про системный бесплатный тариф,
  переписанные под новый маршрут (см. §9 B6, B7).
По месту, внутрь существующих разделов, добавлены `ADM-050…ADM-053` (валидация `IsSystemFree`,
сохранение `Highlights`/`IsPublic`/`SortOrder`) и **`ADM-048`/`ADM-049`** (упразднённые эндпоинты
безусловно отвечают `410 Gone` с адресом замены); `ADM-017`, `ADM-033`, `ADM-036` помечены
удалёнными.
⚠️ Разделы-прогоны содержат **вердикты и найденные баги конкретного прогона**, то есть частично это
журнал, а не только каталог кейсов. Читать их как «описание того, что покрыто» можно, но с оглядкой
на дату прогона.

🗓 **Цикл 6 добавил в каталог +190 строк**, и, в отличие от цикла 5, **не отдельным разделом в
конце, а по месту** — внутрь существующих доменных разделов, рядом с кейсами, которые они
дополняют или заменяют. Новое: `AUTH-014` (400-массив Identity на слабый пароль), `AUTH-015`
(вход аккаунта с иностранным номером — регрессия, которую цикл едва не создал), переписанный
`BK-024` (окно 09:00–21:00 вместо суток) и `BK-057` (`extendedHours`), три новых подраздела —
**«`GET /api/bookings/slots?excludeBookingId=…` — сетка переноса записи»** (`BK-068…BK-073`,
включая проверку порядка «права → совпадение пары»), **«Горизонт записи»** (`BK-058`, `BK-059`) и
**«Несколько услуг за визит»** (`BK-060…BK-067`), плюс `ADM-045`. Заголовки кейсов цикла 6 содержат
ссылки на `ARCHITECTURE_CYCLE6.md`/`API_CONTRACT_CYCLE6.md` с номерами разделов — по ним быстро
находится, какое решение кейс защищает.

⚖️ **Цикл 5 добавил раздел «Legal, цикл 5 (US-64…US-82, правовые основания продукта) — приёмка QA»**
(+128 строк). Его структура отличается от разделов прошлых циклов и это существенно: он начинается с
**контекста** (список функциональных файлов, которые цикл ожидал сломать, зафиксированный заранее в
`ARCHITECTURE_CYCLE5.md` §55.2), затем идёт **«Переработка существующих `LEG-`»** — то есть
перечисление того, **что и почему пришлось переписать** в уже существовавших тестах
(`LegalConsentTests` целиком, `ClientNotePhotosTests`, `NotificationChannelsTests`,
`NotificationQueueingTests`, `DataRightsTests`, `SchedulerTests`), затем **новые `LGL-` тесты
приоритетных пунктов разбора** и отдельно **«Инфраструктурные находки QA»** — правки в
`ServiceBooking.Tests/Infrastructure/`, которые тестами не являются.

🆕 **Цикл 4 добавил раздел `## Notifications (US-27…US-63)`** с подразделами по файлам и указанием
**коллекции**, в которой живёт каждый набор (это существенно — см. §9, урок про общие ресурсы):
`NotificationChannelsTests` (`NTF-C001…C018`, своя фабрика на тест), `NotificationQueueingTests`
(`NTF-Q001…Q004`, коллекция `"Api"` — 🔬 распущена в цикле 8), `NotificationWebhookUnsubscribeTests`
(`NTF-W*`/`NTF-U*`/`NTF-L*`), `NotificationDispatchTests` и `NotificationDispatchExtraTests`
(`NTF-D01…D05`, коллекция `"NotificationDispatch"`), `NotificationCitiesTimeZoneTests`
(`NTF-G001…G006`). В конце раздела — **явный подраздел «Не покрыто функциональными тестами этого
прогона (зафиксировано, не блокер)»**.

- Структура: «Как устроены ссылки на тесты» → «Префиксы по доменам» → **«Юнит-тесты (без БД)»** →
  далее раздел на домен (`Auth`, `Bookings`, `Companies`, `Services`, `WorkingHours`,
  `ScheduleTemplate`, `Reviews`, `Mailing`, `Masters`, `ClientNotePhotos (цикл 2)`, `Scheduler (цикл 2)`,
  `Admin`, `Profile`, `Reports`) и разделы цикла 3: **`Legal (US-36…US-39)`**,
  **`Security / Rate limiting (US-42, US-46)`**, **`Health (US-43)`**, **`Pagination (US-49)`**,
  **«Регрессии продукта, найденные QA при разборе следствий пагинации — ЗАКРЫТЫ»**.
- Разделы цикла 3 помечены «найдено QA» — функциональные тесты правового контура, rate limiting,
  health и пагинации писал QA, а не разработчики.
- Связь с кодом — через стабильный ID из атрибута `[TestCase("PREFIX-NNN")]`.
  Поиск кейса по ID: `grep -rn "BK-003" ServiceBooking.Tests/`.
- ⚠️ **Раздел каталога «Документация, не обновлённая вместе с кодом» (строка 2237) устарел дважды.**
  Он утверждает, что `API_DOCUMENTATION.md` не тронут «в этом цикле» ни одной строкой. Речь про
  **цикл 3**, и уже тогда это перестало быть правдой (коммит `90a69b1`); 🆕 в цикле 4 справочник
  обновлён ещё раз (+116 строк). Замечание из каталога так и не убрали — читать его как
  актуальное нельзя.

🔬 **Цикл 8 правил `TEST_CATALOG.md` только технически** (+20/−17 строк): преамбула и раздел
«как запустить» больше не обещают заранее поднятую PostgreSQL и базу `servicebooking_test`, вместо
этого — Docker и `sbtest_<ключ прогона>_<слот>`. **Описания кейсов не менялись**: набор
функциональных тестов цикл не трогал (465 до и после). Нового раздела цикла 8 в каталоге нет, и это
корректно — новых кейсов цикл не добавил, а 100 его юнит-тестов покрывают инфраструктуру, которую
каталог не описывает.

Отдельного `TESTPLAN.md`, каталога `docs/testing/` или ручных сценариев вне `TEST_CATALOG.md`
в проекте **по-прежнему не найдено** (🗓 перепроверено на `aac6231`; ни цикл 8, ни цикл 6 такого
каталога не заводили). 🔬 Ближайшее к «описанию тест-плана» из нового — **`ARCHITECTURE_CYCLE8_PHASE2.md` §98.2
(критерии приёмки числами) и §98.4 (фактический протокол приёмки: таблица из 10 прогонов с семенами,
временами и результатами)**, плюс §99.5 — **список приёмочных грепов** (проверок кодовой базы
командами `grep`, а не тестами). Это не каталог кейсов, но это единственное место, где записано,
чем именно цикл 8 доказывал свою приёмку; ⚠️ греп № P1 из §99.5 сейчас не проходит буквально
(§9 T8-2, L6).

🚀 **Ручной чек-лист живых проверок теперь существует** — это `DEPLOY.md` §16 «Первый запуск на этой
машине — что проверить вживую». Формат: markdown-чеклист (`- [x]`), семь пунктов, у каждого дата,
вердикт (`ПРОШЛО` / `НЕ ПРОШЛА` + повторная проверка после фикса) и описание найденного дефекта.
**Все семь закрыты, незакрытых ноль.** Это единственный в проекте документ с описанием *ручных*
сценариев; `TEST_CATALOG.md` описывает только автоматизированные кейсы. Отдельного набора
e2e/браузерных автотестов (Playwright, Cypress и т.п.) в репозитории **нет** — соответствующего
пакета в `frontend/package.json` и каталога с такими тестами не найдено.

### 10.5 Документы цикла работ

| Документ | Размер | Что это |
|---|---|---|
| 📸 **`SPEC_CYCLE10_MASTER_BOOKING_HISTORY_PHOTO.md`** | ~55 КБ | **ЦИКЛ 10** «свобода ручной записи, история изменений записи, фото салона», **редакция 3**, истории **US-120…US-126** (нумерация начата со 120 специально — US-60…US-78 и US-101 заняты циклами 5–8). **§0 — решения заказчика П1…П8**, на них ссылается код и этот документ; §0.1 — открытый вопрос к юристу (§9 D1, D2); §0.2 — разбор причины дефекта и решение «один экран записи на два входа» |
| 📸 **`ARCHITECTURE_CYCLE10.md`** | ~87 КБ | **ЦИКЛ 10, разделы §100–§117.** Ключевые ссылки из кода: **§102 модель данных** (§102.1 журнал и его единственный писатель, §102.2 фото и почему уникальность позиции держит сервер, а не индекс), §103 серверная часть блока A (§103.2 `scheduleState`, §103.3 `staffMode`, §103.5 `includeHidden`), §104–§107 журнал/фото/ретенция (**§105** шесть точек вызова журнала, **§106** конвейер фото и осиротевшие файлы, **§107** правило уничтожения без срока), **§108 слияние двух модалок** (самая рискованная часть цикла), §109 фронт блоков B и C, §110 что добавляется и удаляется, §113 как проверяется, что ничего не сломано, §115 совместимость, **§116 риски (R6 — 13 невыкаченных миграций)** |
| 📸 **`API_CONTRACT_CYCLE10.md` / `contracts/cycle10/openapi.yaml`** | ~34 КБ / 807 строк | **ЦИКЛ 10, §120–§133** + машиночитаемая схема, см. §10.3. Читаются вместе. §121 `availability`, §122 история, §123 `historyEventCount`, §124 `masters`, §125–§128 фото, §129 `CompanyDto`, **§130 чего цикл НЕ меняет**, §131 что обязан проверять фронт, §132 автосверка для QA, §133 сводная таблица изменений API. Типы фронта — `npm run types:api:cycle10` |
| 💳 **`SPEC_CYCLE7_PRICING.md`** | ~70 КБ | **ЦИКЛ 7** «биллинг-аккаунты и понятная модель тарифов», истории **US-64…US-67, US-70, US-71, US-72, US-73, US-74, US-77**. ⚠️ **Номера историй пересекаются с циклом 5** — следствие коллизии имён (§9 B10); «US-65» без указания цикла неоднозначно. **§7 — требование не публиковать цены до вычитки юристом**, на нём держится выключенный по умолчанию рубильник `pricing.public-enabled` (§9 B8) |
| 💳 **`ARCHITECTURE_CYCLE7.md`** | ~149 КБ | **ЦИКЛ 7, разделы 41–56.** ⚠️ **Нумерация разделов пересекается с `ARCHITECTURE_CYCLE5.md` и `ARCHITECTURE_CYCLE6.md`** — ссылаться только с именем файла (§9 B10). Ключевые ссылки из кода: **§43 модель данных** (§43.3 опции и матрица, §43.4 подписка аккаунта, §43.5/§43.6 журналы и co-tenancy-ограничения), **§44 расчёт цены и возможностей** (§44.1–44.3), §45.1 чтение денег через `BillingAccountId`, **§46 лимит сотрудников по аккаунту** (§46.4 разбор отказа), §47 финансирование каналов за номер (§47.1/§47.2; §47.4 — отменённое поведение цикла 4), §49 заявки владельца, §50 смена ответственного, **§51 перенос компании** (§51.1/§51.2 правила отказа), §52 порядок advisory-lock'ов, §53.4 разбор `SeatLimitReached`, §54 миграция и сверка (**§54.1 предпроверка, §54.2 backfill, §54.3 сид каталога, §54.5 приёмочный тест миграции — НЕ написан, §9 B3**) |
| 💳 **`API_CONTRACT_CYCLE7.md` / `contracts/cycle7/openapi.yaml`** | ~73 КБ / ~1 905 строк | **ЦИКЛ 7**, редакция 2.1 + машиночитаемая схема, см. §10.3. Читаются вместе. Из YAML генерируются типы фронтенда (`npm run types:api`) |
| 🗓 **`SPEC_CYCLE6_BOOKING_FIXES.md`** | ~88 КБ | **ЦИКЛ 6** «доработка сценария записи на услуги», истории **US-60…US-67**. **Заархивирована под этим именем сразу**, потому что `SPEC.md` занят циклом 8. ⚠️ **Ссылки на неё в коде и документах цикла 6 выглядят как `SPEC_CYCLE6_BOOKING_FIXES.md §0.1`** — §0.1 это раздел ответов заказчика (Q1…Q9), на него ссылаются `SlotCalculator`, `PhoneNormalizer`, `BookingsController` и обе миграции. Раньше эти ссылки указывали на `SPEC.md` и после архивирования вели в чужую спеку цикла 8 — см. §9 C5 |
| 🗓 **`ARCHITECTURE_CYCLE6.md`** | ~138 КБ | **ЦИКЛ 6, разделы 41–54.** Ключевые ссылки из кода: §41 почему стек не расширяется, §42 вход и исходы `LoginOutcome`, §43 тариф (§43.2 диагностика, §43.3 обязательная дата, §43.5 миграция backfill'а), **§44 модель данных цикла** (§44.2 `BookingService`), §45 доступность дней (**§45.7 горизонт записи**), **§46 рабочее окно и `ScheduleFallback`** (§46.2 три режима, §46.3 сетка переноса), §47 несколько услуг за визит (§47.2 текст уведомления), **§48 телефон: почему отдельный предикат, а не правка `IsValid`**, §49 пропуск шага выбора мастера, §50 что добавляется в структуру, §51 разбивка на задачи, §52 риски, **§53 как проверяется, что backend и frontend сошлись** (четыре команды поверх `openapi-cycle6.yaml`), §54 что цикл сознательно не делает |
| 🗓 **`API_CONTRACT_CYCLE6.md` / `openapi-cycle6.yaml`** | ~59 / ~64 КБ | **ЦИКЛ 6, разделы 38–46** + машиночитаемая схема (OpenAPI 3.0.3, 12 путей), см. §10.3. Читаются вместе: схема описывает форму, контракт — смысл |
| 🔬 **`SPEC.md`** | ~71 КБ | **ЦИКЛ 8** «изоляция тестовой и локальной среды». Истории US-83…US-101 (US-83…US-93 — фаза 1, изоляция прогонов; US-94…US-101 — фаза 2, параллелизм), §16 — вопросы архитектору |
| 🔬 **`ARCHITECTURE_CYCLE8.md`** | ~88 КБ | **ЦИКЛ 8, фаза 1, разделы 61–81.** Ключевые ссылки из кода: §65.1/§76 CLI TestKit, §66 жизненный цикл сервера прогона, §68 шаблон и слоты, §69.2/§69.3 переосмысление `SERVICEBOOKING_TEST_CONNECTION` и монополия на `DROP DATABASE`, §70 уборка (Ryuk, sweeper, критерий «мёртвости»), §71 `TestHostSettings` и временные корни, §71.4 `Logs:Directory`, §73.3 порты и Vite, §74 пины образов и CI, §75 параметры эфемерного Postgres, §79 приёмочные грепы фазы 1 |
| 🔬 **`ARCHITECTURE_CYCLE8_PHASE2.md`** | ~108 КБ | **ЦИКЛ 8, фаза 2, разделы 89–99.** §91 выбор единицы изоляции (база на тест-класс) и §91.5 три операционные ловушки клонирования, §92 механика xUnit (коллекция → `IClassFixture`), §93 параллелизм и бюджет соединений, §94 один механизм уникальности (`TestData`), §95.1 разбор статического состояния поимённо, §96 `Log.Logger` и правка `Program.cs`, **§98.4 протокол приёмки**, §99.5 приёмочные грепы, **§98.5 остаточная хрупкость (фиксированные паузы)** — ⚠️ физически лежит ПОСЛЕ §99 |
| 🔬 **`API_CONTRACT_CYCLE8.md` / `API_CONTRACT_CYCLE8_PHASE2.md`** | ~27 / ~17 КБ | **ЦИКЛ 8, разделы 82–88 и продолжение.** Контракт среды, а не эндпоинтов: §83 «ни одного изменения HTTP», §85 таблица переменных окружения, §86 CLI и схема JSON |
| 🔬 **`SPEC_CYCLE5_LEGAL.md`** | ~163 КБ | **сохранённая спека цикла 5** «правовые основания продукта» (истории US-64…US-82) — восстановлена первым действием цикла 8, потому что `SPEC.md` занят циклом 8 |
| ⚖️ **`ARCHITECTURE_CYCLE5.md`** | ~154 КБ | **ЦИКЛ 5, разделы 41–60.** Ключевые ссылки из кода: §41 принципы цикла, **§43 правовой контент: документы, тексты интерфейса, маршруты**, **§44 модель данных цикла** (§44.2 журнал согласий, §44.3 заметка о здоровье, §44.4 обращения, §44.7 ретенция), §45 где вычисляется «текущее согласие» и почему не кешируется, §46 экран регистрации и owner-гейт, §47 отзыв согласия, §48 спецкатегории и шифрование, **§49 ретенция: сухой прогон и затирание**, §50 права субъекта, §51 реклама в шаблонах, **§52 страна сервера и спорный гейт T-24**, §53–54 таблица оснований обработки, **§55 карта регрессионного риска**, §57 конфигурация и приёмочные грепы, §58 правовые ограничения, §59 расхождения и что нужно от заказчика, §60 карта ответов на §16 SPEC |
| ⚖️ **`API_CONTRACT_CYCLE5.md`** | ~52 КБ | **ЦИКЛ 5, разделы 38–53**, см. §10.3. Семь ломающих изменений пронумерованы |
| ⚖️ **`LEGAL_REVIEW.md`** | ~205 КБ | **Юридическое заключение по продукту, редакция 4** — отдельный жанр, которого в проекте раньше не было. Структура: §0 оговорка о статусе → §1 резюме для заказчика → §2 что фактически обрабатывает продукт → §3–8 ответы на семь вопросов (41-ФЗ, роли по 152-ФЗ, передача данных GREEN-API и трансграничность, правомерность рассылки, документы и платная опция, права субъекта) → **§9 «то, чего не было в списке, но что существеннее части списка»** → **§10 что блокирует выпуск, а что можно делать параллельно** → §11 задачи для разработки (вход в SPEC цикла 5) → **§12 действия заказчика, которые команда закрыть не может** → §13 развилки для заказчика (в т.ч. §13.5 таблица сроков хранения, из которой взяты дефолты `Retention`) → §13-бис где лежат тексты и в каком они статусе → §14 источники по состоянию на 2026-09-21 → §15 повторная оговорка. ⚠️ Это **не заключение практикующего юриста** — см. §0 самого документа и L3 в §9 |
| ⚖️ **`legal-drafts/`** | — | **Исходники правовых текстов под контролем версий** (не то, что читает приложение). Двенадцать HTML (`01-privacy-policy` … `12-guardian-confirmation`), `legal.json`, `legal.manifest.proposed.json` (**предлагаемый манифест, НЕ подключён**) и `README.md`, который объясняет разницу между тремя каталогами (`legal-drafts/` в git, `App_Data/legal/` запечён в образ, `legal/` только на машине и не в git), даёт команду поиска незаполненных плейсхолдеров и отсылает к разделу `LEGAL_REVIEW.md` «Что должен предоставить заказчик» |
| ⚖️ `SPEC_CYCLE4_NOTIFICATIONS.md` | ~282 КБ | **сохранённая спека цикла 4** — переехала сюда, потому что `SPEC.md` занят циклом 5 |
| 🆕 **`ARCHITECTURE_CYCLE4.md`** | ~165 КБ | **ЦИКЛ 4, разделы 21–40** — продолжение `ARCHITECTURE.md`. Ключевые ссылки из кода: §23 модель данных цикла, **§24 шифрование чужих секретов**, §25 слои и файлы, **§26 отправщик (главный вопрос цикла)**, §27 как отправщик тестируется, §28 адаптер провайдера, §29 QR, §30 состояние канала и простой, §31 оповещение владельца, §32 вебхук, §33 тариф, **§34 часовые пояса и города**, §35 миграции, §36 порядок работ, §37 конфигурация и «приёмочные грепы», **§38 расхождения со SPEC**, §39 риски, §40 карта ответов на §16 SPEC |
| 🆕 **`API_CONTRACT_CYCLE4.md`** | ~40 КБ | **ЦИКЛ 4, разделы 19–37**, см. §10.3 |
| `SPEC_CYCLE3_PRODUCTION.md` 🆕 | ~139 КБ | **сохранённая спека цикла 3** «готовность к продакшену» — переехала сюда, потому что `SPEC.md` занят циклом 4 |
| `ARCHITECTURE.md` | ~135 КБ | **цикл 3**, разделы **до 21**. Ключевые ссылки, на которые ссылается код: §4 правовые документы, §5 модель согласий, §6.3 451 и claim'ы, §7.3/§7.4/**§19.2** (почему удаление аккаунта — надгробие, а не `DELETE`), §8 IdentityRoleSync, §9 ForwardedHeaders, §10 health, §11 логирование и GlitchTip, §12 деплой/бэкап/откат, §15.1 пагинация, §17.1/§18.2 стиль |
| `API_CONTRACT.md` | ~51 КБ | **цикл 3**, разделы до 19, см. §10.3 |
| **`SPEC_DEFERRED_NOTIFICATIONS.md`** ⭐ | ~90 КБ | SPEC **отложенного** цикла уведомлений клиенту по телефону (MAX/SMS). Тема **отложена решением заказчика, не отменена**. Здесь же живёт пункт **Д-1 «Подтверждение нового номера телефона при смене»**, на который ссылается комментарий в `ProfileController.ChangePhone` |
| **`SPEC_APPENDIX_CHANNELS.md`** ⭐ | ~57 КБ | приложение к нему: исследование каналов доставки |

💳 ⚠️ **Коллизия имён, случившаяся в цикле 7, — практическое подтверждение хрупкости этой
конвенции, и её стоит прочесть целиком в §9 B10.** Коротко: документы цикла 7 изначально назвали
по **пятому** слоту, они столкнулись с документами правового цикла 5, их переименовали в седьмой
слот и **переписали 173 ссылки**. Два следствия, живущих в репозитории прямо сейчас:
- **тексты коммитов диапазона `aac6231..0929b48` говорят «cycle-5 / цикл 5», хотя это цикл 7**
  (`9bb8ec7`, `02c6404`, `8774466`, `de2ecbf`, `f29ec3a`, `066a3e1`, `c374e1a`, `5dcaf3f` и др.);
- **сквозная нумерация разделов не уникальна между циклами**: §43, §44, §54 существуют
  одновременно в `ARCHITECTURE_CYCLE5.md`, `ARCHITECTURE_CYCLE6.md` и `ARCHITECTURE_CYCLE7.md`.
  **Ссылки опираются на имя файла**, поэтому «§54.5» без имени файла — неоднозначная ссылка.
Номера историй тоже пересекаются: US-64…US-67 и US-70 есть и в цикле 5, и в цикле 7.

**Соглашения об архиве (`docs/history/`) в репозитории по-прежнему нет**, каталога такого нет, в
README оно не описано. 📸 **Цикл 10 его тоже не завёл** и следует общей конвенции суффикса, поэтому
`SPEC_CYCLE10_MASTER_BOOKING_HISTORY_PHOTO.md`, `ARCHITECTURE_CYCLE10.md` и
`API_CONTRACT_CYCLE10.md` **остаются в корне** — переносить их было некуда, а `SPEC.md`/
`ARCHITECTURE.md`/`API_CONTRACT.md` без суффикса цикл 10 не занимал и не перезаписывал (они
по-прежнему означают циклы 8 и 3). В корне теперь одновременно лежат документы **шести** циклов
(3, 4, 5, 6, 8, 10 — плюс 7). 💳 **Цикл 7 его тоже не завёл** и следует общей конвенции суффикса, поэтому
`SPEC_CYCLE7_PRICING.md`, `ARCHITECTURE_CYCLE7.md` и `API_CONTRACT_CYCLE7.md` **остаются в корне** —
переносить их было некуда. ⚖️ **Циклы 4, 5, 6 и 8 решают задачу суффиксом в имени файла**
(`*_CYCLE4.md`, `*_CYCLE5.md`, `*_CYCLE8.md`, `SPEC_CYCLE3_PRODUCTION.md`,
`SPEC_CYCLE4_NOTIFICATIONS.md`, 🔬 `SPEC_CYCLE5_LEGAL.md`, 💳 `SPEC_CYCLE7_PRICING.md`), так что
документы **пяти последних
циклов одновременно лежат в корне**, а `SPEC.md` без суффикса означает 🔬 **цикл 8**, тогда как
`ARCHITECTURE.md`/`API_CONTRACT.md` без суффикса — **цикл 3**. Это следует иметь в виду при любой
ссылке «см. SPEC» или «см. §26».
🔬 Документы цикла 8 **никуда не переносились**: каталога `docs/history/` в проекте нет, конвенция
проекта — суффикс, и документы цикла 8 уже ему следуют (`ARCHITECTURE_CYCLE8*.md`,
`API_CONTRACT_CYCLE8*.md`). Единственное, что цикл 8 действительно «заархивировал» — спеку цикла 5,
восстановив её из git в `SPEC_CYCLE5_LEGAL.md` перед тем, как занять `SPEC.md` собой. Поэтому эта
редакция `CURRENT_STATE.md`, как и прошлая, ничего не переносила.

🗓 **Цикл 6 архивировал свою спеку сам** — `SPEC_CYCLE6_BOOKING_FIXES.md`, потому что `SPEC.md`
к тому моменту занял цикл 8. В корне таким образом одновременно лежат документы **пяти** циклов
(3, 4, 5, 6, 8), и `SPEC.md` без суффикса означает цикл 8.

⚠️ 🗓 **К конвенции суффикса нужно добавить явное требование, которого в ней сейчас нет:
переименование спеки обязано включать обновление всех ссылок на неё — в том же коммите.**
Цикл 6 на этом обжёгся: 43 ссылки вида «`SPEC.md` §0.1» остались висеть после архивирования и
стали указывать на спеку цикла 8, у которой **есть свой §0.1 и свой Q7**. Ссылка не ломается, она
приводит в связный, но совершенно чужой текст — и это **хуже битой ссылки**, потому что читатель не
замечает подмены. Исправлено коммитом `aac6231`; ссылки, принадлежащие другим циклам, тронуты не
были — их `SPEC.md` означал свою спеку на момент написания. Подробнее — §9 C5.
Практическая проверка перед архивированием спеки:
`grep -rn "SPEC\.md" --include="*.cs" --include="*.ts" --include="*.tsx" --include="*.md" .`
Предыдущие редакции живут только в git-истории: SPEC цикла 2 — `git show 0492092:SPEC.md`,
цикла 1 — `git show e6b746c:SPEC.md`, ещё более ранняя — `7c86ca2`.
