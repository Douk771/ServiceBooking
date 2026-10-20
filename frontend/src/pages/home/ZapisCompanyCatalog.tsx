import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { companiesApi } from '../../api/companies'
import { Icon } from '../../components/ui/Icon'
import { CatalogCard } from '../../components/landing/CatalogCard'
import { Pagination } from '../../components/ui/Pagination'
import { ShowcaseBadge } from '../../components/showcase/ShowcaseBadge'
import type { Company } from '../../types'

const PAGE_SIZE = 20

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
  const [search, setSearch] = useState('')
  // Debounce so the search-by-name/address query (server-side, per SPEC §115) isn't re-fired per
  // keystroke.
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [page, setPage] = useState(1)

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => clearTimeout(timer)
  }, [search])

  // Reset to page 1 whenever the filters change — a stale page number from a previous, longer
  // result set would otherwise request an out-of-range page.
  useEffect(() => {
    setPage(1)
  }, [debouncedSearch])

  const {
    data: companiesPage,
    isLoading,
    isError,
  } = useQuery({
    queryKey: ['companies-public', debouncedSearch, page],
    queryFn: () =>
      companiesApi.getPublic({
        search: debouncedSearch || undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
  })
  const companies = companiesPage?.items

  return (
    <>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-baseline sm:justify-between mb-6 md:mb-10">
        <div>
          <h2 className="font-serif text-[32px] font-medium mb-2 text-ink">
            Салоны на EZBOOK
          </h2>
          <p className="text-[15px] text-ink-soft">
            Выберите салон и запишитесь онлайн
          </p>
        </div>
        <div className="relative w-full sm:w-auto">
          <Icon
            name="search"
            size={17}
            strokeWidth={1.8}
            className="absolute left-4 top-1/2 -translate-y-1/2 text-gold-dark"
            aria-hidden
          />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Поиск по названию или адресу"
            className="w-full sm:w-[280px] pl-[42px] pr-4 py-3 rounded-full border border-line bg-white text-sm outline-none text-ink focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
          />
        </div>
      </div>

      {isLoading ? (
        <div className="grid gap-6" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(min(320px, 100%), 1fr))' }}>
          {Array.from({ length: 2 }).map((_, i) => (
            <div key={i} className="h-32 bg-cream-deep rounded-[20px] animate-pulse" />
          ))}
        </div>
      ) : isError ? (
        <div role="alert" className="flex items-center gap-3 py-4 text-muted">
          <Icon name="alert-circle" size={20} aria-hidden />
          <p className="text-[15px]">Не удалось загрузить список компаний. Попробуйте обновить страницу.</p>
        </div>
      ) : companies && companies.length > 0 ? (
        <>
          <div className="grid gap-6" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(min(320px, 100%), 1fr))' }}>
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
      ) : (
        <div className="flex items-center gap-3 py-4 text-muted">
          <Icon name="store" size={20} aria-hidden />
          <p className="text-[15px]">Салонов пока нет</p>
        </div>
      )}
    </>
  )
}
