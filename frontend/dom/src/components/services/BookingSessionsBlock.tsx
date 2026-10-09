import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { formatRub } from '@/utils/money'
import { guestBookingsApi } from '../../api/guestBookings'
import type { PublicBookingSessionDto, PublicStayBookingWithServices, ServiceRefusalDto, StayBookingGuestConflictDto } from '../../types'
import { newIdempotencyKey } from '../../utils/idempotency'
import { isTimeGone } from '../../utils/serviceSelection'
import { getStayErrorMessage, readConflict } from '../../utils/stayError'
import { ErrorState, Skeleton } from '../StatePanels'
import { StayNotice } from '../StayNotice'
import { CancelSessionDialog } from './CancelSessionDialog'
import { ServicePickDialog } from './ServicePickDialog'

const NO_MONEY_TEXT = 'Оплата за сеанс не вносилась — отмена без последствий.'

/**
 * «Услуги к проживанию» on the booking page (US-39-09, API_CONTRACT_CYCLE39.md §39.24). A session is paid on the spot and is
 * not part of the prepayment; the guest can cancel it before it starts without consequences. A session the STAFF added gets its
 * own box «Добавлено по вашей просьбе» with the way back (ЮР39-6): the guest can cancel what they did not ask for. For a `Held`
 * booking the server's `hint` says the session stays only if the booking is paid.
 */
export function BookingSessionsBlock({ token, booking, onBooking }: { token: string; booking: PublicStayBookingWithServices; onBooking: (b: PublicStayBookingWithServices) => void }) {
  const [adding, setAdding] = useState(false)
  const [cancelling, setCancelling] = useState<PublicBookingSessionDto | null>(null)
  const [cancelError, setCancelError] = useState('')
  const sessions = booking.sessions ?? []
  const block = booking.servicesBlock

  const cancel = useMutation({
    mutationFn: (sessionId: string) => guestBookingsApi.cancelSession(token, sessionId),
    onSuccess: (b) => {
      onBooking(b)
      setCancelling(null)
      setCancelError('')
    },
    onError: (err) => {
      const c = readConflict<StayBookingGuestConflictDto>(err)
      if (c?.booking) onBooking(c.booking as PublicStayBookingWithServices)
      setCancelError(getStayErrorMessage(err, 'Не удалось отменить сеанс.'))
    },
  })

  if (sessions.length === 0 && !block?.canAdd) return null

  return (
    <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="sessions-title" data-testid="booking-sessions">
      <h2 id="sessions-title" className="mb-3 text-[15px] font-semibold text-ink">
        Услуги к проживанию
      </h2>

      {sessions.length === 0 ? (
        <p className="text-sm text-ink-soft">Услуг пока нет. Баню или чан можно добавить к брони на время проживания.</p>
      ) : (
        <ul className="flex flex-col gap-3">
          {sessions.map((s) => (
            <li
              key={s.id}
              className={`rounded-2xl border px-4 py-3 ${s.addedByStaff ? 'border-warning/40 bg-warning-bg/50' : 'border-line bg-cream/40'}`}
              data-testid="booking-session"
              data-state={s.state}
            >
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="font-medium text-ink">{s.serviceUrl ? <a href={s.serviceUrl} className="!text-ink hover:underline">{s.serviceName}</a> : s.serviceName}</p>
                  <p className="text-sm text-ink-soft">{s.time.label}</p>
                  {s.items.length > 0 && <p className="text-xs text-muted">{s.items.map((i) => `${i.name} × ${i.quantity}`).join(', ')}</p>}
                </div>
                <div className="text-right">
                  <p className="text-sm font-semibold tabular-nums text-ink">{formatRub(s.totalRub)}</p>
                  <p className="text-xs text-muted">{s.stateText}</p>
                </div>
              </div>
              {s.addedByStaff && (
                <p className="mt-2 text-sm text-ink" data-testid="session-added-by-staff">
                  <span className="font-semibold">Добавлено по вашей просьбе.</span> {s.addedByStaffText}
                  {s.canCancel && ' Если вы этого не просили, отмените — без последствий.'}
                </p>
              )}
              {s.statusReason && <p className="mt-1 text-sm text-ink-soft">Причина: {s.statusReason}</p>}
              {s.canCancel ? (
                <Button
                  variant={s.addedByStaff ? 'danger' : 'secondary'}
                  size="sm"
                  className="mt-2 min-h-[44px]"
                  onClick={() => {
                    setCancelError('')
                    setCancelling(s)
                  }}
                >
                  Отменить сеанс
                </Button>
              ) : (
                s.cannotCancelText && <p className="mt-2 text-xs text-ink-soft">{s.cannotCancelText}</p>
              )}
            </li>
          ))}
        </ul>
      )}

      <p className="mt-3 text-xs text-muted">Услуги оплачиваются на месте и в предоплату не входят.</p>

      {block?.canAdd ? (
        <>
          <Button variant="secondary" className="mt-3 min-h-[44px]" onClick={() => setAdding(true)}>
            <Icon name="plus" size={15} strokeWidth={1.8} /> Добавить услугу
          </Button>
          {block.hint && <p className="mt-2 text-xs text-ink-soft">{block.hint}</p>}
        </>
      ) : (
        block?.cannotAddText && <p className="mt-3 text-sm text-ink-soft">{block.cannotAddText}</p>
      )}

      {adding && <AddSessionDialog token={token} booking={booking} onBooking={onBooking} onClose={() => setAdding(false)} />}
      {cancelling && (
        <CancelSessionDialog
          title="Отменить сеанс?"
          refundText={NO_MONEY_TEXT}
          refundMadeByCompany={false}
          summary={`${cancelling.serviceName}, ${cancelling.time.label}`}
          pending={cancel.isPending}
          error={cancelError}
          onConfirm={() => cancel.mutate(cancelling.id)}
          onClose={() => setCancelling(null)}
        />
      )}
    </section>
  )
}

