import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { CompanyMapLinks } from '@/components/company/CompanyMapLinks'
import { formatPhone, telHref } from '@/utils/phone'
import { formatRub } from '@/utils/money'
import { guestBookingsApi } from '../api/guestBookings'
import { CancelBookingDialog } from '../components/CancelBookingDialog'
import { CopyButton } from '../components/CopyButton'
import { GuestPushCard } from '../components/GuestPushCard'
import { HoldCountdown } from '../components/HoldCountdown'
import { PriceBreakdown } from '../components/PriceBreakdown'
import { ProofFileButton } from '../components/ProofFileButton'
import { ProofUploader } from '../components/ProofUploader'
import { ProviderBlock } from '../components/ProviderBlock'
import { ErrorState, Skeleton } from '../components/StatePanels'
import { StayNotice } from '../components/StayNotice'
import { StayTermsModal } from '../components/StayTermsModal'
import type { PublicStayBookingDto, StayGuestConflictDto } from '../types'
import { fmtDateTime } from '@/utils/dateFormat'
import { formatDateWithWeekday, nightsLabel } from '../utils/stayDates'
import { getStayErrorMessage, httpStatus, readConflict } from '../utils/stayError'
import { BOOKING_POLL_MS, TONE_CLASSES, isTerminal, statusTone } from '../utils/stayStatus'
import { NotFoundPage } from './NotFoundPage'

/**
 * `/b/:token` — the guest's booking (US-37-07…09, no login: the token in the address is the access, so the page says «не пересылайте»).
 * Polls every 15 s while the booking is alive and at once when the tab comes back; the hold timer is the server's clock. Shows the
 * payment details (only here and in this booking's messages — Т37-04), the payment proofs, the outcome text of a final status, the
 * check-in information once the server releases it, the cancellation with the refund «не меньше X ₽», and browser push.
 */
