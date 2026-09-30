import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Pagination } from '@/components/ui/Pagination'
import { reportsApi } from '../../api/reports'
import { useShopContext } from '../../hooks/useShop'
import { EmptyState, ErrorState, LoadingList } from '../../components/StatePanels'
import { PeriodFilter } from '../../components/reports/PeriodFilter'
import { OrderHistoryTable } from '../../components/reports/OrderHistoryTable'
import { OrderDetailsModal } from '../../components/orders/OrderDetailsModal'
import { RadioChips } from '../../components/pickup/RadioChips'
import { getGoodsErrorMessage } from '../../utils/orderError'
import {
  DEFAULT_HISTORY_FILTERS,
  ORDER_STATUSES,
  buildHistoryParams,
  hasExtraFilters,
  parseHistoryParams,
  toHistoryQuery,
  type HistoryFilters,
} from '../../utils/reports'
import type { OrderHistoryRowDto, OrderHistorySort, OrderStatus } from '../../types'

interface HistoryState {
  customer?: string
}

/**
 * `/cabinet/:shopId/history` (US-25-03…05). Filters live in the address, EXCEPT the buyer (a name or a phone): it lives in
 * `location.state`, so «back» and a reload keep it, while the address, the access log and the browser history list never
 * see it (API_CONTRACT_CYCLE25.md §538). The request is a POST for the same reason.
 */
