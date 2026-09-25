import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { companiesApi } from '../api/companies'
import { servicesApi } from '../api/services'
import { reviewsApi } from '../api/reviews'
import { Button } from '../components/ui/Button'
import { Icon } from '../components/ui/Icon'
import { Pagination } from '../components/ui/Pagination'
import { BookingModal } from '../components/booking/BookingModal'
import { CompanyPhotoGallery } from '../components/company/CompanyPhotoGallery'
import { CompanyMapLinks } from '../components/company/CompanyMapLinks'
import { telHref, formatPhone } from '../utils/phone'
import type { Service } from '../types'

function ServiceCard({
  service,
  canBook,
  onBook,
}: {
  service: Service
  canBook: boolean
  onBook: (s: Service) => void
}) {
  return (
    <div className="bg-white border border-line rounded-[18px] p-[22px] flex items-center justify-between gap-4 flex-wrap">
      <div className="flex items-center gap-4">
        {service.imageUrl ? (
          <img
            src={service.imageUrl}
            alt={service.name}
            className="w-[52px] h-[52px] rounded-[14px] object-cover shrink-0"
          />
        ) : (
          <div className="w-[52px] h-[52px] rounded-[14px] bg-cream-deep flex items-center justify-center shrink-0">
            <span className="text-lg font-bold text-gold-dark">{service.name[0]?.toUpperCase() ?? '?'}</span>
          </div>
        )}
        <div>
          <h3 className="text-base font-semibold text-ink mb-1">{service.name}</h3>
          {service.description && <p className="text-[13.5px] text-ink-soft mb-1.5 max-w-sm">{service.description}</p>}
          <div className="flex items-center gap-3.5 text-[13px]">
            <span className="text-muted flex items-center gap-1">
              <Icon name="clock" size={13} strokeWidth={1.6} />
              {service.durationMinutes} мин
            </span>
            <span className="font-semibold text-gold-dark">{service.price.toLocaleString('ru-RU')} ₽</span>
          </div>
        </div>
      </div>
      {canBook && (
        <Button onClick={() => onBook(service)} className="shrink-0">
          Записаться
        </Button>
      )}
    </div>
  )
}

