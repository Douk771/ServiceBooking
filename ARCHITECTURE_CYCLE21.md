# ARCHITECTURE — цикл 21 ServiceBooking: уведомления мастеру на iPhone через «На экран Домой»

**Разделы §360–§366.** Вход: `SPEC.md` (цикл 21, решения Р1–Р4, истории US-21-01…US-21-03),
`ARCHITECTURE_CYCLE9.md` §101, §103.2, §105.9, §105.10. Отправная точка — `develop` = `e3774c1`.

**API-контракта у цикла нет:** бэкенд, эндпоинты, модель данных и миграции не меняются (Р4), поэтому
`API_CONTRACT_CYCLE21.md` и `contracts/cycle19/` не заводятся.

---

## §360. Главное решение в пяти строках

1. Манифест переходит с `display: "browser"` на `display: "standalone"` — только так iOS открывает
   иконку с экрана Домой как веб-приложение, а Web Push на iOS 16.4+ есть **только** у него.
2. Service worker не меняется ни на строку: запрет на перехват запросов и кеш (§105.9, R11) остаётся.
3. Проверки iOS встают **первыми** в `getPushUnavailableReason` — до «браузер не умеет».
4. Для айфона вне установленного приложения карточка показывает пошаговую инструкцию.
5. Смоук фронта запросом проверяет, что отдаваемый манифест — `standalone`.

Это **отменяет** две строки цикла 9: «полноценный PWA-манифест с `display: standalone`» из списка
«чего в стеке намеренно не появляется» (§101) и предупреждение «`display: "browser"` — осознанно»
(§103.2). Обоснование отмены: §103.2 опирался на «SPEC §3 выносит PWA за рамки», а SPEC цикла 21 (Р1)
вносит установку в рамки. Офлайн-часть PWA по-прежнему за рамками (Р2).

## §361. Манифест и `index.html`

`frontend/public/manifest.webmanifest`:

| Поле | Значение | Зачем |
|---|---|---|
| `display` | `standalone` | несущее: без него «На экран Домой» на iOS — закладка Safari без push |
| `id` | `/` | стабильная идентичность приложения, не зависит от `start_url` |
| `start_url`, `scope` | `/`, `/` | приложение охватывает весь сайт, в т. ч. `/my-bookings` и `/login` |
| `lang` | `ru` | — |
| `name`, `short_name`, `icons`, `theme_color`, `background_color` | без изменений (цикл 9) | — |

`frontend/index.html` — добавлены `apple-mobile-web-app-capable`, `mobile-web-app-capable`,
`apple-mobile-web-app-title=EZBOOK`, `apple-mobile-web-app-status-bar-style=default`.

- `status-bar-style` именно `default`: `black-translucent` подложил бы шапку под часы и вырез
  экрана, а `viewport-fit=cover` и отступы safe-area в вёрстке не заведены.
- **`<meta name="theme-color">` не добавляется**: Safari 15+ красит им панель вкладки, то есть
  изменился бы вид сайта для всех посетителей в браузере (НФТ SPEC §3). Для установленного
  приложения цвет берётся из `theme_color` манифеста.
- `Content-Type` манифеста в nginx не трогаем: браузеры разбирают манифест по ссылке
  `rel="manifest"` независимо от типа, а отдельный `location` потребовал бы повторить все
  заголовки безопасности (см. комментарий к `location = /sw.js` в `deploy/nginx/ezbook.conf`).

## §362. Порядок причин недоступности (правка §105.10)

`frontend/src/utils/pushAvailability.ts`. Вход `isIosSafariNotInstalled: boolean` заменён на
`ios: { isIos, isStandalone, version }` (`detectIosEnvironment`):

| # | Условие | Причина | Новое? |
|---|---|---|---|
| 1 | iOS и **не** `standalone` | `ios-safari-not-installed` + шаги (§363) | поднято с 4-го места |
| 2 | iOS и версия < 16.4 (известна) | `ios-version-too-old` | 🆕 |
| 3 | нет `serviceWorker`/`PushManager` | `unsupported-browser` | — |
| 4 | не HTTPS | `insecure-context` | — |
| 5 | `permission === 'denied'` | iOS → `ios-permission-denied` (Настройки айфона), иначе `permission-denied` (замок в адресной строке) | 🆕 развилка |
| 6 | `enabled: false` платформы | `platform-disabled` | — |
| 7 | `staffPushEnabled` компании выключен | `company-disabled` | — |

Почему iOS первым: во вкладке Safari `PushManager` отсутствует, поэтому при порядке цикла 9 шаг 3
срабатывал всегда и строка про экран Домой была недостижима (SPEC §0 п. 2).

