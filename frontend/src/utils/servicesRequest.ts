/**
 * Cycle 49 (API_CONTRACT_CYCLE49 §49.26.2): the server keeps ONE open request per account and overwrites it whole on every
 * POST, so the «Записи» screen carries over what the open request of the same line already holds (plan <-> options).
 */
export interface OpenRequestView {
  line?: string
  desiredPlanId?: string | null
  items?: { optionId: string; quantity: number; retired?: boolean }[]
}

export interface ServicesRequestBody {
  planId?: string
  options: { optionId: string; quantity: number }[]
}

const isSameLine = (pr?: OpenRequestView | null): pr is OpenRequestView => !!pr && pr.line === 'Services'

/** «Запросить» on a plan: the plan plus the options of the open request (without retired ones). */
export function buildPlanRequest(planId: string, pr?: OpenRequestView | null): ServicesRequestBody {
  return {
    planId,
    options: isSameLine(pr)
      ? (pr.items ?? []).filter((i) => !i.retired).map((i) => ({ optionId: i.optionId, quantity: i.quantity }))
      : [],
  }
}

/** «Отправить заявку» on options: the chosen set plus the plan of the open request, if any. */
export function buildOptionsRequest(options: Record<string, number>, pr?: OpenRequestView | null): ServicesRequestBody {
  return {
    options: Object.entries(options).map(([optionId, quantity]) => ({ optionId, quantity })),
    ...(isSameLine(pr) && pr.desiredPlanId ? { planId: pr.desiredPlanId } : {}),
  }
}
