# Изображения витрины — журнал происхождения

Цикл 28, задача CT-1 (`ARCHITECTURE_CYCLE28.md` §575.6, долг L28-4).

**Состояние: 231 файлов, 3134 КБ** (из них картинки магазинов «Заказов» — 1952 КБ). Плоские иллюстрации нарисованы программно, фотографий и людей нет.

## Правила набора

- Только интерьеры, инструменты и работы **без узнаваемых лиц** (юриста не привлекали, L28-4). У мастеров фото нет, фронт рисует инициалы.
- Бюджет: весь набор ≤ 8 МБ, файл ≤ 250 КБ, полноразмерный JPEG/WebP ≤ 1600 px и миниатюра рядом.
- Для каждого файла ниже указан **источник и лицензия**. Файл без записи в этом журнале в манифест не попадает.

## Ключи, которые ждёт генератор

- Логотипы: `logo.beauty`, `logo.barber`, `logo.nails`, `logo.massage`, `logo.cosmetology`, `logo.brows`, `logo.home`.
- Фото компаний: `photo.<категория>.1` … `photo.<категория>.6` (категории те же, что у логотипов; сеть «Мята» использует `beauty`).
- **Магазины «Заказов» (цикл 35, `ARCHITECTURE_CYCLE35.md` §35.11).** Логотипы: `logo.shop.coffee`, `logo.shop.bakery`, `logo.shop.canteen`, `logo.shop.flowers`, `logo.shop.farm`.
  Фото магазинов: `photo.shop.<категория>.1` … `photo.shop.<категория>.6` (категории те же; каталог берёт 3–6 на магазин). Картинки товаров: `product.<категория>.<имя>`
  (800×800) с миниатюрой 320×320 рядом в поле `thumbnail` (`products/<категория>/<имя>.thumb.jpg`); имена — в `ShowcaseShopSpecs.cs`, у ~60 % товаров картинка есть, одна
  картинка может стоять у нескольких похожих товаров. Набор дорисовывается командой `python3 tools/showcase-assets/generate.py --only shops`: файлы салонов при этом не
  перерисовываются и остаются в манифесте как есть. Прирост ≤ 3 МБ проверяет `ShowcaseShopAssetsTests`.
- Картинки услуг: `service.haircut`, `service.coloring`, `service.styling`, `service.manicure`, `service.pedicure`, `service.nails-design`, `service.hair-care`,
  `service.makeup`, `service.beard`, `service.shave`, `service.brows`, `service.lashes`, `service.massage`, `service.spa`, `service.wrap`, `service.facial`,
  `service.peeling`, `service.mesotherapy`, `service.consult`.

## Формат `manifest.json`

```json
{ "version": 1, "assets": [ { "key": "logo.beauty", "role": "logo", "file": "logos/beauty.jpg", "thumbnail": null, "width": 600, "height": 600 } ] }
```

