import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { companyCatalogListingApi } from '../../api/companyCatalogListing'
import { CatalogListingCard } from '../../components/company/CatalogListingCard'
import { getCatalogListingErrorMessage } from '../../utils/catalogListingError'

/**
 * ARCHITECTURE_CYCLE31.md §31.10.2 (US-31-05) — «Каталог ezbook.ru» in the salon cabinet: the same visual block as the
 * goods shop, saved immediately (not with the main settings form — R-4). Status and checklist are the server's.
 */
export function SalonCatalogListingSection({ companyId }: { companyId: string }) {
  const qc = useQueryClient()
  const key = ['company-catalog-listing', companyId]
  const q = useQuery({ queryKey: key, queryFn: () => companyCatalogListingApi.get(companyId), retry: false })
  const save = useMutation({
    mutationFn: (show: boolean) => companyCatalogListingApi.put(companyId, show),
    onSuccess: (dto) => {
      qc.setQueryData(key, dto)
      // `CompanyDto.showInPublicListing` is read by other screens off `my-companies`.
      void qc.invalidateQueries({ queryKey: ['my-companies'] })
    },
    onError: () => void qc.invalidateQueries({ queryKey: key }),
  })

  return (
    <CatalogListingCard
      title="Каталог ezbook.ru"
      switchLabel="Показывать салон в каталоге ezbook.ru"
      headingId="salon-listing-title"
      headingAs="h3"
      data={q.data}
      isLoading={q.isLoading}
      loadError={
        q.isError || !q.data ? getCatalogListingErrorMessage(q.error, 'Не удалось загрузить настройку каталога.') : null
      }
      onRetry={() => void q.refetch()}
      saving={save.isPending}
      saveError={
        save.isError ? getCatalogListingErrorMessage(save.error, 'Не удалось сохранить настройку каталога.') : null
      }
      onToggle={(next) => save.mutate(next)}
    />
  )
}
