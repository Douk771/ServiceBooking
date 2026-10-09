import type { CreateServiceOrderInput, PublicServiceQuoteInput, ServiceQuoteDto } from '@/types/slots'
import { validateGuestFields, type GuestFieldErrors } from './guestFields'
import { toSelectionInput, type SessionPick } from './serviceSelection'

/** The order form of a separate session (API_CONTRACT_CYCLE39.md §39.22.5): fields, local checks, request bodies. */

export interface OrderGuestFields {
  name: string
  /** Canonical digits of the phone (anonymous guests only). */
  phone: string
  comment: string
  notifyByMessenger: boolean
}

/** Same checks and texts as the booking form (the server repeats them, §39.31). */
export function validateOrderFields(
  f: OrderGuestFields,
  opts: { anonymous: boolean; captchaRequired: boolean; captchaToken: string },
): GuestFieldErrors {
  return validateGuestFields({ ...f, arrivalTime: '' }, opts)
}

/**
 * «Сколько человек придёт, включая детей» (Т42-06) of a place with a capacity: a whole number 1…capacity, no default. Nothing else is
 * asked (no ages, no adults/children split). null — the field is fine.
 */
export function guestsCountProblem(raw: string, capacity: number): string | null {
  const text = raw.trim()
  if (!/^\d+$/.test(text)) return `Укажите число гостей — от 1 до ${capacity}`
  const n = Number(text)
  return n >= 1 && n <= capacity ? null : `Укажите число гостей — от 1 до ${capacity}`
}

export function toServiceQuoteInput(pick: SessionPick, order: readonly string[]): PublicServiceQuoteInput {
  return toSelectionInput(pick, order)
}

export function toCreateOrderInput(args: {
  pick: SessionPick
  order: readonly string[]
  guest: OrderGuestFields
  anonymous: boolean
  quote: ServiceQuoteDto
  idempotencyKey: string
  captchaToken: string
  /** Only for a place with a capacity (bani); a service of dom has none and the field is not sent. */
  guestsCount?: number | null
}): CreateServiceOrderInput {
  const { guest } = args
  return {
    ...toSelectionInput(args.pick, args.order),
    guestName: guest.name.trim(),
    // A signed-in guest orders for the number of the account: the server ignores the field, so it is not sent.
    guestPhone: args.anonymous ? guest.phone : null,
    comment: guest.comment.trim() || null,
    notifyByMessenger: guest.notifyByMessenger,
    expectedTotalRub: args.quote.totalRub,
    idempotencyKey: args.idempotencyKey,
    captchaToken: args.anonymous && args.captchaToken ? args.captchaToken : null,
    ...(args.guestsCount != null ? { guestsCount: args.guestsCount } : {}),
  }
}
