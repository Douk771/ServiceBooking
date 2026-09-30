import type { ReactNode } from 'react'
import { Icon } from '../ui/Icon'
import { CompanyPhotoGallery } from './CompanyPhotoGallery'
import { CompanyMapLinks } from './CompanyMapLinks'
import { CompanyLogoMark } from './CompanyLogoMark'
import { COMPANY_ACTION_LINK_CLASS, COMPANY_ACTION_STATIC_CLASS } from './companyActionLink'
import { formatPhone, telHref } from '../../utils/phone'
import { publicAddress } from '../../utils/publicAddress'
import type { CompanyPhoto } from '../../types'

/**
 * ARCHITECTURE_CYCLE26.md §550 — the one public company card of ezbook (salon) and goods (shop).
 * Takes explicit props, never a DTO: each page maps its own DTO to {@link CompanyCardData}.
 */
export interface CompanyCardData {
  name: string
  description?: string | null
  logoUrl?: string | null
  phone?: string | null
  email?: string | null
  address?: string | null
  cityName?: string | null
  yandexMapsUrl?: string | null
  twoGisUrl?: string | null
  photos: CompanyPhoto[]
}

export function CompanyCard({ company, children }: { company: CompanyCardData; children?: ReactNode }) {
  const hasGallery = company.photos.length > 0
  const phoneHref = telHref(company.phone)
  const address = publicAddress(company.cityName, company.address)
  const email = company.email?.trim()
  const description = company.description?.trim()
  const overlap = hasGallery ? '-mt-[52px]' : ''
  const hasActions = Boolean(company.phone) || Boolean(company.yandexMapsUrl) || Boolean(company.twoGisUrl)
  const hasMeta = Boolean(address) || Boolean(email)
  const hasSlot = Boolean(children)

  return (
    <div className="bg-white border border-line rounded-3xl p-2 overflow-hidden">
      {hasGallery && <CompanyPhotoGallery photos={company.photos} companyName={company.name} />}
      {/* ARCHITECTURE_CYCLE13.md §204: `relative z-10` on this row is always present — a positioned row
          paints over the unpositioned gallery whatever the source order. The carousel gets no z-index.
          ARCHITECTURE_CYCLE31.md §31.11.1: a grid, DOM order = reading order (h1, description, slot, actions, meta);
          from `sm` the slot is the right column spanning all rows, so expanding hours never move the buttons. */}
      <div
        className={`grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3 px-4 pb-5 pt-5 sm:gap-x-5 sm:gap-y-0 sm:px-6 sm:pb-6 sm:pt-7 relative z-10 ${
          hasSlot ? 'sm:grid-cols-[auto_minmax(0,1fr)_auto]' : ''
        }`}
      >
        <CompanyLogoMark
          size="card"
          name={company.name}
          logoUrl={company.logoUrl}
          className={`col-start-1 row-start-1 self-start sm:row-span-4 ${overlap}`}
        />
        <h1 className="col-start-2 row-start-1 self-center font-serif text-[24px] sm:text-[30px] font-medium text-ink break-words">
          {company.name}
        </h1>
        {description && (
          <p className="col-span-2 sm:col-span-1 sm:col-start-2 sm:mt-2 text-[15px] leading-[1.55] text-ink-soft max-w-[520px] break-words">
            {description}
          </p>
        )}
        {hasSlot && (
          <div
            data-testid="company-card-slot"
            className="col-span-2 flex flex-col items-start gap-2 sm:col-span-1 sm:col-start-3 sm:row-start-1 sm:row-span-4 sm:max-w-[280px] sm:justify-self-end"
          >
            {children}
          </div>
        )}
        {hasActions && (
          <div
            data-testid="company-card-actions"
            className="col-span-2 sm:col-span-1 sm:col-start-2 sm:mt-3 flex flex-wrap items-center gap-2"
          >
            {company.phone &&
              (phoneHref ? (
                <a
                  href={phoneHref}
                  aria-label={`Позвонить ${formatPhone(company.phone)}`}
                  className={COMPANY_ACTION_LINK_CLASS}
                >
                  <Icon name="phone" size={15} strokeWidth={1.6} />
                  {formatPhone(company.phone)}
                </a>
              ) : (
                <span className={COMPANY_ACTION_STATIC_CLASS}>
                  <Icon name="phone" size={15} strokeWidth={1.6} />
                  {formatPhone(company.phone)}
                </span>
              ))}
            <CompanyMapLinks yandexUrl={company.yandexMapsUrl} twoGisUrl={company.twoGisUrl} className="contents" />
          </div>
        )}
        {hasMeta && (
          <div
            data-testid="company-card-meta"
            className="col-span-2 sm:col-span-1 sm:col-start-2 sm:mt-1 flex flex-col gap-1 sm:flex-row sm:flex-wrap sm:gap-x-4 sm:gap-y-1 text-[13.5px] text-ink-soft"
          >
            {address && (
              <span className="flex items-center gap-1.5 min-w-0 break-words">
                <Icon name="map-pin" size={15} strokeWidth={1.6} className="shrink-0" />
                <span className="min-w-0 break-words">{address}</span>
              </span>
            )}
            {email && (
              <a
                href={`mailto:${email}`}
                className="flex items-center gap-1.5 min-w-0 hover:text-gold-darker focus-visible:outline focus-visible:outline-2 focus-visible:outline-gold focus-visible:outline-offset-2"
              >
                <Icon name="mail" size={15} strokeWidth={1.6} className="shrink-0" />
                <span className="min-w-0 break-words">{email}</span>
              </a>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

/** Same grid as the card (§31.11.5): logo square, title bar, three "pills" and a details bar — no jump on load. */
export function CompanyCardSkeleton() {
  return (
    <div
      className="bg-white border border-line rounded-3xl p-2 overflow-hidden"
      aria-busy="true"
      data-testid="company-card-skeleton"
    >
      <div className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3 px-4 pb-5 pt-5 sm:gap-x-5 sm:px-6 sm:pb-6 sm:pt-7">
        <div className="col-start-1 row-start-1 self-start sm:row-span-4 w-16 h-16 rounded-[18px] bg-cream-deep shrink-0 animate-pulse" />
        <div className="col-start-2 row-start-1 self-center h-7 sm:h-8 w-2/3 bg-cream-deep rounded-lg animate-pulse" />
        <div className="col-span-2 sm:col-span-1 sm:col-start-2 flex flex-wrap gap-2">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-11 w-32 rounded-full bg-cream-deep animate-pulse" />
          ))}
        </div>
        <div className="col-span-2 sm:col-span-1 sm:col-start-2 h-4 w-1/2 bg-cream-deep rounded animate-pulse" />
      </div>
    </div>
  )
}
