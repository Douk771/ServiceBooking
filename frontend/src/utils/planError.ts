import { AxiosError } from 'axios'

/**
 * Maps a failed plan-management request (deactivate a plan / edit a plan / assign a subscription) to
 * a clear, actionable Russian message.
 *
 * A bare "try again" hides the real cause — most notably HTTP 409, which means the plan still has
 * active subscribers and retrying never helps until they're moved to another plan.
 *
 * `fallback` names the action for the cases where the server says nothing useful, because the same
 * mapper serves three different buttons: deactivating a plan, saving a plan, and assigning a
 * subscription. Without it every one of them reported "не удалось деактивировать тариф".
 */
export function getPlanErrorMessage(error: unknown, fallback = 'Не удалось деактивировать тариф.'): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const data = ax?.response?.data
  const serverMsg = typeof data === 'string' ? data : ''

  switch (status) {
    case 409: {
      // The count is the only variable part of the server text (API_CONTRACT.md §15.2); anchoring on
      // "active" keeps this from picking up an unrelated number if another 409 ever appears here.
      const match = serverMsg.match(/(\d+)\s+active/)
      return match
        ? `На этом тарифе есть активные подписчики (${match[1]}). Сначала переведите их на другой тариф.`
        : 'На этом тарифе есть активные подписчики. Сначала переведите их на другой тариф.'
    }
    case 404:
      // Two distinct 404s exist here — "Owner not found" and "Plan not found" — and telling the admin
      // the wrong one sends them looking in the wrong place, so prefer the server's own wording.
      return serverMsg || 'Тариф не найден.'
    case 400:
      return serverMsg || fallback
    case 403:
      return 'Недостаточно прав для этого действия.'
    default:
      return fallback
  }
}
