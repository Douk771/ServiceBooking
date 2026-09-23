import { useState, useMemo } from 'react'
import { useQuery, useQueries, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO, isToday, isTomorrow, addDays } from 'date-fns'
import { ru } from 'date-fns/locale'
import { bookingsApi } from '../api/bookings'
import { mastersApi, type MasterClient } from '../api/masters'
import { clientNotesApi } from '../api/clientNotes'
import { getBookingErrorMessage } from '../utils/bookingError'
import { getCancelErrorMessage } from '../utils/cancelError'
import { formatPhone } from '../utils/phone'
import { formatBookingServiceNames } from '../utils/bookingServices'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { StatusBadge } from '../components/ui/Badge'
import { Icon } from '../components/ui/Icon'
import { BookingModal } from '../components/booking/BookingModal'
import { BookingHistoryPanel } from '../components/booking/BookingHistoryPanel'
import { RescheduleModal } from '../components/booking/RescheduleModal'
import { NoteCard } from '../components/clientNotes/NoteCard'
import { NotePhotoUploader } from '../components/clientNotes/NotePhotoUploader'
import { MyDevicesCard } from '../components/push/MyDevicesCard'
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
  const [pendingPhotos, setPendingPhotos] = useState<File[]>([])
  const [photoUploadError, setPhotoUploadError] = useState('')
  const qc = useQueryClient()

  const addNoteMut = useMutation({
    mutationFn: async () => {
      const created = await mastersApi.addNote({
        companyId: booking.companyId,
        clientId: booking.clientId ?? undefined,
        guestPhone: booking.clientId ? undefined : (booking.clientPhone ?? undefined),
        note: newNote,
        // Created from the panel under a specific booking row — the note is linked to that visit
        // automatically (US-17 п. 4), so the history shows what was done and when without extra input.
        bookingId: booking.id,
      })
      setPhotoUploadError('')
      for (const file of pendingPhotos) {
        try {
          await clientNotesApi.uploadPhoto(created.id, file)
        } catch {
          setPhotoUploadError('Заметка сохранена, но не все фото удалось загрузить.')
        }
      }
      return created
    },
    onSuccess: () => {
      setNewNote('')
      setPendingPhotos([])
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
                <span
                  className={`text-xs px-2.5 py-0.5 rounded-full font-medium shrink-0 ${
                    b.status === 'Completed'
                      ? 'bg-success-bg text-success'
                      : b.status === 'Cancelled'
                        ? 'bg-danger-bg text-danger'
                        : 'bg-cream-deep text-ink-soft'
                  }`}
                >
                  {STATUS_LABELS[b.status] ?? b.status}
                </span>
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
          <ul className="flex flex-col gap-1.5 mb-2">
            {notes.map((n) => (
              <NoteCard key={n.id} note={n} companyId={booking.companyId} />
            ))}
          </ul>
        )}
        <div className="flex flex-col gap-2 mt-2">
          <label className="sr-only" htmlFor={`new-note-${booking.id}`}>
            Добавить заметку
          </label>
          <div className="flex gap-2">
            <input
              id={`new-note-${booking.id}`}
              type="text"
              value={newNote}
              onChange={(e) => setNewNote(e.target.value)}
              placeholder="Добавить заметку…"
              className="flex-1 rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold"
              onKeyDown={(e) => {
                if (e.key === 'Enter' && newNote.trim()) addNoteMut.mutate()
              }}
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
          <NotePhotoUploader
            remainingSlots={5 - pendingPhotos.length}
            value={pendingPhotos}
            onChange={setPendingPhotos}
            compact
          />
          {photoUploadError && <p className="text-xs text-danger">{photoUploadError}</p>}
        </div>
      </div>
    </div>
  )
}

// ── Cancel-with-reason inline form ───────────────────────────────────────────

interface CancelFormProps {
  bookingId: string
  onCancel: (reason: string) => void
  onBack: () => void
  loading: boolean
}

