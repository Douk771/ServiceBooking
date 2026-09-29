import type { AssignOptionInput, AssignSubscriptionInput, SubscriptionChangeReason, SubscriptionStatus } from '../../api/adminBilling'

export const STATUS_BADGE_CLASS: Record<SubscriptionStatus, string> = {
  Free: 'bg-cream-deep text-ink-soft',
  Active: 'bg-success-bg text-success',
  Expired: 'bg-danger-bg text-danger',
}

/** ru('₽ 12 345') formatting shared by every money field on this screen — whole rubles only
 *  (Currency = RUB is the only value the contract ever returns), never fractional kopecks. */
export function formatRub(value: number): string {
  return `${Math.round(value).toLocaleString('ru-RU')} ₽`
}

export interface AssignOptionRow {
  optionId: string
  name: string
  kind: 'Toggle' | 'Quantity'
  unitName?: string | null
  pricePerMonth: number
  selected: boolean
  quantity: string
}

/**
 * Builds the AssignOptionInput[] payload from the editable rows shown in the assign-subscription
 * form. Only selected rows are sent — an option left unselected (or a lowered quantity) is exactly
 * the "full desired composition" the contract asks for; the server derives the wind-down (endsAt)
 * from the difference itself (API_CONTRACT_CYCLE7.md §49, AssignSubscriptionInput.options).
 */
export function optionRowsToPayload(rows: AssignOptionRow[]): AssignOptionInput[] {
  return rows
    .filter((r) => r.selected)
    .map((r) => ({
      optionId: r.optionId,
      quantity: r.kind === 'Toggle' ? 1 : Math.max(1, parseInt(r.quantity, 10) || 1),
    }))
}

/**
 * The invariant the contract calls out explicitly (SPEC / AdminBillingAccountDto): total = plan
 * price + Σ (option price per unit × quantity). Computed independently from whatever
 * totalMonthlyPrice the server already sends, so it can be displayed next to the server figure
 * as a visible check rather than an assumption baked into the layout.
 */
export function computeExpectedTotal(planPricePerMonth: number, rows: AssignOptionRow[], pricePerUnitByOption: Record<string, number>): number {
  const optionsTotal = rows
    .filter((r) => r.selected)
    .reduce((sum, r) => {
      const qty = r.kind === 'Toggle' ? 1 : Math.max(1, parseInt(r.quantity, 10) || 1)
      const unitPrice = pricePerUnitByOption[r.optionId] ?? 0
      return sum + unitPrice * qty
    }, 0)
  return planPricePerMonth + optionsTotal
}

/**
 * Free (planId === '') never carries an expiry — the date field is hidden entirely for it in the
 * UI. Any paid plan without "Оплачено до" is the last local guard before the server's own 400
 * (ARCHITECTURE_CYCLE6.md §43.3.6): a never-expiring paid subscription must never reach the API.
 */
export function isPaidUntilMissing(planId: string, paidUntil: string): boolean {
  return planId !== '' && !paidUntil
}

export function buildAssignInput(params: {
  planId: string | null
  isActive: boolean
  paidUntil: string | null
  rows: AssignOptionRow[]
  amount: string
  comment: string
  requestId?: string | null
  confirmLimitOverflow: boolean
  reasonCode?: SubscriptionChangeReason | null
  reasonDetails?: string
}): AssignSubscriptionInput {
  return {
    planId: params.planId,
    isActive: params.isActive,
    // Free plan (no planId) never carries an expiry, regardless of what's left in the date field.
    paidUntil: params.planId ? params.paidUntil || null : null,
    options: optionRowsToPayload(params.rows),
    amount: params.amount.trim() === '' ? null : Number(params.amount),
    comment: params.comment.trim() === '' ? null : params.comment.trim(),
    requestId: params.requestId ?? null,
    confirmLimitOverflow: params.confirmLimitOverflow,
    // `undefined` (not `null`) when unset, so a caller that never mentions a reason keeps sending
    // EXACTLY the same body it sent before this cycle (JSON.stringify drops undefined keys) — this is
    // what the existing buildAssignInput tests assert with a strict `toEqual`, and it also matches
    // §433.1: a reason omitted where it isn't required is simply absent, not an explicit "no reason".
    reasonCode: params.reasonCode ?? undefined,
    reasonDetails: params.reasonDetails?.trim() ? params.reasonDetails.trim() : undefined,
  }
}

/**
 * API_CONTRACT_CYCLE20.md §433.1 (US-20-02, П3) / ARCHITECTURE_CYCLE20.md §403.2
 * (`ManualPlanAssignmentPolicy.RequiresReason`) mirrored on the client so the "Основание" field can
 * appear/become required WITHOUT a round-trip — the server re-checks everything regardless (this is a
 * UX hint, not the source of truth). A reason is required exactly when the target plan is hidden
 * (`isPublic: false`) AND differs from the account's current plan; picking the SAME hidden plan again
 * (renewal) or a public plan never requires one. `targetPlanId: null` (Free) never requires a reason
 * either — Free is always public in spirit and never "hidden" in the §433.1 sense.
 */
export function isManualReasonRequired(currentPlanId: string | null, targetPlanId: string | null, targetIsPublic: boolean | undefined): boolean {
  if (!targetPlanId) return false
  if (targetIsPublic !== false) return false
  return targetPlanId !== (currentPlanId ?? null)
}

/**
 * Client-side mirror of the three `reasonCode`/`reasonDetails` checks the server enforces in
 * `ManualPlanAssignmentPolicy.Validate` (§433.1) — used only to disable the Save button with an
 * inline hint before a doomed request goes out; the server's own 400 text remains authoritative and
 * is what actually gets shown if this check is somehow bypassed (stale UI, race with a plan edit).
 */
export function manualReasonValidationError(reasonCode: SubscriptionChangeReason | '', reasonDetails: string, required: boolean): string | null {
  if (required && !reasonCode) return 'Выберите основание для назначения скрытого тарифа.'
  if (reasonCode === 'TrialReissue') return 'Пробный период назначается только через «Выдать повторно», не эту форму.'
  if (reasonCode === 'OperatorErrorCorrection' && !reasonDetails.trim()) return 'Опишите исправляемую ошибку.'
  if (reasonDetails.length > 1000) return 'Описание не длиннее 1000 символов.'
  return null
}
