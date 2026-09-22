import { AxiosError } from 'axios'

/**
 * Identity error codes (400 on register, API_CONTRACT_CYCLE6.md §39.6) translated into human Russian.
 * `DuplicateUserName` gets special wording — "this phone is already registered" is a materially
 * different, more actionable message than "check your data".
 */
const IDENTITY_ERROR_MESSAGES: Record<string, string> = {
  PasswordTooShort: 'Пароль должен быть не короче 8 символов',
  PasswordRequiresLower: 'Пароль должен содержать хотя бы одну строчную букву',
  PasswordRequiresUpper: 'Пароль должен содержать хотя бы одну заглавную букву',
  PasswordRequiresDigit: 'Пароль должен содержать хотя бы одну цифру',
  PasswordRequiresUniqueChars: 'Пароль содержит слишком много повторяющихся символов',
  DuplicateUserName: 'Этот телефон уже зарегистрирован',
}

interface IdentityError {
  code?: string
  description?: string
}

function isIdentityErrorArray(data: unknown): data is IdentityError[] {
  return Array.isArray(data) && data.every((item) => item && typeof item === 'object')
}

/**
 * Last-resort reader for a body that is an object rather than a string.
 *
 * The server now returns a bare string for every deliberate 4xx, including the automatic
 * model-validation 400 that used to be `application/problem+json` (cycle 6 contract finding,
 * API_CONTRACT_CYCLE6.md §38.1). This function is the belt to that fix's braces: it exists so that
 * an endpoint which was never reconfigured — or a future one added without the convention in mind —
 * degrades to the server's own wording instead of vanishing into "check your data".
 *
 * That silent vanishing is not hypothetical: it is exactly what hid the real cause of US-60, where
 * a rejected registration reported nothing a user could act on.
 */
function readObjectBody(data: unknown): string {
  if (!data || typeof data !== 'object') return ''
  const problem = data as { title?: unknown; detail?: unknown; errors?: unknown }

  // ProblemDetails / ValidationProblemDetails: prefer the per-field messages, then detail, then title.
  if (problem.errors && typeof problem.errors === 'object') {
    const messages = Object.values(problem.errors as Record<string, unknown>)
      .flatMap((v) => (Array.isArray(v) ? v : [v]))
      .filter((v): v is string => typeof v === 'string' && v.trim() !== '')
    if (messages.length > 0) return messages.join(' ')
  }
  if (typeof problem.detail === 'string' && problem.detail.trim() !== '') return problem.detail
  if (typeof problem.title === 'string' && problem.title.trim() !== '') return problem.title
  return ''
}

/** Translates one array of Identity errors (§39.6) into the human Russian text(s) shown to the user. */
export function formatIdentityErrors(errors: IdentityError[]): string {
  return errors
    .map((e) => (e.code && IDENTITY_ERROR_MESSAGES[e.code]) || e.description || 'Проверьте введённые данные.')
    .join(' ')
}

/**
 * Maps a failed login/register request to a clear, actionable Russian message.
 *
 * Bodies are plain strings for every 4xx here (API_CONTRACT.md §0.2), except 400 on register, which
 * can also be a JSON **array** of Identity errors (API_CONTRACT_CYCLE6.md §39.6) — e.g. a weak
 * password or a duplicate phone. That array used to be silently dropped (`typeof data === 'string'`
 * turned it into an empty string), so the user only ever saw a generic "check your data" and never
 * learned the account wasn't even created (US-60).
 *
 * 401/423/403/5xx/"no response" branches are US-60 (cycle 6, ARCHITECTURE_CYCLE6.md §42.3.3): three
 * outcomes that used to collapse into one "wrong credentials" string on the login screen are now
 * distinguishable by status alone (§42.4 has the exact wording), without a JSON error envelope.
 */
export function getAuthErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const data = ax?.response?.data
  const body = typeof data === 'string' ? data : readObjectBody(data)

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
      if (isIdentityErrorArray(data)) return formatIdentityErrors(data)
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
