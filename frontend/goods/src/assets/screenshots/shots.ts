import manifest from './screenshots.json'
import orderPage1x from './order-page-1x.webp'
import orderPage2x from './order-page-2x.webp'
import boardDesktop1x from './board-desktop-1x.webp'
import boardDesktop2x from './board-desktop-2x.webp'
import boardPhone2x from './board-phone-2x.webp'

/** = Tailwind `md`; change together with tailwind.goods.config.js (ARCHITECTURE_CYCLE30.md §30.5). */
export const MD_MEDIA = '(min-width: 768px)'

export const BOARD_ALT =
  'Экран заказов магазина: колонки „Новые“, „Принятые“ и „Готовы к выдаче“ с карточками заказов — номер, время получения, покупатель, состав и сумма'

export const orderPageAlt = (m: { orderNumber: number; pickupClock: string }) =>
  `Страница заказа на телефоне: заказ № ${m.orderNumber} принят магазином, получение сегодня к ${m.pickupClock}, отмечены шаги „Заказ оформлен“ и „Магазин принял заказ“`

export const orderPageShot = {
  src: orderPage1x,
  srcSet: `${orderPage1x} 1x, ${orderPage2x} 2x`,
  width: manifest.orderPage.cssWidth,
  height: manifest.orderPage.cssHeight,
  alt: orderPageAlt(manifest.orderPage),
  caption: 'Так выглядит страница заказа: статус меняется сам',
}

export const boardShot = {
  src: boardPhone2x,
  width: manifest.boardPhone.cssWidth,
  height: manifest.boardPhone.cssHeight,
  sources: [
    {
      media: MD_MEDIA,
      srcSet: `${boardDesktop1x} 1x, ${boardDesktop2x} 2x`,
      width: manifest.boardDesktop.cssWidth,
      height: manifest.boardDesktop.cssHeight,
    },
  ],
  alt: BOARD_ALT,
  caption: 'Экран заказов магазина: новые, принятые и готовые заказы',
}