export function CompanyPage() {
  const { slug } = useParams<{ slug: string }>()
  const [selectedService, setSelectedService] = useState<Service | null>(null)

  const { data: company, isLoading: companyLoading } = useQuery({
    queryKey: ['company', slug],
    queryFn: () => companiesApi.getBySlug(slug!),
    enabled: !!slug,
  })

  const { data: services, isLoading: servicesLoading } = useQuery({
    queryKey: ['services', company?.id],
    queryFn: () => servicesApi.getByCompany(company!.id),
    enabled: !!company,
  })

  const [reviewsPage, setReviewsPage] = useState(1)
  const { data: reviewsData } = useQuery({
    queryKey: ['company-reviews', company?.id, reviewsPage],
    queryFn: () => reviewsApi.getForCompany(company!.id, reviewsPage),
    enabled: !!company,
  })
  const reviews = reviewsData?.items

  // Server-computed aggregate over the company's full review history (Company.averageRating/
  // reviewCount) — NOT derived from the current page of `reviewsData.items`. Computing it from the
  // page was a QA-confirmed regression (TEST_CATALOG.md, item 2 under "Регрессии продукта, найденные
  // QA…"): it changed depending on which page of reviews the visitor happened to be on.
  const avgRating = company?.averageRating ?? null
  const reviewCount = company?.reviewCount ?? 0

  if (companyLoading) {
    return (
      <div className="max-w-[900px] mx-auto px-8 py-10">
        <div className="h-[280px] bg-cream-deep rounded-3xl animate-pulse mb-10" />
        <div className="grid gap-3.5">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-24 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      </div>
    )
  }

  if (!company) return <div className="text-center py-24 text-muted">Компания не найдена</div>

  // Online self-service booking (this page's flow) requires an active paid plan — on the Free plan
  // it's blocked for guests AND authenticated clients alike (only staff manual bookings work there).
  // So the "Записаться" button shows exactly when online booking is enabled, regardless of who's viewing.
  const canBook = !!company.onlineBookingEnabled
  // The company wants online booking (allowSelfBooking) but its plan doesn't allow it yet: online
  // booking is simply unavailable here — logging in won't help, so point the client to the company instead.
  const onlineUnavailable = company.allowSelfBooking && !company.onlineBookingEnabled

  return (
    <div className="max-w-[900px] mx-auto px-8 pt-10 pb-24">
      {/* Company Header */}
      <div className="bg-white border border-line rounded-3xl p-2 mb-10 overflow-hidden">
        {/* API_CONTRACT_CYCLE10.md §129 — `photos` arrives WITH the company on this endpoint (§109.3
            performance), so no extra request; `null`/absent (server not yet on cycle 10) behaves as
            an empty gallery, same as an actually-empty one. */}
        <CompanyPhotoGallery photos={company.photos ?? []} companyName={company.name} />
        {/* ARCHITECTURE_CYCLE13.md §204 (R10): `relative z-10` is the fix for the original defect —
            a positioned, non-zero-z-index row paints over an unpositioned sibling (the gallery)
            regardless of source order. The logo itself needs no z-index of its own; it's already
            inside this raised row. Carousel root gets no z-index at all (§204 p.3) — giving it one
            would restart the exact z-index contest this line just ended. */}
        <div className="px-6 pb-6 pt-7 flex items-start gap-5 relative z-10">
          {company.logoUrl ? (
            <img
              src={company.logoUrl}
              alt={company.name}
              className="w-16 h-16 rounded-[18px] object-cover -mt-[52px] border-4 border-white shrink-0"
            />
          ) : (
            <div className="w-16 h-16 rounded-[18px] bg-cream-deep flex items-center justify-center shrink-0 -mt-[52px] border-4 border-white">
              <Icon name="store" size={28} strokeWidth={1.6} className="text-gold-dark" />
            </div>
          )}
          <div className="flex-1 min-w-0">
            <h1 className="font-serif text-[30px] font-medium text-ink mb-1.5">{company.name}</h1>
            {company.description && (
              <p className="text-[15px] leading-[1.55] text-ink-soft mb-3.5 max-w-[520px]">{company.description}</p>
            )}
            {/* ARCHITECTURE_CYCLE15.md §254 — vertical list, one row per contact, all left-aligned
                on one edge (items-start on the container, items-center within each row). Order:
                phone → address + map links → email. Was `flex gap-5 flex-wrap`, which let the
                two-line address column stretch its single-line neighbours to match its height. */}
            <div className="flex flex-col items-start gap-2 text-[13.5px] text-gold-dark">
              {company.phone &&
                // ARCHITECTURE_CYCLE17.md §305.4 (US-17-05, C15-6.5) — `href=""` renders as a link
                // to nowhere for both mouse and screen reader; `telHref` can return '' for a phone
                // that has no digits at all, so an empty `href` must fall through to `undefined`
                // and the element becomes a plain <span>, not an unclickable <a>.
                (telHref(company.phone) ? (
                  <a
                    href={telHref(company.phone)}
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
              {company.address && (
                <span className="flex items-center gap-1.5">
                  <Icon name="map-pin" size={15} strokeWidth={1.6} />
                  {company.address}
                </span>
              )}
              {company.email && (
                <span className="flex items-center gap-1.5">
                  <Icon name="mail" size={15} strokeWidth={1.6} />
                  {company.email}
                </span>
              )}
              {/* ARCHITECTURE_CYCLE17.md §305.5 (US-17-04, C15-6.6) — a sibling of the address row,
                  not nested inside `{company.address && …}`: the map links come from
                  yandexMapsUrl/twoGisUrl, which the owner can set independently of the free-text
                  address. `CompanyMapLinks` already returns null when both are empty, so "no space
                  when both are empty" needs no extra condition here. */}
              <CompanyMapLinks yandexUrl={company.yandexMapsUrl} twoGisUrl={company.twoGisUrl} />
            </div>
            {!company.allowSelfBooking && (
              <div className="mt-3.5 inline-flex items-center gap-1.5 bg-warning-bg text-warning text-xs px-3 py-1 rounded-full">
                <Icon name="alert-circle" size={12} strokeWidth={1.8} />
                Запись только через мастера
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Services */}
      <h2 className="font-serif text-2xl font-medium text-ink mb-5">Услуги</h2>
      {onlineUnavailable && (
        <div className="p-4 mb-4 rounded-2xl bg-warning-bg border border-[#EAD9AC]">
          <p className="text-sm text-warning">
            Онлайн-запись в этой компании сейчас недоступна.
            {company.phone ? ` Для записи позвоните: ${company.phone}.` : ' Обратитесь к мастеру для записи.'}
          </p>
        </div>
      )}
      {servicesLoading ? (
        <div className="grid gap-3.5 mb-[52px]">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-24 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : services && services.length > 0 ? (
        <div className="grid gap-3.5 mb-[52px]">
          {services.map((s) => (
            <ServiceCard key={s.id} service={s} canBook={canBook} onBook={setSelectedService} />
          ))}
        </div>
      ) : (
        <div className="text-center py-12 text-muted mb-[52px]">
          <p>Услуги ещё не добавлены</p>
        </div>
      )}

      {/* Reviews */}
      <div className="flex items-center gap-3 mb-5">
        <h2 className="font-serif text-2xl font-medium text-ink">Отзывы</h2>
        {avgRating !== null && (
          <div className="flex items-center gap-1.5 bg-warning-bg px-3 py-1 rounded-full text-[13px]">
            <Icon name="star" size={12} className="text-[#B08A3E]" />
            <span className="font-bold text-warning">{avgRating.toFixed(1)}</span>
            <span className="text-muted">· {reviewCount} отзывов</span>
          </div>
        )}
      </div>

      {reviews && reviews.length > 0 ? (
        <div className="flex flex-col gap-3">
          {reviews.map((r, i) => (
            <div key={i} className="bg-white border border-line rounded-2xl px-5 py-[18px]">
              <div className="flex items-start justify-between gap-3 mb-2">
                <div>
                  <span className="font-semibold text-sm text-ink">{r.reviewerName}</span>
                  <span className="text-muted text-[13px]">
                    {' '}
                    · {r.serviceName} у {r.masterName}
                  </span>
                </div>
                <span className="text-[12.5px] text-muted shrink-0">
                  {format(parseISO(r.createdAt), 'd MMM yyyy', { locale: ru })}
                </span>
              </div>
              <div className="flex gap-0.5 mb-2">
                {[1, 2, 3, 4, 5].map((s) => (
                  <Icon key={s} name="star" size={13} className={s <= r.rating ? 'text-[#B08A3E]' : 'text-line'} />
                ))}
              </div>
              {r.comment && <p className="text-sm text-ink-soft leading-[1.55]">{r.comment}</p>}
            </div>
          ))}
        </div>
      ) : (
        <div className="text-center py-8 text-muted">
          <p>Пока нет отзывов</p>
        </div>
      )}

      {reviewsData && (
        <Pagination
          page={reviewsData.page}
          pageSize={reviewsData.pageSize}
          total={reviewsData.total}
          hasNext={reviewsData.hasNext}
          onPageChange={setReviewsPage}
        />
      )}

      {/* Booking Modal */}
      {selectedService && company && (
        <BookingModal service={selectedService} company={company} onClose={() => setSelectedService(null)} />
      )}
    </div>
  )
}
