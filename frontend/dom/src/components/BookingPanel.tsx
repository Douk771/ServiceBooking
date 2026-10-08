import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { InlineError } from '@/components/ui/InlineError'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { SmartCaptcha, smartCaptchaEnabled } from '@/components/booking/SmartCaptcha'
import { useAuthStore } from '@/store/authStore'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { formatPhone } from '@/utils/phone'
import { formatRub } from '@/utils/money'
import { publicStaysApi } from '../api/publicStays'
import type { PublicHouseDto, StayQuoteWithServices, StayRefusalWithService } from '../types'
import { ChosenStayService, StayServicesBlock } from './services/StayServicesBlock'
import {
  COMMENT_MAX,
  MAX_ADULTS,
  MAX_CHILDREN,
  MAX_DOGS,
  extraBedsNeeded,
  fieldOfBookingError,
  guestCountsProblem,
  maxGuests,
  toCreateInput,
  toQuoteInput,
  validateGuestFields,
  type GuestCounts,
  type GuestFieldErrors,
  type GuestFields,
} from '../utils/bookingForm'
import { bookingKeyFor, forgetBookingKey } from '../utils/idempotency'
import { arrivalTimeOptions, formatDateShort, formatDateWithWeekday, nightsLabel } from '../utils/stayDates'
import { validateSelection } from '../utils/stayRules'
import { EMPTY_RANGE, selectionProblemText, type StayRange } from '../utils/staySelection'
import { getStayErrorMessage, httpStatus, plainBody, readConflict } from '../utils/stayError'
import { PriceBreakdown } from './PriceBreakdown'
import { Skeleton } from './StatePanels'
import { StayCalendar } from './StayCalendar'
import { StayNotice } from './StayNotice'
import { Stepper } from './Stepper'

export interface BookingPanelInitial {
  checkIn?: string
  checkOut?: string
  adults?: number
  children?: number
}

/**
 * Calendar + guests + price + the guest's details on ONE screen (US-37-06/07, ARCHITECTURE_CYCLE37.md §37.7.1). The price shown is
 * the server's `quote` for exactly these inputs; the booking is created against that total (`expectedTotalRub`), and a changed price
 * comes back as `PriceChanged` with the new quote — the guest confirms it and presses again (same idempotency key). The key lives
 * until success, so a double tap or a lost answer never makes two bookings.
 */