export function HistoryPage() {
  const { shop } = useShopContext()
  const [sp] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const filters = useMemo(() => parseHistoryParams(sp), [sp])
  const customer = ((location.state as HistoryState | null)?.customer ?? '').toString()

  const [customerDraft, setCustomerDraft] = useState(customer)
  const [amountFromDraft, setAmountFromDraft] = useState(filters.amountFrom)
  const [amountToDraft, setAmountToDraft] = useState(filters.amountTo)
  const [numberDraft, setNumberDraft] = useState(filters.number)
  const [opened, setOpened] = useState<OrderHistoryRowDto | null>(null)

  // «Back» to this screen with other params: keep the drafts in step with what is actually applied.
  useEffect(() => {
    setCustomerDraft(customer)
    setAmountFromDraft(filters.amountFrom)
    setAmountToDraft(filters.amountTo)
    setNumberDraft(filters.number)
  }, [customer, filters.amountFrom, filters.amountTo, filters.number])

  const apply = (next: HistoryFilters, nextCustomer: string = customer) => {
    navigate(
      { pathname: location.pathname, search: buildHistoryParams(next).toString() },
      { state: nextCustomer ? ({ customer: nextCustomer } satisfies HistoryState) : null },
    )
  }
  const change = (patch: Partial<HistoryFilters>) => apply({ ...filters, ...patch, page: patch.page ?? 1 })

  const query = toHistoryQuery(filters, customer)
  const q = useQuery({
    queryKey: ['order-history', shop.id, query],
    queryFn: () => reportsApi.history(shop.id, query),
    placeholderData: (prev) => prev,
    retry: false,
  })

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    apply({ ...filters, amountFrom: amountFromDraft, amountTo: amountToDraft, number: numberDraft, page: 1 }, customerDraft.trim())
  }
  const reset = () =>
    navigate(
      { pathname: location.pathname, search: buildHistoryParams({ ...DEFAULT_HISTORY_FILTERS, period: filters.period, from: filters.from, to: filters.to }).toString() },
      { state: null },
    )

  const toggleStatus = (s: OrderStatus) => {
    const has = filters.statuses.includes(s)
    change({ statuses: has ? filters.statuses.filter((x) => x !== s) : [...filters.statuses, s] })
  }

  const data = q.data
  const showReset = hasExtraFilters(filters, customer)

  return (
    <main className="max-w-[1180px] mx-auto px-4 sm:px-8 pt-8 pb-14">
      <h2 className="font-serif text-[28px] text-ink">История заказов</h2>
      <p className="text-sm text-ink-soft mt-1 mb-6">Заказы по дате получения. Полный телефон покупателя виден только в карточке заказа и карточке покупателя.</p>

      <form onSubmit={onSubmit} className="rounded-2xl border border-line bg-white p-5 flex flex-col gap-5" aria-label="Фильтры истории">
        <PeriodFilter period={filters.period} from={filters.from} to={filters.to} onChange={(p) => change(p)} />

        <fieldset>
          <legend className="text-[13px] font-medium text-[#4A4038] mb-2">Статусы (пусто — все)</legend>
          <div className="flex flex-wrap gap-2">
            {ORDER_STATUSES.map((s) => {
              const on = filters.statuses.includes(s.value)
              return (
                <label
                  key={s.value}
                  className={`inline-flex items-center gap-2 min-h-[44px] rounded-full border px-4 text-sm font-semibold cursor-pointer focus-within:ring-2 focus-within:ring-gold ${on ? 'bg-ink text-cream border-ink' : 'bg-white text-ink border-line hover:border-line-strong'}`}
                >
                  <input type="checkbox" className="sr-only" checked={on} onChange={() => toggleStatus(s.value)} />
                  {s.label}
                </label>
              )
            })}
          </div>
        </fieldset>

        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Input label="Покупатель: имя или 4+ цифры телефона" value={customerDraft} maxLength={100} onChange={(e) => setCustomerDraft(e.target.value)} autoComplete="off" />
          <Input label="Сумма от, ₽" inputMode="decimal" value={amountFromDraft} onChange={(e) => setAmountFromDraft(e.target.value)} />
          <Input label="Сумма до, ₽" inputMode="decimal" value={amountToDraft} onChange={(e) => setAmountToDraft(e.target.value)} />
          <Input label="Номер заказа" inputMode="numeric" value={numberDraft} onChange={(e) => setNumberDraft(e.target.value)} />
        </div>

        <div className="flex items-end justify-between gap-3 flex-wrap">
          <div>
            <p className="text-[13px] font-medium text-[#4A4038] mb-2">Порядок</p>
            <RadioChips<OrderHistorySort>
              label="Порядок по времени получения"
              value={filters.sort}
              onChange={(v) => change({ sort: v })}
              options={[
                { value: 'PickupDesc', label: 'Сначала новые' },
                { value: 'PickupAsc', label: 'Сначала старые' },
              ]}
            />
          </div>
          <div className="flex gap-2">
            {showReset && (
              <Button type="button" variant="ghost" onClick={reset}>
                Сбросить фильтры
              </Button>
            )}
            <Button type="submit">Показать</Button>
          </div>
        </div>
      </form>

      <section aria-live="polite" className="mt-6">
        {q.isLoading ? (
          <LoadingList rows={5} rowClass="h-12" />
        ) : q.isError && !data ? (
          <ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить историю заказов.')} onRetry={() => void q.refetch()} />
        ) : data ? (
          <>
            <p className="text-sm font-semibold text-ink mb-1" data-testid="history-summary">{data.summaryText}</p>
            <p className="text-xs text-muted mb-3">{data.period.label}</p>
            {q.isError && <ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось обновить историю.')} onRetry={() => void q.refetch()} />}
            {data.items.length === 0 ? (
              <EmptyState title={data.emptyText ?? 'Заказов нет'} action={showReset ? <Button variant="secondary" onClick={reset}>Сбросить фильтры</Button> : undefined} />
            ) : (
              <>
                <OrderHistoryTable shopId={shop.id} rows={data.items} caption={`История заказов: ${data.period.label}`} showCustomer onOpen={setOpened} />
                <Pagination page={data.page} pageSize={data.pageSize} total={data.totalCount} hasNext={data.page * data.pageSize < data.totalCount} onPageChange={(p) => change({ page: p })} />
              </>
            )}
          </>
        ) : null}
      </section>

      {opened && <OrderDetailsModal shopId={shop.id} orderId={opened.orderId} number={opened.number} onClose={() => setOpened(null)} />}
    </main>
  )
}
