# Пересъёмка скриншотов главной goods

Кадры на главной goods (блоки «Для покупателей» и «Для бизнеса») снимаются с локального стенда с выдуманными данными.
Архитектура: `ARCHITECTURE_CYCLE30.md` §30.7–§30.9. Цель — уложиться в 30 минут.

## 1. Когда переснимать

После правок «снятых» файлов:

- `goods/src/pages/OrderPage.tsx`, `goods/src/components/OrderTimeline.tsx`, `goods/src/components/OrderStatusBadge.tsx`,
  `goods/src/components/GoodsNavbar.tsx`;
- `goods/src/pages/cabinet/OrdersScreenPage.tsx`, `goods/src/pages/cabinet/ShopLayout.tsx`, `goods/src/components/orders/OrderCard.tsx`;
- палитры в `tailwind.config.js`.

`sourceCommit` в `goods/src/assets/screenshots/screenshots.json` показывает, с какого кода сняты кадры.

## 2. Что нужно

- Docker (на macOS с colima `stack.sh` сам выставит `DOCKER_HOST`, если сокет `~/.colima/default/docker.sock` есть).
- Node >= 20, `npm ci` в `frontend/`.
- Установленный Google Chrome (или путь в `SHOTS_CHROME_PATH`). Без Chrome: `npx playwright-core install chromium` вручную (не в CI) и укажите путь к скачанному Chromium в `SHOTS_CHROME_PATH`: сам по себе он не подхватывается. Снимать при чистом дереве (`git status`), иначе `sourceCommit` получит суффикс `-dirty`.
- Интернет: шрифты грузятся с Google Fonts.
- Снимать днём по времени магазина (в окне 07:00-20:30) и не позже 55 минут после засева: сроки заказов на +60 минут.

## 3. Шаги

Из каталога `frontend/`:

```
bash scripts/screenshots/stack.sh reset      # чистая база sb-shots, API на :55000
npm run shots:seed                           # демо-данные, .state/seed.json
npm run shots:capture                        # 5 WebP + screenshots.json
```

Затем:

1. Открыть 5 файлов из `goods/src/assets/screenshots/` и просмотреть глазами. В кадре не должно быть: «Просрочен»,
   «Нет связи», «Звук выключен», адреса стенда (`localhost`), блока «Ссылка на этот заказ», карточки push, сообщения про
   MAX/WhatsApp, реальных имён и телефонов (только `+7 (900) 000-00-xx`).
2. `npm run test:run -- goods/src/assets goods/src/components goods/src/pages/CatalogHomePage.test.tsx`.
3. Проверить манифест: `npx --yes ajv-cli@5 validate -s ../contracts/cycle30/screenshots-manifest.schema.json -d goods/src/assets/screenshots/screenshots.json`.
4. Закоммитить 5 WebP и `screenshots.json`.
5. `bash scripts/screenshots/stack.sh down`.

Съёмка сама поднимает Vite goods на порту 55174 (или берёт `SHOTS_WEB_URL`). Пока кадров в репозитории ещё нет, импорты
`.webp` и `screenshots.json` подменяются заглушкой только на время съёмки, в файлы ничего не пишется.

## 4. Коды выхода

| Код | `shots:seed` | `shots:capture` |
|---|---|---|
| 0 | засеяно | 5 файлов и манифест записаны |
| 1 | непредвиденная ошибка (стек, сеть) | непредвиденная ошибка (Chrome, Vite): проверьте Chrome и порт 55174 |
| 2 | база уже засеяна: `stack.sh reset` | нет `.state/seed.json` или данные устарели: `reset` и `shots:seed` заново |
| 3 | адрес не локальный или не Development: отказ до записи | не используется |
| 4 | время магазина вне 07:00-20:30: снимать днём или сменить `SHOTS_CITY` | в кадре запретная строка или модальное окно: исправьте причину, файлы не записываются |
| 5 | сервер ответил не так, самопроверка не прошла | бюджет веса (120/200/120 КБ) или размер в пикселях не сошлись: уменьшить высоту обрезки, бюджет не поднимать |

Проверка защиты съёмки: `SHOTS_FORBID_EXTRA='Багет' npm run shots:capture` даёт код 4 и ничего не пишет.

## 5. Ручной запасной путь (без Playwright)

Chrome DevTools -> Device Toolbar 390 и 1280, DPR 2 -> «Capture node screenshot» -> конвертация в WebP (Squoosh или
`cwebp -q 80`) -> ручная правка `screenshots.json` по схеме `contracts/cycle30/screenshots-manifest.schema.json` ->
`npx --yes ajv-cli@5 validate ...` (команда из шага 3).

## 6. Никогда

- Не указывать `SHOTS_API_URL` на `ezbook.ru` и `goods.ezbook.ru`: засев откажет с кодом 3, но не пытайтесь обойти замок.
- Не коммитить `.state/`: там пароль владельца и токен заказа локальной базы.

## Замер времени прогона (FE-30-4)

Прогон 30.09.2026 на colima, при одновременной нагрузке от других сборок на той же машине:

- `stack.sh reset` + `up`: около 20 минут (первая сборка образа API упала на экспорте слоя containerd и пришлось
  повторить; на свободной машине сборка занимает заметно меньше);
