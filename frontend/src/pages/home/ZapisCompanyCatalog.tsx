import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { companiesApi } from '../../api/companies'
import { CityCombobox } from '../../components/ui/CityCombobox'
import { Icon } from '../../components/ui/Icon'
import { CatalogCard } from '../../components/landing/CatalogCard'
import { Pagination } from '../../components/ui/Pagination'
import { ShowcaseBadge } from '../../components/showcase/ShowcaseBadge'
import type { City, Company } from '../../types'

// US-115: the visitor's city choice persists across visits (SPEC §115 п. 4, ARCHITECTURE_CYCLE9.md
// §103.5 / §118 п. 6 name this key `home-city`).
const HOME_CITY_KEY = 'home-city'
const PAGE_SIZE = 20

function isValidStoredCity(value: unknown): value is City {
  if (!value || typeof value !== 'object') return false
  const c = value as Record<string, unknown>
  return typeof c.id === 'number' && typeof c.name === 'string'
}

function loadStoredCity(): City | null {
  try {
    const raw = localStorage.getItem(HOME_CITY_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw)
    if (isValidStoredCity(parsed)) return parsed
    // Malformed entry (e.g. `{}`) — treat as "all cities" and clean up so we don't keep re-reading it.
    localStorage.removeItem(HOME_CITY_KEY)
    return null
  } catch {
    return null
  }
}

function CompanyCard({ company }: { company: Company }) {
  // A closed showcase company shows «Онлайн-запись» in the catalog while the server refuses every booking
  // (§592) — the pill would promise what the confirm step then denies, so it is not shown for them.
  const showPill = company.onlineBookingEnabled && !(company.isShowcase && !company.showcaseBookingOpen)
  return (
    <CatalogCard
      to={`/company/${company.slug}`}
      name={company.name}
      logoUrl={company.logoUrl}
      // API_CONTRACT_CYCLE28.md §591 — fictional showcase company: the catalog card is one of the three places for the mark.
      badge={company.isShowcase ? <ShowcaseBadge className="mt-1.5" /> : undefined}
      description={company.description}
      place={company.address}
      pill={showPill ? { text: 'Онлайн-запись', tone: 'success', icon: 'check' } : undefined}
    />
  )
}

/** Каталог салонов внутри рамки шаблона `#companies` (ARCHITECTURE_CYCLE38.md §38.4.2); поведение прежней главной. */
export function ZapisCompanyCatalog() {
  const [city, setCity] = useState<City | null>(() => loadStoredCity())
  const [search, setSearch] = useState('')
  // Debounce so the search-by-name/address query (server-side, per SPEC §115) isn't re-fired per
  // keystroke.
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [page, setPage] = useState(1)

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => clearTimeout(timer)
  }, [search])

  useEffect(() => {
    if (city) {
      localStorage.setItem(HOME_CITY_KEY, JSON.stringify(city))
    } else {
      localStorage.removeItem(HOME_CITY_KEY)
    }
  }, [city])

  // Reset to page 1 whenever the filters change — a stale page number from a previous, longer
  // result set would otherwise request an out-of-range page.
  useEffect(() => {
    setPage(1)
  }, [city?.id, debouncedSearch])

  const {
    data: companiesPage,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ['companies-public', city?.id ?? null, debouncedSearch, page],
    queryFn: () =>
      companiesApi.getPublic({
        cityId: city?.id,
        search: debouncedSearch || undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
  })
  const companies = companiesPage?.items

  return (
    <>
      <div className="flex items-baseline justify-between mb-6 flex-wrap gap-3">
        <div>
          <h2 className="font-serif text-[32px] font-medium mb-2 text-ink">
            {city ? `Компании и салоны — ${city.name}` : 'Компании и салоны'}
          </h2>
          <p className="text-[15px] text-ink-soft">
            {city ? `Показаны салоны в городе «${city.label}»` : 'Проверенные специалисты на платформе, все города'}
          </p>
        </div>
        <div className="relative">
          <Icon
            name="search"
            size={17}
            strokeWidth={1.8}
            className="absolute left-4 top-1/2 -translate-y-1/2 text-gold-dark"
          />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Поиск по названию или адресу"
            className="w-[280px] pl-[42px] pr-4 py-3 rounded-full border border-line bg-white text-sm outline-none text-ink focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
          />
        </div>
      </div>

      <div className="flex items-end gap-3 mb-10 flex-wrap">
        <div className="w-[280px]">
          <CityCombobox label="" value={city} onChange={setCity} placeholder="Выберите город" />
        </div>
        {city && (
          <button
            type="button"
            onClick={() => setCity(null)}
            className="text-[13px] font-medium text-ink-soft border-b border-line-strong pb-[3px] hover:text-ink"
          >
            Все города
          </button>
        )}
      </div>

      {isLoading ? (
        <div className="grid gap-6" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))' }}>
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="h-40 bg-cream-deep rounded-[20px] animate-pulse" />
          ))}
        </div>
      ) : isError ? (
        <div className="text-center py-16 text-muted">
          <Icon name="alert-circle" size={36} strokeWidth={1.4} className="mx-auto mb-3" />
          <p className="text-lg">Не удалось загрузить список компаний. Попробуйте обновить страницу.</p>
        </div>
      ) : companies && companies.length > 0 ? (
        <>
          <div className="grid gap-6" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))' }}>
            {companies.map((c) => (
              <CompanyCard key={c.id} company={c} />
            ))}
          </div>
          {companiesPage && (
            <Pagination
              page={companiesPage.page}
              pageSize={companiesPage.pageSize}
              total={companiesPage.total}
              hasNext={companiesPage.hasNext}
              onPageChange={setPage}
            />
          )}
        </>
      ) : city ? (
        <div className="text-center py-16 text-muted">
          <Icon name="store" size={36} strokeWidth={1.4} className="mx-auto mb-3" />
          <p className="text-lg">В городе «{city.name}» пока нет компаний на платформе.</p>
          <button
            type="button"
            onClick={() => setCity(null)}
            className="mt-4 text-[14px] font-medium text-gold-dark border-b border-gold-dark"
          >
            Показать все города
          </button>
        </div>
      ) : (
        <div className="text-center py-16 text-muted">
          <Icon name="store" size={36} strokeWidth={1.4} className="mx-auto mb-3" />
          <p className="text-lg">Компании пока не добавлены</p>
        </div>
      )}
    </>
  )
}
