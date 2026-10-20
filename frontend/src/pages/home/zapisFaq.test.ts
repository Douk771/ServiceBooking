import { describe, it, expect } from 'vitest'
import contract from '../../../../contracts/cycle46/zapis-landing.json'
import { zapisFaq } from './zapisFaq'

describe('zapisFaq (T38-09)', () => {
  it('has 6..8 items, pricing question links to /pricing', () => {
    expect(zapisFaq.length).toBeGreaterThanOrEqual(6)
    expect(zapisFaq.length).toBeLessThanOrEqual(8)
    expect(zapisFaq.find((f) => f.link)?.link?.to).toBe('/pricing')
  })

  it('CY46-05: 8 questions, owner questions first, pricing link on the first one', () => {
    expect(zapisFaq).toHaveLength(8)
    expect(zapisFaq[0].question).toBe(contract.faq[0].question)
    expect(zapisFaq[1].question).toBe(contract.faq[1].question)
    expect(zapisFaq[0].link?.to).toBe('/pricing')
    expect(zapisFaq.slice(1).some((f) => f.link)).toBe(false)
  })
})
