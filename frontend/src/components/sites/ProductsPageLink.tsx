import { productsPageHref } from '@/utils/publicSites'

/**
 * Link «Все продукты EZBOOK» in the footers of the four sites (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.8).
 * dev: always; release: only with VITE_PRODUCTS_PAGE_LINK=on (the page needs manual DNS/vhost steps first).
 * A plain <a>: the address is on another origin.
 */
export function ProductsPageLink() {
  const href = productsPageHref(import.meta.env)
  if (href === null) return null
  return (
    <a href={href} data-testid="products-page-link" className="hover:text-ink-soft transition-colors">
      Все продукты EZBOOK
    </a>
  )
}
