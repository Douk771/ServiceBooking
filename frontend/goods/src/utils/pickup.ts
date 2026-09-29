import type { PickupOptionsDto, PickupSelectionInput, PickupSlotDto } from '../types'

/**
 * The buyer's pick-up choice kept in the cart (API_CONTRACT_CYCLE24.md §477.3, §490). The frontend never computes
 * slots or «as soon as possible» — it only stores WHAT the buyer picked and forwards it to `quote`/`orders`.
 * `Asap` is also the implicit default when nothing is stored (server default = Asap).
 */
export type PickupChoice =
  | { kind: 'Asap' }
  /** `dateLabel`/`slotLabel` are the server's own strings («Завтра», «12:00–12:15»), kept only to print the summary in the cart. */
  | { kind: 'Slot'; date: string; slotStartUtc: string; dateLabel?: string; slotLabel?: string }

export const ASAP: PickupChoice = { kind: 'Asap' }

const DATE_RE = /^\d{4}-\d{2}-\d{2}$/

export function pickupStorageKey(slug: string): string {
  return `goods-pickup:${slug}`
}

/** Defensive read: localStorage is user-editable and may hold an older shape. */
export function parsePickup(raw: string | null): PickupChoice | null {
  if (!raw) return null
  try {
    const x = JSON.parse(raw) as Record<string, unknown>
    if (x?.kind === 'Asap') return ASAP
    if (x?.kind === 'Slot' && typeof x.date === 'string' && DATE_RE.test(x.date) && typeof x.slotStartUtc === 'string' && x.slotStartUtc)
      return {
        kind: 'Slot',
        date: x.date,
        slotStartUtc: x.slotStartUtc,
        ...(typeof x.dateLabel === 'string' ? { dateLabel: x.dateLabel } : {}),
        ...(typeof x.slotLabel === 'string' ? { slotLabel: x.slotLabel } : {}),
      }
  } catch {
    // fall through
  }
  return null
}

/**
 * What the cart keeps for pick-up: the `choice` that is sent, and the date the buyer is BROWSING (drives `?date=` of the
 * storefront, so the assortment follows the date even before a slot is picked — US-24-11/12).
 */
export interface PickupState {
  choice: PickupChoice | null
  browseDate: string | null
}
export const EMPTY_PICKUP_STATE: PickupState = { choice: null, browseDate: null }

export function parsePickupState(raw: string | null): PickupState {
  if (!raw) return EMPTY_PICKUP_STATE
  try {
    const x = JSON.parse(raw) as Record<string, unknown>
    const choice = x && typeof x === 'object' && x.choice ? parsePickup(JSON.stringify(x.choice)) : null
    const browseDate = typeof x?.browseDate === 'string' && DATE_RE.test(x.browseDate) ? x.browseDate : null
    return { choice, browseDate }
  } catch {
    return EMPTY_PICKUP_STATE
  }
}

/** The date of the assortment to request: the browsed date, else the chosen slot's date, else today (no param). */
export function requestedDate(state: PickupState): string | undefined {
  return state.browseDate ?? assortmentDate(state.choice)
}

export function toPickupInput(choice: PickupChoice | null): PickupSelectionInput {
  return choice && choice.kind === 'Slot'
    ? { kind: 'Slot', date: choice.date, slotStartUtc: choice.slotStartUtc }
    : { kind: 'Asap' }
}

/** The assortment date the storefront should ask for (`?date=`): only a chosen slot changes it; Asap = today = no param. */
export function assortmentDate(choice: PickupChoice | null): string | undefined {
  return choice?.kind === 'Slot' ? choice.date : undefined
}

/**
 * Does the stored choice still exist among the options the server offers? A saved Slot whose DATE is no longer
 * offered (or whose mode was switched off) is dropped. The exact slot time is re-validated by the server on `quote`
 * and on create (409 `PickupTimeUnavailable`), not here.
 */
export function isChoiceStillOffered(choice: PickupChoice | null, options: PickupOptionsDto): boolean {
  if (!choice) return true
  if (choice.kind === 'Asap') return options.asapEnabled
  return options.scheduledEnabled && options.dates.some((d) => d.date === choice.date && d.hasSlots)
}

/** What to preselect when the buyer has not chosen: «as soon as possible» if the shop offers it and it is possible now. */
export function defaultChoice(options: PickupOptionsDto): PickupChoice | null {
  return options.asapEnabled && options.asap?.available ? ASAP : null
}

export function sameSlot(choice: PickupChoice | null, date: string, slot: Pick<PickupSlotDto, 'startUtc'>): boolean {
  return choice?.kind === 'Slot' && choice.date === date && choice.slotStartUtc === slot.startUtc
}

/** Arrow-key navigation inside a radio group (WAI-ARIA): returns the next index, wrapping around; null = key not handled. */
export function nextRadioIndex(key: string, index: number, count: number): number | null {
  if (count <= 0) return null
  switch (key) {
    case 'ArrowRight':
    case 'ArrowDown':
      return (index + 1) % count
    case 'ArrowLeft':
    case 'ArrowUp':
      return (index - 1 + count) % count
    case 'Home':
      return 0
    case 'End':
      return count - 1
    default:
      return null
  }
}