`role` — `logo`, `photo`, `service` или `product` (только у товаров магазинов есть `thumbnail`); `file` и `thumbnail` — пути внутри `ShowcaseAssets/`.

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
| `logo.shop.coffee` | `logos/shop-coffee.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.coffee.1` | `photos/shop-coffee-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.coffee.2` | `photos/shop-coffee-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.coffee.3` | `photos/shop-coffee-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.coffee.4` | `photos/shop-coffee-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.coffee.5` | `photos/shop-coffee-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.coffee.6` | `photos/shop-coffee-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.espresso` | `products/coffee/espresso.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.espresso (миниатюра)` | `products/coffee/espresso.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.americano` | `products/coffee/americano.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.americano (миниатюра)` | `products/coffee/americano.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.cappuccino` | `products/coffee/cappuccino.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.cappuccino (миниатюра)` | `products/coffee/cappuccino.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.latte` | `products/coffee/latte.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.latte (миниатюра)` | `products/coffee/latte.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.flat-white` | `products/coffee/flat-white.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.flat-white (миниатюра)` | `products/coffee/flat-white.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.raf` | `products/coffee/raf.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.raf (миниатюра)` | `products/coffee/raf.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.tea-black` | `products/coffee/tea-black.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.tea-black (миниатюра)` | `products/coffee/tea-black.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.tea-green` | `products/coffee/tea-green.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.tea-green (миниатюра)` | `products/coffee/tea-green.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.tea-herbal` | `products/coffee/tea-herbal.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.tea-herbal (миниатюра)` | `products/coffee/tea-herbal.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.cocoa` | `products/coffee/cocoa.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.cocoa (миниатюра)` | `products/coffee/cocoa.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.croissant` | `products/coffee/croissant.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.croissant (миниатюра)` | `products/coffee/croissant.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.muffin` | `products/coffee/muffin.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.muffin (миниатюра)` | `products/coffee/muffin.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.cookie` | `products/coffee/cookie.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.cookie (миниатюра)` | `products/coffee/cookie.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.cheesecake` | `products/coffee/cheesecake.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.cheesecake (миниатюра)` | `products/coffee/cheesecake.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.sandwich` | `products/coffee/sandwich.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.sandwich (миниатюра)` | `products/coffee/sandwich.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.toast-avocado` | `products/coffee/toast-avocado.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.toast-avocado (миниатюра)` | `products/coffee/toast-avocado.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.granola` | `products/coffee/granola.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.coffee.granola (миниатюра)` | `products/coffee/granola.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `logo.shop.bakery` | `logos/shop-bakery.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.bakery.1` | `photos/shop-bakery-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.bakery.2` | `photos/shop-bakery-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.bakery.3` | `photos/shop-bakery-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.bakery.4` | `photos/shop-bakery-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.bakery.5` | `photos/shop-bakery-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.bakery.6` | `photos/shop-bakery-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.loaf` | `products/bakery/loaf.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.loaf (миниатюра)` | `products/bakery/loaf.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.baguette` | `products/bakery/baguette.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.baguette (миниатюра)` | `products/bakery/baguette.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.rye-bread` | `products/bakery/rye-bread.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.rye-bread (миниатюра)` | `products/bakery/rye-bread.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.bun` | `products/bakery/bun.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.bun (миниатюра)` | `products/bakery/bun.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.cinnamon-roll` | `products/bakery/cinnamon-roll.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.cinnamon-roll (миниатюра)` | `products/bakery/cinnamon-roll.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.pie-apple` | `products/bakery/pie-apple.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.pie-apple (миниатюра)` | `products/bakery/pie-apple.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.pie-cabbage` | `products/bakery/pie-cabbage.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.pie-cabbage (миниатюра)` | `products/bakery/pie-cabbage.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.pie-meat` | `products/bakery/pie-meat.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.pie-meat (миниатюра)` | `products/bakery/pie-meat.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.cake-honey` | `products/bakery/cake-honey.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.cake-honey (миниатюра)` | `products/bakery/cake-honey.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.cake-chocolate` | `products/bakery/cake-chocolate.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.cake-chocolate (миниатюра)` | `products/bakery/cake-chocolate.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.eclair` | `products/bakery/eclair.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.eclair (миниатюра)` | `products/bakery/eclair.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.cookies` | `products/bakery/cookies.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.bakery.cookies (миниатюра)` | `products/bakery/cookies.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `logo.shop.canteen` | `logos/shop-canteen.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.canteen.1` | `photos/shop-canteen-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.canteen.2` | `photos/shop-canteen-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.canteen.3` | `photos/shop-canteen-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.canteen.4` | `photos/shop-canteen-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.canteen.5` | `photos/shop-canteen-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.canteen.6` | `photos/shop-canteen-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.soup-borsch` | `products/canteen/soup-borsch.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.soup-borsch (миниатюра)` | `products/canteen/soup-borsch.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.soup-chicken` | `products/canteen/soup-chicken.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.soup-chicken (миниатюра)` | `products/canteen/soup-chicken.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.cutlet` | `products/canteen/cutlet.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.cutlet (миниатюра)` | `products/canteen/cutlet.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.goulash` | `products/canteen/goulash.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.goulash (миниатюра)` | `products/canteen/goulash.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.fish` | `products/canteen/fish.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.fish (миниатюра)` | `products/canteen/fish.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.buckwheat` | `products/canteen/buckwheat.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.buckwheat (миниатюра)` | `products/canteen/buckwheat.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.rice` | `products/canteen/rice.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.rice (миниатюра)` | `products/canteen/rice.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.mashed-potato` | `products/canteen/mashed-potato.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.mashed-potato (миниатюра)` | `products/canteen/mashed-potato.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.pasta` | `products/canteen/pasta.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.pasta (миниатюра)` | `products/canteen/pasta.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.salad-vinaigrette` | `products/canteen/salad-vinaigrette.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.salad-vinaigrette (миниатюра)` | `products/canteen/salad-vinaigrette.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.salad-fresh` | `products/canteen/salad-fresh.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.salad-fresh (миниатюра)` | `products/canteen/salad-fresh.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.compote` | `products/canteen/compote.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.compote (миниатюра)` | `products/canteen/compote.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.tea` | `products/canteen/tea.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.tea (миниатюра)` | `products/canteen/tea.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.pancakes` | `products/canteen/pancakes.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.canteen.pancakes (миниатюра)` | `products/canteen/pancakes.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `logo.shop.flowers` | `logos/shop-flowers.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.flowers.1` | `photos/shop-flowers-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.flowers.2` | `photos/shop-flowers-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.flowers.3` | `photos/shop-flowers-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.flowers.4` | `photos/shop-flowers-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.flowers.5` | `photos/shop-flowers-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.flowers.6` | `photos/shop-flowers-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.bouquet-roses` | `products/flowers/bouquet-roses.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.bouquet-roses (миниатюра)` | `products/flowers/bouquet-roses.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.bouquet-mixed` | `products/flowers/bouquet-mixed.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.bouquet-mixed (миниатюра)` | `products/flowers/bouquet-mixed.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.bouquet-tulips` | `products/flowers/bouquet-tulips.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.bouquet-tulips (миниатюра)` | `products/flowers/bouquet-tulips.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.bouquet-peonies` | `products/flowers/bouquet-peonies.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.bouquet-peonies (миниатюра)` | `products/flowers/bouquet-peonies.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.arrangement-box` | `products/flowers/arrangement-box.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.arrangement-box (миниатюра)` | `products/flowers/arrangement-box.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.orchid` | `products/flowers/orchid.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.orchid (миниатюра)` | `products/flowers/orchid.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.succulent` | `products/flowers/succulent.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.succulent (миниатюра)` | `products/flowers/succulent.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.plant-pot` | `products/flowers/plant-pot.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.plant-pot (миниатюра)` | `products/flowers/plant-pot.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.card` | `products/flowers/card.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.flowers.card (миниатюра)` | `products/flowers/card.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `logo.shop.farm` | `logos/shop-farm.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.farm.1` | `photos/shop-farm-1.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.farm.2` | `photos/shop-farm-2.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.farm.3` | `photos/shop-farm-3.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.farm.4` | `photos/shop-farm-4.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.farm.5` | `photos/shop-farm-5.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `photo.shop.farm.6` | `photos/shop-farm-6.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.tomatoes` | `products/farm/tomatoes.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.tomatoes (миниатюра)` | `products/farm/tomatoes.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.cucumbers` | `products/farm/cucumbers.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.cucumbers (миниатюра)` | `products/farm/cucumbers.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.potatoes` | `products/farm/potatoes.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.potatoes (миниатюра)` | `products/farm/potatoes.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.carrots` | `products/farm/carrots.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.carrots (миниатюра)` | `products/farm/carrots.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.apples` | `products/farm/apples.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.apples (миниатюра)` | `products/farm/apples.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.berries` | `products/farm/berries.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.berries (миниатюра)` | `products/farm/berries.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.milk` | `products/farm/milk.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.milk (миниатюра)` | `products/farm/milk.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.cheese` | `products/farm/cheese.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.cheese (миниатюра)` | `products/farm/cheese.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.meat-beef` | `products/farm/meat-beef.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.meat-beef (миниатюра)` | `products/farm/meat-beef.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.honey` | `products/farm/honey.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.honey (миниатюра)` | `products/farm/honey.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.eggs` | `products/farm/eggs.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.eggs (миниатюра)` | `products/farm/eggs.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.bread-farm` | `products/farm/bread-farm.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
| `product.farm.bread-farm (миниатюра)` | `products/farm/bread-farm.thumb.jpg` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |
