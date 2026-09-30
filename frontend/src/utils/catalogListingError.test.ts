import { describe, it, expect } from 'vitest'
import { getCatalogListingErrorMessage } from './catalogListingError'

const err = (status: number, data?: unknown) => ({ response: { status, data } })

describe('getCatalogListingErrorMessage', () => {
  it('409 JSON tariff refusal -> its message', () => {
    expect(getCatalogListingErrorMessage(err(409, { code: 'CatalogListingNotAllowedByPlan', message: 'Не входит в тариф' }), 'fb')).toBe('Не входит в тариф')
  })
  it('400/409 plain string -> the string', () => {
    expect(getCatalogListingErrorMessage(err(400, 'Не указано'), 'fb')).toBe('Не указано')
    expect(getCatalogListingErrorMessage(err(409, 'Это магазин'), 'fb')).toBe('Это магазин')
  })
  it('404 -> "Компания не найдена"', () => {
    expect(getCatalogListingErrorMessage(err(404, ''), 'fb')).toBe('Компания не найдена')
  })
  it('a string body on another status and network errors -> fallback', () => {
    expect(getCatalogListingErrorMessage(err(500, 'boom'), 'fb')).toBe('fb')
    expect(getCatalogListingErrorMessage(new Error('Network Error'), 'fb')).toBe('fb')
    expect(getCatalogListingErrorMessage(undefined, 'fb')).toBe('fb')
  })
  it('an empty 400 body -> fallback', () => {
    expect(getCatalogListingErrorMessage(err(400, ''), 'fb')).toBe('fb')
  })
})
