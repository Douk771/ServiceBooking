import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getAuthErrorMessage } from './authError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getAuthErrorMessage', () => {
  it('429 on login → server text (rate limit)', () => {
    expect(getAuthErrorMessage(axiosErrorWith(429, 'Слишком много попыток входа. Повторите через минуту.'))).toBe(
      'Слишком много попыток входа. Повторите через минуту.',
    )
  })

  it('429 on register → server text (rate limit)', () => {
    expect(getAuthErrorMessage(axiosErrorWith(429, 'Слишком много регистраций с этого адреса. Повторите позже.'))).toBe(
      'Слишком много регистраций с этого адреса. Повторите позже.',
    )
  })

  it('429 with empty body → generic fallback', () => {
    expect(getAuthErrorMessage(axiosErrorWith(429, undefined))).toBe('Слишком много попыток. Повторите позже.')
  })

  it('400 missing acceptedLegal → consent message', () => {
    expect(
      getAuthErrorMessage(axiosErrorWith(400, 'Consent to the Terms of Service and the Privacy Policy is required.')),
    ).toBe('Необходимо принять условия использования и политику обработки персональных данных.')
  })

  it('400 with other text → server text as-is', () => {
    expect(getAuthErrorMessage(axiosErrorWith(400, 'Phone number must contain 10 to 15 digits.'))).toBe(
      'Phone number must contain 10 to 15 digits.',
    )
  })

  it('500 → generic fallback', () => {
    expect(getAuthErrorMessage(axiosErrorWith(500, undefined))).toBe('Произошла ошибка. Попробуйте снова.')
  })

  it('no response at all (network error) → generic fallback', () => {
    expect(getAuthErrorMessage(new Error('Network Error'))).toBe('Произошла ошибка. Попробуйте снова.')
  })
})
