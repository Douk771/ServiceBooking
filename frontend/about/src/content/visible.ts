import type { ProductCard } from './types'

export type VisibleProduct = Exclude<ProductCard, { status: 'hidden' }>

/** Продукты, которые попадают в вывод (скрытые — нигде: ни карточки, ни меню, ни подвала). */
export function visibleProducts(products: readonly ProductCard[]): VisibleProduct[] {
  return products.filter((p): p is VisibleProduct => p.status !== 'hidden')
}
