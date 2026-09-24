import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import {
  getPhoneVerificationErrorMessage,
  getFailureReasonFallback,
  isRetryableFailure,
  isPhoneChangeVerificationRequired,
  isPhoneChangeVerificationUnavailable,
} from './phoneVerificationError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getPhoneVerificationErrorMessage', () => {
  it('400 → server text verbatim, or the "enter a phone" fallback', () => {
    expect(getPhoneVerificationErrorMessage(axiosErrorWith(400, 'Введите номер телефона'))).toBe('Введите номер телефона')
    expect(getPhoneVerificationErrorMessage(axiosErrorWith(400, ''))).toBe('Введите номер телефона в формате +7 (900) 000-00-00')
  })

  it('409 → server text, or the "subsystem disabled" fallback (§163)', () => {
    expect(getPhoneVerificationErrorMessage(axiosErrorWith(409, 'Подтверждение телефона сейчас недоступно.'))).toBe(
      'Подтверждение телефона сейчас недоступно.',
    )
    expect(getPhoneVerificationErrorMessage(axiosErrorWith(409, ''))).toBe('Подтверждение телефона сейчас недоступно.')
  })

  it('429 → server text or the rate-limit fallback', () => {
    expect(getPhoneVerificationErrorMessage(axiosErrorWith(429, 'Слишком много попыток подтверждения. Попробуйте позже.'))).toBe(
      'Слишком много попыток подтверждения. Попробуйте позже.',
    )
    expect(getPhoneVerificationErrorMessage(axiosErrorWith(429, ''))).toBe('Слишком много попыток подтверждения. Попробуйте позже.')
  })

  it('401 → invalid token message (an anonymous call without a token is normal and never hits this)', () => {
    expect(getPhoneVerificationErrorMessage(axiosErrorWith(401, ''))).toBe('Сессия входа истекла. Обновите страницу и попробуйте снова.')
  })

  it('unknown status → caller-supplied fallback', () => {
    expect(getPhoneVerificationErrorMessage(axiosErrorWith(500, ''), 'запасной текст')).toBe('запасной текст')
  })
})

describe('getFailureReasonFallback', () => {
  it('maps every §165 reason to non-empty text, except SessionCancelled which is deliberately silent', () => {
    expect(getFailureReasonFallback('PhoneMismatch')).toContain('совпадает')
    expect(getFailureReasonFallback('MaxAccountLimitReached')).not.toContain('900') // no phone numbers leaked
    expect(getFailureReasonFallback('SessionCancelled')).toBe('')
    expect(getFailureReasonFallback(null)).toBe('')
    expect(getFailureReasonFallback(undefined)).toBe('')
  })
})

describe('isRetryableFailure', () => {
  it('offers "get a new link" for payload/contact-mismatch reasons', () => {
    expect(isRetryableFailure('PayloadExpired')).toBe(true)
    expect(isRetryableFailure('ContactNotOwnedBySender')).toBe(true)
    expect(isRetryableFailure('PhoneMismatch')).toBe(true)
  })

  it('does not offer retry for the account-wide cap or a subsystem outage', () => {
    expect(isRetryableFailure('MaxAccountLimitReached')).toBe(false)
    expect(isRetryableFailure('SubsystemDisabled')).toBe(false)
    expect(isRetryableFailure(null)).toBe(false)
  })
})

describe('change-phone gate 409s (§169) — told apart by substring, same convention as utils/authError.ts', () => {
  const VERIFICATION_REQUIRED =
    'На этом номере уже есть записи. Подтвердите его через MAX — тогда номер можно будет сменить.'
  const SUBSYSTEM_DISABLED = 'Сейчас сменить номер на этот нельзя: подтверждение номера на платформе пока не работает.'

  it('recognizes the "needs verification" wording', () => {
    expect(isPhoneChangeVerificationRequired(axiosErrorWith(409, VERIFICATION_REQUIRED))).toBe(true)
    expect(isPhoneChangeVerificationUnavailable(axiosErrorWith(409, VERIFICATION_REQUIRED))).toBe(false)
  })

  it('recognizes the "subsystem disabled" wording', () => {
    expect(isPhoneChangeVerificationUnavailable(axiosErrorWith(409, SUBSYSTEM_DISABLED))).toBe(true)
    expect(isPhoneChangeVerificationRequired(axiosErrorWith(409, SUBSYSTEM_DISABLED))).toBe(false)
  })

  it('an unrelated 409 (or non-409) matches neither', () => {
    expect(isPhoneChangeVerificationRequired(axiosErrorWith(409, 'Документы были обновлены ещё раз…'))).toBe(false)
    expect(isPhoneChangeVerificationUnavailable(axiosErrorWith(409, 'Документы были обновлены ещё раз…'))).toBe(false)
    expect(isPhoneChangeVerificationRequired(axiosErrorWith(400, VERIFICATION_REQUIRED))).toBe(false)
  })
})
