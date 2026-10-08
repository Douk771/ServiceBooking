import type { CreateStayBookingInput, PublicHouseDto, StayQuoteInput } from '../types'
import { checkGuests } from './stayMoney'
import { isRussianPhone } from '@/utils/phone'

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
export const COMMENT_MAX = 500
export const NAME_MAX = 100

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

export interface GuestFields {
  name: string
  /** Canonical digits of the phone (anonymous guests only). */
  phone: string
  arrivalTime: string
  comment: string
  notifyByMessenger: boolean
}

export interface GuestFieldErrors {
  name?: string
  phone?: string
  comment?: string
  captcha?: string
}

/** Local checks before the request — same texts as the server's 400 (API_CONTRACT_CYCLE37.md §37.36). */
export function validateGuestFields(f: GuestFields, opts: { anonymous: boolean; captchaRequired: boolean; captchaToken: string }): GuestFieldErrors {
  const e: GuestFieldErrors = {}
  const name = f.name.trim()
  if (!name) e.name = 'Укажите имя'
  else if (name.length > NAME_MAX) e.name = 'Имя — не длиннее 100 символов'
  if (opts.anonymous && !isRussianPhone(f.phone)) e.phone = 'Введите номер телефона в формате +7 (900) 000-00-00'
  if (f.comment.length > COMMENT_MAX) e.comment = 'Комментарий — не длиннее 500 символов'
  if (opts.anonymous && opts.captchaRequired && !opts.captchaToken) e.captcha = 'Подтвердите, что вы не робот'
  return e
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

/**
 * A 400 of the booking form is a bare string; this finds the field it belongs to so the text appears under that field
 * (API_CONTRACT_CYCLE37.md §37.36: «фронт показывает дословно у поля»). Unknown text stays a form-level error.
 */
export function fieldOfBookingError(text: string): keyof GuestFieldErrors | 'arrival' | 'dates' | 'guests' | null {
  if (/^Укажите имя|^Имя — /.test(text)) return 'name'
  if (/номер телефона/.test(text)) return 'phone'
  if (/^Комментарий/.test(text)) return 'comment'
  if (/не робот/.test(text)) return 'captcha'
  if (/^Время прибытия/.test(text)) return 'arrival'
  if (/дат[ыа] (заезда|выезда)|формат даты|^Укажите даты/.test(text)) return 'dates'
  if (/^(Взрослых|Детей|Собак) — /.test(text)) return 'guests'
  return null
}
