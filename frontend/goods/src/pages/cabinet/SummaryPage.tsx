import { useMemo } from 'react'
import { Navigate, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { formatRub } from '@/utils/money'
import { reportsApi } from '../../api/reports'
import { useShopContext } from '../../hooks/useShop'
import { EmptyState, ErrorState, LoadingList } from '../../components/StatePanels'
import { PeriodFilter } from '../../components/reports/PeriodFilter'
import { RadioChips } from '../../components/pickup/RadioChips'
import { getGoodsErrorMessage } from '../../utils/orderError'
import { buildSummaryParams, parseSummaryParams, type SummaryFilters } from '../../utils/reports'
import type { ShopSummaryDto, SummaryShareDto, SummaryTopSort } from '../../types'

/**
 * `/cabinet/:shopId/summary` (US-25-06/07) — owner and SuperAdmin only; staff get 403 from the API and never see the tab.
 * Every figure, share, average and «+12 %» is the server's text (API_CONTRACT_CYCLE25.md §527, §538): the client only lays it out.
 */
export function SummaryPage() {
  const { shop, isOwner } = useShopContext()
  const [sp, setSp] = useSearchParams()
  const filters = useMemo(() => parseSummaryParams(sp), [sp])
  const q = useQuery({
    queryKey: ['shop-summary', shop.id, filters],
    queryFn: () => reportsApi.summary(shop.id, { period: filters.period, from: filters.from || undefined, to: filters.to || undefined, top: filters.top, compare: true }),
    enabled: isOwner,
    placeholderData: (prev) => prev,
    retry: false,
  })

  if (!isOwner) return <Navigate to={`/cabinet/${shop.id}/orders`} replace />

  const change = (patch: Partial<SummaryFilters>) => setSp(buildSummaryParams({ ...filters, ...patch }))
  const data = q.data

  return (
    <main className="max-w-[1080px] mx-auto px-4 sm:px-8 pt-8 pb-14">
      <h2 className="font-serif text-[28px] text-ink">Сводка</h2>
      <p className="text-sm text-ink-soft mt-1 mb-6">Заказы по дате получения. Оплата принимается на месте — платформа её не видит.</p>

      <div className="rounded-2xl border border-line bg-white p-5">
        <PeriodFilter period={filters.period} from={filters.from} to={filters.to} onChange={(p) => change(p)} />
      </div>

      <section aria-live="polite" className="mt-6">
        {q.isLoading ? (
          <LoadingList rows={4} rowClass="h-24" />
        ) : q.isError && !data ? (
          <ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить сводку.')} onRetry={() => void q.refetch()} />
        ) : data ? (
          <SummaryBody data={data} top={filters.top} onTop={(t) => change({ top: t })} />
        ) : null}
      </section>
    </main>
  )
}

function Delta({ text }: { text?: string | null }) {
  if (!text) return null
  return <span className="ml-2 text-xs font-semibold text-ink-soft" data-testid="delta">{text} к прошлому периоду</span>
}

function SummaryBody({ data, top, onTop }: { data: ShopSummaryDto; top: SummaryTopSort; onTop: (t: SummaryTopSort) => void }) {
  const prev = data.previous
  return (
    <>
      <p className="text-sm font-semibold text-ink mb-3" data-testid="summary-period">{data.period.label}</p>
      {data.ordersTotal === 0 ? (
        <EmptyState title="За этот период заказов нет" text="Выберите другой период — показатели появятся, когда в нём будут заказы." />
      ) : (
        <div className="grid gap-4 md:grid-cols-[1.4fr_1fr]">
          <div className="rounded-3xl bg-ink text-cream p-7">
            <p className="text-xs uppercase tracking-wider text-cream/70">Выдано на сумму</p>
            <p className="font-serif text-[44px] leading-none mt-2" data-testid="issued-amount">{formatRub(data.issuedAmount)}</p>
            <p className="mt-2 text-sm text-cream/80">
              {data.issuedCount} выдано
              <Delta text={prev?.issuedAmountDeltaText} />
            </p>
            <p className="mt-4 text-xs text-cream/70">{data.paymentNote}</p>
          </div>
          <dl className="rounded-3xl border border-line bg-white p-6 flex flex-col gap-4">
            <div>
              <dt className="text-xs uppercase tracking-wider text-muted">Заказов за период</dt>
              <dd className="font-serif text-3xl text-ink" data-testid="orders-total">
                {data.ordersTotal}
                <Delta text={prev?.ordersDeltaText} />
              </dd>
              <dd className="text-xs text-muted">{data.inProgressText}</dd>
            </div>
            <div>
              <dt className="text-xs uppercase tracking-wider text-muted">Средний чек</dt>
              <dd className="font-serif text-2xl text-ink">
                {data.averageCheckText}
                <Delta text={prev?.averageCheckDeltaText} />
              </dd>
            </div>
          </dl>
        </div>
      )}

      {data.ordersTotal > 0 && (
        <>
          <section aria-labelledby="sum-cancel" className="mt-8">
            <h3 id="sum-cancel" className="font-serif text-xl text-ink">Отмены и невыдачи</h3>
            <p className="text-xs text-muted mb-3">Доля — от заказов периода в конечных статусах: {data.terminalCount}.</p>
            <ul className="divide-y divide-line rounded-2xl border border-line bg-white">
              {[...data.cancellations, data.notPickedUp].map((s: SummaryShareDto) => (
                <li key={s.status} className="flex items-center justify-between gap-4 px-5 py-3 text-sm" data-testid="share-row">
                  <span className="text-ink">{s.label}</span>
                  <span className="text-ink-soft">
                    {s.count} · <span className="font-semibold text-ink">{s.shareText}</span>
                  </span>
                </li>
              ))}
            </ul>
          </section>

          <section aria-labelledby="sum-top" className="mt-8">
            <div className="flex items-end justify-between gap-3 flex-wrap mb-3">
              <h3 id="sum-top" className="font-serif text-xl text-ink">Топ товаров</h3>
              <RadioChips<SummaryTopSort>
                label="Сортировка топа"
                value={top}
                onChange={onTop}
                options={[
                  { value: 'Amount', label: 'По сумме' },
                  { value: 'Quantity', label: 'По количеству' },
                ]}
              />
            </div>
            {data.top.items.length === 0 ? (
              <p className="text-sm text-muted">Выданных заказов за период нет — топ пуст.</p>
            ) : (
              <div className="overflow-x-auto rounded-2xl border border-line bg-white">
                <table className="w-full text-sm text-left">
                  <caption className="sr-only">Топ товаров по выданным заказам</caption>
                  <thead className="bg-cream-deep/60 text-xs uppercase tracking-wide text-muted">
                    <tr>
                      <th scope="col" className="px-4 py-3 font-semibold">Товар</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-right">Количество</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-right">Сумма</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-line">
                    {data.top.items.map((it, i) => (
                      <tr key={`${it.productId ?? 'deleted'}-${i}`}>
                        <th scope="row" className="px-4 py-3 font-medium text-ink">
                          {it.name}
                          {it.isDeleted && <span className="ml-2 text-xs font-normal text-muted">товар удалён</span>}
                        </th>
                        <td className="px-4 py-3 text-right text-ink-soft whitespace-nowrap">{it.quantityText}</td>
                        <td className="px-4 py-3 text-right text-ink whitespace-nowrap">{formatRub(it.amount)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          {data.days && data.days.length > 0 && (
            <section aria-labelledby="sum-days" className="mt-8">
              <h3 id="sum-days" className="font-serif text-xl text-ink mb-3">По дням</h3>
              <div className="overflow-x-auto rounded-2xl border border-line bg-white">
                <table className="w-full text-sm text-left">
                  <caption className="sr-only">Заказы по дням периода</caption>
                  <thead className="bg-cream-deep/60 text-xs uppercase tracking-wide text-muted">
                    <tr>
                      <th scope="col" className="px-4 py-3 font-semibold">День</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-right">Заказов</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-right">Выдано</th>
                      <th scope="col" className="px-4 py-3 font-semibold text-right">На сумму</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-line">
                    {data.days.map((d) => (
                      <tr key={d.date}>
                        <th scope="row" className="px-4 py-3 font-medium text-ink">{d.label}</th>
                        <td className="px-4 py-3 text-right text-ink-soft">{d.orders}</td>
                        <td className="px-4 py-3 text-right text-ink-soft">{d.issuedCount}</td>
                        <td className="px-4 py-3 text-right text-ink whitespace-nowrap">{formatRub(d.issuedAmount)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
          {prev && <p className="mt-4 text-xs text-muted">Сравнение — с периодом «{prev.period.label}».</p>}
        </>
      )}
    </>
  )
}
