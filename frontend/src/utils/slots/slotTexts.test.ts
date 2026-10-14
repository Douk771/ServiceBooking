import { describe, expect, it } from 'vitest'
import { resolveSlotText, type SlotText } from './slotTexts'

const fallbacks: Record<'A', SlotText> = { A: { short: '<p>fallback</p>', full: null } }

describe('resolveSlotText', () => {
  it('falls back when the server has no text', () => {
    expect(resolveSlotText(fallbacks, 'A', undefined)).toBe(fallbacks.A)
    expect(resolveSlotText(fallbacks, 'A', '  ')).toBe(fallbacks.A)
  })
  it('shows a server text without sections whole', () => {
    expect(resolveSlotText(fallbacks, 'A', '<p>x</p>')).toEqual({ short: '<p>x</p>', full: null })
  })
})
