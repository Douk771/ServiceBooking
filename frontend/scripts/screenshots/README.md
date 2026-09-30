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
- Установленный Google Chrome (или путь в `SHOTS_CHROME_PATH`). Без Chrome: `npx playwright-core install chromium` вручную, не в CI.
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
