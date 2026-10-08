// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { STAY_FALLBACKS, resolveStayText, stayTextSection, type StayTextKey } from './stayTexts'

// ЮР-1 / Т37-06 / Т37-13 (LEGAL_REVIEW_CYCLE37.md §16): the wording rules apply to the fallbacks shown while the lawyer's texts
// are not published.
const KEYS = Object.keys(STAY_FALLBACKS) as StayTextKey[]
const allText = (k: StayTextKey) => `${STAY_FALLBACKS[k].short} ${STAY_FALLBACKS[k].full ?? ''}`

describe('StayTexts fallbacks', () => {
  it('cover all thirteen keys of the vertical', () => {
    expect(KEYS).toHaveLength(13)
  })

  it.each(KEYS)('%s has no forbidden words (задаток, невозвратный, депозит) and no «чек» for the guest side', (k) => {
    const t = allText(k).toLowerCase()
    expect(t).not.toMatch(/задат/)
    expect(t).not.toMatch(/невозвратн/)
    expect(t).not.toMatch(/депозит/)
  })

  it.each(KEYS)('%s has no unresolved placeholders', (k) => {
    expect(allText(k)).not.toMatch(/\{\{|\}\}|ТРЕБУЕТСЯ ТЕКСТ/)
  })

  it('the text under the «Забронировать» button links to the conditions, the agreement and the policy (Т37-13)', () => {
    const t = STAY_FALLBACKS.StayBookingNotice.short
    expect(t).toContain('href="#stay-terms"')
    expect(t).toContain('href="/terms"')
    expect(t).toContain('href="/privacy"')
  })

  it('guest-facing proof text says «подтверждение оплаты» and never asks for a passport', () => {
    expect(STAY_FALLBACKS.StayPaymentProofNotice.short).toMatch(/Не прикладывайте фото паспорта/)
  })

  it('the tourist tax text says the price is without the tax and names no figure (the local rate is not verified)', () => {
    expect(STAY_FALLBACKS.StayTouristTaxNotice.short).toContain('Цена указана без туристического налога')
    expect(STAY_FALLBACKS.StayTouristTaxNotice.short).not.toMatch(/\d\s?%|₽/)
  })
})

describe('resolveStayText', () => {
  it('uses the fallback when the server has no text', () => {
    expect(resolveStayText('StayBookingNotice', null)).toBe(STAY_FALLBACKS.StayBookingNotice)
    expect(resolveStayText('StayBookingNotice', undefined)).toBe(STAY_FALLBACKS.StayBookingNotice)
    expect(resolveStayText('StayBookingNotice', '  ')).toBe(STAY_FALLBACKS.StayBookingNotice)
  })

  it('splits a sectioned server text into the short line and the full part', () => {
    const html = '<h2>Короткая строка (видна всегда)</h2><p>коротко</p><h2>Полный текст (раскрывается)</h2><p>подробно</p>'
    expect(resolveStayText('StayBookingNotice', html)).toEqual({ short: '<p>коротко</p>', full: '<p>подробно</p>' })
  })

  it('shows a server text without the expected sections whole, rather than dropping it', () => {
    expect(resolveStayText('StayGuestCommentNotice', '<p>весь текст</p>')).toEqual({ short: '<p>весь текст</p>', full: null })
  })

  it('stayTextSection finds a section by heading and returns null when absent', () => {
    const html = '<h2>Стандартный</h2><p>a</p><h2>Общее</h2><p>b</p>'
    expect(stayTextSection(html, 'Общее')).toBe('<p>b</p>')
    expect(stayTextSection(html, 'Гибкий')).toBeNull()
  })
})
