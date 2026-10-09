import { MessengerAddonLines } from '../components/pricing/MessengerAddonLines'
import { useEffect, useState } from 'react'
import { AxiosError } from 'axios'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { billingApi, type AvailableOptionDto, type BillingLine } from '../api/billing'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Icon } from '../components/ui/Icon'
import { formatMonthlyPrice } from '../utils/pricingFormat'
import { getBillingErrorMessage } from '../utils/billingError'
import { getTrialErrorMessage, isTrialTermsVersionMismatch } from '../utils/trialError'
import { STATUS_BADGE_CLASS } from './billingPageHelpers'
import { TrialCard } from '../components/billing/TrialCard'
import { TrialBanner } from '../components/billing/TrialBanner'
import { TrialExpiredNotice } from '../components/billing/TrialExpiredNotice'
import { TrialActivationTerms } from '../components/billing/TrialActivationTerms'
import { OperatorDetailsSection } from '../components/billing/OperatorDetailsSection'
import { BillingNoticesSummary } from '../components/billing/BillingNoticesSummary'

/**
 * Owner screen "Ваша подписка" (US-65, US-68, US-70) — API_CONTRACT_CYCLE7.md §41. One request
 * covers the whole screen; every text shown (`statusText`, `usage.*Text`, `availabilityText`,
 * `warning.text`) is server-composed and printed verbatim, per contract note on OwnerSubscriptionDto.
 *
 * Backend not shipped yet in this pass (no BillingAccount code on the API side) — this calls the
 * real endpoint from the contract, so it lights up unchanged once the backend exists; until then it
 * renders the error state below (also exercised by unit tests via a mocked client).
 */
