import { useCallback, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { InlineError } from '@/components/ui/InlineError'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { SmartCaptcha, smartCaptchaEnabled } from '@/components/booking/SmartCaptcha'
import { useAuthStore } from '@/store/authStore'
import { formatPhone } from '@/utils/phone'
import { publicServicesApi } from '../../api/publicServices'
import type { PublicServiceDto, ServiceQuoteDto, ServiceRefusalDto } from '../../types'
import { COMMENT_MAX, fieldOfBookingError, type GuestFieldErrors } from '../../utils/bookingForm'
import { bookingKeyFor, forgetBookingKey } from '../../utils/idempotency'
import { isQuoteBookable, isTimeGone, type SessionPick } from '../../utils/serviceSelection'
import { toCreateOrderInput, toServiceQuoteInput, validateOrderFields, type OrderGuestFields } from '../../utils/serviceOrderForm'
import { getStayErrorMessage, httpStatus, plainBody, readConflict } from '../../utils/stayError'
import { StayNotice } from '../StayNotice'
import { ServiceQuoteSummary } from './ServiceQuoteSummary'
import { ServiceTimePicker } from './ServiceTimePicker'

/**
 * The order of a separate session (US-39-11, API_CONTRACT_CYCLE39.md §39.22.5): picker → server quote → the guest's details → order.
 * The order is created against the quoted total; a changed price comes back as 409 `PriceChanged` with the new quote — the guest
 * confirms it and presses again (same idempotency key). A refusal «время занято» sends the guest back to a re-read list. The text
 * under the button is `StayServiceBookingNotice`; the tourist-tax note does not belong here (Т39-15).
 */
export function ServiceOrderPanel({ service, initialDate, onOpenTerms }: { service: PublicServiceDto; initialDate?: string | null; onOpenTerms: () => void }) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const user = useAuthStore((s) => s.user)
  const authed = useAuthStore((s) => s.isAuthenticated())
  const anonymous = !authed
  const itemOrder = useMemo(() => service.items.map((i) => i.id), [service.items])

  const [pick, setPick] = useState<SessionPick | null>(null)
  const [resetSignal, setResetSignal] = useState(0)
  const [guest, setGuest] = useState<OrderGuestFields>({
    name: authed ? `${user?.firstName ?? ''} ${user?.lastName ?? ''}`.trim() : '',
    phone: '',
    comment: '',
    notifyByMessenger: false,
  })
  const [captchaToken, setCaptchaToken] = useState('')
  const [captchaKey, setCaptchaKey] = useState(0)
  const [fieldErrors, setFieldErrors] = useState<GuestFieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [priceChanged, setPriceChanged] = useState<string | null>(null)
  const idempotencyKey = useMemo(() => bookingKeyFor(`svc:${service.id}`), [service.id])

  const quoteInput = pick ? toServiceQuoteInput(pick, itemOrder) : null
  const quoteKey = ['stays-service-quote', service.id, quoteInput] as const
  const quote = useQuery({
    queryKey: quoteKey,
    queryFn: () => publicServicesApi.quote(service.id, quoteInput!),
    enabled: !!quoteInput && service.acceptingBookings,
    staleTime: 0,
    placeholderData: (prev) => prev,
  })
  const q: ServiceQuoteDto | undefined = quote.data
  const quoteFresh = !!q && !quote.isPlaceholderData && !quote.isFetching

  const loadAvailability = useCallback((from: string | undefined, days: number) => publicServicesApi.availability(service.id, { from, days }), [service.id])
  const loadStarts = useCallback((date: string) => publicServicesApi.starts(service.id, { date }), [service.id])

  const create = useMutation({
    mutationFn: () =>
      publicServicesApi.createOrder(service.id, toCreateOrderInput({ pick: pick!, order: itemOrder, guest, anonymous, quote: q!, idempotencyKey, captchaToken })),
    onSuccess: (res) => {
      forgetBookingKey(`svc:${service.id}`)
      void qc.invalidateQueries({ queryKey: ['stays-service-availability', service.id] })
      navigate(`/s/${encodeURIComponent(res.token)}`, { replace: true, state: { justCreated: true } })
    },
    onError: (err) => {
      // The captcha token is single-use: whatever the answer was, a new try needs a new solve.
      if (anonymous && smartCaptchaEnabled) {
        setCaptchaToken('')
        setCaptchaKey((k) => k + 1)
      }
      const refusal = readConflict<ServiceRefusalDto>(err)
      if (refusal) {
        if (refusal.code === 'PriceChanged' && refusal.quote) {
          qc.setQueryData(quoteKey, refusal.quote)
          setPriceChanged(refusal.message)
          setFormError(null)
          return
        }
        setPriceChanged(null)
        setFormError(refusal.message)
        if (isTimeGone(refusal.code)) {
          // The time is no longer free: the lists are read again and the choice starts over.
          setPick(null)
          setResetSignal((n) => n + 1)
        }
        return
      }
      const text = plainBody(err)
      if (httpStatus(err) === 400 && text) {
        const field = fieldOfBookingError(text)
        if (field === 'name' || field === 'phone' || field === 'comment' || field === 'captcha') {
          setFieldErrors({ [field]: text })
          setFormError(null)
          return
        }
      }
      setFormError(getStayErrorMessage(err, 'Не удалось оформить заказ. Попробуйте ещё раз.'))
    },
  })

  const submit = () => {
    setFormError(null)
    const errors = validateOrderFields(guest, { anonymous, captchaRequired: smartCaptchaEnabled, captchaToken })
    setFieldErrors(errors)
    if (Object.keys(errors).length > 0 || !pick || !quoteFresh || !isQuoteBookable(q)) return
    create.mutate()
  }
  const onCaptcha = useCallback((t: string) => setCaptchaToken(t), [])

  const canSubmit = !!pick && quoteFresh && isQuoteBookable(q) && !create.isPending

  return (
    <section id="order" aria-label="Заказ услуги" className="rounded-3xl border border-line bg-white p-5 shadow-soft sm:p-6">
      <h2 className="font-serif text-2xl text-ink">Выберите время</h2>

      <div className="mt-4">
        <ServiceTimePicker
          scope={service.id}
          initialDate={initialDate}
          items={service.items}
          loadAvailability={loadAvailability}
          loadStarts={loadStarts}
          onChange={(p) => {
            setPick(p)
            setPriceChanged(null)
          }}
          resetSignal={resetSignal}
        />
      </div>

      {pick && (
        <div className="mt-5 border-t border-line pt-4">
          <h3 className="mb-2 text-[15px] font-semibold text-ink">Стоимость</h3>
          {quote.isError && !q ? (
            <div role="alert" className="rounded-2xl bg-danger-bg px-4 py-3 text-sm text-danger">
              {getStayErrorMessage(quote.error, 'Не удалось рассчитать стоимость.')}{' '}
              <button type="button" className="font-semibold underline" onClick={() => void quote.refetch()}>
                Повторить
              </button>
            </div>
          ) : (
            <ServiceQuoteSummary quote={q} loading={quote.isLoading} stale={quote.isFetching} />
          )}
        </div>
      )}

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
        <Input label="Имя" autoComplete="name" maxLength={120} value={guest.name} error={fieldErrors.name} onChange={(e) => setGuest((g) => ({ ...g, name: e.target.value }))} />
        {anonymous ? (
          <div>
            <PhoneInput label="Телефон" value={guest.phone} error={fieldErrors.phone} onChange={(phone) => setGuest((g) => ({ ...g, phone }))} />
            <p className="mt-1 text-xs text-muted">На этот номер придёт ссылка на заказ. Проверьте, что номер указан верно.</p>
          </div>
        ) : (
          <div>
            <p className="text-[13px] font-medium text-[#4A4038]">Телефон</p>
            <p className="mt-1 text-sm text-ink">{formatPhone(user?.phone)}</p>
            <p className="mt-0.5 text-xs text-muted">Заказ оформляется на номер вашего аккаунта.</p>
          </div>
        )}

        <div className="flex flex-col gap-1.5">
          <label htmlFor="order-comment" className="text-[13px] font-medium text-[#4A4038]">
            Комментарий <span className="font-normal text-muted">(необязательно)</span>
          </label>
          <textarea
            id="order-comment"
            rows={3}
            maxLength={COMMENT_MAX}
            value={guest.comment}
            aria-describedby="order-comment-notice"
            aria-invalid={!!fieldErrors.comment}
            onChange={(e) => setGuest((g) => ({ ...g, comment: e.target.value }))}
            className="rounded-xl border border-line bg-white px-3 py-2.5 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
          />
          {fieldErrors.comment && <p className="text-xs text-danger">{fieldErrors.comment}</p>}
          <StayNotice textKey="StayServiceCommentNotice" id="order-comment-notice" />
        </div>

        {/* A separate box, off by default, never merged with accepting the conditions (Т37-12). */}
        <label className="flex cursor-pointer items-start gap-3 text-sm text-ink">
          <input
            type="checkbox"
            checked={guest.notifyByMessenger}
            onChange={(e) => setGuest((g) => ({ ...g, notifyByMessenger: e.target.checked }))}
            className="mt-0.5 h-5 w-5 shrink-0 accent-gold"
          />
          <StayNotice textKey="StayMessengerConsent" variant="plain" className="text-xs text-ink-soft" />
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
          {!pick && <p className="text-center text-xs text-muted">Сначала выберите дату, время начала и число часов</p>}
          <StayNotice textKey="StayServiceBookingNotice" variant="plain" companyName={service.company.name} onOpenTerms={onOpenTerms} />
        </div>
      </form>
    </section>
  )
}
