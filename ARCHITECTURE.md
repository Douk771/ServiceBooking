# ARCHITECTURE — цикл B ServiceBooking: доводка недоделанного и фото к работе с клиентом

**Вход:** `SPEC.md` цикла B (решения заказчика Q5–Q18 в §0 — окончательные), `CURRENT_STATE.md`
(снимок до цикла A; его §4 местами устарел — все факты ниже перепроверены чтением кода),
`ARCHITECTURE.md`/`API_CONTRACT.md` цикла A (эта ревизия их **заменяет**, сохраняя всё, что не меняется).
**Тип работ:** расширение существующей кодовой базы. Стек не выбирается заново — он **дополняется**
ровно двумя библиотеками, разрешёнными SPEC §9 п. 9.
**Ветка:** `sanitation-cycle`, база — `1c21bca`.
**Baseline, который нельзя ухудшать:** `dotnet build ServiceBooking.sln` → 0 ошибок;
`dotnet test ServiceBooking.Tests` → зелёный (283 сценария по `TEST_CATALOG.md`);
`dotnet test ServiceBooking.UnitTests` → зелёный, < 10 с, без PostgreSQL; `npx tsc --noEmit` → чисто.

Документ закрывает **все девять вопросов из §12 SPEC** и даёт план, по которому backend- и
frontend-разработчик работают параллельно по `API_CONTRACT.md`, не читая код друг друга.

### Что перенесено из цикла A без пересмотра

| Что | Где было | Статус |
|---|---|---|
| **Формат тел ошибок: голая строка для 4xx, `ProblemDetails` только для 500** | цикл A §6.1 | **Перенесено дословно** — §13. Распространяется на все новые коды цикла B, включая `429` |
| Конвенции кодовой базы (`CURRENT_STATE` §6) | цикл A §0.1 | Не пересматриваются (SPEC §9 п. 9) |
| `pg_advisory_xact_lock` для любого check-then-act | цикл A §0.1, `Services/AdvisoryLock.cs` | Переиспользуется; **добавляется один неблокирующий вариант** (§8.4) |
| `CompanyMembership.IsStaffAsync` / `IsOwnerAsync` как единственный SQL-предикат членства | цикл A §2.1 | Переиспользуется всеми новыми проверками прав |
| `SubscriptionResolver.Resolve` + батчевое разрешение планов без N+1 | цикл A §2.4 | Переиспользуется; новые поля тарифа едут **тем же путём** (§7) |
| Отдельный проект `ServiceBooking.UnitTests` (без БД, < 10 с) | цикл A §1.1 | Существует; принимает новые юнит-наборы цикла B |
| Swagger только в Development, fail-fast на плейсхолдерах в Production | цикл A §7 | Не трогается; fail-fast **дополняется одной проверкой** (§3.4) |
| CI `.github/workflows/ci.yml`, два независимых джоба | цикл A §8 | Дополняется двумя шагами (§16) |
| Решение о `vitest.config.ts` отдельным файлом, без глобалов | цикл A §1.2 (сохранено в git-истории «до цикла B») | **Активируется** — §15 |

### Что в этом документе новое

Всё остальное: два класса хранения файлов, безопасный загрузчик изображений, модель фото,
учёт квоты, инфраструктура периодических задач, rate limiting, каноническая форма телефона,
фронтовый тест-раннер, пять миграций и разбивка работ.

Раздел §21 — расхождения со SPEC и решения, принятые архитектором сверх его буквы. Читать обязательно:
четыре из них меняют состав работ.

---

## 1. Принципы цикла B

1. **Конвенции не пересматриваются** (SPEC §9 п. 9). Primary constructors, `record`-DTO, ручной
   `MapToDto`, приватные асинхронные предикаты прав внутри контроллеров, 402 для тарифных гейтов,
   транзакция + advisory lock для check-then-act, комментарии «почему» на английском; на фронте —
   именованные экспорты, слой `src/api/<домен>.ts`, react-query, мапперы `src/utils/*Error.ts`.
2. **Новых библиотек ровно две** — обработка изображений (§2.1) и фронтовый тест-раннер (§2.2).
   Планировщик пишется на встроенном `BackgroundService`, rate limiting — на встроенном
   `Microsoft.AspNetCore.RateLimiting` (часть shared framework, **не** NuGet-пакет).
3. **Приватность — жёсткая граница, а не настройка.** Ни один байт фото клиента не проходит через
   `UseStaticFiles`. Разделение публичного и приватного класса хранения проведено **типами**, а не
   договорённостью (§3.2): приватный загрузчик физически не умеет вернуть URL.
4. **Порядок «файл ↔ БД» задан в обе стороны** (SPEC §9 п. 3 задавал только удаление):
   удаление — «сначала БД, потом диск»; загрузка — «сначала диск, потом БД».
   В обоих случаях худший исход — файл-сирота, который подберёт фоновая уборка. Строка без файла
   недопустима ни при каком порядке отказа.
5. **Ломающие миграции допустимы** (SPEC, преамбула; `MEMORY`): сервис не в продакшене. Но правило
   разрешения коллизий телефонов детерминировано и остановит миграцию там, где решение неочевидно
   (§14.5) — «допустимо ломать» не значит «удалять молча».
6. **Ломающих изменений контракта ровно четыре** (SPEC §9 п. 7). Пятого не появляется: всё остальное —
   аддитивно. Проверяется сводной таблицей `API_CONTRACT.md` §20.
7. **Все новые выборки ограничены** (`Take`) и не тянут таблицу целиком (SPEC §9 п. 6). Занятый объём
   считается по БД, не обходом диска.
8. **Новых слоёв и абстракций не заводим.** Новые файлы кладутся в существующие каталоги; единственная
   новая подпапка — `ServiceBooking.API/Services/Scheduling/`, и она оправдана тем, что планировщик —
   это четыре файла с одной темой, а не один помощник.

---

## 2. Стек: что добавляется

### 2.1 Обработка изображений — **SkiaSharp** (ответ на §12 п. 1)

**Решение: `SkiaSharp` 2.88.x + `SkiaSharp.NativeAssets.Linux.NoDependencies` 2.88.x
(та же версия, зафиксированная точно, как все пакеты проекта).**

| Кандидат | Лицензия | Нативные зависимости в `aspnet:8.0` | Вердикт |
|---|---|---|---|
| **SkiaSharp** | **MIT** (биндинги), Skia — BSD-3 | нет, если брать `NativeAssets.Linux.NoDependencies`: `libSkiaSharp.so` собран без `libfontconfig`. Текста мы не рисуем | **Выбран** |
| SixLabors.ImageSharp 3.x | **Six Labors Split License**: бесплатно для OSS и компаний с выручкой < $1 млн, иначе — платно | нет | Отклонён: продукт продаётся как SaaS, порог выручки — отложенный счёт, а не лицензия. Риск R9 в чистом виде |
| SixLabors.ImageSharp 2.1.x | Apache-2.0 | нет | Отклонён: ветка не развивается, security-фиксы уехали в 3.x под новой лицензией |
| Magick.NET | Apache-2.0 | тянет ~40 МБ нативных бинарей | Отклонён: вес и площадь атаки несопоставимы с задачей «уменьшить картинку» |
| `System.Drawing.Common` | MIT | — | Отклонён: с .NET 7 не поддерживается вне Windows (`PlatformNotSupportedException`) |

Почему это закрывает риск R9 целиком:
- **лицензия без порогов и без вопросов при продаже продукта** — единственный критерий, по которому
  ImageSharp 3.x отпадает независимо от его технических достоинств;
- **Dockerfile не меняется.** `NoDependencies`-пакет кладёт `libSkiaSharp.so` в `runtimes/linux-x64/native/`
  внутри публикации; базовый образ `mcr.microsoft.com/dotnet/aspnet:8.0` — Debian bookworm (glibc),
  которому этот бинарь и предназначен. **Ни одной строки `apt-get` не добавляется.**
  Ограничение фиксируется явно: **тег базового образа нельзя менять на `-alpine`** без замены пакета
  нативных ассетов на musl-вариант; в `Dockerfile` рядом с `FROM` ставится комментарий об этом;
- **второй контур деплоя работает без правок**: `SkiaSharp` тянет win-x64-натив из основного пакета,
  Windows/IIS-контур (`DEPLOY-windows.md`) получает его штатной публикацией;
- **проверка — сборка образа, а не `dotnet run`** (требование §12 п. 1): критерий готовности задачи
  T-B2 — `docker compose -f docker-compose.yml build api` проходит **и** контейнер отвечает `201`
  на реальную загрузку. Локальный прогон на macOS не засчитывается.

Что SkiaSharp даёт бесплатно и почему это важно:
- **удаление EXIF — свойство пересохранения, а не отдельный шаг.** Мы декодируем в пиксельный битмап
  и кодируем заново; кодировщики Skia пишут только пиксели. Метаданных, геометок и модели устройства
  в выходном файле не существует физически (US-19 п. 3);
- **обезвреживание полиглотов** — тот же эффект: на диск попадают только перекодированные байты;
- **`SKCodec.EncodedOrigin`** — единственная метаданная, которую **обязательно** прочитать до того, как
  выбросить остальные (§4.3).

### 2.2 Фронтовый тест-раннер — Vitest + jsdom + Testing Library

Состав, конфигурация и покрытие — §15 (ответ на §12 п. 9).

### 2.3 Что библиотекой **не** становится

- **Планировщик** — встроенный `BackgroundService` (§8, обоснование отказа от Hangfire/Quartz — §8.2).
- **Rate limiting** — встроенный `Microsoft.AspNetCore.RateLimiting` (§10). Он входит в
  `Microsoft.AspNetCore.App` shared framework: `PackageReference` не появляется, `csproj` не меняется.
- **Валидация телефона** — своя чистая функция (§11). Никаких `libphonenumber-csharp`: правило из
  SPEC §7.1 п. 2 занимает 15 строк, а библиотека принесла бы свою базу кодов стран и свои обновления.

---

## 3. Два класса хранения файлов (ответ на §12 п. 2)

### 3.1 Где проходит граница

| | **Публичный класс** | **Приватный класс** |
|---|---|---|
| Что лежит | логотип компании, аватар пользователя, картинка услуги | фото к заметке о клиенте |
| Кто смотрит | кто угодно, включая анонима на витрине | только `Master`/`CompanyOwner` **этой** компании |
| Корень | `<ContentRoot>/wwwroot/uploads/<area>/` | `Storage:PrivateRoot` — **вне `wwwroot`**, из конфигурации |
| Как отдаётся | `app.UseStaticFiles()`, прямая ссылка `/uploads/...` | **только** `GET /api/client-notes/photos/{id}` с проверкой прав |
| Что хранится в БД | **URL** (`/uploads/companies/<guid>.png`) | **ключ хранения** (`<companyId>/<guid>.jpg`), URL не существует |
| В квоту тарифа | **не входит** (US-25 п. 8) | входит (US-19 п. 6) |
| Лимит на сущность | один файл, старый удаляется при замене | 5 фото на заметку + квота компании |

### 3.2 Как один загрузчик обслуживает оба класса

Граница проведена **типом результата**, а не параметром-флагом, — чтобы приватный ключ было
невозможно случайно положить в поле DTO, которое фронт подставит в `src`:

```
ServiceBooking.API/Services/
├── ImageSignature.cs      static bool TryDetect(ReadOnlySpan<byte>, out ImageKind)   ← чистая
├── ImageProcessor.cs      static ProcessedImage Process(byte[] src, ImageProfile p)  ← чистая
├── FileStorage.cs         роутинг по классам, запись/чтение/удаление, свободное место
└── ImageUploadService.cs  оркестрация: сигнатура → обработка → квота → запись → строка
```

- `ImageSignature` и `ImageProcessor` **не знают ни про диск, ни про EF, ни про HTTP** — обе покрываются
  юнит-тестами в `ServiceBooking.UnitTests` без PostgreSQL (DoD п. 3).
- `FileStorage` — единственное место в решении, знающее слово `wwwroot`. Публичные методы:
  ```
  Task<string> SavePublicAsync(PublicArea area, byte[] bytes, string ext)  → возвращает URL   "/uploads/..."
  Task<string> SavePrivateAsync(Guid companyId, byte[] bytes, string ext)  → возвращает ключ  "<companyId>/<guid>.jpg"
  Task<Stream> OpenPrivateAsync(string storageKey)
  void DeletePublic(string url)   /  void DeletePrivate(string storageKey)
  bool HasFreeSpace(long neededBytes)
  ```
  Два метода записи, два разных возвращаемых значения, ни одной ветки `if (isPublic)` в вызывающем коде.
  Общая часть (проверка сигнатуры, ресайз, удаление EXIF, лимит 5 МБ, rate limiting) живёт **выше**, в
  `ImageUploadService`, и одинакова для обоих классов — требование US-25 п. 2 выполняется структурно,
  а не «на ревью».
- Комментарий «почему два класса» на английском ставится в `FileStorage.cs` один раз, развёрнуто
  (требование US-19 п. 4 и US-25 п. 3).

**Защита от path traversal.** `storageKey` собирается только сервером из `Guid`; при чтении
`OpenPrivateAsync` дополнительно проверяет, что `Path.GetFullPath(root + key)` начинается с
`Path.GetFullPath(root)`, иначе — `404`. Безопасность не строится на неугадываемости имени (US-19 п. 4),
но и путь из БД не считается доверенным.

### 3.3 Путь — из конфигурации

