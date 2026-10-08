import { useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { parseCatalogFilters, toSearchParams, type CatalogFilters } from '../utils/catalogQuery'

/**
 * Filters of the catalog read from / written to the URL — the only state shared by the search panel and the catalog
 * (ARCHITECTURE_CYCLE41.md §41.4.1). Every write is a new history entry, as before the move to the shared template.
 */
export function useCatalogFilters() {
  const [sp, setSp] = useSearchParams()
  const filters = useMemo(() => parseCatalogFilters(sp), [sp])
  const setFilters = (next: CatalogFilters) => setSp(toSearchParams(next))
  const resetFilters = () => setSp(new URLSearchParams())
  return { filters, setFilters, resetFilters }
}
