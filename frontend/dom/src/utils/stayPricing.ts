import type { HousePriceMode } from '../types'

/**
 * TS twin of the server's `HousePricing.PriceFor` (ARCHITECTURE_CYCLE37.md §37.6.1), checked against stay-vectors.json
 * (`nightPrice`). Price of a night is by the date the night STARTS. `ByDates`: a one-day period on the date wins, otherwise the
 * long period that contains it (`endDate` inclusive), otherwise no price. The owner's price calendar uses it for the preview.
 */
export interface PricePeriod {
  startDate: string
  endDate: string
  priceRub: number
}

export function priceFor(
  house: { mode: HousePriceMode; constantPriceRub?: number | null },
  periods: readonly PricePeriod[],
  date: string,
): number | null {
  if (house.mode === 'Constant') return house.constantPriceRub ?? null
  const covering = periods.filter((p) => p.startDate <= date && date <= p.endDate)
  const oneDay = covering.find((p) => p.startDate === p.endDate)
  return (oneDay ?? covering[0])?.priceRub ?? null
}