| Ключ | Env-форма | Значение по умолчанию | Назначение |
|---|---|---|---|
| `Storage:PrivateRoot` | `Storage__PrivateRoot` | `<ContentRoot>/App_Data/private-uploads` | корень приватного класса |
| `Storage:PublicRoot` | `Storage__PublicRoot` | `<ContentRoot>/wwwroot/uploads` | корень публичного класса (существующее поведение) |
| `Storage:MinFreeDiskMb` | `Storage__MinFreeDiskMb` | `1024` | порог свободного места (US-21 п. 11) |

`App_Data` — потому что это единственное имя каталога, которое IIS по умолчанию отказывается отдавать
по HTTP; на Linux это просто каталог. Значение по умолчанию выбрано так, чтобы **dev-запуск
`dotnet run` работал без единой настройки**, а забытая конфигурация на проде приводила к тому, что
файлы лягут внутрь контейнера и пропадут при пересборке (заметно сразу), а не станут публичными.

### 3.4 Fail-fast: приватный корень не может оказаться внутри `wwwroot`

В `Program.cs`, в существующий блок `if (builder.Environment.IsProduction())` добавляется **одна**
проверка (это единственное изменение fail-fast-блока за цикл):

```
Storage:PrivateRoot, приведённый к абсолютному пути, не должен начинаться с пути к wwwroot
→ иначе InvalidOperationException с текстом, объясняющим последствие
  ("client photos would be served by UseStaticFiles to anyone with the link").
```

Опечатка в `.env` — единственный реалистичный способ превратить приватный класс в публичный, и она
должна ронять старт, а не тихо публиковать персональные данные (риск R2).

### 3.5 Контур 1 — Linux VPS (`docker-compose.prod.yml`)

Добавляется **второй named volume** и одна переменная окружения:

```yaml
  api:
    environment:
      - Storage__PrivateRoot=/app/private-uploads
    volumes:
      - api_uploads:/app/wwwroot/uploads          # существующий, публичный класс
      - api_private_uploads:/app/private-uploads  # НОВЫЙ, приватный класс
volumes:
  api_uploads:
  api_private_uploads:                            # НОВЫЙ
```

Почему отдельный volume, а не подкаталог того же: (а) их разная судьба при инцидентах — публичный
можно снести и пережить битые логотипы, приватный — персональные данные, которые нужно бэкапить
отдельно; (б) один volume смонтирован **внутрь** `wwwroot` и потому обязан оставаться публичным;
(в) `docker volume` даёт точку, по которой видно, что бэкапить (в `DEPLOY.md` добавляется абзац).

`deploy/nginx/ezbook.conf` **не меняется**: приватные файлы ходят через `/api/`, который уже
проксируется; `client_max_body_size 6M` уже покрывает лимит 5 МБ. `location /uploads/` остаётся —
он обслуживает только публичный класс. Это фиксируется комментарием в конфиге.

### 3.6 Контур 2 — Windows + IIS (`DEPLOY-windows.md`)

