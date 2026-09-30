import { AxiosError } from 'axios'
import { getDemoRestrictedMessage } from './demoHeaders'

const LINE_TEXT = /линейк|«Записи»|«Заказы»/

/** Maps failures from the owner "Ваша подписка" screen (US-65, US-70) to Russian messages. */
export function getBillingErrorMessage(error: unknown, fallback: string): string {
  const ax = error as AxiosError
  // Cycle 28 (§599): a demo role asked for a plan change — the server's own refusal text, recognised by its header.
  const demoRestricted = getDemoRestrictedMessage(error)
  if (demoRestricted) return demoRestricted
  // Cycle 24 (API_CONTRACT_CYCLE24.md §485.1, §489): «Этот тариф из другой линейки» (400) and «У вас уже есть заявка на смену
  // тарифа «Записи»/«Заказы» — …» (409) are composed by the server and printed as is. Every other 400/409 keeps the fixed text.
  const body = ax?.response?.data
  if ((ax?.response?.status === 400 || ax?.response?.status === 409) && typeof body === 'string' && LINE_TEXT.test(body)) return body.trim()
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
