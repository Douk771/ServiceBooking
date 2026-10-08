import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { formatRub } from '@/utils/money'
import { guestBookingsApi } from '../api/guestBookings'
import { StatusBadge } from '../components/StatusBadge'
import { EmptyState, ErrorState, LoadingList } from '../components/StatePanels'
import { LinkButton } from '../components/LinkButton'
import { formatStayRange } from '../utils/stayDates'
import { getStayErrorMessage } from '../utils/stayError'
import { isTerminal } from '../utils/stayStatus'

/** Path of a booking page from the absolute `bookingUrl`: same-origin only, otherwise the link is not followed (the token is the access). */
export function bookingPathOf(bookingUrl: string): string | null {
  try {
    const u = new URL(bookingUrl, window.location.origin)
    return u.origin === window.location.origin && u.pathname.startsWith('/b/') ? u.pathname : null
  } catch {
    return null
  }
}

/** `/bookings` (P1, US-37-22) — the bookings of the signed-in account: active first, then past ones of the last 12 months (server order). */
export function MyBookingsPage() {
  const q = useQuery({ queryKey: ['stays-my-bookings'], queryFn: guestBookingsApi.my })
  return (
    <main className="mx-auto max-w-[760px] px-4 pt-10 sm:px-8">
      <h1 className="mb-2 font-serif text-[32px] text-ink">Мои брони</h1>
      <p className="mb-7 text-sm text-ink-soft">
        Активные брони и прошедшие за последний год. Брони, сделанные без входа в аккаунт, здесь не показываются — откройте их по сохранённой ссылке.
      </p>
      {q.isLoading ? (
        <LoadingList />
      ) : q.isError ? (
        <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить брони.')} onRetry={() => void q.refetch()} />
      ) : !q.data || q.data.length === 0 ? (
        <EmptyState title="Броней пока нет" text="Выберите дом в каталоге и забронируйте даты." action={<LinkButton to="/">К каталогу</LinkButton>} />
      ) : (
        <ul className="flex flex-col gap-3">
          {q.data.map((b) => {
            const path = bookingPathOf(b.bookingUrl)
            const body = (
              <>
                <div className="min-w-0">
                  <p className="font-semibold text-ink">{b.houseName}</p>
                  <p className="text-xs text-muted">{b.companyName}</p>
                  <p className="mt-1 text-sm text-ink-soft">{formatStayRange(b.checkInDate, b.checkOutDate)}</p>
                </div>
                <div className="flex shrink-0 flex-col items-end gap-1.5">
                  <StatusBadge status={b.displayStatus} text={b.statusText} />
                  <p className="text-sm font-semibold text-ink">{formatRub(b.totalRub)}</p>
                </div>
              </>
            )
            const cls = `flex flex-wrap items-start justify-between gap-3 rounded-2xl border border-line bg-white p-4 sm:p-5 ${isTerminal(b.displayStatus) ? 'opacity-80' : ''}`
            return (
              <li key={b.bookingUrl}>
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
