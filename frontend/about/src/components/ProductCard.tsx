import { Icon } from '@/components/ui/Icon'
import { H3, CHECK_ITEM } from '@/components/landing/classes'
import { DEMO_SITE_URLS, displayHost } from '@/utils/publicSites'
import type { SiteUrls } from '@/utils/publicSites'
import type { ProductCard as Product } from '../content/types'
import { ExternalLink } from './ExternalLink'

interface ProductCardProps {
  product: Product
  sites: SiteUrls
  testingBadge: string
}

/** Карточка продукта. Скрытые продукты сюда не попадают (фильтр в AboutPage). */
export function ProductCard({ product, sites, testingBadge }: ProductCardProps) {
  const origin = sites[product.key]
  const testing = product.status === 'testing'
  const demoOrigin = DEMO_SITE_URLS[product.key]
  const titleId = `product-${product.key}-title`
  return (
    <article
      id={`product-${product.key}`}
      data-product={product.key}
      data-status={product.status}
      aria-labelledby={titleId}
      className={`scroll-mt-6 flex flex-col rounded-3xl p-6 sm:p-8 ${testing ? 'border border-dashed border-line-strong bg-cream' : 'border border-line bg-white shadow-soft'}`}
    >
      <div className="flex items-start justify-between gap-4">
        <span aria-hidden="true" className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-cream-deep text-gold-dark">
          <Icon name={product.icon} size={24} aria-hidden="true" />
        </span>
        {testing && (
          <p data-role="testing-badge" className="rounded-full bg-warning-bg px-3 py-1 text-[13px] font-semibold text-warning">
            {testingBadge}
          </p>
        )}
      </div>
      <h3 id={titleId} className={`${H3} mt-5`}>
        {product.name}
      </h3>
      <p data-role="host" className="mt-1 text-[14px] text-ink-soft">
        {displayHost(origin)}
      </p>
      <p className="mt-4 text-[15px] leading-relaxed text-ink-soft">{product.forWhom}</p>
      {testing && product.testingNote && <p className="mt-3 text-[14px] leading-relaxed text-warning">{product.testingNote}</p>}
      <ul className="mt-5 space-y-2">
        {product.features.map((f) => (
          <li key={f} className={CHECK_ITEM}>
            <Icon name="check" size={16} aria-hidden="true" className="mt-1 shrink-0 text-gold-dark" />
            <span>{f}</span>
          </li>
        ))}
      </ul>
      <p className="mt-5 text-[15px] leading-relaxed text-ink-soft">{product.forClients}</p>
      <div className="mt-auto flex flex-wrap items-center gap-x-3 gap-y-1 pt-6">
        <ExternalLink
          href={`${origin}${product.clientAction.path}`}
          label={product.clientAction.label}
          context={product.name}
          variant="primary"
          action="client"
        />
        {product.status === 'live' && (
          <ExternalLink
            href={`${origin}${product.businessAction.path}`}
            label={product.businessAction.label}
            context={product.name}
            variant="secondary"
            action="business"
          />
        )}
        {/* Демо — витрина на примерных данных, не призыв подключаться, поэтому выводится при любом видимом статусе. */}
        {product.demo && demoOrigin && (
          <ExternalLink
            href={`${demoOrigin}/`}
            label={product.demo.label}
            context={product.name}
            variant="text"
            action="demo"
          />
        )}
      </div>
    </article>
  )
}
