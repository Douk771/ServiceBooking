import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { profileApi } from '@/api/profile'
import { useAuthStore } from '@/store/authStore'
import { useOverlayDismiss } from '@/hooks/useOverlayDismiss'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { usePhoneVerificationConfig } from '@/hooks/usePhoneVerification'
import { VerifyPhoneButton } from '@/components/phoneVerification/VerifyPhoneButton'
import { SmartCaptcha, smartCaptchaEnabled } from '@/components/booking/SmartCaptcha'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { formatPhone, isRussianPhone } from '@/utils/phone'
import { storefrontApi } from '../../api/storefront'
import { shopsApi } from '../../api/shops'
import { MessengerOptIn } from '@/components/notifications/MessengerOptIn'
import { useMessengerOptInDefault } from '@/hooks/useMessengerOptInDefault'
import { CheckoutLegalNotice } from './CheckoutLegalNotice'
import { InlineError } from '../StatePanels'
import { useCart } from '../../hooks/useCart'
import { decrementQuantity, incrementQuantity, priceChanges, quantityRule } from '../../utils/cart'
import { checkoutGate, loginUrlForCheckout, storefrontMessengerOffer, validateCheckout } from '../../utils/checkout'
import { toPickupInput } from '../../utils/pickup'
import type { PickupControl } from '../../hooks/usePickupChoice'
import { newIdempotencyKey, orderPath } from '../../utils/idempotency'
import { lineTotal, orderTotal } from '../../utils/orderMoney'
import { formatMoney, formatQuantity, formatUnitPrice } from '../../utils/quantityFormat'
import { getGoodsErrorMessage, isPhoneVerificationRequired, readRefusal } from '../../utils/orderError'
import type { OrderRefusalDto, QuoteDto, StorefrontDto, StorefrontProductDto } from '../../types'

interface Props {
  slug: string
  shop: StorefrontDto
  products: Map<string, StorefrontProductDto>
  cart: ReturnType<typeof useCart>
  pickup: PickupControl
  /** Server message to show at the picker after `PickupTimeUnavailable` (the cart and the idempotency key stay). */
  onPickupNotice: (text: string | null) => void
  /** Closes the panel and scrolls to «Когда заберёте». */
  onChangePickup: () => void
  onClose: () => void
}

/**
 * Slide-over with the cart and the one-screen checkout (SPEC US-23-18/19). The server's `quote` is the source
 * of prices, sums and problems (opened panel + right before «Заказать»); numbers shown before the answer come
 * from the local preview and use «≈» for weighed lines. Nothing is reserved by `quote`.
 */
