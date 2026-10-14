// @vitest-environment node
import { describe, it, expect } from 'vitest'
import vectors from '../../../../contracts/cycle42/bani-vectors.json'
import { validateGuestsCount } from './guestsCount'
import { matchRestrictedItems, RESTRICTED_STEMS } from './restrictedItems'
import { checkOwnerText, OWNER_TEXT_ERROR, OWNER_TEXT_WARNINGS } from './ownerText'

// contracts/cycle42/bani-vectors.json is the single reference (the same file feeds the C# tests).
describe('bani-vectors guests', () => {
  for (const c of vectors.guests.cases) {
    it(c.id, () => {
      const r = validateGuestsCount(c.guestsCount, c.capacity)
      expect(r.ok).toBe(c.ok)
      if (c.ok) expect(r.stored).toBe(c.stored)
      else expect(r.error).toBe(c.error)
    })
  }
})

describe('bani-vectors restrictedItems', () => {
  it('stems equal the contract list', () => expect([...RESTRICTED_STEMS]).toEqual(vectors.restrictedItems.stems))
  for (const c of vectors.restrictedItems.cases) it(c.id, () => expect(matchRestrictedItems(c.text)).toEqual(c.markers))
})

describe('bani-vectors ownerText', () => {
  it('texts equal the contract', () => {
    expect(OWNER_TEXT_ERROR).toBe(vectors.ownerText.errorText)
    expect(OWNER_TEXT_WARNINGS).toEqual(vectors.ownerText.warningTexts)
  })
  for (const c of vectors.ownerText.cases) {
    it(c.id, () => {
      const r = checkOwnerText(c.text)
      expect(r.errors).toEqual(c.errors)
      expect(r.warnings).toEqual(c.warnings)
    })
  }
})
