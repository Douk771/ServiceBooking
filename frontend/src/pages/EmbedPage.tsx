import { useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { companiesApi } from '../api/companies'
import { servicesApi } from '../api/services'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Icon } from '../components/ui/Icon'
import { CompanyPhotoGallery } from '../components/company/CompanyPhotoGallery'
import { CompanyMapLinks } from '../components/company/CompanyMapLinks'
import { BookingModal } from '../components/booking/BookingModal'
import type { Service } from '../types'
import { formatRub } from '../utils/money'

export function EmbedPage() {
  const { slug } = useParams<{ slug: string }>()
  const navigate = useNavigate()
  const [selectedService, setSelectedService] = useState<Service | null>(null)

  const { data: company, isLoading: companyLoading } = useQuery({
    queryKey: ['company', slug],
    queryFn: () => companiesApi.getBySlug(slug!),
    enabled: !!slug,
  })

  const { data: services, isLoading: servicesLoading } = useQuery({
    queryKey: ['services', company?.id],
    queryFn: () => servicesApi.getByCompany(company!.id),
    enabled: !!company && company.kind !== 'Orders',
  })

  if (companyLoading) {
    return (
      <div className="max-w-lg mx-auto px-4 py-8">
        <div className="h-20 bg-cream-deep rounded-2xl animate-pulse mb-4" />
        {Array.from({ length: 3 }).map((_, i) => (
          <div key={i} className="h-16 bg-cream-deep rounded-xl animate-pulse mb-3" />
        ))}
      </div>
    )
  }

  if (!company) {
    return <div className="text-center py-16 text-muted">Компания не найдена</div>
  }

  // Cycle 23 (§389.3-2): a shop has no booking widget — an empty block instead of a widget that cannot work.
  if (company.kind === 'Orders') {
    return <div className="text-center py-16 text-muted">У этой компании нет онлайн-записи</div>
  }

  return (
    <div className="max-w-lg mx-auto px-4 py-6">
      {/* Compact header */}
      <div className="flex items-center gap-3 mb-6">
        {company.logoUrl ? (
          <img src={company.logoUrl} alt={company.name} className="w-12 h-12 rounded-xl object-cover" />
        ) : (
          <div className="w-12 h-12 rounded-xl bg-cream-deep flex items-center justify-center text-gold-dark text-xl font-bold">
            {company.name[0]}
          </div>
        )}
        <div className="min-w-0 flex-1">
          <h1 className="font-serif text-lg font-medium text-ink break-words">{company.name}</h1>
          {company.address && (
            <p className="text-sm text-muted flex items-center gap-1">
              <Icon name="map-pin" size={12} strokeWidth={1.8} /> {company.address}
            </p>
          )}
          <CompanyMapLinks yandexUrl={company.yandexMapsUrl} twoGisUrl={company.twoGisUrl} />
        </div>
      </div>

      {company.photos?.length ? (
        <div className="mb-4">
          <CompanyPhotoGallery photos={company.photos} companyName={company.name} />
        </div>
      ) : null}

      {/* Services */}
      {servicesLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-16 bg-cream-deep rounded-xl animate-pulse" />
          ))}
        </div>
      ) : services && services.length > 0 ? (
        <div className="grid gap-3">
          {services.map((s) => (
            <Card key={s.id} className="p-4 flex items-center justify-between gap-3">
              <div>
                <p className="font-medium text-ink">{s.name}</p>
                <div className="flex items-center gap-3 mt-0.5 text-sm text-muted">
                  <span className="flex items-center gap-1">
                    <Icon name="clock" size={13} strokeWidth={1.7} /> {s.durationMinutes} мин
                  </span>
                  <span className="font-semibold text-gold-dark">{formatRub(s.price)}</span>
                </div>
              </div>
              {company.allowSelfBooking ? (
                <Button size="sm" onClick={() => setSelectedService(s)} className="shrink-0">
                  Записаться
                </Button>
              ) : (
                <Button
                  size="sm"
                  variant="secondary"
                  onClick={() => navigate(`/company/${company.slug}`)}
                  className="shrink-0"
                >
                  Подробнее
                </Button>
              )}
            </Card>
          ))}
        </div>
      ) : (
        <div className="text-center py-8 text-muted">Услуги ещё не добавлены</div>
      )}

      {/* Footer */}
      <p className="text-center text-xs text-muted mt-6">
        Powered by{' '}
        <a href="/" className="hover:text-muted transition-colors">
          EZBOOK
        </a>
        {' · '}
        <a href="/privacy" target="_blank" rel="noopener noreferrer" className="hover:text-muted transition-colors">
          Политика обработки данных
        </a>
        {' · '}
        <a href="/terms" target="_blank" rel="noopener noreferrer" className="hover:text-muted transition-colors">
          Соглашение
        </a>
      </p>

      {selectedService && company && (
        // US-67 (API_CONTRACT_CYCLE6.md §43.3): the embed widget deliberately stays single-service —
        // this is not an oversight, it's the contract's explicit call. Do not pass `serviceIds` here.
        <BookingModal
          service={selectedService}
          company={company}
          onClose={() => setSelectedService(null)}
          allowMultipleServices={false}
        />
      )}
    </div>
  )
}
