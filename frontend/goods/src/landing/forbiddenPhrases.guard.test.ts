import { describe, expect, it } from 'vitest'
import { publicCopyViolations, visibleStrings } from '@/utils/forbiddenPhrases'
import { goodsLanding } from './goodsLanding'
import { goodsFaq } from './goodsFaq'

// CY48-07 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.13.1): the public copy of goods has none of the phrases of
// contracts/cycle48/forbidden-phrases.json (scope `publicCopy`). The ezbook configs are checked by src/test/forbiddenPhrases.guard.test.ts.
describe.each([
  ['goodsLanding', goodsLanding],
  ['goodsFaq', goodsFaq],
] as const)('CY48-07 %s', (_name, value) => {
  it('has no forbidden phrase', () => {
    expect(visibleStrings(value).length).toBeGreaterThan(3)
    expect(publicCopyViolations(value)).toEqual([])
  })
})
