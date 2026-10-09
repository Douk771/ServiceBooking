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
