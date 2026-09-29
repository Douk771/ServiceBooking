import type { ProductUnit } from '../types'

/** Weight quantities travel as integer grams (§406.3); people read kilograms above 1 kg. */
export function formatGrams(grams: number): string {
  if (grams < 1000) return `${grams} г`
  const kg = grams / 1000
  return `${kg.toLocaleString('ru-RU', { maximumFractionDigits: 3 })} кг`
}

/** "2 шт" / "0,8 кг" / "540 г". `portionText` (e.g. "1 порция") is shown by the caller when relevant. */
export function formatQuantity(unit: ProductUnit, quantity: number): string {
  return unit === 'Weight' ? formatGrams(quantity) : `${quantity} шт`
}

/** Price label under a product: "250 ₽" for pieces, "540 ₽/кг" for weight items. */
export function formatUnitPrice(unit: ProductUnit, price: number): string {
  const p = price.toLocaleString('ru-RU', { minimumFractionDigits: Number.isInteger(price) ? 0 : 2, maximumFractionDigits: 2 })
  return unit === 'Weight' ? `${p} ₽/кг` : `${p} ₽`
}

/** Money with the "≈" prefix the contract requires while the sum depends on the actual weight (§406.3). */
export function formatMoney(value: number, approximate = false): string {
  const s = value.toLocaleString('ru-RU', {
    minimumFractionDigits: Number.isInteger(value) ? 0 : 2,
    maximumFractionDigits: 2,
  })
  return `${approximate ? '≈ ' : ''}${s} ₽`
}

/** Text input like "1,5" (kg) or "1500" (g) → integer grams, or null if it isn't a positive number. */
export function parseKgToGrams(input: string): number | null {
  const n = Number(input.trim().replace(',', '.'))
  if (!Number.isFinite(n) || n <= 0) return null
  return Math.round(n * 1000)
}

/** Snap grams to a valid step: at least `min`, at most `max`, multiple of `step`. */
export function clampToStep(grams: number, step: number, min: number, max: number): number {
  const snapped = Math.round(grams / step) * step
  return Math.min(max, Math.max(min, snapped))
}
