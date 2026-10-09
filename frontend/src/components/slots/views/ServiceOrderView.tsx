import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { CompanyMapLinks } from '@/components/company/CompanyMapLinks'
import { fmtDateTime } from '@/utils/dateFormat'
import { formatPhone, telHref } from '@/utils/phone'
import { formatRub } from '@/utils/money'
import { CopyButton } from '@/components/slots/ui/CopyButton'
import { GuestPushCard } from '@/components/slots/ui/GuestPushCard'
import { HoldCountdown } from '@/components/slots/ui/HoldCountdown'
import { ProofFileButton } from '@/components/slots/ui/ProofFileButton'
import { ProofUploader } from '@/components/slots/ui/ProofUploader'
import { ProviderBlock } from '@/components/slots/ui/ProviderBlock'
import { ErrorState, Skeleton } from '@/components/slots/ui/StatePanels'
import { SlotNotice } from '@/components/slots/ui/SlotNotice'
import { CancelSessionDialog } from '@/components/slots/services/CancelSessionDialog'
import { ServiceTermsModal } from '@/components/slots/services/ServiceTermsModal'
import type { PublicServiceOrderDto, ServiceOrderGuestConflictDto } from '@/types/slots'
import { getStayErrorMessage, httpStatus, readConflict } from '@/utils/slots/slotError'
import { BOOKING_POLL_MS, TONE_CLASSES, isTerminal, statusTone } from '@/utils/slots/slotStatus'
import { useCabinetWords, useGuestWords, useSlotVertical } from '@/components/slots/SlotVerticalContext'

/**
 * `/s/:token` — a separate session of the guest (US-39-12/13, no login: the token is the access, the page says «не пересылайте»).
 * Polls every 15 s while the order is alive and at once when the tab comes back; the hold timer is the server's clock. The payment
 * details, the proofs, the outcome text, the cancellation with the server's refund text and the browser push work as on the booking
 * page; no tourist tax here (Т39-15). Time is shown as the server's guest label — two calendar dates across midnight.
 */