- `Storage__PrivateRoot=C:\ezbook\private-uploads` — задаётся в окружении NSSM-службы бэкенда
  (раздел «Настройка службы» runbook'а), каталог создаётся вручную **вне** `C:\inetpub\wwwroot\ezbook`
  и вне каталога публикации;
- права: учётной записи службы даётся `Modify` на этот каталог; IIS-у права не нужны вовсе —
  он этот каталог не видит;
- `frontend/public/web.config` **не меняется**: правила проксирования `^api/` уже покрывают
  `GET /api/client-notes/photos/{id}`; правило `^uploads/` продолжает обслуживать публичный класс;
- в `DEPLOY-windows.md` добавляется абзац «Два класса хранения» с явным предупреждением: класть
  приватный корень внутрь `C:\inetpub\wwwroot\...` нельзя, потому что IIS раздаст его статикой
  раньше, чем запрос дойдёт до ARR.

---

## 4. Безопасный загрузчик изображений (US-19, US-25)

### 4.1 Конвейер одной загрузки

```
 1. [EnableRateLimiting("uploads")]        → 429, если превышен лимит (§10)
 2. [RequestSizeLimit(5 MB)]               → 413 от Kestrel на заведомо большом теле
 3. file is null || file.Length == 0       → 400 "File is required"
 4. file.Length > Uploads:MaxFileBytes     → 400 "Image is too large — the limit is 5 MB"
 5. читаем в память (≤5 МБ), ImageSignature.TryDetect по первым 12 байтам
                                           → 400 "Unsupported image type — use JPEG, PNG or WEBP"
 6. права на целевую сущность (заметка / профиль / услуга / компания)  → 403
 7. FileStorage.HasFreeSpace(...)          → 400 "Server storage is full — try again later." + WARN в лог
 8. ImageProcessor.Process(...)            → 400 "File is not a valid image" (декодер не осилил)
 9. [только приватный класс] транзакция + advisory lock + проверка квоты (§6) → 400 с цифрами
10. запись файла(ов) на диск
11. вставка строки / обновление сущности, commit
12. 201 (фото) или 200 (аватар/услуга/логотип) + DTO
```

Шаги 5 и 8 — **две независимые проверки**, и обе обязательны: сигнатура отсекает «`.jpg` с чужим
содержимым» до декодирования (тест `MC-2xx`), декодер отсекает валидную по сигнатуре, но битую или
злонамеренно сконструированную картинку. `Content-Type` и имя файла клиента **не используются нигде** —
ни для решения о приёме, ни для расширения на диске (US-19 п. 1). Это же чинит существующую загрузку
логотипа, где расширение выводилось из `Content-Type` (`CompaniesController.cs:271-281`).

Порядок шагов 9→10→11 отвечает на MC-2xu: при исчерпанной квоте **файл на диск не записан**, потому
что решение принимается по размеру уже обработанного изображения, которое до шага 10 существует
только в памяти.

### 4.2 Профили обработки

| Профиль | Где | Длинная сторона | Формат на выходе | В квоту |
|---|---|---|---|---|
| `ClientNotePhoto` | фото к заметке | 1600 px | **всегда JPEG q82** | да |
| `ClientNotePhotoThumb` | миниатюра к нему же | 320 px | всегда JPEG q75 | да (§6.1) |
| `Avatar` | `POST /api/profile/avatar` | 512 px, **квадрат по центру** | по правилу §4.4 | нет |
| `ServiceImage` | `POST /api/services/{id}/image` | 1200 px | по правилу §4.4 | нет |
| `CompanyLogo` | `POST /api/companies/{id}/logo` | 512 px | по правилу §4.4 | нет |

Апскейл запрещён: изображение меньше целевого размера сохраняется как есть (только перекодируется).
Оценка §5.9 п. 12 SPEC (≈300 КБ на фото) держится: 1600 px q82 для типового кадра — 250–400 КБ.

### 4.3 Ориентация: единственная метаданная, которую читаем до того, как всё выбросить

Смартфон пишет портретный кадр как ландшафтный пиксельный буфер + `EXIF Orientation`. Пересохранение
метаданные убивает — и без явной обработки **каждое портретное фото приедет повёрнутым на 90°**.
Поэтому в `ImageProcessor.Process`:

```
using var codec = SKCodec.Create(stream);
var origin = codec.EncodedOrigin;          // прочитали
var bitmap = SKBitmap.Decode(codec);
bitmap = ApplyOrigin(bitmap, origin);      // применили поворот/отражение к пикселям
// дальше resize + encode: метаданных на выходе нет вовсе
```

Юнит-кейс обязателен: изображение 100×200 с `Orientation = 6` после обработки имеет размеры 200×100
и ожидаемый пиксель в ожидаемом углу. Без него дефект не ловится ничем, кроме глаз в ручном чек-листе.

### 4.4 Формат на выходе — правило без сканирования пикселей

```
приватное фото к заметке          → всегда JPEG
публичное изображение, вход PNG   → PNG  (сохраняем альфа-канал)
публичное изображение, вход JPEG/WEBP → JPEG
```

Правило детерминировано по входному формату и не требует O(n)-скана на прозрачность. Причина, по
которой оно вообще есть: логотипы компаний сплошь и рядом PNG с прозрачностью, и «всегда JPEG»
залило бы их белым фоном на тёмной шапке — видимая регрессия работающей функции (US-25 п. 6 требует
перевести логотип на новый загрузчик, а не испортить его).

### 4.5 Что переводится на новый загрузчик

`CompaniesController.UploadLogo` (`:262-303`) теряет собственную проверку `Content-Type` и собственную
запись файла и вызывает `ImageUploadService`. Удаление старого файла (`:291-296`) переезжает в
`FileStorage.DeletePublic`. После этого в решении **один** путь загрузки изображений — проверяется
grep'ом по `IFormFile` на ревью (US-25 п. 2, п. 6).

---

## 5. Модель данных цикла (ответ на §12 п. 3)

### 5.1 `ClientNote` — подтверждаем эскиз SPEC §5.2

```csharp
public class ClientNote
{
    // ... существующие поля без изменений ...
    // NEW. The visit this note was written about, when it was written from the panel under a booking.
    // Optional on purpose: a note can also be filed straight from the client card, outside any visit.
    // SetNull rather than Cascade — deleting a booking (which the product does not do today) must not
    // take the note and its photos with it; the work was still done. See US-20 p.5.
    public Guid? BookingId { get; set; }
    public Booking? Booking { get; set; }
    public ICollection<ClientNotePhoto> Photos { get; set; } = new List<ClientNotePhoto>();
}
```

**Почему эскиз SPEC принимается без изменений.** Ключевой аргумент — гость без аккаунта. Заметка уже
умеет адресоваться двумя способами (`ClientId` либо `GuestPhone`), и `MastersController.GetClients`
уже группирует по обоим. Если бы фото несли собственный ключ клиента, этот двойной ключ пришлось бы
продублировать в новой таблице и держать в согласии с заметкой — второй экземпляр правила
«кто такой этот человек», ровно тот класс дефекта, который цикл A лечил в отчётах. Фото, привязанное
к заметке, получает адресацию клиента **бесплатно и навсегда согласованной**.

Отдельно: канон телефона (US-26) — предусловие качества этой связи. Пока `GuestPhone` хранился как
набрали, «фото этого гостя» разъезжались вместе с его историей. US-26 закрывает это без единой строки
в модели фото.

### 5.2 `ClientNotePhoto` — новая сущность

```csharp
public class ClientNotePhoto
{
    public Guid Id { get; set; }
    public Guid ClientNoteId { get; set; }
    // Denormalised copy of ClientNote.CompanyId. Two readers need it without a join: the quota sum
    // (one indexed aggregate per company) and the private download endpoint (one row → one permission
    // check). Written once from the note at insert time and never updated — a note never changes company.
    public Guid CompanyId { get; set; }
    public string StoragePath { get; set; } = string.Empty;      // "<companyId>/<guid>.jpg", NOT a URL
    public string ThumbnailPath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public long SizeBytes { get; set; }                          // full + thumbnail, what the disk holds
    public int Width { get; set; }
    public int Height { get; set; }
    public string ContentHash { get; set; } = string.Empty;      // SHA-256 of processed bytes, §6.3
    public string? UploadedByUserId { get; set; }                // nullable: see AppDbContext, SetNull
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ClientNote ClientNote { get; set; } = null!;
    public AppUser? UploadedBy { get; set; }
}
```

Конфигурация в `AppDbContext` (каскады описываются **явно**, US-20 п. 2):

```csharp
builder.Entity<ClientNotePhoto>(e =>
{
    e.HasOne(p => p.ClientNote).WithMany(n => n.Photos)
        .HasForeignKey(p => p.ClientNoteId).OnDelete(DeleteBehavior.Cascade);
    // Deleting the uploader must not delete company data: notes and photos belong to the company, not
    // to the employee (same rule as ClientNote.MasterId's comment and RemoveMember, US-20 p.4). This is
    // also what makes the phone-normalisation migration's account merges safe (§14.5).
    e.HasOne(p => p.UploadedBy).WithMany()
        .HasForeignKey(p => p.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
    e.HasIndex(p => p.ClientNoteId);
    e.HasIndex(p => new { p.CompanyId, p.CreatedAt });          // quota sum AND retention scan
    e.HasIndex(p => new { p.ClientNoteId, p.ContentHash }).IsUnique();   // §6.3
});
```

Каскад `ClientNote → ClientNotePhoto` удаляет **строки**; файлы удаляет прикладной код перед
`SaveChangesAsync` — порядок «сначала БД, потом диск» (§1.4) при удалении заметки соблюдается тем,
что файлы удаляются **после** успешного commit'а, а не в его транзакции. Что не удалилось — подберёт
уборка сирот (§9.3).

Удаление компании и удаление аккаунта клиента (US-20 п. 3): `ClientNote` уже каскадится от `Company`,
фото каскадятся от заметки; файлы в этих двух сценариях остаются сиротами и убираются фоном. Отдельного
кода не пишем — это осознанное следствие, зафиксированное здесь и комментарием в `AppDbContext`.

### 5.3 `ScheduledTaskState` — состояние фоновых задач

```csharp
public class ScheduledTaskState
{
    public string Name { get; set; } = string.Empty;   // PK, = IScheduledTask.Name
    public DateTime? LastStartedAtUtc { get; set; }
    public DateTime? LastFinishedAtUtc { get; set; }
    public bool LastSucceeded { get; set; }
    public int LastDurationMs { get; set; }
    public string? LastSummary { get; set; }           // "scanned 812, deleted 37, freed 11.4 MB"
    public string? LastError { get; set; }             // message + type, truncated to 2000
}
```

Ключ — имя задачи (`HasKey(s => s.Name)`, `MaxLength(100)`). Строк столько, сколько задач; читается
целиком одним запросом и планировщиком, и эндпоинтом живости.

### 5.4 Поля тарифа

```csharp
// ServiceBooking.Core/Enums/PhotoRetention.cs — сериализуется строкой, как все enum'ы проекта
public enum PhotoRetention { SixMonths = 0, TwelveMonths = 1, Forever = 2 }

// SubscriptionPlanConfig
public int? PhotoQuotaMb { get; set; } = 100;          // null = без ограничения
public PhotoRetention PhotoRetention { get; set; } = PhotoRetention.SixMonths;
```

Ровно три значения срока — как назвал заказчик (Q7); произвольное число дней не вводится, потому что
это сразу требует UI-валидации, миграции значений и вопроса «что значит 0».

### 5.5 `BookingDto` — три новых поля (US-06, Q12)

`cancellationReason`, `price`, `companySlug`. `Booking.Price` и `Booking.CancellationReason` уже есть
в сущности; `companySlug` берётся из уже загруженной `b.Company`. **Миграции не требуется** — это
изменение только `MapToDto`. Ломающее для контракта (аддитивное для TS), см. `API_CONTRACT.md` §3.

---

## 6. Учёт занятого объёма (ответ на §12 п. 4)

### 6.1 Решение: **агрегат по таблице фото, счётчика в БД нет**

```sql
SELECT COALESCE(SUM("SizeBytes"), 0) FROM "ClientNotePhotos" WHERE "CompanyId" = @companyId
```

Обоснование:

1. **Счётчик — это второй источник истины, который обязан разъехаться.** Его пришлось бы поддерживать
   в трёх местах (загрузка, удаление фото, удаление заметки) плюс в фоновой уборке, каждое — со своей
   транзакцией. Ровно так разъехались `GetStats` и `ReportsController` в цикле A (находка B5), и ровно
   такой дефект чинила комиссия в §11 цикла A. Второй раз тот же класс ошибки заводить нельзя.
2. **Требование US-20 п. 7 («освобождённый объём возвращается немедленно, в той же транзакции»)
   выполняется агрегатом тривиально:** удаление строки *и есть* обновление суммы. Со счётчиком это
   отдельная строка кода, которую можно забыть.
3. **Стоимость нулевая.** Индекс `(CompanyId, CreatedAt)` покрывает предикат; строк на компанию —
   единицы тысяч в самом смелом сценарии (5 фото × 20 визитов × 365 дней = 36 500 в год). Агрегат
   считается один раз на загрузку, а не на каждый запрос.
4. Требование SPEC §5.6 п. 6 («занятое место считается по сумме размеров в БД, а не обходом диска»)
   выполнено буквально.

Миниатюра учитывается в `SizeBytes` вместе с полным файлом: квота обещает пользователю место на
диске, а на диске лежат оба файла. Одна строка — один `SizeBytes` — весь занятый объём.

### 6.2 Корректность при параллельных загрузках, удалениях и уборке

Загрузка — это check-then-act («посчитал → записал»), то есть ровно тот случай, для которого в проекте
уже есть конвенция:

```
await using var tx = await db.Database.BeginTransactionAsync();
await AdvisoryLock.AcquireAsync(db, $"company-photo-quota:{companyId}");   // существующий helper
var used = await db.ClientNotePhotos.Where(p => p.CompanyId == companyId).SumAsync(p => p.SizeBytes);
if (quotaMb is not null && used + processedSize > quotaMb * 1024L * 1024L)  → 400, rollback, файла нет
// ── файл на диск ──
db.ClientNotePhotos.Add(row);
await db.SaveChangesAsync();
await tx.CommitAsync();
```

Свойства:
- **параллельные загрузки в одну компанию сериализуются** ключом `company-photo-quota:{companyId}` —
  две одновременные не могут вдвоём пройти проверку и вдвоём превысить квоту;
- **удаления в блокировке не нуждаются**: удаление только уменьшает сумму, а гонка «удалил, пока кто-то
  считал» безопасна в обе стороны (в худшем случае одна загрузка отклонена чуть строже, чем нужно);
- **фоновая уборка** удаляет строки порциями со своими короткими транзакциями и тоже не мешает: она
  не читает сумму вовсе, она удаляет по сроку. Квота пересчитывается «сама» при следующем чтении
  (US-21 п. 10 выполняется без отдельного шага пересчёта);
- запись файла происходит **внутри** удерживаемой блокировки (десятки миллисекунд на 300 КБ).
  Это осознанно: альтернатива «отпустить лок, записать, вставить» возвращает гонку по квоте.

### 6.3 Двойная отправка формы не расходует квоту дважды (NFR §9 п. 2)

Уникальный индекс `(ClientNoteId, ContentHash)`, где `ContentHash` — SHA-256 **обработанных** байтов.
Повторная отправка того же файла к той же заметке (двойной клик, ретрай сети) нарушает индекс;
`DbUpdateException` с этим индексом ловится, файл-дубль удаляется с диска, а эндпоинт возвращает
**`200` с уже существующим фото** вместо `201`. Пользователь видит своё фото, счётчик не двигается.

Почему хеш обработанных, а не исходных: пользователь мог загрузить два визуально разных снимка,
которые после ресайза совпали байт в байт, — это и есть дубликат по существу. И наоборот: исходные
байты у ретрая одного и того же файла тоже совпадут, так что сценарий из NFR покрыт.

Заголовок `Idempotency-Key` не вводится: он требует своего хранилища и своего протокола на фронте,
а уникальный индекс — одна строка в `AppDbContext` и детерминированный ответ.

### 6.4 Лимит 5 фото на заметку

Проверяется **в той же транзакции**, что и квота, тем же `COUNT` по `ClientNoteId` (US-17 п. 6,
`MC-1xz`). Ключ лока — `company-photo-quota:{companyId}`, отдельного лока на заметку не заводим:
одна заметка всегда принадлежит одной компании, а сериализация по компании строже, чем нужно, и потому
корректна.

---

## 7. Новые поля тарифа через `SubscriptionResolver` (ответ на §12 п. 6)

### 7.1 Разрешение плана: ноль новых веток

```csharp
public record EffectivePlan(
    bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics,
    bool AllowPublicListing, bool AllowOnlinePayment,
    int? MaxEmployees, int? MaxCompanies,
    int? PhotoQuotaMb,                 // ← NEW, null = unlimited
    PhotoRetention PhotoRetention)     // ← NEW
{
    public static readonly EffectivePlan Free = new(
        AllowOnlineBooking: false, AllowMailing: false, AllowAnalytics: false,
        AllowPublicListing: true,  AllowOnlinePayment: false,
        MaxEmployees: 1, MaxCompanies: 1,
        PhotoQuotaMb: 100,                          // SPEC US-24 п. 2, «предположение низкого риска»
        PhotoRetention: PhotoRetention.SixMonths);

    public static EffectivePlan FromConfig(SubscriptionPlanConfig c) => new(
        /* ... как было ... */, c.PhotoQuotaMb, c.PhotoRetention);
}
```

`Resolve(sub, nowUtc)` **не меняется ни на строку**. Это главное свойство решения: новые поля едут по
той же трубе, что и остальные возможности, и потому автоматически получают и Free-базлайн, и правило
«деактивированный `PlanConfig` → Free» из цикла A. Риск R6 («новые поля ломают только что
стабилизированное разрешение подписки») закрывается тем, что ломать нечего — трогается только запись
`EffectivePlan`, а не логика.

**N+1 не появляется физически:** `GetEffectivePlansForOwnersAsync` уже делает один запрос с
`.Include(s => s.PlanConfig)`; новые поля — колонки той же строки. Дополнительных обращений к БД — ноль.

Юнит-кейсы (в существующий `SubscriptionResolverRulesTests.cs`): подписки нет → `100 МБ / 6 мес`;
`PlanConfig.IsActive = false` → то же; активный план с `PhotoQuotaMb = null` → `null` (безлимит) и его
`PhotoRetention`; просроченный `PaidUntil` → Free-базлайн (US-24 п. 7).

### 7.2 Как это читает уборка для множества компаний за один проход

Наивная реализация («для каждой компании спросить план») даёт N+1 на каждую компанию — прямо запрещено
§12 п. 6. Проход устроен так:

```
1. companyIds = SELECT DISTINCT "CompanyId" FROM "ClientNotePhotos"        -- 1 запрос,
                                                                           -- только компании с фото
2. plans = subscriptionResolver.GetEffectivePlansAsync(companyIds)          -- 2 запроса (батч, §7.1)
3. группируем companyIds по plans[id].PhotoRetention → максимум ТРИ ведра
4. ведро Forever                     → пропускаем целиком (US-21 п. 8)
   ведро SixMonths  (cutoff = now-6м)  ┐  для каждого — порционный цикл:
   ведро TwelveMonths (cutoff = now-12м)┘  SELECT ... WHERE "CompanyId" = ANY(@ids)
                                              AND "CreatedAt" < @cutoff
                                            ORDER BY "Id" LIMIT @chunk        -- 1 запрос на порцию
```

Итог: **2 + 3 + (по одному запросу на порцию)** — независимо от того, 5 компаний в системе или 5 000.
Ключевая находка — сроков всего три (Q7), поэтому «персональный cutoff на компанию» вырождается в три
константы, и `ANY(@ids)` заменяет джойн с планами. Это и есть ответ на §12 п. 6.

Расчёт «какое фото просрочено» вынесен в чистую функцию `PhotoQuota.IsExpired(createdAtUtc, retention,
nowUtc)` / `PhotoQuota.CutoffUtc(retention, nowUtc)` — без БД, покрывается юнитами (US-21 п. 13:
три варианта срока + граница «ровно 6 месяцев» + `Forever` никогда не истекает).

### 7.3 Понижение тарифа не удаляет фото

Проверка квоты стоит **только на записи** (§6.2). Компания, перешедшая на тариф с меньшей квотой,
продолжает хранить всё, что загрузила, но новые загрузки получают `400` до освобождения места
(US-24 п. 5). Отдельного кода это не требует — это свойство того, что квота нигде не применяется
к существующим строкам. Фиксируется комментарием, чтобы никто не «дочинил» это до удаления данных.

---

## 8. Инфраструктура периодических задач (ответ на §12 п. 5)

### 8.1 Форма: один `BackgroundService`, обходящий зарегистрированные задачи

```
ServiceBooking.API/Services/Scheduling/
├── IScheduledTask.cs          контракт задачи
├── ScheduledTaskRunner.cs     единственный BackgroundService на всё приложение
├── ScheduledTaskOptions.cs    чтение конфигурации по имени задачи
└── Tasks/PhotoRetentionCleanupTask.cs   первая (и пока единственная) задача
```

```csharp
public interface IScheduledTask
{
    string Name { get; }                     // "photo-retention-cleanup"; ключ конфигурации и лока
    TimeSpan DefaultPeriod { get; }          // TimeSpan.FromDays(1) для уборки
    Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct);
}

public readonly record struct ScheduledTaskOutcome(int Scanned, int Affected, long BytesFreed, string Summary);
```

**Почему один хост, а не хост на задачу.** Хост на задачу означает, что общие свойства — чтение
конфигурации, расчёт «пора ли», advisory lock, запись состояния, обработка исключения, потолок времени —
существуют в стольких копиях, сколько задач. Требование US-21 п. 1 («вторая задача = класс + строка
регистрации, планировщик не правится») с копиями недостижимо в принципе: вторая задача была бы
копипастой первой. Один runner + один интерфейс делают расширяемость структурным свойством, а
рецензент из US-21 п. 1 проверяет её глазами за секунду.

**Регистрация:**
```csharp
builder.Services.AddScoped<IScheduledTask, PhotoRetentionCleanupTask>();   // ← вся вторая задача
builder.Services.AddHostedService<ScheduledTaskRunner>();                   // один раз, навсегда
```
Задачи — `Scoped`, потому что им нужен `AppDbContext`. Runner — singleton (`IHostedService`) и
разрешает задачи через `IServiceScopeFactory`.

### 8.2 Почему не Hangfire и не Quartz

SPEC §9 п. 9 это уже решил; архитектура обязана записать **почему**, а не подразумевать:

1. **Требование — периодический запуск, а не планировщик заданий.** Нам не нужны очередь, приоритеты,
   ретраи с backoff, cron-выражения, батчи, continuations. Нужен цикл «раз в сутки, не дважды сразу».
   Hangfire/Quartz решают задачу на порядок больше и приносят её сложность целиком.
2. **Своя схема в чужой базе.** Hangfire создаёт ~10 таблиц в своей схеме и управляет ими своими
   миграциями, параллельно нашим EF-миграциям в том же `MigrateAsync()`-конвейере. Это второй владелец
   схемы в базе, где сейчас один.
3. **Дашборд Hangfire — новая публичная поверхность.** Его пришлось бы авторизовать вручную в продукте,
   который в прошлом цикле специально убрал Swagger из Production (цикл A §7.1). Заводить обратно
   веб-панель с кнопкой «выполнить сейчас» — движение в противоположную сторону.
4. **Лицензия.** `Hangfire.Core` — LGPLv3 с коммерческой альтернативой, `Hangfire.Pro` — платный. Для
   продукта, который планируется продавать, это тот же класс вопроса, из-за которого отклонён
   ImageSharp 3.x (§2.1). Quartz.NET — Apache-2.0 и лицензионно чист, но пункты 1–2 остаются.
5. **Самое трудное у нас уже есть.** Единственная по-настоящему сложная часть — «два экземпляра не
   должны выполнять задачу одновременно» — решается `pg_advisory_*_lock`, который в проекте уже принят
   конвенцией и используется в четырёх местах. Внешний шедулер принесли бы **ради** этой блокировки,
   а она уже написана.
6. **Ноль новых пакетов против одного.** При лимите «две новые библиотеки на цикл» (§1.2) обе уже
   заняты обработкой изображений и тест-раннером.

Что мы **сознательно теряем**: cron-расписания (у нас период), дашборд (у нас
`GET /api/admin/scheduled-tasks`), автоматические ретраи (у нас следующий проход по расписанию —
US-21 п. 3), распределённые воркеры (у нас один контейнер). Всё перечисленное — не требования цикла.

### 8.3 Цикл выполнения

```
ExecuteAsync(stoppingToken):
  если ScheduledTasks:Enabled == false → лог Information "scheduler disabled" и выход
  каждые ScheduledTasks:TickSeconds (по умолчанию 60):
    для каждой зарегистрированной задачи:
      opts = ScheduledTaskOptions.For(name)          // Enabled, PeriodMinutes, MaxRunMinutes
      если !opts.Enabled                             → continue
      state = SELECT * FROM "ScheduledTaskStates" WHERE "Name" = name      (одна строка)
      если state?.LastStartedAtUtc + opts.Period > now → continue          // ещё не пора
      RunOne(task, opts)
```

`RunOne` — здесь живут все гарантии US-21:

```
scopeLock = CreateScope();  dbLock = scopeLock.AppDbContext
await using tx = dbLock.BeginTransaction()
if (!await AdvisoryLock.TryAcquireAsync(dbLock, $"scheduled-task:{name}"))   // §8.4
      → LogDebug("skipped, another instance holds the lock"); rollback; return;   // US-21 п.5

записываем LastStartedAtUtc = now (отдельным коротким скоупом, вне tx — иначе состояние откатится)
try:
    scopeWork = CreateScope();                     // ОТДЕЛЬНЫЙ scope и ОТДЕЛЬНОЕ подключение:
    task = scopeWork.GetRequiredService<IScheduledTask>()   // задача коммитит порциями, и её коммиты
    cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)   // не должны отпускать
    cts.CancelAfter(opts.MaxRunMinutes)                                     // xact-lock у dbLock
    outcome = await task.ExecuteAsync(cts.Token)
    → LastSucceeded = true, LastSummary = outcome, LogInformation(имя, длительность, цифры)
catch (OperationCanceledException) когда истёк потолок времени:
    → LastSucceeded = true, LastSummary = "... (partial, time budget reached)"   // US-21 п.7
catch (Exception ex):
    → LastSucceeded = false, LastError = ex, LogError(имя, трассировка)           // US-21 п.3
finally:
    LastFinishedAtUtc = now, LastDurationMs = ...   (короткая транзакция)
    tx.Rollback()   // лок отпущен; ничего писать в этой транзакции мы и не собирались
```

Ключевые свойства и как они выполняют критерии приёмки:

| Критерий US-21 | Как выполнен |
|---|---|
| 1. новая задача = класс + строка DI | `IScheduledTask` + `AddScoped`; runner не знает имён задач |
| 2. период и выключение из конфигурации | `ScheduledTasks:{Name}:PeriodMinutes` / `:Enabled`, дефолт из `DefaultPeriod` |
| 3. падение задачи не роняет ни соседей, ни процесс | `try/catch` **вокруг каждой задачи**, не вокруг тика; `catch` ловит `Exception`, а внешний цикл ещё раз обёрнут в `try` вокруг тика — необработанного исключения из `ExecuteAsync` хоста не существует |
| 4. рестарт переживается без дублей | «пора ли» считается от `LastStartedAtUtc` **из БД**; порции коммитятся по ходу |
| 5. защита от параллельного выполнения | `pg_try_advisory_xact_lock` (§8.4), молчаливый пропуск + debug-лог |
| 6. наблюдаемость | Information: старт, длительность, цифры, пропуск по локу; Error: падение. Плюс `GET /api/admin/scheduled-tasks` |
| 7. не мешают запросам | порции, короткие транзакции, `MaxRunMinutes` (по умолчанию 10) |

**Почему два скоупа.** `pg_advisory_xact_lock` живёт до конца транзакции. Если бы задача работала на
том же `DbContext`, её первый же промежуточный коммит (US-21 п. 4) отпустил бы лок посреди прохода.
Разделение на «скоуп, держащий лок» и «скоуп, делающий работу» — единственный способ иметь и порции,
и защиту от параллельного выполнения одновременно. Это неочевидно и потому комментируется в коде абзацем.

### 8.4 `AdvisoryLock` получает неблокирующий вариант — найденное расхождение

`AdvisoryLock.AcquireAsync` использует `pg_advisory_xact_lock`, который **ждёт** освобождения. SPEC
§5.9 п. 5 требует прямо противоположного: «не получивший блокировку экземпляр **молча пропускает
запуск**». Ждать здесь нельзя — второй экземпляр простоял бы у лока весь проход соседа и запустился
бы сразу после, дав ровно тот двойной проход, от которого защищаемся.

Поэтому в `Services/AdvisoryLock.cs` добавляется сосед (файл, стиль и ключевая конвенция те же):

```csharp
/// <summary>Non-blocking sibling of AcquireAsync: returns false immediately if another session holds
/// the lock, instead of waiting for it. Used by the scheduled-task runner, where "someone else is
/// already doing this" must mean "skip this run", not "queue behind them".</summary>
public static async Task<bool> TryAcquireAsync(AppDbContext db, string key) =>
    await db.Database.SqlQuery<bool>(
        $"SELECT pg_try_advisory_xact_lock(hashtextextended({key}, 0))").SingleAsync();
```

Ключ — `scheduled-task:{имя}`, как требует SPEC. Существующие четыре ключа и `AcquireAsync` не трогаются.

### 8.5 Выключение в `Testing`

`ScheduledTasks:Enabled` по умолчанию `true`, но `appsettings.Testing.json` (новый файл, три строки)
ставит `false` — фоновые проходы не должны влиять на 283 функциональных сценария (US-21 п. 15).
Тесты самого планировщика (`SCH-*`) включают его точечно, переопределяя конфигурацию в
`CustomWebApplicationFactory` — по образцу `CaptchaService.IsEnforced`.

---

## 9. Первая задача: уборка фото по сроку (US-21 пп. 8–12)

### 9.1 Проход

```
1. три ведра по сроку хранения (§7.2)
2. для каждого конечного ведра, порциями по 200:
     rows = SELECT ... WHERE "CompanyId" = ANY(@ids) AND "CreatedAt" < @cutoff ORDER BY "Id" LIMIT 200
     if (rows пусто) break
     db.ClientNotePhotos.RemoveRange(rows); SaveChanges()      ← сначала БД
     foreach row: FileStorage.DeletePrivate(row.StoragePath / ThumbnailPath)   ← потом диск
     проверяем ct: истёк потолок времени → выходим штатно, проход продолжится завтра
3. уборка сирот (§9.3)
4. Outcome(Scanned, Affected, BytesFreed, "scanned 812, deleted 37, freed 11.4 MB")
```

Порядок «БД → диск» (US-20 п. 6, US-21 п. 9): при сбое между шагами остаётся файл-сирота, которую
подберёт следующий проход. Обратный порядок дал бы живую строку без файла — битую картинку в
интерфейсе навсегда.

Порция 200 строк выбрана так, чтобы транзакция была заведомо короткой (единицы миллисекунд) и не
конкурировала с загрузками за тот же индекс.

### 9.2 Пересчёт квоты после прохода

Отдельного шага нет и не нужно: квота — агрегат (§6.1), удалённые строки в сумму больше не входят.
Требование US-21 п. 10 («освободившееся место сразу доступно») выполняется как следствие §6.1 —
это второй, независимый аргумент в пользу агрегата против счётчика.

### 9.3 Сироты

Обход `Storage:PrivateRoot` по подкаталогам компаний; для каждой порции имён — один запрос
`WHERE "StoragePath" = ANY(@paths)`; чего нет в ответе — сирота. **Удаляются только файлы старше
24 часов** по времени модификации: это защита от гонки с загрузкой, которая уже записала файл, но ещё
не закоммитила строку (§4.1, шаги 10–11). Без этой отсечки уборка удаляла бы фото прямо из-под
работающего мастера — тихая потеря данных, риск R3.

### 9.4 Порог свободного места

`FileStorage.HasFreeSpace` (`DriveInfo.AvailableFreeSpace` по диску приватного корня) вызывается
**до** записи; при нехватке — `400` `Server storage is full — try again later.` и `LogWarning`
(US-21 п. 11). Загрузка отклоняется понятным текстом, а не падает `IOException` → 500.

---

## 10. Rate limiting (ответ на §12 п. 7)

### 10.1 Решение: встроенный `Microsoft.AspNetCore.RateLimiting`, точечно, именованной политикой

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("uploads", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Uploads:PerUserPerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0                       // отказываем сразу, не выстраиваем очередь
        }));
    o.OnRejected = async (ctx, _) =>             // 4xx = голая строка (§13)
        await ctx.HttpContext.Response.WriteAsync("Too many uploads. Try again in a minute.");
});
...
app.UseRateLimiter();   // после UseAuthorization(), до MapControllers()
```

Применяется **только** атрибутом `[EnableRateLimiting("uploads")]` на четырёх методах загрузки.
`app.UseRateLimiter()` без глобального лимитера — это no-op для всех остальных эндпоинтов: вход,
регистрация и гостевая запись в этом цикле rate limiting **не получают** (SPEC §8.2 — отдельный цикл).

### 10.2 Число: **10 загрузок в минуту на пользователя** (значение фиксирует архитектор, US-19 п. 5)

Обоснование: рабочий сценарий — мастер закончил работу и прикладывает к заметке до 5 фото подряд
(Q7). Десять — это два полных сценария в минуту, то есть запас ×2 на повторы и ретраи, и при этом
потолок ввода 10 × 5 МБ = 50 МБ/мин на пользователя, что типовой VPS переживает. Значение вынесено
в `Uploads:PerUserPerMinute`, чтобы менять без релиза; в `Testing` поднимается до 1000, а один тест
(`MC-2xy2`) опускает его до 1 и проверяет `429`.

### 10.3 Как уживается с запросом к БД в `OnTokenValidated`

Порядок в конвейере: `UseAuthentication()` → `UseAuthorization()` → **`UseRateLimiter()`** →
`MapControllers()`. Следствие, которое нужно принять осознанно: **отклонённый по лимиту запрос всё
равно оплатил один запрос к БД** в `OnTokenValidated` (перечитка ролей + сверка `sstamp`).

Почему это правильный размен:
- партиционировать **до** аутентификации можно только по IP. Целый салон за одним NAT — это несколько
  мастеров с одним адресом; лимит 10/мин на всех превратился бы в лимит 2/мин на человека, то есть
  ломал бы штатный сценарий;
- цена — один `SELECT` по первичному ключу `AspNetUsers`, тот самый, который платит любой другой
  аутентифицированный вызов. Это не новая нагрузка, это существующая стоимость запроса, которая при
  отказе не окупается. При 10 отказах в минуту на пользователя — величина, неотличимая от нуля;
- **rate limiting здесь защищает диск и CPU (ресайз), а не базу.** Запрос, отклонённый до чтения тела,
  не тратит ни того, ни другого — цель достигнута.

Ручной счётчик отклонён: он потребовал бы собственного словаря в памяти со своей эвикцией (утечка при
росте числа пользователей), собственного окна и собственных тестов — то есть переписывания того, что
в фреймворке уже есть, оттестировано и не стоит ни одного пакета.

---

## 11. Каноническая форма телефона (US-26)

### 11.1 Одна чистая функция

`ServiceBooking.API/Services/PhoneNormalizer.cs` — статический класс без `AppDbContext` и без EF-типов
(требование US-26 п. 1, по образцу `SlotCalculator` из цикла A):

```csharp
public static class PhoneNormalizer
{
    /// <summary>Digits only: no '+', spaces, brackets or dashes. RU 11-digit numbers starting with 8
    /// become 7…; 10-digit numbers starting with 9 become 7…; everything else keeps its digits as they
    /// are, so international numbers are not mangled. See SPEC US-26 p.2.</summary>
    public static string Normalize(string? raw);

