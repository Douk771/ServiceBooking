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

export * from '@/utils/slots/slotPush'
