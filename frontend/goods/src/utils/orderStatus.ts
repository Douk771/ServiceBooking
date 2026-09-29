import type { OrderStatus } from '../types'

const TERMINAL: ReadonlySet<OrderStatus> = new Set<OrderStatus>(['Issued', 'Rejected', 'CancelledByCustomer', 'CancelledByShop', 'NotPickedUp'])

/** Final statuses (§396.1) — there is no way back, so the buyer page stops polling. Unknown future statuses
 *  count as non-terminal: polling on is the harmless direction. */
export function isTerminalStatus(status: OrderStatus): boolean {
  return TERMINAL.has(status)
}

/** Terminal statuses that are not «Выдан»: the outcome is shown in words, not as a step on the timeline. */
export function isFailedOutcome(status: OrderStatus): boolean {
  return isTerminalStatus(status) && status !== 'Issued'
}

/** «12 мин назад» for the staff cards, relative to the SERVER clock (§415) so a tablet with a wrong clock does not lie. */
export function minutesAgo(createdAtUtc: string, serverTimeUtc: string | number): number {
  const now = typeof serverTimeUtc === 'number' ? serverTimeUtc : new Date(serverTimeUtc).getTime()
  return Math.max(0, Math.floor((now - new Date(createdAtUtc).getTime()) / 60000))
}

export function formatAgo(minutes: number): string {
  if (minutes < 1) return 'только что'
  if (minutes < 60) return `${minutes} мин назад`
  const h = Math.floor(minutes / 60)
  if (h < 24) return `${h} ч ${minutes % 60 ? `${minutes % 60} мин ` : ''}назад`
  return `${Math.floor(h / 24)} дн назад`
}
