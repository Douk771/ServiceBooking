import type { PushUnavailableReason } from './pushAvailability'
import type { PushSubscriptionDevice } from '../types'
import type { PushSite } from '../api/push'

// ARCHITECTURE_CYCLE33.md §33.8 — one set of copy for the shared «Устройства и уведомления» block on both sites.
// Which reason applies is still decided by getPushUnavailableReason(); this module only holds the words.

/** `short_name` of the site's web manifest = the name of the icon on the iPhone Home Screen. */
export type PushAppName = 'Запись' | 'Заказы' | 'Дома' | 'Бани'

export interface StaffKinds {
  hasServices: boolean
  hasOrders: boolean
  /** Cycle 37 (dom.ezbook.ru): staff of a «Дома» company. Absent = false, so the two-site callers are unchanged. */
  hasStays?: boolean
  /** Cycle 42 (bani.ezbook.ru): staff of a «Бани» company. Absent = false. */
  hasBaths?: boolean
}

function noun({ hasServices, hasOrders, hasStays = false, hasBaths = false }: StaffKinds): string {
  const parts: string[] = []
  if (hasServices) parts.push('записях')
  if (hasOrders) parts.push('заказах')
  if (hasStays) parts.push('бронях домов')
  if (hasBaths) parts.push('бронях бань')
  if (parts.length === 0) return 'записях'
  if (parts.length === 1) return parts[0]
  return `${parts.slice(0, -1).join(', ')} и ${parts[parts.length - 1]}`
}

export function staffPushIntro(kinds: StaffKinds, site: PushSite): string {
  const base = `Push о новых ${noun(kinds)} приходит на ваши устройства, где вы включили уведомления. Выключить push сотрудникам может владелец — в настройках уведомлений компании.`
  return site === 'Services' ? `${base} Это уведомления вам как сотруднику, а не сообщения клиентам.` : base
}

export function staffPushSwitchLabel(kinds: StaffKinds): string {
  return `Уведомлять меня о новых ${noun(kinds)} на этом устройстве`
}

export const ONE_DEVICE_ENOUGH_TEXT =
  'Достаточно включить на одном устройстве один раз — придут уведомления и о записях, и о заказах.'

export const ONE_SITE_ENOUGH_TEXT = 'Достаточно сделать это для одного из сайтов — ezbook.ru или goods.ezbook.ru.'

export function deviceSiteLabel(site: PushSite): string {
  return `через ${siteHost(site)}`
}

function siteHost(site: PushSite): string {
  switch (site) {
    case 'Services':
      return 'ezbook.ru'
    case 'Orders':
      return 'goods.ezbook.ru'
    case 'Stays':
      return 'dom.ezbook.ru'
    case 'Baths':
      return 'bani.ezbook.ru'
  }
}

export function staffPushUnavailableMessage(reason: PushUnavailableReason, appName: PushAppName): string {
  switch (reason) {
    case 'ios-safari-not-installed':
      return `На айфоне уведомления приходят только приложению «${appName}», добавленному на экран «Домой». Это займёт минуту:`
    case 'ios-version-too-old':
      return 'На этой версии iOS уведомления от сайтов не работают даже с экрана «Домой». Нужна iOS 16.4 или новее — обновить: Настройки → Основные → Обновление ПО.'
    case 'unsupported-browser':
      return 'Ваш браузер не умеет присылать уведомления. Включите их на другом устройстве или в другом браузере.'
    case 'insecure-context':
      return 'Уведомления работают только по защищённому соединению (HTTPS). На этом адресе они недоступны.'
    case 'permission-denied':
      return 'Вы запретили уведомления в браузере. Чтобы вернуть: значок замка в адресной строке → Уведомления → Разрешить.'
    case 'ios-permission-denied':
      return `Вы запретили уведомления для «${appName}». Чтобы вернуть: Настройки айфона → Уведомления → «${appName}» → Допуск уведомлений.`
    case 'platform-disabled':
      return 'Уведомления на устройство пока не включены на платформе.'
    case 'company-disabled':
      return 'Уведомления сотрудникам выключены во всех ваших компаниях. Включает их владелец в настройках уведомлений салона или магазина.'
  }
}

/**
 * §33.9.4 (US-33-07) — the server cannot tell that two subscriptions come from one browser, so the page guesses by the
 * device label: the first device of the OTHER site whose label equals this browser's. Only a hint ("Похоже"), never a block.
 */
export function likelySameBrowserOnOtherSite(
  devices: readonly PushSubscriptionDevice[],
  site: PushSite,
  currentLabel: string,
): PushSubscriptionDevice | null {
  return devices.find((d) => d.site !== site && d.deviceLabel === currentLabel) ?? null
}

/** Text under the switch for a likely duplicate, or null. `enabledHere` = this site already has a current device. */
export function duplicateHint(match: PushSubscriptionDevice | null, enabledHere: boolean): string | null {
  if (!match) return null
  const where = siteHost(match.site)
  return enabledHere
    ? `Похоже, на этом устройстве уведомления включены и через ${where}. Одно событие может прийти дважды — лишнюю запись удалите в списке ниже.`
    : `Похоже, на этом устройстве уведомления уже включены через ${where}. Включать ещё раз не нужно — туда приходят все ваши уведомления.`
}