function AddSessionDialog({ token, booking, onBooking, onClose }: { token: string; booking: PublicStayBookingWithServices; onBooking: (b: PublicStayBookingWithServices) => void; onClose: () => void }) {
  const qc = useQueryClient()
  const list = useQuery({ queryKey: ['stay-booking-services', token], queryFn: () => guestBookingsApi.services(token), staleTime: 0 })
  const [error, setError] = useState('')
  const [resetSignal, setResetSignal] = useState(0)
  // one key per opened dialog: a double tap or a retry after a lost answer never adds the session twice
  const idempotencyKey = useMemo(() => newIdempotencyKey(), [])

  const add = useMutation({
    mutationFn: (args: Parameters<typeof guestBookingsApi.addSession>[1]) => guestBookingsApi.addSession(token, args),
    onSuccess: (b) => {
      onBooking(b)
      void qc.invalidateQueries({ queryKey: ['stay-booking-services', token] })
      onClose()
    },
    onError: (err) => {
      const refusal = readConflict<ServiceRefusalDto>(err)
      setError(getStayErrorMessage(err, 'Не удалось добавить услугу.'))
      if (refusal?.code === 'PriceChanged') void qc.invalidateQueries({ queryKey: ['stays-session-quote'] })
      else if (refusal && isTimeGone(refusal.code)) setResetSignal((n) => n + 1)
    },
  })

  if (list.isLoading) {
    return (
      <ShellLoading onClose={onClose}>
        <Skeleton className="h-40" />
      </ShellLoading>
    )
  }
  if (list.isError || !list.data) {
    return (
      <ShellLoading onClose={onClose}>
        <ErrorState message={getStayErrorMessage(list.error, 'Не удалось загрузить услуги.')} onRetry={() => void list.refetch()} />
      </ShellLoading>
    )
  }
  const data = list.data
  if (!data.canAdd || data.services.length === 0) {
    return (
      <ShellLoading onClose={onClose}>
        <p className="text-sm text-ink-soft">{data.cannotAddText ?? 'Сейчас нет услуг, которые можно добавить к этой брони.'}</p>
      </ShellLoading>
    )
  }

  return (
    <ServicePickDialog
      title="Добавить услугу к брони"
      services={data.services}
      staticDays={(id) => data.services.find((s) => s.id === id)?.dates ?? []}
      loadStarts={(id, date) => guestBookingsApi.serviceStarts(token, id, date)}
      loadQuote={(id, sel) => guestBookingsApi.serviceQuote(token, id, sel)}
      confirmLabel="Добавить к брони"
      hint={data.hint ?? booking.servicesBlock?.hint}
      pending={add.isPending}
      error={error}
      companyName={booking.company.name}
      resetSignal={resetSignal}
      onClose={onClose}
      onConfirm={(serviceId, pick, quote, order) => {
        setError('')
        add.mutate({
          serviceId,
          businessDate: pick.businessDate,
          startMinute: pick.startMinute,
          hours: pick.hours,
          items: order.filter((id) => (pick.quantities[id] ?? 0) > 0).map((itemId) => ({ itemId, quantity: pick.quantities[itemId] })),
          expectedTotalRub: quote.totalRub,
          idempotencyKey,
        })
      }}
    />
  )
}

import { Modal } from '@/components/ui/Modal'
import type { ReactNode } from 'react'

function ShellLoading({ children, onClose }: { children: ReactNode; onClose: () => void }) {
  return (
    <Modal title="Добавить услугу к брони" onClose={onClose}>
      {children}
      <StayNotice textKey="StayServiceAddNotice" variant="plain" className="mt-4" />
    </Modal>
  )
}
