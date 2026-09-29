import type { ProductUnit } from '../types'

/** Text like "1 234,50" / "99.9" → rubles, or null when it is not a price with ≤ 2 decimals. */
export function parsePrice(text: string): number | null {
  const t = text.trim().replace(/\s/g, '').replace(',', '.')
  if (!/^\d+(\.\d{1,2})?$/.test(t)) return null
  const n = Number(t)
  return n >= 0.01 && n <= 1_000_000 ? n : null
}

/** Client-side mirror of the ProductInput rules (§410.2) — it only spares round trips, the server decides. */
export function validateProduct(f: {
  name: string
  price: string
  unit: ProductUnit
  step: string
  min: string
}): string | null {
  if (!f.name.trim()) return 'Укажите название товара'
  if (parsePrice(f.price) === null) return 'Цена — от 0,01 до 1 000 000 ₽, не больше двух знаков после запятой'
  if (f.unit === 'Weight') {
    const step = Number(f.step)
    if (!Number.isInteger(step) || step < 10 || step > 5000) return 'Шаг — от 10 до 5000 г'
    if (f.min.trim() !== '') {
      const min = Number(f.min)
      if (!Number.isInteger(min) || min < step || min % step !== 0 || min > 10000) return 'Минимальный вес — не меньше шага и кратен ему'
    }
  }
  return null
}
