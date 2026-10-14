import type { AxiosError } from 'axios'
import { getStayErrorMessage } from '@/utils/slots/slotError'
import type { TrialOutcomeDto } from './types'

/**
 * A refused trial is a 409 whose body is the SAME shape as a granted one (`TrialOutcomeDto`, `granted: false`, API_CONTRACT_CYCLE42.md §42.27.4):
 * it has `message` but no `code`, so the shared conflict reader does not see it. The text is the server's.
 */
export function readTrialRefusal(error: unknown): TrialOutcomeDto | null {
  const ax = error as AxiosError | undefined
  if (ax?.response?.status !== 409) return null
  const data = ax.response.data as unknown
  if (data && typeof data === 'object' && (data as { granted?: unknown }).granted === false) return data as TrialOutcomeDto
  return null
}

export function trialErrorMessage(error: unknown): string {
  const refusal = readTrialRefusal(error)
  if (refusal) return refusal.message ?? 'Пробный период недоступен.'
  return getStayErrorMessage(error, 'Не удалось начать пробный период.')
}

/** The terms changed under the owner's eyes: re-read them instead of resending a stale version. */
export function isTrialTermsMismatch(error: unknown): boolean {
  return readTrialRefusal(error)?.refusalCode === 'TrialTermsVersionMismatch'
}
