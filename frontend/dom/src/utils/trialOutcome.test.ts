// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { isTrialTermsMismatch, readTrialRefusal, trialErrorMessage } from './trialOutcome'

const err = (status: number, data: unknown) => ({ isAxiosError: true, response: { status, data } })

describe('trial refusal', () => {
  it('a 409 with granted:false is a refusal, its message is printed as it is', () => {
    const e = err(409, { granted: false, refusalCode: 'TrialAlreadyUsed', message: 'Пробный период уже использован' })
    expect(readTrialRefusal(e)?.refusalCode).toBe('TrialAlreadyUsed')
    expect(trialErrorMessage(e)).toBe('Пробный период уже использован')
    expect(isTrialTermsMismatch(e)).toBe(false)
  })

  it('a changed terms version asks to read the terms again', () => {
    expect(isTrialTermsMismatch(err(409, { granted: false, refusalCode: 'TrialTermsVersionMismatch', message: 'Условия изменились' }))).toBe(true)
  })

  it('other errors use the common mapping', () => {
    expect(trialErrorMessage(err(500, ''))).toBe('Сервер временно недоступен. Попробуйте позже.')
    expect(trialErrorMessage(err(429, 'Слишком много попыток'))).toBe('Слишком много попыток')
    expect(readTrialRefusal(err(409, 'строка'))).toBeNull()
  })
})
