import { describe, it, expect } from 'vitest'
import routes from '../../../../contracts/cycle42/bani-routes.json'
import { isBathsSlugFormatValid, isBathsSlugReserved, normalizeSlugInput, slugLocalProblem, BATHS_SLUG_FORMAT_TEXT, BATHS_SLUG_RESERVED_TEXT } from './baniSlug'

describe('baniSlug', () => {
  it('accepts latin, digits and inner hyphens within the length of bani-routes.json', () => {
    expect(isBathsSlugFormatValid('sibirskie-bani')).toBe(true)
    expect(isBathsSlugFormatValid('ab')).toBe(false)
    expect(isBathsSlugFormatValid('a'.repeat(routes.slugMaxLength + 1))).toBe(false)
    expect(isBathsSlugFormatValid('-bad')).toBe(false)
    expect(isBathsSlugFormatValid('bad-')).toBe(false)
    expect(isBathsSlugFormatValid('Bad')).toBe(false)
  })

  it('knows every reserved word of the file and ignores case', () => {
    for (const w of routes.reservedSlugs) expect(isBathsSlugReserved(w)).toBe(true)
    expect(isBathsSlugReserved('CABINET')).toBe(true)
    expect(isBathsSlugReserved('sibirskie-bani')).toBe(false)
  })

  it('reports the first local problem: format before reserved word, none for empty', () => {
    expect(slugLocalProblem('')).toBeNull()
    expect(slugLocalProblem('ok-bani')).toBeNull()
    expect(slugLocalProblem('x')).toBe(BATHS_SLUG_FORMAT_TEXT)
    expect(slugLocalProblem('cabinet')).toBe(BATHS_SLUG_RESERVED_TEXT)
  })

  it('normalises typing: lowercase, no junk, no double or leading hyphen', () => {
    expect(normalizeSlugInput('Моя Баня 2')).toBe('2')
    expect(normalizeSlugInput('Sauna  One')).toBe('sauna-one')
    expect(normalizeSlugInput('--a__b')).toBe('a-b')
  })
})
