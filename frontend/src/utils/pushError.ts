import { AxiosError } from 'axios'

/**
 * Maps a failed /push/* or /staff-push-settings request to a Russian message
 * (API_CONTRACT_CYCLE9.md §116). 400/409/429 carry a bare-string body written by the server; 403/404
 * are empty and need a generic fallback here — 402 never appears on push routes.
 */
export function getPushErrorMessage(error: unknown, fallback = 'Не удалось выполнить действие. Попробуйте снова.'): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const data = ax?.response?.data
  const serverMsg = typeof data === 'string' && data.trim().length > 0 ? data : ''

  switch (status) {
    case 400:
      return serverMsg || fallback
    case 409:
      // §115.3 — subsystem disabled on the platform (GET /push/config → enabled:false).
      return serverMsg || 'Уведомления на устройство сейчас недоступны.'
    case 429:
      return serverMsg || 'Слишком много попыток. Повторите позже.'
    case 403:
      return 'Недостаточно прав для этого действия.'
    case 404:
      return 'Устройство уже отключено или не найдено.'
    default:
      return fallback
  }
}
