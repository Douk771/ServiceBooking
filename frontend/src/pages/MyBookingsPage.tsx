import { useState, useMemo } from 'react'
import { useQuery, useQueries, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO, isToday, isTomorrow, addDays } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../api/bookings'
import { reviewsApi } from '../api/reviews'
import { mastersApi, type MasterClient } from '../api/masters'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { StatusBadge } from '../components/ui/Badge'
import { Icon } from '../components/ui/Icon'
import { ManualBookingModal } from '../components/booking/ManualBookingModal'
import { RescheduleModal } from '../components/booking/RescheduleModal'
import { ReviewModal } from '../components/review/ReviewModal'
import type { Booking } from '../types'

const STATUS_LABELS: Record<string, string> = {
  Pending: 'Ожидает',
  Confirmed: 'Подтверждена',
  Completed: 'Выполнена',
  Cancelled: 'Отменена',
  NoShow: 'Не пришёл',
}

function dayLabel(dateStr: string) {
  const d = parseISO(dateStr)
  if (isToday(d)) return 'Сегодня'
  if (isTomorrow(d)) return 'Завтра'
  return format(d, 'd MMMM, EEE', { locale: ru })
}

// ── Expandable client history + notes panel, embedded under a booking row ────
// Mirrors the "Заметки" / "История визитов" card from the Клиенты section
// (MasterClientsPage.tsx) so the master sees the same data in both places.

interface ClientHistoryPanelProps {
  booking: Booking
  client: MasterClient | undefined
}

