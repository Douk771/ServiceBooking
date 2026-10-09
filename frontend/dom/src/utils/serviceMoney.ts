import { weekdayMon0 } from './stayDates'

/**
 * TS twin of `ServicePricing.HourPrices` and `ServiceMoney.Quote` (ARCHITECTURE_CYCLE39.md §39.6), checked against
 * contracts/cycle39/service-vectors.json (`price`, `money`). It gives the preview BEFORE the `quote` answer arrives; the server's
 * number always wins (an order is created against `expectedTotalRub`, a mismatch is a 409 `PriceChanged`). Whole rubles.
 */

export interface PriceRuleLike {
  daysMask: number
  fromHour: number
  toHour: number
  priceRub: number
}

/** Price of every hour of a session: the rule of the START's business day that contains the hour's first minute, else null. */
export function hourPrices(rules: readonly PriceRuleLike[], businessDate: string, startMinute: number, hours: number): (number | null)[] {
  const bit = 1 << weekdayMon0(businessDate)
  const out: (number | null)[] = []
  for (let k = 0; k < hours; k++) {
    const m = startMinute + 60 * k
    const rule = rules.find((r) => (r.daysMask & bit) !== 0 && r.fromHour * 60 <= m && m < r.toHour * 60)
    out.push(rule ? rule.priceRub : null)
  }
  return out
}

export interface MoneyItem {
  unitPriceRub: number
  quantity: number
}

export interface ServiceMoney {
  serviceAmountRub: number
  itemsAmountRub: number
  totalRub: number
  prepayRub: number
  dueOnSiteRub: number
  firstHourRub: number
}

/** Sum of a session; items are NOT part of the prepayment, a half ruble rounds up. */
export function quoteServiceMoney(prices: readonly number[], items: readonly MoneyItem[], prepayPercent: number | null): ServiceMoney {
  const serviceAmountRub = prices.reduce((a, b) => a + b, 0)
  const itemsAmountRub = items.reduce((a, i) => a + i.quantity * i.unitPriceRub, 0)
  const totalRub = serviceAmountRub + itemsAmountRub
  const prepayRub = prepayPercent == null ? 0 : Math.floor((serviceAmountRub * prepayPercent + 50) / 100)
  return { serviceAmountRub, itemsAmountRub, totalRub, prepayRub, dueOnSiteRub: totalRub - prepayRub, firstHourRub: prices[0] ?? 0 }
}
