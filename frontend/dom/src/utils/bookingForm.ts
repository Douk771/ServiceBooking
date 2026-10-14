import type { CreateStayBookingInput, PublicHouseDto, StayQuoteInput } from '../types'
import { checkGuests } from './stayMoney'
import type { GuestFields } from '@/utils/slots/guestFields'

/** The booking form of the house page: guest counts, local checks (the server re-checks all of them), request bodies. */

export interface GuestCounts {
  adults: number
  children: number
  dogs: number
  needCot: boolean
}

export const MAX_ADULTS = 30
export const MAX_CHILDREN = 30
export const MAX_DOGS = 20

type HouseLimits = Pick<PublicHouseDto, 'capacity' | 'extraBeds' | 'dogsForbidden' | 'hasCot'>

/** Most guests the house takes: capacity plus the extra beds when the owner enabled them. */
export function maxGuests(house: Pick<PublicHouseDto, 'capacity' | 'extraBeds'>): number {
  return house.capacity + (house.extraBeds.enabled ? house.extraBeds.max : 0)
}

/** Extra beds the stay needs (guests over capacity). */
export function extraBedsNeeded(house: Pick<PublicHouseDto, 'capacity'>, counts: Pick<GuestCounts, 'adults' | 'children'>): number {
  return Math.max(0, counts.adults + counts.children - house.capacity)
}

/** The server's wording (API_CONTRACT_CYCLE37.md §37.25.2) for a guest count the house cannot take; null when it fits. */
export function guestCountsProblem(house: HouseLimits, counts: GuestCounts): string | null {
  const problem = checkGuests({
    capacity: house.capacity,
    extraBeds: house.extraBeds,
    dogsForbidden: house.dogsForbidden,
    hasCot: house.hasCot,
    ...counts,
  })
  if (problem === 'TooManyGuests') {
    return house.extraBeds.enabled
      ? `В доме помещается не больше ${maxGuests(house)} гостей, включая доп. места`
      : `В доме помещается не больше ${house.capacity} гостей`
  }
  if (problem === 'DogsNotAllowed') return 'В этом доме нельзя проживать с собаками'
  if (problem === 'CotNotAvailable') return 'В этом доме нет детской кроватки'
  return null
}

export function toQuoteInput(checkIn: string, checkOut: string, counts: GuestCounts): StayQuoteInput {
  return { checkIn, checkOut, adults: counts.adults, children: counts.children, dogs: counts.dogs, needCot: counts.needCot }
}

export function toCreateInput(args: {
  checkIn: string
  checkOut: string
  counts: GuestCounts
  guest: GuestFields
  anonymous: boolean
  expectedTotalRub: number
  idempotencyKey: string
  captchaToken: string
}): CreateStayBookingInput {
  const { guest } = args
  return {
    checkIn: args.checkIn,
    checkOut: args.checkOut,
    adults: args.counts.adults,
    children: args.counts.children,
    dogs: args.counts.dogs,
    needCot: args.counts.needCot,
    guestName: guest.name.trim(),
    // A signed-in guest books for the number of the account: the server ignores the field, so it is not sent.
    guestPhone: args.anonymous ? guest.phone : null,
    arrivalTime: guest.arrivalTime || null,
    comment: guest.comment.trim() || null,
    notifyByMessenger: guest.notifyByMessenger,
    expectedTotalRub: args.expectedTotalRub,
    idempotencyKey: args.idempotencyKey,
    captchaToken: args.anonymous && args.captchaToken ? args.captchaToken : null,
  }
}

export { COMMENT_MAX, NAME_MAX, validateGuestFields, fieldOfBookingError } from '@/utils/slots/guestFields'
export type { GuestFields, GuestFieldErrors } from '@/utils/slots/guestFields'
