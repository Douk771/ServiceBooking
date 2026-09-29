import type { ShopCustomerMode } from '../types'

export type CheckoutGate =
  | { kind: 'open' }
  | { kind: 'login-required' }
  | { kind: 'checking' }
  | { kind: 'verify-phone' }
  | { kind: 'verification-unavailable' }

/**
 * Which checkout form to show (SPEC US-23-19, Q2). Only the UI decision lives here — the server re-checks
 * every case (§413.1 steps 6–8) and stays the authority.
 *  - `Anyone`: always open (guest with captcha, or signed-in with the account phone).
 *  - `VerifiedPhoneOnly`: sign in first; then the account phone must be confirmed (MAX). If the confirmation
 *    subsystem is off and the phone is not confirmed, ordering is impossible — say so instead of a dead button.
 */
export function checkoutGate(input: {
  customerMode: ShopCustomerMode
  signedIn: boolean
  /** `undefined` while the profile is loading. */
  phoneVerified: boolean | undefined
  /** `undefined` while the platform config is loading. */
  verificationEnabled: boolean | undefined
}): CheckoutGate {
  if (input.customerMode === 'Anyone') return { kind: 'open' }
  if (!input.signedIn) return { kind: 'login-required' }
  if (input.phoneVerified === true) return { kind: 'open' }
  if (input.phoneVerified === undefined || input.verificationEnabled === undefined) return { kind: 'checking' }
  return input.verificationEnabled ? { kind: 'verify-phone' } : { kind: 'verification-unavailable' }
}

/** `/login?returnTo=/<slug>?checkout=1` — the cart stays in localStorage across the round trip. */
export function loginUrlForCheckout(slug: string, target: 'login' | 'register' = 'login'): string {
  return `/${target}?returnTo=${encodeURIComponent(`/${slug}?checkout=1`)}`
}

export interface CheckoutFields {
  name: string
  comment: string
  /** Canonical phone digits — required only for a guest. */
  phone: string
  guest: boolean
  captchaRequired: boolean
  captchaToken: string
}

/** First problem in the buyer's own fields, in Russian, or null. The server has its own (identical) texts. */
export function validateCheckout(f: CheckoutFields, isRussianPhone: (p: string) => boolean): string | null {
  if (!f.name.trim()) return 'Укажите имя'
  if (f.name.trim().length > 100) return 'Имя — не длиннее 100 символов'
  if (f.comment.length > 500) return 'Комментарий — не длиннее 500 символов'
  if (f.guest) {
    if (!f.phone) return 'Укажите телефон'
    if (!isRussianPhone(f.phone)) return 'Введите номер телефона в формате +7 (900) 000-00-00'
    if (f.captchaRequired && !f.captchaToken) return 'Подтвердите, что вы не робот'
  }
  return null
}

/** «+7 (9**) ***-**-67» — the masked number for the messenger consent line (SPEC: only a mask, never the full number). */
export function maskPhone(canonical: string | null | undefined): string {
  const digits = (canonical ?? '').replace(/\D/g, '')
  if (digits.length !== 11 || digits[0] !== '7') return ''
  return `+7 (${digits[1]}**) ***-**-${digits.slice(9)}`
}

/** The fallback consent line when `OrderMessengerConsent` has no text yet (API_CONTRACT_CYCLE24.md §478.3). */
export function messengerConsentFallback(canonical: string | null | undefined): string {
  const mask = maskPhone(canonical)
  return `Присылать статус заказа в MAX/WhatsApp на номер ${mask || 'из этого заказа'}`
}
