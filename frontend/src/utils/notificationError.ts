import { AxiosError } from 'axios'

/**
 * Maps a failed notification-channel/settings/template request to a Russian message. The contract
 * (API_CONTRACT_CYCLE4.md §19.2) returns a bare string body for 400/402/409/429 — those are shown
 * verbatim since the server already writes the human text; 403/404 have empty bodies and need a
 * generic fallback here.
 */
export function getNotificationErrorMessage(error: unknown, fallback = 'Не удалось выполнить действие. Попробуйте снова.'): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const data = ax?.response?.data
  const serverMsg = typeof data === 'string' && data.trim().length > 0 ? data : ''

  switch (status) {
    case 400:
    case 402:
    case 409:
      return serverMsg || fallback
    case 429:
      return serverMsg || 'Слишком много попыток. Повторите позже.'
    case 403:
      return 'Недостаточно прав для этого действия.'
    case 404:
      return 'Не найдено — возможно, канал уже удалён или недоступен.'
    default:
      // Client-side validation errors (e.g. thrown before the request is even sent) have no
      // `response` — surface `error.message` instead of the generic fallback so the operator
      // knows which field to fix.
      if (status === undefined && error instanceof Error && error.message.trim().length > 0) {
        return error.message
      }
      return fallback
  }
}
