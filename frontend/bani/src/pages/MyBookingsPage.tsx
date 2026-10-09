import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { formatRub } from '@/utils/money'
import { StatusBadge } from '@/components/slots/ui/StatusBadge'
import { EmptyState, ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { LinkButton } from '@/components/slots/ui/LinkButton'
import { getStayErrorMessage } from '@/utils/slots/slotError'
import { isTerminal } from '@/utils/slots/slotStatus'
import { bathsOrdersApi } from '../api/bathsOrders'
import { orderPathOf } from '../utils/myBookings'

/** `/bookings` (P1, US-42-17) — active bookings first, then the past ones of the last 12 months (the server's order). */
export function MyBookingsPage() {
  const q = useQuery({ queryKey: ['baths-my-orders'], queryFn: bathsOrdersApi.my })
  const items = q.data?.items ?? []
  return (
    <main className="mx-auto max-w-[760px] px-4 pt-10 sm:px-8">
      <h1 className="mb-2 font-serif text-[32px] text-ink">Мои брони</h1>
      <p className="mb-7 text-sm text-ink-soft">
        Брони вашего аккаунта и брони на подтверждённый номер телефона: активные и прошедшие за последний год. Остальные откройте по сохранённой
        ссылке.
      </p>
      {q.isLoading ? (
        <LoadingList />
      ) : q.isError ? (
        <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить брони.')} onRetry={() => void q.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState title="Броней пока нет" text="Выберите баню в каталоге и выберите время сеанса." action={<LinkButton to="/">К каталогу</LinkButton>} />
      ) : (
        <ul className="flex flex-col gap-3" aria-label="Список броней">
          {items.map((b) => {
            const path = orderPathOf(b.orderUrl)
            const body = (
              <>
                <div className="min-w-0">
                  <p className="font-semibold text-ink">{b.resourceName}</p>
                  <p className="text-xs text-muted">{b.companyName}</p>
                  <p className="mt-1 text-sm text-ink-soft">{b.timeLabel}</p>
                  {b.localTimeNote && <p className="text-xs text-muted">{b.localTimeNote}</p>}
                </div>
                <div className="flex shrink-0 flex-col items-end gap-1.5">
                  <StatusBadge status={b.displayStatus} text={b.statusText} />
                  <p className="text-sm font-semibold text-ink">{formatRub(b.totalRub)}</p>
                </div>
              </>
            )
            const cls = `flex flex-wrap items-start justify-between gap-3 rounded-2xl border border-line bg-white p-4 sm:p-5 ${
              !b.isActive || isTerminal(b.displayStatus) ? 'opacity-80' : ''
            }`
            return (
              <li key={b.orderUrl}>
                {path ? (
                  <Link to={path} className={`${cls} !text-ink hover:border-line-strong`}>
                    {body}
                  </Link>
                ) : (
                  <div className={cls}>{body}</div>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </main>
  )
}
