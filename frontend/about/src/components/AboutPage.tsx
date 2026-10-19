import { CONTAINER, H2 } from '@/components/landing/classes'
import type { SiteUrls } from '@/utils/publicSites'
import type { PageCopy, ProductCard as Product } from '../content/types'
import { visibleProducts } from '../content/visible'
import { AccountSection } from './AccountSection'
import { ChooserSection } from './ChooserSection'
import { HeroSection } from './HeroSection'
import { ProductCard } from './ProductCard'
import { SiteFooter } from './SiteFooter'
import { SiteHeader } from './SiteHeader'

interface AboutPageProps {
  products: readonly Product[]
  sites: SiteUrls
  copy: PageCopy
  year: number
}

/** Порядок блоков страницы (SPEC_CYCLE44 §4). Только пропсы: ни хуков, ни состояния, ни window. */
export function AboutPage({ products, sites, copy, year }: AboutPageProps) {
  const visible = visibleProducts(products)
  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-10 focus:rounded-full focus:bg-ink focus:px-5 focus:py-3 focus:!text-cream"
      >
        Перейти к содержимому
      </a>
      <SiteHeader brand={copy.brand} products={visible} />
      <main id="main">
        <HeroSection copy={copy} products={visible} />
        <section aria-labelledby="products-title" className={`${CONTAINER} mt-16 md:mt-24`}>
          <h2 id="products-title" className={H2}>
            {copy.productsHeading}
          </h2>
          <div className="mt-8 grid gap-6 lg:grid-cols-2">
            {visible.map((p) => (
              <ProductCard key={p.key} product={p} sites={sites} testingBadge={copy.testingBadge} />
            ))}
          </div>
        </section>
        <AccountSection account={copy.account} />
        {copy.chooser && <ChooserSection title={copy.chooser.title} products={visible} />}
      </main>
      <SiteFooter
        copy={copy}
        products={visible}
        hrefOf={(key) => sites[key]}
        legalHref={`${sites.zapis}${copy.footer.legalPath}`}
        year={year}
      />
    </>
  )
}
