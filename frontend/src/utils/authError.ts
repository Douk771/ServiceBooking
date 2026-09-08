import { AxiosError } from 'axios'

/**
 * Maps a failed login/register request to a clear, actionable Russian message.
 *
 * Bodies are plain strings for every 4xx here (API_CONTRACT.md §0.2), except 400 on register, which
 * can also be a JSON array of Identity errors (API_CONTRACT.md §5.4) — that shape is left to the
 * caller's own field-level handling, this mapper only covers the codes that aren't already shown next
 * to a specific form field: the two rate-limiting policies introduced in this cycle (US-42) and the
 * new consent gate (US-37).
 */
export function getAuthErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''

  switch (status) {
    case 429:
      // Same wording the server sends for both policies (API_CONTRACT.md §6, §5.4) — matched by
      // status alone since the body is fixed text per endpoint, not a substring to branch on.
      return body || 'Слишком много попыток. Повторите позже.'
    case 400:
      if (body.includes('Consent to the Terms of Service'))
        return 'Необходимо принять условия использования и политику обработки персональных данных.'
      return body || 'Проверьте введённые данные.'
    default:
      return body || 'Произошла ошибка. Попробуйте снова.'
  }
}
