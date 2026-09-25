import { AxiosError } from 'axios'
import type { TrialRefusalDto } from '../api/billing'

/**
 * Maps a failed trial-activation call (`POST /billing/trial`, `.../terms-acknowledgement`, and the
 * admin grant/regrant routes) to a Russian message — API_CONTRACT_CYCLE18.md §371 п.5, the 14th
 * mapper in this codebase's convention of "one mapper per screen's error shapes" (planError.ts,
 * billingError.ts, adminBillingError.ts, phoneVerificationError.ts, …).
 *
 * Unlike every other mapper here, the 409 body on this route is JSON `{ code, message }`
 * (`TrialRefusalDto`) rather than a bare string — the contract's ONE deliberate exception to
 * "4xx is plain text" (§360.1), because US-18-06 needs both a machine code to branch on and a
 * ready-made Russian sentence to print verbatim. This mapper never invents copy: for a 409 it
 * always returns the server's own `message`, never a hardcoded string — the text carries legal
 * weight (`LEGAL_REVIEW_CYCLE18.md` §4) and this file is not allowed to paraphrase it.
 */
export function getTrialErrorMessage(error: unknown, fallback = 'Не удалось выполнить действие. Попробуйте снова.'): string {
  const ax = error as AxiosError
  const status = ax?.response?.status

  switch (status) {
    case 409: {
      const data = ax.response?.data as TrialRefusalDto | undefined
      return data?.message || fallback
    }
    case 429: {
      const data = ax.response?.data
      return (typeof data === 'string' && data.trim()) || 'Слишком много попыток. Попробуйте завтра.'
    }
    case 400: {
      const data = ax.response?.data
      return (typeof data === 'string' && data.trim()) || fallback
    }
    case 404:
      return 'Пробный период недоступен — у вас нет биллинг-аккаунта.'
    case 401:
      return 'Сессия входа истекла. Обновите страницу и попробуйте снова.'
    default:
      return fallback
  }
}

/** True for the one 409 that means "the terms text changed under you" (§363: activation went
 *  through against a stale `termsVersion`). §371 п.5: this case is NOT a plain retry — the caller
 *  must re-fetch `GET /billing/trial` and show the refreshed terms before the owner can try again,
 *  never resubmit the same (now stale) version. Told apart by `code`, never by matching text. */
export function isTrialTermsVersionMismatch(error: unknown): boolean {
  const ax = error as AxiosError
  if (ax?.response?.status !== 409) return false
  const data = ax.response?.data as TrialRefusalDto | undefined
  return data?.code === 'TrialTermsVersionMismatch'
}

/** Machine-readable refusal code, when present — lets callers branch (e.g. deciding whether a
 *  "выдать повторно" admin action makes sense) without parsing `message`. */
export function getTrialRefusalCode(error: unknown): TrialRefusalDto['code'] | null {
  const ax = error as AxiosError
  if (ax?.response?.status !== 409) return null
  const data = ax.response?.data as TrialRefusalDto | undefined
  return data?.code ?? null
}
