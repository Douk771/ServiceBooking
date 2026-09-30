import type { ReactNode } from 'react'
import { Icon } from '../ui/Icon'
import { CompanyPhotoGallery } from './CompanyPhotoGallery'
import { CompanyMapLinks } from './CompanyMapLinks'
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
  const overlap = hasGallery ? ' -mt-[52px]' : ''

  return (
    <div className="bg-white border border-line rounded-3xl p-2 overflow-hidden">
      {hasGallery && <CompanyPhotoGallery photos={company.photos} companyName={company.name} />}
      {/* ARCHITECTURE_CYCLE13.md §204: `relative z-10` on this row is always present — a positioned row
          paints over the unpositioned gallery whatever the source order. The carousel gets no z-index. */}
      <div className="px-6 pb-6 pt-7 flex items-start gap-5 relative z-10">
        {company.logoUrl ? (
          <img
            src={company.logoUrl}
            alt=""
            className={`w-16 h-16 rounded-[18px] object-cover border-4 border-white shrink-0${overlap}`}
          />
        ) : (
          <div
            aria-hidden="true"
            className={`w-16 h-16 rounded-[18px] bg-cream-deep text-gold-dark font-serif text-2xl flex items-center justify-center border-4 border-white shrink-0${overlap}`}
          >
            {company.name.trim()[0]?.toUpperCase() ?? ''}
          </div>
        )}
        <div className="flex-1 min-w-0">
          <h1 className="font-serif text-[30px] font-medium text-ink mb-1.5 break-words">{company.name}</h1>
          {company.description && (
            <p className="text-[15px] leading-[1.55] text-ink-soft mb-3.5 max-w-[520px] break-words">
              {company.description}
            </p>
          )}
          <div className="flex flex-col items-start gap-2 text-[13.5px] text-gold-dark">
            {company.phone &&
              (phoneHref ? (
                <a
                  href={phoneHref}
                  aria-label={`Позвонить ${formatPhone(company.phone)}`}
                  className="min-h-[44px] inline-flex items-center gap-1.5 rounded-full -mx-1 px-1 hover:text-gold-darker hover:bg-cream-deep transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-gold focus-visible:outline-offset-2"
                >
                  <Icon name="phone" size={15} strokeWidth={1.6} />
                  {formatPhone(company.phone)}
                </a>
              ) : (
                <span className="min-h-[44px] inline-flex items-center gap-1.5 rounded-full -mx-1 px-1">
                  <Icon name="phone" size={15} strokeWidth={1.6} />
                  {formatPhone(company.phone)}
                </span>
              ))}
            {address && (
              <span className="flex items-center gap-1.5 break-words min-w-0">
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
            <CompanyMapLinks yandexUrl={company.yandexMapsUrl} twoGisUrl={company.twoGisUrl} />
          </div>
          {children && <div className="mt-3.5 flex flex-col items-start gap-2">{children}</div>}
        </div>
      </div>
    </div>
  )
}

export function CompanyCardSkeleton() {
  return (
    <div
      className="bg-white border border-line rounded-3xl p-2 overflow-hidden"
      aria-busy="true"
      data-testid="company-card-skeleton"
    >
      <div className="px-6 pb-6 pt-7 flex items-start gap-5">
        <div className="w-16 h-16 rounded-[18px] bg-cream-deep shrink-0 animate-pulse" />
        <div className="flex-1 min-w-0 grid gap-2.5">
          <div className="h-8 w-2/3 bg-cream-deep rounded-lg animate-pulse" />
          <div className="h-4 w-full bg-cream-deep rounded animate-pulse" />
          <div className="h-4 w-1/2 bg-cream-deep rounded animate-pulse" />
        </div>
      </div>
    </div>
  )
}
