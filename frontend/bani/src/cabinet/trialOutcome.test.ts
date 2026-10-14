import { describe, it, expect } from 'vitest'
import { isTrialTermsMismatch, readTrialRefusal, trialErrorMessage } from './trialOutcome'

const err = (status: number | undefined, data?: unknown) => ({ response: status === undefined ? undefined : { status, data } })

describe('trial refusal', () => {
  it('reads a 409 with granted:false and prints the server text', () => {
    const e = err(409, { granted: false, refusalCode: 'TrialAlreadyUsed', message: 'Пробный период уже использован' })
    expect(readTrialRefusal(e)?.refusalCode).toBe('TrialAlreadyUsed')
    expect(trialErrorMessage(e)).toBe('Пробный период уже использован')
  })

  it('does not take other 409 bodies or other statuses for a refusal', () => {
    expect(readTrialRefusal(err(409, { code: 'X', message: 'm' }))).toBeNull()
    expect(readTrialRefusal(err(400, { granted: false }))).toBeNull()
    expect(readTrialRefusal(err(409, 'строка'))).toBeNull()
  })

  it('falls back to a fixed text for a refusal without a message and for network errors', () => {
    expect(trialErrorMessage(err(409, { granted: false }))).toBe('Пробный период недоступен.')
    expect(trialErrorMessage(err(undefined))).toMatch(/Нет связи/)
  })

  it('recognises changed terms', () => {
    expect(isTrialTermsMismatch(err(409, { granted: false, refusalCode: 'TrialTermsVersionMismatch' }))).toBe(true)
    expect(isTrialTermsMismatch(err(409, { granted: false, refusalCode: 'TrialAlreadyUsed' }))).toBe(false)
  })
})
