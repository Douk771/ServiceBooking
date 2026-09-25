import { AxiosError } from 'axios'

/**
 * Maps a failed `PATCH /bookings/{id}/cancel` (with an optional reason) to a clear Russian message.
 *
 * Kept separate from `bookingError.ts` on purpose (API_CONTRACT.md §20 checklist item 2): that mapper
 * is for the booking-creation flow and its diff must stay empty this cycle so its existing branches
 * aren't disturbed by an unrelated feature.
 */
export function getCancelErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const serverMsg = typeof ax?.response?.data === 'string' ? ax.response.data : ''

  switch (status) {
    case 400:
      if (serverMsg.toLowerCase().includes('300')) return 'Причина не может быть длиннее 300 символов.'
      // Any other 400 isn't in the mapped set — generic Russian message rather than the server's
      // raw (often English) text (same pattern as uploadError.ts).
      return 'Не удалось отменить запись. Проверьте данные и попробуйте снова.'
    case 403:
      return 'Недостаточно прав для отмены этой записи.'
    case 404:
      return 'Запись не найдена — возможно, её уже отменили.'
    // ARCHITECTURE_CYCLE17.md §304.2 / API_CONTRACT_CYCLE17.md §322.3: 409 = "too late to cancel"
    // (client-owner inside the company's window). Server composes the sentence with the actual
    // hour count, so it's shown verbatim rather than re-derived on the front (§320 convention).
    case 409:
      return serverMsg || 'Отменить запись уже нельзя — свяжитесь с салоном.'
    default:
      return 'Не удалось отменить запись. Попробуйте снова.'
  }
}
