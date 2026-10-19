import { CONTAINER, EYEBROW, FOCUS_RING, LEAD } from '@/components/landing/classes'
import { Icon } from '@/components/ui/Icon'
import type { PageCopy } from '../content/types'
import type { VisibleProduct } from '../content/visible'

interface HeroSectionProps {
  copy: PageCopy
  products: readonly VisibleProduct[]
}

export function HeroSection({ copy, products }: HeroSectionProps) {
  return (
    <section aria-labelledby="hero-title" className={`${CONTAINER} pt-12 md:pt-20`}>
      <div className="grid gap-10 lg:grid-cols-[1.25fr_1fr] lg:items-end">
        <div>
          <p className={EYEBROW}>{copy.brand}</p>
          <h1 id="hero-title" className="mt-4 font-serif text-[38px] font-medium leading-[1.05] text-ink sm:text-[56px]">
            {copy.hero.title}
          </h1>
          <p className={`${LEAD} max-w-[560px]`}>{copy.hero.subtitle}</p>
        </div>
        <ul className="divide-y divide-line border-y border-line">
          {products.map((p) => (
            <li key={p.key}>
              <a
                href={`#product-${p.key}`}
                className={`flex min-h-[56px] items-center gap-3 py-2 text-[16px] font-medium !text-ink hover:!text-gold-dark ${FOCUS_RING}`}
              >
                <Icon name={p.icon} size={20} aria-hidden="true" className="text-gold-dark" />
                <span>{p.name}</span>
              </a>
            </li>
          ))}
        </ul>
      </div>
    </section>
  )
}
