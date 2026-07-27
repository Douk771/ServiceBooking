import { AxiosError } from 'axios'

/**
 * Maps a failed POST /companies/{id}/members request to a clear, actionable Russian message.
 *
 * A bare "user already a member or invalid data" message hides the real cause — most notably the
 * tariff seat limit (HTTP 402), where the fix is "upgrade the plan", not "check the data again".
 */
export function getAddMemberErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const data = ax?.response?.data
  const serverMsg = typeof data === 'string' ? data : ''

  switch (status) {
    case 402:
      return 'Достигнут лимит сотрудников по текущему тарифу. Чтобы добавить ещё одного, повысьте тариф.'
    case 409:
      return 'Этот пользователь уже является сотрудником компании.'
    case 403:
      return 'Недостаточно прав, чтобы назначить эту роль.'
    case 400: {
      if (Array.isArray(data) && data.length > 0) {
        const first = data[0]
        const desc = typeof first === 'string' ? first : first?.description
        if (desc) return desc
      }
      return serverMsg || 'Не удалось создать пользователя. Проверьте email и другие данные.'
    }
    default:
      return 'Не удалось добавить сотрудника. Попробуйте снова.'
  }
}
