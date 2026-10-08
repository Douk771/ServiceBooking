import { Link } from 'react-router-dom'
import { Icon } from '@/components/ui/Icon'
import { formatRub } from '@/utils/money'
import type { StayCatalogItemDto } from '../types'
import { houseLink, type CatalogFilters } from '../utils/catalogQuery'
import { nightsLabel } from '../utils/stayDates'

/** A house in the catalog and on the company page (US-37-05/06): cover, facts, registry number, price for the dates. */
export function HouseCard({ item, filters }: { item: StayCatalogItemDto; filters: Pick<CatalogFilters, 'checkIn' | 'checkOut' | 'guests'> }) {
  const withDates = item.totalRub != null && item.nights != null
  const busy = item.availableForDates === false
  const guests = item.capacity + item.extraBedsMax

  return (
    <Link
      to={houseLink(item.url, filters)}
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
            <Icon name="home" size={36} strokeWidth={1.3} className="text-muted" />
          </div>
        )}
        {busy && (
          <span className="absolute left-3 top-3 rounded-full bg-ink/85 px-3 py-1 text-xs font-semibold text-cream">Занято на выбранные даты</span>
        )}
      </div>
      <div className="flex flex-1 flex-col gap-1.5 p-5">
        <p className="text-xs font-medium uppercase tracking-wide text-gold-dark break-words [overflow-wrap:anywhere]">{item.companyName}</p>
        <h3 className="font-serif text-xl leading-snug text-ink break-words [overflow-wrap:anywhere]">{item.houseName}</h3>
        <p className="flex items-center gap-1.5 text-sm text-ink-soft">
          <Icon name="users" size={14} strokeWidth={1.6} />
          до {guests} {guests === 1 ? 'гостя' : 'гостей'}
          {item.dogsForbidden && <span className="text-muted">· без собак</span>}
        </p>
        {item.address && (
          <p className="flex items-start gap-1.5 text-sm text-ink-soft">
            <Icon name="map-pin" size={14} strokeWidth={1.6} className="mt-0.5 shrink-0" />
            <span className="min-w-0 break-words [overflow-wrap:anywhere]">{item.address}</span>
          </p>
        )}
        {item.registryNumber && <p className="text-xs text-muted">Номер в реестре: {item.registryNumber}</p>}
        <div className="mt-auto pt-3">
          {withDates ? (
            <>
              <p className="text-lg font-semibold text-ink">
                {formatRub(item.totalRub!)} <span className="text-sm font-normal text-ink-soft">за {nightsLabel(item.nights!)}</span>
              </p>
              {item.averageNightRub != null && <p className="text-xs text-muted">≈ {formatRub(item.averageNightRub)} за ночь</p>}
            </>
          ) : item.priceFromRub != null ? (
            <p className="text-lg font-semibold text-ink">
              <span className="text-sm font-normal text-ink-soft">от </span>
              {formatRub(item.priceFromRub)} <span className="text-sm font-normal text-ink-soft">за ночь</span>
            </p>
          ) : (
            <p className="text-sm text-muted">Цена уточняется</p>
          )}
        </div>
      </div>
    </Link>
  )
}
