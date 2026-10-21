import manifest from './screenshots.json'
import bookingServices2x from './booking-services-2x.webp'
import bookingMaster2x from './booking-master-2x.webp'
import bookingDate2x from './booking-date-2x.webp'
import bookingSlots2x from './booking-slots-2x.webp'
import bookingContacts2x from './booking-contacts-2x.webp'
import bookingDone2x from './booking-done-2x.webp'
import bookingsDesktop1x from './bookings-desktop-1x.webp'
import bookingsDesktop2x from './bookings-desktop-2x.webp'
import bookingsPhone2x from './bookings-phone-2x.webp'
import type { LandingScreenshot, LandingScreenshotCarousel } from '../../../components/landing/types'

/**
 * Кадры главной «Записи» (по образцу goods `shots.ts`). Снимаются с локального стенда с выдуманным салоном:
 * scripts/screenshots/seed-zapis-demo.mjs + capture-zapis-screenshots.mjs, инструкция — scripts/screenshots/README.md.
 */

/** = Tailwind `md`. */
export const MD_MEDIA = '(min-width: 768px)'

export const BOOKINGS_ALT =
  'Раздел «Мои записи» у мастера: записи на завтра по времени — услуга, клиент, телефон, комментарий клиента и кнопки «Перенести», «Выполнено», «Не пришёл», «Отменить»'

const { serviceName, masterName } = manifest.bookingFlow
const FLOW_FILES: Record<string, string> = {
  services: bookingServices2x,
  master: bookingMaster2x,
  date: bookingDate2x,
  slots: bookingSlots2x,
  contacts: bookingContacts2x,
  done: bookingDone2x,
}
/** Подписи и alt шагов записи, по ключам манифеста; порядок — как в манифесте. */
const FLOW_TEXT: Record<string, { caption: string; alt: string }> = {
  services: {
    caption: '1. Выберите услугу — можно добавить ещё',
    alt: `Окно записи на телефоне: выбрана услуга «${serviceName}», ниже список услуг, которые можно добавить к визиту`,
  },
  master: { caption: '2. Выберите мастера', alt: 'Окно записи: шаг «Выберите мастера», два мастера салона' },
  date: { caption: '3. Выберите день', alt: 'Окно записи: календарь месяца, рабочие дни мастера отмечены' },
  slots: { caption: '4. Выберите свободное время', alt: 'Окно записи: шаг «Выберите время», свободное время мастера на выбранный день' },
  contacts: {
    caption: '5. Оставьте имя и телефон',
    alt: `Окно записи: сводка — «${serviceName}», мастер ${masterName}, дата и время — и поля «Ваше имя» и «Телефон»`,
  },
  done: { caption: '6. Запись подтверждена', alt: 'Окно записи: «Запись подтверждена!» с датой и временем визита' },
}

export const bookingFlowCarousel: LandingScreenshotCarousel = {
  label: 'Шаг за шагом',
  slides: manifest.bookingFlow.slides.map((s) => ({
    src: FLOW_FILES[s.key],
    width: s.cssWidth,
    height: s.cssHeight,
    ...FLOW_TEXT[s.key],
  })) as unknown as LandingScreenshotCarousel['slides'],
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
