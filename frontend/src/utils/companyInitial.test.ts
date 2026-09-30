// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { companyInitial } from './companyInitial'

describe('companyInitial (V29-30)', () => {
  it.each([
    ['Барбершоп', 'Б'],
    ['  салон', 'С'],
    ['«Ромашка»', 'Р'],
    ['"Лаванда"', 'Л'],
    ['— Кофе', 'К'],
    ['ёлка', 'Ё'],
    ['1-я пекарня', '1'],
    ['🍕 Пицца', 'П'],
    ['ß-bar', 'S'],
    ['', ''],
    ['   ', ''],
  ])('%j -> %j', (name, expected) => {
    expect(companyInitial(name)).toBe(expected)
  })
  it('null/undefined -> empty', () => {
    expect(companyInitial(null)).toBe('')
    expect(companyInitial(undefined)).toBe('')
  })
})
