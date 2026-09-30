import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Card } from '@/components/ui/Card'
import { catalogListingApi } from '../api/catalogListing'
import { ErrorState, InlineError, LoadingList } from './StatePanels'
import { getGoodsErrorMessage } from '../utils/orderError'

/**
 * «Показывать магазин в каталоге goods.ezbook.ru» (US-25-12). Whether the shop is visible and why not is the server's
 * `checklist`; a plan that does not allow listing disables the switch and prints the server's text (§532).
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
    <Card className="p-5 sm:p-6" aria-labelledby="listing-title" role="region">
      <h2 id="listing-title" className="font-serif text-xl text-ink">Каталог goods.ezbook.ru</h2>
      {q.isLoading ? (
        <div className="mt-3"><LoadingList rows={1} rowClass="h-14" /></div>
      ) : q.isError || !q.data ? (
        <div className="mt-3"><ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить настройку каталога.')} onRetry={() => void q.refetch()} /></div>
      ) : (
        <>
          <label className={`flex items-start justify-between gap-4 py-3 min-h-[44px] ${q.data.allowedByPlan ? 'cursor-pointer' : 'opacity-60'}`}>
            <span className="text-sm font-medium text-ink">Показывать магазин в каталоге goods.ezbook.ru</span>
            <input
              type="checkbox"
              role="switch"
              className="mt-1 h-5 w-9 shrink-0 accent-[#2B2420]"
              checked={q.data.showInCatalog}
              disabled={!q.data.allowedByPlan || save.isPending}
              onChange={(e) => save.mutate(e.target.checked)}
            />
          </label>
          {!q.data.allowedByPlan && q.data.notAllowedByPlanText && (
            <p className="text-sm text-warning bg-warning-bg rounded-xl px-4 py-2.5" data-testid="listing-not-allowed">{q.data.notAllowedByPlanText}</p>
          )}
          <p className="mt-2 text-sm text-ink-soft" role="status" data-testid="listing-status">{q.data.statusText}</p>
          {q.data.checklist.length > 0 && (
            <ul className="mt-3 flex flex-col gap-1.5 text-sm" aria-label="Что нужно для показа в каталоге">
              {q.data.checklist.map((c) => (
                <li key={c.code} className="flex items-start gap-2" data-testid="listing-check" data-done={c.done ? 'true' : 'false'}>
                  <span aria-hidden="true" className={c.done ? 'text-success' : 'text-muted'}>{c.done ? '✓' : '○'}</span>
                  <span className={c.done ? 'text-ink-soft' : 'text-ink'}>
                    {c.text}
                    <span className="sr-only">{c.done ? ' — выполнено' : ' — не выполнено'}</span>
                  </span>
                </li>
              ))}
            </ul>
          )}
          {save.isError && <div className="mt-3"><InlineError>{getGoodsErrorMessage(save.error, 'Не удалось сохранить настройку каталога.')}</InlineError></div>}
        </>
      )}
    </Card>
  )
}
