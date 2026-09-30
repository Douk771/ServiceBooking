# ARCHITECTURE — цикл 31: плашка обновления goods

Спека: `SPEC_CYCLE31_GOODS_UPDATE_BANNER.md`. Изменений API, БД, контрактов, миграций нет; `API_CONTRACT` не создаётся.

## T-31-01 frontend — `frontend/goods/src/hooks/useAppUpdate.ts`
Хук `useAppUpdate(): boolean`. Текущий бандл — `script[type=module][src*="/assets/index-"]`, путь по `/assets/index-[\w-]+\.js`.
Нет такого скрипта (dev) — проверка отключена. Проверка: `fetch('/index.html', {cache:'no-store'})`, первое совпадение регэкспа в
теле; отличается от текущего — `true` (дальше не опрашивает). Триггеры: `visibilitychange` (visible) и `setInterval` 5 минут.
Ошибки сети и не-2xx глотаются.

## T-31-02 frontend — `components/UpdateBanner.tsx`, подключение в `GoodsApp.tsx`
`role="status"`, fixed снизу, `safe-area-inset-bottom`, кнопка `Button` «Обновить» → `window.location.reload()`. Рендерится в
`GoodsApp` после `OwnerTermsGateModal`. Тесты: `UpdateBanner.test.tsx` (CY31-01 нет плашки при той же сборке, CY31-02 плашка
при новой, CY31-03 ошибка сети).

## T-31-03 devops — `deploy/nginx/goods.ezbook.conf`
`location /assets/` — `Cache-Control: public, max-age=31536000, immutable` плюс security-заголовки (add_header не наследуется);
`location /` — `Cache-Control: no-cache`. DEPLOY.md: упомянуть ручную установку конфига и повтор certbot.

## Проверки
`tsc -p tsconfig.goods.json`, `eslint goods/src`, `vitest run`. Ручной: на iPhone из иконки после деплоя двух разных сборок.