export function CartPanel({ slug, shop, products, cart, pickup, onPickupNotice, onChangePickup, onClose }: Props) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const dismiss = useOverlayDismiss(onClose)
  const { user, token, isAuthenticated } = useAuthStore()
  const signedIn = isAuthenticated()

  // Idempotency key — created once per order attempt and kept until the order is actually created (§395.3).
  const keyRef = useRef<string>(newIdempotencyKey())

  const debouncedItems = useDebouncedValue(cart.items, 300)
  // `pickup` rides on every quote: the server re-checks availability ON THE PICK-UP DATE and the time itself (§477.4).
  const pickupInput = useMemo(() => toPickupInput(pickup.choice), [pickup.choice])
  const quoteBody = useMemo(() => ({ items: debouncedItems.map((i) => ({ productId: i.productId, quantity: i.quantity })), pickup: pickupInput }), [debouncedItems, pickupInput])
  const quoteKey = useMemo(() => JSON.stringify(quoteBody), [quoteBody])
  const quoteQuery = useQuery({
    queryKey: ['quote', slug, quoteKey],
    queryFn: () => storefrontApi.quote(slug, quoteBody),
    enabled: quoteBody.items.length > 0,
    placeholderData: keepPreviousData,
    staleTime: 0,
    retry: 1,
  })
  const quote: QuoteDto | undefined = quoteBody.items.length > 0 ? quoteQuery.data : undefined
  const changes = useMemo(() => (quote ? priceChanges(cart.items, quote.lines) : []), [cart.items, quote])

  // ── checkout fields ────────────────────────────────────────────────────────────────────────────
  const [name, setName] = useState(() => (user ? `${user.firstName} ${user.lastName}`.trim().slice(0, 100) : ''))
  const [phone, setPhone] = useState('')
  const [comment, setComment] = useState('')
  const [captchaToken, setCaptchaToken] = useState('')
  const [captchaNonce, setCaptchaNonce] = useState(0)
  const [formError, setFormError] = useState('')
  const [refusal, setRefusal] = useState<OrderRefusalDto | null>(null)
  const [otherError, setOtherError] = useState('')
  const [checking, setChecking] = useState(false)
  const retryAfterVerify = useRef(false)
  const submittingRef = useRef(false)

  const profile = useQuery({
    queryKey: ['profile'],
    queryFn: profileApi.get,
    enabled: signedIn && shop.customerMode === 'VerifiedPhoneOnly',
  })
  const verifyConfig = usePhoneVerificationConfig()
  const gate = checkoutGate({
    customerMode: shop.customerMode,
    signedIn,
    phoneVerified: profile.data?.phoneVerified,
    verificationEnabled: verifyConfig.data?.enabled,
  })
  const guest = !signedIn
  // Cycle 40: сервер решает, предлагать ли галочку и с какой подписью; умолчание — из профиля вошедшего (§40.11.4).
  const messengerOffer = storefrontMessengerOffer(shop)
  const optIn = useMessengerOptInDefault(signedIn)
  const kinds = useQuery({ queryKey: ['kinds-summary'], queryFn: shopsApi.kindsSummary, enabled: signedIn && optIn.optedOut, retry: false })
  const ezbookProfileHref = kinds.data?.services.siteUrl ? `${kinds.data.services.siteUrl}/profile` : null

  const create = useMutation({
    mutationFn: () =>
      storefrontApi.createOrder(slug, {
        idempotencyKey: keyRef.current,
        items: cart.items.map((i) => ({ productId: i.productId, quantity: i.quantity, expectedUnitPrice: i.unitPriceSeen })),
        customerName: name.trim(),
        customerPhone: guest ? phone : undefined,
        comment: comment.trim() || undefined,
        captchaToken: guest ? captchaToken || undefined : undefined,
        pickup: pickupInput,
        notifyByMessenger: optIn.payload(messengerOffer.offered) ?? false,
      }),
    onSuccess: (res) => {
      cart.clear()
      keyRef.current = newIdempotencyKey()
      navigate(orderPath(res.orderUrl, res.order.token), { state: { justCreated: true }, replace: true })
    },
    onError: (err) => {
      // The captcha token is single-use and was spent by this attempt — a fresh widget for the retry.
      setCaptchaToken('')
      setCaptchaNonce((n) => n + 1)
      const r = readRefusal(err)
      if (r) {
        setRefusal(r)
        if (r.code === 'PriceChanged' || r.code === 'ItemsUnavailable' || r.code === 'ShopNotAcceptingOrders') {
          void qc.invalidateQueries({ queryKey: ['quote', slug] })
          void qc.invalidateQueries({ queryKey: ['storefront', slug] })
        }
        if (r.code === 'PickupTimeUnavailable') {
          // §478.2: refetch the slots and show the choice again with the server's message; cart and key are kept.
          void qc.invalidateQueries({ queryKey: ['pickup-slots', slug] })
          void qc.invalidateQueries({ queryKey: ['storefront', slug] })
          pickup.choose(null)
          onPickupNotice(r.message)
        }
        if (r.code === 'PhoneVerificationRequired' || r.code === 'LoginRequired') void qc.invalidateQueries({ queryKey: ['profile'] })
        if (isPhoneVerificationRequired(r)) retryAfterVerify.current = true
      } else {
        setOtherError(getGoodsErrorMessage(err, 'Не удалось оформить заказ. Попробуйте ещё раз.'))
      }
    },
    onSettled: () => {
      submittingRef.current = false
    },
  })

  const submit = useCallback(async () => {
    if (submittingRef.current || create.isPending) return
    setFormError('')
    setOtherError('')
    setRefusal(null)
    const problem = validateCheckout({ name, comment, phone, guest, captchaRequired: smartCaptchaEnabled, captchaToken }, isRussianPhone)
    if (problem) {
      setFormError(problem)
      return
    }
    submittingRef.current = true
    setChecking(true)
    // Fresh quote right before ordering (§395.3): it shows problems and new prices before anything is created.
    let fresh: QuoteDto | undefined
    try {
      fresh = (await quoteQuery.refetch()).data
    } finally {
      setChecking(false)
    }
    if (!fresh) {
      submittingRef.current = false
      setOtherError('Не удалось проверить корзину. Попробуйте ещё раз.')
      return
    }
    if (fresh.hasProblems || !fresh.acceptingOrders || priceChanges(cart.items, fresh.lines).length > 0) {
      if (fresh.pickupProblem) void qc.invalidateQueries({ queryKey: ['pickup-slots', slug] })
      submittingRef.current = false
      return // the panel now shows the problem lines / the price confirmation
    }
    create.mutate()
  }, [name, comment, phone, guest, captchaToken, create, quoteQuery, cart.items, qc, slug])

  const submitRef = useRef(submit)
  submitRef.current = submit
  // After a confirmed phone, the refused order is retried by itself (§423).
  useEffect(() => {
    if (gate.kind === 'open' && retryAfterVerify.current) {
      retryAfterVerify.current = false
      void submitRef.current()
    }
  }, [gate.kind])

  useEffect(() => {
    document.body.classList.add('overflow-hidden')
    return () => document.body.classList.remove('overflow-hidden')
  }, [])

  // ── render data ────────────────────────────────────────────────────────────────────────────────
  const lines = cart.items.map((item) => {
    const product = products.get(item.productId)
    const q = quote?.lines.find((l) => l.productId === item.productId)
    const unit = q?.unit ?? product?.unit ?? 'Piece'
    const unitPrice = q?.unitPrice ?? item.unitPriceSeen
    return {
      item,
      product,
      name: q?.name ?? product?.name ?? 'Товар',
      unit,
      unitPrice,
      total: q?.lineTotal ?? lineTotal(unit, unitPrice, item.quantity),
      approx: q?.isApproximate ?? unit === 'Weight',
      problem: q?.problem ?? null,
      change: changes.find((c) => c.productId === item.productId),
    }
  })
  const preview = orderTotal(cart.items.map((i) => ({ unit: products.get(i.productId)?.unit ?? 'Piece', price: i.unitPriceSeen, quantity: i.quantity })))
  const total = quote ? quote.total : preview.total
  const totalApprox = quote ? quote.isApproximate : preview.isApproximate
  const acceptingOrders = quote ? quote.acceptingOrders : shop.acceptingOrders
  const notAcceptingReason = quote?.notAcceptingReason ?? shop.notAcceptingReason
  const hasProblems = !!quote?.hasProblems
  const pickupProblem = quote?.pickupProblem ?? null
  const pickupChosen = pickup.choice !== null
  const busy = create.isPending || checking
  const canOrder = cart.items.length > 0 && acceptingOrders && !hasProblems && pickupChosen && changes.length === 0 && gate.kind === 'open' && !busy

  return (
    <div className="fixed inset-0 z-50 bg-ink/45 backdrop-blur-sm flex justify-end" {...dismiss}>
      <div role="dialog" aria-modal="true" aria-labelledby="cart-title" className="bg-cream w-full sm:max-w-[460px] h-full overflow-y-auto shadow-modal flex flex-col">
        <div className="flex items-center justify-between px-5 py-4 border-b border-line sticky top-0 bg-cream z-10">
          <h2 id="cart-title" className="font-serif text-xl text-ink">
            Ваш заказ
          </h2>
          <button onClick={onClose} aria-label="Закрыть корзину" className="w-9 h-9 rounded-full flex items-center justify-center text-muted hover:text-ink hover:bg-cream-deep">
            <Icon name="x" size={18} strokeWidth={1.8} />
          </button>
        </div>

        <div className="px-5 py-5 flex-1 flex flex-col gap-5">
          {cart.items.length === 0 ? (
            <div className="text-center py-16">
              <Icon name="shopping-bag" size={32} strokeWidth={1.3} className="mx-auto text-muted mb-3" />
              <p className="text-ink font-medium">Корзина пуста</p>
              <p className="text-sm text-ink-soft mt-1">Добавьте товары со страницы магазина.</p>
              <Button variant="secondary" className="mt-4" onClick={onClose}>
                К товарам
              </Button>
            </div>
          ) : (
            <>
              {!acceptingOrders && (
                <div role="alert" className="rounded-xl bg-warning-bg text-warning text-sm px-4 py-3">
                  {notAcceptingReason ?? 'Сейчас магазин не принимает заказы.'}
                </div>
              )}
              {quoteQuery.isError && !quote && (
                <div role="alert" className="rounded-xl bg-danger-bg text-danger text-sm px-4 py-3 flex items-center justify-between gap-3">
                  <span>{getGoodsErrorMessage(quoteQuery.error, 'Не удалось проверить корзину.')}</span>
                  <Button size="sm" variant="secondary" onClick={() => void quoteQuery.refetch()}>
                    Повторить
                  </Button>
                </div>
              )}

              <ul className="flex flex-col gap-3" aria-label="Позиции заказа">
                {lines.map((l) => {
                  const rule = l.product ? quantityRule(l.product) : null
                  return (
                    <li key={l.item.productId} className={`rounded-2xl border bg-white p-4 ${l.problem ? 'border-danger' : 'border-line'}`}>
                      <div className="flex justify-between gap-3">
                        <div className="min-w-0">
                          <p className="font-semibold text-ink leading-snug">{l.name}</p>
                          <p className="text-xs text-muted mt-0.5">
                            {formatUnitPrice(l.unit, l.unitPrice)}
                            {l.product?.portionText ? ` · ${l.product.portionText}` : ''}
                          </p>
                        </div>
                        <p className="font-semibold text-ink whitespace-nowrap">{formatMoney(l.total, l.approx)}</p>
                      </div>

                      {l.change && (
                        <p className="mt-2 text-xs font-medium text-warning bg-warning-bg rounded-lg px-3 py-1.5">
                          Цена изменилась: было {formatUnitPrice(l.unit, l.change.was)}, стало {formatUnitPrice(l.unit, l.change.now)}
                        </p>
                      )}
                      {l.problem && (
                        <p role="alert" className="mt-2 text-xs font-medium text-danger">
                          {l.problem.message}
                        </p>
                      )}

                      <div className="mt-3 flex items-center justify-between">
                        {rule ? (
                          <div className="inline-flex items-center rounded-full border border-line bg-cream">
                            <button
                              type="button"
                              aria-label={`Уменьшить: ${l.name}`}
                              className="w-9 h-9 flex items-center justify-center hover:bg-cream-deep rounded-l-full"
                              onClick={() => {
                                const next = decrementQuantity(rule, l.item.quantity)
                                if (next === null) cart.remove(l.item.productId)
                                else cart.setLine({ ...l.item, quantity: next })
                              }}
                            >
                              <Icon name="minus" size={15} />
                            </button>
                            <span className="min-w-[60px] text-center text-sm font-semibold">{formatQuantity(l.unit, l.item.quantity)}</span>
                            <button
                              type="button"
                              aria-label={`Добавить ещё: ${l.name}`}
                              disabled={l.item.quantity >= rule.max}
                              className="w-9 h-9 flex items-center justify-center hover:bg-cream-deep rounded-r-full disabled:opacity-40"
                              onClick={() => cart.setLine({ ...l.item, quantity: incrementQuantity(rule, l.item.quantity) })}
                            >
                              <Icon name="plus" size={15} />
                            </button>
                          </div>
                        ) : (
                          <span className="text-xs text-muted">{formatQuantity(l.unit, l.item.quantity)}</span>
                        )}
                        <button type="button" className="text-xs font-semibold text-danger hover:underline" onClick={() => cart.remove(l.item.productId)}>
                          Убрать
                        </button>
                      </div>
                    </li>
                  )
                })}
              </ul>

              <div className="rounded-2xl border border-line bg-white p-4 flex items-start justify-between gap-3" data-testid="cart-pickup">
                <div className="min-w-0">
                  <p className="text-xs text-muted">Когда заберёте</p>
                  <p className="font-semibold text-ink leading-snug">{pickupSummary(pickup.choice, shop.pickup.asap?.text)}</p>
                </div>
                <Button size="sm" variant="secondary" onClick={onChangePickup}>
                  {pickupChosen ? 'Изменить' : 'Выбрать'}
                </Button>
              </div>
              {pickupProblem && (
                <div role="alert" className="rounded-xl bg-danger-bg text-danger text-sm px-4 py-3" data-testid="pickup-problem">
                  {pickupProblem.message}
                </div>
              )}

              {changes.length > 0 && (
                <div role="alert" className="rounded-xl bg-warning-bg text-warning text-sm px-4 py-3">
                  <p className="font-semibold">Цена изменилась — проверьте и подтвердите заказ ещё раз</p>
                  <Button className="mt-2" size="sm" onClick={() => cart.confirmPrices(Object.fromEntries(changes.map((c) => [c.productId, c.now])))}>
                    Принять новые цены
                  </Button>
                </div>
              )}

              <div className="flex items-baseline justify-between border-t border-line pt-4">
                <span className="text-sm text-ink-soft">Итого</span>
                <span className="font-serif text-2xl text-ink" data-testid="cart-total">
                  {formatMoney(total, totalApprox)}
                </span>
              </div>
              {totalApprox && <p className="text-xs text-muted -mt-3">Сумма уточнится при выдаче по фактическому весу.</p>}
              <button type="button" className="self-start text-xs font-semibold text-ink-soft hover:text-danger" onClick={() => cart.clear()}>
                Очистить корзину
              </button>

              {/* ── checkout ─────────────────────────────────────────────────────────────── */}
              <section aria-labelledby="checkout-title" className="border-t border-line pt-5 flex flex-col gap-4">
                <h3 id="checkout-title" className="text-[15px] font-semibold text-ink">
                  Оформление
                </h3>

                {gate.kind === 'login-required' && (
                  <div className="rounded-xl border border-line bg-white p-4 text-sm text-ink-soft">
                    <p>Этот магазин принимает заказы только от покупателей с подтверждённым телефоном. Войдите или зарегистрируйтесь — корзина сохранится.</p>
                    <div className="mt-3 flex gap-2 flex-wrap">
                      <Link to={loginUrlForCheckout(slug)} className="inline-flex rounded-full bg-ink px-5 py-2.5 text-sm font-semibold !text-cream">
                        Войти
                      </Link>
                      <Link to={loginUrlForCheckout(slug, 'register')} className="inline-flex rounded-full border border-line bg-white px-5 py-2.5 text-sm font-semibold !text-ink">
                        Зарегистрироваться
                      </Link>
                    </div>
                  </div>
                )}
                {gate.kind === 'checking' && <div className="h-16 rounded-xl bg-cream-deep animate-pulse" role="status" aria-label="Проверяем номер" />}
                {gate.kind === 'verify-phone' && (
                  <div className="rounded-xl border border-line bg-white p-4 text-sm text-ink-soft">
                    <p>Подтвердите номер телефона через MAX, чтобы оформить заказ. Вернитесь сюда — корзина сохранится.</p>
                    <div className="mt-3">
                      <VerifyPhoneButton
                        phone={user?.phone ?? ''}
                        onVerifiedChange={(ref) => {
                          if (ref) void qc.invalidateQueries({ queryKey: ['profile'] })
                        }}
                      />
                    </div>
                  </div>
                )}
                {gate.kind === 'verification-unavailable' && (
                  <div role="alert" className="rounded-xl bg-warning-bg text-warning text-sm px-4 py-3">
                    Подтверждение телефона сейчас недоступно — заказать в этом магазине пока нельзя.
                  </div>
                )}

                {gate.kind === 'open' && (
                  <form
                    className="flex flex-col gap-4"
                    noValidate
                    onSubmit={(e) => {
                      e.preventDefault()
                      void submit()
                    }}
                  >
                    <div className="flex flex-col gap-1.5">
                      <label htmlFor="checkout-name" className="text-[13px] font-medium text-[#4A4038]">
                        Имя *
                      </label>
                      <input id="checkout-name" value={name} maxLength={100} autoComplete="name" onChange={(e) => setName(e.target.value)} className="rounded-xl border border-line px-4 py-3 text-sm bg-white text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep" />
                    </div>

                    {guest ? (
                      <PhoneInput label="Телефон *" value={phone} onChange={setPhone} />
                    ) : (
                      <p className="text-sm text-ink-soft">
                        Заказ оформляется на номер вашего аккаунта: <span className="font-medium text-ink">{formatPhone(user?.phone)}</span>
                      </p>
                    )}

                    <div className="flex flex-col gap-1.5">
                      <label htmlFor="checkout-comment" className="text-[13px] font-medium text-[#4A4038]">
                        Комментарий
                      </label>
                      <textarea id="checkout-comment" rows={2} maxLength={500} value={comment} onChange={(e) => setComment(e.target.value)} placeholder="Например: без лука, заберу около 13:00" className="rounded-xl border border-line px-4 py-3 text-sm resize-none bg-white text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep" />
                      <p className="text-[11px] text-muted text-right">{comment.length}/500</p>
                    </div>

                    <MessengerOptIn kind="order" offer={messengerOffer} state={optIn} companyName={shop.name} profileHref={ezbookProfileHref} />

                    {guest && smartCaptchaEnabled && (
                      <div>
                        <SmartCaptcha key={captchaNonce} onToken={setCaptchaToken} />
                        <p className="text-xs text-muted mt-1">Подтвердите, что вы не робот (Yandex SmartCaptcha)</p>
                      </div>
                    )}

                    {formError && <InlineError>{formError}</InlineError>}
                    {otherError && <InlineError>{otherError}</InlineError>}
                    {refusal && <RefusalBanner refusal={refusal} slug={slug} />}

                    <Button type="submit" size="lg" className="w-full" loading={busy} disabled={!canOrder}>
                      Заказать · {formatMoney(total, totalApprox)}
                    </Button>
                    {!canOrder && !busy && cart.items.length > 0 && (
                      <p className="text-xs text-muted text-center -mt-2">
                        {!acceptingOrders ? 'Магазин сейчас не принимает заказы.' : !pickupChosen ? 'Выберите, когда заберёте заказ.' : pickupProblem ? 'Выберите другое время получения.' : hasProblems ? 'Исправьте отмеченные позиции — и можно заказывать.' : changes.length > 0 ? 'Подтвердите новые цены выше.' : ''}
                      </p>
                    )}
                    <CheckoutLegalNotice />
                  </form>
                )}

                {gate.kind !== 'open' && refusal && <RefusalBanner refusal={refusal} slug={slug} />}
                {token === null && shop.customerMode === 'Anyone' && (
                  <p className="text-xs text-muted">
                    Уже есть аккаунт?{' '}
                    <Link to={loginUrlForCheckout(slug)} className="text-gold font-semibold hover:text-gold-dark">
                      Войти
                    </Link>
                    , чтобы имя и телефон подставились сами.
                  </p>
                )}
              </section>
            </>
          )}
        </div>
      </div>
    </div>
  )
}

