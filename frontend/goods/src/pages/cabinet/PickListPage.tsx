import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { Input } from '@/components/ui/Input'
import { reportsApi } from '../../api/reports'
import { useShopContext } from '../../hooks/useShop'
import { EmptyState, ErrorState, LoadingList } from '../../components/StatePanels'
import { RadioChips } from '../../components/pickup/RadioChips'
import { getGoodsErrorMessage } from '../../utils/orderError'
import { buildPickListParams, intervalKey, parsePickListParams, type PickListFilters } from '../../utils/reports'
import type { PickListDto, PickListOrderDto } from '../../types'
import '../../styles/print.css'

type View = 'product' | 'time'
const REFRESH_MS = 60_000

/**
 * `/cabinet/:shopId/picklist` (US-25-08) — what to pick for a date and a time window. Grouping and totals are the server's;
 * the customer's name and phone are not in the response at all [legal L18]. The screen refreshes itself every minute, but
 * only while the tab is visible (§538). «Печать» prints a black-and-white sheet: see styles/print.css.
 */
export function PickListPage() {
  const { shop } = useShopContext()
  const [sp, setSp] = useSearchParams()
  const filters = useMemo(() => parsePickListParams(sp), [sp])
  const [view, setView] = useState<View>('product')
  const [customOpen, setCustomOpen] = useState(false)
  const [customFrom, setCustomFrom] = useState(filters.from)
  const [customTo, setCustomTo] = useState(filters.to)

  const q = useQuery({
    queryKey: ['picklist', shop.id, filters],
    queryFn: () => reportsApi.pickList(shop.id, { date: filters.date || undefined, from: filters.from || undefined, to: filters.to || undefined, includeNew: filters.includeNew }),
    placeholderData: (prev) => prev,
    retry: false,
    refetchInterval: () => (typeof document === 'undefined' || document.visibilityState === 'visible' ? REFRESH_MS : false),
    refetchIntervalInBackground: false,
  })

  // A tab that was hidden for a while comes back with stale data: refresh once when it becomes visible again.
  useEffect(() => {
    const onVisible = () => {
      if (document.visibilityState === 'visible') void q.refetch()
    }
    document.addEventListener('visibilitychange', onVisible)
    return () => document.removeEventListener('visibilitychange', onVisible)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const data = q.data
  const change = (patch: Partial<PickListFilters>) => setSp(buildPickListParams({ ...filters, ...patch }))
  const slots = data?.slots ?? []
  const selected = intervalKey(filters, slots)
  const showCustom = customOpen || selected === 'custom'

  const onInterval = (v: string) => {
    if (v === '') {
      setCustomOpen(false)
      change({ from: '', to: '' })
    } else if (v === 'custom') {
      setCustomOpen(true)
    } else {
      setCustomOpen(false)
      const [from, to] = v.split('|')
      change({ from, to })
    }
  }

  return (
    <main className="picklist-root max-w-[1080px] mx-auto px-4 sm:px-8 pt-8 pb-14">
      <div className="no-print">
        <h2 className="font-serif text-[28px] text-ink">Лист сборки</h2>
        <p className="text-sm text-ink-soft mt-1 mb-6">Что собрать на выбранную дату и время. В листе только принятые заказы и, если включено, новые.</p>

        <div className="rounded-2xl border border-line bg-white p-5 flex flex-col gap-4">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-[auto_1fr]">
            <Input label="Дата" type="date" value={filters.date || data?.date || ''} onChange={(e) => change({ date: e.target.value, from: '', to: '' })} />
            <div className="flex flex-col gap-1.5">
              <label htmlFor="picklist-interval" className="text-[13px] font-medium text-[#4A4038]">Интервал</label>
              <select
                id="picklist-interval"
                value={showCustom ? 'custom' : selected}
                onChange={(e) => onInterval(e.target.value)}
                className="rounded-xl border border-line px-4 py-3 text-sm bg-white text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
              >
                <option value="">Весь день</option>
                {slots.map((s) => (
                  <option key={`${s.from}|${s.to}`} value={`${s.from}|${s.to}`}>{s.label}</option>
                ))}
                <option value="custom">Свой интервал…</option>
              </select>
            </div>
          </div>

          {showCustom && (
            <div className="flex items-end gap-3 flex-wrap">
              <Input label="С" type="time" value={customFrom} onChange={(e) => setCustomFrom(e.target.value)} />
              <Input label="По" type="time" value={customTo} onChange={(e) => setCustomTo(e.target.value)} />
              <Button type="button" variant="secondary" disabled={!customFrom || !customTo} onClick={() => change({ from: customFrom, to: customTo })}>
                Применить
              </Button>
            </div>
          )}

          <label className="flex items-center gap-2 min-h-[44px] cursor-pointer">
            <input type="checkbox" className="w-4 h-4 accent-[#2B2420]" checked={filters.includeNew} onChange={(e) => change({ includeNew: e.target.checked })} />
            <span className="text-sm text-ink">Включая непринятые (статус «Новый»)</span>
          </label>

          <div className="flex items-center justify-between gap-3 flex-wrap">
            <RadioChips<View>
              label="Вид листа"
              value={view}
              onChange={setView}
              options={[
                { value: 'product', label: 'По товарам' },
                { value: 'time', label: 'По времени' },
              ]}
            />
            <div className="flex gap-2">
              <Button type="button" variant="secondary" loading={q.isFetching && !q.isLoading} onClick={() => void q.refetch()}>
                Обновить
              </Button>
              <Button type="button" onClick={() => window.print()} disabled={!data}>
                <Icon name="printer" size={15} strokeWidth={1.8} />
                Печать
              </Button>
            </div>
          </div>
        </div>
      </div>

      <section aria-live="polite" className="mt-6">
        {q.isLoading ? (
          <LoadingList rows={4} rowClass="h-16" />
        ) : q.isError && !data ? (
          <div className="no-print">
            <ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить лист сборки.')} onRetry={() => void q.refetch()} />
          </div>
        ) : data ? (
          <PickListBody data={data} view={view} refreshFailed={q.isError} onRetry={() => void q.refetch()} />
        ) : null}
      </section>
    </main>
  )
}

function PickListBody({ data, view, refreshFailed, onRetry }: { data: PickListDto; view: View; refreshFailed: boolean; onRetry: () => void }) {
  return (
    <>
      <header className="mb-4">
        <h3 className="picklist-title font-serif text-2xl text-ink" data-testid="picklist-title">
          {data.shopName} · {data.dateLabel} · {data.intervalLabel}
        </h3>
        <p className="text-sm text-ink-soft" data-testid="picklist-generated">{data.generatedAtText} · заказов: {data.orderCount}</p>
      </header>
      {refreshFailed && (
        <div className="no-print mb-3">
          <ErrorState message="Не удалось обновить лист — на экране прежние данные." onRetry={onRetry} />
        </div>
      )}
      {data.orderCount === 0 ? (
        <EmptyState title={data.emptyText ?? 'Заказов нет'} />
      ) : view === 'product' ? (
        <div className="overflow-x-auto rounded-2xl border border-line bg-white">
          <table className="w-full text-sm text-left">
            <caption className="sr-only">Что собрать: сумма по товарам</caption>
            <thead className="bg-cream-deep/60 text-xs uppercase tracking-wide text-muted">
              <tr>
                <th scope="col" className="px-4 py-3 font-semibold">Товар</th>
                <th scope="col" className="px-4 py-3 font-semibold text-right">Всего</th>
                <th scope="col" className="px-4 py-3 font-semibold">Заказов</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {data.byProduct.map((p, i) => (
                <tr key={`${p.productId ?? 'x'}-${i}`}>
                  <th scope="row" className="px-4 py-3 font-medium text-ink">
                    {p.name}
                    {p.categoryName && <span className="block text-xs font-normal text-muted">{p.categoryName}</span>}
                  </th>
                  <td className="px-4 py-3 text-right font-semibold text-ink whitespace-nowrap">{p.quantityText}</td>
                  <td className="px-4 py-3 text-ink-soft">
                    {p.orderCount}
                    {p.weightBreakdownText && <span className="block text-xs text-muted">{p.weightBreakdownText}</span>}
                    {p.hasUnaccepted && <span className="block text-xs font-semibold">Есть непринятые заказы</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="flex flex-col gap-5">
          {data.byTime.map((g) => (
            <section key={g.label} className="picklist-group" aria-label={g.label}>
              <h4 className="font-serif text-xl text-ink border-b border-line pb-1 mb-2">{g.label}</h4>
              <ul className="flex flex-col gap-3">
                {g.orders.map((o) => (
                  <PickOrder key={o.orderId} order={o} />
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </>
  )
}

function PickOrder({ order }: { order: PickListOrderDto }) {
  return (
    <li className="rounded-xl border border-line bg-white px-4 py-3 picklist-group">
      <p className="text-sm font-semibold text-ink">
        № {order.number} · {order.pickupText}
        {order.isUnaccepted && <span className="ml-2 text-xs font-bold uppercase border border-current rounded-full px-2 py-0.5">Не принят</span>}
      </p>
      <ul className="mt-1 text-sm text-ink-soft">
        {order.lines.map((l, i) => (
          <li key={i}>
            {l.name} — <span className="font-medium text-ink">{l.quantityText}</span>
            {l.portionText ? ` (${l.portionText})` : ''}
          </li>
        ))}
      </ul>
      {order.comment && <p className="mt-1 text-sm text-ink">Комментарий: {order.comment}</p>}
    </li>
  )
}
