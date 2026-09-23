import { describe, it, expect } from 'vitest'
import { AxiosError } from 'axios'
import { getLegalErrorMessage, getPricingPublicBlockedMessage } from './legalError'

function axiosErrorWith(status: number, data: unknown): AxiosError {
  return {
    isAxiosError: true,
    response: { status, data, statusText: '', headers: {}, config: {} as never },
  } as unknown as AxiosError
}

describe('getLegalErrorMessage', () => {
  it('503 → unavailable message', () => {
    expect(getLegalErrorMessage(axiosErrorWith(503, 'Правовые документы временно недоступны.'))).toBe(
      'Правовые документы временно недоступны.',
    )
  })

  it('503 with empty body → generic unavailable fallback', () => {
    expect(getLegalErrorMessage(axiosErrorWith(503, undefined))).toBe('Правовые документы временно недоступны.')
  })

  it('409 → re-read new revision message', () => {
    expect(
      getLegalErrorMessage(
        axiosErrorWith(409, 'Документы были обновлены ещё раз — перечитайте и примите новую редакцию.'),
      ),
    ).toBe('Документы были обновлены ещё раз — перечитайте и примите новую редакцию.')
  })

  it('404 → not found message', () => {
    expect(getLegalErrorMessage(axiosErrorWith(404, undefined))).toBe('Документ не найден.')
  })

  it('400 → required-fields message', () => {
    expect(getLegalErrorMessage(axiosErrorWith(400, 'Обе версии документов обязательны.'))).toBe(
      'Обе версии документов обязательны.',
    )
  })

  it('500 → generic fallback', () => {
    expect(getLegalErrorMessage(axiosErrorWith(500, undefined))).toBe(
      'Не удалось загрузить документ. Попробуйте снова.',
    )
  })
})

describe('getPricingPublicBlockedMessage', () => {
  it('409 with a body → returns the server message verbatim (legal wording, not rewritten)', () => {
    const body = {
      reason: 'OfferIsDraft',
      message: 'Публичные цены нельзя включить: оферта — черновая редакция.',
      documentType: 'TermsOwner',
      version: '2026-09-22-draft',
    }
    expect(getPricingPublicBlockedMessage(axiosErrorWith(409, body))).toBe(body.message)
  })

  it('409 with no body → generic fallback', () => {
    expect(getPricingPublicBlockedMessage(axiosErrorWith(409, undefined))).toBe(
      'Публичные цены нельзя включить прямо сейчас.',
    )
  })

  it('non-409 error → null (not this endpoint\'s 409 gate)', () => {
    expect(getPricingPublicBlockedMessage(axiosErrorWith(400, 'что-то другое'))).toBeNull()
    expect(getPricingPublicBlockedMessage(axiosErrorWith(500, undefined))).toBeNull()
  })

  it('non-axios error → null', () => {
    expect(getPricingPublicBlockedMessage(new Error('network down'))).toBeNull()
  })
})
