import { describe, it, expect } from 'vitest'
import { zapisFaq } from './zapisFaq'

describe('zapisFaq (T38-09)', () => {
  it('has 6..8 items, pricing question links to /pricing', () => {
    expect(zapisFaq.length).toBeGreaterThanOrEqual(6)
    expect(zapisFaq.length).toBeLessThanOrEqual(8)
    expect(zapisFaq.find((f) => f.link)?.link?.to).toBe('/pricing')
  })
})
