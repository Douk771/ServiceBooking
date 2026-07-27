import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { companiesApi } from '../api/companies'
import { servicesApi } from '../api/services'
import { reviewsApi } from '../api/reviews'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { BookingModal } from '../components/booking/BookingModal'
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
    <Card className="p-5 flex items-start justify-between gap-4 hover:shadow-md transition-shadow">
      <div className="flex items-start gap-4">
        {service.imageUrl ? (
          <img src={service.imageUrl} alt={service.name} className="w-16 h-16 rounded-xl object-cover" />
        ) : (
          <div className="w-16 h-16 rounded-xl bg-gradient-to-br from-primary-100 to-accent-400/20 flex items-center justify-center text-2xl">
            ✂️
          </div>
        )}
        <div>
          <h3 className="font-semibold text-gray-900">{service.name}</h3>
          {service.description && (
            <p className="text-sm text-gray-500 mt-0.5 max-w-sm">{service.description}</p>
          )}
          <div className="flex items-center gap-3 mt-2 text-sm">
            <span className="text-gray-400">⏱ {service.durationMinutes} мин</span>
            <span className="font-semibold text-primary-600">{service.price.toLocaleString('ru-RU')} ₽</span>
          </div>
        </div>
      </div>
      {canBook && (
        <Button onClick={() => onBook(service)} className="shrink-0">
          Записаться
        </Button>
      )}
    </Card>
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

  const { data: reviews } = useQuery({
    queryKey: ['company-reviews', company?.id],
    queryFn: () => reviewsApi.getForCompany(company!.id),
    enabled: !!company,
  })

  const avgRating = reviews && reviews.length > 0
    ? reviews.reduce((s, r) => s + r.rating, 0) / reviews.length
    : null

  if (companyLoading) {
    return (
      <div className="max-w-4xl mx-auto px-4 py-12">
        <div className="h-40 bg-gray-100 rounded-3xl animate-pulse mb-6" />
        <div className="grid gap-4">
          {Array.from({ length: 3 }).map((_, i) => <div key={i} className="h-24 bg-gray-100 rounded-2xl animate-pulse" />)}
        </div>
      </div>
    )
  }

  if (!company) return <div className="text-center py-24 text-gray-400">Компания не найдена</div>

  // Online self-service booking (this page's flow) requires an active paid plan — on the Free plan
  // it's blocked for guests AND authenticated clients alike (only staff manual bookings work there).
  // So the "Записаться" button shows exactly when online booking is enabled, regardless of who's viewing.
  const canBook = !!company.onlineBookingEnabled
  // The company wants online booking (allowSelfBooking) but its plan doesn't allow it yet: online
  // booking is simply unavailable here — logging in won't help, so point the client to the company instead.
  const onlineUnavailable = company.allowSelfBooking && !company.onlineBookingEnabled

  return (
    <div className="max-w-4xl mx-auto px-4 py-8">
      {/* Company Header */}
      <Card className="p-8 mb-8 bg-gradient-to-br from-orange-50 to-white">
        <div className="flex items-start gap-6">
          {company.logoUrl ? (
            <img src={company.logoUrl} alt={company.name} className="w-20 h-20 rounded-2xl object-cover" />
          ) : (
            <div className="w-20 h-20 rounded-2xl bg-gradient-to-br from-primary-400 to-accent-500 flex items-center justify-center text-white text-3xl font-bold">
              {company.name[0]}
            </div>
          )}
          <div>
            <h1 className="text-3xl font-bold text-gray-900">{company.name}</h1>
            {company.description && <p className="text-gray-600 mt-2 max-w-lg">{company.description}</p>}
            <div className="flex flex-wrap gap-4 mt-3 text-sm text-gray-500">
              {company.address && <span>📍 {company.address}</span>}
              {company.phone && <span>📞 {company.phone}</span>}
              {company.email && <span>✉️ {company.email}</span>}
            </div>
            {!company.allowSelfBooking && (
              <div className="mt-3 inline-flex items-center gap-1.5 bg-amber-50 text-amber-700 text-xs px-3 py-1 rounded-full">
                ℹ️ Запись только через мастера
              </div>
            )}
          </div>
        </div>
      </Card>

      {/* Services */}
      <h2 className="text-xl font-bold text-gray-900 mb-4">Услуги</h2>
      {onlineUnavailable && (
        <Card className="p-4 mb-4 bg-amber-50 border border-amber-200">
          <p className="text-sm text-amber-800">
            Онлайн-запись в этой компании сейчас недоступна.
            {company.phone ? ` Для записи позвоните: ${company.phone}.` : ' Обратитесь к мастеру для записи.'}
          </p>
        </Card>
      )}
      {servicesLoading ? (
        <div className="grid gap-4">
          {Array.from({ length: 3 }).map((_, i) => <div key={i} className="h-24 bg-gray-100 rounded-2xl animate-pulse" />)}
        </div>
      ) : services && services.length > 0 ? (
        <div className="grid gap-4">
          {services.map((s) => (
            <ServiceCard
              key={s.id}
              service={s}
              canBook={canBook}
              onBook={setSelectedService}
            />
          ))}
        </div>
      ) : (
        <div className="text-center py-12 text-gray-400">
          <p className="text-3xl mb-2">🛠️</p>
          <p>Услуги ещё не добавлены</p>
        </div>
      )}

      {/* Reviews */}
      <div className="mt-10">
        <div className="flex items-center gap-3 mb-4">
          <h2 className="text-xl font-bold text-gray-900">Отзывы</h2>
          {avgRating !== null && (
            <div className="flex items-center gap-1.5 bg-yellow-50 px-3 py-1 rounded-full">
              <span className="text-yellow-400 font-bold">{avgRating.toFixed(1)}</span>
              <span className="text-yellow-400">★</span>
              <span className="text-gray-400 text-sm">· {reviews!.length}</span>
            </div>
          )}
        </div>

        {reviews && reviews.length > 0 ? (
          <div className="grid gap-4">
            {reviews.map((r, i) => (
              <Card key={i} className="p-4">
                <div className="flex items-start justify-between gap-4">
                  <div className="flex-1">
                    <div className="flex items-center gap-2 mb-1">
                      <span className="font-medium text-gray-900">{r.reviewerName}</span>
                      <span className="text-gray-400 text-sm">·</span>
                      <span className="text-sm text-gray-500">{r.serviceName} у {r.masterName}</span>
                    </div>
                    <div className="flex mb-2">
                      {[1, 2, 3, 4, 5].map(s => (
                        <span key={s} className={s <= r.rating ? 'text-yellow-400' : 'text-gray-200'}>★</span>
                      ))}
                    </div>
                    {r.comment && <p className="text-sm text-gray-600">{r.comment}</p>}
                  </div>
                  <span className="text-xs text-gray-400 shrink-0">
                    {format(parseISO(r.createdAt), 'd MMM yyyy', { locale: ru })}
                  </span>
                </div>
              </Card>
            ))}
          </div>
        ) : (
          <div className="text-center py-8 text-gray-400">
            <p className="text-2xl mb-2">💬</p>
            <p>Пока нет отзывов</p>
          </div>
        )}
      </div>

      {/* Booking Modal */}
      {selectedService && company && (
        <BookingModal
          service={selectedService}
          company={company}
          onClose={() => setSelectedService(null)}
        />
      )}
    </div>
  )
}
