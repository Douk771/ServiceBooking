import { isRussianPhone } from '@/utils/phone'

export const COMMENT_MAX = 500
export const NAME_MAX = 100

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
