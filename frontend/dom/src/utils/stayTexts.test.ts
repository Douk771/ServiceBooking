// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { STAY_FALLBACKS, resolveStayText, stayTextSection, type StayTextKey } from './stayTexts'

// ЮР-1 / Т37-06 / Т37-13 (LEGAL_REVIEW_CYCLE37.md §16): the wording rules apply to the fallbacks shown while the lawyer's texts
// are not published.
const KEYS = Object.keys(STAY_FALLBACKS) as StayTextKey[]
const allText = (k: StayTextKey) => `${STAY_FALLBACKS[k].short} ${STAY_FALLBACKS[k].full ?? ''}`

describe('StayTexts fallbacks', () => {
  it('cover all twenty-two keys of the vertical (13 of cycle 37 + 9 of cycle 39)', () => {
    expect(KEYS).toHaveLength(22)
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
    expect(STAY_FALLBACKS.StayTouristTaxNotice.short).toContain('Цена проживания указана без туристического налога')
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

describe('StayTexts fallbacks of cycle 39', () => {
  it('the new edition of the booking terms has the clause 3а about services added to a stay (Т39-08)', () => {
    expect(STAY_FALLBACKS.StayBookingTerms.short).toContain('3а. <strong>Услуги к проживанию.</strong>')
    expect(STAY_FALLBACKS.StayBookingNotice.full).toContain('заказанные услуги и их позиции')
  })

  it('the tourist tax text puts the tax on the stay only, services are outside it (Т39-15)', () => {
    expect(STAY_FALLBACKS.StayTouristTaxNotice.short).toContain('на стоимость проживания (без бани, чана и других услуг)')
  })

  it('the cancellation terms never promise «не меньше 0 ₽» and never use the SPEC template «Стандартный» (ЮР39-1)', () => {
    const t = allText('StayServiceCancellationTerms')
    expect(t).not.toMatch(/не меньше 0/)
    expect(t).not.toContain('Стандартный')
    expect(t).toContain('только фактически понесённые расходы')
  })

  it('the guest texts of a session never say «бизнес-день» (ЮР39-8)', () => {
    for (const k of KEYS) expect(allText(k).toLowerCase()).not.toContain('бизнес-день')
  })

  it('the push notice names what is dropped from the push and the 180-symbol limit (ЮР39-3)', () => {
    const t = STAY_FALLBACKS.StayReminderPushOwnerNotice.short
    expect(t).toContain('180 символов')
    expect(t).toContain('выпадают')
  })

  it('the service booking notice links to the service conditions, the agreement and the policy', () => {
    const t = STAY_FALLBACKS.StayServiceBookingNotice.short
    expect(t).toContain('href="#service-terms"')
    expect(t).toContain('href="/terms"')
    expect(t).toContain('href="/privacy"')
  })
})

// Т42-01 / Т42-07 (LEGAL_REVIEW_CYCLE42.md §12).
describe('service texts name the company neutrally (Т42-01)', () => {
  it.each(['StayServiceBookingNotice', 'StayServiceBookingTerms'] as StayTextKey[])('%s has no «сдаёт» fallback', (k) => {
    expect(allText(k)).not.toMatch(/сдаёт/)
    expect(allText(k)).toContain('которая оказывает эту услугу')
  })
  it('the house texts keep their wording', () => {
    expect(allText('StayBookingNotice')).toContain('которая сдаёт этот дом')
  })
  it('the owner safety hint warns about curative claims and medical services (Т42-07)', () => {
    expect(allText('StayServiceSafetyOwnerNotice')).toMatch(/лечебного эффекта[\s\S]*медицинск/)
  })
})
