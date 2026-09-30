# Изображения витрины — журнал происхождения

Цикл 28, задача CT-1 (`ARCHITECTURE_CYCLE28.md` §575.6, долг L28-4).

**Состояние: файлов пока нет.** Манифест `manifest.json` пуст, витрина создаётся без картинок: генератор не рисует заглушек и не создаёт строк на
несуществующие файлы. Пункт «3–6 фото у каждой компании» приёмки US-28-03 не выполнен, пока задача CT-1 не заполнит этот каталог.

## Правила набора

- Только интерьеры, инструменты и работы **без узнаваемых лиц** (юриста не привлекали, L28-4). У мастеров фото нет, фронт рисует инициалы.
- Бюджет: весь набор ≤ 8 МБ, файл ≤ 250 КБ, полноразмерный JPEG/WebP ≤ 1600 px и миниатюра рядом.
- Для каждого файла ниже указан **источник и лицензия**. Файл без записи в этом журнале в манифест не попадает.

## Ключи, которые ждёт генератор

- Логотипы: `logo.beauty`, `logo.barber`, `logo.nails`, `logo.massage`, `logo.cosmetology`, `logo.brows`, `logo.home`.
- Фото компаний: `photo.<категория>.1` … `photo.<категория>.6` (категории те же, что у логотипов; сеть «Мята» использует `beauty`).
- Картинки услуг: `service.haircut`, `service.coloring`, `service.styling`, `service.manicure`, `service.pedicure`, `service.nails-design`, `service.hair-care`,
  `service.makeup`, `service.beard`, `service.shave`, `service.brows`, `service.lashes`, `service.massage`, `service.spa`, `service.wrap`, `service.facial`,
  `service.peeling`, `service.mesotherapy`, `service.consult`.

## Формат `manifest.json`

```json
{ "version": 1, "assets": [ { "key": "logo.beauty", "role": "logo", "file": "logos/beauty.jpg", "thumbnail": null, "width": 600, "height": 600 } ] }
```

`role` — `logo`, `photo` или `service`; `file` и `thumbnail` — пути внутри `ShowcaseAssets/`.

## Журнал файлов

| Ключ | Файл | Источник (ссылка) | Лицензия | Дата проверки |
|---|---|---|---|---|
| — | — | — | — | — |
