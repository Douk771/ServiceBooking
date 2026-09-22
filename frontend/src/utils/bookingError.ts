import { AxiosError } from 'axios'

/**
 * Maps a failed booking request to a clear, actionable Russian message.
 *
 * The booking endpoint can fail for reasons a bare "try again" hides — most notably the
 * Free-plan guest gate (HTTP 402), where retrying never helps. We surface the real cause so
 * the user knows what to do next (e.g. log in, pick another slot).
 */
export function getBookingErrorMessage(error: unknown): string {
  const ax = error as AxiosError
  const status = ax?.response?.status
  const serverMsg = typeof ax?.response?.data === 'string' ? ax.response.data : ''
  const lower = serverMsg.toLowerCase()

  switch (status) {
    case 402:
      // API_CONTRACT_CYCLE6.md §42.3 — tariff doesn't include online booking.
      if (lower.includes('expired')) return 'Подписка компании истекла — запись временно недоступна.'
      return 'Онлайн-запись недоступна: тариф компании её не включает'
    case 403:
      // §42.3 — `Company.AllowSelfBooking == false`.
      return 'Салон сейчас не принимает онлайн-записи'
    case 409:
      return 'Это время уже занято. Выберите другой слот.'
    case 404:
      return 'Услуга или компания не найдена.'
    case 429:
      // US-42 `booking-create` policy — same fixed text regardless of guest/staff (API_CONTRACT.md §7.3).
      return serverMsg || 'Слишком много записей с этого адреса. Повторите позже.'
    case 400:
      if (lower.includes('captcha')) return 'Не удалось пройти проверку. Обновите страницу и попробуйте снова.'
      if (lower.includes('name') || lower.includes('phone')) return 'Укажите имя и телефон.'
      return serverMsg || 'Проверьте введённые данные.'
    default:
      return 'Произошла ошибка. Попробуйте снова.'
  }
}
