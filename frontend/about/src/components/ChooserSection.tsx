import { CONTAINER, FOCUS_RING, H2 } from '@/components/landing/classes'
import type { VisibleProduct } from '../content/visible'

interface ChooserSectionProps {
  title: string
  products: readonly VisibleProduct[]
}

/** P2: «Какой продукт выбрать» — строки видимых продуктов со ссылкой-якорем на карточку. */
export function ChooserSection({ title, products }: ChooserSectionProps) {
  const rows = products.flatMap((p) => (p.chooserRows ?? []).map((r) => ({ key: p.key, name: p.name, business: r.business })))
  if (rows.length === 0) return null
  return (
    <section aria-labelledby="chooser-title" className={`${CONTAINER} mt-20 md:mt-28`}>
      <div className="grid gap-6 border-t border-line pt-12 lg:grid-cols-[1fr_1.4fr]">
        <h2 id="chooser-title" className={H2}>
          {title}
        </h2>
        <ul className="divide-y divide-line border-y border-line">
          {rows.map((r) => (
            <li key={`${r.key}-${r.business}`} className="flex flex-wrap items-center justify-between gap-x-4">
              <span className="py-2 text-[16px] text-ink">{r.business}</span>
              <a href={`#product-${r.key}`} className={`inline-flex min-h-[44px] items-center text-[15px] font-semibold ${FOCUS_RING}`}>
                {r.name}
              </a>
            </li>
          ))}
        </ul>
      </div>
    </section>
  )
}
