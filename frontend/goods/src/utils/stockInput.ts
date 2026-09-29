import type { ProductDto } from '../types'
import { parseKgToGrams } from './quantityFormat'

/** Pure: text typed by staff → `onHand` for the API, or an error text. Empty = «не учитывать» (null). */
export function parseStockInput(text: string, unit: ProductDto['unit']): { value: number | null } | { error: string } {
  const t = text.trim()
  if (t === '') return { value: null }
  if (unit === 'Weight') {
    // kilograms to the gram; also accept "0" (out of stock)
    if (Number(t.replace(',', '.')) === 0) return { value: 0 }
    const grams = parseKgToGrams(t)
    if (grams === null) return { error: 'Остаток — целое число от 0' }
    return { value: grams }
  }
  if (!/^\d+$/.test(t)) return { error: 'Остаток — целое число от 0' }
  return { value: Number(t) }
}

/** Editable value for the input: pieces as-is, weight in kilograms with a comma. */
export function stockToInput(onHand: number | null, unit: ProductDto['unit']): string {
  if (onHand === null) return ''
  if (unit === 'Weight') return String(onHand / 1000).replace('.', ',')
  return String(onHand)
}
