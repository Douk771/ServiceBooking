import { Card } from '../ui/Card'
import { Button } from '../ui/Button'
import { formatMonthlyPrice } from '../../utils/pricingFormat'

export interface AvailablePlanView {
  planId: string
  name: string
  pricePerMonth: number
  description?: string | null
  highlights?: string[]
  limitsText: string
}

/**
 * The «Тарифы …» card of /billing. Shared by every line; only «Записи» (cycle 49) passes `currentPlanId`, `pendingPlanId`
 * and `confirmNotice`: the other lines render exactly as before (no marks, no confirmation).
 */
export function AvailablePlansBlock({
  title,
  plans,
  canRequest,
  onRequest,
  inFlightPlanId,
  currentPlanId,
  pendingPlanId,
  confirmPlanId,
  confirmNotice,
  onConfirm,
  onCancelConfirm,
}: {
  title: string
  plans: AvailablePlanView[]
  canRequest: boolean
  onRequest: (planId: string) => void
  /** Plan whose request is being sent right now (spinner on its button). */
  inFlightPlanId?: string | null
  currentPlanId?: string | null
  pendingPlanId?: string | null
  /** Plan waiting for confirmation (trial notice must be read first). */
  confirmPlanId?: string | null
  confirmNotice?: string | null
  onConfirm?: (planId: string) => void
  onCancelConfirm?: () => void
}) {
  return (
    <Card className="p-[26px] mb-6" data-testid="available-plans">
      <h2 className="text-[15.5px] font-semibold text-ink mb-4">{title}</h2>
      <ul className="grid gap-4">
        {plans.map((p) => {
          const isCurrent = !!currentPlanId && p.planId === currentPlanId
          const isPending = !!pendingPlanId && p.planId === pendingPlanId
          const confirming = !!confirmNotice && confirmPlanId === p.planId
          return (
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
                {confirming && (
                  <div className="mt-3" data-testid="plan-confirm">
                    <p className="text-sm font-semibold text-warning bg-warning-bg border border-warning rounded-xl px-3.5 py-2.5 mb-3">
                      {confirmNotice}
                    </p>
                    <div className="flex items-center gap-3">
                      <Button size="sm" className="min-h-[44px]" loading={inFlightPlanId === p.planId} onClick={() => onConfirm?.(p.planId)}>
                        Отправить заявку
                      </Button>
                      <Button size="sm" variant="ghost" className="min-h-[44px]" onClick={() => onCancelConfirm?.()}>
                        Отменить
                      </Button>
                    </div>
                  </div>
                )}
              </div>
              {isCurrent ? (
                <span className="text-sm font-semibold text-success">Ваш тариф</span>
              ) : isPending ? (
                <span className="text-sm font-semibold text-ink-soft">Заявка отправлена</span>
              ) : (
                canRequest &&
                !confirming && (
                  <Button
                    size="sm"
                    variant="secondary"
                    className="min-h-[44px]"
                    loading={inFlightPlanId === p.planId}
                    onClick={() => onRequest(p.planId)}
                    aria-label={`Запросить тариф «${p.name}»`}
                  >
                    Запросить
                  </Button>
                )
              )}
            </li>
          )
        })}
      </ul>
    </Card>
  )
}
