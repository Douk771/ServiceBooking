import type { ProductUnit } from '../types'

/**
 * Preview of the order arithmetic (ARCHITECTURE_CYCLE23.md §396.4) — shown in the cart before the
 * `quote` answer arrives. The server (`OrderMoney.cs`) is the source of truth: whatever is saved and
 * shown as a total comes from server numbers. Both sides are checked against
 * contracts/cycle23/order-money-vectors.json.
 *
 * Rule, in kopecks (integers — no floating-point drift):
 *  - Piece:  lineKop = priceKop × quantity
 *  - Weight: lineKop = floor((priceKop × grams + 500) / 1000)   (price is per 1 kg, quantity in grams)
 *  - total = sum of lines
 */

/** Rubles (as sent by the API, ≤ 2 decimals) → integer kopecks. */
export function toKopecks(rub: number): number {
  return Math.round(rub * 100)
}

export function fromKopecks(kop: number): number {
  return kop / 100
}

export function lineKopecks(unit: ProductUnit, priceRub: number, quantity: number): number {
  const priceKop = toKopecks(priceRub)
  if (unit === 'Weight') return Math.floor((priceKop * quantity + 500) / 1000)
  return priceKop * quantity
}

export function lineTotal(unit: ProductUnit, priceRub: number, quantity: number): number {
  return fromKopecks(lineKopecks(unit, priceRub, quantity))
}

export interface PreviewLine {
  unit: ProductUnit
  price: number
  quantity: number
}

export function orderTotal(lines: PreviewLine[]): { total: number; isApproximate: boolean } {
  const kop = lines.reduce((sum, l) => sum + lineKopecks(l.unit, l.price, l.quantity), 0)
  return { total: fromKopecks(kop), isApproximate: lines.some((l) => l.unit === 'Weight') }
}
