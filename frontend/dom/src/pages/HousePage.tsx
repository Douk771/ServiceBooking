import { useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { CompanyPhotoGallery } from '@/components/company/CompanyPhotoGallery'
import { CompanyMapLinks } from '@/components/company/CompanyMapLinks'
import { formatPhone, telHref } from '@/utils/phone'
import { formatRub } from '@/utils/money'
import { publicStaysApi } from '../api/publicStays'
import { BookingPanel } from '../components/BookingPanel'
import { ProviderBlock } from '../components/ProviderBlock'
import { ErrorState, LoadingList } from '../components/StatePanels'
import { StayNotice } from '../components/StayNotice'
import { StayTermsModal } from '../components/StayTermsModal'
import type { PublicHouseDto } from '../types'
import { housePhotosToCompanyPhotos } from '../utils/housePhotos'
import { isIsoDate, nightsLabel } from '../utils/stayDates'
import { getStayErrorMessage, isNotFound } from '../utils/stayError'
import { maxGuests } from '../utils/bookingForm'
import { NotFoundPage } from './NotFoundPage'

function intParam(raw: string | null, min: number, max: number): number | undefined {
  if (!raw || !/^\d+$/.test(raw)) return undefined
  const n = Number(raw)
  return n >= min && n <= max ? n : undefined
}

/**
 * `/:slug/:houseSlug` — a house (US-37-06): gallery, description, amenities, registry number, conditions, the tourist-tax note under
 * the price, «Об исполнителе», the calendar and the booking form. Query: `checkIn`, `checkOut`, `adults`, `children`.
 * Never shows the requisites for payment (Т37-04): they appear on the booking page only.
 */
export function HousePage() {
  const { slug = '', houseSlug = '' } = useParams()
  const [sp] = useSearchParams()
  const [termsOpen, setTermsOpen] = useState(false)

  const query = useQuery({
    queryKey: ['stays-house', slug, houseSlug],
    queryFn: () => publicStaysApi.house(slug, houseSlug),
    retry: (count, err) => !isNotFound(err) && count < 1,
  })
  const house = query.data

  useEffect(() => {
    if (house) document.title = `${house.name} — ${house.company.name} · ezbook Дома`
  }, [house])

  const initial = useMemo(() => {
    const checkIn = sp.get('checkIn')
    const checkOut = sp.get('checkOut')
    const ok = !!checkIn && !!checkOut && isIsoDate(checkIn) && isIsoDate(checkOut) && checkOut > checkIn
    return {
      checkIn: ok ? checkIn! : undefined,
      checkOut: ok ? checkOut! : undefined,
      adults: intParam(sp.get('adults'), 1, 30),
      children: intParam(sp.get('children'), 0, 30),
    }
  }, [sp])

  if (query.isLoading) {
    return (
      <main className="mx-auto max-w-[1180px] px-4 py-8 sm:px-8">
        <LoadingList rows={3} rowClass="h-56" />
      </main>
    )
  }
  if (query.isError && isNotFound(query.error)) return <NotFoundPage title="Дом не найден" />
  if (query.isError || !house) {
    return (
      <main className="mx-auto max-w-[760px] px-4 py-10 sm:px-8">
        <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить страницу дома.')} onRetry={() => void query.refetch()} />
      </main>
    )
  }

  if (!house.available) {
    return (
      <main className="mx-auto max-w-[560px] px-4 py-24 text-center">
        <h1 className="font-serif text-2xl text-ink">{house.notAvailableText ?? 'Дом недоступен для бронирования'}</h1>
        <Link to={`/${house.company.slug}`} className="mt-6 inline-block text-sm font-semibold text-gold hover:text-gold-dark">
          Другие дома компании
        </Link>
      </main>
    )
  }

  return (
    <main className="mx-auto max-w-[1180px] px-4 pb-6 pt-6 sm:px-8">
      <nav aria-label="Навигация" className="mb-4 flex flex-wrap items-center gap-1.5 text-sm text-ink-soft">
        <Link to="/" className="hover:text-gold-dark">
          Каталог
        </Link>
        <Icon name="chevron-right" size={13} strokeWidth={1.6} className="text-muted" />
        <Link to={`/${house.company.slug}`} className="hover:text-gold-dark">
          {house.company.name}
        </Link>
      </nav>

      <div className="grid gap-8 lg:grid-cols-[minmax(0,1.35fr)_minmax(0,1fr)] lg:items-start">
        <div className="min-w-0">
          <CompanyPhotoGallery photos={housePhotosToCompanyPhotos(house.photos)} companyName={house.name} />

          <header className="mt-6">
            <h1 className="font-serif text-[32px] leading-tight text-ink sm:text-[42px]">{house.name}</h1>
            <ul className="mt-3 flex flex-wrap gap-x-5 gap-y-2 text-sm text-ink-soft">
              <li className="flex items-center gap-1.5">
                <Icon name="users" size={15} strokeWidth={1.6} /> до {maxGuests(house)} гостей
              </li>
              <li>{house.dogsForbidden ? 'Без собак' : house.dogFeeRub > 0 ? `С собаками · ${formatRub(house.dogFeeRub)} в ночь` : 'С собаками'}</li>
              {house.hasCot && <li>Детская кроватка{house.cotFeeRub > 0 ? ` · ${formatRub(house.cotFeeRub)} в ночь` : ''}</li>}
            </ul>
            {house.priceFromRub != null && (
              <p className="mt-4 text-2xl font-semibold text-ink">
                <span className="text-sm font-normal text-ink-soft">от </span>
                {formatRub(house.priceFromRub)} <span className="text-sm font-normal text-ink-soft">за ночь</span>
              </p>
            )}
            <StayNotice textKey="StayTouristTaxNotice" variant="plain" className="mt-1.5 max-w-[560px]" />
          </header>

          <HouseDetails house={house} onOpenTerms={() => setTermsOpen(true)} />
        </div>

        <div className="lg:sticky lg:top-[88px]">
          <BookingPanel house={house} initial={initial} onOpenTerms={() => setTermsOpen(true)} />
        </div>
      </div>

      <ProviderBlock provider={house.provider} className="mt-10 max-w-[760px]" />

      {termsOpen && <StayTermsModal companyName={house.company.name} onClose={() => setTermsOpen(false)} />}
    </main>
  )
}

function HouseDetails({ house, onOpenTerms }: { house: PublicHouseDto; onOpenTerms: () => void }) {
  const r = house.rules
  const reg = house.registry
  const safeRegistryUrl = reg?.registryUrl && /^https:\/\//i.test(reg.registryUrl) ? reg.registryUrl : null
  return (
    <div className="mt-8 flex flex-col gap-8">
      {house.description && (
        <section aria-labelledby="about-title">
          <h2 id="about-title" className="mb-2 font-serif text-2xl text-ink">
            О доме
          </h2>
          <p className="whitespace-pre-line text-[15px] leading-relaxed text-ink-soft">{house.description}</p>
        </section>
      )}

      {house.amenities.length > 0 && (
        <section aria-labelledby="amenities-title">
          <h2 id="amenities-title" className="mb-3 font-serif text-2xl text-ink">
            Удобства
          </h2>
          <ul className="flex flex-wrap gap-2">
            {house.amenities.map((a) => (
              <li key={a.code} className="rounded-full border border-line bg-white px-4 py-2 text-sm text-ink">
                {a.label}
              </li>
            ))}
          </ul>
        </section>
      )}

      {(house.address || house.yandexMapsUrl || house.twoGisUrl) && (
        <section aria-labelledby="where-title">
          <h2 id="where-title" className="mb-2 font-serif text-2xl text-ink">
            Где находится
          </h2>
          {house.address && (
            <p className="flex items-start gap-2 text-[15px] text-ink-soft">
              <Icon name="map-pin" size={16} strokeWidth={1.6} className="mt-0.5 shrink-0 text-gold-dark" />
              {house.address}
            </p>
          )}
          <CompanyMapLinks yandexUrl={house.yandexMapsUrl} twoGisUrl={house.twoGisUrl} className="mt-2" />
        </section>
      )}

      {reg && (
        <section aria-labelledby="registry-title" className="rounded-2xl border border-line bg-white p-5">
          <h2 id="registry-title" className="mb-2 text-[15px] font-semibold text-ink">
            Сведения об объекте
          </h2>
          <dl className="grid gap-x-6 gap-y-1.5 text-sm sm:grid-cols-[170px_1fr]">
            <dt className="text-muted">Вид объекта</dt>
            <dd className="text-ink">{reg.objectKindLabel}</dd>
            {reg.registryNumber && (
              <>
                <dt className="text-muted">Номер в реестре</dt>
                <dd className="text-ink">
                  {reg.registryNumber}
                  {safeRegistryUrl && (
                    <>
                      {' · '}
                      <a href={safeRegistryUrl} target="_blank" rel="noopener noreferrer" className="text-gold-dark underline">
                        запись в реестре<span className="sr-only"> (откроется в новой вкладке)</span>
                      </a>
                    </>
                  )}
                </dd>
              </>
            )}
          </dl>
        </section>
      )}

      <section aria-labelledby="rules-title">
        <h2 id="rules-title" className="mb-3 font-serif text-2xl text-ink">
          Условия
        </h2>
        <dl className="grid gap-x-6 gap-y-2.5 text-sm sm:grid-cols-[190px_1fr]">
          <dt className="text-muted">Заезд / выезд</dt>
          <dd className="text-ink">
            с {r.checkInTime} / до {r.checkOutTime}
          </dd>
          <dt className="text-muted">Срок проживания</dt>
          <dd className="text-ink">
            от {nightsLabel(r.minNights)} до {nightsLabel(r.maxNights)}
          </dd>
          <dt className="text-muted">Предоплата</dt>
          <dd className="text-ink">
            {r.prepayPercent > 0
              ? `${r.prepayPercent} % — вносится переводом по реквизитам компании в течение ${r.holdMinutes} минут после брони`
              : 'не требуется — оплата при заселении'}
          </dd>
          <dt className="text-muted">Отмена</dt>
          <dd className="text-ink">{r.cancellationSummary}</dd>
        </dl>
        <button
          type="button"
          onClick={onOpenTerms}
          className="mt-3 inline-flex min-h-[44px] items-center gap-1.5 text-sm font-semibold text-gold-dark hover:underline"
        >
          Условия бронирования и проживания
        </button>
        {house.company.phone && (
          <p className="mt-1 text-sm text-ink-soft">
            Вопросы по дому:{' '}
            <a href={telHref(house.company.phone) || undefined} className="font-semibold !text-ink">
              {formatPhone(house.company.phone)}
            </a>
          </p>
        )}
      </section>
    </div>
  )
}
