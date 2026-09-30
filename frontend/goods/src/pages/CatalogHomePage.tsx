import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { CityCombobox } from '@/components/ui/CityCombobox'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { Pagination } from '@/components/ui/Pagination'
import type { City } from '@/types'
import { goodsCatalogApi } from '../api/goodsCatalog'
import { BusinessBlock } from '../components/BusinessBlock'
import { EmptyState, ErrorState, LoadingList } from '../components/StatePanels'
import { publicAddress } from '@/utils/publicAddress'
import { getGoodsErrorMessage } from '../utils/orderError'
import { readStoredCity, writeStoredCity, parseCityId } from '../utils/homeCity'
import type { CatalogAcceptance, GoodsCatalogShopDto } from '../types'

const ACCEPT_STYLE: Record<CatalogAcceptance, string> = {
  AcceptingNow: 'bg-success-bg text-success',
  PreorderOnly: 'bg-info-bg text-info',
  NotAccepting: 'bg-cream-deep text-muted',
}

/**
 * `/` and `/city/:cityId` (US-25-13/14) — where to order pickup. Which shops are visible, in which order, «открыто сейчас» and
 * the acceptance wording are all the server's; the client passes city/search/openNow/page and prints `path` as a relative link.
 */
export function CatalogHomePage() {
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
    <main className="max-w-[1180px] mx-auto px-4 sm:px-8 pt-10 md:pt-16 pb-10">
      <div className="max-w-[720px]">
        <p className="text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark mb-4">Заказы с самовывозом</p>
        <h1 className="font-serif text-[36px] sm:text-[48px] leading-[1.08] font-medium text-ink">Где заказать в вашем городе</h1>
        <p className="mt-4 text-[16px] text-ink-soft">Выберите магазин, соберите заказ и заберите его в удобное время.</p>
      </div>

      <form role="search" aria-label="Поиск магазина" onSubmit={(e) => { e.preventDefault(); patch({ search: searchDraft.trim() || null }) }} className="mt-8 grid gap-4 md:grid-cols-[minmax(0,320px)_minmax(0,1fr)_auto] items-end">
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

      <section aria-label="Магазины" aria-live="polite" className="mt-8">
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

      <BusinessBlock />
    </main>
  )
}

function ShopRow({ shop, showCity }: { shop: GoodsCatalogShopDto; showCity: boolean }) {
  const place = publicAddress(showCity ? shop.cityName : null, shop.address)
  return (
    <li>
      <Link
        to={shop.path}
        aria-label={`${shop.name}, ${shop.openState.text}, ${shop.acceptanceText}`}
        className="block h-full bg-white border border-line rounded-[20px] p-[26px] transition-all duration-200 hover:shadow-card hover:-translate-y-[3px] hover:border-line-strong"
        data-testid="catalog-shop"
      >
        <div className="flex items-start gap-4 mb-4">
          <div className="w-14 h-14 rounded-2xl bg-cream-deep flex items-center justify-center shrink-0">
            <Icon name="store" size={24} className="text-gold-dark" />
          </div>
          <div className="min-w-0">
            <h3 className="font-serif text-[19px] font-medium text-ink truncate">{shop.name}</h3>
            <p className="text-sm text-muted mt-0.5">{shop.openState.text}</p>
          </div>
        </div>
        <div className="flex items-center justify-between gap-3 pt-3.5 border-t border-cream-deep">
          <div className="flex items-center gap-1.5 text-[13px] text-ink-soft min-w-0">
            {place && (
              <>
                <Icon name="map-pin" size={14} strokeWidth={1.6} className="shrink-0" />
                <span className="truncate">{place}</span>
              </>
            )}
          </div>
          <span className={`shrink-0 text-xs font-semibold px-2.5 py-1 rounded-full ${ACCEPT_STYLE[shop.acceptance] ?? 'bg-cream-deep text-muted'}`}>{shop.acceptanceText}</span>
        </div>
      </Link>
    </li>
  )
}
