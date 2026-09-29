import { AxiosError } from 'axios'

/** `POST /api/legal/notices/{id}/acknowledge` (§434.2) — 404 (not the addressee/expired/gone) and 409
 *  (revoked) both mean "this notice can no longer be acknowledged"; the caller's only useful reaction
 *  is to drop it from view and refetch, so both collapse to the same outcome rather than two messages
 *  for a state the user can't act on either way. */
export function getNoticeAcknowledgeErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  if (status === 404) return 'Уведомление больше не актуально.'
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''
  if (status === 409) return body || 'Уведомление отозвано.'
  return 'Не удалось отметить как прочитанное. Попробуйте снова.'
}

/** `POST /api/admin/notices` / `.../preview` (§434.5) — every validation failure in the publish matrix
 *  is a bare string, composed server-side; shown verbatim so the specific rule that failed (which
 *  field, which day count) reaches the SuperAdmin instead of a generic "invalid" banner. */
export function getAdminNoticeErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''
  return body || 'Не удалось выполнить действие. Проверьте форму и попробуйте снова.'
}

/** `POST /api/admin/notices/{id}/revoke` — 409 (already revoked) is worth telling apart only to avoid
 *  suggesting a retry; both cases still show the server's own text when present. */
export function getAdminNoticeRevokeErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const body = typeof ax?.response?.data === 'string' ? ax.response.data : ''
  return body || 'Не удалось отозвать уведомление. Попробуйте снова.'
}
