import { describe, it, expect } from 'vitest'
import vectors from '../../../../contracts/cycle42/bani-vectors.json'
import { OWNER_WARNING_TEXT, ownerWarningTexts, restrictedItemPrompt } from './ownerWarnings'

describe('ownerWarningTexts', () => {
  it('prints the contract warning texts verbatim (bani-vectors ownerText.warningTexts)', () => {
    expect(OWNER_WARNING_TEXT).toEqual(vectors.ownerText.warningTexts)
  })

  it('has a text for every code and shows them in the server order, without repeats', () => {
    expect(ownerWarningTexts(['Passport', 'CardNumber', 'Passport'])).toEqual([OWNER_WARNING_TEXT.Passport, OWNER_WARNING_TEXT.CardNumber])
    expect(Object.values(OWNER_WARNING_TEXT).every((t) => t.length > 20)).toBe(true)
  })

  it('ignores a code it does not know yet and an absent list', () => {
    expect(ownerWarningTexts(['SomethingNew', 'HealthClaim'])).toEqual([OWNER_WARNING_TEXT.HealthClaim])
    expect(ownerWarningTexts(undefined)).toEqual([])
    expect(ownerWarningTexts(null)).toEqual([])
  })
})

describe('restrictedItemPrompt', () => {
  it('turns the 409 ItemRestrictedConfirmationRequired into a prompt with plain text', () => {
    expect(
      restrictedItemPrompt({ code: 'ItemRestrictedConfirmationRequired', markers: ['пив', ' ', 'кальян'], noticeText: '<p>Проверьте <b>позицию</b>.</p>' }),
    ).toEqual({ markers: ['пив', 'кальян'], text: 'Проверьте позицию .' })
  })

  it('is null for any other conflict or no conflict', () => {
    expect(restrictedItemPrompt({ code: 'ItemLimitReached' })).toBeNull()
    expect(restrictedItemPrompt(null)).toBeNull()
  })

  it('copes with a conflict that came without markers or text', () => {
    expect(restrictedItemPrompt({ code: 'ItemRestrictedConfirmationRequired' })).toEqual({ markers: [], text: '' })
  })
})
