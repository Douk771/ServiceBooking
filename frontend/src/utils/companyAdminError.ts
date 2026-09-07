import { AxiosError } from 'axios'

/**
 * Maps a failed `PUT /admin/companies/{id}` (block/unblock from the admin panel) to a Russian message.
 *
 * The body isn't shown to the user even when present (e.g. "Photo quota must not be negative." would
 * never apply here, but the pattern is kept consistent with the other mappers): a bare status-based
 * generic message is safer than passing through whatever English text the server happens to send for
 * this endpoint (same pattern as uploadError.ts / cancelError.ts).
 */
export function getCompanyAdminErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status

  switch (status) {
    case 404:
      return 'Компания не найдена — возможно, её уже удалили.'
    case 403:
      return 'Недостаточно прав для этого действия.'
    case 400:
      return 'Проверьте введённые данные и попробуйте снова.'
    default:
      return 'Не удалось сохранить изменения. Попробуйте снова.'
  }
}
