import { describe, it, expect } from 'vitest'
import { BANI_GUEST_WORDS } from './baniGuestWords'
import { DEFAULT_GUEST_WORDS, resolveGuestWords } from '@/utils/slots/slotGuestWords'

describe('guest words of bani', () => {
  it('say «бронь» and never «заказ» (Q-L42-5)', () => {
    for (const [k, v] of Object.entries(BANI_GUEST_WORDS)) expect(v.toLowerCase(), k).not.toContain('заказ')
  })
  it('override every key of the default set', () => {
    expect(Object.keys(BANI_GUEST_WORDS).sort()).toEqual(Object.keys(DEFAULT_GUEST_WORDS).sort())
  })
  it('keep the defaults for what a vertical leaves out (dom unchanged)', () => {
    expect(resolveGuestWords(undefined)).toEqual(DEFAULT_GUEST_WORDS)
    expect(resolveGuestWords({ eyebrow: 'Ваша бронь' }).loadError).toBe(DEFAULT_GUEST_WORDS.loadError)
  })
})
