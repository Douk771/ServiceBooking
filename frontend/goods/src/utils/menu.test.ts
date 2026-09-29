import { describe, it, expect } from 'vitest'
import { groupByCategory, initialSelection, sameSelection, toggleId } from './menu'
import type { DailyMenuProductDto } from '../types'

const p = (productId: string, over: Partial<DailyMenuProductDto> = {}): DailyMenuProductDto => ({ productId, name: productId, categoryName: 'Горячее', isPublished: true, inMenu: false, allowedByWeekdays: true, ...over })

describe('daily menu selection', () => {
  it('starts from what the server ticked (pre-filled for a date without a saved menu)', () => {
    expect([...initialSelection([p('a', { inMenu: true }), p('b'), p('c', { inMenu: true })])]).toEqual(['a', 'c'])
  })
  it('toggles without mutating the previous set', () => {
    const s = new Set(['a'])
    const on = toggleId(s, 'b')
    expect([...on].sort()).toEqual(['a', 'b'])
    expect([...s]).toEqual(['a'])
    expect([...toggleId(on, 'a')]).toEqual(['b'])
  })
  it('compares selections regardless of order', () => {
    expect(sameSelection(new Set(['a', 'b']), new Set(['b', 'a']))).toBe(true)
    expect(sameSelection(new Set(['a']), new Set(['a', 'b']))).toBe(false)
  })
  it('groups by category keeping server order and puts «Другое» last', () => {
    const groups = groupByCategory([p('x', { categoryName: null }), p('a', { categoryName: 'Горячее' }), p('b', { categoryName: 'Напитки' }), p('c', { categoryName: 'Горячее' })])
    expect(groups.map((g) => [g.name, g.products.map((x) => x.productId)])).toEqual([
      ['Горячее', ['a', 'c']],
      ['Напитки', ['b']],
      ['Другое', ['x']],
    ])
  })
})
