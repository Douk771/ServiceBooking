import type { CreateServiceOrderInput, PublicServiceQuoteInput, ServiceQuoteDto } from '../types'
import { validateGuestFields, type GuestFieldErrors } from './bookingForm'
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
  }
}
