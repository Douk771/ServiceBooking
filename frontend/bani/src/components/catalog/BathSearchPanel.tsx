import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { bathsPublicApi } from '../../api/bathsPublic'
import { useCatalogFilters } from '../../hooks/useCatalogFilters'
import { hasActiveFilters } from '../../utils/catalogQuery'

/** Local calendar date of this device — only the `min` of the date input; the server decides what is bookable. */
function deviceToday(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const LABEL = 'flex flex-col gap-1.5 text-[13px] font-medium text-[#4A4038]'
const INPUT = 'min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep'

/**
 * «Подбор бани» — the panel slot of the landing header: city and date (P1). The URL is the only state: a change writes it at once,
 * the form always shows what the URL says (back/forward, shared links). The frame (white card) comes from the template.
 */
export function BathSearchPanel() {
  const { filters, setFilters, resetFilters } = useCatalogFilters()
  const cities = useQuery({ queryKey: ['baths-cities'], queryFn: () => bathsPublicApi.cities(), staleTime: 60_000 })
  const items = cities.data?.items ?? []

  return (
    <form aria-label="Подбор бани" onSubmit={(e) => e.preventDefault()}>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <label className={LABEL}>
          Город
          <select
            value={filters.cityId ?? ''}
            disabled={cities.isLoading}
            onChange={(e) => setFilters({ ...filters, cityId: e.target.value ? Number(e.target.value) : null, page: 1 })}
            className={INPUT}
          >
            <option value="">Все города</option>
            {items.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <label className={LABEL}>
          Дата
          <input
            type="date"
            value={filters.date ?? ''}
            min={deviceToday()}
            onChange={(e) => setFilters({ ...filters, date: e.target.value || null, page: 1 })}
            className={INPUT}
          />
        </label>
      </div>
      {cities.isError && (
        <p role="status" className="mt-2 text-xs text-danger">
          Список городов не загрузился.{' '}
          <button type="button" className="font-semibold underline" onClick={() => void cities.refetch()}>
            Повторить
          </button>
        </p>
      )}
      <p className="mt-2 text-xs text-muted">С датой остаются бани, где в этот день есть свободное время.</p>
      {hasActiveFilters(filters) && (
        <Button type="button" variant="ghost" size="sm" className="mt-1" onClick={resetFilters}>
          Сбросить фильтры
        </Button>
      )}
    </form>
  )
}
