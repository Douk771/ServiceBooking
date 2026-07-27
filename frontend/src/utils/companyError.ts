import { AxiosError } from 'axios'

/**
 * Maps a failed POST /companies request to a clear, actionable Russian message.
 *
 * A bare "possibly the slug is taken" message hides the real cause — most notably the tariff branch
 * limit (HTTP 402), where the fix is "upgrade the plan", not "pick a different slug".
 */
export function getCreateCompanyErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const data = ax?.response?.data
  const serverMsg = typeof data === 'string' ? data : ''

  switch (status) {
    case 402:
      return 'Достигнут лимит компаний по текущему тарифу. Чтобы создать ещё одну, повысьте тариф.'
    case 409:
      return 'Такой slug уже занят. Выберите другой.'
    case 400:
      return serverMsg || 'Проверьте введённые данные.'
    default:
      return 'Не удалось создать компанию. Попробуйте снова.'
  }
}
