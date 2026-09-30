# API_CONTRACT — цикл 31 ServiceBooking: галерея, мультизагрузка, блок «Каталог» у салона, шапка карточки

**Разделы §31.20–§31.29.** Решения и механизмы — `ARCHITECTURE_CYCLE31.md` §31.0–§31.18. **Источник истины по форме** —
`contracts/cycle31/openapi.yaml`: при расхождении этого текста со схемой по форме права схема, по смыслу и порядку
проверок — этот текст. Требования — корневой `SPEC.md` цикла 31. Базовая ревизия — `c19a83c`.

Корневой `API_CONTRACT.md` — документ цикла 3, по конвенции проекта (`CURRENT_STATE.md` §6.5, §10.5) не
перезаписывается. Остальной API описан в `API_DOCUMENTATION.md` и контрактах прошлых циклов, в цикле 31 он не меняется.

---

## §31.20. Сводка изменений API

| Маршрут | Что меняется | Ломает ли клиентов |
|---|---|---|
| `GET /api/companies/{id}/catalog-listing` | **новый** — состояние показа салона в каталоге ezbook.ru | нет |
| `PUT /api/companies/{id}/catalog-listing` | **новый** — переключатель показа салона, сохраняется сразу | нет |
| `GET\|PUT /api/shops/{shopId}/catalog-listing` | текст пункта `HiddenByOwner`: «Показ выключен в настройках» → **«Показ включен в настройках»**. Коды, `done`, логика — прежние | нет (текст фронт выводит дословно) |
| `POST /api/companies/{id}/photos` | политика частоты `uploads` → **`company-photos`** (20/мин на пользователя) | нет |
| `DELETE /api/companies/{id}/photos/{photoId}`, `PUT /api/companies/{id}/photos/order` | политика `uploads` → **`company-photos-edit`** (60/мин на пользователя) | нет |
| `GET /api/companies`, `GET /api/companies/public` | форма и правило видимости **прежние**; фильтр переписан на общий `SalonListingQuery` (§31.5 архитектуры) | нет |
| `PUT /api/companies/{id}` | **без изменений**, поле `showInPublicListing` принимается как раньше. Кабинет салона его больше не шлёт | нет |

Пакетного маршрута загрузки **нет**: мультизагрузка — последовательные одиночные `POST …/photos` (§31.26).
Новых полей в существующих DTO нет. Миграций нет.

## §31.21. `GET /api/companies/{id}/catalog-listing` — блок «Каталог ezbook.ru» салона

**Авторизация:** `[Authorize]`. Видит тот, кто может менять `ShowInPublicListing` через `PUT /api/companies/{id}`:
владелец компании или SuperAdmin (`CompanyAccess.CanManageCompanyAsync`, `superAdminBypass: true`). Мастер и
прочие сотрудники салона — нет. Лимита частоты нет (как у маршрута магазина).

**Порядок проверок:**

| # | Условие | Ответ |
|---|---|---|
| 1 | нет или недействителен токен | 401, пустое тело |
| 2 | не принята новая редакция документов (глобальный `LegalConsentFilter`) | 451 (как везде) |
| 3 | компании нет **или** вызывающий не владелец и не SuperAdmin | **404, пустое тело** — одинаково, не оракул |
| 4 | компания — магазин (`Kind = Orders`) | 409 `text/plain` «Это магазин: записи, услуги и расписание для него недоступны.» (`CompanyKindGuard.ShopRefusalText`) |
| 5 | иначе | 200 `SalonCatalogListingDto` |

Порядок «права, потом вид» — конвенция проекта (`CURRENT_STATE.md` §6.1): посторонний с id магазина получает 404, а не 409.

**Ответ 200** (форма совпадает с `CatalogListingDto` магазина, на сервере это тот же record):

```json
{
  "showInCatalog": true,
  "allowedByPlan": true,
  "visible": true,
  "statusText": "Салон виден в каталоге ezbook.ru",
  "notAllowedByPlanText": null,
  "checklist": [
    { "code": "HiddenByOwner", "text": "Показ включен в настройках", "done": true }
  ]
}
```

