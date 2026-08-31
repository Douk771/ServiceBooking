import { AxiosError } from 'axios'

/**
 * Maps a failed plan-management request (deactivate a plan / assign a subscription) to a clear,
 * actionable Russian message.
 *
 * A bare "try again" hides the real cause — most notably HTTP 409 on deactivation, which means the
 * plan still has active subscribers and retrying never helps until they're moved to another plan.
 */
export function getPlanErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const data = ax?.response?.data
  const serverMsg = typeof data === 'string' ? data : ''

  switch (status) {
    case 409: {
      const match = serverMsg.match(/(\d+)/)
      return match
        ? `На этом тарифе есть активные подписчики (${match[1]}). Сначала переведите их на другой тариф.`
        : 'На этом тарифе есть активные подписчики. Сначала переведите их на другой тариф.'
    }
    case 404:
      return 'Тариф не найден'
    case 400:
      return serverMsg || 'Не удалось сохранить тариф'
    default:
      return 'Не удалось деактивировать тариф'
  }
}
