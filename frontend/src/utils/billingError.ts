import { AxiosError } from 'axios'

/** Maps failures from the owner "Ваша подписка" screen (US-65, US-70) to Russian messages. */
export function getBillingErrorMessage(error: unknown, fallback: string): string {
  const ax = error as AxiosError
  switch (ax?.response?.status) {
    case 400:
      return 'Проверьте выбранный состав — сервер его не принял.'
    case 403:
      return 'Недостаточно прав для этого действия.'
    case 404:
      return 'Заявка не найдена — возможно, её уже обработали.'
    case 409:
      return 'У подписки уже есть необработанная заявка. Обновите страницу.'
    default:
      return fallback
  }
}
