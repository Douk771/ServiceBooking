import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { fmtDateTime } from '@/utils/dateFormat'
import { formatPhone, telHref } from '@/utils/phone'
import { staysBoardApi } from '../../api/staysBoard'
import { PriceBreakdown } from '../../components/PriceBreakdown'
import { ProofFileButton } from '../../components/ProofFileButton'
import { StatusBadge } from '../../components/StatusBadge'
import { ErrorState, LoadingList } from '../../components/StatePanels'
import { StayNotice } from '../../components/StayNotice'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import type { StaffStayAction, StaffStayBookingCardDto } from '../../types'
import { can } from '../../utils/permissions'
import { formatDateWithWeekday, formatInstantInZone } from '../../utils/stayDates'
import { getStayErrorMessage, httpStatus } from '../../utils/stayError'
import { ACTION_LABELS, REASON_MAX, readStaffConflict, reasonProblem } from '../../utils/staffBookingActions'
import { NotFoundPage } from '../NotFoundPage'

const GUEST_KIND: Record<StaffStayBookingCardDto['guestKind'], string> = { Guest: 'гость без аккаунта', Customer: 'вошёл в аккаунт', Staff: 'создана сотрудником' }

/**
 * `/cabinet/:companyId/bookings/:bookingId` (`ViewBookings`) — the booking card: the guest, the stay, the sum, the payment proofs (image in a
 * window, PDF as a download — the file goes through the API with the token, never a link), the actions the server allows, the journal and the
 * messages sent to the guest. Every action carries `expectedVersion`: if somebody changed the booking first, the answer is the CURRENT card
 * and the action is NOT applied — said in words, never retried silently.
 */