| Поле | Смысл |
|---|---|
| `showInCatalog` | `Company.ShowInPublicListing` |
| `allowedByPlan` | действующий тариф разрешает показ, то есть `SubscriptionResolver…AllowPublicListing` (= `CompanyDto.planAllowsPublicListing`) |
| `visible` | салон **прямо сейчас** есть в `GET /api/companies` и в `GET /api/companies/public` (без фильтров города и поиска) |
| `statusText` | `visible ? "Салон виден в каталоге ezbook.ru" : "Салона сейчас нет в каталоге"` |
| `notAllowedByPlanText` | `null`, если `allowedByPlan`; иначе «Показ в каталоге не входит в ваш тариф — повысьте тариф, чтобы включить». Поле **всегда присутствует** |
| `checklist` | 1–3 пункта в фиксированном порядке, таблица ниже. `visible ⇔ все пункты done` |

**Пункты чек-листа (вариант A, Q-31-1 — правило видимости не меняется):**

| Порядок | `code` | `text` (дословно, собирает сервер) | Когда есть в списке | `done` |
|---|---|---|---|---|
| 1 | `SalonBlocked` | «Салон заблокирован администратором» | только если `IsActive = false` | всегда `false` |
| 2 | `NotAllowedByPlan` | «Показ в каталоге не входит в ваш тариф» | только если тариф не разрешает | всегда `false` |
| 3 | `HiddenByOwner` | «Показ включен в настройках» | всегда | `= showInCatalog` |

Примеры состояний:
- всё выполнено → 1 пункт `✓`, `visible = true`;
- показ выключен → 1 пункт `○`, `visible = false`, «Салона сейчас нет в каталоге»;
- тариф не разрешает и показ включён → `[NotAllowedByPlan ○, HiddenByOwner ✓]`, `visible = false`,
  `notAllowedByPlanText` не `null`;
- салон заблокирован, тариф не разрешает, показ выключен → 3 пункта `○`.

`SalonBlocked` — новое значение серверного enum `CatalogListingCheckCode`, дописано **в конец**. Маршрут магазина его
никогда не отдаёт, контракт цикла 25 для магазина не меняется.

## §31.22. `PUT /api/companies/{id}/catalog-listing` — переключатель показа салона

**Авторизация:** как у GET, плюс `[RequiresOwnerTerms]` (как у `PUT /api/companies/{id}` и `PUT /api/shops/{shopId}/catalog-listing`).

**Тело:** `application/json`, `{ "showInCatalog": true }`. На сервере — существующий `CatalogListingInputDto(bool? ShowInCatalog)`.

**Порядок проверок:**

| # | Условие | Ответ |
|---|---|---|
| 1 | нет токена | 401 |
| 2 | устаревшая редакция условий для владельца (`RequiresOwnerTerms`) или глобальный гейт | 451 |
| 3 | нет тела или не `application/json` | 415, пустое тело (фреймворк) |
| 4 | тело — нечитаемый JSON | 400 `text/plain` (фреймворк, `ModelValidationErrorFormatter`). От `{id}` не зависит, поэтому не оракул |
| 5 | компании нет или она не ваша | 404, пустое тело |
| 6 | компания — магазин | 409 `text/plain` `ShopRefusalText`; значение не меняется |
| 7 | `showInCatalog` отсутствует или `null` | 400 `text/plain` «Не указано, показывать ли салон в каталоге» |
| 8 | `showInCatalog = true`, а тариф не разрешает | 409 `application/json` `{"code":"CatalogListingNotAllowedByPlan","message":"Показ в каталоге не входит в ваш тариф — повысьте тариф, чтобы включить"}`; значение не меняется |
| 9 | иначе | сохранить `Company.ShowInPublicListing`, 200 `SalonCatalogListingDto` уже после сохранения |

- Выключить показ можно **всегда**, в том числе на тарифе, который показ не разрешает, и у заблокированного салона.
- Повтор того же значения — 200, идемпотентно.
- Маршрут меняет **ровно одну** колонку. Кэшей на сервере нет: `GET /api/companies[/public]` видят изменение сразу.

