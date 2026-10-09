import { useEffect } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { CompanyLogoMark } from '@/components/company/CompanyLogoMark'
import { EmptyState, ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { ProviderBlock } from '@/components/slots/ui/ProviderBlock'
import { getStayErrorMessage, isNotFound } from '@/utils/slots/slotError'
import { formatPhone, telHref } from '@/utils/phone'
import { bathsPublicApi } from '../api/bathsPublic'
import { BathResourceCard } from '../components/catalog/BathResourceCard'
import { bathsVertical } from '../vertical'
import { NotFoundPage } from './NotFoundPage'

/**
 * `/:slug` — the page of a bath complex (US-42-12): description, contacts, photos of the complex, its baths as cards, the
 * executor block. A blocked or missing company answers 404 (API_CONTRACT_CYCLE42.md §42.23).
 */
export function CompanyPage() {
  const { slug = '' } = useParams()
  const query = useQuery({
    queryKey: ['baths-company', slug],
    queryFn: () => bathsPublicApi.company(slug),
    retry: (count, err) => !isNotFound(err) && count < 1,
  })
  const company = query.data

  useEffect(() => {
    if (company) document.title = `${company.name} — ${bathsVertical.brand}`
  }, [company])

  if (query.isLoading) {
    return (
      <main className="mx-auto max-w-[1180px] px-4 py-10 sm:px-8">
        <LoadingList rows={3} rowClass="h-40" />
      </main>
    )
  }
  if (query.isError && isNotFound(query.error)) return <NotFoundPage title="Комплекс не найден" />
  if (query.isError || !company) {
    return (
      <main className="mx-auto max-w-[760px] px-4 py-10 sm:px-8">
        <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить страницу комплекса.')} onRetry={() => void query.refetch()} />
      </main>
    )
  }

  const photos = [...company.photos].sort((a, b) => a.position - b.position)

  return (
    <main className="mx-auto max-w-[1180px] px-4 pb-4 pt-8 sm:px-8">
      <header className="flex flex-wrap items-start gap-5">
        <CompanyLogoMark name={company.name} logoUrl={company.logoUrl ?? null} size="card" />
        <div className="min-w-0 flex-1">
          <h1 className="font-serif text-[32px] leading-tight text-ink break-words [overflow-wrap:anywhere] sm:text-[40px]">{company.name}</h1>
          <p className="mt-2 flex items-start gap-1.5 text-sm text-ink-soft">
            <Icon name="map-pin" size={14} strokeWidth={1.6} className="mt-0.5 shrink-0" />
            <span className="min-w-0 break-words [overflow-wrap:anywhere]">{company.address ? `${company.cityName}, ${company.address}` : company.cityName}</span>
          </p>
          {company.description && <p className="mt-3 max-w-[680px] whitespace-pre-line text-[15px] leading-relaxed text-ink-soft">{company.description}</p>}
          <div className="mt-3 flex flex-wrap gap-x-5 gap-y-1">
            {company.phone && (
              <a href={telHref(company.phone) || undefined} className="inline-flex min-h-[44px] items-center gap-2 text-sm font-semibold !text-ink">
                <Icon name="phone" size={15} strokeWidth={1.7} className="text-gold-dark" />
                {formatPhone(company.phone)}
              </a>
            )}
            {company.yandexMapsUrl && (
              <a href={company.yandexMapsUrl} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-[44px] items-center gap-1.5 text-sm font-semibold text-gold hover:text-gold-dark">
                Яндекс Карты
                <Icon name="external-link" size={13} strokeWidth={1.7} />
              </a>
            )}
            {company.twoGisUrl && (
              <a href={company.twoGisUrl} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-[44px] items-center gap-1.5 text-sm font-semibold text-gold hover:text-gold-dark">
                2ГИС
                <Icon name="external-link" size={13} strokeWidth={1.7} />
              </a>
            )}
          </div>
        </div>
      </header>

      {photos.length > 0 && (
        <ul className="mt-6 flex snap-x gap-3 overflow-x-auto pb-2" aria-label={`Фото комплекса «${company.name}»`}>
          {photos.map((p) => (
            <li key={p.id} className="shrink-0 snap-start">
              <img src={p.thumbnailUrl || p.url} alt="" loading="lazy" decoding="async" className="h-[200px] w-[300px] rounded-2xl object-cover" />
            </li>
          ))}
        </ul>
      )}

      {!company.acceptingBookings && (
        <p role="status" className="mt-6 rounded-2xl bg-warning-bg px-5 py-3 text-sm text-warning">
          {company.notAcceptingText ?? 'Бронирование временно недоступно'}
        </p>
      )}

      <section className="mt-8" aria-labelledby="bani-title">
        <h2 id="bani-title" className="mb-4 font-serif text-2xl text-ink">
          Бани комплекса
        </h2>
        {company.resources.length === 0 ? (
          <EmptyState
            title="Пока нет опубликованных бань"
            text="Загляните позже или откройте каталог других бань."
            action={
              <Link to="/" className="text-sm font-semibold text-gold hover:text-gold-dark">
                К каталогу
              </Link>
            }
          />
        ) : (
          <ul className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {company.resources.map((r) => (
              <li key={r.resourceId}>
                <BathResourceCard item={r} filters={{ date: null }} />
              </li>
            ))}
          </ul>
        )}
      </section>

      <ProviderBlock provider={company.provider ?? undefined} className="mt-10 max-w-[760px]" />
    </main>
  )
}
