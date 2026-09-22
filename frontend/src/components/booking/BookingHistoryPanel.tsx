import { useQuery } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../../api/bookings'
import { Icon } from '../ui/Icon'

interface Props {
  bookingId: string
}

/**
 * ARCHITECTURE_CYCLE10.md §109.1 / API_CONTRACT_CYCLE10.md §122 — the booking's change journal,
 * embedded only in `MyBookingsPage.tsx` (staff). The caller decides WHETHER to render this at all
 * (via `booking.historyEventCount > 0`, §123) and mounts it only on expand — the query below is
 * `enabled: true` the moment this component exists, so it must not be rendered speculatively.
 *
 * Server order is ascending `occurredAt` (§122.1); this panel shows newest first — the one
 * documented, product-wide convention for "which end is first" (§109.1).
 */
export function BookingHistoryPanel({ bookingId }: Props) {
  const { data, isLoading, error } = useQuery({
    queryKey: ['booking-history', bookingId],
    queryFn: () => bookingsApi.getHistory(bookingId),
  })

  if (isLoading) {
    return (
      <div className="mt-3 flex flex-col gap-2">
        {[1, 2].map((i) => (
          <div key={i} className="h-12 bg-cream-deep rounded-xl animate-pulse" />
        ))}
      </div>
    )
  }

  if (error) {
    return <p className="mt-3 text-sm text-danger">Не удалось загрузить историю записи. Попробуйте позже.</p>
  }

  if (!data) return null

  const events = [...data.events].sort((a, b) => b.occurredAt.localeCompare(a.occurredAt))

  return (
    <div className="mt-3 flex flex-col gap-2">
      {/* §122.2 — only shown when there IS history to show alongside it; a booking with zero events
          isn't rendered by the caller at all, so this line never appears next to an empty list. */}
      {data.precedesJournal && (
        <p className="text-xs text-ink-soft bg-cream-deep rounded-lg px-3 py-2">
          Журнал ведётся с определённого момента — более ранние действия по этой записи в нём не
          отражены.
        </p>
      )}
      {events.map((ev) => (
        <div key={ev.id} className="flex gap-2.5 rounded-xl border border-line bg-white px-3.5 py-2.5">
          <Icon name="clock" size={14} strokeWidth={1.8} className="text-muted shrink-0 mt-0.5" />
          <div className="min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="text-[13px] font-medium text-ink">{ev.title}</span>
              <span className="text-[11px] text-muted">
                {format(parseISO(ev.occurredAt), 'd MMM, HH:mm', { locale: ru })}
              </span>
            </div>
            <p className="text-xs text-ink-soft mt-0.5">{ev.actor.label}</p>
            {ev.reschedule && (
              <p className="text-xs text-ink-soft mt-0.5">
                {ev.reschedule.fromDate} {ev.reschedule.fromStartTime.slice(0, 5)} →{' '}
                {ev.reschedule.toDate} {ev.reschedule.toStartTime.slice(0, 5)}
              </p>
            )}
            {ev.cancellationReason && (
              <p className="text-xs text-danger mt-0.5">Причина: {ev.cancellationReason}</p>
            )}
          </div>
        </div>
      ))}
    </div>
  )
}
