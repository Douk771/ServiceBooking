import { AxiosError } from 'axios'
import type { PhoneVerificationFailureReason } from '../types'

/**
 * Maps a failed `/phone-verification/sessions` call (start/cancel) to a Russian message.
 * API_CONTRACT_CYCLE14.md §163: 400/409/429 carry a bare-string body written by the server; 401 is
 * empty (an invalid token on an otherwise-anonymous-capable call, §163).
 */
export function getPhoneVerificationErrorMessage(
  error: unknown,
  fallback = 'Не удалось начать подтверждение номера. Попробуйте снова.',
): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const data = ax?.response?.data
  const serverMsg = typeof data === 'string' && data.trim() !== '' ? data : ''

  switch (status) {
    case 400:
      return serverMsg || 'Введите номер телефона в формате +7 (900) 000-00-00'
    case 409:
      // Subsystem disabled or the webhook isn't currently subscribed (§163) — same wording either way,
      // the caller isn't meant to tell those two apart.
      return serverMsg || 'Подтверждение телефона сейчас недоступно.'
    case 429:
      return serverMsg || 'Слишком много попыток подтверждения. Попробуйте позже.'
    case 401:
      return 'Сессия входа истекла. Обновите страницу и попробуйте снова.'
    default:
      return fallback
  }
}

/**
 * Fallback Russian text per `failureReason` (API_CONTRACT_CYCLE14.md §165), used ONLY when a session
 * somehow carries a `failureReason` without a server-composed `message` — the server is expected to
 * always fill `message` (§164: "фронт её не сочиняет и не собирает из кусочков"), so this table exists
 * purely as a defensive fallback, not as the primary source of copy.
 */
const FAILURE_REASON_FALLBACK: Record<PhoneVerificationFailureReason, string> = {
  PayloadUnknown: 'Ссылка устарела. Получите новую',
  PayloadExpired: 'Ссылка устарела. Получите новую',
  PayloadAlreadyUsed: 'Ссылка устарела. Получите новую',
  PayloadLinkedToAnotherAccount: 'Эта ссылка уже используется в другом аккаунте MAX',
  SignatureMismatch: 'Не удалось проверить контакт. Попробуйте ещё раз',
  ContactNotOwnedBySender: 'Поделиться можно только собственным контактом',
  NoPhoneInContact: 'В присланном контакте нет номера телефона',
  PhoneMismatch: 'Номер в контакте не совпадает с введённым на сайте',
  MaxAccountLimitReached: 'С этого аккаунта MAX уже подтверждены три номера',
  SessionCancelled: '',
  SubsystemDisabled: 'Подтверждение телефона сейчас недоступно',
}

export function getFailureReasonFallback(reason: PhoneVerificationFailureReason | null | undefined): string {
  if (!reason) return ''
  return FAILURE_REASON_FALLBACK[reason] ?? ''
}

/** §165 — reasons after which offering "get a new link" (start over) makes sense. */
const RETRYABLE_REASONS: ReadonlySet<PhoneVerificationFailureReason> = new Set([
  'PayloadUnknown',
  'PayloadExpired',
  'PayloadAlreadyUsed',
  'PayloadLinkedToAnotherAccount',
  'SignatureMismatch',
  'ContactNotOwnedBySender',
  'NoPhoneInContact',
  'PhoneMismatch',
])

export function isRetryableFailure(reason: PhoneVerificationFailureReason | null | undefined): boolean {
  return !!reason && RETRYABLE_REASONS.has(reason)
}

// ── POST /api/profile/change-phone — US-14-17 gate (§169) ─────────────────────────────────────────
// The two new 409 wordings are told apart by substring, the same convention `utils/authError.ts`
// already uses for the two meanings of 409 on /auth/register (ARCHITECTURE_CYCLE14.md §148.5).

const VERIFICATION_REQUIRED_MARK = 'Подтвердите его через MAX'
const SUBSYSTEM_DISABLED_MARK = 'подтверждение номера на платформе пока не работает'

function changePhone409Text(error: unknown): string | null {
  const ax = error as AxiosError
  if (ax?.response?.status !== 409) return null
  const data = ax.response?.data
  return typeof data === 'string' ? data : null
}

/** The new number has guest bookings on it and needs a verified session before the change can go through. */
export function isPhoneChangeVerificationRequired(error: unknown): boolean {
  return changePhone409Text(error)?.includes(VERIFICATION_REQUIRED_MARK) ?? false
}

/** Same gate, but the subsystem is switched off — there is no way to satisfy it right now (honest 409). */
export function isPhoneChangeVerificationUnavailable(error: unknown): boolean {
  return changePhone409Text(error)?.includes(SUBSYSTEM_DISABLED_MARK) ?? false
}
