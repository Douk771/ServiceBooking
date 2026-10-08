import type { ItemSelectionInput, ServiceQuoteDto, ServiceRefusalCode, ServiceSelectionInput } from '../types'

/**
 * What the guest picked for ONE session: the business date, the start, the hours and the quantity of each position
 * (ARCHITECTURE_CYCLE39.md §39.14.2). Positions default to 0 — nothing is added on the guest's behalf (69-ФЗ, ЮР39-6): only the
 * positions with a quantity above zero go into a request.
 */
export interface SessionPick {
  businessDate: string
  startMinute: number
  hours: number
  quantities: Record<string, number>
}

/** Positions that are actually chosen, in the order of `order` (stable bodies make stable query keys). */
export function chosenItems(quantities: Record<string, number>, order: readonly string[] = Object.keys(quantities)): ItemSelectionInput[] {
  return order.filter((id) => (quantities[id] ?? 0) > 0).map((itemId) => ({ itemId, quantity: quantities[itemId] }))
}

export function toSelectionInput(pick: SessionPick, order?: readonly string[]): ServiceSelectionInput {
  return { businessDate: pick.businessDate, startMinute: pick.startMinute, hours: pick.hours, items: chosenItems(pick.quantities, order) }
}

/** Changing the start keeps the hours only if the new start can hold them. */
export function keepHours(hours: number | null, options: readonly { hours: number }[]): number | null {
  return hours != null && options.some((o) => o.hours === hours) ? hours : null
}

export function clampQuantity(value: number, max: number): number {
  if (!Number.isFinite(value)) return 0
  return Math.min(Math.max(0, Math.trunc(value)), max)
}

/** The codes after which the chosen time is dead and the guest must pick again (the lists are re-read). */
const TIME_GONE: ReadonlySet<ServiceRefusalCode> = new Set(['SlotTaken', 'StartUnavailable', 'DateInPast', 'BeyondHorizon', 'TooEarly', 'OutsideStay', 'NoPriceForHours', 'HoursOutOfRange'])

export function isTimeGone(code: ServiceRefusalCode): boolean {
  return TIME_GONE.has(code)
}

/** A quote can be paid for only when it is `ok` and the gate is open. */
export function isQuoteBookable(q: ServiceQuoteDto | undefined): q is ServiceQuoteDto {
  return !!q && q.ok && q.acceptingBookings
}
