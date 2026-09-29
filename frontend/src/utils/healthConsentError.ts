import { AxiosError } from 'axios'

/**
 * API_CONTRACT_CYCLE20.md §432.2 (US-20-01) — `PUT …/health-note` still has the one 400 with a JSON
 * body that all the OTHER "4xx = bare string" routes in this project don't (§430, an existing
 * exception from cycle 5, `RequiredConsentDto`). Distinguishing "no consent yet" from "value invalid"
 * by Content-Type (not by parsing the string) is the contract's own instruction (§432.2: "Фронт
 * различает два 400 по Content-Type").
 */
export function isRequiredWrittenConsent(error: unknown): boolean {
  const ax = error as AxiosError
  const data = ax?.response?.data
  return ax?.response?.status === 400 && !!data && typeof data === 'object' && 'requiredTextKey' in data
}

/** Maps the bare-string 400 (plain validation failure) on `PUT …/health-note` — used only once
 *  {@link isRequiredWrittenConsent} has already ruled out the JSON-body case above. */
export function getHealthNoteSaveErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''
  return body || 'Не удалось сохранить. Попробуйте снова.'
}

/**
 * `POST …/health-written-consent` (§432.5) — the one case worth telling apart from a generic failure
 * is 409 (form text was republished between printing and marking): the caller must reprint, not just
 * retry the same call. `formVersion`/`confirmed` 400s are shown with the server's own bare string.
 */
export function getMarkWrittenConsentErrorMessage(error: unknown): { message: string; outdatedForm: boolean } {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''

  if (status === 409) {
    return {
      message: body || 'Текст бланка обновлён — распечатайте бланк заново и отметьте получение по новой редакции.',
      outdatedForm: true,
    }
  }
  return { message: body || 'Не удалось записать отметку. Попробуйте снова.', outdatedForm: false }
}

/** `POST …/health-written-consent/revoke` (§432.6) — always a bare string, same convention as most
 *  4xx in this project; kept as its own function only so call sites don't need to know that. */
export function getRevokeWrittenConsentErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''
  return body || 'Не удалось снять отметку. Попробуйте снова.'
}

/** `GET|PUT /api/billing/operator-details` (§432.8) — `PUT` 400 is a bare string (validation of ФИО
 *  без инициалов / ИНН). */
export function getOperatorDetailsErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''
  return body || 'Не удалось сохранить реквизиты. Попробуйте снова.'
}
