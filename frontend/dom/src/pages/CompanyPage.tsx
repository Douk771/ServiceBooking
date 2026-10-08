import { useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { CompanyLogoMark } from '@/components/company/CompanyLogoMark'
import { formatPhone, telHref } from '@/utils/phone'
import { publicStaysApi } from '../api/publicStays'
import { HouseCard } from '../components/HouseCard'
import { ProviderBlock } from '../components/ProviderBlock'
import { EmptyState, ErrorState, LoadingList } from '../components/StatePanels'
import { parseCatalogFilters, toSearchParams } from '../utils/catalogQuery'
import { addDays } from '../utils/stayDates'
import { getStayErrorMessage, isNotFound } from '../utils/stayError'
import { NotFoundPage } from './NotFoundPage'

/**
 * `/:slug` — a «Дома» company page (US-37-05, Q2 «бронирование временно недоступно»). Works while the company is hidden from the
 * catalog. Blocked company: 200 with `available: false` and no houses. With `?checkIn&checkOut&guests` every house says whether it
 * is free for those dates (a busy house is shown, not hidden).
 */
export function CompanyPage() {
  const { slug = '' } = useParams()
  const [sp, setSp] = useSearchParams()
  const filters = useMemo(() => parseCatalogFilters(sp), [sp])
  const [checkIn, setCheckIn] = useState(filters.checkIn ?? '')
  const [checkOut, setCheckOut] = useState(filters.checkOut ?? '')

  useEffect(() => {
    setCheckIn(filters.checkIn ?? '')
    setCheckOut(filters.checkOut ?? '')
  }, [filters.checkIn, filters.checkOut])

  const params = filters.checkIn && filters.checkOut ? { checkIn: filters.checkIn, checkOut: filters.checkOut, guests: filters.guests } : undefined
  const query = useQuery({
    queryKey: ['stays-company', slug, params ?? null],
    queryFn: () => publicStaysApi.company(slug, params),
    retry: (count, err) => !isNotFound(err) && count < 1,
  })
  const company = query.data

  useEffect(() => {
    if (company) document.title = `${company.name} — ezbook · Дома`
  }, [company])

  if (query.isLoading) {
    return (
      <main className="mx-auto max-w-[1180px] px-4 py-10 sm:px-8">
        <LoadingList rows={3} rowClass="h-40" />
      </main>
    )
  }
  if (query.isError && isNotFound(query.error)) return <NotFoundPage title="Компания не найдена" />
  if (query.isError || !company) {
    return (
      <main className="mx-auto max-w-[760px] px-4 py-10 sm:px-8">
        <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить страницу компании.')} onRetry={() => void query.refetch()} />
      </main>
    )
  }

  if (!company.available) {
    return (
      <main className="mx-auto max-w-[560px] px-4 py-24 text-center">
        <h1 className="font-serif text-2xl text-ink">{company.notAvailableText ?? 'Страница недоступна'}</h1>
        <Link to="/" className="mt-6 inline-block text-sm font-semibold text-gold hover:text-gold-dark">
          К каталогу
        </Link>
      </main>
    )
  }

  const applyDates = (nextIn: string, nextOut: string) => {
    setCheckIn(nextIn)
    setCheckOut(nextOut)
    if (nextIn && nextOut && nextOut > nextIn) setSp(toSearchParams({ ...filters, checkIn: nextIn, checkOut: nextOut, page: 1 }))
    else if (!nextIn && !nextOut) setSp(toSearchParams({ ...filters, checkIn: null, checkOut: null, page: 1 }))
  }

  return (
    <main className="mx-auto max-w-[1180px] px-4 pb-4 pt-8 sm:px-8">
      <header className="flex flex-wrap items-start gap-5">
        <CompanyLogoMark name={company.name} logoUrl={company.logoUrl} size="card" />
        <div className="min-w-0 flex-1">
          <h1 className="font-serif text-[32px] leading-tight text-ink sm:text-[40px]">{company.name}</h1>
          {company.description && <p className="mt-2 max-w-[680px] whitespace-pre-line text-[15px] leading-relaxed text-ink-soft">{company.description}</p>}
          {company.phone && (
            <a href={telHref(company.phone) || undefined} className="mt-3 inline-flex min-h-[44px] items-center gap-2 text-sm font-semibold !text-ink">
              <Icon name="phone" size={15} strokeWidth={1.7} className="text-gold-dark" />
              {formatPhone(company.phone)}
            </a>
          )}
        </div>
      </header>

      {!company.acceptingBookings && (
        <p role="status" className="mt-6 rounded-2xl bg-warning-bg px-5 py-3 text-sm text-warning">
          {company.notAcceptingText ?? 'Бронирование временно недоступно'}
        </p>
      )}

      <section className="mt-8" aria-labelledby="houses-title">
        <div className="mb-4 flex flex-wrap items-end justify-between gap-4">
          <h2 id="houses-title" className="font-serif text-2xl text-ink">
            Дома
          </h2>
          <div className="flex flex-wrap items-end gap-3">
            <label className="flex flex-col gap-1 text-xs font-medium text-ink-soft">
              Заезд
              <input
                type="date"
                value={checkIn}
                onChange={(e) => applyDates(e.target.value, checkOut && checkOut <= e.target.value ? '' : checkOut)}
                className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink"
              />
            </label>
            <label className="flex flex-col gap-1 text-xs font-medium text-ink-soft">
              Выезд
              <input
                type="date"
                value={checkOut}
                min={checkIn ? addDays(checkIn, 1) : undefined}
                onChange={(e) => applyDates(checkIn, e.target.value)}
                className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink"
              />
            </label>
          </div>
        </div>
        {company.houses.length === 0 ? (
          <EmptyState title="Пока нет опубликованных домов" text="Загляните позже или откройте каталог других домов." />
        ) : (
          <ul className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {company.houses.map((h) => (
              <li key={h.houseId}>
                <HouseCard item={h} filters={filters} />
              </li>
            ))}
          </ul>
        )}
      </section>

      <ProviderBlock provider={company.provider} className="mt-10 max-w-[760px]" />
    </main>
  )
}
