import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { companiesApi } from '../api/companies'
import { Card } from '../components/ui/Card'
import type { Company } from '../types'

function CompanyCard({ company }: { company: Company }) {
  return (
    <Link to={`/company/${company.slug}`}>
      <Card className="p-6 hover:shadow-md hover:-translate-y-1 transition-all duration-200 cursor-pointer group">
        <div className="flex items-start gap-4">
          {company.logoUrl ? (
            <img src={company.logoUrl} alt={company.name} className="w-14 h-14 rounded-2xl object-cover" />
          ) : (
            <div className="w-14 h-14 rounded-2xl bg-gradient-to-br from-primary-400 to-accent-500 flex items-center justify-center text-white text-xl font-bold">
              {company.name[0]}
            </div>
          )}
          <div className="flex-1 min-w-0">
            <h3 className="font-semibold text-gray-900 group-hover:text-primary-600 transition-colors">
              {company.name}
            </h3>
            {company.description && (
              <p className="text-sm text-gray-500 mt-1 line-clamp-2">{company.description}</p>
            )}
            <div className="flex items-center gap-3 mt-2 text-xs text-gray-400">
              {company.address && <span>📍 {company.address}</span>}
              {company.allowSelfBooking && (
                <span className="text-green-600 font-medium">✓ Онлайн запись</span>
              )}
            </div>
          </div>
        </div>
      </Card>
    </Link>
  )
}

export function HomePage() {
  const { data: companies, isLoading } = useQuery({
    queryKey: ['companies'],
    queryFn: companiesApi.getAll,
  })

  return (
    <div className="min-h-screen bg-gradient-to-b from-orange-50 to-white">
      {/* Hero */}
      <section className="max-w-6xl mx-auto px-4 pt-20 pb-16 text-center">
        <div className="inline-flex items-center gap-2 bg-primary-100 text-primary-700 px-4 py-1.5 rounded-full text-sm font-medium mb-6">
          <span>✨</span> Запись онлайн — быстро и удобно
        </div>
        <h1 className="text-5xl font-bold text-gray-900 leading-tight mb-4">
          Запишитесь на услугу<br />
          <span className="text-primary-500">в несколько кликов</span>
        </h1>
        <p className="text-xl text-gray-500 max-w-xl mx-auto mb-8">
          Найдите специалиста, выберите удобное время и получите подтверждение мгновенно.
        </p>
      </section>

      {/* Companies */}
      <section className="max-w-6xl mx-auto px-4 pb-24">
        <h2 className="text-2xl font-bold text-gray-900 mb-6">Компании и салоны</h2>
        {isLoading ? (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {Array.from({ length: 6 }).map((_, i) => (
              <div key={i} className="h-28 bg-gray-100 rounded-2xl animate-pulse" />
            ))}
          </div>
        ) : companies && companies.length > 0 ? (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {companies.map((c) => <CompanyCard key={c.id} company={c} />)}
          </div>
        ) : (
          <div className="text-center py-16 text-gray-400">
            <p className="text-4xl mb-3">🏪</p>
            <p className="text-lg">Компании пока не добавлены</p>
          </div>
        )}
      </section>
    </div>
  )
}
