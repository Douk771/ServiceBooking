import type { ManualStayBookingInput, StaffStayQuoteInput } from '../types'
import { isRussianPhone } from '@/utils/phone'
import { isIsoDate, nightsBetween } from './stayDates'

/**
 * Manual booking by staff (`ManageBookings`, P1, US-37-24, API_CONTRACT_CYCLE37.md §37.30.5): phone calls and acquaintances. It is
 * `Confirmed` at once, with no prepayment; the minimum, maximum and horizon do not apply, an overlap does. The total may be replaced by a
 * hand-made one (a single line «Итог изменён вручную»).
 */
export interface ManualForm {
  houseId: string
  checkIn: string
  checkOut: string
  adults: number
  children: number
  dogs: number
  needCot: boolean
  guestName: string
  /** Canonical digits; empty = no phone. */
  guestPhone: string
  notifyGuest: boolean
  /** NaN / empty = the calculated total. */
  totalOverrideRub: number
  comment: string
}

export type ManualField = 'houseId' | 'dates' | 'guestName' | 'guestPhone' | 'totalOverrideRub' | 'comment'

export const MANUAL_TOTAL_MAX = 10_000_000

export function validateManual(f: ManualForm): Partial<Record<ManualField, string>> {
  const e: Partial<Record<ManualField, string>> = {}
  if (!f.houseId) e.houseId = 'Выберите дом'
  if (!isIsoDate(f.checkIn) || !isIsoDate(f.checkOut)) e.dates = 'Укажите даты заезда и выезда'
  else if (nightsBetween(f.checkIn, f.checkOut) <= 0) e.dates = 'Дата выезда должна быть позже даты заезда'
  const name = f.guestName.trim()
  if (!name) e.guestName = 'Укажите имя'
  else if (name.length > 100) e.guestName = 'Имя — не длиннее 100 символов'
  if (f.guestPhone && !isRussianPhone(f.guestPhone)) e.guestPhone = 'Введите номер телефона в формате +7 (900) 000-00-00'
  else if (f.notifyGuest && !f.guestPhone) e.guestPhone = 'Чтобы уведомить гостя, укажите телефон'
  if (!Number.isNaN(f.totalOverrideRub)) {
    if (!Number.isInteger(f.totalOverrideRub) || f.totalOverrideRub < 0 || f.totalOverrideRub > MANUAL_TOTAL_MAX) e.totalOverrideRub = 'Итог — от 0 до 10 000 000 ₽'
  }
  if (f.comment.length > 500) e.comment = 'Комментарий — не длиннее 500 символов'
  return e
}

export function toManualQuote(f: ManualForm): StaffStayQuoteInput {
  return { houseId: f.houseId, checkIn: f.checkIn, checkOut: f.checkOut, adults: f.adults, children: f.children, dogs: f.dogs, needCot: f.needCot }
}

export function toManualInput(f: ManualForm): ManualStayBookingInput {
  return {
    ...toManualQuote(f),
    guestName: f.guestName.trim(),
    guestPhone: f.guestPhone || null,
    notifyGuest: f.notifyGuest && !!f.guestPhone,
    totalOverrideRub: Number.isNaN(f.totalOverrideRub) ? null : f.totalOverrideRub,
    comment: f.comment.trim() || null,
  }
}

export function manualFieldOfError(text: string): ManualField | null {
  if (/укажите имя|^Имя — /i.test(text)) return 'guestName'
  if (/телефон/i.test(text)) return 'guestPhone'
  if (/^Итог/.test(text)) return 'totalOverrideRub'
  if (/^Комментарий/.test(text)) return 'comment'
  return null
}
