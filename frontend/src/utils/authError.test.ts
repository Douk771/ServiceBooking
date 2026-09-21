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
    expect(getAuthErrorMessage(axiosErrorWith(429, undefined))).toBe(
      'Слишком много попыток входа, попробуйте через несколько минут',
    )
  })

  it('401 → wrong credentials, byte-identical regardless of which side is wrong', () => {
    expect(getAuthErrorMessage(axiosErrorWith(401, undefined))).toBe('Неверный телефон или пароль')
  })

  it('423 → lockout message, distinct from wrong credentials', () => {
    expect(getAuthErrorMessage(axiosErrorWith(423, 'Account temporarily locked'))).toBe(
      'Вход временно заблокирован из-за нескольких неудачных попыток. Попробуйте через 15 минут',
    )
  })

  it('403 → sign-in not allowed message', () => {
    expect(getAuthErrorMessage(axiosErrorWith(403, 'Sign-in not allowed'))).toBe(
      'Вход в этот аккаунт недоступен. Обратитесь в поддержку',
    )
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

  it('500 → service unavailable message, distinct from network error', () => {
    expect(getAuthErrorMessage(axiosErrorWith(500, undefined))).toBe(
      'Сервис временно недоступен. Попробуйте ещё раз через минуту',
    )
  })

  it('400 register with a single Identity error array → human Russian text', () => {
    expect(
      getAuthErrorMessage(axiosErrorWith(400, [{ code: 'PasswordRequiresUpper', description: 'x' }])),
    ).toBe('Пароль должен содержать хотя бы одну заглавную букву')
  })

  it('400 register with several Identity errors → all shown, not just the first', () => {
    expect(
      getAuthErrorMessage(
        axiosErrorWith(400, [
          { code: 'PasswordRequiresLower', description: 'x' },
          { code: 'PasswordRequiresUpper', description: 'y' },
          { code: 'PasswordRequiresDigit', description: 'z' },
        ]),
      ),
    ).toBe(
      'Пароль должен содержать хотя бы одну строчную букву Пароль должен содержать хотя бы одну заглавную букву Пароль должен содержать хотя бы одну цифру',
    )
  })

  it('400 register DuplicateUserName → phone-already-registered message, not "check your data"', () => {
    expect(getAuthErrorMessage(axiosErrorWith(400, [{ code: 'DuplicateUserName', description: 'x' }]))).toBe(
      'Этот телефон уже зарегистрирован',
    )
  })

  it('400 register with an unknown Identity code falls back to the server description', () => {
    expect(getAuthErrorMessage(axiosErrorWith(400, [{ code: 'SomeNewCode', description: 'some server text' }]))).toBe(
      'some server text',
    )
  })

  it('no response at all (network error) → distinct message from 5xx', () => {
    expect(getAuthErrorMessage(new Error('Network Error'))).toBe(
      'Не удалось связаться с сервером. Проверьте интернет и попробуйте ещё раз',
    )
  })
})
