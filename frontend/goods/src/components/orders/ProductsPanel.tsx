import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { useOverlayDismiss } from '@/hooks/useOverlayDismiss'
import { catalogApi } from '../../api/catalog'
import { StockEditor } from '../catalog/StockEditor'
import { ErrorState, InlineError, LoadingList } from '../StatePanels'
import { filterProducts } from '../../utils/catalogGroups'
import { formatUnitPrice } from '../../utils/quantityFormat'
import { getCatalogErrorMessage } from '../../utils/catalogError'
import type { ProductDto } from '../../types'

/** US-23-16 — «закончилось» from the orders screen: product list with search, takes effect immediately. */
export function ProductsPanel({ shopId, trackStock, onClose }: { shopId: string; trackStock: boolean; onClose: () => void }) {
  const qc = useQueryClient()
  const dismiss = useOverlayDismiss(onClose)
  const key = ['shop-products', shopId]
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')
  const q = useQuery({ queryKey: key, queryFn: () => catalogApi.products(shopId) })
  const replace = (p: ProductDto) => qc.setQueryData<ProductDto[]>(key, (old) => old?.map((x) => (x.id === p.id ? p : x)) ?? old)
  const toggle = useMutation({
    mutationFn: (v: { p: ProductDto; value: boolean }) => catalogApi.setSoldOut(shopId, v.p.id, v.value),
    onMutate: () => setError(''),
    onSuccess: replace,
    onError: (e) => setError(getCatalogErrorMessage(e, 'Не удалось изменить отметку.')),
  })

  return (
    <div className="fixed inset-0 z-50 bg-ink/45 backdrop-blur-sm flex justify-end" {...dismiss}>
      <div role="dialog" aria-modal="true" aria-labelledby="products-panel-title" className="bg-cream w-full sm:max-w-[460px] h-full overflow-y-auto shadow-modal">
        <div className="flex items-center justify-between px-5 py-4 border-b border-line sticky top-0 bg-cream z-10">
          <h2 id="products-panel-title" className="font-serif text-xl text-ink">Товары</h2>
          <button onClick={onClose} aria-label="Закрыть" className="w-9 h-9 rounded-full flex items-center justify-center text-muted hover:text-ink hover:bg-cream-deep">
            <Icon name="x" size={18} strokeWidth={1.8} />
          </button>
        </div>
        <div className="p-5 flex flex-col gap-4">
          <input type="search" aria-label="Поиск товара" placeholder="Найти товар" value={search} onChange={(e) => setSearch(e.target.value)} className="w-full rounded-full border border-line bg-white px-4 py-2.5 text-sm outline-none focus:border-gold" />
          {error && <InlineError>{error}</InlineError>}
          {q.isLoading ? (
            <LoadingList rows={4} rowClass="h-16" />
          ) : q.isError ? (
            <ErrorState message={getCatalogErrorMessage(q.error, 'Не удалось загрузить товары.')} onRetry={() => void q.refetch()} />
          ) : (
            <ul className="flex flex-col gap-2">
              {filterProducts(q.data ?? [], search).map((p) => (
                <li key={p.id} className="rounded-xl border border-line bg-white p-3">
                  <div className="flex items-center justify-between gap-3">
                    <div className="min-w-0">
                      <p className="text-sm font-medium text-ink truncate">{p.name}</p>
                      <p className="text-xs text-muted">{formatUnitPrice(p.unit, p.price)}{p.isSoldOut ? ' · закончилось' : ''}</p>
                    </div>
                    <Button size="sm" variant={p.isSoldOut ? 'primary' : 'secondary'} aria-pressed={p.isSoldOut} loading={toggle.isPending && toggle.variables?.p.id === p.id} onClick={() => toggle.mutate({ p, value: !p.isSoldOut })}>
                      {p.isSoldOut ? 'Вернуть' : 'Закончилось'}
                    </Button>
                  </div>
                  {trackStock && <div className="mt-2"><StockEditor shopId={shopId} product={p} onChanged={replace} /></div>}
                </li>
              ))}
              {(q.data ?? []).length === 0 && <li className="text-sm text-muted py-6 text-center">В каталоге пока нет товаров.</li>}
            </ul>
          )}
        </div>
      </div>
    </div>
  )
}
