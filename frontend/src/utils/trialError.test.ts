import { describe, it, expect } from 'vitest'
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios'
import { getTrialErrorMessage, getTrialRefusalCode, isTrialTermsVersionMismatch } from './trialError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  const response: AxiosResponse = {
    status,
    data,
    statusText: '',
    headers: {},
    config: { headers: new AxiosHeaders() },
  }
  return {
    isAxiosError: true,
    response,
  } as AxiosError
}

describe('getTrialErrorMessage', () => {
  it('prints the server JSON message verbatim for 409 (TrialRefusalDto, not a bare string)', () => {
    const err = axiosErrorWith(409, { code: 'TrialAlreadyUsed', message: 'Пробный период уже был использован 01.01.2026.' })
    expect(getTrialErrorMessage(err)).toBe('Пробный период уже был использован 01.01.2026.')
  })

  it('falls back when the 409 body has no message', () => {
    const err = axiosErrorWith(409, {})
    expect(getTrialErrorMessage(err, 'резервный текст')).toBe('резервный текст')
  })

  it('maps 429 to the rate-limit text, preferring the server body if present', () => {
    expect(getTrialErrorMessage(axiosErrorWith(429, ''))).toBe('Слишком много попыток. Попробуйте завтра.')
    expect(getTrialErrorMessage(axiosErrorWith(429, 'Превышен лимит попыток в сутки'))).toBe('Превышен лимит попыток в сутки')
  })

  it('maps 400 and 404 as documented (API_CONTRACT_CYCLE18.md §363, §373)', () => {
    expect(getTrialErrorMessage(axiosErrorWith(400, 'termsVersion обязателен'))).toBe('termsVersion обязателен')
    expect(getTrialErrorMessage(axiosErrorWith(404, ''))).toBe('Пробный период недоступен — у вас нет биллинг-аккаунта.')
  })

  it('falls back on unrecognised statuses', () => {
    expect(getTrialErrorMessage(axiosErrorWith(500, ''), 'запасной текст')).toBe('запасной текст')
  })
})

describe('isTrialTermsVersionMismatch', () => {
  it('is true only for 409 with code TrialTermsVersionMismatch — never by matching text', () => {
    const err = axiosErrorWith(409, { code: 'TrialTermsVersionMismatch', message: 'Условия обновились, перечитайте их.' })
    expect(isTrialTermsVersionMismatch(err)).toBe(true)
  })

  it('is false for a different 409 code, even with similar-looking wording', () => {
    const err = axiosErrorWith(409, { code: 'TrialAlreadyActive', message: 'Пробный период уже идёт.' })
    expect(isTrialTermsVersionMismatch(err)).toBe(false)
  })

  it('is false for non-409 statuses', () => {
    expect(isTrialTermsVersionMismatch(axiosErrorWith(400, { code: 'TrialTermsVersionMismatch', message: 'x' }))).toBe(false)
  })
})

describe('getTrialRefusalCode', () => {
  it('returns the code from a 409 TrialRefusalDto', () => {
    const err = axiosErrorWith(409, { code: 'PhoneNotVerified', message: 'Подтвердите номер телефона.' })
    expect(getTrialRefusalCode(err)).toBe('PhoneNotVerified')
  })

  it('returns null when there is no 409 body', () => {
    expect(getTrialRefusalCode(axiosErrorWith(404, ''))).toBeNull()
    expect(getTrialRefusalCode(axiosErrorWith(409, undefined))).toBeNull()
  })
})