function ClientHistoryPanel({ booking, client }: ClientHistoryPanelProps) {
  const [newNote, setNewNote] = useState('')
  const qc = useQueryClient()

  const addNoteMut = useMutation({
    mutationFn: () =>
      mastersApi.addNote({
        companyId: booking.companyId,
        clientId: booking.clientId ?? undefined,
        guestPhone: booking.clientId ? undefined : (booking.clientPhone ?? undefined),
        note: newNote,
      }),
    onSuccess: () => {
      setNewNote('')
      qc.invalidateQueries({ queryKey: ['master-clients', booking.companyId] })
    },
  })

  const summaries = client?.bookingSummaries ?? []
  const notes = client?.notes ?? []

  return (
    <div className="mt-4 border-t border-line pt-4 flex flex-col gap-4">
      <div>
        <p className="text-sm font-semibold text-ink-soft mb-2">История визитов</p>
        {summaries.length === 0 ? (
          <p className="text-sm text-muted">Нет записей</p>
        ) : (
          <div className="flex flex-col gap-1.5">
            {summaries.map((b, i) => (
              <div key={i} className="flex items-center gap-3 text-sm">
                <span className="text-muted shrink-0">{b.date}</span>
                <span className="text-ink flex-1">{b.serviceName}</span>
                <span className={`text-xs px-2.5 py-0.5 rounded-full font-medium shrink-0 ${
                  b.status === 'Completed' ? 'bg-success-bg text-success'
                  : b.status === 'Cancelled' ? 'bg-danger-bg text-danger'
                  : 'bg-cream-deep text-ink-soft'
                }`}>{STATUS_LABELS[b.status] ?? b.status}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      <div>
        <p className="text-sm font-semibold text-ink-soft mb-2">Заметки о клиенте</p>
        {notes.length === 0 ? (
          <p className="text-sm text-muted mb-2">Нет заметок</p>
        ) : (
          <ul className="flex flex-col gap-1 mb-2">
            {notes.map((n, i) => (
              <li key={i} className="text-sm text-ink-soft bg-cream-deep rounded-xl px-3 py-2">
                {n}
              </li>
            ))}
          </ul>
        )}
        <div className="flex gap-2 mt-2">
          <input
            type="text"
            value={newNote}
            onChange={e => setNewNote(e.target.value)}
            placeholder="Оставить отзыв о клиенте…"
            className="flex-1 rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold"
            onKeyDown={e => { if (e.key === 'Enter' && newNote.trim()) addNoteMut.mutate() }}
          />
          <Button
            size="sm"
            onClick={() => addNoteMut.mutate()}
            disabled={!newNote.trim()}
            loading={addNoteMut.isPending}
          >
            Добавить
          </Button>
        </div>
      </div>
    </div>
  )
}

// ── Single booking row ───────────────────────────────────────────────────────

interface BookingRowProps {
  booking: Booking
  client: MasterClient | undefined
  onReschedule: (b: Booking) => void
  onReview: (b: Booking) => void
  canReview: boolean
  cancel: ReturnType<typeof useMutation<unknown, Error, string>>
  complete: ReturnType<typeof useMutation<unknown, Error, string>>
  noShow: ReturnType<typeof useMutation<unknown, Error, string>>
  markPaid: ReturnType<typeof useMutation<unknown, Error, string>>
}

function BookingRow({ booking: b, client, onReschedule, onReview, canReview, cancel, complete, noShow, markPaid }: BookingRowProps) {
  const [historyOpen, setHistoryOpen] = useState(false)
  const isFinalized = b.status === 'Completed' || b.status === 'Cancelled' || b.status === 'NoShow'

  return (
    <Card className="p-4">
      <div className="flex items-center justify-between gap-4">
        <div className="flex items-center gap-4">
          <div className="text-center bg-cream-deep rounded-xl px-3 py-2 min-w-[60px] shrink-0">
            <div className="text-lg font-bold text-gold-dark">{b.startTime.slice(0, 5)}</div>
            <div className="text-xs text-muted">{b.endTime.slice(0, 5)}</div>
          </div>
          <div>
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-medium text-ink">{b.serviceName}</span>
              <StatusBadge status={b.status} />
              {b.paymentStatus === 'Pending' && (
                <span className="text-xs font-medium bg-warning-bg text-warning px-2.5 py-0.5 rounded-full">
                  Ожидает оплаты
                </span>
              )}
              {b.paymentStatus === 'Paid' && (
                <span className="text-xs font-medium bg-success-bg text-success px-2.5 py-0.5 rounded-full flex items-center gap-1">
                  <Icon name="check" size={11} strokeWidth={2} /> Оплачено
                </span>
              )}
            </div>
            <p className="text-sm text-muted mt-0.5 flex items-center gap-1">
              <Icon name="user" size={12} strokeWidth={1.8} /> {b.clientName}
            </p>
            {b.clientPhone && (
              <p className="text-xs text-muted mt-0.5 flex items-center gap-1">
                <Icon name="phone" size={11} strokeWidth={1.8} /> {b.clientPhone}
              </p>
            )}
            {b.notes && (
              <p className="text-xs text-ink-soft mt-1 bg-cream-deep rounded-lg px-2 py-1">
                {b.notes}
              </p>
            )}
          </div>
        </div>
        <div className="flex gap-2 shrink-0 flex-wrap justify-end">
          {(b.status === 'Pending' || b.status === 'Confirmed') && (<>
            {b.paymentStatus === 'Pending' && (
              <Button size="sm" variant="secondary" loading={markPaid.isPending} onClick={() => markPaid.mutate(b.id)}>
                <Icon name="credit-card" size={13} strokeWidth={1.8} /> Отметить оплату
              </Button>
            )}
            <Button size="sm" variant="secondary" onClick={() => onReschedule(b)}>Перенести</Button>
            <Button size="sm" loading={complete.isPending} onClick={() => complete.mutate(b.id)}>Выполнено</Button>
            <Button size="sm" variant="secondary" loading={noShow.isPending} onClick={() => noShow.mutate(b.id)}>Не пришёл</Button>
            <Button size="sm" variant="danger" loading={cancel.isPending} onClick={() => cancel.mutate(b.id)}>Отменить</Button>
          </>)}
          {b.status === 'Completed' && canReview && (
            <Button size="sm" variant="secondary" onClick={() => onReview(b)}>
              <Icon name="star" size={13} strokeWidth={1.8} /> Отзыв об услуге
            </Button>
          )}
          {isFinalized ? (
            <Button size="sm" variant="secondary" onClick={() => setHistoryOpen(v => !v)}>
              <Icon name="star" size={13} strokeWidth={1.8} /> Отзыв о клиенте
              <Icon name="chevron-down" size={13} strokeWidth={1.8} className={`transition-transform ${historyOpen ? 'rotate-180' : ''}`} />
            </Button>
          ) : (
            <Button size="sm" variant="ghost" onClick={() => setHistoryOpen(v => !v)}>
              История клиента
              <Icon name="chevron-down" size={13} strokeWidth={1.8} className={`transition-transform ${historyOpen ? 'rotate-180' : ''}`} />
            </Button>
          )}
        </div>
      </div>

      {historyOpen && <ClientHistoryPanel booking={b} client={client} />}
    </Card>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function MyBookingsPage() {
  const [showCreateModal, setShowCreateModal] = useState(false)
  const [rescheduleBooking, setRescheduleBooking] = useState<Booking | null>(null)
  const [reviewBooking, setReviewBooking] = useState<Booking | null>(null)
  const qc = useQueryClient()

  const today = format(new Date(), 'yyyy-MM-dd')
  const weekEnd = format(addDays(new Date(), 6), 'yyyy-MM-dd')

  const { data: bookings, isLoading } = useQuery({
    queryKey: ['master-bookings', today, weekEnd],
    queryFn: () => bookingsApi.getMasterBookings(today),
  })

  const cancel = useMutation({
    mutationFn: (id: string) => bookingsApi.cancel(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['master-bookings'] }),
  })
  const complete = useMutation({
    mutationFn: (id: string) => bookingsApi.complete(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['master-bookings'] }),
  })
  const noShow = useMutation({
    mutationFn: (id: string) => bookingsApi.noShow(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['master-bookings'] }),
  })
  const markPaid = useMutation({
    mutationFn: (id: string) => bookingsApi.markPaid(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['master-bookings'] }),
  })

  const { data: canReviewList } = useQuery({
    queryKey: ['can-review'],
    queryFn: reviewsApi.canReview,
  })

  const canReviewSet = useMemo(() => new Set((canReviewList ?? []).map(r => r.bookingId)), [canReviewList])

  // Client history/notes are scoped per company (see MastersController.GetClients) — fetch the
  // client list for every company the master has bookings in, then look each booking's client up.
  const companyIds = useMemo(
    () => Array.from(new Set((bookings ?? []).map(b => b.companyId))),
    [bookings]
  )
  const clientQueries = useQueries({
    queries: companyIds.map(companyId => ({
      queryKey: ['master-clients', companyId],
      queryFn: () => mastersApi.getClients(companyId),
    })),
  })
  const clientByKey = useMemo(() => {
    const map = new Map<string, MasterClient>()
    for (const q of clientQueries) {
      for (const c of q.data ?? []) {
        const key = c.clientId ?? c.guestPhone
        if (key) map.set(key, c)
      }
    }
    return map
  }, [clientQueries])

  const grouped = useMemo(() => {
    if (!bookings) return []
    const map = new Map<string, Booking[]>()
    for (const b of bookings) {
      const arr = map.get(b.date) ?? []
      arr.push(b)
      map.set(b.date, arr)
    }
    return Array.from(map.entries()).sort(([a], [b]) => a.localeCompare(b))
  }, [bookings])

  return (
    <div className="max-w-3xl mx-auto px-8 pt-11 pb-24">
      <div className="flex items-center justify-between mb-7">
        <h1 className="font-serif text-[30px] font-medium text-ink">Мои записи</h1>
        <Button onClick={() => setShowCreateModal(true)}>
          <Icon name="plus" size={15} strokeWidth={2} /> Добавить запись
        </Button>
      </div>

      {showCreateModal && <ManualBookingModal onClose={() => setShowCreateModal(false)} />}
      {rescheduleBooking && <RescheduleModal booking={rescheduleBooking} onClose={() => setRescheduleBooking(null)} />}
      {reviewBooking && (
        <ReviewModal
          bookingId={reviewBooking.id}
          serviceName={reviewBooking.serviceName}
          masterName={reviewBooking.clientName}
          companyId={reviewBooking.companyId}
          onClose={() => setReviewBooking(null)}
          onSuccess={() => setReviewBooking(null)}
        />
      )}

      {isLoading ? (
        <div className="grid gap-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="h-20 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : grouped.length > 0 ? (
        <div className="grid gap-6">
          {grouped.map(([date, dayBookings]) => (
            <div key={date}>
              <h3 className="text-sm font-semibold text-muted uppercase tracking-wide mb-2">
                {dayLabel(date)}
              </h3>
              <div className="grid gap-2">
                {dayBookings.map((b: Booking) => (
                  <BookingRow
                    key={b.id}
                    booking={b}
                    client={clientByKey.get(b.clientId ?? b.clientPhone ?? '')}
                    onReschedule={setRescheduleBooking}
                    onReview={setReviewBooking}
                    canReview={canReviewSet.has(b.id)}
                    cancel={cancel}
                    complete={complete}
                    noShow={noShow}
                    markPaid={markPaid}
                  />
                ))}
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="text-center py-12 text-muted">
          <Icon name="calendar" size={32} strokeWidth={1.4} className="mx-auto mb-2" />
          <p>Записей на ближайшую неделю нет</p>
        </div>
      )}
    </div>
  )
}
