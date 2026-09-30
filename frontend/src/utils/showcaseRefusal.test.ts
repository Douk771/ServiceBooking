import { describe, it, expect } from 'vitest'
import { getShowcaseRefusalMessage } from './showcaseRefusal'
import { getBookingErrorMessage } from './bookingError'
import { SHOWCASE_FALLBACK_TEXTS } from './showcaseTexts'

const closed = (message?: unknown) => ({
  response: { status: 409, data: { code: 'ShowcaseBookingClosed', message } },
})

describe('getShowcaseRefusalMessage (API_CONTRACT_CYCLE28.md §592)', () => {
  it('recognises the JSON 409 and returns the server message', () => {
    expect(getShowcaseRefusalMessage(closed('Запись не принимается.'))).toBe('Запись не принимается.')
  })

  it('a JSON 409 without a message is still the refusal (empty string, not null)', () => {
    expect(getShowcaseRefusalMessage(closed(undefined))).toBe('')
  })

  it('the text/plain "slot taken" 409 is NOT the refusal', () => {
    expect(getShowcaseRefusalMessage({ response: { status: 409, data: 'Slot is already booked' } })).toBeNull()
  })

  it('a JSON body with another code, other statuses and non-axios errors are not the refusal', () => {
    expect(getShowcaseRefusalMessage({ response: { status: 409, data: { code: 'Other' } } })).toBeNull()
    expect(getShowcaseRefusalMessage({ response: { status: 403, data: { code: 'ShowcaseBookingClosed' } } })).toBeNull()
    expect(getShowcaseRefusalMessage(new Error('boom'))).toBeNull()
    expect(getShowcaseRefusalMessage(undefined)).toBeNull()
  })
})

describe('getBookingErrorMessage — closed showcase vs slot taken', () => {
  it('shows the server message for the closed-showcase 409, never "время занято"', () => {
    expect(getBookingErrorMessage(closed('Это пример страницы салона.'))).toBe('Это пример страницы салона.')
  })

  it('falls back to the §600 text when the refusal carries no message', () => {
    expect(getBookingErrorMessage(closed(undefined))).toBe(SHOWCASE_FALLBACK_TEXTS.ShowcaseBookingClosed)
  })

  it('a plain 409 still means the slot is taken', () => {
    expect(getBookingErrorMessage({ response: { status: 409, data: 'Slot is already booked' } })).toBe(
      'Это время уже занято. Выберите другой слот.',
    )
  })
})