function CancelForm({ bookingId, onCancel, onBack, loading }: CancelFormProps) {
  const [reason, setReason] = useState('')
  return (
    <div className="mt-3 border-t border-line pt-3 flex flex-col gap-2">
      <label htmlFor={`cancel-reason-${bookingId}`} className="text-xs font-medium text-ink-soft">
        Причина (увидит клиент)
      </label>
      <textarea
        id={`cancel-reason-${bookingId}`}
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        maxLength={300}
        rows={2}
        placeholder="Необязательно"
        className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold resize-none"
      />
      <div className="flex gap-2 justify-end">
        <Button size="sm" variant="secondary" onClick={onBack} disabled={loading}>
          Назад
        </Button>
        <Button size="sm" variant="danger" loading={loading} onClick={() => onCancel(reason.trim())}>
          Отменить запись
        </Button>
      </div>
    </div>
  )
}

// ── Single booking row ───────────────────────────────────────────────────────

interface BookingRowProps {
  booking: Booking
  client: MasterClient | undefined
  onReschedule: (b: Booking) => void
  cancel: ReturnType<typeof useMutation<unknown, Error, { id: string; reason: string }>>
  complete: ReturnType<typeof useMutation<unknown, Error, string>>
  noShow: ReturnType<typeof useMutation<unknown, Error, string>>
  markPaid: ReturnType<typeof useMutation<unknown, Error, string>>
  error: string | null
}