export function BillingPage({ line = 'Services' }: { line?: BillingLine } = {}) {
  const qc = useQueryClient()
  // Cycle 24: goods mounts this same screen with line="Orders" (ARCHITECTURE_CYCLE24.md §462.2 п.2); cycle 37: dom with line="Stays"
  // (ARCHITECTURE_CYCLE37.md §37.10.4). Without the prop nothing below differs from before: same query key, same request, same blocks.
  const isOrders = line === 'Orders'
  const isStays = line === 'Stays'
  // Cycle 42: bani mounts the same screen with line="Baths" (ARCHITECTURE_CYCLE42.md §42.12.3).
  const isBaths = line === 'Baths'
  const isLine = line !== 'Services'
  const queryKey = isLine ? ['owner-subscription', line] : ['owner-subscription']
  const [requestError, setRequestError] = useState('')
  const [trialError, setTrialError] = useState('')
  const [desiredOptions, setDesiredOptions] = useState<Record<string, number> | null>(null)

  const { data, isLoading, isError, error, refetch, isRefetching } = useQuery({
    queryKey,
    queryFn: () => billingApi.getSubscription(isLine ? line : undefined),
    retry: false,
  })

  useEffect(() => {
    document.title = isBaths ? 'Ваша подписка — EZBOOK Бани' : isStays ? 'Ваша подписка — ezbook · Дома' : isOrders ? 'Ваша подписка — ezbook · Заказы' : 'Ваша подписка — ServiceBooking'
  }, [isOrders, isStays, isBaths])

  // Cycle 24 (L14): a plan of the «Заказы» line (cycle 37: «Дома») is requested straight from `availablePlans`.
  const planRequestMut = useMutation({
    mutationFn: (planId: string) => billingApi.submitRequest({ line: isBaths ? 'Baths' : isStays ? 'Stays' : 'Orders', planId, options: [] }),
    onSuccess: () => {
      setRequestError('')
      qc.invalidateQueries({ queryKey: ['owner-subscription'] })
    },
    onError: (err: unknown) => setRequestError(getBillingErrorMessage(err, 'Не удалось отправить заявку.')),
  })

  const cancelMut = useMutation({
    mutationFn: billingApi.cancelRequest,
    onSuccess: () => {
      setRequestError('')
      qc.invalidateQueries({ queryKey: ['owner-subscription'] })
    },
    onError: (err: unknown) => setRequestError(getBillingErrorMessage(err, 'Не удалось отозвать заявку.')),
  })

  const requestMut = useMutation({
    mutationFn: (options: Record<string, number>) =>
      billingApi.submitRequest({
        options: Object.entries(options).map(([optionId, quantity]) => ({ optionId, quantity })),
        ...(isLine ? { line } : {}),
      }),
    onSuccess: () => {
      setRequestError('')
      setDesiredOptions(null)
      qc.invalidateQueries({ queryKey: ['owner-subscription'] })
    },
    onError: (err: unknown) => setRequestError(getBillingErrorMessage(err, 'Не удалось отправить заявку.')),
  })

  // §363 — activation returns the WHOLE OwnerSubscriptionDto; it replaces the cached query result
  // directly rather than triggering a refetch, so the screen never shows a stale "Available" state
  // for even one request (§371 п.4: "использовать ответ как новое состояние экрана").
  const activateTrialMut = useMutation({
    mutationFn: (termsVersion: string) => billingApi.activateTrial(termsVersion),
    onSuccess: (next) => {
      setTrialError('')
      qc.setQueryData(queryKey, next)
    },
    onError: (err: unknown) => {
      // §371 п.5 — a stale termsVersion (409 TrialTermsVersionMismatch) is not a plain retry: the
      // terms text has moved on, so the fix is to re-fetch and show the CURRENT text, never to
      // resend the same (now-stale) version.
      if (isTrialTermsVersionMismatch(err)) {
        qc.invalidateQueries({ queryKey: ['owner-subscription'] })
        setTrialError('Условия обновились — прочитайте их заново.')
        return
      }
      setTrialError(getTrialErrorMessage(err, 'Не удалось активировать пробный период.'))
    },
  })

  const acknowledgeTermsMut = useMutation({
    mutationFn: (termsVersion: string) => billingApi.acknowledgeTerms(termsVersion),
    onSuccess: () => {
      setTrialError('')
      qc.invalidateQueries({ queryKey: ['owner-subscription'] })
    },
    onError: (err: unknown) => {
      if (isTrialTermsVersionMismatch(err)) {
        qc.invalidateQueries({ queryKey: ['owner-subscription'] })
        setTrialError('Условия обновились — прочитайте их заново.')
        return
      }
      setTrialError(getTrialErrorMessage(err, 'Не удалось подтвердить ознакомление с условиями.'))
    },
  })

  if (isLoading) {
    return (
      <div className="max-w-[860px] mx-auto px-8 pt-16 pb-24">
        <div className="h-9 w-1/3 bg-cream-deep rounded-xl animate-pulse mb-10" />
        <div className="h-52 bg-cream-deep rounded-[22px] animate-pulse mb-6" />
        <div className="h-40 bg-cream-deep rounded-[22px] animate-pulse" />
      </div>
    )
  }

  const notFound = isError && (error as AxiosError)?.response?.status === 404

  if (notFound) {
    return (
      <div className="max-w-[760px] mx-auto px-8 pt-16 pb-24">
        <Card className="p-12 text-center text-muted">
          <Icon name="alert-circle" size={32} strokeWidth={1.4} className="mx-auto mb-3" />
          <p className="text-lg text-ink-soft">
            У вас нет подписки — она появляется, когда вы становитесь ответственным хотя бы за одну компанию.
          </p>
        </Card>
      </div>
    )
  }

  if (isError || !data) {
    return (
      <div className="max-w-[760px] mx-auto px-8 pt-16 pb-24">
        <Card className="p-12 text-center text-muted">
          <Icon name="alert-circle" size={32} strokeWidth={1.4} className="mx-auto mb-3" />
          <p className="text-lg font-medium text-ink-soft mb-4">Не удалось загрузить подписку. Попробуйте снова.</p>
          <Button variant="secondary" loading={isRefetching} onClick={() => refetch()}>
            Попробовать снова
          </Button>
        </Card>
      </div>
    )
  }

  // Н5 (code review, A5): the same "old API response missing an array field" risk that broke
  // PlansTab applies here — default to [] so a stale/partial subscription payload degrades to "no
  // options" instead of throwing on .map/.find/.length.
  const options = data.options ?? []

  const startEditing = () => desiredOptions ?? Object.fromEntries(options.map((o) => [o.optionId, o.quantity]))

  const toggleOption = (option: AvailableOptionDto) => {
    setDesiredOptions((prev) => {
      const base = prev ?? startEditing()
      const next = { ...base }
      if (next[option.optionId]) delete next[option.optionId]
      else {
        // B10: keep the currently-subscribed quantity when re-adding an option that's already
        // connected (e.g. it was in the base set but got removed then re-added during editing),
        // otherwise default to 1 for a brand-new option.
        const subscribed = options.find((o) => o.optionId === option.optionId)
        next[option.optionId] = subscribed?.quantity ?? 1
      }
      return next
    })
  }

  const setOptionQuantity = (optionId: string, quantity: number) => {
    setDesiredOptions((prev) => {
      const base = prev ?? startEditing()
      return { ...base, [optionId]: Math.max(1, quantity) }
    })
  }

  const isEditing = desiredOptions !== null

  return (
    <div className="max-w-[860px] mx-auto px-8 pt-16 pb-24">
      <header className="mb-10">
        <h1 className="font-serif text-[36px] font-medium text-ink mb-2">Ваша подписка</h1>
        <p className="text-sm text-ink-soft">{isBaths ? 'Тариф действует на все ваши бани сразу; он ограничивает число опубликованных ресурсов.' : isStays ? 'Тариф действует на все ваши дома сразу; он ограничивает число опубликованных домов.' : isOrders ? 'Тариф действует на все ваши магазины сразу; опции — общие для аккаунта.' : 'Тариф и опции действуют на все ваши компании сразу.'}</p>
      </header>

      {/* Cycle 18 — трial plan (API_CONTRACT_CYCLE18.md §371). `trial` is null only when it has
          never been available/granted for this account at all. */}
      {data.trial && data.trial.warning && (
        data.trial.warning.dismissible === false ? (
          <TrialExpiredNotice warning={data.trial.warning} />
        ) : (
          <TrialBanner warning={data.trial.warning} />
        )
      )}

      {data.trial && (data.trial.state === 'Active' || data.trial.state === 'Expired') && <TrialCard trial={data.trial} />}

      {data.trial && data.trial.state === 'Available' && data.trial.activationTerms && (
        <TrialActivationTerms
          activationTerms={data.trial.activationTerms}
          onActivate={() => activateTrialMut.mutate(data.trial!.activationTerms!.version)}
          activating={activateTrialMut.isPending}
        />
      )}

      {data.trial &&
        data.trial.state === 'Active' &&
        data.trial.activationTerms?.acknowledgementRequired &&
        !data.trial.warning /* the expired notice above already fully covers this account's screen */ && (
          <TrialActivationTerms
            activationTerms={data.trial.activationTerms}
            acknowledgementOnly
            onAcknowledge={() => acknowledgeTermsMut.mutate(data.trial!.activationTerms!.version)}
            acknowledging={acknowledgeTermsMut.isPending}
          />
        )}

      {trialError && <p className="text-sm text-danger mb-6">{trialError}</p>}

      {data.warning && (
        <Card className={`p-5 mb-6 border ${data.warning.kind === 'Expired' ? 'border-danger bg-danger-bg' : 'border-warning bg-warning-bg'}`}>
          <p className="text-sm font-semibold text-ink mb-1">{data.warning.text}</p>
          {data.warning.affected.length > 0 && (
            <ul className="text-xs text-ink-soft list-disc list-inside">
              {data.warning.affected.map((a) => (
                <li key={a}>{a}</li>
              ))}
            </ul>
          )}
        </Card>
      )}

      {data.usage.overLimitText && (
        <Card className="p-5 mb-6 border border-warning bg-warning-bg">
          <p className="text-sm font-semibold text-ink">{data.usage.overLimitText}</p>
        </Card>
      )}

      {isOrders && data.orders && data.orders.warningLevel !== 'None' && data.orders.text && (
        <Card className={`p-5 mb-6 border ${data.orders.warningLevel === 'Reached' ? 'border-danger bg-danger-bg' : 'border-warning bg-warning-bg'}`} data-testid="orders-limit-banner">
          <p className="text-sm font-semibold text-ink">{data.orders.text}</p>
        </Card>
      )}

      {isStays && data.stays && data.stays.warningLevel !== 'None' && data.stays.text && (
        <Card
          className={`p-5 mb-6 border ${data.stays.warningLevel === 'TrialEnding3d' || data.stays.warningLevel === 'TrialEnding1d' ? 'border-warning bg-warning-bg' : 'border-danger bg-danger-bg'}`}
          data-testid="stays-plan-banner"
        >
          <p className="text-sm font-semibold text-ink">{data.stays.text}</p>
        </Card>
      )}

      {isBaths && data.baths && data.baths.warningLevel !== 'None' && data.baths.text && (
        <Card
          className={`p-5 mb-6 border ${data.baths.warningLevel === 'TrialEnding3d' || data.baths.warningLevel === 'TrialEnding1d' ? 'border-warning bg-warning-bg' : 'border-danger bg-danger-bg'}`}
          data-testid="baths-plan-banner"
        >
          <p className="text-sm font-semibold text-ink">{data.baths.text}</p>
        </Card>
      )}

      <Card className="p-[26px] mb-6">
        <div className="flex items-center justify-between flex-wrap gap-2 mb-4">
          <h2 className="text-[15.5px] font-semibold text-ink">{data.plan.name}</h2>
          <span
            className={`text-xs font-semibold px-2.5 py-1 rounded-full ${STATUS_BADGE_CLASS[data.status] ?? 'bg-cream-deep text-muted'}`}
          >
            {data.statusText}
          </span>
        </div>
        <p className="text-2xl font-bold text-ink mb-1">{formatMonthlyPrice(data.totalMonthlyPrice)}</p>
        <p className="text-xs text-muted mb-4">
          {formatMonthlyPrice(data.plan.pricePerMonth)} тариф
          {options.length > 0 && ` + ${options.length} опц.`}
        </p>

        <MessengerAddonLines addons={data.messengerAddons} note={data.messengerAddonsNote} />

        <dl className="grid gap-2 text-sm text-ink-soft mb-2">
          <div className="flex justify-between">
            <dt>{data.usage.companiesText}</dt>
          </div>
          <div className="flex justify-between">
            <dt>{data.usage.employeesText}</dt>
          </div>
          <div className="flex justify-between">
            <dt>{data.usage.numbersText}</dt>
          </div>
          {isOrders && data.orders?.text && (
            <div className="flex justify-between" data-testid="orders-usage">
              <dt>{data.orders.text}</dt>
            </div>
          )}
          {isStays && data.stays && (
            <div className="flex justify-between" data-testid="stays-usage">
              <dt>
                Опубликовано домов: {data.stays.housesPublished}
                {data.stays.maxHouses != null ? ` из ${data.stays.maxHouses}` : ''}
                {data.stays.isTrial ? ' · пробный период' : ''}
              </dt>
            </div>
          )}
          {isBaths && data.baths && (
            <div className="flex justify-between" data-testid="baths-usage">
              <dt>
                Опубликовано ресурсов: {data.baths.resourcesPublished}
                {data.baths.maxResources != null ? ` из ${data.baths.maxResources}` : ''}
                {data.baths.isTrial ? ' · пробный период' : ''}
              </dt>
            </div>
          )}
        </dl>
      </Card>

      {options.length > 0 && (
        <Card className="p-[26px] mb-6">
          <h2 className="text-[15.5px] font-semibold text-ink mb-4">Подключённые опции</h2>
          <ul className="grid gap-3">
            {options.map((o) => (
              <li key={o.optionId} className="flex items-center justify-between text-sm">
                <span>
                  {o.name}
                  {o.kind === 'Quantity' && ` × ${o.quantity}`}
                </span>
                <span className="text-ink-soft">{o.statusText}</span>
              </li>
            ))}
          </ul>
        </Card>
      )}

      {data.lastRejectedRequest && !data.pendingRequest && (
        <Card className="p-[26px] mb-6 border border-danger bg-danger-bg">
          <h2 className="text-[15.5px] font-semibold text-ink mb-2">Заявка отклонена</h2>
          <p className="text-sm text-ink-soft">{data.lastRejectedRequest.reason}</p>
        </Card>
      )}

      {isLine && data.availablePlans && data.availablePlans.length > 0 && (
        <Card className="p-[26px] mb-6" data-testid="available-plans">
          <h2 className="text-[15.5px] font-semibold text-ink mb-4">{isBaths ? 'Тарифы «Бань»' : isStays ? 'Тарифы «Домов»' : 'Тарифы «Заказов»'}</h2>
          <ul className="grid gap-4">
            {data.availablePlans.map((p) => (
              <li key={p.planId} className="flex items-start justify-between gap-4 flex-wrap">
                <div className="min-w-0">
                  <p className="font-semibold text-ink">
                    {p.name} · <span className="font-normal">{formatMonthlyPrice(p.pricePerMonth)}</span>
                  </p>
                  {p.description && <p className="text-sm text-ink-soft mt-0.5">{p.description}</p>}
                  {p.highlights && p.highlights.length > 0 && (
                    <ul className="text-sm text-ink-soft list-disc pl-5 mt-1">
                      {p.highlights.map((h) => (
                        <li key={h}>{h}</li>
                      ))}
                    </ul>
                  )}
                  <p className="text-xs text-muted mt-1">{p.limitsText}</p>
                </div>
                {data.canRequestChanges && (
                  <Button size="sm" variant="secondary" className="min-h-[44px]" loading={planRequestMut.isPending && planRequestMut.variables === p.planId} onClick={() => planRequestMut.mutate(p.planId)} aria-label={`Запросить тариф «${p.name}»`}>
                    Запросить
                  </Button>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}

      {data.pendingRequest && !isEditing && (
        <Card className="p-[26px] mb-6 border border-line">
          <h2 className="text-[15.5px] font-semibold text-ink mb-2">Заявка на рассмотрении</h2>
          {data.pendingRequest.items && data.pendingRequest.items.length > 0 && (
            <ul className="grid gap-1 mb-3 text-sm">
              {data.pendingRequest.items.map((i) => (
                <li key={i.optionId} className={i.retired ? 'text-muted' : 'text-ink-soft'}>
                  <span className={i.retired ? 'line-through' : undefined}>
                    {i.name} × {i.quantity}
                  </span>
                  {i.retired && <span className="ml-1.5 text-xs text-warning">выведена</span>}
                </li>
              ))}
            </ul>
          )}
          <p className="text-sm text-ink-soft mb-4">
            Ожидаемый итог: {formatMonthlyPrice(data.pendingRequest.estimatedMonthlyPrice)}
          </p>
          {/* ARCHITECTURE_CYCLE19.md FE-4, API_CONTRACT_CYCLE19.md §408 — server-composed text,
              printed verbatim, shown whenever the pending request has at least one retired
              (opция-лимит) line, i.e. it was submitted before this cycle's rollout. */}
          {data.pendingRequest.retiredOptionsNotice && (
            <p className="text-sm font-semibold text-warning bg-warning-bg border border-warning rounded-xl px-3.5 py-2.5 mb-4">
              {data.pendingRequest.retiredOptionsNotice}
            </p>
          )}
          {/* API_CONTRACT_CYCLE17.md §325.1 (US-17-08, C15-7) — server-composed text, shown verbatim,
              both right after submitting a request (the mutation invalidates and refetches this same
              query) and on every later open of this screen, since it lives on `pendingRequest`. */}
          {data.pendingRequest.irreversibilityNotice && (
            <p className="text-sm font-semibold text-warning bg-warning-bg border border-warning rounded-xl px-3.5 py-2.5 mb-4">
              {data.pendingRequest.irreversibilityNotice}
            </p>
          )}
          <Button variant="secondary" size="sm" loading={cancelMut.isPending} onClick={() => cancelMut.mutate()}>
            Отозвать заявку
          </Button>
        </Card>
      )}

      {data.canRequestChanges && data.availableOptions.length > 0 && (
        <Card className="p-[26px] mb-6">
          <h2 className="text-[15.5px] font-semibold text-ink mb-4">Доступные опции</h2>
          <ul className="grid gap-3 mb-4">
            {data.availableOptions.map((option) => {
              const subscribed = options.find((o) => o.optionId === option.optionId)
              const selected = isEditing ? !!desiredOptions?.[option.optionId] : !!subscribed
              const quantity = isEditing ? desiredOptions?.[option.optionId] ?? 0 : subscribed?.quantity ?? 0
              const showQuantityInput = option.kind === 'Quantity' && isEditing && selected
              return (
                <li key={option.optionId} className="flex items-center justify-between text-sm gap-3 flex-wrap">
                  <div>
                    <p className="font-medium text-ink">{option.name}</p>
                    <p className="text-xs text-muted">{option.availabilityText}</p>
                    {!isEditing && option.kind === 'Quantity' && subscribed && (
                      <p className="text-xs text-ink-soft mt-0.5">Подключено: {quantity}</p>
                    )}
                  </div>
                  <div className="flex items-center gap-2">
                    {showQuantityInput && (
                      <input
                        type="number"
                        min={1}
                        max={option.maxQuantity ?? undefined}
                        value={quantity}
                        aria-label={`Количество: ${option.name}`}
                        onChange={(e) => setOptionQuantity(option.optionId, parseInt(e.target.value) || 1)}
                        className="w-16 rounded-lg border border-line px-2 py-1.5 text-sm outline-none focus:border-gold"
                      />
                    )}
                    {option.canRequest && (
                      <Button
                        variant={selected ? 'secondary' : 'primary'}
                        size="sm"
                        onClick={() => toggleOption(option)}
                      >
                        {selected ? 'Убрать' : 'Добавить'}
                      </Button>
                    )}
                  </div>
                </li>
              )
            })}
          </ul>

          {isEditing && (
            <>
              {/* О9 (§365, §371 п.13) — must be shown BEFORE confirming a request for a paid plan
                  while a trial is active: activating this request forfeits the trial's remainder. */}
              {data.trial?.planChangeNotice && (
                <p className="text-sm font-semibold text-warning bg-warning-bg border border-warning rounded-xl px-3.5 py-2.5 mb-4">
                  {data.trial.planChangeNotice}
                </p>
              )}
              <div className="flex items-center gap-3">
                <Button loading={requestMut.isPending} onClick={() => requestMut.mutate(desiredOptions!)}>
                  Отправить заявку
                </Button>
                <Button variant="ghost" onClick={() => setDesiredOptions(null)}>
                  Отменить
                </Button>
              </div>
            </>
          )}
        </Card>
      )}

      {requestError && <p className="text-sm text-danger mb-6">{requestError}</p>}

      {/* US-20-03 — compact summary + link to /notices; ARCHITECTURE_CYCLE20.md §404.5. */}
      <BillingNoticesSummary />

      {/* Т20-04 п. 3 (US-20-01) — operator-of-record details for the paper health-consent form. */}
      {!isLine && <OperatorDetailsSection />}

      {!isLine && (
        <p className="text-xs text-muted">
          Хотите сравнить тарифы целиком? <Link to="/pricing" className="underline">Смотрите страницу тарифов</Link>.
        </p>
      )}
    </div>
  )
}