export function BookingCardPage() {
  const { bookingId = '' } = useParams()
  const { company, refresh } = useStaysCompany()
  const qc = useQueryClient()
  const key = ['stays-booking-card', company.id, bookingId]
  const canManage = can(company.myPermissions, 'ManageBookings')
  const [dialog, setDialog] = useState<'reject' | 'cancel' | null>(null)
  const [banner, setBanner] = useState('')

  const q = useQuery({
    queryKey: key,
    queryFn: () => staysBoardApi.booking(company.id, bookingId),
    staleTime: 0,
    refetchInterval: 15_000,
    enabled: can(company.myPermissions, 'ViewBookings'),
    retry: (count, err) => httpStatus(err) === undefined && count < 2,
  })
  const card = q.data

  useEffect(() => {
    if (card) document.title = `Бронь ${card.guestName ?? ''} — ${card.house.name}`
  }, [card])

  const applyCard = (c: StaffStayBookingCardDto) => {
    qc.setQueryData(key, c)
    void qc.invalidateQueries({ queryKey: ['stays-bookings', company.id] })
    void qc.invalidateQueries({ queryKey: ['stays-board', company.id] })
    refresh()
  }

  const act = useMutation({
    mutationFn: (v: { action: StaffStayAction; reason?: string }) => {
      const version = card!.version
      if (v.action === 'ConfirmPayment') return staysBoardApi.confirmPayment(company.id, bookingId, version)
      if (v.action === 'RejectPayment') return staysBoardApi.rejectPayment(company.id, bookingId, version, v.reason!.trim())
      return staysBoardApi.cancel(company.id, bookingId, version, v.reason!.trim())
    },
    onSuccess: (c) => {
      setBanner('')
      setDialog(null)
      applyCard(c)
    },
    onError: (err) => {
      setDialog(null)
      const conflict = readStaffConflict(err)
      if (conflict) {
        applyCard(conflict.card)
        setBanner(conflict.message)
        return
      }
      setBanner(getStayErrorMessage(err, 'Не удалось выполнить действие.'))
    },
  })

  if (!can(company.myPermissions, 'ViewBookings')) return <NotFoundPage title="Раздел недоступен" />
  if (q.isLoading) {
    return (
      <main className="mx-auto max-w-[900px] px-4 pt-8 sm:px-8">
        <LoadingList rows={3} />
      </main>
    )
  }
  if (q.isError && httpStatus(q.error) === 404) return <NotFoundPage title="Бронь не найдена" hint="Её нет в этой компании." />
  if (q.isError || !card) {
    return (
      <main className="mx-auto max-w-[760px] px-4 pt-8 sm:px-8">
        <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить бронь.')} onRetry={() => void q.refetch()} />
      </main>
    )
  }

  const tz = company.timeZoneId
  const allowed = (a: StaffStayAction) => canManage && card.availableActions.includes(a)

  return (
    <main className="mx-auto max-w-[900px] px-4 pb-8 pt-6 sm:px-8">
      <Link to={`/cabinet/${company.id}/bookings`} className="text-xs font-medium text-ink-soft hover:text-gold-dark">
        ← Все брони
      </Link>

      <header className="mt-1 flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="font-serif text-[28px] leading-tight text-ink">{card.guestName || 'Гость'}</h2>
          <p className="mt-0.5 text-sm text-ink-soft">
            {card.house.name} · {formatDateWithWeekday(card.checkInDate)} → {formatDateWithWeekday(card.checkOutDate)} · {card.nights} н.
          </p>
        </div>
        <StatusBadge status={card.displayStatus} text={card.statusText} className="text-sm" />
      </header>

      {banner && (
        <p role="alert" className="mt-4 rounded-2xl bg-warning-bg px-5 py-3 text-sm text-warning" data-testid="card-banner">
          {banner}
        </p>
      )}

      {card.status === 'Held' && card.holdExpiresAtUtc && (
        <p className="mt-4 rounded-2xl bg-warning-bg px-5 py-3 text-sm text-warning">Ждём оплату до {formatInstantInZone(card.holdExpiresAtUtc, tz)}. Если гость не успеет, даты освободятся сами.</p>
      )}

      {(allowed('ConfirmPayment') || allowed('RejectPayment') || allowed('Cancel')) && (
        <section aria-label="Действия" className="mt-5 flex flex-wrap gap-3" data-testid="card-actions">
          {allowed('ConfirmPayment') && (
            <Button size="lg" className="min-h-[44px]" loading={act.isPending && act.variables?.action === 'ConfirmPayment'} onClick={() => act.mutate({ action: 'ConfirmPayment' })}>
              {ACTION_LABELS.ConfirmPayment}
            </Button>
          )}
          {allowed('RejectPayment') && (
            <Button size="lg" variant="danger" className="min-h-[44px]" onClick={() => setDialog('reject')}>
              {ACTION_LABELS.RejectPayment}
            </Button>
          )}
          {allowed('Cancel') && (
            <Button size="lg" variant="secondary" className="min-h-[44px]" onClick={() => setDialog('cancel')}>
              {ACTION_LABELS.Cancel}
            </Button>
          )}
        </section>
      )}

      <div className="mt-6 grid gap-5 md:grid-cols-2">
        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="g-h">
          <h3 id="g-h" className="mb-2 text-[15px] font-semibold text-ink">
            Гость
          </h3>
          <dl className="grid gap-x-4 gap-y-1.5 text-sm sm:grid-cols-[110px_1fr]">
            <dt className="text-muted">Имя</dt>
            <dd className="text-ink">{card.guestName ?? '—'}</dd>
            <dt className="text-muted">Телефон</dt>
            <dd className="text-ink">
              {card.guestPhone ? (
                <a href={telHref(card.guestPhone) || undefined} className="font-medium !text-ink underline">
                  {formatPhone(card.guestPhone)}
                </a>
              ) : (
                '—'
              )}
            </dd>
            <dt className="text-muted">Бронь</dt>
            <dd className="text-ink">{card.isManual ? 'создана вручную' : GUEST_KIND[card.guestKind]}</dd>
            {card.comment && (
              <>
                <dt className="text-muted">Комментарий</dt>
                <dd className="whitespace-pre-line text-ink">{card.comment}</dd>
              </>
            )}
          </dl>
        </section>

        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="s-h">
          <h3 id="s-h" className="mb-2 text-[15px] font-semibold text-ink">
            Проживание
          </h3>
          <dl className="grid gap-x-4 gap-y-1.5 text-sm sm:grid-cols-[110px_1fr]">
            <dt className="text-muted">Заезд</dt>
            <dd className="text-ink">
              {formatDateWithWeekday(card.checkInDate)}, с {card.checkInTime}
            </dd>
            <dt className="text-muted">Выезд</dt>
            <dd className="text-ink">
              {formatDateWithWeekday(card.checkOutDate)}, до {card.checkOutTime}
            </dd>
            <dt className="text-muted">Гости</dt>
            <dd className="text-ink">
              {card.adults} взр.{card.children > 0 ? `, ${card.children} дет.` : ''}
              {card.dogs > 0 ? `, собак: ${card.dogs}` : ''}
              {card.needCot ? ', кроватка' : ''}
              {card.extraBeds > 0 ? `, доп. мест: ${card.extraBeds}` : ''}
            </dd>
            {card.arrivalTime && (
              <>
                <dt className="text-muted">Прибытие</dt>
                <dd className="text-ink">около {card.arrivalTime}</dd>
              </>
            )}
          </dl>
        </section>

        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="m-h">
          <h3 id="m-h" className="mb-2 text-[15px] font-semibold text-ink">
            Стоимость
          </h3>
          <PriceBreakdown lines={card.lines} totalRub={card.totalRub} prepayPercent={card.prepayPercent} prepayRub={card.prepayRub} dueAtCheckInRub={card.dueAtCheckInRub} />
          {card.paymentConfirmed && (
            <p className="mt-2 text-xs text-success">
              Оплату подтвердил {card.paymentConfirmed.byName}, {fmtDateTime(card.paymentConfirmed.atUtc)}
            </p>
          )}
        </section>

        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="p-h">
          <h3 id="p-h" className="mb-2 text-[15px] font-semibold text-ink">
            Подтверждение оплаты
          </h3>
          {card.paymentProofs.length === 0 ? (
            <p className="text-sm text-ink-soft">{card.paymentProofsPurgedAtUtc ? 'Файлы удалены по сроку хранения.' : 'Гость ещё не приложил файл.'}</p>
          ) : (
            <ul className="flex flex-col gap-3">
              {card.paymentProofs.map((p, i) => (
                <li key={p.id}>
                  <ProofFileButton
                    contentType={p.contentType}
                    sizeBytes={p.sizeBytes}
                    purged={p.purged}
                    title={`Файл ${i + 1}, ${fmtDateTime(p.uploadedAtUtc)}`}
                    fetchBlob={() => staysBoardApi.proofBlob(company.id, bookingId, p.id)}
                  />
                </li>
              ))}
            </ul>
          )}
          {card.paymentProofsPurgedAtUtc && card.paymentProofs.length > 0 && <p className="mt-2 text-xs text-muted">Часть файлов удалена по сроку хранения.</p>}
        </section>
      </div>

      {card.statusReason && (
        <p className="mt-5 rounded-2xl border border-line bg-white px-5 py-3 text-sm text-ink-soft">
          <span className="font-semibold text-ink">Причина:</span> {card.statusReason}
        </p>
      )}

      {card.events.length > 0 && (
        <section className="mt-6 rounded-2xl border border-line bg-white p-5" aria-labelledby="j-h">
          <h3 id="j-h" className="mb-3 text-[15px] font-semibold text-ink">
            Журнал
          </h3>
          <ol className="flex flex-col gap-2.5" data-testid="journal">
            {card.events.map((e, i) => (
              <li key={`${e.occurredAtUtc}-${i}`} className="text-sm">
                <p className="text-ink">{e.text}</p>
                <p className="text-xs text-muted">
                  {fmtDateTime(e.occurredAtUtc)} · {e.actorText}
                  {e.reason ? ` · ${e.reason}` : ''}
                </p>
              </li>
            ))}
          </ol>
        </section>
      )}

      {card.messages && card.messages.length > 0 && (
        <section className="mt-5 rounded-2xl border border-line bg-white p-5" aria-labelledby="mm-h">
          <h3 id="mm-h" className="mb-3 text-[15px] font-semibold text-ink">
            Сообщения гостю
          </h3>
          <ul className="flex flex-col gap-1.5 text-sm text-ink-soft">
            {card.messages.map((m, i) => (
              <li key={`${m.createdAtUtc}-${i}`}>
                {fmtDateTime(m.createdAtUtc)} · {m.type} · {m.channel} · {m.status}
              </li>
            ))}
          </ul>
        </section>
      )}

      {dialog && (
        <ReasonDialog
          kind={dialog}
          card={card}
          pending={act.isPending}
          onClose={() => setDialog(null)}
          onSubmit={(reason) => act.mutate({ action: dialog === 'reject' ? 'RejectPayment' : 'Cancel', reason })}
        />
      )}
    </main>
  )
}

