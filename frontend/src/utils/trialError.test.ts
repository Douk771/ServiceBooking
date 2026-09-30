import { describe, it, expect } from 'vitest'
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios'
import { getTrialErrorMessage, isTrialTermsVersionMismatch } from './trialError'

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

describe('getTrialErrorMessage — demo restriction (API_CONTRACT_CYCLE28.md §599)', () => {
  it('a 403 with X-Demo-Restricted shows the demo refusal text', () => {
    const e = { response: { status: 403, data: 'В демо-версии это действие недоступно.', headers: { 'x-demo-restricted': '1' } } }
    expect(getTrialErrorMessage(e, 'запасной')).toBe('В демо-версии это действие недоступно.')
  })
  it('a plain 403 still falls through to the fallback', () => {
    expect(getTrialErrorMessage({ response: { status: 403, data: '', headers: {} } }, 'запасной')).toBe('запасной')
  })
})