`standalone` определяется как `navigator.standalone === true` **или**
`matchMedia('(display-mode: standalone)').matches` — первый флаг собственный у Apple, второй
стандартный; хватает любого. iOS — по UA (`iPhone|iPad|iPod`, либо `Macintosh` + сенсорный экран
для iPadOS). Версия — из токена `OS 17_4`, для iPadOS с «настольным» UA — из `Version/18.1`.
Неизвестная версия не блокирует (SPEC R19-3). Все браузеры iOS считаются одинаково: это WebKit с
тем же ограничением, и начиная с 16.4 каждый умеет «На экран Домой» через своё меню «Поделиться».

Старая функция `detectIosSafariNotInstalled` оставлена обёрткой над `detectIosEnvironment`.

## §363. Инструкция (US-21-01)

`components/push/PushUnavailableNotice.tsx` для причины `ios-safari-not-installed` дописывает к
тексту `<ol>` из четырёх шагов: «Поделиться» (встроенный SVG-значок — в `ui/Icon` такого нет;
подсказка про «⋯» для iOS 26, где кнопка спрятана) → «На экран «Домой»» → «Добавить» → открыть с
иконки и **войти заново** → включить тумблер в «Мои записи». Компонент по-прежнему ничего не решает,
только печатает (§105.10). Место показа прежнее — `MyDevicesCard` на `/my-bookings`.

## §364. Что не меняется — и почему это безопасно

- **`public/sw.js`** — ни строки. CI-грэп на `fetch`/`caches` (`.github/workflows/ci.yml`) остаётся
  стражем R11. `standalone` не требует обработчика `fetch` ни на iOS, ни в текущем Chrome.
- **`useWebPush.enableOnThisDevice`** — прежний путь: `Notification.requestPermission()` первым
  вызовом в обработчике нажатия (на iOS это обязательно — запрос вне жеста отклоняется), затем
  `register('/sw.js')`, `subscribe`, `POST /api/push/subscriptions`. Endpoint Apple
  (`web.push.apple.com`) бэкенд отправляет той же библиотекой VAPID — отдельной ветки не нужно.
  ⚠️ **Условие выката:** Apple строже других к полю `sub` VAPID — принимает только настоящий
  `mailto:` или `https:` (заглушки вроде `mailto:admin@localhost` получают `403 BadJwtToken`).
  `DeploymentSafetyChecks` проверяет лишь непустоту `WEBPUSH_VAPID_SUBJECT`; значение из
  `.env.production.example` (`mailto:ops@ezbook.ru`) годится — на сервере сверить, что стоит оно или
  другой реальный адрес.
- **Бэкенд, контракт, БД, сроки хранения** — без изменений (Р4).
- **Вход.** Токен в `localStorage` (`authStore`, `persist`); у установленного приложения на iOS
  своё хранилище — отсюда шаг «войти заново». Выход в приложении вызывает прежний
  `unsubscribeCurrentDeviceOnLogout` (рубеж 2, §105.5) и отписывает именно это устройство.

## §365. Проверки

| Что | Где | ID |
|---|---|---|
| порядок причин, iOS-развилки, граница 16.4, неизвестная версия | `src/utils/pushAvailability.test.ts` | CY21-01…CY21-07 |
| `detectIosEnvironment`: iPhone, `display-mode`, Chrome на iOS, iPadOS, Mac | там же | CY21-08…CY21-12 |
| шаги инструкции, «войти заново», тексты других причин | `src/components/push/PushUnavailableNotice.test.tsx` | CY21-13…CY21-15 |
| манифест отдаётся и `standalone`; `index.html` ссылается на манифест и `apple-touch-icon` | `deploy/ci/smoke-frontend.sh` (джоб `frontend`) | US-21-03 |

**Проверка на устройстве (не автоматизируется, делается при выкате):** iPhone с iOS ≥ 16.4 →
`https://ezbook.ru/my-bookings` в Safari → видны шаги → «На экран Домой» → открыть с иконки
(нет адресной строки) → войти → тумблер → разрешить → устройство «Safari на iOS» в списке →
тестовая запись → уведомление на заблокированном экране → нажатие открывает «Мои записи».

## §366. Риски

- **Уже созданные ярлыки.** Иконка, добавленная до выката, была создана с `display: browser` и
  останется закладкой. Мастеру нужно удалить её и добавить заново — сказано в CHANGELOG и
  `docs/master.md`.
- **Android.** Chrome может предложить «Установить приложение» — безвредно, push на Android работал
  и во вкладке и работает в установленном приложении так же.
- **Без кеша установленное приложение без сети покажет ошибку браузера** — ожидаемо (Р2), как и
  вкладка.