**Почему у 409 два формата:** 409 от `CompanyKindGuard` — голая строка на всех салонных маршрутах. 409 «тариф не
разрешает» — JSON `{code, message}`, как у того же отказа в блоке магазина (`CatalogConflictDto`, цикл 25). Общий
фронт-компонент разбирает его одинаково для обоих сайтов. Кабинет салона на маршрут с id магазина не ходит, поэтому
текстовый 409 фронт видит только в тестах.

## §31.23. Совместимость `ShowInPublicListing` (R-4)

- `PUT /api/companies/{id}` **не меняется**: `showInPublicListing` в теле по-прежнему меняет колонку, отсутствие или
  `null` оставляет её как есть. Проверки тарифа на этом пути по-прежнему **нет**: старый клиент может поставить `true`
  на тарифе без показа, как и раньше. Салон в каталог всё равно не попадёт, чек-лист это покажет.
- `POST /api/companies` (создание, `CabinetPage`) — без изменений.
- `CompanyDto.showInPublicListing`, `publicListingEnabled`, `planAllowsPublicListing` — без изменений.
- **Фронт кабинета салона перестаёт отправлять `showInPublicListing`** в `PUT /api/companies/{id}`: поле убрано из
  значений формы `SettingsTab`. Кнопка «Сохранить изменения» поэтому не может затереть значение, поставленное
  переключателем. Проверяется vitest: в теле запроса нет ключа `showInPublicListing` (§31.13 архитектуры).

## §31.24. Блок магазина `GET|PUT /api/shops/{shopId}/catalog-listing` — новый текст (US-31-04)

Всё как в цикле 25 (`API_CONTRACT_CYCLE25.md` §532), кроме одной строки:

| `code` | Было | Стало |
|---|---|---|
| `HiddenByOwner` | «Показ выключен в настройках» | **«Показ включен в настройках»** |

`done = showInCatalog`, как и раньше. С новым текстом «✓ Показ включен в настройках» означает «включён», а
«○ Показ включен в настройках» — «условие не выполнено». Константа — `CatalogListingRules.HiddenByOwnerText`. Её же
использует чек-лист салона (§31.21), вторую копию строки не заводить. Исторические `ARCHITECTURE_CYCLE25.md` и
`API_CONTRACT_CYCLE25.md` не правятся.

## §31.25. Лимиты частоты галереи (R-1)

| Маршрут | Политика | По умолчанию | Ключ конфигурации |
|---|---|---|---|
| `POST /api/companies/{id}/photos` | `company-photos` | 20 запросов / 1 мин / пользователь, без очереди | `RateLimits:company-photos:PermitLimit`, `…:WindowMinutes` |
| `DELETE /api/companies/{id}/photos/{photoId}` | `company-photos-edit` | 60 / 1 мин / пользователь | `RateLimits:company-photos-edit:*` |
| `PUT /api/companies/{id}/photos/order` | `company-photos-edit` | 60 / 1 мин / пользователь | то же |
| логотип, аватар, фото заметок, картинки услуг и товаров | `uploads` | 10 / 1 мин / пользователь (**не меняется**) | `Uploads:PerUserPerMinute` |

- У разных политик разные окна: пакет из 10 фото тратит 10 из 20 окна `company-photos` и не трогает ни `uploads`, ни
  `company-photos-edit`. «Сделать обложкой» и удаление сразу после пакета не получают 429.
- Тело 429 у всех трёх маршрутов галереи — прежняя строка `Too many uploads. Try again in a minute.` (`text/plain`).
  Фронт распознаёт 429 по коду (`utils/uploadError.ts` → «Слишком много загрузок подряд. Подождите минуту.»).
- Явное ослабление, о котором надо знать (NFR «Безопасность»): суммарный бюджет загрузок одного пользователя вырос с
  10/мин до 10 (`uploads`) + 20 (галерея) = 30 обработок изображения в минуту. Удаление и перестановка — дешёвые
  операции БД под advisory lock, 60/мин. Остальные барьеры галереи на месте: 10 фото на компанию, 5 МБ на запрос,
  сигнатура файла, advisory lock, дедупликация по SHA-256, права владельца.

