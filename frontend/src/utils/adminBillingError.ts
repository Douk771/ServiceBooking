import { AxiosError } from 'axios'

/**
 * Maps a failed admin billing-account request (assign subscription / reject request) to a Russian
 * message. PUT .../subscription returns a bare-string 409 for three distinct cases — option not
 * available on the chosen plan, request already processed, or new limits below what's already in
 * use without confirmLimitOverflow (API_CONTRACT_CYCLE7.md §49) — the server already composes the
 * specific wording (which number overflowed, which option, etc.), so it's shown verbatim rather
 * than collapsed into a generic "error" banner.
 *
 * Deliberately separate from `utils/billingError.ts` (the owner-facing "Ваша подписка" screen,
 * US-65/US-70) — that mapper hardcodes different, owner-oriented copy for the same status codes,
 * and merging the two would either change the owner's wording or hide the admin's server text.
 */
export function getAdminBillingErrorMessage(error: unknown, fallback = 'Не удалось выполнить действие. Попробуйте снова.'): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const data = ax?.response?.data
  const serverMsg = typeof data === 'string' && data.trim().length > 0 ? data : ''

  switch (status) {
    case 400:
    case 409:
      return serverMsg || fallback
    case 403:
      return 'Недостаточно прав для этого действия.'
    case 404:
      return 'Не найдено — возможно, аккаунт или заявка уже удалены.'
    default:
      return fallback
  }
}

/** True for the specific 409 case that requires re-submitting with confirmLimitOverflow — the
 *  limit-overflow case is the only one of the three §49 409s that has a retry path in the UI, so
 *  it needs to be told apart from "option unavailable" / "request already processed" (neither of
 *  which a checkbox can fix). The server's overflow wording always mentions the limit noun. */
export function isLimitOverflowConflict(error: unknown): boolean {
  const ax = error as AxiosError
  if (ax?.response?.status !== 409) return false
  const data = ax.response?.data
  const text = typeof data === 'string' ? data : ''
  return /лимит|мест|компани|сотрудник/i.test(text)
}