export function BookingPanel({ house, initial, onOpenTerms }: { house: PublicHouseDto; initial: BookingPanelInitial; onOpenTerms: () => void }) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const user = useAuthStore((s) => s.user)
  const authed = useAuthStore((s) => s.isAuthenticated())

  const calendar = useQuery({
    queryKey: ['stays-calendar', house.id],
    queryFn: () => publicStaysApi.calendar(house.id),
  })

  const [range, setRange] = useState<StayRange>(
    initial.checkIn && initial.checkOut ? { checkIn: initial.checkIn, checkOut: initial.checkOut } : EMPTY_RANGE,
  )
  const [calendarMessage, setCalendarMessage] = useState<string | null>(null)
  const [counts, setCounts] = useState<GuestCounts>({
    adults: Math.min(Math.max(initial.adults ?? 2, 1), Math.max(maxGuests(house), 1)),
    children: Math.min(Math.max(initial.children ?? 0, 0), MAX_CHILDREN),
    dogs: 0,
    needCot: false,
  })
  const [guest, setGuest] = useState<GuestFields>({
    name: authed ? `${user?.firstName ?? ''} ${user?.lastName ?? ''}`.trim() : '',
    phone: '',
    arrivalTime: '',
    comment: '',
    notifyByMessenger: false,
  })
  const [captchaToken, setCaptchaToken] = useState('')
  const [captchaKey, setCaptchaKey] = useState(0)
  const [fieldErrors, setFieldErrors] = useState<GuestFieldErrors & { arrival?: string }>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [priceChanged, setPriceChanged] = useState<string | null>(null)
  // Services of the stay (P1, US-39-10): nothing is chosen by default; the choice belongs to ONE range of dates.
  const [stayServices, setStayServices] = useState<ChosenStayService[]>([])
  useEffect(() => setStayServices([]), [range.checkIn, range.checkOut])

  // A range that came in the URL is only a suggestion: drop it when the calendar says it cannot be booked.
  useEffect(() => {
    if (!calendar.data || !range.checkIn || !range.checkOut) return
    const problem = validateSelection(calendar.data, range.checkIn, range.checkOut)
    if (problem !== 'Ok') {
      setRange(EMPTY_RANGE)
      setCalendarMessage(selectionProblemText(problem, calendar.data))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- checked once per loaded calendar
  }, [calendar.data])

  const hasRange = !!range.checkIn && !!range.checkOut
  // A range is quoted only once the calendar has confirmed it can be booked (one that came in the URL may be stale).
  const rangeConfirmed = hasRange && !!calendar.data && validateSelection(calendar.data, range.checkIn!, range.checkOut!) === 'Ok'
  const guestsProblem = guestCountsProblem(house, counts)
  const debouncedCounts = useDebouncedValue(counts, 250)
  const servicesBody = stayServices.length > 0 ? stayServices.map((c) => c.selection) : undefined
  const quoteInput = rangeConfirmed ? { ...toQuoteInput(range.checkIn!, range.checkOut!, debouncedCounts), ...(servicesBody ? { services: servicesBody } : {}) } : null
  const settled = debouncedCounts === counts

  const quoteKey = ['stays-quote', house.id, quoteInput] as const
  const quote = useQuery({
    queryKey: quoteKey,
    queryFn: () => publicStaysApi.quote(house.id, quoteInput!),
    enabled: rangeConfirmed && house.acceptingBookings && !guestsProblem,
    staleTime: 0,
    placeholderData: (prev) => prev,
  })
  const q: StayQuoteWithServices | undefined = quote.data
  const quoteFresh = !!q && !quote.isPlaceholderData && !quote.isFetching && settled

  const idempotencyKey = useMemo(() => bookingKeyFor(house.id), [house.id])
  const anonymous = !authed

  const create = useMutation({
    mutationFn: () =>
      publicStaysApi.createBooking(house.id, {
        ...toCreateInput({
          checkIn: range.checkIn!,
          checkOut: range.checkOut!,
          counts,
          guest,
          anonymous,
          expectedTotalRub: q!.totalRub,
          idempotencyKey,
          captchaToken,
        }),
        ...(servicesBody ? { services: servicesBody } : {}),
      }),
    onSuccess: (res) => {
      forgetBookingKey(house.id)
      void qc.invalidateQueries({ queryKey: ['stays-calendar', house.id] })
      navigate(`/b/${encodeURIComponent(res.token)}`, { replace: true, state: { justCreated: true } })
    },
    onError: (err) => {
      // The captcha token is single-use: whatever the answer was, a new try needs a new solve.
      if (anonymous && smartCaptchaEnabled) {
        setCaptchaToken('')
        setCaptchaKey((k) => k + 1)
      }
      const refusal = readConflict<StayRefusalWithService>(err)
      if (refusal) {
        if (refusal.code === 'PriceChanged' && refusal.quote) {
          qc.setQueryData(quoteKey, refusal.quote)
          setPriceChanged(refusal.message)
          setFormError(null)
          return
        }
        setPriceChanged(null)
        setFormError(refusal.message)
        if (refusal.code === 'ServiceSlotUnavailable' || refusal.code === 'ServiceSelectionInvalid') {
          // Neither the booking nor any session was created; the dates stay, the failed service is dropped from the request.
          const i = refusal.serviceIndex
          setStayServices((list) => (i == null ? [] : list.filter((_, j) => j !== i)))
          return
        }
        if (refusal.code !== 'NotAcceptingBookings' && refusal.code !== 'TooManyGuests' && refusal.code !== 'DogsNotAllowed' && refusal.code !== 'CotNotAvailable') {
          // The dates are no longer bookable: show the fresh calendar and start the dates over.
          setRange(EMPTY_RANGE)
          void qc.invalidateQueries({ queryKey: ['stays-calendar', house.id] })
        }
        return
      }
      const status = httpStatus(err)
      const text = plainBody(err)
      if (status === 400 && text) {
        const field = fieldOfBookingError(text)
        if (field === 'name' || field === 'phone' || field === 'comment' || field === 'captcha' || field === 'arrival') {
          setFieldErrors({ [field]: text })
          setFormError(null)
          return
        }
      }
      setFormError(getStayErrorMessage(err, 'Не удалось оформить бронь. Попробуйте ещё раз.'))
    },
  })

  const submit = () => {
    setFormError(null)
    const errors = validateGuestFields(guest, { anonymous, captchaRequired: smartCaptchaEnabled, captchaToken })
    setFieldErrors(errors)
    if (Object.keys(errors).length > 0 || !q || !quoteFresh || !q.ok) return
    create.mutate()
  }

  const onCaptcha = useCallback((t: string) => setCaptchaToken(t), [])

  const setCount = (patch: Partial<GuestCounts>) => {
    setCounts((c) => ({ ...c, ...patch }))
    setPriceChanged(null)
  }

  const maxAdults = Math.max(1, Math.min(MAX_ADULTS, maxGuests(house) - counts.children))
  const maxChildren = Math.max(0, Math.min(MAX_CHILDREN, maxGuests(house) - counts.adults))
  const extra = extraBedsNeeded(house, counts)

  if (!house.acceptingBookings) {
    return (
      <section aria-label="Бронирование" className="rounded-3xl border border-line bg-white p-6 shadow-soft">
        <h2 className="font-serif text-2xl text-ink">Бронирование</h2>
        <p role="status" className="mt-3 rounded-2xl bg-warning-bg px-4 py-3 text-sm text-warning">
          {house.notAcceptingText ?? 'Бронирование временно недоступно'}
        </p>
      </section>
    )
  }

  const servicesOk = !q?.services || q.services.every((s) => s.ok)
  const canSubmit = hasRange && !guestsProblem && !!q && q.ok && servicesOk && quoteFresh && !create.isPending

  return (
    <section id="booking" aria-label="Бронирование" className="rounded-3xl border border-line bg-white p-5 shadow-soft sm:p-6">
      <h2 className="font-serif text-2xl text-ink">Выберите даты</h2>
      <p className="mt-1 text-sm text-ink-soft">
        {house.priceFromRub != null && (
          <>
            от <span className="font-semibold text-ink">{formatRub(house.priceFromRub)}</span> за ночь ·{' '}
          </>
        )}
        минимум {nightsLabel(house.rules.minNights)}
      </p>

      <div className="mt-4">
        {calendar.isLoading ? (
          <Skeleton className="h-[320px]" />
        ) : calendar.isError || !calendar.data ? (
          <div role="alert" className="rounded-2xl bg-danger-bg px-4 py-3 text-sm text-danger">
            {getStayErrorMessage(calendar.error, 'Не удалось загрузить календарь.')}{' '}
            <button type="button" className="font-semibold underline" onClick={() => void calendar.refetch()}>
              Повторить
            </button>
          </div>
        ) : (
          <StayCalendar calendar={calendar.data} value={range} onChange={setRange} onMessage={setCalendarMessage} />
        )}
        {calendarMessage && !calendar.isError && <span className="sr-only">{calendarMessage}</span>}
      </div>

      {hasRange && (
        <div className="mt-4 grid grid-cols-2 gap-3 rounded-2xl bg-cream-deep px-4 py-3 text-sm" data-testid="stay-summary">
          <div>
            <p className="text-xs text-muted">Заезд</p>
            <p className="font-medium text-ink">{formatDateWithWeekday(range.checkIn!)}</p>
            <p className="text-xs text-ink-soft">с {house.rules.checkInTime}</p>
          </div>
          <div>
            <p className="text-xs text-muted">Выезд</p>
            <p className="font-medium text-ink">{formatDateWithWeekday(range.checkOut!)}</p>
            <p className="text-xs text-ink-soft">до {house.rules.checkOutTime}</p>
          </div>
        </div>
      )}

      <div className="mt-5 border-t border-line pt-4">
        <h3 className="mb-1 text-[15px] font-semibold text-ink">Гости</h3>
        <Stepper label="Взрослые" value={counts.adults} min={1} max={maxAdults} onChange={(v) => setCount({ adults: v })} />
        <Stepper label="Дети" hint="только число, без имён" value={counts.children} min={0} max={maxChildren} onChange={(v) => setCount({ children: v })} />
        {!house.dogsForbidden && (
          <Stepper
            label="Собаки"
            hint={house.dogFeeRub > 0 ? `${formatRub(house.dogFeeRub)} за собаку в ночь` : 'без доплаты'}
            value={counts.dogs}
            min={0}
            max={MAX_DOGS}
            onChange={(v) => setCount({ dogs: v })}
          />
        )}
        {house.hasCot && (
          <label className="flex min-h-[44px] cursor-pointer items-center gap-3 py-1.5 text-sm text-ink">
            <input
              type="checkbox"
              checked={counts.needCot}
              onChange={(e) => setCount({ needCot: e.target.checked })}
              className="h-5 w-5 accent-gold"
            />
            <span>
              Нужна детская кроватка
              <span className="block text-xs text-muted">{house.cotFeeRub > 0 ? `${formatRub(house.cotFeeRub)} за ночь` : 'бесплатно'}</span>
            </span>
          </label>
        )}
        {extra > 0 && !guestsProblem && (
          <p className="mt-1 text-xs text-ink-soft">
            Сверх вместимости ({house.capacity}) — {extra} доп. {extra === 1 ? 'место' : 'места'}, {formatRub(house.extraBeds.priceRub)} за место в ночь.
          </p>
        )}
        {guestsProblem && (
          <p role="alert" className="mt-1 text-sm text-danger">
            {guestsProblem}
          </p>
        )}
      </div>

      {hasRange && !guestsProblem && (
        <div className="mt-5 border-t border-line pt-4" aria-live="polite" data-testid="stay-quote">
          <h3 className="mb-2 text-[15px] font-semibold text-ink">Стоимость</h3>
          {quote.isLoading || (quote.isFetching && !q) ? (
            <Skeleton className="h-40" />
          ) : quote.isError && !q ? (
            <div role="alert" className="rounded-2xl bg-danger-bg px-4 py-3 text-sm text-danger">
              {getStayErrorMessage(quote.error, 'Не удалось рассчитать стоимость.')}{' '}
              <button type="button" className="font-semibold underline" onClick={() => void quote.refetch()}>
                Повторить
              </button>
            </div>
          ) : q && !q.ok ? (
            <ul role="alert" className="flex flex-col gap-1 rounded-2xl bg-danger-bg px-4 py-3 text-sm text-danger">
              {q.problems.map((p) => (
                <li key={p.code}>{p.message}</li>
              ))}
            </ul>
          ) : q ? (
            <div className={quote.isFetching ? 'opacity-60 transition-opacity' : ''}>
              <PriceBreakdown lines={q.lines} totalRub={q.totalRub} prepayPercent={q.prepayPercent} prepayRub={q.prepayRub} dueAtCheckInRub={q.dueAtCheckInRub} />
              <StayNotice textKey="StayTouristTaxNotice" variant="plain" className="mt-2" />
              {q.prepayRub > 0 && (
                <p className="mt-3 text-xs text-ink-soft">
                  Даты удерживаются {q.holdMinutes} минут — за это время внесите предоплату по реквизитам и приложите подтверждение оплаты.
                  Реквизиты появятся на странице брони.
                </p>
              )}
              <p className="mt-2 text-xs text-ink-soft">Отмена: {q.cancellationSummary}</p>
            </div>
          ) : null}
        </div>
      )}

      <StayServicesBlock
        companySlug={house.company.slug}
        houseId={house.id}
        checkIn={rangeConfirmed ? range.checkIn! : null}
        checkOut={rangeConfirmed ? range.checkOut! : null}
        chosen={stayServices}
        quoteServices={q?.services}
        onChange={(next) => {
          setStayServices(next)
          setPriceChanged(null)
        }}
        companyName={house.company.name}
      />

      {priceChanged && (
        <div role="alert" className="mt-4 rounded-2xl bg-warning-bg px-4 py-3 text-sm text-warning" data-testid="price-changed">
          {priceChanged}
        </div>
      )}

      <form
        className="mt-5 flex flex-col gap-4 border-t border-line pt-4"
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          submit()
        }}
      >
        <h3 className="text-[15px] font-semibold text-ink">Ваши данные</h3>
        <Input
          label="Имя"
          autoComplete="name"
          maxLength={120}
          value={guest.name}
          error={fieldErrors.name}
          onChange={(e) => setGuest((g) => ({ ...g, name: e.target.value }))}
        />
        {anonymous ? (
          <div>
            <PhoneInput
              label="Телефон"
              value={guest.phone}
              error={fieldErrors.phone}
              onChange={(phone) => setGuest((g) => ({ ...g, phone }))}
            />
            <p className="mt-1 text-xs text-muted">На этот номер придёт ссылка на бронь. Проверьте, что номер указан верно.</p>
          </div>
        ) : (
          <div>
            <p className="text-[13px] font-medium text-[#4A4038]">Телефон</p>
            <p className="mt-1 text-sm text-ink">{formatPhone(user?.phone)}</p>
            <p className="mt-0.5 text-xs text-muted">Бронь оформляется на номер вашего аккаунта.</p>
          </div>
        )}

        <div className="flex flex-col gap-1.5">
          <label htmlFor="arrival" className="text-[13px] font-medium text-[#4A4038]">
            Примерное время прибытия
          </label>
          <select
            id="arrival"
            value={guest.arrivalTime}
            onChange={(e) => setGuest((g) => ({ ...g, arrivalTime: e.target.value }))}
            aria-invalid={!!fieldErrors.arrival}
            className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
          >
            <option value="">Не знаю</option>
            {arrivalTimeOptions(house.rules.checkInTime).map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
          {fieldErrors.arrival && <p className="text-xs text-danger">{fieldErrors.arrival}</p>}
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="comment" className="text-[13px] font-medium text-[#4A4038]">
            Комментарий <span className="font-normal text-muted">(необязательно)</span>
          </label>
          <textarea
            id="comment"
            rows={3}
            maxLength={COMMENT_MAX}
            value={guest.comment}
            aria-describedby="comment-notice"
            aria-invalid={!!fieldErrors.comment}
            onChange={(e) => setGuest((g) => ({ ...g, comment: e.target.value }))}
            className="rounded-xl border border-line bg-white px-3 py-2.5 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
          />
          {fieldErrors.comment && <p className="text-xs text-danger">{fieldErrors.comment}</p>}
          <StayNotice textKey="StayGuestCommentNotice" id="comment-notice" />
        </div>

        {/* A separate box, off by default, never merged with accepting the conditions (Т37-12). */}
        <label className="flex cursor-pointer items-start gap-3 text-sm text-ink">
          <input
            type="checkbox"
            checked={guest.notifyByMessenger}
            onChange={(e) => setGuest((g) => ({ ...g, notifyByMessenger: e.target.checked }))}
            className="mt-0.5 h-5 w-5 shrink-0 accent-gold"
          />
          <MessengerConsentText />
        </label>

        {anonymous && smartCaptchaEnabled && (
          <div>
            <SmartCaptcha key={captchaKey} onToken={onCaptcha} />
            {fieldErrors.captcha && <p className="mt-1 text-xs text-danger">{fieldErrors.captcha}</p>}
          </div>
        )}

        {formError && <InlineError>{formError}</InlineError>}

        <div className="flex flex-col gap-2.5">
          <Button type="submit" size="lg" loading={create.isPending} disabled={!canSubmit} className="w-full">
            {priceChanged ? 'Подтвердить новую стоимость' : q?.ok && q.prepayRub === 0 ? 'Забронировать' : 'Забронировать и перейти к оплате'}
          </Button>
          {!hasRange && <p className="text-center text-xs text-muted">Сначала выберите даты заезда и выезда</p>}
          <StayNotice textKey="StayBookingNotice" variant="plain" companyName={house.company.name} onOpenTerms={onOpenTerms} />
        </div>
      </form>
      <p className="sr-only" aria-live="polite">
        {hasRange ? `Выбрано: ${formatDateShort(range.checkIn!)} — ${formatDateShort(range.checkOut!)}` : ''}
      </p>
    </section>
  )
}

/** The consent text, short form only: it sits next to a checkbox. */
function MessengerConsentText() {
  return <StayNotice textKey="StayMessengerConsent" variant="plain" className="text-xs text-ink-soft" />
}
