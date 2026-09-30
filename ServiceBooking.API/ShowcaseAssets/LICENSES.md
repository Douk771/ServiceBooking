# Изображения витрины — журнал происхождения

Цикл 28, задача CT-1 (`ARCHITECTURE_CYCLE28.md` §575.6, долг L28-4).

**Состояние: 68 файлов, 1181 КБ.** Плоские иллюстрации нарисованы программно, фотографий и людей нет.

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

| Ключ | Файл | Источник | Лицензия | Дата проверки |
|---|---|---|---|---|
| `logo.beauty` | `logos/beauty.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.beauty.1` | `photos/beauty-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.beauty.2` | `photos/beauty-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.beauty.3` | `photos/beauty-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.beauty.4` | `photos/beauty-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.beauty.5` | `photos/beauty-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.beauty.6` | `photos/beauty-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `logo.barber` | `logos/barber.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.barber.1` | `photos/barber-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.barber.2` | `photos/barber-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.barber.3` | `photos/barber-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.barber.4` | `photos/barber-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.barber.5` | `photos/barber-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.barber.6` | `photos/barber-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `logo.nails` | `logos/nails.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.nails.1` | `photos/nails-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.nails.2` | `photos/nails-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.nails.3` | `photos/nails-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.nails.4` | `photos/nails-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.nails.5` | `photos/nails-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.nails.6` | `photos/nails-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `logo.massage` | `logos/massage.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.massage.1` | `photos/massage-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.massage.2` | `photos/massage-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.massage.3` | `photos/massage-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.massage.4` | `photos/massage-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.massage.5` | `photos/massage-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.massage.6` | `photos/massage-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `logo.cosmetology` | `logos/cosmetology.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.cosmetology.1` | `photos/cosmetology-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.cosmetology.2` | `photos/cosmetology-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.cosmetology.3` | `photos/cosmetology-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.cosmetology.4` | `photos/cosmetology-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.cosmetology.5` | `photos/cosmetology-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.cosmetology.6` | `photos/cosmetology-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `logo.brows` | `logos/brows.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.brows.1` | `photos/brows-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.brows.2` | `photos/brows-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.brows.3` | `photos/brows-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.brows.4` | `photos/brows-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.brows.5` | `photos/brows-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.brows.6` | `photos/brows-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `logo.home` | `logos/home.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.home.1` | `photos/home-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.home.2` | `photos/home-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.home.3` | `photos/home-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.home.4` | `photos/home-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.home.5` | `photos/home-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.home.6` | `photos/home-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.haircut` | `services/haircut.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.coloring` | `services/coloring.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.styling` | `services/styling.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.hair-care` | `services/hair-care.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.makeup` | `services/makeup.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.manicure` | `services/manicure.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.pedicure` | `services/pedicure.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.nails-design` | `services/nails-design.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.beard` | `services/beard.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.shave` | `services/shave.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.brows` | `services/brows.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.lashes` | `services/lashes.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.massage` | `services/massage.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.spa` | `services/spa.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.wrap` | `services/wrap.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.facial` | `services/facial.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.peeling` | `services/peeling.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.mesotherapy` | `services/mesotherapy.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `service.consult` | `services/consult.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