## §31.26. Мультизагрузка — как клиент пользуется одиночным маршрутом

Сервер не меняется: один файл на запрос, поле `file`. Контракт клиента:
1. Отсев **до отправки**: тип (`image/jpeg`, `image/png`, `image/webp`; если `File.type` пуст — по расширению
   `.jpg/.jpeg/.png/.webp`) и размер (`> 5 × 1024 × 1024` байт). Такие файлы помечаются ошибкой, запросов за ними нет.
2. Обрезка по остатку мест: `remaining = 10 − текущее число фото`. Из прошедших отсев берутся первые `remaining` в
   порядке выбора. Для остальных — одно сообщение со списком имён, запросов нет.
3. Отправка **строго последовательно**, по одному запросу за раз, в порядке выбора. Так сервер ставит фото в конец в
   нужном порядке (`Position = count` под advisory lock), и гонки нет (R-2).
4. Ответ 201 или 200 — фото добавлено или уже было. Клиент вставляет его в кэш галереи **по `id`**, поэтому
   повторный файл не создаёт вторую плитку.
5. Ошибка одного файла (400 с текстом сервера, 403, 404, 413, 429, сеть, 5xx) показывается у этого файла через
   `getUploadErrorMessage`, очередь идёт дальше.
6. Временными (для «Повторить неудавшиеся», US-31-08) считаются: 429, ответ без статуса (сеть, таймаут), 5xx.
   Не повторяются: 400, 403, 404, 413.

## §31.27. Что не меняется

- `GET /api/companies/{id}/photos`, `GET /api/companies/{slug}`, `GET /api/storefront/{slug}`, `CompanyDto`,
  `StorefrontDto`: шапка карточки (US-31-06) новых запросов и полей не требует.
- Тексты ошибок галереи, лимит 10 фото, 5 МБ, правовая подсказка `CompanyPhotoPeopleNotice`, диалог причины удаления
  SuperAdmin.
- `GET /api/goods/catalog`, `GoodsCatalogService` (правило магазина `CatalogListingRules.Evaluate` не меняется, кроме
  текста одной константы).

## §31.28. Примеры для prism/тестов

**Салон на тарифе без показа, показ включён ранее через старый путь:**
```json
{
  "showInCatalog": true, "allowedByPlan": false, "visible": false,
  "statusText": "Салона сейчас нет в каталоге",
  "notAllowedByPlanText": "Показ в каталоге не входит в ваш тариф — повысьте тариф, чтобы включить",
  "checklist": [
    { "code": "NotAllowedByPlan", "text": "Показ в каталоге не входит в ваш тариф", "done": false },
    { "code": "HiddenByOwner", "text": "Показ включен в настройках", "done": true }
  ]
}
```

**Заблокированный салон, показ выключен:**
```json
{
  "showInCatalog": false, "allowedByPlan": true, "visible": false,
  "statusText": "Салона сейчас нет в каталоге", "notAllowedByPlanText": null,
  "checklist": [
    { "code": "SalonBlocked", "text": "Салон заблокирован администратором", "done": false },
    { "code": "HiddenByOwner", "text": "Показ включен в настройках", "done": false }
  ]
}
```

## §31.29. Приёмка контракта

- Функциональные `CY31-` (перечень — `ARCHITECTURE_CYCLE31.md` §31.14.1) зелёные, в том числе сверка ответов с
  `contracts/cycle31/openapi.json` валидатором `OpenApiContract` (CY31-30…34).
- `npx @redocly/cli lint` по `contracts/cycle31/openapi.yaml` чистый. `npm run types:api:cycle31` и
  `npm run contracts:json` не дают diff в CI.
- `Cycle22RouteTable.golden.txt`: +2 строки (`GET`, `PUT api/companies/{id:guid}/catalog-listing`), у трёх строк
  галереи в поле `ratelimit` новое имя политики. Других изменений в эталоне нет.
- Найденное расхождение исправляется так: неверный код ответа — в коде. Неполная схема — в
  `contracts/cycle31/openapi.yaml`, затем `types:api:cycle31` и `contracts:json`.