function ReasonDialog({ kind, card, pending, onClose, onSubmit }: { kind: 'reject' | 'cancel'; card: StaffStayBookingCardDto; pending: boolean; onClose: () => void; onSubmit: (reason: string) => void }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState('')
  return (
    <Modal title={kind === 'reject' ? 'Отклонить оплату' : 'Отменить бронь'} onClose={onClose} dismissible={!pending}>
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault()
          const p = reasonProblem(reason)
          setError(p ?? '')
          if (!p) onSubmit(reason)
        }}
      >
        <StayNotice textKey="StayOwnerCancelNotice" />
        {kind === 'cancel' && card.ownerCancelRefundText && (
          <p className="rounded-xl bg-cream-deep px-4 py-3 text-sm font-medium text-ink" data-testid="owner-refund-text">
            {card.ownerCancelRefundText}
          </p>
        )}
        {kind === 'reject' && <p className="text-sm text-ink-soft">Если деньги всё же пришли, верните их или восстановите бронь.</p>}
        <div className="flex flex-col gap-1.5">
          <label htmlFor="reason" className="text-[13px] font-medium text-[#4A4038]">
            Причина — гость её увидит
          </label>
          <textarea
            id="reason"
            rows={3}
            maxLength={REASON_MAX}
            value={reason}
            aria-invalid={!!error}
            onChange={(e) => setReason(e.target.value)}
            className="rounded-xl border border-line bg-white px-3 py-2.5 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
          />
          {error && <p className="text-xs text-danger">{error}</p>}
        </div>
        <div className="flex gap-3">
          <Button type="button" variant="secondary" className="min-h-[44px] flex-1" onClick={onClose} disabled={pending}>
            Назад
          </Button>
          <Button type="submit" variant="danger" className="min-h-[44px] flex-1" loading={pending}>
            {kind === 'reject' ? 'Отклонить оплату' : 'Отменить бронь'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