export function ServiceOrderView() {
  const { api, words, legal, NotFound } = useSlotVertical()
  const gw = useGuestWords()
  const cw = useCabinetWords()
  const serviceOrdersApi = api.orders
  const { token = '' } = useParams<{ token: string }>()
  const location = useLocation()
  const justCreated = (location.state as { justCreated?: boolean } | null)?.justCreated === true
  const qc = useQueryClient()
  const key = ['stay-service-order', token]
  const [termsOpen, setTermsOpen] = useState(false)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [cancelError, setCancelError] = useState('')
  const [proofRefusal, setProofRefusal] = useState('')

  const query = useQuery({
    queryKey: key,
    queryFn: () => serviceOrdersApi.get(token),
    staleTime: 0,
    refetchInterval: (q) => (q.state.data && isTerminal(q.state.data.displayStatus) ? false : BOOKING_POLL_MS),
    retry: (count, err) => httpStatus(err) === undefined && count < 2,
  })
  const order = query.data

  const setOrder = useCallback((o: PublicServiceOrderDto) => qc.setQueryData(key, o), [qc, token]) // eslint-disable-line react-hooks/exhaustive-deps
  const refetch = useCallback(() => void query.refetch(), [query])

  const cancel = useMutation({
    mutationFn: () => serviceOrdersApi.cancel(token),
    onSuccess: (o) => {
      setOrder(o)
      setCancelOpen(false)
      setCancelError('')
    },
    onError: (err) => {
      const c = readConflict<ServiceOrderGuestConflictDto>(err)
      if (c?.order) setOrder(c.order)
      setCancelError(getStayErrorMessage(err, gw.cancelError))
    },
  })

  useEffect(() => {
    if (order) document.title = `${gw.titlePrefix} — ${order.service.name}`
    return () => {
      document.title = words.brandTitle
    }
  }, [order, words, gw.titlePrefix])

  if (query.isLoading) {
    return (
      <main className="mx-auto max-w-[720px] px-4 pt-10">
        <Skeleton className="mb-4 h-40" />
        <Skeleton className="h-64" />
      </main>
    )
  }
  if (query.isError && !order) {
    if (httpStatus(query.error) === 404) return <NotFound title={gw.notFoundTitle} hint="Проверьте ссылку: она должна быть скопирована целиком." />
    return (
      <main className="mx-auto max-w-[720px] px-4 pt-10">
        <ErrorState message={getStayErrorMessage(query.error, gw.loadError)} onRetry={() => void query.refetch()} />
      </main>
    )
  }
  if (!order) return null

  const o = order
  const tone = TONE_CLASSES[statusTone(o.displayStatus)]
  const terminal = isTerminal(o.displayStatus)
  const holding = o.status === 'Held' && !!o.holdExpiresAtUtc
  const companyPhone = o.company.phone

  return (
    <main className="mx-auto max-w-[760px] px-4 pb-4 pt-8 sm:px-8">
      {justCreated && o.displayStatus === 'Held' && (
        <p className="mb-4 rounded-2xl bg-success-bg px-5 py-3 text-sm text-success" role="status">
          {gw.createdHeld}
        </p>
      )}
      {justCreated && o.displayStatus === 'Confirmed' && (
        <p className="mb-4 rounded-2xl bg-success-bg px-5 py-3 text-sm text-success" role="status">
          {gw.createdConfirmed}
        </p>
      )}

      <header className="flex items-start gap-4">
        {o.service.coverUrl ? (
          <img src={o.service.coverUrl} alt="" className="h-20 w-20 shrink-0 rounded-2xl object-cover" />
        ) : (
          <span className="flex h-20 w-20 shrink-0 items-center justify-center rounded-2xl bg-cream-deep">
            <Icon name="calendar" size={26} className="text-muted" strokeWidth={1.4} />
          </span>
        )}
        <div className="min-w-0">
          <p className="text-xs font-semibold uppercase tracking-wide text-gold-dark">{gw.eyebrow}</p>
          <h1 className="font-serif text-[28px] leading-tight text-ink sm:text-[34px]">
            <Link to={o.service.url} className="!text-ink hover:underline">
              {o.service.name}
            </Link>
          </h1>
          <p className="mt-1 text-sm text-ink-soft" data-testid="session-time">
            {o.time.label}
          </p>
          {o.localTimeNote && (
            <p className="text-xs text-muted" data-testid="local-time-note">
              {o.localTimeNote}
            </p>
          )}
        </div>
      </header>

      <div className={`mt-5 rounded-2xl px-5 py-4 ${tone}`} data-testid="order-status" data-status={o.displayStatus}>
        <p className="text-xs font-semibold uppercase tracking-wide opacity-80">Статус</p>
        <p className="mt-0.5 text-xl font-semibold">{o.statusText}</p>
      </div>

      {proofRefusal && (
        <p role="alert" className="mt-4 rounded-2xl bg-danger-bg px-5 py-3 text-sm text-danger" data-testid="proof-refusal">
          {proofRefusal}
        </p>
      )}

      <div className="mt-6 flex flex-col gap-5">
        {holding && o.holdExpiresAtUtc && (
          <section className="rounded-2xl border border-warning/30 bg-white p-5" aria-label="Время на оплату">
            <p className="mb-2 text-sm font-semibold text-ink">Время удерживается за вами. Осталось на оплату:</p>
            <HoldCountdown expiresAtUtc={o.holdExpiresAtUtc} serverTimeUtc={o.serverTimeUtc} receivedAtMs={query.dataUpdatedAt} onExpire={refetch} />
            <ol className="mt-4 list-decimal space-y-1 pl-5 text-sm text-ink-soft">
              <li>Внесите предоплату {formatRub(o.prepayRub)} по реквизитам ниже.</li>
              <li>Приложите подтверждение оплаты — квитанцию или скриншот перевода.</li>
              <li>{gw.companyChecks}</li>
            </ol>
          </section>
        )}

        {o.status === 'AwaitingPaymentCheck' && (
          <p className="rounded-2xl border border-line bg-white px-5 py-4 text-sm text-ink-soft" role="status">
            Подтверждение оплаты получено — компания проверяет перевод. Страница обновится сама; можно закрыть её и вернуться по ссылке.
          </p>
        )}

        {terminal && (o.outcomeText || o.statusReason) && (
          <section className="rounded-2xl border border-line bg-white p-5" aria-label="Что дальше" data-testid="order-outcome">
            {o.outcomeText && <p className="whitespace-pre-line text-sm leading-relaxed text-ink">{o.outcomeText}</p>}
            {o.statusReason && (
              <p className="mt-2 text-sm text-ink-soft">
                <span className="font-semibold text-ink">Причина:</span> {o.statusReason}
              </p>
            )}
            {companyPhone && (
              <p className="mt-3 text-sm">
                Телефон компании:{' '}
                <a href={telHref(companyPhone) || undefined} className="font-semibold !text-ink">
                  {formatPhone(companyPhone)}
                </a>
              </p>
            )}
          </section>
        )}

        {o.sessionReminder && (
          <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="reminder-title" data-testid="session-reminder">
            <h2 id="reminder-title" className="mb-1 text-[15px] font-semibold text-ink">
              Напоминание
            </h2>
            <p className="whitespace-pre-line text-sm text-ink">{o.sessionReminder.text}</p>
            {o.sessionReminder.sentAtUtc && <p className="mt-1 text-xs text-muted">Отправлено {fmtDateTime(o.sessionReminder.sentAtUtc)}</p>}
          </section>
        )}

        {o.payment && (
          <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="payment-title" data-testid="payment-details">
            <h2 id="payment-title" className="mb-3 text-[15px] font-semibold text-ink">
              Оплата
            </h2>
            <dl className="flex flex-col gap-3 text-sm">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <dt className="text-xs text-muted">Сумма предоплаты</dt>
                  <dd className="text-lg font-semibold text-ink">{formatRub(o.payment.amountRub)}</dd>
                </div>
                <CopyButton text={String(o.payment.amountRub)} label="сумму" />
              </div>
              {o.payment.details && (
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <dt className="text-xs text-muted">Реквизиты</dt>
                    <dd className="whitespace-pre-line break-words text-ink">{o.payment.details}</dd>
                  </div>
                  <CopyButton text={o.payment.details} label="реквизиты" />
                </div>
              )}
              {o.payment.purpose && (
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <dt className="text-xs text-muted">Назначение платежа</dt>
                    <dd className="break-words text-ink">{o.payment.purpose}</dd>
                  </div>
                  <CopyButton text={o.payment.purpose} label="назначение платежа" />
                </div>
              )}
            </dl>
            <SlotNotice textKey={legal.keys.paymentProofNotice} className="mt-4" />
          </section>
        )}

        {(o.paymentProofs.length > 0 || o.proofs.canAttach) && (
          <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="proofs-title">
            <h2 id="proofs-title" className="mb-3 text-[15px] font-semibold text-ink">
              Подтверждение оплаты
            </h2>
            {o.paymentProofs.length > 0 && (
              <ul className="mb-4 flex flex-col gap-3">
                {o.paymentProofs.map((p, i) => (
                  <li key={p.id}>
                    <ProofFileButton
                      contentType={p.contentType}
                      sizeBytes={p.sizeBytes}
                      purged={p.purged}
                      title={`Файл ${i + 1}, ${fmtDateTime(p.uploadedAtUtc)}`}
                      fetchBlob={() => serviceOrdersApi.proofBlob(token, p.id)}
                    />
                  </li>
                ))}
              </ul>
            )}
            <ProofUploader token={token} booking={o} onBooking={setOrder} onRefusal={setProofRefusal} upload={serviceOrdersApi.uploadProof} />
          </section>
        )}

        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="session-title">
          <h2 id="session-title" className="mb-3 text-[15px] font-semibold text-ink">
            {cw.sessionTitle}
          </h2>
          <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[170px_1fr]">
            <dt className="text-muted">Время</dt>
            <dd className="text-ink">{o.time.label}</dd>
            <dt className="text-muted">Длительность</dt>
            <dd className="text-ink">{o.time.hours} ч</dd>
            {o.guestsCount != null && (
              <>
                <dt className="text-muted">Гостей</dt>
                <dd className="text-ink">{o.guestsCount}</dd>
              </>
            )}
            {o.items.length > 0 && (
              <>
                <dt className="text-muted">Дополнительно</dt>
                <dd className="text-ink">{o.items.map((i) => `${i.name} × ${i.quantity}`).join(', ')}</dd>
              </>
            )}
            <dt className="text-muted">Гость</dt>
            <dd className="text-ink">
              {o.guestName ?? '—'}
              {o.guestPhoneMasked ? `, ${o.guestPhoneMasked}` : ''}
            </dd>
            {o.comment && (
              <>
                <dt className="text-muted">Комментарий</dt>
                <dd className="whitespace-pre-line text-ink">{o.comment}</dd>
              </>
            )}
            {o.company.address && (
              <>
                <dt className="text-muted">Адрес</dt>
                <dd className="text-ink">{o.company.address}</dd>
              </>
            )}
          </dl>
          <CompanyMapLinks yandexUrl={o.company.yandexMapsUrl} twoGisUrl={o.company.twoGisUrl} className="mt-2" />
        </section>

        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="price-title">
          <h2 id="price-title" className="mb-2 text-[15px] font-semibold text-ink">
            Стоимость
          </h2>
          <dl className="text-sm">
            {o.lines.map((l, i) => (
              <div key={`${l.kind}-${i}`} className="flex items-baseline justify-between gap-4 border-b border-line/70 py-2">
                <dt className="text-ink-soft">{l.label}</dt>
                <dd className="shrink-0 font-medium tabular-nums text-ink">{l.amountRub === 0 ? 'бесплатно' : formatRub(l.amountRub)}</dd>
              </div>
            ))}
            <div className="flex items-baseline justify-between gap-4 py-2.5">
              <dt className="font-semibold text-ink">Итого</dt>
              <dd className="text-lg font-semibold tabular-nums text-ink">{formatRub(o.totalRub)}</dd>
            </div>
            {o.prepayRub > 0 ? (
              <>
                <div className="flex items-baseline justify-between gap-4 rounded-xl bg-cream-deep px-3 py-2">
                  <dt className="text-ink">Предоплата{o.prepayPercent ? ` ${o.prepayPercent} %` : ''}</dt>
                  <dd className="font-semibold tabular-nums text-ink">{formatRub(o.prepayRub)}</dd>
                </div>
                <div className="flex items-baseline justify-between gap-4 px-3 py-2">
                  <dt className="text-ink-soft">На месте</dt>
                  <dd className="tabular-nums text-ink">{formatRub(o.dueOnSiteRub)}</dd>
                </div>
              </>
            ) : (
              <div className="rounded-xl bg-cream-deep px-3 py-2 text-ink">Оплата на месте, в компании. Через сервис оплата не производится.</div>
            )}
          </dl>
        </section>

        <ProviderBlock provider={o.provider} />

        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="company-title">
          <h2 id="company-title" className="mb-2 text-[15px] font-semibold text-ink">
            Компания
          </h2>
          <p className="text-sm text-ink">
            <Link to={o.company.url} className="!text-ink hover:underline">
              {o.company.name}
            </Link>
          </p>
          {companyPhone && (
            <a href={telHref(companyPhone) || undefined} className="mt-1 inline-flex min-h-[44px] items-center gap-2 text-sm font-semibold !text-ink">
              <Icon name="phone" size={15} strokeWidth={1.7} className="text-gold-dark" />
              {formatPhone(companyPhone)}
            </a>
          )}
        </section>

        {!terminal && <GuestPushCard kind="order" token={token} info={o.notifications.webPush} api={serviceOrdersApi} />}

        {!terminal && (
          <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="cancel-title">
            <h2 id="cancel-title" className="mb-1 text-[15px] font-semibold text-ink">
              Отмена
            </h2>
            <p className="text-sm text-ink-soft">{o.cancellation.summary}</p>
            {o.cancellation.canCancel ? (
              <Button
                variant="danger"
                className="mt-3 min-h-[44px]"
                onClick={() => {
                  setCancelError('')
                  // Open on a fresh answer: the refund text depends on the server's time.
                  void query.refetch().then(() => setCancelOpen(true))
                }}
              >
                Отменить сеанс
              </Button>
            ) : (
              o.cancellation.cannotCancelText && <p className="mt-2 text-sm text-ink">{o.cancellation.cannotCancelText}</p>
            )}
          </section>
        )}

        {o.bookAgainUrl && (
          <Link
            to={o.bookAgainUrl}
            className="inline-flex min-h-[44px] items-center justify-center rounded-xl border border-line bg-white px-5 text-sm font-semibold !text-ink hover:border-line-strong"
            data-testid="book-again"
          >
            Забронировать ещё в этом комплексе
          </Link>
        )}

        <p className="text-xs leading-relaxed text-muted">
          <button type="button" onClick={() => setTermsOpen(true)} className="font-semibold text-gold-dark underline">
            Условия оказания услуги
          </button>
          . {gw.linkNote}
        </p>
      </div>

      {cancelOpen && (
        <CancelSessionDialog
          title="Отменить сеанс?"
          refundText={o.cancellation.refund.text}
          refundMadeByCompany={o.cancellation.refund.kind !== 'NothingPaid'}
          summary={o.cancellation.summary}
          phone={companyPhone}
          pending={cancel.isPending}
          error={cancelError}
          onConfirm={() => cancel.mutate()}
          onClose={() => setCancelOpen(false)}
        />
      )}
      {termsOpen && <ServiceTermsModal companyName={o.company.name} onClose={() => setTermsOpen(false)} />}
    </main>
  )
}
