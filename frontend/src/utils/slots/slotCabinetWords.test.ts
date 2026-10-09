import { describe, it, expect } from 'vitest'
import { DEFAULT_CABINET_WORDS, resolveCabinetWords } from './slotCabinetWords'

describe('resolveCabinetWords', () => {
  it('keeps the dom wording without overrides', () => {
    expect(resolveCabinetWords()).toEqual(DEFAULT_CABINET_WORDS)
    expect(DEFAULT_CABINET_WORDS.manualOrder).toBe('Ручной заказ')
  })
  it('lets a vertical override a part and keeps the rest', () => {
    const w = resolveCabinetWords({ manualOrder: 'Ручная бронь' })
    expect(w.manualOrder).toBe('Ручная бронь')
    expect(w.servicesTitle).toBe(DEFAULT_CABINET_WORDS.servicesTitle)
  })
})
