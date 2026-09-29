import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { format } from 'date-fns'
import { ru } from 'date-fns/locale'
import { ordersApi } from '../api/orders'
import { OrderStatusBadge } from '../components/OrderStatusBadge'
import { EmptyState, ErrorState, LoadingList } from '../components/StatePanels'
import { formatMoney } from '../utils/quantityFormat'
import { getGoodsErrorMessage } from '../utils/orderError'

/** US-23-22 (P1) — active orders first, then finished ones from the last 30 days (server order). */
export function MyOrdersPage() {
  const { data, isLoading, isError, error, refetch } = useQuery({ queryKey: ['my-orders'], queryFn: ordersApi.my })

  return (
    <main className="max-w-[760px] mx-auto px-4 sm:px-8 pt-10">
      <h1 className="font-serif text-[32px] text-ink mb-2">Мои заказы</h1>
      <p className="text-sm text-ink-soft mb-7">
        Активные заказы и завершённые за последние 30 дней. Заказы, сделанные без входа в аккаунт, здесь не показываются —
        откройте их по сохранённой ссылке.
      </p>

      {isLoading ? (
        <LoadingList />
      ) : isError ? (
        <ErrorState message={getGoodsErrorMessage(error, 'Не удалось загрузить заказы.')} onRetry={() => void refetch()} />
      ) : !data || data.length === 0 ? (
        <EmptyState title="Заказов пока нет" text="Откройте магазин по ссылке или QR-коду и оформите первый заказ." />
      ) : (
        <ul className="flex flex-col gap-3">
          {data.map((o) => (
            <li key={o.token}>
              <Link
                to={new URL(o.orderUrl, window.location.origin).pathname}
                className="block rounded-2xl border border-line bg-white p-5 hover:border-line-strong transition-colors !text-ink"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-semibold text-ink truncate">
                      Заказ № {o.number} · {o.shopName}
                    </p>
                    <p className="text-xs text-muted mt-0.5">{format(new Date(o.createdAtUtc), 'd MMMM, HH:mm', { locale: ru })}</p>
                  </div>
                  <OrderStatusBadge status={o.status} text={o.statusText} />
                </div>
                <p className="mt-3 text-sm text-ink-soft">
                  Сумма: <span className="font-medium text-ink">{formatMoney(o.total, o.totalIsApproximate)}</span>
                </p>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </main>
  )
}
