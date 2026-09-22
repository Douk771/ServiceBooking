import type { AssignOptionInput, AssignSubscriptionInput, SubscriptionStatus } from '../../api/adminBilling'

export const STATUS_LABEL_RU: Record<SubscriptionStatus, string> = {
  Free: 'бесплатный',
  Active: 'активна',
  Expired: 'истекла',
}

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
 * from the difference itself (API_CONTRACT_CYCLE5.md §49, AssignSubscriptionInput.options).
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

export function buildAssignInput(params: {
  planId: string | null
  isActive: boolean
  paidUntil: string | null
  rows: AssignOptionRow[]
  amount: string
  comment: string
  requestId?: string | null
  confirmLimitOverflow: boolean
}): AssignSubscriptionInput {
  return {
    planId: params.planId,
    isActive: params.isActive,
    paidUntil: params.paidUntil || null,
    options: optionRowsToPayload(params.rows),
    amount: params.amount.trim() === '' ? null : Number(params.amount),
    comment: params.comment.trim() === '' ? null : params.comment.trim(),
    requestId: params.requestId ?? null,
    confirmLimitOverflow: params.confirmLimitOverflow,
  }
}
