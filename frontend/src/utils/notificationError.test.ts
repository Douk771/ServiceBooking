import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getNotificationErrorMessage } from './notificationError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getNotificationErrorMessage', () => {
  it('400 → server text verbatim', () => {
    expect(getNotificationErrorMessage(axiosErrorWith(400, 'Напоминание за 1 час при пороге 2 часа не уйдёт никогда'))).toBe(
      'Напоминание за 1 час при пороге 2 часа не уйдёт никогда',
    )
  })

  it('402 → server text verbatim (tariff or channel unpaid)', () => {
    expect(getNotificationErrorMessage(axiosErrorWith(402, 'Канал не оплачен'))).toBe('Канал не оплачен')
  })

  it('409 → server text verbatim (state conflict)', () => {
    expect(getNotificationErrorMessage(axiosErrorWith(409, 'Номер уже подключается'))).toBe('Номер уже подключается')
  })

  it('429 with server text → server text', () => {
    expect(getNotificationErrorMessage(axiosErrorWith(429, 'Проверять канал можно не чаще одного раза в 5 минут'))).toBe(
      'Проверять канал можно не чаще одного раза в 5 минут',
    )
  })

  it('429 with empty body → generic rate-limit fallback', () => {
    expect(getNotificationErrorMessage(axiosErrorWith(429, undefined))).toBe('Слишком много попыток. Повторите позже.')
  })

  it('403 → permissions fallback regardless of body (empty per contract)', () => {
    expect(getNotificationErrorMessage(axiosErrorWith(403, undefined))).toBe('Недостаточно прав для этого действия.')
  })

  it('404 → not-found fallback', () => {
    expect(getNotificationErrorMessage(axiosErrorWith(404, undefined))).toBe(
      'Не найдено — возможно, канал уже удалён или недоступен.',
    )
  })

  it('400 with empty body → custom fallback passed by caller', () => {
    expect(getNotificationErrorMessage(axiosErrorWith(400, ''), 'Кастомная ошибка')).toBe('Кастомная ошибка')
  })

  it('unknown status → default fallback', () => {
    expect(getNotificationErrorMessage(axiosErrorWith(500, 'oops'))).toBe(
      'Не удалось выполнить действие. Попробуйте снова.',
    )
  })
})
