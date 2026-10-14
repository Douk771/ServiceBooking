import { Link } from 'react-router-dom'
import { Icon } from '@/components/ui/Icon'
import { formatRub } from '@/utils/money'
import type { BathResourceCardDto } from '../../types'
import { resourceLink, type CatalogFilters } from '../../utils/catalogQuery'

/** A bath in the catalog and on the company page: cover, place, capacity, the price of an hour «от». */
export function BathResourceCard({ item, filters }: { item: BathResourceCardDto; filters: Pick<CatalogFilters, 'date'> }) {
  return (
    <Link
      to={resourceLink(item.url, filters)}
      className="group flex h-full flex-col overflow-hidden rounded-3xl border border-line bg-white !text-ink transition-all hover:-translate-y-0.5 hover:border-line-strong hover:shadow-card"
    >
      <div className="relative aspect-[4/3] overflow-hidden bg-cream-deep">
        {item.coverThumbUrl || item.coverUrl ? (
          <img
            src={item.coverThumbUrl ?? item.coverUrl ?? undefined}
            alt=""
            loading="lazy"
            decoding="async"
            className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-[1.03]"
          />
        ) : (
          <div className="flex h-full w-full items-center justify-center">
            <Icon name="image" size={36} strokeWidth={1.3} className="text-muted" />
          </div>
        )}
      </div>
      <div className="flex flex-1 flex-col gap-1.5 p-5">
        <p className="text-xs font-medium uppercase tracking-wide text-gold-dark break-words [overflow-wrap:anywhere]">{item.companyName}</p>
        <h3 className="font-serif text-xl leading-snug text-ink break-words [overflow-wrap:anywhere]">{item.resourceName}</h3>
        <p className="flex items-start gap-1.5 text-sm text-ink-soft">
          <Icon name="map-pin" size={14} strokeWidth={1.6} className="mt-0.5 shrink-0" />
          <span className="min-w-0 break-words [overflow-wrap:anywhere]">{item.address ? `${item.cityName}, ${item.address}` : item.cityName}</span>
        </p>
        {item.capacity != null && (
          <p className="flex items-center gap-1.5 text-sm text-ink-soft">
            <Icon name="users" size={14} strokeWidth={1.6} />
            до {item.capacity} {item.capacity === 1 ? 'человека' : 'человек'}
          </p>
        )}
        <div className="mt-auto pt-3">
          {item.priceFromRub != null ? (
            <p className="text-lg font-semibold text-ink">
              <span className="text-sm font-normal text-ink-soft">от </span>
              {formatRub(item.priceFromRub)} <span className="text-sm font-normal text-ink-soft">за час</span>
            </p>
          ) : (
            <p className="text-sm text-muted">Цена уточняется</p>
          )}
          <p className="text-xs text-muted">от {item.minHours} ч</p>
        </div>
      </div>
    </Link>
  )
}
