import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { Pagination } from '@/components/ui/Pagination'
import { CatalogCard } from '@/components/landing/CatalogCard'
import { goodsCatalogApi } from '../api/goodsCatalog'
import { EmptyState, ErrorState, LoadingList } from '../components/StatePanels'
import { getGoodsErrorMessage } from '../utils/orderError'
import type { CatalogAcceptance, GoodsCatalogShopDto } from '../types'

const ACCEPT_TONE: Record<CatalogAcceptance, 'success' | 'info' | 'muted'> = {
  AcceptingNow: 'success',
  PreorderOnly: 'info',
  NotAccepting: 'muted',
}

/**
 * Каталог магазинов внутри рамки шаблона (ARCHITECTURE_CYCLE38.md §38.3.4): `/` — where to
 * order pickup. Which shops are visible, in which order, «открыто сейчас» and the acceptance wording are all the server's;
 * the client passes search/openNow/page and prints `path` as a relative link. The city is not offered (all shops are in Барнаул).
 */
export function ShopCatalog() {
  const [sp, setSp] = useSearchParams()
  const openNow = sp.get('openNow') === '1'
  const page = Math.max(1, Number(sp.get('page')) || 1)
  const urlSearch = sp.get('search') ?? ''
  const [searchDraft, setSearchDraft] = useState(urlSearch)

  useEffect(() => setSearchDraft(urlSearch), [urlSearch])

  const q = useQuery({
    queryKey: ['goods-catalog', openNow, urlSearch, page],
    queryFn: () => goodsCatalogApi.list({ openNow: openNow || undefined, search: urlSearch || undefined, page }),
    placeholderData: (prev) => prev,
    retry: false,
  })
  const data = q.data

  const patch = (next: Record<string, string | null>) => {
    const n = new URLSearchParams(sp)
    for (const [k, v] of Object.entries(next)) {
      if (v) n.set(k, v)
      else n.delete(k)
    }
    if (!('page' in next)) n.delete('page')
    setSp(n)
  }

  return (
    <>
      <form role="search" aria-label="Поиск магазина" onSubmit={(e) => { e.preventDefault(); patch({ search: searchDraft.trim() || null }) }} className="grid gap-4 md:grid-cols-[minmax(0,1fr)_auto] items-end">
        <div className="flex flex-col gap-1.5">
          <label htmlFor="catalog-search" className="text-[13px] font-medium text-[#4A4038]">Название или адрес</label>
          <div className="flex gap-2">
            <input
              id="catalog-search"
              type="search"
              value={searchDraft}
              maxLength={100}
              onChange={(e) => setSearchDraft(e.target.value)}
              className="flex-1 min-w-0 rounded-xl border border-line px-4 py-3 text-sm bg-white text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
            />
            <Button type="submit" variant="secondary"><Icon name="search" size={15} strokeWidth={1.8} />Найти</Button>
          </div>
        </div>
        <label className="flex items-center gap-2 min-h-[44px] cursor-pointer">
          <input type="checkbox" className="w-4 h-4 accent-[#2B2420]" checked={openNow} onChange={(e) => patch({ openNow: e.target.checked ? '1' : null })} />
          <span className="text-sm text-ink">Открыто сейчас</span>
        </label>
      </form>

      <section id="shop-list" tabIndex={-1} aria-label="Магазины" aria-live="polite" className="mt-8 scroll-mt-24 focus:outline-none">
        {q.isLoading ? (
          <LoadingList rows={4} rowClass="h-24" />
        ) : q.isError && !data ? (
          <ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить каталог магазинов.')} onRetry={() => void q.refetch()} />
        ) : data ? (
          data.items.length === 0 ? (
            <EmptyState title={data.emptyText ?? 'Пока нет магазинов на goods'} />
          ) : (
            <>
              <ul className="grid gap-6 list-none p-0" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))' }} data-testid="catalog-list">
                {data.items.map((s) => (
                  <ShopRow key={s.slug} shop={s} />
                ))}
              </ul>
              <Pagination page={data.page} pageSize={data.pageSize} total={data.totalCount} hasNext={data.page * data.pageSize < data.totalCount} onPageChange={(p) => patch({ page: p > 1 ? String(p) : null })} />
            </>
          )
        ) : null}
      </section>
    </>
  )
}

function ShopRow({ shop }: { shop: GoodsCatalogShopDto }) {
  return (
    <li>
      <CatalogCard
        to={shop.path}
        name={shop.name}
        logoUrl={shop.logoUrl}
        ariaLabel={`${shop.name}, ${shop.openState.text}, ${shop.acceptanceText}`}
        testId="catalog-shop"
        subtitle={shop.openState.text}
        place={shop.address}
        pill={{ text: shop.acceptanceText, tone: ACCEPT_TONE[shop.acceptance] ?? 'muted' }}
      />
    </li>
  )
}
