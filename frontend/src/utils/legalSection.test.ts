import { describe, it, expect } from 'vitest'
import { ownerFacingSection } from './legalSection'
// `?raw` (vite/client.d.ts) reads the file as a plain string at build/test time — no Node `fs`
// needed, so this stays typecheckable under the project's `src`-only tsconfig (no @types/node),
// same precedent as `legalRoutes.test.ts`.
import realHtml from '../../../legal-drafts/13-public-address-notice.html?raw'

describe('ownerFacingSection — ARCHITECTURE_CYCLE13.md §220.2/§220.5 (R24)', () => {
  it('extracts the "Текст для владельца" section from the real legal-drafts file', () => {
    const section = ownerFacingSection(realHtml)
    expect(section).not.toBeNull()
    expect(section).toContain('Этот адрес увидит любой человек в интернете')
    expect(section).toContain('Адрес можно убрать в любой момент')
  })

  it('does not leak the service appendices into the extracted section', () => {
    const section = ownerFacingSection(realHtml)
    expect(section).not.toContain('Служебное приложение')
    expect(section).not.toContain('152-ФЗ')
    expect(section).not.toContain('Роскомнадзора')
  })

  it('stops at the next heading ("Подтверждение"), not just the end of the document', () => {
    const section = ownerFacingSection(realHtml)
    expect(section).not.toContain('Понятно, сохранить адрес')
  })

  it('returns null when the anchor heading is missing, rather than guessing', () => {
    const withoutAnchor = '<h2>Другой заголовок</h2><p>Текст</p>'
    expect(ownerFacingSection(withoutAnchor)).toBeNull()
  })

  it('returns null on a document with no headings at all', () => {
    expect(ownerFacingSection('<p>Просто текст без якорей</p>')).toBeNull()
  })
})
