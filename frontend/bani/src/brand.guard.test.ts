// @vitest-environment node
import { describe, it, expect } from 'vitest'
import indexHtml from '../index.html?raw'
import manifest from '../public/manifest.webmanifest?raw'
import { bathsVertical } from './vertical'

// Q42-7: the brand is «EZBOOK Бани» in one spelling everywhere (header, footer, manifest, <title>, push).
describe('bani brand', () => {
  it('is spelled once: vertical, <title>, manifest name', () => {
    expect(bathsVertical.brand).toBe('EZBOOK Бани')
    expect(indexHtml).toContain('<title>EZBOOK Бани</title>')
    expect(JSON.parse(manifest).name).toBe('EZBOOK Бани')
  })

  it('has no old «ezbook · Бани» spelling', () => {
    expect(indexHtml).not.toContain('ezbook · Бани')
    expect(manifest).not.toContain('ezbook · Бани')
  })

  it('pushes to the Baths site', () => {
    expect(bathsVertical.pushSite).toBe('Baths')
  })
})
