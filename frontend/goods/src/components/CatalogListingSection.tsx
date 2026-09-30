import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CatalogListingCard } from '@/components/company/CatalogListingCard'
import { catalogListingApi } from '../api/catalogListing'
import { getGoodsErrorMessage } from '../utils/orderError'

/**
 * «Показывать магазин в каталоге goods.ezbook.ru» (US-25-12). Whether the shop is visible and why not is the server's
 * `checklist`; a plan that does not allow listing disables the switch and prints the server's text (§532).
 * The markup is the shared `CatalogListingCard` (ARCHITECTURE_CYCLE31.md §31.10.2): this file only wires data and errors.
 */
export function CatalogListingSection({ shopId }: { shopId: string }) {
  const qc = useQueryClient()
  const key = ['catalog-listing', shopId]
  const q = useQuery({ queryKey: key, queryFn: () => catalogListingApi.get(shopId), retry: false })
  const save = useMutation({
    mutationFn: (show: boolean) => catalogListingApi.put(shopId, show),
    onSuccess: (dto) => qc.setQueryData(key, dto),
    onError: () => void qc.invalidateQueries({ queryKey: key }),
  })

  return (
    <CatalogListingCard
      title="Каталог goods.ezbook.ru"
      switchLabel="Показывать магазин в каталоге goods.ezbook.ru"
      headingId="listing-title"
      data={q.data}
      isLoading={q.isLoading}
      loadError={q.isError || !q.data ? getGoodsErrorMessage(q.error, 'Не удалось загрузить настройку каталога.') : null}
      onRetry={() => void q.refetch()}
      saving={save.isPending}
      saveError={save.isError ? getGoodsErrorMessage(save.error, 'Не удалось сохранить настройку каталога.') : null}
      onToggle={(next) => save.mutate(next)}
    />
  )
}
