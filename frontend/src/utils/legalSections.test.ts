import { describe, it, expect } from 'vitest'
import { splitLegalSections, findSection } from './legalSections'

const SAMPLE = `<p><strong>Title.</strong> Редакция 1.</p>
<p><em>Implementer note, not shown to users.</em></p>
<h2>Текст у поля ввода (виден всегда)</h2>
<p>Field warning text.</p>
<h2>Усиленное предупреждение (если найдены маркеры)</h2>
<p>Heightened warning text.</p>
<h2>Подтверждение при сохранении</h2>
<p>Save confirmation text.</p>`

describe('splitLegalSections', () => {
  it('discards content before the first <h2> (title/meta commentary)', () => {
    const sections = splitLegalSections(SAMPLE)
    expect(sections).toHaveLength(3)
    expect(sections[0].html).not.toContain('Implementer note')
  })

  it('keeps each section under its own heading, without leaking into the next one', () => {
    const sections = splitLegalSections(SAMPLE)
    expect(sections[0].heading).toBe('Текст у поля ввода (виден всегда)')
    expect(sections[0].html).toContain('Field warning text.')
    expect(sections[0].html).not.toContain('Heightened warning')

    expect(sections[1].heading).toBe('Усиленное предупреждение (если найдены маркеры)')
    expect(sections[1].html).toContain('Heightened warning text.')

    expect(sections[2].heading).toBe('Подтверждение при сохранении')
    expect(sections[2].html).toContain('Save confirmation text.')
  })

  it('returns an empty array for text with no <h2> at all', () => {
    expect(splitLegalSections('<p>Just a paragraph.</p>')).toEqual([])
  })
})

describe('findSection', () => {
  const sections = splitLegalSections(SAMPLE)

  it('matches case- and whitespace-insensitively on a heading substring', () => {
    const found = findSection(sections, 'подтверждение при сохранении')
    expect(found?.html).toContain('Save confirmation text.')
  })

  it('matches a partial heading', () => {
    const found = findSection(sections, 'усиленное')
    expect(found?.html).toContain('Heightened warning text.')
  })

  it('returns null, not throw, when nothing matches — callers fall back to the whole document', () => {
    expect(findSection(sections, 'раздел, которого нет')).toBeNull()
  })
})