export function BookingPage() {
  const { token = '' } = useParams<{ token: string }>()
  const location = useLocation()
  const justCreated = (location.state as { justCreated?: boolean } | null)?.justCreated === true
  const qc = useQueryClient()
  const key = ['stay-booking', token]
  const [termsOpen, setTermsOpen] = useState(false)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [cancelError, setCancelError] = useState('')
  const [proofRefusal, setProofRefusal] = useState('')

  const query = useQuery({
    queryKey: key,
    queryFn: () => guestBookingsApi.get(token),
    staleTime: 0, // a tab that comes back refetches at once (visibilitychange → window focus)
    refetchInterval: (q) => (q.state.data && isTerminal(q.state.data.displayStatus) ? false : BOOKING_POLL_MS),
    retry: (count, err) => httpStatus(err) === undefined && count < 2,
  })
  const booking = query.data

  const setBooking = useCallback((b: PublicStayBookingDto) => qc.setQueryData(key, b), [qc, token]) // eslint-disable-line react-hooks/exhaustive-deps
  const refetch = useCallback(() => void query.refetch(), [query])

  const cancel = useMutation({
    mutationFn: () => guestBookingsApi.cancel(token),
    onSuccess: (b) => {
      setBooking(b)
      setCancelOpen(false)
      setCancelError('')
    },
    onError: (err) => {
      const c = readConflict<StayGuestConflictDto>(err)
      if (c?.booking) setBooking(c.booking)
      setCancelError(getStayErrorMessage(err, 'Не удалось отменить бронь.'))
    },
  })

  useEffect(() => {
    if (booking) document.title = `Бронь — ${booking.house.name}`
    return () => {
      document.title = 'ezbook · Дома'
    }
  }, [booking])

  if (query.isLoading) {
    return (
      <main className="mx-auto max-w-[720px] px-4 pt-10">
        <Skeleton className="mb-4 h-40" />
        <Skeleton className="h-64" />
      </main>
    )
  }
  if (query.isError && !booking) {
    if (httpStatus(query.error) === 404) return <NotFoundPage title="Бронь не найдена" hint="Проверьте ссылку: она должна быть скопирована целиком." />
    return (
      <main className="mx-auto max-w-[720px] px-4 pt-10">
        <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить бронь.')} onRetry={() => void query.refetch()} />
      </main>
    )
  }
  if (!booking) return null

  const b = booking
  const tone = TONE_CLASSES[statusTone(b.displayStatus)]
  const terminal = isTerminal(b.displayStatus)
  const holding = b.status === 'Held' && !!b.holdExpiresAtUtc
  const companyPhone = b.company.phone

  return (
    <main className="mx-auto max-w-[760px] px-4 pb-4 pt-8 sm:px-8">
      {justCreated && b.displayStatus === 'Held' && (
        <p className="mb-4 rounded-2xl bg-success-bg px-5 py-3 text-sm text-success" role="status">
          Бронь создана, даты удерживаются за вами. Сохраните эту ссылку — по ней бронь всегда можно открыть.
        </p>
      )}

      <header className="flex items-start gap-4">
        {b.house.coverUrl ? (
          <img src={b.house.coverUrl} alt="" className="h-20 w-20 shrink-0 rounded-2xl object-cover" />
        ) : (
          <span className="flex h-20 w-20 shrink-0 items-center justify-center rounded-2xl bg-cream-deep">
            <Icon name="home" size={26} className="text-muted" strokeWidth={1.4} />
          </span>
        )}
        <div className="min-w-0">
          <p className="text-xs font-semibold uppercase tracking-wide text-gold-dark">Ваша бронь</p>
          <h1 className="font-serif text-[28px] leading-tight text-ink sm:text-[34px]">
            <Link to={b.house.url} className="!text-ink hover:underline">
              {b.house.name}
            </Link>
          </h1>
          <p className="mt-1 text-sm text-ink-soft">
            {formatDateWithWeekday(b.checkInDate)} → {formatDateWithWeekday(b.checkOutDate)} · {nightsLabel(b.nights)}
          </p>
        </div>
      </header>

      <div className={`mt-5 rounded-2xl px-5 py-4 ${tone}`} data-testid="booking-status" data-status={b.displayStatus}>
        <p className="text-xs font-semibold uppercase tracking-wide opacity-80">Статус</p>
        <p className="mt-0.5 text-xl font-semibold">{b.statusText}</p>
      </div>

      {proofRefusal && (
        <p role="alert" className="mt-4 rounded-2xl bg-danger-bg px-5 py-3 text-sm text-danger" data-testid="proof-refusal">
          {proofRefusal}
        </p>
      )}

      <div className="mt-6 flex flex-col gap-5">
        {holding && b.holdExpiresAtUtc && (
          <section className="rounded-2xl border border-warning/30 bg-white p-5" aria-label="Время на оплату">
            <p className="mb-2 text-sm font-semibold text-ink">Даты удерживаются за вами. Осталось на оплату:</p>
            <HoldCountdown expiresAtUtc={b.holdExpiresAtUtc} serverTimeUtc={b.serverTimeUtc} receivedAtMs={query.dataUpdatedAt} onExpire={refetch} />
            <ol className="mt-4 list-decimal space-y-1 pl-5 text-sm text-ink-soft">
              <li>Внесите предоплату {formatRub(b.prepayRub)} по реквизитам ниже.</li>
              <li>Приложите подтверждение оплаты — квитанцию или скриншот перевода.</li>
              <li>Компания проверит оплату и подтвердит бронь.</li>
            </ol>
          </section>
        )}

        {b.status === 'AwaitingPaymentCheck' && (
          <p className="rounded-2xl border border-line bg-white px-5 py-4 text-sm text-ink-soft" role="status">
            Подтверждение оплаты получено — компания проверяет перевод. Страница обновится сама; можно закрыть её и вернуться по ссылке.
          </p>
        )}

        {terminal && (b.outcomeText || b.statusReason) && (
          <section className="rounded-2xl border border-line bg-white p-5" aria-label="Что дальше" data-testid="booking-outcome">
            {b.outcomeText && <p className="whitespace-pre-line text-sm leading-relaxed text-ink">{b.outcomeText}</p>}
            {b.statusReason && (
              <p className="mt-2 text-sm text-ink-soft">
                <span className="font-semibold text-ink">Причина:</span> {b.statusReason}
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

        {b.payment && (
          <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="payment-title" data-testid="payment-details">
            <h2 id="payment-title" className="mb-3 text-[15px] font-semibold text-ink">
              Оплата
            </h2>
            <dl className="flex flex-col gap-3 text-sm">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <dt className="text-xs text-muted">Сумма предоплаты</dt>
                  <dd className="text-lg font-semibold text-ink">{formatRub(b.payment.amountRub)}</dd>
                </div>
                <CopyButton text={String(b.payment.amountRub)} label="сумму" />
              </div>
              {b.payment.details && (
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <dt className="text-xs text-muted">Реквизиты</dt>
                    <dd className="whitespace-pre-line break-words text-ink">{b.payment.details}</dd>
                  </div>
                  <CopyButton text={b.payment.details} label="реквизиты" />
                </div>
              )}
              {b.payment.purpose && (
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <dt className="text-xs text-muted">Назначение платежа</dt>
                    <dd className="break-words text-ink">{b.payment.purpose}</dd>
                  </div>
                  <CopyButton text={b.payment.purpose} label="назначение платежа" />
                </div>
              )}
            </dl>
            <StayNotice textKey="StayPaymentProofNotice" className="mt-4" />
          </section>
        )}

        {(b.paymentProofs.length > 0 || b.proofs.canAttach) && (
          <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="proofs-title">
            <h2 id="proofs-title" className="mb-3 text-[15px] font-semibold text-ink">
              Подтверждение оплаты
            </h2>
            {b.paymentProofs.length > 0 && (
              <ul className="mb-4 flex flex-col gap-3">
                {b.paymentProofs.map((p, i) => (
                  <li key={p.id}>
                    <ProofFileButton
                      contentType={p.contentType}
                      sizeBytes={p.sizeBytes}
                      purged={p.purged}
                      title={`Файл ${i + 1}, ${fmtDateTime(p.uploadedAtUtc)}`}
                      fetchBlob={() => guestBookingsApi.proofBlob(token, p.id)}
                    />
                  </li>
                ))}
              </ul>
            )}
            <ProofUploader token={token} booking={b} onBooking={setBooking} onRefusal={setProofRefusal} />
          </section>
        )}

        {b.checkInInfo && (b.checkInInfo.companyText || b.checkInInfo.houseText) && (
          <section className="rounded-2xl border border-success/30 bg-success-bg/40 p-5" aria-labelledby="checkin-title" data-testid="checkin-info">
            <h2 id="checkin-title" className="mb-2 text-[15px] font-semibold text-ink">
              Информация к заселению
            </h2>
            {b.checkInInfo.companyText && <p className="whitespace-pre-line text-sm leading-relaxed text-ink">{b.checkInInfo.companyText}</p>}
            {b.checkInInfo.houseText && <p className="mt-2 whitespace-pre-line text-sm leading-relaxed text-ink">{b.checkInInfo.houseText}</p>}
          </section>
        )}

        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="stay-title">
          <h2 id="stay-title" className="mb-3 text-[15px] font-semibold text-ink">
            Проживание
          </h2>
          <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[170px_1fr]">
            <dt className="text-muted">Заезд</dt>
            <dd className="text-ink">
              {formatDateWithWeekday(b.checkInDate)}, с {b.checkInTime}
            </dd>
            <dt className="text-muted">Выезд</dt>
            <dd className="text-ink">
              {formatDateWithWeekday(b.checkOutDate)}, до {b.checkOutTime}
            </dd>
            <dt className="text-muted">Гости</dt>
            <dd className="text-ink">
              {b.adults} взр.{b.children > 0 ? `, ${b.children} дет.` : ''}
              {b.dogs > 0 ? `, собак: ${b.dogs}` : ''}
              {b.needCot ? ', детская кроватка' : ''}
              {b.extraBeds > 0 ? `, доп. мест: ${b.extraBeds}` : ''}
            </dd>
            {b.arrivalTime && (
              <>
                <dt className="text-muted">Прибытие</dt>
                <dd className="text-ink">около {b.arrivalTime}</dd>
              </>
            )}
            <dt className="text-muted">Гость</dt>
            <dd className="text-ink">
              {b.guestName ?? '—'}
              {b.guestPhoneMasked ? `, ${b.guestPhoneMasked}` : ''}
            </dd>
            {b.comment && (
              <>
                <dt className="text-muted">Комментарий</dt>
                <dd className="whitespace-pre-line text-ink">{b.comment}</dd>
              </>
            )}
            {b.house.address && (
              <>
                <dt className="text-muted">Адрес</dt>
                <dd className="text-ink">{b.house.address}</dd>
              </>
            )}
          </dl>
          <CompanyMapLinks yandexUrl={b.house.yandexMapsUrl} twoGisUrl={b.house.twoGisUrl} className="mt-2" />
        </section>

        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="price-title">
          <h2 id="price-title" className="mb-2 text-[15px] font-semibold text-ink">
            Стоимость
          </h2>
          <PriceBreakdown lines={b.lines} totalRub={b.totalRub} prepayPercent={b.prepayPercent} prepayRub={b.prepayRub} dueAtCheckInRub={b.dueAtCheckInRub} />
          <StayNotice textKey="StayTouristTaxNotice" variant="plain" className="mt-2" />
        </section>

        <ProviderBlock provider={b.provider} />

        <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="company-title">
          <h2 id="company-title" className="mb-2 text-[15px] font-semibold text-ink">
            Компания
          </h2>
          <p className="text-sm text-ink">{b.company.name}</p>
          {companyPhone && (
            <a href={telHref(companyPhone) || undefined} className="mt-1 inline-flex min-h-[44px] items-center gap-2 text-sm font-semibold !text-ink">
              <Icon name="phone" size={15} strokeWidth={1.7} className="text-gold-dark" />
              {formatPhone(companyPhone)}
            </a>
          )}
        </section>

        {!terminal && <GuestPushCard token={token} info={b.notifications.webPush} />}

        {!terminal && (
          <section className="rounded-2xl border border-line bg-white p-5" aria-labelledby="cancel-title">
            <h2 id="cancel-title" className="mb-1 text-[15px] font-semibold text-ink">
              Отмена
            </h2>
            <p className="text-sm text-ink-soft">{b.cancellation.summary}</p>
            {b.cancellation.canCancel ? (
              <Button
                variant="danger"
                className="mt-3 min-h-[44px]"
                onClick={() => {
                  setCancelError('')
                  // Open on a fresh answer: the refund amount depends on the server's time.
                  void query.refetch().then(() => setCancelOpen(true))
                }}
              >
                Отменить бронь
              </Button>
            ) : (
              b.cancellation.cannotCancelText && <p className="mt-2 text-sm text-ink">{b.cancellation.cannotCancelText}</p>
            )}
          </section>
        )}

        <p className="text-xs leading-relaxed text-muted">
          <button type="button" onClick={() => setTermsOpen(true)} className="font-semibold text-gold-dark underline">
            Условия бронирования и проживания
          </button>
          . Кто знает ссылку на эту страницу, тот видит бронь и может её отменить — не пересылайте её посторонним.
        </p>
      </div>

      {cancelOpen && (
        <CancelBookingDialog
          booking={b}
          pending={cancel.isPending}
          error={cancelError}
          onConfirm={() => cancel.mutate()}
          onClose={() => setCancelOpen(false)}
        />
      )}
      {termsOpen && <StayTermsModal companyName={b.company.name} onClose={() => setTermsOpen(false)} />}
    </main>
  )
}
