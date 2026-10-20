import { describe, it, expect } from 'vitest'
import { BTN_PRIMARY, BTN_SECONDARY, CHECK_ITEM, CONTAINER, EYEBROW, FOCUS_RING, H2, H3, LEAD } from './classes'

// CY46-14: витрина about.ezbook.ru использует эти константы — цикл 46 их не меняет (значения на 9d87a2dc).
describe('classes used by about.ezbook.ru are unchanged (CY46-14)', () => {
  it('literals', () => {
    expect(EYEBROW).toBe('text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark')
    expect(H2).toBe('font-serif text-[32px] sm:text-[40px] leading-[1.1] font-medium text-ink')
    expect(H3).toBe('font-serif text-[26px] sm:text-[28px] leading-[1.2] font-medium text-ink')
    expect(LEAD).toBe('mt-5 text-[16px] leading-relaxed text-ink-soft max-w-[480px]')
    expect(BTN_PRIMARY).toBe(
      'inline-flex items-center gap-2 bg-ink hover:bg-ink/90 !text-cream text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors',
    )
    expect(BTN_SECONDARY).toBe(
      'inline-flex items-center bg-white hover:bg-cream-deep !text-ink border border-line text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors',
    )
    expect(CHECK_ITEM).toBe('flex items-start gap-3 text-[15px] leading-[1.6] text-ink')
    expect(FOCUS_RING).toBe('focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-gold-dark')
    expect(CONTAINER).toBe('max-w-[1180px] mx-auto px-4 sm:px-8')
  })
})
