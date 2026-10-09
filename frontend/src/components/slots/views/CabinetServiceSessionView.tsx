import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { fmtDateTime } from '@/utils/dateFormat'
import { formatPhone, telHref } from '@/utils/phone'
import { formatRub } from '@/utils/money'
import { ProofFileButton } from '@/components/slots/ui/ProofFileButton'
import { ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { StatusBadge } from '@/components/slots/ui/StatusBadge'
import { useCabinetCompany, useSlotVertical } from '@/components/slots/SlotVerticalContext'
import type { ServiceStaffAction, ServiceStaffConflictDto, StaffServiceSessionCardDto } from '@/types/slots'
import { can } from '@/utils/slots/slotPermissions'
import { REASON_MAX, reasonProblem } from '@/utils/slots/slotReason'
import { getStayErrorMessage, httpStatus, readConflict } from '@/utils/slots/slotError'

const ACTION_LABELS: Record<ServiceStaffAction, string> = {
  ConfirmPayment: 'Подтвердить оплату',
  RejectPayment: 'Отклонить оплату',
  Cancel: 'Отменить сеанс',
}
const KIND_TEXT: Record<StaffServiceSessionCardDto['addedBy']['kind'], string> = { Guest: 'гость без аккаунта', Customer: 'гость вошёл в аккаунт', Staff: 'добавлено сотрудником' }

/**
 * `/cabinet/:companyId/service-sessions/:sessionId` (`ViewBookings`, US-39-14) — the card of a session, either a separate order or a session
 * of a booking: the time in the staff wording, the hours and prices, the guest (the phone is shown here, not in the schedule), the payment
 * proofs, the actions the server allows with `expectedVersion`, the journal. A stale action comes back as the CURRENT card with a
 * 409 and is not applied — said in words, never retried silently.
 */
export function CabinetServiceSessionView() {
  const { api, paths, words, NotFound } = useSlotVertical()
  const staysBoardApi = api.sessions
  const { sessionId = '' } = useParams()
  const { company, refresh } = useCabinetCompany()
  const qc = useQueryClient()
  const key = ['stays-service-session', company.id, sessionId]
  const canManage = can(company.myPermissions, 'ManageBookings')
  const [dialog, setDialog] = useState<'reject' | 'cancel' | null>(null)
  const [banner, setBanner] = useState('')

  const q = useQuery({
    queryKey: key,
    queryFn: () => staysBoardApi.session(company.id, sessionId),
    staleTime: 0,
    refetchInterval: 15_000,
    enabled: can(company.myPermissions, 'ViewBookings'),
    retry: (count, err) => httpStatus(err) === undefined && count < 2,
  })
  const card = q.data

  useEffect(() => {
    if (card) document.title = `Сеанс ${card.serviceName} — ${card.guestName ?? 'гость'}`
  }, [card])

  const applyCard = (c: StaffServiceSessionCardDto) => {
    qc.setQueryData(key, c)
    void qc.invalidateQueries({ queryKey: ['stays-service-sessions', company.id] })
    void qc.invalidateQueries({ queryKey: ['stays-service-day', company.id] })
    void qc.invalidateQueries({ queryKey: ['stays-board', company.id] })
    void qc.invalidateQueries({ queryKey: ['stays-booking-card', company.id] })
    refresh()
  }

  const act = useMutation({
    mutationFn: (v: { action: ServiceStaffAction; reason?: string }) => {
      const version = card!.version
      if (v.action === 'ConfirmPayment') return staysBoardApi.confirmSessionPayment(company.id, sessionId, version)
      if (v.action === 'RejectPayment') return staysBoardApi.rejectSessionPayment(company.id, sessionId, version, v.reason!.trim())
      return staysBoardApi.cancelSession(company.id, sessionId, version, v.reason!.trim())
    },
    onSuccess: (c) => {
      setBanner('')
      setDialog(null)
      applyCard(c)
    },
    onError: (err) => {
      setDialog(null)
      const c = readConflict<ServiceStaffConflictDto>(err)
      if (c?.session && (c.code === 'VersionMismatch' || c.code === 'InvalidTransition')) {
        applyCard(c.session)
        setBanner(c.message)
        return
      }
      setBanner(getStayErrorMessage(err, 'Не удалось выполнить действие.'))
    },
  })

  if (!can(company.myPermissions, 'ViewBookings')) return <NotFound title="Раздел недоступен" />
  if (q.isLoading) {
    return (
      <main className="mx-auto max-w-[900px] px-4 pt-8 sm:px-8">
        <LoadingList rows={3} />
      </main>
    )
  }
  if (q.isError && httpStatus(q.error) === 404) return <NotFound title="Сеанс не найден" hint="Его нет в этой компании." />
  if (q.isError || !card) {
    return (
      <main className="mx-auto max-w-[760px] px-4 pt-8 sm:px-8">
        <ErrorState message={getStayErrorMessage(q.error, 'Не удалось загрузить сеанс.')} onRetry={() => void q.refetch()} />
      </main>
    )
  }

  const allowed = (a: ServiceStaffAction) => canManage && card.availableActions.includes(a)

  return (
    <main className="mx-auto max-w-[900px] px-4 pb-8 pt-6 sm:px-8">
      <Link to={paths.cabinetDay(company.id, card.time.businessDate)} className="text-xs font-medium text-ink-soft hover:text-gold-dark">
        ← День услуг
      </Link>

      <header className="mt-1 flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="font-serif text-[28px] leading-tight text-ink">{card.serviceName}</h2>
          <p className="mt-0.5 text-sm text-ink-soft" data-testid="session-time">
            {card.time.label}
          </p>
          <p className="text-xs text-muted">Подготовка занимает время до {card.preparedUntilLabel}</p>
        </div>
        {card.displayStatus ? <StatusBadge status={card.displayStatus} text={card.statusText} className="text-sm" /> : <span className="rounded-full bg-cream-deep px-3 py-1 text-sm font-semibold text-ink-soft">{card.statusText}</span>}
      </header>

      {banner && (
        <p role="alert" className="mt-4 rounded-2xl bg-warning-bg px-5 py-3 text-sm text-warning" data-testid="card-banner">
          {banner}
        </p>
      )}

      {card.orderStatus === 'Held' && card.holdExpiresAtUtc && (
        <p className="mt-4 rounded-2xl bg-warning-bg px-5 py-3 text-sm text-warning">Ждём оплату до {fmtDateTime(card.holdExpiresAtUtc)}. Если гость не успеет, время освободится само.</p>
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
            <dt className="text-muted">Заказ</dt>
            <dd className="text-ink">{card.isManual ? 'создан вручную' : KIND_TEXT[card.addedBy.kind]}</dd>
            {card.booking && (
              <>
                <dt className="text-muted">{words.stayBookingLabel}</dt>
                <dd className="text-ink">
                  {paths.cabinetBooking(company.id, card.booking.id) ? (
                    <Link to={paths.cabinetBooking(company.id, card.booking.id)!} className="font-medium !text-ink underline">
                      {card.booking.houseName}
                    </Link>
                  ) : (
                    <span className="font-medium">{card.booking.houseName}</span>
                  )}{' '}
                  · {card.booking.statusText}
                </dd>
              </>
            )}
            {card.guestsCount != null && (
              <>
                <dt className="text-muted">Гостей</dt>
                <dd className="text-ink">{card.guestsCount}</dd>
              </>
            )}
            <dt className="text-muted">Добавил</dt>
            <dd className="text-ink">{card.addedBy.text}</dd>
            {card.comment && (
              <>
                <dt className="text-muted">Комментарий</dt>
                <dd className="whitespace-pre-line text-ink">{card.comment}</dd>
              </>
            )}
          </dl>
        </section>

        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="m-h">
          <h3 id="m-h" className="mb-2 text-[15px] font-semibold text-ink">
            Стоимость
          </h3>
          <dl className="text-sm">
            {card.lines.map((l, i) => (
              <div key={`${l.kind}-${i}`} className="flex items-baseline justify-between gap-4 border-b border-line/70 py-2">
                <dt className="text-ink-soft">{l.label}</dt>
                <dd className="shrink-0 font-medium tabular-nums text-ink">{l.amountRub === 0 ? 'бесплатно' : formatRub(l.amountRub)}</dd>
              </div>
            ))}
            <div className="flex items-baseline justify-between gap-4 py-2.5">
              <dt className="font-semibold text-ink">Итого</dt>
              <dd className="text-lg font-semibold tabular-nums text-ink">{formatRub(card.totalRub)}</dd>
            </div>
            <div className="flex items-baseline justify-between gap-4 rounded-xl bg-cream-deep px-3 py-2">
              <dt className="text-ink">{card.prepayRub > 0 ? `Предоплата${card.prepayPercent ? ` ${card.prepayPercent} %` : ''}` : 'Предоплата не нужна'}</dt>
              <dd className="tabular-nums text-ink">{card.prepayRub > 0 ? formatRub(card.prepayRub) : `на месте ${formatRub(card.dueOnSiteRub)}`}</dd>
            </div>
          </dl>
          <p className="mt-2 text-xs text-muted">Часы: {card.hourPrices.map((h) => `${h.label} — ${formatRub(h.priceRub)}`).join('; ')}</p>
          {card.paymentConfirmed && (
            <p className="mt-2 text-xs text-success">
              Оплату подтвердил {card.paymentConfirmed.byName}, {fmtDateTime(card.paymentConfirmed.atUtc)}
            </p>
          )}
        </section>

        {card.orderStatus && (
          <section className="rounded-2xl border border-line bg-white p-5 md:col-span-2" aria-labelledby="p-h">
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
                      fetchBlob={() => staysBoardApi.sessionProofBlob(company.id, sessionId, p.id)}
                    />
                  </li>
                ))}
              </ul>
            )}
          </section>
        )}
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

function ReasonDialog({ kind, card, pending, onClose, onSubmit }: { kind: 'reject' | 'cancel'; card: StaffServiceSessionCardDto; pending: boolean; onClose: () => void; onSubmit: (reason: string) => void }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState('')
  return (
    <Modal title={kind === 'reject' ? 'Отклонить оплату' : 'Отменить сеанс'} onClose={onClose} dismissible={!pending}>
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
        {kind === 'cancel' && card.ownerCancelRefundText && (
          <p className="rounded-xl bg-cream-deep px-4 py-3 text-sm font-medium text-ink" data-testid="owner-refund-text">
            {card.ownerCancelRefundText}
          </p>
        )}
        {kind === 'reject' && <p className="text-sm text-ink-soft">Если деньги всё же пришли, верните их или восстановите заказ.</p>}
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
            {kind === 'reject' ? 'Отклонить оплату' : 'Отменить сеанс'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