    /// <summary>E.164 bounds: 10..15 digits.</summary>
    public static bool IsValid(string canonical);

    public static bool TryNormalize(string? raw, out string canonical);
}
```

Юнит-набор `PhoneNormalizerTests.cs` — не менее 10 кейсов из US-26 п. 10.

### 11.2 Точки применения — все семь, поимённо

| Файл | Что делает |
|---|---|
| `AuthController.Register` | нормализует до создания; `400` на невалидный |
| `AuthController.Login` | нормализует перед `FindByNameAsync` |
| `CompaniesController.AddMember` | нормализует и для поиска существующего, и для автосоздания (включая генерацию пароля из последних 6 цифр — она начинает работать от канона) |
| `ProfileController.ChangePhone` | нормализует до `SetUserNameAsync` |
| `BookingsController.Create` | `GuestPhone` |
| `MastersController.AddNote` | `GuestPhone` |
| `AdminController.GetUsers` | нормализует строку поиска, **если она похожа на телефон** (после удаления нецифровых остаётся ≥ 5 цифр и не остаётся букв); иначе ищет как есть — иначе поиск по фамилии сломается |

Текст `400`: `Phone number must contain 10 to 15 digits.` — голая строка (§13).

**Важное замечание для фронта, которое нельзя пропустить.** Этот текст содержит подстроку `phone`,
а `frontend/src/utils/bookingError.ts:30` матчит `includes('phone')` и покажет «Укажите имя и телефон».
В гостевой записи это **корректное** сообщение для невалидного номера, поэтому текст оставляем как
есть и маппер не правим. Правило цикла A «новые тексты 400 не содержат `captcha`/`name`/`phone`»
здесь нарушено **осознанно и с проверенным результатом**; отмечено в `API_CONTRACT.md` §0.2 и
в чек-листе §21.

### 11.3 Показ пользователю

`frontend/src/utils/phone.ts` — `formatPhone(canonical: string): string`: 11 цифр, начинающихся с 7 →
`+7 (999) 000-00-00`; иначе → `+<цифры>`; пустая строка → пустая строка. Маска ввода **не вводится**
(US-26 п. 6, NFR §9 п. 8). Функция — первый обязательный объект покрытия фронтового раннера (§15).

---

## 12. Приватная раздача фото и то, как её видит фронт

### 12.1 Эндпоинт

`GET /api/client-notes/photos/{id}` и `GET /api/client-notes/photos/{id}/thumb`:

```
401  аноним
404  фото нет ИЛИ вызывающий не является персоналом компании этого фото   ← межарендная изоляция:
                                                                            существование чужого id
                                                                            не подтверждаем
