import type { CategoryDto, ProductDto } from '../types'

export interface CatalogSection {
  /** null = products without a category, shown as «Другое» (US-23-14). */
  category: CategoryDto | null
  products: ProductDto[]
}

/** Categories by `position`, then the uncategorised block last; products by `position` inside each. Empty
 *  categories are kept (the owner needs them to add products), the «Другое» block only when non-empty. */
export function groupProducts(categories: CategoryDto[], products: ProductDto[]): CatalogSection[] {
  const byPosition = (a: { position: number }, b: { position: number }) => a.position - b.position
  const sections: CatalogSection[] = [...categories].sort(byPosition).map((category) => ({
    category,
    products: products.filter((p) => p.categoryId === category.id).sort(byPosition),
  }))
  const known = new Set(categories.map((c) => c.id))
  const other = products.filter((p) => !p.categoryId || !known.has(p.categoryId)).sort(byPosition)
  if (other.length > 0) sections.push({ category: null, products: other })
  return sections
}

/** Moves `list[index]` by `delta` (−1 up, +1 down); returns the same array when the move is impossible. */
export function moveItem<T>(list: T[], index: number, delta: -1 | 1): T[] {
  const target = index + delta
  if (index < 0 || index >= list.length || target < 0 || target >= list.length) return list
  const copy = [...list]
  ;[copy[index], copy[target]] = [copy[target], copy[index]]
  return copy
}

/** Case-insensitive name filter used by the catalog and orders-screen product search. */
export function filterProducts(products: ProductDto[], query: string): ProductDto[] {
  const q = query.trim().toLowerCase()
  return q ? products.filter((p) => p.name.toLowerCase().includes(q)) : products
}
