import type { PushUnavailableReason } from '@/utils/pushAvailability'

/** The only reader of these texts is the buyer on the order page; staff texts live in `@/utils/staffPushTexts` (cycle 33). */
export type PushAudience = 'customer'

/**
 * Explanations for «notifications are not available on this device» for the BUYER on goods. The REASON is picked by the
 * shared `getPushUnavailableReason`; only the words are goods-specific. API_CONTRACT_CYCLE24.md §479 adds the advice:
 * choose messages in MAX/WhatsApp next time. The `'staff'` branch was removed in cycle 33 (ARCHITECTURE_CYCLE33.md §33.8).
 */
export function goodsPushMessage(reason: PushUnavailableReason, _audience: PushAudience = 'customer'): string {
  const advice = ' В следующий раз выберите при оформлении сообщения в MAX/WhatsApp.'
  switch (reason) {
    case 'ios-safari-not-installed':
      return 'На айфоне уведомления из браузера приходят только приложению, добавленному на экран «Домой». Добавьте этот сайт на экран «Домой» и откройте заказ оттуда — или в следующий раз выберите при оформлении сообщения в MAX/WhatsApp.'
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
      return 'Уведомления в браузере пока не включены на платформе.'
    case 'company-disabled':
      return 'Уведомления сотрудникам отключены владельцем магазина.'
  }
}

/** localStorage key remembering WHICH endpoint this browser registered for an order (there is no «am I subscribed» route for buyers). */
export function orderPushStorageKey(token: string): string {
  return `goods-order-push:${token}`
}

const ORDER_PUSH_AT_PREFIX = 'goods-order-push-at:'
const ORDER_PUSH_KEY_PREFIX = 'goods-order-push:'
/** Client-side approximation: the server deletes a subscription N days after the order reaches a FINAL status (not after subscribing), so this local TTL can expire early for long-open orders; margin is 14 days. */
export const ORDER_PUSH_STORAGE_TTL_MS = 14 * 24 * 60 * 60 * 1000

export function orderPushStoredAtKey(token: string): string {
  return `${ORDER_PUSH_AT_PREFIX}${token}`
}

/** Removes remembered endpoints (and their timestamps) older than the TTL; entries without a timestamp (legacy) are removed too. */
export function pruneOrderPushStorage(storage: Pick<Storage, 'length' | 'key' | 'getItem' | 'removeItem'>, now: number): void {
  const keys: string[] = []
  for (let i = 0; i < storage.length; i++) {
    const k = storage.key(i)
    if (k) keys.push(k)
  }
  for (const k of keys) {
    if (!k.startsWith(ORDER_PUSH_KEY_PREFIX)) continue
    const token = k.slice(ORDER_PUSH_KEY_PREFIX.length)
    const at = Number(storage.getItem(orderPushStoredAtKey(token)))
    if (!Number.isFinite(at) || at <= 0 || now - at > ORDER_PUSH_STORAGE_TTL_MS) {
      storage.removeItem(k)
      storage.removeItem(orderPushStoredAtKey(token))
    }
  }
}
