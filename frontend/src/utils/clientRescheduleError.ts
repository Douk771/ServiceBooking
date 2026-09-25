import { AxiosError } from 'axios'

/**
 * ARCHITECTURE_CYCLE15.md §287.2 — `PATCH /bookings/{id}/reschedule` for a client rescheduling their
 * OWN booking. Unlike the staff path, every 400 here already comes back as the exact sentence to
 * show the user (§283's convention: "server composes, front only shows"), with the actual hour/day
 * limit substituted server-side — so 400 is shown verbatim. 402/403/409 answer with a fixed
 * English/empty body (existing product convention predating this cycle), which this maps to Russian.
 *
 * 404 covers both "no such booking" and "not yours" (§287.3 — posторонний gets 404, not 403, so the
 * endpoint doesn't confirm which booking ids exist to someone who doesn't own them).
 */
export function getClientRescheduleErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const serverMsg = typeof ax?.response?.data === 'string' ? ax.response.data.trim() : ''

  switch (status) {
    case 400:
      return serverMsg || 'Перенести запись не получилось — проверьте выбранное время.'
    case 402:
      return 'Онлайн-запись сейчас недоступна: у салона нет активной подписки, разрешающей это.'
    case 403:
      return 'Салон сейчас не принимает переносы записей онлайн.'
    case 404:
      return 'Запись не найдена — возможно, её уже изменили.'
    case 409:
      return 'Это время уже занято. Выберите другое.'
    case 429:
      return serverMsg || 'Слишком много попыток. Повторите позже.'
    default:
      return 'Не удалось перенести запись. Попробуйте снова.'
  }
}
