import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Pagination } from '@/components/ui/Pagination'
import { EmptyState, ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { getStayErrorMessage, httpStatus } from '@/utils/slots/slotError'
import { bathsPublicApi } from '../../api/bathsPublic'
import { useCatalogFilters } from '../../hooks/useCatalogFilters'
import { CATALOG_PAGE_SIZE, hasActiveFilters, toApiQuery } from '../../utils/catalogQuery'
import { BathResourceCard } from './BathResourceCard'

/**
 * Catalog slot of the landing: heading, count, list, states and pagination. Reads the filters from the URL; writes to it only on a
 * direct user action (page change, «Сбросить фильтры»).
 */
export function BathCatalog() {
  const { filters, setFilters, resetFilters } = useCatalogFilters()
  const query = useQuery({
    queryKey: ['baths-catalog', toApiQuery(filters)],
    queryFn: () => bathsPublicApi.catalog(toApiQuery(filters)),
    placeholderData: (prev) => prev,
    // 400 is an answer about the filter (unknown city, past date), not a hiccup
    retry: (count, err) => httpStatus(err) !== 400 && count < 1,
  })
  const list = query.data
  const filtered = hasActiveFilters(filters)

  return (
    <>
      <h2 id="bani-title" className="mb-4 font-serif text-[32px] font-medium text-ink">
        Каталог бань
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
          <div className="flex flex-col items-start gap-3">
            <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить каталог.')} onRetry={() => void query.refetch()} />
            {httpStatus(query.error) === 400 && filtered && (
              <Button variant="secondary" onClick={resetFilters}>
                Сбросить фильтры
              </Button>
            )}
          </div>
        ) : !list || list.items.length === 0 ? (
          <EmptyState
            title={filtered ? 'По этим условиям бань нет' : 'Пока нет бань в каталоге'}
            text={list?.emptyText ?? (filtered ? 'Попробуйте другую дату или другой город.' : 'Как только владельцы опубликуют бани, они появятся здесь.')}
            action={
              filtered ? (
                <Button variant="secondary" onClick={resetFilters}>
                  Сбросить фильтры
                </Button>
              ) : undefined
            }
          />
        ) : (
          <>
            <p className="mb-5 text-sm text-ink-soft">
              {list.dateFilterApplied ? 'Есть свободное время на выбранную дату' : 'Все бани'}: {list.totalCount}
            </p>
            <ul className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
              {list.items.map((item) => (
                <li key={item.resourceId}>
                  <BathResourceCard item={item} filters={filters} />
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
