import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getCancelErrorMessage } from './cancelError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getCancelErrorMessage', () => {
  it('400 with a "300" length-limit body → reason-too-long message', () => {
    expect(getCancelErrorMessage(axiosErrorWith(400, 'Cancellation reason must be 300 characters or fewer.'))).toBe(
      'Причина не может быть длиннее 300 символов.',
    )
  })

  it('400 with an unrecognized body → generic Russian message, not the raw server text', () => {
    expect(getCancelErrorMessage(axiosErrorWith(400, 'Booking is already cancelled.'))).toBe(
      'Не удалось отменить запись. Проверьте данные и попробуйте снова.',
    )
  })

  it('403 → insufficient rights message', () => {
    expect(getCancelErrorMessage(axiosErrorWith(403, undefined))).toBe('Недостаточно прав для отмены этой записи.')
  })

  it('404 → booking-not-found message', () => {
    expect(getCancelErrorMessage(axiosErrorWith(404, undefined))).toBe('Запись не найдена — возможно, её уже отменили.')
  })

  it('409 with server-composed sentence → shown verbatim (ARCHITECTURE_CYCLE17.md §304.2)', () => {
    expect(
      getCancelErrorMessage(
        axiosErrorWith(409, 'Отменить запись можно не позже чем за 24 ч до визита. Чтобы отменить, свяжитесь с салоном.'),
      ),
    ).toBe('Отменить запись можно не позже чем за 24 ч до визита. Чтобы отменить, свяжитесь с салоном.')
  })

  it('409 with no body → fallback message', () => {
    expect(getCancelErrorMessage(axiosErrorWith(409, undefined))).toBe(
      'Отменить запись уже нельзя — свяжитесь с салоном.',
    )
  })

  it('unrecognized status → generic fallback', () => {
    expect(getCancelErrorMessage(axiosErrorWith(500, undefined))).toBe('Не удалось отменить запись. Попробуйте снова.')
  })

  it('no response at all → generic fallback', () => {
    expect(getCancelErrorMessage(new Error('Network Error'))).toBe('Не удалось отменить запись. Попробуйте снова.')
  })
})
