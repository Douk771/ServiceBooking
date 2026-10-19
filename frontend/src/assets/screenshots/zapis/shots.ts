import manifest from './screenshots.json'
import bookingSlots1x from './booking-slots-1x.webp'
import bookingSlots2x from './booking-slots-2x.webp'
import bookingsDesktop1x from './bookings-desktop-1x.webp'
import bookingsDesktop2x from './bookings-desktop-2x.webp'
import bookingsPhone2x from './bookings-phone-2x.webp'
import type { LandingScreenshot } from '../../../components/landing/types'

/**
 * Кадры главной «Записи» (по образцу goods `shots.ts`). Снимаются с локального стенда с выдуманным салоном:
 * scripts/screenshots/seed-zapis-demo.mjs + capture-zapis-screenshots.mjs, инструкция — scripts/screenshots/README.md.
 */

/** = Tailwind `md`. */
export const MD_MEDIA = '(min-width: 768px)'

export const BOOKING_SLOTS_ALT = `Запись на услугу с телефона: «${manifest.bookingSlots.serviceName}», шаг «Выберите время» со свободным временем мастера на выбранный день`

export const BOOKINGS_ALT =
  'Раздел «Мои записи» у мастера: записи на завтра по времени — услуга, клиент, телефон, комментарий клиента и кнопки «Перенести», «Выполнено», «Не пришёл», «Отменить»'

export const bookingSlotsShot: LandingScreenshot = {
  src: bookingSlots1x,
  srcSet: `${bookingSlots1x} 1x, ${bookingSlots2x} 2x`,
  width: manifest.bookingSlots.cssWidth,
  height: manifest.bookingSlots.cssHeight,
  alt: BOOKING_SLOTS_ALT,
  caption: 'Клиент видит только свободное время мастера',
}

export const bookingsShot: LandingScreenshot = {
  src: bookingsPhone2x,
  width: manifest.bookingsPhone.cssWidth,
  height: manifest.bookingsPhone.cssHeight,
  sources: [
    {
      media: MD_MEDIA,
      srcSet: `${bookingsDesktop1x} 1x, ${bookingsDesktop2x} 2x`,
      width: manifest.bookingsDesktop.cssWidth,
      height: manifest.bookingsDesktop.cssHeight,
    },
  ],
  alt: BOOKINGS_ALT,
  caption: 'Записи мастера на день: клиент, услуга и время',
}
