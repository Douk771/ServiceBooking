// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  isRequiredWrittenConsent,
  getHealthNoteSaveErrorMessage,
  getMarkWrittenConsentErrorMessage,
  getRevokeWrittenConsentErrorMessage,
} from './healthConsentError'

function axiosError(status: number, data: unknown) {
  return { isAxiosError: true, response: { status, data } }
}

describe('isRequiredWrittenConsent', () => {
  it('is true for a 400 with a requiredTextKey JSON body', () => {
    expect(isRequiredWrittenConsent(axiosError(400, { message: 'нужно согласие', requiredTextKey: 'HealthDataWrittenConsentForm' }))).toBe(true)
  })

  it('is false for a 400 with a bare string body (value too long, etc.)', () => {
    expect(isRequiredWrittenConsent(axiosError(400, 'Значение не может быть пустым.'))).toBe(false)
  })

  it('is false for a non-400 status', () => {
    expect(isRequiredWrittenConsent(axiosError(404, { requiredTextKey: 'x' }))).toBe(false)
  })

  it('is false for a plain Error with no response at all', () => {
    expect(isRequiredWrittenConsent(new Error('network down'))).toBe(false)
  })
})

describe('getHealthNoteSaveErrorMessage', () => {
  it('shows the server string verbatim', () => {
    expect(getHealthNoteSaveErrorMessage(axiosError(400, 'Значение не может быть пустым.'))).toBe('Значение не может быть пустым.')
  })

  it('falls back to a generic message when the body is empty', () => {
    expect(getHealthNoteSaveErrorMessage(axiosError(400, ''))).toBe('Не удалось сохранить. Попробуйте снова.')
  })
})

describe('getMarkWrittenConsentErrorMessage', () => {
  it('flags outdatedForm on 409 and passes the server text through', () => {
    const result = getMarkWrittenConsentErrorMessage(axiosError(409, 'Текст бланка обновлён — распечатайте бланк заново.'))
    expect(result.outdatedForm).toBe(true)
    expect(result.message).toBe('Текст бланка обновлён — распечатайте бланк заново.')
  })

  it('does not flag outdatedForm on 400', () => {
    const result = getMarkWrittenConsentErrorMessage(axiosError(400, 'confirmed должен быть true.'))
    expect(result.outdatedForm).toBe(false)
    expect(result.message).toBe('confirmed должен быть true.')
  })
})

describe('getRevokeWrittenConsentErrorMessage', () => {
  it('shows the server string verbatim', () => {
    expect(getRevokeWrittenConsentErrorMessage(axiosError(400, 'Неизвестная причина.'))).toBe('Неизвестная причина.')
  })
})
