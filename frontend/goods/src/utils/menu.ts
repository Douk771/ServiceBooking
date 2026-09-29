import type { DailyMenuProductDto } from '../types'

/** Ids ticked in the editor: the server sends `inMenu` (pre-filled with weekday-allowed products when the menu does not exist yet, Q-24-6). */
export function initialSelection(products: readonly DailyMenuProductDto[]): Set<string> {
  return new Set(products.filter((p) => p.inMenu).map((p) => p.productId))
}

export function toggleId(selection: ReadonlySet<string>, id: string): Set<string> {
  const next = new Set(selection)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  return next
}

/** Same set of ids? (order-independent) — decides whether «Сохранить» is enabled. */
export function sameSelection(a: ReadonlySet<string>, b: ReadonlySet<string>): boolean {
  return a.size === b.size && [...a].every((x) => b.has(x))
}

/** Products in the server's order, grouped by category name; products without a category go under «Другое», last. */
export function groupByCategory(products: readonly DailyMenuProductDto[]): { name: string; products: DailyMenuProductDto[] }[] {
  const groups = new Map<string, DailyMenuProductDto[]>()
  for (const p of products) {
    const key = p.categoryName ?? ''
    groups.set(key, [...(groups.get(key) ?? []), p])
  }
  const named = [...groups.entries()].filter(([k]) => k !== '').map(([name, list]) => ({ name, products: list }))
  const other = groups.get('')
  return other ? [...named, { name: 'Другое', products: other }] : named
}
