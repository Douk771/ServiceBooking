import type { StorefrontProductDto } from '../types'

/** One line of the client-side cart (ARCHITECTURE_CYCLE23.md §395.3). `quantity` is pieces or grams;
 *  `unitPriceSeen` is the price the buyer last saw/confirmed and is sent back as `expectedUnitPrice`. */
export interface CartItem {
  productId: string
  quantity: number
  unitPriceSeen: number
}

export const MAX_CART_LINES = 50

export function cartStorageKey(slug: string): string {
  return `goods-cart:${slug}`
}

/** Reads a stored cart defensively — localStorage is user-editable and may hold an older shape. Bad lines,
 *  duplicates and anything beyond the 50-line server limit are dropped instead of crashing the page. */
export function parseCart(raw: string | null): CartItem[] {
  if (!raw) return []
  let data: unknown
  try {
    data = JSON.parse(raw)
  } catch {
    return []
  }
  if (!Array.isArray(data)) return []
  const seen = new Set<string>()
  const items: CartItem[] = []
  for (const x of data) {
    if (!x || typeof x !== 'object') continue
    const { productId, quantity, unitPriceSeen } = x as Record<string, unknown>
    if (typeof productId !== 'string' || !productId || seen.has(productId)) continue
    if (typeof quantity !== 'number' || !Number.isInteger(quantity) || quantity <= 0) continue
    if (typeof unitPriceSeen !== 'number' || !Number.isFinite(unitPriceSeen) || unitPriceSeen <= 0) continue
    seen.add(productId)
    items.push({ productId, quantity, unitPriceSeen })
    if (items.length === MAX_CART_LINES) break
  }
  return items
}

/** Quantity rules of one product as the storefront sends them (§406.3: integers in the base unit). */
export interface QuantityRule {
  unit: StorefrontProductDto['unit']
  step: number
  min: number
  max: number
}

export function quantityRule(p: Pick<StorefrontProductDto, 'unit' | 'weightStepGrams' | 'minQuantity' | 'maxQuantity'>): QuantityRule {
  if (p.unit === 'Weight') {
    const step = p.weightStepGrams && p.weightStepGrams > 0 ? p.weightStepGrams : 100
    return { unit: 'Weight', step, min: p.minQuantity > 0 ? p.minQuantity : step, max: p.maxQuantity > 0 ? p.maxQuantity : 10000 }
  }
  return { unit: 'Piece', step: 1, min: p.minQuantity > 0 ? p.minQuantity : 1, max: p.maxQuantity > 0 ? p.maxQuantity : 99 }
}

/** Snaps to the step grid and clamps into [min, max]. */
export function normalizeQuantity(rule: QuantityRule, quantity: number): number {
  const snapped = Math.round(quantity / rule.step) * rule.step
  return Math.min(rule.max, Math.max(rule.min, snapped))
}

/** "+" pressed: the first add lands on the minimum, later ones move one step (capped at max). */
export function incrementQuantity(rule: QuantityRule, current: number | undefined): number {
  if (current === undefined || current <= 0) return rule.min
  return Math.min(rule.max, current + rule.step)
}

/** "−" pressed: one step down; below the minimum the line is removed (null). */
export function decrementQuantity(rule: QuantityRule, current: number): number | null {
  const next = current - rule.step
  return next < rule.min ? null : next
}

export function upsertItem(items: CartItem[], item: CartItem): CartItem[] {
  const i = items.findIndex((x) => x.productId === item.productId)
  if (i === -1) return items.length >= MAX_CART_LINES ? items : [...items, item]
  const copy = [...items]
  copy[i] = item
  return copy
}

export function removeItem(items: CartItem[], productId: string): CartItem[] {
  return items.filter((x) => x.productId !== productId)
}

/** The buyer accepted the current prices: `unitPriceSeen` follows `current` for the listed products. */
export function acceptPrices(items: CartItem[], current: Record<string, number>): CartItem[] {
  return items.map((x) => (x.productId in current ? { ...x, unitPriceSeen: current[x.productId] } : x))
}

/** Lines whose price on the server differs from the one the buyer saw — they must be re-confirmed. */
export function priceChanges(
  items: CartItem[],
  quoteLines: { productId: string; unitPrice: number; problem?: unknown }[],
): { productId: string; was: number; now: number }[] {
  const seen = new Map(items.map((i) => [i.productId, i.unitPriceSeen]))
  const out: { productId: string; was: number; now: number }[] = []
  for (const l of quoteLines) {
    const was = seen.get(l.productId)
    if (was !== undefined && Math.round(was * 100) !== Math.round(l.unitPrice * 100)) out.push({ productId: l.productId, was, now: l.unitPrice })
  }
  return out
}
