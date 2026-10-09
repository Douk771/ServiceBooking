import { useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { CityCombobox } from '@/components/ui/CityCombobox'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { Pagination } from '@/components/ui/Pagination'
import { CatalogCard } from '@/components/landing/CatalogCard'
import type { City } from '@/types'
import { goodsCatalogApi } from '../api/goodsCatalog'
import { EmptyState, ErrorState, LoadingList } from '../components/StatePanels'
import { publicAddress } from '@/utils/publicAddress'
import { getGoodsErrorMessage } from '../utils/orderError'
import { readStoredCity, writeStoredCity, parseCityId } from '../utils/homeCity'
import type { CatalogAcceptance, GoodsCatalogShopDto } from '../types'

const ACCEPT_TONE: Record<CatalogAcceptance, 'success' | 'info' | 'muted'> = {
  AcceptingNow: 'success',
  PreorderOnly: 'info',
  NotAccepting: 'muted',
}

/**
 * Каталог магазинов внутри рамки шаблона (ARCHITECTURE_CYCLE38.md §38.3.4): `/` и `/city/:cityId` (US-25-13/14) — where to
 * order pickup. Which shops are visible, in which order, «открыто сейчас» and the acceptance wording are all the server's;
 * the client passes city/search/openNow/page and prints `path` as a relative link.
 */
export function ShopCatalog() {
  const params = useParams<{ cityId?: string }>()
  const navigate = useNavigate()
  const [sp, setSp] = useSearchParams()
  const routeCityId = parseCityId(params.cityId)
  const [stored, setStored] = useState<City | null>(() => readStoredCity())
  const cityId = params.cityId !== undefined ? routeCityId : stored?.id
  const openNow = sp.get('openNow') === '1'
  const page = Math.max(1, Number(sp.get('page')) || 1)
  const urlSearch = sp.get('search') ?? ''
  const [searchDraft, setSearchDraft] = useState(urlSearch)
  const [copied, setCopied] = useState(false)

  useEffect(() => setSearchDraft(urlSearch), [urlSearch])

  const q = useQuery({
    queryKey: ['goods-catalog', cityId ?? null, openNow, urlSearch, page],
    queryFn: () => goodsCatalogApi.list({ cityId, openNow: openNow || undefined, search: urlSearch || undefined, page }),
    placeholderData: (prev) => prev,
    retry: false,
  })
  const data = q.data
  const city: City | null = data?.city
    ? { id: data.city.id, name: data.city.name, region: data.city.region, label: data.city.label, timeZoneId: '', utcOffsetMinutes: 0 }
    : cityId !== undefined && stored?.id === cityId
      ? stored
      : null

  const patch = (next: Record<string, string | null>) => {
    const n = new URLSearchParams(sp)
    for (const [k, v] of Object.entries(next)) {
      if (v) n.set(k, v)
      else n.delete(k)
    }
    if (!('page' in next)) n.delete('page')
    setSp(n)
  }

  const pickCity = (c: City | null) => {
    setStored(c)
    writeStoredCity(c)
    navigate(c ? `/city/${c.id}` : '/')
  }
  const share = async () => {
    if (cityId === undefined) return
    try {
      await navigator.clipboard.writeText(`${window.location.origin}/city/${cityId}`)
      setCopied(true)
    } catch {
      /* the address bar carries the same link */
    }
  }

  return (
    <>
      <form role="search" aria-label="Поиск магазина" onSubmit={(e) => { e.preventDefault(); patch({ search: searchDraft.trim() || null }) }} className="grid gap-4 md:grid-cols-[minmax(0,320px)_minmax(0,1fr)_auto] items-end">
        <CityCombobox label="Город" value={city} onChange={pickCity} placeholder="Все города" />
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

      {cityId !== undefined && (
        <p className="mt-3 text-xs text-muted">
          <button type="button" onClick={() => void share()} className="underline underline-offset-2 hover:no-underline min-h-[36px]">Поделиться каталогом города</button>
          {copied && <span role="status" className="ml-2 text-success font-medium">Ссылка скопирована</span>}
          {city === null && <span className="ml-2">Город не найден в каталоге.</span>}
        </p>
      )}

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
                  <ShopRow key={s.slug} shop={s} showCity={cityId === undefined} />
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

function ShopRow({ shop, showCity }: { shop: GoodsCatalogShopDto; showCity: boolean }) {
  return (
    <li>
      <CatalogCard
        to={shop.path}
        name={shop.name}
        logoUrl={shop.logoUrl}
        ariaLabel={`${shop.name}, ${shop.openState.text}, ${shop.acceptanceText}`}
        testId="catalog-shop"
        subtitle={shop.openState.text}
        place={publicAddress(showCity ? shop.cityName : null, shop.address)}
        pill={{ text: shop.acceptanceText, tone: ACCEPT_TONE[shop.acceptance] ?? 'muted' }}
      />
    </li>
  )
}
