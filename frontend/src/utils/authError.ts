import { AxiosError } from 'axios'

/**
 * Maps a failed login/register request to a clear, actionable Russian message.
 *
 * Bodies are plain strings for every 4xx here (API_CONTRACT.md §0.2), except 400 on register, which
 * can also be a JSON array of Identity errors (API_CONTRACT.md §5.4) — that shape is left to the
 * caller's own field-level handling, this mapper only covers the codes that aren't already shown next
 * to a specific form field: the two rate-limiting policies introduced in this cycle (US-42) and the
 * new consent gate (US-37).
 *
 * 401/423/403/5xx/"no response" branches are US-60 (cycle 6, ARCHITECTURE_CYCLE6.md §42.3.3): three
 * outcomes that used to collapse into one "wrong credentials" string on the login screen are now
 * distinguishable by status alone (§42.4 has the exact wording), without a JSON error envelope.
 */
export function getAuthErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''

  switch (status) {
    case 401:
      // Outcomes И1 (account not found) and И2 (wrong password) stay byte-identical on purpose —
      // telling them apart would let an attacker enumerate registered phone numbers (§42.3, НФТ §3).
      return 'Неверный телефон или пароль'
    case 423:
      return 'Вход временно заблокирован из-за нескольких неудачных попыток. Попробуйте через 15 минут'
    case 403:
      return 'Вход в этот аккаунт недоступен. Обратитесь в поддержку'
    case 429:
      // Same wording the server sends for both policies (API_CONTRACT.md §6, §5.4) — matched by
      // status alone since the body is fixed text per endpoint, not a substring to branch on.
      return body || 'Слишком много попыток входа, попробуйте через несколько минут'
    case 400:
      if (body.includes('Consent to the Terms of Service'))
        return 'Необходимо принять условия использования и политику обработки персональных данных.'
      return body || 'Проверьте введённые данные.'
    default:
      if (status !== undefined && status >= 500)
        return 'Сервис временно недоступен. Попробуйте ещё раз через минуту'
      if (status === undefined)
        // Network error / CORS / no response at all — axios leaves `response` undefined, distinct
        // from a 5xx (§42.4: "different places break").
        return 'Не удалось связаться с сервером. Проверьте интернет и попробуйте ещё раз'
      return body || 'Произошла ошибка. Попробуйте снова.'
  }
}
