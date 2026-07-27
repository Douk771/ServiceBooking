import { useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { companiesApi } from '../api/companies'
import { servicesApi } from '../api/services'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { BookingModal } from '../components/booking/BookingModal'
import type { Service } from '../types'

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
    enabled: !!company,
  })

  if (companyLoading) {
    return (
      <div className="max-w-lg mx-auto px-4 py-8">
        <div className="h-20 bg-gray-100 rounded-2xl animate-pulse mb-4" />
        {Array.from({ length: 3 }).map((_, i) => (
          <div key={i} className="h-16 bg-gray-100 rounded-xl animate-pulse mb-3" />
        ))}
      </div>
    )
  }

  if (!company) {
    return <div className="text-center py-16 text-gray-400">Компания не найдена</div>
  }

  return (
    <div className="max-w-lg mx-auto px-4 py-6">
      {/* Compact header */}
      <div className="flex items-center gap-3 mb-6">
        {company.logoUrl ? (
          <img src={company.logoUrl} alt={company.name} className="w-12 h-12 rounded-xl object-cover" />
        ) : (
          <div className="w-12 h-12 rounded-xl bg-gradient-to-br from-primary-400 to-accent-500 flex items-center justify-center text-white text-xl font-bold">
            {company.name[0]}
          </div>
        )}
        <div>
          <h1 className="text-lg font-bold text-gray-900">{company.name}</h1>
          {company.address && <p className="text-sm text-gray-500">📍 {company.address}</p>}
        </div>
      </div>

      {/* Services */}
      {servicesLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-16 bg-gray-100 rounded-xl animate-pulse" />
          ))}
        </div>
      ) : services && services.length > 0 ? (
        <div className="grid gap-3">
          {services.map(s => (
            <Card key={s.id} className="p-4 flex items-center justify-between gap-3">
              <div>
                <p className="font-medium text-gray-900">{s.name}</p>
                <div className="flex items-center gap-3 mt-0.5 text-sm text-gray-500">
                  <span>⏱ {s.durationMinutes} мин</span>
                  <span className="font-semibold text-primary-600">{s.price.toLocaleString('ru-RU')} ₽</span>
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
        <div className="text-center py-8 text-gray-400">Услуги ещё не добавлены</div>
      )}

      {/* Footer */}
      <p className="text-center text-xs text-gray-300 mt-6">
        Powered by{' '}
        <a href="/" className="hover:text-gray-400 transition-colors">
          ServiceBooking
        </a>
      </p>

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
