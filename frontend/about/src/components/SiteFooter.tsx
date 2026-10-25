import { CONTAINER, FOCUS_RING } from '@/components/landing/classes'
import { OperatorRequisitesLine } from '@/components/sites/OperatorRequisites'
import type { PageCopy } from '../content/types'
import type { VisibleProduct } from '../content/visible'

interface SiteFooterProps {
  copy: PageCopy
  products: readonly VisibleProduct[]
  /** Адрес сайта продукта по ключу — уже абсолютный origin. */
  hrefOf: (key: VisibleProduct['key']) => string
  legalHref: string
  year: number
}

export function SiteFooter({ copy, products, hrefOf, legalHref, year }: SiteFooterProps) {
  const link = `inline-flex min-h-[44px] items-center !text-ink-soft hover:!text-ink ${FOCUS_RING}`
  return (
    <footer className="mt-20 border-t border-line md:mt-28">
      {/* ARCHITECTURE_CYCLE48 §48.6.4: the requisites of the operator; links are absolute, to the «Записи» site (this page has no /contacts of its own). */}
      <div className={`${CONTAINER} pt-6 text-[14px] text-ink-soft`}>
        <OperatorRequisitesLine linkMode={{ origin: hrefOf('zapis') }} />
      </div>
      <div className={`${CONTAINER} flex flex-wrap items-center justify-between gap-x-8 gap-y-2 py-6 text-[14px] text-ink-soft`}>
        <span>
          © {year} {copy.brand}
        </span>
        <ul className="flex flex-wrap gap-x-5">
          {products.map((p) => (
            <li key={p.key}>
              <a href={`${hrefOf(p.key)}/`} className={link}>
                {p.name}
              </a>
            </li>
          ))}
          <li>
            <a href={legalHref} className={link}>
              {copy.footer.legalLabel}
            </a>
          </li>
        </ul>
      </div>
    </footer>
  )
}
