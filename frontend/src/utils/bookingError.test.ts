import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getBookingErrorMessage } from './bookingError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getBookingErrorMessage', () => {
  // API_CONTRACT_CYCLE6.md §42.3 — the reader must understand it's a tariff issue, not a generic
  // "something's off" message.
  it('402 (tariff does not include online booking) → tariff-specific message', () => {
    expect(getBookingErrorMessage(axiosErrorWith(402, 'AllowOnlineBooking is false'))).toBe(
      'Онлайн-запись недоступна: тариф компании её не включает',
    )
  })

  it('402 with "expired" body → subscription-expired message, not the generic tariff one', () => {
    expect(getBookingErrorMessage(axiosErrorWith(402, 'Subscription expired'))).toBe(
      'Подписка компании истекла — запись временно недоступна.',
    )
  })

  it('403 (Company.AllowSelfBooking == false) → salon-not-accepting message', () => {
    expect(getBookingErrorMessage(axiosErrorWith(403, undefined))).toBe('Салон сейчас не принимает онлайн-записи')
  })

  it('409 → slot-taken message', () => {
    expect(getBookingErrorMessage(axiosErrorWith(409, undefined))).toBe('Это время уже занято. Выберите другой слот.')
  })

  it('unrecognized status → generic fallback', () => {
    expect(getBookingErrorMessage(axiosErrorWith(500, undefined))).toBe('Произошла ошибка. Попробуйте снова.')
  })
})