function BookingRow({ booking: b, client, onReschedule, cancel, complete, noShow, markPaid, error }: BookingRowProps) {
  const [historyOpen, setHistoryOpen] = useState(false)
  // ARCHITECTURE_CYCLE10.md §109.1 — the booking's change journal, distinct from `historyOpen`
  // above (client notes/"История клиента"). Named separately so the two panels can be open
  // independently and neither hides the other's toggle.
  const [journalOpen, setJournalOpen] = useState(false)
  const [cancelling, setCancelling] = useState(false)
  const isFinalized = b.status === 'Completed' || b.status === 'Cancelled' || b.status === 'NoShow'

  return (
    <Card className="p-4">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="flex items-center gap-4 min-w-0">
          <div className="text-center bg-cream-deep rounded-xl px-3 py-2 min-w-[60px] shrink-0">
            <div className="text-lg font-bold text-gold-dark">{b.startTime.slice(0, 5)}</div>
            <div className="text-xs text-muted">{b.endTime.slice(0, 5)}</div>
          </div>
          <div>
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-medium text-ink">{formatBookingServiceNames(b)}</span>
              {b.companyName && (
                <span className="text-xs text-muted flex items-center gap-1">
                  <Icon name="store" size={11} strokeWidth={1.8} /> {b.companyName}
                </span>
              )}
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
              {/* US-32 п. 6 — compact reminder-delivery mark, server-supplied text (statusText). */}
              {b.reminderStatus && (
                <span
                  className={`text-xs font-medium px-2.5 py-0.5 rounded-full flex items-center gap-1 ${
                    b.reminderStatus.status === 'Delivered'
                      ? 'bg-success-bg text-success'
                      : b.reminderStatus.status === 'Failed' || b.reminderStatus.status === 'Cancelled'
                        ? 'bg-danger-bg text-danger'
                        : 'bg-cream-deep text-ink-soft'
                  }`}
                >
                  <Icon name="megaphone" size={11} strokeWidth={1.8} /> {b.reminderStatus.text}
                </span>
              )}
            </div>
            <p className="text-sm text-muted mt-0.5 flex items-center gap-1">
              <Icon name="user" size={12} strokeWidth={1.8} /> {b.clientName}
            </p>
            {b.clientPhone && (
              <p className="text-xs text-muted mt-0.5 flex items-center gap-1">
                <Icon name="phone" size={11} strokeWidth={1.8} /> {formatPhone(b.clientPhone)}
              </p>
            )}
            {b.notes && <p className="text-xs text-ink-soft mt-1 bg-cream-deep rounded-lg px-2 py-1">{b.notes}</p>}
            {b.status === 'Cancelled' && b.cancellationReason && (
              <p className="text-xs text-danger mt-1 bg-danger-bg rounded-lg px-2 py-1">
                Причина отмены: {b.cancellationReason}
              </p>
            )}
            {/* §109.1/§123 — the button exists ONLY when the server actually counted events; a
                `0`/`null` historyEventCount renders no block at all, not an empty one. */}
            {!!b.historyEventCount && (
              <button
                type="button"
                onClick={() => setJournalOpen((v) => !v)}
                className="mt-1.5 flex items-center gap-1 text-xs text-ink-soft hover:text-ink"
              >
                <Icon name="clock" size={12} strokeWidth={1.8} />
                История записи
                <Icon
                  name="chevron-down"
                  size={12}
                  strokeWidth={1.8}
                  className={`transition-transform ${journalOpen ? 'rotate-180' : ''}`}
                />
              </button>
            )}
            {journalOpen && !!b.historyEventCount && <BookingHistoryPanel bookingId={b.id} />}
          </div>
        </div>
        <div className="flex gap-2 flex-wrap sm:justify-end">
          {(b.status === 'Pending' || b.status === 'Confirmed') && (
            <>
              {b.paymentStatus === 'Pending' && (
                <Button
                  size="sm"
                  variant="secondary"
                  loading={markPaid.isPending}
                  onClick={() => markPaid.mutate(b.id)}
                >
                  <Icon name="credit-card" size={13} strokeWidth={1.8} /> Отметить оплату
                </Button>
              )}
              <Button size="sm" variant="secondary" onClick={() => onReschedule(b)}>
                Перенести
              </Button>
              <Button size="sm" loading={complete.isPending} onClick={() => complete.mutate(b.id)}>
                Выполнено
              </Button>
              <Button size="sm" variant="secondary" loading={noShow.isPending} onClick={() => noShow.mutate(b.id)}>
                Не пришёл
              </Button>
              {!cancelling && (
                <Button size="sm" variant="danger" onClick={() => setCancelling(true)}>
                  Отменить
                </Button>
              )}
            </>
          )}
          {isFinalized ? (
            <Button size="sm" variant="secondary" onClick={() => setHistoryOpen((v) => !v)}>
              <Icon name="star" size={13} strokeWidth={1.8} /> Заметки о клиенте
              <Icon
                name="chevron-down"
                size={13}
                strokeWidth={1.8}
                className={`transition-transform ${historyOpen ? 'rotate-180' : ''}`}
              />
            </Button>
          ) : (
            <Button size="sm" variant="ghost" onClick={() => setHistoryOpen((v) => !v)}>
              История клиента
              <Icon
                name="chevron-down"
                size={13}
                strokeWidth={1.8}
                className={`transition-transform ${historyOpen ? 'rotate-180' : ''}`}
              />
            </Button>
          )}
        </div>
      </div>

      {/* Rendered inside the row rather than as a page-level banner: in a long list the banner
          appeared off-screen, so an action that failed still looked like nothing had happened. */}
      {error && (
        <p className="mt-3 text-sm text-danger flex items-center gap-2">
          <Icon name="alert-circle" size={15} strokeWidth={1.8} /> {error}
        </p>
      )}
      {cancelling && (
        <CancelForm
          bookingId={b.id}
          loading={cancel.isPending}
          onBack={() => setCancelling(false)}
          onCancel={(reason) => {
            cancel.mutate({ id: b.id, reason })
            setCancelling(false)
          }}
        />
      )}
      {historyOpen && <ClientHistoryPanel booking={b} client={client} />}
    </Card>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function MyBookingsPage() {
  const [showCreateModal, setShowCreateModal] = useState(false)
  const [rescheduleBooking, setRescheduleBooking] = useState<Booking | null>(null)
  const [actionError, setActionError] = useState<{ bookingId: string; message: string } | null>(null)
  const qc = useQueryClient()

  const today = format(new Date(), 'yyyy-MM-dd')
  const weekEnd = format(addDays(new Date(), 6), 'yyyy-MM-dd')

  const { data: bookings, isLoading } = useQuery({
    queryKey: ['master-bookings', today, weekEnd],
    queryFn: () => bookingsApi.getMasterBookings(today),
  })

  const cancel = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => bookingsApi.cancel(id, reason || undefined),
    // Clear on start, not only on success: otherwise a stale failure from another booking stays on
    // screen while the new request is in flight and reads as if it belongs to the new action.
    onMutate: () => setActionError(null),
    onSuccess: () => {
      setActionError(null)
      qc.invalidateQueries({ queryKey: ['master-bookings'] })
    },
    onError: (err, { id }) => setActionError({ bookingId: id, message: getCancelErrorMessage(err) }),
  })
  const complete = useMutation({
    mutationFn: (id: string) => bookingsApi.complete(id),
    // Clear on start, not only on success: otherwise a stale failure from another booking stays on
    // screen while the new request is in flight and reads as if it belongs to the new action.
    onMutate: () => setActionError(null),
    onSuccess: () => {
      setActionError(null)
      qc.invalidateQueries({ queryKey: ['master-bookings'] })
    },
    onError: (err, id) => setActionError({ bookingId: id, message: getBookingErrorMessage(err) }),
  })
  const noShow = useMutation({
    mutationFn: (id: string) => bookingsApi.noShow(id),
    // Clear on start, not only on success: otherwise a stale failure from another booking stays on
    // screen while the new request is in flight and reads as if it belongs to the new action.
    onMutate: () => setActionError(null),
    onSuccess: () => {
      setActionError(null)
      qc.invalidateQueries({ queryKey: ['master-bookings'] })
    },
    onError: (err, id) => setActionError({ bookingId: id, message: getBookingErrorMessage(err) }),
  })
  const markPaid = useMutation({
    mutationFn: (id: string) => bookingsApi.markPaid(id),
    // Clear on start, not only on success: otherwise a stale failure from another booking stays on
    // screen while the new request is in flight and reads as if it belongs to the new action.
    onMutate: () => setActionError(null),
    onSuccess: () => {
      setActionError(null)
      qc.invalidateQueries({ queryKey: ['master-bookings'] })
    },
    onError: (err, id) => setActionError({ bookingId: id, message: getBookingErrorMessage(err) }),
  })

  // Client history/notes are scoped per company (see MastersController.GetClients) — fetch the
  // client list for every company the master has bookings in, then look each booking's client up.
  const companyIds = useMemo(() => Array.from(new Set((bookings ?? []).map((b) => b.companyId))), [bookings])
  const clientQueries = useQueries({
    queries: companyIds.map((companyId) => ({
      queryKey: ['master-clients', companyId],
      // Pagination on this endpoint (API_CONTRACT.md §11) is designed for the "Мои клиенты" list
      // screen, not this cross-reference lookup — pageSize is set to the server's own cap (100) to
      // keep the practical impact low, but a company with more clients than that will have some
      // bookings here show without their client's note/history context. Flagged for the architect:
      // this lookup arguably wants an unpaginated variant of the endpoint.
      queryFn: () => mastersApi.getClients(companyId, 1, 100),
    })),
  })
  const clientByKey = useMemo(() => {
    const map = new Map<string, MasterClient>()
    for (const q of clientQueries) {
      for (const c of q.data?.items ?? []) {
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

      {showCreateModal && <BookingModal onClose={() => setShowCreateModal(false)} />}
      {rescheduleBooking && <RescheduleModal booking={rescheduleBooking} onClose={() => setRescheduleBooking(null)} />}

      {/* §105.10 — lives next to the master's own bookings, the one place SPEC US-118 calls for it. */}
      <div className="mb-6">
        <MyDevicesCard />
      </div>

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
              <h3 className="text-sm font-semibold text-muted uppercase tracking-wide mb-2">{dayLabel(date)}</h3>
              <div className="grid gap-2">
                {dayBookings.map((b: Booking) => (
                  <BookingRow
                    key={b.id}
                    booking={b}
                    client={clientByKey.get(b.clientId ?? b.clientPhone ?? '')}
                    onReschedule={setRescheduleBooking}
                    cancel={cancel}
                    complete={complete}
                    noShow={noShow}
                    markPaid={markPaid}
                    error={actionError?.bookingId === b.id ? actionError.message : null}
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