/** Refusal of `POST …/orders`: the server's `message` plus every problem line (§413). Branches on `code`. */
function RefusalBanner({ refusal, slug }: { refusal: OrderRefusalDto; slug: string }) {
  return (
    <div role="alert" className="rounded-xl bg-danger-bg text-danger text-sm px-4 py-3" data-testid="order-refusal" data-code={refusal.code}>
      <p className="font-semibold">{refusal.message}</p>
      {refusal.problems && refusal.problems.length > 0 && (
        <ul className="mt-1.5 list-disc pl-5 text-[13px]">
          {refusal.problems.map((p) => (
            <li key={p.productId}>
              {p.name}: {p.message}
            </li>
          ))}
        </ul>
      )}
      {refusal.code === 'LoginRequired' && (
        <Link to={loginUrlForCheckout(slug)} className="inline-block mt-2 font-semibold underline !text-danger">
          Войти
        </Link>
      )}
    </div>
  )
}

function pickupSummary(choice: PickupControl['choice'], asapText: string | null | undefined): string {
  if (!choice) return 'Время не выбрано'
  if (choice.kind === 'Asap') return asapText ? `Как можно скорее (${asapText})` : 'Как можно скорее'
  return [choice.dateLabel ?? choice.date, choice.slotLabel].filter(Boolean).join(', ')
}
