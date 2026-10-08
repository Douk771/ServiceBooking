import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Pagination } from '@/components/ui/Pagination'
import { publicStaysApi } from '../../api/publicStays'
import { useCatalogFilters } from '../../hooks/useCatalogFilters'
import { CATALOG_PAGE_SIZE, hasActiveFilters, toApiQuery } from '../../utils/catalogQuery'
import { getStayErrorMessage } from '../../utils/stayError'
import { HouseCard } from '../HouseCard'
import { EmptyState, ErrorState, LoadingList } from '../StatePanels'

/**
 * Catalog slot of the landing (ARCHITECTURE_CYCLE41.md §41.4.3): heading, count, list, states and pagination. Reads the filters
 * from the URL; writes to it only on a direct user action (page change, «Сбросить фильтры»).
 */
export function StayCatalog() {
  const { filters, setFilters, resetFilters } = useCatalogFilters()
  const query = useQuery({
    queryKey: ['stays-catalog', toApiQuery(filters)],
    queryFn: () => publicStaysApi.catalog(toApiQuery(filters)),
    placeholderData: (prev) => prev,
  })
  const list = query.data

  return (
    <>
      <h2 id="houses-title" className="mb-4 font-serif text-[32px] font-medium text-ink">
        Дома в Шерегеше
      </h2>
      <div aria-live="polite">
        {query.isLoading ? (
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            <LoadingList rows={1} rowClass="h-[360px]" />
            <div className="hidden sm:block">
              <LoadingList rows={1} rowClass="h-[360px]" />
            </div>
            <div className="hidden lg:block">
              <LoadingList rows={1} rowClass="h-[360px]" />
            </div>
          </div>
        ) : query.isError && !list ? (
          <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить каталог.')} onRetry={() => void query.refetch()} />
        ) : !list || list.items.length === 0 ? (
          <EmptyState
            title={hasActiveFilters(filters) ? 'По этим условиям домов нет' : 'Пока нет домов в каталоге'}
            text={
              hasActiveFilters(filters)
                ? 'Попробуйте другие даты, меньше гостей или более высокую цену — или сбросьте фильтры.'
                : 'Как только владельцы опубликуют дома, они появятся здесь.'
            }
            action={
              hasActiveFilters(filters) ? (
                <Button variant="secondary" onClick={resetFilters}>
                  Сбросить фильтры
                </Button>
              ) : undefined
            }
          />
        ) : (
          <>
            <p className="mb-5 text-sm text-ink-soft">
              {filters.checkIn && filters.checkOut ? 'Свободно на ваши даты' : 'Все дома'}: {list.totalCount}
            </p>
            <ul className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
              {list.items.map((item) => (
                <li key={item.houseId}>
                  <HouseCard item={item} filters={filters} />
                </li>
              ))}
            </ul>
            <Pagination
              page={list.page}
              pageSize={list.pageSize || CATALOG_PAGE_SIZE}
              total={list.totalCount}
              hasNext={list.page * (list.pageSize || CATALOG_PAGE_SIZE) < list.totalCount}
              onPageChange={(page) => setFilters({ ...filters, page })}
            />
          </>
        )}
      </div>
    </>
  )
}
