import { AxiosError } from 'axios'

/** Maps a failed `POST /api/subject-requests` to a Russian message (API_CONTRACT_CYCLE5.md §48.1). */
export function getSubjectRequestErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''

  switch (status) {
    case 429:
      return body || 'Слишком много обращений с этого адреса. Попробуйте позже.'
    case 400:
      return body || 'Проверьте, что все обязательные поля заполнены верно.'
    default:
      return 'Не удалось отправить обращение. Попробуйте снова.'
  }
}

/** Maps a failed `POST /api/admin/subject-requests/{id}/status` to a Russian message
 *  (API_CONTRACT_CYCLE5.md §48.3 — 400 when moving to Answered/Rejected without `resolution`). */
export function getSubjectRequestAdminErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''

  switch (status) {
    case 400:
      return body || 'Укажите результат обработки обращения.'
    case 403:
      return 'Недостаточно прав для этого действия.'
    case 404:
      return 'Обращение не найдено — возможно, обновите список.'
    default:
      return 'Не удалось сохранить. Попробуйте снова.'
  }
}
