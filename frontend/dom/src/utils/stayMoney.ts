import type { StayChargeKind } from '../types'
import { nightsLabel } from './stayDates'

/**
 * TS twin of the server's `StayMoney.Quote` (ARCHITECTURE_CYCLE37.md §37.6.3), checked against contracts/cycle37/stay-vectors.json
 * (`money`). Whole rubles. It gives the preview BEFORE the `quote` answer arrives; the server's number always wins (the booking
 * is created against `expectedTotalRub`, a mismatch is a 409 `PriceChanged`).
 */

export interface MoneyInput {
  nightPrices: number[]
  capacity: number
  extraBeds: { enabled: boolean; max: number; priceRub: number }
  dogsForbidden: boolean
  hasCot: boolean
  dogFeeRub: number
  cotFeeRub: number
  prepayPercent: number
  adults: number
  children: number
  dogs: number
  needCot: boolean
}

export interface MoneyLine {
  kind: StayChargeKind
  quantity?: number
  unitPriceRub?: number
  nights?: number
  amountRub: number
  prepayEligible: boolean
}

export type MoneyError = 'TooManyGuests' | 'DogsNotAllowed' | 'CotNotAvailable'

export type MoneyResult =
  | {
      ok: true
      extraBeds: number
      lines: MoneyLine[]
      totalRub: number
      prepayRub: number
      dueAtCheckInRub: number
      averageNightRub: number
      firstNightRub: number
    }
  | { ok: false; error: MoneyError }

/** Guest count, rule order of the server: TooManyGuests → DogsNotAllowed → CotNotAvailable. */
export function checkGuests(input: Pick<MoneyInput, 'capacity' | 'extraBeds' | 'dogsForbidden' | 'hasCot' | 'adults' | 'children' | 'dogs' | 'needCot'>): MoneyError | null {
  const guests = input.adults + input.children
  const extra = Math.max(0, guests - input.capacity)
  if (extra > 0 && (!input.extraBeds.enabled || extra > input.extraBeds.max)) return 'TooManyGuests'
  if (input.dogs > 0 && input.dogsForbidden) return 'DogsNotAllowed'
  if (input.needCot && !input.hasCot) return 'CotNotAvailable'
  return null
}

/** Prepayment of the eligible lines: floor((Σ × percent + 50) / 100) — half rounds up. */
export function prepayOf(eligibleSumRub: number, percent: number): number {
  return Math.floor((eligibleSumRub * percent + 50) / 100)
}

export function averageNight(nightsSumRub: number, nights: number): number {
  return nights <= 0 ? 0 : Math.floor((nightsSumRub + Math.floor(nights / 2)) / nights)
}

export function quoteMoney(input: MoneyInput): MoneyResult {
  const error = checkGuests(input)
  if (error) return { ok: false, error }
  const n = input.nightPrices.length
  const nightsSum = input.nightPrices.reduce((a, b) => a + b, 0)
  const extra = Math.max(0, input.adults + input.children - input.capacity)

  const lines: MoneyLine[] = [{ kind: 'Nights', amountRub: nightsSum, prepayEligible: true }]
  if (extra > 0) {
    lines.push({ kind: 'ExtraBeds', quantity: extra, unitPriceRub: input.extraBeds.priceRub, nights: n, amountRub: extra * input.extraBeds.priceRub * n, prepayEligible: false })
  }
  if (input.dogs > 0) {
    lines.push({ kind: 'Dogs', quantity: input.dogs, unitPriceRub: input.dogFeeRub, nights: n, amountRub: input.dogs * input.dogFeeRub * n, prepayEligible: false })
  }
  if (input.needCot) {
    lines.push({ kind: 'Cot', quantity: 1, unitPriceRub: input.cotFeeRub, nights: n, amountRub: input.cotFeeRub * n, prepayEligible: false })
  }

  const totalRub = lines.reduce((a, l) => a + l.amountRub, 0)
  const eligible = lines.filter((l) => l.prepayEligible).reduce((a, l) => a + l.amountRub, 0)
  const prepayRub = prepayOf(eligible, input.prepayPercent)
  return {
    ok: true,
    extraBeds: extra,
    lines,
    totalRub,
    prepayRub,
    dueAtCheckInRub: totalRub - prepayRub,
    averageNightRub: averageNight(nightsSum, n),
    firstNightRub: input.nightPrices[0] ?? 0,
  }
}

/** Short caption of a charge line when the server did not send its own `label` (preview only). */
export function previewLineLabel(l: MoneyLine): string {
  switch (l.kind) {
    case 'Nights':
      return 'Проживание'
    case 'ExtraBeds':
      return `Доп. места × ${l.quantity ?? 0}, ${nightsLabel(l.nights ?? 0)}`
    case 'Dogs':
      return `Собаки × ${l.quantity ?? 0}, ${nightsLabel(l.nights ?? 0)}`
    case 'Cot':
      return `Детская кроватка, ${nightsLabel(l.nights ?? 0)}`
    case 'ManualTotal':
      return 'Итог изменён вручную'
    case 'ServiceSlot':
      return 'Услуга'
    case 'ServiceItem':
      return 'Позиция услуги'
  }
}
