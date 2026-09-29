import type { PushUnavailableReason } from '@/utils/pushAvailability'

/** Who is reading the explanation: the buyer on the order page, or staff on `/cabinet/devices`. */
export type PushAudience = 'customer' | 'staff'

/**
 * Explanations for «notifications are not available on this device» on goods. The REASON is picked by the shared
 * `getPushUnavailableReason` (cycles 9 and 21, same order of causes); only the words are goods-specific: the ezbook
 * texts talk about «записи», «салон» and the «EZBOOK» app. API_CONTRACT_CYCLE24.md §479 adds the advice for the buyer:
 * choose messages in MAX/WhatsApp next time.
 */
export function goodsPushMessage(reason: PushUnavailableReason, audience: PushAudience): string {
  const advice = audience === 'customer' ? ' В следующий раз выберите при оформлении сообщения в MAX/WhatsApp.' : ''
  switch (reason) {
    case 'ios-safari-not-installed':
      return audience === 'customer'
        ? 'На айфоне уведомления из браузера приходят только приложению, добавленному на экран «Домой». Добавьте этот сайт на экран «Домой» и откройте заказ оттуда — или в следующий раз выберите при оформлении сообщения в MAX/WhatsApp.'
        : 'На айфоне уведомления приходят только приложению, добавленному на экран «Домой». Это займёт минуту:'
    case 'ios-version-too-old':
      return 'На этой версии iOS уведомления от сайтов не работают даже с экрана «Домой». Нужна iOS 16.4 или новее — обновить: Настройки → Основные → Обновление ПО.' + advice
    case 'unsupported-browser':
      return 'Ваш браузер не умеет присылать уведомления.' + advice
    case 'insecure-context':
      return 'Уведомления работают только по защищённому соединению (HTTPS). На этом адресе они недоступны.' + advice
    case 'permission-denied':
      return 'Вы запретили уведомления в браузере. Чтобы вернуть: значок замка в адресной строке → Уведомления → Разрешить.' + advice
    case 'ios-permission-denied':
      return 'Вы запретили уведомления для этого приложения. Чтобы вернуть: Настройки айфона → Уведомления → «Заказы» → Допуск уведомлений.' + advice
    case 'platform-disabled':
      return audience === 'customer' ? 'Уведомления в браузере пока не включены на платформе.' : 'Уведомления на устройство пока не включены на платформе'
    case 'company-disabled':
      return 'Уведомления сотрудникам отключены владельцем магазина.'
  }
}

/** localStorage key remembering WHICH endpoint this browser registered for an order (there is no «am I subscribed» route for buyers). */
export function orderPushStorageKey(token: string): string {
  return `goods-order-push:${token}`
}