- `shots:seed`: секунды;
- `shots:capture`: 42 секунды, вес 2x: заказ 20 КБ, доска (десктоп) 102 КБ, доска (телефон) 47 КБ;
- просмотр кадров, тесты и коммит: около 10 минут.

Ориентир на свободной машине с прогретым кешем образов: до 15 минут.

---

# Пересъёмка скриншотов главной «Записи» (ezbook.ru)

Тот же подход, что у goods: локальный стенд `sb-shots`, выдуманный салон засевается через обычный HTTP API, съёмка — Playwright
с установленным Chrome. Кадры лежат в `src/assets/screenshots/zapis/`, подключены в `src/pages/home/zapisLanding.ts` через `shots.ts`.

Данные (`zapis-demo-data.mjs`) — всё выдуманное: «Студия красоты «Лаванда»», ул. Липовая, 7; мастера Алина Воронцова (владелица)
и Марина Ковалёва; клиенты и телефоны только `+7 (900) 000-01-xx`. Логотип и картинки услуг — нарисованные иллюстрации из
`ServiceBooking.API/ShowcaseAssets` (без фото и людей). Мастеров двое: столько мест у тарифа «Бесплатный», тариф засев не меняет.

## Кадры

| Файл | Где на главной | Что |
|---|---|---|
| `booking-{services,master,date,slots,contacts,done}-2x.webp` | «Для клиентов», карусель | весь путь записи гостя на «Женскую стрижку»: услуга → мастер → день → время (первые 4 ряда) → имя и телефон → «Запись подтверждена» |
| `bookings-desktop-1x/2x.webp` | «Для бизнеса», от `md` | «Мои записи» владелицы на 1024 px, две записи на завтра |
| `bookings-phone-2x.webp` | «Для бизнеса», телефон | то же на 390 px |

Карусель (`components/landing/LandingScreenshotCarousel.tsx`) листается сама каждые 4,5 с, останавливается при наведении,
фокусе и по кнопке паузы; при «уменьшить движение» в системе не листается. Последний шаг съёмки создаёт настоящую запись гостя
(«Анна Петрова», `+7 (900) 000-01-30`) на самое позднее свободное время дня кадра: повторный запуск возьмёт следующий
поздний слот, а первые 4 ряда в кадре не меняются. Анонимных записей — не больше 10 в час с одного адреса (лимит API).

Переснимать после правок `components/booking/BookingModal.tsx`, `pages/MyBookingsPage.tsx`, `components/layout/Navbar.tsx`, палитры `tailwind.config.js`.

## Шаги

Из каталога `frontend/`:

```
bash scripts/screenshots/stack.sh reset      # чистая база sb-shots, API на :55000
npm run shots:zapis:seed                     # салон, услуги, мастера, график, 7 записей на завтра; .state/zapis-seed.json
npm run shots:zapis:capture                  # 9 WebP + screenshots.json в src/assets/screenshots/zapis/
npm run test:run -- src/assets/screenshots/zapis src/components/landing src/pages/HomePage.test.tsx
```

Окна съёмки по часам нет: записи ставятся на завтра по времени салона (Москва). Снимать нужно в тот же день, что засев:
после полуночи по Москве «Завтра» в кадре не получится, `capture` откажет с кодом 2 — пересейте.

Повторный засев салона на той же базе — код 2, нужен `stack.sh reset`.

Если Vite уже запущен на :55173 (например, превью), передайте `SHOTS_WEB_URL=http://localhost:55173`.

Что меняется в кадрах и почему. На шаге времени скрыты слоты после четвёртого ряда — чтобы кадр не вытягивался.
Из кадра «Мои записи» убирается метка доставки напоминания. На стенде у салона нет каналов уведомлений, и сервер
пишет «напоминание пропущено» — у салона с подключённым каналом там «ожидает отправки». Скрипт убирает ровно эти метки (и падает
с кодом 5, если их число не совпало с числом записей), всё остальное снимается как есть. Окно записи снимается на ровном
кремовом фоне вместо размытой страницы под ним.

## Если образ API не собирается (нет сети внутри colima)

`stack.sh reset` собирает образ и качает пакеты NuGet внутри VM. Если это падает по таймауту, поднимите только Postgres и
запустите API на хосте (пакеты берутся из локального кеша):

```
DOCKER_HOST=unix://$HOME/.colima/default/docker.sock SB_DB_PORT=55432 docker compose -p sb-shots -f ../docker-compose.yml up -d postgres
cd .. && ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__DefaultConnection="Host=localhost;Port=55432;Database=servicebooking;Username=postgres;Password=postgres" Jwt__Key=CHANGE_ME_TO_A_LONG_SECRET_KEY_AT_LEAST_32_CHARS Jwt__Issuer=ServiceBooking Jwt__Audience=ServiceBookingClient dotnet run --project ServiceBooking.API --no-launch-profile --urls http://localhost:55000
```

Чистая база в этом режиме: `DROP DATABASE servicebooking; CREATE DATABASE servicebooking;` в контейнере `sb-shots-postgres-1`
при остановленном API, затем снова `dotnet run` (миграции применятся на старте).
