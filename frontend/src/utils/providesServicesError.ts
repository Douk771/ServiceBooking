import { AxiosError } from 'axios'

/**
 * Result of a failed `PUT /companies/{id}/members/{memberId}/provides-services` call
 * (API_CONTRACT_CYCLE6.md §40.3).
 *
 * `needsConfirmation` distinguishes the one case that isn't a plain error: turning the flag OFF for
 * someone with future bookings comes back as 409 with a human-readable warning ("У специалиста 3
 * будущие записи…") that the caller should show with a "confirm anyway" action, retrying the same
 * request with `confirm: true` — the already-created bookings are never affected either way.
 */
export interface ProvidesServicesErrorResult {
  message: string
  needsConfirmation: boolean
}

export function getProvidesServicesErrorMessage(error: unknown): ProvidesServicesErrorResult {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const data = ax?.response?.data
  const serverMsg = typeof data === 'string' ? data : ''

  if (status === 409) {
    return {
      message: serverMsg || 'У специалиста есть будущие записи. Повторите с подтверждением.',
      needsConfirmation: true,
    }
  }
  if (status === 403) {
    return { message: 'Недостаточно прав, чтобы изменить этот признак.', needsConfirmation: false }
  }
  if (status === 404) {
    return { message: 'Сотрудник не найден.', needsConfirmation: false }
  }
  return { message: serverMsg || 'Не удалось сохранить изменение. Попробуйте снова.', needsConfirmation: false }
}
