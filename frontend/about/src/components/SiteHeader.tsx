import { CONTAINER, FOCUS_RING } from '@/components/landing/classes'
import type { VisibleProduct } from '../content/visible'

interface SiteHeaderProps {
  brand: string
  products: readonly VisibleProduct[]
}

export function SiteHeader({ brand, products }: SiteHeaderProps) {
  return (
    <header className="border-b border-line">
      <div className={`${CONTAINER} flex flex-wrap items-center justify-between gap-x-8 gap-y-1 py-3`}>
        <a href="#main" className={`font-serif text-[22px] font-semibold tracking-[0.08em] !text-ink ${FOCUS_RING} inline-flex min-h-[44px] items-center`}>
          {brand}
        </a>
        <nav aria-label={`Продукты ${brand}`}>
          <ul className="flex flex-wrap gap-x-5 gap-y-0">
            {products.map((p) => (
              <li key={p.key}>
                <a href={`#product-${p.key}`} className={`inline-flex min-h-[44px] items-center text-[14px] font-medium !text-ink-soft hover:!text-ink ${FOCUS_RING}`}>
                  {p.name}
                </a>
              </li>
            ))}
          </ul>
        </nav>
      </div>
    </header>
  )
}
