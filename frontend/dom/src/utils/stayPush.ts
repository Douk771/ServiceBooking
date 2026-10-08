import type { PushUnavailableReason } from '@/utils/pushAvailability'

/**
 * Explanations for «notifications are not available on this device» for the GUEST of a booking on dom. The REASON is picked by
 * the shared `getPushUnavailableReason`; only the words are dom's. The advice mirrors goods: the booking page always shows
 * everything, notifications are a convenience (R37-5).
 */
export function guestPushMessage(reason: PushUnavailableReason): string {
  const advice = ' Сведения о брони всегда доступны на этой странице — сохраните ссылку.'
  switch (reason) {
    case 'ios-safari-not-installed':
      return 'На айфоне уведомления из браузера приходят только приложению, добавленному на экран «Домой». Добавьте этот сайт на экран «Домой» и откройте бронь оттуда.' + advice
    case 'ios-version-too-old':
      return 'На этой версии iOS уведомления от сайтов не работают даже с экрана «Домой». Нужна iOS 16.4 или новее — обновить: Настройки → Основные → Обновление ПО.' + advice
    case 'unsupported-browser':
      return 'Ваш браузер не умеет присылать уведомления.' + advice
    case 'insecure-context':
      return 'Уведомления работают только по защищённому соединению (HTTPS). На этом адресе они недоступны.' + advice
    case 'permission-denied':
      return 'Вы запретили уведомления в браузере. Чтобы вернуть: значок замка в адресной строке → Уведомления → Разрешить.' + advice
    case 'ios-permission-denied':
      return 'Вы запретили уведомления для этого приложения. Чтобы вернуть: Настройки айфона → Уведомления → «Дома» → Допуск уведомлений.' + advice
    case 'platform-disabled':
      return 'Уведомления в браузере пока не включены на платформе.'
    case 'company-disabled':
      return 'Компания отключила уведомления о бронях.'
  }
}

const KEY_PREFIX = 'stays-booking-push:'
const AT_PREFIX = 'stays-booking-push-at:'
/** The server drops a guest subscription some days after the booking ends; the local memory expires with a margin. */
export const BOOKING_PUSH_TTL_MS = 14 * 24 * 60 * 60 * 1000

/** localStorage key remembering WHICH endpoint this browser registered for a booking (there is no «am I subscribed» route). */
export const bookingPushKey = (token: string) => `${KEY_PREFIX}${token}`
export const bookingPushAtKey = (token: string) => `${AT_PREFIX}${token}`

/** Removes remembered endpoints (and their timestamps) older than the TTL; entries without a timestamp are removed too. */
export function pruneBookingPushStorage(storage: Pick<Storage, 'length' | 'key' | 'getItem' | 'removeItem'>, now: number): void {
  const keys: string[] = []
  for (let i = 0; i < storage.length; i++) {
    const k = storage.key(i)
    if (k) keys.push(k)
  }
  for (const k of keys) {
    if (!k.startsWith(KEY_PREFIX)) continue
    const token = k.slice(KEY_PREFIX.length)
    const at = Number(storage.getItem(bookingPushAtKey(token)))
    if (!Number.isFinite(at) || at <= 0 || now - at > BOOKING_PUSH_TTL_MS) {
      storage.removeItem(k)
      storage.removeItem(bookingPushAtKey(token))
    }
  }
}