403  вызывающий — SuperAdmin (решение Q5: счётчики да, содержимое нет)
200  Content-Type: image/jpeg|image/png, Cache-Control: private, max-age=86400
```

Ветка `403` для `SuperAdmin` — **единственное место в продукте, где суперадмин получает отказ**, и
потому обязана быть закомментирована абзацем: иначе следующий читающий «починит» это как баг.
Счётчики и объём суперадмин получает через `GET /api/companies/{id}/photo-usage` (§12.3).

`UseStaticFiles` к приватному корню не применяется никогда: он смонтирован вне `wwwroot`, и второго
вызова `UseStaticFiles` с `FileProvider` в `Program.cs` не появляется. Это проверяется grep'ом на ревью.

### 12.2 Следствие, которого нет в SPEC: `<img src>` не носит токен

Токен лежит в `localStorage` и подставляется axios-интерцептором (`client.ts:9-13`). Браузер, загружая
`<img src="/api/client-notes/photos/{id}">`, заголовок `Authorization` **не отправит** — получит `401`
и покажет битую картинку. То есть буквальная реализация US-18 п. 1 не работает.

**Решение: изображение загружается через тот же axios (`responseType: 'blob'`), а в `src` идёт
`URL.createObjectURL(blob)`.**

- новый хук `frontend/src/hooks/useAuthedImage.ts` + компонент `components/ui/AuthedImage.tsx`;
- кэширование — react-query (`['note-photo', id, 'thumb']`, `staleTime: Infinity`), объектные URL
  освобождаются в `useEffect`-cleanup;
- **ленивость** (US-18 п. 3): запрос не отправляется, пока элемент не попал в вьюпорт —
  `IntersectionObserver` внутри хука. Атрибут `loading="lazy"` на `<img>` остаётся (он корректен и
  ничего не ломает), но настоящую отсрочку даёт наблюдатель — с blob-URL атрибут смысла не имеет,
  и это надо понимать, а не считать, что требование закрыто атрибутом;
- полноразмерное фото запрашивается только при открытии модалки просмотра (NFR §9 п. 6: «экран истории
  с 50 фото открывается без загрузки полноразмерных изображений»).

Отвергнутые альтернативы: подписанные ссылки с HMAC (новая криптоинфраструктура и новый класс секретов
ради удобства `<img>`); cookie-аутентификация (переписывание модели аутентификации целиком, CSRF).
Выбранный вариант не добавляет на сервер ни строки и даёт ровно то, что требует ручной чек-лист DoD:
прямая ссылка, открытая в браузере без авторизации, изображение не отдаёт.

**Именование полей DTO.** `url` и `thumbnailUrl` в `ClientNotePhotoDto` — это **пути к API**, а не
готовые значения для `src`. Имена сохранены (их называет SPEC US-18 п. 1), но в `API_CONTRACT.md` §5
это выделено предупреждением, а на фронте единственный потребитель — `AuthedImage`, так что ошибиться
негде.

### 12.3 Владелец и суперадмин видят цифры

`GET /api/companies/{id}/photo-usage` (персонал компании или `SuperAdmin`) →
`{ usedBytes, photoCount, quotaMb, retention, percentUsed }`.

Почему отдельный эндпоинт, а не поля в `CompanyDto`: `CompanyDto` отдаётся в публичном каталоге и в
списках владельца — занятый объём стал бы публичным, а агрегат по фото пришлось бы считать **на каждую
компанию списка**, то есть завести N+1 ровно там, где цикл A его вычищал. Отдельный эндпоинт вызывается
одним экраном настроек, один раз, для одной компании.

---

## 13. Формат ошибок — граница, перенесённая из цикла A без изменений

| Код | Кто отдаёт | Content-Type | Тело | Цикл B |
|---|---|---|---|---|
| 400 | `BadRequest("текст")` | `text/plain` | голая строка | **без изменений**, включая все новые 400 |
| 400 | `BadRequest(identityErrors)` | JSON-массив строк | без изменений |
| 400 | автовалидация `[ApiController]` | `application/problem+json` | `ValidationProblemDetails` | без изменений |
| 401 | пайплайн JWT | — | пустое | без изменений |
| 402 | `StatusCode(402, "текст")` | `text/plain` | голая строка | без изменений |
| 403 | `Forbid()` | — | пустое | без изменений |
| 404 | `NotFound()` / `NotFound("текст")` | — / `text/plain` | пусто / строка | без изменений |
| 409 | `Conflict("текст")` | `text/plain` | голая строка | без изменений |
| **429** | rate limiter, `OnRejected` | `text/plain` | **голая строка** | **новый код, подчиняется той же границе** |
| 413 | Kestrel/`RequestSizeLimit` | — | пустое | новый в этом цикле (был у логотипа) |
| 500 | необработанное исключение | `application/problem+json` | `ProblemDetails` + `traceId` | без изменений |

`builder.Services.AddProblemDetails()` и `UseStatusCodePages*` по-прежнему **запрещены** — они переписали
бы тела существующих 4xx, на которых стоят мапперы `src/utils/*Error.ts`. Единственная тонкость нового
кода: `429` по умолчанию отдаёт **пустое** тело, поэтому `OnRejected` обязателен — иначе новый маппер
`uploadError.ts` не сможет отличить его от 403.

---

## 14. Миграции цикла (ответ на §12 п. 8)

Пять миграций. Применяются автоматически на старте (`db.Database.MigrateAsync()`), отдельного ручного
шага нет. Порядок обеспечивается порядком мёржа задач.

| # | Миграция | Задача | `Up` | `Down` | Независима? |
|---|---|---|---|---|---|
| 1 | `AddClientNoteBookingId` | T-B4 | `AddColumn ClientNotes.BookingId uuid NULL` + FK на `Bookings` (`SetNull`) + индекс | `DropColumn` | **да** |
| 2 | `AddClientNotePhotos` | T-B5 | `CreateTable ClientNotePhotos` + 3 индекса (в т.ч. уникальный `(ClientNoteId, ContentHash)`) | `DropTable` | **да** |
| 3 | `AddPlanPhotoLimits` | T-B7 | `AddColumn SubscriptionPlanConfigs.PhotoQuotaMb int NULL`, `PhotoRetention int NOT NULL DEFAULT 0` + `Sql`-бэкфилл существующих планов (`PhotoQuotaMb = 1024`, `PhotoRetention = 1`) | `DropColumn` ×2 | **да** |
| 4 | `AddScheduledTaskState` | T-B9 | `CreateTable ScheduledTaskStates` (PK `Name`) | `DropTable` | **да** |
| 5 | **`NormalizePhoneNumbers`** | **T-B14, отдельным PR, последней** | разрешение коллизий + нормализация всех колонок | **пустой**, с комментарием | **нет: после № 2** |

### 14.1 Почему четыре первых независимы

Ни одна из них не читает и не меняет то, что создаёт другая: колонка в `ClientNotes`, новая таблица
фото, две колонки в справочнике тарифов, новая таблица состояния задач. Их можно мёржить в любом
порядке и в любых комбинациях; перечисленный порядок выбран только для читаемости истории и совпадает
с порядком волн (§18). Единственная механическая связь — `AppDbContextModelSnapshot`: параллельная
генерация двух миграций даст конфликт в снапшоте, поэтому **генерируются они последовательно**
(волны), хотя применяться могут в любом.

### 14.2 Почему № 5 обязана быть последней и отдельной

1. **Требование SPEC §12 п. 8** — её нужно уметь прогнать и проверить изолированно, на копии dev-базы
   (DoD п. 5).
2. **Реальная зависимость от № 2.** Разрешение коллизий **удаляет пользователей**. Строки, ссылающиеся
   на удалённых, должны корректно обработаться: `Booking.ClientId` уже `SetNull`, а
   `ClientNotePhoto.UploadedByUserId` — новая ссылка, и она объявлена nullable + `SetNull` именно
   поэтому (§5.2). Прогони мы № 5 до № 2 — ничего бы не сломалось, но после появления таблицы фото
   правило нужно проверять уже с ней; безопаснее иметь финальную схему на момент удаления аккаунтов.
3. **Она единственная необратима.** `Down` пустой с комментарием (удалённые аккаунты не восстанавливаются) —
   та же конвенция, что у `DeduplicateWorkingHours` в цикле A.

### 14.3 Содержание № 5

Одна `Sql(...)`-инструкция с `DO $$ … $$` блоком, в порядке:

```
1. временная таблица: (userId, canonical) для всех AspNetUsers
2. поиск коллизий: canonical, встречающийся более одного раза
3. СТОП-условие (RAISE EXCEPTION с перечислением id):
     - две и более схлопывающихся записи владеют компаниями (Companies.OwnerUserId), ИЛИ
     - две и более являются мастерами с бронями (Bookings.MasterId)
   → вся миграция откатывается, сообщение называет конкретные строки        (SPEC §7.1 п.8)
4. выбор выжившего в группе: максимум по (записи как клиент + записи как мастер + членства),
   при равенстве — минимальный CreatedAt; порядок детерминирован явным ORDER BY со всеми полями
5. DELETE остальных из AspNetUsers (FK Bookings.ClientId уже SetNull;
   ClientNotePhotos.UploadedByUserId — SetNull, см. §5.2)
6. UPDATE AspNetUsers SET PhoneNumber = canon, UserName = canon, NormalizedUserName = UPPER(canon)
7. UPDATE Bookings   SET GuestPhone = canon(GuestPhone)   WHERE GuestPhone IS NOT NULL
8. UPDATE ClientNotes SET GuestPhone = canon(GuestPhone)  WHERE GuestPhone IS NOT NULL
```

Функция `canon(x)` внутри блока — точная транслитерация `PhoneNormalizer.Normalize`:
`regexp_replace(x, '\D', '', 'g')`, затем `CASE` на «11 цифр с 8 → 7…» и «10 цифр с 9 → 7…».

**Риск, который здесь есть, и как он держится (R4).** Правило написано дважды — на C# и на SQL, и они
могут разъехаться. Митигация: (а) в PR обе реализации показываются рядом, построчно; (б) функциональный
тест `AUTH-0xx`/`MC-0xx` после миграции проверяет ровно те же фикстуры, что юнит-тесты
`PhoneNormalizerTests` — если SQL разошёлся с C#, тест красный; (в) обязательный прогон на копии
dev-базы до выкладки (DoD п. 5). Вынести правило в одну реализацию нельзя: миграция не может звать
код приложения, не превратившись в скрипт вне EF.

`NormalizedUserName` пересчитывается как `UPPER(canonical)` — для строки из одних цифр это та же
строка, то есть операция бесплатная и корректная.

---

## 15. Фронтовый тест-раннер (ответ на §12 п. 9, US-23)

### 15.1 Состав

| Пакет | Роль | Почему он |
|---|---|---|
| `vitest` ^2.1 | раннер | Переиспользует существующий Vite/esbuild-конвейер: ни babel, ни ts-jest, ни второго TS-конфига. Jest потребовал бы отдельной трансформации TSX |
| `jsdom` ^25 | DOM | Требуется SPEC; альтернатива `happy-dom` быстрее, но хуже совместима с Testing Library |
| `@testing-library/react` ^16 | рендер и запросы | Стандарт для React 18 |
| `@testing-library/user-event` ^14 | ввод пользователя | Нужен для загрузки файла и клавиатуры (a11y-критерии NFR §9 п. 8) |
| `@testing-library/jest-dom` ^6 | матчеры | `toBeInTheDocument` и соседи |

Все пять — в `devDependencies`. Это **вторая и последняя** новая библиотека цикла (SPEC §9 п. 9 считает
тест-раннер одной позицией).

### 15.2 Конфигурация

Отдельный `frontend/vitest.config.ts` (решение из цикла A, сохранённое в git-истории, активируется
без пересмотра):

```ts
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: false,                       // явные импорты describe/it/expect — как именованные экспорты в коде
    setupFiles: ['./src/test/setup.ts'],  // только import '@testing-library/jest-dom'
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
    coverage: { provider: 'v8', include: ['src/utils/**', 'src/components/clientNotes/**'] }
  }
})
```

`vite.config.ts` **не трогается** — прод-сборка и тесты не должны делить конфиг. `package.json`
получает два скрипта: `"test": "vitest"` и `"test:run": "vitest run"` (второй назван в DoD §10 п. 4).
Тесты лежат рядом с исходником (`src/utils/phone.test.ts`), а не в отдельном дереве.

### 15.3 Что покрывается в первую очередь

Обязательный минимум SPEC (US-23) плюс то, что дёшево и ломается молча:

| Файл | Что проверяется | ~кейсов |
|---|---|---|
| `src/utils/phone.test.ts` | `formatPhone`: 11 цифр с 7, 10 цифр, международный, пустая строка, уже отформатированный вход, канон из БД — **те же фикстуры, что у `PhoneNormalizerTests`** | 8 |
| `src/utils/uploadError.test.ts` | `getUploadErrorMessage`: 400 «слишком большой», 400 «формат», 400 «6-е фото», 400 «квота» (с цифрами и без), 429, 403, 401, 500, отсутствие `response` | 10 |
| `src/components/clientNotes/PhotoGallery.test.tsx` | **пустой список — ничего не рисуется и ничего не падает**; непустой — N миниатюр с осмысленным `alt`; клик открывает просмотр; Esc закрывает | 5 |
| `src/utils/cancelError.test.ts` | маппер ошибок отмены (400 «причина длиннее 300») | 3 |

Сеть в тестах не участвует: слой `src/api/*` мокается через `vi.mock`. MSW **не вводится** — это ещё
одна зависимость ради сценариев, которых в обязательном минимуме нет.

Что **не** покрывается в этом цикле и почему: три модалки бронирования (`BookingModal` 343 стр. и
соседи) — они цикл не трогает, а их покрытие само по себе размером с историю; отложено с пометкой
в `TEST_CATALOG.md`.

### 15.4 Встраивание в CI без роста времени прогона

Шаг добавляется в **существующий** джоб `frontend`, между type-check и build:

```yaml
      - name: Type-check
        run: npx tsc --noEmit
      - name: Unit tests
        run: npm run test:run          # ← НОВЫЙ
      - name: Build
        run: npm run build
```

Бюджет: сейчас джоб `frontend` ≈ 2 мин (`npm ci` ~60 с из кэша, `tsc` ~20 с, `vite build` ~30 с).
~26 тестов на jsdom с холодной трансформацией — **15–25 с**. Джоб становится ≈ 2 мин 20 с.
Критический путь workflow — джоб `backend` (~5 мин), джобы идут **параллельно**, поэтому суммарное
время прогона не меняется вовсе и остаётся втрое ниже потолка 10 минут (§12 п. 9).

Coverage в CI **не собирается** (v8-инструментация стоит времени, а порога у нас нет) — отчёт
снимается локально командой, записанной в `TEST_CATALOG.md`.

---

## 16. CI и деплой: что меняется

| Файл | Изменение | Задача |
|---|---|---|
| `.github/workflows/ci.yml` | шаг `npm run test:run` в джобе `frontend` | T-F1 |
| `.github/workflows/ci.yml` | `-warnaserror` в шаге `dotnet build` — включается **после** удаления Blazor-проекта (US-22), не раньше | T-B17 |
| `docker-compose.prod.yml` | `Storage__PrivateRoot` + volume `api_private_uploads` | T-B3 |
| `.env.production.example` | комментарий про приватный volume и его бэкап | T-B3 |
| `DEPLOY.md` | абзац «Два класса хранения»: что бэкапить, где лежит | T-B3 |
| `DEPLOY-windows.md` | абзац про `Storage__PrivateRoot` вне `wwwroot` и права службы | T-B3 |
| `deploy/nginx/ezbook.conf` | **не меняется**; добавляется комментарий, что `/uploads/` — только публичный класс | T-B3 |
| `ServiceBooking.API/Dockerfile` | **не меняется**; добавляется комментарий про запрет тега `-alpine` (§2.1) | T-B2 |
| `ServiceBooking.API/appsettings.Testing.json` | новый, три ключа: планировщик выключен, лимит загрузок поднят | T-B9 |

Требование US-03 п. 6 (security-заголовки должны разрешать `/embed/*` и запрещать остальное) —
**задача для devops, вне кода цикла**; фиксируется в `DEPLOY.md` отдельным абзацем «когда будете
добавлять `X-Frame-Options`/CSP», чтобы виджет не сломали позже.

---

## 17. Состояние дерева после цикла B

```
ServiceBooking.sln                       4 проекта (Blazor-шаблон удалён, US-22)
ServiceBooking.API/
├── Program.cs                           + AddRateLimiter/UseRateLimiter; + AddHostedService(runner);
│                                          + регистрация задач; + fail-fast на PrivateRoot
├── Dockerfile                           не меняется (комментарий про -alpine)
├── appsettings.Testing.json             ← НОВЫЙ
├── Controllers/
│   ├── ClientNotePhotosController.cs    ← НОВЫЙ (upload / get / thumb / delete)
│   ├── MastersController.cs             заметки как объекты; BookingId; права удаления (Q16); Take
│   ├── BookingsController.cs            − GET /my; + 3 поля в MapToDto; + валидация причины отмены;
│   │                                      + нормализация GuestPhone
│   ├── CompaniesController.cs           логотип → ImageUploadService; + GET {id}/photo-usage;
│   │                                      + нормализация телефона в AddMember
│   ├── ServicesController.cs            + POST {id}/image
│   ├── ProfileController.cs             + POST avatar; + нормализация в ChangePhone;
│   │                                      − commissionPercent (US-22)
│   ├── AuthController.cs                + нормализация в Register/Login
│   └── AdminController.cs               + GET scheduled-tasks; + нормализация поиска;
│                                          − commissionPercent из AdminUserDto (US-22)
├── DTOs/
│   ├── ClientNotes/ClientNoteDto.cs     ← НОВЫЙ (ClientNoteDto, ClientNotePhotoDto, MasterClientDto)
│   └── Bookings/BookingDto.cs           + CancellationReason, Price, CompanySlug
├── Services/
│   ├── PhoneNormalizer.cs               ← НОВЫЙ, чистая
│   ├── ImageSignature.cs                ← НОВЫЙ, чистая
│   ├── ImageProcessor.cs                ← НОВЫЙ, чистая (SkiaSharp)
│   ├── PhotoQuota.cs                    ← НОВЫЙ, чистая (квота + срок хранения)
│   ├── FileStorage.cs                   ← НОВЫЙ (два класса хранения)
│   ├── ImageUploadService.cs            ← НОВЫЙ (оркестрация)
│   ├── AdvisoryLock.cs                  + TryAcquireAsync (§8.4)
│   ├── SubscriptionResolver.cs          + 2 поля в EffectivePlan и Free
│   └── Scheduling/
│       ├── IScheduledTask.cs            ← НОВЫЙ
│       ├── ScheduledTaskRunner.cs       ← НОВЫЙ (единственный BackgroundService)
│       ├── ScheduledTaskOptions.cs      ← НОВЫЙ
│       └── Tasks/PhotoRetentionCleanupTask.cs  ← НОВЫЙ
ServiceBooking.Core/
├── Entities/ClientNotePhoto.cs          ← НОВЫЙ
├── Entities/ScheduledTaskState.cs       ← НОВЫЙ
├── Entities/ClientNote.cs               + BookingId
├── Entities/SubscriptionPlanConfig.cs   + PhotoQuotaMb, PhotoRetention
├── Entities/AppUser.cs                  − CommissionPercent (US-22)
└── Enums/PhotoRetention.cs              ← НОВЫЙ
ServiceBooking.Infrastructure/
├── Data/AppDbContext.cs                 + ClientNotePhotos, ScheduledTaskStates, явные каскады
└── Migrations/                          + 5 миграций (§14)
ServiceBooking.UnitTests/                + PhoneNormalizerTests, ImageProcessorTests,
                                           ImageSignatureTests, PhotoQuotaTests,
                                           ScheduledTaskScheduleTests (~45 новых кейсов)
ServiceBooking.Tests/                    + MC-1xx..3xx, SCH-0xx, PROF-0xx, SVC-0xx, ADM-0xx, AUTH-0xx
frontend/
├── package.json                         + 5 devDependencies, + scripts test / test:run
├── vitest.config.ts                     ← НОВЫЙ
├── src/test/setup.ts                    ← НОВЫЙ
├── src/api/clientNotes.ts               ← НОВЫЙ
├── src/api/masters.ts                   notes: ClientNote[] вместо string[]
├── src/utils/phone.ts                   ← НОВЫЙ
├── src/utils/uploadError.ts             ← НОВЫЙ
├── src/utils/cancelError.ts             ← НОВЫЙ
├── src/hooks/useAuthedImage.ts          ← НОВЫЙ
├── src/components/ui/AuthedImage.tsx    ← НОВЫЙ
├── src/components/clientNotes/          ← НОВЫЙ каталог: NoteCard, NotePhotoUploader,
│                                          PhotoGallery, PhotoViewerModal
├── src/pages/MasterClientsPage.tsx      заметки-объекты, фото, удаление
├── src/pages/MyBookingsPage.tsx         то же в панели под записью; форма причины отмены
├── src/pages/ClientBookingsPage.tsx     причина отмены; оживлённые цена и «Записаться снова»
├── src/pages/ProfilePage.tsx            загрузка аватара
├── src/pages/owner/CompanyManagePage.tsx  виджет-сниппет; картинка услуги; «занято X из Y МБ»
├── src/pages/AdminPage.tsx              блокировка/разблокировка компании
├── src/pages/admin/PlansTab.tsx         квота и срок хранения; кнопка «Активировать»
├── src/components/layout/Navbar.tsx     «Мои визиты» всем аутентифицированным
├── src/pages/DashboardPage.tsx          ← УДАЛЁН (US-22)
└── src/pages/owner/OwnerPage.tsx        ← УДАЛЁН (US-22)
docker-compose.prod.yml                  + Storage__PrivateRoot, + api_private_uploads
DEPLOY.md / DEPLOY-windows.md            + разделы про приватное хранилище
.github/workflows/ci.yml                 + шаг фронтовых тестов, + -warnaserror
```

---

## 18. Порядок работ: что параллельно, что последовательно

```
ВОЛНА 0 — фундамент. Без него не начинается ни одна история эпика фото.
  BACKEND                                        ║  FRONTEND (по API_CONTRACT, не по коду)
  T-B1  PhoneNormalizer (чистая ф-я + юниты)      ║   T-F1  тест-раннер: vitest.config, setup,
  T-B2  SkiaSharp: ImageSignature/ImageProcessor  ║          package.json, шаг в CI, phone.ts + тест
        + юниты + ПРОВЕРКА СБОРКИ ОБРАЗА          ║   T-F2  US-03 виджет-сниппет (эндпоинтов не нужно)
  T-B3  FileStorage + конфиг + compose/DEPLOY     ║   T-F3  US-08 «Мои визиты» в меню
          │                                       ║
ВОЛНА 1 — максимально параллельная. Внутри волны задачи независимы.
  T-B4  US-07: заметка-объект + ClientNote.BookingId (миграция 1)   ║  T-F4  US-04 блок/разблок компании
  T-B7  US-24: поля тарифа + EffectivePlan (миграция 3)             ║  T-F5  US-05 реактивация тарифа
  T-B8  US-06: причина отмены + 3 поля BookingDto                   ║  T-F6  US-24 поля тарифа в PlansTab
  T-B9  US-21 ч.1: runner + IScheduledTask + TryAcquireAsync        ║  T-F7  US-06 формы причины отмены
        + ScheduledTaskState (миграция 4) + GET scheduled-tasks     ║         + оживление цены/слага
  T-B12 US-04 блокировка компании (правок BE нет — только контракт) ║  T-F8  uploadError.ts + тесты
          │                                       ║
ВОЛНА 2 — эпик фото. Строго после T-B2/T-B3/T-B4.
  T-B5  US-19: ClientNotePhoto + миграция 2 + ImageUploadService    ║  T-F9  AuthedImage + useAuthedImage
        + POST photo + GET photo/thumb + квота + rate limiting      ║  T-F10 PhotoGallery + PhotoViewerModal
  T-B6  US-20: удаление фото/заметки, каскады, файлы               ║         + тест «пустой список»
  T-B10 US-21 ч.2: PhotoRetentionCleanupTask (после T-B5, T-B7, T-B9)║  T-F11 NotePhotoUploader в двух экранах
  T-B11 US-25: аватар, картинка услуги, перевод логотипа            ║  T-F12 US-25 формы загрузки
  T-B13 GET /api/companies/{id}/photo-usage (после T-B5, T-B7)      ║  T-F13 «занято X из Y МБ» в настройках
          │                                       ║
ВОЛНА 3 — то, что трогает всё и потому идёт после всего.
  T-B14 US-26: нормализация в 7 точках входа + миграция 5 (ОТДЕЛЬНЫЙ PR, ПОСЛЕДНИЙ ИЗ BE)
  T-B15 US-22: удаление мёртвого кода, GET /api/bookings/my, CommissionPercent
  T-B16 US-22 ч.2: удаление Blazor-проекта из solution
  T-B17 CI: -warnaserror (только после T-B16)
```

### Жёсткие последовательные связи

| Связь | Причина |
|---|---|
| T-B2 → T-B5, T-B11 | без `ImageProcessor` загружать нечего |
| T-B3 → T-B5, T-B11 | без `FileStorage` некуда писать |
| T-B4 → T-B5, T-B6 | фото крепится к заметке; пока заметка — строка, привязать не к чему (SPEC §5.3) |
| T-B5 → T-B6 → T-B10 | три правки одной темы; T-B10 удаляет то, что T-B5 создаёт |
| T-B7 → T-B10, T-B13 | уборке и счётчику нужны поля тарифа в `EffectivePlan` |
| T-B9 → T-B10 | задача не существует без runner'а |
| T-B4 → T-B14 | нормализация `ClientNote.GuestPhone` правит тот же контроллер |
| T-B5 → T-B14 | миграция 5 удаляет пользователей; `ClientNotePhoto.UploadedByUserId` должен уже существовать со `SetNull` (§14.2) |
| T-B15 → T-B16 → T-B17 | `-warnaserror` красит CI, пока Blazor-проект в solution |
| T-B8 ↔ T-F7 | **мёржатся вместе**: `BookingDto` без фронта оставит мёртвые ветки мёртвыми, фронт без DTO — сломает `tsc` |
| T-B4 ↔ T-F10/T-F11 | **мёржатся вместе**: `notes: string[] → object[]` — ломающее, оба экрана падают на старом типе (R7) |
| T-B14 ↔ фронт | правок фронта не требует (канон приходит с сервера), но `formatPhone` (T-F1) должен быть в `main` раньше, иначе пользователь увидит цифровую строку |

### Точки пересечения BE↔FE (обе стороны читают только `API_CONTRACT.md`)

| # | Изменение бэкенда | Что делает фронт |
|---|---|---|
| 1 | `GET /api/masters/clients`: `notes: string[]` → объекты **(BREAKING)** | T-F10/T-F11: `MasterClient.notes` меняет тип, оба экрана переписывают рендер заметок |
| 2 | `BookingDto` + `cancellationReason`/`price`/`companySlug` **(BREAKING)** | T-F7: показ причины, оживление цены и «Записаться снова» |
| 3 | `GET /api/bookings/my` удалён **(BREAKING)** | T-F15: удалить `bookingsApi.getMyBookings` (потребителей нет) |
| 4 | Канон телефона в телах ответов **(BREAKING)** | T-F1: `formatPhone` во всех местах показа номера |
| 5 | `POST /api/client-notes/{noteId}/photos` → 201 | T-F11: форма загрузки, прогресс, `uploadError.ts` |
| 6 | `GET /api/client-notes/photos/{id}` требует токен | T-F9: **blob через axios**, не `<img src>` (§12.2) |
| 7 | `POST /api/profile/avatar`, `POST /api/services/{id}/image` | T-F12: две формы |
| 8 | `GET /api/companies/{id}/photo-usage` | T-F13: «занято X из Y МБ» + предупреждение при > 90 % |
| 9 | `PUT /api/admin/plans/{id}` принимает 2 новых поля | T-F6: два поля в форме тарифа + показ в карточке |
| 10 | `PUT /api/admin/companies/{id}` — **эндпоинт уже есть** | T-F4: кнопка + модалка подтверждения (BE не правится вовсе) |
| 11 | `PUT /api/admin/plans/{id}` с `isActive: true` — **эндпоинт уже есть** | T-F5: кнопка «Активировать» (BE не правится вовсе) |
| 12 | `GET /api/admin/scheduled-tasks` | Экрана нет (US-21 п. 6): проверяется curl'ом. Фронтовой задачи нет |
| 13 | Виджет `/embed/:slug` — **нового эндпоинта не требуется** | T-F2: сниппет из `window.location.origin` + `company.slug`; предупреждение по `onlineBookingEnabled` |
| 14 | `PATCH /api/bookings/{id}/cancel` — 400 на причину > 300 | T-F7: `maxLength={300}` в поле + `cancelError.ts` |
| 15 | Загрузки могут вернуть `429` | T-F8: ветка в `uploadError.ts` |

---

## 19. Задачи

Формат: **ID · название** — файлы · зависимости · тесты · критерий готовности. Каждая задача = один PR.
`TEST_CATALOG.md`, `API_CONTRACT.md` и `API_DOCUMENTATION.md` обновляются **в том же PR**, что и код.

### 19.1 Backend

**T-B1 · `PhoneNormalizer`** (US-26 ч. 1)
- Файлы: `Services/PhoneNormalizer.cs`; `ServiceBooking.UnitTests/PhoneNormalizerTests.cs`.
- Зависимости: нет. Точек вызова **пока не подключаем** — это отдельная задача T-B14.
- Тесты: ≥ 10 юнит-кейсов из US-26 п. 10.
- Готово: `dotnet test ServiceBooking.UnitTests` зелёный без PostgreSQL; функциональный набор не задет.

**T-B2 · SkiaSharp: проверка содержимого и обработка** (US-19 пп. 1, 3)
- Файлы: `ServiceBooking.API.csproj` (2 пакета); `Services/ImageSignature.cs`, `Services/ImageProcessor.cs`;
  `ServiceBooking.UnitTests/ImageSignatureTests.cs`, `ImageProcessorTests.cs`; комментарий в `Dockerfile`.
- Зависимости: нет.
- Тесты (юниты): JPEG/PNG/WEBP-сигнатуры распознаны; `.jpg` с текстом внутри → не распознан;
  **EXIF `Orientation=6` → размеры перевёрнуты** (§4.3); 3000×2000 → 1600×1067; 800×600 не апскейлится;
  PNG-вход → PNG-выход, JPEG-вход → JPEG-выход; выходные байты не содержат маркера `Exif`.
- **Готово: `docker compose build api` проходит И контейнер обрабатывает реальный файл** (ручная проверка
  задокументирована в PR). Локального `dotnet run` недостаточно (§12 п. 1 SPEC).

**T-B3 · `FileStorage`: два класса хранения** (US-19 п. 4)
- Файлы: `Services/FileStorage.cs`; `Program.cs` (fail-fast на `PrivateRoot`);
  `docker-compose.prod.yml`; `.env.production.example`; `DEPLOY.md`; `DEPLOY-windows.md`;
  комментарий в `deploy/nginx/ezbook.conf`.
- Зависимости: нет.
- Тесты: юнит на `HasFreeSpace` и на проверку выхода за корень; функциональных пока нет.
- Готово: `Storage:PrivateRoot` внутри `wwwroot` роняет старт в Production и **не** роняет в Development
  (dev-удобство важнее, дефолт и так корректен); оба runbook'а описывают, где лежат файлы и что бэкапить.

**T-B4 · US-07: заметка становится объектом + `BookingId`** — **BREAKING, мёржится с T-F10/T-F11**
- Файлы: `Core/Entities/ClientNote.cs`; `Infrastructure/Data/AppDbContext.cs`;
  миграция `AddClientNoteBookingId`; `DTOs/ClientNotes/ClientNoteDto.cs`;
  `Controllers/MastersController.cs` (`GetClients` — объекты + `Take(50)` на заметки; `AddNote` — `bookingId`;
  `DeleteNote` — права по Q16 и правило 403/404 из `API_CONTRACT` §5.4).
- Зависимости: нет (по коду), но блокирует всю волну 2.
- Тесты: `MC-0xx` автор удаляет свою → 204; `MC-0xy` владелец удаляет чужую → 204;
  `MC-0xz` мастер удаляет заметку коллеги → **403**; `MC-0xw` чужая компания → **404**;
  `MC-0xv` выдача содержит `id`, `createdAt`, `authorName`, `canDelete`.
- Готово: `GET /api/masters/clients` отдаёт заметки объектами; `bookingId` заполняется при создании
  из панели под записью; терминология «отзыв → заметка» приведена в порядок на фронте (US-07 п. 5, T-F11).

**T-B5 · US-19: фото — модель, загрузка, приватная раздача, квота, rate limiting** — **самая крупная**
- Файлы: `Core/Entities/ClientNotePhoto.cs`; `AppDbContext`; миграция `AddClientNotePhotos`;
  `Services/ImageUploadService.cs`, `Services/PhotoQuota.cs`;
  `Controllers/ClientNotePhotosController.cs`; `Program.cs` (`AddRateLimiter`/`UseRateLimiter`);
  `appsettings.Testing.json`.
- Зависимости: **T-B2, T-B3, T-B4, T-B7** (квота читается из `EffectivePlan`).
- Тесты: `MC-1xx` мастер загружает → 201 и фото в выдаче; `MC-1xy` посторонний → 403;
  `MC-1xz` шестое фото → 400; `MC-2xx` `.jpg` с не-изображением → 400; `MC-2xy` фото чужой компании → 404;
  `MC-2xz` аноним → 401; `MC-2xw` 6 МБ → 400/413; `MC-2xv` EXIF отсутствует в сохранённом потоке;
  `MC-2xu` исчерпанная квота → 400 **и файла на диске нет**; `MC-2xt` `SuperAdmin` → 403;
  `MC-2xs` повторная отправка того же файла → 200, второй строки нет, объём не вырос (§6.3);
  `MC-2xr` при `Uploads:PerUserPerMinute = 1` вторая загрузка → 429.
- Готово: ни один тест не находит файл фото через `/uploads/`; grep по `UseStaticFiles` даёт одну строку.

**T-B6 · US-20: удаление, каскады, жизненный цикл файлов**
- Файлы: `ClientNotePhotosController.Delete`; `MastersController.DeleteNote` (удаление файлов после commit);
  `AppDbContext` (явные каскады + комментарии).
- Зависимости: **T-B5**.
- Тесты: `MC-3xx` удаление заметки удаляет фото и файлы; `MC-3xy` чужой мастер → 403;
  `MC-3xz` владелец удаляет фото своего мастера → 204; `MC-3xw` после удаления объём уменьшился;
  `MC-3xv` удаление мастера из компании фото **не** удаляет (US-20 п. 4).
- Готово: комментарии в коде объясняют и порядок «БД → диск», и почему `RemoveMember` фото не трогает.

**T-B7 · US-24: поля тарифа**
- Файлы: `Core/Enums/PhotoRetention.cs`; `SubscriptionPlanConfig`; `Services/SubscriptionResolver.cs`;
  миграция `AddPlanPhotoLimits`; валидация в `AdminController.CreatePlan/UpdatePlan`.
- Зависимости: нет.
- Тесты: `ADM-0xx` создание/редактирование плана с новыми полями; `ADM-0xy` отрицательная квота → 400;
  юниты резолвера: Free → 100 МБ / 6 мес, неактивный план → Free, безлимит (`null`) сохраняется.
- Готово: `GET /api/admin/plans` отдаёт новые поля (сущность сериализуется напрямую — правок DTO нет).

**T-B8 · US-06 + Q12: причина отмены и три поля `BookingDto`** — **BREAKING, мёржится с T-F7**
- Файлы: `DTOs/Bookings/BookingDto.cs`; `BookingsController.MapToDto` и `Cancel` (валидация 300).
- Зависимости: нет.
- Тесты: `BK-0xx` отмена с причиной → `GET /{id}` её возвращает; `BK-0xy` > 300 символов → 400;
  `BK-0xz` отмена без причины → 204 и `null`; `BK-0xw` `price`/`companySlug` присутствуют.
- Готово: миграции не потребовалось (оба поля уже в сущности) — отмечено в PR.

**T-B9 · US-21 ч. 1: планировщик** (инфраструктура без задач)
- Файлы: `Services/Scheduling/*`; `Services/AdvisoryLock.cs` (+`TryAcquireAsync`);
  `Core/Entities/ScheduledTaskState.cs`; `AppDbContext`; миграция `AddScheduledTaskState`;
  `Program.cs`; `AdminController.GetScheduledTasks`; `appsettings.Testing.json`.
- Зависимости: нет.
- Тесты: `SCH-0xz` падающая задача не мешает соседней и не роняет процесс (регистрируется тестовая
  задача-бросающая); `SCH-0xw` второй экземпляр пропускает запуск при занятом локе;
  `SCH-0xv` `GET /api/admin/scheduled-tasks` — 403 не-админу, 200 админу с последним запуском;
  юниты: расчёт «пора ли» по периоду, поведение после неуспешного прохода, пометка «просрочена»
  при > 2 периодов (≥ 8 кейсов, US-21 п. 13).
- Готово: **рецензент письменно подтверждает**, что вторая задача добавляется классом и одной строкой DI
  (US-21 п. 1) — в PR приводится дифф гипотетической второй задачи.

**T-B10 · US-21 ч. 2: уборка фото по сроку**
- Файлы: `Services/Scheduling/Tasks/PhotoRetentionCleanupTask.cs`; `Program.cs` (одна строка DI).
- Зависимости: **T-B5, T-B7, T-B9**.
- Тесты: `SCH-0xx` просроченное фото и файл удалены; `SCH-0xy` тариф «бессрочно» — не тронуто;
  `SCH-0xu` сирота младше 24 ч **не** удалена, старше — удалена; юниты `PhotoQuota` на границу
  «ровно 6 месяцев» и три варианта срока.
- Готово: в логе видны старт, длительность и цифры; ни одного запроса к БД на компанию (проверяется
  счётчиком запросов в тесте или чтением кода на ревью — §7.2).

**T-B11 · US-25: аватар, картинка услуги, перевод логотипа**
- Файлы: `ProfileController` (+`POST avatar`), `ServicesController` (+`POST {id}/image`),
  `CompaniesController.UploadLogo` (переписан на `ImageUploadService`).
- Зависимости: **T-B2, T-B3, T-B5** (rate-limit-политика заводится в T-B5).
- Тесты: `PROF-0xx` аватар → 200, `avatarUrl` не `null`, файл существует; `SVC-0xx` картинка услуги
  мастером → 403; `CO-0xx` логотип с подменённым `Content-Type` → 400 (регрессия на новый загрузчик).
- Готово: grep по `IFormFile` показывает один путь обработки; аватары/услуги/логотипы **не** попадают
  в квоту (проверяется тестом на `photo-usage`).

**T-B12 · US-04: блокировка компании** — **backend-правок нет**
- Файлы: только `API_CONTRACT.md` и `API_DOCUMENTATION.md` (фиксация существующего поведения).
- Тесты: `ADM-0xx` блокировка → компания пропадает из `GET /api/companies`, `GET /{slug}` → 404;
  разблокировка возвращает.
- Готово: тест закрепляет поведение, на которое опирается T-F4.

**T-B13 · `GET /api/companies/{id}/photo-usage`**
- Зависимости: **T-B5, T-B7**.
- Тесты: `CO-0xx` владелец видит объём и квоту; `CO-0xy` посторонний → 403;
  `CO-0xz` `SuperAdmin` видит цифры (но не содержимое — см. `MC-2xt`).

**T-B14 · US-26 ч. 2: нормализация во всех точках входа + миграция** — **отдельный PR, последний из BE**
- Файлы: семь контроллеров из §11.2; миграция `NormalizePhoneNumbers`.
- Зависимости: **T-B1, T-B4, T-B5** (§14.2).
- Тесты: `AUTH-0xx` регистрация «8 999…» и вход «+7 999…» — один аккаунт;
  `AUTH-0xy` повторная регистрация в другом формате → «номер занят»;
  `PROF-0xx` смена телефона на форматированный сохраняет канон;
  `CO-0xx` добавление сотрудника в другом формате находит существующего;
  `MC-0xx` гость, записанный дважды в разных форматах — один человек с общей историей и заметками;
  тест «в БД только цифры» по всем пяти колонкам (US-26 п. 5).
- Готово: **миграция прогнана на копии dev-базы** (DoD п. 5); в PR перечислены все переписанные
  существующие тесты (US-26 п. 11) и показаны рядом C#- и SQL-реализации правила (§14.3).

**T-B15 · US-22: мёртвый код и вестигиальные поля** — **BREAKING (`GET /api/bookings/my`)**
- Файлы: `BookingsController.cs:273-293`; `ProfileController` и `AdminController` (`CommissionPercent`);
  `Core/Entities/AppUser.cs`; `frontend/src/api/bookings.ts`, `admin.ts`;
  удаление `pages/DashboardPage.tsx`, `pages/owner/OwnerPage.tsx`;
  `ServiceBooking.API/Properties/launchSettings.json`.
- Зависимости: T-B8 (общий файл `BookingsController`).
- Готово: удалены только тесты, покрывавшие удалённый эндпоинт; перечислены в PR и `TEST_CATALOG.md`.

**T-B16 · US-22 ч. 2: удаление Blazor-проекта** · **T-B17 · CI `-warnaserror`**
- Готово: `dotnet build ServiceBooking.sln` — **0 предупреждений**; CI красный на первом же новом варнинге.

### 19.2 Frontend

**T-F1 · Тест-раннер + `formatPhone`** (US-23, US-26 п. 6)
- Файлы: `package.json`, `vitest.config.ts`, `src/test/setup.ts`, `src/utils/phone.ts` + тест;
  шаг в `.github/workflows/ci.yml`.
- Зависимости: нет. **Делать первой** — остальные фронтовые задачи кладут в неё свои тесты.
- Готово: `npm run test:run` зелёный локально и в CI; джоб `frontend` ≤ 3 мин.

**T-F2 · US-03: код вставки виджета** — эндпоинтов не требует
- Файлы: `pages/owner/CompanyManagePage.tsx` (вкладка «Настройки»).
- Готово: сниппет с `width`/`height`/`title`/`loading="lazy"`, домен из `window.location.origin`,
  кнопки «Скопировать» и «Открыть предпросмотр», предупреждение по `allowSelfBooking`/`onlineBookingEnabled`;
  ручная проверка — сниппет в пустом HTML показывает рабочую форму.

**T-F3 · US-08: «Мои визиты» в меню** — `Navbar.tsx`, порядок пунктов из US-08 п. 2.

**T-F4 · US-04: блокировка компании** — `AdminPage.tsx` + новый `src/utils/companyAdminError.ts`;
инвалидация `['admin-companies']`; модалка с перечислением последствий.

**T-F5 · US-05: реактивация тарифа** — `admin/PlansTab.tsx`, кнопка «Активировать» и «Редактировать»
у неактивных, ошибки через `getPlanErrorMessage`.

**T-F6 · US-24: поля тарифа в админке** — `admin/PlansTab.tsx`, два поля в форме и в карточке.

**T-F7 · US-06 + Q12: причина отмены, цена, «Записаться снова»** — **мёржится с T-B8**
- Файлы: `MyBookingsPage.tsx`, `ClientBookingsPage.tsx`, `types/index.ts`, `src/utils/cancelError.ts` + тест.
- Готово: у отменённой записи причина видна обеим сторонам; при пустой блок не рисуется;
  `maxLength={300}`; ветки цены и слага наконец отрисовываются.

**T-F8 · `uploadError.ts` + тесты** — по `API_CONTRACT.md` §6, до мёржа T-B5.

**T-F9 · `AuthedImage` + `useAuthedImage`** (§12.2) — blob через axios, `IntersectionObserver`,
освобождение объектных URL, тест на отсутствие запроса вне вьюпорта.

**T-F10 · Галерея фото** — `components/clientNotes/PhotoGallery.tsx`, `PhotoViewerModal.tsx`;
`useOverlayDismiss` для Esc; осмысленный `alt` («Фото работы, 12 марта, стрижка»);
**тест на пустой список — обязательный минимум US-23**.

**T-F11 · Заметки-объекты и загрузка фото в двух экранах** — **мёржится с T-B4**
- Файлы: `api/masters.ts` (тип `notes`), `api/clientNotes.ts` (новый),
  `MasterClientsPage.tsx`, `MyBookingsPage.tsx` (панель под записью), `components/clientNotes/*`.
- Готово: автор и дата у заметки; кнопка удаления при `canDelete` с подтверждением; до 5 фото,
  `accept="image/*"` + `capture`; прогресс; инвалидация `['master-clients', companyId]`;
  терминология «Заметки о клиенте» / «Добавить заметку…» (US-07 п. 5).

**T-F12 · US-25: формы аватара и картинки услуги** — `ProfilePage.tsx`, `owner/CompanyManagePage.tsx`;
показ аватара в витрине и списке мастеров, иначе — существующая заглушка с инициалами.

**T-F13 · Лимиты в настройках компании** — «занято X из Y МБ», «фото хранятся N месяцев»,
предупреждение при > 90 % с предложением сменить тариф (US-24 п. 4).

---

## 20. Риски SPEC → ответ архитектуры

| # | Риск | Ответ |
|---|---|---|
| **R1** | Фото валят диск | §4.2 (ресайз всегда), §6 (квота под advisory lock), §7.2 (уборка по сроку без N+1), §9.4 (порог свободного места). Оценка §5.9 п. 12 SPEC держится: 1600 px q82 ≈ 300 КБ |
| **R2** | Утечка фото | §3 (вне `wwwroot`, конфигурируемый корень), §3.4 (fail-fast на корне внутри `wwwroot`), §12.1 (авторизованный эндпоинт, 404 для чужой компании, 403 для `SuperAdmin`), §12.2 (blob вместо `<img src>`: прямая ссылка без токена не отдаёт ничего). Тест `MC-2xz` обязателен |
| **R3** | Фоновая задача удаляет лишнее | §8.4 (**неблокирующий** лок вместо блокирующего — иначе второй экземпляр просто ждал бы и удвоил проход), §9.1 (порции, «БД → диск»), §9.3 (сироты только старше 24 ч), юниты `PhotoQuota` на границу срока и на `Forever` |
| **R4** | Миграция телефонов схлопывает не то | §14.3: детерминированное правило, `RAISE EXCEPTION` с перечислением строк на владельцах компаний, отдельный последний PR, обязательный прогон на копии dev-базы. Остаточный риск «C# и SQL разошлись» закрыт общими фикстурами (§14.3) |
| **R5** | Правовой риск по фото | Принят заказчиком (Q6). Архитектура его не закрывает и механику согласия **не проектирует**; единственное, что она делает, — не мешает добавить её позже: согласие ляжет полем на `ClientNote`, а не в схему фото |
| **R6** | Новые поля тарифа ломают резолвер | §7.1: `Resolve` не меняется ни на строку, поля едут записью `EffectivePlan`; юниты на Free и на неактивный план |
| **R7** | Ломающие изменения роняют экраны | §18: T-B4↔T-F10/T-F11 и T-B8↔T-F7 мёржатся вместе; сводная таблица `API_CONTRACT.md` §20 перечисляет все четыре |
| **R8** | Планировщик «под одну задачу» | §8.1: расширяемость — структурное свойство (`IScheduledTask` + runner, не знающий имён); критерий готовности T-B9 — письменное подтверждение рецензента с диффом гипотетической второй задачи |
| **R9** | Библиотека изображений: лицензия/нативные зависимости | §2.1: MIT без порогов выручки; `NoDependencies`-натив, Dockerfile не меняется; **критерий готовности — сборка образа**, а не локальный запуск |
| **R10** | Скоуп не влезает | §18: порядок урезания из SPEC §3 сохранён — T-B11/T-F12 (US-25) режутся первыми и ни от чего не зависят снизу; T-F5 (US-05) — вторым. T-B4, T-B5, T-B9/T-B10, T-B14 не режутся |

---

## 21. Расхождения со SPEC и решения архитектора

Четыре позиции, где архитектура выходит за букву SPEC или дополняет его. Каждая обоснована; если
заказчик решит иначе, меняется только объём перечисленных задач.

### 21.1 `AdvisoryLock` не умеет того, что требует US-21 п. 5

SPEC ссылается на «уже принятый в проекте `pg_advisory_xact_lock`» и одновременно требует, чтобы не
получивший блокировку экземпляр **пропускал** запуск. Существующий helper блокирует и ждёт — то есть
буквальная реализация дала бы два последовательных прохода вместо одного пропущенного. Решение —
добавить `TryAcquireAsync` на `pg_try_advisory_xact_lock` (§8.4). Ключевая конвенция и файл те же;
существующие четыре вызова не трогаются.

### 21.2 Порядок «файл ↔ БД» при загрузке SPEC не задаёт

SPEC §9 п. 3 и US-20 п. 6 фиксируют только удаление («сначала БД, потом диск»). Для загрузки этот
порядок дал бы строку без файла при сбое записи — прямо запрещённое состояние. Поэтому загрузка идёт
в обратном порядке: диск → БД (§1.4, §4.1). Инвариант формулируется не как «порядок», а как
«**строки без файла не существует ни при каком отказе**», и оба порядка — его следствия.

### 21.3 Право удалить фото шире буквы Q16 на одного человека

Q16 говорит «автор или владелец компании» — про заметку. Буквально перенесённое на фото, это значит,
что мастер, приложивший фото к заметке коллеги, не может убрать собственную ошибку. Принято:
**удалить фото может автор заметки, загрузивший это фото, или владелец компании.** Это ровно тот же
принцип («создатель данных плюс владелец»), которым обоснован сам Q16, и он не расширяет круг за
пределы персонала компании. Отменяется удалением одного условия, если заказчик прочтёт Q16 строго.

### 21.4 `403` vs `404` при доступе к чужой заметке/фото

SPEC (US-07 п. 6) оставляет выбор контракту. Принято правило, сохраняющее межарендную изоляцию —
главное свойство цикла A:

- вызывающий **не** персонал компании этого объекта → **`404`** (существование чужого id не подтверждаем);
- вызывающий персонал этой компании, но не автор и не владелец → **`403`** (правило прав видно и тестируемо).

### 21.5 Мелкие уточнения без изменения объёма

- **`GET /api/masters/clients` получает `Take(50)` на заметки** (NFR §9 п. 6): выборка перестаёт быть
  неограниченной, пагинация при этом не вводится (она отложена, SPEC §8.2).
- **`ProfilePlanDto` новых полей не получает.** Владелец видит квоту через
  `GET /api/companies/{id}/photo-usage` (§12.3) — квота осмысленна на компанию, а профиль про аккаунт.
- **Текст `400` про телефон содержит слово `phone`** и потому попадает в существующую ветку
  `bookingError.ts:30`. Проверено: результат («Укажите имя и телефон») для этого сценария корректен,
  маппер не правится. Осознанное исключение из правила цикла A, §11.2.
- **`GET /api/admin/scheduled-tasks` экрана не получает** (US-21 п. 6, предположение низкого риска) —
  фронтовой задачи в §19.2 нет.

---

## 22. Что архитектура сознательно НЕ делает

- **Не вводит внешний шедулер** (§8.2), **не вводит очередь сообщений**, **не вводит облачное
  хранилище** (S3/MinIO): местный диск + docker volume соответствуют обоим контурам деплоя; переход на
  объектное хранилище — замена реализации `FileStorage`, и она изолирована в одном файле именно затем.
- **Не показывает клиенту его фото** и **не трогает `Review`** — Q5 и SPEC §5.1.
- **Не проектирует механику согласия на съёмку** — Q6, риск R5 принят заказчиком.
- **Не вводит пагинацию** — только `Take` на новых и затронутых выборках (SPEC §8.2).
- **Не трогает часовые пояса**: срок хранения считается сутками, `DateTime.UtcNow` достаточно (§8.1 SPEC).
- **Не расширяет rate limiting на вход, регистрацию и гостевую запись** — отдельный цикл (SPEC §8.2).
- **Не переписывает `SubscriptionResolver`, `SlotCalculator`, `CompanyMembership`** — они стабилизированы
  в цикле A и в этом цикле только используются.
- **Не унифицирует шесть копий предикатов прав** и **не рефакторит `CompaniesController`** —
  `CURRENT_STATE` §9 P2-10/11, отложено.
- **Не меняет формат тел ошибок 4xx** (§13) и не добавляет `AddProblemDetails()`/`UseStatusCodePages`.
- **Не добавляет security-заголовки** — задача devops; цикл только фиксирует требование к ней (US-03 п. 6).
