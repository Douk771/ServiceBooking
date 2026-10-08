import type { DateRangeDto, HousePriceMode } from '../types'
import { addDays, daysInMonth, weekdayMon0 } from './stayDates'
import { priceFor, type PricePeriod } from './stayPricing'

/**
 * The owner's price calendar (US-37-13, P1 «цена в ячейке»): one cell per date with the price the guest would pay for the night that
 * STARTS on it (`HousePricing.PriceFor`, twin checked against stay-vectors.json). In «по датам» mode the server also names the future
 * stretches with no price (`uncoveredDates`) — those cells are marked, because a night without a price cannot be booked.
 */
export interface PriceCell {
  date: string
  priceRub: number | null
  /** Future date with no price in the by-dates mode (inside a server-reported uncovered range). */
  uncovered: boolean
  past: boolean
}

export function inRanges(date: string, ranges: readonly DateRangeDto[]): boolean {
  return ranges.some((r) => r.startDate <= date && date <= r.endDate)
}

/** Cells of a month in calendar order, `null` for the empty cells before the first day (Monday-first week). */
export function buildPriceMonth(
  monthFirst: string,
  house: { mode: HousePriceMode; constantPriceRub?: number | null },
  periods: readonly PricePeriod[],
  uncovered: readonly DateRangeDto[],
  today: string,
): (PriceCell | null)[] {
  const out: (PriceCell | null)[] = Array(weekdayMon0(monthFirst)).fill(null)
  for (let i = 0; i < daysInMonth(monthFirst); i++) {
    const date = addDays(monthFirst, i)
    const priceRub = priceFor(house, periods, date)
    out.push({ date, priceRub, past: date < today, uncovered: house.mode === 'ByDates' && date >= today && priceRub === null && inRanges(date, uncovered) })
  }
  return out
}

/** The text of an uncovered stretch for the warning under the calendar: «с 5 янв по 9 янв». */
export function countUncoveredDays(ranges: readonly DateRangeDto[]): number {
  return ranges.reduce((n, r) => n + Math.max(0, Math.round((Date.parse(r.endDate) - Date.parse(r.startDate)) / 86_400_000) + 1), 0)
}
